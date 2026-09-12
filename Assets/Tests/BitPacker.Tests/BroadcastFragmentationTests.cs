using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;

public class BroadcastFragmentationTests
{
    sealed class SentPacket
    {
        public Connection connection;
        public Channel channel;
        public byte[] data;
    }

    sealed class TestTransport : ITransport
    {
        readonly List<Connection> _connections = new List<Connection>();

        public int mtu = 64;
        public bool capturePackets = true;
        public int sendCount;
        public uint checksum;
        public readonly List<SentPacket> sent = new List<SentPacket>();

#pragma warning disable CS0067
        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;
#pragma warning restore CS0067

        public ConnectionState clientState => ConnectionState.Connected;
        public ConnectionState listenerState => ConnectionState.Connected;
        public IReadOnlyList<Connection> connections => _connections;

        public int GetMTU(Connection target, Channel channel, bool asServer) => mtu;

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.ReliableOrdered)
        {
            Capture(target, data, method);
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
            Capture(default, data, method);
        }

        void Capture(Connection connection, ByteData data, Channel channel)
        {
            sendCount++;
            checksum += data.data[data.offset];
            checksum += data.data[data.offset + data.length - 1];
            if (!capturePackets)
                return;

            var copy = new byte[data.length];
            Buffer.BlockCopy(data.data, data.offset, copy, 0, data.length);
            sent.Add(new SentPacket { connection = connection, channel = channel, data = copy });
        }

