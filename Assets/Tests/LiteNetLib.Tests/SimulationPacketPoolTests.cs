using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using LiteNetLib;
using NUnit.Framework;

namespace PurrNet.Tests
{
    public class SimulationPacketPoolTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly byte[] Payload = { 17, 23, 41, 59 };

        private sealed class Listener : INetEventListener
        {
            public int Received;
            public int Errors;
            public bool RecycleAndThrow;

            public void OnPeerConnected(NetPeer peer) { }
            public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) { }
            public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
            public void OnConnectionRequest(ConnectionRequest request) => request.Reject();
            public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) => Errors++;
            public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber,
                DeliveryMethod deliveryMethod) => Errors++;

            public void OnNetworkReceiveUnconnected(IPEndPoint endPoint, NetPacketReader reader,
                UnconnectedMessageType messageType)
            {
                if (reader.AvailableBytes != Payload.Length)
                    Errors++;
                else
                    for (int i = 0; i < Payload.Length; i++)
                        if (reader.RawData[reader.Position + i] != Payload[i])
                            Errors++;
                Received++;
                if (RecycleAndThrow)
                {
                    reader.Recycle();
                    throw new InvalidOperationException("Simulated user callback failure.");
                }
            }
        }

        private sealed class Pair : IDisposable
        {
            public readonly Listener SenderListener = new Listener();
            public readonly Listener ReceiverListener = new Listener();
            public readonly NetManager Sender;
            public readonly NetManager Receiver;
            public readonly IPEndPoint SenderAddress;
            public readonly IPEndPoint ReceiverAddress;

            public Pair()
            {
                if (typeof(LiteNetManager).GetField("_outboundSimulationList", PrivateInstance) == null)
                    Assert.Ignore("LiteNetLib was compiled without network simulation.");
                Sender = CreateManager(SenderListener);
                Receiver = CreateManager(ReceiverListener);
                try
                {
                    Assert.IsTrue(Sender.StartInManualMode(0));
                    Assert.IsTrue(Receiver.StartInManualMode(0));
                    SenderAddress = new IPEndPoint(IPAddress.Loopback, Sender.LocalPort);
                    ReceiverAddress = new IPEndPoint(IPAddress.Loopback, Receiver.LocalPort);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            private static NetManager CreateManager(Listener listener) => new NetManager(listener)
            {
                AutoRecycle = true,
                IPv6Enabled = false,
                UseNativeSockets = false,
                UnconnectedMessagesEnabled = true,
                SimulationMinLatency = 20000,
                SimulationMaxLatency = 20001
            };

            public void SendToReceiver() =>
                Assert.IsTrue(Sender.SendUnconnectedMessage(Payload, ReceiverAddress));

            public void Dispose()
            {
                Sender?.Stop(false);
                Receiver?.Stop(false);
            }
        }

        private static IList Queue(NetManager manager, string name) =>
            (IList)typeof(LiteNetManager).GetField(name, PrivateInstance).GetValue(manager);

        // Advance only the simulation deadline; wall-clock sleeps would make these lifecycle tests flaky.
        private static void MakeDue(NetManager manager, string queueName, string timeField)
        {
            var queue = Queue(manager, queueName);
            for (int i = 0; i < queue.Count; i++)
            {
                var entry = queue[i];
                entry.GetType().GetField(timeField).SetValue(entry, DateTime.MinValue);
                queue[i] = entry;
            }
        }

        private static void ReceiveUntil(NetManager manager, Func<bool> complete)
        {
            for (int i = 0; i < 1000 && !complete(); i++)
            {
                manager.PollEvents();
                if (!complete())
                    Thread.Sleep(1);
            }
            Assert.IsTrue(complete(), "Loopback datagram was not processed.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TotalInboundLossReturnsPacketsWithoutHoldingLatency(bool latency)
        {
            using var pair = new Pair();
            pair.Receiver.SimulateLatency = latency;
            pair.Receiver.SimulatePacketLoss = true;
            pair.Receiver.SimulationPacketLossChance = 100;
            pair.Receiver.EnableStatistics = true;
            for (int i = 0; i < 32; i++)
                pair.SendToReceiver();

            // Poll through the entire socket batch. Lost packets do not reach protocol statistics.
            for (int i = 0; i < 20; i++)
            {
                pair.Receiver.PollEvents();
                Thread.Sleep(1);
            }
            Assert.Zero(pair.ReceiverListener.Received);
            Assert.Zero(Queue(pair.Receiver, "_pingSimulationList").Count);
            Assert.AreEqual(1, pair.Receiver.PoolCount, "All drops should reuse the same receive packet.");
            Assert.Zero(pair.ReceiverListener.Errors);
        }

        [Test]
        public void WarmOutboundLatencyReusesBuffersAndPreservesDatagrams()
        {
            using var pair = new Pair();
            using var recorder = new AllocationRecorder();
            pair.Sender.SimulateLatency = true;
            const int burst = 64;
            for (int pass = 0; pass < 3; pass++)
            {
                // Assertions and queue inspection stay outside the measured send loop.
                bool sent = true;
                recorder.Start();
                for (int i = 0; i < burst; i++)
                    sent &= pair.Sender.SendUnconnectedMessage(Payload, pair.ReceiverAddress);
                int allocations = recorder.Stop();
                Assert.IsTrue(sent);
                if (pass == 2)
                    Assert.Zero(allocations, "Warm simulated sends should reuse packet buffers.");
                Assert.AreEqual(burst, Queue(pair.Sender, "_outboundSimulationList").Count);
                pair.Receiver.PollEvents();
                Assert.AreEqual(pass * burst, pair.ReceiverListener.Received,
                    "Latency must defer the actual socket send.");

                MakeDue(pair.Sender, "_outboundSimulationList", "TimeWhenSend");
                pair.Sender.PollEvents();
                int expected = (pass + 1) * burst;
                ReceiveUntil(pair.Receiver, () => pair.ReceiverListener.Received == expected);
                Assert.Zero(Queue(pair.Sender, "_outboundSimulationList").Count);
            }
            Assert.Zero(pair.SenderListener.Errors);
            Assert.Zero(pair.ReceiverListener.Errors);
        }

        [Test]
        public void StopReturnsPendingInboundAndOutboundBuffers()
        {
            using var pair = new Pair();
            pair.Receiver.SimulateLatency = true;
            pair.SendToReceiver();
            ReceiveUntil(pair.Receiver, () => Queue(pair.Receiver, "_pingSimulationList").Count == 1);
            Assert.IsTrue(pair.Receiver.SendUnconnectedMessage(Payload, pair.SenderAddress));
            Assert.AreEqual(1, Queue(pair.Receiver, "_outboundSimulationList").Count);
            int poolBeforeStop = pair.Receiver.PoolCount;

            pair.Receiver.Stop(false);

            Assert.Zero(Queue(pair.Receiver, "_pingSimulationList").Count);
            Assert.Zero(Queue(pair.Receiver, "_outboundSimulationList").Count);
            Assert.AreEqual(poolBeforeStop + 2, pair.Receiver.PoolCount);
            Assert.Zero(pair.ReceiverListener.Received);
        }

        [Test]
        public void ThrowingCallbackCannotLeaveRecycledPacketInDelayQueue()
        {
            using var pair = new Pair();
            pair.Receiver.SimulateLatency = true;
            pair.Receiver.AutoRecycle = false;
            pair.ReceiverListener.RecycleAndThrow = true;
            pair.SendToReceiver();
            ReceiveUntil(pair.Receiver, () => Queue(pair.Receiver, "_pingSimulationList").Count == 1);
            MakeDue(pair.Receiver, "_pingSimulationList", "TimeWhenGet");

            Assert.Throws<InvalidOperationException>(() => pair.Receiver.PollEvents());

            Assert.Zero(Queue(pair.Receiver, "_pingSimulationList").Count);
            Assert.AreEqual(1, pair.Receiver.PoolCount);
            pair.Receiver.PollEvents();
            pair.Receiver.Stop(false);
            Assert.AreEqual(1, pair.ReceiverListener.Received);
            Assert.AreEqual(1, pair.Receiver.PoolCount, "Cleanup must not recycle the delivered packet twice.");
        }
    }
}
