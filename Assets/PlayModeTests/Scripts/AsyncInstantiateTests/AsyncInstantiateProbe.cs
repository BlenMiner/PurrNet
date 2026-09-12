using System.Collections.Generic;
using System.Text;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using Channel = PurrNet.Transports.Channel;

/// <summary>
/// Shared probe used by the server- and client-authoritative InstantiateAsync phases.
/// It deliberately changes state as soon as the caller's native operation finishes. Once a
/// receiver reports ready, the server sends normal state/RPC traffic before FinishSpawnPacket.
/// </summary>
public sealed class AsyncInstantiateProbe : NetworkIdentity
{
    [SerializeField] private SyncVar<int> _state = new(0, sendIntervalInSeconds: 0f, ownerAuth: true);

    // The native result can be used before PurrNet's delayed OnSpawned callback. This mirrors the
    // existing SpawnPacket lifecycle: identities are integrated early and FinishSpawnPacket is the
    // final callback boundary.
    private bool _isLocalOperationResult;
    private int _stateSeenOnSpawn;

    private static readonly HashSet<AsyncInstantiateProbe> _instances = new();
    private static readonly HashSet<int> _serverRpcTokens = new();
    private static readonly HashSet<int> _observerRpcTokens = new();
    private static readonly HashSet<int> _targetRpcTokens = new();
    private static readonly HashSet<int> _forwardedRpcTokens = new();
    private static readonly HashSet<int> _clientEchoTokens = new();
    private static readonly Dictionary<AsyncInstantiateProbe, int> _expectedState = new();
    private static readonly Dictionary<NetworkID, HashSet<ulong>> _observerAdds = new();
    private static readonly List<GameObject> _despawnedObjects = new();
    private static readonly HashSet<int> _poolResetInstanceIds = new();
    private static bool _despawnBeforeAsyncReady;
    private static int _lateReadyDespawnRequestsSent;
    private static int _lateReadyDespawnRequestsHandled;
    private static bool _hideBeforeAsyncReady;
    private static int _hideBeforeReadyRequestsSent;
    private static int _hideBeforeReadyRequestsHandled;
    private static bool _captureHierarchyOnSpawn;
    private static readonly List<string> _hierarchySnapshots = new();

    public static int aliveCount => _instances.Count;
    public static int serverRpcTokenCount => _serverRpcTokens.Count;
    public static int observerRpcTokenCount => _observerRpcTokens.Count;
    public static int targetRpcTokenCount => _targetRpcTokens.Count;
    public static int forwardedRpcTokenCount => _forwardedRpcTokens.Count;
    public static int clientEchoTokenCount => _clientEchoTokens.Count;
    public static int despawnedObjectCount => _despawnedObjects.Count;
    public static int lateReadyDespawnRequestsSent => _lateReadyDespawnRequestsSent;
    public static int lateReadyDespawnRequestsHandled => _lateReadyDespawnRequestsHandled;
    public static int hideBeforeReadyRequestsSent => _hideBeforeReadyRequestsSent;
    public static int hideBeforeReadyRequestsHandled => _hideBeforeReadyRequestsHandled;
    public static IReadOnlyList<string> hierarchySnapshots => _hierarchySnapshots;

    public static bool stateMissingAtSpawn { get; private set; }
    public static bool sawPooledAsyncInstance { get; private set; }
    public static bool reusedPooledInstance { get; private set; }

    public int currentState => _state.value;

    public static void ResetAll()
    {
        _instances.Clear();
        _poolResetInstanceIds.Clear();
        ResetCycle();
    }

    public static void ResetCycle()
    {
        _serverRpcTokens.Clear();
        _observerRpcTokens.Clear();
        _targetRpcTokens.Clear();
        _forwardedRpcTokens.Clear();
        _clientEchoTokens.Clear();
        _expectedState.Clear();
        _observerAdds.Clear();
        _despawnedObjects.Clear();
        stateMissingAtSpawn = false;
        sawPooledAsyncInstance = false;
        reusedPooledInstance = false;
        _despawnBeforeAsyncReady = false;
        _lateReadyDespawnRequestsSent = 0;
        _lateReadyDespawnRequestsHandled = 0;
        _hideBeforeAsyncReady = false;
        _hideBeforeReadyRequestsSent = 0;
        _hideBeforeReadyRequestsHandled = 0;
        _captureHierarchyOnSpawn = false;
        _hierarchySnapshots.Clear();
    }

    public static void SetDespawnBeforeAsyncReady(bool enabled)
    {
        _despawnBeforeAsyncReady = enabled;
    }

    public static void SetHideBeforeAsyncReady(bool enabled)
    {
        _hideBeforeAsyncReady = enabled;
    }

    public static void SetCaptureHierarchyOnSpawn(bool enabled)
    {
        _captureHierarchyOnSpawn = enabled;
    }

