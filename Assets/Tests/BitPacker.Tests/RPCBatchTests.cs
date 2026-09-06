using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;

public class RPCBatchTests
{
    sealed class CapturedBatch
    {
        public PlayerID target;
        public Channel channel;
        public Size count;
        public MTUExceededBehaviour? mtuOverride;
        public BitPacker data;
    }

    sealed class TestRPCBatchBackend : IRPCBatchBackend, IDisposable
    {
        readonly List<CapturedBatch> _sent = new List<CapturedBatch>();
        readonly List<CapturedBatch> _sentImmediate = new List<CapturedBatch>();
        PlayerBroadcastDelegate<RPCBatchPacket> _callback;

        public int mtu = int.MaxValue;
        public MTUExceededBehaviour mtuExceededBehaviour { get; set; } = MTUExceededBehaviour.Fragment;
        public PlayerID deliveringTarget { get; private set; }
        public Channel deliveringChannel { get; private set; }
        public IReadOnlyList<CapturedBatch> sent => _sent;
        public IReadOnlyList<CapturedBatch> sentImmediate => _sentImmediate;

        public int GetMTU(PlayerID player, Channel channel, bool asServer) => mtu;

        public void Send(PlayerID player, RPCBatchPacket packet, Channel channel, MTUExceededBehaviour? mtuOverride = null)
        {
            var copy = BitPackerPool.Get();
            copy.WriteBitDataWithoutConsumingIt(packet.data);
            _sent.Add(new CapturedBatch
            {
                target = player,
                channel = channel,
                count = packet.count,
                mtuOverride = mtuOverride,
                data = copy
            });
        }

        public void Send(PlayerID player, ImmediateRPCBatchPacket packet, Channel channel, MTUExceededBehaviour? mtuOverride = null)
        {
            var copy = BitPackerPool.Get();
            copy.WriteBitDataWithoutConsumingIt(packet.data);
            _sentImmediate.Add(new CapturedBatch
            {
                target = player,
                channel = channel,
                count = packet.count,
                mtuOverride = mtuOverride,
                data = copy
            });
        }

        public void DeliverAllImmediate(RPCBatch batch)
        {
            for (int i = 0; i < _sentImmediate.Count; i++)
            {
                var captured = _sentImmediate[i];
                deliveringTarget = captured.target;
                deliveringChannel = captured.channel;

                try
                {
                    batch.ProcessReceivedBatch(new PlayerID(999, false), captured.count,
                        new BitData(captured.data), true);
                }
                finally
                {
                    captured.data.Dispose();
                    captured.data = null;
                }
            }
        }

        public void Subscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback)
        {
            if (_callback != null)
                throw new InvalidOperationException("Only one RPCBatch subscription is supported by this test backend.");
            _callback = callback;
        }

        public void Unsubscribe(PlayerBroadcastDelegate<RPCBatchPacket> callback)
        {
            if (_callback == callback)
                _callback = null;
        }

        public int Count(PlayerID target, Channel channel)
        {
            int count = 0;
            for (int i = 0; i < _sent.Count; i++)
            {
                if (_sent[i].target == target && _sent[i].channel == channel)
                    count++;
            }
            return count;
        }

