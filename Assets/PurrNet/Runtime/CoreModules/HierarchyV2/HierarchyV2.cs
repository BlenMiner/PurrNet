using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public delegate void IdentityAction(NetworkIdentity identity);

    public delegate void ObserverAction(PlayerID player, NetworkIdentity identity);

    public delegate void SpawnedAction(PlayerID player, SceneID scene, NetworkID identity);

    public delegate bool ValidateSpawnAction(PlayerID player, SpawnPacket data);

    public delegate void SpawnDelegate(GameObject instance, bool isSceneObject);

    public class HierarchyV2 : IPromoteToServerModule, ITransferToNewServer
    {
        private bool _asServer;

        private readonly NetworkManager _manager;
        private readonly SceneID _sceneId;
        private readonly Scene _scene;
        private readonly ScenePlayersModule _scenePlayers;
        private readonly PlayersManager _playersManager;
        private readonly VisilityV2 _visibility;

        private readonly HierarchyPool _scenePool;
        private readonly HierarchyPool _prefabsPool;

        private readonly List<NetworkIdentity> _spawnedIdentities = new();
        private readonly Dictionary<NetworkID, NetworkIdentity> _spawnedIdentitiesMap = new();

        private ulong _nextId;

        private bool _areSceneObjectsReady;

        /// <summary>
        /// Invoked to validate the spawning of a client-side object before it is instantiated.
        /// This event allows implementing custom rules to determine whether the object spawn
        /// should proceed or be rejected.
        /// </summary>
        private readonly List<ValidateSpawnAction> _clientSpawnValidators = new();

        public event ValidateSpawnAction onClientSpawnValidate
        {
            add
            {
                if (value != null)
                    _clientSpawnValidators.Add(value);
            }
            remove
            {
                if (value == null)
                    return;

                for (int i = _clientSpawnValidators.Count - 1; i >= 0; i--)
                {
                    if (_clientSpawnValidators[i] != value)
                        continue;

                    _clientSpawnValidators.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// Fired when a NetworkIdentity is added to the hierarchy early in its lifecycle,
        /// before the standard identity initialization or observer assignment processes occur.
        /// This event is typically leveraged to perform custom logic or setup on new identities
        /// before they are fully managed by the hierarchy.
        /// </summary>
        public event IdentityAction onEarlyIdentityAdded;

        /// <summary>
        /// Triggered when a new identity is added to the network hierarchy.
        /// This event is invoked after the identity has been initialized and is ready to participate
        /// in the network lifecycle, such as spawning, synchronization, or visibility evaluation.
        /// </summary>
        public event IdentityAction onIdentityAdded;

        /// <summary>
        /// Triggered when a network identity is removed from the hierarchy.
        /// This event provides an opportunity to handle cleanup or additional logic
        /// associated with the removal of a network identity from the system.
        /// </summary>
        public event IdentityAction onIdentityRemoved;

        /// <summary>
        /// Triggered whenever a new observer is added to a networked identity.
        /// This event allows for custom logic to be executed when an observer becomes associated
        /// with a specific networked object within the hierarchy.
        /// </summary>
        public event ObserverAction onObserverAdded;

        /// <summary>
        /// Triggered after an observer has been added to a networked entity during the late evaluation phase.
        /// This event allows for additional logic to be executed after the observer is linked to the entity,
        /// such as custom visibility or state synchronization actions.
        /// </summary>
        public event ObserverAction onLateObserverAdded;

        /// <summary>
        /// Triggered when an observer is removed from the system or process.
        /// This event can be used to handle any necessary cleanup or updates
        /// associated with the removal of the observer.
        /// </summary>
        public event ObserverAction onObserverRemoved;

        /// <summary>
        /// Triggered when a spawn packet is sent to a client. This event provides details about the player,
        /// the scene, and the spawned object's identifier, enabling the implementation of custom behavior
        /// upon the transmission of spawn data.
        /// </summary>
        public event SpawnedAction onSentSpawnPacket;

        /// <summary>
        /// Fired in PostNetworkMessages after observer-add state RPCs have been flushed but before
        /// FinishSpawnPacket ships. Modules with per-spawn data that piggybacks on observer adds
        /// (e.g. GlobalOwnershipModule's pending ownership changes) flush here so their packets
        /// arrive before the receiver fires OnSpawned.
        /// </summary>
        public event Action<SceneID> onPreFinishSpawn;

        private bool _isPlayerReady;

        public HierarchyV2(NetworkManager manager, SceneID sceneId, Scene scene,
            ScenePlayersModule players, PlayersManager playersManager, bool asServer)
        {
            isReadyToSpawn = asServer;
            _manager = manager;
            _sceneId = sceneId;
            _scene = scene;
            _scenePlayers = players;
            _visibility = new VisilityV2(_manager);
            _asServer = asServer;
            _playersManager = playersManager;

            _scenePool = NetworkPoolManager.GetScenePool(scene, sceneId, manager);
            _scenePool.Warmup(manager.prefabResolver.GetScenePrefabs(sceneId));
            _prefabsPool = NetworkPoolManager.GetPool(manager);

            UnityLatestUpdate.TriggerPendingAsaps();

            SetupSceneObjects(scene);
        }

        public void PromoteToServerModule()
        {
            _asServer = true;
            _nextId = default;
            _isDisposed = false;

            // catch up with the server's next id
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                if (identity.id.HasValue && identity.id.Value.id.value >= _nextId)
                    _nextId = identity.id.Value.id.value + 1;

                identity.ClearObservers();
            }
        }

        public void PostPromoteToServerModule()
        {
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var clientId = identity.GetNetworkID(false);
                if (clientId.HasValue)
                    identity.SetID(clientId.Value);

                if (identity.IsSpawned(false))
                {
                    var owner = identity.owner;
                    if (owner.HasValue)
                    {
                        identity.TriggerOnOwnerChanged(owner.Value, null, false, false);
                    }
                    identity.TriggerDespawnEvent(false);
                    identity.SetIsSpawned(false, false);
                }
            }

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                var prevOwner = identity.internalOwnerServer;
                identity.SetIdentity(_manager, this, _sceneId, _asServer, false);
                identity.internalOwnerServer = prevOwner;
                identity.TriggerEarlySpawnEvent(true);

                if (prevOwner.HasValue)
                {
                    identity.TriggerOnOwnerChanged(null, prevOwner.Value, true, false);
                    identity.TriggerOnOwnerDisconnected(prevOwner.Value);
                }

                identity.TriggerSpawnEvent(true);
            }

            RebuildSpawnedHierarchyLinks();

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                identity.TriggerPromoteToServer();
            }
        }

        private void RebuildSpawnedHierarchyLinks()
        {
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                if (!identity || !identity.isSpawned)
                    continue;

                identity.parent = identity.GetNearestParent();
                identity.RecalculateNearestPath();
            }

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];
                if (!identity || !identity.isSpawned)
                    continue;

                if (identity.gameObject.GetComponent<NetworkIdentity>() != identity)
                    continue;

                identity.RecalculateDirectChildren();
            }
        }

        readonly List<GameObjectPrototype> _defaultPrototypes = new List<GameObjectPrototype>();

        private void SetupSceneObjects(Scene scene)
        {
            if (_manager.TryGetModule<HierarchyFactory>(!_asServer, out var factory) &&
                factory.TryGetHierarchy(_sceneId, out var other))
            {
                if (other._areSceneObjectsReady)
                {
                    _areSceneObjectsReady = true;
                    return;
                }
            }

            if (_areSceneObjectsReady)
                return;

            _defaultPrototypes.Clear();

            var allSceneIdentities = ListPool<NetworkIdentity>.Instantiate();
            SceneObjectsModule.GetSceneIdentities(scene, allSceneIdentities, _manager.networkRules.ShouldIncludeInstantiatedSceneObjects());

            var roots = HashSetPool<NetworkIdentity>.Instantiate();

            var count = allSceneIdentities.Count;
            for (int i = 0; i < count; i++)
            {
                var identity = allSceneIdentities[i];
                if (!identity)
                    continue;

                var root = identity.GetRootIdentity();

                if (!root || !roots.Add(root))
                    continue;

                if (root.skipSceneAutoSpawning)
                    continue;

                var children = ListPool<NetworkIdentity>.Instantiate();
                root.GetComponentsInChildren(true, children);

                // don't spawn scene objects that don't pass the filters
                for (int j = 0; j < children.Count; j++)
                {
                    if (children[j].skipSceneAutoSpawning)
                        children.RemoveAt(j--);
                }

                var cc = children.Count;
                if (cc == 0)
                {
                    ListPool<NetworkIdentity>.Destroy(children);
                    continue;
                }

                onPreSpawn?.Invoke(root.gameObject, true);

                var pid = -i - 2;

                for (int j = 0; j < cc; j++)
                {
                    var child = children[j];

                    if (child.isSetup)
                        continue;

                    var trs = child.transform;
                    var first = trs.GetComponent<NetworkIdentity>();

                    child.PreparePrefabInfo(pid, child == first ? j : first.componentIndex, true, true);

                    if (!_asServer)
                        child.ResetIdentity();
                }

                if (_asServer)
                {
                    SpawnSceneObject(children);
                }
                else
                {
                    for (var j = 0; j < cc; j++)
                        _scenePool.RegisterActiveScenePiece(children[j]);
                }

                _defaultPrototypes.Add(HierarchyPool.GetFullPrototype(root.transform, null, true));
                ListPool<NetworkIdentity>.Destroy(children);
            }

            ListPool<NetworkIdentity>.Destroy(allSceneIdentities);
            HashSetPool<NetworkIdentity>.Destroy(roots);
            _areSceneObjectsReady = true;
        }

        public void Enable()
        {
            _enabled = true;
            PurrNetGameObjectUtils.onGameObjectCreated += OnGameObjectCreated;
#if PURRNET_UNITY_INSTANTIATE_ASYNC
            UnityProxy.onAsyncInstantiateCompleted += OnAsyncInstantiateCompleted;
#endif
            _visibility.visibilityChanged += OnVisibilityChanged;
            _visibility.visibilityCleared += OnVisibilityCleared;
            _scenePlayers.onPrePlayerLoadedScene += OnPlayerLoadedScene;
            _scenePlayers.onPlayerUnloadedScene += OnPlayerUnloadedScene;
            _playersManager.onNetworkIDReceived += OnNetworkIDReceived;

            Init();

            _playersManager.Subscribe<SpawnPacketBatch>(OnSpawnPacketBatch);
            _playersManager.Subscribe<SpawnPacket>(OnSpawnPacket);
            _playersManager.Subscribe<DespawnPacket>(OnDespawnPacket);
            _playersManager.Subscribe<FinishSpawnPacket>(OnFinishSpawnPacket);
            _playersManager.Subscribe<AsyncSpawnReadyPacket>(OnAsyncSpawnReadyPacket);
            _playersManager.Subscribe<SceneSpawnReconcilePacket>(OnSceneSpawnReconcilePacket);
            _playersManager.Subscribe<ChangeParentPacket>(OnParentChangedPacket);
        }

        private void Init()
        {
            if (_playersManager.lastNid.HasValue)
                OnNetworkIDReceived(_playersManager.lastNid.Value);
            if (_playersManager.localPlayerId.HasValue)
                OnPlayerReceivedID(_playersManager.localPlayerId.Value);
            else _playersManager.onLocalPlayerReceivedID += OnPlayerReceivedID;
        }

        public void Disable()
        {
            _enabled = false;
            _pendingUnauthorizedParentReverts.Clear();
            ClearAsyncSpawnState();
            _cachedPrefabAsyncShapes.Clear();
            _pendingLocalDespawnEchoes.Dispose();
            PurrNetGameObjectUtils.onGameObjectCreated -= OnGameObjectCreated;
#if PURRNET_UNITY_INSTANTIATE_ASYNC
            UnityProxy.onAsyncInstantiateCompleted -= OnAsyncInstantiateCompleted;
#endif
            _visibility.visibilityChanged -= OnVisibilityChanged;
            _visibility.visibilityCleared -= OnVisibilityCleared;
            _scenePlayers.onPrePlayerLoadedScene -= OnPlayerLoadedScene;
            _scenePlayers.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            _playersManager.onLocalPlayerReceivedID -= OnPlayerReceivedID;
            _playersManager.onNetworkIDReceived -= OnNetworkIDReceived;

            _playersManager.Unsubscribe<SpawnPacketBatch>(OnSpawnPacketBatch);
            _playersManager.Unsubscribe<SpawnPacket>(OnSpawnPacket);
            _playersManager.Unsubscribe<DespawnPacket>(OnDespawnPacket);
            _playersManager.Unsubscribe<FinishSpawnPacket>(OnFinishSpawnPacket);
            _playersManager.Unsubscribe<AsyncSpawnReadyPacket>(OnAsyncSpawnReadyPacket);
            _playersManager.Unsubscribe<SceneSpawnReconcilePacket>(OnSceneSpawnReconcilePacket);
            _playersManager.Unsubscribe<ChangeParentPacket>(OnParentChangedPacket);

            if (!_manager.isTranferingToNewServer)
                NetworkPoolManager.RemovePool(_sceneId);
        }

        private void OnSceneSpawnReconcilePacket(PlayerID player, SceneSpawnReconcilePacket data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;

            if (_asServer)
                return;

            _scenePool.ReconcileActiveScenePieces();
        }

        public void TransferToNewServer()
        {
            ClearAsyncSpawnState();
            _pendingLocalDespawnEchoes.Dispose();
            isReadyToSpawn = false;
            _nextId = default;
            _isPlayerReady = false;

            var hash = HashSetPool<NetworkIdentity>.Instantiate();

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var nid = _spawnedIdentities[i];
                if (!nid)
                    continue;

                var root = nid.GetRootIdentity();

                if (!root)
                    continue;

                hash.Add(root);
            }

            foreach (var r in hash)
            {
                if (!r) continue;
                Despawn(r.gameObject, true, true);
            }

            HashSetPool<NetworkIdentity>.Destroy(hash);

            Init();

            UnityLatestUpdate.TriggerPendingAsaps();
        }

        private void OnSpawnPacketBatch(PlayerID player, SpawnPacketBatch data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;

            int count = data.spawnPackets.Count;
            for (var i = 0; i < count; ++i)
                HandleSpawn(player, data.spawnPackets[i], false);

            count = data.despawnPackets.Count;
            for (var i = 0; i < count; ++i)
                OnDespawnPacket(player, data.despawnPackets[i], asServer);

            FlushSpawnPackets();
            data.Dispose();
        }

        bool _isDisposed;
        bool _enabled;

        public bool Cleanup()
        {
            _pendingLocalDespawnEchoes.Dispose();

            var rules = _manager.networkRules;
            if (rules && !rules.ShouldCleanupSpawnedObjectsOnDisconnect())
                return true;

            if (_isDisposed)
                return true;

            _isDisposed = true;
            ClearAsyncSpawnState();

            if (ApplicationContext.isQuitting)
            {
                return true;
            }

            var hash = HashSetPool<NetworkIdentity>.Instantiate();

            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var nid = _spawnedIdentities[i];
                var root = nid.GetRootIdentity();

                if (!root)
                    continue;

                hash.Add(root);
            }

            foreach (var r in hash)
            {
                if (!r) continue;
                Despawn(r.gameObject, true, true);
            }

            if (!_manager.isTranferingToNewServer)
            {
                for (var i = 0; i < _defaultPrototypes.Count; i++)
                {
                    var defaultPrototype = _defaultPrototypes[i];
                    CreatePrototype(defaultPrototype, null);
                    defaultPrototype.Dispose();
                }
                _defaultPrototypes.Clear();
            }

            HashSetPool<NetworkIdentity>.Destroy(hash);
            return true;
        }

        /// <summary>
        /// Indicates whether the system is ready to spawn networked objects.
        /// This flag is typically set when the necessary conditions for spawning
        /// objects, such as proper initialization and synchronization, have been met.
        /// </summary>
        public bool isReadyToSpawn { get; private set; }

        private void OnNetworkIDReceived(NetworkID nid)
        {
            if (nid.id >= _nextId)
                _nextId = nid.id.value + 1;

            isReadyToSpawn = true;
        }

        private void OnPlayerReceivedID(PlayerID player)
        {
            _isPlayerReady = true;

            if (_asServer || !_manager.isServer)
                return;

            if (!_manager.TryGetModule<HierarchyFactory>(true, out var factory) ||
                !factory.TryGetHierarchy(_sceneId, out var serverHierarchy))
                return;

            if (!serverHierarchy._scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                return;

            serverHierarchy.CatchupClient(player);
        }

        private void OnParentChangedPacket(PlayerID player, ChangeParentPacket data, bool asserver)
        {
            // when in host mode, let the server handle the spawning on their module
            if (!_asServer && _manager.isServer)
                return;

            if (data.sceneId != _sceneId)
                return;

            if (!TryGetIdentity(data.childId, out var identity))
                return;

            if (_asServer && !identity.HasChangeParentAuthority(player, !_asServer))
            {
                PurrLogger.LogError(
                    $"Change parent failed for '{identity.gameObject.name}' due to lack of permissions.",
                    identity.gameObject);
                return;
            }

            NetworkIdentity parent = null;

            if (data.newParentId.HasValue && !TryGetIdentity(data.newParentId.Value, out parent))
            {
                PurrLogger.LogError($"Change parent failed for '{identity.gameObject.name}'. Parent `{data.newParentId.Value}` not found.",
                    identity.gameObject);
                return;
            }

            ApplyParentChange(identity, parent, data.path, true, data.worldPositionStays);

            if (_asServer)
            {
                // forward parent change to other observers
                var observers = DisposableList<PlayerID>.Create(identity.observers);
                observers.Remove(player);
                if (_playersManager.localPlayerId.HasValue)
                    observers.Remove(_playersManager.localPlayerId.Value);
                _playersManager.Send(observers, data);
            }
        }

        static NetworkIdentity ClosestParent(Transform trs)
        {
            if (!trs)
                return null;

            var parent = trs;
            while (parent)
            {
                if (parent.TryGetComponent<NetworkIdentity>(out var nid) && nid.isSpawned)
                    return nid;

                parent = parent.parent;
            }

            return null;
        }

        void ApplyParentChange(NetworkIdentity identity, NetworkIdentity parent, int[] path, bool refreshVisibility, bool worldPositionStays = true, bool applyToTransform = true)
        {
            var idTrs = identity.transform;
            var oldParent = identity.parent;

            var tmpList = ListPool<NetworkIdentity>.Instantiate();
            identity.GetComponents(tmpList);

            var first = tmpList[0];

            for (var i = 0; i < tmpList.Count; i++)
            {
                var child = tmpList[i];
                child.parent = parent;
                child.invertedPathToNearestParentArray = path;
            }

            ListPool<NetworkIdentity>.Destroy(tmpList);

            if (applyToTransform)
            {
                var nt = identity.GetComponent<NetworkTransform>();
                if (nt) nt.StartIgnoringParentChanges();

                var nrb = identity.GetComponent<NetworkRigidbody>();
                if (nrb) nrb.StartIgnoringParentChanges();

                if (parent)
                    HierarchyPool.WalkThePath(parent.transform, idTrs, path, worldPositionStays);
                else
                    idTrs.SetParent(null, worldPositionStays);

                if (nt) nt.StopIgnoringParentChanges();
                if (nrb) nrb.StopIgnoringParentChanges();
            }

            if (parent)
                parent.AddDirectChild(first);

            if (oldParent && parent != oldParent)
                oldParent.RemoveDirectChild(first);

            if (refreshVisibility && _asServer && _scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    _visibility.RefreshVisibilityForGameObject(player, first, parent);
                }

                FlushSpawnPackets();
            }
        }

        private readonly HashSet<NetworkIdentity> _pendingUnauthorizedParentReverts = new();

        private void OnUnauthorizedLocalParentChange(NetworkIdentity identity)
        {
            if (!_pendingUnauthorizedParentReverts.Add(identity))
                return;

            var rules = identity.networkRules;
            var localPlayer = _playersManager.localPlayerId;
            bool isOwner = identity.owner.HasValue && identity.owner == localPlayer;

            PurrLogger.LogError(
                $"Parent change of '{identity.gameObject.name}' was ignored and will be reverted: " +
                $"local player {localPlayer}{(isOwner ? " (owner)" : " (not the owner)")} lacks change-parent authority " +
                $"under rules '{(rules && !string.IsNullOrEmpty(rules.name) ? rules.name : "<unnamed>")}'.\n" +
                "Reparent from the server, use NetworkRules whose 'changeParentAuth' includes this peer, " +
                "or call StartIgnoringParentChanges() on the NetworkTransform for intentional local-only parenting.",
                identity.gameObject);
        }

        private void RevertUnauthorizedParentChanges()
        {
            if (_pendingUnauthorizedParentReverts.Count == 0)
                return;

            var toRevert = ListPool<NetworkIdentity>.Instantiate();
            toRevert.AddRange(_pendingUnauthorizedParentReverts);
            _pendingUnauthorizedParentReverts.Clear();

            for (var i = 0; i < toRevert.Count; i++)
            {
                var identity = toRevert[i];
                if (!identity || !identity.isSpawned)
                    continue;

                ApplyParentChange(identity, identity.parent, identity.invertedPathToNearestParentArray, false);
            }

            ListPool<NetworkIdentity>.Destroy(toRevert);
        }

        public void OnParentChanged(NetworkIdentity identity, Transform parent, bool worldPositionStays = true)
        {
            if (!_asServer)
            {
                if (!_playersManager.localPlayerId.HasValue)
                    return;

                bool hasAuthority = identity.HasChangeParentAuthority(_playersManager.localPlayerId.Value, _asServer);

                if (!hasAuthority)
                {
                    OnUnauthorizedLocalParentChange(identity);
                    return;
                }
            }

            if (parent && parent.gameObject.scene.handle != _scene.handle)
            {
                PurrLogger.LogError($"Change parent failed for '{identity.gameObject.name}'.\n" +
                                    $"Moving networked objects to a different scene is not supported.\n" +
                                    $"Original scene: `{parent.gameObject.scene.name}`, new parent's scene: `{_scene.name}`\n" +
                                    $"Try moving the player spawner to it's own game object in the scene or toggle off `DontDestroyOnLoad` on the `NetworkManager`.",
                    identity.gameObject);
                return;
            }

            var closestNid = ClosestParent(parent);
            var oldParent = identity.parent;

            var tmpList = ListPool<NetworkIdentity>.Instantiate();
            identity.GetComponents(tmpList);

            var first = tmpList[0];
            first.parent = closestNid;
            first.RecalculateNearestPath();

            for (var i = 1; i < tmpList.Count; i++)
            {
                var child = tmpList[i];
                child.parent = closestNid;
                child.invertedPathToNearestParentArray = first.invertedPathToNearestParentArray;
            }

            ListPool<NetworkIdentity>.Destroy(tmpList);

            if (closestNid)
                closestNid.AddDirectChild(first);

            if (oldParent && oldParent != closestNid)
                oldParent.RemoveDirectChild(first);

            if (identity.id.HasValue)
            {
                var packet = new ChangeParentPacket
                {
                    sceneId = _sceneId,
                    childId = identity.id.Value,
                    newParentId = closestNid?.id,
                    path = identity.invertedPathToNearestParentArray,
                    worldPositionStays = worldPositionStays
                };

                _manager.FlushBatchedRPCs();
                if (_asServer)
                    _playersManager.Send(identity.observers, packet);
                else _playersManager.SendToServer(packet);
            }

            if (_asServer && _scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    _visibility.RefreshVisibilityForGameObject(player, first, closestNid);
                }

                _manager.FlushBatchedRPCs();
                FlushSpawnPackets();
            }
        }

        private readonly Dictionary<SpawnID, DisposableList<NetworkIdentity>> _pendingSpawns = new();
        private readonly HashSet<SpawnID> _asyncPendingSpawns = new();
        private readonly List<(SpawnID packetIdx, PlayerID player, bool asServer)> _pendingFinishSpawns = new();
        private readonly List<(PlayerID player, DespawnPacket packet, bool asServer)> _pendingDespawns = new();
        private DisposableList<NetworkID> _pendingLocalDespawnEchoes;
        private readonly HashSet<SpawnID> _cancelledPendingSpawns = new();
        private readonly Dictionary<SpawnID, PendingAsyncObserverSpawn> _pendingAsyncObservers = new();
        private readonly Dictionary<SpawnID, PendingAsyncObserverSpawn> _readyAsyncObservers = new();
        private readonly HashSet<(PlayerID player, NetworkID root)> _failedAsyncObserverRoots = new();
        private readonly HashSet<SpawnID> _relayAsyncSpawns = new();
        private readonly HashSet<NetworkID> _failedAsyncSpawnRoots = new();
        private int _asyncVisibilityDepth;
        private int _asyncObserverPromotionDepth;
        // One-way: later catch-up packets must retain bypass checks after async/unpooled provenance appears.
        private bool _hasConfiguredPoolBypass;

        private bool HasActiveAsyncObserverState =>
            _asyncObserverPromotionDepth > 0 ||
            _pendingAsyncObservers.Count > 0 ||
            _readyAsyncObservers.Count > 0 ||
            _failedAsyncObserverRoots.Count > 0;

        private sealed class PendingAsyncObserverSpawn
        {
            public readonly PlayerID player;
            public readonly List<NetworkIdentity> identities;
            public readonly float createdAt;
            public bool sent;

            public PendingAsyncObserverSpawn(PlayerID player, List<NetworkIdentity> identities)
            {
                this.player = player;
                this.identities = identities;
                createdAt = Time.realtimeSinceStartup;
            }
        }

        internal static float asyncSpawnReadyTimeoutSeconds = 60f;

        internal static float debugHoldAsyncSpawnReadySeconds;

        private readonly List<(float dueTime, AsyncSpawnReadyPacket packet)> _heldAsyncSpawnReadies = new();

        private readonly List<(PlayerID player, NetworkIdentity root)> _collateralCancelRetries = new();
        private readonly List<(PlayerID player, NetworkIdentity root)> _collateralCancelRetriesArmed = new();

        private void RetryCollateralCancelledSpawns()
        {
            if (!_asServer)
                return;

            for (var i = 0; i < _collateralCancelRetriesArmed.Count; i++)
            {
                var (player, root) = _collateralCancelRetriesArmed[i];
                if (!root || !root.isSpawned)
                    continue;

                EvaluateVisibility(player, root.transform);
            }
            _collateralCancelRetriesArmed.Clear();

            if (_collateralCancelRetries.Count > 0)
            {
                _collateralCancelRetriesArmed.AddRange(_collateralCancelRetries);
                _collateralCancelRetries.Clear();
            }
        }

