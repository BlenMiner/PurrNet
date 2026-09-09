using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

// Run before scenarios that replace Bootstrap with a Single scene load. A successful
// legacy transfer must acknowledge its bootstrap scene again, or later spawns are lost.
public sealed class BootstrapSceneTransferReadinessScenario : Scenario
{
    [SerializeField] private float _transferTimeoutSeconds = 45f;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _handshakeTimeoutSeconds = 30f;

    private static readonly HashSet<PlayerID> PreparedPlayers = new();
    private static readonly HashSet<PlayerID> ObservedPlayers = new();
    private static readonly HashSet<PlayerID> CleanedPlayers = new();
    private static ulong _victimId;
    private static SceneID _bootstrapId;
    private static bool _begin;
    private static bool _transferRequested;
    private static bool _victimTransferred;
    private static bool _spawnRequested;
    private static bool _cleanupRequested;
    private static bool _released;
    private static string _serverFailure;

    private BootstrapSceneTransferReadinessProbe _prefab;
    private bool _victimDisconnected;
    private int _freshBootstrapAcknowledgements;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        PreparedPlayers.Clear();
        ObservedPlayers.Clear();
        CleanedPlayers.Clear();
        _victimId = 0;
        _bootstrapId = default;
        _begin = _transferRequested = _victimTransferred = _spawnRequested = _cleanupRequested = _released = false;
        _serverFailure = null;
        _victimDisconnected = false;
        _freshBootstrapAcknowledgements = 0;
        BootstrapSceneTransferReadinessProbe.ResetAll();

