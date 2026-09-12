using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

/// <summary>
/// Reproduces spawn custom-data (OnSerialize/OnDeserialize) corruption on the two deferred
/// spawn paths, which both copy the packet's customData view with BitData.Duplicate():
/// - InstantiateAsync spawns (HierarchyV2.BeginAsyncRemoteSpawn)
/// - prefabs that are not loaded yet on the receiving peer (HierarchyV2.ProcessSpawnWhenLoadedAsync)
/// The synchronous control phase passes; both deferred phases read shifted garbage.
/// </summary>
public sealed class AsyncSpawnSerializeScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 30f;
    [SerializeField] private float _ackTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 90f;

    private const int BarrierBase = 8300;

    private AsyncSpawnSerializeIdentity _syncPrefab;
    private AsyncSpawnSerializeIdentity _asyncPrefab;
    private AsyncSpawnSerializeIdentity _deferredPrefab;
    private DeferredSpawnSerializePrefabProvider _deferredProvider;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        _syncPrefab = CreatePrefab("AsyncSpawnSerializeSyncPrefab");
        _asyncPrefab = CreatePrefab("AsyncSpawnSerializeAsyncPrefab");
        _deferredPrefab = CreatePrefab("AsyncSpawnSerializeDeferredPrefab");

        manager.prefabProvider.AddRuntimePrefab(_syncPrefab.name, _syncPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_asyncPrefab.name, _asyncPrefab.gameObject);
        manager.prefabProvider.AddRuntimePrefab(_deferredPrefab.name, _deferredPrefab.gameObject);

        AsyncSpawnSerializeIdentity.ResetAll();
    }

    private static AsyncSpawnSerializeIdentity CreatePrefab(string name)
    {
        var go = new GameObject(name);
        var identity = go.AddComponent<AsyncSpawnSerializeIdentity>();
        go.SetActive(false);
        return identity;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();

        await RunPhase(ctx, failures, "sync-control", BarrierBase + 0, SpawnSync);

#if PURRNET_HAS_INSTANTIATE_ASYNC
        await RunPhase(ctx, failures, "instantiate-async", BarrierBase + 2, SpawnInstantiateAsync);
#endif

        await RunDeferredPhase(ctx, failures);

        return failures.Count == 0
            ? ScenarioResult.Ok(
                $"serialize={AsyncSpawnSerializeIdentity.SerializeCount}, " +
                $"acks={AsyncSpawnSerializeIdentity.ServerAckCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private UniTask<AsyncSpawnSerializeIdentity> SpawnSync(ScenarioContext ctx)
    {
        return UniTask.FromResult(Instantiate(_syncPrefab));
    }

#if PURRNET_HAS_INSTANTIATE_ASYNC
    private async UniTask<AsyncSpawnSerializeIdentity> SpawnInstantiateAsync(ScenarioContext ctx)
    {
        var operation = UnityEngine.Object.InstantiateAsync(_asyncPrefab);
        await UniTaskUtils.WaitWithTimeout(() => operation.isDone, _spawnTimeoutSeconds, ctx.cancellationToken);
        var results = operation.Result;
        if (results == null || results.Length != 1 || !results[0])
            throw new InvalidOperationException("InstantiateAsync returned no usable result");
        return results[0];
    }
#endif

    private async UniTask RunDeferredPhase(ScenarioContext ctx, List<string> failures)
    {
        var manager = ctx.networkManager;
        var originalProvider = manager.prefabProvider;
        bool swapped = false;

        try
        {
            // Only pure clients defer: the spawning server must see the real prefab, and a host
            // never deserializes its own spawn packets.
            if (ctx.role == NetworkRole.Client)
            {
                _deferredProvider = new DeferredSpawnSerializePrefabProvider(
                    originalProvider, _deferredPrefab.gameObject);
                _deferredProvider.Arm();
                SetPrefabProviderForTest(manager, _deferredProvider);
                swapped = true;
            }

            await RunPhase(ctx, failures, "deferred-prefab-load", BarrierBase + 4,
                c => UniTask.FromResult(Instantiate(_deferredPrefab)));

            if (swapped && !_deferredProvider.loadStarted)
                failures.Add("deferred-prefab-load: the deferred provider was never asked to load");
        }
        finally
        {
            if (swapped)
                SetPrefabProviderForTest(manager, originalProvider);
        }
    }

    private async UniTask RunPhase(
        ScenarioContext ctx,
        List<string> failures,
        string name,
        int barrierId,
        Func<ScenarioContext, UniTask<AsyncSpawnSerializeIdentity>> serverSpawn)
    {
        AsyncSpawnSerializeIdentity.ResetCycle();
        await SafeBarrier(ctx, barrierId, $"{name} armed", failures);

        AsyncSpawnSerializeIdentity serverInstance = null;
        if (ctx.isServer)
        {
            try
            {
                serverInstance = await serverSpawn(ctx);
                await UniTaskUtils.WaitWithTimeout(
                    () => serverInstance && serverInstance.isSpawned,
                    _spawnTimeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (Exception e)
            {
                failures.Add($"{name}: server spawn failed: {e.Message}");
            }
        }

        if (ctx.role == NetworkRole.Client)
            await VerifyClient(ctx, failures, name);

        if (ctx.isServer)
        {
            int expected = ExpectedDeserializers(ctx);
            if (expected > 0)
            {
                try
                {
                    await UniTaskUtils.WaitWithTimeout(
                        () => AsyncSpawnSerializeIdentity.ServerAckCount >= expected,
                        _ackTimeoutSeconds,
                        ctx.cancellationToken);
                }
                catch (TimeoutException)
                {
                    failures.Add(
                        $"{name}: client acks {AsyncSpawnSerializeIdentity.ServerAckCount}/{expected} " +
                        "(a client never ran OnDeserialize or failed to spawn)");
                }

                if (AsyncSpawnSerializeIdentity.ServerBadCount > 0)
                {
                    failures.Add(
                        $"{name}: {AsyncSpawnSerializeIdentity.ServerBadCount} client(s) read corrupted " +
                        $"custom data, e.g. magic {AsyncSpawnSerializeIdentity.LastBadMagic} != " +
                        $"{AsyncSpawnSerializeIdentity.MagicSentinel}");
                }
            }

            if (serverInstance)
                serverInstance.Despawn();
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AsyncSpawnSerializeIdentity.AliveCount == 0,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"{name}: {AsyncSpawnSerializeIdentity.AliveCount} identities survived despawn");
        }

        await SafeBarrier(ctx, barrierId + 1, $"{name} done", failures);
    }

    private async UniTask VerifyClient(ScenarioContext ctx, List<string> failures, string name)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AsyncSpawnSerializeIdentity.DeserializeCount >= 1,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"{name}: OnDeserialize never ran on the client");
            return;
        }

        // Async spawns run OnDeserialize immediately but only fire OnSpawned (which sets
        // LocalInstance) after the async ready/finish handshake, so wait for the spawn too.
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AsyncSpawnSerializeIdentity.LocalInstance,
                _spawnTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"{name}: deserialized but never spawned a local instance");
            return;
        }

        var inst = AsyncSpawnSerializeIdentity.LocalInstance;

        if (!inst.ReadOk)
        {
            failures.Add(
                $"{name}: PurrNet really dropped the ball here {inst.readMagic} " +
                $"(vector={inst.readVector})");
        }

        inst.SignalDeserialized(inst.ReadOk, inst.readMagic);
    }

    private async UniTask SafeBarrier(ScenarioContext ctx, int barrierId, string label, List<string> failures)
    {
        try
        {
            await ScenarioBarrier.Wait(ctx, barrierId, _barrierTimeoutSeconds);
        }
        catch (Exception e)
        {
            failures.Add($"barrier '{label}': {e.Message}");
        }
    }

    private static int ExpectedDeserializers(ScenarioContext ctx) =>
        ctx.role == NetworkRole.Host ? ctx.expectedConnections - 1 : ctx.expectedConnections;

    private static void SetPrefabProviderForTest(NetworkManager manager, IPrefabProvider provider)
    {
        var property = typeof(NetworkManager).GetProperty(
            nameof(NetworkManager.prefabProvider),
            BindingFlags.Instance | BindingFlags.Public);
        var setter = property?.GetSetMethod(true);
        if (setter == null)
            throw new MissingMethodException(nameof(NetworkManager), $"set_{nameof(NetworkManager.prefabProvider)}");
        setter.Invoke(manager, new object[] { provider });
    }
}

