using System;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Syncs a NetworkIdentity reference lazily, resolving the target when (or after) its scene is loaded.
    /// Unlike a regular SyncVar&lt;NetworkIdentity&gt;, this does not require the target to be registered
    /// at the moment the ID arrives — useful for cross-scene references where the target's scene may
    /// not yet be loaded on the receiving client.
    /// </summary>
    /// <typeparam name="T">The NetworkIdentity subtype this reference points to.</typeparam>
    [Serializable]
    public class SyncLazyRef<T> : NetworkModule where T : NetworkIdentity
    {
        [SerializeField, PurrReadOnly] private T _value;

        private SyncVar<GlobalNetworkID> _networkID;

        public event Action<T, T> onChanged;

        public T value
        {
            get
            {
                return _value;
            }
            set
            {
                if (value == _value)
                    return;

                if (value && !value.isSpawned)
                {
                    PurrLogger.LogError($"Cannot set `{parent}.{name}` to reference `{value}` because it is not spawned.", parent);
                    return;
                }

                _networkID.value = new GlobalNetworkID(value);

                var old = _value;
                _value = value;

                onChanged?.Invoke(old, _value);
            }
        }

        private HierarchyFactory _hierarchy;

        public override bool ownerOnly => _networkID.ownerOnly;

        public SyncLazyRef(bool ownerAuth = false, bool ownerOnly = false)
        {
            _networkID = new SyncVar<GlobalNetworkID>(default, ownerAuth: ownerAuth, ownerOnly: ownerOnly);
            _networkID.onChanged += OnNetworkIDChanged;
        }

        private void OnNetworkIDChanged(GlobalNetworkID globalId)
        {
            if (_networkID.isControllingSyncVar)
                return;

            if (globalId.id == default)
            {
                var old = _value;
                _value = null;
                onChanged?.Invoke(old, null);
            }
            else if (_hierarchy != null && _hierarchy.TryGetIdentity(globalId.scene, globalId.id, out var identity))
            {
                SetFromBaseIdentity(globalId.scene, identity);
            }
        }

        public override void OnEarlySpawn(bool asServer)
        {
            if (_hierarchy == null && networkManager.TryGetModule<HierarchyFactory>(asServer, out var module))
            {
                _hierarchy = module;
                _hierarchy.onEarlyIdentityAdded += OnIdentityAdded;
                _hierarchy.onIdentityRemoved += OnIdentityRemoved;

                // The ID may have arrived with the spawn data, before the hierarchy was available.
                var pending = _networkID.value;
                if (pending.id != default)
                    OnNetworkIDChanged(pending);
            }
        }

        public override void OnDespawned()
        {
            if (_hierarchy != null)
            {
                _hierarchy.onEarlyIdentityAdded -= OnIdentityAdded;
                _hierarchy.onIdentityRemoved -= OnIdentityRemoved;
                _hierarchy = null;
            }
        }

        private void OnIdentityAdded(NetworkIdentity identity)
        {
            if (_networkID.isControllingSyncVar)
                return;

            var target = _networkID.value;

            if (target.id == default)
                return;

            if (identity.sceneId != target.scene)
                return;

            if (identity.id != target.id)
                return;

            SetFromBaseIdentity(target.scene, identity);
        }

        private void OnIdentityRemoved(NetworkIdentity identity)
        {
            if (!ReferenceEquals(_value, identity))
                return;

            var old = _value;
            _value = null;
            onChanged?.Invoke(old, null);
        }

        private void SetFromBaseIdentity(SceneID scene, NetworkIdentity identity)
        {
            if (identity is T cast)
            {
                var old  = _value;
                _value = cast;
                onChanged?.Invoke(old, _value);
                return;
            }

            if (_value)
            {
                var old  = _value;
                _value = null;
                onChanged?.Invoke(old, _value);
            }

            PurrLogger.LogError(
                $"Failed to resolve `{parent}.{name}` to `{typeof(T).Name}` with NetworkID `{_networkID.value.id}`" +
                $" in scene `{scene}` because the identity found was of type `{identity.GetType().Name}`.",
                parent);
        }
    }
}
