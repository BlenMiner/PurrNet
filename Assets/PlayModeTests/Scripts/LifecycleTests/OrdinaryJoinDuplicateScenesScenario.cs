using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

// Targeted standalone scenario: duplicated scene assets must be matched by instance
// and physics mode, while their network initialization follows the server's IDs.
public sealed class OrdinaryJoinDuplicateScenesScenario : Scenario, IScenarioConnectionPreparation
{
    private const string SceneX = "OrdinaryJoinSceneX";
    private const string SceneY = "OrdinaryJoinSceneY";
    private const string SceneW = "OrdinaryJoinSceneW";
    private static readonly LocalPhysicsMode[] ServerPhysics =
        { LocalPhysicsMode.None, LocalPhysicsMode.None, LocalPhysicsMode.Physics3D };

    [SerializeField] private float _timeoutSeconds = 45f;

    private readonly List<string> _failures = new();
    private readonly List<SceneID> _clientReady = new();
    private readonly Dictionary<PlayerID, List<SceneID>> _serverReady = new();
    private readonly List<string> _physicalClientLoads = new();
    private readonly List<RetainedScene> _retained = new();
    private readonly List<Scene> _serverScenes = new();
    private NetworkManager _manager;
    private NetworkRole _role;
    private Manifest _manifest;
    private bool _watchPhysicalLoads;
    private string _readyPath;
    private string _manifestPath;

    [Serializable]
    private sealed class Manifest
    {
        public List<SceneExpectation> scenes = new();
    }

    [Serializable]
    private sealed class SceneExpectation
    {
        public int sceneId;
        public string name;
        public int physicsMode;
        public string[] probeNames;
        public string[] networkIds;
    }

    private sealed class RetainedScene
    {
        public Scene scene;
        public OrdinaryJoinSceneProbe root;
        public OrdinaryJoinSceneProbe child;
        public bool inManifest;
        public LocalPhysicsMode physicsMode;
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
            throw new InvalidOperationException("Duplicate join scenario requires -results in a shared fresh directory.");

        var directory = Path.GetFullPath(Path.GetDirectoryName(resultsPath));
        Directory.CreateDirectory(directory);
        _readyPath = Path.Combine(directory, "ordinary-join-duplicates-server-ready.txt");
        _manifestPath = Path.Combine(directory, "ordinary-join-duplicates-manifest.json");
        UnityEngine.Object.DontDestroyOnLoad(transform.root.gameObject);
        UnityEngine.Object.DontDestroyOnLoad(ctx.networkManager.gameObject);

        if (ctx.isServer)
        {
            if (File.Exists(_readyPath) || File.Exists(_manifestPath))
                throw new InvalidOperationException("Duplicate join scenario requires a fresh results directory.");
            var scene = await LoadOffline(SceneX, LoadSceneMode.Single, LocalPhysicsMode.None, ctx);
            ctx.networkManager.ResetOriginalScene(scene);
            _serverScenes.Add(scene);
            return;
        }

        Capture(await LoadOffline(SceneW, LoadSceneMode.Single, LocalPhysicsMode.None, ctx), false);
        // The isolated physics instance comes first locally and last on the server.
        Capture(await LoadOffline(SceneY, LoadSceneMode.Additive, LocalPhysicsMode.Physics3D, ctx), true);
        Capture(await LoadOffline(SceneY, LoadSceneMode.Additive, LocalPhysicsMode.None, ctx), true);
        var original = await LoadOffline(SceneY, LoadSceneMode.Additive, LocalPhysicsMode.None, ctx);
        Capture(original, true);
        if (!SceneManager.SetActiveScene(original))
            throw new InvalidOperationException("Could not select the last preloaded Y as the client's original scene.");
        ctx.networkManager.ResetOriginalScene(original);

