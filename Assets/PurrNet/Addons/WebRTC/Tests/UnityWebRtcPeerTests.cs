using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet.Transports;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.TestTools;

namespace PurrNet.WebRTC.Tests
{
    public sealed class UnityWebRtcPeerTests
    {
        [UnityTest]
        public IEnumerator RealChannelsExchangeEveryDeliveryModeAndCloseOnce()
        {
            var first = new UnityWebRtcPeer();
            var second = new UnityWebRtcPeer();
            var errors = new List<Exception>();
            var firstMessages = new Dictionary<byte, byte[]>();
            var secondMessages = new Dictionary<byte, byte[]>();
            int firstCloses = 0, secondCloses = 0;
            first.onError += errors.Add;
            second.onError += errors.Add;
            first.onDisconnect += () => ++firstCloses;
            second.onDisconnect += () => ++secondCloses;
            first.onData += data => firstMessages[first.receivedDeliveryMethod] = Copy(data);
            second.onData += data => secondMessages[second.receivedDeliveryMethod] = Copy(data);
            first.onSignal += second.ReceiveSignal;
            second.onSignal += first.ReceiveSignal;
            try
            {
                Assert.IsNotNull(PurrWebRtcPeerProvider.nativeFactory, "Provider registration must run in Play Mode.");
                second.ConnectPeer(false, "[]");
                first.ConnectPeer(true, "[]");
                yield return Pump(first, second, () => first.isConnected && second.isConnected, errors);
                foreach (byte method in new byte[] { 0, 1, 2, 4 })
                {
                    first.Send(new ArraySegment<byte>(new byte[] { 90, method, 7, 91 }, 1, 2), method);
                    second.Send(new ArraySegment<byte>(new byte[] { 90, method, 8, 91 }, 1, 2), method);
                }
                yield return Pump(first, second, () => firstMessages.Count == 4 && secondMessages.Count == 4, errors);
                foreach (byte method in new byte[] { 0, 1, 2, 4 })
                {
                    CollectionAssert.AreEqual(new byte[] { method, 8 }, firstMessages[method]);
                    CollectionAssert.AreEqual(new byte[] { method, 7 }, secondMessages[method]);
                }
                first.Disconnect();
                first.Disconnect();
                yield return Pump(first, second, () => firstCloses == 1 && secondCloses == 1);
                Assert.AreEqual(1, firstCloses);
                Assert.AreEqual(1, secondCloses);
                Assert.IsFalse(first.isConnected);
                Assert.IsFalse(second.isConnected);
            }
            finally
            {
                first.Disconnect();
                second.Disconnect();
            }
        }

        [UnityTest]
        public IEnumerator SequencedChannelRejectsDuplicateAndStaleWireMessages()
        {
            var first = new UnityWebRtcPeer();
            var second = new UnityWebRtcPeer();
            var errors = new List<Exception>();
            var messages = new List<byte>();
            first.onError += errors.Add;
            second.onError += errors.Add;
            first.onSignal += second.ReceiveSignal;
            second.onSignal += first.ReceiveSignal;
            second.onData += data => messages.Add(data.Array[data.Offset]);
            try
            {
                second.ConnectPeer(false, "[]");
                first.ConnectPeer(true, "[]");
                yield return Pump(first, second, () => first.isConnected && second.isConnected, errors);
                var session = typeof(UnityWebRtcPeer).GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(first);
                var channels = (RTCDataChannel[])session.GetType().GetField("channels").GetValue(session);
                channels[1].Send(new byte[] { 255, 255, 255, 255, 1 });
                yield return Pump(first, second, () => messages.Count == 1, errors);
                channels[1].Send(new byte[] { 255, 255, 255, 255, 2 });
                channels[1].Send(new byte[] { 254, 255, 255, 255, 3 });
                channels[1].Send(new byte[] { 0, 0, 0, 0, 4 });
                yield return Pump(first, second, () => messages.Count >= 2, errors);
                CollectionAssert.AreEqual(new byte[] { 1, 4 }, messages);
            }
            finally
            {
                first.Disconnect();
                second.Disconnect();
            }
        }

        [Test]
        public void InvalidSignalFailsOnlyWhenQueueIsPolledAndDisconnectsOnce()
        {
            var peer = new UnityWebRtcPeer();
            int errors = 0, closes = 0;
            peer.onError += _ => ++errors;
            peer.onDisconnect += () => ++closes;
            peer.ConnectPeer(false, "[]");
            peer.ReceiveSignal(new string('x', 32769));
            Assert.AreEqual(0, errors);
            Assert.AreEqual(0, closes);
            peer.ProcessMessageQueue();
            peer.Disconnect();
            peer.ProcessMessageQueue();
            Assert.AreEqual(1, errors);
            Assert.AreEqual(1, closes);
        }

        private static IEnumerator Pump(UnityWebRtcPeer first, UnityWebRtcPeer second, Func<bool> complete,
            List<Exception> errors = null)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!complete())
            {
                first.ProcessMessageQueue();
                second.ProcessMessageQueue();
                if (errors != null && errors.Count != 0)
                    Assert.Fail(errors[0].ToString());
                Assert.Less(Time.realtimeSinceStartup, deadline, "WebRTC test timed out.");
                yield return null;
            }
        }

        private static byte[] Copy(ArraySegment<byte> data)
        {
            var copy = new byte[data.Count];
            Buffer.BlockCopy(data.Array, data.Offset, copy, 0, copy.Length);
            return copy;
        }
    }
}
