using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

#if PURRNET_HAS_INSTANTIATE_ASYNC

/// <summary>
/// End-to-end coverage for UnityEngine.Object.InstantiateAsync interception.
///
/// The scenario intentionally registers the normal and client-authoritative prefabs as pooled.
/// Every async result must nevertheless be marked non-pooled and destroyed after despawn.
/// </summary>
public sealed class AsyncInstantiateScenario : Scenario
{
    [Tooltip("Rules with client spawn authority (the PlayModeTests AnyoneCanSpawn asset).")]
    [SerializeField] private NetworkRules _clientSpawnRules;

    [Header("Load")]
    [SerializeField] private int _stressCycles = 3;
    [SerializeField] private int _stressInstancesPerCycle = 24;
    [SerializeField] private int _rapidDespawnInstances = 32;
    [SerializeField] private int _clientInstances = 4;
    [SerializeField] private int _nonNetworkInstances = 12;
    [SerializeField] private int _cancellationInstances = 256;
    [SerializeField] private int _burstChildCount = 6;

    [Header("Timeouts")]
    [SerializeField] private float _operationTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 30f;
    [SerializeField] private float _stateAndRpcTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 90f;
    [SerializeField] private float _readyTimeoutRetrySeconds = 1.5f;

    private const int BarrierBase = 7900;
    private const int ServerTokenBase = 100000;
    private const int ClientTokenBase = 200000;
    private const int OrderingToken = ServerTokenBase + 9400;
    private const int BurstParentToken = ServerTokenBase + 9450;
    private const int BurstChildTokenBase = ServerTokenBase + 9460;
    private const int MutatedToken = ServerTokenBase + 9480;
    private const string ParameterSceneName = "SceneTransferTarget";
    private const string ParameterScenePath = "Assets/PlayModeTests/SceneTransferTarget.unity";

    private static readonly string[] PendingStateFieldNames =
    {
        "_pendingSpawns",
        "_asyncPendingSpawns",
        "_pendingFinishSpawns",
        "_pendingLocalDespawnEchoes",
        "_cancelledPendingSpawns",
        "_pendingAsyncObservers",
        "_readyAsyncObservers",
        "_relayAsyncSpawns",
        "_pendingAsyncInstantiations",
        "_reservedAsyncNetworkIds",
        "_orphansWaitingForAsyncParent",
        "_toCompleteNextFrame",
        "_heldAsyncSpawnReadies"
    };

    private AsyncInstantiateProbe _serverPrefab;
    private AsyncInstantiateProbe _clientPrefab;
    private AsyncInstantiateProbe _orderingPrefab;
    private AsyncInstantiateProbe _heavyParentPrefab;
    private AsyncInstantiateProbe _burstChildPrefab;
    private AsyncInstantiateCancellationIdentity _cancellationPrefab;
    private AsyncInstantiateAwakeShapeIdentity _shapePrefab;
    private AsyncInstantiateProbe _receiverFailurePrefab;
    private AsyncInstantiateProbe _deferredSyncPrefab;
    private GameObject _nonNetworkTemplate;
    private AsyncInstantiateDeferredPrefabProvider _deferredProvider;

    private bool _shapeDiagnosticSeen;
    private bool _runClientAuthoritative;
    private bool _prepared;

    private static bool _cancellationOutcomeReceived;
    private static bool _cancellationSucceeded;
    private static string _cancellationDetail;

    private static bool _rapidDespawnOutcomeReceived;
    private static bool _rapidDespawnSucceeded;
    private static string _rapidDespawnDetail;

    private static bool _shapeOutcomeReceived;
    private static bool _shapeSucceeded;
    private static string _shapeDetail;

