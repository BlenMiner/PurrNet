using System;
using System.Collections.Generic;
using LiteNetLib.Utils;

namespace LiteNetLib
{
    internal sealed class MergedPacketUserData
    {
        public readonly object[] Items;

        public MergedPacketUserData(object[] items)
        {
            Items = items;
        }
    }

    internal sealed class ReliableChannel : BaseChannel
    {
        [ThreadStatic]
        private static List<object> _mergedPacketUserDataList;
        private const int MergeHeaderSize = 2;
        private const int MergeSizeThreshold = 20;
        private NetPacket _mergeTail;

        private struct PendingPacket
        {
            private NetPacket _packet;
            private long _timeStamp;
            private bool _isSent;

            public override string ToString() => _packet == null ? "Empty" : _packet.Sequence.ToString();

            public void Init(NetPacket packet)
            {
                _packet = packet;
                _isSent = false;
            }

            //Returns true if there is a pending packet inside
            public bool TrySend(long currentTime, LiteNetPeer peer)
            {
                if (_packet == null)
                    return false;

                if (_isSent) //check send time
                {
                    double resendDelay = peer.ResendDelay * TimeSpan.TicksPerMillisecond;
                    double packetHoldTime = currentTime - _timeStamp;
                    if (packetHoldTime < resendDelay)
                        return true;
                    NetDebug.Write($"[RC]Resend: {packetHoldTime} > {resendDelay}");
                }
                _timeStamp = currentTime;
                _isSent = true;
                peer.SendUserData(_packet);
                return true;
            }

            public bool IsEmpty => _packet == null;

            public bool Clear(LiteNetPeer peer)
            {
                if (_packet != null)
                {
                    peer.RecycleAndDeliver(_packet);
                    _packet = null;
                    return true;
                }
                return false;
            }
        }

        private readonly NetPacket _outgoingAcks;            //for send acks
        private readonly PendingPacket[] _pendingPackets;    //for unacked packets and duplicates
        private readonly NetPacket[] _receivedPackets;       //for order
        private readonly bool[] _earlyReceived;              //for unordered

        private int _localSeqence;
        private int _remoteSequence;
        private int _localWindowStart;
        private int _remoteWindowStart;

        private bool _mustSendAcks;

        private readonly DeliveryMethod _deliveryMethod;
        private readonly bool _ordered;
        private readonly int _windowSize;
        private const int BitsInByte = 8;
        private readonly byte _id;

        public ReliableChannel(LiteNetPeer peer, bool ordered, byte id) : base(peer)
        {
            _id = id;
            _windowSize = NetConstants.DefaultWindowSize;
            _ordered = ordered;
            _pendingPackets = new PendingPacket[_windowSize];
            for (int i = 0; i < _pendingPackets.Length; i++)
                _pendingPackets[i] = new PendingPacket();

            if (_ordered)
            {
                _deliveryMethod = DeliveryMethod.ReliableOrdered;
                _receivedPackets = new NetPacket[_windowSize];
            }
            else
            {
                _deliveryMethod = DeliveryMethod.ReliableUnordered;
                _earlyReceived = new bool[_windowSize];
            }

            _localWindowStart = 0;
            _localSeqence = 0;
            _remoteSequence = 0;
            _remoteWindowStart = 0;
            _outgoingAcks = new NetPacket(PacketProperty.Ack, (_windowSize - 1) / BitsInByte + 2) {ChannelId = id};
        }

        public override void AddToQueue(NetPacket packet)
        {
            lock (OutgoingQueue)
            {
                _mergeTail = null;
                OutgoingQueue.Enqueue(packet);
            }
            AddToPeerChannelSendQueue();
        }

