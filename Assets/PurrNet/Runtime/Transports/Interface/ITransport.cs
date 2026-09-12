using System;
using System.Collections.Generic;
using System.Text;
using PurrNet.Packing;

namespace PurrNet.Transports
{
    public delegate void OnConnectionState(ConnectionState state, bool asServer);

    public delegate void OnDataReceived(Connection conn, ByteData data, bool asServer);

    public delegate void OnDataSent(Connection conn, ByteData data, bool asServer); //Cannot send from clients

    public delegate void OnConnected(Connection conn, bool asServer);

    public delegate void OnDisconnected(Connection conn, DisconnectReason reason, bool asServer);

    public enum ConnectionState
    {
        Connecting,
        Connected,

        Disconnected,
        Disconnecting
    }

    [Serializable]
    public readonly struct ByteData : IEquatable<ByteData>, IDuplicate<ByteData>
    {
        public readonly byte[] data;
        public readonly int length;
        public readonly int offset;

        public ReadOnlySpan<byte> span => new(data, offset, length);

        public ArraySegment<byte> segment => new(data, offset, length);

        public static readonly ByteData empty = new(Array.Empty<byte>(), 0, 0);

        public ByteData(ArraySegment<byte> segment)
        {
            data = segment.Array;
            offset = segment.Offset;
            length = segment.Count;
        }

        public ByteData(byte[] data, int offset, int length)
        {
            this.data = data;
            this.offset = offset;
            this.length = length;
        }

        public ByteData Duplicate()
        {
            var newData = new byte[length];
            Array.Copy(data, newData, length);
            return new ByteData(newData);
        }

        public override bool Equals(object obj)
        {
            if (obj is not ByteData other)
                return false;

            if (length != other.length)
                return false;

            for (int i = 0; i < length; i++)
            {
                if (data[i + offset] != other.data[i + other.offset])
                    return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            int hash = 17;
            for (int i = 0; i < length; i++)
                hash = hash * 31 + data[i + offset];
            return hash;
        }

        public override string ToString()
        {
            var sb = new StringBuilder(16 + length * 3);
            sb.Append("LENGTH: ").Append(length).Append(" DATA: ");
            for (int i = 0; i < length; i++)
                sb.Append(data[i + offset].ToString("X2")).Append(' ');
            return sb.ToString();
        }

        public bool Equals(ByteData other)
        {
            if (length != other.length)
                return false;

            for (int i = 0; i < length; i++)
            {
                if (data[i + offset] != other.data[i + other.offset])
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Defines what happens when a packet exceeds the MTU on an unreliable channel.
    /// </summary>
    public enum MTUExceededBehaviour : byte
    {
        /// <summary>
        /// Automatically upgrade the channel to ReliableOrdered so the packet is
        /// fragmented and delivered reliably. Logs a warning.
        /// </summary>
        UpgradeToReliable = 0,

        /// <summary>
        /// Drop the packet and log a warning. Preserves unreliable semantics.
        /// </summary>
        Drop = 1,

        /// <summary>
        /// Split the message into unreliable MTU-sized fragments. The message is only
        /// delivered when every fragment arrives; missing fragments are not retransmitted.
        /// </summary>
        Fragment = 2
    }

    /// <summary>
    /// Per-RPC override for what happens when the RPC exceeds the MTU on an unreliable channel.
    /// Ignored on <see cref="Channel.UnreliableSequenced"/>: sequencing is a channel-wide
    /// property, so the NetworkManager setting governs that whole channel.
    /// </summary>
    public enum MTUBehaviour : byte
    {
        /// <summary>
        /// Follow the behaviour configured on the NetworkManager.
        /// </summary>
        NetworkManager = 0,

        /// <inheritdoc cref="MTUExceededBehaviour.UpgradeToReliable"/>
        UpgradeToReliable = 1,

        /// <inheritdoc cref="MTUExceededBehaviour.Drop"/>
        Drop = 2,

        /// <inheritdoc cref="MTUExceededBehaviour.Fragment"/>
        Fragment = 3
    }

    public static class MTUBehaviourExtensions
    {
        public static MTUExceededBehaviour Resolve(this MTUBehaviour value, MTUExceededBehaviour fallback)
        {
            switch (value)
            {
                case MTUBehaviour.UpgradeToReliable: return MTUExceededBehaviour.UpgradeToReliable;
                case MTUBehaviour.Drop: return MTUExceededBehaviour.Drop;
                case MTUBehaviour.Fragment: return MTUExceededBehaviour.Fragment;
                default: return fallback;
            }
        }

        public static MTUExceededBehaviour? AsOverride(this MTUBehaviour value)
        {
            if (value == MTUBehaviour.NetworkManager)
                return null;
            return value.Resolve(default);
        }
    }

    public enum Channel : byte
    {
        /// <summary>
        /// It ensures that the data is received but the order is not guaranteed.
        /// </summary>
        ReliableUnordered,

        /// <summary>
        /// Packets are guaranteed to be in order but not guaranteed to be received.
        /// </summary>
        UnreliableSequenced,

        /// <summary>
        /// Packets are guaranteed to be received in order.
        /// </summary>
        ReliableOrdered,

        /// <summary>
        /// Packets are not guaranteed to be received nor in order.
        /// </summary>
        Unreliable
    }

    public interface IConnectable
    {
        ConnectionState clientState { get; }

        void Connect(string ip, ushort port);

        void Disconnect();
    }

    public interface IListener
    {
        ConnectionState listenerState { get; }

        void Listen(ushort port);

        void StopListening();
    }

    public interface ITransport : IListener, IConnectable
    {
        event OnConnected onConnected;
        event OnDisconnected onDisconnected;
        event OnDataReceived onDataReceived;
        event OnDataSent onDataSent;
        event OnConnectionState onConnectionState;

        public IReadOnlyList<Connection> connections { get; }

        bool SupportsChannel(Channel channel)
        {
            return true;
        }

        int GetMTU(Connection target, Channel channel, bool asServer)
        {
            return channel switch
            {
                Channel.Unreliable or Channel.UnreliableSequenced or Channel.ReliableUnordered => 1024,
                Channel.ReliableOrdered => 8192,
                _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, null)
            };
        }

        /// <summary>
        /// Round trip time in milliseconds for a connection, or -1 when the transport
        /// has not measured it yet or does not support it.
        /// On the client side the connection argument is ignored.
        /// </summary>
        int GetRoundTripTime(Connection conn, bool asServer)
        {
            return -1;
        }

        /// <summary>
        /// Whether <see cref="GetRoundTripTime"/> can ever return a value on this transport.
        /// When false a ping reports connect time only instead of waiting for a measurement.
        /// </summary>
        bool measuresRoundTripTime => false;

        /// <summary>
        /// Pushes data already queued for one connection onto the wire without a full send pass.
        /// Returns false when the transport cannot do this, in which case the caller falls back to
        /// <see cref="SendMessages"/>. On the client side the connection argument is ignored.
        /// </summary>
        bool FlushConnection(Connection conn, bool asServer) => false;

        /// <summary>
        /// Short human readable description of the link the local client is currently using,
        /// for example which relay region or whether it is a direct P2P session. Null when the transport has nothing to add.
        /// </summary>
        string clientLinkDescription => null;

        bool shouldServerSendKeepAlive => false;

        bool shouldClientSendKeepAlive => false;

        void SendServerKeepAlive()
        {
        }

        void RaiseDataReceived(Connection conn, ByteData data, bool asServer);

        void RaiseDataSent(Connection conn, ByteData data, bool asServer);

        void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered);

        void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered);

        void CloseConnection(Connection conn);

        void ReceiveMessages(float delta);

        void SendMessages(float delta);

        void UnityUpdate(float delta)
        {
        }
    }

    public enum DisconnectReason
    {
        Timeout,
        ClientRequest,
        ServerRequest,
    }
}