/// <summary>
/// Wraps the real provider and pretends the target prefab is not loaded until the hierarchy
/// asks for it, forcing HierarchyV2.ProcessSpawnWhenLoadedAsync (the Duplicate path).
/// </summary>
internal sealed class DeferredSpawnSerializePrefabProvider : IAsyncPrefabProvider
{
    private readonly IPrefabProvider _inner;
    private readonly int _prefabId;
    private bool _armed;

    public bool loadStarted { get; private set; }

    public DeferredSpawnSerializePrefabProvider(IPrefabProvider inner, GameObject prefab)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (!_inner.TryGetPrefabData(prefab, out var data))
            throw new InvalidOperationException("The deferred test prefab is not registered.");
        _prefabId = (int)data.prefabId;
    }

    public IEnumerable<PrefabData> allPrefabs => _inner.allPrefabs;

    public void Arm() => _armed = true;

    public bool NeedsLoad(int prefabId)
    {
        if (prefabId == _prefabId && _armed)
            return true;
        return _inner is IAsyncPrefabProvider asyncProvider && asyncProvider.NeedsLoad(prefabId);
    }

    public async Task<PrefabData> LoadPrefabAsync(int prefabId)
    {
        if (prefabId != _prefabId || !_armed)
        {
            if (_inner is IAsyncPrefabProvider asyncProvider)
                return await asyncProvider.LoadPrefabAsync(prefabId);
            return _inner.TryGetPrefabData(prefabId, out var existing) ? existing : default;
        }

        loadStarted = true;
        await Task.Delay(250);
        _armed = false;
        return _inner.TryGetPrefabData(_prefabId, out var data)
            ? data
            : throw new InvalidOperationException("The deferred test prefab registration disappeared.");
    }

    public bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
    {
        if (!_inner.TryGetPrefabData(prefabId, out prefabData))
            return false;

        if (prefabId == _prefabId && _armed)
            prefabData.prefab = null;
        return true;
    }

    public bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        => _inner.TryGetPrefabData(prefab, out prefabData);

    public void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5)
        => _inner.AddRuntimePrefab(uniqueName, prefab, pooled, warmup);

    public void Refresh()
        => _inner.Refresh();
}
