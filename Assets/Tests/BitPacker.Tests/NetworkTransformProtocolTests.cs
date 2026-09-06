using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using UnityEngine;

public class NetworkTransformProtocolTests
{
    [OneTimeSetUp]
    public void RegisterSerializers()
    {
        NetworkManager.CallAllRegisters();
    }

    [Test]
    public void PerTransformOrderSurvivesContinuousSequenceHalfRange()
    {
        var stream = new NTUnreliableRecvStream();
        Assert.That(NetworkTransformModule.MarkReceived(stream, 1, out var lastAppliedOrder), Is.True);

        long incomingOrder = default;
        for (int logicalSequence = 2; logicalSequence <= 32769; logicalSequence++)
        {
            ushort wireSequence = unchecked((ushort)logicalSequence);
            Assert.That(NetworkTransformModule.MarkReceived(stream, wireSequence, out incomingOrder), Is.True);
        }

        Assert.That(NTUnreliable.ShouldApplyOrder(true, lastAppliedOrder, incomingOrder), Is.True);
    }

    [Test]
    public void OutOfOrderPacketsStillUsePerTransformOrdering()
    {
        var stream = new NTUnreliableRecvStream();
        Assert.That(NetworkTransformModule.MarkReceived(stream, 100, out var firstOrder), Is.True);
        Assert.That(NetworkTransformModule.MarkReceived(stream, 102, out var latestOrder), Is.True);
        Assert.That(NetworkTransformModule.MarkReceived(stream, 101, out var delayedOrder), Is.True);

        Assert.That(delayedOrder, Is.GreaterThan(firstOrder));
        Assert.That(delayedOrder, Is.LessThan(latestOrder));
        Assert.That(NTUnreliable.ShouldApplyOrder(true, firstOrder, delayedOrder), Is.True);
        Assert.That(NTUnreliable.ShouldApplyOrder(true, latestOrder, delayedOrder), Is.False);
    }

    [Test]
    public void UnlimitedMtuDoesNotOverflowBitBudget()
    {
        long budget = NetworkTransformModule.CalculateBudgetBits(int.MaxValue);

        Assert.That(budget, Is.EqualTo((int.MaxValue - 32L) * 8L));
        Assert.That(budget, Is.GreaterThan(0));
    }

    [Test]
    public void PiggybackedAckUsesNaturalFieldWidths()
    {
        NetworkTransformUnreliableAckHeader? expected = new NetworkTransformUnreliableAckHeader
        {
            seq = 1234,
            ackBits = 0xA5A55A5A
        };

        using var packer = BitPackerPool.Get();
        Packer<NetworkTransformUnreliableAckHeader?>.Write(packer, expected);

        Assert.That(packer.positionInBits, Is.EqualTo(1 + 16 + 32));

        packer.ResetPositionAndMode(true);
        NetworkTransformUnreliableAckHeader? actual = default;
        Packer<NetworkTransformUnreliableAckHeader?>.Read(packer, ref actual);

        Assert.That(actual.HasValue, Is.True);
        Assert.That(actual!.Value.seq, Is.EqualTo(expected.Value.seq));
        Assert.That(actual.Value.ackBits, Is.EqualTo(expected.Value.ackBits));
    }

    [Test]
    public void StandaloneAckIsRateLimitedWithoutExceedingItsPacketWindow()
    {
        var stream = new NTUnreliableRecvStream { ackDirty = true };

        for (int tick = 1; tick < NTUnreliable.ACK_INTERVAL_TICKS; tick++)
            Assert.That(NetworkTransformModule.ShouldFlushAckAfterTick(stream), Is.False);

        Assert.That(NetworkTransformModule.ShouldFlushAckAfterTick(stream), Is.True);

        stream.ackDelayTicks = 0;
        stream.packetsSinceAck = NTUnreliable.ACK_PACKET_THRESHOLD - 1;
        Assert.That(NetworkTransformModule.ShouldFlushAckAfterPacket(stream), Is.False);

        stream.packetsSinceAck++;
        Assert.That(NetworkTransformModule.ShouldFlushAckAfterPacket(stream), Is.True);
        Assert.That(NTUnreliable.ACK_PACKET_THRESHOLD, Is.LessThan(32));
    }