        public void DeliverAll()
        {
            if (_callback == null)
                throw new InvalidOperationException("RPCBatch is not subscribed.");

            for (int i = 0; i < _sent.Count; i++)
            {
                var captured = _sent[i];
                deliveringTarget = captured.target;
                deliveringChannel = captured.channel;

                try
                {
                    _callback(new PlayerID(999, false), new RPCBatchPacket
                    {
                        count = captured.count,
                        data = new BitData(captured.data)
                    }, true);
                }
                finally
                {
                    captured.data.Dispose();
                    captured.data = null;
                }
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < _sent.Count; i++)
            {
                _sent[i].data?.Dispose();
                _sent[i].data = null;
            }

            for (int i = 0; i < _sentImmediate.Count; i++)
            {
                _sentImmediate[i].data?.Dispose();
                _sentImmediate[i].data = null;
            }
        }
    }

    static readonly Channel[] AllChannels =
    {
        Channel.ReliableUnordered,
        Channel.UnreliableSequenced,
        Channel.ReliableOrdered,
        Channel.Unreliable
    };

    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    static UnionRPCHeader MakeHeader(Channel channel, int sequence)
    {
        return new UnionRPCHeader(new NetworkIdentityRPCHeader
        {
            senderId = new PlayerID((ulong)(100 + (int)channel), false),
            networkId = new NetworkID((ulong)(200 + (int)channel)),
            sceneId = new SceneID(0),
            rpcId = new Size((uint)sequence),
            targetId = null
        });
    }

    static void WritePayload(BitPacker payload, int sequence, int byteCount)
    {
        payload.ResetPositionAndMode(false);
        for (int i = 0; i < byteCount; i++)
            payload.WriteBits((ulong)(byte)(sequence + i), 8);
    }

    static void RecordReceived(TestRPCBatchBackend backend,
        Dictionary<BatchKey, List<int>> received, UnionRPCHeader header, BitData content)
    {
        int sequence;
        using (content.AutoScope())
            sequence = (int)content.packer.ReadBits(8);

        Assert.That(header.rpcId.value, Is.EqualTo((uint)sequence));
        var key = new BatchKey
        {
            playerId = backend.deliveringTarget,
            channel = backend.deliveringChannel
        };

        if (!received.TryGetValue(key, out var values))
        {
            values = new List<int>();
            received.Add(key, values);
        }
        values.Add(sequence);
    }

    [Test]
    public void BatchIndexMapKeepsChannelsIndependent()
    {
        using var map = new BatchIndexMap(4);
        var player = new PlayerID(42, false);
        var channels = new[]
        {
            Channel.ReliableUnordered,
            Channel.UnreliableSequenced,
            Channel.ReliableOrdered,
            Channel.Unreliable
        };

        for (int i = 0; i < channels.Length; i++)
            map.Set(player.id.value, channels[i], 100 + i);

        for (int i = 0; i < channels.Length; i++)
        {
            Assert.That(map.TryGetValue(player.id.value, channels[i], out int value),
                Is.True);
            Assert.That(value, Is.EqualTo(100 + i));
        }

        var reliableOrdered = new BatchKey { playerId = player, channel = Channel.ReliableOrdered };
        Assert.That(map.Remove(reliableOrdered.playerId.id.value, reliableOrdered.channel), Is.True);
        Assert.That(map.TryGetValue(reliableOrdered.playerId.id.value, reliableOrdered.channel, out _), Is.False);
        Assert.That(map.TryGetValue(player.id.value, Channel.ReliableUnordered, out int reliableUnordered),
            Is.True);
        Assert.That(reliableUnordered, Is.EqualTo(100));
    }

    [Test]
    public void BatchIndexMapClearClearsEveryChannel()
    {
        using var map = new BatchIndexMap(4);
        var player = new PlayerID(7, false);
        var channels = new[]
        {
            Channel.ReliableUnordered,
            Channel.UnreliableSequenced,
            Channel.ReliableOrdered,
            Channel.Unreliable
        };

        for (int i = 0; i < channels.Length; i++)
            map.Set(player.id.value, channels[i], i);

        map.Clear();

        for (int i = 0; i < channels.Length; i++)
            Assert.That(map.TryGetValue(player.id.value, channels[i], out _), Is.False);
    }

    [Test]
    public void BatchIndexMapUsesEveryPlayerIdBit()
    {
        using var map = new BatchIndexMap(5);
        var ids = new[]
        {
            0UL,
            1UL,
            0x0000000100000001UL,
            0xFFFFFFFF00000001UL,
            ulong.MaxValue
        };

        for (int i = 0; i < ids.Length; i++)
            map.Set(ids[i], Channel.ReliableOrdered, 100 + i);

        for (int i = 0; i < ids.Length; i++)
        {
            Assert.That(map.TryGetValue(ids[i], Channel.ReliableOrdered, out int value), Is.True);
            Assert.That(value, Is.EqualTo(100 + i));
        }
    }

    [Test]
    public void QueueRejectsUndefinedChannelBeforeIndexLookup()
    {
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend, (_, _, _, _) => { });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            batch.Queue(new PlayerID(1, false), default, default, (Channel)byte.MaxValue));
        Assert.That(backend.sent, Is.Empty);
    }

    [Test]
    public void StateVersionWrapDoesNotAliasDivergedRecipients()
    {
        var targets = new[]
        {
            new PlayerID(41, false),
            new PlayerID(42, false),
            new PlayerID(43, false)
        };
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        for (int i = 0; i < targets.Length; i++)
        {
            int sequence = i + 1;
            WritePayload(payload, sequence, 8);
            batch.Queue(targets[i], MakeHeader(Channel.ReliableOrdered, sequence), new BitData(payload),
                Channel.ReliableOrdered);
        }

        var versionField = typeof(RPCBatch).GetField("_nextStateVersion",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(versionField, Is.Not.Null);
        versionField.SetValue(batch, ulong.MaxValue);

        WritePayload(payload, 4, 8);
        batch.Queue(targets[1], MakeHeader(Channel.ReliableOrdered, 4), new BitData(payload),
            Channel.ReliableOrdered);

        WritePayload(payload, 5, 8);
        batch.Queue(targets, MakeHeader(Channel.ReliableOrdered, 5), new BitData(payload),
            Channel.ReliableOrdered);

        batch.Flush();
        backend.DeliverAll();

        Assert.That(received[new BatchKey
        {
            playerId = targets[0],
            channel = Channel.ReliableOrdered
        }], Is.EqualTo(new[] { 1, 5 }));
        Assert.That(received[new BatchKey
        {
            playerId = targets[1],
            channel = Channel.ReliableOrdered
        }], Is.EqualTo(new[] { 2, 4, 5 }));
        Assert.That(received[new BatchKey
        {
            playerId = targets[2],
            channel = Channel.ReliableOrdered
        }], Is.EqualTo(new[] { 3, 5 }));
    }

    [Test]
    public void QueueFanoutPreservesOrderAndChannelAcrossMtuSplits()
    {
        var targets = new[]
        {
            new PlayerID(11, false),
            new PlayerID(12, false),
            new PlayerID(13, false)
        };
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend { mtu = 64 };
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        const int sequenceCount = 6;
        for (int sequence = 0; sequence < sequenceCount; sequence++)
        {
            for (int channelIdx = 0; channelIdx < AllChannels.Length; channelIdx++)
            {
                var channel = AllChannels[channelIdx];
                WritePayload(payload, sequence, 32);
                batch.Queue(targets, MakeHeader(channel, sequence), new BitData(payload), channel);
            }
        }

        batch.Flush();

        for (int targetIdx = 0; targetIdx < targets.Length; targetIdx++)
        {
            for (int channelIdx = 0; channelIdx < AllChannels.Length; channelIdx++)
            {
                Assert.That(backend.Count(targets[targetIdx], AllChannels[channelIdx]), Is.GreaterThan(1),
                    $"Expected forced MTU splits for target {targets[targetIdx]} on {AllChannels[channelIdx]}.");
            }
        }

        backend.DeliverAll();

        for (int targetIdx = 0; targetIdx < targets.Length; targetIdx++)
        {
            for (int channelIdx = 0; channelIdx < AllChannels.Length; channelIdx++)
            {
                var key = new BatchKey
                {
                    playerId = targets[targetIdx],
                    channel = AllChannels[channelIdx]
                };
                Assert.That(received.TryGetValue(key, out var values), Is.True);
                Assert.That(values, Has.Count.EqualTo(sequenceCount));
                for (int sequence = 0; sequence < sequenceCount; sequence++)
                    Assert.That(values[sequence], Is.EqualTo(sequence));
            }
        }
    }

    [Test]
    public void OversizedUnreliableEntryDropsWhenOverrideIsDrop()
    {
        var target = new PlayerID(50, false);
        using var backend = new TestRPCBatchBackend { mtu = 64 };
        using var batch = new RPCBatch(backend, (_, _, _, _) => { });
        using var payload = BitPackerPool.Get();

        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        try
        {
            WritePayload(payload, 0, 100);
            batch.Queue(target, MakeHeader(Channel.Unreliable, 0), new BitData(payload), Channel.Unreliable,
                MTUBehaviour.Drop);
            batch.Flush();
        }
        finally
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        Assert.That(backend.sent, Is.Empty);
    }

    [Test]
    public void OversizedUnreliableEntryUpgradesToReliableWhenOverrideIsUpgrade()
    {
        var target = new PlayerID(51, false);
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend { mtu = 64 };
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        WritePayload(payload, 0, 100);
        batch.Queue(target, MakeHeader(Channel.Unreliable, 0), new BitData(payload), Channel.Unreliable,
            MTUBehaviour.UpgradeToReliable);
        batch.Flush();

        Assert.That(backend.Count(target, Channel.Unreliable), Is.Zero);
        Assert.That(backend.Count(target, Channel.ReliableOrdered), Is.EqualTo(1));

        backend.DeliverAll();
        Assert.That(received[new BatchKey { playerId = target, channel = Channel.ReliableOrdered }],
            Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void OversizedUnreliableEntrySoloSendsWithForcedFragmentOverride()
    {
        var target = new PlayerID(52, false);
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend { mtu = 64 };
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        WritePayload(payload, 0, 8);
        batch.Queue(target, MakeHeader(Channel.Unreliable, 0), new BitData(payload), Channel.Unreliable,
            MTUBehaviour.Fragment);
        WritePayload(payload, 1, 100);
        batch.Queue(target, MakeHeader(Channel.Unreliable, 1), new BitData(payload), Channel.Unreliable,
            MTUBehaviour.Fragment);

        // the pending small entry is flushed first, then the oversized entry is sent solo
        Assert.That(backend.sent, Has.Count.EqualTo(2));
        Assert.That(backend.sent[0].channel, Is.EqualTo(Channel.Unreliable));
        Assert.That(backend.sent[0].mtuOverride, Is.Null);
        Assert.That(backend.sent[1].channel, Is.EqualTo(Channel.Unreliable));
        Assert.That(backend.sent[1].count.value, Is.EqualTo(1));
        Assert.That(backend.sent[1].mtuOverride, Is.EqualTo(MTUExceededBehaviour.Fragment));

        batch.Flush();
        Assert.That(backend.sent, Has.Count.EqualTo(2));

        backend.DeliverAll();
        Assert.That(received[new BatchKey { playerId = target, channel = Channel.Unreliable }],
            Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void OversizedUnreliableEntryFollowsBackendDefaultWithoutOverride()
    {
        var target = new PlayerID(53, false);
        using var backend = new TestRPCBatchBackend { mtu = 64, mtuExceededBehaviour = MTUExceededBehaviour.Drop };
        using var batch = new RPCBatch(backend, (_, _, _, _) => { });
        using var payload = BitPackerPool.Get();

        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        try
        {
            WritePayload(payload, 0, 100);
            batch.Queue(target, MakeHeader(Channel.Unreliable, 0), new BitData(payload), Channel.Unreliable);
            batch.Flush();
        }
        finally
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        Assert.That(backend.sent, Is.Empty);
    }

    [Test]
    public void SequencedChannelIgnoresPerRpcOverride()
    {
        var target = new PlayerID(57, false);

        // global Drop wins over a per-RPC Fragment override on the sequenced channel
        using (var backend = new TestRPCBatchBackend { mtu = 64, mtuExceededBehaviour = MTUExceededBehaviour.Drop })
        using (var batch = new RPCBatch(backend, (_, _, _, _) => { }))
        using (var payload = BitPackerPool.Get())
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                WritePayload(payload, 0, 100);
                batch.Queue(target, MakeHeader(Channel.UnreliableSequenced, 0), new BitData(payload),
                    Channel.UnreliableSequenced, MTUBehaviour.Fragment);
                batch.Flush();
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }

            Assert.That(backend.sent, Is.Empty);
        }

        // global Fragment wins over a per-RPC Drop override on the sequenced channel
        using (var backend = new TestRPCBatchBackend { mtu = 64 })
        using (var batch = new RPCBatch(backend, (_, _, _, _) => { }))
        using (var payload = BitPackerPool.Get())
        {
            WritePayload(payload, 0, 100);
            batch.Queue(target, MakeHeader(Channel.UnreliableSequenced, 0), new BitData(payload),
                Channel.UnreliableSequenced, MTUBehaviour.Drop);

            Assert.That(backend.sent, Has.Count.EqualTo(1));
            Assert.That(backend.sent[0].channel, Is.EqualTo(Channel.UnreliableSequenced));
            Assert.That(backend.sent[0].count.value, Is.EqualTo(1));
            Assert.That(backend.sent[0].mtuOverride, Is.EqualTo(MTUExceededBehaviour.Fragment));
        }
    }

    [Test]
    public void OversizedFanoutEntryUpgradesToReliablePerTarget()
    {
        var targets = new[]
        {
            new PlayerID(54, false),
            new PlayerID(55, false),
            new PlayerID(56, false)
        };
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend { mtu = 64 };
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        WritePayload(payload, 0, 100);
        batch.Queue(targets, MakeHeader(Channel.Unreliable, 0), new BitData(payload), Channel.Unreliable,
            mtuOverride: MTUBehaviour.UpgradeToReliable);
        batch.Flush();

        for (int i = 0; i < targets.Length; i++)
        {
            Assert.That(backend.Count(targets[i], Channel.Unreliable), Is.Zero);
            Assert.That(backend.Count(targets[i], Channel.ReliableOrdered), Is.EqualTo(1));
        }

        backend.DeliverAll();
        for (int i = 0; i < targets.Length; i++)
        {
            Assert.That(received[new BatchKey { playerId = targets[i], channel = Channel.ReliableOrdered }],
                Is.EqualTo(new[] { 0 }));
        }
    }

    [Test]
    public void HasPendingIsFalseUntilQueueAndClearsOnFlush()
    {
        var target = new PlayerID(60, false);
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend, (_, _, _, _) => { });
        using var payload = BitPackerPool.Get();

        Assert.That(batch.hasPending, Is.False);

        WritePayload(payload, 0, 8);
        batch.Queue(target, MakeHeader(Channel.Unreliable, 0), new BitData(payload), Channel.Unreliable);
        Assert.That(batch.hasPending, Is.True);

        batch.Flush();
        Assert.That(batch.hasPending, Is.False);
        Assert.That(backend.Count(target, Channel.Unreliable), Is.EqualTo(1));
    }

    [Test]
    public void HasPendingStaysFalseWhenMtuDropEmptiesTheBatch()
    {
        var target = new PlayerID(61, false);
        using var backend = new TestRPCBatchBackend { mtu = 64 };
        using var batch = new RPCBatch(backend, (_, _, _, _) => { });
        using var payload = BitPackerPool.Get();

        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
        try
        {
            WritePayload(payload, 0, 100);
            batch.Queue(target, MakeHeader(Channel.Unreliable, 0), new BitData(payload), Channel.Unreliable,
                MTUBehaviour.Drop);
        }
        finally
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
        }

        Assert.That(batch.hasPending, Is.False);
        Assert.That(backend.sent, Is.Empty);
    }

    [Test]
    public void FlushChannelSendsOnlyRequestedChannelAndKeepsOtherBatchesQueued()
    {
        var targets = new[]
        {
            new PlayerID(21, false),
            new PlayerID(22, false),
            new PlayerID(23, false)
        };
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        for (int sequence = 0; sequence < 3; sequence++)
        {
            WritePayload(payload, sequence, 8);
            batch.Queue(targets, MakeHeader(Channel.ReliableOrdered, sequence), new BitData(payload),
                Channel.ReliableOrdered);
            batch.Queue(targets, MakeHeader(Channel.Unreliable, sequence), new BitData(payload),
                Channel.Unreliable);
        }

        batch.FlushChannel(Channel.ReliableOrdered);
        Assert.That(backend.sent, Has.Count.EqualTo(targets.Length));
        for (int i = 0; i < backend.sent.Count; i++)
            Assert.That(backend.sent[i].channel, Is.EqualTo(Channel.ReliableOrdered));

        batch.Flush();
        Assert.That(backend.sent, Has.Count.EqualTo(targets.Length * 2));
        for (int i = targets.Length; i < backend.sent.Count; i++)
            Assert.That(backend.sent[i].channel, Is.EqualTo(Channel.Unreliable));

        backend.DeliverAll();
        for (int targetIdx = 0; targetIdx < targets.Length; targetIdx++)
        {
            for (int channelIdx = 0; channelIdx < 2; channelIdx++)
            {
                var channel = channelIdx == 0 ? Channel.ReliableOrdered : Channel.Unreliable;
                var key = new BatchKey { playerId = targets[targetIdx], channel = channel };
                Assert.That(received.TryGetValue(key, out var values), Is.True);
                Assert.That(values, Is.EqualTo(new[] { 0, 1, 2 }));
            }
        }
    }

    [Test]
    public void FilteredSmallFanoutSendsOnlyToIncludedTargets()
    {
        var targets = new[]
        {
            new PlayerID(31, false),
            new PlayerID(32, false),
            new PlayerID(33, false),
            new PlayerID(34, false)
        };
        var filter = new ObserverFilter(targets[0], true, targets[3], true);
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        WritePayload(payload, 0, 8);
        batch.Queue(targets, MakeHeader(Channel.ReliableOrdered, 0), new BitData(payload),
            Channel.ReliableOrdered, filter);
        batch.Flush();

        Assert.That(backend.Count(targets[0], Channel.ReliableOrdered), Is.Zero);
        Assert.That(backend.Count(targets[1], Channel.ReliableOrdered), Is.EqualTo(1));
        Assert.That(backend.Count(targets[2], Channel.ReliableOrdered), Is.EqualTo(1));
        Assert.That(backend.Count(targets[3], Channel.ReliableOrdered), Is.Zero);

        backend.DeliverAll();
        Assert.That(received.ContainsKey(new BatchKey
        {
            playerId = targets[0],
            channel = Channel.ReliableOrdered
        }), Is.False);
        Assert.That(received.ContainsKey(new BatchKey
        {
            playerId = targets[3],
            channel = Channel.ReliableOrdered
        }), Is.False);
    }

    [Test]
    public void HundredRecipientReliableOrderedFanoutSurvivesDivergenceAndMtuSplits()
    {
        const int targetCount = 100;
        const int sequenceCount = 96;
        var targets = new PlayerID[targetCount];
        var expected = new Dictionary<BatchKey, List<int>>();
        var received = new Dictionary<BatchKey, List<int>>();

        for (int i = 0; i < targets.Length; i++)
        {
            targets[i] = new PlayerID((ulong)(1000 + i), false);
            expected[new BatchKey
            {
                playerId = targets[i],
                channel = Channel.ReliableOrdered
            }] = new List<int>();
        }

        using var backend = new TestRPCBatchBackend { mtu = 192 };
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content));
        using var payload = BitPackerPool.Get();

        for (int sequence = 0; sequence < sequenceCount; sequence++)
        {
            int singledOut = (sequence * 17) % targetCount;
            if ((sequence & 3) == 0)
            {
                WritePayload(payload, sequence, 24 + sequence % 41);
                batch.Queue(targets[singledOut], MakeHeader(Channel.ReliableOrdered, sequence),
                    new BitData(payload), Channel.ReliableOrdered);
                expected[new BatchKey
                {
                    playerId = targets[singledOut],
                    channel = Channel.ReliableOrdered
                }].Add(sequence);
            }

            int skipA = (sequence * 7 + 3) % targetCount;
            int skipB = (sequence * 11 + 5) % targetCount;
            var filter = new ObserverFilter(targets[skipA], true, targets[skipB], true);
            WritePayload(payload, sequence, 32 + sequence % 73);
            batch.Queue(targets, MakeHeader(Channel.ReliableOrdered, sequence), new BitData(payload),
                Channel.ReliableOrdered, filter);

            for (int target = 0; target < targets.Length; target++)
            {
                if (target != skipA && target != skipB)
                {
                    expected[new BatchKey
                    {
                        playerId = targets[target],
                        channel = Channel.ReliableOrdered
                    }].Add(sequence);
                }
            }

            if (sequence % 13 == 12)
                batch.FlushChannel(Channel.ReliableOrdered);
        }

        batch.Flush();
        backend.DeliverAll();

        for (int i = 0; i < targets.Length; i++)
        {
            var key = new BatchKey
            {
                playerId = targets[i],
                channel = Channel.ReliableOrdered
            };
            Assert.That(received.TryGetValue(key, out var actual), Is.True, $"No RPCs received by {targets[i]}.");
            Assert.That(actual, Is.EqualTo(expected[key]), $"Reliable-ordered stream diverged for {targets[i]}.");
        }
    }

    [Test]
    public void ImmediateBatchSendsAsImmediatePacketAndRoundTrips()
    {
        var target = new PlayerID(70, false);
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content), sendAsImmediate: true);
        using var payload = BitPackerPool.Get();

        for (int sequence = 0; sequence < 3; sequence++)
        {
            WritePayload(payload, sequence, 8);
            batch.Queue(target, MakeHeader(Channel.Unreliable, sequence), new BitData(payload), Channel.Unreliable);
        }

        batch.Flush();

        Assert.That(backend.sent, Is.Empty);
        Assert.That(backend.sentImmediate, Has.Count.EqualTo(1));
        Assert.That(backend.sentImmediate[0].target, Is.EqualTo(target));
        Assert.That(backend.sentImmediate[0].channel, Is.EqualTo(Channel.Unreliable));
        Assert.That(backend.sentImmediate[0].count.value, Is.EqualTo(3));

        backend.DeliverAllImmediate(batch);
        Assert.That(received[new BatchKey { playerId = target, channel = Channel.Unreliable }],
            Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void ImmediateOversizedSoloEntryStaysOnImmediatePacket()
    {
        var target = new PlayerID(71, false);
        var received = new Dictionary<BatchKey, List<int>>();
        using var backend = new TestRPCBatchBackend { mtu = 64 };
        using var batch = new RPCBatch(backend,
            (_, header, content, _) => RecordReceived(backend, received, header, content), sendAsImmediate: true);
        using var payload = BitPackerPool.Get();

        WritePayload(payload, 0, 8);
        batch.Queue(target, MakeHeader(Channel.Unreliable, 0), new BitData(payload), Channel.Unreliable,
            MTUBehaviour.Fragment);
        WritePayload(payload, 1, 100);
        batch.Queue(target, MakeHeader(Channel.Unreliable, 1), new BitData(payload), Channel.Unreliable,
            MTUBehaviour.Fragment);

        batch.Flush();

        Assert.That(backend.sent, Is.Empty);
        Assert.That(backend.sentImmediate, Has.Count.EqualTo(2));
        Assert.That(backend.sentImmediate[1].count.value, Is.EqualTo(1));
        Assert.That(backend.sentImmediate[1].mtuOverride, Is.EqualTo(MTUExceededBehaviour.Fragment));

        backend.DeliverAllImmediate(batch);
        Assert.That(received[new BatchKey { playerId = target, channel = Channel.Unreliable }],
            Is.EqualTo(new[] { 0, 1 }));
    }

    static int FramingBitsPerEntry(bool consecutiveIds, int count, out int payloadBits)
    {
        using var backend = new TestRPCBatchBackend();
        using var batch = new RPCBatch(backend, (sender, header, content, asServer) => { });
        var targets = new List<PlayerID> { new PlayerID(1, false), new PlayerID(2, false), new PlayerID(3, false) };
        using var content = BitPackerPool.Get();
        content.WriteBits(0x12345678UL, 32);
        payloadBits = content.positionInBits;

        for (int i = 0; i < count; i++)
        {
            var header = new UnionRPCHeader(new NetworkIdentityRPCHeader
            {
                senderId = PlayerID.Server,
                networkId = new NetworkID((ulong)(consecutiveIds ? 200 + i : 200)),
                sceneId = new SceneID(0),
                rpcId = new Size(3),
                targetId = null
            });
            batch.Queue(targets, header, new BitData(content), Channel.ReliableOrdered);
        }

        batch.Flush();
        Assert.That(backend.sent.Count, Is.EqualTo(3));
        return backend.sent[0].data.positionInBits - payloadBits * count;
    }

    [Test]
    public void ObserverRpcBatchFramingPerEntry()
    {
        const int count = 100;
        int consecutive = FramingBitsPerEntry(true, count, out _);
        int identical = FramingBitsPerEntry(false, count, out _);
        Assert.That(consecutive, Is.LessThanOrEqualTo(24 * count), "a batched identity RPC to the next id should cost about 3 bytes of framing");
        Assert.That(identical, Is.LessThanOrEqualTo(4 * count));
    }
}