#if PURRNET_UNITY_INSTANTIATE_ASYNC
        private sealed class PendingAsyncInstantiation
        {
            public SpawnPacket packet;
            public AsyncInstantiateOperation<GameObject> operation;
            public GameObject result;
            public bool flushData;
            public bool cancelled;
            bool _packetDisposed;

            public void DisposePacket()
            {
                if (_packetDisposed)
                    return;
                _packetDisposed = true;
                packet.Dispose();
            }
        }

        private readonly Dictionary<NetworkID, PendingAsyncInstantiation> _pendingAsyncInstantiations = new();
        private readonly HashSet<NetworkID> _reservedAsyncNetworkIds = new();

        private readonly struct OrphanAwaitingAsyncParent
        {
            public readonly NetworkIdentity identity;
            public readonly int[] path;
            public readonly Vector3 position;
            public readonly Quaternion rotation;
            public readonly Vector3 scale;

            public OrphanAwaitingAsyncParent(NetworkIdentity identity, GameObjectPrototype prototype)
            {
                this.identity = identity;
                position = prototype.position;
                rotation = prototype.rotation;
                scale = prototype.scale;

                var source = prototype.path;
                if (source == null || source.Length == 0)
                {
                    path = Array.Empty<int>();
                }
                else
                {
                    path = new int[source.Length];
                    Array.Copy(source, path, source.Length);
                }
            }
        }

        /// <summary>
        /// Spawned identities whose parent id is reserved by a pending InstantiateAsync. They are
        /// applied and live immediately (traffic keeps flowing to them) but stay parked at the
        /// scene root until the parent's async instantiation registers its identities.
        /// </summary>
        private readonly Dictionary<NetworkID, List<OrphanAwaitingAsyncParent>> _orphansWaitingForAsyncParent = new();
#endif

        private void ClearAsyncSpawnState()
        {
            foreach (var pending in _pendingAsyncObservers.Values)
            {
                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (identity)
                        identity.TryRemovePendingObserver(pending.player);
                }
            }
            _pendingAsyncObservers.Clear();

            foreach (var pair in _readyAsyncObservers)
            {
                var ready = pair.Value;
                _toCompleteNextFrame.Remove(pair.Key);
                for (var i = 0; i < ready.identities.Count; i++)
                {
                    var identity = ready.identities[i];
                    if (identity)
                        identity.TryRemoveObserver(ready.player);
                }
            }
            _readyAsyncObservers.Clear();

            foreach (var failed in _failedAsyncObserverRoots)
            {
                if (!TryGetIdentity(failed.root, out var root) || !root)
                    continue;

                var identities = ListPool<NetworkIdentity>.Instantiate();
                GetComponentsInChildren(root.gameObject, identities);
                for (var i = 0; i < identities.Count; i++)
                {
                    var identity = identities[i];
                    if (identity)
                        identity.TryRemovePendingObserver(failed.player);
                }
                ListPool<NetworkIdentity>.Destroy(identities);
            }
            _failedAsyncObserverRoots.Clear();
            _relayAsyncSpawns.Clear();
            _failedAsyncSpawnRoots.Clear();
            _cancelledPendingSpawns.Clear();
            _asyncPendingSpawns.Clear();
            _heldAsyncSpawnReadies.Clear();
            _collateralCancelRetries.Clear();
            _collateralCancelRetriesArmed.Clear();
            _asyncVisibilityDepth = 0;

#if PURRNET_UNITY_INSTANTIATE_ASYNC
            if (_orphansWaitingForAsyncParent.Count > 0)
            {
                foreach (var orphans in _orphansWaitingForAsyncParent.Values)
                    ListPool<OrphanAwaitingAsyncParent>.Destroy(orphans);
                _orphansWaitingForAsyncParent.Clear();
            }

            if (_pendingAsyncInstantiations.Count > 0)
            {
                var states = new List<PendingAsyncInstantiation>(_pendingAsyncInstantiations.Values);
                _pendingAsyncInstantiations.Clear();
                _reservedAsyncNetworkIds.Clear();

                for (var i = 0; i < states.Count; i++)
                {
                    var state = states[i];
                    state.cancelled = true;
                    try { state.operation?.Cancel(); }
                    catch { /* teardown must continue */ }
                    if (state.result)
                        UnityProxy.DestroyDirectly(state.result);
                    state.result = null;
                    state.DisposePacket();
                }
            }