        internal void AddToQueue(ReadOnlySpan<byte> data, int mtu)
        {
            lock (OutgoingQueue)
            {
                if (!TryAppendToTail(data, mtu))
                {
                    var packet = Peer.NetManager.PoolGetPacket(mtu);
                    packet.Property = PacketProperty.Channeled;
                    packet.Size = NetConstants.ChanneledHeaderSize + data.Length;
                    packet.UserData = null;
                    data.CopyTo(new Span<byte>(packet.RawData, NetConstants.ChanneledHeaderSize, data.Length));
                    OutgoingQueue.Enqueue(packet);
                    _mergeTail = packet;
                }
            }
            AddToPeerChannelSendQueue();
        }

        private bool TryAppendToTail(ReadOnlySpan<byte> data, int mtu)
        {
            var packet = _mergeTail;
            if (packet == null || OutgoingQueue.Count == 0 || data.Overlaps(new ReadOnlySpan<byte>(packet.RawData)))
                return false;

            int headerSize = NetConstants.ChanneledHeaderSize;
            bool merged = packet.Property == PacketProperty.ReliableMerged;
            int firstSize = packet.Size - headerSize;
            int size = packet.Size + data.Length + MergeHeaderSize + (merged ? 0 : MergeHeaderSize);
            if (size + MergeSizeThreshold > mtu || size > packet.RawData.Length)
                return false;

            if (!merged)
            {
                Buffer.BlockCopy(packet.RawData, headerSize, packet.RawData, headerSize + MergeHeaderSize, firstSize);
                FastBitConverter.GetBytes(packet.RawData, headerSize, (ushort)firstSize);
                packet.Size += MergeHeaderSize;
                packet.Property = PacketProperty.ReliableMerged;
            }

            FastBitConverter.GetBytes(packet.RawData, packet.Size, (ushort)data.Length);
            data.CopyTo(new Span<byte>(packet.RawData, packet.Size + MergeHeaderSize, data.Length));
            packet.Size = size;
            return true;
        }

        private NetPacket DequeueOutgoingPacket()
        {
            var packet = OutgoingQueue.Dequeue();
            if (ReferenceEquals(packet, _mergeTail))
                _mergeTail = null;
            return packet;
        }

        private NetPacket TakeMergedPacket(NetPacket packet)
        {
            int headerSize = NetConstants.ChanneledHeaderSize;
            if (packet.Size + MergeSizeThreshold <= Peer.Mtu &&
                headerSize + MergeHeaderSize + BitConverter.ToUInt16(packet.RawData, headerSize) < packet.Size)
                return DequeueOutgoingPacket();

            int position = headerSize;
            int count = 0;
            int maxSize = Peer.Mtu - MergeSizeThreshold;
            while (position + MergeHeaderSize <= packet.Size)
            {
                int length = BitConverter.ToUInt16(packet.RawData, position);
                int next = position + MergeHeaderSize + length;
                if (count > 0 && next > maxSize)
                    break;
                position = next;
                ++count;
            }

            if (position == packet.Size)
            {
                DequeueOutgoingPacket();
                if (count == 1)
                {
                    int length = packet.Size - headerSize - MergeHeaderSize;
                    Buffer.BlockCopy(packet.RawData, headerSize + MergeHeaderSize, packet.RawData, headerSize, length);
                    packet.Size = headerSize + length;
                    packet.Property = PacketProperty.Channeled;
                }
                return packet;
            }

            int payloadSize = position - headerSize;
            var result = Peer.NetManager.PoolGetPacket(position - (count == 1 ? MergeHeaderSize : 0));
            result.Property = count == 1 ? PacketProperty.Channeled : PacketProperty.ReliableMerged;
            result.UserData = null;
            int offset = count == 1 ? MergeHeaderSize : 0;
            Buffer.BlockCopy(packet.RawData, headerSize + offset, result.RawData, headerSize, payloadSize - offset);
            int remaining = packet.Size - position;
            Buffer.BlockCopy(packet.RawData, position, packet.RawData, headerSize, remaining);
            packet.Size = headerSize + remaining;
            return result;
        }

