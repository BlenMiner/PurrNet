using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

// Targeted standalone test: all peers share the fresh directory containing their -results files.
// A file barrier lets the server finish its manifest before external clients start connecting.
public sealed class OrdinaryJoinSceneReconciliationScenario : Scenario, IScenarioConnectionPreparation
{
    private const string SceneX = "OrdinaryJoinSceneX";
    private const string SceneY = "OrdinaryJoinSceneY";
    private const string SceneZ = "OrdinaryJoinSceneZ";
    private const string SceneW = "OrdinaryJoinSceneW";
    private static readonly string[] ExpectedOrder = { SceneX, SceneY, SceneZ };

    [SerializeField] private float _timeoutSeconds = 45f;

    private readonly List<string> _failures = new();
    private readonly List<string> _clientReady = new();
    private readonly Dictionary<PlayerID, List<string>> _serverReady = new();
    private readonly List<string> _physicalClientLoads = new();
    private readonly List<RetainedScene> _retainedScenes = new();
    private NetworkManager _manager;
    private NetworkRole _role;
    private bool _watchPhysicalLoads;
    private string _readyPath;

    private sealed class RetainedScene
    {
        public Scene scene;
        public OrdinaryJoinSceneProbe[] probes;
        public bool expectedInManifest;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _manager = manager;
        _role = ctx.role;
        manager.onPlayerLoadedScene += OnPlayerLoadedScene;
        SceneManager.sceneLoaded += OnPhysicalSceneLoaded;
    }

    public async UniTask BeforeConnection(ScenarioContext ctx)
    {
        if (!CommandLineUtils.TryGetArgument("-results", out var resultsPath) ||
            string.IsNullOrEmpty(Path.GetDirectoryName(resultsPath)))
            throw new InvalidOperationException("Ordinary join scenario requires -results in a shared fresh directory.");

        var directory = Path.GetFullPath(Path.GetDirectoryName(resultsPath));
        Directory.CreateDirectory(directory);
        _readyPath = Path.Combine(directory, "ordinary-join-server-ready.txt");

        UnityEngine.Object.DontDestroyOnLoad(transform.root.gameObject);
        UnityEngine.Object.DontDestroyOnLoad(ctx.networkManager.gameObject);

        if (ctx.isServer)
        {
            if (File.Exists(_readyPath))
                throw new InvalidOperationException("Ordinary join scenario requires a fresh results directory.");
            await LoadOffline(SceneX, LoadSceneMode.Single, ctx);
            ctx.networkManager.ResetOriginalScene(SceneManager.GetSceneByName(SceneX));
            return;
        }

        await LoadOffline(SceneW, LoadSceneMode.Single, ctx);
        // Reverse the local order as well as changing the original scene. Readiness must
        // follow the server's X/Y/Z manifest, not Unity's pre-existing scene enumeration.
        await LoadOffline(SceneZ, LoadSceneMode.Additive, ctx);
        await LoadOffline(SceneY, LoadSceneMode.Additive, ctx);
        var original = SceneManager.GetSceneByName(SceneY);
        if (!SceneManager.SetActiveScene(original))
            throw new InvalidOperationException("Could not select the client's different original scene.");
        ctx.networkManager.ResetOriginalScene(original);
        Capture(SceneY, true);
        Capture(SceneZ, true);
        Capture(SceneW, false);

        await UniTaskUtils.WaitWithTimeout(() => File.Exists(_readyPath), _timeoutSeconds, ctx.cancellationToken);
        _watchPhysicalLoads = true;
    }

