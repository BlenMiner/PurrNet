using UnityEngine;
using PurrNet.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Transports;

namespace PurrNet
{
    /// <summary>
    /// The operation which has happened to the dictionary
    /// </summary>
    public enum SyncDictionaryOperation
    {
        Added,
        Removed,
        Set,
        Cleared
    }

    /// <summary>
    /// All the data relevant to the change that happened to the dictionary
    /// </summary>
    public struct SyncDictionaryChange<TKey, TValue>
    {
        public SyncDictionaryOperation operation;
        public TKey key;
        public TValue value;

        public SyncDictionaryChange(SyncDictionaryOperation operation, TKey key = default, TValue value = default)
        {
            this.operation = operation;
            this.key = key;
            this.value = value;
        }

        public override string ToString()
        {
            string valueStr = $"Key: {key} | Value: {value} | Operation: {operation}";
            return valueStr;
        }
    }

    [Serializable]
    public class SyncDictionary<TKey, TValue> : NetworkModule, IDictionary<TKey, TValue>, ISerializationCallbackReceiver, ITick
    {
        [SerializeField] private bool _ownerAuth;

        [SerializeField] private bool _ownerOnly;

        [SerializeField]
        private SerializableDictionary<TKey, TValue> _serializedDict = new SerializableDictionary<TKey, TValue>();
        [SerializeField, Min(0)] private float _sendIntervalInSeconds;
        [SerializeField, Tooltip("This will send the entire state when things change. It's reliable, but more data heavy")] //We should optimize this in the future
        private bool _useForceSend;
        [SerializeField, HideInInspector]
        private SerializableDictionary<TKey, TValue> _initialSerializedDict = new SerializableDictionary<TKey, TValue>();


        private Dictionary<TKey, TValue> _dict = new Dictionary<TKey, TValue>();

        public delegate void SyncDictionaryChanged<Key, Value>(SyncDictionaryChange<Key, Value> change);

        /// <summary>
        /// Event that is invoked when the dictionary is changed
        /// </summary>
        public event SyncDictionaryChanged<TKey, TValue> onChanged;

        /// <summary>
        /// Whether it is the owner or the server that has the authority to modify the dictionary
        /// </summary>
        public bool ownerAuth => _ownerAuth;

        public override bool ownerOnly => _ownerOnly;

        public float sendIntervalInSeconds
        {
            get => _sendIntervalInSeconds;
            set => _sendIntervalInSeconds = value;
        }
        /// <summary>
        /// The amount of entries in the dictionary
        /// </summary>
        public int Count => _dict.Count;

        public bool IsReadOnly => false;
        public ICollection<TKey> Keys => _dict.Keys;
        public ICollection<TValue> Values => _dict.Values;

        private List<SyncDictionaryChange<TKey, TValue>> _pendingChanges = new();
        private float _lastSendTime;
        private bool _isDirty;
        private bool _wasLastDirty;

        public override void OnPoolReset()
        {
            onChanged = null;
            _pendingChanges.Clear();
            RestoreInitialDict();
            _lastSendTime = default;
            _isDirty = default;
            _wasLastDirty = default;

#if UNITY_EDITOR
            onChanged += UpdateSerializedDict;
#endif
        }

        /// <summary>
        /// Creates a new Sync Dictionary
        /// </summary>
        /// <param name="ownerAuth">Whether the dictionary is owner authed or server auth</param>
        /// <param name="useForceSend">This will send the full state after state syncing. This will be more data heavy, but more consistent</param>
        public SyncDictionary(bool ownerAuth = false, bool useForceSend = false, bool ownerOnly = false)
        {
            _ownerAuth = ownerAuth;
            _ownerOnly = ownerOnly;
            _useForceSend = useForceSend;

#if UNITY_EDITOR
            onChanged += UpdateSerializedDict;
#endif
        }

        public TValue this[TKey key]
        {
            get => _dict[key];
            set
            {
                if (!ValidateAuthority())
                    return;

                bool isNewKey = !_dict.ContainsKey(key);
                _dict[key] = value;

                var operation = isNewKey ? SyncDictionaryOperation.Added : SyncDictionaryOperation.Set;
                var change = new SyncDictionaryChange<TKey, TValue>(operation, key, value);
                QueueChange(change);
                InvokeChange(change);
            }
        }

        public void OnBeforeSerialize()
        {
            _serializedDict?.FromDictionary(_dict);
        }

