using UnityEngine;
using PurrNet.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using UnityEngine.Scripting;

namespace PurrNet
{
    /// <summary>
    /// The operation which has happened to the list
    /// </summary>
    public enum SyncListOperation
    {
        Added,
        Removed,
        Insert,
        Set,
        Cleared
    }

    /// <summary>
    /// All the data relevant to the change that happened to the list
    /// </summary>
    public readonly struct SyncListChange<T>
    {
        public readonly SyncListOperation operation;
        public readonly T value;
        public readonly T oldValue;
        public readonly int index;

        private SyncListChange(SyncListOperation operation, T value, T oldValue, int index)
        {
            this.operation = operation;
            this.value = value;
            this.oldValue = oldValue;
            this.index = index;
        }

        public static SyncListChange<T> Added(T item, int index)
        {
            return new SyncListChange<T>(SyncListOperation.Added, item, default(T), index);
        }

        public static SyncListChange<T> Removed(T item, T oldValue, int index)
        {
            return new SyncListChange<T>(SyncListOperation.Removed, item, oldValue, index);
        }

        public static SyncListChange<T> Inserted(T item, int index)
        {
            return new SyncListChange<T>(SyncListOperation.Insert, item, default(T), index);
        }

        public static SyncListChange<T> Set(T newValue, T oldValue, int index)
        {
            return new SyncListChange<T>(SyncListOperation.Set, newValue, oldValue, index);
        }

        public static SyncListChange<T> SetDirty(T value, int index)
        {
            return new SyncListChange<T>(SyncListOperation.Set, value, value, index);
        }

        public static SyncListChange<T> Cleared()
        {
            return new SyncListChange<T>(SyncListOperation.Cleared, default(T), default(T), -1);
        }

        public override string ToString()
        {
            string valueStr = $"Value: {value} | OldValue: {oldValue} | Operation: {operation} | Index: {index}";
            return valueStr;
        }
    }

    [Serializable]
    public class SyncList<T> : NetworkModule, IList<T>, ISerializationCallbackReceiver, ITick
    {
        [SerializeField] private bool _ownerAuth;

        [SerializeField] private bool _ownerOnly;
        [SerializeField, Min(0)] private float _sendIntervalInSeconds;
        [SerializeField] private List<T> _list = new List<T>();
        [SerializeField, HideInInspector] private List<T> _initialList = new List<T>();

        public List<T> list => _list;
        public List<T> ToList() => _list;

        public delegate void SyncListChanged<TYPE>(SyncListChange<TYPE> change);

        /// <summary>
        /// Event that is invoked when the list is changed
        /// </summary>
        public event SyncListChanged<T> onChanged;

        /// <summary>
        /// Whether it is the owner or the server that has the authority to modify the list
        /// </summary>
        public bool ownerAuth => _ownerAuth;

        public override bool ownerOnly => _ownerOnly;

        public float sendIntervalInSeconds
        {
            get => _sendIntervalInSeconds;
            set => _sendIntervalInSeconds = value;
        }

        /// <summary>
        /// The amount of entries in the list
        /// </summary>
        public int Count => _list.Count;

        public bool IsReadOnly => false;

        private List<SyncListChange<T>> _pendingChanges = new();
        private float _lastSendTime;
        private bool _wasLastDirty;
        private bool _isDirty;

        public override void OnPoolReset()
        {
            onChanged = null;
            _pendingChanges.Clear();
            RestoreInitialList();
            _lastSendTime = default;
            _wasLastDirty = default;
            _isDirty = default;
        }

        public SyncList(bool ownerAuth = false, bool ownerOnly = false)
        {
            _ownerAuth = ownerAuth;
            _ownerOnly = ownerOnly;
        }

        public SyncList(List<T> defaultValues, bool ownerAuth = false, bool ownerOnly = false)
        {
            _list = defaultValues;
            _ownerAuth = ownerAuth;
            _ownerOnly = ownerOnly;
            CacheInitialList();
        }

        public T this[int idx]
        {
            get => _list[idx];
            set
            {
                if (!ValidateAuthority())
                    return;

                var oldValue = _list[idx];

                if (PurrEquality<T>.Equals(oldValue, value))
                    return;

                _list[idx] = value;

                var change = SyncListChange<T>.Set(value, oldValue, idx);
                QueueChange(change);
                InvokeChange(change);

                if (isSpawned)
                {
                    if (isServer)
                        SendSetToAll(idx, value);
                    else
                        SendSetToServer(idx, value);
                }
            }
        }