        var root = new GameObject(nameof(BootstrapSceneTransferReadinessProbe));
        _prefab = root.AddComponent<BootstrapSceneTransferReadinessProbe>();
        _prefab.Configure(false);
        var child = new GameObject("BootstrapTransferChild");
        child.transform.SetParent(root.transform);
        child.AddComponent<BootstrapSceneTransferReadinessProbe>().Configure(true);
        root.SetActive(false);
        manager.prefabProvider.AddRuntimePrefab(root.name, root);
    }

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx) =>
        RunSplit(ctx, RunAsClient, RunAsServer);

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        BootstrapSceneTransferReadinessProbe spawned = null;
        string failure = null;
        var manager = ctx.networkManager;
        manager.onPlayerLeft += OnPlayerLeft;
        manager.onPlayerLoadedScene += OnPlayerLoadedScene;
        try
        {
            var bootstrap = SceneManager.GetSceneByName("Bootstrap");
            if (!bootstrap.IsValid() || !bootstrap.isLoaded ||
                !manager.sceneModule.TryGetSceneID(bootstrap, out var bootstrapId))
                throw new InvalidOperationException("Bootstrap must be loaded and registered before this scenario.");

            var victim = PickExternalClient(ctx);
            if (!victim.HasValue)
                throw new InvalidOperationException("Bootstrap transfer regression requires an external client.");

            var scenePlayers = manager.GetModule<ScenePlayersModule>(true);
            await UniTaskUtils.WaitWithTimeout(
                () => scenePlayers.IsPlayerLoadedInScene(victim.Value, bootstrapId),
                _handshakeTimeoutSeconds, ctx.cancellationToken);

            BroadcastBegin(victim.Value.id.value, bootstrapId);
            await UniTaskUtils.WaitWithTimeout(() => PreparedPlayers.Count == ctx.expectedConnections,
                _handshakeTimeoutSeconds, ctx.cancellationToken);

            BroadcastTransfer();
            await UniTaskUtils.WaitWithTimeout(
                () => _victimDisconnected && _freshBootstrapAcknowledgements > 0 && _victimTransferred,
                _transferTimeoutSeconds, ctx.cancellationToken);

            if (_freshBootstrapAcknowledgements != 1 ||
                !scenePlayers.IsPlayerLoadedInScene(victim.Value, bootstrapId))
                throw new InvalidOperationException("Expected one fresh Bootstrap acknowledgement after the victim disconnected.");

            // Instantiate only after the server received the replacement connection's
            // scene-ready acknowledgement. Existing spawn snapshots cannot satisfy this test.
            BroadcastSpawn();
            // Place the inactive clone before explicitly spawning it. Selecting an
            // already active scene need not report a change, and auto-spawning from
            // Instantiate would bind the hierarchy before a subsequent scene move.
            spawned = UnityProxy.InstantiateDirectly(_prefab);
            if (spawned.gameObject.scene != bootstrap)
                SceneManager.MoveGameObjectToScene(spawned.gameObject, bootstrap);
            if (spawned.gameObject.scene != bootstrap || spawned.isSpawned)
                throw new InvalidOperationException("The new hierarchy must be unspawned in Bootstrap before publication.");
            spawned.Spawn(_prefab.gameObject, manager);

            await UniTaskUtils.WaitWithTimeout(
                () => BootstrapSceneTransferReadinessProbe.HasCompleteHierarchy(true, bootstrapId) &&
                      ObservedPlayers.Count == ctx.expectedConnections,
                _spawnTimeoutSeconds, ctx.cancellationToken);

            if (!ObservedPlayers.Contains(victim.Value) || BootstrapSceneTransferReadinessProbe.ServerSpawnCount != 2)
                throw new InvalidOperationException("The transferred victim did not confirm the newly spawned root and child.");

            spawned.Despawn();
            BroadcastCleanup();
            await UniTaskUtils.WaitWithTimeout(
                () => BootstrapSceneTransferReadinessProbe.AliveCount(true) == 0 &&
                      CleanedPlayers.Count == ctx.expectedConnections,
                _handshakeTimeoutSeconds, ctx.cancellationToken);
        }
        catch (Exception exception)
        {
            failure = $"bootstrap transfer readiness: {exception.Message}; left={_victimDisconnected}, " +
                $"freshAck={_freshBootstrapAcknowledgements}, transferred={_victimTransferred}, " +
                $"prepared={PreparedPlayers.Count}, observed={ObservedPlayers.Count}, cleaned={CleanedPlayers.Count}";
        }
        finally
        {
            if (spawned)
            {
                if (spawned.isSpawned)
                    spawned.Despawn();
                else
                    UnityProxy.DestroyDirectly(spawned.gameObject);
            }
            manager.onPlayerLeft -= OnPlayerLeft;
            manager.onPlayerLoadedScene -= OnPlayerLoadedScene;
            BroadcastRelease(failure);
        }

        return failure == null
            ? ScenarioResult.Ok($"victim={_victimId}; one fresh Bootstrap ACK; new root/child received by {ObservedPlayers.Count} players")
            : ScenarioResult.Fail(failure);
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        try
        {
            await WaitForPhase(() => _begin, ctx);
            bool victim = ctx.networkManager.localPlayer.id.value == _victimId;
            if (BootstrapSceneTransferReadinessProbe.ClientSpawnCount != 0)
                return ScenarioResult.Fail("Bootstrap transfer probe spawned before the fresh-ACK phase.");

            SignalPrepared();
            await WaitForPhase(() => _transferRequested, ctx);
            if (victim)
            {
                bool transferred = await ctx.networkManager.TransferToNewServerAsync(
                    preserveWorld: false, timeoutSeconds: _transferTimeoutSeconds,
                    cancellationToken: ctx.cancellationToken);
                if (!transferred)
                    return ScenarioResult.Fail("Victim's ordinary TransferToNewServer did not complete.");
                if (ctx.networkManager.localPlayer.id.value != _victimId ||
                    BootstrapSceneTransferReadinessProbe.ClientSpawnCount != 0)
                    return ScenarioResult.Fail("Victim changed player identity or received probes before transfer completed.");
                SignalTransferred();
            }

            await WaitForPhase(() => _spawnRequested, ctx);
            await UniTaskUtils.WaitWithTimeout(
                () => _released || BootstrapSceneTransferReadinessProbe.HasCompleteHierarchy(false, _bootstrapId),
                _spawnTimeoutSeconds, ctx.cancellationToken);
            ThrowIfReleasedWithFailure();
            if (BootstrapSceneTransferReadinessProbe.ClientSpawnCount != 2)
                return ScenarioResult.Fail("Expected exactly one new root and one new child after Bootstrap readiness.");
            SignalObserved();

            await WaitForPhase(() => _cleanupRequested, ctx);
            await UniTaskUtils.WaitWithTimeout(() => BootstrapSceneTransferReadinessProbe.AliveCount(false) == 0,
                _handshakeTimeoutSeconds, ctx.cancellationToken);
            if (BootstrapSceneTransferReadinessProbe.ClientDespawnCount != 2)
                return ScenarioResult.Fail("Bootstrap transfer probe cleanup did not despawn both identities exactly once.");
            SignalCleaned();
            await UniTaskUtils.WaitWithTimeout(() => _released, _handshakeTimeoutSeconds, ctx.cancellationToken);
            ThrowIfReleasedWithFailure();
            return ScenarioResult.Ok(victim
                ? "legacy transfer received a newly spawned Bootstrap root/child after its fresh scene-ready ACK"
                : "observer received and cleaned the new Bootstrap root/child");
        }
        catch (Exception exception)
        {
            return ScenarioResult.Fail($"bootstrap transfer client: {exception.Message}; " +
                $"spawns={BootstrapSceneTransferReadinessProbe.ClientSpawnCount}, " +
                $"alive={BootstrapSceneTransferReadinessProbe.AliveCount(false)}");
        }
    }

    private async UniTask WaitForPhase(Func<bool> phase, ScenarioContext ctx)
    {
        await UniTaskUtils.WaitWithTimeout(() => phase() || _released,
            _transferTimeoutSeconds + _handshakeTimeoutSeconds, ctx.cancellationToken);
        ThrowIfReleasedWithFailure();
    }

    private static void ThrowIfReleasedWithFailure()
    {
        if (_released && !string.IsNullOrEmpty(_serverFailure))
            throw new InvalidOperationException(_serverFailure);
    }

    private void OnPlayerLeft(PlayerID player, bool asServer)
    {
        if (asServer && _transferRequested && player.id.value == _victimId)
            _victimDisconnected = true;
    }

    private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
    {
        if (asServer && _victimDisconnected && player.id.value == _victimId && scene.Equals(_bootstrapId))
            _freshBootstrapAcknowledgements++;
    }

    private static PlayerID? PickExternalClient(ScenarioContext ctx)
    {
        PlayerID? selected = null;
        foreach (var player in ctx.networkManager.players)
        {
            if (player.isServer || ctx.role == NetworkRole.Host && player == ctx.networkManager.localPlayer)
                continue;
            if (!selected.HasValue || player.id.value < selected.Value.id.value)
                selected = player;
        }
        return selected;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastBegin(ulong victimId, SceneID bootstrapId)
    {
        _victimId = victimId;
        _bootstrapId = bootstrapId;
        _begin = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastTransfer() => _transferRequested = true;

    [ObserversRpc(runLocally: true)]
    private static void BroadcastSpawn() => _spawnRequested = true;

    [ObserversRpc(runLocally: true)]
    private static void BroadcastCleanup() => _cleanupRequested = true;

    [ObserversRpc(runLocally: true)]
    private static void BroadcastRelease(string failure)
    {
        _serverFailure = failure;
        _released = true;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalPrepared(RPCInfo info = default) => PreparedPlayers.Add(info.sender);

    [ServerRpc(requireOwnership: false)]
    private static void SignalTransferred(RPCInfo info = default)
    {
        if (info.sender.id.value == _victimId)
            _victimTransferred = true;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalObserved(RPCInfo info = default) => ObservedPlayers.Add(info.sender);

    [ServerRpc(requireOwnership: false)]
    private static void SignalCleaned(RPCInfo info = default) => CleanedPlayers.Add(info.sender);
}