    private static bool _spawnerReceived;
    private static bool _hasSpawner;
    private static ulong _spawnerId;
    private static bool _hasLateReadyObserver;
    private static ulong _lateReadyObserverId;
    private static bool _testObserverSelectionReceived;
    private static bool _hasTestObserver;
    private static ulong _testObserverId;
    private static AsyncInstantiateDeferredPrefabProvider _localDeferredProvider;
    private static bool _deferredReleaseReceived;
    private static bool _mutatedShapeReceived;
    private static string _mutatedShapeExpected;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        if (CommandLineUtils.TryGetArgument("-scenario", out var scenario) &&
            scenario == nameof(AsyncInstantiateScenario))
            Prepare(manager, true);
    }

    private void Prepare(NetworkManager manager, bool allowRuleOverride)
    {
        if (_prepared)
            return;
        _prepared = true;

        CreateRuntimeTemplates();
        _deferredProvider = new AsyncInstantiateDeferredPrefabProvider(_deferredSyncPrefab.gameObject);
        _localDeferredProvider = _deferredProvider;

        AsyncInstantiateProbe.ResetAll();
        AsyncInstantiateCancellationIdentity.ResetAll();
        AsyncInstantiateAwakeShapeIdentity.ResetAll();
        AsyncInstantiateAwakeShapeMutator.ResetAll();

        if (_clientSpawnRules)
        {
            _clientPrefab.SetNetworkRules(_clientSpawnRules);

            // Client spawn validation uses manager-level rules on the server. Only replace the
            // manager rules when this is the sole requested scenario; every full-suite Setup runs
            // before connecting, so changing them there would affect unrelated scenarios.
            if (allowRuleOverride)
                manager.SetNetworkRules(_clientSpawnRules);
        }
        else
        {
            Debug.LogError(
                "[AsyncInstantiateScenario] _clientSpawnRules is missing; the client-authoritative phase will fail.");
        }

        _runClientAuthoritative = manager.networkRules &&
                                  manager.networkRules.HasSpawnAuthority(manager, false);

        // These two are deliberately configured as pooled. InstantiateAsync must override the
        // provider setting on every peer and destroy the resulting instances on despawn.
        manager.prefabProvider.AddRuntimePrefab(_serverPrefab.name, _serverPrefab.gameObject, true, 2);
        manager.prefabProvider.AddRuntimePrefab(_clientPrefab.name, _clientPrefab.gameObject, true, 2);
        manager.prefabProvider.AddRuntimePrefab(_cancellationPrefab.name, _cancellationPrefab.gameObject, false);
        manager.prefabProvider.AddRuntimePrefab(_shapePrefab.name, _shapePrefab.gameObject, false);
        manager.prefabProvider.AddRuntimePrefab(
            _receiverFailurePrefab.name,
            _receiverFailurePrefab.gameObject,
            false);
        manager.prefabProvider.AddRuntimePrefab(
            _deferredSyncPrefab.name,
            _deferredSyncPrefab.gameObject,
            false);
        manager.prefabProvider.AddRuntimePrefab(_orderingPrefab.name, _orderingPrefab.gameObject, false);
        manager.prefabProvider.AddRuntimePrefab(_heavyParentPrefab.name, _heavyParentPrefab.gameObject, false);
        manager.prefabProvider.AddRuntimePrefab(_burstChildPrefab.name, _burstChildPrefab.gameObject, false);

        _cancellationOutcomeReceived = false;
        _rapidDespawnOutcomeReceived = false;
        _shapeOutcomeReceived = false;
        _spawnerReceived = false;
        _hasLateReadyObserver = false;
        _lateReadyObserverId = 0;
        _testObserverSelectionReceived = false;
        _hasTestObserver = false;
        _testObserverId = 0;
        _deferredReleaseReceived = false;
    }

    private void CreateRuntimeTemplates()
    {
        _serverPrefab = CreateProbePrefab("AsyncInstantiateServerPooledPrefab");
        _clientPrefab = CreateProbePrefab("AsyncInstantiateClientPooledPrefab");
        _receiverFailurePrefab = CreateProbePrefab("AsyncInstantiateReceiverFailurePrefab");
        var receiverFailureChild = _receiverFailurePrefab.transform.Find("NestedNetworkIdentity");
        if (receiverFailureChild)
            receiverFailureChild.name = "ExpectedNetworkChild";
        _receiverFailurePrefab.gameObject.AddComponent<AsyncInstantiateAwakeShapeMutator>();
        _deferredSyncPrefab = CreateProbePrefab("AsyncInstantiateDeferredSyncPrefab");
        _orderingPrefab = CreateOrderingPrefab("AsyncInstantiateOrderingPrefab");
        _heavyParentPrefab = CreateHeavyParentPrefab("AsyncInstantiateHeavyParentPrefab");
        _burstChildPrefab = CreateProbePrefab("AsyncInstantiateBurstChildPrefab");

        var cancellationGo = new GameObject("AsyncInstantiateCancellationPrefab");
        _cancellationPrefab = cancellationGo.AddComponent<AsyncInstantiateCancellationIdentity>();
        // Give the worker thread meaningful clone work so allowSceneActivation can reliably
        // hold the operation before Cancel is exercised.
        for (int i = 0; i < 12; i++)
        {
            var child = new GameObject($"Payload_{i}");
            child.transform.SetParent(cancellationGo.transform, false);
        }
        cancellationGo.SetActive(false);

        var shapeGo = new GameObject("AsyncInstantiateAwakeShapePrefab");
        _shapePrefab = shapeGo.AddComponent<AsyncInstantiateAwakeShapeIdentity>();
        shapeGo.AddComponent<AsyncInstantiateAwakeShapeMutator>();
        var expectedChild = new GameObject("ExpectedNetworkChild");
        expectedChild.transform.SetParent(shapeGo.transform, false);
        expectedChild.AddComponent<NetworkIdentity>();
        var secondChild = new GameObject("SecondNetworkChild");
        secondChild.transform.SetParent(shapeGo.transform, false);
        secondChild.AddComponent<NetworkIdentity>();
        shapeGo.SetActive(false);

        _nonNetworkTemplate = new GameObject("AsyncInstantiateNonNetworkTemplate");
        _nonNetworkTemplate.SetActive(false);
    }

    private static AsyncInstantiateProbe CreateProbePrefab(string name)
    {
        var go = new GameObject(name);
        var result = go.AddComponent<AsyncInstantiateProbe>();

        // Exercise framework ordering, nested identity IDs, and parent resolution on every
        // successful async spawn instead of only testing the trivial one-component shape.
        var networkChild = new GameObject("NestedNetworkIdentity");
        networkChild.transform.SetParent(go.transform, false);
        networkChild.AddComponent<NetworkIdentity>();
        for (var i = 0; i < 12; i++)
        {
            var payload = new GameObject($"AsyncPayload_{i}");
            payload.transform.SetParent(networkChild.transform, false);
        }

        go.SetActive(false);
        return result;
    }

    /// <summary>
    /// Interleaved plain/networked children with mixed active states across two depth levels.
    /// Both the sibling order and every activeSelf flag must survive the async spawn verbatim on
    /// every peer — this is the shape reported broken by users ("children reordered, items
    /// randomly disabled" after InstantiateAsync).
    /// </summary>
    private static AsyncInstantiateProbe CreateOrderingPrefab(string name)
    {
        var go = new GameObject(name);
        var result = go.AddComponent<AsyncInstantiateProbe>();

        AddOrderingChild(go.transform, "Plain_A", true, false);
        var netA = AddOrderingChild(go.transform, "Net_A", true, true);
        AddOrderingChild(netA, "Net_A_Plain", false, false);
        AddOrderingChild(netA, "Net_A_Child", false, true);
        AddOrderingChild(go.transform, "Plain_B", false, false);
        var netB = AddOrderingChild(go.transform, "Net_B", false, true);
        AddOrderingChild(netB, "Net_B_Child", true, true);
        AddOrderingChild(go.transform, "Net_C", true, true);
        AddOrderingChild(go.transform, "Plain_C", true, false);

        go.SetActive(false);
        return result;
    }

    private static Transform AddOrderingChild(Transform parent, string name, bool active, bool networked)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        if (networked)
            child.AddComponent<NetworkIdentity>();
        child.SetActive(active);
        return child.transform;
    }

    private static AsyncInstantiateProbe CreateHeavyParentPrefab(string name)
    {
        var result = CreateProbePrefab(name);

        // Bulk payload directly under the root widens the receiver-side native-clone window so
        // the burst children's spawn packets reliably arrive while the parent is still pending.
        for (int i = 0; i < 40; i++)
        {
            var payload = new GameObject($"HeavyPayload_{i}");
            payload.transform.SetParent(result.transform, false);
        }

        return result;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();

        Prepare(ctx.networkManager, false);
        await SafeBarrier(ctx, BarrierBase - 2, "runtime prefab setup", failures);

        await RunPhase(ctx, BarrierBase - 1, "proxy overload surface", TestProxyOverloadSurface, failures);
        await RunPhase(ctx, BarrierBase + 0, "non-network passthrough", TestNonNetworkPassthrough, failures);
        await RunPhase(ctx, BarrierBase + 1, "pending observer storage", TestPendingObserverStorage, failures);
        await RunPhase(ctx, BarrierBase + 2, "default despawn echo storage",
            TestDefaultDespawnEchoStorage, failures);
        await RunServerStress(ctx, failures);
        await RunPhase(ctx, BarrierBase + 30, "despawn during remote async work", TestRapidDespawn, failures);
        await RunPhase(ctx, BarrierBase + 40, "cancellation", TestCancellation, failures);
        await SelectTestObserver(ctx, failures);
        await RunReceiverFailureIsolation(ctx, failures);
        await RunVisibilityCancellationRetry(ctx, failures);
        await RunReadyTimeoutRetry(ctx, failures);
        await RunChildDespawnDuringPending(ctx, failures);
        await RunDeferredSynchronousDespawn(ctx, failures);
        await RunClientAuthoritative(ctx, failures);
        await RunPhase(ctx, BarrierBase + 95, "InstantiateParameters scene", TestInstantiateParametersScene,
            failures);
        await RunPhase(ctx, BarrierBase + 79, "Awake shape mismatch", TestAwakeShapeMismatch, failures);
        await RunPhase(ctx, BarrierBase + 104, "child order fidelity", TestChildOrderFidelity, failures);
        await RunPhase(ctx, BarrierBase + 107, "async parent child burst", TestAsyncParentChildBurst, failures);
        await RunPhase(ctx, BarrierBase + 112, "mutated reobserve ordering", TestMutatedReobserveOrdering,
            failures);

        return failures.Count == 0
            ? ScenarioResult.Ok(
                $"server={_stressCycles}x{_stressInstancesPerCycle}, " +
                $"client={(_runClientAuthoritative ? _clientInstances.ToString() : "skipped")}, " +
                $"rapidDespawn={_rapidDespawnInstances}, cancel={_cancellationInstances}, shape=all, " +
                $"order=ok, burst={_burstChildCount}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private UniTask<string> TestProxyOverloadSurface(ScenarioContext ctx)
    {
        Func<GameObject, AsyncInstantiateOperation<GameObject>> methodGroup =
            UnityEngine.Object.InstantiateAsync;
        if (methodGroup.Method.DeclaringType != typeof(UnityProxy))
            return UniTask.FromResult<string>("InstantiateAsync method group was not retargeted to UnityProxy");

        var nativeMethods = typeof(UnityEngine.Object).GetMethods(BindingFlags.Public | BindingFlags.Static);
        var proxyMethods = typeof(UnityProxy).GetMethods(BindingFlags.Public | BindingFlags.Static);

        for (var i = 0; i < nativeMethods.Length; i++)
        {
            var native = nativeMethods[i];
            if (native.Name != "InstantiateAsync")
                continue;

            bool matched = false;
            for (var j = 0; j < proxyMethods.Length; j++)
            {
                var proxy = proxyMethods[j];
                if (proxy.Name != native.Name ||
                    proxy.GetGenericArguments().Length != native.GetGenericArguments().Length)
                    continue;

                var nativeParameters = native.GetParameters();
                var proxyParameters = proxy.GetParameters();
                if (nativeParameters.Length != proxyParameters.Length)
                    continue;

                matched = true;
                for (var parameterIndex = 0; parameterIndex < nativeParameters.Length; parameterIndex++)
                {
                    if (ReflectionTypesMatch(nativeParameters[parameterIndex].ParameterType,
                            proxyParameters[parameterIndex].ParameterType))
                        continue;
                    matched = false;
                    break;
                }

                if (matched)
                    break;
            }

            if (!matched)
                return UniTask.FromResult<string>($"Unity overload has no proxy match: {native}");
        }

        return UniTask.FromResult<string>(null);
    }

    private static bool ReflectionTypesMatch(Type left, Type right)
    {
        if (left.IsGenericParameter || right.IsGenericParameter)
            return left.IsGenericParameter && right.IsGenericParameter &&
                   left.GenericParameterPosition == right.GenericParameterPosition;
        if (left.IsArray || right.IsArray)
            return left.IsArray && right.IsArray && left.GetArrayRank() == right.GetArrayRank() &&
                   ReflectionTypesMatch(left.GetElementType(), right.GetElementType());
        if (!left.IsGenericType || !right.IsGenericType)
            return left == right;
        if (left.GetGenericTypeDefinition() != right.GetGenericTypeDefinition())
            return false;

        var leftArguments = left.GetGenericArguments();
        var rightArguments = right.GetGenericArguments();
        if (leftArguments.Length != rightArguments.Length)
            return false;
        for (var i = 0; i < leftArguments.Length; i++)
        {
            if (!ReflectionTypesMatch(leftArguments[i], rightArguments[i]))
                return false;
        }
        return true;
    }

    private async UniTask RunPhase(
        ScenarioContext ctx,
        int barrier,
        string name,
        Func<ScenarioContext, UniTask<string>> phase,
        List<string> failures)
    {
        try
        {
            var failure = await phase(ctx);
            if (!string.IsNullOrEmpty(failure))
                failures.Add($"{name}: {failure}");
        }
        catch (Exception e)
        {
            failures.Add($"{name}: {e.GetType().Name}: {e.Message}");
            Debug.LogException(e);
        }

        try
        {
            await ScenarioBarrier.Wait(ctx, barrier, _barrierTimeoutSeconds);
        }
        catch (Exception e)
        {
            failures.Add($"{name} barrier: {e.Message}");
        }
    }

    private async UniTask<string> TestNonNetworkPassthrough(ScenarioContext ctx)
    {
        var parent = new GameObject("AsyncInstantiateNonNetworkParent");
        var operation = UnityEngine.Object.InstantiateAsync(
            _nonNetworkTemplate,
            _nonNetworkInstances,
            parent.transform);

        if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
        {
            UnityProxy.DestroyDirectly(parent);
            return "native operation timed out";
        }

        var results = operation.Result;
        var failures = new List<string>();

        if (results == null || results.Length != _nonNetworkInstances)
            failures.Add($"result count was {results?.Length ?? -1}/{_nonNetworkInstances}");

        if (results != null)
        {
            for (int i = 0; i < results.Length; i++)
            {
                var result = results[i];
                if (!result)
                {
                    failures.Add($"result {i} was null");
                    continue;
                }

                if (result.transform.parent != parent.transform)
                    failures.Add($"result {i} lost its requested parent");
                if (result.GetComponentInChildren<NetworkIdentity>(true))
                    failures.Add($"result {i} unexpectedly gained a NetworkIdentity");

                UnityProxy.DestroyDirectly(result);
            }
        }

        UnityProxy.DestroyDirectly(parent);
        await UniTask.NextFrame(ctx.cancellationToken);

        return failures.Count == 0 ? null : string.Join(", ", failures);
    }

    private static UniTask<string> TestPendingObserverStorage(ScenarioContext ctx)
    {
        var testObject = new GameObject("AsyncInstantiatePendingObserverStorageTest");
        var identity = testObject.AddComponent<NetworkIdentity>();
        var player = new PlayerID(987654, false);

        try
        {
            if (identity.pendingObserverStorageAllocated)
                return UniTask.FromResult<string>("storage was allocated before first async observer");

            if (!identity.TryAddObserver(player) || identity.pendingObserverStorageAllocated)
                return UniTask.FromResult<string>("ordinary observer add allocated pending storage");

            if (!identity.TryMoveObserverToPending(player) ||
                !identity.pendingObserverStorageAllocated ||
                !identity.hasPendingObservers ||
                !identity.IsObserverOrPending(player))
                return UniTask.FromResult<string>("moving an observer to pending did not allocate valid storage");

            if (!identity.TryPromotePendingObserver(player) ||
                identity.pendingObserverStorageAllocated ||
                !identity.IsObserver(player))
                return UniTask.FromResult<string>("promotion did not release pending storage");

            if (!identity.TryMoveObserverToPending(player) ||
                !identity.TryRemovePendingObserver(player) ||
                identity.pendingObserverStorageAllocated)
                return UniTask.FromResult<string>("explicit pending removal did not release storage");

            if (!identity.TryAddObserver(player) ||
                !identity.TryMoveObserverToPending(player) ||
                !identity.TryRemoveObserver(player) ||
                identity.pendingObserverStorageAllocated)
                return UniTask.FromResult<string>("generic observer removal did not release pending storage");

            if (!identity.TryAddObserver(player) || !identity.TryMoveObserverToPending(player))
                return UniTask.FromResult<string>("failed to prepare clear-observers lifecycle check");
            identity.ClearObservers();
            if (identity.pendingObserverStorageAllocated || identity.IsObserverOrPending(player))
                return UniTask.FromResult<string>("ClearObservers did not release pending storage");

            return UniTask.FromResult<string>(null);
        }
        finally
        {
            UnityProxy.DestroyDirectly(testObject);
        }
    }

    private UniTask<string> TestDefaultDespawnEchoStorage(ScenarioContext ctx)
    {
        var field = typeof(HierarchyV2).GetField(
            "_pendingLocalDespawnEchoes",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            return UniTask.FromResult<string>("test field _pendingLocalDespawnEchoes is missing");

        if (ctx.isServer && !IsDespawnEchoStorageDisposed(ctx, field, true, out var serverDetail))
            return UniTask.FromResult<string>($"server {serverDetail}");
        if (ctx.isClient && !IsDespawnEchoStorageDisposed(ctx, field, false, out var clientDetail))
            return UniTask.FromResult<string>($"client {clientDetail}");

        return UniTask.FromResult<string>(null);
    }

    private bool IsDespawnEchoStorageDisposed(
        ScenarioContext ctx,
        FieldInfo field,
        bool asServer,
        out string detail)
    {
        if (!ctx.networkManager.TryGetModule<HierarchyFactory>(asServer, out var factory) ||
            !factory.TryGetHierarchy(gameObject.scene, out var hierarchy))
        {
            detail = "hierarchy unavailable";
            return false;
        }

        var value = field.GetValue(hierarchy);
        var isDisposed = value?.GetType().GetProperty("isDisposed")?.GetValue(value);
        detail = isDisposed is true ? null : "storage allocated before any client despawn";
        return isDisposed is true;
    }

    private async UniTask RunServerStress(ScenarioContext ctx, List<string> failures)
    {
        for (int cycle = 0; cycle < _stressCycles; cycle++)
        {
            AsyncInstantiateProbe.ResetCycle();
            AsyncInstantiateProbe[] serverResults = null;
            int tokenBase = ServerTokenBase + cycle * 1000;

            await SafeBarrier(ctx, BarrierBase + 20 + cycle,
                $"server cycle {cycle} reset", failures);

            if (ctx.isServer)
            {
                try
                {
                    // Count plus transform arguments exercises a different overload than the
                    // single shape operation and the client count-only operation.
                    var operation = UnityEngine.Object.InstantiateAsync(
                        _serverPrefab,
                        _stressInstancesPerCycle,
                        new Vector3(cycle * 10f, 0f, 0f),
                        Quaternion.identity);

                    if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                    {
                        failures.Add($"server cycle {cycle}: native operation timed out");
                    }
                    else
                    {
                        serverResults = operation.Result;
                        if (serverResults == null || serverResults.Length != _stressInstancesPerCycle)
                        {
                            failures.Add(
                                $"server cycle {cycle}: operation returned " +
                                $"{serverResults?.Length ?? -1}/{_stressInstancesPerCycle} results");
                        }

                        bool sourceSpawned = await WaitUntil(
                            () => AllResultsSpawned(serverResults, _stressInstancesPerCycle),
                            _spawnTimeoutSeconds,
                            ctx);

                        if (!sourceSpawned)
                            failures.Add($"server cycle {cycle}: source results did not become spawned");

                        if (serverResults != null)
                        {
                            for (int i = 0; i < serverResults.Length; i++)
                            {
                                var result = serverResults[i];
                                if (result && result.isSpawned)
                                    result.SetStateAndBroadcast(tokenBase + i);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    failures.Add($"server cycle {cycle}: {e.GetType().Name}: {e.Message}");
                    Debug.LogException(e);
                }
            }

            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.AllInstancesAreSpawnedAndUnpooled(_stressInstancesPerCycle),
                    _spawnTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"server cycle {cycle}: local async spawn incomplete/unpooled check failed " +
                    $"(alive={AsyncInstantiateProbe.aliveCount}, pooled={AsyncInstantiateProbe.sawPooledAsyncInstance})");
            }

            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.observerRpcTokenCount == _stressInstancesPerCycle &&
                          AsyncInstantiateProbe.AllExpectedStatesApplied(_stressInstancesPerCycle),
                    _stateAndRpcTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"server cycle {cycle}: immediate traffic incomplete " +
                    $"(rpc={AsyncInstantiateProbe.observerRpcTokenCount}/{_stressInstancesPerCycle})");
            }

            if (AsyncInstantiateProbe.stateMissingAtSpawn)
                failures.Add($"server cycle {cycle}: post-operation state was absent during remote OnSpawned");

            if (ctx.isClient && !await WaitUntil(
                    () => AsyncInstantiateProbe.targetRpcTokenCount == _stressInstancesPerCycle,
                    _stateAndRpcTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"server cycle {cycle}: target traffic incomplete " +
                    $"({AsyncInstantiateProbe.targetRpcTokenCount}/{_stressInstancesPerCycle})");
            }

            if (ctx.isServer)
            {
                int expectedObserverPairs = _stressInstancesPerCycle * ctx.expectedConnections;
                if (!await WaitUntil(
                        () => AsyncInstantiateProbe.ObserverPairCount() >= expectedObserverPairs,
                        _stateAndRpcTimeoutSeconds,
                        ctx))
                {
                    failures.Add(
                        $"server cycle {cycle}: observer confirmations incomplete " +
                        $"({AsyncInstantiateProbe.ObserverPairCount()}/{expectedObserverPairs})");
                }
            }

            await SafeBarrier(ctx, BarrierBase + 10 + cycle * 2, $"server cycle {cycle} ready", failures);

            if (!AsyncInstantiateProbe.AllPendingObserverStorageReleased())
                failures.Add($"server cycle {cycle}: pending observer storage was retained after confirmation");

            if (ctx.isServer)
            {
                var instances = AsyncInstantiateProbe.SnapshotInstances();
                for (int i = 0; i < instances.Length; i++)
                {
                    if (instances[i])
                        instances[i].Despawn();
                }
            }

            if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
                failures.Add($"server cycle {cycle}: despawn incomplete ({AsyncInstantiateProbe.aliveCount} alive)");

            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.AllDespawnedObjectsDestroyed(_stressInstancesPerCycle),
                    _despawnTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"server cycle {cycle}: async objects were retained after despawn " +
                    $"({AsyncInstantiateProbe.despawnedObjectCount}/{_stressInstancesPerCycle} tracked)");
            }

            if (AsyncInstantiateProbe.sawPooledAsyncInstance)
                failures.Add($"server cycle {cycle}: provider pooling leaked onto an async result");
            if (AsyncInstantiateProbe.reusedPooledInstance)
                failures.Add($"server cycle {cycle}: an async result was taken from PurrNet's warm pool");

            await SafeBarrier(ctx, BarrierBase + 11 + cycle * 2, $"server cycle {cycle} despawn", failures);
        }
    }

    private async UniTask<string> TestRapidDespawn(ScenarioContext ctx)
    {
        _rapidDespawnOutcomeReceived = false;
        AsyncInstantiateProbe.ResetCycle();

        // Reset the cross-process outcome before the authoritative peer starts work.
        await ScenarioBarrier.Wait(ctx, BarrierBase + 29, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            bool success = true;
            string detail = string.Empty;
            AsyncInstantiateProbe[] results = null;

            try
            {
                var operation = UnityEngine.Object.InstantiateAsync(_serverPrefab, _rapidDespawnInstances);
                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                {
                    success = false;
                    detail = "native operation timed out";
                }
                else
                {
                    results = operation.Result;
                    if (!await WaitUntil(
                            () => AllResultsSpawned(results, _rapidDespawnInstances),
                            _spawnTimeoutSeconds,
                            ctx))
                    {
                        success = false;
                        detail = "source results did not become spawned";
                    }

                    // Do not wait for observers. Spawn and despawn packets can land in the same
                    // receive drain while native remote cloning is still in flight.
                    if (results != null)
                    {
                        for (int i = 0; i < results.Length; i++)
                        {
                            if (results[i])
                                results[i].Despawn();
                        }
                    }

                    if (!await WaitUntil(
                            () => AllResultsDestroyed(results, _rapidDespawnInstances),
                            _despawnTimeoutSeconds,
                            ctx))
                    {
                        success = false;
                        detail = "source results were retained after immediate despawn";
                    }
                }
            }
            catch (Exception e)
            {
                success = false;
                detail = $"{e.GetType().Name}: {e.Message}";
            }

            BroadcastRapidDespawnOutcome(success, detail);
        }

        if (!await WaitUntil(() => _rapidDespawnOutcomeReceived, _operationTimeoutSeconds, ctx))
            return "server did not broadcast its rapid-despawn outcome";

        // Give a cancelled native operation ample opportunity to complete late. A leaked pending
        // transaction would resurrect its identity after the despawn packet was already handled.
        await UniTask.WaitForSeconds(1.5f, cancellationToken: ctx.cancellationToken);

        if (AsyncInstantiateProbe.aliveCount != 0)
            return $"{AsyncInstantiateProbe.aliveCount} identities resurrected after rapid despawn";

        return _rapidDespawnSucceeded ? null : _rapidDespawnDetail;
    }

    private async UniTask<string> TestCancellation(ScenarioContext ctx)
    {
        _cancellationOutcomeReceived = false;
        AsyncInstantiateCancellationIdentity.ResetAll();

        // Ensure every process cleared the previous static outcome before the server can
        // broadcast the new one.
        await ScenarioBarrier.Wait(ctx, BarrierBase + 39, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            bool success = true;
            string detail = string.Empty;

            try
            {
                var operation = UnityEngine.Object.InstantiateAsync(
                    _cancellationPrefab,
                    _cancellationInstances);
                operation.allowSceneActivation = false;

                bool reachedGate = await WaitUntil(
                    () => operation.IsWaitingForSceneActivation() || operation.isDone,
                    _operationTimeoutSeconds,
                    ctx);

                if (!reachedGate)
                {
                    success = false;
                    detail = "operation never reached the integration gate";
                }
                else if (operation.isDone)
                {
                    success = false;
                    detail = "operation completed before cancellation could be exercised";
                }
                else
                {
                    operation.Cancel();
                    // Unity documents cancellation itself as asynchronous and does not guarantee
                    // when isDone flips after Cancel. The contract we care about is that no result
                    // reaches Awake/network spawn and that any integrated clone is released.
                }

                await UniTask.WaitForSeconds(1f, cancellationToken: ctx.cancellationToken);

                if (AsyncInstantiateCancellationIdentity.everSpawnedCount != 0)
                {
                    success = false;
                    detail = $"{AsyncInstantiateCancellationIdentity.everSpawnedCount} cancelled identities ever spawned";
                }
                else if (AsyncInstantiateCancellationIdentity.liveCloneCount != 0)
                {
                    success = false;
                    detail = $"{AsyncInstantiateCancellationIdentity.liveCloneCount} cancelled clones leaked";
                }
            }
            catch (Exception e)
            {
                success = false;
                detail = $"{e.GetType().Name}: {e.Message}";
            }

            BroadcastCancellationOutcome(success, detail);
        }

        if (!await WaitUntil(() => _cancellationOutcomeReceived, _operationTimeoutSeconds, ctx))
            return "server did not broadcast its cancellation outcome";

        if (AsyncInstantiateCancellationIdentity.everSpawnedCount != 0)
            return $"local peer ever spawned {AsyncInstantiateCancellationIdentity.everSpawnedCount} cancelled identities";
        if (AsyncInstantiateCancellationIdentity.liveCloneCount != 0)
            return $"local peer leaked {AsyncInstantiateCancellationIdentity.liveCloneCount} cancelled clones";

        return _cancellationSucceeded ? null : _cancellationDetail;
    }

    private async UniTask SelectTestObserver(ScenarioContext ctx, List<string> failures)
    {
        _testObserverSelectionReceived = false;
        _hasTestObserver = false;
        _testObserverId = 0;

        await SafeBarrier(ctx, BarrierBase + 80, "async regression observer selection", failures);

        if (ctx.isServer)
        {
            var observer = PickClientSpawner(ctx, null, true);
            BroadcastTestObserver(
                observer.HasValue,
                observer.HasValue ? observer.Value.id.value : 0);
        }

        if (!await WaitUntil(() => _testObserverSelectionReceived, _operationTimeoutSeconds, ctx))
            failures.Add("async regression observer selection was not received");
    }

    private async UniTask RunReceiverFailureIsolation(ScenarioContext ctx, List<string> failures)
    {
        AsyncInstantiateProbe.ResetCycle();
        AsyncInstantiateAwakeShapeMutator.ResetAll();

        if (!_hasTestObserver)
        {
            await SafeBarrier(ctx, BarrierBase + 81, "receiver failure isolation skipped", failures);
            return;
        }

        bool failingPeer = IsLocalTestObserver(ctx);
        AsyncInstantiateProbe serverResult = null;
        AsyncInstantiateAwakeShapeMutator.mutation = AsyncInstantiateAwakeMutation.ReparentNetworkIdentity;
        AsyncInstantiateAwakeShapeMutator.mutationEnabled = failingPeer;
        _receiverFailurePrefab.gameObject.SetActive(true);

        try
        {
            await SafeBarrier(ctx, BarrierBase + 81, "receiver failure isolation armed", failures);

            if (ctx.isServer)
            {
                try
                {
                    var operation = UnityEngine.Object.InstantiateAsync(_receiverFailurePrefab);
                    if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                        failures.Add("receiver failure isolation: source operation timed out");
                    else if (operation.Result == null || operation.Result.Length != 1 || !operation.Result[0])
                        failures.Add("receiver failure isolation: source operation returned no usable result");
                    else
                    {
                        serverResult = operation.Result[0];
                        serverResult.SetStateAndBroadcast(ServerTokenBase + 9000);
                    }
                }
                catch (Exception e)
                {
                    failures.Add($"receiver failure isolation: {e.GetType().Name}: {e.Message}");
                    Debug.LogException(e);
                }
            }

            if (failingPeer)
            {
                if (!await WaitUntil(
                        () => AsyncInstantiateAwakeShapeMutator.mutatedCloneCount == 1,
                        _spawnTimeoutSeconds,
                        ctx))
                {
                    failures.Add(
                        "receiver failure isolation: designated receiver mutated " +
                        $"{AsyncInstantiateAwakeShapeMutator.mutatedCloneCount}/1 clones");
                }

                if (AsyncInstantiateProbe.aliveCount != 0)
                {
                    failures.Add(
                        "receiver failure isolation: local alive count was " +
                        $"{AsyncInstantiateProbe.aliveCount}/0");
                }
            }
            else
            {
                if (!await WaitUntil(
                        () => AsyncInstantiateProbe.aliveCount == 1,
                        _spawnTimeoutSeconds,
                        ctx))
                {
                    failures.Add(
                        "receiver failure isolation: local alive count was " +
                        $"{AsyncInstantiateProbe.aliveCount}/1");
                }

                if (AsyncInstantiateAwakeShapeMutator.mutatedCloneCount != 0)
                    failures.Add("receiver failure isolation: a non-designated peer mutated its clone");

                if (!await WaitUntil(
                        () => AsyncInstantiateProbe.observerRpcTokenCount == 1 &&
                              AsyncInstantiateProbe.AllExpectedStatesApplied(1),
                        _stateAndRpcTimeoutSeconds,
                        ctx))
                {
                    failures.Add("receiver failure isolation: a successful peer missed bootstrap state/RPC");
                }
            }

            if (ctx.isServer)
            {
                if (!await WaitUntil(
                        () => GetHierarchyCollectionCount(ctx, true, "_failedAsyncObserverRoots") == 1,
                        _stateAndRpcTimeoutSeconds,
                        ctx))
                {
                    failures.Add("receiver failure isolation: server did not retain exactly one failed observer");
                }

                int expectedObserverPairs = Math.Max(0, ctx.expectedConnections - 1);
                if (!await WaitUntil(
                        () => AsyncInstantiateProbe.ObserverPairCount() >= expectedObserverPairs,
                        _stateAndRpcTimeoutSeconds,
                        ctx))
                {
                    failures.Add(
                        "receiver failure isolation: successful observer confirmations were incomplete " +
                        $"({AsyncInstantiateProbe.ObserverPairCount()}/{expectedObserverPairs})");
                }
            }

            await SafeBarrier(ctx, BarrierBase + 82, "receiver failure isolated", failures);
        }
        finally
        {
            AsyncInstantiateAwakeShapeMutator.mutationEnabled = false;
            _receiverFailurePrefab.gameObject.SetActive(false);
            AsyncInstantiateAwakeShapeMutator.CleanupDetachedObjects();
        }

        if (ctx.isServer && serverResult)
            serverResult.Despawn();

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"receiver failure isolation: {AsyncInstantiateProbe.aliveCount} identities remained alive");

        await AssertNoPendingSpawnState(ctx, "receiver failure isolation", failures);
        await SafeBarrier(ctx, BarrierBase + 83, "receiver failure cleanup", failures);
    }

    private async UniTask RunVisibilityCancellationRetry(ScenarioContext ctx, List<string> failures)
    {
        AsyncInstantiateProbe.ResetCycle();

        if (!_hasTestObserver)
        {
            await SafeBarrier(ctx, BarrierBase + 84, "visibility cancellation skipped", failures);
            return;
        }

        bool hiddenPeer = IsLocalTestObserver(ctx);
        AsyncInstantiateProbe.SetHideBeforeAsyncReady(hiddenPeer);
        AsyncInstantiateProbe serverResult = null;

        await SafeBarrier(ctx, BarrierBase + 84, "visibility cancellation armed", failures);

        if (ctx.isServer)
        {
            try
            {
                var operation = UnityEngine.Object.InstantiateAsync(_serverPrefab);
                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                    failures.Add("visibility cancellation: source operation timed out");
                else if (operation.Result == null || operation.Result.Length != 1 || !operation.Result[0])
                    failures.Add("visibility cancellation: source operation returned no usable result");
                else
                {
                    serverResult = operation.Result[0];
                    serverResult.SetStateAndBroadcast(ServerTokenBase + 9100);
                }
            }
            catch (Exception e)
            {
                failures.Add($"visibility cancellation: {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }
        }

        if (hiddenPeer && !await WaitUntil(
                () => AsyncInstantiateProbe.hideBeforeReadyRequestsSent == 1 &&
                      AsyncInstantiateProbe.aliveCount == 0,
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add("visibility cancellation: designated observer was not removed before Ready");
        }

        if (ctx.isServer && !await WaitUntil(
                () => AsyncInstantiateProbe.hideBeforeReadyRequestsHandled == 1 &&
                      serverResult && !serverResult.IsObserver(FindPlayer(ctx, _testObserverId)),
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add("visibility cancellation: server did not cancel the pending observer");
        }

        if (!hiddenPeer && !await WaitUntil(
                () => AsyncInstantiateProbe.aliveCount == 1,
                _spawnTimeoutSeconds,
                ctx))
        {
            failures.Add("visibility cancellation: an unaffected peer lost the identity");
        }

        await SafeBarrier(ctx, BarrierBase + 85, "visibility cancellation delivered", failures);
        AsyncInstantiateProbe.SetHideBeforeAsyncReady(false);
        await SafeBarrier(ctx, BarrierBase + 86, "visibility retry armed", failures);

        if (ctx.isServer && serverResult)
        {
            var observer = FindPlayer(ctx, _testObserverId);
            if (!serverResult.RemoveBlacklistPlayer(observer))
            {
                failures.Add("visibility retry: designated observer was not blacklisted");
            }
            else if (!ctx.networkManager.TryGetModule<HierarchyFactory>(true, out var factory) ||
                     !factory.TryGetHierarchy(serverResult.sceneId, out var hierarchy))
            {
                failures.Add("visibility retry: server hierarchy was unavailable");
            }
            else hierarchy.EvaluateVisibility(observer, serverResult.transform);
        }

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.aliveCount == 1 &&
                      AsyncInstantiateProbe.observerRpcTokenCount == 1 &&
                      AsyncInstantiateProbe.AllExpectedStatesApplied(1),
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add("visibility retry: the observer did not receive a clean replacement spawn");
        }

        if (ctx.isServer && serverResult && !serverResult.IsObserver(FindPlayer(ctx, _testObserverId)))
            failures.Add("visibility retry: server did not restore the observer");

        await SafeBarrier(ctx, BarrierBase + 87, "visibility retry completed", failures);

        if (ctx.isServer && serverResult)
            serverResult.Despawn();

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"visibility retry: {AsyncInstantiateProbe.aliveCount} identities remained alive");

        await AssertNoPendingSpawnState(ctx, "visibility retry", failures);
    }

    /// <summary>
    /// A designated observer holds its AsyncSpawnReadyPacket past the (shrunk) server-side ready
    /// timeout. The server must cancel that observer's async transaction and resend the spawn
    /// synchronously — never tombstone the player — and the late Ready must arrive harmlessly.
    /// </summary>
    private async UniTask RunReadyTimeoutRetry(ScenarioContext ctx, List<string> failures)
    {
        AsyncInstantiateProbe.ResetCycle();

        if (!_hasTestObserver)
        {
            await SafeBarrier(ctx, BarrierBase + 96, "ready timeout retry skipped", failures);
            return;
        }

        bool slowPeer = IsLocalTestObserver(ctx);
        float originalTimeout = HierarchyV2.asyncSpawnReadyTimeoutSeconds;
        float holdSeconds = _readyTimeoutRetrySeconds * 3f;
        if (ctx.isServer)
            HierarchyV2.asyncSpawnReadyTimeoutSeconds = _readyTimeoutRetrySeconds;
        if (slowPeer)
            HierarchyV2.debugHoldAsyncSpawnReadySeconds = holdSeconds;

        AsyncInstantiateProbe serverResult = null;

        try
        {
            await SafeBarrier(ctx, BarrierBase + 96, "ready timeout retry armed", failures);

            if (ctx.isServer)
            {
                try
                {
                    var operation = UnityEngine.Object.InstantiateAsync(_serverPrefab);
                    if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                        failures.Add("ready timeout retry: source operation timed out");
                    else if (operation.Result == null || operation.Result.Length != 1 || !operation.Result[0])
                        failures.Add("ready timeout retry: source operation returned no usable result");
                    else
                    {
                        serverResult = operation.Result[0];
                        serverResult.SetStateAndBroadcast(ServerTokenBase + 9200);
                    }
                }
                catch (Exception e)
                {
                    failures.Add($"ready timeout retry: {e.GetType().Name}: {e.Message}");
                    Debug.LogException(e);
                }
            }

            // The held Ready keeps the slow peer's first copy unfinished (FinishSpawn only follows
            // promotion), so aliveCount == 1 there can only come from the synchronous resend.
            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.aliveCount == 1 &&
                          AsyncInstantiateProbe.observerRpcTokenCount >= 1 &&
                          AsyncInstantiateProbe.AllExpectedStatesApplied(1),
                    _spawnTimeoutSeconds,
                    ctx))
            {
                failures.Add(slowPeer
                    ? "ready timeout retry: the timed-out observer did not receive the synchronous replacement spawn"
                    : "ready timeout retry: a peer did not converge on the identity");
            }

            if (ctx.isServer)
            {
                var observer = FindPlayer(ctx, _testObserverId);
                if (!await WaitUntil(
                        () => serverResult && serverResult.IsObserver(observer),
                        _stateAndRpcTimeoutSeconds,
                        ctx))
                    failures.Add("ready timeout retry: server did not restore the timed-out observer");

                if (GetHierarchyCollectionCount(ctx, true, "_failedAsyncObserverRoots") != 0)
                    failures.Add("ready timeout retry: a timeout must not leave a failed-observer tombstone");
            }

            // Wait out the held Ready so its late arrival at the server (a transaction that no
            // longer exists) is proven harmless. Peer-local asserts must run BEFORE the barrier:
            // the server despawns right after it, and as barrier coordinator it resumes first —
            // its despawn packet reaches the other peers before their continuations run.
            if (slowPeer)
                await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds + 1f), true,
                    cancellationToken: ctx.cancellationToken);

            if (AsyncInstantiateProbe.aliveCount != 1)
                failures.Add($"ready timeout retry: late Ready disturbed the identity (alive={AsyncInstantiateProbe.aliveCount}/1)");

            await SafeBarrier(ctx, BarrierBase + 97, "ready timeout retry late Ready flushed", failures);

            // The slow peer only reports the barrier after waiting out the hold, so by release
            // time the late Ready has landed server-side; these are server-local, race-free.
            if (ctx.isServer)
            {
                if (serverResult && !serverResult.IsObserver(FindPlayer(ctx, _testObserverId)))
                    failures.Add("ready timeout retry: late Ready revoked the restored observer");

                if (GetHierarchyCollectionCount(ctx, true, "_failedAsyncObserverRoots") != 0)
                    failures.Add("ready timeout retry: late Ready recreated a failed-observer tombstone");
            }
        }
        finally
        {
            if (ctx.isServer)
                HierarchyV2.asyncSpawnReadyTimeoutSeconds = originalTimeout;
            if (slowPeer)
                HierarchyV2.debugHoldAsyncSpawnReadySeconds = 0f;
        }

        if (ctx.isServer && serverResult)
            serverResult.Despawn();

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"ready timeout retry: {AsyncInstantiateProbe.aliveCount} identities remained alive");

        await AssertNoPendingSpawnState(ctx, "ready timeout retry", failures);
        await SafeBarrier(ctx, BarrierBase + 98, "ready timeout retry cleanup", failures);
    }

    private async UniTask RunChildDespawnDuringPending(ScenarioContext ctx, List<string> failures)
    {
        AsyncInstantiateProbe.ResetCycle();

        AsyncInstantiateProbe serverResult = null;

        await SafeBarrier(ctx, BarrierBase + 99, "child despawn during pending armed", failures);

        if (ctx.isServer)
        {
            try
            {
                var operation = UnityEngine.Object.InstantiateAsync(_serverPrefab);
                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                    failures.Add("child despawn during pending: source operation timed out");
                else if (operation.Result == null || operation.Result.Length != 1 || !operation.Result[0])
                    failures.Add("child despawn during pending: source operation returned no usable result");
                else
                {
                    serverResult = operation.Result[0];
                    serverResult.SetStateAndBroadcast(ServerTokenBase + 9300);

                    var child = serverResult.transform.Find("NestedNetworkIdentity");
                    if (child)
                        UnityEngine.Object.Destroy(child.gameObject);
                    else
                        failures.Add("child despawn during pending: nested network child not found on source");
                }
            }
            catch (Exception e)
            {
                failures.Add($"child despawn during pending: {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }
        }

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.aliveCount == 1 &&
                      AsyncInstantiateProbe.observerRpcTokenCount >= 1 &&
                      AsyncInstantiateProbe.AllExpectedStatesApplied(1),
                _spawnTimeoutSeconds,
                ctx))
        {
            failures.Add(
                "child despawn during pending: a peer was left without the root after its transaction " +
                $"was cancelled over the destroyed child (alive={AsyncInstantiateProbe.aliveCount}/1)");
        }

        if (!await WaitUntil(NoLiveInstanceHasNestedChild, _despawnTimeoutSeconds, ctx))
            failures.Add("child despawn during pending: the destroyed child survived on a peer");

        if (ctx.isServer && GetHierarchyCollectionCount(ctx, true, "_failedAsyncObserverRoots") != 0)
            failures.Add("child despawn during pending: collateral cancel left a failed-observer tombstone");

        await SafeBarrier(ctx, BarrierBase + 100, "child despawn during pending delivered", failures);

        if (ctx.isServer && serverResult)
            serverResult.Despawn();

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"child despawn during pending: {AsyncInstantiateProbe.aliveCount} identities remained alive");

        await AssertNoPendingSpawnState(ctx, "child despawn during pending", failures);
        await SafeBarrier(ctx, BarrierBase + 101, "child despawn during pending cleanup", failures);
    }

    private static bool NoLiveInstanceHasNestedChild()
    {
        var instances = AsyncInstantiateProbe.SnapshotInstances();
        if (instances.Length == 0)
            return false;

        for (var i = 0; i < instances.Length; i++)
        {
            var instance = instances[i];
            if (instance && instance.transform.Find("NestedNetworkIdentity"))
                return false;
        }

        return true;
    }

    private async UniTask RunDeferredSynchronousDespawn(ScenarioContext ctx, List<string> failures)
    {
        AsyncInstantiateProbe.ResetCycle();
        _deferredReleaseReceived = false;

        if (!_hasTestObserver)
        {
            await SafeBarrier(ctx, BarrierBase + 88, "deferred synchronous spawn skipped", failures);
            return;
        }

        var originalProvider = ctx.networkManager.prefabProvider;
        _deferredProvider.Attach(originalProvider);
        SetPrefabProviderForTest(ctx.networkManager, _deferredProvider);
        try
        {
            bool delayedPeer = IsLocalTestObserver(ctx);
            if (delayedPeer)
                _deferredProvider.Arm();

            await SafeBarrier(ctx, BarrierBase + 88, "deferred synchronous spawn armed", failures);

            if (ctx.isServer)
            {
                try
                {
                    var result = UnityEngine.Object.Instantiate(_deferredSyncPrefab);
                    if (!result || !result.isSpawned)
                        failures.Add("deferred synchronous spawn: source identity did not spawn synchronously");
                    else result.Despawn();
                }
                catch (Exception e)
                {
                    failures.Add($"deferred synchronous spawn: {e.GetType().Name}: {e.Message}");
                    Debug.LogException(e);
                }
            }

            if (delayedPeer && !await WaitUntil(
                    () => _deferredProvider.loadStarted,
                    _stateAndRpcTimeoutSeconds,
                    ctx))
            {
                failures.Add("deferred synchronous spawn: delayed provider was never requested");
            }

            await SafeBarrier(ctx, BarrierBase + 89, "deferred synchronous despawn buffered", failures);

            if (ctx.isServer)
            {
                BroadcastDeferredProviderRelease();
                ctx.networkManager.FlushBatchedRPCs();
            }

            if (!await WaitUntil(() => _deferredReleaseReceived, _operationTimeoutSeconds, ctx))
                failures.Add("deferred synchronous spawn: release command was not received");

            if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            {
                failures.Add(
                    $"deferred synchronous spawn: {AsyncInstantiateProbe.aliveCount} identities survived " +
                    "the buffered Despawn");
            }

            await AssertNoPendingSpawnState(ctx, "deferred synchronous spawn", failures);
            await SafeBarrier(ctx, BarrierBase + 90, "deferred synchronous spawn cleanup", failures);
        }
        finally
        {
            _deferredProvider.Reset();
            SetPrefabProviderForTest(ctx.networkManager, originalProvider);
        }
    }

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

    private bool IsLocalTestObserver(ScenarioContext ctx)
    {
        return ctx.role == NetworkRole.Client &&
               ctx.networkManager.isLocalPlayerReady &&
               ctx.networkManager.localPlayer.id.value == _testObserverId;
    }

    private static PlayerID FindPlayer(ScenarioContext ctx, ulong id)
    {
        var players = ctx.networkManager.players;
        for (var i = 0; i < players.Count; i++)
        {
            if (players[i].id.value == id)
                return players[i];
        }
        return default;
    }

    private int GetHierarchyCollectionCount(ScenarioContext ctx, bool asServer, string fieldName)
    {
        if (!ctx.networkManager.TryGetModule<HierarchyFactory>(asServer, out var factory) ||
            !factory.TryGetHierarchy(gameObject.scene, out var hierarchy))
            return -1;

        var field = typeof(HierarchyV2).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        var value = field?.GetValue(hierarchy);
        return value?.GetType().GetProperty("Count")?.GetValue(value) is int count ? count : -1;
    }

    private async UniTask RunClientLateFinish(
        ScenarioContext ctx,
        bool localSpawner,
        List<string> failures)
    {
        AsyncInstantiateProbe.ResetCycle();

        await SafeBarrier(ctx, BarrierBase + 53, "client late Finish armed", failures);

        AsyncInstantiateProbe localResult = null;
        if (localSpawner)
        {
            HierarchyFactory clientFactory = null;
            Action<SceneID> despawnBeforeFinish = null;
            AsyncInstantiateOperation<AsyncInstantiateProbe> operation = null;
            bool despawnTriggered = false;

            try
            {
                if (!ctx.networkManager.TryGetModule<HierarchyFactory>(false, out clientFactory))
                {
                    failures.Add("client late Finish: client HierarchyFactory was unavailable");
                }
                else
                {
                    // PostNetworkMessages invokes this hook immediately before it sends queued
                    // FinishSpawnPackets. Despawn therefore enters the same ReliableOrdered stream
                    // first, without delaying, intercepting, or reordering either packet.
                    despawnBeforeFinish = sceneId =>
                    {
                        if (despawnTriggered || operation == null || !operation.isDone)
                            return;

                        var results = operation.Result;
                        if (results == null || results.Length != 1 || !results[0] || sceneId != results[0].sceneId)
                            return;

                        localResult = results[0];
                        despawnTriggered = true;
                        localResult.Despawn();
                    };
                    clientFactory.onPreFinishSpawn += despawnBeforeFinish;

                    operation = UnityEngine.Object.InstantiateAsync(_clientPrefab);
                    if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                        failures.Add("client late Finish: native operation timed out");
                    else if (!await WaitUntil(() => despawnTriggered, _operationTimeoutSeconds, ctx))
                        failures.Add("client late Finish: pre-Finish despawn hook did not run");
                    else if (!await WaitUntil(() => !localResult, _despawnTimeoutSeconds, ctx))
                        failures.Add("client late Finish: source result was retained after despawn");
                }
            }
            catch (Exception e)
            {
                failures.Add($"client late Finish: {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                if (clientFactory != null && despawnBeforeFinish != null)
                    clientFactory.onPreFinishSpawn -= despawnBeforeFinish;
            }
        }

        // The source reaches this barrier only after queuing Despawn and the subsequent Finish.
        // Its barrier report is another ReliableOrdered fence, so the server has consumed both
        // packets before it releases the phase.
        await SafeBarrier(ctx, BarrierBase + 54, "client late Finish delivered", failures);

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"client late Finish: {AsyncInstantiateProbe.aliveCount} identities remained alive");

        await AssertNoPendingSpawnState(ctx, "client late Finish", failures);
    }

    private async UniTask RunClientLateReady(
        ScenarioContext ctx,
        bool localSpawner,
        List<string> failures)
    {
        AsyncInstantiateProbe.ResetCycle();

        if (!_hasLateReadyObserver)
        {
            // A one-client local smoke run has no non-spawner that can produce observer Ready.
            await SafeBarrier(ctx, BarrierBase + 55, "client late Ready skipped", failures);
            return;
        }

        bool requestOnThisPeer = ctx.role == NetworkRole.Client &&
                                 ctx.networkManager.isLocalPlayerReady &&
                                 ctx.networkManager.localPlayer.id.value == _lateReadyObserverId;
        AsyncInstantiateProbe.SetDespawnBeforeAsyncReady(requestOnThisPeer);

        await SafeBarrier(ctx, BarrierBase + 55, "client late Ready armed", failures);

        AsyncInstantiateProbe localResult = null;
        if (localSpawner)
        {
            try
            {
                localResult = await InstantiateOneClientProbe(
                    ctx,
                    "client late Ready",
                    failures);
            }
            catch (Exception e)
            {
                failures.Add($"client late Ready: {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }
        }

        if (requestOnThisPeer && !await WaitUntil(
                () => AsyncInstantiateProbe.lateReadyDespawnRequestsSent > 0,
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add("client late Ready: non-spawner did not request despawn from OnSpawnReceived");
        }

        if (ctx.isServer && !await WaitUntil(
                () => AsyncInstantiateProbe.lateReadyDespawnRequestsHandled > 0,
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add("client late Ready: server did not handle the pre-Ready despawn request");
        }

        if (localSpawner && localResult && !await WaitUntil(
                () => !localResult,
                _despawnTimeoutSeconds,
                ctx))
        {
            failures.Add("client late Ready: source result was retained after observer-requested despawn");
        }

        // On each non-spawner, the request is flushed before OnSpawnReceived returns and before
        // AsyncSpawnReadyPacket is sent. Its later barrier report fences both packets on the same
        // ReliableOrdered stream. The server enters only after it has actually handled deletion.
        await SafeBarrier(ctx, BarrierBase + 56, "client late Ready delivered", failures);
        AsyncInstantiateProbe.SetDespawnBeforeAsyncReady(false);

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"client late Ready: {AsyncInstantiateProbe.aliveCount} identities remained alive");

        await AssertNoPendingSpawnState(ctx, "client late Ready", failures);
    }

    private async UniTask RunClientAuthoritative(ScenarioContext ctx, List<string> failures)
    {
        if (!_runClientAuthoritative)
            return;

        AsyncInstantiateProbe.ResetCycle();
        _spawnerReceived = false;
        _hasSpawner = false;
        _spawnerId = 0;

        await SafeBarrier(ctx, BarrierBase + 50, "client spawn selection", failures);

        if (ctx.isServer)
        {
            var spawner = PickClientSpawner(ctx);
            var lateReadyObserver = spawner.HasValue
                ? PickClientSpawner(ctx, spawner.Value, true)
                : (PlayerID?)null;
            BroadcastSpawner(
                spawner.HasValue,
                spawner.HasValue ? spawner.Value.id.value : 0,
                lateReadyObserver.HasValue,
                lateReadyObserver.HasValue ? lateReadyObserver.Value.id.value : 0);
        }

        if (!await WaitUntil(() => _spawnerReceived, _operationTimeoutSeconds, ctx))
        {
            failures.Add("client spawn: spawner selection was not received");
            await SafeBarrier(ctx, BarrierBase + 51, "client spawn failed selection", failures);
            return;
        }

        if (!_hasSpawner)
        {
            // Dedicated server runs with zero clients are valid for local smoke runs, but cannot
            // exercise client authority. Every normal multi-peer run takes the branch below.
            await SafeBarrier(ctx, BarrierBase + 51, "client spawn skipped", failures);
            return;
        }

        AsyncInstantiateProbe[] localResults = null;
        bool localSpawner = ctx.networkManager.isLocalPlayerReady &&
                            ctx.networkManager.localPlayer.id.value == _spawnerId;

        await RunClientLateFinish(ctx, localSpawner, failures);
        await RunClientLateReady(ctx, localSpawner, failures);
        AsyncInstantiateProbe.ResetCycle();

        if (localSpawner)
        {
            try
            {
                var operation = UnityEngine.Object.InstantiateAsync(_clientPrefab, _clientInstances);
                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                {
                    failures.Add("client spawn: native operation timed out on the designated spawner");
                }
                else
                {
                    localResults = operation.Result;
                    if (localResults == null || localResults.Length != _clientInstances)
                        failures.Add($"client spawn: operation returned {localResults?.Length ?? -1}/{_clientInstances}");

                    if (!await WaitUntil(
                            () => AllResultsSpawned(localResults, _clientInstances),
                            _spawnTimeoutSeconds,
                            ctx))
                    {
                        failures.Add("client spawn: designated spawner results did not become spawned");
                    }

                    if (localResults != null)
                    {
                        for (int i = 0; i < localResults.Length; i++)
                        {
                            var result = localResults[i];
                            if (result && result.isSpawned)
                                result.SetStateAndSignalServer(ClientTokenBase + i);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                failures.Add($"client spawn: {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }
        }

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.AllInstancesAreSpawnedAndUnpooled(_clientInstances),
                _spawnTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: local spawn incomplete/unpooled check failed " +
                $"(alive={AsyncInstantiateProbe.aliveCount}, pooled={AsyncInstantiateProbe.sawPooledAsyncInstance})");
        }

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.AllInstancesOwnedBy(_spawnerId),
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add($"client spawn: not every result became owned by spawner {_spawnerId}");
        }

        if (!await WaitUntil(
                () => (ctx.isServer || localSpawner
                          ? AsyncInstantiateProbe.clientEchoTokenCount == _clientInstances
                          : AsyncInstantiateProbe.observerRpcTokenCount == _clientInstances) &&
                      AsyncInstantiateProbe.AllExpectedStatesApplied(_clientInstances),
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: bootstrap traffic/state incomplete " +
                $"(echo={AsyncInstantiateProbe.clientEchoTokenCount}, " +
                $"observer={AsyncInstantiateProbe.observerRpcTokenCount}, expected={_clientInstances})");
        }

        if (AsyncInstantiateProbe.stateMissingAtSpawn)
            failures.Add("client spawn: post-operation state was absent during remote OnSpawned");

        if (ctx.isClient && !await WaitUntil(
                () => AsyncInstantiateProbe.targetRpcTokenCount == _clientInstances,
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: target traffic incomplete " +
                $"({AsyncInstantiateProbe.targetRpcTokenCount}/{_clientInstances})");
        }


        if ((ctx.role == NetworkRole.Server || localSpawner) && !await WaitUntil(
                () => AsyncInstantiateProbe.forwardedRpcTokenCount == _clientInstances,
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: forwarded observer traffic incomplete " +
                $"({AsyncInstantiateProbe.forwardedRpcTokenCount}/{_clientInstances})");
        }

        if (ctx.isServer)
        {
            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.serverRpcTokenCount == _clientInstances,
                    _stateAndRpcTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"client spawn: server received {AsyncInstantiateProbe.serverRpcTokenCount}/{_clientInstances} " +
                    "immediate ServerRpcs");
            }

            int expectedObserverPairs = _clientInstances * ctx.expectedConnections;
            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.ObserverPairCount() >= expectedObserverPairs,
                    _stateAndRpcTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"client spawn: observer confirmations incomplete " +
                    $"({AsyncInstantiateProbe.ObserverPairCount()}/{expectedObserverPairs})");
            }
        }

        await SafeBarrier(ctx, BarrierBase + 51, "client spawn ready", failures);

        if (!AsyncInstantiateProbe.AllPendingObserverStorageReleased())
            failures.Add("client spawn: pending observer storage was retained after confirmation");

        if (ctx.isServer)
        {
            var instances = AsyncInstantiateProbe.SnapshotInstances();
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i])
                    instances[i].Despawn();
            }
        }

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"client spawn: despawn incomplete ({AsyncInstantiateProbe.aliveCount} alive)");

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.AllDespawnedObjectsDestroyed(_clientInstances),
                _despawnTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"client spawn: async objects were retained after despawn " +
                $"({AsyncInstantiateProbe.despawnedObjectCount}/{_clientInstances} tracked)");
        }


        if (AsyncInstantiateProbe.sawPooledAsyncInstance)
            failures.Add("client spawn: provider pooling leaked onto an async result");
        if (AsyncInstantiateProbe.reusedPooledInstance)
            failures.Add("client spawn: an async result was taken from PurrNet's warm pool");

        await SafeBarrier(ctx, BarrierBase + 52, "client spawn despawn", failures);
    }

    private async UniTask<string> TestInstantiateParametersScene(ScenarioContext ctx)
    {
        AsyncInstantiateProbe.ResetCycle();
        int buildIndex = SceneUtility.GetBuildIndexByScenePath(ParameterScenePath);
        if (buildIndex < 0)
            return $"target scene is missing from build settings: {ParameterScenePath}";

        AsyncOperation loadOperation = null;
        if (ctx.isServer)
        {
            loadOperation = ctx.networkManager.sceneModule.LoadSceneAsync(
                ParameterSceneName,
                new PurrSceneSettings
                {
                    mode = LoadSceneMode.Additive,
                    physicsMode = LocalPhysicsMode.None,
                    isPublic = true
                });
            if (loadOperation == null)
                return "network scene load returned null";
        }

        if (!await WaitUntil(
                () => TryGetParameterScene(ctx, buildIndex, out _),
                _operationTimeoutSeconds,
                ctx))
            return "target scene did not load and register on this peer";

        await ScenarioBarrier.Wait(ctx, BarrierBase + 92, _barrierTimeoutSeconds);

        AsyncInstantiateProbe serverResult = null;
        if (ctx.isServer)
        {
            if (!TryGetParameterScene(ctx, buildIndex, out var targetScene))
                return "server target scene disappeared before InstantiateAsync";

            var parameters = new InstantiateParameters { scene = targetScene };
            var operation = UnityEngine.Object.InstantiateAsync(_serverPrefab, parameters);
            if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                return "native operation timed out";
            if (operation.Result == null || operation.Result.Length != 1 || !operation.Result[0])
                return "native operation returned no usable result";

            serverResult = operation.Result[0];
            if (serverResult.gameObject.scene != targetScene)
                return $"source result landed in {serverResult.gameObject.scene.name}, expected {ParameterSceneName}";
            serverResult.SetStateAndBroadcast(ServerTokenBase + 9200);
        }

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.aliveCount == 1 &&
                      AsyncInstantiateProbe.AllInstancesInScene(ParameterSceneName) &&
                      AsyncInstantiateProbe.observerRpcTokenCount == 1 &&
                      AsyncInstantiateProbe.AllExpectedStatesApplied(1),
                _stateAndRpcTimeoutSeconds,
                ctx))
        {
            return
                $"scene-targeted spawn incomplete (alive={AsyncInstantiateProbe.aliveCount}, " +
                $"rpc={AsyncInstantiateProbe.observerRpcTokenCount}, " +
                $"inScene={AsyncInstantiateProbe.AllInstancesInScene(ParameterSceneName)})";
        }

        await ScenarioBarrier.Wait(ctx, BarrierBase + 93, _barrierTimeoutSeconds);

        if (ctx.isServer && serverResult)
            serverResult.Despawn();
        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            return $"{AsyncInstantiateProbe.aliveCount} scene-targeted identities remained alive";

        await ScenarioBarrier.Wait(ctx, BarrierBase + 94, _barrierTimeoutSeconds);

        if (ctx.isServer && TryGetParameterScene(ctx, buildIndex, out var sceneToUnload))
            _ = ctx.networkManager.sceneModule.UnloadSceneAsync(sceneToUnload);

        if (!await WaitUntil(
                () => !TryGetParameterScene(ctx, buildIndex, out _),
                _operationTimeoutSeconds,
                ctx))
            return "target scene did not unload on this peer";

        return null;
    }

    private bool TryGetParameterScene(ScenarioContext ctx, int buildIndex, out Scene scene)
    {
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var candidate = SceneManager.GetSceneAt(i);
            if (!candidate.isLoaded || candidate.buildIndex != buildIndex ||
                !ctx.networkManager.sceneModule.TryGetSceneID(candidate, out _))
                continue;

            scene = candidate;
            return true;
        }

        scene = default;
        return false;
    }

    private async UniTask<string> TestAwakeShapeMismatch(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var mutations = new[]
        {
            AsyncInstantiateAwakeMutation.AddNetworkIdentity,
            AsyncInstantiateAwakeMutation.ReparentNetworkIdentity,
            AsyncInstantiateAwakeMutation.RemoveNetworkIdentity,
            AsyncInstantiateAwakeMutation.SwapNetworkSiblings
        };

        for (var i = 0; i < mutations.Length; i++)
        {
            var failure = await TestAwakeShapeMismatch(ctx, mutations[i], i);
            if (!string.IsNullOrEmpty(failure))
                failures.Add($"{mutations[i]}: {failure}");
        }

        return failures.Count == 0 ? null : string.Join(", ", failures);
    }

    private async UniTask<string> TestAwakeShapeMismatch(
        ScenarioContext ctx,
        AsyncInstantiateAwakeMutation mutation,
        int iteration)
    {
        _shapeOutcomeReceived = false;
        AsyncInstantiateAwakeShapeIdentity.ResetAll();
        AsyncInstantiateAwakeShapeMutator.ResetAll();

        await ScenarioBarrier.Wait(ctx, BarrierBase + 70 + iteration * 2, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            bool success = true;
            var details = new List<string>();
            AsyncInstantiateAwakeShapeIdentity result = null;

            _shapeDiagnosticSeen = false;
            Application.logMessageReceived += CaptureShapeDiagnostic;

            try
            {
                AsyncInstantiateAwakeShapeMutator.mutation = mutation;
                AsyncInstantiateAwakeShapeMutator.mutationEnabled = true;

                // The template is only active for this call. The mutator ignores the template
                // itself and changes the clone in Awake during native async integration.
                _shapePrefab.gameObject.SetActive(true);
                var operation = UnityEngine.Object.InstantiateAsync(_shapePrefab);

                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                {
                    success = false;
                    details.Add("native operation timed out");
                }
                else if (operation.Result == null || operation.Result.Length != 1 || !operation.Result[0])
                {
                    success = false;
                    details.Add("native operation did not preserve its local result");
                }
                else
                {
                    result = operation.Result[0];
                    await UniTask.NextFrame(ctx.cancellationToken);

                    if (AsyncInstantiateAwakeShapeMutator.mutatedCloneCount != 1)
                    {
                        success = false;
                        details.Add(
                            $"Awake mutation ran {AsyncInstantiateAwakeShapeMutator.mutatedCloneCount} times");
                    }

                    if (!AwakeMutationTookEffect(result, mutation))
                    {
                        success = false;
                        details.Add("clone topology did not change");
                    }

                    if (result.isSpawned || AsyncInstantiateAwakeShapeIdentity.spawnedCount != 0)
                    {
                        success = false;
                        details.Add("shape-mismatched clone was network-spawned");
                    }

                    var resultIdentities = result.GetComponentsInChildren<NetworkIdentity>(true);
                    for (var i = 0; i < resultIdentities.Length; i++)
                    {
                        if (!resultIdentities[i].isSpawned)
                            continue;
                        success = false;
                        details.Add("a mutated NetworkIdentity was network-spawned");
                        break;
                    }
                    if (AsyncInstantiateAwakeShapeMutator.anyMutatedIdentitySpawned)
                    {
                        success = false;
                        details.Add("a detached Awake-mutated identity was network-spawned");
                    }

                    // Diagnostics are deliberately reported once per prefab. The first mutation
                    // verifies the user-facing error; the remaining mutations verify rejection.
                    if (iteration == 0 && !await WaitUntil(() => _shapeDiagnosticSeen, 5f, ctx))
                    {
                        success = false;
                        details.Add("no diagnostic named InstantiateAsync and the prefab");
                    }
                }
            }
            catch (Exception e)
            {
                success = false;
                details.Add($"{e.GetType().Name}: {e.Message}");
            }
            finally
            {
                _shapePrefab.gameObject.SetActive(false);
                AsyncInstantiateAwakeShapeMutator.mutationEnabled = false;
                Application.logMessageReceived -= CaptureShapeDiagnostic;

                if (result)
                {
                    if (result.isSpawned)
                        result.Despawn();
                    else
                        UnityProxy.DestroyDirectly(result.gameObject);
                }
                AsyncInstantiateAwakeShapeMutator.CleanupDetachedObjects();
            }

            BroadcastShapeOutcome(success, string.Join(", ", details));
        }

        if (!await WaitUntil(() => _shapeOutcomeReceived, _operationTimeoutSeconds, ctx))
            return "server did not broadcast its shape-validation outcome";

        await UniTask.WaitForSeconds(0.5f, cancellationToken: ctx.cancellationToken);

        if (AsyncInstantiateAwakeShapeIdentity.spawnedCount != 0)
            return $"local peer spawned {AsyncInstantiateAwakeShapeIdentity.spawnedCount} shape-mismatched identities";

        await ScenarioBarrier.Wait(ctx, BarrierBase + 71 + iteration * 2, _barrierTimeoutSeconds);
        return _shapeSucceeded ? null : _shapeDetail;
    }

    /// <summary>The shape template holds root + ExpectedNetworkChild + SecondNetworkChild.</summary>
    private static bool AwakeMutationTookEffect(
        AsyncInstantiateAwakeShapeIdentity result,
        AsyncInstantiateAwakeMutation mutation)
    {
        int identityCount = result.GetComponentsInChildren<NetworkIdentity>(true).Length;
        switch (mutation)
        {
            case AsyncInstantiateAwakeMutation.AddNetworkIdentity:
                return identityCount == 4;
            case AsyncInstantiateAwakeMutation.ReparentNetworkIdentity:
            case AsyncInstantiateAwakeMutation.RemoveNetworkIdentity:
                return identityCount == 2;
            case AsyncInstantiateAwakeMutation.SwapNetworkSiblings:
                return identityCount == 3 &&
                       result.transform.childCount >= 2 &&
                       result.transform.GetChild(0).name == "SecondNetworkChild";
            default:
                return false;
        }
    }

    private void CaptureShapeDiagnostic(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;

        if (condition.IndexOf("InstantiateAsync", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        if (condition.IndexOf(_shapePrefab.name, StringComparison.OrdinalIgnoreCase) < 0)
            return;

        _shapeDiagnosticSeen = true;
    }

    /// <summary>
    /// The async result's children must land on every peer with the template's exact sibling
    /// order and activeSelf pattern — verified both at OnSpawned time (the user callback where
    /// stale order/active state would be observed) and at steady state.
    /// </summary>
    private async UniTask<string> TestChildOrderFidelity(ScenarioContext ctx)
    {
        AsyncInstantiateProbe.ResetCycle();
        AsyncInstantiateProbe.SetCaptureHierarchyOnSpawn(true);
        string expected = AsyncInstantiateProbe.DescribeChildren(_orderingPrefab.transform);

        try
        {
            await ScenarioBarrier.Wait(ctx, BarrierBase + 102, _barrierTimeoutSeconds);

            AsyncInstantiateProbe serverResult = null;
            if (ctx.isServer)
            {
                var operation = UnityEngine.Object.InstantiateAsync(_orderingPrefab);
                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                    return "native operation timed out";
                if (operation.Result == null || operation.Result.Length != 1 || !operation.Result[0])
                    return "native operation returned no usable result";

                serverResult = operation.Result[0];
                var sourceShape = AsyncInstantiateProbe.DescribeChildren(serverResult.transform);
                if (sourceShape != expected)
                    return $"source clone diverged from the template: {sourceShape} != {expected}";
                serverResult.SetStateAndBroadcast(OrderingToken);
            }

            var failures = new List<string>();

            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.aliveCount == 1 &&
                          AsyncInstantiateProbe.observerRpcTokenCount == 1 &&
                          AsyncInstantiateProbe.AllExpectedStatesApplied(1),
                    _spawnTimeoutSeconds,
                    ctx))
            {
                failures.Add($"spawn incomplete (alive={AsyncInstantiateProbe.aliveCount})");
            }

            var instances = AsyncInstantiateProbe.SnapshotInstances();
            for (var i = 0; i < instances.Length; i++)
            {
                var instance = instances[i];
                if (!instance)
                    continue;

                if (!instance.gameObject.activeInHierarchy)
                    failures.Add("spawned root was left disabled");

                var shape = AsyncInstantiateProbe.DescribeChildren(instance.transform);
                if (shape != expected)
                    failures.Add($"steady-state child order/active mismatch: [{shape}] != [{expected}]");
            }

            var snapshots = AsyncInstantiateProbe.hierarchySnapshots;
            if (snapshots.Count == 0)
                failures.Add("no OnSpawned hierarchy snapshot was captured");
            for (var i = 0; i < snapshots.Count; i++)
            {
                if (snapshots[i] != expected)
                    failures.Add($"OnSpawned-time child order/active mismatch: [{snapshots[i]}] != [{expected}]");
            }

            await SafeBarrier(ctx, BarrierBase + 103, "child order fidelity asserted", failures);

            if (ctx.isServer && serverResult)
                serverResult.Despawn();

            if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
                failures.Add($"{AsyncInstantiateProbe.aliveCount} identities remained alive");

            await AssertNoPendingSpawnState(ctx, "child order fidelity", failures);
            return failures.Count == 0 ? null : string.Join(", ", failures);
        }
        finally
        {
            AsyncInstantiateProbe.SetCaptureHierarchyOnSpawn(false);
        }
    }

    /// <summary>
    /// Children spawned synchronously under a parent whose remote native clone is still in
    /// flight. Instantiating them inside the parent operation's completion callback puts their
    /// spawn packets in the same batch as the parent's, so receiving peers must hold them until
    /// the parent's async instantiation completes — and then attach every child under the parent
    /// in the server's order, fully activated.
    /// </summary>
    private async UniTask<string> TestAsyncParentChildBurst(ScenarioContext ctx)
    {
        AsyncInstantiateProbe.ResetCycle();

        await ScenarioBarrier.Wait(ctx, BarrierBase + 105, _barrierTimeoutSeconds);

        AsyncInstantiateProbe parent = null;
        var burstChildren = new List<AsyncInstantiateProbe>();
        Exception burstError = null;

        if (ctx.isServer)
        {
            var operation = UnityEngine.Object.InstantiateAsync(_heavyParentPrefab);
            // UnityProxy registered its own completion handler during the call above, so by the
            // time this callback runs the parent is already network-spawned.
            operation.completed += _ =>
            {
                try
                {
                    var results = operation.Result;
                    if (results == null || results.Length != 1 || !results[0])
                        return;

                    parent = results[0];
                    parent.SetStateAndBroadcast(BurstParentToken);
                    for (int i = 0; i < _burstChildCount; i++)
                    {
                        var child = UnityEngine.Object.Instantiate(_burstChildPrefab, parent.transform);
                        child.SetStateAndBroadcast(BurstChildTokenBase + i);
                        burstChildren.Add(child);
                    }
                }
                catch (Exception e)
                {
                    burstError = e;
                }
            };

            if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                return "native operation timed out";
            if (burstError != null)
                return $"burst instantiation failed: {burstError.GetType().Name}: {burstError.Message}";
            if (!parent)
                return "native operation returned no usable result";
            if (burstChildren.Count != _burstChildCount)
                return $"only {burstChildren.Count}/{_burstChildCount} children were instantiated";
        }

        var failures = new List<string>();
        int expectedAlive = 1 + _burstChildCount;

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.aliveCount == expectedAlive &&
                      AsyncInstantiateProbe.AllExpectedStatesApplied(expectedAlive),
                _spawnTimeoutSeconds,
                ctx))
        {
            failures.Add($"burst spawn incomplete (alive={AsyncInstantiateProbe.aliveCount}/{expectedAlive})");
        }
        else
        {
            ValidateBurstHierarchy(failures);
        }

        await SafeBarrier(ctx, BarrierBase + 106, "async parent burst asserted", failures);

        if (ctx.isServer)
        {
            for (int i = 0; i < burstChildren.Count; i++)
            {
                if (burstChildren[i])
                    burstChildren[i].Despawn();
            }

            if (parent)
                parent.Despawn();
        }

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"{AsyncInstantiateProbe.aliveCount} identities remained alive");

        await AssertNoPendingSpawnState(ctx, "async parent burst", failures);
        return failures.Count == 0 ? null : string.Join(", ", failures);
    }

    private void ValidateBurstHierarchy(List<string> failures)
    {
        var instances = AsyncInstantiateProbe.SnapshotInstances();
        AsyncInstantiateProbe parentInstance = null;
        var children = new List<AsyncInstantiateProbe>();

        for (var i = 0; i < instances.Length; i++)
        {
            var instance = instances[i];
            if (!instance)
                continue;

            if (instance.currentState == BurstParentToken)
            {
                if (parentInstance)
                    failures.Add("more than one instance carries the burst parent token");
                parentInstance = instance;
            }
            else if (instance.currentState >= BurstChildTokenBase &&
                     instance.currentState < BurstChildTokenBase + _burstChildCount)
            {
                children.Add(instance);
            }
        }

        if (!parentInstance)
        {
            failures.Add("burst parent instance is missing");
            return;
        }

        if (children.Count != _burstChildCount)
            failures.Add($"found {children.Count}/{_burstChildCount} burst children");

        var parentTransform = parentInstance.transform;
        int templateChildCount = _heavyParentPrefab.transform.childCount;
        if (parentTransform.childCount != templateChildCount + children.Count)
        {
            failures.Add(
                $"parent has {parentTransform.childCount} children, " +
                $"expected {templateChildCount + children.Count}");
        }

        for (int i = 0; i < templateChildCount && i < parentTransform.childCount; i++)
        {
            if (parentTransform.GetChild(i).name == _heavyParentPrefab.transform.GetChild(i).name)
                continue;
            failures.Add(
                $"pre-existing child {i} order changed: found '{parentTransform.GetChild(i).name}', " +
                $"expected '{_heavyParentPrefab.transform.GetChild(i).name}'");
            break;
        }

        children.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.transform.parent != parentTransform)
                failures.Add($"burst child with token {child.currentState} is not under the async parent");

            int expectedToken = BurstChildTokenBase + i;
            if (child.currentState != expectedToken)
            {
                failures.Add(
                    $"burst child sibling order diverged: sibling {child.transform.GetSiblingIndex()} " +
                    $"holds token {child.currentState}, expected {expectedToken}");
                break;
            }

            if (!child.gameObject.activeSelf || !child.gameObject.activeInHierarchy)
                failures.Add($"burst child {i} was left disabled");
        }
    }

    /// <summary>
    /// Emulates the reported production failure: a spawned hierarchy mutates at runtime (one
    /// networked child destroyed, two surviving networked siblings reordered), then a fresh
    /// observer receives the spawn. The rebuild happens from the server's live prototype against
    /// prefab-complete local pieces — recorded sibling paths must still resolve to the correct
    /// parents and order, not shift into neighboring slots.
    /// </summary>
    private async UniTask<string> TestMutatedReobserveOrdering(ScenarioContext ctx)
    {
        AsyncInstantiateProbe.ResetCycle();
        _mutatedShapeReceived = false;
        _mutatedShapeExpected = null;

        var failures = new List<string>();

        if (!_hasTestObserver)
        {
            await SafeBarrier(ctx, BarrierBase + 108, "mutated reobserve skipped arm", failures);
            await SafeBarrier(ctx, BarrierBase + 109, "mutated reobserve skipped converge", failures);
            await SafeBarrier(ctx, BarrierBase + 110, "mutated reobserve skipped hide", failures);
            await SafeBarrier(ctx, BarrierBase + 111, "mutated reobserve skipped reshow", failures);
            return failures.Count == 0 ? null : string.Join(", ", failures);
        }

        await SafeBarrier(ctx, BarrierBase + 108, "mutated reobserve armed", failures);

        AsyncInstantiateProbe serverResult = null;
        bool hiddenPeer = IsLocalTestObserver(ctx);

        if (ctx.isServer)
        {
            try
            {
                var operation = UnityEngine.Object.InstantiateAsync(_orderingPrefab);
                if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
                    failures.Add("native operation timed out");
                else if (operation.Result == null || operation.Result.Length != 1 || !operation.Result[0])
                    failures.Add("native operation returned no usable result");
                else
                {
                    serverResult = operation.Result[0];
                    serverResult.SetStateAndBroadcast(MutatedToken);

                    var doomed = serverResult.transform.Find("Net_A");
                    if (doomed)
                        UnityEngine.Object.Destroy(doomed.gameObject);
                    else
                        failures.Add("Net_A was not found on the source instance");
                }
            }
            catch (Exception e)
            {
                failures.Add($"{e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }
        }

        if (!await WaitUntil(
                () => AsyncInstantiateProbe.aliveCount == 1 &&
                      AsyncInstantiateProbe.AllExpectedStatesApplied(1) &&
                      NoLiveInstanceHasChild("Net_A"),
                _spawnTimeoutSeconds,
                ctx))
        {
            failures.Add(
                $"mutated spawn did not converge (alive={AsyncInstantiateProbe.aliveCount}, " +
                $"netAGone={NoLiveInstanceHasChild("Net_A")})");
        }

        if (ctx.isServer && serverResult)
        {
            // Reorder two surviving networked siblings the way gameplay does (drop/re-slot). The
            // recorded prototype must reflect the live order for any observer spawned afterwards.
            var netB = serverResult.transform.Find("Net_B");
            var netC = serverResult.transform.Find("Net_C");
            if (netB && netC)
                netC.SetSiblingIndex(netB.GetSiblingIndex());
            else
                failures.Add("Net_B/Net_C were not found on the source instance");

            BroadcastMutatedShape(AsyncInstantiateProbe.DescribeChildren(serverResult.transform));
        }

        if (!await WaitUntil(() => _mutatedShapeReceived, _operationTimeoutSeconds, ctx))
            failures.Add("server did not broadcast the mutated shape");

        await SafeBarrier(ctx, BarrierBase + 109, "mutated shape converged", failures);

        if (ctx.isServer && serverResult)
        {
            // Blacklist + visibility re-evaluation is the canonical hide: it removes the observer
            // for the whole subtree and sends the despawn, mirroring the reshow step below.
            var observer = FindPlayer(ctx, _testObserverId);
            serverResult.BlacklistPlayer(observer);
            if (ctx.networkManager.TryGetModule<HierarchyFactory>(true, out var factory) &&
                factory.TryGetHierarchy(serverResult.sceneId, out var hierarchy))
                hierarchy.EvaluateVisibility(observer, serverResult.transform);
            else
                failures.Add("server hierarchy was unavailable for the hide step");
        }

        if (hiddenPeer && !await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add("designated observer did not lose the mutated instance");

        await SafeBarrier(ctx, BarrierBase + 110, "mutated instance hidden", failures);

        if (ctx.isServer && serverResult)
        {
            var observer = FindPlayer(ctx, _testObserverId);
            if (!serverResult.RemoveBlacklistPlayer(observer))
                failures.Add("designated observer was not blacklisted");
            else if (ctx.networkManager.TryGetModule<HierarchyFactory>(true, out var factory) &&
                     factory.TryGetHierarchy(serverResult.sceneId, out var hierarchy))
                hierarchy.EvaluateVisibility(observer, serverResult.transform);
            else
                failures.Add("server hierarchy was unavailable for the reshow step");
        }

        if (hiddenPeer)
        {
            if (!await WaitUntil(
                    () => AsyncInstantiateProbe.aliveCount == 1 &&
                          SingleInstanceHasState(MutatedToken),
                    _spawnTimeoutSeconds,
                    ctx))
            {
                failures.Add(
                    $"re-shown observer did not receive a fresh spawn (alive={AsyncInstantiateProbe.aliveCount})");
            }
            else
            {
                var instances = AsyncInstantiateProbe.SnapshotInstances();
                for (var i = 0; i < instances.Length; i++)
                {
                    if (!instances[i])
                        continue;
                    var shape = AsyncInstantiateProbe.DescribeChildren(instances[i].transform);
                    if (shape != _mutatedShapeExpected)
                        failures.Add(
                            $"rebuilt hierarchy diverged from the live server shape: [{shape}] != " +
                            $"[{_mutatedShapeExpected}]");
                }
            }
        }

        await SafeBarrier(ctx, BarrierBase + 111, "mutated reobserve verified", failures);

        if (ctx.isServer && serverResult)
            serverResult.Despawn();

        if (!await WaitUntil(() => AsyncInstantiateProbe.aliveCount == 0, _despawnTimeoutSeconds, ctx))
            failures.Add($"{AsyncInstantiateProbe.aliveCount} identities remained alive");

        await AssertNoPendingSpawnState(ctx, "mutated reobserve", failures);
        return failures.Count == 0 ? null : string.Join(", ", failures);
    }

    private static bool NoLiveInstanceHasChild(string childName)
    {
        var instances = AsyncInstantiateProbe.SnapshotInstances();
        if (instances.Length == 0)
            return false;

        for (var i = 0; i < instances.Length; i++)
        {
            if (instances[i] && instances[i].transform.Find(childName))
                return false;
        }

        return true;
    }

    private static bool SingleInstanceHasState(int expectedToken)
    {
        var instances = AsyncInstantiateProbe.SnapshotInstances();
        return instances.Length == 1 && instances[0] && instances[0].currentState == expectedToken;
    }

    private static bool AllResultsSpawned(AsyncInstantiateProbe[] results, int expectedCount)
    {
        if (results == null || results.Length != expectedCount)
            return false;

        for (int i = 0; i < results.Length; i++)
        {
            if (!results[i] || !results[i].isSpawned || results[i].shouldBePooled)
                return false;
        }

        return true;
    }

    private static bool AllResultsDestroyed(AsyncInstantiateProbe[] results, int expectedCount)
    {
        if (results == null || results.Length != expectedCount)
            return false;

        for (int i = 0; i < results.Length; i++)
        {
            if (results[i])
                return false;
        }

        return true;
    }

    private async UniTask<AsyncInstantiateProbe> InstantiateOneClientProbe(
        ScenarioContext ctx,
        string phase,
        List<string> failures)
    {
        var operation = UnityEngine.Object.InstantiateAsync(_clientPrefab);
        if (!await WaitUntil(() => operation.isDone, _operationTimeoutSeconds, ctx))
        {
            failures.Add($"{phase}: native operation timed out");
            return null;
        }

        var results = operation.Result;
        if (results == null || results.Length != 1 || !results[0])
        {
            failures.Add($"{phase}: operation returned {results?.Length ?? -1}/1 usable results");
            return null;
        }

        if (!results[0].isSpawned)
            failures.Add($"{phase}: local result was not early-spawned");
        return results[0];
    }

    private async UniTask AssertNoPendingSpawnState(
        ScenarioContext ctx,
        string phase,
        List<string> failures)
    {
        string detail = null;
        if (!await WaitUntil(() => HasNoPendingSpawnState(ctx, out detail), _despawnTimeoutSeconds, ctx))
            failures.Add($"{phase}: hierarchy pending state was retained ({detail})");
    }

    private bool HasNoPendingSpawnState(ScenarioContext ctx, out string detail)
    {
        if (ctx.isServer && !HasNoPendingSpawnState(ctx, true, "server", out detail))
            return false;
        // A host's client hierarchy deliberately ignores replicated spawn packets; it is not a
        // participant in either transaction. Inspect the server hierarchy there, and inspect the
        // client hierarchy only in actual client processes.
        if (ctx.role == NetworkRole.Client && !HasNoPendingSpawnState(ctx, false, "client", out detail))
            return false;
        detail = null;
        return true;
    }

    private bool HasNoPendingSpawnState(
        ScenarioContext ctx,
        bool asServer,
        string side,
        out string detail)
    {
        if (!ctx.networkManager.TryGetModule<HierarchyFactory>(asServer, out var factory) ||
            !factory.TryGetHierarchy(gameObject.scene, out var hierarchy))
        {
            detail = $"{side} hierarchy unavailable";
            return false;
        }

        for (var i = 0; i < PendingStateFieldNames.Length; i++)
        {
            var name = PendingStateFieldNames[i];
            var field = typeof(HierarchyV2).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            var value = field?.GetValue(hierarchy);
            if (name == "_pendingLocalDespawnEchoes")
            {
                var isDisposed = value?.GetType().GetProperty("isDisposed")?.GetValue(value);
                if (isDisposed is true)
                    continue;

                detail = field == null ? $"test field {name} missing" : $"{side}.{name} was not disposed";
                return false;
            }

            var count = value?.GetType().GetProperty("Count")?.GetValue(value);
            if (count is int pending && pending == 0)
                continue;

            detail = field == null ? $"test field {name} missing" : $"{side}.{name}={count ?? "null"}";
            return false;
        }

        detail = null;
        return true;
    }

    private static async UniTask<bool> WaitUntil(
        Func<bool> predicate,
        float timeoutSeconds,
        ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(predicate, timeoutSeconds, ctx.cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async UniTask SafeBarrier(
        ScenarioContext ctx,
        int id,
        string name,
        List<string> failures)
    {
        try
        {
            await ScenarioBarrier.Wait(ctx, id, _barrierTimeoutSeconds);
        }
        catch (Exception e)
        {
            failures.Add($"{name} barrier: {e.Message}");
        }
    }

    private static PlayerID? PickClientSpawner(
        ScenarioContext ctx,
        PlayerID? excluded = null,
        bool requireRemote = false)
    {
        var manager = ctx.networkManager;
        PlayerID? hostLocal = manager.isLocalPlayerReady && ctx.role == NetworkRole.Host
            ? manager.localPlayer
            : (PlayerID?)null;

        PlayerID? remote = null;
        PlayerID? fallback = null;
        var players = manager.players;

        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer || excluded.HasValue && player == excluded.Value)
                continue;

            if (!fallback.HasValue || player.id.value < fallback.Value.id.value)
                fallback = player;

            if (hostLocal.HasValue && player == hostLocal.Value)
                continue;

            if (!remote.HasValue || player.id.value < remote.Value.id.value)
                remote = player;
        }

        return remote ?? (requireRemote ? null : fallback);
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastCancellationOutcome(bool success, string detail)
    {
        _cancellationSucceeded = success;
        _cancellationDetail = detail;
        _cancellationOutcomeReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastRapidDespawnOutcome(bool success, string detail)
    {
        _rapidDespawnSucceeded = success;
        _rapidDespawnDetail = detail;
        _rapidDespawnOutcomeReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastShapeOutcome(bool success, string detail)
    {
        _shapeSucceeded = success;
        _shapeDetail = detail;
        _shapeOutcomeReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastSpawner(
        bool hasSpawner,
        ulong spawnerId,
        bool hasLateReadyObserver,
        ulong lateReadyObserverId)
    {
        _hasSpawner = hasSpawner;
        _spawnerId = spawnerId;
        _hasLateReadyObserver = hasLateReadyObserver;
        _lateReadyObserverId = lateReadyObserverId;
        _spawnerReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastTestObserver(bool hasObserver, ulong observerId)
    {
        _hasTestObserver = hasObserver;
        _testObserverId = observerId;
        _testObserverSelectionReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastDeferredProviderRelease()
    {
        _localDeferredProvider?.Release();
        _deferredReleaseReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastMutatedShape(string expected)
    {
        _mutatedShapeExpected = expected;
        _mutatedShapeReceived = true;
    }
}

internal sealed class AsyncInstantiateDeferredPrefabProvider : IAsyncPrefabProvider
{
    private readonly GameObject _prefab;
    private IPrefabProvider _inner;
    private int _prefabId;
    private TaskCompletionSource<PrefabData> _gate;
    private bool _armed;

    public bool loadStarted { get; private set; }

    public AsyncInstantiateDeferredPrefabProvider(GameObject prefab)
    {
        _prefab = prefab;
    }

    public IEnumerable<PrefabData> allPrefabs => _inner.allPrefabs;

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
        return _gate == null ? GetTargetData() : await _gate.Task;
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

    public void Attach(IPrefabProvider inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (!_inner.TryGetPrefabData(_prefab, out var data))
            throw new InvalidOperationException("The deferred test prefab is not registered.");
        _prefabId = (int)data.prefabId;
        Reset();
    }

    public void Arm()
    {
        _armed = true;
        loadStarted = false;
        _gate = new TaskCompletionSource<PrefabData>();
    }

    public void Release()
    {
        _armed = false;
        if (_inner != null)
            _gate?.TrySetResult(GetTargetData());
    }

    public void Reset()
    {
        Release();
        _gate = null;
        loadStarted = false;
    }

    private PrefabData GetTargetData()
    {
        if (_inner.TryGetPrefabData(_prefabId, out var data))
            return data;
        throw new InvalidOperationException("The deferred test prefab registration disappeared.");
    }
}

#else

/// <summary>Keeps the PlayMode scene compatible with Unity versions before 2022.3.20.</summary>
public sealed class AsyncInstantiateScenario : Scenario
{
    public override UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        return UniTask.FromResult(ScenarioResult.Ok(
            "Object.InstantiateAsync is unavailable before Unity 2022.3.20"));
    }
}

#endif
