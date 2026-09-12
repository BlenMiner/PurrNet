using UnityEngine;
using PurrNet.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet.Transports;

namespace PurrNet
{
    /// <summary>
    /// The operation which has happened to the hash set
    /// </summary>
    public enum SyncHashSetOperation
    {
        Added,
        Removed,
        Cleared
    }

    /// <summary>
    /// All the data relevant to the change that happened to the hash set
    /// </summary>
    public struct SyncHashSetChange<T>
    {
        public SyncHashSetOperation operation;
        public T value;

        public SyncHashSetChange(SyncHashSetOperation operation, T value = default)
        {
            this.operation = operation;
            this.value = value;
        }

        public override string ToString()
        {
            string valueStr = $"Value: {value} | Operation: {operation}";
            return valueStr;
        }
    }

    [Serializable]
    public class SyncHashSet<T> : NetworkModule, ISet<T>
    {
        [SerializeField] private bool _ownerAuth;

        [SerializeField] private bool _ownerOnly;
        [SerializeField] private List<T> _serializedSet = new List<T>();
        private HashSet<T> _set = new HashSet<T>();

        public delegate void SyncHashSetChanged<Key>(SyncHashSetChange<Key> change);

        /// <summary>
        /// Event that is invoked when the hash set is changed
        /// </summary>
        public event SyncHashSetChanged<T> onChanged;

        /// <summary>
        /// Whether it is the owner or the server that has the authority to modify the hash set
        /// </summary>
        public bool ownerAuth => _ownerAuth;

        public override bool ownerOnly => _ownerOnly;

        /// <summary>
        /// The amount of entries in the hash set
        /// </summary>
        public int Count => _set.Count;

        public bool IsReadOnly => false;

        public SyncHashSet(bool ownerAuth = false, bool ownerOnly = false)
        {
            _ownerAuth = ownerAuth;
            _ownerOnly = ownerOnly;

#if UNITY_EDITOR
            onChanged += UpdateSerializedSet;
#endif
        }

        protected virtual void OnValidate()
        {
            _set = new HashSet<T>(_serializedSet);
        }

        protected virtual void OnBeforeSerialize()
        {
            _serializedSet.Clear();
            _serializedSet.AddRange(_set);
        }

        public override void OnSpawn()
        {
            if (!IsController(_ownerAuth)) return;

            if (isServer)
                SendInitialStateToAll(_set);
            else SendInitialStateToServer(_set);
        }

        public override void OnObserverAdded(PlayerID player)
        {
            HandleInitialStateTarget(player, _set);
        }

        [TargetRpc(Channel.ReliableOrdered)]
        private void HandleInitialStateTarget(PlayerID player, HashSet<T> initialState)
        {
            HandleInitialState(initialState);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendInitialStateToAll(HashSet<T> initialState)
        {
            HandleInitialState(initialState);
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendInitialStateToServer(HashSet<T> initialState)
        {
            if (!_ownerAuth) return;
            SendInitialStateToOthers(initialState);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendInitialStateToOthers(HashSet<T> initialState)
        {
            _set = initialState;

            InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Cleared));

            foreach (var value in _set)
            {
                InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Added, value));
            }
        }

        private void HandleInitialState(HashSet<T> initialState)
        {
            if (!isHost)
            {
                if (initialState == null)
                    return;

                _set = initialState;

                InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Cleared));

                foreach (var value in _set)
                {
                    InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Added, value));
                }
            }
        }

#if UNITY_EDITOR
        private void UpdateSerializedSet(SyncHashSetChange<T> _)
        {
            if (!UnityEditor.EditorApplication.isPlaying) return;
            _serializedSet.Clear();
            _serializedSet.AddRange(_set);
            UnityEditor.EditorUtility.SetDirty(parent);
        }