    public async UniTask AfterServerStarted(ScenarioContext ctx)
    {
        await UniTaskUtils.WaitWithTimeout(() => ctx.networkManager.isServer, _timeoutSeconds, ctx.cancellationToken);
        var scenes = ctx.networkManager.sceneModule;
        foreach (var name in new[] { SceneY, SceneZ })
        {
            var operation = scenes.LoadSceneAsync(name, LoadSceneMode.Additive);
            if (operation == null)
                throw new InvalidOperationException($"Could not start server scene {name}.");
            await UniTaskUtils.WaitWithTimeout(
                () => operation.isDone && scenes.TryGetSceneID(SceneManager.GetSceneByName(name), out _),
                _timeoutSeconds, ctx.cancellationToken);
        }

        if (ctx.isClient)
        {
            Capture(SceneX, true);
            Capture(SceneY, true);
            Capture(SceneZ, true);
        }

        // The runner creates a fresh shared output directory; existence is the release
        // signal, and no client reads partially written contents or relies on a delay.
        File.WriteAllText(_readyPath, "X,Y,Z");
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _failures.Count > 0 || ReadyAndSpawned(ctx), _timeoutSeconds, ctx.cancellationToken);
            // Only these known authored probes need to finish for the assertions below.
            // Scene readiness itself is checked in the callback, without a spawn barrier.
            await UniTask.WaitForSeconds(0.15f, cancellationToken: ctx.cancellationToken);
            Verify(ctx);
            return _failures.Count == 0
                ? ScenarioResult.Ok(ctx.role == NetworkRole.Client
                    ? "ordinary join loaded only X; retained Y/Z authored instances and local W; ready X,Y,Z"
                    : "ordinary join manifest and each player's readiness followed X,Y,Z")
                : ScenarioResult.Fail(string.Join(" | ", _failures));
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"ordinary join timed out; client ready=[{string.Join(",", _clientReady)}], " +
                $"server ready players={_serverReady.Count}; {string.Join(" | ", _failures)}");
        }
        finally
        {
            _manager.onPlayerLoadedScene -= OnPlayerLoadedScene;
            SceneManager.sceneLoaded -= OnPhysicalSceneLoaded;
        }
    }

    private static async UniTask LoadOffline(string name, LoadSceneMode mode, ScenarioContext ctx)
    {
        var operation = SceneManager.LoadSceneAsync(name, mode);
        if (operation == null)
            throw new InvalidOperationException($"Could not preload {name}.");
        await UniTaskUtils.WaitWithTimeout(() => operation.isDone, 30f, ctx.cancellationToken);
    }

    private void Capture(string name, bool inManifest)
    {
        var scene = SceneManager.GetSceneByName(name);
        var probes = FindProbes(scene);
        if (probes.Length != 2)
            throw new InvalidOperationException($"Expected two authored probes in {name}, got {probes.Length}.");
        _retainedScenes.Add(new RetainedScene { scene = scene, probes = probes, expectedInManifest = inManifest });
    }

    private void OnPhysicalSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_watchPhysicalLoads && _role == NetworkRole.Client)
            _physicalClientLoads.Add(scene.name);
    }

    private void OnPlayerLoadedScene(PlayerID player, SceneID sceneId, bool asServer)
    {
        if (!_manager.TryGetModule<ScenesModule>(asServer, out var scenes) ||
            !scenes.TryGetSceneState(sceneId, out var state))
        {
            _failures.Add($"Readiness referred to unregistered scene {sceneId}.");
            return;
        }

        string name = state.scene.name;
        if (name == SceneW)
        {
            _failures.Add("Client-only W was announced as network ready.");
            return;
        }
        if (Array.IndexOf(ExpectedOrder, name) < 0)
            return;

        List<string> ready;
        if (asServer)
        {
            if (!_serverReady.TryGetValue(player, out ready))
                _serverReady.Add(player, ready = new List<string>());
        }
        else
        {
            ready = _clientReady;
            if (!IsLoaded(SceneX))
                _failures.Add($"{name} became network ready before X finished physically loading.");
        }

        if (ready.Count >= ExpectedOrder.Length || name != ExpectedOrder[ready.Count])
            _failures.Add($"{(asServer ? "server" : "client")} readiness for {player}: " +
                $"[{string.Join(",", ready)}] then {name}; expected X,Y,Z.");
        ready.Add(name);
    }

    private bool ReadyAndSpawned(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            if (_serverReady.Count < ctx.expectedConnections)
                return false;
            foreach (var ready in _serverReady.Values)
                if (ready.Count < ExpectedOrder.Length)
                    return false;
        }
        if (ctx.isClient && _clientReady.Count < ExpectedOrder.Length)
            return false;

        foreach (var name in ExpectedOrder)
        {
            if (!IsLoaded(name))
                return false;
            var probes = FindProbes(SceneManager.GetSceneByName(name));
            if (probes.Length != 2)
                return false;
            foreach (var probe in probes)
            {
                if (ctx.isServer && !probe.IsSpawned(true) || ctx.isClient && !probe.IsSpawned(false) ||
                    probe.value != probe.expectedValue)
                    return false;
            }
        }
        return true;
    }

    private void Verify(ScenarioContext ctx)
    {
        var scenes = ctx.networkManager.sceneModule;
        foreach (var name in ExpectedOrder)
        {
            var scene = SceneManager.GetSceneByName(name);
            if (!scene.IsValid() || !scene.isLoaded || !scenes.TryGetSceneID(scene, out _))
                _failures.Add($"Manifest scene {name} is not physically loaded and registered.");
        }

        foreach (var retained in _retainedScenes)
        {
            var current = SceneManager.GetSceneByName(retained.scene.name);
            if (!retained.scene.IsValid() || !retained.scene.isLoaded || current.handle != retained.scene.handle)
                _failures.Add($"Preloaded {retained.scene.name} was unloaded or replaced.");
            if (scenes.TryGetSceneID(retained.scene, out _) != retained.expectedInManifest)
                _failures.Add($"Wrong network registration for preloaded {retained.scene.name}.");
            foreach (var probe in retained.probes)
            {
                if (!probe || probe.gameObject.scene.handle != retained.scene.handle)
                {
                    _failures.Add($"An authored Unity instance from {retained.scene.name} was replaced.");
                    continue;
                }
                if (retained.expectedInManifest)
                {
                    if (!probe.isSceneObject || !probe.IsSpawned(false) || probe.clientSpawnCount != 1 ||
                        probe.clientDespawnCount != 0 || probe.value != probe.expectedValue)
                        _failures.Add($"Retained probe {probe.name} did not get one authored spawn and its baseline.");
                }
                else if (probe.isSpawned || probe.clientSpawnCount != 0 || probe.clientDespawnCount != 0)
                    _failures.Add($"Client-only probe {probe.name} entered the network lifecycle.");
            }
        }

        if (ctx.role == NetworkRole.Client &&
            (_physicalClientLoads.Count != 1 || _physicalClientLoads[0] != SceneX))
            _failures.Add($"Expected only X to load after connect; got [{string.Join(",", _physicalClientLoads)}].");
    }

    private static bool IsLoaded(string name)
    {
        var scene = SceneManager.GetSceneByName(name);
        return scene.IsValid() && scene.isLoaded;
    }

    private static OrdinaryJoinSceneProbe[] FindProbes(Scene scene)
    {
        var result = new List<OrdinaryJoinSceneProbe>();
        if (!scene.IsValid() || !scene.isLoaded)
            return result.ToArray();
        foreach (var root in scene.GetRootGameObjects())
            result.AddRange(root.GetComponentsInChildren<OrdinaryJoinSceneProbe>(true));
        return result.ToArray();
    }
}
