using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Will automatically sync a value from client to server, so the server can utilize it. Automatically filters away multiple entries of the same value.
    /// </summary>
    /// <typeparam name="T">Type to sync to the server</typeparam>
    [Serializable]
    public class SyncInput<T> : NetworkModule, ITick, ISerializationCallbackReceiver where T : unmanaged, IEquatable<T>
    {
        public SyncInput(T defaultValue = default, float hostPing = 0f)
        {
            _value = defaultValue;
            _initialValue = defaultValue;
            _simulatedHostPing = hostPing;
        }

        [SerializeField, PurrLock] private T _value;

        [SerializeField, HideInInspector] private T _initialValue;

        [Tooltip("Simulated ping in ms. This is only for the host to simulate a ping delay.")]
        [SerializeField, PurrLock] private float _simulatedHostPing;

        private T _lastValue;
        private int _currentId;
        private int _lastAckId;
        private bool _isDirty;
        private Dictionary<int, T> _history = new();
        private T _queuedHostValue, _lastQueuedHostValue;

        private readonly Queue<PendingInput> _pendingHostInputs = new();

        public delegate void OnChangedDelegate(T newInput);

        /// <summary>
        /// Called back every time the server receives a value change
        /// </summary>
        public event OnChangedDelegate onChanged;

        /// <summary>
        /// Called back every time the value has been marked as dirty and sent to the server.
        /// </summary>
        public event Action onSentData;

        /// <summary>
        /// The current value of the SyncInput.
        /// </summary>
        public T value
        {
            get => _value;
            set
            {
                if (!IsController(true)) {
                    if(parent)
                        PurrLogger.LogError($"Only the controller can set the value of SyncInput. | {parent.gameObject.name}", parent);
                    else if(isSpawned)
                        PurrLogger.LogError($"Only the controller can set the value of SyncInput.");
                    return;
                }
                if (_value.Equals(value)) return;

                if (isHost)
                    _queuedHostValue = value;
                else
                    _value = value;

                SetDirty();
            }
        }

        /// <summary>
        /// Ping in ms
        /// </summary>
        public float simulatedHostPing
        {
            get => _simulatedHostPing;
            set
            {
                if (!isServer)
                {
                    PurrLogger.LogWarning($"Only the server can set the simulated host ping. | IsSpawned: {isSpawned} | IsServer: {isServer}", parent);
                    return;
                }

                if (value < 0)
                    value = 0;
                _simulatedHostPing = value;
            }
        }

        public override void OnPoolReset()
        {
            onChanged = null;
            onSentData = null;

            _value = _initialValue;
            _lastValue = default;
            _currentId = 0;
            _lastAckId = 0;
            _isDirty = false;
            _history.Clear();
            _queuedHostValue = _initialValue;
            _lastQueuedHostValue = _initialValue;
            _pendingHostInputs.Clear();
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            _initialValue = _value;
        }

        public void SetDirty()
        {
            if (!_history.ContainsKey(_currentId + 1))
                _history[_currentId + 1] = _value;

            _isDirty = true;
        }

        public void OnTick(float delta)
        {
            if (!IsController(true))
                return;

            if (!isHost && _lastAckId < _currentId)
            {
                int resendId = _currentId;
                while (!_history.ContainsKey(resendId) && resendId > _lastAckId)
                    resendId--;

                if (_history.TryGetValue(resendId, out var val))
                    SendInput(val, resendId);
            }

            if (_isDirty)
            {
                _isDirty = false;
                _currentId++;
                var current = isHost ? _queuedHostValue : _value;
                _history[_currentId] = current;
                onSentData?.Invoke();

                if (isHost)
                    QueueOrApplyHostInput(current);
                else
                    SendInput(current, _currentId);
            }

            if (isHost)
                ProcessPendingHostInputs();
        }

        private void QueueOrApplyHostInput(T value)
        {
            if (_simulatedHostPing <= 0f)
            {
                _value = value;
                onChanged?.Invoke(value);
                return;
            }

            if (_lastQueuedHostValue.Equals(value))
                return;

            _lastQueuedHostValue = value;

            _pendingHostInputs.Enqueue(new PendingInput
            {
                value = value,
                timeSent = Time.unscaledTime
            });
        }

        private void ProcessPendingHostInputs()
        {
            float delay = _simulatedHostPing * 0.001f;
            float now = Time.unscaledTime;

            while (_pendingHostInputs.Count > 0)
            {
                var input = _pendingHostInputs.Peek();
                if (now < input.timeSent + delay)
                    break;
                _value = _pendingHostInputs.Dequeue().value;
                onChanged?.Invoke(_value);
            }
        }

        private void SendInput(T value, int id)
        {
            SendInputServerRpc(value, id);
        }

        [ServerRpc(channel: Channel.Unreliable, immediate:true)]
        private void SendInputServerRpc(T value, int id, RPCInfo info = default)
        {
            _lastValue = _value;
            _value = value;
            Acknowledge(info.sender, id);
            if(!_lastValue.Equals(_value))
                onChanged?.Invoke(_value);
        }

        [TargetRpc(channel: Channel.Unreliable, immediate:true)]
        private void Acknowledge(PlayerID player, int id)
        {
            if (isHost) return;

            if (id > _lastAckId)
                _lastAckId = id;
        }

        private struct PendingInput
        {
            public T value;
            public float timeSent;
        }
    }
}