    [Test]
    public void CaptureRevisionOnlyAdvancesWhenQuantizedStateChanges()
    {
        var go = new GameObject(nameof(CaptureRevisionOnlyAdvancesWhenQuantizedStateChanges));

        try
        {
            var nt = go.AddComponent<NetworkTransform>();
            nt.CaptureUnreliableState();
            uint first = nt.capturedRevision;

            nt.CaptureUnreliableState();
            Assert.That(nt.capturedRevision, Is.EqualTo(first));

            SetField(nt, "_currentData", new NetworkTransformData
            {
                position = (CompressedVector3)Vector3.one,
                rotation = Quaternion.identity,
                scale = Vector3.one
            });
            nt.CaptureUnreliableState();

            Assert.That(nt.capturedRevision, Is.EqualTo(first + 1));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void AcknowledgedSendPacketReleasesItsSnapshots()
    {
        var module = new NetworkTransformModule(null, null, null, default, null);
        var stream = new NTUnreliableSendStream();
        const ushort seq = 7;
        stream.ring[seq % NTUnreliable.RING_SIZE] = new NTUnreliableSlot
        {
            used = true,
            seq = seq,
            entries = new List<NTUnreliableEntry>()
        };

        module.ProcessAck(stream, seq, 0);

        ref var slot = ref stream.ring[seq % NTUnreliable.RING_SIZE];
        Assert.That(slot.acked, Is.True);
        Assert.That(slot.entries, Is.Null);
    }

    [Test]
    public void PredictionVelocityStaysCompact()
    {
        Assert.That(Marshal.SizeOf<NetworkTransformVelocity>(), Is.EqualTo(32));
    }

    [Test]
    public void BaselineHistoryOutlivesTheSafePredictionWindow()
    {
        Assert.That(NTUnreliable.MAX_BASELINE_AGE, Is.EqualTo(1 << NTUnreliable.DISTANCE_BITS));
        Assert.That(NTUnreliable.RING_SIZE, Is.EqualTo(NTUnreliable.MAX_BASELINE_AGE));
        Assert.That(NTUnreliable.MAX_PREDICTED_BASELINE_AGE, Is.LessThan(NTUnreliable.MAX_BASELINE_AGE));

        var baseline = new NetworkTransformState
        {
            data = new NetworkTransformData
            {
                position = (CompressedVector3)Vector3.one,
                rotation = Quaternion.identity,
                scale = Vector3.one
            }
        };
        var velocity = new NetworkTransformVelocity { posX = 10, rotX = 10, scaleX = 10 };

        var near = NTUnreliable.GetDeltaPrediction(
            baseline, velocity, NTUnreliable.MAX_PREDICTED_BASELINE_AGE);
        var far = NTUnreliable.GetDeltaPrediction(
            baseline, velocity, NTUnreliable.MAX_PREDICTED_BASELINE_AGE + 1);

        Assert.That(near.Equals(baseline), Is.False);
        Assert.That(far.Equals(baseline), Is.True);
    }

    [Test]
    public void ChordCheckAcceptsLinearHistoryAndRejectsDeviation()
    {
        var go = new GameObject(nameof(ChordCheckAcceptsLinearHistoryAndRejectsDeviation));

        try
        {
            var nt = go.AddComponent<NetworkTransform>();

            for (ushort tick = 0; tick <= 4; tick++)
            {
                SetField(nt, "_currentData", new NetworkTransformData
                {
                    position = (CompressedVector3)new Vector3(tick * 0.1f, 0f, 0f),
                    rotation = Quaternion.identity,
                    scale = Vector3.one
                });
                nt.CaptureUnreliableState(tick);
            }

            Assert.That(nt.IsChordInterpolable(LinearState(Vector3.zero), 0, 4,
                LinearState(new Vector3(0.4f, 0f, 0f))), Is.True);

            SetField(nt, "_currentData", new NetworkTransformData
            {
                position = (CompressedVector3)new Vector3(0.9f, 0f, 0f),
                rotation = Quaternion.identity,
                scale = Vector3.one
            });
            nt.CaptureUnreliableState(2);

            Assert.That(nt.IsChordInterpolable(LinearState(Vector3.zero), 0, 4,
                LinearState(new Vector3(0.4f, 0f, 0f))), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void ChordCheckRejectsMissingHistory()
    {
        var go = new GameObject(nameof(ChordCheckRejectsMissingHistory));

        try
        {
            var nt = go.AddComponent<NetworkTransform>();

            Assert.That(nt.IsChordInterpolable(LinearState(Vector3.zero), 10, 14,
                LinearState(new Vector3(0.4f, 0f, 0f))), Is.False);

            Assert.That(nt.IsChordInterpolable(LinearState(Vector3.zero), 10, 11,
                LinearState(new Vector3(0.1f, 0f, 0f))), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void PredictionMatchesRespectsPerComponentTolerances()
    {
        var predicted = LinearState(Vector3.one);

        var withinPos = LinearState(Vector3.one);
        withinPos.data.position = new CompressedVector3(
            new CompressedFloat(1000 + NTUnreliable.ADAPTIVE_POS_TOLERANCE),
            new CompressedFloat(1000), new CompressedFloat(1000));
        Assert.That(NTUnreliable.PredictionMatches(predicted, withinPos, default, 2, 64), Is.True);

        var beyondPos = LinearState(Vector3.one);
        beyondPos.data.position = new CompressedVector3(
            new CompressedFloat(1000 + NTUnreliable.ADAPTIVE_POS_TOLERANCE + 1),
            new CompressedFloat(1000), new CompressedFloat(1000));
        Assert.That(NTUnreliable.PredictionMatches(predicted, beyondPos, default, 2, 64), Is.False);

        var fastVelocity = new NetworkTransformVelocity { posX = 100 << NetworkTransformVelocity.FRACTION_BITS };
        Assert.That(NTUnreliable.PredictionMatches(predicted, beyondPos, fastVelocity, 2, 64), Is.True);

        var rotated = LinearState(Vector3.one);
        rotated.data.rotation = Quaternion.Euler(0f, 5f, 0f);
        Assert.That(NTUnreliable.PredictionMatches(predicted, rotated, default, 2, 64), Is.False);

        var scaled = LinearState(Vector3.one);
        scaled.data.scale = (CompressedVector3)(Vector3.one * 1.5f);
        Assert.That(NTUnreliable.PredictionMatches(predicted, scaled, default, 2, 64), Is.False);
    }

    [Test]
    public void TickBasedVelocityRoundTripsLinearMotion()
    {
        var from = LinearState(Vector3.zero);
        var to = LinearState(new Vector3(1.2f, -0.6f, 0.3f));

        const int dist = 6;
        var velocity = NetworkTransformVelocity.Derive(from, to, dist);
        var predicted = NetworkTransformVelocity.Predict(from, velocity, dist);

        Assert.That(predicted.data.position, Is.EqualTo(to.data.position));
        Assert.That(NTUnreliable.PredictionMatches(predicted, to, velocity, 2, 64), Is.True);

        var slowTo = LinearState(new Vector3(0.01f, 0f, 0f));
        var slowVelocity = NetworkTransformVelocity.Derive(from, slowTo, 3);
        var slowPredicted = NetworkTransformVelocity.Predict(from, slowVelocity, 3);
        Assert.That(NTUnreliable.PredictionMatches(slowPredicted, slowTo, slowVelocity, 2, 64), Is.True);
    }

    [Test]
    public void StateLerpBlendsAllComponents()
    {
        var a = LinearState(Vector3.zero);
        var b = LinearState(new Vector3(1f, 0f, 0f));
        b.data.scale = (CompressedVector3)(Vector3.one * 3f);

        var mid = NetworkTransformVelocity.Lerp(a, b, 0.5f);

        Assert.That(mid.data.position!.Value.x.rounded, Is.EqualTo(500));
        Assert.That(mid.data.scale.x.rounded, Is.EqualTo(2000));
        Assert.That(mid.data.rotation, Is.EqualTo((PackedQuaternion)Quaternion.identity));
    }

    private static NetworkTransformState LinearState(Vector3 position)
    {
        return new NetworkTransformState
        {
            frame = NetworkTransformFrame.World,
            data = new NetworkTransformData
            {
                position = (CompressedVector3)position,
                rotation = Quaternion.identity,
                scale = Vector3.one
            }
        };
    }

    [Test]
    public void EntryBoundsRejectCursorRewindAndOverflow()
    {
        Assert.That(NetworkTransformModule.IsValidEntryBounds(10, 1, 11), Is.True);
        Assert.That(NetworkTransformModule.IsValidEntryBounds(10, 0, 11), Is.False);
        Assert.That(NetworkTransformModule.IsValidEntryBounds(10, -1, 11), Is.False);
        Assert.That(NetworkTransformModule.IsValidEntryBounds(10, 2, 11), Is.False);
        Assert.That(NetworkTransformModule.IsValidEntryBounds(12, 1, 11), Is.False);
    }

    [Test]
    public void MalformedEntryDoesNotAdvanceAckWindow()
    {
        var module = new NetworkTransformModule(null, null, null, default, null);
        var stream = module.GetRecvStream(PlayerID.Server);
        var handler = typeof(NetworkTransformModule).GetMethod("OnUnreliableDelta",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        using (var packer = BitPackerPool.Get())
        {
            Packer<int>.Write(packer, 1);
            DeltaPacker<PackedInt>.Write(packer, default, new PackedInt(-1));
            DeltaPacker<NetworkID>.Write(packer, default, new NetworkID(10));
            var malformed = new NetworkTransformUnreliableDelta(default, 1, 0, packer);
            handler.Invoke(module, new object[] { PlayerID.Server, malformed, false });
        }

        Assert.That(stream.ackInit, Is.False);
        Assert.That(stream.ackDirty, Is.False);

        using (var packer = BitPackerPool.Get())
        {
            Packer<int>.Write(packer, 0);
            var validEmpty = new NetworkTransformUnreliableDelta(default, 2, 0, packer);
            handler.Invoke(module, new object[] { PlayerID.Server, validEmpty, false });
        }

        Assert.That(stream.ackInit, Is.True);
        Assert.That(stream.latestSeq, Is.EqualTo(2));
        Assert.That(stream.ackDirty, Is.True);
        NTUnreliable.Release(stream.ring);
    }

    [Test]
    public void TruncatedEntryIsDroppedWithoutEscapingTheNetworkCallback()
    {
        var module = new NetworkTransformModule(null, null, null, default, null);
        var stream = module.GetRecvStream(PlayerID.Server);
        var handler = typeof(NetworkTransformModule).GetMethod("OnUnreliableDelta",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        using (var packer = BitPackerPool.Get())
        {
            // Claims one entry, but omits its length, NetworkID, header, and body.
            Packer<int>.Write(packer, 1);
            var truncated = new NetworkTransformUnreliableDelta(default, 1, 0, packer);

            Assert.DoesNotThrow(() =>
                handler.Invoke(module, new object[] { PlayerID.Server, truncated, false }));
        }

        Assert.That(stream.ackInit, Is.False);
        Assert.That(stream.ackDirty, Is.False);
    }

    [Test]
    public void DisabledParentSyncKeepsReceiverLocalFrameSemantics()
    {
        var parentGo = new GameObject(nameof(DisabledParentSyncKeepsReceiverLocalFrameSemantics) + "-Parent");
        var childGo = new GameObject(nameof(DisabledParentSyncKeepsReceiverLocalFrameSemantics) + "-Child");

        try
        {
            var parent = parentGo.AddComponent<NetworkIdentity>();
            typeof(NetworkIdentity).GetField("_idServer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(parent, (NetworkID?)new NetworkID(99));
            parent.SetIsSpawned(true, true);

            childGo.transform.SetParent(parentGo.transform);
            var nt = childGo.AddComponent<NetworkTransform>();
            var refresh = typeof(NetworkTransform).GetMethod("RefreshLatestFrame",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var latestFrame = typeof(NetworkTransform).GetField("_latestFrame",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            SetField(nt, "_syncParent", true);
            refresh.Invoke(nt, null);
            Assert.That(latestFrame.GetValue(nt), Is.EqualTo(NetworkTransformFrame.LocalIdentity));

            SetField(nt, "_syncParent", false);
            refresh.Invoke(nt, null);
            Assert.That(latestFrame.GetValue(nt), Is.EqualTo(NetworkTransformFrame.LocalStatic));
        }
        finally
        {
            var parent = parentGo.GetComponent<NetworkIdentity>();
            if (parent)
                parent.SetIsSpawned(false, true);
            Object.DestroyImmediate(childGo);
            Object.DestroyImmediate(parentGo);
        }
    }

    [Test]
    public void PendingTransformsStaySortedAndDeduplicated()
    {
        var objects = new List<GameObject>();

        try
        {
            var stream = new NTUnreliableSendStream();
            var thirty = CreateNetworkTransform(30, objects);
            var ten = CreateNetworkTransform(10, objects);
            var twenty = CreateNetworkTransform(20, objects);

            NetworkTransformModule.AddPending(stream, thirty);
            NetworkTransformModule.AddPending(stream, ten);
            NetworkTransformModule.AddPending(stream, twenty);
            NetworkTransformModule.AddPending(stream, twenty);

            Assert.That(stream.pending.Count, Is.EqualTo(3));
            Assert.That(stream.pending[0].id, Is.EqualTo(new NetworkID(10)));
            Assert.That(stream.pending[1].id, Is.EqualTo(new NetworkID(20)));
            Assert.That(stream.pending[2].id, Is.EqualTo(new NetworkID(30)));

            Assert.That(NetworkTransformModule.RemovePending(stream, new NetworkID(20)), Is.True);
            Assert.That(stream.pending.Count, Is.EqualTo(2));
            Assert.That(stream.pending[0].id, Is.EqualTo(new NetworkID(10)));
            Assert.That(stream.pending[1].id, Is.EqualTo(new NetworkID(30)));

            var forty = CreateNetworkTransform(40, objects);
            NetworkTransformModule.MergePending(stream, new List<NetworkTransform> { twenty, forty });

            Assert.That(stream.pending.Count, Is.EqualTo(4));
            Assert.That(stream.pending[0].id, Is.EqualTo(new NetworkID(10)));
            Assert.That(stream.pending[1].id, Is.EqualTo(new NetworkID(20)));
            Assert.That(stream.pending[2].id, Is.EqualTo(new NetworkID(30)));
            Assert.That(stream.pending[3].id, Is.EqualTo(new NetworkID(40)));
        }
        finally
        {
            for (int i = 0; i < objects.Count; i++)
                Object.DestroyImmediate(objects[i]);
        }
    }

    [Test]
    public void HostDespawnUnregistersTransformFromClientAndServerModulesWithoutThrowing()
    {
        var objects = new List<GameObject>();
        var serverModule = new NetworkTransformModule(null, null, null, default, null);
        var clientModule = new NetworkTransformModule(null, null, null, default, null);
        var serverOnly = CreateNetworkTransform(5, objects);
        var nt = CreateNetworkTransform(10, objects);

        try
        {
            // A host registers the same NetworkTransform with distinct server and client modules.
            serverModule.PromoteToServerModule();
            serverModule.Register(serverOnly);
            serverModule.Register(nt);
            clientModule.Register(nt);

            Assert.That(nt.ntServerIndex, Is.EqualTo(1));
            Assert.That(nt.ntIndex, Is.EqualTo(0));

            var serverStream = serverModule.GetSendStream(new PlayerID(1, false));
            var clientStream = clientModule.GetSendStream(PlayerID.Server);
            Assert.That(serverStream.baselines.Length, Is.GreaterThan(nt.ntServerIndex));
            Assert.That(clientStream.baselines.Length, Is.GreaterThan(nt.ntIndex));

            nt.CaptureUnreliableState();
            NetworkTransformModule.AddPending(serverStream, nt);
            const ushort seq = 1;
            serverStream.ring[seq % NTUnreliable.RING_SIZE] = SlotWith(nt, seq, nt.capturedRevision);
            Assert.DoesNotThrow(() => serverModule.ProcessAck(serverStream, seq, 0));
            Assert.That(serverStream.baselines[nt.ntServerIndex].has, Is.True);

            Assert.DoesNotThrow(() => clientModule.Unregister(nt));
            Assert.That(nt.ntIndex, Is.EqualTo(-1));
            Assert.That(nt.ntServerIndex, Is.EqualTo(1));
            Assert.That(nt.ntRegistered, Is.True);
            Assert.That(serverStream.baselines[nt.ntServerIndex].has, Is.True);

            Assert.DoesNotThrow(() => serverModule.Unregister(nt));
            Assert.That(nt.ntServerIndex, Is.EqualTo(-1));
            Assert.That(nt.ntRegistered, Is.False);
            serverModule.Unregister(serverOnly);
        }
        finally
        {
            for (int i = 0; i < objects.Count; i++)
                Object.DestroyImmediate(objects[i]);
        }
    }

    [Test]
    public void PromotedClientModuleKeepsItsTransformRegistration()
    {
        var objects = new List<GameObject>();
        var module = new NetworkTransformModule(null, null, null, default, null);
        var nt = CreateNetworkTransform(10, objects);

        try
        {
            module.Register(nt);
            int index = nt.ntIndex;

            module.PromoteToServerModule();

            Assert.That(nt.ntIndex, Is.EqualTo(-1));
            Assert.That(nt.ntServerIndex, Is.EqualTo(index));
            Assert.That(module.GetSendStream(new PlayerID(1, false)).baselines.Length,
                Is.GreaterThan(nt.ntServerIndex));
            Assert.DoesNotThrow(() => module.Unregister(nt));
            Assert.That(nt.ntRegistered, Is.False);
        }
        finally
        {
            for (int i = 0; i < objects.Count; i++)
                Object.DestroyImmediate(objects[i]);
        }
    }

    [Test]
    public void OnlyAnchorPacketsMoveAnEstablishedBaseline()
    {
        var objects = new List<GameObject>();
        var module = new NetworkTransformModule(null, null, null, default, null);
        var nt = CreateNetworkTransform(10, objects);

        try
        {
            module.Register(nt);
            var stream = module.GetSendStream(new PlayerID(1, false));
            int index = nt.ntIndex;

            nt.CaptureUnreliableState();
            NetworkTransformModule.AddPending(stream, nt);

            stream.ring[1] = SlotWith(nt, 1, nt.capturedRevision - 2, anchor: false);
            module.ProcessAck(stream, 1, 0);
            Assert.That(stream.baselines[index].has, Is.True, "first ack establishes a baseline even off-anchor");
            Assert.That(stream.baselines[index].order, Is.EqualTo(1));

            stream.ring[2] = SlotWith(nt, 2, nt.capturedRevision - 1, anchor: false);
            module.ProcessAck(stream, 2, 0);
            Assert.That(stream.baselines[index].order, Is.EqualTo(1), "off-anchor ack must not move the baseline");
            Assert.That(stream.IsPending(nt), Is.True);

            stream.ring[3] = SlotWith(nt, 3, nt.capturedRevision - 1, anchor: true);
            module.ProcessAck(stream, 3, 0);
            Assert.That(stream.baselines[index].order, Is.EqualTo(3), "anchor ack moves the baseline");

            stream.ring[4] = SlotWith(nt, 4, nt.capturedRevision, anchor: false);
            module.ProcessAck(stream, 4, 0);
            Assert.That(stream.baselines[index].order, Is.EqualTo(4), "an ack of the current revision completes off-anchor");
            Assert.That(stream.IsPending(nt), Is.False);
        }
        finally
        {
            for (int i = 0; i < objects.Count; i++)
                Object.DestroyImmediate(objects[i]);
        }
    }

    [Test]
    public void AckOnlyRemovesPendingCurrentRevision()
    {
        var objects = new List<GameObject>();

        try
        {
            var module = new NetworkTransformModule(null, null, null, default, null);
            var player = new PlayerID(1, false);
            var nt = CreateNetworkTransform(10, objects);
            nt.CaptureUnreliableState();
            module.Register(nt);

            var stream = module.GetSendStream(player);
            stream.pendingInitialized = true;
            NetworkTransformModule.AddPending(stream, nt);

            const ushort oldSeq = 1;
            stream.ring[oldSeq % NTUnreliable.RING_SIZE] = SlotWith(nt, oldSeq, nt.capturedRevision - 1);
            module.ProcessAck(stream, oldSeq, 0);
            Assert.That(stream.pending, Has.Count.EqualTo(1));

            const ushort currentSeq = 2;
            stream.ring[currentSeq % NTUnreliable.RING_SIZE] = SlotWith(nt, currentSeq, nt.capturedRevision);
            module.ProcessAck(stream, currentSeq, 0);
            Assert.That(stream.pending, Is.Empty);
        }
        finally
        {
            for (int i = 0; i < objects.Count; i++)
                Object.DestroyImmediate(objects[i]);
        }
    }

    [Test]
    public void LegacyNetworkTransformApiRemainsAvailable()
    {
        Assert.That(typeof(INetworkTransform).GetMethod(nameof(INetworkTransform.HasChanges)), Is.Not.Null);
        Assert.That(typeof(NetworkTransform).GetMethod(nameof(NetworkTransform.DeltaSave)), Is.Not.Null);
        Assert.That(typeof(NetworkTransformDelta).IsValueType, Is.True);
    }

    [Test]
    public void TargetedResetInvalidatesOnlyTheTargetBaseline()
    {
        var objects = new List<GameObject>();
        var module = new NetworkTransformModule(null, null, null, default, null);
        var target = new PlayerID(1, false);
        var other = new PlayerID(2, false);
        var nt = CreateNetworkTransform(10, objects);
        var nid = nt.id!.Value;
        module.Register(nt);
        var targetStream = module.GetSendStream(target);
        var otherStream = module.GetSendStream(other);

        try
        {
            targetStream.baselines[nt.ntIndex] = new NTBaselineSlot { has = true, gen = 1, genEpoch = 1 };
            otherStream.baselines[nt.ntIndex] = new NTBaselineSlot { has = true, gen = 1, genEpoch = 1 };
            targetStream.ring[0] = SlotWith(nid, 1);
            otherStream.ring[0] = SlotWith(nid, 1);

            module.PrepareTargetedReset(target, nid, 1, 1);

            Assert.That(targetStream.baselines[nt.ntIndex].has, Is.False);
            Assert.That(targetStream.ring[0].entries, Is.Empty);
            Assert.That(targetStream.generationOverrides.ContainsKey(nid), Is.False);
            Assert.That(otherStream.baselines[nt.ntIndex].has, Is.True);
            Assert.That(otherStream.baselines[nt.ntIndex].gen, Is.EqualTo(1));
            Assert.That(otherStream.baselines[nt.ntIndex].genEpoch, Is.EqualTo(1));
            Assert.That(otherStream.ring[0].entries[0].genEpoch, Is.EqualTo(1));
            Assert.That(otherStream.generationOverrides[nid].gen, Is.EqualTo(1));
            Assert.That(otherStream.generationOverrides[nid].epoch, Is.EqualTo(1));
        }
        finally
        {
            for (int i = 0; i < objects.Count; i++)
                Object.DestroyImmediate(objects[i]);
        }

        module.ClearGenerationOverrides(nid);
        Assert.That(otherStream.generationOverrides.ContainsKey(nid), Is.False);
    }

    [Test]
    public void ReliableGenerationRejectsFarBehindAbsoluteButKeepsNormalWrapRecovery()
    {
        var go = new GameObject(nameof(ReliableGenerationRejectsFarBehindAbsoluteButKeepsNormalWrapRecovery));

        try
        {
            var nt = go.AddComponent<NetworkTransform>();
            DisableSynchronizedFields(nt);

            Assert.That((bool)InvokePrivate(nt, "ForceAdoptRecvGen", (byte)20), Is.True);
            Assert.That(nt.TryApplyUnreliableState(default, 10, 1, 0, null, true), Is.False);
            Assert.That(GetField<byte>(nt, "_recvGen"), Is.EqualTo(20));

            Assert.That((bool)InvokePrivate(nt, "ForceAdoptRecvGen", (byte)250), Is.True);
            Assert.That(nt.TryApplyUnreliableState(default, 3, 2, 0, null, true), Is.True);
            Assert.That(GetField<byte>(nt, "_recvGen"), Is.EqualTo(3));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void UnanchoredGenerationCanStillRecoverFromFarBehindAbsolute()
    {
        var go = new GameObject(nameof(UnanchoredGenerationCanStillRecoverFromFarBehindAbsolute));

        try
        {
            var nt = go.AddComponent<NetworkTransform>();
            DisableSynchronizedFields(nt);
            SetField(nt, "_recvGen", (byte)20);
            SetField(nt, "_hasRecvGen", true);

            Assert.That(nt.TryApplyUnreliableState(default, 10, 1, 0, null, true), Is.True);
            Assert.That(GetField<byte>(nt, "_recvGen"), Is.EqualTo(10));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void StaleTargetedSnapshotDoesNotReplaceNewerUnreliableState()
    {
        var go = new GameObject(nameof(StaleTargetedSnapshotDoesNotReplaceNewerUnreliableState));

        try
        {
            var nt = go.AddComponent<NetworkTransform>();
            var newer = new NetworkTransformData
            {
                position = (CompressedVector3)new Vector3(10, 20, 30),
                rotation = Quaternion.Euler(10, 20, 30),
                scale = Vector3.one * 2
            };
            var stale = new NetworkTransformState
            {
                data = new NetworkTransformData
                {
                    position = (CompressedVector3)Vector3.one,
                    rotation = Quaternion.identity,
                    scale = Vector3.one
                }
            };

            SetField(nt, "_currentData", newer);
            SetField(nt, "_latestData", newer);
            SetField(nt, "_lastReadData", newer);
            Assert.That((bool)InvokePrivate(nt, "ForceAdoptRecvGen", (byte)7), Is.True);
            SetField(nt, "_hasAppliedSeq", true);

            Assert.That(nt.TryApplyTargetedState(stale, false, 7), Is.False);

            Assert.That(GetField<NetworkTransformData>(nt, "_currentData"), Is.EqualTo(newer));
            Assert.That(GetField<NetworkTransformData>(nt, "_latestData"), Is.EqualTo(newer));
            Assert.That(GetField<NetworkTransformData>(nt, "_lastReadData"), Is.EqualTo(newer));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void AbsoluteStateOmitsDisabledFieldsAndFrame()
    {
        var go = new GameObject(nameof(AbsoluteStateOmitsDisabledFieldsAndFrame));

        try
        {
            var nt = go.AddComponent<NetworkTransform>();
            SetField(nt, "_syncPosition", SyncMode.World);
            SetField(nt, "_syncRotation", SyncMode.No);
            SetField(nt, "_syncScale", false);

            var position = (CompressedVector3)new Vector3(1.25f, -2.5f, 3.75f);
            var state = new NetworkTransformState
            {
                frame = NetworkTransformFrame.LocalIdentity,
                parentId = new NetworkID(99),
                data = new NetworkTransformData
                {
                    position = position,
                    rotation = Quaternion.Euler(10, 20, 30),
                    scale = Vector3.one * 2
                }
            };
            SetField(nt, "_capturedState", state);

            using var packer = BitPackerPool.Get();
            nt.WriteAbsoluteState(packer);
            int positionOnlyBits = packer.positionInBits;
            packer.ResetPositionAndMode(true);
            var decoded = nt.ReadAbsoluteState(packer);

            Assert.That(decoded.frame, Is.EqualTo(NetworkTransformFrame.World));
            Assert.That(decoded.parentId, Is.EqualTo(default(NetworkID)));
            Assert.That(decoded.data.position, Is.EqualTo(position));
            Assert.That(decoded.data.rotation, Is.EqualTo((PackedQuaternion)Quaternion.identity));
            Assert.That(decoded.data.scale, Is.EqualTo(default(CompressedVector3)));

            SetField(nt, "_syncRotation", SyncMode.World);
            SetField(nt, "_syncScale", true);
            packer.ResetPositionAndMode(false);
            nt.WriteAbsoluteState(packer);

            Assert.That(packer.positionInBits, Is.GreaterThan(positionOnlyBits));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void SharedEntriesOutliveAcksOfOtherPeers()
    {
        var objects = new List<GameObject>();
        var module = new NetworkTransformModule(null, null, null, default, null);
        var nt = CreateNetworkTransform(10, objects);

        try
        {
            module.Register(nt);
            var first = module.GetSendStream(new PlayerID(1, false));
            var second = module.GetSendStream(new PlayerID(2, false));
            int index = nt.ntIndex;
            nt.CaptureUnreliableState();

            var shared = NTSharedEntries.Rent();
            shared.Add(new NTUnreliableEntry
            {
                nid = nt.id!.Value,
                state = nt.capturedState,
                gen = nt.sendGen,
                genEpoch = nt.sendGenEpoch,
                revision = nt.capturedRevision,
                transform = nt
            });
            NTSharedEntries.Retain(shared);
            NTSharedEntries.Retain(shared);
            first.ring[1] = new NTUnreliableSlot { used = true, anchor = true, seq = 1, order = 1, entries = shared };
            second.ring[1] = new NTUnreliableSlot { used = true, anchor = true, seq = 1, order = 1, entries = shared };

            module.ProcessAck(first, 1, 0);
            Assert.That(first.baselines[index].has, Is.True);
            Assert.That(first.ring[1].entries, Is.Null);
            Assert.That(second.ring[1].entries, Is.SameAs(shared));
            Assert.That(shared, Has.Count.EqualTo(1), "the other peer's slot still holds the shared list");

            module.ProcessAck(second, 1, 0);
            Assert.That(second.baselines[index].has, Is.True);
            Assert.That(second.ring[1].entries, Is.Null);
            Assert.That(shared, Is.Empty, "the last release returns the list to the pool");
        }
        finally
        {
            for (int i = 0; i < objects.Count; i++)
                Object.DestroyImmediate(objects[i]);
        }
    }

    [Test]
    public void SharedAdaptiveWriteIsCopiedBeforeOnePeerMutatesIt()
    {
        var objects = new List<GameObject>();
        var module = new NetworkTransformModule(null, null, null, default, null);
        var nt = CreateNetworkTransform(10, objects);

        try
        {
            module.Register(nt);
            var first = module.GetSendStream(new PlayerID(1, false));
            var second = module.GetSendStream(new PlayerID(2, false));
            int index = nt.ntIndex;

            var write = NTLastAdaptiveWrite.Rent();
            write.tick = 7;
            first.SetAdaptiveAt(index, write);
            second.SetAdaptiveAt(index, write);
            Assert.That(write.refs, Is.EqualTo(2));

            var owned = NetworkTransformModule.Own(first, nt, write);
            Assert.That(owned, Is.Not.SameAs(write));
            Assert.That(owned.tick, Is.EqualTo(7));
            Assert.That(first.GetAdaptive(nt), Is.SameAs(owned));
            Assert.That(second.GetAdaptive(nt), Is.SameAs(write));
            Assert.That(write.refs, Is.EqualTo(1));
            Assert.That(NetworkTransformModule.Own(second, nt, write), Is.SameAs(write), "a sole holder mutates in place");

            second.SetAdaptiveAt(index, null);
            Assert.That(write.refs, Is.EqualTo(0));
            Assert.That(write.tick, Is.EqualTo(0), "a released write is reset before reuse");
        }
        finally
        {
            for (int i = 0; i < objects.Count; i++)
                Object.DestroyImmediate(objects[i]);
        }
    }

    private static NTUnreliableSlot SlotWith(NetworkID nid, uint genEpoch)
    {
        return new NTUnreliableSlot
        {
            used = true,
            anchor = true,
            entries = new List<NTUnreliableEntry>
            {
                new() { nid = nid, gen = 1, genEpoch = genEpoch }
            }
        };
    }

    private static NTUnreliableSlot SlotWith(NetworkTransform nt, ushort seq, uint revision, bool anchor = true)
    {
        return new NTUnreliableSlot
        {
            used = true,
            anchor = anchor,
            seq = seq,
            order = seq,
            entries = new List<NTUnreliableEntry>
            {
                new()
                {
                    nid = nt.id!.Value,
                    state = nt.capturedState,
                    gen = nt.sendGen,
                    genEpoch = nt.sendGenEpoch,
                    revision = revision
                }
            }
        };
    }

    private static NetworkTransform CreateNetworkTransform(ulong id, List<GameObject> objects)
    {
        var go = new GameObject($"NetworkTransform-{id}");
        objects.Add(go);
        var nt = go.AddComponent<NetworkTransform>();
        typeof(NetworkIdentity).GetField("_idServer", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(nt, (NetworkID?)new NetworkID(id));
        return nt;
    }

    private static void DisableSynchronizedFields(NetworkTransform target)
    {
        SetField(target, "_syncPosition", SyncMode.No);
        SetField(target, "_syncRotation", SyncMode.No);
        SetField(target, "_syncScale", false);
    }

    private static object InvokePrivate(NetworkTransform target, string name, params object[] args)
    {
        return typeof(NetworkTransform).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(target, args);
    }

    private static T GetField<T>(NetworkTransform target, string name)
    {
        return (T)typeof(NetworkTransform).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target);
    }

    private static void SetField<T>(NetworkTransform target, string name, T value)
    {
        typeof(NetworkTransform).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
}
