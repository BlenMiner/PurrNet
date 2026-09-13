using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet.Transports;

public sealed class FragmentationPressureTests
{
    [Test]
    public void ThrowingDropCallbackDoesNotPoisonLaterPressureAdmission()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        Fill(receiver, sender, 7, 16, Payload(100, 1));
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        object oldestBuffer = PendingBuffer(receiver, 7, 0);
        var payload = Payload(100, 99);
        var fragments = Fragments(sender, payload);
        receiver.onMessageDropped = _ => throw new InvalidOperationException("drop observer failed");

        Assert.Throws<InvalidOperationException>(() =>
            Receive(receiver, 7, 0, false, fragments[0], out _));
        Assert.That(Pending(receiver), Is.EqualTo(16));
        Assert.That(PendingBytes(receiver), Is.EqualTo(1600));
        Assert.That(PressureEvictions(receiver).Count, Is.Zero,
            "an assembly that remains pending must not acquire an eviction record");
        Assert.That(BufferDisposed(oldestBuffer), Is.False);

        receiver.onMessageDropped = null;
        DeliverAll(receiver, 7, 0, false, fragments, payload);
        Assert.That(Pending(receiver), Is.EqualTo(15));
        Assert.That(PendingBytes(receiver), Is.EqualTo(1500));
        Assert.That(PressureEvictions(receiver).Count, Is.EqualTo(1));
        Assert.That(BufferDisposed(oldestBuffer), Is.True);
    }

    [Test]
    public void FreshTwoFragmentMessageCompletesAfterSixteenLossesAtSixtyHz()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;
        Fill(receiver, sender, 7, 16, Payload(100, 1));
        // Simulate 16 send ticks without sleeping.
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        receiver.CleanupStale(5000);
        Assert.That(Pending(receiver), Is.EqualTo(16));
        object oldestBuffer = PendingBuffer(receiver, 7, 0);

        var payload = Payload(100, 99);
        var fragments = Fragments(sender, payload);
        Assert.That(fragments.Count, Is.EqualTo(2));
        DeliverAll(receiver, 7, 0, false, fragments, payload);

        Assert.That(Pending(receiver), Is.EqualTo(15));
        Assert.That(PendingBytes(receiver), Is.EqualTo(1500));
        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.Evicted));
        Assert.That(drops[0].senderId, Is.EqualTo(7));
        Assert.That(BufferDisposed(oldestBuffer), Is.True, "pressure eviction must release the old owned buffer");
        receiver.Reset();
        Assert.That(Pending(receiver), Is.Zero);
        Assert.That(PendingBytes(receiver), Is.Zero);
    }

    [Test]
    public void LateEvictedFragmentsCannotResurrectAndDisplaceFreshMessages()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var old = Fragments(sender, Payload(100, 1));
        Assert.That(Receive(receiver, 7, 0, false, old[0], out _), Is.False);
        Fill(receiver, sender, 7, 15, Payload(100, 2));
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        var payload = Payload(100, 99);
        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload), payload);
        Assert.That(Pending(receiver), Is.EqualTo(15));

        foreach (byte[] fragment in old)
            Assert.That(Receive(receiver, 7, 0, false, fragment, out _), Is.False);
        Assert.That(Pending(receiver), Is.EqualTo(15), "late pieces of an evicted message cannot reserve again");
        Assert.That(PendingBytes(receiver), Is.EqualTo(1500));
        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload), payload);
        Assert.That(Pending(receiver), Is.EqualTo(15));
    }

    [Test]
    public void SenderPressurePreservesAnotherPeersIncompleteMessage()
    {
        using var sender = new FragmentationLayer();
        using var otherSender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var otherPayload = Payload(100, 42);
        var other = Fragments(otherSender, otherPayload);
        Assert.That(Receive(receiver, 8, 0, false, other[0], out _), Is.False);
        Fill(receiver, sender, 7, 16, Payload(100, 1));
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;
        var payload = Payload(100, 99);
        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload), payload);

        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].senderId, Is.EqualTo(7));
        Assert.That(Receive(receiver, 8, 0, false, other[1], out var assembled), Is.True);
        AssertPayload(otherPayload, assembled);
        Assert.That(Pending(receiver), Is.EqualTo(15));
        Assert.That(PendingBytes(receiver), Is.EqualTo(1500));
    }

    [Test]
    public void SenderBytePressureEvictsItsOwnBufferBeforeFiveSecondExpiry()
    {
        using var sender = new FragmentationLayer();
        using var otherSender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var otherPayload = Payload(100, 42);
        var other = Fragments(otherSender, otherPayload);
        Assert.That(Receive(receiver, 8, 0, false, other[0], out _), Is.False);
        var payload = Payload(FragmentationLayer.MAX_MESSAGE_SIZE, 19);
        Fill(receiver, sender, 7, 2, payload, 8192);
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        receiver.CleanupStale(5000);
        Assert.That(PendingBytes(receiver), Is.EqualTo(2 * payload.Length + 100));
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;

        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload, 8192), payload);
        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.Evicted));
        Assert.That(drops[0].senderId, Is.EqualTo(7));
        Assert.That(Pending(receiver), Is.EqualTo(2));
        Assert.That(PendingBytes(receiver), Is.EqualTo(payload.Length + 100));
        Assert.That(Receive(receiver, 8, 0, false, other[1], out var assembled), Is.True);
        AssertPayload(otherPayload, assembled);
    }

    [Test]
    public void IncomingSequencedMessageKeepsItsExistingBudgetRejectionPolicy()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        Fill(receiver, sender, 7, 16, Payload(100, 1));
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;
        foreach (byte[] fragment in Fragments(sender, Payload(100, 99)))
            Assert.That(Receive(receiver, 7, 1, true, fragment, out _), Is.False);
        Assert.That(Pending(receiver), Is.EqualTo(16));
        Assert.That(PendingBytes(receiver), Is.EqualTo(1600));
        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.BudgetExceeded));
    }

    [Test]
    public void OrdinaryPressureCannotEvictSequencedAssemblies()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var first = new List<byte[]>();
        for (byte stream = 1; stream <= 16; stream++)
        {
            var fragments = Fragments(sender, Payload(100, stream));
            if (stream == 1) first = fragments;
            Assert.That(Receive(receiver, 7, stream, true, fragments[0], out _), Is.False);
        }
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;
        foreach (byte[] fragment in Fragments(sender, Payload(100, 99)))
            Assert.That(Receive(receiver, 7, 0, false, fragment, out _), Is.False);
        Assert.That(Pending(receiver), Is.EqualTo(16));
        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.BudgetExceeded));
        Assert.That(Receive(receiver, 7, 1, true, first[1], out var assembled), Is.True);
        AssertPayload(Payload(100, 1), assembled);
    }

    [Test]
    public void ImpossibleReservationPreservesOrdinaryAssemblyBesideProtectedSequencedBytes()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var protectedFirst = Fragments(sender, Payload(FragmentationLayer.MAX_MESSAGE_SIZE, 1), 8192);
        var protectedSecond = Fragments(sender, Payload(FragmentationLayer.MAX_MESSAGE_SIZE - 100, 2), 8192);
        Assert.That(Receive(receiver, 7, 1, true, protectedFirst[0], out _), Is.False);
        Assert.That(Receive(receiver, 7, 2, true, protectedSecond[0], out _), Is.False);
        var ordinaryPayload = Payload(100, 3);
        var ordinary = Fragments(sender, ordinaryPayload);
        Assert.That(Receive(receiver, 7, 0, false, ordinary[0], out _), Is.False);
        object ordinaryBuffer = PendingBuffer(receiver, 7, 2);
        Assert.That(PendingBytes(receiver), Is.EqualTo(2 * FragmentationLayer.MAX_MESSAGE_SIZE));
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;

        // Eviction leaves only 100 bytes available for the 200-byte message.
        foreach (byte[] fragment in Fragments(sender, Payload(200, 4)))
            Assert.That(Receive(receiver, 7, 0, false, fragment, out _), Is.False);

        Assert.That(Pending(receiver), Is.EqualTo(3));
        Assert.That(PendingBytes(receiver), Is.EqualTo(2 * FragmentationLayer.MAX_MESSAGE_SIZE));
        Assert.That(BufferDisposed(ordinaryBuffer), Is.False);
        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.BudgetExceeded));
        Assert.That(Receive(receiver, 7, 0, false, ordinary[1], out var assembled), Is.True);
        AssertPayload(ordinaryPayload, assembled);
        Assert.That(Pending(receiver), Is.EqualTo(2));
        Assert.That(PendingBytes(receiver), Is.EqualTo(2 * FragmentationLayer.MAX_MESSAGE_SIZE - 100));
    }

    [TestCase(0u)]
    [TestCase(uint.MaxValue - 16u)]
    public void UnseenReorderedMessageCompletesAfterPressureIncludingCounterWrap(uint firstMessageId)
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        typeof(FragmentationLayer).GetField("_nextMessageId", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(sender, firstMessageId);
        var delayedPayload = Payload(100, 42);
        var delayed = Fragments(sender, delayedPayload);
        var pendingPayload = Payload(100, 1);
        var messages = new List<List<byte[]>>();
        for (int i = 0; i < 16; i++)
        {
            var fragments = Fragments(sender, pendingPayload);
            messages.Add(fragments);
            Assert.That(Receive(receiver, 7, 0, false, fragments[0], out _), Is.False);
        }
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;
        var freshPayload = Payload(100, 99);
        DeliverAll(receiver, 7, 0, false, Fragments(sender, freshPayload), freshPayload);
        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.Evicted));

        for (int i = 1; i < messages.Count; i++)
        {
            Assert.That(Receive(receiver, 7, 0, false, messages[i][1], out var assembled), Is.True);
            AssertPayload(pendingPayload, assembled);
        }
        Assert.That(Pending(receiver), Is.Zero);

        DeliverAll(receiver, 7, 0, false, delayed, delayedPayload);
        foreach (byte[] fragment in messages[0])
            Assert.That(Receive(receiver, 7, 0, false, fragment, out _), Is.False);
        Assert.That(Pending(receiver), Is.Zero);
        Assert.That(PendingBytes(receiver), Is.Zero);
        Assert.That(drops.Count, Is.EqualTo(1));
    }

    [Test]
    public void PressureEvictionDistinguishesIdenticalMessageIdsBySenderAndStream()
    {
        using var sender = new FragmentationLayer();
        using var otherSender = new FragmentationLayer();
        using var otherStream = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        var payload = Payload(100, 1);
        var evicted = Fragments(sender, payload);
        Assert.That(Receive(receiver, 7, 0, false, evicted[0], out _), Is.False);
        Fill(receiver, sender, 7, 15, payload);
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload), payload);
        Assert.That(Pending(receiver), Is.EqualTo(15));

        DeliverAll(receiver, 8, 0, false, Fragments(otherSender, payload), payload);
        DeliverAll(receiver, 7, 1, false, Fragments(otherStream, payload), payload);
        foreach (byte[] fragment in evicted)
            Assert.That(Receive(receiver, 7, 0, false, fragment, out _), Is.False);
        Assert.That(Pending(receiver), Is.EqualTo(15));
        Assert.That(PendingBytes(receiver), Is.EqualTo(1500));
    }

    [Test]
    public void FullSenderEvictionHistoryRejectsPressureButAllowsUnpressuredMessages()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        int limit = (int)typeof(FragmentationLayer)
            .GetField("MAX_PRESSURE_EVICTIONS_PER_SENDER", BindingFlags.Static | BindingFlags.NonPublic)
            .GetRawConstantValue();
        var payload = Payload(100, 1);
        var messages = FillSenderPressureHistory(receiver, sender, 7, limit, payload);
        Assert.That(Pending(receiver), Is.EqualTo(16));
        Assert.That(PressureHistoryCount(receiver), Is.EqualTo(limit));
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;

        foreach (byte[] fragment in Fragments(sender, Payload(100, 99)))
            Assert.That(Receive(receiver, 7, 0, false, fragment, out _), Is.False);
        Assert.That(Pending(receiver), Is.EqualTo(16));
        Assert.That(PendingBytes(receiver), Is.EqualTo(1600));
        Assert.That(PressureHistoryCount(receiver), Is.EqualTo(limit));
        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.BudgetExceeded));

        CompletePendingTwoFragmentMessages(receiver, 7, messages, payload);
        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload), payload);
        Assert.That(Pending(receiver), Is.Zero);
        Assert.That(PendingBytes(receiver), Is.Zero);
        Assert.That(PressureHistoryCount(receiver), Is.EqualTo(limit));
        Assert.That(drops.Count, Is.EqualTo(1));
    }

    [Test]
    public void NestedPressureAdmissionCannotConsumeReservedGlobalHistoryCapacity()
    {
        using var receiver = new FragmentationLayer();
        int senderLimit = (int)typeof(FragmentationLayer)
            .GetField("MAX_PRESSURE_EVICTIONS_PER_SENDER", BindingFlags.Static | BindingFlags.NonPublic)
            .GetRawConstantValue();
        int globalLimit = (int)typeof(FragmentationLayer)
            .GetField("MAX_PRESSURE_EVICTIONS", BindingFlags.Static | BindingFlags.NonPublic)
            .GetRawConstantValue();
        var payload = Payload(100, 1);
        int historySender = 0;
        while (PressureHistoryCount(receiver) < globalLimit - 1)
        {
            using var sender = new FragmentationLayer();
            int count = Math.Min(senderLimit, globalLimit - 1 - PressureHistoryCount(receiver));
            var messages = FillSenderPressureHistory(receiver, sender, historySender, count, payload);
            CompletePendingTwoFragmentMessages(receiver, historySender, messages, payload);
            historySender++;
        }

        using var outerSender = new FragmentationLayer();
        using var nestedSender = new FragmentationLayer();
        Fill(receiver, outerSender, 100, 16, payload);
        Fill(receiver, nestedSender, 101, 16, payload);
        var nestedBuffers = new List<object>();
        for (uint messageId = 0; messageId < 16; messageId++)
            nestedBuffers.Add(PendingBuffer(receiver, 101, messageId));
        var nestedMessage = Fragments(nestedSender, payload);
        var drops = new List<FragmentDropInfo>();
        bool nestedAttempted = false;
        receiver.onMessageDropped = info =>
        {
            drops.Add(info);
            if (info.senderId != 100 || nestedAttempted)
                return;

            nestedAttempted = true;
            Assert.That(PressureHistoryCount(receiver), Is.EqualTo(globalLimit),
                "outer eviction must reserve the last history slot before its callback");
            int beforeCount = Pending(receiver);
            int beforeBytes = PendingBytes(receiver);
            foreach (byte[] fragment in nestedMessage)
                Assert.That(Receive(receiver, 101, 0, false, fragment, out _), Is.False);
            Assert.That(Pending(receiver), Is.EqualTo(beforeCount));
            Assert.That(PendingBytes(receiver), Is.EqualTo(beforeBytes));
        };

        DeliverAll(receiver, 100, 0, false, Fragments(outerSender, payload), payload);

        Assert.That(nestedAttempted, Is.True);
        Assert.That(PressureHistoryCount(receiver), Is.EqualTo(globalLimit));
        Assert.That(Pending(receiver), Is.EqualTo(31));
        Assert.That(PendingBytes(receiver), Is.EqualTo(3100));
        foreach (object buffer in nestedBuffers)
            Assert.That(BufferDisposed(buffer), Is.False);
        Assert.That(drops.Count, Is.EqualTo(2));
        Assert.That(drops[0].senderId, Is.EqualTo(100));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.Evicted));
        Assert.That(drops[1].senderId, Is.EqualTo(101));
        Assert.That(drops[1].reason, Is.EqualTo(FragmentDropReason.BudgetExceeded));
    }

    [Test]
    public void GlobalEvictionHistoryIsBoundedAndCleanupRestoresPressureAdmission()
    {
        using var receiver = new FragmentationLayer();
        int senderLimit = (int)typeof(FragmentationLayer)
            .GetField("MAX_PRESSURE_EVICTIONS_PER_SENDER", BindingFlags.Static | BindingFlags.NonPublic)
            .GetRawConstantValue();
        int globalLimit = (int)typeof(FragmentationLayer)
            .GetField("MAX_PRESSURE_EVICTIONS", BindingFlags.Static | BindingFlags.NonPublic)
            .GetRawConstantValue();
        var payload = Payload(100, 1);
        int senderId = 0;
        while (PressureHistoryCount(receiver) < globalLimit)
        {
            using var sender = new FragmentationLayer();
            int count = Math.Min(senderLimit, globalLimit - PressureHistoryCount(receiver));
            var messages = FillSenderPressureHistory(receiver, sender, senderId, count, payload);
            CompletePendingTwoFragmentMessages(receiver, senderId, messages, payload);
            senderId++;
        }
        Assert.That(PressureHistoryCount(receiver), Is.EqualTo(globalLimit));

        using var pressuredSender = new FragmentationLayer();
        Fill(receiver, pressuredSender, senderId, 16, payload);
        var fresh = Fragments(pressuredSender, payload);
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;
        foreach (byte[] fragment in fresh)
            Assert.That(Receive(receiver, senderId, 0, false, fragment, out _), Is.False);
        Assert.That(Pending(receiver), Is.EqualTo(16));
        Assert.That(PendingBytes(receiver), Is.EqualTo(1600));
        Assert.That(PressureHistoryCount(receiver), Is.EqualTo(globalLimit));
        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.BudgetExceeded));

        AgePressureEvictions(receiver, 6000);
        receiver.CleanupStale(5000);
        Assert.That(PressureHistoryCount(receiver), Is.Zero);
        Assert.That(EvictionSenderCount(receiver), Is.Zero);
        DeliverAll(receiver, senderId, 0, false, fresh, payload);
        Assert.That(Pending(receiver), Is.EqualTo(15));
        Assert.That(PendingBytes(receiver), Is.EqualTo(1500));
        Assert.That(PressureHistoryCount(receiver), Is.EqualTo(1));
        Assert.That(drops.Count, Is.EqualTo(2));
        Assert.That(drops[1].reason, Is.EqualTo(FragmentDropReason.Evicted));
    }

    [TestCase(7)]
    [TestCase(8)]
    public void BytePressureChecksHistoryCapacityForEveryVictimBeforeEvicting(int remainingHistory)
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        int limit = (int)typeof(FragmentationLayer)
            .GetField("MAX_PRESSURE_EVICTIONS_PER_SENDER", BindingFlags.Static | BindingFlags.NonPublic)
            .GetRawConstantValue();
        var smallPayload = Payload(100, 1);
        var messages = FillSenderPressureHistory(receiver, sender, 7, limit - remainingHistory, smallPayload);
        CompletePendingTwoFragmentMessages(receiver, 7, messages, smallPayload);

        // Admitting 1 MiB requires eight eviction records; seven must reject atomically.
        Fill(receiver, sender, 7, 16, Payload(FragmentationLayer.MAX_MESSAGE_SIZE / 8, 2), 8192);
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        var buffers = new List<object>();
        foreach (DictionaryEntry pair in Entries(receiver))
            buffers.Add(pair.Value.GetType().GetField("buffer").GetValue(pair.Value));
        var drops = new List<FragmentDropInfo>();
        receiver.onMessageDropped = drops.Add;

        var payload = Payload(FragmentationLayer.MAX_MESSAGE_SIZE, 99);
        var incoming = Fragments(sender, payload, 8192);
        if (remainingHistory == 8)
        {
            DeliverAll(receiver, 7, 0, false, incoming, payload);
            Assert.That(Pending(receiver), Is.EqualTo(8));
            Assert.That(PendingBytes(receiver), Is.EqualTo(FragmentationLayer.MAX_MESSAGE_SIZE));
            Assert.That(PressureHistoryCount(receiver), Is.EqualTo(limit));
            int disposed = 0;
            foreach (object buffer in buffers)
                if (BufferDisposed(buffer)) disposed++;
            Assert.That(disposed, Is.EqualTo(8));
            Assert.That(drops.Count, Is.EqualTo(8));
            foreach (var drop in drops)
                Assert.That(drop.reason, Is.EqualTo(FragmentDropReason.Evicted));
            return;
        }

        foreach (byte[] fragment in incoming)
            Assert.That(Receive(receiver, 7, 0, false, fragment, out _), Is.False);

        Assert.That(Pending(receiver), Is.EqualTo(16));
        Assert.That(PendingBytes(receiver), Is.EqualTo(2 * FragmentationLayer.MAX_MESSAGE_SIZE));
        Assert.That(PressureHistoryCount(receiver), Is.EqualTo(limit - remainingHistory));
        foreach (object buffer in buffers)
            Assert.That(BufferDisposed(buffer), Is.False, "failed admission must preserve every existing assembly");
        Assert.That(drops.Count, Is.EqualTo(1));
        Assert.That(drops[0].reason, Is.EqualTo(FragmentDropReason.BudgetExceeded));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisconnectOrResetClearsPressureStateForReusedMessageIds(bool reset)
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        Fill(receiver, sender, 7, 16, Payload(100, 1));
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        var payload = Payload(100, 99);
        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload), payload);
        if (reset) receiver.Reset();
        else receiver.RemoveSender(7);
        Assert.That(Pending(receiver), Is.Zero);
        Assert.That(PendingBytes(receiver), Is.Zero);
        Assert.That(PressureHistoryCount(receiver), Is.Zero);
        Assert.That(EvictionSenderCount(receiver), Is.Zero);
        using var replacement = new FragmentationLayer();
        DeliverAll(receiver, 7, 0, false, Fragments(replacement, payload), payload);
        Assert.That(Pending(receiver), Is.Zero);
    }

    [Test]
    public void ExpiredPressureEvictionsAllowMessageIdReuse()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        Fill(receiver, sender, 7, 16, Payload(100, 1));
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        var payload = Payload(100, 99);
        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload), payload);
        Assert.That(PressureEvictions(receiver).Count, Is.EqualTo(1));

        SetCreationTicks(receiver, 7, unchecked(Environment.TickCount - 6000), 60);
        AgePressureEvictions(receiver, 6000);
        receiver.CleanupStale(5000);
        Assert.That(Pending(receiver), Is.Zero);
        Assert.That(PressureEvictions(receiver).Count, Is.Zero);

        typeof(FragmentationLayer).GetField("_nextMessageId", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(sender, 0u);
        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload), payload);
        Assert.That(Pending(receiver), Is.Zero);
        Assert.That(PendingBytes(receiver), Is.Zero);
    }

    [Test]
    public void ScheduledCleanupReclaimsExpiredEvictionsWhenNoBuffersRemain()
    {
        using var sender = new FragmentationLayer();
        using var receiver = new FragmentationLayer();
        Fill(receiver, sender, 7, 16, Payload(100, 1));
        SetCreationTicks(receiver, 7, Environment.TickCount, 60);
        var payload = Payload(100, 99);
        DeliverAll(receiver, 7, 0, false, Fragments(sender, payload), payload);
        SetCreationTicks(receiver, 7, unchecked(Environment.TickCount - 6000), 60);
        receiver.CleanupStale(5000);
        Assert.That(Pending(receiver), Is.Zero);
        Assert.That(PressureEvictions(receiver).Count, Is.EqualTo(1));
        ForceScheduledCleanup(receiver);
        ForceScheduledCleanup(receiver);
        object completed = typeof(FragmentationLayer)
            .GetField("_completedBuffer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(receiver);
        Assert.That(BufferDisposed(completed), Is.True);

        AgePressureEvictions(receiver, 6000);
        ForceScheduledCleanup(receiver);
        Assert.That(PressureEvictions(receiver).Count, Is.Zero,
            "idle-buffer early return must not strand pressure metadata forever");
        Assert.That(EvictionSenderCount(receiver), Is.Zero);
    }

    private static List<List<byte[]>> FillSenderPressureHistory(FragmentationLayer receiver,
        FragmentationLayer sender, int senderId, int evictionCount, byte[] payload)
    {
        var messages = new List<List<byte[]>>();
        for (int i = 0; i < 16 + evictionCount; i++)
        {
            var fragments = Fragments(sender, payload);
            messages.Add(fragments);
            Assert.That(fragments.Count, Is.EqualTo(2));
            Assert.That(Receive(receiver, senderId, 0, false, fragments[0], out _), Is.False);
        }
        return messages;
    }

    private static void CompletePendingTwoFragmentMessages(FragmentationLayer receiver, int senderId,
        List<List<byte[]>> messages, byte[] payload)
    {
        var messageIds = new List<uint>();
        foreach (DictionaryEntry pair in Entries(receiver))
            if ((int)pair.Key.GetType().GetField("senderId").GetValue(pair.Key) == senderId)
                messageIds.Add((uint)pair.Key.GetType().GetField("messageId").GetValue(pair.Key));
        foreach (uint messageId in messageIds)
        {
            Assert.That(Receive(receiver, senderId, 0, false, messages[(int)messageId][1], out var assembled), Is.True);
            AssertPayload(payload, assembled);
        }
        Assert.That(Pending(receiver), Is.Zero);
    }

    private static int EvictionSenderCount(FragmentationLayer receiver) =>
        ((IDictionary)typeof(FragmentationLayer)
            .GetField("_senderEvictionCounts", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(receiver)).Count;

    private static int PressureHistoryCount(FragmentationLayer receiver) => PressureEvictions(receiver).Count;

    private static void Fill(FragmentationLayer receiver, FragmentationLayer sender, int senderId,
        int messages, byte[] payload, int mtu = 64)
    {
        for (int message = 0; message < messages; message++)
        {
            var fragments = Fragments(sender, payload, mtu);
            Assert.That(fragments.Count, Is.GreaterThan(1));
            Assert.That(Receive(receiver, senderId, 0, false, fragments[0], out _), Is.False);
        }
    }

    private static List<byte[]> Fragments(FragmentationLayer sender, byte[] payload, int mtu = 64)
    {
        var fragments = new List<byte[]>();
        sender.Send(new ByteData(payload, 0, payload.Length), mtu, fragment =>
        {
            var copy = new byte[fragment.length];
            Buffer.BlockCopy(fragment.data, fragment.offset, copy, 0, copy.Length);
            fragments.Add(copy);
        });
        return fragments;
    }

    private static bool Receive(FragmentationLayer receiver, int sender, byte stream, bool sequenced,
        byte[] bytes, out ByteData assembled) =>
        receiver.Receive(sender, stream, sequenced, new ByteData(bytes, 0, bytes.Length), out assembled);

    private static void DeliverAll(FragmentationLayer receiver, int sender, byte stream, bool sequenced,
        List<byte[]> fragments, byte[] payload)
    {
        ByteData assembled = default;
        for (int i = 0; i < fragments.Count; i++)
            Assert.That(Receive(receiver, sender, stream, sequenced, fragments[i], out assembled),
                Is.EqualTo(i == fragments.Count - 1), $"fragment {i}/{fragments.Count}");
        AssertPayload(payload, assembled);
    }

    private static byte[] Payload(int length, byte seed)
    {
        var data = new byte[length];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(seed + i * 17);
        return data;
    }

    private static void AssertPayload(byte[] expected, ByteData actual)
    {
        var copy = new byte[actual.length];
        Buffer.BlockCopy(actual.data, actual.offset, copy, 0, copy.Length);
        CollectionAssert.AreEqual(expected, copy);
    }

    private static IDictionary Entries(FragmentationLayer receiver) =>
        (IDictionary)typeof(FragmentationLayer).GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(receiver);

    private static int Pending(FragmentationLayer receiver) => Entries(receiver).Count;

    private static IDictionary PressureEvictions(FragmentationLayer receiver) =>
        (IDictionary)typeof(FragmentationLayer).GetField("_unreliableEvictions", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(receiver);

    private static void AgePressureEvictions(FragmentationLayer receiver, int ageMs)
    {
        var evictions = PressureEvictions(receiver);
        var keys = new List<object>();
        foreach (DictionaryEntry pair in evictions) keys.Add(pair.Key);
        foreach (object key in keys)
            evictions[key] = unchecked(Environment.TickCount - ageMs);
    }

    private static void ForceScheduledCleanup(FragmentationLayer receiver)
    {
        typeof(FragmentationLayer).GetField("_cleanupScheduled", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(receiver, false);
        receiver.CleanupStaleIfDue(5000, 500);
    }

    private static int PendingBytes(FragmentationLayer receiver) =>
        (int)typeof(FragmentationLayer).GetField("_pendingBytes", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(receiver);

    private static object PendingBuffer(FragmentationLayer receiver, int senderId, uint messageId)
    {
        foreach (DictionaryEntry pair in Entries(receiver))
        {
            if ((int)pair.Key.GetType().GetField("senderId").GetValue(pair.Key) == senderId &&
                (uint)pair.Key.GetType().GetField("messageId").GetValue(pair.Key) == messageId)
                return pair.Value.GetType().GetField("buffer").GetValue(pair.Value);
        }
        Assert.Fail("Missing expected pending buffer");
        return null;
    }

    private static bool BufferDisposed(object buffer) => (bool)buffer.GetType().GetProperty("isDisposed").GetValue(buffer);

    private static void SetCreationTicks(FragmentationLayer receiver, int senderId, int now, int tickRate)
    {
        var entries = Entries(receiver);
        var keys = new List<object>();
        foreach (DictionaryEntry pair in entries)
            if ((int)pair.Key.GetType().GetField("senderId").GetValue(pair.Key) == senderId) keys.Add(pair.Key);
        keys.Sort((a, b) => ((uint)a.GetType().GetField("messageId").GetValue(a))
            .CompareTo((uint)b.GetType().GetField("messageId").GetValue(b)));
        for (int i = 0; i < keys.Count; i++)
        {
            object value = entries[keys[i]];
            value.GetType().GetField("createdAtTick").SetValue(value,
                unchecked(now - (keys.Count - i) * 1000 / tickRate));
            entries[keys[i]] = value;
        }
    }
}