        public void OnAfterDeserialize()
        {
            _dict ??= new Dictionary<TKey, TValue>();
            _serializedDict ??= new SerializableDictionary<TKey, TValue>();
            _initialSerializedDict ??= new SerializableDictionary<TKey, TValue>();

            _serializedDict.CopyTo(_dict);
            CacheInitialDict();
        }

        private void CacheInitialDict()
        {
            _initialSerializedDict.FromDictionary(_dict);
        }

        private void RestoreInitialDict()
        {
            _initialSerializedDict.CopyTo(_dict);
        }
        
        public override void OnSpawnSent()
        {
            if (isServer || !IsController(_ownerAuth))
                return;

            if (!_isDirty && _initialSerializedDict.Matches(_dict))
                return;

            SendInitialStateToServer(_dict);
            _pendingChanges.Clear();
            _isDirty = false;
            _wasLastDirty = false;
        }

        public override void OnObserverAdded(PlayerID player, bool isSpawner)
        {
            if (isSpawner && ownerAuth && owner == player)
                return;

            HandleInitialStateTarget(player, _dict);
        }

        [TargetRpc(Channel.ReliableOrdered)]
        private void HandleInitialStateTarget(PlayerID player, Dictionary<TKey, TValue> initialState)
        {
            HandleInitialState(initialState);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendInitialStateToAll(Dictionary<TKey, TValue> initialState)
        {
            HandleInitialState(initialState);
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendInitialStateToServer(Dictionary<TKey, TValue> initialState)
        {
            if (!_ownerAuth) return;
            SendInitialStateToOthers(initialState);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendInitialStateToOthers(Dictionary<TKey, TValue> initialState)
        {
            _dict = initialState;

            var initialChang = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Cleared);
            InvokeChange(initialChang);

            foreach (var kvp in _dict)
            {
                var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Added, kvp.Key, kvp.Value);
                InvokeChange(change);
            }
        }

        private void HandleInitialState(Dictionary<TKey, TValue> initialState)
        {
            if (!isHost)
            {
                if (initialState == null)
                    return;

                _dict = initialState;

                var initialChange = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Cleared);
                InvokeChange(initialChange);

                foreach (var kvp in _dict)
                {
                    var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Added, kvp.Key, kvp.Value);
                    InvokeChange(change);
                }
            }
        }

#if UNITY_EDITOR
        private void UpdateSerializedDict(SyncDictionaryChange<TKey, TValue> _)
        {
            if (!UnityEditor.EditorApplication.isPlaying) return;
            if (_dict == null) return;
            _serializedDict.FromDictionary(_dict);
            if (!parent) return;
            UnityEditor.EditorUtility.SetDirty(parent);
        }
#endif

        /// <summary>
        /// Adds an entry to the dictionary and syncs the change
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            if (!ValidateAuthority())
                return;