        private NetPacket GetNextOutgoingPacket()
        {
            var packet = OutgoingQueue.Peek();
            if (packet.Property == PacketProperty.ReliableMerged)
                return TakeMergedPacket(packet);

            packet = DequeueOutgoingPacket();
            if (OutgoingQueue.Count == 0 || packet.IsFragmented)
                return packet;

            int maxPayloadSize = Peer.Mtu - NetConstants.ChanneledHeaderSize;

            // The caller holds the queue lock. Only allocate a merge packet if
            // the first two messages can actually be sent together.
            var second = OutgoingQueue.Peek();
            int firstTwoSize = packet.Size + second.Size - 2 * NetConstants.ChanneledHeaderSize + 2 * MergeHeaderSize;
            if (second.IsFragmented || second.Property == PacketProperty.ReliableMerged ||
                firstTwoSize + MergeSizeThreshold > maxPayloadSize)
                return packet;

            var mergedPacket = Peer.NetManager.PoolGetPacket(Peer.Mtu);
            mergedPacket.Property = PacketProperty.ReliableMerged;
            int mergePos = 0;

            var userDataList = _mergedPacketUserDataList;
            if (userDataList == null)
            {
                userDataList = new List<object>();
                _mergedPacketUserDataList = userDataList;
            }
            else
            {
                userDataList.Clear();
            }

            while (true)
            {
                int payloadSize = packet.Size - NetConstants.ChanneledHeaderSize;
                FastBitConverter.GetBytes(mergedPacket.RawData, NetConstants.ChanneledHeaderSize + mergePos, (ushort)payloadSize);
                Buffer.BlockCopy(packet.RawData, NetConstants.ChanneledHeaderSize, mergedPacket.RawData, NetConstants.ChanneledHeaderSize + mergePos + MergeHeaderSize, payloadSize);
                mergePos += payloadSize + MergeHeaderSize;

                if (packet.UserData != null)
                {
                    userDataList.Add(packet.UserData);
                    packet.UserData = null;
                }

                Peer.NetManager.PoolRecycle(packet);
                if (OutgoingQueue.Count == 0)
                    break;

                packet = OutgoingQueue.Peek();
                int newSize = mergePos + MergeHeaderSize + packet.Size - NetConstants.ChanneledHeaderSize;
                if (packet.IsFragmented || packet.Property == PacketProperty.ReliableMerged ||
                    newSize + MergeSizeThreshold > maxPayloadSize)
                    break;

                DequeueOutgoingPacket();
            }

            mergedPacket.Size = NetConstants.ChanneledHeaderSize + mergePos;
            if (userDataList.Count > 0)
                mergedPacket.UserData = new MergedPacketUserData(userDataList.ToArray());

            return mergedPacket;
        }

        private void ProcessIncomingPacket(NetPacket packet)
        {
            if (packet.Property == PacketProperty.ReliableMerged)
            {
                //ProcessMerged
                int pos = NetConstants.ChanneledHeaderSize;
                while (pos + MergeHeaderSize <= packet.Size)
                {
                    ushort size = BitConverter.ToUInt16(packet.RawData, pos);
                    pos += MergeHeaderSize;
                    if (size == 0 || pos + size > packet.Size)
                    {
                        NetDebug.Write("[RR]Merged packet corrupted");
                        break;
                    }

                    NetPacket mergedPacket = Peer.NetManager.PoolGetPacket(NetConstants.ChanneledHeaderSize + size);
                    mergedPacket.Property = PacketProperty.Channeled;
                    mergedPacket.ChannelId = packet.ChannelId;
                    Buffer.BlockCopy(packet.RawData, pos, mergedPacket.RawData, NetConstants.ChanneledHeaderSize, size);
                    pos += size;

                    Peer.AddReliablePacket(_deliveryMethod, mergedPacket);
                }
                Peer.NetManager.PoolRecycle(packet);
            }
            else
            {
                Peer.AddReliablePacket(_deliveryMethod, packet);
            }
        }

