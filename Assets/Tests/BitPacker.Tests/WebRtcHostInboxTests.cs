using System;
using NUnit.Framework;
using PurrNet.Transports;

public class WebRtcHostInboxTests
{
    [Test]
    public void RetainedMessagesOwnTheirBytesAndPreserveArrivalOrder()
    {
        var inbox = new WebRtcHostInbox();
        var source = new byte[] { 99, 10, 11, 99 };
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Queued,
            inbox.TryQueue(5, new ArraySegment<byte>(source, 1, 2), 0));
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Queued,
            inbox.TryQueue(5, new ArraySegment<byte>(new byte[] { 12 }), 2));
        Array.Clear(source, 0, source.Length);

        var pending = inbox.MarkConnected(5);
        Assert.AreEqual(2, pending.Count);
        CollectionAssert.AreEqual(new byte[] { 10, 11 }, pending[0]);
        CollectionAssert.AreEqual(new byte[] { 12 }, pending[1]);
        Assert.IsNull(inbox.MarkConnected(5));
    }

    [TestCase((byte)1)]
    [TestCase((byte)4)]
    public void UnreliableUpdatesBeforeConnectionAreDropped(byte method)
    {
        var inbox = new WebRtcHostInbox();
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Dropped,
            inbox.TryQueue(7, new ArraySegment<byte>(new byte[] { 1 }), method));
        Assert.IsNull(inbox.MarkConnected(7));
    }

    [Test]
    public void LateReliableDataForDisconnectedConnectionsIsNotRetained()
    {
        var inbox = new WebRtcHostInbox();
        var data = new ArraySegment<byte>(new byte[] { 1 });
        inbox.TryQueue(7, data, 0);
        inbox.MarkDisconnected(7);
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Dropped, inbox.TryQueue(7, data, 0));
        Assert.IsNull(inbox.MarkConnected(7));

        inbox.MarkConnected(8);
        inbox.MarkDisconnected(8);
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Dropped, inbox.TryQueue(8, data, 2));
    }

    [Test]
    public void PendingMessageLimitAppliesAcrossConnectionsAndIncludesEmptyFrames()
    {
        var inbox = new WebRtcHostInbox();
        var empty = new ArraySegment<byte>(Array.Empty<byte>());
        for (var i = 0; i < 128; ++i)
            Assert.AreEqual(WebRtcHostInbox.QueueResult.Queued, inbox.TryQueue(i, empty, 0));
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Overflow, inbox.TryQueue(128, empty, 0));

        inbox.MarkConnected(0);
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Queued, inbox.TryQueue(128, empty, 0));
    }

    [Test]
    public void ByteLimitIsGlobalAndDisconnectReleasesCapacity()
    {
        var inbox = new WebRtcHostInbox();
        var halfLimit = new ArraySegment<byte>(new byte[512 * 1024]);
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Queued, inbox.TryQueue(1, halfLimit, 0));
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Queued, inbox.TryQueue(2, halfLimit, 0));
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Overflow,
            inbox.TryQueue(3, new ArraySegment<byte>(new byte[1]), 0));

        inbox.MarkDisconnected(1);
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Queued, inbox.TryQueue(3, halfLimit, 0));
        Assert.AreEqual(1, inbox.MarkConnected(2).Count);
        Assert.AreEqual(1, inbox.MarkConnected(3).Count);
    }

    [Test]
    public void ClearReleasesPendingDataAndConnectionTombstonesForNextHostSession()
    {
        var inbox = new WebRtcHostInbox();
        var fullLimit = new ArraySegment<byte>(new byte[1024 * 1024]);
        inbox.MarkDisconnected(1);
        inbox.TryQueue(2, fullLimit, 0);
        inbox.Clear();

        Assert.IsNull(inbox.MarkConnected(2));
        Assert.AreEqual(WebRtcHostInbox.QueueResult.Queued, inbox.TryQueue(1, fullLimit, 0));
        Assert.AreEqual(1, inbox.MarkConnected(1).Count);
    }
}
