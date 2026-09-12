using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// Owner-authoritative SyncBigData contract: an owning client calls SetData, the server downloads
/// the payload from that owner and proxies it out to every other observer. Every receiver must end
/// up with the identical bytes and must be driven there by onSyncStatusChanged.
/// The sender raises exactly one event of its own, isDone right after SetData, because its copy is
/// ready immediately; that is local readiness, not upload progress towards the other peers.
/// </summary>
public class SyncBigDataOwnerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _transferTimeoutSeconds = 60f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncBigDataOwnerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncBigDataOwnerAuthScenario));
        _prefab = go.AddComponent<SyncBigDataOwnerAuthIdentity>();
        go.SetActive(false);
        SyncBigDataOwnerAuthIdentity.ResetAll();
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
            () => SyncBigDataOwnerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncBigDataOwnerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncBigDataOwnerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncBigDataOwnerAuthIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncBigDataOwnerAuthIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the big data");

        inst.GiveOwnership(owner.Value);
        inst.BroadcastOwner(owner.Value.id.value);

        // the server is a receiver in owner-auth mode, it downloads from the owner before proxying
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.Received(),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"server never downloaded the owner payload; progress={inst.progress:0.###}, " +
                $"statusEvents={SyncBigDataOwnerAuthIdentity.StatusEvents}");
        }

        if (ctx.role == NetworkRole.Server)
        {
            if (SyncBigDataOwnerAuthIdentity.StatusEvents == 0)
                failures.Add("server received the payload without a single onSyncStatusChanged event");

            if (!SyncBigDataOwnerAuthIdentity.SawDoneEvent)
                failures.Add("server never saw an onSyncStatusChanged event with isDone=true");
        }

        if (SyncBigDataOwnerAuthIdentity.PercentWentBackwards)
            failures.Add("server progress went backwards during a single transfer");

        if (inst.IsController(true))
            failures.Add("server reports IsController(ownerAuth:true)=true for client-owned big data");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncBigDataOwnerAuthIdentity.ReceivedCount >= ctx.expectedConnections,
                _transferTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"proxy timeout: received={SyncBigDataOwnerAuthIdentity.ReceivedCount}/{ctx.expectedConnections}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncBigDataOwnerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"done timeout: {SyncBigDataOwnerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}, statusEvents={SyncBigDataOwnerAuthIdentity.StatusEvents}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncBigDataOwnerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncBigDataOwnerAuthIdentity.OwnerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        bool designated = ctx.networkManager.isLocalPlayerReady
                          && ctx.networkManager.localPlayer.id.value == SyncBigDataOwnerAuthIdentity.OwnerId;

        if (designated)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.isOwner,
                    _readyTimeoutSeconds,
                    ctx.cancellationToken);
                inst.Send();
            }
            catch (TimeoutException)
            {
                failures.Add("designated owner never became isOwner");
            }

            if (!inst.Received())
                failures.Add("owner does not hold its own payload right after SetData");

            if (SyncBigDataOwnerAuthIdentity.StatusEvents == 0)
                failures.Add("owner got no onSyncStatusChanged event for its own SetData");

            if (!SyncBigDataOwnerAuthIdentity.SawDoneEvent)
                failures.Add("owner's own status event did not report isDone");

            if (!Mathf.Approximately(inst.progress, 1f))
                failures.Add($"owner progress is {inst.progress:0.###} right after SetData, expected 1");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.Received(),
                _transferTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalReceived();
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"payload never received / mismatched; progress={inst.progress:0.###}, " +
                $"statusEvents={SyncBigDataOwnerAuthIdentity.StatusEvents}");
        }

        // pure non-owner clients are driven purely by the proxied transfer
        if (ctx.role == NetworkRole.Client && !designated)
        {
            if (SyncBigDataOwnerAuthIdentity.StatusEvents == 0)
                failures.Add("observer received the payload without a single onSyncStatusChanged event");

            if (!SyncBigDataOwnerAuthIdentity.SawDoneEvent)
                failures.Add("observer never saw an onSyncStatusChanged event with isDone=true");

            if (SyncBigDataOwnerAuthIdentity.PercentWentBackwards)
                failures.Add("observer progress went backwards during a single transfer");

            if (inst.IsController(true))
                failures.Add("non-owner client reports IsController(ownerAuth:true)=true");
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncBigDataOwnerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncBigDataOwnerAuthIdentity.LocalInstance != null)
            SyncBigDataOwnerAuthIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok(designated
                ? $"owner, statusEvents={SyncBigDataOwnerAuthIdentity.StatusEvents}"
                : $"observer, statusEvents={SyncBigDataOwnerAuthIdentity.StatusEvents}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
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
