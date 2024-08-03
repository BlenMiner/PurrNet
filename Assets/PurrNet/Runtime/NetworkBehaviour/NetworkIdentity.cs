using System;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [DefaultExecutionOrder(-1000)]
    public partial class NetworkIdentity : MonoBehaviour
    {
        /// <summary>
        /// The prefab id of this object;
        /// Is -1 if scene object.
        /// </summary>
        public int prefabId { get; private set; } = -1;
        
        /// <summary>
        /// Network id of this object.
        /// </summary>
        public NetworkID? id { get; private set; }
        
        /// <summary>
        /// Scene id of this object.
        /// </summary>
        public SceneID sceneId { get; private set; }
        
        /// <summary>
        /// Is spawned over the network.
        /// </summary>
        public bool isSpawned { get; private set; }

        public bool isServer => isSpawned && networkManager.isServer;
        
        public bool isClient => isSpawned && networkManager.isClient;
        
        public bool isHost => isSpawned && networkManager.isHost;
        
        public bool isOwner => isSpawned && owner == localPlayer;
        
        public bool hasOwner => owner.HasValue;

        internal PlayerID? internalOwnerServer;
        internal PlayerID? internalOwnerClient;
        
        /// <summary>
        /// Returns the owner of this object.
        /// It will return the owner of the closest parent object.
        /// If you can, cache this value for performance.
        /// </summary>
        public PlayerID? owner => internalOwnerServer ?? internalOwnerClient;
        
        public NetworkManager networkManager { get; private set; }
        
        public PlayerID localPlayer => isSpawned && networkManager.TryGetModule<PlayersManager>(false, out var module) && module.localPlayerId.HasValue 
            ? module.localPlayerId.Value : default;
        
        internal event Action<NetworkIdentity> onRemoved;
        internal event Action<NetworkIdentity, bool> onEnabledChanged;
        internal event Action<NetworkIdentity, bool> onActivatedChanged;
        
        private bool _lastEnabledState;
        private GameObjectEvents _events;
        private GameObject _gameObject;

        internal PlayerID? GetOwner(bool asServer)
        {
            if (asServer)
                return internalOwnerServer;
            return internalOwnerClient;
        }

        protected virtual void OnSpawned() { }
        
        protected virtual void OnDespawned() { }
        
        protected virtual void OnSpawned(bool asServer) { }
        
        protected virtual void OnDespawned(bool asServer) { }

        protected virtual void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer) { }

        protected virtual void OnOwnerDisconnected(PlayerID owner, bool asServer) { }

        protected virtual void OnOwnerConnected(PlayerID owner, bool asServer) { }

        private bool IsNotOwnerPredicate(PlayerID player)
        {
            return player != owner;
        }
        
        private void OnActivated(bool active)
        {
            if (_ignoreNextActivation)
            {
                _ignoreNextActivation = false;
                return;
            }
            
            onActivatedChanged?.Invoke(this, active);
        }

        public virtual void OnEnable()
        {
            UpdateEnabledState();
        }
        
        public virtual void OnDisable()
        {
            UpdateEnabledState();
        }

        internal void UpdateEnabledState()
        {
            if (_lastEnabledState != enabled)
            {
                if (_ignoreNextEnable)
                     _ignoreNextEnable = false;
                else onEnabledChanged?.Invoke(this, enabled);

                _lastEnabledState = enabled;
            }
        }
        
        internal void SetIdentity(NetworkManager manager, SceneID scene, int pid, NetworkID identityId, bool asServer)
        {
            networkManager = manager;

            Hasher.PrepareType(GetType());

            sceneId = scene;
            prefabId = pid;
            id = identityId;
            isSpawned = true;

            if (asServer)
                 internalOwnerServer = null;
            else internalOwnerClient = null;
            
            _lastEnabledState = enabled;
            _gameObject = gameObject;
            
            if (!_gameObject.TryGetComponent(out _events))
            {
                _events = _gameObject.AddComponent<GameObjectEvents>();
                _events.InternalAwake();
                _events.hideFlags = HideFlags.HideInInspector;
                _events.onActivatedChanged += OnActivated;
                _events.Register(this);
            }
        }

        private bool _ignoreNextDestroy;
        
        public void IgnoreNextDestroyCallback()
        {
            _ignoreNextDestroy = true;
        }
        
        public void GiveOwnership(PlayerID player)
        {
            if (!networkManager)
            {
                PurrLogger.LogError($"Trying to give ownership to '{name}' which is not spawned.", this);
                return;
            }
            
            if (networkManager.TryGetModule(networkManager.isServer, out GlobalOwnershipModule module))
            {
                module.GiveOwnership(this, player);
            }
            else PurrLogger.LogError("Failed to get ownership module.", this);
        }
        
        public void RemoveOwnership()
        {
            if (networkManager.TryGetModule(networkManager.isServer, out GlobalOwnershipModule module))
            {
                module.RemoveOwnership(this);
            }
            else PurrLogger.LogError("Failed to get ownership module.");
        }
        
        protected virtual void OnDestroy()
        {
            if (_events)
                _events.Unregister(this);
            
            if (_ignoreNextDestroy)
            {
                _ignoreNextDestroy = false;
                return;
            }
            
            if (ApplicationContext.isQuitting)
                return;

            onRemoved?.Invoke(this);
        }
        
        private bool _ignoreNextActivation;
        private bool _ignoreNextEnable;

        internal void IgnoreNextActivationCallback()
        {
            _ignoreNextActivation = true;
        }
        
        internal void IgnoreNextEnableCallback()
        {
            _ignoreNextEnable = true;
        }
        
        private int _spawnedCount;
        
        internal void TriggetSpawnEvent(bool asServer)
        {
            OnSpawned(asServer);

            if (_spawnedCount == 0)
                OnSpawned();
            
            _spawnedCount++;
        }

        internal void ClearSpawnedData()
        {
            isSpawned = false;
            internalOwnerServer = null;
            internalOwnerClient = null;
        }

        internal void TriggetDespawnEvent(bool asServer)
        {
            OnDespawned(asServer);

            _spawnedCount--;

            if (_spawnedCount == 0)
                OnDespawned();
        }

        internal void TriggerOnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer) 
        {
            OnOwnerChanged(oldOwner, newOwner, asServer);
        }

        internal void TriggerOnOwnerDisconnected(PlayerID owner, bool asServer)
        {
            OnOwnerDisconnected(owner, asServer);
        }

        internal void TriggerOnOwnerReconnected(PlayerID owner, bool asServer)
        {
            OnOwnerConnected(owner, asServer);
        }
    }
}
