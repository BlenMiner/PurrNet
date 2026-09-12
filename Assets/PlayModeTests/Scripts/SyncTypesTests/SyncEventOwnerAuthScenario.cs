using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// Owner-authoritative SyncEvent contract: the owning client invokes the event, the server relays it
/// to the other observers, and every client's listener fires with the original payload.
/// </summary>
public class SyncEventOwnerAuthScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _stateTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;

    private SyncEventOwnerAuthIdentity _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncEventOwnerAuthScenario));
        _prefab = go.AddComponent<SyncEventOwnerAuthIdentity>();
        go.SetActive(false);
        SyncEventOwnerAuthIdentity.ResetAll();
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
            () => SyncEventOwnerAuthIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncEventOwnerAuthIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncEventOwnerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncEventOwnerAuthIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncEventOwnerAuthIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        var owner = PickOwner(ctx);
        if (!owner.HasValue)
            return ScenarioResult.Fail("no eligible non-server / non-host client to own the event");

        inst.GiveOwnership(owner.Value);
        inst.BroadcastOwner(owner.Value.id.value);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncEventOwnerAuthIdentity.ReceivedCount >= ctx.expectedConnections,
                _stateTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"event-received timeout: received={SyncEventOwnerAuthIdentity.ReceivedCount}/{ctx.expectedConnections}");
        }

        inst.BroadcastPhaseDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncEventOwnerAuthIdentity.ServerDoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"done timeout: {SyncEventOwnerAuthIdentity.ServerDoneCount}/{ctx.expectedConnections}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"owner={owner.Value.id.value}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncEventOwnerAuthIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncEventOwnerAuthIdentity.OwnerIdReceived,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("client did not receive BroadcastOwner");
        }

        bool designated = ctx.networkManager.isLocalPlayerReady
                          && ctx.networkManager.localPlayer.id.value == SyncEventOwnerAuthIdentity.OwnerId;

        if (designated)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => inst.isOwner,
                    _stateTimeoutSeconds,
                    ctx.cancellationToken);
                inst.Fire();
            }
            catch (TimeoutException)
            {
                failures.Add("designated owner never became isOwner");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => inst.Received(),
                _stateTimeoutSeconds,
                ctx.cancellationToken);
            inst.SignalReceived();
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"{(designated ? "owner" : "observer")} listener never fired with sentinel; got {inst.Describe()}");
        }

        if (inst.IsController(true) != inst.isOwner)
            failures.Add($"IsController(true)={inst.IsController(true)} but isOwner={inst.isOwner}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncEventOwnerAuthIdentity.PhaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("client did not receive BroadcastPhaseDone");
        }

        if (SyncEventOwnerAuthIdentity.LocalInstance != null)
            SyncEventOwnerAuthIdentity.LocalInstance.SignalDone();

        return failures.Count == 0
            ? ScenarioResult.Ok(designated ? "owner" : "observer")
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
