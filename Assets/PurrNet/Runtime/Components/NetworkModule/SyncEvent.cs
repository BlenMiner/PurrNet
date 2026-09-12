using UnityEngine;
using PurrNet.Logging;
using System;
using PurrNet.Packing;
using UnityEngine.Events;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    public struct SyncEventData : IDisposable
    {
        public BitData data;

        [DontPack] private bool _ownsPacker;

        public SyncEventData AddData<T>(T value)
        {
            var packer = _ownsPacker ? data.packer : BitPackerPool.Get();
            _ownsPacker = true;

            Packer<T>.Write(packer, value);
            data = new BitData(packer);
            return this;
        }

        public T ReadData<T>()
        {
            if (data.packer == null)
                return default;

            return Packer<T>.Read(data.packer);
        }

        public BitDataScope BeginRead() => data.AutoScope();

        public void Dispose()
        {
            if (_ownsPacker)
            {
                data.Dispose();
                _ownsPacker = false;
            }

            data = default;
        }
    }

    [Serializable]
    public abstract class SyncEventBase : NetworkModule
    {
        [SerializeField, PurrLock] protected bool _ownerAuth;

        [SerializeField, PurrLock] protected bool _ownerOnly;

        /// <summary>
        /// Whether it is the owner or  the server that has the authority to invoke the event
        /// </summary>
        public bool ownerAuth => _ownerAuth;

        public override bool ownerOnly => _ownerOnly;

        protected SyncEventBase(bool ownerAuth = false, bool ownerOnly = false)
        {
            _ownerAuth = ownerAuth;
            _ownerOnly = ownerOnly;
        }

        protected bool ValidateInvoke()
        {
            if (!isSpawned) return true;

            bool controller = parent.IsController(_ownerAuth);
            if (!controller)
            {
                PurrLogger.LogError(
                    $"Invalid permissions when invoking `<b>{GetType().Name} {name}</b>` on `{parent.name}`." +
                    $"\n{GetPermissionErrorDetails(_ownerAuth, this)}", parent);
                return false;
            }

            return true;
        }

        protected abstract void InvokeLocal();
    }

    [Serializable]
    public abstract class SyncEventLogic<T> : SyncEventBase
    {
        protected SyncEventData _lastData;

        protected SyncEventLogic(bool ownerAuth = false, bool ownerOnly = false) : base(ownerAuth, ownerOnly) { }

        protected abstract void InvokeUnityEvent(T data);
        protected virtual void ClearUnityEvent() { }

        public override void OnPoolReset()
        {
            ClearUnityEvent();
            _lastData.Dispose();
        }

        public void InvokePacket(SyncEventData data)
        {
            if (!ValidateInvoke())
            {
                data.Dispose();
                return;
            }

            _lastData.Dispose();
            _lastData = data;

            if (isSpawned)
            {
                if (isServer) SendToAll(data);
                else SendToServer(data);
            }

            InvokeLocal();
        }

        public void RemoveAllListeners(bool sync = false)
        {
            if (!sync) { ClearUnityEvent(); return; }
            RemoveAllListenersRpc();
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendToServer(SyncEventData data)
        {
            using (data)
            {
                if (!_ownerAuth)
                    return;
                SendToOthers(data);
            }
        }

        [ObserversRpc(Channel.ReliableOrdered, excludeOwner: true)]
        private void SendToOthers(SyncEventData data)
        {
            if (isServer && !isHost)
            {
                data.Dispose();
                return;
            }

            _lastData.Dispose();
            _lastData = data;
            InvokeLocal();
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendToAll(SyncEventData data)
        {
            if (!isHost)
            {
                _lastData.Dispose();
                _lastData = data;
                InvokeLocal();
            }
            else data.Dispose();
        }

        [ObserversRpc(runLocally: true)]
        private void RemoveAllListenersRpc() => ClearUnityEvent();
    }

    [Serializable]
    public class SerializableSyncUnityEvent : UnityEvent { }

    [Serializable]
    public class SyncEvent : SyncEventLogic<byte>
    {
        [SerializeField] private SerializableSyncUnityEvent unityEvent = new SerializableSyncUnityEvent();

        public SyncEvent(bool ownerAuth = false, bool ownerOnly = false) : base(ownerAuth, ownerOnly) { }

        public void AddListener(UnityAction listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction listener) => unityEvent.RemoveListener(listener);

        public void Invoke() => InvokePacket(default);

        protected override void InvokeLocal()
        {
            unityEvent.Invoke();
        }

        protected override void InvokeUnityEvent(byte data) => unityEvent?.Invoke();
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();

        public static SyncEvent operator +(SyncEvent e, UnityAction listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent operator -(SyncEvent e, UnityAction listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }

    [Serializable]
    public class SerializableSyncUnityEvent<T> : UnityEvent<T> { }

    [Serializable]
    public class SyncEvent<T> : SyncEventLogic<T>
    {
        [SerializeField] private SerializableSyncUnityEvent<T> unityEvent = new SerializableSyncUnityEvent<T>();

        public SyncEvent(bool ownerAuth = false, bool ownerOnly = false) : base(ownerAuth, ownerOnly) { }

        public void AddListener(UnityAction<T> listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction<T> listener) => unityEvent.RemoveListener(listener);

        public void Invoke(T arg) => InvokePacket(new SyncEventData().AddData<T>(arg));

        protected override void InvokeLocal()
        {
            using var scope = _lastData.BeginRead();
            var value = _lastData.ReadData<T>();
            unityEvent.Invoke(value);
        }

        protected override void InvokeUnityEvent(T arg) => unityEvent?.Invoke(arg);
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();

        public static SyncEvent<T> operator +(SyncEvent<T> e, UnityAction<T> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T> operator -(SyncEvent<T> e, UnityAction<T> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }

    [Serializable]
    public class SerializableSyncUnityEvent<T1, T2> : UnityEvent<T1, T2> { }

    [Serializable]
    public class SyncEvent<T1, T2> : SyncEventLogic<(T1, T2)>
    {
        [SerializeField] private SerializableSyncUnityEvent<T1, T2> unityEvent = new SerializableSyncUnityEvent<T1, T2>();

        public SyncEvent(bool ownerAuth = false, bool ownerOnly = false) : base(ownerAuth, ownerOnly) { }

        public void AddListener(UnityAction<T1, T2> listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction<T1, T2> listener) => unityEvent.RemoveListener(listener);

        public void Invoke(T1 arg1, T2 arg2) => InvokePacket(new SyncEventData().AddData<T1>(arg1).AddData<T2>(arg2));

        protected override void InvokeLocal()
        {
            using var scope = _lastData.BeginRead();
            var value1 = _lastData.ReadData<T1>();
            var value2 = _lastData.ReadData<T2>();
            unityEvent.Invoke(value1, value2);
        }

        protected override void InvokeUnityEvent((T1, T2) data) => unityEvent?.Invoke(data.Item1, data.Item2);
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();

        public static SyncEvent<T1, T2> operator +(SyncEvent<T1, T2> e, UnityAction<T1, T2> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T1, T2> operator -(SyncEvent<T1, T2> e, UnityAction<T1, T2> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }

    [Serializable]
    public class SerializableSyncUnityEvent<T1, T2, T3> : UnityEvent<T1, T2, T3> { }

    [Serializable]
    public class SyncEvent<T1, T2, T3> : SyncEventLogic<(T1, T2, T3)>
    {
        [SerializeField] private SerializableSyncUnityEvent<T1, T2, T3> unityEvent = new SerializableSyncUnityEvent<T1, T2, T3>();

        public SyncEvent(bool ownerAuth = false, bool ownerOnly = false) : base(ownerAuth, ownerOnly) { }

        public void AddListener(UnityAction<T1, T2, T3> listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction<T1, T2, T3> listener) => unityEvent.RemoveListener(listener);

        public void Invoke(T1 a, T2 b, T3 c) => InvokePacket(new SyncEventData().AddData<T1>(a).AddData<T2>(b).AddData<T3>(c));
        protected override void InvokeLocal()
        {
            using var scope = _lastData.BeginRead();
            var value1 = _lastData.ReadData<T1>();
            var value2 = _lastData.ReadData<T2>();
            var value3 = _lastData.ReadData<T3>();
            unityEvent.Invoke(value1, value2, value3);
        }

        protected override void InvokeUnityEvent((T1, T2, T3) data) => unityEvent?.Invoke(data.Item1, data.Item2, data.Item3);
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();

        public static SyncEvent<T1, T2, T3> operator +(SyncEvent<T1, T2, T3> e, UnityAction<T1, T2, T3> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T1, T2, T3> operator -(SyncEvent<T1, T2, T3> e, UnityAction<T1, T2, T3> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }

    [Serializable]
    public class SerializableSyncUnityEvent<T1, T2, T3, T4> : UnityEvent<T1, T2, T3, T4> { }

    [Serializable]
    public class SyncEvent<T1, T2, T3, T4> : SyncEventLogic<(T1, T2, T3, T4)>
    {
        [SerializeField] private SerializableSyncUnityEvent<T1, T2, T3, T4> unityEvent = new SerializableSyncUnityEvent<T1, T2, T3, T4>();

        public SyncEvent(bool ownerAuth = false, bool ownerOnly = false) : base(ownerAuth, ownerOnly) { }

        public void AddListener(UnityAction<T1, T2, T3, T4> listener) => unityEvent.AddListener(listener);
        public void RemoveListener(UnityAction<T1, T2, T3, T4> listener) => unityEvent.RemoveListener(listener);

        public void Invoke(T1 a, T2 b, T3 c, T4 d) => InvokePacket(new SyncEventData().AddData<T1>(a).AddData<T2>(b).AddData<T3>(c).AddData<T4>(d));
        protected override void InvokeLocal()
        {
            using var scope = _lastData.BeginRead();
            var value1 = _lastData.ReadData<T1>();
            var value2 = _lastData.ReadData<T2>();
            var value3 = _lastData.ReadData<T3>();
            var value4 = _lastData.ReadData<T4>();
            unityEvent.Invoke(value1, value2, value3, value4);
        }

        protected override void InvokeUnityEvent((T1, T2, T3, T4) data) => unityEvent?.Invoke(data.Item1, data.Item2, data.Item3, data.Item4);
        protected override void ClearUnityEvent() => unityEvent.RemoveAllListeners();

        public static SyncEvent<T1, T2, T3, T4> operator +(SyncEvent<T1, T2, T3, T4> e, UnityAction<T1, T2, T3, T4> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T1, T2, T3, T4> operator -(SyncEvent<T1, T2, T3, T4> e, UnityAction<T1, T2, T3, T4> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }

    [Serializable]
    public class SyncEvent<T1, T2, T3, T4, T5> : SyncEventLogic<(T1, T2, T3, T4, T5)>
    {
        private event Action<T1, T2, T3, T4, T5> unityEvent;

        public SyncEvent(bool ownerAuth = false, bool ownerOnly = false) : base(ownerAuth, ownerOnly) { }

        public void AddListener(Action<T1, T2, T3, T4, T5> listener) => unityEvent += listener;
        public void RemoveListener(Action<T1, T2, T3, T4, T5> listener) => unityEvent -= listener;

        public void Invoke(T1 a, T2 b, T3 c, T4 d, T5 e) => InvokePacket(new SyncEventData().AddData<T1>(a).AddData<T2>(b).AddData<T3>(c).AddData<T4>(d).AddData<T5>(e));
        protected override void InvokeLocal()
        {
            using var scope = _lastData.BeginRead();
            var value1 = _lastData.ReadData<T1>();
            var value2 = _lastData.ReadData<T2>();
            var value3 = _lastData.ReadData<T3>();
            var value4 = _lastData.ReadData<T4>();
            var value5 = _lastData.ReadData<T5>();
            unityEvent?.Invoke(value1, value2, value3, value4, value5);
        }

        protected override void InvokeUnityEvent((T1, T2, T3, T4, T5) data) => unityEvent?.Invoke(data.Item1, data.Item2, data.Item3, data.Item4, data.Item5);
        protected override void ClearUnityEvent() => unityEvent = null;

        public static SyncEvent<T1, T2, T3, T4, T5> operator +(SyncEvent<T1, T2, T3, T4, T5> e, Action<T1, T2, T3, T4, T5> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.AddListener(listener);
            return e;
        }

        public static SyncEvent<T1, T2, T3, T4, T5> operator -(SyncEvent<T1, T2, T3, T4, T5> e, Action<T1, T2, T3, T4, T5> listener)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            e.RemoveListener(listener);
            return e;
        }
    }
}
