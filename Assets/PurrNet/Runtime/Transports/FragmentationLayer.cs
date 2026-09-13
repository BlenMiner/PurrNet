using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Pooling;

namespace PurrNet.Transports
{
    /// <summary>
    /// Allocation-free-on-the-hot-path message fragmentation and bounded reassembly.
    /// Fragment loss is never recovered here: an incomplete message expires as a unit.
    ///
    /// Header format:
    ///   Unfragmented: [1 byte: 0x00] [payload]
    ///   Sequenced:    [1 byte: 0x02] [4 bytes: messageId] [payload]
    ///   Fragmented:   [1 byte: 0x01] [4 bytes: messageId] [4 bytes: totalLength]
    ///                 [2 bytes: fragmentStride] [1 byte: fragmentIndex]
    ///                 [1 byte: totalFragments] [payload]
    /// </summary>
    public enum FragmentDropReason : byte
    {
        /// <summary>The message did not complete within the reassembly timeout.</summary>
        Expired = 0,

        /// <summary>The message was evicted to make room under sender or global reassembly pressure.</summary>
        Evicted = 1,

        /// <summary>The sender exceeded its reassembly budget; the message was rejected on arrival.</summary>
        BudgetExceeded = 2,

        /// <summary>A newer message on the same sequenced stream superseded this incomplete one.</summary>
        SequencedStale = 3
    }

    public readonly struct FragmentDropInfo
    {
        public readonly int senderId;
        public readonly FragmentDropReason reason;
        public readonly int totalLength;

        /// <summary>True when the first fragment was received and <see cref="firstWord"/> holds the message's leading 4 bytes.</summary>
        public readonly bool hasFirstWord;
        public readonly uint firstWord;

        public FragmentDropInfo(int senderId, FragmentDropReason reason, int totalLength, bool hasFirstWord, uint firstWord)
        {
            this.senderId = senderId;
            this.reason = reason;
            this.totalLength = totalLength;
            this.hasFirstWord = hasFirstWord;
            this.firstWord = firstWord;
        }
    }

    public sealed class FragmentationLayer : IDisposable
    {
        public delegate void FragmentCallback<TState>(ByteData fragment, TState state);

        /// <summary>
        /// Raised when a fragmented message is discarded without completing. Not raised for
        /// sender removal or <see cref="Reset"/>.
        /// </summary>
        public Action<FragmentDropInfo> onMessageDropped;

        /// <summary>Header overhead for unfragmented messages.</summary>
        public const int UNFRAGMENTED_OVERHEAD = 1;

        /// <summary>Header overhead for sequenced, unfragmented messages.</summary>
        public const int SEQUENCED_OVERHEAD = 5;

        /// <summary>Header overhead per fragmented message packet.</summary>
        public const int FRAGMENT_OVERHEAD = 13;

        public const int MAX_FRAGMENTS = byte.MaxValue;
        public const int MAX_MESSAGE_SIZE = 1024 * 1024;

        // Reassembly is exposed to the network and must be bounded independently of expiry.
        const int MAX_PENDING_MESSAGES = 128;
        const int MAX_PENDING_MESSAGES_PER_SENDER = 16;
        const int MAX_PENDING_BYTES = 8 * 1024 * 1024;
        const int MAX_PENDING_BYTES_PER_SENDER = 2 * 1024 * 1024;

        const int MAX_PRESSURE_EVICTIONS_PER_SENDER = 512;
        const int MAX_PRESSURE_EVICTIONS = 8192;

        const byte FLAG_UNFRAGMENTED = 0;
        const byte FLAG_FRAGMENTED = 1;
        const byte FLAG_SEQUENCED = 2;

        static readonly FragmentCallback<Action<ByteData>> _invokeAction = InvokeAction;

        uint _nextMessageId;
        int _pendingBytes;
        int _nextCleanupAt;
        bool _cleanupScheduled;
        bool _buffersTouched;
        bool _hasLastRejectedKey;
        ReassemblyKey _lastRejectedKey;

