using UnityEngine;
using PurrNet.Logging;
using PurrNet.Modules;
using System;
using JetBrains.Annotations;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    [Serializable]
    public class SyncVar<T> : NetworkModule, ISerializationCallbackReceiver, ITickListener
    {
        private TickManager _tickManager;

        [SerializeField, PurrLock] private T _value;

        private bool _isDirty;

        [SerializeField, Space(-5), Header("Sync Settings"), PurrLock]
        private bool _ownerAuth;

        [SerializeField, PurrLock] private bool _ownerOnly;

        [SerializeField, Min(0)] private float _sendIntervalInSeconds;

        public bool ownerAuth => _ownerAuth;

        public override bool ownerOnly => _ownerOnly;

        public float sendIntervalInSeconds
        {
            get => _sendIntervalInSeconds;
            set => _sendIntervalInSeconds = value;
        }

        public event Action<T> onChanged;

        public delegate void ActionWithOld(T oldValue, T newValue);
        public event ActionWithOld onChangedWithOld;

        public bool isControllingSyncVar { get; private set; }

        private bool _isSubscribedToTickManager;

        private bool _ignoreServerUpdates;

        public T value
        {
            get => _value;
            set
            {
                if (PurrEquality<T>.Default.Equals(value, _value))
                    return;

                if (isSpawned && !isControllingSyncVar)
                {
                    InvalidateIsController(); // Re-check controller status in case it changed since last check.
                    if (!isControllingSyncVar)
                    {
                        PurrLogger.LogError(
                            $"Invalid permissions when setting `<b>SyncVar<{typeof(T).Name}> {name}</b>` on `{parent.name}`." +
                            $"\n{GetPermissionErrorDetails(_ownerAuth, this)}", parent);
                        return;
                    }
                }

                var oldValue = _value;
                _value = value;
                _ignoreServerUpdates = true;

                SetDirty();
                TriggerEvents(oldValue);
            }
        }

        public override void OnPoolReset()
        {
            onChanged = null;
            onChangedWithOld = null;
            isControllingSyncVar = false;
            _isDirty = false;
            _wasLastDirty = false;
            _sentLastTick = false;
            _id = 0;
            _ignoreServerUpdates = false;
            _value = _initialValue;
        }

        public override void OnOwnerDisconnected(PlayerID ownerId)
        {
            InvalidateIsController();
        }

        public override void OnOwnerReconnected(PlayerID ownerId)
        {
            InvalidateIsController();

            if (_ownerAuth && isServer)
                SendLatestState(ownerId, _id, _value);
        }

        public override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool isSpawnEvent, bool asServer)
        {
            InvalidateIsController();

            if (isSpawnEvent)
                return;

            if (_ownerAuth)
            {
                if (asServer || !isOwner)
                    _id = 0;

                if (isOwner)
                    SetDirty();
            }
        }

        private TickManager _subscribedTicker;

        private void SubscribeToTickManager()
        {
            if (_isSubscribedToTickManager)
                return;

            _isSubscribedToTickManager = true;
            _subscribedTicker = networkManager.tickModule;
            _subscribedTicker.AddTickListener(this);
        }

        private void UnsubscribeFromTickManager()
        {
            if (!_isSubscribedToTickManager)
                return;

            _isSubscribedToTickManager = false;
            _subscribedTicker.RemoveTickListener(this);
        }

        public override void OnInitializeModules()
        {
            InvalidateIsController();
        }

        public override void OnEarlySpawn()
        {
            InvalidateIsController();
        }

        public override void OnSpawn()
        {
            InvalidateIsController();
        }

        public override void OnSpawnSent()
        {
            if (isServer || !isControllingSyncVar)
                return;

            if (_isDirty || !PurrEquality<T>.Default.Equals(_value, _initialValue))
                FlushImmediately();
        }

        public override void OnObserverAdded(PlayerID player, bool isSpawner)
        {
            if (isSpawner && ownerAuth && owner == player)
                return;

            SendLatestState(player, _id, _value);
        }

        private void InvalidateIsController()
        {
            bool old = isControllingSyncVar;
            bool @new = parent && parent.IsController(_ownerAuth);

            isControllingSyncVar = @new;

            if (old != @new && !@new)
                _ignoreServerUpdates = false;
        }

        public override void OnDespawned()
        {
            InvalidateIsController();

            try
            {
                if (isControllingSyncVar && parent)
                {
                    ForceSendReliable();
                    _lastSendTime = Time.time;
                }
            }
            finally
            {
                _wasLastDirty = false;
                _sentLastTick = false;
                _isDirty = false;
                UnsubscribeFromTickManager();
            }
        }

        public void SetDirty()
        {
            if (!isControllingSyncVar)
                return;

            _isDirty = true;
            SubscribeToTickManager();
        }

        private float _lastSendTime;

        private void ForceSendUnreliable()
        {
            if (isServer)
                SendToAll(++_id, _value);
            else SendToServer(++_id, _value);
        }

        private void ForceSendReliable()
        {
            if (isServer)
                SendToAllReliably(++_id, _value);
            else SendToServerReliably(++_id, _value);
        }

        public void FlushImmediately()
        {
            ForceSendReliable();
            _lastSendTime = Time.time;
            _wasLastDirty = false;
            _sentLastTick = false;
            _isDirty = false;
            UnsubscribeFromTickManager();
        }

        public void OnTick()
        {
            if (!isControllingSyncVar || !parent)
            {
                UnsubscribeFromTickManager();
                return;
            }

            if (!parent.isFullySpawned)
                return;

            if (_isDirty)
            {
                float time = Time.time;

                if (time - _lastSendTime < _sendIntervalInSeconds)
                {
                    _sentLastTick = false;
                    return;
                }

                if (_sentLastTick)
                {
                    ForceSendUnreliable();
                    _wasLastDirty = true;
                }
                else
                {
                    ForceSendReliable();
                    _wasLastDirty = false;
                }

                _lastSendTime = time;
                _sentLastTick = true;
                _isDirty = false;
            }
            else
            {
                _sentLastTick = false;

                if (_wasLastDirty)
                {
                    ForceSendReliable();
                    _wasLastDirty = false;
                }

                UnsubscribeFromTickManager();
            }
        }

        private ulong _id;

        private bool _wasLastDirty;

        private bool _sentLastTick;

        [SerializeField, HideInInspector]
        private T _initialValue;

        public SyncVar(T initialValue = default, float sendIntervalInSeconds = 0f, bool ownerAuth = false, bool ownerOnly = false)
        {
            _initialValue = initialValue;
            _value = initialValue;
            _sendIntervalInSeconds = sendIntervalInSeconds;
            _ownerAuth = ownerAuth;
            _ownerOnly = ownerOnly;
        }

        [TargetRpc, UsedImplicitly]
        private void SendLatestState(PlayerID player, PackedULong packetId, T newValue)
        {
            if (isServer)
                return;

            if (packetId.value < _id)
                return;

            if (_ignoreServerUpdates)
            {
                if (packetId.value > _id)
                    _id = packetId.value;
                return;
            }

            _id = packetId.value;

            var oldValue = _value;

            if (!Packer.Transform(ref _value, newValue))
                return;

            TriggerEvents(oldValue);
        }

        [ServerRpc(Channel.Unreliable, requireOwnership: true)]
        private void SendToServer(PackedULong packetId, T newValue)
        {
            if (!_ownerAuth)
                return;

            OnReceivedValue(packetId, newValue);
            SendToOthers(packetId, newValue);
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendToServerReliably(PackedULong packetId, T newValue)
        {
            if (!_ownerAuth)
                return;

            OnReceivedValue(packetId, newValue);
            SendToOthersReliably(packetId, newValue);
        }

        [ObserversRpc(Channel.Unreliable, excludeOwner: true)]
        private void SendToOthers(PackedULong packetId, T newValue)
        {
            if (!isServer)
                OnReceivedValue(packetId, newValue);
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendToOthersReliably(PackedULong packetId, T newValue)
        {
            if (!isHost)
                OnReceivedValue(packetId, newValue);
        }

        [ObserversRpc(Channel.Unreliable)]
        private void SendToAll(PackedULong packetId, T newValue)
        {
            if (!isHost)
                OnReceivedValue(packetId, newValue);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendToAllReliably(PackedULong packetId, T newValue)
        {
            if (!isHost)
                OnReceivedValue(packetId, newValue);
        }

        private void OnReceivedValue(PackedULong packetId, T newValue)
        {
            if (isControllingSyncVar)
                return;

            if (packetId.value <= _id)
                return;

            _id = packetId.value;
            var oldValue = _value;

            if (!Packer.Transform(ref _value, newValue))
                return;

            TriggerEvents(oldValue);
        }

        private void TriggerEvents(T oldValue)
        {
            try
            {
                onChanged?.Invoke(value);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            try
            {
                onChangedWithOld?.Invoke(oldValue, value);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static implicit operator T(SyncVar<T> syncVar)
        {
            return syncVar._value;
        }

        public override string ToString()
        {
            return value.ToString();
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            _initialValue = _value;
        }
    }
}
