using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;

/// <summary>
/// Reconnect regression for owner-authoritative SyncVars. A delayed catch-up snapshot must not
/// rewind the restored owner's packet id and cause its next value update to be dropped.
/// </summary>
public class SyncVarOwnerReconnectScenario : Scenario
{
    private const int PreReconnectFirstValue = 1000;
    private const int PreReconnectBurstCount = 96;
    private const int PostReconnectFirstValue = 2000;
    private const int PostReconnectBurstCount = 1;

    [SerializeField] private NetworkRules _rules;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _preSyncTimeoutSeconds = 30f;
    [SerializeField] private float _disconnectTimeoutSeconds = 30f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _postSyncTimeoutSeconds = 10f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 0.5f;
    [SerializeField] private float _stayDisconnectedSeconds = 1f;
    [Tooltip("Replay a captured snapshot after an unchanged owner resend to exercise packet-id rollback deterministically. Disable to exercise natural reconnect timing only.")]
    [SerializeField] private bool _replayDelayedSnapshot = true;

    private SyncVarOwnerReconnectIdentity _prefab;

    private static int PreReconnectLastValue => PreReconnectFirstValue + PreReconnectBurstCount - 1;
    private static int PostReconnectLastValue => PostReconnectFirstValue + PostReconnectBurstCount - 1;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncVarOwnerReconnectScenario));
        _prefab = go.AddComponent<SyncVarOwnerReconnectIdentity>();
        go.SetActive(false);

        if (_rules)
            _prefab.SetNetworkRules(_rules);
        else
            Debug.LogError("[SyncVarOwnerReconnectScenario] _rules is not assigned; the identity must survive owner disconnect.");

        SyncVarOwnerReconnectIdentity.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        await UniTaskUtils.WaitWithTimeout(
            () => SyncVarOwnerReconnectIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncVarOwnerReconnectIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarOwnerReconnectIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerReconnectIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"server-ready timeout: got {SyncVarOwnerReconnectIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the SyncVar");

        inst.GiveOwnership(owner.Value);
        inst.BroadcastOwner(owner.Value.id.value);
        await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.currentValue >= PreReconnectLastValue,
                _preSyncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"pre-reconnect owner burst did not reach server: value={inst.currentValue}, expected>={PreReconnectLastValue}");
        }

        inst.BroadcastDisconnectCommand();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerReconnectIdentity.VictimReturnedCount >= 1,
                _reconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"owner {owner.Value.id.value} did not reconnect and signal return within {_reconnectTimeoutSeconds}s");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerReconnectIdentity.OwnerReconnectCheckpointCount >= 1,
                _postSyncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                "owner did not report reconnect checkpoint; " +
                $"server={inst.DescribeLocalSyncVar()}");
        }

        ulong? acknowledgedResendPacketId = null;
        if (_replayDelayedSnapshot)
        {
            try
            {
                acknowledgedResendPacketId = await ReplayDelayedSnapshot(ctx, inst, owner.Value);
            }
            catch (Exception e)
            {
                failures.Add($"delayed-snapshot setup failed: {e.Message}");
            }
        }

        // Queued after the replay on the same reliable ordered RPC lane. The owner records
        // its counter and performs the actual application write only after applying it.
        inst.BroadcastPostReconnectBurst(PostReconnectFirstValue, PostReconnectBurstCount);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerReconnectIdentity.BurstReportCount >= 1,
                _postSyncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                "owner did not report receiving/sending the post-reconnect burst; " +
                $"server={inst.DescribeLocalSyncVar()}; " +
                $"burst=({SyncVarOwnerReconnectIdentity.DescribeBurstReport()})");
        }

        if (acknowledgedResendPacketId.HasValue && SyncVarOwnerReconnectIdentity.BurstReportCount >= 1 &&
            SyncVarOwnerReconnectIdentity.BurstReportPacketIdBefore < acknowledgedResendPacketId.Value)
        {
            failures.Add(
                $"delayed snapshot rewound owner packet id: acknowledged={acknowledgedResendPacketId.Value}, " +
                $"beforeWrite={SyncVarOwnerReconnectIdentity.BurstReportPacketIdBefore}");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.currentValue >= PostReconnectLastValue,
                _postSyncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"post-reconnect owner burst did not reach server: server={inst.DescribeLocalSyncVar()}, " +
                $"expectedValue>={PostReconnectLastValue}; " +
                $"reconnect=({SyncVarOwnerReconnectIdentity.DescribeReconnectCheckpoint()}); " +
                $"burst=({SyncVarOwnerReconnectIdentity.DescribeBurstReport()}). " +
                "If the owner packetIdBefore/After values are <= the server packetId, this is a stale owner packet-id drop.");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerReconnectIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server-done timeout: got {SyncVarOwnerReconnectIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}, value={inst.currentValue}, delayedSnapshot={_replayDelayedSnapshot}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ulong> ReplayDelayedSnapshot(
        ScenarioContext ctx, SyncVarOwnerReconnectIdentity inst, PlayerID owner)
    {
        ulong snapshotPacketId = inst.debugPacketId;
        int snapshotValue = inst.currentValue;

        // Send a real baseline snapshot, retaining its arguments for a delayed duplicate.
        // No private SyncVar state is written and no receiver or transport is replaced.
        inst.SendCapturedSnapshot(owner, snapshotPacketId, snapshotValue);
        inst.RequestUnchangedOwnerResend(owner, snapshotPacketId);

        await UniTaskUtils.WaitWithTimeout(
            () => SyncVarOwnerReconnectIdentity.UnchangedResendReportCount >= 1,
            _readyTimeoutSeconds,
            ctx.cancellationToken);

        if (SyncVarOwnerReconnectIdentity.UnchangedResendError != null)
            throw new InvalidOperationException(SyncVarOwnerReconnectIdentity.UnchangedResendError);

        ulong resendPacketId = SyncVarOwnerReconnectIdentity.UnchangedResendPacketId;
        await UniTaskUtils.WaitWithTimeout(
            () => inst.debugPacketId >= resendPacketId,
            _postSyncTimeoutSeconds,
            ctx.cancellationToken);

        if (resendPacketId <= snapshotPacketId || inst.currentValue != snapshotValue)
            throw new InvalidOperationException(
                $"unchanged resend did not establish the baseline: snapshot={snapshotPacketId}/{snapshotValue}, " +
                $"resend={resendPacketId}, server={inst.DescribeLocalSyncVar()}");

        Debug.Log(
            $"[SyncVarOwnerReconnectScenario] Replaying snapshot packetId={snapshotPacketId}, value={snapshotValue} " +
            $"after owner resend packetId={resendPacketId} reached the server.");
        inst.SendCapturedSnapshot(owner, snapshotPacketId, snapshotValue);
        return resendPacketId;
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerReconnectIdentity.OwnerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        bool isOwnerPeer = ctx.networkManager.isLocalPlayerReady
                           && ctx.networkManager.localPlayer.id.value == SyncVarOwnerReconnectIdentity.OwnerId;

        if (isOwnerPeer)
        {
            var inst = SyncVarOwnerReconnectIdentity.LocalInstance;

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst != null && inst.isOwner,
                    _readyTimeoutSeconds,
                    ctx.cancellationToken);
                inst.RunOwnerBurst(PreReconnectFirstValue, PreReconnectBurstCount);
            }
            catch (TimeoutException)
            {
                failures.Add("owner peer never became isOwner before the pre-reconnect burst");
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => SyncVarOwnerReconnectIdentity.DisconnectCommandReceived,
                    _disconnectTimeoutSeconds,
                    ctx.cancellationToken);
                await PerformDisconnectReconnect(ctx);
            }
            catch (TimeoutException)
            {
                var currentInst = SyncVarOwnerReconnectIdentity.LocalInstance;
                failures.Add(
                    "owner peer did not complete disconnect/reconnect phase " +
                    $"(restored={SyncVarOwnerReconnectIdentity.RestoredAfterReconnect}, " +
                    $"value={(currentInst ? currentInst.currentValue.ToString() : "<missing>")}, " +
                    $"expectedRestored={PreReconnectLastValue})");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerReconnectIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncVarOwnerReconnectIdentity.LocalInstance != null)
            SyncVarOwnerReconnectIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok(isOwnerPeer ? "owner" : "observer")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask PerformDisconnectReconnect(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;

        SyncVarOwnerReconnectIdentity.RestoredAfterReconnect = false;

        manager.StopClient();

        await UniTaskUtils.WaitWithTimeout(
            () => manager.clientState == ConnectionState.Disconnected,
            _disconnectTimeoutSeconds,
            ctx.cancellationToken);

        await UniTask.WaitForSeconds(_stayDisconnectedSeconds, cancellationToken: ctx.cancellationToken);

        manager.StartClient();

        await UniTaskUtils.WaitWithTimeout(
            () => manager.isClient && manager.isLocalPlayerReady,
            _reconnectTimeoutSeconds,
            ctx.cancellationToken);

        await UniTaskUtils.WaitWithTimeout(
            () => SyncVarOwnerReconnectIdentity.LocalInstance != null
                  && SyncVarOwnerReconnectIdentity.LocalInstance.currentValue == PreReconnectLastValue,
            _reconnectTimeoutSeconds,
            ctx.cancellationToken);

        await UniTask.NextFrame(ctx.cancellationToken);
        await UniTask.NextFrame(ctx.cancellationToken);

        SyncVarOwnerReconnectIdentity.RestoredAfterReconnect = true;

        SyncVarOwnerReconnectIdentity.LocalInstance.ReportOwnerReconnectCheckpoint();
        SyncVarOwnerReconnectIdentity.LocalInstance.SignalVictimReturned();
    }

    private static PlayerID? PickOwner(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        PlayerID? best = null;
        var players = manager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var p = players[i];
            if (p.isServer) continue;
            if (hostLocal.HasValue && hostLocal.Value == p) continue;
            if (!best.HasValue || p.id.value < best.Value.id.value)
                best = p;
        }

        return best;
    }
}