    /// <summary>
    /// Canonical depth-first description of a transform's children: name, activeSelf, and
    /// sibling order all participate, so two hierarchies compare equal only when every child
    /// sits at the same index with the same active state. The root itself is excluded because
    /// clones differ from templates in name ("(Clone)") and root activation (OnEarlySpawn).
    /// </summary>
    public static string DescribeChildren(Transform root)
    {
        var builder = new StringBuilder();
        AppendChildren(root, builder);
        return builder.ToString();
    }

    private static void AppendChildren(Transform parent, StringBuilder builder)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (i > 0)
                builder.Append(' ');
            // PURRNET_DEBUG_POOLING builds rename pool-routed instances ("X" -> "X-Warmup");
            // structural comparisons must ignore that instrumentation suffix.
            var childName = child.name;
            if (childName.EndsWith("-Warmup", System.StringComparison.Ordinal))
                childName = childName.Substring(0, childName.Length - "-Warmup".Length);
            builder.Append(childName);
            builder.Append(child.gameObject.activeSelf ? "[on]" : "[off]");
            if (child.childCount > 0)
            {
                builder.Append('(');
                AppendChildren(child, builder);
                builder.Append(')');
            }
        }
    }

    public static AsyncInstantiateProbe[] SnapshotInstances()
    {
        var result = new AsyncInstantiateProbe[_instances.Count];
        _instances.CopyTo(result);
        return result;
    }

    public static bool AllInstancesAreSpawnedAndUnpooled(int expectedCount)
    {
        if (_instances.Count != expectedCount)
            return false;

        foreach (var instance in _instances)
        {
            if (!instance || !instance.isSpawned || !instance.id.HasValue || instance.shouldBePooled)
                return false;
        }

        return true;
    }

    public static bool AllInstancesOwnedBy(ulong expectedOwner)
    {
        foreach (var instance in _instances)
        {
            if (!instance || !instance.owner.HasValue || instance.owner.Value.id.value != expectedOwner)
                return false;
        }

        return true;
    }

    public static bool AllInstancesInScene(string expectedScene)
    {
        if (_instances.Count == 0)
            return false;

        foreach (var instance in _instances)
        {
            if (!instance || instance.gameObject.scene.name != expectedScene)
                return false;
        }

        return true;
    }

    public static bool AllPendingObserverStorageReleased()
    {
        foreach (var instance in _instances)
        {
            if (!instance)
                continue;

            var identities = instance.GetComponentsInChildren<NetworkIdentity>(true);
            for (var i = 0; i < identities.Length; i++)
            {
                if (identities[i].pendingObserverStorageAllocated)
                    return false;
            }
        }

        return true;
    }

    public static bool AllExpectedStatesApplied(int expectedCount)
    {
        if (_expectedState.Count != expectedCount)
            return false;

        foreach (var pair in _expectedState)
        {
            if (!pair.Key || pair.Key.currentState != pair.Value)
                return false;
        }

        return true;
    }

    public static int ObserverPairCount()
    {
        int count = 0;
        foreach (var pair in _observerAdds)
            count += pair.Value.Count;
        return count;
    }

    public static bool AllDespawnedObjectsDestroyed(int expectedCount)
    {
        if (_despawnedObjects.Count != expectedCount)
            return false;

        for (int i = 0; i < _despawnedObjects.Count; i++)
        {
            if (_despawnedObjects[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Server-authoritative immediate post-operation traffic.
    /// </summary>
    public void SetStateAndBroadcast(int token)
    {
        _isLocalOperationResult = true;
        _state.value = token;
    }

    /// <summary>
    /// Client-authoritative immediate post-operation traffic. The server echoes from inside
    /// the ServerRpc, exercising both client-to-server ordering and server-to-observer queuing.
    /// </summary>
    public void SetStateAndSignalServer(int token)
    {
        _isLocalOperationResult = true;
        _state.value = token;
        _state.FlushImmediately();
        ReceiveClientSpawnTraffic(token);
        ReceiveForwardedClientTraffic(token);
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawnReceived()
    {
        if (isServer || !id.HasValue)
            return;

        if (_despawnBeforeAsyncReady)
        {
            _lateReadyDespawnRequestsSent++;
            RequestDespawnBeforeAsyncReady(sceneId, id.Value);
        }
        else if (_hideBeforeAsyncReady)
        {
            _hideBeforeReadyRequestsSent++;
            RequestHideBeforeAsyncReady(sceneId, id.Value);
        }
        else return;

        // ServerRpcs are batched, while AsyncSpawnReadyPacket is sent directly immediately after
        // this callback. Flush the request so both enter the ReliableOrdered transport in the
        // intended request-then-ready order without altering production packet scheduling.
        networkManager.FlushBatchedRPCs();
    }

    protected override void OnSpawned(bool asServer)
    {
        if (_captureHierarchyOnSpawn)
            _hierarchySnapshots.Add(DescribeChildren(transform));

        _stateSeenOnSpawn = _state.value;
        if (!_isLocalOperationResult &&
            (!_expectedState.TryGetValue(this, out var expected) || _stateSeenOnSpawn != expected))
            stateMissingAtSpawn = true;
        _instances.Add(this);
        if (shouldBePooled)
            sawPooledAsyncInstance = true;
        if (_poolResetInstanceIds.Contains(GetHashCode()))
            reusedPooledInstance = true;
    }

    protected override void OnObserverAdded(PlayerID player)
    {
        if (!id.HasValue)
            return;

        if (!_observerAdds.TryGetValue(id.Value, out var players))
        {
            players = new HashSet<ulong>();
            _observerAdds.Add(id.Value, players);
        }

        players.Add(player.id.value);

        // These use the ordinary observer/target paths after Ready promoted this player, and are
        // intentionally sent before FinishSpawnPacket just like bootstrap state on a normal spawn.
        ReceiveServerSpawnTraffic(_state.value);
        ReceiveTargetSpawnTraffic(player, _state.value);
    }

    protected override void OnDespawned()
    {
        // A host receives both server- and client-side callbacks for the same object.
        // HashSet.Remove keeps the live count stable; only retain the GameObject once.
        if (_instances.Remove(this))
            _despawnedObjects.Add(gameObject);
    }

    protected override void OnPoolReset()
    {
        _poolResetInstanceIds.Add(GetHashCode());
        base.OnPoolReset();
    }

    [ObserversRpc(runLocally: true)]
    private void ReceiveServerSpawnTraffic(int token)
    {
        _observerRpcTokens.Add(token);
        _expectedState[this] = token;
    }

    [ServerRpc(requireOwnership: false)]
    private void ReceiveClientSpawnTraffic(int token)
    {
        // The owner-auth SyncVar update is ordered before this RPC. Do not write it again on the
        // server: the server is not the owner, and doing so would only test a permission failure.
        _serverRpcTokens.Add(token);
        EchoClientSpawnTraffic(token);

        var players = networkManager.players;
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            if (player.isServer && !networkManager.isHost)
                continue;
            ReceiveTargetSpawnTraffic(player, token);
        }
    }

    [ServerRpc(requireOwnership: false, channel: Channel.ReliableOrdered)]
    private static void RequestDespawnBeforeAsyncReady(SceneID scene, NetworkID identityId)
    {
        _lateReadyDespawnRequestsHandled++;
        var manager = NetworkManager.main;
        if (!manager || !manager.TryGetModule<HierarchyFactory>(true, out var factory) ||
            !factory.TryGetIdentity(scene, identityId, out var identity) || !identity || !identity.isSpawned)
            return;

        identity.Despawn();
    }

    [ServerRpc(requireOwnership: false, channel: Channel.ReliableOrdered)]
    private static void RequestHideBeforeAsyncReady(
        SceneID scene,
        NetworkID identityId,
        RPCInfo info = default)
    {
        var manager = NetworkManager.main;
        if (!manager || !manager.TryGetModule<HierarchyFactory>(true, out var factory) ||
            !factory.TryGetHierarchy(scene, out var hierarchy) ||
            !factory.TryGetIdentity(scene, identityId, out var identity) || !identity || !identity.isSpawned)
            return;

        identity.BlacklistPlayer(info.sender);
        hierarchy.ManualRemoveObserver(identity, info.sender);
        _hideBeforeReadyRequestsHandled++;
    }

    [ObserversRpc(runLocally: true)]
    private void EchoClientSpawnTraffic(int token)
    {
        _clientEchoTokens.Add(token);
        _expectedState[this] = token;
    }

    [TargetRpc]
    private void ReceiveTargetSpawnTraffic(PlayerID target, int token)
    {
        _targetRpcTokens.Add(token);
    }

    [ObserversRpc(requireServer: false, runLocally: true)]
    private void ReceiveForwardedClientTraffic(int token)
    {
        _forwardedRpcTokens.Add(token);
    }
}

/// <summary>Separate type so cancellation can assert that no identity ever spawned.</summary>
public sealed class AsyncInstantiateCancellationIdentity : NetworkIdentity
{
    private static readonly HashSet<NetworkID> _spawned = new();
    private NetworkID? _trackedId;
    private bool _trackedClone;

    public static int spawnedCount => _spawned.Count;
    public static int everSpawnedCount { get; private set; }
    public static int liveCloneCount { get; private set; }

    public static void ResetAll()
    {
        _spawned.Clear();
        everSpawnedCount = 0;
        liveCloneCount = 0;
    }

    private void Awake()
    {
        if (!gameObject.name.EndsWith("(Clone)", System.StringComparison.Ordinal))
            return;
        _trackedClone = true;
        liveCloneCount++;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned()
    {
        if (!id.HasValue)
            return;

        _trackedId = id.Value;
        _spawned.Add(id.Value);
        everSpawnedCount++;
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
            _spawned.Remove(_trackedId.Value);
        _trackedId = null;
    }

    protected override void OnDestroy()
    {
        if (_trackedClone)
            liveCloneCount--;
        _trackedClone = false;
        base.OnDestroy();
    }
}
