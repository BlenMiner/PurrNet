using System;
using Cysharp.Threading.Tasks;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

public class SceneUnloadApiScenario : Scenario
{
    private const string TargetSceneName = "SceneMembershipTargetB";
    private const string TargetScenePath = "Assets/PlayModeTests/SceneMembershipTargetB.unity";
    private const string AddressableSceneName = "AddressableSceneTransferTarget";
    private const string AddressableSceneGuid = "c41b9a783d694893a97bc1cae588df22";
    private const int BarrierBase = 8100;
    private const int Cycles = 2;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _unloadTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private enum UnloadApi
    {
        SceneStruct,
        SceneName,
        BuildIndex,
        SceneId,
        AddressableHandle,
        AddressableSceneStruct,
        Mixed
    }

    private static readonly UnloadApi[] Phases =
    {
        UnloadApi.SceneStruct,
        UnloadApi.SceneName,
        UnloadApi.BuildIndex,
        UnloadApi.SceneId,
        UnloadApi.AddressableHandle,
        UnloadApi.AddressableSceneStruct,
        UnloadApi.Mixed
    };

    private static bool UsesRegular(UnloadApi phase) => phase != UnloadApi.AddressableHandle &&
                                                        phase != UnloadApi.AddressableSceneStruct;

    private static bool UsesAddressable(UnloadApi phase) => phase == UnloadApi.AddressableHandle ||
                                                            phase == UnloadApi.AddressableSceneStruct ||
                                                            phase == UnloadApi.Mixed;

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var buildIndex = GetBuildIndex(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"target scene missing from build settings: {TargetScenePath}");

        string failure;

        try
        {
            failure = await RunPhases(ctx, buildIndex);
        }
        finally
        {
            if (ctx.isServer)
                await Cleanup(ctx, buildIndex);
        }

