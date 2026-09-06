using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using LiteNetLib;
using NUnit.Framework;

namespace PurrNet.Tests
{
    public class NativeEndPointTests
    {
        private static object CreateKey(byte[] address, AddressFamily family)
        {
            var type = typeof(LiteNetManager).Assembly.GetType("LiteNetLib.NativeEndPoint", throwOnError: true);
            return Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { address, family }, null);
        }

        private static byte[] Serialize(IPEndPoint endpoint)
        {
            var serialized = endpoint.Serialize();
            var bytes = new byte[serialized.Size];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = serialized[i];
            return bytes;
        }

        [Test]
        public void IPv4KeyIgnoresPaddingAndOwnsItsValue()
        {
            var address = Serialize(new IPEndPoint(IPAddress.Loopback, 24680));
            object original = CreateKey(address, AddressFamily.InterNetwork);
            for (int i = 8; i < address.Length; i++)
                address[i] = (byte)i;
            object padded = CreateKey(address, AddressFamily.InterNetwork);
            Assert.AreEqual(original, padded);
            Assert.AreEqual(original.GetHashCode(), padded.GetHashCode());
            address[3]++;
            Assert.AreNotEqual(original, CreateKey(address, AddressFamily.InterNetwork),
                "Changing the scratch buffer must not change a stored key.");
        }

        [Test]
        public void IPv6KeyIgnoresNativeFamilyAndFlowInfoButDistinguishesScope()
        {
            var scoped = new IPAddress(IPAddress.IPv6Loopback.GetAddressBytes(), 7);
            var address = Serialize(new IPEndPoint(scoped, 24680));
            object original = CreateKey(address, AddressFamily.InterNetworkV6);
            address[0] = 10; // AF_INET6 on Linux differs from the managed/Windows constant.
            address[1] = 0;
            for (int i = 4; i < 8; i++)
                address[i] = (byte)i;
            object otherFlow = CreateKey(address, AddressFamily.InterNetworkV6);
            Assert.AreEqual(original, otherFlow);
            Assert.AreEqual(original.GetHashCode(), otherFlow.GetHashCode());
            address[24]++;
            Assert.AreNotEqual(original, CreateKey(address, AddressFamily.InterNetworkV6));
        }
    }
}
