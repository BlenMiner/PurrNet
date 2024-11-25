using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet
{
    public class NetworkReflection : NetworkIdentity, ITick
    {
        [SerializeField, HideInInspector] Object _trackedBehaviour;
        [SerializeField, HideInInspector] List<ReflectionData> _trackedFields;
        [SerializeField, HideInInspector] private bool _ownerAuth = true;

        private ReflectedValue[] _reflectedValues;
        
        public Type trackedType => _trackedBehaviour ? _trackedBehaviour.GetType() : null;
        
        public List<ReflectionData> trackedFields
        {
            get => _trackedFields;
            set => _trackedFields = value;
        }

        private void Awake()
        {
            if (_trackedBehaviour == null)
            {
                PurrLogger.LogError("Tracked behaviour is null, aborting", this);
                return;
            }
            
            _reflectedValues = new ReflectedValue[_trackedFields.Count];

            for (var i = 0; i < _trackedFields.Count; i++)
            {
                var value = new ReflectedValue(_trackedBehaviour, _trackedFields[i]);
                if (value.valueType != null)
                    Utils.Hasher.PrepareType(value.valueType);
                _reflectedValues[i] = value;
            }
        }

        protected override void OnObserverAdded(PlayerID player)
        {
            for (var i = 0; i < _reflectedValues.Length; i++)
            {
                var reflectedValue = _reflectedValues[i];
                SendMemberUpdate(i, reflectedValue.lastValue);
            }
        }

        public void OnTick(float delta)
        {
            if (!IsController(_ownerAuth)) return;
            
            if (!_trackedBehaviour || _reflectedValues == null)
                return;
            
            for (var i = 0; i < _reflectedValues.Length; i++)
            {
                var reflectedValue = _reflectedValues[i];
                if (reflectedValue.Update())
                    SendMemberUpdate(i, reflectedValue.lastValue);
            }
        }

        private void SendMemberUpdate(int index, object data)
        {
            using var stream = BitPackerPool.Get();

            Packer.Write(stream, data);
            
            var byteData = stream.ToByteData();
            
            if (isServer)
                 ObserversRpc(index, byteData);
            else ForwardThroughServer(index, byteData);
        }
        
        [ServerRpc]
        private void ForwardThroughServer(int index, ByteData data)
        {
            if (_ownerAuth)
                ObserversRpc(index, data);
        }
        
        [ObserversRpc]
        private void ObserversRpc(int index, ByteData data)
        {
            if (index < 0 || index >= _reflectedValues.Length)
            {
                PurrLogger.LogError($"Invalid index {index} on {name}", this);
                return;
            }
            
            var reflectedValue = _reflectedValues[index];

            if (reflectedValue.valueType == null)
            {
                PurrLogger.LogError($"Invalid type on {name} for {reflectedValue.name}", this);
                return;
            }
            
            using var stream = BitPackerPool.Get();
            
            stream.Write(data);
            stream.ResetPositionAndMode(true);
            
            object reflectedData = null;
            Packer.Read(stream, reflectedValue.valueType, ref reflectedData);
            reflectedValue.SetValue(reflectedData);
        }
    }
}