        return failure == null
            ? ScenarioResult.Ok($"{Cycles} cycles x {Phases.Length} unload entry points")
            : ScenarioResult.Fail(failure);
    }

    /// <summary>
    /// Returns the first failure message, or null when every phase passed. Bails out on the first
    /// failure instead of limping through the remaining barriers: once one side stops participating
    /// every later barrier costs its full timeout, which would blow the sequencer's ack budget and
    /// take down the scenarios that come after this one.
    /// </summary>
    private async UniTask<string> RunPhases(ScenarioContext ctx, int buildIndex)
    {
        for (var cycle = 0; cycle < Cycles; cycle++)
        {
            for (var i = 0; i < Phases.Length; i++)
            {
                var phase = Phases[i];
                var label = $"cycle {cycle} / {phase}";
                var barrier = BarrierBase + (cycle * Phases.Length + i) * 10;

                var load = ctx.isServer
                    ? await ServerLoad(ctx, phase, buildIndex, label)
                    : await ClientWaitLoaded(ctx, phase, buildIndex, label);
                if (!load.success)
                    return load.message;

                if (!await WaitBarrier(ctx, barrier + 1))
                    return $"{label}: peers never reached the post-load barrier";

                var unload = ctx.isServer
                    ? await ServerUnload(ctx, phase, buildIndex, label)
                    : await ClientWaitUnloaded(ctx, phase, buildIndex, label);
                if (!unload.success)
                    return unload.message;

                if (!await WaitBarrier(ctx, barrier + 2))
                    return $"{label}: peers never reached the post-unload barrier";
            }
        }

        return null;
    }

    private async UniTask<ScenarioResult> ServerLoad(
        ScenarioContext ctx, UnloadApi phase, int buildIndex, string label)
    {
        var scenes = ctx.networkManager.sceneModule;

        if (UsesRegular(phase))
        {
            var op = scenes.LoadSceneAsync(TargetSceneName, PublicAdditive());
            if (op == null)
                return ScenarioResult.Fail($"{label}: LoadSceneAsync returned null for {TargetSceneName}");

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                    _sceneTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"{label}: scene load timeout: {DescribeState(ctx, buildIndex)}");
            }
        }

        if (UsesAddressable(phase))
        {
            var handle = scenes.LoadAddressableSceneAsync(AddressableSceneGuid, PublicAdditive());
            if (!handle.IsValid())
                return ScenarioResult.Fail($"{label}: LoadAddressableSceneAsync returned an invalid handle");

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => handle.IsDone
                          && handle.Status == AsyncOperationStatus.Succeeded
                          && IsAddressableRegistered(ctx),
                    _sceneTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail(
                    $"{label}: addressable scene load timeout: {DescribeState(ctx, buildIndex)}");
            }
        }

        if (UsesRegular(phase))
        {
            if (!scenes.TryGetScene(buildIndex, out var regularId))
                return ScenarioResult.Fail($"{label}: no scene id for build index {buildIndex}");

            if (scenes.IsAddressableScene(regularId))
                return ScenarioResult.Fail($"{label}: build-index scene {regularId} reported as addressable");
        }

        if (UsesAddressable(phase))
        {
            if (!scenes.TryGetSceneIdByAddressableGuid(AddressableSceneGuid, out var addressableId))
                return ScenarioResult.Fail($"{label}: no scene id for addressable guid");

            if (!scenes.IsAddressableScene(addressableId))
                return ScenarioResult.Fail($"{label}: addressable scene {addressableId} not reported as addressable");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> ServerUnload(
        ScenarioContext ctx, UnloadApi phase, int buildIndex, string label)
    {
        var scenes = ctx.networkManager.sceneModule;

        switch (phase)
        {
            case UnloadApi.SceneStruct:
            {
                var result = await UnloadRegularAndAwaitOp(
                    ctx, scenes.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneName)),
                    $"{label}: UnloadSceneAsync(Scene)", buildIndex);
                if (!result.success)
                    return result;
                break;
            }
            case UnloadApi.SceneName:
            {
                var result = await UnloadRegularAndAwaitOp(
                    ctx, scenes.UnloadSceneAsync(TargetSceneName),
                    $"{label}: UnloadSceneAsync(string)", buildIndex);
                if (!result.success)
                    return result;
                break;
            }
            case UnloadApi.BuildIndex:
            {
                var result = await UnloadRegularAndAwaitOp(
                    ctx, scenes.UnloadSceneAsync(buildIndex),
                    $"{label}: UnloadSceneAsync(int)", buildIndex);
                if (!result.success)
                    return result;
                break;
            }
            case UnloadApi.SceneId:
            {
                if (!scenes.TryGetScene(buildIndex, out var sceneId))
                    return ScenarioResult.Fail($"{label}: no scene id for build index {buildIndex}");

                scenes.UnloadSceneAsync(sceneId);
                break;
            }
            case UnloadApi.AddressableHandle:
            {
                var result = await UnloadAddressableAndAwaitHandle(ctx, label, buildIndex);
                if (!result.success)
                    return result;
                break;
            }
            case UnloadApi.AddressableSceneStruct:
            {
                _ = scenes.UnloadSceneAsync(SceneManager.GetSceneByName(AddressableSceneName));
                break;
            }
            case UnloadApi.Mixed:
            {
                var regular = await UnloadRegularAndAwaitOp(
                    ctx, scenes.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneName)),
                    $"{label}: UnloadSceneAsync(Scene) with an addressable scene loaded", buildIndex);
                if (!regular.success)
                    return regular;

                if (!IsAddressableRegistered(ctx))
                    return ScenarioResult.Fail(
                        $"{label}: addressable scene was dropped by an unrelated unload: {DescribeState(ctx, buildIndex)}");

                var addressable = await UnloadAddressableAndAwaitHandle(ctx, label, buildIndex);
                if (!addressable.success)
                    return addressable;
                break;
            }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IsFullyUnloaded(ctx, phase, buildIndex),
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{label}: server unload timeout: {DescribeState(ctx, buildIndex)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> UnloadRegularAndAwaitOp(
        ScenarioContext ctx, AsyncOperation op, string what, int buildIndex)
    {
        if (op == null)
            return ScenarioResult.Fail($"{what} returned null for a non-addressable scene");

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => op.isDone, _unloadTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"{what} never completed (progress={op.progress}): {DescribeState(ctx, buildIndex)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> UnloadAddressableAndAwaitHandle(
        ScenarioContext ctx, string label, int buildIndex)
    {
        var scenes = ctx.networkManager.sceneModule;

        if (!scenes.TryGetSceneIdByAddressableGuid(AddressableSceneGuid, out var sceneId))
            return ScenarioResult.Fail($"{label}: no scene id for addressable guid before unload");

        var handle = scenes.UnloadAddressableSceneAsync(sceneId);
        if (!handle.IsValid())
            return ScenarioResult.Fail($"{label}: UnloadAddressableSceneAsync returned an invalid handle");

        try
        {
            // An auto-released handle goes invalid the moment it completes, and every member
            // access on it then throws - that would make the handle impossible to await, which
            // is the whole point of returning it.
            await UniTaskUtils.WaitWithTimeout(
                () => !handle.IsValid() || handle.IsDone,
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"{label}: addressable unload handle never completed: {DescribeState(ctx, buildIndex)}");
        }

        if (!handle.IsValid())
            return ScenarioResult.Fail($"{label}: addressable unload handle was released before it could be awaited");

        if (handle.Status != AsyncOperationStatus.Succeeded)
            return ScenarioResult.Fail($"{label}: addressable unload failed: {handle.OperationException}");

        Addressables.Release(handle);
        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> ClientWaitLoaded(
        ScenarioContext ctx, UnloadApi phase, int buildIndex, string label)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => (!UsesRegular(phase) || (IsSceneLoaded(TargetSceneName) && IsNetworkSceneLoaded(ctx, buildIndex)))
                      && (!UsesAddressable(phase) ||
                          (IsSceneLoaded(AddressableSceneName) && IsAddressableRegistered(ctx))),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{label}: client never saw the scene load: {DescribeState(ctx, buildIndex)}");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> ClientWaitUnloaded(
        ScenarioContext ctx, UnloadApi phase, int buildIndex, string label)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IsFullyUnloaded(ctx, phase, buildIndex),
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{label}: client never saw the scene unload: {DescribeState(ctx, buildIndex)}");
        }

        return ScenarioResult.Ok();
    }

    private static bool IsFullyUnloaded(ScenarioContext ctx, UnloadApi phase, int buildIndex)
    {
        if (UsesRegular(phase) && (IsSceneLoaded(TargetSceneName) || IsNetworkSceneLoaded(ctx, buildIndex)))
            return false;

        if (UsesAddressable(phase) && (IsSceneLoaded(AddressableSceneName) || IsAddressableRegistered(ctx)))
            return false;

        return true;
    }

    private async UniTask Cleanup(ScenarioContext ctx, int buildIndex)
    {
        var scenes = ctx.networkManager.sceneModule;

        if (IsNetworkSceneLoaded(ctx, buildIndex))
            _ = scenes.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneName));
        else if (IsSceneLoaded(TargetSceneName))
            _ = SceneManager.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneName));

        if (scenes.TryGetSceneIdByAddressableGuid(AddressableSceneGuid, out var addressableId))
        {
            var handle = scenes.UnloadAddressableSceneAsync(addressableId);
            if (handle.IsValid())
                handle.Completed += completed => Addressables.Release(completed);
        }
        else if (IsSceneLoaded(AddressableSceneName))
            _ = SceneManager.UnloadSceneAsync(SceneManager.GetSceneByName(AddressableSceneName));

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !IsSceneLoaded(TargetSceneName)
                      && !IsSceneLoaded(AddressableSceneName)
                      && !IsNetworkSceneLoaded(ctx, buildIndex)
                      && !IsAddressableRegistered(ctx),
                _unloadTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
        }
    }

    private async UniTask<bool> WaitBarrier(ScenarioContext ctx, int barrierId)
    {
        try
        {
            await ScenarioBarrier.Wait(ctx, barrierId, _barrierTimeoutSeconds);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static PurrSceneSettings PublicAdditive()
    {
        return new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        };
    }

    private static int GetBuildIndex(string scenePath) => SceneUtility.GetBuildIndexByScenePath(scenePath);

    private static bool IsSceneLoaded(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private static bool IsNetworkSceneLoaded(ScenarioContext ctx, int buildIndex)
    {
        return buildIndex >= 0
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.IsSceneLoaded(buildIndex);
    }

    private static bool IsAddressableRegistered(ScenarioContext ctx)
    {
        var scenes = ctx.networkManager.sceneModule;
        if (scenes == null)
            return false;

        return scenes.IsAddressableSceneLoaded(AddressableSceneGuid)
               && scenes.TryGetSceneIdByAddressableGuid(AddressableSceneGuid, out var sceneId)
               && scenes.IsAddressableScene(sceneId);
    }

    private static string DescribeState(ScenarioContext ctx, int buildIndex)
    {
        var scenes = ctx.networkManager.sceneModule;
        return $"role={ctx.role}, " +
               $"regularSceneLoaded={IsSceneLoaded(TargetSceneName)}, " +
               $"regularNetworkSceneLoaded={IsNetworkSceneLoaded(ctx, buildIndex)}, " +
               $"addressableSceneLoaded={IsSceneLoaded(AddressableSceneName)}, " +
               $"addressableRegistered={IsAddressableRegistered(ctx)}, " +
               $"addressableLoading={scenes != null && scenes.IsAddressableSceneLoading(AddressableSceneGuid)}, " +
               $"trackedScenes={(scenes != null ? scenes.scenes.Count : -1)}";
    }
}
