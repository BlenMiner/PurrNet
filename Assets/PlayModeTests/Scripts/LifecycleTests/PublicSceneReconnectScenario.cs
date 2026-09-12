using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.SceneManagement;

// Replicates the "Live With Process" reconnect bug: while a public additive scene
// is loaded, a client that disconnects and rejoins keeps its session cookie, so the
// server treats the join as a reconnect. The reconnect first-join batch includes the
// public scene's Load action AND re-adding the player to the public scene sends the
// same Load action again, so the client loads a second copy of the scene and logs
// "[ScenesModule] Scene with ID X already exists under ...".
public class PublicSceneReconnectScenario : Scenario
{
    private const string TargetSceneName = "SceneMembershipTargetB";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneMembershipTargetB.unity";
    private const int BarrierBase = 7600;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _membershipTimeoutSeconds = 30f;
    [SerializeField] private float _disconnectTimeoutSeconds = 30f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _settleSeconds = 2f;
    [SerializeField] private float _stayDisconnectedSeconds = 1f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    [Tooltip("Drop the victim's scene membership while they are disconnected, mirroring " +
             "NetworkRules.removePlayerFromSceneOnDisconnect. The reconnect then hits both " +
             "the first-join reconnect batch and the public-scene re-add path.")]
    [SerializeField] private bool _removeVictimFromSceneWhileDisconnected = true;

    private static ulong _victimId;
    private static bool _victimReceived;
    private static bool _disconnectCommandReceived;
    private static bool _phaseDoneReceived;
    private static int _victimReturnedCount;
    private static int _doneCount;

    private static readonly List<string> _duplicateSceneErrors = new List<string>();
    private static bool _capturing;

