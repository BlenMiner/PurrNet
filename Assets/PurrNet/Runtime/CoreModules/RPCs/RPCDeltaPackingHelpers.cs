using System;
using System.Reflection;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    [UsedByIL]
    public struct RPCPacketPacker
    {
        private RPCPacket? _rpcPacket;
        private StaticRPCPacket? _staticPacket;
        private ChildRPCPacket? _childPacket;

        private PlayerID _target;
        private PackedUInt _cache;
        private DeltaModule _deltaModule;
        private bool _reliable;
        private ulong _offset;

        [UsedByIL]
        public static RPCPacketPacker CreateWithInfo(NetworkManager manager, RPCPacket context, RPCInfo info)
        {
            return Create(manager, context, info.compileTimeSignature, info.sender);
        }

        [UsedByIL]
        public static RPCPacketPacker CreateStaticWithInfo(NetworkManager manager, StaticRPCPacket context, RPCInfo info)
        {
            return CreateStatic(manager, context, info.compileTimeSignature, info.sender);
        }

        [UsedByIL]
        public static RPCPacketPacker CreateChildWithInfo(NetworkManager manager, ChildRPCPacket context, RPCInfo info)
        {
            return CreateChild(manager, context, info.compileTimeSignature, info.sender);
        }

        // A missing manager/module falls back to per-player groups, so the per-target loop
        // below behaves (and fails) exactly as it did before grouping.
        private static DeltaModule TryGetDeltaModule(NetworkManager manager)
        {
            return manager ? manager.deltaModule : null;
        }

        [UsedByIL]
        public static DeltaFanoutGrouper BeginFanout(NetworkManager manager, RPCPacket context, RPCSignature signature,
            DisposableList<PlayerID> players)
        {
            return DeltaFanoutGrouper.Begin(TryGetDeltaModule(manager), context,
                signature.channel == Channel.ReliableOrdered, players);
        }

        [UsedByIL]
        public static DeltaFanoutGrouper BeginFanoutStatic(NetworkManager manager, StaticRPCPacket context,
            RPCSignature signature, DisposableList<PlayerID> players)
        {
            return DeltaFanoutGrouper.Begin(TryGetDeltaModule(manager), context,
                signature.channel == Channel.ReliableOrdered, players);
        }

        [UsedByIL]
        public static DeltaFanoutGrouper BeginFanoutChild(NetworkManager manager, ChildRPCPacket context,
            RPCSignature signature, DisposableList<PlayerID> players)
        {
            return DeltaFanoutGrouper.Begin(TryGetDeltaModule(manager), context,
                signature.channel == Channel.ReliableOrdered, players);
        }

        [UsedByIL]
        public static RPCPacketPacker Create(NetworkManager manager, RPCPacket context, RPCSignature signature, PlayerID target)
        {
            if (!manager)
                throw new InvalidOperationException("NetworkManager is not initialized.");

            var deltaModule = manager.deltaModule;

            if (deltaModule == null)
                throw new InvalidOperationException("Delta module is not initialized.");

            return new RPCPacketPacker
            {
                _rpcPacket = context,
                _target = target,
                _cache = default,
                _deltaModule = deltaModule,
                _reliable = signature.channel == Channel.ReliableOrdered
            };
        }

        [UsedByIL]
        public static RPCPacketPacker CreateStatic(NetworkManager manager, StaticRPCPacket context, RPCSignature signature, PlayerID target)
        {
            if (!manager)
                throw new InvalidOperationException("NetworkManager is not initialized.");

            var deltaModule = manager.deltaModule;

            if (deltaModule == null)
                throw new InvalidOperationException("Delta module is not initialized.");

            return new RPCPacketPacker
            {
                _staticPacket = context,
                _target = target,
                _cache = default,
                _deltaModule = deltaModule,
                _reliable = signature.channel == Channel.ReliableOrdered
            };
        }

        [UsedByIL]
        public static RPCPacketPacker CreateChild(NetworkManager manager, ChildRPCPacket context, RPCSignature signature, PlayerID target)
        {
            if (!manager)
                throw new InvalidOperationException("NetworkManager is not initialized.");

            var deltaModule = manager.deltaModule;

            if (deltaModule == null)
                throw new InvalidOperationException("Delta module is not initialized.");

            return new RPCPacketPacker
            {
                _childPacket = context,
                _target = target,
                _cache = default,
                _deltaModule = deltaModule,
                _reliable = signature.channel == Channel.ReliableOrdered
            };
        }

        [UsedByIL]
        public object ReadObject(BitPacker packer, Type type)
        {
            var method = GetType().GetMethod(nameof(Read), BindingFlags.Instance | BindingFlags.Public);

            if (method == null)
                throw new InvalidOperationException("Cannot find `Read<>` method.");

            // Create the generic method Read<T> with the runtime type
            var generic = method.MakeGenericMethod(type);

            // Prepare the arguments (the out/ref value will be passed as an object)
            var args = PreciseArrayPool<object>.Rent(2);
            args[0] = packer;
            args[1] = GetDefault(type);

            // Invoke the generic method
            generic.Invoke(this, args);
            var res = args[1];

            PreciseArrayPool<object>.Return(args);

            // The second argument now holds the resulting value
            return res;
        }

        [UsedByIL]
        public T ReadObject<T>(BitPacker packer)
        {
            T res = default;
            Read(packer, ref res);
            return res;
        }

        static object GetDefault(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        [UsedByIL]
        public void Write<T>(BitPacker packer, T value)
        {
            if (_rpcPacket.HasValue)
            {
                var key = new NetworkIdentityRpcHash<T, RPCPacket>(_rpcPacket.Value, _offset++);
                if (_reliable)
                    _deltaModule.WriteReliable(packer, _target, key, value);
                else _deltaModule.Write(packer, _target, key, value, ref _cache);
            }
            else if (_staticPacket.HasValue)
            {
                var key = new NetworkIdentityRpcHash<T, StaticRPCPacket>(_staticPacket.Value, _offset++);
                if (_reliable)
                    _deltaModule.WriteReliable(packer, _target, key, value);
                else _deltaModule.Write(packer, _target, key, value, ref _cache);
            }
            else if (_childPacket.HasValue)
            {
                var key = new NetworkIdentityRpcHash<T, ChildRPCPacket>(_childPacket.Value, _offset++);
                if (_reliable)
                    _deltaModule.WriteReliable(packer, _target, key, value);
                else _deltaModule.Write(packer, _target, key, value, ref _cache);
            }
        }

        [UsedByIL]
        public void Read<T>(BitPacker packer, ref T value)
        {
            if (_rpcPacket.HasValue)
            {
                var key = new NetworkIdentityRpcHash<T, RPCPacket>(_rpcPacket.Value, _offset++);
                if (_reliable)
                    _deltaModule.ReadReliable(packer, key, ref value);
                else _deltaModule.Read(packer, key, _target, ref value, ref _cache);
            }
            else if (_staticPacket.HasValue)
            {
                var key = new NetworkIdentityRpcHash<T, StaticRPCPacket>(_staticPacket.Value, _offset++);
                if (_reliable)
                    _deltaModule.ReadReliable(packer, key, ref value);
                else _deltaModule.Read(packer, key, _target, ref value, ref _cache);
            }
            else if (_childPacket.HasValue)
            {
                var key = new NetworkIdentityRpcHash<T, ChildRPCPacket>(_childPacket.Value, _offset++);
                if (_reliable)
                    _deltaModule.ReadReliable(packer, key, ref value);
                else _deltaModule.Read(packer, key, _target, ref value, ref _cache);
            }

            AsyncPackableHelper.PrepareAfterUnpack(ref value);
        }
    }
}