        private void QueueChange(SyncListChange<T> change)
        {
            _pendingChanges.Add(change);
            _isDirty = true;
        }

        private void CacheInitialList()
        {
            _initialList.Clear();
            _initialList.AddRange(_list);
        }

        private void RestoreInitialList()
        {
            _list.Clear();
            _list.AddRange(_initialList);
        }

        private bool MatchesInitialList()
        {
            if (_list.Count != _initialList.Count)
                return false;

            for (int i = 0; i < _list.Count; i++)
            {
                if (!PurrEquality<T>.Equals(_list[i], _initialList[i]))
                    return false;
            }

            return true;
        }

        public override void OnSpawnSent()
        {
            if (isServer || !IsController(_ownerAuth))
                return;

            if (!_isDirty && MatchesInitialList())
                return;

            SendInitialStateToServer(_list);
            _pendingChanges.Clear();
            _isDirty = false;
            _wasLastDirty = false;
        }

        public override void OnObserverAdded(PlayerID player, bool isSpawner)
        {
            if (isSpawner && ownerAuth && owner == player)
                return;

            SendInitialToTarget(player, _list);
        }

        [TargetRpc(Channel.ReliableOrdered), Preserve]
        private void SendInitialToTarget(PlayerID player, List<T> items)
        {
            HandleFullState(items);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendInitialStateToAll(List<T> items)
        {
            HandleFullState(items);
        }

        private void HandleFullState(List<T> newList)
        {
            if (isHost || IsController(ownerAuth)) return;

            bool listChanged = false;

            if (_list.Count > newList.Count)
            {
                for (int i = _list.Count - 1; i >= newList.Count; i--)
                {
                    var removed = _list[i];
                    _list.RemoveAt(i);
                    InvokeChange(SyncListChange<T>.Removed(removed, removed, i));
                }

                listChanged = true;
            }

            for (int i = 0; i < newList.Count; i++)
            {
                if (i >= _list.Count)
                {
                    _list.Add(newList[i]);
                    InvokeChange(SyncListChange<T>.Added(newList[i], i));
                    listChanged = true;
                }
                else if (!PurrEquality<T>.Equals(_list[i], newList[i]))
                {
                    var old = _list[i];
                    _list[i] = newList[i];
                    InvokeChange(SyncListChange<T>.Set(newList[i], old, i));
                    listChanged = true;
                }
            }

            if (listChanged && newList.Count == 0)
                InvokeChange(SyncListChange<T>.Cleared());
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendInitialStateToServer(List<T> items)
        {
            if (!_ownerAuth) return;
            SendInitialStateToOthers(items);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendInitialStateToOthers(List<T> items)
        {
            _list.Clear();
            _list.AddRange(items);

            var change = SyncListChange<T>.Cleared();
            InvokeChange(change);

            for (int i = 0; i < items.Count; i++)
            {
                var changeI = SyncListChange<T>.Added(items[i], i);
                InvokeChange(changeI);
            }
        }

        /// <summary>
        /// adds an item to the list and syncs the change
        /// </summary>
        /// <param name="item">The item you want to add</param>
        public void Add(T item)
        {
            if (!ValidateAuthority())
                return;

            _list.Add(item);
            var change = SyncListChange<T>.Added(item, _list.Count - 1);
            QueueChange(change);
            InvokeChange(change);
        }

        /// <summary>
        /// Clears the list and syncs the change
        /// </summary>
        public void Clear()
        {
            if (!ValidateAuthority())
                return;

            _list.Clear();
            var change = SyncListChange<T>.Cleared();
            QueueChange(change);
            InvokeChange(change);
        }

        /// <summary>
        /// Inserts an item at a specific index and syncs the change
        /// </summary>
        /// <param name="index">Index to insert the item to</param>
        /// <param name="item">Item to be inserted at the given index</param>
        public void Insert(int index, T item)
        {
            if (!ValidateAuthority())
                return;

            _list.Insert(index, item);
            var change = SyncListChange<T>.Inserted(item, index);
            QueueChange(change);
            InvokeChange(change);
        }

        /// <summary>
        /// Removes an item from the list and syncs the change
        /// </summary>
        /// <param name="item">Item to be removed</param>
        /// <returns></returns>
        public bool Remove(T item)
        {
            if (!ValidateAuthority())
                return false;

            int idx = _list.IndexOf(item);
            if (idx < 0) return false;

            var oldValue = _list[idx];
            _list.RemoveAt(idx);
            var change = SyncListChange<T>.Removed(item, oldValue, idx);
            QueueChange(change);
            InvokeChange(change);

            return true;
        }

        /// <summary>
        /// Removes an item at a specific index and syncs the change
        /// </summary>
        /// <param name="index">Index of which to remove the entry</param>
        public void RemoveAt(int index)
        {
            if (!ValidateAuthority())
                return;

            var oldValue = _list[index];
            T item = _list[index];
            _list.RemoveAt(index);
            var change = SyncListChange<T>.Removed(item, oldValue, index);
            QueueChange(change);
            InvokeChange(change);
        }

        /// <summary>
        /// Sorts the list using the given comparison and syncs the changes.
        /// </summary>
        /// <param name="comparison">Comparison to use for sorting the list</param>
        public void Sort(Comparison<T> comparison)
        {
            if (!ValidateAuthority())
                return;

            using var sorted = DisposableList<T>.Create(_list);
            sorted.list.Sort(comparison);

            for (int i = 0; i < sorted.Count; i++)
            {
                T newItem = sorted[i];
                T oldItem = _list[i];

                if (!EqualityComparer<T>.Default.Equals(oldItem, newItem))
                {
                    _list[i] = newItem;

                    var change = SyncListChange<T>.Set(newItem, oldItem, i);
                    QueueChange(change);
                    InvokeChange(change);
                }
            }
        }

        public bool Contains(T item) => _list.Contains(item);
        public void CopyTo(T[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public List<T>.Enumerator GetEnumerator() => _list.GetEnumerator();
        public int IndexOf(T item) => _list.IndexOf(item);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

        private bool ValidateAuthority()
        {
            if (!isSpawned) return true;

            bool controller = parent.IsController(_ownerAuth);
            if (!controller)
            {
                PurrLogger.LogError(
                    $"Invalid permissions when modifying `<b>SyncList<{typeof(T).Name}> {name}</b>` on `{parent.name}`." +
                    $"\n{GetPermissionErrorDetails(_ownerAuth, this)}", parent);
                return false;
            }
            return true;
        }

        private void InvokeChange(SyncListChange<T> change)
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

        /// <summary>
        /// Forces the list to be synced again at the given index. Good for when you modify something inside the list
        /// </summary>
        /// <param name="index">Index to set dirty</param>
        public void SetDirty(int index)
        {
            if (!isSpawned) return;

            if (!ValidateAuthority())
                return;

            if (index < 0 || index >= _list.Count)
            {
                PurrLogger.LogError($"Invalid index {index} for SetDirty in SyncList. List count: {_list.Count}",
                    parent);
                return;
            }

            var value = _list[index];
            var change = SyncListChange<T>.SetDirty(value, index);
            QueueChange(change);
            InvokeChange(change);
        }

        public void OnTick(float delta)
        {
            if (!_isDirty && !_wasLastDirty)
                return;

            if (!IsController(_ownerAuth))
                return;

            float timeSinceLastSend = Time.time - _lastSendTime;

            if (timeSinceLastSend < _sendIntervalInSeconds)
                return;

            if (_isDirty)
            {
                foreach (var change in _pendingChanges)
                {
                    switch (change.operation)
                    {
                        case SyncListOperation.Added:
                            if (isServer) SendAddToAll(change.value);
                            else SendAddToServer(change.value);
                            break;
                        case SyncListOperation.Removed:
                            if (isServer) SendRemoveToAll(change.value);
                            else SendRemoveToServer(change.value);
                            break;
                        case SyncListOperation.Insert:
                            if (isServer) SendInsertToAll(change.index, change.value);
                            else SendInsertToServer(change.index, change.value);
                            break;
                        case SyncListOperation.Set:
                            if (isServer) SendSetToAll(change.index, change.value);
                            else SendSetToServer(change.index, change.value);
                            break;
                        case SyncListOperation.Cleared:
                            if (isServer) SendClearToAll();
                            else SendClearToServer();
                            break;
                    }
                }

                _pendingChanges.Clear();
                _lastSendTime = Time.time;
                _wasLastDirty = true;
                _isDirty = false;
            }
            else if (_wasLastDirty)
            {
                if(isServer)
                    SendInitialStateToAll(_list);
                else
                    ForceSendReliable();
                _wasLastDirty = false;
            }
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
            _list.Add(item);
            var change = SyncListChange<T>.Added(item, _list.Count - 1);
            InvokeChange(change);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendAddToAll(T item)
        {
            if (!isHost)
            {
                _list.Add(item);
                var change = SyncListChange<T>.Added(item, _list.Count - 1);
                InvokeChange(change);
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
            int idx = _list.IndexOf(item);
            if (idx >= 0)
            {
                var oldValue = _list[idx];
                _list.RemoveAt(idx);
                var change = SyncListChange<T>.Removed(item, oldValue, idx);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendRemoveToAll(T item)
        {
            if (!isHost)
            {
                int idx = _list.IndexOf(item);
                if (idx >= 0)
                {
                    var oldValue = _list[idx];
                    _list.RemoveAt(idx);
                    var change = SyncListChange<T>.Removed(item, oldValue, idx);
                    InvokeChange(change);
                }
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendRemoveAtToServer(int index)
        {
            if (!_ownerAuth) return;
            SendRemoveAtToOthers(index);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendRemoveAtToOthers(int index)
        {
            if (index < _list.Count)
            {
                var oldValue = _list[index];
                T item = _list[index];
                _list.RemoveAt(index);
                var change = SyncListChange<T>.Removed(item, oldValue, index);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendRemoveAtToAll(int index)
        {
            if (!isHost && index < _list.Count)
            {
                var oldValue = _list[index];
                T item = _list[index];
                _list.RemoveAt(index);
                var change = SyncListChange<T>.Removed(item, oldValue, index);
                InvokeChange(change);
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
            _list.Clear();
            var change = SyncListChange<T>.Cleared();
            InvokeChange(change);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendClearToAll()
        {
            if (!isHost)
            {
                _list.Clear();
                var change = SyncListChange<T>.Cleared();
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendSetToServer(int index, T item)
        {
            if (!_ownerAuth) return;
            SendSetToOthers(index, item);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendSetToOthers(int index, T item)
        {
            if (index < _list.Count)
            {
                var oldValue = _list[index];
                _list[index] = item;
                var change = SyncListChange<T>.Set(item, oldValue, index);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendSetToAll(int index, T item)
        {
            if (!isHost && index < _list.Count)
            {
                var oldValue = _list[index];
                _list[index] = item;
                var change = SyncListChange<T>.Set(item, oldValue, index);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendInsertToServer(int index, T item)
        {
            if (!_ownerAuth) return;
            SendInsertToOthers(index, item);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendInsertToOthers(int index, T item)
        {
            if (index <= _list.Count)
            {
                _list.Insert(index, item);
                var change = SyncListChange<T>.Inserted(item, index);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendInsertToAll(int index, T item)
        {
            if (!isHost && index <= _list.Count)
            {
                _list.Insert(index, item);
                var change = SyncListChange<T>.Inserted(item, index);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendSetDirtyToServer(int index, T value)
        {
            if (!_ownerAuth) return;
            SendSetDirtyToOthers(index, value);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendSetDirtyToOthers(int index, T value)
        {
            if (index >= 0 && index < _list.Count)
            {
                _list[index] = value;
                var change = SyncListChange<T>.SetDirty(value, index);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendSetDirtyToAll(int index, T value)
        {
            if (!isHost)
            {
                if (index >= 0 && index < _list.Count)
                {
                    _list[index] = value;
                    var change = SyncListChange<T>.SetDirty(value, index);
                    InvokeChange(change);
                }
            }
        }

        [ServerRpc(Channel.ReliableOrdered)]
        private void ForceSendReliable()
        {
            SendInitialStateToAll(_list);
        }

        #endregion

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            _list ??= new List<T>();
            _initialList ??= new List<T>();

            CacheInitialList();
        }
    }
}
