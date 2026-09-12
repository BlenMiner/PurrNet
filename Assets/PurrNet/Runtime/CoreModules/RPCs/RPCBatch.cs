using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using Unity.Profiling;

namespace PurrNet.Modules
{
    internal struct RPCBatchPacket : IPackedAuto
    {
        public const int HEADER = 6;

        public Size count;
        public BitData data;
    }

    internal struct ImmediateRPCBatchPacket : IPackedAuto
    {
        public Size count;
        public BitData data;
    }

    internal struct PendingBatchedData
    {
        public BatchKey key;
        public UnionRPCHeader lastHeader;
        public Size lastDataLen;
        public ulong stateVersion;
        public int batchCount;
        public int cachedMTU;
        public BitPacker batchedData;
    }

    internal struct CachedRPCEntry
    {
        public UnionRPCHeader previousHeader;
        public Size previousDataLen;
        public ulong previousStateVersion;
        public int bitLength;
        public BitPacker data;
        public bool isValid;
    }

#if UNITY_INCLUDE_TESTS
    internal interface IRPCBatchBackend
    {
        MTUExceededBehaviour mtuExceededBehaviour { get; }
        int GetMTU(PlayerID player, Channel channel, bool asServer);
        void Send(PlayerID player, RPCBatchPacket data, Channel channel, MTUExceededBehaviour? mtuOverride = null);

        void Send(PlayerID player, ImmediateRPCBatchPacket data, Channel channel,
            MTUExceededBehaviour? mtuOverride = null)
        {
        }

        void Subscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback);
        void Unsubscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback);
    }
