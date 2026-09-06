using System;
using System.Net.Sockets;

namespace LiteNetLib
{
    // Immutable lookup key for recvfrom's reusable sockaddr buffer. Ignore native
    // family numbers, IPv6 flow info and padding; include scope to distinguish interfaces.
    internal readonly struct NativeEndPoint : IEquatable<NativeEndPoint>
    {
        private readonly ulong _addressLow;
        private readonly ulong _addressHigh;
        private readonly uint _scope;
        private readonly ushort _port;
        private readonly bool _ipv6;

        internal NativeEndPoint(byte[] address, AddressFamily family)
        {
            _ipv6 = family == AddressFamily.InterNetworkV6;
            _port = (ushort)((address[2] << 8) | address[3]);
            _addressLow = _ipv6 ? BitConverter.ToUInt64(address, 8) : BitConverter.ToUInt32(address, 4);
            _addressHigh = _ipv6 ? BitConverter.ToUInt64(address, 16) : 0;
            _scope = _ipv6 ? BitConverter.ToUInt32(address, 24) : 0;
        }

        public bool Equals(NativeEndPoint other) =>
            _addressLow == other._addressLow && _addressHigh == other._addressHigh &&
            _scope == other._scope && _port == other._port && _ipv6 == other._ipv6;

        public override bool Equals(object obj) => obj is NativeEndPoint other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _addressLow.GetHashCode();
                hash = hash * 397 ^ _addressHigh.GetHashCode();
                hash = hash * 397 ^ (int)_scope;
                hash = hash * 397 ^ _port;
                return hash * 397 ^ (_ipv6 ? 1 : 0);
            }
        }
    }
}
