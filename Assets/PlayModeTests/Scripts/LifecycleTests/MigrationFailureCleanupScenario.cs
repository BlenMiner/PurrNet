using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

// Standalone/inactive: every external client deliberately leaves the authority
// five times. Reports use the runner's shared fresh directory while networking is
// unavailable; each case must restore a normal connection before continuing.
public sealed class MigrationFailureCleanupScenario : Scenario
{
    private const int ReadyBarrier = 28760;
    private const float StageTimeout = 30f;
    private const float CampaignTimeout = 240f;
    private const float ApiTimeout = 2f;
    private const int CaseCount = 5;

    [Serializable]
    private sealed class Report
    {
        public bool success;
        public int cases;
        public int expectedOutcomes;
        public string message;
    }

    private MigrationFailureCleanupProbe _prefab;
    private MigrationFailurePendingTransport _pending;
    private GenericTransport _originalTransport;
    private string _directory;
    private string _authorityReport;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        MigrationFailureCleanupProbe.ResetAll();
        _originalTransport = manager.transport;
        var prefabObject = new GameObject(nameof(MigrationFailureCleanupProbe));
        prefabObject.SetActive(false);
        _prefab = prefabObject.AddComponent<MigrationFailureCleanupProbe>();
        manager.prefabProvider.AddRuntimePrefab(prefabObject.name, prefabObject);
        if (ctx.role == NetworkRole.Client)
            _pending = manager.gameObject.AddComponent<MigrationFailurePendingTransport>();
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (!CommandLineUtils.TryGetArgument("-results", out var results) ||
            string.IsNullOrEmpty(Path.GetDirectoryName(results)))
            return ScenarioResult.Fail("Migration failure tests require -results in a shared fresh directory.");
        _directory = Path.GetFullPath(Path.GetDirectoryName(results));
        _authorityReport = Path.Combine(_directory, "migration-failure-authority.json");
        Directory.CreateDirectory(_directory);
        return ctx.isServer ? await RunAuthority(ctx) : await RunExternalClient(ctx);
    }

    private async UniTask<ScenarioResult> RunAuthority(ScenarioContext ctx)
    {
        MigrationFailureCleanupProbe spawned = null;
        var report = new Report();
        try
        {
            Require(!File.Exists(_authorityReport), "Use a fresh results directory for migration failure tests.");
            var externalPlayers = new List<PlayerID>();
            foreach (var player in ctx.networkManager.players)
                if (!player.isServer && (!ctx.isClient || player != ctx.networkManager.localPlayer))
                    externalPlayers.Add(player);
            Require(externalPlayers.Count == ctx.expectedConnections - (ctx.isClient ? 1 : 0) &&
                    externalPlayers.Count > 0, "Expected at least one external client; no failure cases may be skipped.");
            foreach (var player in externalPlayers)
                Require(!File.Exists(ClientReport(player.id.value)), "A stale client report exists.");

            var bootstrap = SceneManager.GetSceneByName("Bootstrap");
            Require(bootstrap.IsValid() && bootstrap.isLoaded, "Bootstrap must remain loaded for this standalone scenario.");
            spawned = UnityProxy.InstantiateDirectly(_prefab);
            if (spawned.gameObject.scene != bootstrap)
                SceneManager.MoveGameObjectToScene(spawned.gameObject, bootstrap);
            spawned.Spawn(_prefab.gameObject, ctx.networkManager);
            await UniTaskUtils.WaitWithTimeout(() => spawned && spawned.IsSpawned(true) &&
                    (!ctx.isClient || HasClientReplica()), StageTimeout, ctx.cancellationToken);
            await ScenarioBarrier.Wait(ctx, ReadyBarrier, StageTimeout);

            await UniTaskUtils.WaitWithTimeout(() => AllReportsExist(externalPlayers), CampaignTimeout,
                ctx.cancellationToken);
            foreach (var player in externalPlayers)
            {
                var client = ReadReport(ClientReport(player.id.value));
                Require(client.success && client.cases == CaseCount && client.expectedOutcomes == 4,
                    $"External client {player} did not complete all failure cases: {client.message}");
                report.cases += client.cases;
                report.expectedOutcomes += client.expectedOutcomes;
            }

            Require(ctx.networkManager.sceneModule.TryGetSceneID(bootstrap, out var sceneId),
                "Authority lost the Bootstrap registration.");
            var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
            await UniTaskUtils.WaitWithTimeout(() => AllPlayersReady(ctx, scenePlayers, sceneId),
                StageTimeout, ctx.cancellationToken);
            Require(spawned && spawned.IsSpawned(true) && spawned.value == MigrationFailureCleanupProbe.ExpectedValue,
                "A failed migration changed the original authority's probe.");
            report.success = true;
            report.message = $"{externalPlayers.Count} external clients completed {report.cases} cases; " +
                "pending transfer/promotion cancellation and timeout cleaned up, preparation release cleaned up, every reconnect restored state";
        }
        catch (Exception exception)
        {
            report.message = exception.Message;
        }
        finally
        {
            if (spawned && spawned.isSpawned)
                spawned.Despawn();
            else if (spawned)
                UnityProxy.DestroyDirectly(spawned.gameObject);
            WriteReport(_authorityReport, report);
        }
        if (report.success)
        {
            try
            {
                // Keep the authority alive until every reconnected peer observed
                // the final despawn; writing the report alone is not a wire flush.
                await UniTaskUtils.WaitWithTimeout(() => !ctx.isClient || !MigrationFailureCleanupProbe.clientInstance,
                    StageTimeout, ctx.cancellationToken);
                await ScenarioBarrier.Wait(ctx, ReadyBarrier + 1, StageTimeout);
            }
            catch (Exception exception)
            {
                report.success = false;
                report.message += $"; final cleanup acknowledgement: {exception.Message}";
            }
        }
        return report.success ? ScenarioResult.Ok(report.message) : ScenarioResult.Fail(report.message);
    }

    private async UniTask<ScenarioResult> RunExternalClient(ScenarioContext ctx)
    {
        var report = new Report();
        ulong originalPlayerId = ctx.networkManager.localPlayer.id.value;
        try
        {
            await UniTaskUtils.WaitWithTimeout(HasClientReplica, StageTimeout, ctx.cancellationToken);
            await ScenarioBarrier.Wait(ctx, ReadyBarrier, StageTimeout);
            report.expectedOutcomes += await RunApiCase(ctx, false, true);
            report.cases++;
            report.expectedOutcomes += await RunApiCase(ctx, false, false);
            report.cases++;
            report.expectedOutcomes += await RunApiCase(ctx, true, true);
            report.cases++;
            report.expectedOutcomes += await RunApiCase(ctx, true, false);
            report.cases++;
            await RunReleasedPreparation(ctx);
            report.cases++;
            report.success = true;
            report.message = "5 cases completed; 4 awaited false results after pending transport state; " +
                "all old replicas cleaned, migration flags/modules cleared, normal reconnect restored each replica";
        }
        catch (Exception exception)
        {
            report.message = exception.Message;
            try
            {
                await RecoverOriginalTransport(ctx);
            }
            catch (Exception recovery)
            {
                report.message += $"; recovery: {recovery.Message}";
            }
        }
        finally
        {
            WriteReport(ClientReport(originalPlayerId), report);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => File.Exists(_authorityReport), CampaignTimeout,
                ctx.cancellationToken);
            var authority = ReadReport(_authorityReport);
            Require(authority.success, $"Authority validation: {authority.message}");
            await UniTaskUtils.WaitWithTimeout(() => !MigrationFailureCleanupProbe.clientInstance,
                StageTimeout, ctx.cancellationToken);
            Require(ctx.networkManager.transport == _originalTransport && ctx.networkManager.isClient &&
                    !ctx.networkManager.isServer, "Original client transport was not restored.");
            await ScenarioBarrier.Wait(ctx, ReadyBarrier + 1, StageTimeout);
        }
        catch (Exception exception)
        {
            report.success = false;
            report.message += $"; final cleanup: {exception.Message}";
        }
        return report.success ? ScenarioResult.Ok(report.message) : ScenarioResult.Fail(report.message);
    }

    private async UniTask<int> RunApiCase(ScenarioContext ctx, bool promote, bool cancel)
    {
        string label = (promote ? "promotion" : "transfer") + (cancel ? " cancellation" : " timeout");
        var manager = ctx.networkManager;
        var oldReplica = MigrationFailureCleanupProbe.clientInstance;
        int despawns = MigrationFailureCleanupProbe.clientDespawns;
        int spawns = MigrationFailureCleanupProbe.clientSpawns;
        await PreserveAndDisconnect(ctx, oldReplica);
        _pending.ResetAttempts();
        manager.transport = _pending;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ctx.cancellationToken);
        using var logs = new MigrationFailureExpectedLogScope(label, cancel, cancellation.Token,
            () => promote ? manager.isPromotingToServer : manager.isTranferingToNewServer);
        Task<bool> operation = promote
            ? manager.PromoteToServerAsync(cancel ? StageTimeout : ApiTimeout, cancellation.Token)
            : manager.TransferToNewServerAsync(true, cancel ? StageTimeout : ApiTimeout, cancellation.Token);
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => operation.IsCompleted || (promote
                    ? _pending.listenAttempts > 0 && manager.serverState == ConnectionState.Connecting
                    : _pending.connectAttempts > 0 && manager.clientState == ConnectionState.Connecting),
                StageTimeout, ctx.cancellationToken);
            Require(!operation.IsCompleted, $"{label}: API returned before entering a pending transport state.");
            Require(oldReplica, $"{label}: retained Unity identity was destroyed before the API failed.");
            Require(oldReplica.isSpawned == promote,
                $"{label}: only promotion should keep the old network lifetime while transport is pending.");
            Require(manager.isMigratingServer,
                $"{label}: migration flag was not active while transport was pending.");
            await UniTask.NextFrame();
            Require(!operation.IsCompleted, $"{label}: operation did not remain pending for one update.");
            if (cancel)
                cancellation.Cancel();
            bool result = await UniTaskUtils.WithTimeout(operation, StageTimeout, ctx.cancellationToken, label);
            Require(!result, $"{label}: expected false after deliberate {(cancel ? "cancellation" : "timeout")}.");
            Require(!manager.isMigratingServer,
                $"{label}: migration flags remained set after the awaited result.");
            Require(logs.count == 1, $"{label}: expected exactly one captured outcome, got {logs.count}.");
        }
        finally
        {
            // Ensure an assertion cannot leave a still-running migration behind.
            if (!operation.IsCompleted)
            {
                cancellation.Cancel();
                await UniTaskUtils.WithTimeout(operation, StageTimeout, ctx.cancellationToken, label + " termination");
            }
        }

        await VerifyCleanup(ctx, oldReplica, despawns, label);
        await Reconnect(ctx, oldReplica, spawns, label);
        Debug.Log($"[MigrationFailureCase] {label}: pending state observed, false result, cleanup and reconnect verified");
        return logs.count;
    }

    private async UniTask RunReleasedPreparation(ScenarioContext ctx)
    {
        var oldReplica = MigrationFailureCleanupProbe.clientInstance;
        int despawns = MigrationFailureCleanupProbe.clientDespawns;
        int spawns = MigrationFailureCleanupProbe.clientSpawns;
        await PreserveAndDisconnect(ctx, oldReplica);
        // Observe preservation across updates before explicitly abandoning it.
        await UniTask.NextFrame();
        await UniTask.NextFrame();
        Require(oldReplica && oldReplica.IsSpawned(false) &&
                MigrationFailureCleanupProbe.clientDespawns == despawns,
            "Abandoned preparation was not preserving its original replica.");
        Require(!ctx.networkManager.isPromotingToServer && !ctx.networkManager.isTranferingToNewServer,
            "Preparation unexpectedly started a migration operation.");
        ctx.networkManager.ReleaseClientStateForHostMigration();
        await VerifyCleanup(ctx, oldReplica, despawns, "released preparation");
        await Reconnect(ctx, oldReplica, spawns, "released preparation");
        Debug.Log("[MigrationFailureCase] released preparation: retained state observed, cleanup and reconnect verified");
    }

    private static async UniTask PreserveAndDisconnect(ScenarioContext ctx, MigrationFailureCleanupProbe replica)
    {
        Require(HasClientReplica() && replica == MigrationFailureCleanupProbe.clientInstance,
            "Each failure case must start with a live authoritative replica.");
        ctx.networkManager.PreserveClientStateForHostMigration();
        ctx.networkManager.StopClient();
        await UniTaskUtils.WaitWithTimeout(() => ctx.networkManager.clientState == ConnectionState.Disconnected,
            StageTimeout, ctx.cancellationToken);
        Require(replica && replica.IsSpawned(false) &&
                ctx.networkManager.TryGetModule<ScenesModule>(false, out _) &&
                ctx.networkManager.TryGetModule<HierarchyFactory>(false, out _),
            "Preparation did not keep the client modules and original identity alive.");
    }

    private static async UniTask VerifyCleanup(ScenarioContext ctx, MigrationFailureCleanupProbe oldReplica,
        int despawnsBefore, string label)
    {
        var manager = ctx.networkManager;
        await UniTaskUtils.WaitWithTimeout(() => manager.clientState == ConnectionState.Disconnected &&
                manager.serverState == ConnectionState.Disconnected && !oldReplica &&
                !manager.TryGetModule<ScenesModule>(false, out _) &&
                !manager.TryGetModule<HierarchyFactory>(false, out _) &&
                !manager.TryGetModule<ScenesModule>(true, out _) &&
                !manager.TryGetModule<HierarchyFactory>(true, out _), StageTimeout, ctx.cancellationToken);
        Require(!manager.isPromotingToServer && !manager.isTranferingToNewServer && !manager.isLocalPlayerReady,
            $"{label}: migration or player readiness state survived cleanup.");
        Require(MigrationFailureCleanupProbe.clientDespawns == despawnsBefore + 1,
            $"{label}: old replica did not despawn exactly once.");
    }

    private async UniTask Reconnect(ScenarioContext ctx, MigrationFailureCleanupProbe oldReplica, int spawnsBefore,
        string label)
    {
        ctx.networkManager.transport = _originalTransport;
        ctx.networkManager.StartClient();
        await UniTaskUtils.WaitWithTimeout(() => ctx.networkManager.isClient && ctx.networkManager.isLocalPlayerReady &&
                HasClientReplica(), StageTimeout, ctx.cancellationToken);
        Require(!ReferenceEquals(oldReplica, MigrationFailureCleanupProbe.clientInstance) &&
                MigrationFailureCleanupProbe.clientSpawns == spawnsBefore + 1,
            $"{label}: normal reconnect did not receive one fresh replica.");
        Require(!ctx.networkManager.isServer && !ctx.networkManager.isPromotingToServer &&
                !ctx.networkManager.isTranferingToNewServer,
            $"{label}: reconnect retained an abandoned server or migration state.");
    }

    private async UniTask RecoverOriginalTransport(ScenarioContext ctx)
    {
        ctx.networkManager.ReleaseClientStateForHostMigration();
        ctx.networkManager.StopClient();
        ctx.networkManager.StopServer();
        await UniTaskUtils.WaitWithTimeout(() => ctx.networkManager.clientState == ConnectionState.Disconnected &&
                ctx.networkManager.serverState == ConnectionState.Disconnected, StageTimeout, ctx.cancellationToken);
        ctx.networkManager.transport = _originalTransport;
        ctx.networkManager.StartClient();
        await UniTaskUtils.WaitWithTimeout(() => ctx.networkManager.isClient && ctx.networkManager.isLocalPlayerReady,
            StageTimeout, ctx.cancellationToken);
    }

    private static bool HasClientReplica()
    {
        var replica = MigrationFailureCleanupProbe.clientInstance;
        return replica && replica.IsSpawned(false) && replica.value == MigrationFailureCleanupProbe.ExpectedValue;
    }

    private static bool AllPlayersReady(ScenarioContext ctx, ScenePlayersModule players, SceneID scene)
    {
        if (ctx.networkManager.playerCount != ctx.expectedConnections)
            return false;
        foreach (var player in ctx.networkManager.players)
            if (!players.IsPlayerLoadedInScene(player, scene))
                return false;
        return true;
    }

    private bool AllReportsExist(List<PlayerID> players)
    {
        foreach (var player in players)
            if (!File.Exists(ClientReport(player.id.value)))
                return false;
        return true;
    }

    private string ClientReport(ulong playerId) => Path.Combine(_directory, $"migration-failure-client-{playerId}.json");
    private static Report ReadReport(string path) => JsonUtility.FromJson<Report>(File.ReadAllText(path));

    private static void WriteReport(string path, Report report)
    {
        var pending = path + ".pending";
        File.WriteAllText(pending, JsonUtility.ToJson(report));
        File.Move(pending, path);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