#endif

        /// <summary>
        /// Adds an item to the hash set and syncs the change
        /// </summary>
        public bool Add(T item)
        {
            if (!ValidateAuthority())
                return false;

            if (!_set.Add(item))
                return false;

            var change = new SyncHashSetChange<T>(SyncHashSetOperation.Added, item);
            InvokeChange(change);

            if (isSpawned)
            {
                if (isServer)
                    SendAddToAll(item);
                else
                    SendAddToServer(item);
            }

            return true;
        }

        void ICollection<T>.Add(T item)
        {
            Add(item!);
        }

        /// <summary>
        /// Removes an item from the hash set and syncs the change
        /// </summary>
        public bool Remove(T item)
        {
            if (!ValidateAuthority())
                return false;

            if (!_set.Remove(item))
                return false;

            var change = new SyncHashSetChange<T>(SyncHashSetOperation.Removed, item);
            InvokeChange(change);

            if (isSpawned)
            {
                if (isServer)
                    SendRemoveToAll(item);
                else
                    SendRemoveToServer(item);
            }

            return true;
        }

        /// <summary>
        /// Clears the hash set and syncs the change
        /// </summary>
        public void Clear()
        {
            if (!ValidateAuthority())
                return;

            _set.Clear();
            var change = new SyncHashSetChange<T>(SyncHashSetOperation.Cleared);
            InvokeChange(change);

            if (isSpawned)
            {
                if (isServer)
                    SendClearToAll();
                else
                    SendClearToServer();
            }
        }

        public bool Contains(T item) => _set.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _set.CopyTo(array, arrayIndex);

        public void ExceptWith(IEnumerable<T> other) =>
            throw new NotSupportedException("Use individual Add/Remove operations instead");

        public void IntersectWith(IEnumerable<T> other) =>
            throw new NotSupportedException("Use individual Add/Remove operations instead");

        public bool IsProperSubsetOf(IEnumerable<T> other) => _set.IsProperSubsetOf(other);
        public bool IsProperSupersetOf(IEnumerable<T> other) => _set.IsProperSupersetOf(other);
        public bool IsSubsetOf(IEnumerable<T> other) => _set.IsSubsetOf(other);
        public bool IsSupersetOf(IEnumerable<T> other) => _set.IsSupersetOf(other);
        public bool Overlaps(IEnumerable<T> other) => _set.Overlaps(other);
        public bool SetEquals(IEnumerable<T> other) => _set.SetEquals(other);

        public void SymmetricExceptWith(IEnumerable<T> other) =>
            throw new NotSupportedException("Use individual Add/Remove operations instead");

        public void UnionWith(IEnumerable<T> other) =>
            throw new NotSupportedException("Use individual Add/Remove operations instead");

        public IEnumerator<T> GetEnumerator() => _set.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private bool ValidateAuthority()
        {
            if (!isSpawned) return true;

            bool controlling = parent.IsController(_ownerAuth);
            if (!controlling)
            {
                PurrLogger.LogError(
                    $"Invalid permissions when modifying `<b>SyncHashSet<{typeof(T).Name}> {name}</b>` on `{parent.name}`." +
                    $"\n{GetPermissionErrorDetails(_ownerAuth, this)}", parent);
                return false;
            }

            return true;
        }

        private void InvokeChange(SyncHashSetChange<T> change)
        {
            try
            {
                onChanged?.Invoke(change);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public override void OnPoolReset()
        {
            onChanged = null;
#if UNITY_EDITOR
            onChanged += UpdateSerializedSet;
#endif
        }

        #region RPCs

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendAddToServer(T item)
        {
            if (!_ownerAuth) return;
            SendAddToOthers(item);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendAddToOthers(T item)
        {
            if (_set.Add(item))
            {
                InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Added, item));
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendAddToAll(T item)
        {
            if (!isHost)
            {
                if (_set.Add(item))
                {
                    InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Added, item));
                }
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendRemoveToServer(T item)
        {
            if (!_ownerAuth) return;
            SendRemoveToOthers(item);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendRemoveToOthers(T item)
        {
            if (_set.Remove(item))
            {
                InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Removed, item));
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendRemoveToAll(T item)
        {
            if (!isHost)
            {
                if (_set.Remove(item))
                {
                    InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Removed, item));
                }
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendClearToServer()
        {
            if (!_ownerAuth) return;
            SendClearToOthers();
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendClearToOthers()
        {
            _set.Clear();
            InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Cleared));
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendClearToAll()
        {
            if (!isHost)
            {
                _set.Clear();
                InvokeChange(new SyncHashSetChange<T>(SyncHashSetOperation.Cleared));
            }
        }

        #endregion
    }
}