#endif

    public readonly struct ObserverFilter
    {
        public readonly PlayerID skipA;
        public readonly PlayerID skipB;
        public readonly PlayerID only;
        public readonly bool hasSkipA;
        public readonly bool hasSkipB;
        public readonly bool hasOnly;

        public ObserverFilter(PlayerID skipA, bool hasSkipA, PlayerID skipB, bool hasSkipB)
        {
            this.skipA = skipA;
            this.hasSkipA = hasSkipA;
            this.skipB = skipB;
            this.hasSkipB = hasSkipB;
            only = default;
            hasOnly = false;
        }

        public ObserverFilter(PlayerID skipA, bool hasSkipA, PlayerID skipB, bool hasSkipB, PlayerID only, bool hasOnly)
        {
            this.skipA = skipA;
            this.hasSkipA = hasSkipA;
            this.skipB = skipB;
            this.hasSkipB = hasSkipB;
            this.only = only;
            this.hasOnly = hasOnly;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldSkip(PlayerID player)
        {
            if (hasOnly && player != only) return true;
            if (hasSkipA && player == skipA) return true;
            if (hasSkipB && player == skipB) return true;
            return false;
        }
    }

    public sealed class RPCBatch : IDisposable
    {
        public const int MAX_HEADER_SIZE = RPCBatchPacket.HEADER + UnionRPCHeader.MAX_SIZE;

        static readonly ProfilerMarker _flushMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.Flush");
        static readonly ProfilerMarker _flushChannelMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.FlushChannel");
        static readonly ProfilerMarker _queueSingleMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.Queue");
        static readonly ProfilerMarker _queueMultiMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.QueueMulti");
        static readonly ProfilerMarker _batchReceivedMarker = new ProfilerMarker($"RPCBatch<{nameof(UnionRPCHeader)}>.OnBatchReceived");

        private readonly PlayersManager _playersManager;
#if UNITY_INCLUDE_TESTS
        private readonly IRPCBatchBackend _backend;
#endif
        private PendingBatchedData[] _batches = new PendingBatchedData[128];
        private readonly BatchIndexMap _batchIndexMap;
        private int _batchCount = 0;
        private ulong _nextStateVersion;
        private CachedRPCEntry _entryCacheA;
        private CachedRPCEntry _entryCacheB;

        public delegate void RPCReceivedDelegate(PlayerID sender, UnionRPCHeader header, BitData content, bool asServer);
        private readonly RPCReceivedDelegate _onRPCReceived;

        private readonly bool _subscribed;
        private readonly bool _sendAsImmediate;

        /// <summary>
        /// Raised when immediate content reaches the transport outside a flush
        /// (oversized solo entry), so the owner can still force a send this frame.
        /// </summary>
        internal Action onImmediateAutoFlush;

        public RPCBatch(PlayersManager playersManager, RPCReceivedDelegate callback, bool subscribeToReceives = true,
            bool sendAsImmediate = false)
        {
            _playersManager = playersManager;
            _onRPCReceived = callback;
            _batchIndexMap = new BatchIndexMap(128);
            _subscribed = subscribeToReceives;
            _sendAsImmediate = sendAsImmediate;
            if (subscribeToReceives)
                _playersManager.Subscribe<RPCBatchPacket>(OnBatchReceived);
        }

#if UNITY_INCLUDE_TESTS
        internal RPCBatch(IRPCBatchBackend backend, RPCReceivedDelegate callback, bool sendAsImmediate = false)
        {
            _backend = backend;
            _onRPCReceived = callback;
            _batchIndexMap = new BatchIndexMap(128);
            _subscribed = true;
            _sendAsImmediate = sendAsImmediate;
            _backend.Subscribe(OnBatchReceived);
        }
#endif

        public bool hasPending
        {
            get
            {
                for (int i = 0; i < _batchCount; i++)
                {
                    if (_batches[i].batchCount > 0)
                        return true;
                }

                return false;
            }
        }

        public void Dispose()
        {
#if UNITY_INCLUDE_TESTS
            if (_playersManager != null)
            {
                if (_subscribed)
                    _playersManager.Unsubscribe<RPCBatchPacket>(OnBatchReceived);
            }
            else
                _backend.Unsubscribe(OnBatchReceived);
#else
            if (_subscribed)
                _playersManager.Unsubscribe<RPCBatchPacket>(OnBatchReceived);
#endif

            Clear();
            _entryCacheA.data?.Dispose();
            _entryCacheB.data?.Dispose();
            _batchIndexMap.Dispose();
        }

        public void Flush()
        {
            using (_flushMarker.Auto())
            {
                for (int i = 0; i < _batchCount; i++)
                {
                    ref var batch = ref _batches[i];
                    SendBatch(ref batch);
                    batch.batchedData.Dispose();
                }

                _batchCount = 0;
                _batchIndexMap.Clear();
                _nextStateVersion = 0;
            }
        }

        public void FlushChannel(Channel channel)
        {
            using (_flushChannelMarker.Auto())
            {
                int writeIdx = 0;

                for (int i = 0; i < _batchCount; i++)
                {
                    ref var batch = ref _batches[i];

                    if (batch.key.channel == channel)
                    {
                        SendBatch(ref batch);
                        batch.batchedData.Dispose();
                        _batchIndexMap.Remove(batch.key.playerId.id.value, batch.key.channel);
                    }
                    else
                    {
                        // Keep this batch, shift it down if needed
                        if (writeIdx != i)
                        {
                            _batches[writeIdx] = _batches[i];
                            _batchIndexMap.Set(batch.key.playerId.id.value, batch.key.channel, writeIdx);
                        }
                        writeIdx++;
                    }
                }

                _batchCount = writeIdx;
            }
        }

        private void SendBatch(ref PendingBatchedData batch)
        {
            if (batch.batchCount == 0)
                return;

            Send(batch.key.playerId, batch.batchCount, new BitData(batch.batchedData), batch.key.channel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Send(PlayerID player, Size count, BitData payload, Channel channel,
            MTUExceededBehaviour? mtuOverride = null)
        {
            if (_sendAsImmediate)
            {
                var immediate = new ImmediateRPCBatchPacket
                {
                    count = count,
                    data = payload
                };

#if UNITY_INCLUDE_TESTS
                if (_playersManager != null)
                    _playersManager.Send(player, immediate, channel, mtuOverride);
                else
                    _backend.Send(player, immediate, channel, mtuOverride);
#else
                _playersManager.Send(player, immediate, channel, mtuOverride);
#endif
                return;
            }

            var data = new RPCBatchPacket
            {
                count = count,
                data = payload
            };

#if UNITY_INCLUDE_TESTS
            if (_playersManager != null)
                _playersManager.Send(player, data, channel, mtuOverride);
            else
                _backend.Send(player, data, channel, mtuOverride);
#else
            _playersManager.Send(player, data, channel, mtuOverride);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private MTUExceededBehaviour ResolveMTUBehaviour(Channel channel, MTUBehaviour mtuOverride)
        {
            // sequencing is a channel-wide property; per-message overrides only apply to Unreliable
            if (channel != Channel.UnreliableSequenced &&
                mtuOverride != MTUBehaviour.NetworkManager)
                return mtuOverride.Resolve(default);

#if UNITY_INCLUDE_TESTS
            return _playersManager != null
                ? _playersManager.mtuExceededBehaviour
                : _backend.mtuExceededBehaviour;
#else
            return _playersManager.mtuExceededBehaviour;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsUnreliable(Channel channel)
        {
            return channel is Channel.Unreliable or Channel.UnreliableSequenced;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetMTU(PlayerID player, Channel channel, bool asServer)
        {
#if UNITY_INCLUDE_TESTS
            return _playersManager != null
                ? _playersManager.GetMTU(player, channel, asServer)
                : _backend.GetMTU(player, channel, asServer);
#else
            return _playersManager.GetMTU(player, channel, asServer);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetBatchIndex(PlayerID playerId, Channel channel)
        {
            if (_batchIndexMap.TryGetValue(playerId.id.value, channel, out int idx))
                return idx;
            return CreateBatch(new BatchKey { playerId = playerId, channel = channel });
        }

        private int CreateBatch(BatchKey key)
        {
            // Resize if needed
            if (_batchCount >= _batches.Length)
                Array.Resize(ref _batches, _batches.Length * 2);

            int c = _batchCount;
            _batches[c] = new PendingBatchedData
            {
                key = key,
                batchedData = BitPackerPool.Get(),
                cachedMTU = GetMTU(key.playerId, key.channel, key.playerId != PlayerID.Server)
            };
            _batchIndexMap.Set(key.playerId.id.value, key.channel, c);
            _batchCount++;
            return c;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ResetEntryCache()
        {
            _entryCacheA.isValid = false;
            _entryCacheB.isValid = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureEntryCache()
        {
            _entryCacheA.data ??= BitPackerPool.Get();
            _entryCacheB.data ??= BitPackerPool.Get();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong NextStateVersion()
        {
            ulong version = ++_nextStateVersion;
            if (version != 0)
                return version;

            // Version zero is reserved for a new/default batch state. If the counter wraps,
            // give every live batch a unique version before issuing the next shared version.
            // This prevents a partially filtered recipient from aliasing a state last used
            // 2^64 queue operations ago.
            for (int i = 0; i < _batchCount; i++)
                _batches[i].stateVersion = ++_nextStateVersion;

            return ++_nextStateVersion;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCacheHit(in CachedRPCEntry cache, ulong previousStateVersion,
            in UnionRPCHeader previousHeader, Size previousDataLen)
        {
            if (!cache.isValid)
                return false;

            if (previousStateVersion != 0 && cache.previousStateVersion == previousStateVersion)
                return true;

            return cache.previousDataLen.value == previousDataLen.value &&
                   cache.previousHeader.Equals(previousHeader);
        }

        private static unsafe void BuildCachedEntry(ref CachedRPCEntry cache, in UnionRPCHeader previousHeader,
            Size previousDataLen, ulong previousStateVersion, in UnionRPCHeader header, in BitData content,
            Size contentLen)
        {
            var packer = cache.data;
            packer.ResetPositionAndMode(false);
            NativeDeltaPacker<UnionRPCHeader>.WriteFunc(packer, previousHeader, header);
            DeltaPackInteger.WriteIndex(packer, previousDataLen, contentLen);

            if (contentLen.value > 0)
                packer.WriteBitDataWithoutConsumingIt(content);

            cache.previousHeader = previousHeader;
            cache.previousDataLen = previousDataLen;
            cache.previousStateVersion = previousStateVersion;
            cache.bitLength = packer.positionInBits;
            cache.isValid = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void GetCachedEntry(in UnionRPCHeader previousHeader, Size previousDataLen,
            ulong previousStateVersion, in UnionRPCHeader header, in BitData content, Size contentLen,
            out BitPacker data, out int bitLength)
        {
            if (IsCacheHit(_entryCacheA, previousStateVersion, previousHeader, previousDataLen))
            {
                data = _entryCacheA.data;
                bitLength = _entryCacheA.bitLength;
                return;
            }

            if (IsCacheHit(_entryCacheB, previousStateVersion, previousHeader, previousDataLen))
            {
                data = _entryCacheB.data;
                bitLength = _entryCacheB.bitLength;
                return;
            }

            ref var cache = ref (_entryCacheA.isValid ? ref _entryCacheB : ref _entryCacheA);
            BuildCachedEntry(ref cache, previousHeader, previousDataLen, previousStateVersion, header, content,
                contentLen);
            data = cache.data;
            bitLength = cache.bitLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void QueueCached(PlayerID target, in UnionRPCHeader header, in BitData content, Size contentLen,
            Channel channel, ulong stateVersion, MTUBehaviour mtuOverride)
        {
            var batchIdx = GetBatchIndex(target, channel);
            ref var batch = ref _batches[batchIdx];

            GetCachedEntry(batch.lastHeader, batch.lastDataLen, batch.stateVersion, header, content, contentLen,
                out var entryData, out int entryBitLength);

            if (batch.batchCount > 0)
            {
                int bytesAfterEntry = (batch.batchedData.positionInBits + entryBitLength + 7) >> 3;
                if (bytesAfterEntry + 10 >= batch.cachedMTU)
                {
                    SendBatch(ref batch);
                    batch.batchCount = 0;
                    batch.batchedData.ResetPositionAndMode(false);
                    GetCachedEntry(default, default, 0, header, content, contentLen, out entryData,
                        out entryBitLength);

                    if (_sendAsImmediate)
                        onImmediateAutoFlush?.Invoke();
                }
            }

            if (batch.batchCount == 0 && IsUnreliable(channel) &&
                ((entryBitLength + 7) >> 3) + 10 >= batch.cachedMTU)
            {
                switch (ResolveMTUBehaviour(channel, mtuOverride))
                {
                    case MTUExceededBehaviour.UpgradeToReliable:
                        ResetBatchEncoderState(ref batch);
                        PurrLogger.LogWarning(
                            $"RPC exceeds MTU ({(entryBitLength + 7) >> 3} bytes, MTU {batch.cachedMTU}). " +
                            $"Upgrading {channel} to {Channel.ReliableOrdered}.");
                        QueueDirect(target, header, content, Channel.ReliableOrdered, mtuOverride);
                        return;
                    case MTUExceededBehaviour.Drop:
                        ResetBatchEncoderState(ref batch);
                        PurrLogger.LogError(
                            $"RPC exceeds MTU ({(entryBitLength + 7) >> 3} bytes, MTU {batch.cachedMTU}). " +
                            $"Dropping {channel} packet.");
                        return;
                    default:
                        batch.batchedData.WriteBitsWithoutConsumingItUnchecked(entryData, entryBitLength);
                        SendSoloEntry(ref batch, channel);
                        return;
                }
            }

            batch.batchedData.WriteBitsWithoutConsumingItUnchecked(entryData, entryBitLength);
            ++batch.batchCount;
            batch.lastHeader = header;
            batch.lastDataLen = contentLen;
            batch.stateVersion = stateVersion;
        }

        private void SendSoloEntry(ref PendingBatchedData batch, Channel channel)
        {
            Send(batch.key.playerId, 1, new BitData(batch.batchedData), channel, MTUExceededBehaviour.Fragment);
            ResetBatchEncoderState(ref batch);

            if (_sendAsImmediate)
                onImmediateAutoFlush?.Invoke();
        }

        private static void ResetBatchEncoderState(ref PendingBatchedData batch)
        {
            batch.batchedData.ResetPositionAndMode(false);
            batch.lastHeader = default;
            batch.lastDataLen = default;
            batch.stateVersion = 0;
            batch.batchCount = 0;
        }

        private void OnBatchReceived(PlayerID player, RPCBatchPacket data, bool asServer)
        {
            ProcessReceivedBatch(player, data.count, data.data, asServer);
        }

        internal unsafe void ProcessReceivedBatch(PlayerID player, Size count, BitData data, bool asServer)
        {
            _batchReceivedMarker.Begin();

            UnionRPCHeader lastHeader = default;
            Size lastLen = default;

            var packer = data.packer;
            using (data.AutoScope())
            {
                for (var i = 0; i < count.value; ++i)
                {
                    NativeDeltaPacker<UnionRPCHeader>.ReadFunc(packer, lastHeader, ref lastHeader);
                    DeltaPackInteger.ReadIndex(packer, lastLen, ref lastLen);

                    int pos = packer.positionInBits;
                    int len = (int)lastLen.value;

                    var bitData = new BitData(packer, pos, len);
                    _onRPCReceived.Invoke(player, lastHeader, bitData, asServer);

                    packer.SetBitPosition(pos + len);
                }
            }

            _batchReceivedMarker.End();
        }

        public unsafe void Queue(DisposableList<PlayerID> targets, UnionRPCHeader header, BitData content, Channel channel,
            MTUBehaviour mtuOverride = MTUBehaviour.NetworkManager)
        {
            ValidateChannel(channel);
            _queueMultiMarker.Begin();

            if (targets.Count < 3)
            {
                for (var i = targets.Count - 1; i >= 0; i--)
                    QueueDirect(targets[i], header, content, channel, mtuOverride);

                _queueMultiMarker.End();
                return;
            }

            EnsureEntryCache();
            ResetEntryCache();
            ulong stateVersion = NextStateVersion();
            var contentLen = content.bitLength;

            for (var i = targets.Count - 1; i >= 0; i--)
                QueueCached(targets[i], header, content, contentLen, channel, stateVersion, mtuOverride);

            _queueMultiMarker.End();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldQueueDirect(IReadOnlyList<PlayerID> targets, in ObserverFilter filter,
            bool hasFilter)
        {
            if (targets.Count < 3)
                return true;

            if (!hasFilter || targets.Count > 4)
                return false;

            int includedCount = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!filter.ShouldSkip(targets[i]))
                    includedCount++;
            }
            return includedCount < 3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateChannel(Channel channel)
        {
            if ((uint)channel > (uint)Channel.Unreliable)
                throw new ArgumentOutOfRangeException(nameof(channel), channel, null);
        }

        public unsafe void Queue(IReadOnlyList<PlayerID> targets, UnionRPCHeader header, BitData content, Channel channel,
            ObserverFilter filter = default,
            MTUBehaviour mtuOverride = MTUBehaviour.NetworkManager)
        {
            ValidateChannel(channel);
            _queueMultiMarker.Begin();

            bool hasFilter = filter.hasSkipA || filter.hasSkipB || filter.hasOnly;

            if (ShouldQueueDirect(targets, filter, hasFilter))
            {
                for (var i = targets.Count - 1; i >= 0; i--)
                {
                    var target = targets[i];
                    if (!hasFilter || !filter.ShouldSkip(target))
                        QueueDirect(target, header, content, channel, mtuOverride);
                }

                _queueMultiMarker.End();
                return;
            }

            EnsureEntryCache();
            ResetEntryCache();
            ulong stateVersion = NextStateVersion();
            var contentLen = content.bitLength;

            for (var i = targets.Count - 1; i >= 0; i--)
            {
                var target = targets[i];
                if (hasFilter && filter.ShouldSkip(target)) continue;
                QueueCached(target, header, content, contentLen, channel, stateVersion, mtuOverride);
            }

            _queueMultiMarker.End();
        }

        private unsafe void QueueDirect(PlayerID target, in UnionRPCHeader header, in BitData content,
            Channel channel, MTUBehaviour mtuOverride)
        {
            var batchIdx = GetBatchIndex(target, channel);
            ref var batch = ref _batches[batchIdx];

            int before = batch.batchedData.positionInBits;
            int contentByteLen = content.byteLength;
            var contentLen = content.bitLength;

            NativeDeltaPacker<UnionRPCHeader>.WriteFunc(batch.batchedData, batch.lastHeader, header);
            DeltaPackInteger.WriteIndex(batch.batchedData, batch.lastDataLen, contentLen);

            // do some MTU checks past 1 batch
            if (batch.batchCount > 0)
            {
                int bytesAfterHeaderLen = batch.batchedData.positionInBytes + contentByteLen;
                if (bytesAfterHeaderLen + 10 >= batch.cachedMTU)
                {
                    // undo the last write
                    batch.batchedData.SetBitPosition(before);
                    SendBatch(ref batch);
                    batch.batchCount = 0;
                    batch.batchedData.ResetPositionAndMode(false);

                    if (_sendAsImmediate)
                        onImmediateAutoFlush?.Invoke();

                    // redo the last write
                    NativeDeltaPacker<UnionRPCHeader>.WriteFunc(batch.batchedData, default, header);
                    DeltaPackInteger.WriteIndex(batch.batchedData, default, contentLen);
                }
            }

            // the entry alone exceeds the MTU; the packer only holds this entry's header at this point
            if (batch.batchCount == 0 && IsUnreliable(channel) &&
                batch.batchedData.positionInBytes + contentByteLen + 10 >= batch.cachedMTU)
            {
                int size = batch.batchedData.positionInBytes + contentByteLen;
                switch (ResolveMTUBehaviour(channel, mtuOverride))
                {
                    case MTUExceededBehaviour.UpgradeToReliable:
                        ResetBatchEncoderState(ref batch);
                        PurrLogger.LogWarning(
                            $"RPC exceeds MTU ({size} bytes, MTU {batch.cachedMTU}). " +
                            $"Upgrading {channel} to {Channel.ReliableOrdered}.");
                        QueueDirect(target, header, content, Channel.ReliableOrdered, mtuOverride);
                        return;
                    case MTUExceededBehaviour.Drop:
                        ResetBatchEncoderState(ref batch);
                        PurrLogger.LogError(
                            $"RPC exceeds MTU ({size} bytes, MTU {batch.cachedMTU}). " +
                            $"Dropping {channel} packet.");
                        return;
                    default:
                        if (contentLen.value > 0)
                            batch.batchedData.WriteBitDataWithoutConsumingIt(content);
                        SendSoloEntry(ref batch, channel);
                        return;
                }
            }

            ++batch.batchCount;
            batch.lastHeader = header;
            batch.lastDataLen = contentLen;
            batch.stateVersion = 0;

            if (contentLen.value > 0)
                batch.batchedData.WriteBitDataWithoutConsumingIt(content);
        }

        public unsafe void Queue(PlayerID target, UnionRPCHeader header, BitData content, Channel channel,
            MTUBehaviour mtuOverride = MTUBehaviour.NetworkManager)
        {
            ValidateChannel(channel);
            _queueSingleMarker.Begin();

            QueueDirect(target, header, content, channel, mtuOverride);
            _queueSingleMarker.End();
        }

        public void Clear()
        {
            for (int i = 0; i < _batchCount; i++)
                _batches[i].batchedData.Dispose();

            _batchCount = 0;
            _batchIndexMap.Clear();
            _nextStateVersion = 0;
        }
    }
}
