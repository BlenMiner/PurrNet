using System;
using System.Collections.Generic;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    public class ReliableDeltaStream<T> : NetworkModule, ITick where T : unmanaged, IEquatable<T>
    {
        private readonly Dictionary<int, T> _values = new();
        private readonly List<int> _dirtyIndices = new();
        private readonly IStableHashable _keyBase;

        public ReliableDeltaStream(IStableHashable keyBase)
        {
            _keyBase = keyBase;
        }

        public T this[int index]
        {
            get => _values.GetValueOrDefault(index);
            set
            {
                if (!_values.TryGetValue(index, out var existing) || !existing.Equals(value))
                {
                    _values[index] = value;
                    if (!_dirtyIndices.Contains(index))
                        _dirtyIndices.Add(index);
                }
            }
        }

        public void Clear()
        {
            _values.Clear();
            _dirtyIndices.Clear();
        }

        public override void OnPoolReset()
        {
            Clear();
        }

        public void OnTick(float delta)
        {
            if (!isServer || _dirtyIndices.Count == 0)
                return;

            foreach (var player in networkManager.players)
            {
                using var packer = BitPackerPool.Get();
                Packer<int>.Write(packer, _dirtyIndices.Count);
                foreach (var idx in _dirtyIndices)
                {
                    Packer<int>.Write(packer, idx);
                    var key = new IndexKey(_keyBase, idx);
                    networkManager.deltaModule.Write(packer, player, key, _values[idx]);
                }

                if (packer.positionInBits > 0)
                    RpcDeltaChunk(player, packer.ToByteData());
            }

            _dirtyIndices.Clear();
        }

        [TargetRpc(channel: Channel.Unreliable)]
        private void RpcDeltaChunk(PlayerID target, ByteData data)
        {
            using var packer = BitPackerPool.Get();
            packer.MakeWrapper(data);
            packer.ResetPositionAndMode(true);

            int count = 0;
            Packer<int>.Read(packer, ref count);
            for (int i = 0; i < count; i++)
            {
                int idx = 0;
                Packer<int>.Read(packer, ref idx);
                var key = new IndexKey(_keyBase, idx);
                T val = default;
                networkManager.deltaModule.Read(packer, key,default, ref val);
                _values[idx] = val;
            }
        }

        private readonly struct IndexKey : IStableHashable
        {
            private readonly IStableHashable _base;
            private readonly int _index;

            public IndexKey(IStableHashable baseKey, int index)
            {
                _base = baseKey;
                _index = index;
            }

            public uint GetStableHash() => Hasher.CombineHashes(_base.GetStableHash(), (uint)_index);
        }
    }
}