#endif
        }

        private void OnFinishSpawnPacket(PlayerID player, FinishSpawnPacket data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;

            data.packetIdx = RestoreTarget(data.packetIdx, player);

            if (_cancelledPendingSpawns.Count > 0 && _cancelledPendingSpawns.Remove(data.packetIdx))
                return;

            if (_pendingSpawns.Remove(data.packetIdx, out var list))
            {
                if (_asyncPendingSpawns.Count > 0)
                    _asyncPendingSpawns.Remove(data.packetIdx);
                using (list)
                {
                    int count = list.Count;

                    switch (count)
                    {
                        case > 0 when !list[0] || !list[0].isSpawned:
                            if (_relayAsyncSpawns.Count > 0)
                                _relayAsyncSpawns.Remove(data.packetIdx);
                            return;
                        case > 0 when list[0] && _asServer:
                        {
                            var spawner = data.packetIdx.scope;
                            for (var i = 0; i < count; i++)
                            {
                                var nid = list[i];
                                if (!nid || !nid.isSpawned) continue;
                                if (!nid.IsObserver(spawner)) continue;
                                onObserverAdded?.Invoke(spawner, nid);
                                nid.TriggerOnPreObserverAdded(spawner, true);
                                _triggerLateObserverAdded.Add(new PlayerNid { player = spawner, nid = nid, isSpawner = true });
                            }

                            var lastNid = list[count - 1];
                            if (lastNid && lastNid.id.HasValue)
                                _playersManager.RegisterClientLastId(spawner, lastNid.id.Value);

                            bool relayAsync = _relayAsyncSpawns.Count > 0 &&
                                              _relayAsyncSpawns.Remove(data.packetIdx);
                            RefreshVisibilityAfterRemoteSpawn(list[0], relayAsync);

                            DrainObserverEventsFor(list);
                            break;
                        }
                    }

                    bool isHost = IsServerHost();

                    // trigger spawn event
                    for (var i = 0; i < count; i++)
                    {
                        var nid = list[i];
                        if (!nid || !nid.isSpawned) continue;

                        nid.TriggerSpawnEvent(_asServer);
                        if (_asServer && isHost)
                            nid.TriggerSpawnEvent(false);
                        onIdentityAdded?.Invoke(nid);
                    }
                }
            }
            else
            {
                _pendingFinishSpawns.Add((data.packetIdx, player, asServer));
            }
        }

        private void DrainObserverEventsFor(DisposableList<NetworkIdentity> list)
        {
            for (int i = 0; i < _triggerLateObserverAdded.Count; i++)
            {
                var entry = _triggerLateObserverAdded[i];
                if (!ListContainsNid(list, entry.nid)) continue;
                if (!entry.nid || !entry.nid.isSpawned) continue;
                entry.nid.TriggerOnObserverAdded(entry.player, entry.isSpawner);
                onLateObserverAdded?.Invoke(entry.player, entry.nid);
            }
            for (int i = _triggerLateObserverAdded.Count - 1; i >= 0; i--)
            {
                if (ListContainsNid(list, _triggerLateObserverAdded[i].nid))
                    _triggerLateObserverAdded.RemoveAt(i);
            }
        }

        private static bool ListContainsNid(DisposableList<NetworkIdentity> list, NetworkIdentity target)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == target) return true;
            return false;
        }

        private void RefreshVisibilityAfterRemoteSpawn(NetworkIdentity root, bool relayAsync)
        {
            if (!root)
                return;

            if (relayAsync)
                ++_asyncVisibilityDepth;

            try
            {
                if (_scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
                {
                    for (var i = 0; i < players.Count; i++)
                        _visibility.RefreshVisibilityForGameObject(players[i], root);
                }
            }
            finally
            {
                if (relayAsync)
                    --_asyncVisibilityDepth;
            }

            FlushSpawnPackets();
        }

        private void ProcessBufferedFinishSpawnsFor(SpawnID packetIdx)
        {
            for (int i = _pendingFinishSpawns.Count - 1; i >= 0; i--)
            {
                var (idx, spawner, _) = _pendingFinishSpawns[i];
                if (!idx.Equals(packetIdx))
                    continue;

                _pendingFinishSpawns.RemoveAt(i);

                if (!_pendingSpawns.Remove(packetIdx, out var list))
                    return;

                if (_asyncPendingSpawns.Count > 0)
                    _asyncPendingSpawns.Remove(packetIdx);

                bool disposeList = true;
                try
                {
                    int count = list.Count;
                    // root destroyed before finishing: drop & dispose, never re-add (re-adding leaks the pooled list)
                    if (count > 0 && !list[0])
                        return;
                    if (count > 0 && !list[0].isSpawned)
                    {
                        _pendingSpawns.Add(packetIdx, list);
                        disposeList = false;
                        return;
                    }

                    if (count > 0 && list[0] && _asServer)
                    {
                        for (int j = 0; j < count; j++)
                        {
                            var nid = list[j];
                            if (!nid || !nid.isSpawned) continue;
                            if (!nid.IsObserver(spawner)) continue;
                            onObserverAdded?.Invoke(spawner, nid);
                            nid.TriggerOnPreObserverAdded(spawner, true);
                            _triggerLateObserverAdded.Add(new PlayerNid { player = spawner, nid = nid, isSpawner = true });
                        }

                        var lastNid = list[count - 1];
                        if (lastNid && lastNid.id.HasValue)
                            _playersManager.RegisterClientLastId(spawner, lastNid.id.Value);

                        bool relayAsync = _relayAsyncSpawns.Count > 0 &&
                                          _relayAsyncSpawns.Remove(packetIdx);
                        RefreshVisibilityAfterRemoteSpawn(list[0], relayAsync);

                        DrainObserverEventsFor(list);
                    }

                    bool isHost = IsServerHost();
                    for (int j = 0; j < count; j++)
                    {
                        var nid = list[j];
                        if (!nid || !nid.isSpawned) continue;
                        nid.TriggerSpawnEvent(_asServer);
                        if (_asServer && isHost)
                            nid.TriggerSpawnEvent(false);
                        onIdentityAdded?.Invoke(nid);
                    }
                }
                finally
                {
                    if (disposeList && !list.isDisposed)
                        list.Dispose();
                }
                return;
            }
        }

        private void ProcessBufferedDespawnsFor(GameObjectPrototype prototype)
        {
            for (int i = _pendingDespawns.Count - 1; i >= 0; i--)
            {
                var (_, packet, _) = _pendingDespawns[i];

                for (int j = 0; j < prototype.framework.Count; j++)
                {
                    var piece = prototype.framework[j];
                    if (piece.id != packet.parentId || !TryGetIdentity(piece.id, out var nid) || !nid)
                        continue;

                    _pendingDespawns.RemoveAt(i);
                    try
                    {
                        CancelPendingAsyncSpawnRoot(nid);
                        Despawn(nid.gameObject, true, true);
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogError($"ProcessBufferedDespawnsFor: exception despawning {nid.gameObject.name}: {e.Message}\n{e.StackTrace}");
                    }
                    return;
                }
            }
        }

        private bool RemoveBufferedDespawnsFor(NetworkID rootId)
        {
            bool removed = false;
            for (var i = _pendingDespawns.Count - 1; i >= 0; i--)
            {
                if (_pendingDespawns[i].packet.parentId != rootId)
                    continue;

                _pendingDespawns.RemoveAt(i);
                removed = true;
            }
            return removed;
        }

        private void OnPlayerUnloadedScene(PlayerID player, SceneID scene, bool asserver)
        {
            if (!asserver)
                return;

            if (scene != _sceneId)
                return;

            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var count = _spawnedIdentities.Count;

            for (var i = 0; i < count; i++)
            {
                var id = _spawnedIdentities[i];

                if (!id) continue;

                var root = id.GetRootIdentity();

                if (!root || !roots.Add(root))
                    continue;

                _visibility.ClearVisibilityForGameObject(root, player);
            }
            FlushSpawnPackets();
            HashSetPool<NetworkIdentity>.Destroy(roots);
        }

        private void OnSpawnPacket(PlayerID player, SpawnPacket data, bool asServer)
        {
            HandleSpawn(player, data, true);
        }

        private void HandleSpawn(PlayerID player, SpawnPacket data, bool flushData)
        {
            if (_asServer)
                data.packetIdx.scope = player;

            if (data.sceneId != _sceneId)
                return;

            switch (_asServer)
            {
                case true when !_manager.networkRules.HasSpawnAuthority(_manager, false):
                    PurrLogger.LogError($"Spawn failed from client due to lack of permissions.");
                    RollbackSpawnOnClient(player, data);
                    return;
                // when in host mode, let the server handle the spawning on their module
                case false when _manager.isServer:
                    return;
            }

            ReplacePartialLocalHierarchy(data.prototype);

            if (data.prototype.framework.Count > 0)
            {
                for (var i = 0; i < data.prototype.framework.Count; i++)
                {
                    var piece = data.prototype.framework[i];
                    if (TryGetIdentity(piece.id, out var existing))
                    {
                        PurrLogger.LogError(
                            $"Spawn failed for player `{player}`. Identity with id `{piece.id}` already exists: `{existing.gameObject.name}`",
                            existing);
                        RejectAsyncSpawn(data);
                        return;
                    }
                }
            }

            if (_asServer && _clientSpawnValidators.Count > 0)
            {
                for (var i = 0; i < _clientSpawnValidators.Count; i++)
                {
                    var validator = _clientSpawnValidators[i];
                    if (!validator(player, data))
                    {
                        var declaring = validator.Method.DeclaringType;
                        var methodName = validator.Method.Name;
                        if (data.prototype.framework.Count > 0 &&
                            _manager.prefabResolver.TryGetPrefabData(data.prototype.framework[0].pid.prefabId,
                                out var pdata) &&
                            pdata.prefab)
                        {
                            PurrLogger.LogWarning(
                                $"Spawn validation of `{pdata.prefab.name}` failed for player `{player}` by `{declaring?.Name}.{methodName}`");
                        }
                        else
                            PurrLogger.LogWarning(
                                $"Spawn validation failed for player `{player}` by `{declaring?.Name}.{methodName}`");

                        RollbackSpawnOnClient(player, data);
                        return;
                    }
                }
            }

            if (data.prototype.framework.Count > 0)
            {
                var rootPrefabId = data.prototype.framework[0].pid.prefabId;
                if (_manager.prefabResolver.NeedsLoad(rootPrefabId))
                {
                    if (data.isAsync)
                    {
                        PurrLogger.LogError(
                            $"InstantiateAsync spawn {data.packetIdx} was rejected because prefab {rootPrefabId} is not loaded on this peer. Preload it before instantiating so reliable ordered traffic can be applied immediately.");
                        RejectAsyncSpawn(data);
                        return;
                    }

                    ProcessSpawnWhenLoadedAsync(data, flushData, rootPrefabId);
                    return;
                }
            }

            CompleteReceivedSpawn(data, flushData);
        }

        private void ReplacePartialLocalHierarchy(GameObjectPrototype prototype)
        {
            if (_asServer || prototype.framework.Count <= 1)
                return;

            var rootId = prototype.framework[0].id;
            if (!TryGetIdentity(rootId, out var existingRoot))
                return;

            var existingPieces = 0;
            for (var i = 0; i < prototype.framework.Count; i++)
            {
                if (TryGetIdentity(prototype.framework[i].id, out _))
                    existingPieces++;
            }

            if (existingPieces >= prototype.framework.Count)
                return;

            Despawn(existingRoot.gameObject, true, true);
        }

        private async void ProcessSpawnWhenLoadedAsync(SpawnPacket data, bool flushData, PrefabID rootPrefabId)
        {
            try
            {
                var prototypeCopy = data.prototype.Clone();
                var customDataCopy = data.customData.Duplicate();
                var packetIdx = data.packetIdx;
                var sceneId = data.sceneId;
                var bypassPool = data.bypassPool;
                var isAsync = data.isAsync;

                try
                {
                    var loaded = await _manager.prefabResolver.LoadPrefabAsync(rootPrefabId);
                    if (loaded.prefab == null)
                    {
                        PurrLogger.LogError($"ProcessSpawnWhenLoadedAsync: failed to load prefab {rootPrefabId}.");
                        RejectDeferredAsyncSpawn(packetIdx, sceneId, isAsync, prototypeCopy);
                        prototypeCopy.Dispose();
                        customDataCopy.Dispose();
                        return;
                    }

                    if (_isDisposed || !_enabled)
                    {
                        prototypeCopy.Dispose();
                        customDataCopy.Dispose();
                        return;
                    }

                    var spawnData = new SpawnPacket
                    {
                        sceneId = sceneId,
                        packetIdx = packetIdx,
                        bypassPool = bypassPool,
                        isAsync = isAsync,
                        prototype = prototypeCopy,
                        customData = customDataCopy
                    };
                    CompleteReceivedSpawn(spawnData, flushData);
                    spawnData.Dispose();
                }
                catch (Exception e)
                {
                    PurrLogger.LogError($"ProcessSpawnWhenLoadedAsync: exception for prefab {rootPrefabId}: {e.Message}\n{e.StackTrace}");
                    RejectDeferredAsyncSpawn(packetIdx, sceneId, isAsync, prototypeCopy);
                    try { prototypeCopy.Dispose(); } catch { /* ignore */ }
                    try { customDataCopy.Dispose(); } catch { /* ignore */ }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void RejectDeferredAsyncSpawn(SpawnID packetIdx, SceneID sceneId, bool isAsync,
            GameObjectPrototype prototype)
        {
            if (!isAsync || _isDisposed)
                return;

            var packet = new SpawnPacket
            {
                packetIdx = packetIdx,
                sceneId = sceneId,
                isAsync = true,
                prototype = prototype
            };
            RejectAsyncSpawn(packet);
        }

        private void RejectAsyncSpawn(SpawnPacket packet)
        {
            if (!packet.isAsync)
                return;

            if (_asServer)
                RollbackSpawnOnClient(packet.packetIdx.scope, packet);
            else
                SendAsyncSpawnFailure(packet);
        }

        private void CompleteReceivedSpawn(SpawnPacket data, bool flushData)
        {
            if (!data.isAsync)
            {
                CompleteSpawn(data, flushData, data.bypassPool);
                return;
            }

            if (_asServer)
            {
                // Client-authoritative async spawns are integrated synchronously on the server so
                // reliable packets sent immediately after the source operation cannot overtake the
                // server identity. They still bypass pooling and relay asynchronously to observers.
                _relayAsyncSpawns.Add(data.packetIdx);
                if (!CompleteSpawn(data, flushData, true))
                {
                    _relayAsyncSpawns.Remove(data.packetIdx);
                    RollbackSpawnOnClient(data.packetIdx.scope, data);
                }
                return;
            }

#if PURRNET_UNITY_INSTANTIATE_ASYNC
            BeginAsyncRemoteSpawn(data, flushData);
#else
            PurrLogger.LogError("Received an asynchronous spawn on a Unity version that does not support Object.InstantiateAsync.");
            SendAsyncSpawnFailure(data);
#endif
        }

        private bool CompleteSpawn(SpawnPacket data, bool flushData, bool forceUnpooled = false)
        {
            if (forceUnpooled)
                _hasConfiguredPoolBypass = true;

            var createdNids = DisposableList<NetworkIdentity>.Create(16);
            var go = forceUnpooled
                ? CreateUnpooledPrototype(data.prototype, createdNids.list)
                : CreatePrototype(data.prototype, createdNids.list);

            if (!go || createdNids.Count == 0)
            {
                PurrLogger.LogError($"CompleteSpawn: CreatePrototype failed for packet {data.packetIdx}.");
                createdNids.Dispose();
                if (go)
                    UnityProxy.DestroyDirectly(go);
                return false;
            }

            return CompleteSpawnWithInstance(data, flushData, go, createdNids);
        }

        private bool CompleteSpawnWithInstance(SpawnPacket data, bool flushData, GameObject go,
            DisposableList<NetworkIdentity> createdNids)
        {
            bool hasCustomData = data.customData.bitLength > 0;
            bool ownsPendingEntry = false;

            try
            {
                onPreSpawn?.Invoke(go, false);
                using var scope = data.customData.AutoScope();

                bool isHost = _asServer && IsServerHost();
                var spawner = data.packetIdx.scope;

                for (var i = 0; i < createdNids.Count; i++)
                {
                    var nid = createdNids[i];
                    if (_failedAsyncSpawnRoots.Count > 0 && nid.id.HasValue)
                        _failedAsyncSpawnRoots.Remove(nid.id.Value);
                    nid.SetIdentity(_manager, this, _sceneId, _asServer, isHost);
                    RegisterIdentity(nid, false, false);
                }

                if (hasCustomData)
                {
                    for (var i = 0; i < createdNids.Count; i++)
                    {
                        var nid = createdNids[i];
                        if (nid)
                            nid.TriggerOnDeserialize(data.customData.packer);
                    }
                }

                for (var i = 0; i < createdNids.Count; i++)
                {
                    var nid = createdNids[i];
                    if (!nid)
                        continue;

                    TriggerEarlySpawnForRegisteredIdentity(nid);

                    if (_asServer)
                        nid.TryAddObserver(spawner);
                }

                for (var i = 0; i < createdNids.Count; i++)
                {
                    var nid = createdNids[i];
                    if (nid)
                        nid.TriggerOnSpawnReceived();
                }

                if (!_pendingSpawns.TryAdd(data.packetIdx, createdNids))
                {
                    PurrLogger.LogError($"CompleteSpawn: failed to add spawn packet {data.packetIdx} to pending spawns.");
                    RollbackFailedSpawn(data.packetIdx, go, createdNids, false);
                    return false;
                }
                ownsPendingEntry = true;

                if (data.isAsync && !_asServer)
                    _asyncPendingSpawns.Add(data.packetIdx);

                if (data.isAsync && !_asServer)
                    SendAsyncSpawnReady(data.packetIdx, true);

#if PURRNET_UNITY_INSTANTIATE_ASYNC
                if (_pendingAsyncInstantiations.Count > 0)
                    ProcessAsyncInstantiationsWaitingForParents();
                AttachOrphansWaitingForAsyncParent();
#endif
                ProcessBufferedFinishSpawnsFor(data.packetIdx);
                ProcessBufferedDespawnsFor(data.prototype);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"CompleteSpawn: exception for packet {data.packetIdx}: {e.Message}\n{e.StackTrace}");

                // A buffered Finish may already have removed the entry and completed the spawn.
                // In that case an exception came from user spawn callbacks; the network transaction
                // is complete and must not be retroactively destroyed.
                if (ownsPendingEntry && !_pendingSpawns.ContainsKey(data.packetIdx))
                    return true;

                RollbackFailedSpawn(data.packetIdx, go, createdNids, ownsPendingEntry);
                return false;
            }

            if (flushData)
                FlushSpawnPackets();
            return true;
        }

        private void RollbackFailedSpawn(SpawnID packetIdx, GameObject go,
            DisposableList<NetworkIdentity> createdNids, bool ownsPendingEntry)
        {
            DisposableList<NetworkIdentity> pendingNids = default;
            if (ownsPendingEntry)
                _pendingSpawns.Remove(packetIdx, out pendingNids);

            _asyncPendingSpawns.Remove(packetIdx);
            _relayAsyncSpawns.Remove(packetIdx);

            if (!createdNids.isDisposed)
            {
                for (var i = 0; i < createdNids.Count; i++)
                    RollbackFailedIdentity(createdNids[i]);
            }
            else if (!pendingNids.isDisposed)
            {
                for (var i = 0; i < pendingNids.Count; i++)
                    RollbackFailedIdentity(pendingNids[i]);
            }
            else if (go)
            {
                var identities = ListPool<NetworkIdentity>.Instantiate();
                go.GetComponentsInChildren(true, identities);
                for (var i = 0; i < identities.Count; i++)
                    RollbackFailedIdentity(identities[i]);
                ListPool<NetworkIdentity>.Destroy(identities);
            }

            if (!pendingNids.isDisposed)
                pendingNids.Dispose();
            if (!createdNids.isDisposed)
                createdNids.Dispose();

            if (go)
                UnityProxy.DestroyDirectly(go);
        }

        private void RollbackFailedIdentity(NetworkIdentity identity)
        {
            if (!identity)
                return;

            _toSpawnNextFrame.Remove(identity);
            _toSpawnNextFrameBuffer.Remove(identity);
            for (var i = _triggerLateObserverAdded.Count - 1; i >= 0; i--)
            {
                if (_triggerLateObserverAdded[i].nid == identity)
                    _triggerLateObserverAdded.RemoveAt(i);
            }

            _spawnedIdentities.Remove(identity);
            if (!identity.id.HasValue ||
                !_spawnedIdentitiesMap.TryGetValue(identity.id.Value, out var registered) ||
                !ReferenceEquals(registered, identity))
                return;

            _spawnedIdentitiesMap.Remove(identity.id.Value);
            try
            {
                onIdentityRemoved?.Invoke(identity);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"CompleteSpawn: exception while rolling back '{identity.name}': {e.Message}\n{e.StackTrace}", identity);
            }
        }

        private void SendAsyncSpawnReady(SpawnID packetIdx, bool success)
        {
            if (_asServer || !_enabled || _isDisposed)
                return;

            var packet = new AsyncSpawnReadyPacket
            {
                sceneId = _sceneId,
                packetIdx = packetIdx,
                success = success
            };

            if (debugHoldAsyncSpawnReadySeconds > 0f)
            {
                _heldAsyncSpawnReadies.Add((Time.realtimeSinceStartup + debugHoldAsyncSpawnReadySeconds, packet));
                return;
            }

            _playersManager.SendToServer(packet);
        }

        private void FlushHeldAsyncSpawnReadies()
        {
            if (_asServer || _heldAsyncSpawnReadies.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            for (var i = 0; i < _heldAsyncSpawnReadies.Count; i++)
            {
                if (now < _heldAsyncSpawnReadies[i].dueTime)
                    continue;

                if (_enabled && !_isDisposed)
                    _playersManager.SendToServer(_heldAsyncSpawnReadies[i].packet);
                _heldAsyncSpawnReadies.RemoveAt(i--);
            }
        }

        private void SendAsyncSpawnFailure(SpawnPacket packet)
        {
            if (packet.prototype.framework.Count > 0)
            {
                var rootId = packet.prototype.framework[0].id;
                _failedAsyncSpawnRoots.Add(rootId);
#if PURRNET_UNITY_INSTANTIATE_ASYNC
                FailAsyncInstantiationsWaitingForParent(rootId);
#endif
                if (RemoveBufferedDespawnsFor(rootId))
                    _failedAsyncSpawnRoots.Remove(rootId);
            }
            SendAsyncSpawnReady(packet.packetIdx, false);
        }

#if PURRNET_UNITY_INSTANTIATE_ASYNC
        private void BeginAsyncRemoteSpawn(SpawnPacket data, bool flushData)
        {
            if (data.prototype.framework.Count == 0)
            {
                SendAsyncSpawnFailure(data);
                return;
            }

            var reserved = ListPool<NetworkID>.Instantiate();
            for (var i = 0; i < data.prototype.framework.Count; i++)
            {
                var id = data.prototype.framework[i].id;
                if (_spawnedIdentitiesMap.ContainsKey(id) || !_reservedAsyncNetworkIds.Add(id))
                {
                    for (var j = 0; j < reserved.Count; j++)
                        _reservedAsyncNetworkIds.Remove(reserved[j]);
                    ListPool<NetworkID>.Destroy(reserved);
                    PurrLogger.LogError($"Async spawn packet {data.packetIdx} contains an identity id that is already active or pending: {id}.");
                    SendAsyncSpawnFailure(data);
                    return;
                }
                reserved.Add(id);
            }
            ListPool<NetworkID>.Destroy(reserved);

            var prefabId = data.prototype.framework[0].pid.prefabId;
            if (!_manager.prefabResolver.TryGetPrefabData(prefabId, out var prefabData) || !prefabData.prefab)
            {
                ReleaseAsyncReservations(data);
                SendAsyncSpawnFailure(data);
                return;
            }

            var packetCopy = new SpawnPacket
            {
                sceneId = data.sceneId,
                packetIdx = data.packetIdx,
                bypassPool = true,
                isAsync = true,
                prototype = data.prototype.Clone(),
                customData = data.customData.Duplicate()
            };

            var rootId = packetCopy.prototype.framework[0].id;
            var state = new PendingAsyncInstantiation
            {
                packet = packetCopy,
                flushData = flushData
            };

            try
            {
                state.operation = UnityProxy.InstantiateAsyncDirectly(prefabData.prefab);
                _pendingAsyncInstantiations.Add(rootId, state);
                state.operation.completed += _ => OnAsyncRemoteInstantiateCompleted(rootId, state, prefabData.prefab);
            }
            catch (Exception e)
            {
                _pendingAsyncInstantiations.Remove(rootId);
                ReleaseAsyncReservations(packetCopy);
                state.DisposePacket();
                PurrLogger.LogError($"Failed to start remote InstantiateAsync for `{prefabData.prefab.name}`: {e.Message}");
                SendAsyncSpawnFailure(data);
            }
        }

        private void OnAsyncRemoteInstantiateCompleted(NetworkID rootId, PendingAsyncInstantiation state,
            GameObject prefab)
        {
            GameObject result = null;
            try
            {
                var results = state.operation.Result;
                if (results != null && results.Length > 0)
                    result = results[0];

                if (results != null)
                {
                    for (var i = 1; i < results.Length; i++)
                    {
                        if (results[i])
                            UnityProxy.DestroyDirectly(results[i]);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is an expected outcome when visibility is revoked mid-operation.
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Remote InstantiateAsync for `{prefab.name}` failed: {e.Message}");
            }

            if (state.cancelled ||
                !_pendingAsyncInstantiations.TryGetValue(rootId, out var current) || current != state)
            {
                if (result)
                    UnityProxy.DestroyDirectly(result);
                return;
            }

            if (!_enabled || _isDisposed)
            {
                _pendingAsyncInstantiations.Remove(rootId);
                ReleaseAsyncReservations(state.packet);
                if (result)
                    UnityProxy.DestroyDirectly(result);
                state.DisposePacket();
                return;
            }

            if (!result)
            {
                FailPendingAsyncInstantiation(rootId, state, null);
                return;
            }

            var prefabId = state.packet.prototype.framework[0].pid.prefabId;
            var identities = ListPool<NetworkIdentity>.Instantiate();
            try
            {
                result.GetComponentsInChildren(true, identities);
                NetworkManager.SetupPrefabInfo(result, prefabId, false, identities);
                if (!HasMatchingAsyncNetworkShape(prefab, result, identities, out var mismatch))
                {
                    ReportAsyncShapeMismatch(prefab, result, mismatch);
                    FailPendingAsyncInstantiation(rootId, state, result);
                    return;
                }
            }
            finally
            {
                ListPool<NetworkIdentity>.Destroy(identities);
            }

            state.result = result;
            TryCompletePendingAsyncInstantiation(rootId, state);
        }

        private void TryCompletePendingAsyncInstantiation(NetworkID rootId, PendingAsyncInstantiation state)
        {
            if (state.cancelled || !state.result || !_enabled || _isDisposed ||
                !_pendingAsyncInstantiations.TryGetValue(rootId, out var current) || current != state)
                return;

            if (state.packet.prototype.parentID.HasValue &&
                !TryGetIdentity(state.packet.prototype.parentID.Value, out _))
            {
                if (_failedAsyncSpawnRoots.Contains(state.packet.prototype.parentID.Value))
                    FailPendingAsyncInstantiation(rootId, state, state.result);
                return;
            }

            ReleaseAsyncReservations(state.packet);

            for (var i = 0; i < state.packet.prototype.framework.Count; i++)
            {
                if (!TryGetIdentity(state.packet.prototype.framework[i].id, out _))
                    continue;
                FailPendingAsyncInstantiation(rootId, state, state.result);
                return;
            }

            var createdNids = DisposableList<NetworkIdentity>.Create(16);
            if (!TryApplyPrototypeToExisting(state.result, state.packet.prototype, createdNids.list,
                    out var shouldActivate))
            {
                createdNids.Dispose();
                PurrLogger.LogError(
                    $"`InstantiateAsync` could not apply spawn packet {state.packet.packetIdx} because the receiver got a partial or mismatched NetworkIdentity hierarchy.",
                    state.result);
                FailPendingAsyncInstantiation(rootId, state, state.result);
                return;
            }

            var result = FinalizePrototypeInstance(state.result, state.packet.prototype, shouldActivate);
            _pendingAsyncInstantiations.Remove(rootId);
            state.result = null;

            _hasConfiguredPoolBypass = true;
            bool completed = CompleteSpawnWithInstance(state.packet, state.flushData, result, createdNids);
            if (!completed)
            {
                DropOrphansWaitingForAsyncParent(state.packet);
                SendAsyncSpawnFailure(state.packet);
            }
            state.DisposePacket();
        }

        private void AttachOrphansWaitingForAsyncParent()
        {
            if (_orphansWaitingForAsyncParent.Count == 0)
                return;

            List<NetworkID> readyParents = null;
            foreach (var pair in _orphansWaitingForAsyncParent)
            {
                if (!TryGetIdentity(pair.Key, out _))
                    continue;
                readyParents ??= ListPool<NetworkID>.Instantiate();
                readyParents.Add(pair.Key);
            }

            if (readyParents == null)
                return;

            for (var i = 0; i < readyParents.Count; i++)
            {
                if (!_orphansWaitingForAsyncParent.Remove(readyParents[i], out var orphans))
                    continue;

                if (TryGetIdentity(readyParents[i], out var parent) && parent)
                {
                    // Same sequence as the FinalizePrototypeInstance happy path: parked children
                    // attach in arrival (server spawn) order, so their recorded sibling indices
                    // apply without clamping.
                    for (var j = 0; j < orphans.Count; j++)
                    {
                        var orphan = orphans[j];
                        var identity = orphan.identity;
                        if (!identity || !identity.isSpawned)
                            continue;

                        identity.transform.SetParent(parent.transform, false);
                        ApplyParentChange(identity, parent, orphan.path, false);
                        SetLocalPosAndRot(identity.transform, orphan.position, orphan.rotation, orphan.scale);
                    }
                }

                ListPool<OrphanAwaitingAsyncParent>.Destroy(orphans);
            }

            ListPool<NetworkID>.Destroy(readyParents);
        }

        private void DropOrphansWaitingForAsyncParent(SpawnPacket packet)
        {
            if (_orphansWaitingForAsyncParent.Count == 0)
                return;

            for (var i = 0; i < packet.prototype.framework.Count; i++)
            {
                if (!_orphansWaitingForAsyncParent.Remove(packet.prototype.framework[i].id, out var orphans))
                    continue;

                for (var j = 0; j < orphans.Count; j++)
                {
                    var identity = orphans[j].identity;
                    if (identity)
                    {
                        PurrLogger.LogError(
                            $"Failed to find parent for '{identity.name}' with id " +
                            $"'{packet.prototype.framework[i].id}'.",
                            identity);
                    }
                }

                ListPool<OrphanAwaitingAsyncParent>.Destroy(orphans);
            }
        }

        private void ProcessAsyncInstantiationsWaitingForParents()
        {
            if (_pendingAsyncInstantiations.Count == 0)
                return;

            var ready = ListPool<(NetworkID id, PendingAsyncInstantiation state)>.Instantiate();
            foreach (var pair in _pendingAsyncInstantiations)
            {
                var state = pair.Value;
                if (!state.result)
                    continue;
                if (!state.packet.prototype.parentID.HasValue ||
                    TryGetIdentity(state.packet.prototype.parentID.Value, out _))
                    ready.Add((pair.Key, state));
            }

            for (var i = 0; i < ready.Count; i++)
                TryCompletePendingAsyncInstantiation(ready[i].id, ready[i].state);
            ListPool<(NetworkID id, PendingAsyncInstantiation state)>.Destroy(ready);
        }

        private void FailPendingAsyncInstantiation(NetworkID rootId, PendingAsyncInstantiation state,
            GameObject result)
        {
            _pendingAsyncInstantiations.Remove(rootId);
            ReleaseAsyncReservations(state.packet);
            if (result)
                UnityProxy.DestroyDirectly(result);
            state.result = null;
            DropOrphansWaitingForAsyncParent(state.packet);
            SendAsyncSpawnFailure(state.packet);
            state.DisposePacket();
        }

        private void FailAsyncInstantiationsWaitingForParent(NetworkID failedParent)
        {
            if (_pendingAsyncInstantiations.Count == 0)
                return;

            var dependants = ListPool<(NetworkID id, PendingAsyncInstantiation state)>.Instantiate();
            foreach (var pair in _pendingAsyncInstantiations)
            {
                if (pair.Value.packet.prototype.parentID == failedParent)
                    dependants.Add((pair.Key, pair.Value));
            }

            for (var i = 0; i < dependants.Count; i++)
            {
                var dependant = dependants[i];
                if (_pendingAsyncInstantiations.TryGetValue(dependant.id, out var current) &&
                    current == dependant.state)
                    FailPendingAsyncInstantiation(dependant.id, dependant.state, dependant.state.result);
            }
            ListPool<(NetworkID id, PendingAsyncInstantiation state)>.Destroy(dependants);
        }

        private void ReleaseAsyncReservations(SpawnPacket packet)
        {
            for (var i = 0; i < packet.prototype.framework.Count; i++)
                _reservedAsyncNetworkIds.Remove(packet.prototype.framework[i].id);
        }
#endif

        private bool TryCancelPendingAsyncInstantiation(NetworkID rootId)
        {
#if PURRNET_UNITY_INSTANTIATE_ASYNC
            if (_pendingAsyncInstantiations.Count == 0)
                return false;

            if (!_pendingAsyncInstantiations.Remove(rootId, out var state))
                return false;

            state.cancelled = true;
            var packetIdx = state.packet.packetIdx;
            _failedAsyncSpawnRoots.Add(rootId);
            ReleaseAsyncReservations(state.packet);
            try
            {
                state.operation?.Cancel();
            }
            catch (Exception e)
            {
                PurrLogger.LogWarning($"Cancelling remote InstantiateAsync failed: {e.Message}");
            }

            if (state.result)
                UnityProxy.DestroyDirectly(state.result);
            state.result = null;
            FailAsyncInstantiationsWaitingForParent(rootId);
            _failedAsyncSpawnRoots.Remove(rootId);
            DropOrphansWaitingForAsyncParent(state.packet);
            state.DisposePacket();

            for (var i = _pendingFinishSpawns.Count - 1; i >= 0; i--)
            {
                if (_pendingFinishSpawns[i].packetIdx.Equals(packetIdx))
                    _pendingFinishSpawns.RemoveAt(i);
            }
            return true;
#else
            return false;
#endif
        }

        private void RollbackSpawnOnClient(PlayerID player, SpawnPacket data)
        {
            if (_asServer)
                _cancelledPendingSpawns.Add(data.packetIdx);

            if (data.prototype.framework.Count > 0)
            {
                var packet = new DespawnPacket
                {
                    sceneId = _sceneId,
                    parentId = data.prototype.framework[0].id
                };
                _playersManager.Send(player, packet);
            }
        }

        private void OnDespawnPacket(PlayerID player, DespawnPacket data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;

            if (!asServer && _manager.isServer)
            {
                // when in host mode, let the server handle the despawn on their module
                return;
            }

            if (!_asServer && _failedAsyncSpawnRoots.Count > 0 &&
                _failedAsyncSpawnRoots.Remove(data.parentId))
                return;

            if (!_asServer && TryCancelPendingAsyncInstantiation(data.parentId))
                return;

            if (!TryGetIdentity(data.parentId, out var identity))
            {
                if (!_asServer && !ConsumePendingLocalDespawnEcho(data.parentId))
                    _pendingDespawns.Add((player, data, asServer));
                return;
            }

            if (_asServer && !identity.HasDespawnAuthority(player, !_asServer))
            {
                PurrLogger.LogError($"Despawn failed for '{identity.gameObject.name}' due to lack of permissions.",
                    identity.gameObject);
                return;
            }

            CancelPendingAsyncSpawnRoot(identity);
            Despawn(identity.gameObject, true, true);
        }

        private bool ConsumePendingLocalDespawnEcho(NetworkID identityId)
        {
            if (_asServer || _pendingLocalDespawnEchoes.isDisposed ||
                !_pendingLocalDespawnEchoes.Remove(identityId))
                return false;

            if (_pendingLocalDespawnEchoes.Count == 0)
                _pendingLocalDespawnEchoes.Dispose();
            return true;
        }

        private void CancelPendingAsyncSpawnRoot(NetworkIdentity identity)
        {
            if (_asyncPendingSpawns.Count == 0 || !identity)
                return;

            // A nested despawn is part of the staged transaction: FinishSpawn must still
            // complete its surviving identities. Only cancelling the async root removes
            // the whole transaction because the server may intentionally omit its Finish.
            SpawnID found = default;
            DisposableList<NetworkIdentity> list = default;
            bool hasFound = false;

            foreach (var packetIdx in _asyncPendingSpawns)
            {
                if (!_pendingSpawns.TryGetValue(packetIdx, out var pending) ||
                    pending.Count == 0 || pending[0] != identity)
                    continue;

                found = packetIdx;
                list = pending;
                hasFound = true;
                break;
            }

            if (!hasFound)
                return;

            _pendingSpawns.Remove(found);
            if (!list.isDisposed)
                list.Dispose();
            _asyncPendingSpawns.Remove(found);

            for (var i = _pendingFinishSpawns.Count - 1; i >= 0; i--)
            {
                if (_pendingFinishSpawns[i].packetIdx.Equals(found))
                    _pendingFinishSpawns.RemoveAt(i);
            }
        }

        /// <summary>
        /// Evaluates the visibility of all spawned network identities for all players in the current scene.
        /// This operation gathers the list of players currently present in the scene and applies a visibility evaluation
        /// for each player based on the active set of spawned network identities.
        /// Intended to be called when visibility recalculations are required, such as after significant state changes.
        /// </summary>
        public void EvaluateAllVisibilities()
        {
            if (_asServer && _scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
                _visibility.EvaluateAll(players, _spawnedIdentities);
            FlushSpawnPackets();
        }

        private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asserver)
        {
            if (!_asServer)
                return;

            if (scene != _sceneId)
                return;

            if (IsServerHost() && _manager.localPlayer == player)
                CatchupClient(player);

            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var count = _spawnedIdentities.Count;

            for (var i = 0; i < count; i++)
            {
                var id = _spawnedIdentities[i];

                if (!id) continue;

                if (id.isManualSpawn)
                    continue;

                var root = id.GetRootIdentity();

                if (!root || !roots.Add(root))
                    continue;

                _visibility.RefreshVisibilityForGameObject(player, root);
            }

            FlushSpawnPackets();
            SendSceneSpawnReconcile(player);
            HashSetPool<NetworkIdentity>.Destroy(roots);
        }

        private void SendSceneSpawnReconcile(PlayerID player)
        {
            if (!_asServer)
                return;

            _playersManager.Send(player, new SceneSpawnReconcilePacket
            {
                sceneId = _sceneId
            });
        }

        public void EvaluateVisibilityForPlayer(PlayerID player)
        {
            if (!_asServer || !_scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                return;

            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var count = _spawnedIdentities.Count;

            for (var i = 0; i < count; i++)
            {
                var id = _spawnedIdentities[i];
                if (!id || id.isManualSpawn) continue;
                var root = id.GetRootIdentity();
                if (root && roots.Add(root))
                    _visibility.RefreshVisibilityForGameObject(player, root);
            }

            FlushSpawnPackets();
            HashSetPool<NetworkIdentity>.Destroy(roots);
        }

        /// <summary>
        /// Evaluates the visibility of a hierarchy of objects rooted at the specified transform
        /// for all players currently present in the associated scene. This operation is intended
        /// to be used on the server to ensure that visibility states are up-to-date for all relevant players.
        /// </summary>
        /// <param name="root">The root transform of the hierarchy of objects to evaluate visibility for.</param>
        public void EvaluateVisibility(Transform root)
        {
            if (_asServer && _scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                var identity = root ? root.GetComponent<NetworkIdentity>() : null;
                for (var index = 0; index < players.Count; index++)
                {
                    var player = players[index];
                    _visibility.RefreshVisibilityForGameObject(player, identity);
                }

                FlushSpawnPackets();
            }
        }

        /// <summary>
        /// Evaluates the visibility of the specified root transform for a given player.
        /// This method checks if the player is loaded into the current scene and refreshes the visibility
        /// of the specified GameObject hierarchy. It is generally used to update client visibility
        /// when changes occur in the scene or the player's network state.
        /// </summary>
        /// <param name="player">The unique identifier of the player for whom the visibility is being evaluated.</param>
        /// <param name="root">The root transform of the GameObject hierarchy whose visibility is being evaluated.</param>
        public void EvaluateVisibility(PlayerID player, Transform root)
        {
            if (_asServer && _scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                _visibility.RefreshVisibilityForGameObject(player, root);
            FlushSpawnPackets();
        }

        private ulong _nextPacketIdx;

        struct PlayerNid
        {
            public PlayerID player;
            public NetworkIdentity nid;
            public bool isSpawner;
        }

        private readonly List<PlayerNid> _triggerLateObserverAdded = new List<PlayerNid>();
        private readonly Dictionary<PlayerID, SpawnPacketBatch> _spawnPackets = new();

        private void ClearPendingLateObserverAdded(PlayerID player, NetworkIdentity id)
        {
            for (var i = 0; i < _triggerLateObserverAdded.Count; i++)
            {
                if (_triggerLateObserverAdded[i].player == player && _triggerLateObserverAdded[i].nid == id)
                    _triggerLateObserverAdded.RemoveAt(i--);
            }
        }

        private bool MoveObserversToAsyncPending(SpawnID spawnId, PlayerID player,
            List<NetworkIdentity> identities)
        {
            var pendingIdentities = new List<NetworkIdentity>(identities.Count);
            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (identity && identity.TryMoveObserverToPending(player))
                    pendingIdentities.Add(identity);
            }

            if (pendingIdentities.Count == 0)
                return false;

            _pendingAsyncObservers[spawnId] = new PendingAsyncObserverSpawn(player, pendingIdentities);
            return true;
        }

        private void RemovePendingAsyncObservers(PlayerID player, List<NetworkIdentity> identities,
            HashSet<NetworkIdentity> unconfirmed = null, List<NetworkIdentity> cancelledRoots = null,
            List<NetworkIdentity> confirmedRemoved = null)
        {
            if (_pendingAsyncObservers.Count == 0 && _readyAsyncObservers.Count == 0)
                return;

            var pendingIds = ListPool<SpawnID>.Instantiate();
            var readyIds = ListPool<SpawnID>.Instantiate();

            foreach (var pair in _pendingAsyncObservers)
            {
                var pending = pair.Value;
                if (pending.player == player && AsyncTransactionIntersects(pending.identities, identities))
                    pendingIds.Add(pair.Key);
            }

            foreach (var pair in _readyAsyncObservers)
            {
                var ready = pair.Value;
                if (ready.player == player && AsyncTransactionIntersects(ready.identities, identities))
                    readyIds.Add(pair.Key);
            }

            for (var keyIndex = 0; keyIndex < pendingIds.Count; keyIndex++)
            {
                if (!_pendingAsyncObservers.Remove(pendingIds[keyIndex], out var pending))
                    continue;

                AddAsyncTransactionRoot(pending, cancelledRoots);

                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (identity)
                    {
                        unconfirmed?.Add(identity);
                        identity.TryRemovePendingObserver(player);
                    }
                }
            }

            for (var keyIndex = 0; keyIndex < readyIds.Count; keyIndex++)
            {
                var key = readyIds[keyIndex];
                if (!_readyAsyncObservers.Remove(key, out var ready))
                    continue;
                _toCompleteNextFrame.Remove(key);
                AddAsyncTransactionRoot(ready, cancelledRoots);

                for (var i = 0; i < ready.identities.Count; i++)
                {
                    var identity = ready.identities[i];
                    if (identity && identity.TryRemoveObserver(player))
                        confirmedRemoved?.Add(identity);
                }
            }

            ListPool<SpawnID>.Destroy(pendingIds);
            ListPool<SpawnID>.Destroy(readyIds);
        }

        private static void AddAsyncTransactionRoot(PendingAsyncObserverSpawn pending,
            List<NetworkIdentity> roots)
        {
            if (roots == null || pending.identities.Count == 0)
                return;

            var root = pending.identities[0];
            if (root && !roots.Contains(root))
                roots.Add(root);
        }

        private void ConsumeFailedAsyncObserverRoots(PlayerID player, List<NetworkIdentity> candidates,
            List<NetworkIdentity> failedRoots, HashSet<NetworkIdentity> unconfirmed = null)
        {
            if (_failedAsyncObserverRoots.Count == 0)
                return;

            for (var i = 0; i < candidates.Count; i++)
            {
                var current = candidates[i];
                while (current)
                {
                    if (current.id.HasValue &&
                        _failedAsyncObserverRoots.Remove((player, current.id.Value)) &&
                        !failedRoots.Contains(current))
                        failedRoots.Add(current);
                    current = current.parent;
                }
            }

            var transaction = ListPool<NetworkIdentity>.Instantiate();
            for (var i = 0; i < failedRoots.Count; i++)
            {
                var root = failedRoots[i];
                if (!root)
                    continue;
                transaction.Clear();
                GetComponentsInChildren(root.gameObject, transaction);
                for (var j = 0; j < transaction.Count; j++)
                {
                    var member = transaction[j];
                    if (!member)
                        continue;
                    member.TryRemovePendingObserver(player);
                    unconfirmed?.Add(member);
                }
            }
            ListPool<NetworkIdentity>.Destroy(transaction);
        }

        private static bool AsyncTransactionIntersects(List<NetworkIdentity> transaction,
            List<NetworkIdentity> identities)
        {
            for (var i = 0; i < identities.Count; i++)
            {
                if (transaction.Contains(identities[i]))
                    return true;
            }
            return false;
        }

        private void OnAsyncSpawnReadyPacket(PlayerID player, AsyncSpawnReadyPacket data, bool asServer)
        {
            if (!_asServer || data.sceneId != _sceneId)
                return;

            data.packetIdx = RestoreTarget(data.packetIdx, player);

            if (!_pendingAsyncObservers.TryGetValue(data.packetIdx, out var pending) ||
                pending.player != player || !pending.sent)
                return;

            _pendingAsyncObservers.Remove(data.packetIdx);

            if (!data.success)
            {
                var root = MarkAsyncObserverSpawnFailed(pending);
                // Clear the receiver's failure tombstone and any dependent staged spawns. The
                // identities remain pending until visibility turns false, suppressing retries.
                if (root)
                    SendDespawnPacket(player, root, false);
                return;
            }

            // Make the ready transaction visible before invoking user callbacks. A callback may
            // remove visibility or despawn the root; that cancellation must suppress FinishSpawn.
            _readyAsyncObservers[data.packetIdx] = pending;

            _asyncObserverPromotionDepth++;
            try
            {
                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (!identity || !identity.isSpawned || !identity.TryPromotePendingObserver(player))
                        continue;

                    onObserverAdded?.Invoke(player, identity);
                    identity.TriggerOnPreObserverAdded(player, false);
                    _triggerLateObserverAdded.Add(new PlayerNid
                    {
                        player = player,
                        nid = identity,
                        isSpawner = false
                    });
                }

                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (!identity || !identity.id.HasValue ||
                        identity.gameObject.GetComponent<NetworkIdentity>() != identity)
                        continue;

                    _playersManager.Send(player, new ChangeParentPacket
                    {
                        sceneId = _sceneId,
                        childId = identity.id.Value,
                        newParentId = identity.parent ? identity.parent.id : null,
                        path = identity.invertedPathToNearestParentArray,
                        worldPositionStays = false
                    });
                }

                for (var i = 0; i < pending.identities.Count; i++)
                {
                    var identity = pending.identities[i];
                    if (identity && identity.id.HasValue && identity.IsObserver(player))
                        onSentSpawnPacket?.Invoke(player, _sceneId, identity.id.Value);
                }

                // Finish must be sent only after observer-state packets produced above have flushed,
                // and only if no callback cancelled the transaction while it was being promoted.
                if (_readyAsyncObservers.ContainsKey(data.packetIdx))
                    _toCompleteNextFrame.Add(data.packetIdx);
            }
            finally
            {
                _asyncObserverPromotionDepth--;
            }
        }

        private void ExpireTimedOutAsyncObservers()
        {
            if (!_asServer || _pendingAsyncObservers.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            var expired = ListPool<SpawnID>.Instantiate();
            foreach (var pair in _pendingAsyncObservers)
            {
                var pending = pair.Value;
                if (!pending.sent || now - pending.createdAt < asyncSpawnReadyTimeoutSeconds)
                    continue;

                expired.Add(pair.Key);
            }

            for (var i = 0; i < expired.Count; i++)
            {
                if (!_pendingAsyncObservers.Remove(expired[i], out var pending))
                    continue;

                var root = pending.identities.Count > 0 ? pending.identities[0] : null;

                for (var j = 0; j < pending.identities.Count; j++)
                {
                    var identity = pending.identities[j];
                    if (identity)
                        identity.TryRemovePendingObserver(pending.player);
                }

                PurrLogger.LogWarning(
                    $"InstantiateAsync spawn {expired[i]} did not become ready on player {pending.player} within " +
                    $"{asyncSpawnReadyTimeoutSeconds:0} seconds. The remote operation was cancelled and the spawn " +
                    "is resent synchronously.", root);

                if (!root)
                    continue;

                SendDespawnPacket(pending.player, root, false);

                // A timeout is transient (slow remote instantiate, congested link), unlike an
                // explicit receiver failure it must not tombstone the player, or the identity
                // stays invisible to them for its entire lifetime. The pending state was cleared
                // above, so re-evaluating resends the spawn synchronously: no ready handshake,
                // no second timeout, and a late Ready for the old transaction is ignored.
                //For context, this was a previous bug that should now be fixed, hence the bigger comment
                if (root.isSpawned)
                    EvaluateVisibility(pending.player, root.transform);
            }

            ListPool<SpawnID>.Destroy(expired);
        }

        private NetworkIdentity MarkAsyncObserverSpawnFailed(PendingAsyncObserverSpawn pending)
        {
            var root = pending.identities.Count > 0 ? pending.identities[0] : null;

            if (root && root.id.HasValue)
                _failedAsyncObserverRoots.Add((pending.player, root.id.Value));
            return root;
        }

        private struct PrototypeCacheEntry
        {
            public PlayerID observer;
            public GameObjectPrototype prototype;
            public List<NetworkIdentity> children;
            public List<NetworkIdentity> components;
        }

        private readonly Dictionary<Transform, PrototypeCacheEntry> _prototypeCache = new();

        static readonly ProfilerMarker _prototypeMarker = new ProfilerMarker("PurrNet.Hierarchy.Prototype");
        static readonly ProfilerMarker _spawnPacketMarker = new ProfilerMarker("PurrNet.Hierarchy.SpawnPacket");
        static readonly ProfilerMarker _preObserverMarker = new ProfilerMarker("PurrNet.Hierarchy.PreObserverAdded");
        static readonly ProfilerMarker _visibilityLostMarker = new ProfilerMarker("PurrNet.Hierarchy.VisibilityLost");
        static readonly ProfilerMarker _flushSpawnMarker = new ProfilerMarker("PurrNet.Hierarchy.FlushSpawn");
        static readonly ProfilerMarker _lateObserversMarker = new ProfilerMarker("PurrNet.Hierarchy.LateObservers");

        private bool TryGetPrototypeCached(Transform scope, PlayerID player, List<NetworkIdentity> children,
            out GameObjectPrototype prototype)
        {
            if (_prototypeCache.TryGetValue(scope, out var cached))
            {
                if (HierarchyPool.SameObserverPattern(cached.components, cached.observer, player))
                {
                    children.AddRange(cached.children);
                    prototype = cached.prototype.Clone();
                    return true;
                }

                return HierarchyPool.TryGetPrototype(scope, player, children, out prototype, null, cached.components);
            }

            var components = ListPool<NetworkIdentity>.Instantiate();
            if (!HierarchyPool.TryGetPrototype(scope, player, children, out prototype, components))
            {
                ListPool<NetworkIdentity>.Destroy(components);
                return false;
            }

            var keep = ListPool<NetworkIdentity>.Instantiate();
            keep.AddRange(children);
            _prototypeCache.Add(scope, new PrototypeCacheEntry
            {
                observer = player,
                prototype = prototype.Clone(),
                children = keep,
                components = components
            });
            return true;
        }

        private void ClearPrototypeCache()
        {
            if (_prototypeCache.Count == 0)
                return;

            foreach (var entry in _prototypeCache.Values)
            {
                var prototype = entry.prototype;
                prototype.Dispose();
                ListPool<NetworkIdentity>.Destroy(entry.children);
                ListPool<NetworkIdentity>.Destroy(entry.components);
            }

            _prototypeCache.Clear();
        }

        private void OnVisibilityChanged(PlayerID player, Transform scope, bool isVisible)
        {
            if (isVisible)
            {
                var children = ListPool<NetworkIdentity>.Instantiate();
                _prototypeMarker.Begin();
                bool hasPrototype = TryGetPrototypeCached(scope, player, children, out var prototype);
                _prototypeMarker.End();
                if (hasPrototype)
                {
                    if (_scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                    {
                        bool sendAsync = _asyncVisibilityDepth > 0 &&
                                         player != _manager.localPlayer &&
                                         !player.isBot && !player.isServer;
                        _spawnPacketMarker.Begin();
                        var spawnId = SendSpawnPacket(player, prototype, children, true, sendAsync);
                        _spawnPacketMarker.End();

                        if (sendAsync && _pendingAsyncObservers.ContainsKey(spawnId))
                            return;
                    }

                    using var _ = _preObserverMarker.Auto();
                    for (var i = 0; i < children.Count; i++)
                    {
                        var nid = children[i];
                        onObserverAdded?.Invoke(player, nid);
                        nid.TriggerOnPreObserverAdded(player, false);
                        _triggerLateObserverAdded.Add(new PlayerNid { player = player, nid = nid, isSpawner = false });
                    }
                }
                else PurrLogger.LogError($"Failed to get prototype for '{scope.name}'.", scope);
                return;
            }

            if (scope.TryGetComponent<NetworkIdentity>(out var identity))
            {
                var children = ListPool<NetworkIdentity>.Instantiate();
                GetComponentsInChildren(identity.gameObject, children);
                OnVisibilityLost(player, identity, children);
                ListPool<NetworkIdentity>.Destroy(children);
            }
        }

        private void OnVisibilityCleared(Transform scope, HashSet<PlayerID> players)
        {
            if (!scope.TryGetComponent<NetworkIdentity>(out var identity))
                return;

            var children = ListPool<NetworkIdentity>.Instantiate();
            GetComponentsInChildren(identity.gameObject, children);
            foreach (var player in players)
                OnVisibilityLost(player, identity, children);
            ListPool<NetworkIdentity>.Destroy(children);
        }

        private void OnVisibilityLost(PlayerID player, NetworkIdentity identity, List<NetworkIdentity> children)
        {
            using var _ = _visibilityLostMarker.Auto();

            if (!HasActiveAsyncObserverState)
            {
                for (var i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    ClearPendingLateObserverAdded(player, child);
                    child.TriggerOnObserverRemoved(player);
                    onObserverRemoved?.Invoke(player, child);
                }


                if (_scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
                {
                    _manager.FlushBatchedRPCs();
                    SendDespawnPacket(player, identity, true);
                }
                return;
            }

            var unconfirmed = HashSetPool<NetworkIdentity>.Instantiate();
            var cancelledRoots = ListPool<NetworkIdentity>.Instantiate();
            var confirmedRemoved = ListPool<NetworkIdentity>.Instantiate();
            var failedRoots = ListPool<NetworkIdentity>.Instantiate();
            RemovePendingAsyncObservers(player, children, unconfirmed, cancelledRoots, confirmedRemoved);
            ConsumeFailedAsyncObserverRoots(player, children, failedRoots, unconfirmed);

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (unconfirmed.Contains(child))
                    continue;

                ClearPendingLateObserverAdded(player, child);
                child.TriggerOnObserverRemoved(player);
                onObserverRemoved?.Invoke(player, child);
            }

            for (var i = 0; i < confirmedRemoved.Count; i++)
            {
                var removed = confirmedRemoved[i];
                if (!removed || children.Contains(removed))
                    continue;
                ClearPendingLateObserverAdded(player, removed);
                removed.TriggerOnObserverRemoved(player);
                onObserverRemoved?.Invoke(player, removed);
            }

            HashSetPool<NetworkIdentity>.Destroy(unconfirmed);
            ListPool<NetworkIdentity>.Destroy(confirmedRemoved);

            if (_scenePlayers.IsPlayerLoadedInScene(player, _sceneId))
            {
                _manager.FlushBatchedRPCs();
                bool identityCovered = false;
                for (var i = 0; i < cancelledRoots.Count; i++)
                {
                    var cancelledRoot = cancelledRoots[i];
                    if (!cancelledRoot)
                        continue;
                    identityCovered |= identity.transform.IsChildOf(cancelledRoot.transform);
                    SendDespawnPacket(player, cancelledRoot, true);

                    if (cancelledRoot != identity)
                        _collateralCancelRetries.Add((player, cancelledRoot));
                }

                for (var i = 0; i < failedRoots.Count; i++)
                {
                    var failedRoot = failedRoots[i];
                    if (failedRoot)
                        identityCovered |= identity.transform.IsChildOf(failedRoot.transform);
                }

                if (!identityCovered)
                    SendDespawnPacket(player, identity, true);
            }
            ListPool<NetworkIdentity>.Destroy(cancelledRoots);
            ListPool<NetworkIdentity>.Destroy(failedRoots);
        }

        private void SendDespawnPacket(PlayerID player, NetworkIdentity identity, bool batched)
        {
            var identityId = identity.GetNetworkID(_asServer) ?? identity.id;
            if (!identityId.HasValue)
                return;

            SendDespawnPacket(player, identityId.Value, batched);
        }

        private void SendDespawnPacket(PlayerID player, NetworkID identityId, bool batched)
        {

            // dont send despawn packet to the local player
            if (player == _manager.localPlayer)
                return;

            var packet = new DespawnPacket
            {
                sceneId = _sceneId,
                parentId = identityId
            };

            if (batched)
            {
                if (!_spawnPackets.TryGetValue(player, out var batch))
                {
                    batch = new SpawnPacketBatch(
                        _sceneId,
                        DisposableList<SpawnPacket>.Create(),
                        DisposableList<DespawnPacket>.Create()
                    );
                    batch.despawnPackets.Add(packet);
                    _spawnPackets.Add(player, batch);
                }
                else
                {
                    batch.despawnPackets.Add(packet);
                }
            }
            else
            {
                if (player.isServer)
                    _playersManager.SendToServer(packet);
                else _playersManager.Send(player, packet);
            }
        }

        private SpawnID RestoreTarget(SpawnID id, PlayerID sender)
        {
            if (!_asServer || id.scope.Equals(sender))
                return id;
            return id.WithTarget(sender);
        }

        private readonly Dictionary<NetworkID, ulong> _spawnIdxByRoot = new();

        private SpawnID SendSpawnPacket(PlayerID player, GameObjectPrototype prototype,
            List<NetworkIdentity> spawned, bool batched, bool isAsync = false)
        {
            ulong idx;
            if (batched && _asServer && spawned.Count > 0 && spawned[0] && spawned[0].id.HasValue)
            {
                var rootId = spawned[0].id.Value;
                if (!_spawnIdxByRoot.TryGetValue(rootId, out idx))
                {
                    idx = _nextPacketIdx++;
                    _spawnIdxByRoot.Add(rootId, idx);
                }
            }
            else idx = _nextPacketIdx++;

            var spawnId = new SpawnID(idx, player, _playersManager.localPlayerId);
            if (_asServer && isAsync)
                isAsync = MoveObserversToAsyncPending(spawnId, player, spawned);
            var data = BitPackerPool.Get();

            try
            {
                if (player != _manager.localPlayer)
                {
                    for (var i = 0; i < spawned.Count; i++)
                    {
                        var identity = spawned[i];
                        identity.TriggerOnSerialize(data);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                data.SetBitPosition(0);
            }

            bool bypassPool = _hasConfiguredPoolBypass && ShouldBypassConfiguredPool(spawned);

            var packet = new SpawnPacket
            {
                sceneId = _sceneId,
                packetIdx = spawnId,
                bypassPool = bypassPool,
                isAsync = isAsync,
                prototype = prototype,
                localcache = spawned,
                customData = new BitData(data)
            };

            if (batched)
            {
                if (!_spawnPackets.TryGetValue(player, out var batch))
                {
                    batch = new SpawnPacketBatch(
                        _sceneId,
                        DisposableList<SpawnPacket>.Create(),
                        DisposableList<DespawnPacket>.Create()
                    );
                    batch.spawnPackets.Add(packet);
                    _spawnPackets.Add(player, batch);
                }
                else
                {
                    batch.spawnPackets.Add(packet);
                }
            }
            else
            {
                if (player.isServer)
                    _playersManager.SendToServer(packet);
                else _playersManager.Send(player, packet);
                packet.Dispose();
                if (!(_asServer && isAsync))
                    _toCompleteNextFrame.Add(spawnId);
            }

            return spawnId;
        }

        private bool ShouldBypassConfiguredPool(List<NetworkIdentity> spawned)
        {
            for (var i = 0; i < spawned.Count; i++)
            {
                var identity = spawned[i];
                if (!identity || identity.shouldBePooled)
                    continue;

                if (_manager.prefabResolver.TryGetPrefabData(identity.scopedPrefabId, out var prefabData) &&
                    prefabData.pooled)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Called before a gameobject is spawned.
        /// Both locally and for incoming remote spawns.
        /// </summary>
        public static event SpawnDelegate onPreSpawn;

        public void OnGameObjectCreated(GameObject obj, GameObject prefab)
        {
            if (!obj)
                return;

            if (!_asServer && _manager.isServer)
                return;

            if (obj.scene.handle != _scene.handle)
                return;

            if (!_manager.prefabResolver.TryGetPrefabData(prefab, _sceneId, out var data))
                return;

            NetworkManager.SetupPrefabInfo(obj, data.prefabId, data.pooled);

            if (!ShouldAutoSpawn(obj, false))
                return;

            InternalSpawn(obj);
        }

        private bool ShouldAutoSpawn(GameObject obj, bool isAsync)
        {
            if (!obj.TryGetComponent<NetworkIdentity>(out var identity))
                return true;

            return identity.ShouldAutoSpawnOnInstantiate(_manager, isAsync);
        }

#if PURRNET_UNITY_INSTANTIATE_ASYNC
        private void OnAsyncInstantiateCompleted(UnityEngine.Object original, UnityEngine.Object instance)
        {
            var obj = GetAsyncGameObject(instance);
            var prefab = GetAsyncGameObject(original);

            if (!obj || !prefab)
                return;

            if (!_asServer && _manager.isServer)
                return;

            if (obj.scene.handle != _scene.handle)
                return;

            if (!_manager.prefabResolver.TryGetPrefabData(prefab, _sceneId, out var data))
                return;

            // Async-origin instances are never allowed into PurrNet's pool, even when the
            // registered prefab is normally poolable.
            _hasConfiguredPoolBypass = true;

            var identities = ListPool<NetworkIdentity>.Instantiate();
            try
            {
                obj.GetComponentsInChildren(true, identities);
                NetworkManager.SetupPrefabInfo(obj, data.prefabId, false, identities);

                if (!ShouldAutoSpawn(obj, true))
                    return;

                if (!HasMatchingAsyncNetworkShape(data.prefab, obj, identities, out var mismatch))
                {
                    ReportAsyncShapeMismatch(data.prefab, obj, mismatch);
                    return;
                }
            }
            finally
            {
                ListPool<NetworkIdentity>.Destroy(identities);
            }

            InternalSpawn(obj, true);
        }

        private static GameObject GetAsyncGameObject(UnityEngine.Object obj)
        {
            return obj switch
            {
                Component component => component.gameObject,
                GameObject gameObject => gameObject,
                _ => null
            };
        }
#endif

        internal void InternalSpawn(GameObject gameObject, bool instantiateRemotelyAsync = false)
        {
            if (!isReadyToSpawn)
            {
                PurrLogger.LogError("Failed to spawn object. Hierarchy module is not ready.\n" +
                                    "Use scene events to check when ready before spawning on client.", gameObject);
                return;
            }

            if (!gameObject)
                return;

            if (!gameObject.TryGetComponent<NetworkIdentity>(out var id))
            {
                PurrLogger.LogError($"Failed to spawn object '{gameObject.name}'. No NetworkIdentity found.",
                    gameObject);
                return;
            }

            if (id.isSpawned)
                return;

            if (!id.isSetup)
            {
                PurrLogger.LogError($"Failed to spawn object '{gameObject.name}'. It has no prefab info, " +
                                    "so peers could not recreate it. Register its prefab in a NetworkPrefabs asset " +
                                    "(global or scene scoped) and instantiate it through PurrNet.", gameObject);
                return;
            }

            if (!id.HasSpawnAuthority(_manager, _asServer))
            {
                PurrLogger.LogError($"Spawn failed from for '{gameObject.name}' due to lack of permissions.",
                    gameObject);
                return;
            }

            PlayerID scope = default;

            if (!_asServer)
            {
                if (!_playersManager.localPlayerId.HasValue)
                {
                    PurrLogger.LogError($"Failed to spawn object '{gameObject.name}'. No local player id found.",
                        gameObject);
                    return;
                }

                scope = _playersManager.localPlayerId.Value;
            }

            onPreSpawn?.Invoke(gameObject, false);

            var baseNid = new NetworkID(_nextId++, scope);
            SetupIdsLocally(id, ref baseNid);
            ApplyParentChange(id, id.parent, id.invertedPathToNearestParentArray, false, applyToTransform: false);

            if (!_asServer)
            {
                var children = ListPool<NetworkIdentity>.Instantiate();
                var prototype = HierarchyPool.GetFullPrototype(gameObject.transform, children);
                SendSpawnPacket(default, prototype, children, false, instantiateRemotelyAsync);
            }
            else if (_scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                if (instantiateRemotelyAsync)
                    ++_asyncVisibilityDepth;

                try
                {
                    for (var i = 0; i < players.Count; i++)
                    {
                        var player = players[i];
                        _visibility.RefreshVisibilityForGameObject(player, id);
                    }
                }
                finally
                {
                    if (instantiateRemotelyAsync)
                        --_asyncVisibilityDepth;
                }

                FlushSpawnPackets();
            }

            AutoAssignOwnership(id);
        }

        private static int _supressAutoOwner;

        public static void SupressAutoOwner()
        {
            ++_supressAutoOwner;
        }

        public static void ResumeAutoOwner()
        {
            --_supressAutoOwner;
            if (_supressAutoOwner < 0)
                _supressAutoOwner = 0;
        }

        private void AutoAssignOwnership(NetworkIdentity id)
        {
            bool shouldSupressAutoOwner = _supressAutoOwner > 0;

            if (shouldSupressAutoOwner)
                return;

            if (!id.ShouldClientGiveOwnershipOnSpawn(_manager))
                return;

            PlayersManager playersManager;

            switch (_asServer)
            {
                case true when _manager.isClient:
                    playersManager = _manager.GetModule<PlayersManager>(false);
                    break;
                case false:
                    playersManager = _playersManager;
                    break;
                default:
                    return;
            }

            if (playersManager.localPlayerId.HasValue)
                id.GiveOwnershipInternal(playersManager.localPlayerId.Value, false, true);
        }

        public static void GetComponentsInChildren(GameObject go, List<NetworkIdentity> list)
        {
            if (!go)
                return;

            // workaround for the fact that GetComponents clears the list
            var tmpList = ListPool<NetworkIdentity>.Instantiate();
            int startIdx = list.Count;
            go.GetComponents(tmpList);
            list.AddRange(tmpList);
            ListPool<NetworkIdentity>.Destroy(tmpList);

            if (list.Count <= startIdx)
                return;

            var identity = list[startIdx];
            var children = identity.directChildren;
            var dcount = children.Count;

            for (int j = 0; j < dcount; j++)
            {
                var child = children[j];
                if (!child)
                    continue;
                GetComponentsInChildren(child.gameObject, list);
            }
        }

        public void Despawn(GameObject gameObject, bool bypassPermissions = false, bool bypassBroadcast = false)
        {
            var children = ListPool<NetworkIdentity>.Instantiate();
            GetComponentsInChildren(gameObject, children);

            if (children.Count == 0)
            {
                ListPool<NetworkIdentity>.Destroy(children);
                return;
            }

            int c = children.Count;
            for (int i = 0; i < c; i++)
            {
                if (!children[i].isSpawned)
                {
                    children.RemoveAt(i--);
                    --c;
                }
            }

            if (c == 0)
            {
                ListPool<NetworkIdentity>.Destroy(children);
                return;
            }
            if (!bypassPermissions &&
                !children[0].HasDespawnAuthority(_playersManager?.localPlayerId ?? default, _asServer))
            {
                PurrLogger.LogError($"Despawn failed for '{gameObject.name}' due to lack of permissions.", gameObject);
                ListPool<NetworkIdentity>.Destroy(children);
                return;
            }

            NetworkID? localDespawnId = null;
            if (!_asServer && !bypassBroadcast)
            {
                localDespawnId = children[0].GetNetworkID(false) ?? children[0].id;
                if (localDespawnId.HasValue)
                    TrackPendingLocalDespawnEcho(localDespawnId.Value);
            }

            bool isHost = IsServerHost();

            // Try to despawn the object properly if despawn was on the same tick (by first calling OnSpawned)
            for (var i = 0; i < c; i++)
                CompletePendingSpawnsFor(children[i], isHost);

            if (_asServer)
            {
                _visibility.ClearVisibilityForGameObject(children[0]);

                for (var i = 0; i < c; i++)
                {
                    var child = children[i];

                    TriggerDespawnEvent(child, child.shouldBePooled);
                }

                _manager.FlushBatchedRPCs();
                FlushSpawnPackets();
            }
            else if (!bypassBroadcast)
            {
                for (var i = 0; i < c; i++)
                {
                    var child = children[i];

                    TriggerDespawnEvent(child, child.shouldBePooled);
                }

                _manager.FlushBatchedRPCs();
                if (localDespawnId.HasValue)
                    SendDespawnPacket(default, localDespawnId.Value, false);
            }
            else
            {
                for (var i = 0; i < c; i++)
                {
                    var child = children[i];

                    TriggerDespawnEvent(child, child.shouldBePooled);
                }
            }

            for (var i = 0; i < c; i++)
            {
                var child = children[i];

                UnregisterIdentity(child);

                if (child.shouldBePooled)
                    child.ResetIdentity();
            }

            var pair = new PoolPair(_scenePool, _prefabsPool);
            HierarchyPool.PutBackInPool(pair, gameObject);

            ListPool<NetworkIdentity>.Destroy(children);
        }

        private void TrackPendingLocalDespawnEcho(NetworkID identityId)
        {
            if (_pendingLocalDespawnEchoes.isDisposed)
                _pendingLocalDespawnEchoes = DisposableList<NetworkID>.Create(1);
            if (!_pendingLocalDespawnEchoes.Contains(identityId))
                _pendingLocalDespawnEchoes.Add(identityId);
        }

        private void SetupIdsLocally(NetworkIdentity root, ref NetworkID baseNid)
        {
            bool isHost = IsServerHost();
            using var siblings = DisposableList<NetworkIdentity>.Create(16);
            root.GetComponents(siblings.list);

            // handle root
            for (var i = 0; i < siblings.Count; i++)
            {
                var sibling = siblings[i];
                sibling.SetID(new NetworkID(baseNid, (uint)i));
                sibling.SetIdentity(_manager, this, _sceneId, _asServer, isHost);
                RegisterIdentity(sibling, true);
            }

            // update next id
            _nextId += (uint)siblings.list.Count;
            baseNid = new NetworkID(_nextId, baseNid.scope);

            // handle children
            if (root.directChildren == null)
                return;

            for (var i = 0; i < root.directChildren.Count; i++)
            {
                SetupIdsLocally(root.directChildren[i], ref baseNid);
            }
        }

        public NetworkID ReserveNetworkID()
        {
            if (_asServer)
                return new NetworkID(_nextId++, default);
            return new NetworkID(_nextId++, _playersManager.localPlayerId ?? default);
        }

        private void SpawnSceneObject(List<NetworkIdentity> children)
        {
            bool isHost = IsServerHost();

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.isSceneObject)
                {
                    var id = new NetworkID(default, _nextId++);
                    child.SetID(id);
                    if (_asServer)
                    {
                        child.SetIdentity(_manager, this, _sceneId, _asServer, isHost);
                        RegisterIdentity(child, true);
                    }
                }
            }
        }

        private static bool SamePrototype(in GameObjectPrototype a, in GameObjectPrototype b)
        {
            if (a.position != b.position || a.rotation != b.rotation || a.scale != b.scale ||
                !Nullable.Equals(a.parentID, b.parentID) || a.defaultParentSiblingIndex != b.defaultParentSiblingIndex)
                return false;

            if (!SameInts(a.path, b.path))
                return false;

            var fa = a.framework;
            var fb = b.framework;
            if (fa.Count != fb.Count)
                return false;

            for (var i = 0; i < fa.Count; i++)
            {
                var pa = fa[i];
                var pb = fb[i];
                if (!pa.pid.Equals(pb.pid) || !pa.id.Equals(pb.id) || pa.childCount.value != pb.childCount.value ||
                    pa.isActive != pb.isActive || !pa.localTransform.Equals(pb.localTransform) ||
                    !SameInts(pa.inversedRelativePath, pb.inversedRelativePath))
                    return false;
            }

            return true;
        }

        private static bool SameInts(int[] a, int[] b)
        {
            int la = a?.Length ?? 0;
            int lb = b?.Length ?? 0;
            if (la != lb)
                return false;
            for (var i = 0; i < la; i++)
            {
                if (a![i] != b![i])
                    return false;
            }

            return true;
        }

        private static bool SameBatch(in SpawnPacketBatch a, in SpawnPacketBatch b)
        {
            var sa = a.spawnPackets;
            var sb = b.spawnPackets;
            if (sa.Count != sb.Count)
                return false;

            for (var i = 0; i < sa.Count; i++)
            {
                var pa = sa[i];
                var pb = sb[i];
                if (!pa.packetIdx.SameWire(pb.packetIdx) || pa.isAsync != pb.isAsync || pa.bypassPool != pb.bypassPool ||
                    !pa.customData.Equals(pb.customData) || !SamePrototype(pa.prototype, pb.prototype))
                    return false;
            }

            var da = a.despawnPackets;
            var db = b.despawnPackets;
            if (da.Count != db.Count)
                return false;

            for (var i = 0; i < da.Count; i++)
            {
                if (!da[i].parentId.Equals(db[i].parentId))
                    return false;
            }

            return true;
        }

        private readonly List<(PlayerID first, List<PlayerID> members)> _batchGroups = new();

        private void FlushSpawnPackets()
        {
            using var _ = _flushSpawnMarker.Auto();
            _spawnIdxByRoot.Clear();
            ClearPrototypeCache();

            if (_spawnPackets.Count == 0)
                return;

            foreach (var (player, batch) in _spawnPackets)
            {
                if (player.isServer)
                    continue;

                bool grouped = false;
                for (var g = 0; g < _batchGroups.Count && !grouped; g++)
                {
                    var group = _batchGroups[g];
                    if (SameBatch(_spawnPackets[group.first], batch))
                    {
                        group.members.Add(player);
                        grouped = true;
                    }
                }

                if (!grouped)
                {
                    var members = ListPool<PlayerID>.Instantiate();
                    members.Add(player);
                    _batchGroups.Add((player, members));
                }
            }

            for (var g = 0; g < _batchGroups.Count; g++)
            {
                var (first, members) = _batchGroups[g];
                var batch = _spawnPackets[first];
                if (members.Count == 1)
                    _playersManager.Send(first, batch);
                else
                    _playersManager.Send(members, batch);
                ListPool<PlayerID>.Destroy(members);
            }

            _batchGroups.Clear();

            foreach (var (player, batch) in _spawnPackets)
            {
                using (batch)
                {
                    int count = batch.spawnPackets.Count;
                    if (player.isServer)
                    {
                        _playersManager.SendToServer(batch);
                    }
                    else
                    {
                        for (var i = 0; i < count; i++)
                        {
                            var packet = batch.spawnPackets[i];

                            if (packet.isAsync &&
                                _pendingAsyncObservers.TryGetValue(packet.packetIdx, out var pendingAsync))
                                pendingAsync.sent = true;

                            if (_asServer && packet.isAsync)
                                continue;

                            if (packet.localcache != null)
                            {
                                for (var j = 0; j < packet.localcache.Count; j++)
                                {
                                    var piece = packet.localcache[j];
                                    if (!piece) continue;
                                    var pieceid = piece.id;
                                    if (!pieceid.HasValue) continue;
                                    onSentSpawnPacket?.Invoke(player, _sceneId, pieceid.Value);
                                }
                            }
                            else if (packet.prototype.framework.Count > 0)
                            {
                                for (var j = 0; j < packet.prototype.framework.Count; j++)
                                {
                                    var piece = packet.prototype.framework[j];
                                    onSentSpawnPacket?.Invoke(player, _sceneId, piece.id);
                                }
                            }
                        }
                    }

                    for (var i = 0; i < count; i++)
                    {
                        var packet = batch.spawnPackets[i];
                        if (!(_asServer && packet.isAsync))
                            _toCompleteNextFrame.Add(packet.packetIdx);
                    }
                }
            }

            _spawnPackets.Clear();
        }

        public void PreNetworkMessages()
        {
            _manager.FlushBatchedRPCs();
        }

        public void PostNetworkMessages()
        {
            RevertUnauthorizedParentChanges();
            RetryCollateralCancelledSpawns();
            FlushHeldAsyncSpawnReadies();
            ExpireTimedOutAsyncObservers();
            FlushSpawnPackets();
            SendDelayedObserverEvents();
            TriggerSpawnSentEvents();
            _manager.FlushBatchedRPCs();
            onPreFinishSpawn?.Invoke(_sceneId);
            SendDelayedCompleteSpawns();
            SpawnDelayedIdentities();
        }

        private void TriggerSpawnSentEvents()
        {
            if (_toSpawnNextFrame.Count == 0)
                return;

            var snapshot = ListPool<NetworkIdentity>.Instantiate();
            snapshot.AddRange(_toSpawnNextFrame);

            for (var i = 0; i < snapshot.Count; i++)
            {
                var nid = snapshot[i];
                if (!nid || !nid.isSpawned)
                    continue;
                nid.TriggerOnSpawnSent();
            }

            ListPool<NetworkIdentity>.Destroy(snapshot);
        }

        private void CompletePendingSpawnsFor(NetworkIdentity toSpawn, bool isHost)
        {
            if (_toSpawnNextFrame.Remove(toSpawn))
            {
                if (!toSpawn || !toSpawn.isSpawned)
                    return;

                toSpawn.TriggerSpawnEvent(_asServer);

                if (_asServer && isHost)
                {
                    toSpawn.SetIsSpawned(true, false);
                    toSpawn.TriggerSpawnEvent(false);
                }

                onIdentityAdded?.Invoke(toSpawn);
            }
        }

        private readonly Dictionary<(NetworkIdentity nid, bool isSpawner), List<PlayerID>> _observerBatches = new();
        private readonly List<(NetworkIdentity nid, bool isSpawner)> _observerBatchOrder = new();

        private void SendDelayedObserverEvents()
        {
            using var _ = _lateObserversMarker.Auto();
            int count = _triggerLateObserverAdded.Count;
            if (count == 0)
                return;

            if (count == 1)
            {
                var single = _triggerLateObserverAdded[0];
                _triggerLateObserverAdded.Clear();
                if (single.nid && single.nid.isSpawned)
                {
                    single.nid.TriggerOnObserverAdded(single.player, single.isSpawner);
                    onLateObserverAdded?.Invoke(single.player, single.nid);
                }
                return;
            }

            for (var i = 0; i < count; i++)
            {
                var entry = _triggerLateObserverAdded[i];
                if (!entry.nid || !entry.nid.isSpawned)
                    continue;

                var key = (entry.nid, entry.isSpawner);
                if (!_observerBatches.TryGetValue(key, out var players))
                {
                    players = ListPool<PlayerID>.Instantiate();
                    _observerBatches.Add(key, players);
                    _observerBatchOrder.Add(key);
                }

                players.Add(entry.player);
            }

            _triggerLateObserverAdded.Clear();

            for (var g = 0; g < _observerBatchOrder.Count; g++)
            {
                var key = _observerBatchOrder[g];
                var players = _observerBatches[key];
                if (key.nid && key.nid.isSpawned)
                {
                    key.nid.TriggerOnObserversAdded(players, key.isSpawner);
                    for (var i = 0; i < players.Count; i++)
                        onLateObserverAdded?.Invoke(players[i], key.nid);
                }

                ListPool<PlayerID>.Destroy(players);
            }

            _observerBatches.Clear();
            _observerBatchOrder.Clear();
        }

        private readonly Dictionary<SpawnID, List<PlayerID>> _completeTargets = new();
        private readonly List<SpawnID> _completeOrder = new();

        private void SendDelayedCompleteSpawns()
        {
            if (_toCompleteNextFrame.Count == 0)
                return;

            if (!_asServer)
            {
                for (var i = 0; i < _toCompleteNextFrame.Count; i++)
                {
                    _playersManager.SendToServer(new FinishSpawnPacket
                    {
                        sceneId = _sceneId,
                        packetIdx = _toCompleteNextFrame[i]
                    });
                }

                _toCompleteNextFrame.Clear();
                return;
            }

            for (var i = 0; i < _toCompleteNextFrame.Count; i++)
            {
                var toComplete = _toCompleteNextFrame[i];
                var key = toComplete.WithTarget(default);
                if (!_completeTargets.TryGetValue(key, out var targets))
                {
                    targets = ListPool<PlayerID>.Instantiate();
                    _completeTargets.Add(key, targets);
                    _completeOrder.Add(key);
                }

                targets.Add(toComplete.target);

                if (_readyAsyncObservers.Count > 0)
                    _readyAsyncObservers.Remove(toComplete);
            }

            for (var i = 0; i < _completeOrder.Count; i++)
            {
                var key = _completeOrder[i];
                var targets = _completeTargets[key];
                var packet = new FinishSpawnPacket
                {
                    sceneId = _sceneId,
                    packetIdx = key
                };

                if (targets.Count == 1)
                    _playersManager.Send(targets[0], packet);
                else
                    _playersManager.Send(targets, packet);
                ListPool<PlayerID>.Destroy(targets);
            }

            _completeTargets.Clear();
            _completeOrder.Clear();
            _toCompleteNextFrame.Clear();
        }

        private void CatchupClient(PlayerID playerId)
        {
            for (var i = 0; i < _spawnedIdentities.Count; i++)
            {
                var identity = _spawnedIdentities[i];

                if (!identity.isSpawned)
                    continue;

                if (identity.IsSpawned(false))
                    continue;

                if (!identity.id.HasValue)
                    continue;

                if (_toSpawnNextFrame.Contains(identity))
                    continue;

                identity.SetIsSpawned(true, false);
                identity.TriggerEarlySpawnEvent(false);

                onSentSpawnPacket?.Invoke(playerId, _sceneId, identity.id.Value);

                if (identity.TryAddObserver(playerId))
                {
                    onObserverAdded?.Invoke(playerId, identity);
                    identity.TriggerOnPreObserverAdded(playerId, false);
                    _triggerLateObserverAdded.Add(new PlayerNid { player = playerId, nid = identity, isSpawner = false });
                }

                identity.TriggerSpawnEvent(false);
                onIdentityAdded?.Invoke(identity);
            }
        }

        private bool IsServerHost()
        {
            if (!_asServer)
                return false;

            if (_manager.TryGetModule<HierarchyFactory>(false, out var factory) &&
                factory.TryGetHierarchy(_sceneId, out var other))
            {
                return other._isPlayerReady;
            }

            return false;
        }

        private void SpawnDelayedIdentities()
        {
            bool isHost = IsServerHost();

            // swap buffers to avoid editing while iterating
            var actual = _toSpawnNextFrame;
            _toSpawnNextFrame = _toSpawnNextFrameBuffer;
            _toSpawnNextFrameBuffer = actual;

            // trigger spawn events
            foreach (var toSpawn in actual)
            {
                if (!toSpawn || !toSpawn.isSpawned) continue;

                toSpawn.TriggerSpawnEvent(_asServer);

                if (_asServer && isHost)
                {
                    toSpawn.SetIsSpawned(true, false);
                    toSpawn.TriggerSpawnEvent(false);
                }

                onIdentityAdded?.Invoke(toSpawn);
            }

            actual.Clear();
        }

        public static void SetLocalPosAndRot(Transform t, Vector3 pos, Quaternion rot, Vector3 scale)
        {
#if UNITY_PHYSICS_3D
            var cc = t.GetComponent<CharacterController>();
            bool wasCCEnabled = cc && cc.enabled;

            if (wasCCEnabled)
                cc.enabled = false;
#endif

            t.SetLocalPositionAndRotation(pos, rot);
            t.localScale = scale;

#if UNITY_PHYSICS_3D
            if (t.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.position = t.position;
                rb.rotation = t.rotation;
            }
#endif

#if UNITY_PHYSICS_2D
            if (t.TryGetComponent<Rigidbody2D>(out var rb2d))
            {
                rb2d.position = t.position;
                rb2d.rotation = t.rotation.eulerAngles.z;
            }
#endif

#if UNITY_PHYSICS_3D
            if (wasCCEnabled)
                cc.enabled = true;
#endif
        }

        /// <summary>
        /// Creates a new GameObject instance based on the provided prototype and optionally associates it with a list of network identities.
        /// This method handles initializing the GameObject's position, rotation, scale, and parenting. If activation conditions are met,
        /// the created GameObject is activated before being returned.
        /// </summary>
        /// <param name="prototype">The prototype containing the configuration details for the GameObject to be created.</param>
        /// <param name="createdNids">An optional list of NetworkIdentity objects that will be associated with the created GameObject. Can be null.</param>
        /// <returns>The newly created GameObject configured according to the given prototype, or null if creation fails.</returns>
        public GameObject CreatePrototype(GameObjectPrototype prototype, List<NetworkIdentity> createdNids)
        {
            var pair = new PoolPair(_scenePool, _prefabsPool);

            if (!HierarchyPool.TryBuildPrototype(pair, prototype, createdNids, out var result, out var shouldActivate))
                return null;

            return FinalizePrototypeInstance(result, prototype, shouldActivate);
        }

        private static bool TryApplyPrototypeToExisting(GameObject result, GameObjectPrototype prototype,
            List<NetworkIdentity> createdNids, out bool shouldActivate)
        {
            shouldActivate = false;
            if (!result || prototype.framework.Count == 0 ||
                !result.TryGetComponent<NetworkIdentity>(out var root))
                return false;

            var actual = HierarchyPool.GetFullPrototype(result.transform, null, true);
            try
            {
                if (!HaveMatchingNetworkFramework(prototype, actual))
                    return false;
            }
            finally
            {
                actual.Dispose();
            }

            var queue = new Queue<NetworkIdentity>();
            queue.Enqueue(root);

            for (var i = 0; i < prototype.framework.Count; i++)
            {
                if (queue.Count == 0)
                    return false;

                var pieceRoot = queue.Dequeue();
                if (!pieceRoot)
                    return false;

                var current = prototype.framework[i];
                var siblings = ListPool<NetworkIdentity>.Instantiate();
                pieceRoot.gameObject.GetComponents(siblings);

                if (siblings.Count == 0)
                {
                    ListPool<NetworkIdentity>.Destroy(siblings);
                    return false;
                }

                for (var siblingIndex = 0; siblingIndex < siblings.Count; siblingIndex++)
                {
                    var sibling = siblings[siblingIndex];
                    sibling.SetID(new NetworkID(current.id, (ulong)siblingIndex));
                    sibling.parent = i == 0 ? null : sibling.GetNearestParent();
                    sibling.invertedPathToNearestParentArray = current.inversedRelativePath;
                }
                ListPool<NetworkIdentity>.Destroy(siblings);

                var directChildren = pieceRoot.directChildren;
                for (var childIndex = 0; childIndex < directChildren.Count; childIndex++)
                    queue.Enqueue(directChildren[childIndex]);

                current.localTransform.Apply(pieceRoot.transform);
                if (i != 0 && pieceRoot.gameObject.activeSelf != current.isActive)
                    pieceRoot.gameObject.SetActive(current.isActive);
            }

            if (queue.Count != 0)
                return false;

            shouldActivate = prototype.framework[0].isActive;
            if (!shouldActivate && result.activeSelf)
                result.SetActive(false);

            if (createdNids != null)
            {
                var ordered = HierarchyPool.GetFullPrototype(result.transform, createdNids, true);
                ordered.Dispose();
            }
            return true;
        }

        private static bool HaveMatchingNetworkFramework(GameObjectPrototype expected, GameObjectPrototype actual)
        {
            if (expected.framework.Count != actual.framework.Count)
                return false;

            for (var i = 0; i < expected.framework.Count; i++)
            {
                var a = expected.framework[i];
                var b = actual.framework[i];
                if (!a.pid.Equals(b.pid) || a.childCount != b.childCount ||
                    !HaveMatchingPath(a.inversedRelativePath, b.inversedRelativePath))
                    return false;
            }
            return true;
        }

        private static bool HaveMatchingPath(int[] a, int[] b)
        {
            int aLength = a?.Length ?? 0;
            int bLength = b?.Length ?? 0;
            if (aLength != bLength)
                return false;

            for (var i = 0; i < aLength; i++)
            {
                if (a![i] != b![i])
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Sibling-index paths cannot distinguish two same-type siblings that swapped places in
        /// Awake: positionally the shapes are identical, but network ids would silently cross-map
        /// between the sender and every receiver. Transform names along the path catch that case
        /// whenever the siblings are distinguishable at all.
        /// </summary>
        private static bool HaveMatchingNamePath(string[] a, string[] b)
        {
            int aLength = a?.Length ?? 0;
            int bLength = b?.Length ?? 0;
            if (aLength != bLength)
                return false;

            for (var i = 0; i < aLength; i++)
            {
                if (!string.Equals(a![i], b![i], StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private readonly struct AsyncNetworkShapeEntry
        {
            public readonly Type type;
            public readonly int componentIndex;
            public readonly int[] transformPath;
            public readonly string[] namePath;

            public AsyncNetworkShapeEntry(Type type, int componentIndex, int[] transformPath, string[] namePath)
            {
                this.type = type;
                this.componentIndex = componentIndex;
                this.transformPath = transformPath;
                this.namePath = namePath;
            }
        }

        private static readonly HashSet<int> _reportedAsyncShapeMismatches = new();
        private static readonly Dictionary<GameObject, List<AsyncNetworkShapeEntry>> _cachedPrefabAsyncShapes = new();

        private static readonly List<AsyncNetworkShapeEntry> _emptyAsyncShape = new();

        private static List<AsyncNetworkShapeEntry> GetPrefabAsyncNetworkShape(GameObject prefab)
        {
            if (!prefab)
                return _emptyAsyncShape;

            if (_cachedPrefabAsyncShapes.TryGetValue(prefab, out var shape))
                return shape;

            shape = new List<AsyncNetworkShapeEntry>();
            CaptureAsyncNetworkShape(prefab, shape);
            _cachedPrefabAsyncShapes[prefab] = shape;
            return shape;
        }

        private static bool HasMatchingAsyncNetworkShape(GameObject prefab, GameObject instance,
            List<NetworkIdentity> instanceIdentities, out string mismatch)
        {
            var expected = GetPrefabAsyncNetworkShape(prefab);
            var actual = new List<AsyncNetworkShapeEntry>();
            CaptureAsyncNetworkShape(instance, instanceIdentities, actual);

            if (expected.Count != actual.Count)
            {
                mismatch = $"expected {expected.Count} NetworkIdentity components, but the result has {actual.Count}";
                return false;
            }

            for (var i = 0; i < expected.Count; i++)
            {
                var a = expected[i];
                var b = actual[i];
                if (a.type != b.type || a.componentIndex != b.componentIndex ||
                    !HaveMatchingPath(a.transformPath, b.transformPath) ||
                    !HaveMatchingNamePath(a.namePath, b.namePath))
                {
                    mismatch = $"NetworkIdentity component {i} changed type, component order, transform path, or name";
                    return false;
                }
            }

            mismatch = null;
            return true;
        }

        private static void CaptureAsyncNetworkShape(GameObject root, List<AsyncNetworkShapeEntry> result)
        {
            if (!root)
                return;

            var identities = ListPool<NetworkIdentity>.Instantiate();
            root.GetComponentsInChildren(true, identities);
            CaptureAsyncNetworkShape(root, identities, result);
            ListPool<NetworkIdentity>.Destroy(identities);
        }

        private static void CaptureAsyncNetworkShape(GameObject root, List<NetworkIdentity> identities,
            List<AsyncNetworkShapeEntry> result)
        {
            if (!root)
                return;

            Transform runTransform = null;
            int runStart = 0;

            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (!identity)
                    continue;

                var trs = identity.transform;
                if (!ReferenceEquals(trs, runTransform))
                {
                    runTransform = trs;
                    runStart = i;
                }

                int componentIndex = i - runStart;

                var inversePath = ListPool<int>.Instantiate();
                var inverseNames = ListPool<string>.Instantiate();
                var current = trs;
                while (current && current != root.transform)
                {
                    inversePath.Add(current.GetSiblingIndex());
                    inverseNames.Add(current.name);
                    current = current.parent;
                }

                var path = new int[inversePath.Count];
                var names = new string[inversePath.Count];
                for (var pathIndex = 0; pathIndex < inversePath.Count; pathIndex++)
                {
                    path[pathIndex] = inversePath[inversePath.Count - pathIndex - 1];
                    names[pathIndex] = inverseNames[inversePath.Count - pathIndex - 1];
                }
                ListPool<int>.Destroy(inversePath);
                ListPool<string>.Destroy(inverseNames);

                result.Add(new AsyncNetworkShapeEntry(identity.GetType(), componentIndex, path, names));
            }
        }

        private static void ReportAsyncShapeMismatch(GameObject prefab, GameObject instance, string mismatch)
        {
            if (!prefab || !_reportedAsyncShapeMismatches.Add(prefab.GetHashCode()))
                return;

            PurrLogger.LogError(
                $"`InstantiateAsync` could not network-spawn prefab `{prefab.name}` because its NetworkIdentity hierarchy changed during asynchronous instantiation ({mismatch}). " +
                "Do not add, remove, destroy, reparent, reorder, or rename NetworkIdentity objects in Awake. Perform network hierarchy changes after spawning, or use regular Instantiate.",
                instance);
        }

        private GameObject CreateUnpooledPrototype(GameObjectPrototype prototype, List<NetworkIdentity> createdNids)
        {
            if (prototype.framework.Count == 0)
                return null;

            var poolRoot = new GameObject("[PurrNet] Unpooled Prototype Pieces")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            poolRoot.SetActive(false);
            var temporaryPool = new HierarchyPool(poolRoot.transform, _manager.prefabResolver, true);

            try
            {
                var pair = new PoolPair(_scenePool, temporaryPool, temporaryPool);
                if (!HierarchyPool.TryBuildPrototype(pair, prototype, createdNids, out var result,
                        out var shouldActivate))
                    return null;

                if (createdNids != null)
                {
                    for (var i = 0; i < createdNids.Count; i++)
                    {
                        var identity = createdNids[i];
                        if (!identity || identity.prefabId < 0)
                            continue;
                        identity.PreparePrefabInfo(identity.scopedPrefabId, identity.componentIndex, false, false);
                    }
                }

                result = FinalizePrototypeInstance(result, prototype, shouldActivate);
                var actual = HierarchyPool.GetFullPrototype(result.transform, null, true);
                bool shapeMatches = HaveMatchingNetworkFramework(prototype, actual);
                actual.Dispose();
                if (!shapeMatches)
                {
                    var prefabId = prototype.framework[0].pid.prefabId;
                    if (_manager.prefabResolver.TryGetPrefabData(prefabId, out var prefabData))
                        ReportAsyncShapeMismatch(prefabData.prefab, result,
                            "the NetworkIdentity framework changed when the instance was activated");
                    else
                        PurrLogger.LogError(
                            "`InstantiateAsync` could not apply a spawn packet because its NetworkIdentity framework changed when the instance was activated.",
                            result);
                    createdNids?.Clear();
                    UnityProxy.DestroyDirectly(result);
                    return null;
                }

                return result;
            }
            finally
            {
                temporaryPool.Dispose();
            }
        }

        private GameObject FinalizePrototypeInstance(GameObject result, GameObjectPrototype prototype,
            bool shouldActivate)
        {

            var resultTrs = result.transform;
            result.transform.SetParent(null, false);

            if (prototype.parentID.HasValue)
            {
                if (TryGetIdentity(prototype.parentID.Value, out var parent))
                {
                    result.transform.SetParent(parent.transform, false);
                    if (result.TryGetComponent<NetworkIdentity>(out var nid))
                        ApplyParentChange(nid, parent, prototype.path, false);
                    SetLocalPosAndRot(resultTrs, prototype.position, prototype.rotation, prototype.scale);
                }
                else
                {
                    if (result.scene != _scene)
                    {
                        try
                        {
                            SceneManager.MoveGameObjectToScene(result, _scene);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }

#if PURRNET_UNITY_INSTANTIATE_ASYNC
                    // The parent is still being cloned by a pending InstantiateAsync on this
                    // peer: its ids are reserved but not registered yet. Park the child and
                    // attach it once the parent's spawn registers, instead of stranding it.
                    if (_reservedAsyncNetworkIds.Contains(prototype.parentID.Value) &&
                        result.TryGetComponent<NetworkIdentity>(out var orphan))
                    {
                        if (!_orphansWaitingForAsyncParent.TryGetValue(prototype.parentID.Value, out var orphans))
                        {
                            orphans = ListPool<OrphanAwaitingAsyncParent>.Instantiate();
                            _orphansWaitingForAsyncParent.Add(prototype.parentID.Value, orphans);
                        }
                        orphans.Add(new OrphanAwaitingAsyncParent(orphan, prototype));
                    }
                    else
                    {
                        PurrLogger.LogError(
                            $"Failed to find parent for '{result.name}' with id '{prototype.parentID}'.",
                            result);
                    }
#else
                    PurrLogger.LogError($"Failed to find parent for '{result.name}' with id '{prototype.parentID}'.",
                        result);
#endif
                }
            }
            else if (prototype.defaultParentSiblingIndex.HasValue &&
                     result.TryGetComponent<NetworkIdentity>(out var nid) && nid.defaultParent)
            {
                result.transform.SetParent(nid.defaultParent, false);
                result.transform.SetSiblingIndex(prototype.defaultParentSiblingIndex.Value);
                SetLocalPosAndRot(resultTrs, prototype.position, prototype.rotation, prototype.scale);
            }
            else
            {
                if (result.scene != _scene)
                {
                    try
                    {
                        SceneManager.MoveGameObjectToScene(result, _scene);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
                SetLocalPosAndRot(resultTrs, prototype.position, prototype.rotation, prototype.scale);
            }

            if (shouldActivate && !result.activeSelf)
                result.SetActive(true);

            return result;
        }

        HashSet<NetworkIdentity> _toSpawnNextFrame = new HashSet<NetworkIdentity>();
        HashSet<NetworkIdentity> _toSpawnNextFrameBuffer = new HashSet<NetworkIdentity>();

        readonly List<SpawnID> _toCompleteNextFrame = new List<SpawnID>();

        /// <summary>
        /// For manual spawning of identities with custom spawn data.
        /// Custom data is deserialized after identity setup and before early-spawn callbacks.
        /// After this call, you should call <see cref="ManualFinalizeSpawn(NetworkIdentity)"/> to finalize the spawning.
        /// This needs to be called manually on all conserned clients.
        /// </summary>
        [PublicAPI]
        public void ManualEarlySpawn(NetworkIdentity identity, NetworkID id, BitData customData = default)
        {
            _spawnedIdentities.Add(identity);
            _spawnedIdentitiesMap.Add(id, identity);

            bool isHost = IsServerHost();

            identity.isManualSpawn = true;
            identity.SetID(id);
            identity.SetIdentity(_manager, this, _sceneId, _asServer, isHost);

            if (customData.bitLength > 0 && customData.packer != null)
            {
                using var scope = customData.AutoScope();
                identity.TriggerOnDeserialize(customData.packer);
            }

            identity.TriggerEarlySpawnEvent(_asServer);
            if (isHost) identity.TriggerEarlySpawnEvent(false);

            onEarlyIdentityAdded?.Invoke(identity);
        }

        /// <summary>
        /// For manual despawning of identities.
        /// </summary>
        public void ManualDespawn(NetworkIdentity identity)
        {
            if (!_asServer)
                return;

            var observersCopy = ListPool<PlayerID>.Instantiate();
            observersCopy.AddRange(identity.observers);
            for (var i = 0; i < observersCopy.Count; i++)
                ManualRemoveObserver(identity, observersCopy[i]);
            ListPool<PlayerID>.Destroy(observersCopy);

            TriggerDespawnEvent(identity);
            UnregisterIdentity(identity);

            identity.SetIsSpawned(false, false);
            onIdentityRemoved?.Invoke(identity);
        }

        /// <summary>
        /// For manual finalization of spawning an identity.
        /// This needs to be called manually on all conserned clients.
        /// </summary>
        public void ManualFinalizeSpawn(NetworkIdentity identity)
        {
            bool isHost = IsServerHost();

            identity.TriggerOnSpawnReceived();

            identity.TriggerSpawnEvent(_asServer);
            if (isHost) identity.TriggerSpawnEvent(false);

            onIdentityAdded?.Invoke(identity);
        }

        /// <summary>
        /// Once the identity is created, you should call this method to refresh visibility for all players in the scene.
        /// This will send visibility updates to all players in the scene.
        /// </summary>
        public void ManualAddObserver(NetworkIdentity identity, PlayerID player)
        {
            if (!_asServer)
                return;

            if (identity.TryAddObserver(player))
            {
                onObserverAdded?.Invoke(player, identity);
                identity.TriggerOnPreObserverAdded(player, true);


                identity.TriggerOnObserverAdded(player, true);
                onLateObserverAdded?.Invoke(player, identity);

                if (identity.id.HasValue)
                    onSentSpawnPacket?.Invoke(player, _sceneId, identity.id.Value);
            }
        }

        /// <summary>
        /// Manually remove an observer from the identity.
        /// </summary>
        public void ManualRemoveObserver(NetworkIdentity identity, PlayerID player)
        {
            if (!_asServer)
                return;

            if (!HasActiveAsyncObserverState)
            {
                if (identity.TryRemoveObserver(player))
                {
                    ClearPendingLateObserverAdded(player, identity);
                    identity.TriggerOnObserverRemoved(player);
                    onObserverRemoved?.Invoke(player, identity);
                }
                return;
            }

            var identities = ListPool<NetworkIdentity>.Instantiate();
            var cancelledRoots = ListPool<NetworkIdentity>.Instantiate();
            var confirmedRemoved = ListPool<NetworkIdentity>.Instantiate();
            var failedRoots = ListPool<NetworkIdentity>.Instantiate();
            identity.gameObject.GetComponents(identities);
            ConsumeFailedAsyncObserverRoots(player, identities, failedRoots);
            RemovePendingAsyncObservers(player, identities, null, cancelledRoots, confirmedRemoved);

            bool identityHandled = false;
            for (var i = 0; i < cancelledRoots.Count; i++)
            {
                var root = cancelledRoots[i];
                if (!root)
                    continue;
                identityHandled |= identity.transform.IsChildOf(root.transform);
                SendDespawnPacket(player, root, false);
            }
            for (var i = 0; i < failedRoots.Count; i++)
            {
                var root = failedRoots[i];
                if (root)
                    identityHandled |= identity.transform.IsChildOf(root.transform);
            }

            ListPool<NetworkIdentity>.Destroy(identities);
            ListPool<NetworkIdentity>.Destroy(cancelledRoots);
            ListPool<NetworkIdentity>.Destroy(failedRoots);

            for (var i = 0; i < confirmedRemoved.Count; i++)
            {
                var removed = confirmedRemoved[i];
                if (!removed)
                    continue;
                identityHandled |= removed == identity;
                ClearPendingLateObserverAdded(player, removed);
                removed.TriggerOnObserverRemoved(player);
                onObserverRemoved?.Invoke(player, removed);
            }
            ListPool<NetworkIdentity>.Destroy(confirmedRemoved);

            if (identityHandled)
                return;

            if (identity.TryRemoveObserver(player))
            {
                ClearPendingLateObserverAdded(player, identity);
                identity.TriggerOnObserverRemoved(player);
                onObserverRemoved?.Invoke(player, identity);
            }
        }

        /// <summary>
        /// Local spawn will trigger the spawn event next frame immediately after the identity is registered.
        /// </summary>
        private void RegisterIdentity(NetworkIdentity identity, bool isLocalSpawn, bool triggerEarlySpawn = true)
        {
            if (identity && identity.id.HasValue)
            {
                _spawnedIdentities.Add(identity);
                _spawnedIdentitiesMap.Add(identity.id.Value, identity);

                if (triggerEarlySpawn)
                    TriggerEarlySpawnForRegisteredIdentity(identity);

                if (isLocalSpawn)
                    _toSpawnNextFrame.Add(identity);
            }
        }

        private void TriggerEarlySpawnForRegisteredIdentity(NetworkIdentity identity)
        {
            if (!identity || !identity.id.HasValue)
                return;

            identity.TriggerEarlySpawnEvent(_asServer);
            if (_asServer && _manager.isClient)
                identity.TriggerEarlySpawnEvent(false);

            onEarlyIdentityAdded?.Invoke(identity);
        }

        private void TriggerDespawnEvent(NetworkIdentity identity, bool preserveModules = false)
        {
            if (_asServer && IsServerHost())
                identity.TriggerDespawnEvent(false, preserveModules);
            identity.TriggerDespawnEvent(_asServer, preserveModules);
        }

        private void UnregisterIdentity(NetworkIdentity identity)
        {
            if (identity.id.HasValue)
            {
                RemoveFailedAsyncObserverRoots(identity.id.Value);
                _spawnedIdentities.Remove(identity);
                _spawnedIdentitiesMap.Remove(identity.id.Value);
                onIdentityRemoved?.Invoke(identity);
            }
        }

        private void RemoveFailedAsyncObserverRoots(NetworkID root)
        {
            if (_failedAsyncObserverRoots.Count == 0)
                return;

            _failedAsyncObserverRoots.RemoveWhere(pair => pair.root == root);
        }

        internal void CleanupDestroyedIdentity(NetworkIdentity identity)
        {
            _toSpawnNextFrame.Remove(identity);
            _toSpawnNextFrameBuffer.Remove(identity);

            var nid = identity.GetNetworkID(_asServer) ?? identity.id;
            if (!nid.HasValue)
                return;

            // a proper Despawn already unregistered it; nothing left to clean up
            if (!_spawnedIdentitiesMap.TryGetValue(nid.Value, out var registered) ||
                !ReferenceEquals(registered, identity))
                return;

            if (!HasActiveAsyncObserverState)
            {
                if (_enabled && !_isDisposed && _asServer && _playersManager != null &&
                    identity.observers.Count > 0)
                {
                    using var syncTargets = DisposableList<PlayerID>.Create(identity.observers);
                    if (_playersManager.localPlayerId.HasValue)
                        syncTargets.Remove(_playersManager.localPlayerId.Value);
                    if (syncTargets.Count > 0)
                        _playersManager.Send(syncTargets,
                            new DespawnPacket { sceneId = _sceneId, parentId = nid.Value });
                }

                _spawnedIdentities.Remove(identity);
                _spawnedIdentitiesMap.Remove(nid.Value);
                onIdentityRemoved?.Invoke(identity);
                return;
            }

            var destroyed = ListPool<NetworkIdentity>.Instantiate();
            GetComponentsInChildren(identity.gameObject, destroyed);

            using var targets = DisposableList<PlayerID>.Create(identity.observers);
            for (var identityIndex = 0; identityIndex < destroyed.Count; identityIndex++)
            {
                var member = destroyed[identityIndex];
                if (!member)
                    continue;
                for (var i = 0; i < member.observers.Count; i++)
                {
                    var observer = member.observers[i];
                    if (!targets.Contains(observer))
                        targets.Add(observer);
                }
                for (var i = 0; i < member.pendingObservers.Count; i++)
                {
                    var pendingPlayer = member.pendingObservers[i];
                    if (!targets.Contains(pendingPlayer))
                        targets.Add(pendingPlayer);
                }
            }

            for (var i = targets.Count - 1; i >= 0; i--)
            {
                var target = targets[i];
                var cancelledRoots = ListPool<NetworkIdentity>.Instantiate();
                var confirmedRemoved = ListPool<NetworkIdentity>.Instantiate();
                var failedRoots = ListPool<NetworkIdentity>.Instantiate();
                ConsumeFailedAsyncObserverRoots(target, destroyed, failedRoots);
                RemovePendingAsyncObservers(target, destroyed, null, cancelledRoots, confirmedRemoved);

                for (var removedIndex = 0; removedIndex < confirmedRemoved.Count; removedIndex++)
                {
                    var removed = confirmedRemoved[removedIndex];
                    if (!removed || removed == identity)
                        continue;
                    ClearPendingLateObserverAdded(target, removed);
                    removed.TriggerOnObserverRemoved(target);
                    onObserverRemoved?.Invoke(target, removed);
                }

                if (_enabled && !_isDisposed && _asServer && _playersManager != null &&
                    (!_playersManager.localPlayerId.HasValue || target != _playersManager.localPlayerId.Value))
                {
                    bool identityCovered = false;
                    for (var rootIndex = 0; rootIndex < cancelledRoots.Count; rootIndex++)
                    {
                        var root = cancelledRoots[rootIndex];
                        if (!root)
                            continue;
                        identityCovered |= identity.transform.IsChildOf(root.transform);
                        SendDespawnPacket(target, root, false);
                    }
                    for (var rootIndex = 0; rootIndex < failedRoots.Count; rootIndex++)
                    {
                        var root = failedRoots[rootIndex];
                        if (root)
                            identityCovered |= identity.transform.IsChildOf(root.transform);
                    }

                    if (!identityCovered)
                        SendDespawnPacket(target, identity, false);
                }

                ListPool<NetworkIdentity>.Destroy(cancelledRoots);
                ListPool<NetworkIdentity>.Destroy(confirmedRemoved);
                ListPool<NetworkIdentity>.Destroy(failedRoots);
            }
            ListPool<NetworkIdentity>.Destroy(destroyed);
            RemoveFailedAsyncObserverRoots(nid.Value);

            _spawnedIdentities.Remove(identity);
            _spawnedIdentitiesMap.Remove(nid.Value);
            onIdentityRemoved?.Invoke(identity);
        }


        public bool TryGetIdentity(NetworkID id, out NetworkIdentity identity)
        {
            if (_spawnedIdentitiesMap.TryGetValue(id, out identity))
                return identity;

            if (!_asServer && _manager.isServer)
            {
                if (_manager.TryGetModule<HierarchyFactory>(true, out var factory) &&
                    factory.TryGetHierarchy(_sceneId, out var other))
                {
                    return other.TryGetIdentity(id, out identity);
                }
            }

            return false;
        }

    }
}
