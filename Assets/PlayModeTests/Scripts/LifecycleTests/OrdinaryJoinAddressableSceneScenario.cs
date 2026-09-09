using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

// Uses a real built Addressables catalog and an externally owned scene handle.
// Run separately because the client and server start from different scenes.
public sealed class OrdinaryJoinAddressableSceneScenario : Scenario, IScenarioConnectionPreparation
{
    private const string SceneX = "OrdinaryJoinSceneX";
    private const string SceneW = "OrdinaryJoinSceneW";
    private const string AddressableName = "OrdinaryJoinAddressableTarget";
    private const string AddressableGuid = "2d67a3f702cb4fd3bb984de0c1d22fc8";
    private const int Barrier = 28600;
    private const float Timeout = 40f;
    private readonly List<string> _failures = new();
    private readonly List<string> _clientReady = new();
    private readonly Dictionary<PlayerID, List<string>> _serverReady = new();
    private readonly List<string> _physicalLoads = new();
    private NetworkManager _manager;
    private bool _externalClient;
    private bool _watchLoads;
    private string _readyPath;
    private Scene _addressableScene;
    private Scene _localScene;
    private OrdinaryJoinSceneProbe[] _retainedProbes;
    private OrdinaryJoinSceneProbe[] _localProbes;
    private AsyncOperationHandle<SceneInstance> _externalHandle;
    private SceneID _addressableId;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _manager = manager;
        _externalClient = ctx.role == NetworkRole.Client;
        manager.onPlayerLoadedScene += OnReady;
        SceneManager.sceneLoaded += OnPhysicalLoad;
    }

    public async UniTask BeforeConnection(ScenarioContext ctx)
    {
        if (!CommandLineUtils.TryGetArgument("-results", out var results) ||
            string.IsNullOrEmpty(Path.GetDirectoryName(results)))
            throw new InvalidOperationException("Addressable join requires -results in a shared fresh directory.");
        var directory = Path.GetFullPath(Path.GetDirectoryName(results));
        Directory.CreateDirectory(directory);
        _readyPath = Path.Combine(directory, "ordinary-addressable-server-ready.txt");
        DontDestroyOnLoad(transform.root.gameObject);
        DontDestroyOnLoad(ctx.networkManager.gameObject);
        await LoadOffline(ctx.isServer ? SceneX : SceneW, ctx);
        if (ctx.isServer)
        {
            if (File.Exists(_readyPath))
                throw new InvalidOperationException("Addressable join requires a fresh results directory.");
            ctx.networkManager.ResetOriginalScene(SceneManager.GetSceneByName(SceneX));
            return;
        }

        _localScene = SceneManager.GetSceneByName(SceneW);
        _localProbes = Probes(_localScene);
        _externalHandle = Addressables.LoadSceneAsync(AddressableGuid, LoadSceneMode.Additive);
        await UniTaskUtils.WaitWithTimeout(() => _externalHandle.IsDone, Timeout, ctx.cancellationToken);
        if (_externalHandle.Status != AsyncOperationStatus.Succeeded)
            throw new InvalidOperationException($"Could not preload the Addressable: {_externalHandle.OperationException}");
        Capture(_externalHandle.Result.Scene);
        // Also exercise a different bootstrap scene, whose build index is -1.
        ctx.networkManager.ResetOriginalScene(_addressableScene);
        await UniTaskUtils.WaitWithTimeout(() => File.Exists(_readyPath), Timeout, ctx.cancellationToken);
        _watchLoads = true;
    }

    public async UniTask AfterServerStarted(ScenarioContext ctx)
    {
        await UniTaskUtils.WaitWithTimeout(() => _manager.isServer, Timeout, ctx.cancellationToken);
        var operation = _manager.sceneModule.LoadAddressableSceneAsync(AddressableGuid,
            new PurrSceneSettings { mode = LoadSceneMode.Additive, isPublic = true });
        await UniTaskUtils.WaitWithTimeout(() => operation.IsDone, Timeout, ctx.cancellationToken);
        if (operation.Status != AsyncOperationStatus.Succeeded)
            throw new InvalidOperationException($"Server Addressable load failed: {operation.OperationException}");
        Capture(operation.Result.Scene);
        await UniTaskUtils.WaitWithTimeout(
            () => _manager.sceneModule.TryGetSceneID(_addressableScene, out _addressableId),
            Timeout, ctx.cancellationToken);
        File.WriteAllText(_readyPath, "X,Addressable");
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => ReadyAndSpawned(ctx), Timeout, ctx.cancellationToken);
            VerifyRetained(ctx);
            // Every peer finishes its retention checks before the server requests an unload.
            await ScenarioBarrier.Wait(ctx, Barrier + 1, Timeout);
            if (ctx.isServer)
                _manager.sceneModule.UnloadSceneAsync(_addressableScene);
            await UniTaskUtils.WaitWithTimeout(
                () => !_addressableScene.isLoaded && !_manager.sceneModule.TryGetSceneID(_addressableScene, out _) &&
                      !_manager.sceneModule.IsAddressableSceneLoaded(AddressableGuid),
                Timeout, ctx.cancellationToken);
            if (_externalClient)
                VerifyLocalScene();
            await ScenarioBarrier.Wait(ctx, Barrier + 2, Timeout);
            return _failures.Count == 0
                ? ScenarioResult.Ok(_externalClient
                    ? "reused external Addressable handle and authored instances; only X loaded; ready X,Addressable; explicit unload removed it and preserved local W"
                    : "all players acknowledged X,Addressable; reused Addressable scene unloaded on server request")
                : ScenarioResult.Fail(string.Join(" | ", _failures));
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"Addressable join timed out; ready=[{string.Join(",", _clientReady)}]; " +
                                       $"registered={_manager.sceneModule.TryGetSceneID(_addressableScene, out _)}; " +
                                       string.Join(" | ", _failures));
        }
        finally
        {
            _manager.onPlayerLoadedScene -= OnReady;
            SceneManager.sceneLoaded -= OnPhysicalLoad;
            // Unity/Addressables may already release the handle on explicit scene unload.
            if (_externalHandle.IsValid() && !_addressableScene.isLoaded)
                Addressables.Release(_externalHandle);
        }
    }

    private bool ReadyAndSpawned(ScenarioContext ctx)
    {
        if (ctx.isClient && _clientReady.Count != 2)
            return false;
        if (ctx.isServer)
        {
            if (_serverReady.Count != ctx.expectedConnections)
                return false;
            foreach (var ready in _serverReady.Values)
                if (ready.Count != 2)
                    return false;
        }
        foreach (var probe in _retainedProbes)
            if (!probe || (ctx.isServer && !probe.IsSpawned(true)) ||
                (ctx.isClient && !probe.IsSpawned(false)) || probe.value != probe.expectedValue)
                return false;
        return true;
    }

    private void VerifyRetained(ScenarioContext ctx)
    {
        var scenes = _manager.sceneModule;
        if (!_addressableScene.IsValid() || !_addressableScene.isLoaded ||
            !scenes.TryGetSceneID(_addressableScene, out _addressableId) || !scenes.IsAddressableScene(_addressableId))
            _failures.Add("The retained scene is not registered as the authoritative Addressable.");
        var copies = 0;
        for (var i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).name == AddressableName)
                copies++;
        if (copies != 1)
            _failures.Add($"Expected one Addressable instance, got {copies}.");
        foreach (var probe in _retainedProbes)
            if (!probe || probe.gameObject.scene != _addressableScene ||
                (ctx.isClient && (probe.clientSpawnCount != 1 || probe.clientDespawnCount != 0)))
                _failures.Add("A preloaded authored instance was replaced or replayed its lifecycle.");
        if (!_externalClient)
            return;
        if (!_externalHandle.IsValid() || _externalHandle.Result.Scene != _addressableScene)
            _failures.Add("Initial reconciliation released or replaced the external owner's handle.");
        if (_physicalLoads.Count != 1 || _physicalLoads[0] != SceneX)
            _failures.Add($"Expected only X to load after connection, got [{string.Join(",", _physicalLoads)}].");
        VerifyLocalScene();
    }

    private void VerifyLocalScene()
    {
        if (!_localScene.IsValid() || !_localScene.isLoaded || _manager.sceneModule.TryGetSceneID(_localScene, out _))
            _failures.Add("Local W was removed or registered with the server.");
        foreach (var probe in _localProbes)
            if (!probe || probe.isSpawned || probe.clientSpawnCount != 0 || probe.clientDespawnCount != 0)
                _failures.Add("A local W object entered the network lifecycle or was replaced.");
    }

    private void OnReady(PlayerID player, SceneID id, bool asServer)
    {
        if (!_manager.GetModule<ScenesModule>(asServer).TryGetSceneState(id, out var state))
            return;
        var name = state.scene.name;
        if (name != SceneX && name != AddressableName)
            return;
        var ready = _clientReady;
        if (asServer && !_serverReady.TryGetValue(player, out ready))
            _serverReady.Add(player, ready = new List<string>());
        if (ready.Count > 1 || name != (ready.Count == 0 ? SceneX : AddressableName))
            _failures.Add($"Readiness order for {player}: [{string.Join(",", ready)}], then {name}.");
        ready.Add(name);
    }

    private void OnPhysicalLoad(Scene scene, LoadSceneMode mode)
    {
        if (_externalClient && _watchLoads)
            _physicalLoads.Add(scene.name);
    }

    private void Capture(Scene scene)
    {
        _addressableScene = scene;
        _retainedProbes = Probes(scene);
    }

    private static OrdinaryJoinSceneProbe[] Probes(Scene scene)
    {
        var result = new List<OrdinaryJoinSceneProbe>();
        foreach (var root in scene.GetRootGameObjects())
            result.AddRange(root.GetComponentsInChildren<OrdinaryJoinSceneProbe>(true));
        if (result.Count != 2)
            throw new InvalidOperationException($"Expected two authored probes in {scene.name}, got {result.Count}.");
        return result.ToArray();
    }

    private static async UniTask LoadOffline(string name, ScenarioContext ctx)
    {
        var operation = SceneManager.LoadSceneAsync(name, LoadSceneMode.Single);
        await UniTaskUtils.WaitWithTimeout(() => operation.isDone, Timeout, ctx.cancellationToken);
    }
}