        public void Connect(string ip, ushort port) { }
        public void Disconnect() { }
        public void Listen(ushort port) { }
        public void StopListening() { }
        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer) { }
        public void RaiseDataSent(Connection conn, ByteData data, bool asServer) { }
        public void CloseConnection(Connection conn) { }
        public void ReceiveMessages(float delta) { }
        public void SendMessages(float delta) { }
    }

    sealed class TestManager : INetworkManager
    {
        public TestManager(ITransport transport, MTUExceededBehaviour behaviour)
        {
            rawTransport = transport;
            mtuExceededBehaviour = behaviour;
        }

        public bool isOffline => false;
        public bool isServer => false;
        public bool isClient => false;
        public ITransport rawTransport { get; }
        public MTUExceededBehaviour mtuExceededBehaviour { get; }
        public ConnectionState serverState => ConnectionState.Connected;
        public ConnectionState clientState => ConnectionState.Connected;
        public bool shouldAutoStartServer => false;
        public bool shouldAutoStartClient => false;
        public NetworkRules networkRules => null;
        public void StartServer() { }
        public void StartClient() { }
        public void StopServer() { }
        public void StopClient() { }
        public void InternalRegisterClientModules() { }
        public void InternalRegisterServerModules() { }
        public void InternalUnregisterClientModules() { }
        public void InternalUnregisterServerModules() { }
        public bool HasModule<T>(bool asServer) where T : INetworkModule => false;
        public void RequestSendFlushThisFrame() { }
    }

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        NetworkManager.CallAllRegisters();
    }

    [Test]
    public void MTUExceededBehaviour_PreservesSerializedValues()
    {
        Assert.AreEqual(0, (byte)MTUExceededBehaviour.UpgradeToReliable);
        Assert.AreEqual(1, (byte)MTUExceededBehaviour.Drop);
        Assert.AreEqual(2, (byte)MTUExceededBehaviour.Fragment);
    }

    [Test]
    public void NewRawNetManager_DefaultsToFragmentWithoutChangingLegacyEnumValues()
    {
        var gameObject = new GameObject("RawNetManager fragmentation default test");
        try
        {
            var manager = gameObject.AddComponent<RawNetManager>();
            Assert.AreEqual(MTUExceededBehaviour.Fragment, manager.mtuExceededBehaviour);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    [Test]
    public void UnreliableOversizeMessage_IsFragmentedWithinMTUAndReassembledOutOfOrder()
    {
        var senderTransport = new TestTransport { mtu = 64 };
        var receiverTransport = new TestTransport { mtu = 64 };
        var sender = new BroadcastModule(
            new TestManager(senderTransport, MTUExceededBehaviour.Fragment), true);
        var receiver = new BroadcastModule(
            new TestManager(receiverTransport, MTUExceededBehaviour.Fragment), false);
        var connection = new Connection(42);
        string expected = CreatePayload(400, 7);
        string received = null;
        int receiveCount = 0;

        receiver.Subscribe<string>((_, value, _) =>
        {
            received = value;
            receiveCount++;
        });

        sender.Send(connection, expected, Channel.Unreliable);

        Assert.Greater(senderTransport.sent.Count, 1);
        for (int i = 0; i < senderTransport.sent.Count; i++)
        {
            Assert.LessOrEqual(senderTransport.sent[i].data.Length, senderTransport.mtu);
            Assert.AreEqual(Channel.Unreliable, senderTransport.sent[i].channel);
            Assert.AreEqual(connection, senderTransport.sent[i].connection);
        }

        for (int i = senderTransport.sent.Count - 1; i >= 0; i--)
        {
            byte[] packet = senderTransport.sent[i].data;
            receiver.OnDataReceived(connection, new ByteData(packet, 0, packet.Length), false);
        }

        Assert.AreEqual(1, receiveCount);
        Assert.AreEqual(expected, received);
    }

    [Test]
    public void UnreliableSequenced_NewerFragmentedMessageInvalidatesOlderIncompleteMessage()
    {
        var transport = new TestTransport { mtu = 64 };
        var sender = new BroadcastModule(new TestManager(transport, MTUExceededBehaviour.Fragment), true);
        var receiver = new BroadcastModule(
            new TestManager(new TestTransport(), MTUExceededBehaviour.Fragment), false);
        var connection = new Connection(5);
        string oldValue = CreatePayload(250, 1);
        string newValue = CreatePayload(250, 2);
        var received = new List<string>();
        receiver.Subscribe<string>((_, value, _) => received.Add(value));

        sender.Send(connection, oldValue, Channel.UnreliableSequenced);
        int oldFragmentCount = transport.sent.Count;
        sender.Send(connection, newValue, Channel.UnreliableSequenced);

        Assert.Greater(oldFragmentCount, 1);
        Assert.Greater(transport.sent.Count - oldFragmentCount, 1);

        Deliver(receiver, connection, transport.sent[0]);
        Deliver(receiver, connection, transport.sent[oldFragmentCount]);

        for (int i = 1; i < oldFragmentCount; i++)
            Deliver(receiver, connection, transport.sent[i]);

        for (int i = oldFragmentCount + 1; i < transport.sent.Count; i++)
            Deliver(receiver, connection, transport.sent[i]);

        Assert.AreEqual(1, received.Count);
        Assert.AreEqual(newValue, received[0]);
    }

    [Test]
    public void UnreliableSequenced_NewerUnderMTUMessageInvalidatesOlderIncompleteMessage()
    {
        var transport = new TestTransport { mtu = 64 };
        var sender = new BroadcastModule(new TestManager(transport, MTUExceededBehaviour.Fragment), true);
        var receiver = new BroadcastModule(
            new TestManager(new TestTransport(), MTUExceededBehaviour.Fragment), false);
        var connection = new Connection(6);
        string oldValue = CreatePayload(250, 3);
        const string newValue = "new-small-value";
        var received = new List<string>();
        receiver.Subscribe<string>((_, value, _) => received.Add(value));

        sender.Send(connection, oldValue, Channel.UnreliableSequenced);
        int oldFragmentCount = transport.sent.Count;
        sender.Send(connection, newValue, Channel.UnreliableSequenced);

        Assert.Greater(oldFragmentCount, 1);
        Assert.AreEqual(oldFragmentCount + 1, transport.sent.Count);

        Deliver(receiver, connection, transport.sent[0]);
        Deliver(receiver, connection, transport.sent[oldFragmentCount]);

        for (int i = 1; i < oldFragmentCount; i++)
            Deliver(receiver, connection, transport.sent[i]);

        Assert.AreEqual(1, received.Count);
        Assert.AreEqual(newValue, received[0]);
    }

    [Test]
    public void UnreliableSequenced_FragmentsTravelOnPlainUnreliable()
    {
        var transport = new TestTransport { mtu = 64 };
        var sender = new BroadcastModule(new TestManager(transport, MTUExceededBehaviour.Fragment), true);
        var receiver = new BroadcastModule(
            new TestManager(new TestTransport(), MTUExceededBehaviour.Fragment), false);
        var connection = new Connection(11);
        string expected = CreatePayload(400, 9);
        var received = new List<string>();
        receiver.Subscribe<string>((_, value, _) => received.Add(value));

        sender.Send(connection, expected, Channel.UnreliableSequenced);

        Assert.Greater(transport.sent.Count, 1);
        for (int i = 0; i < transport.sent.Count; i++)
            Assert.AreEqual(Channel.Unreliable, transport.sent[i].channel);

        for (int i = transport.sent.Count - 1; i >= 0; i--)
            Deliver(receiver, connection, transport.sent[i]);

        Assert.AreEqual(1, received.Count);
        Assert.AreEqual(expected, received[0]);
    }

    [Test]
    public void UnderMTUUnreliableMessage_UsesOriginalSinglePacketPath()
    {
        var transport = new TestTransport { mtu = 256 };
        var sender = new BroadcastModule(new TestManager(transport, MTUExceededBehaviour.Fragment), true);
        var connection = new Connection(9);

        sender.Send(connection, "small", Channel.Unreliable);

        Assert.AreEqual(1, transport.sent.Count);
        Assert.AreEqual(Channel.Unreliable, transport.sent[0].channel);
    }

    [Test]
    public void BroadcastFragmentation_SteadyStateOversizeSendAllocatesZeroManagedBytes()
    {
        const int iterations = 10_000;
        var transport = new TestTransport { mtu = 64, capturePackets = false };
        var sender = new BroadcastModule(new TestManager(transport, MTUExceededBehaviour.Fragment), true);
        var connection = new Connection(12);
        string payload = CreatePayload(400, 11);

        for (int i = 0; i < 128; i++)
            sender.Send(connection, payload, Channel.Unreliable);

        transport.sendCount = 0;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            sender.Send(connection, payload, Channel.Unreliable);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Greater(transport.sendCount, iterations);
        Assert.AreEqual(0, allocated, $"Broadcast fragmentation allocated {allocated} managed bytes.");
    }

    [Test]
    public void BroadcastUnderMTU_SteadyStateSendAllocatesZeroManagedBytes()
    {
        const int iterations = 100_000;
        var transport = new TestTransport { mtu = 256, capturePackets = false };
        var sender = new BroadcastModule(new TestManager(transport, MTUExceededBehaviour.Fragment), true);
        var connection = new Connection(13);

        for (int i = 0; i < 128; i++)
            sender.Send(connection, 123456, Channel.Unreliable);

        transport.sendCount = 0;
        GC.GetAllocatedBytesForCurrentThread();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
            sender.Send(connection, 123456, Channel.Unreliable);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(iterations, transport.sendCount);
        Assert.AreEqual(0, allocated, $"Under-MTU broadcast send allocated {allocated} managed bytes.");
    }

    [Test]
    public void Benchmark_BroadcastUnderMTUAndFragmentedSend()
    {
        const int fastIterations = 250_000;
        const int fragmentedIterations = 20_000;
        var connection = new Connection(14);

        var fastTransport = new TestTransport { mtu = 256, capturePackets = false };
        var fastSender = new BroadcastModule(
            new TestManager(fastTransport, MTUExceededBehaviour.Fragment), true);
        var fragmentedTransport = new TestTransport { mtu = 64, capturePackets = false };
        var fragmentedSender = new BroadcastModule(
            new TestManager(fragmentedTransport, MTUExceededBehaviour.Fragment), true);
        string fragmentedPayload = CreatePayload(400, 19);

        for (int i = 0; i < 128; i++)
        {
            fastSender.Send(connection, 123456, Channel.Unreliable);
            fragmentedSender.Send(connection, fragmentedPayload, Channel.Unreliable);
        }

        fastTransport.sendCount = 0;
        fragmentedTransport.sendCount = 0;
        var fastWatch = new Stopwatch();
        var fragmentedWatch = new Stopwatch();

        long fastBefore = GC.GetAllocatedBytesForCurrentThread();
        fastWatch.Start();
        for (int i = 0; i < fastIterations; i++)
            fastSender.Send(connection, 123456, Channel.Unreliable);
        fastWatch.Stop();
        long fastAllocated = GC.GetAllocatedBytesForCurrentThread() - fastBefore;

        long fragmentedBefore = GC.GetAllocatedBytesForCurrentThread();
        fragmentedWatch.Start();
        for (int i = 0; i < fragmentedIterations; i++)
            fragmentedSender.Send(connection, fragmentedPayload, Channel.Unreliable);
        fragmentedWatch.Stop();
        long fragmentedAllocated = GC.GetAllocatedBytesForCurrentThread() - fragmentedBefore;

        double fastNs = fastWatch.Elapsed.TotalMilliseconds * 1_000_000.0 / fastIterations;
        double fragmentedNs = fragmentedWatch.Elapsed.TotalMilliseconds * 1_000_000.0 / fragmentedIterations;
        double packetsPerMessage = (double)fragmentedTransport.sendCount / fragmentedIterations;
        UnityEngine.Debug.Log($"[Broadcast Fragmentation] under-MTU={fastNs:F1} ns/message " +
                              $"managed={fastAllocated} B | fragmented={fragmentedNs:F1} ns/message " +
                              $"packets/message={packetsPerMessage:F1} managed={fragmentedAllocated} B");

        Assert.AreEqual(fastIterations, fastTransport.sendCount);
        Assert.Greater(fragmentedTransport.sendCount, fragmentedIterations);
        Assert.AreEqual(0, fastAllocated);
        Assert.AreEqual(0, fragmentedAllocated);
    }

    static void Deliver(BroadcastModule receiver, Connection connection, SentPacket sent)
    {
        receiver.OnDataReceived(connection, new ByteData(sent.data, 0, sent.data.Length), false);
    }

    static string CreatePayload(int length, int seed)
    {
        var chars = new char[length];
        for (int i = 0; i < chars.Length; i++)
            chars[i] = (char)('!' + ((i * 31 + seed * 17) % 90));
        return new string(chars);
    }
}
