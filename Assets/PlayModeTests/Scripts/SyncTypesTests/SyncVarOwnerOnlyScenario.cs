using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

/// <summary>
/// [OwnerOnly] SyncVar contract, phase by phase:
/// 1. no owner: secret goes nowhere, shared var reaches everyone
/// 2. give ownership to A: A catches up to the pre-ownership secret
/// 3. steady state: secret changes reach A only
/// 4. transfer A -> B: B catches up
/// 5. new changes reach B only; A keeps its stale value
/// 6. owner-auth [OwnerOnly] var: B's write reaches the server, not A
/// 7. remove ownership: secret changes go nowhere again
/// The host's client half shares the server instance, so it always sees server-side truth.
/// </summary>
public class SyncVarOwnerOnlyScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _readyTimeoutSeconds = 30f;
    [SerializeField] private float _phaseTimeoutSeconds = 30f;
    [SerializeField] private float _propagationDelaySeconds = 1f;

    private SyncVarOwnerOnlyIdentity _prefab;

    private const int Secret1 = 11;
    private const int Secret2 = 22;
    private const int Secret3 = 33;
    private const int Secret4 = 44;
    private const int OwnerWriteValue = 77;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(SyncVarOwnerOnlyScenario));
        _prefab = go.AddComponent<SyncVarOwnerOnlyIdentity>();
        go.SetActive(false);
        SyncVarOwnerOnlyIdentity.ResetAll();
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
            // Spawn ownerless even on host so phase 1 exercises the no-owner path.
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        await UniTaskUtils.WaitWithTimeout(
            () => SyncVarOwnerOnlyIdentity.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        if (ctx.isClient)
            SyncVarOwnerOnlyIdentity.LocalInstance.SignalReady();

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var inst = SyncVarOwnerOnlyIdentity.LocalInstance;

        for (int phase = 1; phase <= SyncVarOwnerOnlyIdentity.PhaseCount; phase++)
        {
            try
            {
                int expected = phase;
                await UniTaskUtils.WaitWithTimeout(
                    () =>
                    {
                        inst.TryOwnerWrite();
                        return inst.phaseToSample >= expected;
                    },
                    _phaseTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"client never saw phase {phase} (at {inst.phaseToSample})");
            }

            inst.ReportState(phase, inst.secret, inst.ownerSecret, inst.shared);
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = SyncVarOwnerOnlyIdentity.LocalInstance;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SyncVarOwnerOnlyIdentity.ServerReadyCount >= ctx.expectedConnections,
                _readyTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"ready timeout: {SyncVarOwnerOnlyIdentity.ServerReadyCount}/{ctx.expectedConnections}");
        }

        bool isHost = ctx.role == NetworkRole.Host;
        var localPlayer = ctx.networkManager.localPlayer;
        ulong hostId = localPlayer.id.value;

        // The host's local client shares the server instance, so it sees everything and
        // can't act as a scoped-visibility victim; owners must be pure remote clients.
        var clients = new List<PlayerID>();
        var players = ctx.networkManager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];

            if (player.isServer || (isHost && player == localPlayer))
                continue;

            clients.Add(player);
        }
        clients.Sort((a, b) => a.id.value.CompareTo(b.id.value));

        if (clients.Count == 0)
            return ScenarioResult.Fail("no pure clients to assign ownership to");

        var playerA = clients[0];
        var playerB = clients.Count >= 2 ? clients[1] : clients[0];
        bool twoClients = clients.Count >= 2;

        // Per-phase expectations for pure clients A and B. The host client shares the server
        // instance, so its expected values are simply the server-side values at sample time.
        int[] sharedByPhase = { 0, 101, 101, 103, 103, 105, 105, 107 };
        int[] serverSecretByPhase = { 0, Secret1, Secret1, Secret2, Secret2, Secret3, Secret3, Secret4 };
        int[] secretA = twoClients
            ? new[] { 0, 0, Secret1, Secret2, Secret2, Secret2, Secret2, Secret2 }
            : new[] { 0, 0, Secret1, Secret2, Secret2, Secret3, Secret3, Secret3 };
        int[] secretB = twoClients
            ? new[] { 0, 0, 0, 0, Secret2, Secret3, Secret3, Secret3 }
            : secretA;
        int[] ownerSecretB = { 0, 0, 0, 0, 0, 0, OwnerWriteValue, OwnerWriteValue };

        for (int phase = 1; phase <= SyncVarOwnerOnlyIdentity.PhaseCount; phase++)
        {
            switch (phase)
            {
                case 1:
                    inst.ServerSetSecret(Secret1);
                    inst.ServerSetShared(101);
                    break;
                case 2:
                    inst.GiveOwnership(playerA);
                    break;
                case 3:
                    inst.ServerSetSecret(Secret2);
                    inst.ServerSetShared(103);
                    break;
                case 4:
                    inst.GiveOwnership(playerB);
                    break;
                case 5:
                    inst.ServerSetSecret(Secret3);
                    inst.ServerSetShared(105);
                    break;
                case 6:
                    inst.ServerRequestOwnerWrite(OwnerWriteValue);
                    try
                    {
                        await UniTaskUtils.WaitWithTimeout(
                            () => inst.ownerSecret == OwnerWriteValue,
                            _phaseTimeoutSeconds,
                            ctx.cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                        failures.Add($"owner-auth write never reached server: ownerSecret={inst.ownerSecret}");
                    }
                    break;
                case 7:
                    inst.RemoveOwnership();
                    inst.ServerSetSecret(Secret4);
                    inst.ServerSetShared(107);
                    break;
            }

            await UniTask.WaitForSeconds(_propagationDelaySeconds, cancellationToken: ctx.cancellationToken);
            inst.ServerSetPhase(phase);

            try
            {
                int expected = phase;
                await UniTaskUtils.WaitWithTimeout(
                    () => SyncVarOwnerOnlyIdentity.ReportCount(expected) >= ctx.expectedConnections,
                    _phaseTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add(
                    $"phase {phase} report timeout: {SyncVarOwnerOnlyIdentity.ReportCount(phase)}/{ctx.expectedConnections}");
                continue;
            }

            CheckReport(phase, playerA.id.value, "A", secretA[phase],
                twoClients ? 0 : ownerSecretB[phase], sharedByPhase[phase], failures);

            if (twoClients)
                CheckReport(phase, playerB.id.value, "B", secretB[phase], ownerSecretB[phase], sharedByPhase[phase], failures);

            if (isHost)
                CheckReport(phase, hostId, "host", serverSecretByPhase[phase], ownerSecretB[phase], sharedByPhase[phase], failures);

            if (inst.secret != serverSecretByPhase[phase])
                failures.Add($"phase {phase}: server secret={inst.secret}, expected {serverSecretByPhase[phase]}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"clients={clients.Count}, host={isHost}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void CheckReport(int phase, ulong playerId, string label,
        int expectedSecret, int expectedOwnerSecret, int expectedShared, List<string> failures)
    {
        if (!SyncVarOwnerOnlyIdentity.TryGetReport(phase, playerId, out var report))
        {
            failures.Add($"phase {phase}: no report from {label} ({playerId})");
            return;
        }

        if (report.secret != expectedSecret)
            failures.Add($"phase {phase}: {label} secret={report.secret}, expected {expectedSecret}");

        if (report.ownerSecret != expectedOwnerSecret)
            failures.Add($"phase {phase}: {label} ownerSecret={report.ownerSecret}, expected {expectedOwnerSecret}");

        if (report.shared != expectedShared)
            failures.Add($"phase {phase}: {label} shared={report.shared}, expected {expectedShared}");
    }
}