            _dict.Add(key, value);
            var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Added, key, value);
            QueueChange(change);
            InvokeChange(change);
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        /// <summary>
        /// Removes an entry from the dictionary and syncs the change
        /// </summary>
        public bool Remove(TKey key)
        {
            if (!ValidateAuthority())
                return false;

            if (!_dict.Remove(key, out var value))
                return false;

            var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Removed, key, value);
            QueueChange(change);
            InvokeChange(change);

            return true;
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (!Contains(item))
                return false;

            return Remove(item.Key);
        }

        /// <summary>
        /// Clears the dictionary and syncs the change
        /// </summary>
        public void Clear()
        {
            if (!ValidateAuthority())
                return;

            _dict.Clear();
            var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Cleared);
            QueueChange(change);
            InvokeChange(change);
        }

        /// <summary>
        /// Creates a new Dictionary from the SyncDictionary
        /// </summary>
        /// <returns>A new Dictionary containing all key-value pairs from this SyncDictionary</returns>
        public Dictionary<TKey, TValue> ToDictionary()
        {
            return new Dictionary<TKey, TValue>(_dict);
        }

        public bool ContainsKey(TKey key) => _dict.ContainsKey(key);
        public bool TryGetValue(TKey key, out TValue value) => _dict.TryGetValue(key, out value);
        public bool Contains(KeyValuePair<TKey, TValue> item) => (_dict as IDictionary<TKey, TValue>).Contains(item);

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
            (_dict as IDictionary<TKey, TValue>).CopyTo(array, arrayIndex);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dict.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private bool ValidateAuthority()
        {
            if (!isSpawned)
                return true;

            bool controlling = parent.IsController(_ownerAuth);
            if (!controlling)
            {
                PurrLogger.LogError(
                    $"Invalid permissions when modifying `<b>SyncDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}> {name}</b>` on `{parent.name}`." +
                    $"\n{GetPermissionErrorDetails(_ownerAuth, this)}", parent);
                return false;
            }
            return true;
        }

        private void InvokeChange(SyncDictionaryChange<TKey, TValue> change)
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

        private void QueueChange(SyncDictionaryChange<TKey, TValue> change)
        {
            _pendingChanges.Add(change);
            _isDirty = true;
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
                Flush();
            }
            else if (_wasLastDirty)
            {
                if (_useForceSend)
                {
                    if(isServer)
                        SendInitialStateToAll(_dict);
                    else
                        ForceSendReliable();
                }
                _wasLastDirty = false;
            }
        }

        private void Flush()
        {
            for (var i = 0; i < _pendingChanges.Count; i++)
            {
                var change = _pendingChanges[i];
                switch (change.operation)
                {
                    case SyncDictionaryOperation.Added:
                    case SyncDictionaryOperation.Set:
                        if (isServer)
                            SendSetToAll(change.key, change.value, change.operation == SyncDictionaryOperation.Added);
                        else
                            SendSetToServer(change.key, change.value, change.operation == SyncDictionaryOperation.Added);
                        break;

                    case SyncDictionaryOperation.Removed:
                        if (isServer)
                            SendRemoveToAll(change.key);
                        else
                            SendRemoveToServer(change.key);
                        break;

                    case SyncDictionaryOperation.Cleared:
                        if (isServer)
                            SendClearToAll();
                        else
                            SendClearToServer();
                        break;
                }
            }

            _pendingChanges.Clear();
            _lastSendTime = Time.time;
            _wasLastDirty = true;
            _isDirty = false;
        }

        #region RPCs

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendAddToServer(TKey key, TValue value)
        {
            if (!_ownerAuth) return;
            SendAddToOthers(key, value);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendAddToOthers(TKey key, TValue value)
        {
            _dict[key] = value;
            var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Added, key, value);
            InvokeChange(change);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendAddToAll(TKey key, TValue value)
        {
            if (!isHost)
            {
                _dict[key] = value;
                var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Added, key, value);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendRemoveToServer(TKey key)
        {
            if (!_ownerAuth) return;
            SendRemoveToOthers(key);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendRemoveToOthers(TKey key)
        {
            if (_dict.TryGetValue(key, out TValue value))
            {
                _dict.Remove(key);
                var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Removed, key, value);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendRemoveToAll(TKey key)
        {
            if (!isHost)
            {
                if (_dict.Remove(key, out TValue value))
                {
                    var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Removed, key, value);
                    InvokeChange(change);
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
            _dict.Clear();
            var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Cleared);
            InvokeChange(change);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendClearToAll()
        {
            if (!isHost)
            {
                _dict.Clear();
                var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Cleared);
                InvokeChange(change);
            }
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendSetToServer(TKey key, TValue value, bool isNewKey)
        {
            if (!_ownerAuth) return;
            SendSetToOthers(key, value, isNewKey);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendSetToOthers(TKey key, TValue value, bool isNewKey)
        {
            _dict[key] = value;
            var operation = isNewKey ? SyncDictionaryOperation.Added : SyncDictionaryOperation.Set;
            var change = new SyncDictionaryChange<TKey, TValue>(operation, key, value);
            InvokeChange(change);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendSetToAll(TKey key, TValue value, bool isNewKey)
        {
            if (!isHost)
            {
                _dict[key] = value;
                var operation = isNewKey ? SyncDictionaryOperation.Added : SyncDictionaryOperation.Set;
                var change = new SyncDictionaryChange<TKey, TValue>(operation, key, value);
                InvokeChange(change);
            }
        }

        /// <summary>
        /// Forces the dictionary to be synced again at the given key. Good for when you modify something inside a value
        /// </summary>
        /// <param name="key">Key of the value to set dirty</param>
        public void SetDirty(TKey key)
        {
            if (!isSpawned) return;

            if (!ValidateAuthority())
                return;

            if (!_dict.TryGetValue(key, out var value))
            {
                PurrLogger.LogError($"Key {key} not found in SyncDictionary when trying to SetDirty", parent);
                return;
            }

            var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Set, key, value);
            QueueChange(change);
            InvokeChange(change);

            if (isServer)
                SendSetDirtyToAll(key, value);
            else
                SendSetDirtyToServer(key, value);
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendSetDirtyToServer(TKey key, TValue value)
        {
            if (!_ownerAuth) return;
            SendSetDirtyToOthers(key, value);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true, runLocally: true)]
        private void SendSetDirtyToOthers(TKey key, TValue value)
        {
            if (_dict.ContainsKey(key))
            {
                _dict[key] = value;
                var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Set, key, value);
                InvokeChange(change);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendSetDirtyToAll(TKey key, TValue value)
        {
            if (!isHost)
            {
                if (_dict.ContainsKey(key))
                {
                    _dict[key] = value;
                    var change = new SyncDictionaryChange<TKey, TValue>(SyncDictionaryOperation.Set, key, value);
                    InvokeChange(change);
                }
            }
        }

        [ServerRpc(Channel.ReliableOrdered)]
        private void ForceSendReliable()
        {
            if(_useForceSend)
                SendInitialStateToAll(_dict);
        }


        #endregion
    }

    [Serializable]
    public class SerializableDictionary<TKey, TValue>
    {
        [SerializeField] private List<TKey> keys = new List<TKey>();
        [SerializeField] private List<TValue> values = new List<TValue>();

        [SerializeField] private List<string> stringKeys = new List<string>();
        [SerializeField] private List<string> stringValues = new List<string>();

        private static readonly bool isKeySerializable =
            typeof(TKey).IsSerializable || typeof(UnityEngine.Object).IsAssignableFrom(typeof(TKey));

        private static readonly bool isValueSerializable =
            typeof(TValue).IsSerializable || typeof(UnityEngine.Object).IsAssignableFrom(typeof(TValue));

        private void EnsureLists()
        {
            keys ??= new List<TKey>();
            values ??= new List<TValue>();
            stringKeys ??= new List<string>();
            stringValues ??= new List<string>();
        }

        public Dictionary<TKey, TValue> ToDictionary()
        {
            var dict = new Dictionary<TKey, TValue>(keys.Count);
            CopyTo(dict);
            return dict;
        }

        public bool Matches(Dictionary<TKey, TValue> dict)
        {
            if (!isKeySerializable || !isValueSerializable)
                return false;

            EnsureLists();

            int count = Mathf.Min(keys.Count, values.Count);

            if (dict.Count != count)
                return false;

            for (int i = 0; i < count; i++)
            {
                if (keys[i] == null)
                    return false;

                if (!dict.TryGetValue(keys[i], out var value))
                    return false;

                if (!PurrEquality<TValue>.Equals(value, values[i]))
                    return false;
            }

            return true;
        }

        public void CopyTo(Dictionary<TKey, TValue> dict)
        {
            if (dict == null)
                return;

            EnsureLists();
            dict.Clear();

            if (isKeySerializable && isValueSerializable)
            {
                int count = Mathf.Min(keys.Count, values.Count);
                for (int i = 0; i < count; i++)
                {
                    if (keys[i] != null && !dict.ContainsKey(keys[i]))
                        dict.Add(keys[i], values[i]);
                }
            }
            else
            {
                var count = Mathf.Min(stringKeys.Count, stringValues.Count);
                for (int i = 0; i < count; i++)
                {
                    if (stringKeys[i] != null && !dict.ContainsKey(default(TKey)!))
                        dict.Add(default(TKey)!, default(TValue));
                }
            }
        }

        public void FromDictionary(Dictionary<TKey, TValue> dict)
        {
            if (dict == null)
                return;

            EnsureLists();
            keys.Clear();
            values.Clear();
            stringKeys.Clear();
            stringValues.Clear();

            foreach (var kvp in dict)
            {
                if (isKeySerializable && isValueSerializable)
                {
                    keys.Add(kvp.Key);
                    values.Add(kvp.Value);
                }
                else
                {
                    stringKeys.Add(kvp.Key?.ToString() ?? "null");
                    stringValues.Add(kvp.Value?.ToString() ?? "null");
                }
            }
        }

        public bool IsSerializable => isKeySerializable && isValueSerializable;
        public int Count => isKeySerializable ? keys.Count : stringKeys.Count;

        public string GetDisplayKey(int index) =>
            isKeySerializable ? (keys[index]?.ToString() ?? "null") : (stringKeys[index] ?? "null");

        public string GetDisplayValue(int index) => isValueSerializable
            ? (values[index]?.ToString() ?? "null")
            : (stringValues[index] ?? "null");
    }
}
