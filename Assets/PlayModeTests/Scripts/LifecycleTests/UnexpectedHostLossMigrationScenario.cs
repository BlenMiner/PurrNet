using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run alone with Bootstrap. The external runner kills its tracked authority process
// after the ready marker; this scenario never shuts down the original authority.
public sealed class UnexpectedHostLossMigrationScenario : Scenario
{
    private const string TargetScene = "SceneMembershipTargetA";
    private const string CycleScene = "SceneMembershipTargetB";
    private const int InitialServerRoot = 8111;
    private const int InitialServerChild = 8112;
    private const int InitialOwnerRoot = 8211;
    private const int InitialOwnerChild = 8212;
    private const int DivergentOwnerRoot = 8511;
    private const int DivergentOwnerChild = 8512;
    private const int PostServerRoot = 8311;
    private const int PostServerChild = 8312;
    private const int PostOwnerRoot = 8411;
    private const int PostOwnerChild = 8412;

    [SerializeField] private NetworkRules _rules;
    [SerializeField] private float _timeoutSeconds = 60f;

    private static ulong[] _externalRoster;
    private static ulong _ownerId;
    private static bool _postStatePhase;
    private static bool _migrationComplete;
    private static string _readinessError;
    private static readonly HashSet<ulong> InitialReady = new();
    private static readonly HashSet<ulong> Restored = new();
    private static readonly HashSet<ulong> PostStateObserved = new();
    private static readonly HashSet<ulong> FreshObserved = new();
    private static readonly HashSet<ulong> FreshGone = new();
    private static readonly HashSet<ulong> SceneLoaded = new();
    private static readonly HashSet<ulong> SceneUnloaded = new();
    private static readonly HashSet<ulong> Completed = new();

    private NetworkManager _manager;
    private UDPTransport _udp;
    private UnexpectedHostLossMigrationProbe _prefab;
    private UnexpectedHostLossMigrationProbe _retainedRoot;
    private UnexpectedHostLossMigrationProbe _retainedChild;
    private TransferContinuitySnapshot _snapshot;
    private Scene _retainedScene;
    private int _rootSpawns;
    private int _childSpawns;
    private ulong _localId;
    private ulong _electedId;
    private ushort _initialPort;
    private ushort _migrationPort;
    private string _readyPath;
    private string _receiptPath;
    private bool _handlerInstalled;
    private bool _waitingForUnexpectedLoss;
    private bool _disconnected;
    private bool _migrationStarted;
    private bool _followerRespawned;
    private int _preservationCalls;

    [Serializable]
    private sealed class CrashMarker
    {
        public string scenario = nameof(UnexpectedHostLossMigrationScenario);
        public bool ready = true;
    }

    [Serializable]
    private sealed class CrashReceipt
    {
        public string scenario;
        public bool injected;
        public int authorityPid;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _manager = manager;
        _externalRoster = null;
        _ownerId = 0;
        _postStatePhase = false;
        _migrationComplete = false;
        _readinessError = null;
        InitialReady.Clear();
        Restored.Clear();
        PostStateObserved.Clear();
        FreshObserved.Clear();
        FreshGone.Clear();
        SceneLoaded.Clear();
        SceneUnloaded.Clear();
        Completed.Clear();
        UnexpectedHostLossMigrationProbe.ResetAll();