        //ProcessAck in packet
        private void ProcessAck(NetPacket packet)
        {
            if (packet.Size != _outgoingAcks.Size)
            {
                NetDebug.Write("[PA]Invalid acks packet size");
                return;
            }

            ushort ackWindowStart = packet.Sequence;
            int windowRel = NetUtils.RelativeSequenceNumber(_localWindowStart, ackWindowStart);
            if (ackWindowStart >= NetConstants.MaxSequence || windowRel < 0)
            {
                NetDebug.Write("[PA]Bad window start");
                return;
            }

            //check relevance
            if (windowRel >= _windowSize)
            {
                NetDebug.Write("[PA]Old acks");
                return;
            }

            byte[] acksData = packet.RawData;
            lock (_pendingPackets)
            {
                for (int pendingSeq = _localWindowStart;
                    pendingSeq != _localSeqence;
                    pendingSeq = (pendingSeq + 1) % NetConstants.MaxSequence)
                {
                    int rel = NetUtils.RelativeSequenceNumber(pendingSeq, ackWindowStart);
                    if (rel >= _windowSize)
                    {
                        //NetDebug.Write($"[PA]REL: {rel}");
                        break;
                    }

                    int pendingIdx = pendingSeq % _windowSize;
                    int currentByte = NetConstants.ChanneledHeaderSize + pendingIdx / BitsInByte;
                    int currentBit = pendingIdx % BitsInByte;
                    if ((acksData[currentByte] & (1 << currentBit)) == 0)
                    {
                        if (Peer.NetManager.EnableStatistics && !_pendingPackets[pendingIdx].IsEmpty)
                        {
                            Peer.Statistics.IncrementPacketLoss();
                            Peer.NetManager.Statistics.IncrementPacketLoss();
                        }

                        //Skip false ack
                        //NetDebug.Write($"[PA]False ack: {pendingSeq}");
                        continue;
                    }

                    if (pendingSeq == _localWindowStart)
                    {
                        //Move window
                        _localWindowStart = (_localWindowStart + 1) % NetConstants.MaxSequence;
                    }

                    //clear packet
                    if (_pendingPackets[pendingIdx].Clear(Peer))
                        NetDebug.Write($"[PA]Removing reliableInOrder ack: {pendingSeq} - true");
                }
            }
        }

        public override bool SendNextPackets()
        {
            if (_mustSendAcks)
            {
                _mustSendAcks = false;
                NetDebug.Write("[RR]SendAcks");
                lock(_outgoingAcks)
                    Peer.SendUserData(_outgoingAcks);
            }

            long currentTime = DateTime.UtcNow.Ticks;
            bool hasPendingPackets = false;

            lock (_pendingPackets)
            {
                //get packets from queue
                lock (OutgoingQueue)
                {
                    while (OutgoingQueue.Count > 0)
                    {
                        int relate = NetUtils.RelativeSequenceNumber(_localSeqence, _localWindowStart);
                        if (relate >= _windowSize)
                            break;

                        var netPacket = GetNextOutgoingPacket();
                        netPacket.Sequence = (ushort) _localSeqence;
                        netPacket.ChannelId = _id;
                        _pendingPackets[_localSeqence % _windowSize].Init(netPacket);
                        _localSeqence = (_localSeqence + 1) % NetConstants.MaxSequence;
                    }
                }

                //send
                for (int pendingSeq = _localWindowStart; pendingSeq != _localSeqence; pendingSeq = (pendingSeq + 1) % NetConstants.MaxSequence)
                {
                    // Please note: TrySend is invoked on a mutable struct, it's important to not extract it into a variable here
                    if (_pendingPackets[pendingSeq % _windowSize].TrySend(currentTime, Peer))
                        hasPendingPackets = true;
                }
            }

            return hasPendingPackets || _mustSendAcks || OutgoingQueue.Count > 0;
        }

