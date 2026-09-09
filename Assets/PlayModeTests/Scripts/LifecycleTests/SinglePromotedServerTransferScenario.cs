using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SinglePromotedServerTransferScenario : Scenario
{
    private const string TargetSceneName = "SceneMembershipTargetB";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneMembershipTargetB.unity";
    private const string PostMigrationSceneName = "SceneMembershipTargetA";
    private const int ExpectedChildren = 1;
    private const int RootServerStateValue = 8311;
    private const int ChildServerStateValue = 8312;
    private const int RootOwnerPrePromotionValue = 8411;
    private const int ChildOwnerPrePromotionValue = 8412;
    private const int RootOwnerPostTransferValue = 8511;
    private const int ChildOwnerPostTransferValue = 8512;

    [SerializeField] private NetworkRules _rules;
    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _promotionTimeoutSeconds = 45f;
    [SerializeField] private float _transferTimeoutSeconds = 45f;
    [SerializeField] private float _promotionStartupDelaySeconds = 1f;
    [SerializeField] private float _flushDelaySeconds = 0.2f;

    private static ulong _promotedId;
    private static ulong _ownerId;
    private static int _expectedTransfers;
    private static bool _planReceived;
    private static bool _promotionCommandReceived;
    private static int _initialObservedCount;
    private static int _transferRestoredCount;
    private static int _postMigrationLoadedCount;
    private static int _postMigrationUnloadedCount;
    private static bool _probeVisibilityReady;
    private static bool _prePromotionOwnerStateSent;
    private static bool _postTransferOwnerStateSent;
    private TransferContinuitySnapshot _retainedHierarchy;
    private TransferContinuitySnapshot _retainedPlayers;
    private int _rootSpawnsBeforeMigration;
    private int _childSpawnsBeforeMigration;
    private SinglePromotedServerTransferRoot _prefab;
    private SinglePromotedPlayerPrefabRoot _playerPrefab;
    private MigrationReconcileProbe _probePrefab;
    private MigrationReconcileProbe _probeBeforeMigration;

    private void CreatePrefab()
    {
        var rootGo = new GameObject(nameof(SinglePromotedServerTransferRoot));
        _prefab = rootGo.AddComponent<SinglePromotedServerTransferRoot>();

        var childGo = new GameObject(nameof(SinglePromotedServerTransferChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<SinglePromotedServerTransferChild>();

        var identities = rootGo.GetComponentsInChildren<NetworkIdentity>(true);
        for (int i = 0; i < identities.Length; i++)
            identities[i].skipSceneAutoSpawning = true;

        if (_rules)
        {
            for (int i = 0; i < identities.Length; i++)
                identities[i].SetNetworkRules(_rules);
        }
        else
        {
            Debug.LogError("[SinglePromotedServerTransferScenario] _rules is not assigned; the owned identity must survive promotion.");
        }

        rootGo.SetActive(false);
        CreatePlayerPrefab();
        var probeGo = new GameObject(nameof(MigrationReconcileProbe));
        _probePrefab = probeGo.AddComponent<MigrationReconcileProbe>();
        _probePrefab.skipSceneAutoSpawning = true;
        _probePrefab.SetNetworkRules(_rules);
        probeGo.SetActive(false);
        MigrationReconcileProbe.ResetAll();
        SinglePromotedServerTransferRoot.ResetAll();
        SinglePromotedServerTransferChild.ResetAll();
        SinglePromotedPlayerPrefabRoot.ResetAll();
        SinglePromotedPlayerPrefabChild.ResetAll();
        _promotedId = 0;
        _ownerId = 0;
        _expectedTransfers = 0;
        _planReceived = false;
        _promotionCommandReceived = false;
        _initialObservedCount = 0;
        _transferRestoredCount = 0;
        _postMigrationLoadedCount = 0;
        _postMigrationUnloadedCount = 0;
        _probeVisibilityReady = false;
        _prePromotionOwnerStateSent = false;
        _postTransferOwnerStateSent = false;
    }

    private void CreatePlayerPrefab()
    {
        var rootGo = new GameObject(nameof(SinglePromotedPlayerPrefabRoot));
        _playerPrefab = rootGo.AddComponent<SinglePromotedPlayerPrefabRoot>();

        var childGo = new GameObject(nameof(SinglePromotedPlayerPrefabChild));
        childGo.transform.SetParent(rootGo.transform);
        childGo.AddComponent<SinglePromotedPlayerPrefabChild>();

        var identities = rootGo.GetComponentsInChildren<NetworkIdentity>(true);
        for (int i = 0; i < identities.Length; i++)
            identities[i].skipSceneAutoSpawning = true;

        if (_rules)
        {
            for (int i = 0; i < identities.Length; i++)
                identities[i].SetNetworkRules(_rules);
        }

        rootGo.SetActive(false);
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_playerPrefab.name, _playerPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_probePrefab.name, _probePrefab.gameObject);
    }

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        PreserveSingleSceneHarness(ctx);
        return RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private void PreserveSingleSceneHarness(ScenarioContext ctx)
    {
        UnityEngine.Object.DontDestroyOnLoad(transform.root.gameObject);
        UnityEngine.Object.DontDestroyOnLoad(ctx.networkManager.gameObject);
        PreserveRuntimePrefabs(ctx.networkManager.prefabProvider);
    }

    private static void PreserveRuntimePrefabs(IPrefabProvider provider)
    {
        if (provider == null)
            return;

        foreach (var data in provider.allPrefabs)
        {
            var prefab = data.prefab;
            if (!prefab)
                continue;

            var root = prefab.transform.root ? prefab.transform.root.gameObject : prefab;
            if (root && root.scene.IsValid() && root.scene.isLoaded)
                UnityEngine.Object.DontDestroyOnLoad(root);
        }
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        if (!_rules)
            return ScenarioResult.Fail("single promotion transfer: _rules is not assigned");

        int buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"single promotion transfer: target scene missing from build settings: {TargetScenePath}");

        var load = await LoadSingleScene(ctx, buildIndex);
        if (!load.success) return load;

        var promoted = PickPromotedClient(ctx);
        if (!promoted.HasValue)
        {
            BroadcastPlan(0, 0, 0);
            return ScenarioResult.Ok("single promotion transfer requires a non-host client to promote");
        }

        var owner = PickTransferClient(ctx, promoted.Value);
        if (!owner.HasValue)
        {
            BroadcastPlan(0, 0, 0);
            return ScenarioResult.Ok("single promotion transfer requires two non-host clients");
        }

        int expectedTransfers = CountTransferClients(ctx, promoted.Value);
        if (expectedTransfers <= 0)
        {
            BroadcastPlan(0, 0, 0);
            return ScenarioResult.Ok("single promotion transfer requires at least one transfer client");
        }

        BroadcastPlan(promoted.Value.id.value, owner.Value.id.value, expectedTransfers);

        var playerPrefabs = SpawnPlayerPrefabsInScene(ctx, SceneManager.GetSceneByName(TargetSceneName));
        if (!playerPrefabs.success) return playerPrefabs;

        var serverPlayers = await WaitForServerPlayerPrefabs(ctx, "initial server player prefabs", _spawnTimeoutSeconds);
        if (!serverPlayers.success) return serverPlayers;

        var instance = SpawnInScene(SceneManager.GetSceneByName(TargetSceneName));

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SinglePromotedServerTransferRoot.ServerAliveCount == 1
                      && SinglePromotedServerTransferChild.ServerAliveCount == ExpectedChildren,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer server spawn timeout: {DescribeState(ctx)}");
        }

        if (SinglePromotedServerTransferRoot.SawBadId || SinglePromotedServerTransferChild.SawBadId)
            return ScenarioResult.Fail($"single promotion transfer server spawn saw default id: {DescribeState(ctx)}");

        instance.GiveOwnership(owner.Value, propagateToChildren: true);
        SetServerAuthState(instance);

        var probes = await PrepareReconciliationProbes(ctx, promoted.Value);
        if (!probes.success) return probes;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _initialObservedCount >= ctx.expectedConnections,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"single promotion transfer initial observation timeout: got {_initialObservedCount}/{ctx.expectedConnections}; {DescribeState(ctx)}");
        }

        BroadcastPromotionCommand();

        await UniTask.WaitForSeconds(_flushDelaySeconds, cancellationToken: ctx.cancellationToken);

        ctx.networkManager.StopClient();
        ctx.networkManager.StopServer();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Disconnected
                      && ctx.networkManager.serverState == ConnectionState.Disconnected
                      && ctx.networkManager.playerCount == 0,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer original server shutdown timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok(
            $"promoted={promoted.Value.id.value}, owner={owner.Value.id.value}, transfers={expectedTransfers}");
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var plan = await WaitForPlan(ctx);
        if (!plan.success) return plan;

        if (_expectedTransfers <= 0)
            return ScenarioResult.Ok("single promotion transfer not applicable to this topology");

        bool isPromoted = IsLocal(_promotedId, ctx);
        bool isOriginalHostLocal = ctx.role == NetworkRole.Host && !isPromoted;
        bool shouldTransfer = ctx.role == NetworkRole.Client && !isPromoted;

        if (IsLocal(_ownerId, ctx))
        {
            var seeded = await SeedOwnerState(
                ctx,
                RootOwnerPrePromotionValue,
                ChildOwnerPrePromotionValue,
                postTransfer: false,
                "pre-promotion owner state");
            if (!seeded.success) return seeded;
        }

        var initial = await WaitForClientScene(
            ctx,
            "initial single promotion transfer scene",
            requireRetainedSpawn: false,
            rootSpawnsBefore: 0,
            childSpawnsBefore: 0,
            requireOwnerState: IsLocal(_ownerId, ctx));
        if (!initial.success) return initial;

        var playerInitial = await WaitForLocalPlayerPrefab(ctx, "initial player prefab");
        if (!playerInitial.success) return playerInitial;

        if (!isOriginalHostLocal)
        {
            var probes = await WaitForInitialProbeVisibility(ctx, isPromoted);
            if (!probes.success) return probes;
            _probeBeforeMigration = MigrationReconcileProbe.Find(
                isPromoted ? MigrationReconcileProbe.CandidateOnly : MigrationReconcileProbe.OldHostOnly);
        }

        if (!isOriginalHostLocal)
        {
            _retainedHierarchy = TransferContinuitySnapshot.Capture<SinglePromotedServerTransferRoot, SinglePromotedServerTransferChild>(TargetSceneName);
            _retainedPlayers = TransferContinuitySnapshot.Capture<SinglePromotedPlayerPrefabRoot, SinglePromotedPlayerPrefabChild>(TargetSceneName);
            _rootSpawnsBeforeMigration = SinglePromotedServerTransferRoot.ClientSpawnCount;
            _childSpawnsBeforeMigration = SinglePromotedServerTransferChild.ClientSpawnCount;
            // Arm every surviving peer before acknowledging; the old host can stop immediately afterward.
            ctx.networkManager.PreserveClientStateForHostMigration();
        }

        SignalInitialObserved();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _promotionCommandReceived,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer command timeout: {DescribeState(ctx)}");
        }

        if (isOriginalHostLocal)
            return await WaitForOriginalHostShutdown(ctx);

        if (isPromoted)
            return await RunPromotedClient(ctx);

        if (shouldTransfer)
            return await RunTransferClient(ctx);

        return ScenarioResult.Fail($"single promotion transfer: unhandled client role: {DescribeState(ctx)}");
    }

    private async UniTask<ScenarioResult> RunPromotedClient(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Disconnected,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            ctx.networkManager.ReleaseClientStateForHostMigration();
            return ScenarioResult.Fail($"single promotion transfer promoted client did not detach from old server: {DescribeState(ctx)}");
        }

        await UniTask.WaitForSeconds(_promotionStartupDelaySeconds, cancellationToken: ctx.cancellationToken);

        ctx.networkManager.PromoteToServer();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.isServer
                      && ctx.networkManager.serverState == ConnectionState.Connected,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer promoted client did not become server: {DescribeState(ctx)}");
        }

        var promotedLocal = await WaitForLocalPlayerPrefab(ctx, "promoted host player prefab");
        if (!promotedLocal.success) return promotedLocal;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SinglePromotedServerTransferRoot.ServerAliveCount == 1
                      && SinglePromotedServerTransferChild.ServerAliveCount == ExpectedChildren
                      && HasServerState(RootOwnerPrePromotionValue, ChildOwnerPrePromotionValue),
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer promoted server hierarchy/state timeout: {DescribeState(ctx)}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _transferRestoredCount >= _expectedTransfers,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"single promotion transfer promoted server restored timeout: got {_transferRestoredCount}/{_expectedTransfers}; {DescribeState(ctx)}");
        }

        var promotedPlayers = await WaitForServerPlayerPrefabs(ctx, "promoted server player prefabs", _transferTimeoutSeconds);
        if (!promotedPlayers.success) return promotedPlayers;

        if (!SinglePromotedServerTransferRoot.DisconnectCalls.Contains(_ownerId))
            return ScenarioResult.Fail($"single promotion transfer promoted server did not mark owner disconnected: {DescribeState(ctx)}");

        if (!SinglePromotedServerTransferRoot.ReconnectCalls.Contains(_ownerId))
            return ScenarioResult.Fail($"single promotion transfer promoted server did not mark owner reconnected: {DescribeState(ctx)}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HasServerState(RootOwnerPostTransferValue, ChildOwnerPostTransferValue),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer promoted server did not receive post-transfer owner state: {DescribeState(ctx)}");
        }

        var continuity = _retainedHierarchy.Verify("promoted server continuity", asServer: true);
        if (!continuity.success) return continuity;
        var playerContinuity = _retainedPlayers.Verify("promoted player continuity", asServer: true);
        if (!playerContinuity.success) return playerContinuity;
        if (!_probeBeforeMigration || !_probeBeforeMigration.IsSpawned(true) ||
            MigrationReconcileProbe.Count(MigrationReconcileProbe.OldHostOnly, asServer: true) != 0 ||
            MigrationReconcileProbe.Count(MigrationReconcileProbe.CandidateOnly, asServer: true) != 1)
            return ScenarioResult.Fail("promoted host did not retain its candidate-only probe");

        var sceneCycle = await PostMigrationSceneCycle.RunServer(
            ctx, TargetSceneName, PostMigrationSceneName, _sceneTimeoutSeconds,
            () => _postMigrationLoadedCount, () => _postMigrationUnloadedCount, _expectedTransfers);
        if (!sceneCycle.success) return sceneCycle;

        continuity = _retainedHierarchy.Verify("promoted server after scene cycle", asServer: true);
        if (!continuity.success) return continuity;

        ScenarioSequencer.IssueSequenceComplete();
        await UniTask.NextFrame(ctx.cancellationToken);
        await UniTask.NextFrame(ctx.cancellationToken);

        return ScenarioResult.Ok($"promoted server restored {_transferRestoredCount}/{_expectedTransfers}");
    }

    private async UniTask<ScenarioResult> RunTransferClient(ScenarioContext ctx)
    {
        bool isOwner = IsLocal(_ownerId, ctx);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Disconnected,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer client did not detach from old server: {DescribeState(ctx)}");
        }

        await UniTask.WaitForSeconds(_promotionStartupDelaySeconds, cancellationToken: ctx.cancellationToken);

        bool transferred = await ctx.networkManager.TransferToNewServerAsync(
            preserveWorld: true, timeoutSeconds: _transferTimeoutSeconds, cancellationToken: ctx.cancellationToken);
        if (!transferred)
            return ScenarioResult.Fail("world-preserving transfer did not complete reconciliation");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.isClient && ctx.networkManager.isLocalPlayerReady,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer reconnect timeout: {DescribeState(ctx)}");
        }

        var restored = await WaitForClientScene(
            ctx,
            "post-single-promotion transfer restore",
            requireRetainedSpawn: true,
            rootSpawnsBefore: _rootSpawnsBeforeMigration,
            childSpawnsBefore: _childSpawnsBeforeMigration,
            requireOwnerState: isOwner,
            requirePreTransferState: isOwner);
        if (!restored.success) return restored;

        var continuity = _retainedHierarchy.Verify("transferred client continuity");
        if (!continuity.success) return continuity;
        var playerContinuity = _retainedPlayers.Verify("transferred player continuity");
        if (!playerContinuity.success) return playerContinuity;
        if ((_probeBeforeMigration && _probeBeforeMigration.isSpawned) ||
            MigrationReconcileProbe.Count(MigrationReconcileProbe.OldHostOnly) != 0 ||
            MigrationReconcileProbe.Count(MigrationReconcileProbe.CandidateOnly) != 1)
            return ScenarioResult.Fail("transfer did not remove the host-unknown probe and spawn the missing authoritative probe");

        var playerRestored = await WaitForLocalPlayerPrefab(ctx, "post-transfer player prefab");
        if (!playerRestored.success) return playerRestored;

        if (isOwner)
        {
            var seeded = await SeedOwnerState(
                ctx,
                RootOwnerPostTransferValue,
                ChildOwnerPostTransferValue,
                postTransfer: true,
                "post-transfer owner state");
            if (!seeded.success) return seeded;
        }

        var postOwnerState = await WaitForClientState(
            ctx,
            "post-single-promotion transfer owner state",
            RootOwnerPostTransferValue,
            ChildOwnerPostTransferValue);
        if (!postOwnerState.success) return postOwnerState;

        SignalTransferRestored();
        var sceneCycle = await PostMigrationSceneCycle.RunClient(
            ctx, TargetSceneName, PostMigrationSceneName, _sceneTimeoutSeconds,
            () => SignalPostMigrationLoaded(), () => SignalPostMigrationUnloaded());
        if (!sceneCycle.success) return sceneCycle;

        continuity = _retainedHierarchy.Verify("transferred client after scene cycle");
        if (!continuity.success) return continuity;
        if (SinglePromotedServerTransferRoot.ClientSpawnCount != _rootSpawnsBeforeMigration + 1 ||
            SinglePromotedServerTransferChild.ClientSpawnCount != _childSpawnsBeforeMigration + ExpectedChildren)
            return ScenarioResult.Fail("retained identities did not replay exactly one client spawn callback during migration");

        await UniTask.WaitForSeconds(_flushDelaySeconds, cancellationToken: ctx.cancellationToken);

        return ScenarioResult.Ok(isOwner ? "owner restored through single promoted server" : "peer restored through single promoted server");
    }

    private async UniTask<ScenarioResult> WaitForOriginalHostShutdown(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ctx.networkManager.clientState == ConnectionState.Disconnected
                      && ctx.networkManager.serverState == ConnectionState.Disconnected,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer original host did not shut down: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok("original host shut down");
    }

    private async UniTask<ScenarioResult> PrepareReconciliationProbes(ScenarioContext ctx, PlayerID promoted)
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        var oldHostOnly = UnityProxy.Instantiate(_probePrefab.gameObject, Vector3.zero, Quaternion.identity, scene)
            .GetComponent<MigrationReconcileProbe>();
        oldHostOnly.SetKind(MigrationReconcileProbe.OldHostOnly);
        var candidateOnly = UnityProxy.Instantiate(_probePrefab.gameObject, Vector3.one, Quaternion.identity, scene)
            .GetComponent<MigrationReconcileProbe>();
        candidateOnly.SetKind(MigrationReconcileProbe.CandidateOnly);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => oldHostOnly.IsSpawned(true) && candidateOnly.IsSpawned(true),
                _spawnTimeoutSeconds, ctx.cancellationToken);
            oldHostOnly.BlacklistPlayer(promoted);
            foreach (var player in ctx.networkManager.players)
                if (!player.isServer && player != promoted)
                    candidateOnly.BlacklistPlayer(player);
            oldHostOnly.EvaluateVisibility();
            candidateOnly.EvaluateVisibility();
            await UniTask.WaitForSeconds(_flushDelaySeconds, cancellationToken: ctx.cancellationToken);
            BroadcastProbeVisibilityReady();
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("migration reconciliation probes did not spawn on the original host");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForInitialProbeVisibility(ScenarioContext ctx, bool isPromoted)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _probeVisibilityReady &&
                      MigrationReconcileProbe.Count(MigrationReconcileProbe.OldHostOnly) == (isPromoted ? 0 : 1) &&
                      MigrationReconcileProbe.Count(MigrationReconcileProbe.CandidateOnly) == (isPromoted ? 1 : 0),
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("migration reconciliation probes did not establish different client views");
        }

        return ScenarioResult.Ok();
    }

    private SinglePromotedServerTransferRoot SpawnInScene(Scene targetScene)
    {
        var previous = SceneManager.GetActiveScene();
        bool changed = SceneManager.SetActiveScene(targetScene);
        try
        {
            return Instantiate(_prefab);
        }
        finally
        {
            if (changed && previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
        }
    }

    private ScenarioResult SpawnPlayerPrefabsInScene(ScenarioContext ctx, Scene targetScene)
    {
        if (!_playerPrefab)
            return ScenarioResult.Fail("initial server player prefabs: player prefab was not created");

        if (!targetScene.IsValid() || !targetScene.isLoaded)
            return ScenarioResult.Fail("initial server player prefabs: target scene is not loaded");

        var players = ctx.networkManager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;

            _playerPrefab.transform.GetPositionAndRotation(out var position, out var rotation);
            var instance = UnityProxy.Instantiate(_playerPrefab.gameObject, position, rotation, targetScene);
            if (!instance || !instance.TryGetComponent(out NetworkIdentity identity))
                return ScenarioResult.Fail($"initial server player prefabs: failed to instantiate player prefab for {player}");

            identity.GiveOwnership(player, propagateToChildren: true);
        }

        return ScenarioResult.Ok();
    }

    private static void SetServerAuthState(SinglePromotedServerTransferRoot root)
    {
        root.SetServerValue(RootServerStateValue);
        if (SinglePromotedServerTransferChild.ServerInstance)
            SinglePromotedServerTransferChild.ServerInstance.SetServerValue(ChildServerStateValue);
    }

    private async UniTask<ScenarioResult> LoadSingleScene(ScenarioContext ctx, int buildIndex)
    {
        var op = ctx.networkManager.sceneModule.LoadSceneAsync(TargetSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Single,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });

        if (op == null)
            return ScenarioResult.Fail($"single promotion transfer LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForPlan(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _planReceived,
                _promotionTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"single promotion transfer plan timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForClientScene(
        ScenarioContext ctx,
        string phase,
        bool requireRetainedSpawn,
        int rootSpawnsBefore,
        int childSpawnsBefore,
        bool requireOwnerState,
        bool requirePreTransferState = true)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SinglePromotedServerTransferRoot.ClientAliveCount == 1
                      && SinglePromotedServerTransferChild.ClientAliveCount == ExpectedChildren
                      && SinglePromotedServerTransferRoot.ClientSceneName == TargetSceneName
                      && SinglePromotedServerTransferRoot.LocalClientInstance != null
                      && (!requireRetainedSpawn ||
                          (SinglePromotedServerTransferRoot.ClientSpawnCount == rootSpawnsBefore + 1
                           && SinglePromotedServerTransferChild.ClientSpawnCount == childSpawnsBefore + ExpectedChildren))
                      && (!requirePreTransferState ||
                          HasClientState(RootOwnerPrePromotionValue, ChildOwnerPrePromotionValue))
                      && (!requireOwnerState ||
                          (SinglePromotedServerTransferRoot.LocalClientInstance.isOwner
                           && SinglePromotedServerTransferRoot.LocalClientInstance.isController
                           && SinglePromotedServerTransferRoot.LocalClientInstance.hasConnectedOwner)),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase} timeout: {DescribeState(ctx)}");
        }

        if (SinglePromotedServerTransferRoot.SawBadId || SinglePromotedServerTransferChild.SawBadId)
            return ScenarioResult.Fail($"{phase}: missing/default id observed: {DescribeState(ctx)}");

        if (!requireRetainedSpawn || !requireOwnerState)
            return ScenarioResult.Ok();

        var child = SinglePromotedServerTransferChild.LocalClientInstance;
        if (!child || !child.isOwner || !child.isController || !child.hasConnectedOwner || child.owner?.id.value != _ownerId)
            return ScenarioResult.Fail($"{phase}: retained child ownership was not restored");

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForServerPlayerPrefabs(
        ScenarioContext ctx,
        string phase,
        float timeoutSeconds)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ServerHasPlayerPrefabsForConnectedPlayers(ctx),
                timeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase}: player prefab ownership timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> WaitForLocalPlayerPrefab(ScenarioContext ctx, string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SinglePromotedPlayerPrefabRoot.HasCorrectLocalPlayer(ctx.networkManager),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase}: local player prefab ownership timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private static bool ServerHasPlayerPrefabsForConnectedPlayers(ScenarioContext ctx)
    {
        if (!ctx.networkManager.isServer)
            return false;

        if (SinglePromotedPlayerPrefabRoot.ServerHasDuplicateOwner() ||
            SinglePromotedPlayerPrefabChild.ServerHasDuplicateOwner())
            return false;

        var players = ctx.networkManager.players;
        int expectedPlayers = 0;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;

            expectedPlayers++;
            if (SinglePromotedPlayerPrefabRoot.ServerOwnerCount(player) != 1)
                return false;
            if (SinglePromotedPlayerPrefabChild.ServerOwnerCount(player) != 1)
                return false;
        }

        return expectedPlayers > 0;
    }

    private async UniTask<ScenarioResult> SeedOwnerState(
        ScenarioContext ctx,
        int rootOwnerValue,
        int childOwnerValue,
        bool postTransfer,
        string phase)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => TrySeedOwnerState(rootOwnerValue, childOwnerValue, postTransfer),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase}: owner could not seed SyncVar state: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private static bool TrySeedOwnerState(int rootOwnerValue, int childOwnerValue, bool postTransfer)
    {
        if (postTransfer && _postTransferOwnerStateSent)
            return true;
        if (!postTransfer && _prePromotionOwnerStateSent)
            return true;

        var root = SinglePromotedServerTransferRoot.LocalClientInstance;
        var child = SinglePromotedServerTransferChild.LocalClientInstance;
        if (!root || !child)
            return false;
        if (!root.isOwner || !child.isOwner)
            return false;

        root.SetOwnerValue(rootOwnerValue);
        child.SetOwnerValue(childOwnerValue);

        if (postTransfer)
            _postTransferOwnerStateSent = true;
        else
            _prePromotionOwnerStateSent = true;

        return true;
    }

    private async UniTask<ScenarioResult> WaitForClientState(
        ScenarioContext ctx,
        string phase,
        int rootOwnerValue,
        int childOwnerValue)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => HasClientState(rootOwnerValue, childOwnerValue),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{phase}: SyncVar state timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private static bool HasClientState(int rootOwnerValue, int childOwnerValue)
    {
        var root = SinglePromotedServerTransferRoot.LocalClientInstance;
        var child = SinglePromotedServerTransferChild.LocalClientInstance;
        return root && child
                    && root.HasState(RootServerStateValue, rootOwnerValue)
                    && child.HasState(ChildServerStateValue, childOwnerValue);
    }

    private static bool HasServerState(int rootOwnerValue, int childOwnerValue)
    {
        var root = SinglePromotedServerTransferRoot.ServerInstance;
        var child = SinglePromotedServerTransferChild.ServerInstance;
        return root && child
                    && root.HasState(RootServerStateValue, rootOwnerValue)
                    && child.HasState(ChildServerStateValue, childOwnerValue);
    }

    private static int GetBuildIndex(string scenePath) => SceneUtility.GetBuildIndexByScenePath(scenePath);

    private static bool IsNetworkSceneLoaded(ScenarioContext ctx, int buildIndex)
    {
        return buildIndex >= 0
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.IsSceneLoaded(buildIndex);
    }

    private static bool IsSceneLoaded(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private static PlayerID? PickPromotedClient(ScenarioContext ctx)
    {
        PlayerID? best = null;
        var players = ctx.networkManager.players;
        var hostLocal = GetHostLocalPlayer(ctx);
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;
            if (hostLocal.HasValue && hostLocal.Value == player)
                continue;
            if (!best.HasValue || player.id.value < best.Value.id.value)
                best = player;
        }

        return best;
    }

    private static PlayerID? PickTransferClient(ScenarioContext ctx, PlayerID promoted)
    {
        PlayerID? best = null;
        var players = ctx.networkManager.players;
        var hostLocal = GetHostLocalPlayer(ctx);
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer || player == promoted)
                continue;
            if (hostLocal.HasValue && hostLocal.Value == player)
                continue;
            if (!best.HasValue || player.id.value < best.Value.id.value)
                best = player;
        }

        return best;
    }

    private static int CountTransferClients(ScenarioContext ctx, PlayerID promoted)
    {
        int count = 0;
        var players = ctx.networkManager.players;
        var hostLocal = GetHostLocalPlayer(ctx);
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer || player == promoted)
                continue;
            if (hostLocal.HasValue && hostLocal.Value == player)
                continue;
            count++;
        }

        return count;
    }

    private static PlayerID? GetHostLocalPlayer(ScenarioContext ctx)
    {
        return ctx.role == NetworkRole.Host && ctx.networkManager.isLocalPlayerReady
            ? ctx.networkManager.localPlayer
            : (PlayerID?)null;
    }

    private static bool IsLocal(ulong playerId, ScenarioContext ctx)
    {
        return ctx.networkManager.isLocalPlayerReady
               && ctx.networkManager.localPlayer.id.value == playerId;
    }

    private static string DescribeState(ScenarioContext ctx)
    {
        return $"role={ctx.role}, promoted={_promotedId}, owner={_ownerId}, expectedTransfers={_expectedTransfers}, " +
               $"plan={_planReceived}, command={_promotionCommandReceived}, " +
               $"initial={_initialObservedCount}, restored={_transferRestoredCount}, " +
               $"clientState={ctx.networkManager.clientState}, serverState={ctx.networkManager.serverState}, " +
               $"client={ctx.networkManager.isClient}, server={ctx.networkManager.isServer}, ready={ctx.networkManager.isLocalPlayerReady}, " +
               $"playerCount={ctx.networkManager.playerCount}, " +
               $"playerPrefabServerRoots={SinglePromotedPlayerPrefabRoot.ServerAliveCount}, " +
               $"playerPrefabServerChildren={SinglePromotedPlayerPrefabChild.ServerAliveCount}, " +
               $"playerPrefabServerRootOwners=[{SinglePromotedPlayerPrefabRoot.ServerOwners}], " +
               $"playerPrefabServerChildOwners=[{SinglePromotedPlayerPrefabChild.ServerOwners}], " +
               $"playerPrefabClientRoots={SinglePromotedPlayerPrefabRoot.ClientAliveCount}, " +
               $"playerPrefabClientChildren={SinglePromotedPlayerPrefabChild.ClientAliveCount}, " +
               $"playerPrefabClientRootOwners=[{SinglePromotedPlayerPrefabRoot.ClientOwners}], " +
               $"playerPrefabClientChildOwners=[{SinglePromotedPlayerPrefabChild.ClientOwners}], " +
               $"playerPrefabLocal={SinglePromotedPlayerPrefabRoot.DescribeLocal(ctx.networkManager)}, " +
               $"clientRoots={SinglePromotedServerTransferRoot.ClientAliveCount}, " +
               $"clientChildren={SinglePromotedServerTransferChild.ClientAliveCount}/{ExpectedChildren}, " +
               $"clientRootSpawns={SinglePromotedServerTransferRoot.ClientSpawnCount}, " +
               $"clientChildSpawns={SinglePromotedServerTransferChild.ClientSpawnCount}, " +
               $"clientScene={SinglePromotedServerTransferRoot.ClientSceneName ?? "<none>"}, " +
               $"serverRoots={SinglePromotedServerTransferRoot.ServerAliveCount}, " +
               $"serverChildren={SinglePromotedServerTransferChild.ServerAliveCount}, " +
               $"serverRootId={SinglePromotedServerTransferRoot.ServerId}, " +
               $"serverChildId={SinglePromotedServerTransferChild.ServerId}, " +
               $"serverRootDirectChildren={SinglePromotedServerTransferRoot.ServerDirectChildCount}, " +
               $"serverChildDirectChildren={SinglePromotedServerTransferChild.ServerDirectChildCount}, " +
               $"serverRootObservers=[{SinglePromotedServerTransferRoot.ServerObservers}], " +
               $"serverChildObservers=[{SinglePromotedServerTransferChild.ServerObservers}], " +
               $"rootOwned={SinglePromotedServerTransferRoot.LocalClientInstance != null && SinglePromotedServerTransferRoot.LocalClientInstance.isOwner}, " +
               $"rootController={SinglePromotedServerTransferRoot.LocalClientInstance != null && SinglePromotedServerTransferRoot.LocalClientInstance.isController}, " +
               $"clientRootState={SinglePromotedServerTransferRoot.LocalClientInstance?.DescribeState() ?? "<none>"}, " +
               $"clientChildState={SinglePromotedServerTransferChild.LocalClientInstance?.DescribeState() ?? "<none>"}, " +
               $"serverRootState={SinglePromotedServerTransferRoot.ServerInstance?.DescribeState() ?? "<none>"}, " +
               $"serverChildState={SinglePromotedServerTransferChild.ServerInstance?.DescribeState() ?? "<none>"}, " +
               $"disconnectCalls=[{string.Join(",", SinglePromotedServerTransferRoot.DisconnectCalls)}], " +
               $"reconnectCalls=[{string.Join(",", SinglePromotedServerTransferRoot.ReconnectCalls)}], " +
               $"rootBadId={SinglePromotedServerTransferRoot.SawBadId}, childBadId={SinglePromotedServerTransferChild.SawBadId}, " +
               $"sceneLoaded={IsSceneLoaded(TargetSceneName)}, " +
               $"networkSceneLoaded={IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath))}";
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastPlan(ulong promotedId, ulong ownerId, int expectedTransfers)
    {
        _promotedId = promotedId;
        _ownerId = ownerId;
        _expectedTransfers = expectedTransfers;
        _planReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastProbeVisibilityReady()
    {
        _probeVisibilityReady = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPromotionCommand()
    {
        _promotionCommandReceived = true;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalInitialObserved(RPCInfo info = default)
    {
        _initialObservedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalPostMigrationLoaded(RPCInfo info = default)
    {
        _postMigrationLoadedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalPostMigrationUnloaded(RPCInfo info = default)
    {
        _postMigrationUnloadedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalTransferRestored(RPCInfo info = default)
    {
        _transferRestoredCount++;
    }
}
