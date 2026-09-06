using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using LiteNetLib;
using NUnit.Framework;

namespace PurrNet.Tests
{
    public class ManualSocketReceiveTests
    {
        private const string ConnectionKey = "manual-receive-tests";

        // Callbacks only inspect existing storage so the allocation test includes event delivery.
        private sealed class Listener : INetEventListener
        {
            public readonly NetPeer[] Peers = new NetPeer[4];
            public readonly int[] Ports = new int[4];
            public readonly IPAddress[] Addresses = new IPAddress[4];
            public int Received;
            public int Errors;

            public void OnPeerConnected(NetPeer peer) { }
            public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) { }
            public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
            public void OnConnectionRequest(ConnectionRequest request) => request.AcceptIfKey(ConnectionKey);
            public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) => Errors++;
            public void OnNetworkReceiveUnconnected(IPEndPoint endPoint, NetPacketReader reader,
                UnconnectedMessageType messageType) => Errors++;

            public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber,
                DeliveryMethod deliveryMethod)
            {
                if (reader.AvailableBytes != 64)
                {
                    Errors++;
                    return;
                }

                int marker = reader.RawData[reader.Position];
                if (marker >= Peers.Length)
                {
                    Errors++;
                    return;
                }

                for (int i = 1; i < 64; i++)
                    if (reader.RawData[reader.Position + i] != (byte)(marker + i))
                        Errors++;

                if (Peers[marker] == null)
                {
                    Peers[marker] = peer;
                    Ports[marker] = peer.Port;
                    Addresses[marker] = peer.Address;
                }
                else if (!ReferenceEquals(Peers[marker], peer) || Ports[marker] != peer.Port ||
                         !Addresses[marker].Equals(peer.Address))
                {
                    Errors++;
                }

                Received++;
            }
        }

        private sealed class Network : IDisposable
        {
            public readonly Listener ServerListener = new Listener();
            public readonly NetManager Server;
            public readonly NetManager[] Clients;
            public readonly Listener[] ClientListeners;
            public readonly NetPeer[] Peers;
            private readonly byte[][] _payloads;
            private readonly IPAddress[] _loopbacks;

            public Network(bool native, bool ipv6, int clientCount, bool mixedFamilies = false)
            {
                if (native && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                    !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    Assert.Ignore("Native sockets are supported on Windows and Linux.");
                if (ipv6 && !Socket.OSSupportsIPv6)
                    Assert.Ignore("IPv6 is unavailable on this host.");

                Server = CreateManager(ServerListener, native, ipv6);
                Clients = new NetManager[clientCount];
                ClientListeners = new Listener[clientCount];
                Peers = new NetPeer[clientCount];
                _payloads = new byte[clientCount][];
                _loopbacks = new IPAddress[clientCount];
                try
                {
                    Assert.IsTrue(Server.StartInManualMode(0), "Server failed to bind.");
                    Assert.AreEqual(native, Server.UseNativeSockets);
                    for (int i = 0; i < clientCount; i++)
                    {
                        ClientListeners[i] = new Listener();
                        _loopbacks[i] = ipv6 && (!mixedFamilies || i % 2 == 1)
                            ? IPAddress.IPv6Loopback
                            : IPAddress.Loopback;
                        Clients[i] = CreateManager(ClientListeners[i], native, ipv6);
                        Assert.IsTrue(Clients[i].StartInManualMode(0), "Client failed to bind.");
                        _payloads[i] = new byte[64];
                        for (int j = 0; j < _payloads[i].Length; j++)
                            _payloads[i][j] = (byte)(i + j);
                        ConnectClient(i);
                    }

                    for (int i = 0; i < 2000 && !AllConnected(); i++)
                    {
                        Pump();
                        Thread.Sleep(1);
                    }
                    Assert.IsTrue(AllConnected(), "Loopback connections timed out.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            private static NetManager CreateManager(Listener listener, bool native, bool ipv6) =>
                new NetManager(listener)
                {
                    AutoRecycle = true,
                    UseNativeSockets = native,
                    IPv6Enabled = ipv6,
                    PingInterval = 10000000,
                    DisconnectTimeout = 10000000
                };

            public void ConnectClient(int index) =>
                Peers[index] = Clients[index].Connect(new IPEndPoint(_loopbacks[index], Server.LocalPort), ConnectionKey);

            public bool AllConnected()
            {
                if (Server.ConnectedPeersCount != Clients.Length)
                    return false;
                for (int i = 0; i < Clients.Length; i++)
                    if (Clients[i].ConnectedPeersCount != 1)
                        return false;
                return true;
            }

            public void Pump()
            {
                for (int i = 0; i < Clients.Length; i++)
                    Clients[i].ManualUpdate(1);
                Server.ManualUpdate(1);
                Server.PollEvents();
                for (int i = 0; i < Clients.Length; i++)
                    Clients[i].PollEvents();
            }

            public bool Exchange(int rounds, DeliveryMethod method)
            {
                for (int round = 0; round < rounds; round++)
                {
                    int target = ServerListener.Received + Clients.Length;
                    for (int i = 0; i < Peers.Length; i++)
                        Peers[i].Send(_payloads[i], method);
                    for (int wait = 0; wait < 1000 && ServerListener.Received < target; wait++)
                    {
                        Pump();
                        if (wait > 10)
                            Thread.Sleep(1);
                    }
                    if (ServerListener.Received != target)
                        return false;
                    // Send acknowledgements and recycle reliable packets before the next round.
                    Pump();
                    Pump();
                }
                return true;
            }

            public void AssertValid()
            {
                Assert.Zero(ServerListener.Errors);
                for (int i = 0; i < Clients.Length; i++)
                {
                    Assert.Zero(ClientListeners[i].Errors);
                    Assert.AreEqual(Clients[i].LocalPort, ServerListener.Peers[i].Port);
                    Assert.AreEqual(_loopbacks[i], ServerListener.Peers[i].Address);
                    for (int j = 0; j < i; j++)
                        Assert.AreNotSame(ServerListener.Peers[i], ServerListener.Peers[j]);
                }
            }

            public void Dispose()
            {
                for (int i = 0; i < Clients.Length; i++)
                    Clients[i]?.Stop();
                Server.Stop();
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ManualPolling_PreservesMultiplePeerEndpoints(bool native, bool ipv6)
        {
            using (var network = new Network(native, ipv6, 3))
            {
                Assert.IsTrue(network.Exchange(100, DeliveryMethod.Unreliable));
                Assert.IsTrue(network.Exchange(100, DeliveryMethod.ReliableOrdered));
                Assert.AreEqual(600, network.ServerListener.Received);
                network.AssertValid();
            }
        }

        [Test]
        public void NativeManualPolling_HandlesIPv4AndIPv6PeersTogether()
        {
            using (var network = new Network(true, true, 3, mixedFamilies: true))
            {
                Assert.IsTrue(network.Exchange(100, DeliveryMethod.Unreliable));
                Assert.IsTrue(network.Exchange(100, DeliveryMethod.ReliableOrdered));
                Assert.AreEqual(600, network.ServerListener.Received);
                network.AssertValid();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeManualPolling_AllocatesZeroWhenIdle(bool ipv6)
        {
            using (var network = new Network(true, ipv6, 1))
            using (var recorder = new AllocationRecorder())
            {
                for (int i = 0; i < 64; i++)
                    network.Pump();
                recorder.Start();
                for (int i = 0; i < 256; i++)
                    network.Pump();
                int allocations = recorder.Stop();
                Assert.Zero(allocations, "Polling idle native sockets must not allocate.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeManualPolling_DrainsEmptyDatagramsThenReceivesTraffic(bool ipv6)
        {
            using (var network = new Network(true, ipv6, 1))
            using (var sender = new Socket(ipv6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork,
                       SocketType.Dgram, ProtocolType.Udp))
            {
                var endpoint = new IPEndPoint(ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback,
                    network.Server.LocalPort);
                var socket = (Socket)typeof(LiteNetManager).GetField(ipv6 ? "_udpSocketv6" : "_udpSocketv4",
                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(network.Server);
                Assert.Zero(sender.SendTo(Array.Empty<byte>(), endpoint));
                Assert.IsTrue(socket.Poll(1000000, SelectMode.SelectRead), "Empty datagram did not arrive.");
                network.Server.PollEvents();
                Assert.IsFalse(socket.Poll(0, SelectMode.SelectRead), "The empty datagram was left queued.");
                Assert.IsTrue(network.Exchange(16, DeliveryMethod.ReliableOrdered));
                network.AssertValid();
            }
        }

        [TestCase(false, DeliveryMethod.Unreliable)]
        [TestCase(true, DeliveryMethod.Unreliable)]
        [TestCase(false, DeliveryMethod.ReliableOrdered)]
        [TestCase(true, DeliveryMethod.ReliableOrdered)]
        public void NativeManualPolling_AllocatesZeroAfterWarmup(bool ipv6, DeliveryMethod method)
        {
            using (var network = new Network(true, ipv6, 3))
            using (var recorder = new AllocationRecorder())
            {
                Assert.IsTrue(network.Exchange(256, method));
                int beforeReceived = network.ServerListener.Received;
                recorder.Start();
                bool delivered = network.Exchange(256, method);
                int allocations = recorder.Stop();

                Assert.IsTrue(delivered);
                Assert.AreEqual(768, network.ServerListener.Received - beforeReceived);
                network.AssertValid();
                Assert.Zero(allocations, "Warmed connected send/update/receive/event delivery allocated.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeManualPolling_ReconnectsFromTheSamePort(bool ipv6)
        {
            using (var network = new Network(true, ipv6, 1))
            {
                Assert.IsTrue(network.Exchange(4, DeliveryMethod.ReliableOrdered));
                var oldPeer = network.ServerListener.Peers[0];
                int port = network.Clients[0].LocalPort;
                network.Clients[0].DisconnectAll();
                for (int i = 0; i < 2000 && network.Server.ConnectedPeersCount != 0; i++)
                {
                    network.Pump();
                    Thread.Sleep(1);
                }
                Assert.Zero(network.Server.ConnectedPeersCount);
                network.Clients[0].Stop();
                Assert.IsTrue(network.Clients[0].StartInManualMode(port));
                network.ServerListener.Peers[0] = null;
                network.ConnectClient(0);
                for (int i = 0; i < 2000 && !network.AllConnected(); i++)
                {
                    network.Pump();
                    Thread.Sleep(1);
                }
                Assert.IsTrue(network.AllConnected());
                Assert.IsTrue(network.Exchange(32, DeliveryMethod.ReliableOrdered));
                Assert.AreNotSame(oldPeer, network.ServerListener.Peers[0]);
                network.AssertValid();
            }
        }
    }
}