        var root = new GameObject(nameof(UnexpectedHostLossMigrationProbe));
        _prefab = root.AddComponent<UnexpectedHostLossMigrationProbe>();
        root.AddComponent<NetworkTransform>();
        var child = new GameObject("UnexpectedHostLossMigrationChild");
        child.transform.SetParent(root.transform);
        child.AddComponent<UnexpectedHostLossMigrationProbe>().ConfigureChild();
        if (_rules)
            foreach (var identity in root.GetComponentsInChildren<NetworkIdentity>(true))
                identity.SetNetworkRules(_rules);
        root.SetActive(false);
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, root);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        try
        {
            PrepareContract(ctx);
            return ctx.isServer ? await RunAuthority(ctx) : await RunSurvivor(ctx);
        }
        catch (Exception e)
        {
            return ScenarioResult.Fail($"unexpected host loss: {e.GetType().Name}: {e.Message}; " +
                $"local={_localId}, elected={_electedId}, disconnect={_disconnected}, preserveCalls={_preservationCalls}, " +
                $"client={_manager.clientState}, server={_manager.serverState}, endpoint={_udp?.serverPort}");
        }
        finally
        {
            if (_handlerInstalled)
                _manager.onClientConnectionState -= OnClientConnectionState;
            if (_preservationCalls > 0 && !_migrationStarted)
                _manager.ReleaseClientStateForHostMigration();
        }
    }

    private void PrepareContract(ScenarioContext ctx)
    {
        if (!_rules)
            throw new InvalidOperationException("Assign the migration identity rules that retain disconnected owners.");
        if (ctx.isServer && ctx.expectedConnections < 3)
            throw new InvalidOperationException("Crash migration requires at least three players in host or dedicated mode.");
        _udp = _manager.transport as UDPTransport;
        if (!_udp)
            throw new InvalidOperationException("Unexpected host loss scenario requires UDPTransport.");
        _initialPort = _udp.serverPort;
        if (!CommandLineUtils.TryGetArgument("-migrationPort", out var port) ||
            !ushort.TryParse(port, out _migrationPort) || _migrationPort == 0 || _migrationPort == _initialPort)
            throw new InvalidOperationException("Pass a valid -migrationPort different from the initial -port.");
        if (!CommandLineUtils.TryGetArgument("-results", out var results) ||
            string.IsNullOrEmpty(Path.GetDirectoryName(results)))
            throw new InvalidOperationException("Pass -results in the runner's shared fresh directory.");
        var directory = Path.GetFullPath(Path.GetDirectoryName(results));
        Directory.CreateDirectory(directory);
        _readyPath = Path.Combine(directory, "migration-crash-ready.json");
        _receiptPath = Path.Combine(directory, "migration-crash-injected.json");
        if (ctx.isServer && (File.Exists(_readyPath) || File.Exists(_receiptPath)))
            throw new InvalidOperationException("Crash-injection results directory is not fresh.");
    }

    private async UniTask<ScenarioResult> RunAuthority(ScenarioContext ctx)
    {
        var roster = new List<ulong>();
        foreach (var player in _manager.players)
            if (!player.isServer && !(ctx.role == NetworkRole.Host && _manager.isLocalPlayerReady && player == _manager.localPlayer))
                roster.Add(player.id.value);
        roster.Sort();
        if (roster.Count < 2)
            return ScenarioResult.Fail("Crash migration requires at least two surviving external clients.");

        // This is a roster and initial ownership assignment, not a promotion plan.
        // Survivors make the deterministic election only after observing the loss.
        BroadcastRoster(roster.ToArray(), roster[roster.Count - 1]);
        var load = _manager.sceneModule.LoadSceneAsync(TargetScene, LoadSceneMode.Additive);
        if (load == null)
            throw new InvalidOperationException("Could not load the initial public scene.");
        await Wait(ctx, () => load.isDone && IsRegistered(TargetScene));
        var root = await SpawnProbe(ctx, 1);
        var child = FindChild(root);
        root.GiveOwnership(new PlayerID(roster[roster.Count - 1], false), propagateToChildren: true);
        root.SetServerValue(InitialServerRoot);
        child.SetServerValue(InitialServerChild);

        await Wait(ctx, () => _readinessError != null || AllReported(InitialReady, includeCandidate: true));
        if (_readinessError != null)
            throw new InvalidOperationException(_readinessError);
        await Wait(ctx, () => root.HasState(InitialServerRoot, InitialOwnerRoot) &&
                              child.HasState(InitialServerChild, InitialOwnerChild));

        WriteAtomic(_readyPath, JsonUtility.ToJson(new CrashMarker()));
        Debug.Log("[UnexpectedHostLossMigrationScenario] All external replicas and disconnect callbacks ready; waiting for external process kill.");
        // Deliberately no StopClient, StopServer, Application.Quit, or self-kill.
        await UniTask.WaitUntil(() => false, cancellationToken: ctx.cancellationToken);
        return ScenarioResult.Fail("Original authority unexpectedly resumed after its crash wait.");
    }

    private async UniTask<ScenarioResult> RunSurvivor(ScenarioContext ctx)
    {
        await Wait(ctx, () => _externalRoster != null && _externalRoster.Length >= 2 && _manager.isLocalPlayerReady);
        _localId = _manager.localPlayer.id.value;
        if (Array.IndexOf(_externalRoster, _localId) < 0)
            throw new InvalidOperationException("This external client is absent from the pre-crash roster.");
        await Wait(ctx, () => InitialWorldReady());
        _retainedRoot = UnexpectedHostLossMigrationProbe.Find(1, false, false);
        _retainedChild = UnexpectedHostLossMigrationProbe.Find(1, true, false);
        if (_localId == _ownerId)
        {
            await Wait(ctx, () => _retainedRoot.isOwner && _retainedChild.isOwner && _retainedRoot.hasConnectedOwner);
            _retainedRoot.SetOwnerValue(InitialOwnerRoot);
            _retainedChild.SetOwnerValue(InitialOwnerChild);
        }
        await Wait(ctx, () => _retainedRoot.HasState(InitialServerRoot, InitialOwnerRoot) &&
                              _retainedChild.HasState(InitialServerChild, InitialOwnerChild));
        _retainedScene = _retainedRoot.gameObject.scene;
        _snapshot = TransferContinuitySnapshot.Capture<UnexpectedHostLossMigrationProbe, NetworkTransform>(TargetScene);
        _rootSpawns = _retainedRoot.clientSpawnCount;
        _childSpawns = _retainedChild.clientSpawnCount;
        if (_rootSpawns != 1 || _childSpawns != 1)
            throw new InvalidOperationException("Initial replica did not receive exactly one root/child client spawn.");
        RequireContinuity(false, checkClientCallbacks: true);

        _waitingForUnexpectedLoss = true;
        _manager.onClientConnectionState += OnClientConnectionState;
        _handlerInstalled = true;
        // Preservation has NOT been armed. Only the callback below can arm it.
        ReportInitialReady(_handlerInstalled, _preservationCalls);
        await Wait(ctx, () => _disconnected);
        if (_preservationCalls != 1)
            throw new InvalidOperationException("Loss did not synchronously arm preservation exactly once.");

        // Exercise delayed application-level preparation after an unannounced loss.
        await UniTask.WaitForSeconds(1f, cancellationToken: ctx.cancellationToken);
        RequireContinuity(false, checkClientCallbacks: true);
        await Wait(ctx, () => File.Exists(_receiptPath));
        RequireCrashReceipt();
        _electedId = _externalRoster[0];
        foreach (var id in _externalRoster)
            if (id < _electedId)
                _electedId = id;

        _udp.address = "127.0.0.1";
        _udp.serverPort = _migrationPort;
        if (_localId == _ownerId && _localId != _electedId)
        {
            Debug.Log($"[UnexpectedHostLossMigrationScenario] owner={_localId} deliberately delays reconnect by 2 seconds so other followers can finish their own transfer first.");
            await UniTask.WaitForSeconds(2f, cancellationToken: ctx.cancellationToken);
        }
        Debug.Log($"[UnexpectedHostLossMigrationScenario] local={_localId} elected={_electedId}; switching {_initialPort}->{_migrationPort} after loss delay.");
        return _localId == _electedId ? await RunCandidate(ctx) : await RunFollower(ctx);
    }

    private void OnClientConnectionState(ConnectionState state)
    {
        if (!_waitingForUnexpectedLoss || state != ConnectionState.Disconnected)
            return;
        // Must remain synchronous and precede every await/election/endpoint change.
        _manager.PreserveClientStateForHostMigration();
        _preservationCalls++;
        _waitingForUnexpectedLoss = false;
        _disconnected = true;
        Debug.Log($"[UnexpectedHostLossMigrationScenario] local={_localId} observed Disconnected; preserved synchronously.");
    }

    private async UniTask<ScenarioResult> RunCandidate(ScenarioContext ctx)
    {
        ExpectMigrationLifecycle(asServer: true);
        _migrationStarted = true;
        if (!await _manager.PromoteToServerAsync(_timeoutSeconds, ctx.cancellationToken))
            throw new InvalidOperationException("Awaited promotion returned false after external host loss.");
        await Wait(ctx, () => _manager.isServer && InitialServerWorldReady());
        RequireMigrationLifecycle();
        RequireContinuity(true, checkClientCallbacks: false);
        await Wait(ctx, () => AllReported(Restored, includeCandidate: false));
        RequireOwnership(true);
        if (!InitialServerWorldReady())
            throw new InvalidOperationException("A returning owner's divergent local values replaced the elected host's known state.");

        _retainedRoot.SetServerValue(PostServerRoot);
        _retainedChild.SetServerValue(PostServerChild);
        BroadcastPostStatePhase();
        await Wait(ctx, () => PostWorldReady(true) && AllReported(PostStateObserved, includeCandidate: false));

        var fresh = await SpawnProbe(ctx, 2);
        if (fresh.id == _retainedRoot.id || FindChild(fresh).id == _retainedChild.id)
            throw new InvalidOperationException("A fresh spawn reused a retained object's NetworkID.");
        await Wait(ctx, () => AllReported(FreshObserved, includeCandidate: false));
        fresh.Despawn();
        await Wait(ctx, () => UnexpectedHostLossMigrationProbe.Count(2, true) == 0 && AllReported(FreshGone, includeCandidate: false));

        var serverContext = ctx;
        serverContext.role = NetworkRole.Server;
        var cycle = await PostMigrationSceneCycle.RunServer(serverContext, TargetScene, CycleScene, _timeoutSeconds,
            () => SceneLoaded.Count, () => SceneUnloaded.Count, _externalRoster.Length - 1);
        Require(cycle);
        RequireContinuity(true, checkClientCallbacks: false);
        RequireCrashReceipt();
        BroadcastMigrationComplete();
        await Wait(ctx, () => AllReported(Completed, includeCandidate: false));
        ScenarioSequencer.IssueSequenceComplete();
        var handshakeContext = ctx;
        handshakeContext.role = _manager.isClient ? NetworkRole.Host : NetworkRole.Server;
        await ScenarioSequencer.WaitForEndOfRunHandshake(handshakeContext);
        return ScenarioResult.Ok($"unexpected host loss: elected={_electedId}, awaited promotion succeeded on new port {_migrationPort}; replica, ownership, fresh spawns and scene cycle retained");
    }

    private async UniTask<ScenarioResult> RunFollower(ScenarioContext ctx)
    {
        if (_localId == _ownerId)
        {
            if (_manager.clientState != ConnectionState.Disconnected || !InitialServerWorldReady(false))
                throw new InvalidOperationException("Divergent owner state must start from the known baseline while disconnected.");
            RequireOwnerIds();
            // No await or tick between these dirty local writes and transfer: the
            // disconnected owner's changes have never reached the elected host.
            _retainedRoot.SetOwnerValue(DivergentOwnerRoot);
            _retainedChild.SetOwnerValue(DivergentOwnerChild);
            if (!_retainedRoot.HasState(InitialServerRoot, DivergentOwnerRoot) ||
                !_retainedChild.HasState(InitialServerChild, DivergentOwnerChild) || InitialServerWorldReady(false))
                throw new InvalidOperationException("The owner's local values did not actually diverge before transfer.");
            Debug.Log($"[UnexpectedHostLossMigrationScenario] owner={_localId} has unsent local values/list entries {DivergentOwnerRoot}/{DivergentOwnerChild} and root pose {_retainedRoot.transform.localPosition}; elected host baseline remains {InitialOwnerRoot}/{InitialOwnerChild}.");
        }
        ExpectMigrationLifecycle();
        _migrationStarted = true;
        if (!await _manager.TransferToNewServerAsync(preserveWorld: true, timeoutSeconds: _timeoutSeconds,
                cancellationToken: ctx.cancellationToken))
            throw new InvalidOperationException("Awaited preserving transfer returned false after external host loss.");
        _followerRespawned = true;
        if (!_manager.isLocalPlayerReady || _manager.localPlayer.id.value != _localId)
            throw new InvalidOperationException("Follower lost its original PlayerID.");
        RequireContinuity(false, checkClientCallbacks: true);
        RequireMigrationLifecycle();
        Debug.Log("[UnexpectedHostLossMigrationScenario] Retained instances ran normal despawn/spawn callbacks with isMigratingServer=true; host values, list and pose were applied in OnSpawned.");
        // NetworkTransform interpolation can settle after hierarchy reconciliation.
        await Wait(ctx, () => InitialServerWorldReady(false));
        RequireOwnerIds();
        // Local reconciliation can finish before another surviving peer reconnects.
        // Keep the preserved owner assignment strict, but await its independent connection.
        await Wait(ctx, () => _retainedRoot && _retainedChild &&
                              _retainedRoot.hasConnectedOwner && _retainedChild.hasConnectedOwner);
        RequireOwnership(false);
        ReportRestored();
        await Wait(ctx, () => _postStatePhase);
        if (_localId == _ownerId)
        {
            _retainedRoot.SetOwnerValue(PostOwnerRoot);
            _retainedChild.SetOwnerValue(PostOwnerChild);
        }
        await Wait(ctx, () => PostWorldReady(false));
        RequireOwnership(false);
        ReportPostStateObserved();

        await Wait(ctx, () => UnexpectedHostLossMigrationProbe.Count(2, false) == 2);
        var freshRoot = UnexpectedHostLossMigrationProbe.Find(2, false, false);
        var freshChild = UnexpectedHostLossMigrationProbe.Find(2, true, false);
        if (!freshRoot || !freshChild || freshRoot == _retainedRoot || freshChild == _retainedChild ||
            freshRoot.clientSpawnCount != 1 || freshChild.clientSpawnCount != 1 ||
            freshRoot.migrationLifecycleError != null || freshChild.migrationLifecycleError != null ||
            freshChild.transform.parent != freshRoot.transform || freshRoot.id == _retainedRoot.id || freshChild.id == _retainedChild.id)
            throw new InvalidOperationException("Fresh post-migration hierarchy was duplicated or confused with retained objects.");
        RequireContinuity(false, checkClientCallbacks: true);
        ReportFreshObserved();
        await Wait(ctx, () => UnexpectedHostLossMigrationProbe.Count(2, false) == 0);
        ReportFreshGone();

        var cycle = await PostMigrationSceneCycle.RunClient(ctx, TargetScene, CycleScene, _timeoutSeconds,
            () => ReportSceneLoaded(), () => ReportSceneUnloaded());
        Require(cycle);
        await Wait(ctx, () => _migrationComplete);
        RequireContinuity(false, checkClientCallbacks: true);
        RequireOwnership(false);
        RequireCrashReceipt();
        if (!PostWorldReady(false))
            throw new InvalidOperationException("Post-migration state changed during the fresh-spawn/scene cycle.");
        ReportCompleted();
        return ScenarioResult.Ok($"unexpected host loss: followed elected={_electedId} on new port {_migrationPort}; retained Unity instances ran normal network lifecycle with migration flag and host state");
    }

    private bool InitialWorldReady()
    {
        var root = UnexpectedHostLossMigrationProbe.Find(1, false, false);
        var child = UnexpectedHostLossMigrationProbe.Find(1, true, false);
        return root && child && root.owner.HasValue && child.owner.HasValue &&
               root.owner.Value.id.value == _ownerId && child.owner.Value.id.value == _ownerId &&
               root.HasState(InitialServerRoot, _localId == _ownerId ? 0 : InitialOwnerRoot) &&
               child.HasState(InitialServerChild, _localId == _ownerId ? 0 : InitialOwnerChild);
    }

    private bool InitialServerWorldReady(bool asServer = true)
    {
        return _retainedRoot && _retainedChild && _retainedRoot.IsSpawned(asServer) && _retainedChild.IsSpawned(asServer) &&
               _retainedRoot.HasState(InitialServerRoot, InitialOwnerRoot) && _retainedChild.HasState(InitialServerChild, InitialOwnerChild);
    }

    private bool PostWorldReady(bool asServer)
    {
        return _retainedRoot && _retainedChild && _retainedRoot.IsSpawned(asServer) && _retainedChild.IsSpawned(asServer) &&
               _retainedRoot.HasState(PostServerRoot, PostOwnerRoot) && _retainedChild.HasState(PostServerChild, PostOwnerChild);
    }

    private void RequireOwnerIds()
    {
        foreach (var probe in new[] { _retainedRoot, _retainedChild })
        {
            if (!probe || !probe.owner.HasValue || probe.owner.Value.id.value != _ownerId)
                throw new InvalidOperationException("Retained root/child lost their original owner PlayerID.");
        }
    }

    private void RequireOwnership(bool asServer)
    {
        RequireOwnerIds();
        foreach (var probe in new[] { _retainedRoot, _retainedChild })
        {
            if (!probe.hasConnectedOwner)
                throw new InvalidOperationException("Retained root/child ownership did not reconnect on the replacement server.");
            if (!asServer && _localId == _ownerId && (!probe.isOwner || !probe.isController))
                throw new InvalidOperationException("The retained owner lost controller authority.");
        }
    }

    private void RequireContinuity(bool asServer, bool checkClientCallbacks)
    {
        Require(_snapshot.Verify("unexpected host loss continuity", asServer));
        if (!_retainedScene.IsValid() || !_retainedScene.isLoaded || !_retainedRoot || !_retainedChild ||
            _retainedRoot.gameObject.scene != _retainedScene || _retainedChild.gameObject.scene != _retainedScene ||
            _retainedChild.transform.parent != _retainedRoot.transform)
            throw new InvalidOperationException("Original Unity scene or root/child instances were replaced.");
        var respawns = _followerRespawned ? 1 : 0;
        if (checkClientCallbacks && (_retainedRoot.clientSpawnCount != _rootSpawns + respawns ||
                                    _retainedChild.clientSpawnCount != _childSpawns + respawns ||
                                    _retainedRoot.clientDespawnCount != respawns || _retainedChild.clientDespawnCount != respawns))
            throw new InvalidOperationException("Retained client did not run exactly one normal despawn/spawn lifecycle per transfer.");
        if (_retainedRoot.migrationLifecycleError != null || _retainedChild.migrationLifecycleError != null)
            throw new InvalidOperationException(_retainedRoot.migrationLifecycleError ?? _retainedChild.migrationLifecycleError);
        if (_preservationCalls > 1)
            throw new InvalidOperationException("Preservation was armed more than once.");
    }

    private void ExpectMigrationLifecycle(bool asServer = false)
    {
        _retainedRoot.ExpectMigrationLifecycle(InitialServerRoot, InitialOwnerRoot, asServer);
        _retainedChild.ExpectMigrationLifecycle(InitialServerChild, InitialOwnerChild, asServer);
    }

    private void RequireMigrationLifecycle()
    {
        foreach (var probe in new[] { _retainedRoot, _retainedChild })
        {
            if (probe.migrationLifecycleError != null)
                throw new InvalidOperationException(probe.migrationLifecycleError);
            if (probe.migrationDespawnChecks != 1 || probe.migrationSpawnChecks != 1)
                throw new InvalidOperationException($"{probe.name}: expected exactly one migration despawn and spawn, got {probe.migrationDespawnChecks}/{probe.migrationSpawnChecks}.");
            if (probe.isMigratingServer || _manager.isMigratingServer)
                throw new InvalidOperationException("Migration flag remained set after the awaited operation completed.");
        }
    }

    private void RequireCrashReceipt()
    {
        var receipt = JsonUtility.FromJson<CrashReceipt>(File.ReadAllText(_receiptPath));
        if (receipt == null || receipt.scenario != nameof(UnexpectedHostLossMigrationScenario) || !receipt.injected || receipt.authorityPid <= 0)
            throw new InvalidOperationException("Runner did not confirm an external authority process kill.");
    }

    private async UniTask<UnexpectedHostLossMigrationProbe> SpawnProbe(ScenarioContext ctx, int kind)
    {
        var scene = SceneManager.GetSceneByName(TargetScene);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("The retained scene is not loaded for spawning.");
        var root = UnityProxy.InstantiateDirectly(_prefab);
        if (root.gameObject.scene != scene)
            SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        var child = FindChild(root);
        root.Spawn(_prefab.gameObject, _manager);
        await Wait(ctx, () => root && child && root.IsSpawned(true) && child.IsSpawned(true));
        root.SetKind(kind);
        child.SetKind(kind);
        return root;
    }

    private static UnexpectedHostLossMigrationProbe FindChild(UnexpectedHostLossMigrationProbe root)
    {
        foreach (var probe in root.GetComponentsInChildren<UnexpectedHostLossMigrationProbe>(true))
            if (probe.isChild)
                return probe;
        throw new InvalidOperationException("Runtime probe prefab lost its child.");
    }

    private bool IsRegistered(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded && _manager.sceneModule.TryGetSceneID(scene, out _);
    }

    private UniTask Wait(ScenarioContext ctx, Func<bool> predicate) =>
        UniTaskUtils.WaitWithTimeout(predicate, _timeoutSeconds, ctx.cancellationToken);

    private static void Require(ScenarioResult result)
    {
        if (!result.success)
            throw new InvalidOperationException(result.message);
    }

    private static bool AllReported(HashSet<ulong> reports, bool includeCandidate)
    {
        if (_externalRoster == null)
            return false;
        var candidate = ulong.MaxValue;
        foreach (var id in _externalRoster)
            if (id < candidate)
                candidate = id;
        foreach (var id in _externalRoster)
            if ((includeCandidate || id != candidate) && !reports.Contains(id))
                return false;
        return true;
    }

    private static void WriteAtomic(string path, string text)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path);
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastRoster(ulong[] roster, ulong ownerId)
    {
        _externalRoster = roster;
        _ownerId = ownerId;
    }

    [ServerRpc(requireOwnership: false)]
    private static void ReportInitialReady(bool handlerInstalled, int preservationCalls, RPCInfo info = default)
    {
        if (!handlerInstalled || preservationCalls != 0)
            _readinessError = "A survivor was not ready or pre-armed preservation before unexpected loss.";
        InitialReady.Add(info.sender.id.value);
    }

    [ServerRpc(requireOwnership: false)]
    private static void ReportRestored(RPCInfo info = default) => Restored.Add(info.sender.id.value);
    [ServerRpc(requireOwnership: false)]
    private static void ReportPostStateObserved(RPCInfo info = default) => PostStateObserved.Add(info.sender.id.value);
    [ServerRpc(requireOwnership: false)]
    private static void ReportFreshObserved(RPCInfo info = default) => FreshObserved.Add(info.sender.id.value);
    [ServerRpc(requireOwnership: false)]
    private static void ReportFreshGone(RPCInfo info = default) => FreshGone.Add(info.sender.id.value);
    [ServerRpc(requireOwnership: false)]
    private static void ReportSceneLoaded(RPCInfo info = default) => SceneLoaded.Add(info.sender.id.value);
    [ServerRpc(requireOwnership: false)]
    private static void ReportSceneUnloaded(RPCInfo info = default) => SceneUnloaded.Add(info.sender.id.value);
    [ServerRpc(requireOwnership: false)]
    private static void ReportCompleted(RPCInfo info = default) => Completed.Add(info.sender.id.value);

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPostStatePhase() => _postStatePhase = true;
    [ObserversRpc(runLocally: true)]
    private static void BroadcastMigrationComplete() => _migrationComplete = true;
}