        //Process incoming packet
        public override bool ProcessPacket(NetPacket packet)
        {
            if (packet.Property == PacketProperty.Ack)
            {
                ProcessAck(packet);
                return false;
            }
            int seq = packet.Sequence;
            if (seq >= NetConstants.MaxSequence)
            {
                NetDebug.Write("[RR]Bad sequence");
                return false;
            }

            int relate = NetUtils.RelativeSequenceNumber(seq, _remoteWindowStart);
            int relateSeq = NetUtils.RelativeSequenceNumber(seq, _remoteSequence);

            if (relateSeq > _windowSize)
            {
                NetDebug.Write("[RR]Bad sequence");
                return false;
            }

            //Drop bad packets
            if (relate < 0)
            {
                //Too old packet doesn't ack
                NetDebug.Write("[RR]ReliableInOrder too old");
                return false;
            }
            if (relate >= _windowSize * 2)
            {
                //Some very new packet
                NetDebug.Write("[RR]ReliableInOrder too new");
                return false;
            }

            //If very new - move window
            int ackIdx;
            int ackByte;
            int ackBit;
            lock (_outgoingAcks)
            {
                if (relate >= _windowSize)
                {
                    //New window position
                    int newWindowStart = (_remoteWindowStart + relate - _windowSize + 1) % NetConstants.MaxSequence;
                    _outgoingAcks.Sequence = (ushort) newWindowStart;

                    //Clean old data
                    while (_remoteWindowStart != newWindowStart)
                    {
                        ackIdx = _remoteWindowStart % _windowSize;
                        ackByte = NetConstants.ChanneledHeaderSize + ackIdx / BitsInByte;
                        ackBit = ackIdx % BitsInByte;
                        _outgoingAcks.RawData[ackByte] &= (byte) ~(1 << ackBit);
                        _remoteWindowStart = (_remoteWindowStart + 1) % NetConstants.MaxSequence;
                    }
                }

                //Final stage - process valid packet
                //trigger acks send
                _mustSendAcks = true;

                ackIdx = seq % _windowSize;
                ackByte = NetConstants.ChanneledHeaderSize + ackIdx / BitsInByte;
                ackBit = ackIdx % BitsInByte;
                if ((_outgoingAcks.RawData[ackByte] & (1 << ackBit)) != 0)
                {
                    NetDebug.Write("[RR]ReliableInOrder duplicate");
                    //because _mustSendAcks == true
                    AddToPeerChannelSendQueue();
                    return false;
                }

                //save ack
                _outgoingAcks.RawData[ackByte] |= (byte) (1 << ackBit);
            }

            AddToPeerChannelSendQueue();

            //detailed check
            if (seq == _remoteSequence)
            {
                NetDebug.Write("[RR]ReliableInOrder packet succes");
                ProcessIncomingPacket(packet);
                _remoteSequence = (_remoteSequence + 1) % NetConstants.MaxSequence;

                if (_ordered)
                {
                    NetPacket p;
                    while ((p = _receivedPackets[_remoteSequence % _windowSize]) != null)
                    {
                        //process holden packet
                        _receivedPackets[_remoteSequence % _windowSize] = null;
                        ProcessIncomingPacket(p);
                        _remoteSequence = (_remoteSequence + 1) % NetConstants.MaxSequence;
                    }
                }
                else
                {
                    while (_earlyReceived[_remoteSequence % _windowSize])
                    {
                        //process early packet
                        _earlyReceived[_remoteSequence % _windowSize] = false;
                        _remoteSequence = (_remoteSequence + 1) % NetConstants.MaxSequence;
                    }
                }
                return true;
            }

            //holden packet
            if (_ordered)
            {
                _receivedPackets[ackIdx] = packet;
            }
            else
            {
                _earlyReceived[ackIdx] = true;
                ProcessIncomingPacket(packet);
            }
            return true;
        }
    }
}
