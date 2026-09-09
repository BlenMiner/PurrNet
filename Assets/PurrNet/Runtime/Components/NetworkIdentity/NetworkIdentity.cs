using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    public delegate void NidParentChanged(NetworkIdentity oldParent, NetworkIdentity newParent);

    [DefaultExecutionOrder(-1000)]
    public partial class NetworkIdentity : MonoBehaviour
    {
        [SerializeField, HideInInspector] private bool _isSetup;

        [SerializeField, HideInInspector] private int _prefabId = int.MinValue;

        [SerializeField, HideInInspector] private bool _hasPrefabScope;

        [SerializeField, HideInInspector] private ushort _prefabScope;

        [SerializeField, HideInInspector] private int _componentIndex = int.MinValue;

        [SerializeField, HideInInspector] private bool _shouldBePooled;

        [SerializeField, HideInInspector] private NetworkIdentity _parent;

        [SerializeField, HideInInspector] private int[] _invertedPathToNearestParent;

        private bool _ownsInvertedPath;

        [SerializeField, HideInInspector] private List<NetworkIdentity> _directChildren;

        private NetworkIdentity[] _siblingIdentities;

        internal NetworkIdentity[] siblingIdentities
        {
            get
            {
                if (_siblingIdentities != null)
                    return _siblingIdentities;

                var siblings = GetComponents<NetworkIdentity>();
                for (var i = 0; i < siblings.Length; i++)
                    siblings[i]._siblingIdentities = siblings;
                return siblings;
            }
        }

        private void InvalidateSiblingIdentities()
        {
            var siblings = _siblingIdentities;
            if (siblings == null)
                return;

            for (var i = 0; i < siblings.Length; i++)
                siblings[i]._siblingIdentities = null;
        }

        public event Action<PlayerID> onObserverAdded;

        public event Action<PlayerID> onObserverRemoved;

        internal Transform defaultParent { get; private set; }

        public RollbackModule rollbackModule
        {
            get
            {
                if (!networkManager)
                    return null;

                if (networkManager.TryGetModule<ColliderRollbackFactory>(isServer, out var factory) &&
                    factory.TryGetModule(sceneId, out var module))
                    return module;

                return null;
            }
        }

        public double rollbackTick => networkManager ? networkManager.tickModule.rollbackTick : 0;

        public ReadOnlySpan<int> invertedPathToNearestParent => _invertedPathToNearestParent;

        internal int[] invertedPathToNearestParentArray
        {
            get => _invertedPathToNearestParent;
            set
            {
                _invertedPathToNearestParent = value;
                _ownsInvertedPath = false;
            }
        }

        public IReadOnlyList<NetworkIdentity> directChildren => _directChildren;

        /// <summary>
        /// The nearest network parent of this object.
        /// This can differ from transform.parent.
        /// Especially if syncing the parent is disabled.
        /// </summary>
        public NetworkIdentity parent
        {
            get => _parent;
            internal set
            {
                if (_parent == value)
                    return;

                var oldParent = _parent;
                _parent = value;
                onParentChanged?.Invoke(oldParent, value);
            }
        }

        /// <summary>
        /// Called when the network parent of this object changes.
        /// This will be the closest parent with a NetworkIdentity.
        /// </summary>
        public event NidParentChanged onParentChanged;

        public int prefabId => _prefabId;

        /// <summary>
        /// The prefab id including its scene scope, if the prefab was registered per scene.
        /// </summary>
        public PrefabID scopedPrefabId => _hasPrefabScope
            ? new PrefabID(_prefabId, new SceneID(_prefabScope))
            : new PrefabID(_prefabId);

        public int componentIndex => _componentIndex;

        public bool shouldBePooled => _shouldBePooled;

        public bool isSetup => _isSetup;

        public bool skipSceneAutoSpawning { get; set; }

        /// <summary>
        /// Used for internal cleanup, avoid using this.
        /// </summary>
        public void ResetIsSetup()
        {
            _isSetup = false;
            InvalidateSiblingIdentities();
        }

        public void PreparePrefabInfo(int prefabId, int componentIndex, bool shouldBePooled, bool isSceneObject)
        {
            PreparePrefabInfo(new PrefabID(prefabId), componentIndex, shouldBePooled, isSceneObject);
        }

        public void PreparePrefabInfo(PrefabID prefabId, int componentIndex, bool shouldBePooled, bool isSceneObject)
        {
            if (isSceneObject && skipSceneAutoSpawning)
                return;

            _isSetup = true;

            if (isSceneObject)
                defaultParent = transform.parent;

            this.isSceneObject = isSceneObject;

            this._prefabId = (int)prefabId;
            this._hasPrefabScope = prefabId.scope.HasValue;
            this._prefabScope = prefabId.scope.HasValue ? (ushort)prefabId.scope.Value.id.value : (ushort)0;
            this._componentIndex = componentIndex;
            this._shouldBePooled = shouldBePooled;

            parent = GetNearestParent();

            RecalculateNearestPath();

            var firstIdentity = GetComponent<NetworkIdentity>();
            // Preparing a newly added component must also invalidate the existing siblings.
            firstIdentity.InvalidateSiblingIdentities();

            if (firstIdentity != this)
            {
                if (_directChildren == null)
                    _directChildren = new List<NetworkIdentity>();
                else _directChildren.Clear();
            }
            else RecalculateDirectChildren();
        }

        internal void RecalculateDirectChildren()
        {
            using var dChildren = DisposableList<TransformIdentityPair>.Create(16);
            HierarchyPool.GetDirectChildren(transform, dChildren);

            if (isSceneObject)
            {
                for (var i = 0; i < dChildren.Count; i++)
                {
                    var child = dChildren[i];
                    if (child.identity.skipSceneAutoSpawning)
                        dChildren.RemoveAt(i--);
                }
            }

            _directChildren ??= new List<NetworkIdentity>(dChildren.Count);
            _directChildren.Clear();
            for (int i = 0; i < dChildren.Count; i++)
                _directChildren.Add(dChildren[i].identity);
        }

        internal void ClearDirectChildren()
        {
            _directChildren.Clear();
        }

        internal void AddDirectChild(NetworkIdentity identity)
        {
            if (_directChildren.Contains(identity))
                return;
            _directChildren.Add(identity);
        }

        internal void RemoveDirectChild(NetworkIdentity identity)
        {
            _directChildren.Remove(identity);
        }

        internal void RecalculateNearestPath()
        {
            if (_parent)
            {
                using var invPath = HierarchyPool.GetInvPath(_parent.transform, transform);

                if (HierarchyPool.TryInternPath(invPath, out var interned))
                {
                    _invertedPathToNearestParent = interned;
                    _ownsInvertedPath = false;
                    return;
                }

                if (!_ownsInvertedPath || _invertedPathToNearestParent.Length != invPath.Count)
                {
                    _invertedPathToNearestParent = new int[invPath.Count];
                    _ownsInvertedPath = true;
                }

                for (var i = 0; i < invPath.Count; i++)
                    _invertedPathToNearestParent[i] = invPath[i];
            }
            else
            {
                _invertedPathToNearestParent = Array.Empty<int>();
                _ownsInvertedPath = false;
            }
        }

        /// <summary>
        /// Navigates the hierarchy to find the nearest parent with a NetworkIdentity.
        /// </summary>
        /// <returns>Nearest parent with a NetworkIdentity</returns>
        public NetworkIdentity GetNearestParent()
        {
            var current = transform.parent;
            while (current)
            {
                if (current.TryGetComponent(out NetworkIdentity identity))
                    return identity;

                current = current.parent;
            }

            return null;
        }

        /// <summary>
        /// Network id of this object. Holds more information than the ObjectId
        /// </summary>
        public NetworkID? id => _idServer ?? _idClient;

        public NetworkID? GetNetworkID(bool asServer) => asServer ? _idServer : _idClient;

        /// <summary>
        /// Unique ObjectId of this object
        /// </summary>
        public ulong objectId => id?.id.value ?? 0;

        /// <summary>
        /// Scene id of this object.
        /// </summary>
        public SceneID sceneId { get; private set; }

        /// <summary>
        /// Is spawned over the network.
        /// </summary>
        public bool isSpawned => _isSpawnedServer || _isSpawnedClient;

        public bool isSceneObject { get; private set; }

        public bool isServer => isSpawned && networkManager.isServer;

        [UsedByIL] public bool isServerOnly => isSpawned && networkManager.isServerOnly;

        public bool isClient => isSpawned && networkManager.isClient;

        public bool isClientAndObserving => isClient && _observers.Contains(localPlayerForced);

        public bool isHost => isSpawned && networkManager.isHost;

        public bool isOwner => isSpawned && localPlayer.HasValue && owner == localPlayer;

        public bool hasOwner => owner.HasValue;

        Queue<Action> _onSpawnedQueue;

        /// <summary>
        /// Returns if you can control this object.
        /// If the object has an owner, it will return if you are the owner.
        /// If the object doesn't have an owner, it will return if you are the server.
        /// </summary>
        [UsedImplicitly]
        public bool isController => isSpawned && (hasConnectedOwner ? isOwner : isServer);

        /// <summary>
        /// Returns if you can control this object.
        /// If ownerHasAuthority is true, it will return true if you are the owner.
        /// If ownerHasAuthority is false, it will return true if you are the server.
        /// Otherwise, similar to isController.
        /// </summary>
        /// <param name="ownerHasAuthority">Should owner be controller or is it server only</param>
        /// <returns>Can you control this identity</returns>
        [UsedImplicitly]
        public bool IsController(bool ownerHasAuthority) => ownerHasAuthority ? isController : isServer;

        public bool IsController(bool ownerHasAuthority, bool asServer) => ownerHasAuthority ? isController : asServer;

        public bool IsController(PlayerID player, bool ownerHasAuthority, bool asServer)
        {
            if (!ownerHasAuthority)
                return asServer;

            if (!hasConnectedOwner)
                return asServer;

            if (player == owner)
                return true;

            return asServer;
        }

        /// <summary>
        /// Whether this object's owner is currently connected.
        /// On the server this means the owner is loaded in this scene; on a client it means the owner is connected.
        /// Cached and refreshed on owner-change / owner-disconnect / owner-reconnect / spawn events.
        /// </summary>
        public bool hasConnectedOwner => _cachedHasConnectedOwner;

        private bool _cachedHasConnectedOwner;

        private bool ComputeHasConnectedOwner()
        {
            if (!owner.HasValue || !isSpawned)
                return false;

            if (isServer)
            {
                return networkManager.TryGetModule<ScenePlayersModule>(true, out var scenesModule) &&
                       scenesModule.IsPlayerLoadedInScene(owner.Value, sceneId);
            }

            return networkManager.TryGetModule<PlayersManager>(false, out var module) &&
                   module.IsPlayerConnected(owner.Value);
        }

        internal void RecacheHasConnectedOwner()
        {
            _cachedHasConnectedOwner = ComputeHasConnectedOwner();
        }

        internal PlayerID? internalOwnerServer;
        internal PlayerID? internalOwnerClient;

        private TickManager _serverTickManager;
        private TickManager _clientTickManager;

        private bool _isSpawnedClient;
        private bool _isSpawnedServer;

        private NetworkID? _idServer;
        private NetworkID? _idClient;

        /// <summary>
        /// Returns the owner of this object.
        /// It will return the owner of the closest parent object.
        /// If you can, cache this value for performance.
        /// </summary>
        public PlayerID? owner => isServer ? internalOwnerServer : internalOwnerClient;

        public NetworkManager networkManager { get; private set; }

        private HierarchyV2 _clientHierarchy;
        private HierarchyV2 _serverHierarchy;

        private PlayerID? _localPlayer;

        /// <summary>
        /// The cached value of the local player.
        /// </summary>
        public PlayerID? localPlayer
        {
            get
            {
                if (_localPlayer.HasValue)
                    return _localPlayer;

                if (networkManager && networkManager.TryGetModule<PlayersManager>(false, out var players))
                {
                    _localPlayer = players.localPlayerId;
                    return _localPlayer;
                }

                return null;
            }
        }

        /// <summary>
        /// Returns the local player if it exists.
        /// Defaults to default(PlayerID) if it doesn't exist.
        /// </summary>
        [UsedByIL]
        public PlayerID localPlayerForced => localPlayer ?? default;

        private readonly List<PlayerID> _observers = new List<PlayerID>(4);
        private List<PlayerID> _pendingObservers;

        public IReadOnlyList<PlayerID> observers => _observers;

        internal IReadOnlyList<PlayerID> pendingObservers =>
            _pendingObservers != null ? _pendingObservers : Array.Empty<PlayerID>();

        internal bool hasPendingObservers => _pendingObservers is { Count: > 0 };

        internal bool pendingObserverStorageAllocated => _pendingObservers != null;

        public bool IsObserver(PlayerID player) => _observers.Contains(player);

        internal bool IsObserverOrPending(PlayerID player) =>
            _observers.Contains(player) || _pendingObservers?.Contains(player) == true;

        private void ReleasePendingObserversIfEmpty()
        {
            if (_pendingObservers is not { Count: 0 })
                return;

            ReleasePendingObservers();
        }

        private void ReleasePendingObservers()
        {
            if (_pendingObservers == null)
                return;

            var cached = _pendingObservers;
            _pendingObservers = null;
            ListPool<PlayerID>.Destroy(cached);
        }

        [UsedByIL, UsedImplicitly]
        public virtual void OnReceivedRpc(int id, RPCPacket packet, RPCInfo info, bool asServer) { }

        [UsedByIL, UsedImplicitly]
        public static void OnReceivedRpc(int id, StaticRPCPacket packet, RPCInfo info, bool asServer) { }

        [UsedImplicitly]
        public void QueueOnSpawned(Action action)
        {
            _onSpawnedQueue ??= new Queue<Action>();
            _onSpawnedQueue.Enqueue(action);
        }

        public NetworkIdentity GetRootIdentity()
        {
            if (!this)
                return null;

            var lastKnown = gameObject.GetComponent<NetworkIdentity>();
            var currentParent = parent;

            while (currentParent)
            {
                lastKnown = currentParent;
                currentParent = currentParent.parent;
            }

            return lastKnown;
        }

        private IServerSceneEvents _serverSceneEvents;
        private int onTickCount;
        private ITick _ticker;

        private readonly List<ITick> _tickables = new List<ITick>();

        [ContextMenu("PurrNet/Take Ownership"), PurrContextButton]
        protected void TakeOwnership()
        {
            GiveOwnership(localPlayer);
        }

        [ContextMenu("PurrNet/Print Prototype"), PurrContextButton]
        private void PrintPrototype()
        {
            using var prototype = HierarchyPool.GetFullPrototype(transform);
            PurrLogger.Log(prototype.ToString());
        }

        [ContextMenu("PurrNet/Duplicate Prototype"), PurrContextButton]
        private void DuplicatePrototype()
        {
            Duplicate();
        }

        /// <summary>
        /// Duplicates the object.
        /// </summary>
        public GameObject Duplicate()
        {
            using var prototype = HierarchyPool.GetFullPrototype(transform);
            if (networkManager.TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                factory.TryGetHierarchy(sceneId, out var hierarchy))
            {
                var go = hierarchy.CreatePrototype(prototype, new List<NetworkIdentity>());
                hierarchy.InternalSpawn(go);
                return go;
            }

            return null;
        }

        [ContextMenu("PurrNet/Destroy GameObject"), PurrContextButton]
        private void DeleteGameObject()
        {
            Destroy(gameObject);
        }

        private void InternalOnSpawn(bool asServer)
        {
            // ReSharper disable once SuspiciousTypeConversion.Global
            if (_ticker == null && this is ITick ticker)
                _ticker = ticker;

            if (_ticker != null || _tickables.Count > 0)
            {
                RegisterTickEvent(asServer);
            }

            if (networkManager.TryGetModule<PlayersManager>(asServer, out var players))
            {
                // ReSharper disable once SuspiciousTypeConversion.Global
                if (this is IPlayerEvents events)
                {
                    players.onPlayerJoined += events.OnPlayerConnected;
                    players.onPlayerLeft += events.OnPlayerDisconnected;
                }

                if (networkManager.TryGetModule<ScenePlayersModule>(asServer, out var scenePlayers))
                {
                    // ReSharper disable once SuspiciousTypeConversion.Global
                    if (this is IServerSceneEvents sceneEvents)
                    {
                        _serverSceneEvents = sceneEvents;
                        scenePlayers.onPlayerLoadedScene += OnServerJoinedScene;
                        scenePlayers.onPlayerUnloadedScene += OnServerLeftScene;
                    }
                }
            }
        }

        private int _tickRegisteredServer;
        private int _tickRegisteredClient;
        private int _serverTickHandle = PurrAction<NetworkIdentity>.InvalidHandle;
        private int _clientTickHandle = PurrAction<NetworkIdentity>.InvalidHandle;

        private void RegisterTickEvent(bool asServer)
        {
            if (asServer)
            {
                if (_tickRegisteredServer++ > 0)
                    return;

                _serverTickManager = networkManager.GetModule<TickManager>(true);
                _serverTickHandle = _serverTickManager.AddIdentityTick(this);
            }
            else
            {
                if (_tickRegisteredClient++ > 0)
                    return;

                _clientTickManager = networkManager.GetModule<TickManager>(false);
                _clientTickHandle = _clientTickManager.AddIdentityTick(this);
            }
        }

        private void InternalOnDespawn(bool asServer)
        {
            if (_ticker != null || _tickables.Count > 0)
            {
                UnregisterTickEvent(asServer);
            }

            if (!networkManager.TryGetModule<PlayersManager>(asServer, out var players)) return;

            // ReSharper disable once SuspiciousTypeConversion.Global
            if (this is IPlayerEvents events)
            {
                players.onPlayerJoined -= events.OnPlayerConnected;
                players.onPlayerLeft -= events.OnPlayerDisconnected;
            }

            if (!networkManager.TryGetModule<ScenePlayersModule>(asServer, out var scenePlayers)) return;

            if (_serverSceneEvents == null) return;

            scenePlayers.onPlayerLoadedScene -= OnServerJoinedScene;
            scenePlayers.onPlayerUnloadedScene -= OnServerLeftScene;
        }

        private void UnregisterTickEvent(bool asServer)
        {
            if (asServer)
            {
                if (--_tickRegisteredServer <= 0)
                {
                    if (_serverTickManager != null)
                        _serverTickManager.RemoveIdentityTick(_serverTickHandle, this);
                    _serverTickHandle = PurrAction<NetworkIdentity>.InvalidHandle;
                }
            }
            else if (--_tickRegisteredClient <= 0)
            {
                if (_clientTickManager != null)
                    _clientTickManager.RemoveIdentityTick(_clientTickHandle, this);
                _clientTickHandle = PurrAction<NetworkIdentity>.InvalidHandle;
            }
        }

        void OnServerJoinedScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (scene == sceneId)
                _serverSceneEvents?.OnPlayerLoadedScene(player);
        }

        void OnServerLeftScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (scene == sceneId)
                _serverSceneEvents?.OnPlayerUnloadedScene(player);
        }

        internal void ClientTick()
        {
            InternalTick();

            try
            {
                _ticker?.OnTick(_clientTickManager.tickDelta);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (var i = 0; i < _tickables.Count; i++)
            {
                try
                {
                    var ticker = _tickables[i];
                    ticker.OnTick(_clientTickManager.tickDelta);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        internal void ServerTick()
        {
            if (_tickRegisteredClient <= 0)
            {
                InternalTick();

                try
                {
                    _ticker?.OnTick(_serverTickManager.tickDelta);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                for (var i = 0; i < _tickables.Count; i++)
                {
                    try
                    {
                        var ticker = _tickables[i];
                        ticker.OnTick(_serverTickManager.tickDelta);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        private void InternalTick()
        {
            if (_whiteBlackDirty)
            {
                foreach (var player in _whiteBlackDirtyPlayers)
                    EvaluateVisibility(player);

                _whiteBlackDirtyPlayers.Clear();
                _whiteBlackDirty = false;
                UnregisterTickEvent(true);
            }
        }

        internal PlayerID? GetOwner(bool asServer) => asServer ? internalOwnerServer : internalOwnerClient;

        internal bool HasOwner(bool asServer) => GetOwner(asServer).HasValue;

        [UsedImplicitly]
        public bool IsSpawned(bool asServer) => asServer ? _isSpawnedServer : _isSpawnedClient;

        /// <summary>
        /// Invokes custom spawn serialization on all external modules and then this identity.
        /// This is used internally for spawn packets and can be used by manual spawn flows that need the same data pipeline.
        /// </summary>
        /// <param name="packer">The packer to write custom data into.</param>
        public void TriggerOnSerialize(BitPacker packer)
        {
            for (int i = 0; i < _externalModulesView.Count; i++)
                _externalModulesView[i].OnSerialize(packer);
            OnSerialize(packer);
        }

        /// <summary>
        /// Invokes custom spawn deserialization on all external modules and then this identity.
        /// Manual spawn flows should call this after identity setup and before early-spawn if they need spawn-packet ordering.
        /// </summary>
        /// <param name="packer">The packer to read custom data from.</param>
        public void TriggerOnDeserialize(BitPacker packer)
        {
            for (int i = 0; i < _externalModulesView.Count; i++)
                _externalModulesView[i].OnDeserialize(packer);
            OnDeserialize(packer);
        }

        /// <summary>
        /// Called on the spawner to attach custom data to the spawn packet.
        /// Whatever you write here travels with the object and is read back in OnDeserialize.
        /// </summary>
        /// <param name="packer">The packer to write your data into</param>
        protected virtual void OnSerialize(BitPacker packer)
        {
        }

        /// <summary>
        /// Called on peers that create the object, after the spawn packet arrives.
        /// Read the data back in the same order you wrote it in OnSerialize.
        /// Only called if something was actually written during OnSerialize.
        /// </summary>
        /// <param name="packer">The packer to read your data from</param>
        protected virtual void OnDeserialize(BitPacker packer)
        {
        }

        /// <summary>
        /// Called when this object is spawned
        /// This is only called once even if in host mode.
        /// </summary>
        protected virtual void OnSpawned()
        {
        }

        /// <summary>
        /// Called when this object is spawned but before any other data is received.
        /// At this point you might be missing ownership data, module data, etc.
        /// This is only called once even if in host mode.
        /// </summary>
        protected virtual void OnEarlySpawn()
        {
        }

        /// <summary>
        /// Called when this object is spawned but before any other data is received.
        /// At this point you might be missing ownership data, module data, etc.
        /// This is called twice in host mode, once for the server and once for the client.
        /// </summary>
        protected virtual void OnEarlySpawn(bool asServer)
        {
        }

        /// <summary>
        /// Called when this object is de-spawned.
        /// This is only called once even if in host mode.
        /// </summary>
        protected virtual void OnDespawned()
        {
        }

        /// <summary>
        /// Called when this object is spawned.
        /// This might be called twice times in host mode.
        /// Once for the server and once for the client.
        /// </summary>
        /// <param name="asServer">Is this on the server</param>
        protected virtual void OnSpawned(bool asServer)
        {
        }

        /// <summary>
        /// Called before the NetworkModules are initialized.
        /// You can use this to update their values before they are networked.
        /// </summary>
        protected virtual void OnInitializeModules()
        {
        }

        /// <summary>
        /// Called when this object is de-spawned.
        /// This might be called twice times in host mode.
        /// Once for the server and once for the client.
        /// </summary>
        /// <param name="asServer">Is this on the server</param>
        protected virtual void OnDespawned(bool asServer)
        {
        }

        /// <summary>
        /// Called when the owner of this object changes.
        /// </summary>
        /// <param name="oldOwner">The old owner of this object</param>
        /// <param name="newOwner">The new owner of this object</param>
        /// <param name="asServer">Is this on the server</param>
        protected virtual void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
        }

        /// <summary>
        /// Called when the owner of this object changes.
        /// </summary>
        /// <param name="oldOwner">The old owner of this object</param>
        /// <param name="newOwner">The new owner of this object</param>
        /// <param name="isSpawner">True when this fires as part of the spawn pipeline and the newOwner is the spawner. False for all other ownership changes (including post-spawn GiveOwnership/RemoveOwnership).</param>
        /// <param name="asServer">Is this on the server</param>
        protected virtual void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool isSpawner, bool asServer)
        {
        }

        /// <summary>
        /// Called when the owner of this object disconnects.
        /// Server only.
        /// </summary>
        /// <param name="ownerId">The current owner id</param>
        protected virtual void OnOwnerDisconnected(PlayerID ownerId)
        {
        }

        /// <summary>
        /// Called when the owner of this object reconnects.
        /// Server only.
        /// </summary>
        /// <param name="ownerId">The current owner id</param>
        protected virtual void OnOwnerReconnected(PlayerID ownerId)
        {
        }

        /// <summary>
        /// Called when an observer is added.
        /// Server only.
        /// </summary>
        /// <param name="player">The observer player id</param>
        protected virtual void OnPreObserverAdded(PlayerID player)
        {
        }

        /// <summary>
        /// Called when an observer is added.
        /// Server only.
        /// </summary>
        /// <param name="player">The observer player id</param>
        /// <param name="isSpawner">If this object was just spawned and the observer is the spawner</param>
        protected virtual void OnPreObserverAdded(PlayerID player, bool isSpawner)
        {
        }

        /// <summary>
        /// Called when an observer is added.
        /// Server only.
        /// </summary>
        /// <param name="player">The observer player id</param>
        protected virtual void OnObserverAdded(PlayerID player)
        {
        }

        /// <summary>
        /// Called when an observer is added.
        /// Server only.
        /// </summary>
        /// <param name="player">The observer player id</param>
        /// <param name="isSpawner">If this object was just spawned and the observer is the spawner</param>
        protected virtual void OnObserverAdded(PlayerID player, bool isSpawner)
        {
        }

        /// <summary>
        /// Called when an observer is removed.
        /// Server only.
        /// </summary>
        /// <param name="player">The observer player id</param>
        protected virtual void OnObserverRemoved(PlayerID player)
        {
        }

        [UsedByIL, UsedImplicitly]
        public virtual void CallGeneratedInitMethods() { }

        /// <summary>
        /// The layer of this object. Avoids gameObject.layer.
        /// Only available when spawned.
        /// </summary>
        public int layer { get; private set; }

        public bool isInPool { get; private set; }

        [ContextMenu("PurrNet/Despawn"), PurrContextButton]
        public void Despawn()
        {
            if (isSpawned)
            {
                if (_serverHierarchy != null)
                    _serverHierarchy.Despawn(gameObject, false, false);
                else _clientHierarchy?.Despawn(gameObject, false, false);
            }
            else if (!isInPool)
                UnityProxy.DestroyDirectly(gameObject);
        }

        /// <summary>
        /// Called when this object is put back into the pool.
        /// Use this to reset any values for the next spawn.
        /// </summary>
        protected virtual void OnPoolReset()
        {
        }

        internal void ResetIdentity()
        {
            try
            {
                OnPoolReset();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnPoolReset();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            // notify parent
            if (parent && parent.isSpawned)
                parent.OnChildDespawned(this);

            // reset all values
            TriggerDespawnEvent(false);
            TriggerDespawnEvent(true);
            networkManager = null;
            sceneId = default;
            _localPlayer = null;
            _isSpawnedServer = false;
            _isSpawnedClient = false;
            _idServer = null;
            _idClient = null;
            internalOwnerServer = null;
            internalOwnerClient = null;
            _cachedHasConnectedOwner = false;
            _moduleId = 0;
            _modules.Clear();
            _externalModulesView.Clear();
            _tickables.Clear();
            _visitiblityRules = null;
            _spawnedCount = 0;
            _onSpawnedQueue?.Clear();
            _serverSceneEvents = null;
            _serverTickManager = null;
            _clientTickManager = null;
            _ticker = null;
            isInPool = true;
            _wasEarlySpawned = false;
            _tickRegisteredServer = 0;
            _tickRegisteredClient = 0;
            _whiteBlackDirty = false;
            isManualSpawn = false;
            _whitelist.Clear();
            _blacklist.Clear();
            _whiteBlackDirtyPlayers.Clear();
        }

        private void OnChildDespawned(NetworkIdentity networkIdentity)
        {
            _directChildren.Remove(networkIdentity);
        }

        internal void SetIdentity(NetworkManager manager, HierarchyV2 hierarchy, SceneID scene, bool asServer,
            bool asHost)
        {
            isInPool = false;
            layer = gameObject.layer;
            networkManager = manager;
            sceneId = scene;

            bool wasAlreadySpawned = isSpawned;

            if (asServer)
            {
                _isSpawnedServer = true;
                if (asHost)
                    _isSpawnedClient = true;

                _serverHierarchy = hierarchy;
                internalOwnerServer = null;
            }
            else
            {
                _isSpawnedClient = true;
                _clientHierarchy = hierarchy;
                internalOwnerClient = null;
            }

            if (!wasAlreadySpawned)
            {
                _modules.Clear();
                _externalModulesView.Clear();
                _moduleId = 0;

                OnInitializeModules();
                CallGeneratedInitMethods();

                foreach (var module in _externalModulesView)
                    module.OnInitializeModules();

                _tickables.Clear();
                RegisterEvents();
            }

            if (_visitiblityRules && !_visitiblityRules.isInitialized)
            {
                _visitiblityRules = Instantiate(_visitiblityRules);
                _visitiblityRules.Setup(manager);
            }
        }

        private PlayerID? _pendingOwnershipRequest;

        /// <summary>
        /// Evaluates the visibility of this object.
        /// This will recalculate the observers of this object.
        /// This is server specific.
        /// Re-evaulation includes all children.
        /// </summary>
        [ContextMenu("PurrNet/Evaluate Visibility"), PurrContextButton]
        public void EvaluateVisibility()
        {
            if (!isServer)
                return;

            _serverHierarchy.EvaluateVisibility(transform);
        }

        /// <summary>
        /// Evaluates the visibility of this object for a specific player.
        /// This will recalculate the observers of this object.
        /// This is server specific.
        /// Re-evaulation includes all children.
        /// </summary>
        public void EvaluateVisibility(PlayerID player)
        {
            if (isServer)
            {
                _serverHierarchy.EvaluateVisibility(player, transform);
            }
        }

        /// <summary>
        /// Gives ownership of this object to the player.
        /// </summary>
        /// <param name="player">PlayerID to give ownership to</param>
        /// <param name="silent">Dont log any errors if in silent mode</param>
        /// <param name="propagateToChildren">If true, will give ownership to all children</param>
        public void GiveOwnership(PlayerID player, bool silent = false, bool? propagateToChildren = null)
        {
            if (!networkManager)
                return;
            GiveOwnershipInternal(player, silent, false, propagateToChildren);
        }

        /// <summary>
        /// Spawns the object over the network.
        /// The gameobject must contain a PrefabLink component in order to spawn.
        /// Errors will be logged if something goes wrong.
        /// </summary>
        /// <param name="prefab">Prefab used to spawn the object</param>
        /// <param name="manager">Optional NetworkManager to use, will use NetworkManager.main if not provided</param>
        public void Spawn(GameObject prefab, NetworkManager manager = null)
        {
            if (isSpawned)
                return;

            if (!manager)
            {
                manager = NetworkManager.main;

                if (!manager)
                {
                    PurrLogger.LogError("Failed to spawn object. No NetworkManager found.", this);
                    return;
                }
            }

            if (manager.TryGetModule(manager.isServer, out HierarchyFactory module) &&
                module.TryGetHierarchy(gameObject.scene, out var hierarchy))
            {
                hierarchy.OnGameObjectCreated(gameObject, prefab);
            }
        }

        internal void Spawn(NetworkManager manager = null)
        {
            if (isSpawned)
                return;

            if (!manager)
            {
                manager = NetworkManager.main;

                if (!manager)
                {
                    PurrLogger.LogError("Failed to spawn object. No NetworkManager found.", this);
                    return;
                }
            }

            if (manager.TryGetModule(manager.isServer, out HierarchyFactory module) &&
                module.TryGetHierarchy(gameObject.scene, out var hierarchy))
            {
                hierarchy.InternalSpawn(gameObject);
            }
        }


        /// <summary>
        /// Spawn any child objects of this object.
        /// </summary>
        /// <param name="go">GameObject to spawn</param>
        /// <param name="prefab">Prefab used to spawn the object</param>
        /// <param name="manager">Optional NetworkManager to use, will use NetworkManager.main if not provided</param>
        public static void Spawn(GameObject go, GameObject prefab, NetworkManager manager = null)
        {
            if (!go)
                return;

            if (go.TryGetComponent(out NetworkIdentity identity))
            {
                identity.Spawn(prefab, manager);
                return;
            }

            using var identities = DisposableList<TransformIdentityPair>.Create(16);
            HierarchyPool.GetDirectChildren(go.transform, identities);

            for (var i = 0; i < identities.Count; i++)
            {
                var pair = identities[i];
                pair.identity.Spawn(prefab, manager);
            }
        }

        public static void SpawnInternal(GameObject go, NetworkManager manager = null)
        {
            if (!go)
                return;

            if (go.TryGetComponent(out NetworkIdentity identity))
            {
                identity.Spawn(manager);
                return;
            }

            using var identities = DisposableList<TransformIdentityPair>.Create(16);
            HierarchyPool.GetDirectChildren(go.transform, identities);

            for (var i = 0; i < identities.Count; i++)
            {
                var pair = identities[i];
                pair.identity.Spawn(manager);
            }
        }

        [UsedImplicitly]
        public void GiveOwnership(PlayerID? player, bool silent = false, bool? propagateToChildren = null)
        {
            if (!player.HasValue)
            {
                RemoveOwnership(propagateToChildren);
                return;
            }

            GiveOwnership(player.Value, silent, propagateToChildren);
        }

        internal void GiveOwnershipInternal(PlayerID player, bool silent, bool isSpawner, bool? propagateToChildren = null)
        {
            if (!networkManager)
            {
                if (!silent)
                    PurrLogger.LogError("Trying to give ownership to " + player + " but identity isn't spawned.", this);
                return;
            }

            if (networkManager.TryGetModule(networkManager.isServer, out GlobalOwnershipModule module))
            {
                module.GiveOwnership(this, player, silent: silent, isSpawner: isSpawner, propagateToChildren: propagateToChildren);
            }
            else if (!silent) PurrLogger.LogError("Failed to get ownership module.", this);
        }

        public void RemoveOwnership(bool? propagateToChildren = null)
        {
            if (!networkManager)
                return;

            if (networkManager.TryGetModule(networkManager.isServer, out GlobalOwnershipModule module))
            {
                module.RemoveOwnership(this, propagateToChildren);
            }
            else PurrLogger.LogError("Failed to get ownership module.");
        }

        protected virtual void OnDestroy()
        {
            try
            {
                if (ApplicationContext.isQuitting)
                    return;

                if (parent)
                    parent.RemoveDirectChild(this);

                TriggerDespawnEvent(false);
                TriggerDespawnEvent(true);

                _serverHierarchy?.CleanupDestroyedIdentity(this);
                _clientHierarchy?.CleanupDestroyedIdentity(this);

                _ticker = null;
            }
            finally
            {
                InvalidateSiblingIdentities();
                ReleasePendingObservers();
            }
        }

        private int _spawnedCount;
        private bool _wasEarlySpawned;

        public bool isFullySpawned => _spawnedCount > 0;

        public bool isManualSpawn { get; internal set; }

        /// <summary>
        /// Promotes the NetworkIdentity instance to function as a server entity.
        /// This is used for host-migration, when a client is promoted to host.
        /// Use this to ensure client has everything it needs to function as server.
        /// </summary>
        protected virtual void PromoteToServer() { }

        internal void TriggerPromoteToServer()
        {
            try
            {
                PromoteToServer();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].PromoteToServer();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>
        /// Called only on the spawning peer, at the end of the frame the object was spawned in,
        /// right before batched RPCs flush. Anything queued here departs in the same flush as the
        /// spawn, ordered right behind the spawn packet.
        /// </summary>
        protected virtual void OnSpawnSent() { }

        internal void TriggerOnSpawnSent()
        {
            try
            {
                OnSpawnSent();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnSpawnSent();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>
        /// Called only on peers that created the object from a spawn packet, once the entire
        /// packet has been deserialized and early-spawned. Everything that spawned alongside
        /// this object exists at this point; safe to react to spawn data.
        /// </summary>
        protected virtual void OnSpawnReceived() { }

        internal void TriggerOnSpawnReceived()
        {
            try
            {
                OnSpawnReceived();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnSpawnReceived();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        internal void TriggerSpawnEvent(bool asServer)
        {
            RecacheHasConnectedOwner();

            InternalOnSpawn(asServer);

            try
            {
                OnSpawned(asServer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnSpawn(asServer);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (_spawnedCount == 0)
            {
                while (_onSpawnedQueue is { Count: > 0 })
                    _onSpawnedQueue.Dequeue().Invoke();

                try
                {
                    OnSpawned();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                for (int i = 0; i < _externalModulesView.Count; i++)
                {
                    try
                    {
                        _externalModulesView[i].OnSpawn();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }

            _spawnedCount++;
        }

        internal void TriggerEarlySpawnEvent(bool asServer)
        {
            try
            {
                OnEarlySpawn(asServer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnEarlySpawn(asServer);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (!_wasEarlySpawned)
            {
                try
                {
                    OnEarlySpawn();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                for (int i = 0; i < _externalModulesView.Count; i++)
                {
                    try
                    {
                        _externalModulesView[i].OnEarlySpawn();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }

                _wasEarlySpawned = true;
            }
        }

        internal void TriggerDespawnEvent(bool asServer, bool preserveModules = false)
        {
            if (!IsSpawned(asServer)) return;

            InternalOnDespawn(asServer);

            --_spawnedCount;
            _wasEarlySpawned = false;

            try
            {
                OnDespawned(asServer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnDespawned(asServer);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            if (_spawnedCount == 0)
            {
                try
                {
                    OnDespawned();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                for (int i = 0; i < _externalModulesView.Count; i++)
                {
                    try
                    {
                        _externalModulesView[i].OnDespawned();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }

            if (asServer)
                _isSpawnedServer = false;
            else _isSpawnedClient = false;

            RecacheHasConnectedOwner();

            if (_spawnedCount == 0 && !preserveModules)
            {
                _externalModulesView.Clear();
                _modules.Clear();
            }
        }

        internal void TriggerOnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer, bool isSpawner)
        {
            RecacheHasConnectedOwner();

            try
            {
                OnOwnerChanged(oldOwner, newOwner, asServer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                OnOwnerChanged(oldOwner, newOwner, isSpawner, asServer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnOwnerChanged(oldOwner, newOwner, asServer);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                try
                {
                    _externalModulesView[i].OnOwnerChanged(oldOwner, newOwner, isSpawner, asServer);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            // ownerOnly modules scope visibility by ownership, but the identity observer set doesn't
            // change on transfer, so no add/remove fires. Emulate it so their catch-up logic runs.
            if (asServer && !isSpawner)
            {
                for (int i = 0; i < _externalModulesView.Count; i++)
                {
                    var module = _externalModulesView[i];

                    if (!module.ownerOnly)
                        continue;

                    if (oldOwner.HasValue && IsObserver(oldOwner.Value))
                    {
                        try
                        {
                            module.OnObserverRemoved(oldOwner.Value);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }

                    if (newOwner.HasValue && IsObserver(newOwner.Value))
                    {
                        try
                        {
                            module.OnObserverAdded(newOwner.Value);
                            module.OnObserverAdded(newOwner.Value, false);
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }
            }
        }

        internal void TriggerOnOwnerDisconnected(PlayerID ownerId)
        {
            _cachedHasConnectedOwner = false;

            try
            {
                OnOwnerDisconnected(ownerId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnOwnerDisconnected(ownerId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        internal void TriggerOnOwnerReconnected(PlayerID ownerId, bool asServer)
        {
            _cachedHasConnectedOwner = true;

            try
            {
                OnOwnerReconnected(ownerId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnOwnerReconnected(ownerId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public void TriggerOnPreObserverAdded(PlayerID target, bool isSpawner)
        {
            try
            {
                OnPreObserverAdded(target);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                OnPreObserverAdded(target, isSpawner);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                var module = _externalModulesView[i];

                if (module.ownerOnly && target != owner)
                    continue;

                try
                {
                    module.OnPreObserverAdded(target);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                try
                {
                    module.OnPreObserverAdded(target, isSpawner);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>
        /// Called for every observer add with all the players that were added together, one or
        /// more, before the per-player <see cref="OnObserverAdded(PlayerID)"/> callbacks run for
        /// each of them. Lets a component send one message to all of them instead of one per player.
        /// </summary>
        protected virtual void OnObserversAdded(IReadOnlyList<PlayerID> players, bool isSpawner) { }

        public void TriggerOnObserversAdded(IReadOnlyList<PlayerID> players, bool isSpawner)
        {
            try
            {
                OnObserversAdded(players, isSpawner);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < players.Count; i++)
                TriggerOnObserverAddedCore(players[i], isSpawner);
        }

        public void TriggerOnObserverAdded(PlayerID target, bool isSpawner)
        {
            var single = ListPool<PlayerID>.Instantiate();
            single.Add(target);
            try
            {
                TriggerOnObserversAdded(single, isSpawner);
            }
            finally
            {
                ListPool<PlayerID>.Destroy(single);
            }
        }

        private void TriggerOnObserverAddedCore(PlayerID target, bool isSpawner)
        {
            try
            {
                OnObserverAdded(target);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                OnObserverAdded(target, isSpawner);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                var module = _externalModulesView[i];

                if (module.ownerOnly && target != owner)
                    continue;

                try
                {
                    module.OnObserverAdded(target);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                try
                {
                    module.OnObserverAdded(target, isSpawner);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            try
            {
                onObserverAdded?.Invoke(target);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public void TriggerOnObserverRemoved(PlayerID target)
        {
            if (target == localPlayerForced)
                UnregisterTickEvent(false);

            try
            {
                OnObserverRemoved(target);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            for (int i = 0; i < _externalModulesView.Count; i++)
            {
                try
                {
                    _externalModulesView[i].OnObserverRemoved(target);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            try
            {
                onObserverRemoved?.Invoke(target);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        internal void ClearObservers()
        {
            _observers.Clear();
            ReleasePendingObservers();
        }

        public void SetID(NetworkID networkID)
        {
            _idServer = networkID;
            _idClient = networkID;
        }

        public void SetIsSpawned(bool isSpawned, bool asServer)
        {
            if (asServer)
                _isSpawnedServer = isSpawned;
            else _isSpawnedClient = isSpawned;
        }
    }
}
