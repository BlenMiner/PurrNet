using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
#if UNITY_6000_3_OR_NEWER
using SceneHandle = UnityEngine.SceneManagement.SceneHandle;
#else
using SceneHandle = System.Int32;
#endif

/// <summary>
/// Loads a scene that carries its own NetworkPrefabs and NetworkAssets registries, spawns scene scoped
/// prefabs from it (pooled and unpooled), ships a scene scoped asset through an RPC, despawns back into the
/// scene pool, unloads the scene and verifies that every registry, pool, cached prototype and the assets
/// themselves are gone. Runs twice so the second load gets a fresh SceneID and proves nothing stale survives.
/// </summary>
public class SceneScopedRegistryScenario : Scenario
{
    private const string TargetSceneName = "SceneScopedTarget";
    private const string TargetScenePath = "Assets/PlayModeTestsScoped/SceneScopedTarget.unity";
    private const string RegistryObjectName = "SceneScopedRegistry";
    private const string PooledPrefabName = "SceneScopedPooledPrefab";
    private const string PlainPrefabName = "SceneScopedPlainPrefab";
    private const string TextAssetName = "SceneScopedText";
    private const int WarmupCount = 2;
    private const int BarrierBase = 9500;
    private const int Cycles = 2;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _syncTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private static string _receivedAssetName;
    private static bool _assetReceived;

    private sealed class CycleState
    {
        public SceneID sceneId;
        public SceneHandle handle;
        public PrefabID pooledId;
        public PrefabID plainId;
        public NetworkAssetID assetId;
        public WeakReference<GameObject> pooledPrefab;
        public WeakReference<Object> textAsset;
        public WeakReference<NetworkPrefabs> prefabRegistry;
        public GameObject pooledInstance;
        public GameObject plainInstance;
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _receivedAssetName = null;
        _assetReceived = false;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var buildIndex = SceneUtility.GetBuildIndexByScenePath(TargetScenePath);
        if (buildIndex < 0)
            return ScenarioResult.Fail($"target scene missing from build settings: {TargetScenePath}");

        string failure;
        var memoryChecked = false;

        try
        {
            failure = await RunCycles(ctx, buildIndex, checkedMemory => memoryChecked = checkedMemory);
        }
        finally
        {
            if (ctx.isServer)
                await Cleanup(ctx, buildIndex);
        }

        if (failure != null)
            return ScenarioResult.Fail(failure);

        var memoryNote = memoryChecked ? "assets released after unload" : "asset release check skipped in editor";
        return ScenarioResult.Ok($"{Cycles} cycles of load/spawn/despawn/unload with scene scoped ids; {memoryNote}");
    }

    private async UniTask<string> RunCycles(ScenarioContext ctx, int buildIndex, Action<bool> reportMemoryChecked)
    {
        for (var cycle = 0; cycle < Cycles; cycle++)
        {
            var label = $"cycle {cycle}";
            var barrier = BarrierBase + cycle * 10;
            var state = new CycleState();
            _assetReceived = false;
            _receivedAssetName = null;

            var load = ctx.isServer
                ? await ServerLoad(ctx, buildIndex, label)
                : await ClientWaitLoaded(ctx, buildIndex, label);
            if (!load.success)
                return load.message;

            var inspect = InspectLoadedScene(ctx, buildIndex, state);
            if (inspect != null)
                return $"{label}: {inspect}";

            if (!await WaitBarrier(ctx, barrier + 1))
                return $"{label}: peers never reached the post-load barrier";

            if (ctx.isServer)
            {
                var spawn = ServerSpawn(ctx, state);
                if (spawn != null)
                    return $"{label}: {spawn}";
            }

            var spawned = await WaitSpawned(ctx, state, 2, label);
            if (spawned != null)
                return spawned;

            var asset = await WaitAssetRpc(ctx, label);
            if (asset != null)
                return asset;

            var pooledAfterSpawn = await WaitPooledCount(ctx, state, WarmupCount - 1, label + ": after spawn");
            if (pooledAfterSpawn != null)
                return pooledAfterSpawn;

            if (!await WaitBarrier(ctx, barrier + 2))
                return $"{label}: peers never reached the post-spawn barrier";

            if (ctx.isServer)
                UnityProxy.Destroy(state.pooledInstance);

            var pooledAfterDespawn = await WaitPooledCount(ctx, state, WarmupCount, label + ": after despawn");
            if (pooledAfterDespawn != null)
                return pooledAfterDespawn;

            var remaining = await WaitSpawned(ctx, state, 1, label + ": after despawn");
            if (remaining != null)
                return remaining;

            if (!await WaitBarrier(ctx, barrier + 3))
                return $"{label}: peers never reached the post-despawn barrier";

            var unload = ctx.isServer
                ? await ServerUnload(ctx, buildIndex, label)
                : await ClientWaitUnloaded(ctx, buildIndex, label);
            if (!unload.success)
                return unload.message;

            var released = await WaitReleased(ctx, state, label);
            if (released != null)
                return released;

            var memory = await VerifyAssetsUnloaded(state);
            if (memory.failure != null)
                return $"{label}: {memory.failure}";
            reportMemoryChecked(memory.checkedMemory);

            if (!await WaitBarrier(ctx, barrier + 4))
                return $"{label}: peers never reached the post-unload barrier";
        }

        return null;
    }

