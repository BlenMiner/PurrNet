using UnityEngine;
using PurrNet.Logging;
using PurrNet.Modules;
using System;
using JetBrains.Annotations;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    [Serializable]
    public class SyncVar<T> : NetworkModule where T : struct
    {
        private TickManager _tickManager;

        [SerializeField, PurrLock]
        private T _value;

        private bool _isDirty;

        [SerializeField, Space(-5), Header("Sync Settings"), PurrLock]
        private bool _ownerAuth;
        
        [SerializeField, Min(0)]
        private float _sendIntervalInSeconds;
        
        public bool ownerAuth => _ownerAuth;

        public float sendIntervalInSeconds
        {
            get => _sendIntervalInSeconds;
            set => _sendIntervalInSeconds = value;
        }

        public event Action<T> onChanged;

        public T value
        {
            get => _value;
            set
            {
                if (isSpawned)
                {
                    bool isController = parent.IsController(_ownerAuth);
                    if (!isController)
                    {
                        PurrLogger.LogError(
                            $"Invalid permissions when setting '<b>SyncVar<{typeof(T).Name}> {name}</b>' on '{parent.name}'." +
                            $"\nMaybe try enabling owner authority.", parent);
                        return;
                    }

                    if (value.Equals(_value))
                        return;

                    _value = value;
                    _isDirty = true;
                }
                else
                {
                    _value = value;
                }

                onChanged?.Invoke(value);
            }
        }

        public override void OnSpawn()
        {
            _tickManager = networkManager.GetModule<TickManager>(isServer);
            _tickManager.onTick += OnTick;
            
        }

        public override void OnDespawned()
        {
            _tickManager.onTick -= OnTick;
        }
        
        public override void OnObserverAdded(PlayerID player)
        {
            SendLatestState(player, _id, _value);
        }

        private float _lastSendTime;

        private void ForceSendUnreliable()
        {
            if (isServer)
                 SendToAll(_id++, _value);
            else SendToServer(_id++, _value);
        }
        
        private void ForceSendReliable()
        {
            if (isServer)
                 SendToAllReliably(_id++, _value);
            else SendToServerReliably(_id++, _value);
        }

        private void OnTick()
        {
            bool isController = parent.IsController(_ownerAuth);

            if (!isController) 
                return;
            
            float timeSinceLastSend = Time.time - _lastSendTime;

            if (timeSinceLastSend < _sendIntervalInSeconds)
                return;

            if (_isDirty)
            {
                ForceSendUnreliable();
                
                _lastSendTime = Time.time;
                _wasLastDirty = true;
                _isDirty = false;
            }
            else
            {
                if (_wasLastDirty)
                {
                    ForceSendReliable();
                    _wasLastDirty = false;
                }
            }
        }

        private ushort _id;
        private bool _wasLastDirty;

        public SyncVar(T initialValue = default, float sendIntervalInSeconds = 0f, bool ownerAuth = false)
        {
            _value = initialValue;
            _sendIntervalInSeconds = sendIntervalInSeconds;
            _ownerAuth = ownerAuth;
        }
        
        [TargetRpc, UsedImplicitly]
        private void SendLatestState(PlayerID player, ushort packetId, T newValue)
        {
            if (isServer) return;
            
            _id = packetId;
            
            if (_value.Equals(newValue))
                return;
            
            _value = newValue;
            onChanged?.Invoke(value);
        }
        
        [ServerRpc(Channel.Unreliable, requireOwnership: true)]
        private void SendToServer(ushort packetId, T newValue)
        {
            if (!_ownerAuth) return;
            
            OnReceivedValue(packetId, newValue);
            SendToOthers(packetId, newValue);
        }
        
        [ServerRpc(Channel.ReliableUnordered, requireOwnership: true)]
        private void SendToServerReliably(ushort packetId, T newValue)
        {
            if (!_ownerAuth) return;
            
            OnReceivedValue(packetId, newValue);
            SendToOthersReliably(packetId, newValue);
        }
        
        [ObserversRpc(Channel.Unreliable, excludeOwner: true)]
        private void SendToOthers(ushort packetId, T newValue)
        {
            if (!isHost) OnReceivedValue(packetId, newValue);
        }
        
        [ObserversRpc(Channel.ReliableUnordered, excludeOwner: true)]
        private void SendToOthersReliably(ushort packetId, T newValue)
        {
            if (!isHost) OnReceivedValue(packetId, newValue);
        }
        
        [ObserversRpc(Channel.Unreliable, excludeOwner: true)]
        private void SendToAll(ushort packetId, T newValue)
        {
            if (!isHost) OnReceivedValue(packetId, newValue);
        }
        
        [ObserversRpc(Channel.ReliableUnordered, excludeOwner: true)]
        private void SendToAllReliably(ushort packetId, T newValue)
        {
            if (!isHost) OnReceivedValue(packetId, newValue);
        }
        
        private void OnReceivedValue(ushort packetId, T newValue)
        {
            bool isController = parent.IsController(_ownerAuth);
            
            if (isController)
                return;
            
            if (packetId <= _id)
                return;
            
            _id = packetId;
            
            if (_value.Equals(newValue))
                return;
            
            _value = newValue;
            onChanged?.Invoke(value);
        }

        public static implicit operator T(SyncVar<T> syncVar)
        {
            return syncVar._value;
        }

        public override string ToString()
        {
            return value.ToString();
        }
    }
}