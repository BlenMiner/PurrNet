using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

// Addressable-scene variant of PublicSceneReconnectScenario: a client that keeps
// its LiveWithProcess cookie disconnects while a public addressable scene is
// loaded, loses its scene membership (removePlayerFromSceneOnDisconnect), and
// rejoins. The reconnect used to deliver the LoadAddressable action twice (the
// first-join reconnect batch plus the public-scene re-add), duplicating the
// addressable scene on the client.
public class PublicAddressableSceneReconnectScenario : Scenario
{
    private const string TargetSceneName = "AddressableSceneTransferTarget";
    private const string TargetSceneGuid = "c41b9a783d694893a97bc1cae588df22";
    private const int BarrierBase = 7650;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _membershipTimeoutSeconds = 30f;
    [SerializeField] private float _disconnectTimeoutSeconds = 30f;
    [SerializeField] private float _reconnectTimeoutSeconds = 30f;
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _settleSeconds = 2f;
    [SerializeField] private float _stayDisconnectedSeconds = 1f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private static ulong _victimId;
    private static bool _victimReceived;
    private static bool _disconnectCommandReceived;
    private static bool _phaseDoneReceived;
    private static int _victimReturnedCount;
    private static int _doneCount;

    private static readonly List<string> _duplicateSceneErrors = new List<string>();
    private static bool _capturing;

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
        bool cleanupNeeded = false;

        try
        {
            var load = await LoadAddressableTarget(ctx);
            if (!load.success) return load;
            cleanupNeeded = true;

            if (!TryGetTargetSceneId(ctx, out var sceneId))
                return ScenarioResult.Fail($"addressable network scene id missing after load: {DescribeState(ctx)}");

            var scenePlayers = ctx.networkManager.GetModule<ScenePlayersModule>(true);
            var clients = GetExternalClients(ctx);
            if (clients.Count == 0)
                return ScenarioResult.Fail("no eligible non-server / non-host client for addressable reconnect");

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => AllPlayersLoaded(scenePlayers, clients, sceneId),
                    _membershipTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"clients did not auto-join public addressable scene: {DescribeState(ctx)}");
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
                    failures.Add($"victim not marked loaded in addressable scene after reconnect: {DescribeState(ctx)}");
                }
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

            var cleanup = await UnloadTarget(ctx);
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
                await UnloadTarget(ctx);
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
            return ScenarioResult.Fail($"client did not load public addressable scene / receive victim id: {DescribeState(ctx)}");
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
                    $"public addressable scene loaded {LoadedCopyCount(TargetSceneName)} times after reconnect " +
                    "(duplicate scene load on rejoin)");
            }

            if (failures.Count == 0 && _victimSceneLoadEvents != 1)
            {
                failures.Add(
                    $"addressable scene load events during reconnect = {_victimSceneLoadEvents} (expected 1); " +
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
            failures.Add($"bystander has {LoadedCopyCount(TargetSceneName)} copies of the addressable scene");

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
                $"client cleanup timeout: {LoadedCopyCount(TargetSceneName)} copies of the addressable scene " +
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
            ? ScenarioResult.Ok(isVictim ? "victim rejoined addressable scene cleanly" : "bystander unaffected")
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
                    () => LoadedCopyCount(TargetSceneName) >= 1 && !IsAddressableLoading(ctx),
                    _sceneTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"victim reconnect timeout: {DescribeState(ctx)}");
            }

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

    private async UniTask<ScenarioResult> LoadAddressableTarget(ScenarioContext ctx)
    {
        if (IsNetworkTargetLoaded(ctx))
            return ScenarioResult.Ok();

        var handle = ctx.networkManager.sceneModule.LoadAddressableSceneAsync(TargetSceneGuid, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });

        if (!handle.IsValid())
            return ScenarioResult.Fail($"LoadAddressableSceneAsync returned invalid handle for {TargetSceneGuid}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => handle.IsDone
                      && handle.Status == AsyncOperationStatus.Succeeded
                      && IsNetworkTargetLoaded(ctx),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"addressable scene load timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> UnloadTarget(ScenarioContext ctx)
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        if (scene.IsValid() && scene.isLoaded)
        {
            if (IsNetworkTargetLoaded(ctx))
                _ = ctx.networkManager.sceneModule.UnloadSceneAsync(scene);
            else
                _ = SceneManager.UnloadSceneAsync(scene);
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !IsNetworkTargetLoaded(ctx) && !IsSceneLoaded(TargetSceneName),
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"server cleanup timeout: {DescribeState(ctx)}");
        }

        return ScenarioResult.Ok();
    }

    private static bool IsAddressableLoading(ScenarioContext ctx)
    {
        var module = ctx.networkManager.sceneModule;
        return module != null && module.IsAddressableSceneLoading(TargetSceneGuid);
    }

    private static bool IsNetworkTargetLoaded(ScenarioContext ctx)
    {
        return ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.IsAddressableSceneLoaded(TargetSceneGuid)
               && IsSceneLoaded(TargetSceneName);
    }

    private static bool TryGetTargetSceneId(ScenarioContext ctx, out SceneID sceneId)
    {
        sceneId = default;
        return ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.TryGetSceneIdByAddressableGuid(TargetSceneGuid, out sceneId);
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
               $"addressableLoading={ctx.networkManager.sceneModule != null && ctx.networkManager.sceneModule.IsAddressableSceneLoading(TargetSceneGuid)}, " +
               $"dupErrors={_duplicateSceneErrors.Count}, " +
               $"loadEvents={_victimSceneLoadEvents}, " +
               $"joins=[{string.Join(",", _serverJoins.ConvertAll(j => $"{j.id}:{j.isReconnect}"))}], " +
               $"networkSceneLoaded={IsNetworkTargetLoaded(ctx)}";
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