        await UniTaskUtils.WaitWithTimeout(() => File.Exists(_readyPath), _timeoutSeconds, ctx.cancellationToken);
        _manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(_manifestPath));
        ValidateManifest();
        _watchPhysicalLoads = true;
    }

    public async UniTask AfterServerStarted(ScenarioContext ctx)
    {
        await UniTaskUtils.WaitWithTimeout(() => ctx.networkManager.isServer, _timeoutSeconds, ctx.cancellationToken);
        var scenes = ctx.networkManager.sceneModule;
        foreach (var physics in ServerPhysics)
        {
            var before = LoadedScenes();
            var operation = scenes.LoadSceneAsync(SceneY, new PurrSceneSettings
            {
                mode = LoadSceneMode.Additive,
                physicsMode = physics,
                isPublic = true
            });
            if (operation == null)
                throw new InvalidOperationException($"Could not start server Y with physics {physics}.");
            await UniTaskUtils.WaitWithTimeout(() => operation.isDone, _timeoutSeconds, ctx.cancellationToken);
            var scene = FindNewScene(SceneY, before);
            await UniTaskUtils.WaitWithTimeout(() => scenes.TryGetSceneID(scene, out _),
                _timeoutSeconds, ctx.cancellationToken);
            _serverScenes.Add(scene);
        }

        // Capture the finite authored baseline, including IDs, before releasing clients.
        await UniTaskUtils.WaitWithTimeout(() => ServerProbesSpawned(), _timeoutSeconds, ctx.cancellationToken);
        _manifest = new Manifest();
        foreach (var scene in _serverScenes)
        {
            if (!scenes.TryGetSceneID(scene, out var id))
                throw new InvalidOperationException($"Server scene handle {scene.handle} is not registered.");
            var probes = FindProbes(scene);
            _manifest.scenes.Add(new SceneExpectation
            {
                sceneId = id.id,
                name = scene.name,
                physicsMode = (int)PhysicsMode(scene),
                probeNames = new[] { probes[0].name, probes[1].name },
                networkIds = new[] { probes[0].id.Value.ToString(), probes[1].id.Value.ToString() }
            });
            if (ctx.isClient)
                Capture(scene, true);
        }
        ValidateManifest();
        File.WriteAllText(_manifestPath, JsonUtility.ToJson(_manifest));
        File.WriteAllText(_readyPath, "ready");
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _failures.Count > 0 || ReadyAndSpawned(ctx),
                _timeoutSeconds, ctx.cancellationToken);
            await UniTask.WaitForSeconds(0.15f, cancellationToken: ctx.cancellationToken);
            Verify(ctx);
            return _failures.Count == 0
                ? ScenarioResult.Ok("three Y instances reused once with matching physics and server IDs; ready X then Y IDs; local W retained")
                : ScenarioResult.Fail(string.Join(" | ", _failures));
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"duplicate join timeout; client ready=[{string.Join(",", _clientReady)}], " +
                $"server ready players={_serverReady.Count}, Y copies={CountLoaded(SceneY)}; {string.Join(" | ", _failures)}");
        }
        finally
        {
            _manager.onPlayerLoadedScene -= OnPlayerLoadedScene;
            SceneManager.sceneLoaded -= OnPhysicalSceneLoaded;
        }
    }

    private void ValidateManifest()
    {
        if (_manifest?.scenes == null || _manifest.scenes.Count != 4)
            throw new InvalidOperationException("Expected X and three Y instances in the duplicate join manifest.");
        var ids = new HashSet<int>();
        for (var i = 0; i < _manifest.scenes.Count; i++)
        {
            var expected = _manifest.scenes[i];
            var physics = i == 0 ? LocalPhysicsMode.None : ServerPhysics[i - 1];
            if (!ids.Add(expected.sceneId) || expected.name != (i == 0 ? SceneX : SceneY) ||
                expected.physicsMode != (int)physics || expected.probeNames?.Length != 2 || expected.networkIds?.Length != 2)
                throw new InvalidOperationException($"Invalid duplicate join manifest entry {i}.");
        }
    }

    private bool ServerProbesSpawned()
    {
        foreach (var scene in _serverScenes)
        {
            var probes = FindProbes(scene);
            if (probes.Length != 2)
                return false;
            foreach (var probe in probes)
                if (!probe.IsSpawned(true) || !probe.id.HasValue || probe.value != probe.expectedValue)
                    return false;
        }
        return true;
    }

    private bool ReadyAndSpawned(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            if (_serverReady.Count != ctx.expectedConnections)
                return false;
            foreach (var ready in _serverReady.Values)
                if (ready.Count != 4)
                    return false;
        }
        if (ctx.isClient && _clientReady.Count != 4)
            return false;

        var scenes = _manager.GetModule<ScenesModule>(ctx.isServer);
        foreach (var expected in _manifest.scenes)
        {
            if (!scenes.TryGetSceneState(new SceneID((ushort)expected.sceneId), out var state))
                return false;
            var probes = FindProbes(state.scene);
            if (probes.Length != 2)
                return false;
            foreach (var probe in probes)
                if ((ctx.isServer && !probe.IsSpawned(true)) || (ctx.isClient && !probe.IsSpawned(false)) ||
                    probe.value != probe.expectedValue)
                    return false;
        }
        return true;
    }

    private void OnPlayerLoadedScene(PlayerID player, SceneID id, bool asServer)
    {
        if (!_manager.TryGetModule<ScenesModule>(asServer, out var scenes) || !scenes.TryGetSceneState(id, out var state))
        {
            _failures.Add($"Readiness referenced unregistered scene {id}.");
            return;
        }
        if (state.scene.name == SceneW)
            _failures.Add("Client-only W became network ready.");
        if (state.scene.name != SceneX && state.scene.name != SceneY)
            return;

        List<SceneID> ready;
        if (asServer)
        {
            if (!_serverReady.TryGetValue(player, out ready))
                _serverReady.Add(player, ready = new List<SceneID>());
        }
        else
        {
            ready = _clientReady;
            if (CountLoaded(SceneX) != 1)
                _failures.Add($"Scene {id} became ready before exactly one X was loaded.");
        }
        if (_manifest == null || ready.Count >= _manifest.scenes.Count ||
            id.id != _manifest.scenes[ready.Count].sceneId)
            _failures.Add($"{(asServer ? "server" : "client")} readiness for {player}: [{string.Join(",", ready)}] then {id}, outside server order.");
        ready.Add(id);
    }

    private void Verify(ScenarioContext ctx)
    {
        if (CountLoaded(SceneX) != 1 || CountLoaded(SceneY) != 3)
            _failures.Add($"Expected one X and three Y instances; got X={CountLoaded(SceneX)}, Y={CountLoaded(SceneY)}.");
        if (ctx.isServer)
            VerifyNetworkMappings(true);
        if (ctx.isClient)
            VerifyNetworkMappings(false);

        if (ctx.isClient)
        {
            var scenes = _manager.GetModule<ScenesModule>(false);
            var retainedIds = new HashSet<SceneID>();
            foreach (var retained in _retained)
            {
                if (!retained.scene.IsValid() || !retained.scene.isLoaded || PhysicsMode(retained.scene) != retained.physicsMode)
                    _failures.Add($"Retained handle {retained.scene.handle} was replaced or its physics changed.");
                var mapped = scenes.TryGetSceneID(retained.scene, out var id);
                if (mapped != retained.inManifest || (mapped && !retainedIds.Add(id)))
                    _failures.Add($"Retained handle {retained.scene.handle} has a missing, duplicate, or unexpected SceneID.");
                var current = FindProbes(retained.scene);
                if (current.Length != 2 || !Array.Exists(current, probe => ReferenceEquals(probe, retained.root)) ||
                    !Array.Exists(current, probe => ReferenceEquals(probe, retained.child)))
                    _failures.Add($"Authored probe instances changed in retained handle {retained.scene.handle}.");
                if (!retained.root || !retained.child || retained.child.transform.parent != retained.root.transform)
                    _failures.Add($"Authored root/child relation changed in retained handle {retained.scene.handle}.");
                VerifyRetainedProbe(retained, retained.root);
                VerifyRetainedProbe(retained, retained.child);
            }
        }
        if (ctx.role == NetworkRole.Client &&
            (_physicalClientLoads.Count != 1 || _physicalClientLoads[0] != SceneX))
            _failures.Add($"Expected only X to load after connection; got [{string.Join(",", _physicalClientLoads)}].");
    }

    private void VerifyNetworkMappings(bool asServer)
    {
        var scenes = _manager.GetModule<ScenesModule>(asServer);
        var hierarchy = _manager.GetModule<HierarchyFactory>(asServer);
        var matchedScenes = new HashSet<Scene>();
        foreach (var expected in _manifest.scenes)
        {
            var id = new SceneID((ushort)expected.sceneId);
            if (!scenes.TryGetSceneState(id, out var state) || !state.scene.IsValid() || !state.scene.isLoaded)
            {
                _failures.Add($"Missing mapped scene {id} on {(asServer ? "server" : "client")}.");
                continue;
            }
            if (!matchedScenes.Add(state.scene) || state.scene.name != expected.name ||
                (int)state.settings.physicsMode != expected.physicsMode || (int)PhysicsMode(state.scene) != expected.physicsMode)
                _failures.Add($"SceneID {id} has the wrong instance or declared/physical physics mode.");
            var probes = FindProbes(state.scene);
            if (probes.Length != 2)
                _failures.Add($"SceneID {id} has {probes.Length} probes instead of its authored pair.");
            foreach (var probe in probes)
            {
                var index = Array.IndexOf(expected.probeNames, probe.name);
                if (!probe.id.HasValue || probe.sceneId != id || index < 0 ||
                    probe.id.Value.ToString() != expected.networkIds[index] ||
                    !hierarchy.TryGetIdentity(id, probe.id.Value, out var resolved) || !ReferenceEquals(probe, resolved))
                    _failures.Add($"Probe {probe.name} in handle {state.scene.handle} is not the server's identity scoped to SceneID {id}.");
            }
        }
    }

    private void VerifyRetainedProbe(RetainedScene retained, OrdinaryJoinSceneProbe probe)
    {
        if (!probe || probe.gameObject.scene.handle != retained.scene.handle)
        {
            _failures.Add($"A retained probe was destroyed or moved from handle {retained.scene.handle}.");
            return;
        }
        if (retained.inManifest)
        {
            if (!probe.isSceneObject || !probe.IsSpawned(false) || probe.clientSpawnCount != 1 ||
                probe.clientDespawnCount != 0 || probe.value != probe.expectedValue)
                _failures.Add($"Probe {probe.name} in handle {retained.scene.handle} did not receive exactly one authored spawn and its baseline.");
        }
        else if (probe.isSpawned || probe.clientSpawnCount != 0 || probe.clientDespawnCount != 0)
            _failures.Add($"Local-only W probe {probe.name} entered the network lifecycle.");
    }

    private void Capture(Scene scene, bool inManifest)
    {
        var probes = FindProbes(scene);
        if (probes.Length != 2)
            throw new InvalidOperationException($"Expected two authored probes in handle {scene.handle}, got {probes.Length}.");
        var root = probes[0].transform.parent == null ? probes[0] : probes[1];
        var child = root == probes[0] ? probes[1] : probes[0];
        if (child.transform.parent != root.transform)
            throw new InvalidOperationException($"Expected an authored root/child pair in handle {scene.handle}.");
        _retained.Add(new RetainedScene
        {
            scene = scene, root = root, child = child, inManifest = inManifest, physicsMode = PhysicsMode(scene)
        });
    }

    private void OnPhysicalSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_watchPhysicalLoads && _role == NetworkRole.Client)
            _physicalClientLoads.Add(scene.name);
    }

    private static async UniTask<Scene> LoadOffline(string name, LoadSceneMode mode, LocalPhysicsMode physics, ScenarioContext ctx)
    {
        var before = LoadedScenes();
        var operation = SceneManager.LoadSceneAsync(name, new LoadSceneParameters(mode, physics));
        if (operation == null)
            throw new InvalidOperationException($"Could not preload {name} with physics {physics}.");
        await UniTaskUtils.WaitWithTimeout(() => operation.isDone, 30f, ctx.cancellationToken);
        var scene = FindNewScene(name, before);
        if (PhysicsMode(scene) != physics)
            throw new InvalidOperationException($"Preloaded {name} did not use requested physics {physics}.");
        return scene;
    }

    private static HashSet<Scene> LoadedScenes()
    {
        var scenes = new HashSet<Scene>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
            scenes.Add(SceneManager.GetSceneAt(i));
        return scenes;
    }

    private static Scene FindNewScene(string name, HashSet<Scene> previousScenes)
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.name == name && scene.isLoaded && !previousScenes.Contains(scene))
                return scene;
        }
        throw new InvalidOperationException($"No newly loaded instance of {name} was found.");
    }

    private static LocalPhysicsMode PhysicsMode(Scene scene)
    {
        return scene.GetPhysicsScene() == Physics.defaultPhysicsScene ? LocalPhysicsMode.None : LocalPhysicsMode.Physics3D;
    }

    private static int CountLoaded(string name)
    {
        var count = 0;
        for (var i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).name == name && SceneManager.GetSceneAt(i).isLoaded)
                count++;
        return count;
    }

    private static OrdinaryJoinSceneProbe[] FindProbes(Scene scene)
    {
        var result = new List<OrdinaryJoinSceneProbe>();
        if (scene.IsValid() && scene.isLoaded)
            foreach (var root in scene.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<OrdinaryJoinSceneProbe>(true));
        return result.ToArray();
    }
}
