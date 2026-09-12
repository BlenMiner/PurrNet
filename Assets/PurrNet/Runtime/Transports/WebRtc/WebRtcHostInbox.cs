using System;
using System.Collections.Generic;

namespace PurrNet.Transports
{
    /// <summary>
    /// Retains reliable data that overtakes a host's client-connected notification
    /// on another data channel. Closed connection IDs remain retired until Clear.
    /// </summary>
    internal sealed class WebRtcHostInbox
    {
        internal enum QueueResult { Queued, Dropped, Overflow }

        private const int MaxPendingBytes = 1024 * 1024;
        private const int MaxPendingMessages = 128;
        private readonly Dictionary<int, List<ArraySegment<byte>>> _pending =
            new Dictionary<int, List<ArraySegment<byte>>>();
        private readonly HashSet<int> _seenConnections = new HashSet<int>();
        private int _pendingBytes;
        private int _pendingMessages;

        public QueueResult TryQueue(int connectionId, ArraySegment<byte> data, byte deliveryMethod)
        {
            if (_seenConnections.Contains(connectionId) || (deliveryMethod != 0 && deliveryMethod != 2))
                return QueueResult.Dropped;
            if (data.Count > MaxPendingBytes - _pendingBytes || _pendingMessages >= MaxPendingMessages)
                return QueueResult.Overflow;

            var copy = new byte[data.Count];
            if (copy.Length > 0)
                Buffer.BlockCopy(data.Array!, data.Offset, copy, 0, copy.Length);
            if (!_pending.TryGetValue(connectionId, out var messages))
            {
                messages = new List<ArraySegment<byte>>();
                _pending.Add(connectionId, messages);
            }
            messages.Add(new ArraySegment<byte>(copy));
            _pendingBytes += copy.Length;
            ++_pendingMessages;
            return QueueResult.Queued;
        }

        public List<ArraySegment<byte>> MarkConnected(int connectionId)
        {
            _seenConnections.Add(connectionId);
            return TakePending(connectionId);
        }

        public void MarkDisconnected(int connectionId)
        {
            _seenConnections.Add(connectionId);
            TakePending(connectionId);
        }

        private List<ArraySegment<byte>> TakePending(int connectionId)
        {
            if (!_pending.Remove(connectionId, out var messages))
                return null;
            foreach (var message in messages)
                _pendingBytes -= message.Count;
            _pendingMessages -= messages.Count;
            return messages;
        }

        public void Clear()
        {
            _pending.Clear();
            _seenConnections.Clear();
            _pendingBytes = 0;
            _pendingMessages = 0;
        }
    }
}
