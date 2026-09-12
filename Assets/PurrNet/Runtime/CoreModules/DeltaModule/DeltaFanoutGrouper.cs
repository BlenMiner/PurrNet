using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    /// <summary>
    /// Partitions the recipients of one delta-packed multi-target RPC call by the acknowledged
    /// baseline of every argument. Recipients that share every baseline decode an identical
    /// payload, so the generated send path encodes once per group (for a representative player)
    /// and fans the entry out through the batch's shared-entry path instead of running the
    /// whole pipeline once per player.
    /// </summary>
    [UsedByIL]
    public sealed class DeltaFanoutGrouper
    {
        private static readonly Stack<DeltaFanoutGrouper> _pool = new();

        private DeltaModule _deltaModule;
        private RPCPacket? _rpcPacket;
        private StaticRPCPacket? _staticPacket;
        private ChildRPCPacket? _childPacket;
        private bool _reliable;
        private ulong _offset;

        private PlayerID[] _players = new PlayerID[16];
        private int[] _playerSlots = new int[16];
        private int[] _groupOf = new int[16];
        private int[] _nextGroupOf = new int[16];
        private int[] _pairGroup = new int[16];
        private uint[] _pairBaseline = new uint[16];
        private int _playerCount;
        private int _groupCount;
        private bool _singletons;

        private readonly List<List<PlayerID>> _members = new();
        private int _builtGroupCount;

        internal int groupCount => _builtGroupCount;

        private static DeltaFanoutGrouper Rent()
        {
            return _pool.Count > 0 ? _pool.Pop() : new DeltaFanoutGrouper();
        }

        internal static DeltaFanoutGrouper Begin(DeltaModule module, RPCPacket context, bool reliable,
            DisposableList<PlayerID> players)
        {
            var grouper = Rent();
            grouper._rpcPacket = context;
            grouper.Setup(module, reliable, players);
            return grouper;
        }

        internal static DeltaFanoutGrouper Begin(DeltaModule module, StaticRPCPacket context, bool reliable,
            DisposableList<PlayerID> players)
        {
            var grouper = Rent();
            grouper._staticPacket = context;
            grouper.Setup(module, reliable, players);
            return grouper;
        }

        internal static DeltaFanoutGrouper Begin(DeltaModule module, ChildRPCPacket context, bool reliable,
            DisposableList<PlayerID> players)
        {
            var grouper = Rent();
            grouper._childPacket = context;
            grouper.Setup(module, reliable, players);
            return grouper;
        }

        private void Setup(DeltaModule module, bool reliable, DisposableList<PlayerID> players)
        {
            _deltaModule = module;
            _reliable = reliable;
            _offset = 0;
            _singletons = module == null;
            _builtGroupCount = 0;

            int count = players.Count;
            EnsureCapacity(count);
            _playerCount = count;

            int specialGroups = 0;
            for (int i = 0; i < count; i++)
            {
                var player = players[i];
                _players[i] = player;
                _playerSlots[i] = module != null ? module.GetPlayerSlot(player) : 0;
                _groupOf[i] = player == PlayerID.Server ? ++specialGroups : 0;
            }

            _groupCount = count == 0 ? 0 : specialGroups + 1;
        }

        private void EnsureCapacity(int count)
        {
            if (_players.Length >= count)
                return;

            int size = _players.Length;
            while (size < count)
                size *= 2;

            _players = new PlayerID[size];
            _playerSlots = new int[size];
            _groupOf = new int[size];
            _nextGroupOf = new int[size];
            _pairGroup = new int[size];
            _pairBaseline = new uint[size];
        }

        [UsedByIL]
        public void Key<T>(T value)
        {
            ulong offset = _offset++;

            if (_singletons)
                return;

            if (_reliable || !DeltaSharedEncodeInfo<T>.eligible)
            {
                _singletons = true;
                return;
            }

            uint hash;
            if (_rpcPacket.HasValue)
                hash = DeltaModule.GetKeyHash(new NetworkIdentityRpcHash<T, RPCPacket>(_rpcPacket.Value, offset));
            else if (_staticPacket.HasValue)
                hash = DeltaModule.GetKeyHash(new NetworkIdentityRpcHash<T, StaticRPCPacket>(_staticPacket.Value, offset));
            else if (_childPacket.HasValue)
                hash = DeltaModule.GetKeyHash(new NetworkIdentityRpcHash<T, ChildRPCPacket>(_childPacket.Value, offset));
            else
                return;

            Refine(_deltaModule.PrepareFanoutKey(hash, value));
        }

        private void Refine(DeltaModule.SenderKeyState state)
        {
            int newCount = 0;

            for (int i = 0; i < _playerCount; i++)
            {
                int group = _groupOf[i];
                uint baseline = state.GetAckedBaseline(_playerSlots[i]);

                int next = -1;
                for (int p = 0; p < newCount; p++)
                {
                    if (_pairGroup[p] == group && _pairBaseline[p] == baseline)
                    {
                        next = p;
                        break;
                    }
                }

                if (next < 0)
                {
                    next = newCount++;
                    _pairGroup[next] = group;
                    _pairBaseline[next] = baseline;
                }

                _nextGroupOf[i] = next;
            }

            (_groupOf, _nextGroupOf) = (_nextGroupOf, _groupOf);
            _groupCount = newCount;
        }

        /// <summary>
        /// Returns one player per group, in group order. Group <c>i</c> of the returned list
        /// corresponds to <see cref="GetMembers"/><c>(i)</c>.
        /// </summary>
        [UsedByIL]
        public DisposableList<PlayerID> BuildRepresentatives()
        {
            if (_singletons)
            {
                for (int i = 0; i < _playerCount; i++)
                    _groupOf[i] = i;
                _groupCount = _playerCount;
            }

            while (_members.Count < _groupCount)
                _members.Add(null);

            for (int g = 0; g < _groupCount; g++)
            {
                _members[g] ??= ListPool<PlayerID>.Instantiate();
                _members[g].Clear();
            }

            for (int i = 0; i < _playerCount; i++)
                _members[_groupOf[i]].Add(_players[i]);

            _builtGroupCount = _groupCount;

            var representatives = DisposableList<PlayerID>.Create(_groupCount);
            for (int g = 0; g < _groupCount; g++)
                representatives.Add(_members[g][0]);

            return representatives;
        }

        internal List<PlayerID> GetMembers(int group)
        {
            return _members[group];
        }

        [UsedByIL]
        public void End()
        {
            for (int g = 0; g < _members.Count; g++)
            {
                if (_members[g] == null)
                    continue;

                ListPool<PlayerID>.Destroy(_members[g]);
                _members[g] = null;
            }

            _members.Clear();
            _builtGroupCount = 0;
            _playerCount = 0;
            _groupCount = 0;
            _deltaModule = null;
            _rpcPacket = null;
            _staticPacket = null;
            _childPacket = null;
            _pool.Push(this);
        }
    }
}