        readonly Dictionary<ReassemblyKey, ReassemblyEntry> _pending = new(MAX_PENDING_MESSAGES);
        readonly Dictionary<int, SenderBudget> _senderBudgets = new(MAX_PENDING_MESSAGES);
        readonly Dictionary<StreamKey, uint> _latestSequencedMessages = new(MAX_PENDING_MESSAGES);
        readonly Dictionary<ReassemblyKey, int> _unreliableEvictions = new(MAX_PENDING_MESSAGES);
        readonly Dictionary<int, int> _senderEvictionCounts = new(MAX_PENDING_MESSAGES);
        readonly List<ReassemblyKey> _removeBuffer = new(MAX_PENDING_MESSAGES);
        readonly List<StreamKey> _streamRemoveBuffer = new(MAX_PENDING_MESSAGES);

        DisposableArray<byte> _sendBuffer;
        DisposableArray<byte> _completedBuffer;

        readonly struct ReassemblyKey : IEquatable<ReassemblyKey>
        {
            public readonly int senderId;
            public readonly byte streamId;
            public readonly uint messageId;

            public ReassemblyKey(int senderId, byte streamId, uint messageId)
            {
                this.senderId = senderId;
                this.streamId = streamId;
                this.messageId = messageId;
            }

            public bool Equals(ReassemblyKey other) => senderId == other.senderId &&
                                                        streamId == other.streamId &&
                                                        messageId == other.messageId;

            public override bool Equals(object obj) => obj is ReassemblyKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = senderId;
                    hash = (hash * 397) ^ streamId;
                    return (hash * 397) ^ (int)messageId;
                }
            }
        }

        readonly struct StreamKey : IEquatable<StreamKey>
        {
            public readonly int senderId;
            public readonly byte streamId;

            public StreamKey(int senderId, byte streamId)
            {
                this.senderId = senderId;
                this.streamId = streamId;
            }

            public bool Equals(StreamKey other) => senderId == other.senderId && streamId == other.streamId;
            public override bool Equals(object obj) => obj is StreamKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (senderId * 397) ^ streamId;
                }
            }
        }

        struct SenderBudget
        {
            public int messageCount;
            public int byteCount;
        }

        struct ReassemblyEntry
        {
            public byte totalFragments;
            public byte receivedCount;
            public ushort fragmentStride;
            public int totalLength;
            public int createdAtTick;
            public bool sequenced;
            public DisposableArray<byte> buffer;

            // 255 flags without allocating a second array for every pending message.
            ulong _received0;
            ulong _received1;
            ulong _received2;
            ulong _received3;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryMarkReceived(byte index)
            {
                int word = index >> 6;
                ulong bit = 1UL << (index & 63);

                switch (word)
                {
                    case 0:
                        if ((_received0 & bit) != 0) return false;
                        _received0 |= bit;
                        return true;
                    case 1:
                        if ((_received1 & bit) != 0) return false;
                        _received1 |= bit;
                        return true;
                    case 2:
                        if ((_received2 & bit) != 0) return false;
                        _received2 |= bit;
                        return true;
                    default:
                        if ((_received3 & bit) != 0) return false;
                        _received3 |= bit;
                        return true;
                }
            }

            public bool receivedFirstFragment => (_received0 & 1UL) != 0;
        }

        internal int pendingCount => _pending.Count;
        internal int pendingBytes => _pendingBytes;

        /// <summary>Returns the largest message accepted for the supplied packet size.</summary>
        public static int GetMaxMessageSize(int mtu, int reservedPrefix = 0)
        {
            int maxPayload = mtu - reservedPrefix - FRAGMENT_OVERHEAD;
            if (maxPayload <= 0)
                return 0;

            maxPayload = Math.Min(maxPayload, ushort.MaxValue);
            long result = (long)maxPayload * MAX_FRAGMENTS;
            return (int)Math.Min(result, MAX_MESSAGE_SIZE);
        }

        /// <summary>Returns the largest sequenced message accepted for the supplied packet size.</summary>
        public static int GetMaxSequencedMessageSize(int mtu, int reservedPrefix = 0)
        {
            int singlePacketPayload = Math.Max(0, mtu - reservedPrefix - SEQUENCED_OVERHEAD);
            return Math.Max(singlePacketPayload, GetMaxMessageSize(mtu, reservedPrefix));
        }

        /// <summary>
        /// Sends a message with no prefix reserved for the caller.
        /// </summary>
        public void Send(ByteData data, int mtu, Action<ByteData> sendFragment)
        {
            if (sendFragment == null)
                throw new ArgumentNullException(nameof(sendFragment));

            Send(data, mtu, 0, sendFragment, _invokeAction);
        }

        /// <summary>
        /// Sends a message on a sequenced stream. Even a single-packet message carries a message id
        /// so it can invalidate older incomplete fragmented messages on the same stream.
        /// </summary>
        public void SendSequenced(ByteData data, int mtu, Action<ByteData> sendFragment)
        {
            if (sendFragment == null)
                throw new ArgumentNullException(nameof(sendFragment));

            Send(data, mtu, 0, sendFragment, _invokeAction, true);
        }

        /// <summary>
        /// Splits a message into packets no larger than <paramref name="mtu"/>. The caller may
        /// reserve bytes at the front of every packet for its own framing and must overwrite all
        /// reserved bytes in the callback. Buffers passed to the callback are reused and are only
        /// valid for the duration of the callback.
        /// </summary>
        public void Send<TState>(ByteData data, int mtu, int reservedPrefix, TState state,
            FragmentCallback<TState> sendFragment)
        {
            Send(data, mtu, reservedPrefix, state, sendFragment, false);
        }

        /// <summary>
        /// Sends on a sequenced stream while reserving a caller-owned prefix. Single-packet and
        /// fragmented messages share the same monotonically increasing message-id space.
        /// </summary>
        public void SendSequenced<TState>(ByteData data, int mtu, int reservedPrefix, TState state,
            FragmentCallback<TState> sendFragment)
        {
            Send(data, mtu, reservedPrefix, state, sendFragment, true);
        }

        void Send<TState>(ByteData data, int mtu, int reservedPrefix, TState state,
            FragmentCallback<TState> sendFragment, bool sequenced)
        {
            if (sendFragment == null)
                throw new ArgumentNullException(nameof(sendFragment));
            if (reservedPrefix < 0)
                throw new ArgumentOutOfRangeException(nameof(reservedPrefix));

            _buffersTouched = true;

            int singlePacketOverhead = sequenced ? SEQUENCED_OVERHEAD : UNFRAGMENTED_OVERHEAD;
            if (data.length + reservedPrefix + singlePacketOverhead <= mtu)
            {
                int packetLength = reservedPrefix + singlePacketOverhead + data.length;
                EnsureBuffer(ref _sendBuffer, packetLength);
                _sendBuffer.array[reservedPrefix] = sequenced ? FLAG_SEQUENCED : FLAG_UNFRAGMENTED;
                if (sequenced)
                    WriteUInt32(_sendBuffer.array, reservedPrefix + 1, NextMessageId());
                Buffer.BlockCopy(data.data, data.offset, _sendBuffer.array,
                    reservedPrefix + singlePacketOverhead, data.length);
                sendFragment(new ByteData(_sendBuffer.array, 0, packetLength), state);
                return;
            }

            int availablePayload = mtu - reservedPrefix - FRAGMENT_OVERHEAD;
            if (availablePayload <= 0)
                throw new ArgumentException(
                    $"MTU {mtu} is too small for {reservedPrefix} reserved bytes and the " +
                    $"{FRAGMENT_OVERHEAD}-byte fragmentation header.", nameof(mtu));

            int fragmentStride = Math.Min(availablePayload, ushort.MaxValue);
            int totalFragments = (data.length + fragmentStride - 1) / fragmentStride;

            if (data.length > MAX_MESSAGE_SIZE || totalFragments > MAX_FRAGMENTS)
                throw new ArgumentException(
                    $"Data ({data.length} bytes) exceeds max fragmentable size for MTU {mtu}. " +
                    $"Max: {GetMaxMessageSize(mtu, reservedPrefix)} bytes.", nameof(data));

            uint messageId = NextMessageId();

            for (int i = 0; i < totalFragments; i++)
            {
                int payloadOffset = i * fragmentStride;
                int payloadLength = Math.Min(fragmentStride, data.length - payloadOffset);
                int packetLength = reservedPrefix + FRAGMENT_OVERHEAD + payloadLength;

                EnsureBuffer(ref _sendBuffer, packetLength);
                byte[] packet = _sendBuffer.array;
                int header = reservedPrefix;
                packet[header] = FLAG_FRAGMENTED;
                WriteUInt32(packet, header + 1, messageId);
                WriteInt32(packet, header + 5, data.length);
                WriteUInt16(packet, header + 9, (ushort)fragmentStride);
                packet[header + 11] = (byte)i;
                packet[header + 12] = (byte)totalFragments;

                Buffer.BlockCopy(data.data, data.offset + payloadOffset, packet,
                    reservedPrefix + FRAGMENT_OVERHEAD, payloadLength);
                sendFragment(new ByteData(packet, 0, packetLength), state);
            }
        }

        /// <summary>Receives on a single unordered stream.</summary>
        public bool Receive(ByteData data, out ByteData assembled)
        {
            return Receive(0, 0, false, data, out assembled);
        }

        /// <summary>
        /// Processes a packet for a sender and logical stream. Sequenced streams discard older
        /// incomplete messages as soon as any newer message is observed. Reassembled data remains
        /// valid until the next completed reassembly, the next idle buffer release in
        /// <see cref="CleanupStaleIfDue"/>, <see cref="Reset"/>, or disposal.
        /// </summary>
        public bool Receive(int senderId, byte streamId, bool sequenced, ByteData data, out ByteData assembled)
        {
            assembled = default;

            if (data.length < UNFRAGMENTED_OVERHEAD)
                return false;

            int header = data.offset;
            byte flag = data.data[header];

            if (flag == FLAG_UNFRAGMENTED)
            {
                assembled = new ByteData(data.data, header + UNFRAGMENTED_OVERHEAD,
                    data.length - UNFRAGMENTED_OVERHEAD);
                return true;
            }

            if (flag == FLAG_SEQUENCED)
            {
                if (!sequenced || data.length < SEQUENCED_OVERHEAD)
                    return false;

                uint completedMessageId = ReadUInt32(data.data, header + 1);
                if (!AcceptCompletedSequenced(senderId, streamId, completedMessageId))
                    return false;

                assembled = new ByteData(data.data, header + SEQUENCED_OVERHEAD,
                    data.length - SEQUENCED_OVERHEAD);
                return true;
            }

            if (flag != FLAG_FRAGMENTED || data.length < FRAGMENT_OVERHEAD)
                return false;

            uint messageId = ReadUInt32(data.data, header + 1);
            int totalLength = ReadInt32(data.data, header + 5);
            ushort fragmentStride = ReadUInt16(data.data, header + 9);
            byte fragmentIndex = data.data[header + 11];
            byte totalFragments = data.data[header + 12];
            int payloadLength = data.length - FRAGMENT_OVERHEAD;

            if (!ValidateFragment(totalLength, fragmentStride, fragmentIndex, totalFragments, payloadLength))
                return false;

            var key = new ReassemblyKey(senderId, streamId, messageId);
            bool hasEntry = _pending.TryGetValue(key, out var entry);

            if (!sequenced && !hasEntry && _unreliableEvictions.ContainsKey(key))
                return false;

            if (sequenced && !AcceptSequenced(key, hasEntry))
                return false;

            if (!hasEntry)
            {
                if (!TryReserve(senderId, totalLength, sequenced))
                {
                    ReportRejected(key, totalLength, fragmentIndex, payloadLength, data, header);
                    return false;
                }

                entry = new ReassemblyEntry
                {
                    totalFragments = totalFragments,
                    fragmentStride = fragmentStride,
                    totalLength = totalLength,
                    createdAtTick = Environment.TickCount,
                    sequenced = sequenced,
                    // Every range is validated and copied exactly once before completion.
                    buffer = DisposableArray<byte>.CreateUninitialized(totalLength)
                };
            }
            else if (entry.totalFragments != totalFragments ||
                     entry.fragmentStride != fragmentStride ||
                     entry.totalLength != totalLength)
            {
                return false;
            }

            if (!entry.TryMarkReceived(fragmentIndex))
                return false;

            int destinationOffset = fragmentIndex * fragmentStride;
            Buffer.BlockCopy(data.data, header + FRAGMENT_OVERHEAD, entry.buffer.array,
                destinationOffset, payloadLength);
            entry.receivedCount++;

            if (entry.receivedCount < entry.totalFragments)
            {
                _pending[key] = entry;
                return false;
            }

            _completedBuffer.Dispose();
            _completedBuffer = entry.buffer;
            _buffersTouched = true;
            entry.buffer = default;
            _pending.Remove(key);
            Release(senderId, totalLength);

            assembled = new ByteData(_completedBuffer.array, 0, totalLength);
            return true;
        }

        /// <summary>
        /// Runs stale cleanup at most once per interval and releases the internal send and
        /// reassembly buffers once they have been idle for a full interval.
        /// </summary>
        public void CleanupStaleIfDue(int maxAgeMs, int intervalMs)
        {
            if (_pending.Count == 0 && _unreliableEvictions.Count == 0 &&
                _sendBuffer.isDisposed && _completedBuffer.isDisposed)
                return;

            int now = Environment.TickCount;
            if (_cleanupScheduled && unchecked(now - _nextCleanupAt) < 0)
                return;

            _nextCleanupAt = unchecked(now + Math.Max(1, intervalMs));
            _cleanupScheduled = true;

            if (_pending.Count > 0 || _unreliableEvictions.Count > 0)
                CleanupStale(maxAgeMs, now);

            if (_buffersTouched)
            {
                _buffersTouched = false;
            }
            else
            {
                _sendBuffer.Dispose();
                _sendBuffer = default;
                _completedBuffer.Dispose();
                _completedBuffer = default;
            }
        }

        /// <summary>Removes incomplete messages at least <paramref name="maxAgeMs"/> old.</summary>
        public void CleanupStale(int maxAgeMs)
        {
            CleanupStale(maxAgeMs, Environment.TickCount);
        }

        public void RemoveSender(int senderId)
        {
            _removeBuffer.Clear();
            foreach (var pair in _pending)
            {
                if (pair.Key.senderId == senderId)
                    _removeBuffer.Add(pair.Key);
            }

            RemoveBufferedEntries(null);

            _streamRemoveBuffer.Clear();
            foreach (var pair in _latestSequencedMessages)
            {
                if (pair.Key.senderId == senderId)
                    _streamRemoveBuffer.Add(pair.Key);
            }

            for (int i = 0; i < _streamRemoveBuffer.Count; i++)
                _latestSequencedMessages.Remove(_streamRemoveBuffer[i]);

            _removeBuffer.Clear();
            foreach (var pair in _unreliableEvictions)
            {
                if (pair.Key.senderId == senderId)
                    _removeBuffer.Add(pair.Key);
            }
            for (int i = 0; i < _removeBuffer.Count; i++)
                _unreliableEvictions.Remove(_removeBuffer[i]);
            _senderEvictionCounts.Remove(senderId);
        }

        public void Reset()
        {
            foreach (var pair in _pending)
            {
                var entry = pair.Value;
                entry.buffer.Dispose();
            }

            _pending.Clear();
            _senderBudgets.Clear();
            _latestSequencedMessages.Clear();
            _unreliableEvictions.Clear();
            _senderEvictionCounts.Clear();
            _removeBuffer.Clear();
            _streamRemoveBuffer.Clear();
            _pendingBytes = 0;
            _nextCleanupAt = 0;
            _cleanupScheduled = false;
            _buffersTouched = false;
            _hasLastRejectedKey = false;
            _lastRejectedKey = default;

            _completedBuffer.Dispose();
            _completedBuffer = default;
            _sendBuffer.Dispose();
            _sendBuffer = default;
        }

        public void Dispose() => Reset();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        uint NextMessageId()
        {
            uint messageId = _nextMessageId;
            _nextMessageId = unchecked(messageId + 1);
            return messageId;
        }

        bool AcceptSequenced(ReassemblyKey key, bool hasEntry)
        {
            var stream = new StreamKey(key.senderId, key.streamId);
            if (!_latestSequencedMessages.TryGetValue(stream, out uint latest))
            {
                _latestSequencedMessages.Add(stream, key.messageId);
                return true;
            }

            int relative = unchecked((int)(key.messageId - latest));
            if (relative < 0)
                return false;

            if (relative == 0)
                return hasEntry;

            DiscardStream(stream);
            _latestSequencedMessages[stream] = key.messageId;
            return true;
        }

        bool AcceptCompletedSequenced(int senderId, byte streamId, uint messageId)
        {
            var stream = new StreamKey(senderId, streamId);
            if (!_latestSequencedMessages.TryGetValue(stream, out uint latest))
            {
                _latestSequencedMessages.Add(stream, messageId);
                return true;
            }

            int relative = unchecked((int)(messageId - latest));
            if (relative <= 0)
                return false;

            DiscardStream(stream);
            _latestSequencedMessages[stream] = messageId;
            return true;
        }

        void DiscardStream(StreamKey stream)
        {
            _removeBuffer.Clear();
            foreach (var pair in _pending)
            {
                if (pair.Key.senderId == stream.senderId && pair.Key.streamId == stream.streamId)
                    _removeBuffer.Add(pair.Key);
            }

            RemoveBufferedEntries(FragmentDropReason.SequencedStale);
        }

        void RememberPressureEviction(ReassemblyKey key)
        {
            _unreliableEvictions.Add(key, Environment.TickCount);
            _senderEvictionCounts.TryGetValue(key.senderId, out int count);
            _senderEvictionCounts[key.senderId] = count + 1;
        }

        void ForgetPressureEviction(ReassemblyKey key)
        {
            _unreliableEvictions.Remove(key);
            int count = _senderEvictionCounts[key.senderId] - 1;
            if (count == 0)
                _senderEvictionCounts.Remove(key.senderId);
            else
                _senderEvictionCounts[key.senderId] = count;
        }

        bool TryMakeSenderRoom(int senderId, int byteCount, SenderBudget budget)
        {
            Span<ReassemblyKey> evictions = stackalloc ReassemblyKey[MAX_PENDING_MESSAGES_PER_SENDER];
            int victimCount = 0;
            _senderEvictionCounts.TryGetValue(senderId, out int evictionCount);

            while (budget.messageCount >= MAX_PENDING_MESSAGES_PER_SENDER ||
                   byteCount > MAX_PENDING_BYTES_PER_SENDER - budget.byteCount)
            {
                if (evictionCount + victimCount >= MAX_PRESSURE_EVICTIONS_PER_SENDER ||
                    _unreliableEvictions.Count + victimCount >= MAX_PRESSURE_EVICTIONS ||
                    !TryFindOldest(senderId, out var key, evictions.Slice(0, victimCount)))
                    return false;

                evictions[victimCount++] = key;
                budget.messageCount--;
                budget.byteCount -= _pending[key].totalLength;
            }

            // Reserve history before callbacks can trigger nested admissions.
            for (int i = 0; i < victimCount; i++)
                RememberPressureEviction(evictions[i]);

            int evictedCount = 0;
            try
            {
                for (; evictedCount < victimCount; evictedCount++)
                    Evict(evictions[evictedCount]);
            }
            finally
            {
                for (int i = evictedCount; i < victimCount; i++)
                    ForgetPressureEviction(evictions[i]);
            }
            return true;
        }

        bool TryReserve(int senderId, int byteCount, bool sequenced)
        {
            if (byteCount > MAX_PENDING_BYTES_PER_SENDER)
                return false;
            _senderBudgets.TryGetValue(senderId, out var budget);
            while (budget.messageCount >= MAX_PENDING_MESSAGES_PER_SENDER ||
                byteCount > MAX_PENDING_BYTES_PER_SENDER - budget.byteCount)
            {
                if (sequenced || !TryMakeSenderRoom(senderId, byteCount, budget))
                    return false;
                _senderBudgets.TryGetValue(senderId, out budget);
            }

            // Global pressure evicts the oldest pending message instead of rejecting the new
            // one, so senders within their own budget can't be starved by everyone else.
            while (_pending.Count >= MAX_PENDING_MESSAGES || byteCount > MAX_PENDING_BYTES - _pendingBytes)
            {
                if (!TryFindOldest(null, out var key))
                    return false;
                Evict(key);
            }

            _senderBudgets.TryGetValue(senderId, out budget);
            budget.messageCount++;
            budget.byteCount += byteCount;
            _senderBudgets[senderId] = budget;
            _pendingBytes += byteCount;
            return true;
        }

        bool TryFindOldest(int? pressuredSender, out ReassemblyKey oldestKey,
            ReadOnlySpan<ReassemblyKey> skipped = default)
        {
            oldestKey = default;
            if (_pending.Count == 0)
                return false;

            int now = Environment.TickCount;
            int oldestAge = int.MinValue;
            bool found = false;

            foreach (var pair in _pending)
            {
                // A pending entry with a record is reserved by an outer admission.
                if (_unreliableEvictions.ContainsKey(pair.Key))
                    continue;
                if (pressuredSender.HasValue &&
                    (pair.Key.senderId != pressuredSender.Value || pair.Value.sequenced ||
                     ContainsKey(skipped, pair.Key)))
                    continue;
                int age = unchecked(now - pair.Value.createdAtTick);
                if (!found || age > oldestAge)
                {
                    oldestKey = pair.Key;
                    oldestAge = age;
                    found = true;
                }
            }

            return found;
        }

        static bool ContainsKey(ReadOnlySpan<ReassemblyKey> keys, ReassemblyKey key)
        {
            for (int i = 0; i < keys.Length; i++)
                if (keys[i].Equals(key))
                    return true;
            return false;
        }

        void Evict(ReassemblyKey key)
        {
            var entry = _pending[key];
            ReportDrop(key.senderId, FragmentDropReason.Evicted, in entry);
            entry.buffer.Dispose();
            _pending.Remove(key);
            Release(key.senderId, entry.totalLength);
        }

        void ReportDrop(int senderId, FragmentDropReason reason, in ReassemblyEntry entry)
        {
            var callback = onMessageDropped;
            if (callback == null)
                return;

            bool hasFirstWord = entry.receivedFirstFragment && entry.totalLength >= sizeof(uint) &&
                                entry.fragmentStride >= sizeof(uint) && !entry.buffer.isDisposed;
            uint firstWord = hasFirstWord ? ReadUInt32(entry.buffer.array, 0) : 0u;
            callback(new FragmentDropInfo(senderId, reason, entry.totalLength, hasFirstWord, firstWord));
        }

        void ReportRejected(ReassemblyKey key, int totalLength, byte fragmentIndex, int payloadLength,
            ByteData data, int header)
        {
            // Every fragment of a rejected message retries the reservation; report the message once.
            if (_hasLastRejectedKey && _lastRejectedKey.Equals(key))
                return;

            _hasLastRejectedKey = true;
            _lastRejectedKey = key;

            var callback = onMessageDropped;
            if (callback == null)
                return;

            bool hasFirstWord = fragmentIndex == 0 && payloadLength >= sizeof(uint);
            uint firstWord = hasFirstWord ? ReadUInt32(data.data, header + FRAGMENT_OVERHEAD) : 0u;
            callback(new FragmentDropInfo(key.senderId, FragmentDropReason.BudgetExceeded, totalLength,
                hasFirstWord, firstWord));
        }

        void Release(int senderId, int byteCount)
        {
            _pendingBytes -= byteCount;
            if (!_senderBudgets.TryGetValue(senderId, out var budget))
                return;

            budget.messageCount--;
            budget.byteCount -= byteCount;
            if (budget.messageCount <= 0)
                _senderBudgets.Remove(senderId);
            else
                _senderBudgets[senderId] = budget;
        }

        void CleanupStale(int maxAgeMs, int now)
        {
            _removeBuffer.Clear();
            foreach (var pair in _pending)
            {
                int elapsed = unchecked(now - pair.Value.createdAtTick);
                if (elapsed < 0 || elapsed >= maxAgeMs)
                    _removeBuffer.Add(pair.Key);
            }

            RemoveBufferedEntries(FragmentDropReason.Expired);

            _removeBuffer.Clear();
            foreach (var pair in _unreliableEvictions)
            {
                int elapsed = unchecked(now - pair.Value);
                if (elapsed < 0 || elapsed >= maxAgeMs)
                    _removeBuffer.Add(pair.Key);
            }
            for (int i = 0; i < _removeBuffer.Count; i++)
                ForgetPressureEviction(_removeBuffer[i]);
        }

        void RemoveBufferedEntries(FragmentDropReason? reason)
        {
            for (int i = 0; i < _removeBuffer.Count; i++)
            {
                var key = _removeBuffer[i];
                if (!_pending.TryGetValue(key, out var entry))
                    continue;

                if (reason.HasValue)
                    ReportDrop(key.senderId, reason.Value, in entry);

                entry.buffer.Dispose();
                _pending.Remove(key);
                Release(key.senderId, entry.totalLength);
            }
        }

        static bool ValidateFragment(int totalLength, ushort stride, byte index, byte count, int payloadLength)
        {
            if (totalLength <= 0 || totalLength > MAX_MESSAGE_SIZE || stride == 0 ||
                count == 0 || index >= count)
                return false;

            int expectedCount = (totalLength + stride - 1) / stride;
            if (expectedCount != count)
                return false;

            int offset = index * stride;
            int expectedPayload = Math.Min(stride, totalLength - offset);
            return expectedPayload > 0 && payloadLength == expectedPayload;
        }

        static void EnsureBuffer(ref DisposableArray<byte> buffer, int size)
        {
            if (!buffer.isDisposed && buffer.Count >= size)
                return;

            buffer.Dispose();
            buffer = DisposableArray<byte>.CreateUninitialized(size);
        }

        static void InvokeAction(ByteData data, Action<ByteData> callback) => callback(data);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint ReadUInt32(byte[] data, int offset)
        {
            return data[offset] |
                   ((uint)data[offset + 1] << 8) |
                   ((uint)data[offset + 2] << 16) |
                   ((uint)data[offset + 3] << 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void WriteInt32(byte[] data, int offset, int value) => WriteUInt32(data, offset, (uint)value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int ReadInt32(byte[] data, int offset) => (int)ReadUInt32(data, offset);
    }
}