    private async UniTask<ScenarioResult> ServerLoad(ScenarioContext ctx, int buildIndex, string label)
    {
        var op = ctx.networkManager.sceneModule.LoadSceneAsync(TargetSceneName, PublicAdditive());
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
            return ScenarioResult.Fail($"{label}: scene load timeout on the server");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> ClientWaitLoaded(ScenarioContext ctx, int buildIndex, string label)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IsSceneLoaded() && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{label}: client never saw the scene load");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> ServerUnload(ScenarioContext ctx, int buildIndex, string label)
    {
        var op = ctx.networkManager.sceneModule.UnloadSceneAsync(TargetSceneName);
        if (op == null)
            return ScenarioResult.Fail($"{label}: UnloadSceneAsync returned null for {TargetSceneName}");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && !IsSceneLoaded() && !IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{label}: scene unload timeout on the server");
        }

        return ScenarioResult.Ok();
    }

    private async UniTask<ScenarioResult> ClientWaitUnloaded(ScenarioContext ctx, int buildIndex, string label)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !IsSceneLoaded() && !IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"{label}: client never saw the scene unload");
        }

        return ScenarioResult.Ok();
    }

    /// <summary>
    /// Checks registries, resolution and pool warmup on whichever peer runs it, keeping only weak
    /// references to the assets so the release check at the end is not defeated by the scenario itself.
    /// </summary>
    private static string InspectLoadedScene(ScenarioContext ctx, int buildIndex, CycleState state)
    {
        var manager = ctx.networkManager;

        if (!manager.sceneModule.TryGetScene(buildIndex, out state.sceneId))
            return "no SceneID for the target scene";

        if (!manager.TryGetScene(state.sceneId, out var scene))
            return "no Unity scene for the target SceneID";

        state.handle = scene.handle;

        if (!SceneRegistry<NetworkPrefabs>.TryGetEntries(scene.handle, out var prefabRegistries) ||
            prefabRegistries.Count != 1)
            return "scene prefab registry did not register";

        if (!SceneRegistry<NetworkAssets>.TryGetEntries(scene.handle, out var assetRegistries) ||
            assetRegistries.Count != 1)
            return "scene asset registry did not register";

        var registry = prefabRegistries[0];
        GameObject pooled = null;
        GameObject plain = null;

        foreach (var data in registry.allPrefabs)
        {
            if (!data.prefab)
                continue;

            if (data.prefab.name == PooledPrefabName)
                pooled = data.prefab;
            else if (data.prefab.name == PlainPrefabName)
                plain = data.prefab;
        }

        if (!pooled || !plain)
            return "test prefabs missing from the scene registry";

        var resolver = manager.prefabResolver;

        if (!resolver.TryGetPrefabData(pooled, state.sceneId, out var pooledData) ||
            !pooledData.prefabId.isSceneScoped || pooledData.prefabId.scope.Value != state.sceneId)
            return "pooled prefab did not resolve to a scene scoped id with a hint";

        if (!resolver.TryGetPrefabData(plain, out var plainData) ||
            !plainData.prefabId.isSceneScoped || plainData.prefabId.scope.Value != state.sceneId)
            return "plain prefab did not resolve to a scene scoped id without a hint";

        if (!resolver.TryGetPrefabData(pooledData.prefabId, out var roundTrip) || roundTrip.prefab != pooled)
            return "scene scoped id did not resolve back to the pooled prefab";

        if (!pooledData.pooled)
            return "pooled prefab lost its pooling flag through the resolver";

        Object text = null;
        foreach (var asset in assetRegistries[0].AllAssets)
        {
            if (asset && asset.name == TextAssetName)
                text = asset;
        }

        if (!text)
            return "test text asset missing from the scene asset registry";

        var assets = manager.networkAssetResolver;

        if (!assets.TryGetId(text, out state.assetId) ||
            !state.assetId.isSceneScoped || state.assetId.scope.Value != state.sceneId)
            return "text asset did not resolve to a scene scoped id";

        if (!assets.TryGetAsset(state.assetId, out var resolvedAsset) || resolvedAsset != text)
            return "scene scoped asset id did not resolve back to the text asset";

        if (!NetworkPoolManager.TryGetScenePool(state.sceneId, out var pool))
            return "scene pool missing after load";

        var warm = pool.GetPooledCount(pooledData.prefabId);
        if (warm != WarmupCount)
            return $"expected {WarmupCount} warm pooled pieces in the scene pool, found {warm}";

        if (!HierarchyPool.HasPrototype(pooledData.prefabId))
            return "prototype not cached for the pooled prefab after warmup";

        state.pooledId = pooledData.prefabId;
        state.plainId = plainData.prefabId;
        state.pooledPrefab = new WeakReference<GameObject>(pooled);
        state.textAsset = new WeakReference<Object>(text);
        state.prefabRegistry = new WeakReference<NetworkPrefabs>(registry);
        return null;
    }

    private static string ServerSpawn(ScenarioContext ctx, CycleState state)
    {
        var manager = ctx.networkManager;

        if (!manager.TryGetScene(state.sceneId, out var scene))
            return "target scene vanished before spawning";

        Transform parent = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == RegistryObjectName)
                parent = root.transform;
        }

        if (!parent)
            return "registry object not found in the target scene";

        var resolver = manager.prefabResolver;
        if (!resolver.TryGetPrefabData(state.pooledId, out var pooledData) ||
            !resolver.TryGetPrefabData(state.plainId, out var plainData))
            return "scene scoped ids stopped resolving before spawning";

        state.pooledInstance = Instantiate(pooledData.prefab, parent);
        state.plainInstance = Instantiate(plainData.prefab, parent);

        if (!state.pooledInstance.TryGetComponent<NetworkIdentity>(out var pooledIdentity) || !pooledIdentity.isSpawned)
            return "pooled instance did not auto spawn";

        if (pooledIdentity.scopedPrefabId != state.pooledId)
            return $"pooled instance carries id {pooledIdentity.scopedPrefabId}, expected {state.pooledId}";

        if (!state.plainInstance.TryGetComponent<NetworkIdentity>(out var plainIdentity) || !plainIdentity.isSpawned)
            return "plain instance did not auto spawn";

        if (plainIdentity.scopedPrefabId != state.plainId)
            return $"plain instance carries id {plainIdentity.scopedPrefabId}, expected {state.plainId}";

        if (manager.networkAssetResolver.TryGetAsset(state.assetId, out var asset) && asset is TextAsset text)
            BroadcastAsset(text);
        else
            return "text asset could not be fetched for the RPC";

        return null;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastAsset(TextAsset asset)
    {
        _receivedAssetName = asset ? asset.name : null;
        _assetReceived = true;
    }

    private async UniTask<string> WaitAssetRpc(ScenarioContext ctx, string label)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => _assetReceived, _syncTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return $"{label}: scene scoped asset RPC never arrived";
        }

        if (_receivedAssetName != TextAssetName)
            return $"{label}: scene scoped asset RPC resolved to '{_receivedAssetName ?? "null"}'";

        return null;
    }

    private async UniTask<string> WaitSpawned(ScenarioContext ctx, CycleState state, int expected, string label)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => CountScopedSpawned(state) == expected,
                _syncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return $"{label}: expected {expected} spawned scene scoped identities, found {CountScopedSpawned(state)}";
        }

        return null;
    }

    private static int CountScopedSpawned(CycleState state)
    {
        var count = 0;
        var identities = FindObjectsByType<NetworkIdentity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (var i = 0; i < identities.Length; i++)
        {
            var identity = identities[i];
            if (!identity || !identity.isSpawned || identity.gameObject.scene.name != TargetSceneName)
                continue;

            var id = identity.scopedPrefabId;
            if (id.isSceneScoped && id.scope.Value == state.sceneId)
                count++;
        }

        return count;
    }

    private async UniTask<string> WaitPooledCount(ScenarioContext ctx, CycleState state, int expected, string label)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => GetPooledCount(state) == expected,
                _syncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return $"{label}: expected {expected} idle pooled pieces, found {GetPooledCount(state)}";
        }

        return null;
    }

    private static int GetPooledCount(CycleState state)
    {
        return NetworkPoolManager.TryGetScenePool(state.sceneId, out var pool)
            ? pool.GetPooledCount(state.pooledId)
            : -1;
    }

    /// <summary>
    /// The scene module drops its state as soon as the unload completes but plays the unload events,
    /// which tear down the hierarchy and its pool, on a later tick; so give teardown a moment.
    /// </summary>
    private async UniTask<string> WaitReleased(ScenarioContext ctx, CycleState state, string label)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => VerifyReleased(ctx, state) == null,
                _syncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return $"{label}: {VerifyReleased(ctx, state)}";
        }

        return null;
    }

    private static string VerifyReleased(ScenarioContext ctx, CycleState state)
    {
        if (NetworkPoolManager.TryGetScenePool(state.sceneId, out _))
            return "scene pool survived the unload";

        if (SceneRegistry<NetworkPrefabs>.TryGetEntries(state.handle, out _))
            return "scene prefab registry survived the unload";

        if (SceneRegistry<NetworkAssets>.TryGetEntries(state.handle, out _))
            return "scene asset registry survived the unload";

        if (HierarchyPool.HasPrototype(state.pooledId))
            return "cached prototype survived the unload";

        if (ctx.networkManager.prefabResolver.TryGetPrefabData(state.pooledId, out _))
            return "scene scoped prefab id still resolves after the unload";

        if (ctx.networkManager.networkAssetResolver.TryGetAsset(state.assetId, out _))
            return "scene scoped asset id still resolves after the unload";

        return null;
    }

    /// <summary>
    /// Only a player build actually unloads assets; the editor keeps everything resident, so there the
    /// check is reported as skipped rather than failed.
    /// </summary>
    private static async UniTask<(string failure, bool checkedMemory)> VerifyAssetsUnloaded(CycleState state)
    {
        state.pooledInstance = null;
        state.plainInstance = null;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Resources.UnloadUnusedAssets();

        if (Application.isEditor)
            return (null, false);

        if (IsAlive(state.pooledPrefab))
            return ("pooled prefab asset is still loaded after the scene unload", true);

        if (IsAlive(state.textAsset))
            return ("scene scoped text asset is still loaded after the scene unload", true);

        if (IsAlive(state.prefabRegistry))
            return ("scene NetworkPrefabs asset is still loaded after the scene unload", true);

        return (null, true);
    }

    private static bool IsAlive<T>(WeakReference<T> reference) where T : Object
    {
        return reference != null && reference.TryGetTarget(out var target) && (Object)target != null;
    }

    private async UniTask Cleanup(ScenarioContext ctx, int buildIndex)
    {
        var scenes = ctx.networkManager.sceneModule;

        if (IsNetworkSceneLoaded(ctx, buildIndex))
            _ = scenes.UnloadSceneAsync(TargetSceneName);
        else if (IsSceneLoaded())
            _ = SceneManager.UnloadSceneAsync(SceneManager.GetSceneByName(TargetSceneName));

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !IsSceneLoaded() && !IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
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

    private static bool IsSceneLoaded()
    {
        var scene = SceneManager.GetSceneByName(TargetSceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private static bool IsNetworkSceneLoaded(ScenarioContext ctx, int buildIndex)
    {
        return buildIndex >= 0
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.IsSceneLoaded(buildIndex);
    }
}