    // Diagnostics: server-side join records (id, isReconnect) and how many times the
    // victim's Unity SceneManager actually loaded the target scene while reconnecting.
    private static readonly List<(ulong id, bool isReconnect)> _serverJoins = new List<(ulong, bool)>();
    private static readonly HashSet<ulong> _serverLefts = new HashSet<ulong>();
    private static int _victimSceneLoadEvents;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _victimId = 0;
        _victimReceived = false;
        _disconnectCommandReceived = false;
        _phaseDoneReceived = false;
        _victimReturnedCount = 0;
        _doneCount = 0;
        _duplicateSceneErrors.Clear();
        _serverJoins.Clear();
        _serverLefts.Clear();
        _victimSceneLoadEvents = 0;
        StopCapture();
    }

    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        return RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        int buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"target scene missing from build settings: {TargetScenePath}");

        bool cleanupNeeded = false;

        try
        {
            var load = await LoadPublicScene(ctx, buildIndex);
            if (!load.success) return load;
            cleanupNeeded = true;

            if (!TryGetSceneId(ctx, buildIndex, out var sceneId))
                return ScenarioResult.Fail($"network scene id missing after load: {DescribeState(ctx)}");

            var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
            var clients = GetExternalClients(ctx);
            if (clients.Count == 0)
                return ScenarioResult.Fail("no eligible non-server / non-host client for public scene reconnect");

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => AllPlayersLoaded(scenePlayers, clients, sceneId),
                    _membershipTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"clients did not auto-join public scene: {DescribeState(ctx)}");
            }

            var victim = clients[0];
            BroadcastVictim(victim.id.value);

            try
            {
                await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"initial barrier timeout: {DescribeState(ctx)}");
            }

            var playersManager = ctx.networkManager.GetModule<PlayersManager>(true);
            playersManager.onPrePlayerJoined += RecordServerJoin;
            playersManager.onPlayerLeft += RecordServerLeft;

            BroadcastDisconnectCommand();

            var failures = new List<string>();

            if (_removeVictimFromSceneWhileDisconnected)
            {
                try
                {
                    await UniTaskUtils.WaitWithTimeout(
                        () => _serverLefts.Contains(victim.id.value),
                        _disconnectTimeoutSeconds,
                        ctx.cancellationToken);

                    // Same call NetworkRules.removePlayerFromSceneOnDisconnect performs
                    // in ScenePlayersModule.OnPlayerLeft.
                    scenePlayers.RemovePlayerFromScene(victim, sceneId);
                }
                catch (TimeoutException)
                {
                    failures.Add($"server did not observe victim disconnect: {DescribeState(ctx)}");
                }
            }

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => _victimReturnedCount >= 1,
                    _reconnectTimeoutSeconds + _disconnectTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add($"victim {victim.id.value} did not reconnect and report scene state: {DescribeState(ctx)}");
            }
            finally
            {
                playersManager.onPrePlayerJoined -= RecordServerJoin;
                playersManager.onPlayerLeft -= RecordServerLeft;
            }

            // If the rejoin wasn't flagged as a reconnect the LiveWithProcess cookie
            // didn't survive and this scenario isn't exercising the reported path.
            if (!_serverJoins.Exists(j => j.id == victim.id.value && j.isReconnect))
            {
                failures.Add(
                    "victim rejoin was not flagged as reconnect " +
                    $"(joins=[{string.Join(",", _serverJoins.ConvertAll(j => $"{j.id}:{j.isReconnect}"))}])");
            }

            if (failures.Count == 0)
            {
                try
                {
                    await UniTaskUtils.WaitWithTimeout(
                        () => scenePlayers.IsPlayerLoadedInScene(victim, sceneId),
                        _membershipTimeoutSeconds,
                        ctx.cancellationToken);
                }
                catch (TimeoutException)
                {
                    failures.Add($"victim not marked loaded in public scene after reconnect: {DescribeState(ctx)}");
                }
            }

            // Unload -> reload cycle: the deduped reconnect must leave no stale
            // state behind; the scene has to unload cleanly and come back under
            // a fresh SceneID that every client (including the victim) loads.
            if (failures.Count == 0)
            {
                var cycle = await ReloadTargetUnderNewId(ctx, buildIndex, sceneId, scenePlayers, clients);
                if (!cycle.success)
                    failures.Add(cycle.message);
            }

            BroadcastPhaseDone();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => _doneCount >= ctx.expectedConnections,
                    _doneTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add($"done timeout: got {_doneCount}/{ctx.expectedConnections}");
            }

            var cleanup = await UnloadTarget(ctx, buildIndex);
            if (!cleanup.success)
                failures.Add(cleanup.message);
            cleanupNeeded = false;

            try
            {
                await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
            }
            catch (TimeoutException)
            {
                failures.Add($"final barrier timeout: {DescribeState(ctx)}");
            }

            return failures.Count == 0
                ? ScenarioResult.Ok($"victim={victim.id.value}")
                : ScenarioResult.Fail(string.Join(" | ", failures));
        }
        finally
        {
            if (cleanupNeeded)
                await UnloadTarget(ctx, buildIndex);
        }
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _victimReceived && LoadedCopyCount(TargetSceneName) == 1,
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"client did not load public scene / receive victim id: {DescribeState(ctx)}");
        }

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 1, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"initial barrier timeout: {DescribeState(ctx)}");
        }

        var failures = new List<string>();
        bool isVictim = IsLocalVictim(ctx);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _disconnectCommandReceived,
                _disconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"client did not receive disconnect command: {DescribeState(ctx)}");
        }

        if (isVictim)
        {
            var reconnect = await PerformDisconnectReconnect(ctx);
            if (!reconnect.success)
                failures.Add(reconnect.message);

            if (failures.Count == 0 && LoadedCopyCount(TargetSceneName) != 1)
            {
                failures.Add(
                    $"public scene loaded {LoadedCopyCount(TargetSceneName)} times after reconnect " +
                    "(duplicate scene load on rejoin)");
            }

            if (failures.Count == 0 && _victimSceneLoadEvents != 1)
            {
                failures.Add(
                    $"target scene load events during reconnect = {_victimSceneLoadEvents} (expected 1); " +
                    "more than one means the server sent the load action twice");
            }

            if (_duplicateSceneErrors.Count > 0)
            {
                failures.Add(
                    "duplicate scene id errors during reconnect: " +
                    string.Join(" ;; ", _duplicateSceneErrors));
            }

            SignalVictimReturned();
        }

        // Server-driven reload cycle: the scene unloads, then comes back under a
        // fresh SceneID. Every client should observe exactly 1 -> 0 -> 1 copies.
        if (failures.Count == 0)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => LoadedCopyCount(TargetSceneName) == 0,
                    _doneTimeoutSeconds,
                    ctx.cancellationToken);
                await UniTaskUtils.WaitWithTimeout(
                    () => LoadedCopyCount(TargetSceneName) == 1,
                    _sceneTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                failures.Add($"reload cycle not observed on client: {DescribeState(ctx)}");
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _phaseDoneReceived,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"client did not receive phase done: {DescribeState(ctx)}");
        }

        if (!isVictim && LoadedCopyCount(TargetSceneName) > 1)
            failures.Add($"bystander has {LoadedCopyCount(TargetSceneName)} copies of the public scene");

        SignalDone();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => LoadedCopyCount(TargetSceneName) == 0,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add(
                $"client cleanup timeout: {LoadedCopyCount(TargetSceneName)} copies of the public scene " +
                $"still loaded after server unload (leaked duplicate?): {DescribeState(ctx)}");
        }

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 2, _barrierTimeoutSeconds);
        }
        catch (TimeoutException)
        {
            failures.Add($"final barrier timeout: {DescribeState(ctx)}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok(isVictim ? "victim rejoined public scene cleanly" : "bystander unaffected")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> PerformDisconnectReconnect(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;

        manager.StopClient();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => manager.clientState == ConnectionState.Disconnected,
                _disconnectTimeoutSeconds,
                ctx.cancellationToken);

            // OnlineOnly scene cleanup unloads the networked scene locally; mirror the
            // reporter's flow by only rejoining once the local copy is gone.
            await UniTaskUtils.WaitWithTimeout(
                () => LoadedCopyCount(TargetSceneName) == 0,
                _disconnectTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"victim disconnect cleanup timeout: {DescribeState(ctx)}");
        }

        await UniTask.WaitForSeconds(_stayDisconnectedSeconds, cancellationToken: ctx.cancellationToken);

        StartCapture();
        _victimSceneLoadEvents = 0;
        SceneManager.sceneLoaded += CountTargetSceneLoads;
        try
        {
            manager.StartClient();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => manager.isClient && manager.isLocalPlayerReady,
                    _reconnectTimeoutSeconds,
                    ctx.cancellationToken);

                await UniTaskUtils.WaitWithTimeout(
                    () => LoadedCopyCount(TargetSceneName) >= 1 && !HasPendingSceneOperations(ctx),
                    _sceneTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"victim reconnect timeout: {DescribeState(ctx)}");
            }

            // Give a trailing duplicate Load action time to finish before counting copies.
            await UniTask.WaitForSeconds(_settleSeconds, cancellationToken: ctx.cancellationToken);
        }
        finally
        {
            SceneManager.sceneLoaded -= CountTargetSceneLoads;
            StopCapture();
        }

        return ScenarioResult.Ok();
    }

    private static void CountTargetSceneLoads(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == TargetSceneName)
            _victimSceneLoadEvents++;
    }

    private static void RecordServerJoin(PlayerID player, bool isReconnect, bool asServer)
    {
        _serverJoins.Add((player.id.value, isReconnect));
    }

    private static void RecordServerLeft(PlayerID player, bool asServer)
    {
        _serverLefts.Add(player.id.value);
    }

    private static void StartCapture()
    {
        if (_capturing) return;
        _capturing = true;
        Application.logMessageReceived += OnLogMessage;
    }

    private static void StopCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        Application.logMessageReceived -= OnLogMessage;
    }

    private static void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception)
            return;

        if (condition.Contains("already exists"))
            _duplicateSceneErrors.Add(condition);
    }

    private async UniTask<ScenarioResult> LoadPublicScene(ScenarioContext ctx, int buildIndex)
    {
        if (IsNetworkSceneLoaded(ctx, buildIndex))
            return ScenarioResult.Ok();

        var op = ctx.networkManager.sceneModule.LoadSceneAsync(TargetSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.Physics3D,
            isPublic = true
        });

        if (op == null)
            return ScenarioResult.Fail($"LoadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> ReloadTargetUnderNewId(
        ScenarioContext ctx,
        int buildIndex,
        SceneID oldSceneId,
        ScenePlayersModule scenePlayers,
        List<PlayerID> clients)
    {
        var unload = await UnloadTarget(ctx, buildIndex);
        if (!unload.success)
            return ScenarioResult.Fail($"reload cycle: {unload.message}");

        var load = await LoadPublicScene(ctx, buildIndex);
        if (!load.success)
            return ScenarioResult.Fail($"reload cycle: {load.message}");

        if (!TryGetSceneId(ctx, buildIndex, out var newSceneId))
            return ScenarioResult.Fail($"reload cycle: network scene id missing after reload: {DescribeState(ctx)}");

        if (newSceneId == oldSceneId)
            return ScenarioResult.Fail($"reload cycle: scene id {newSceneId} was reused after reload");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AllPlayersLoaded(scenePlayers, clients, newSceneId),
                _membershipTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"reload cycle: clients did not load reloaded scene: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> UnloadTarget(ScenarioContext ctx, int buildIndex)
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            if (IsNetworkSceneLoaded(ctx, buildIndex))
                _ = ctx.networkManager.sceneModule.UnloadSceneAsync(scene);
            else
                _ = SceneManager.UnloadSceneAsync(scene);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !IsNetworkSceneLoaded(ctx, buildIndex) && !IsSceneLoaded(TargetSceneName),
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"server cleanup timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private static bool HasPendingSceneOperations(ScenarioContext ctx)
    {
        var module = ctx.networkManager.sceneModule;
        return module != null && module.GetPendingOperations().Count > 0;
    }

    private static int LoadedCopyCount(string sceneName)
    {
        int count = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.name == sceneName)
                count++;
        }

        return count;
    }

    private static List<PlayerID> GetExternalClients(ScenarioContext ctx)
    {
        var result = new List<PlayerID>();
        var manager = ctx.networkManager;
        var hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        var players = manager.players;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer)
                continue;
            if (hostLocal.HasValue && hostLocal.Value == player)
                continue;
            result.Add(player);
        }

        result.Sort((a, b) => a.id.value.CompareTo(b.id.value));
        return result;
    }

    private static bool AllPlayersLoaded(ScenePlayersModule scenePlayers, List<PlayerID> players, SceneID sceneId)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (!scenePlayers.IsPlayerLoadedInScene(players[i], sceneId))
                return false;
        }

        return true;
    }

    private static bool IsLocalVictim(ScenarioContext ctx)
    {
        return ctx.networkManager.isLocalPlayerReady
               && ctx.networkManager.localPlayer.id.value == _victimId;
    }

    private static int GetBuildIndex(string scenePath) => SceneUtility.GetBuildIndexByScenePath(scenePath);

    private static bool TryGetSceneId(ScenarioContext ctx, int buildIndex, out SceneID sceneId)
    {
        sceneId = default;
        return buildIndex >= 0
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.TryGetScene(buildIndex, out sceneId);
    }

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

    private static string DescribeState(ScenarioContext ctx)
    {
        return $"role={ctx.role}, victim={_victimId}, victimReceived={_victimReceived}, " +
               $"disconnect={_disconnectCommandReceived}, phaseDone={_phaseDoneReceived}, " +
               $"returned={_victimReturnedCount}, done={_doneCount}, " +
               $"copies={LoadedCopyCount(TargetSceneName)}, " +
               $"pendingOps={(ctx.networkManager.sceneModule != null ? ctx.networkManager.sceneModule.GetPendingOperations().Count : -1)}, " +
               $"dupErrors={_duplicateSceneErrors.Count}, " +
               $"loadEvents={_victimSceneLoadEvents}, " +
               $"joins=[{string.Join(",", _serverJoins.ConvertAll(j => $"{j.id}:{j.isReconnect}"))}], " +
               $"networkSceneLoaded={IsNetworkSceneLoaded(ctx, GetBuildIndex(TargetScenePath))}";
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private static void BroadcastVictim(ulong victimId)
    {
        _victimId = victimId;
        _victimReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastDisconnectCommand()
    {
        _disconnectCommandReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastPhaseDone()
    {
        _phaseDoneReceived = true;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalVictimReturned(RPCInfo info = default)
    {
        _victimReturnedCount++;
    }

    [ServerRpc(requireOwnership: false)]
    private static void SignalDone(RPCInfo info = default)
    {
        _doneCount++;
    }
}
