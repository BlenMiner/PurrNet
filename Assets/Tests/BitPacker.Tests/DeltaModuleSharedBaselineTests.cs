using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class DeltaModuleSharedBaselineTests
{
    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    struct WriteOp
    {
        public Vector3 value;
        public bool ackAfter;
        public uint ackId;
    }

    static PlayerID Player(int i) => new PlayerID((ulong)i, false);

    static (int bits, byte[] bytes) BitsOf(BitPacker packer)
    {
        int bits = packer.positionInBits;
        var data = packer.ToByteData();
        var result = new byte[data.length];
        data.span.CopyTo(result);
        int rem = bits & 7;
        if (rem != 0 && result.Length > 0)
            result[^1] &= (byte)((1 << rem) - 1);
        return (bits, result);
    }

    static void AssertMatchesPerPlayerReplay(uint keyHash, Dictionary<int, List<WriteOp>> opsPerPlayer, int ticks)
    {
        var sharedModule = new DeltaModule(null, null);
        var sharedBits = new Dictionary<(int player, int tick), (int bits, byte[] bytes)>();

        for (int tick = 0; tick < ticks; tick++)
        {
            foreach (var (playerIdx, ops) in opsPerPlayer)
            {
                if (tick >= ops.Count)
                    continue;

                var op = ops[tick];
                using var packer = BitPackerPool.Get();
                PackedUInt cachedKey = default;
                sharedModule.Write(packer, Player(playerIdx), keyHash, op.value, ref cachedKey);
                sharedBits[(playerIdx, tick)] = BitsOf(packer);

                if (op.ackAfter)
                    sharedModule.ConfirmDeliveryForTests<Vector3>(Player(playerIdx), keyHash, new PackedUInt(op.ackId));
            }
        }

        foreach (var (playerIdx, ops) in opsPerPlayer)
        {
            var soloModule = new DeltaModule(null, null);

            for (int tick = 0; tick < ops.Count; tick++)
            {
                var op = ops[tick];
                using var packer = BitPackerPool.Get();
                PackedUInt cachedKey = default;
                soloModule.Write(packer, Player(playerIdx), keyHash, op.value, ref cachedKey);

                var solo = BitsOf(packer);
                var shared = sharedBits[(playerIdx, tick)];
                Assert.That(shared.bits, Is.EqualTo(solo.bits),
                    $"Bit length differs from solo replay for player {playerIdx} at tick {tick}");
                Assert.That(shared.bytes, Is.EqualTo(solo.bytes),
                    $"Shared-module bits differ from solo replay for player {playerIdx} at tick {tick}");

                if (op.ackAfter)
                    soloModule.ConfirmDeliveryForTests<Vector3>(Player(playerIdx), keyHash, new PackedUInt(op.ackId));
            }
        }
    }

    static List<WriteOp> Sequence(params WriteOp[] ops) => new(ops);

    static WriteOp Send(Vector3 v) => new() { value = v };
    static WriteOp SendAndAck(Vector3 v, uint ackId) => new() { value = v, ackAfter = true, ackId = ackId };

    [Test]
    public void SharedFanout_FreshPlayers_MatchesSoloReplay()
    {
        var ops = new Dictionary<int, List<WriteOp>>();
        for (int p = 1; p <= 4; p++)
        {
            ops[p] = Sequence(
                Send(new Vector3(1, 2, 3)),
                Send(new Vector3(1.5f, 2, 3)),
                Send(new Vector3(1.5f, 2.5f, 3)));
        }

        AssertMatchesPerPlayerReplay(1234, ops, 3);
    }

    [Test]
    public void SharedFanout_ClusteredAcks_MatchesSoloReplay()
    {
        var ops = new Dictionary<int, List<WriteOp>>();
        for (int p = 1; p <= 4; p++)
        {
            ops[p] = Sequence(
                SendAndAck(new Vector3(1, 2, 3), 1),
                SendAndAck(new Vector3(4, 5, 6), 2),
                Send(new Vector3(7, 8, 9)),
                Send(new Vector3(7, 8, 9)),
                SendAndAck(new Vector3(10, 11, 12), 4));
        }

        AssertMatchesPerPlayerReplay(77, ops, 5);
    }

    [Test]
    public void SharedFanout_DivergentAcks_MatchesSoloReplay()
    {
        var ops = new Dictionary<int, List<WriteOp>>
        {
            [1] = Sequence(
                SendAndAck(new Vector3(1, 1, 1), 1),
                SendAndAck(new Vector3(2, 2, 2), 2),
                SendAndAck(new Vector3(3, 3, 3), 3),
                Send(new Vector3(4, 4, 4))),
            [2] = Sequence(
                SendAndAck(new Vector3(1, 1, 1), 1),
                Send(new Vector3(2, 2, 2)),
                Send(new Vector3(3, 3, 3)),
                Send(new Vector3(4, 4, 4))),
            [3] = Sequence(
                Send(new Vector3(1, 1, 1)),
                Send(new Vector3(2, 2, 2)),
                Send(new Vector3(3, 3, 3)),
                Send(new Vector3(4, 4, 4))),
            [4] = Sequence(
                Send(new Vector3(1, 1, 1)),
                Send(new Vector3(2, 2, 2)),
                SendAndAck(new Vector3(3, 3, 3), 2),
                Send(new Vector3(4, 4, 4)))
        };

        AssertMatchesPerPlayerReplay(9001, ops, 4);
    }

    [Test]
    public void SharedFanout_ValueToggle_MatchesSoloReplay()
    {
        var v = new Vector3(5, 5, 5);
        var w = new Vector3(9, 9, 9);
        var ops = new Dictionary<int, List<WriteOp>>();
        for (int p = 1; p <= 3; p++)
            ops[p] = Sequence(SendAndAck(v, 1), SendAndAck(w, 2), SendAndAck(v, 3), Send(w));

        AssertMatchesPerPlayerReplay(4242, ops, 4);
    }

    [Test]
    public void SharedFanout_StreamDecodesToSentValues()
    {
        const uint keyHash = 555;
        var module = new DeltaModule(null, null);
        var values = new[]
        {
            new Vector3(1, 2, 3),
            new Vector3(1.25f, 2, 3),
            new Vector3(1.25f, 2, 3),
            new Vector3(-4, 100, 0.5f)
        };

        var recvHistory = new Dictionary<int, Dictionary<uint, Vector3>>();
        var pendingAck = new Dictionary<int, uint>();

        for (int tick = 0; tick < values.Length; tick++)
        {
            for (int p = 1; p <= 3; p++)
            {
                using var packer = BitPackerPool.Get();
                PackedUInt cachedKey = default;
                module.Write(packer, Player(p), keyHash, values[tick], ref cachedKey);

                packer.ResetPositionAndMode(true);

                PackedUInt readCache = default;
                PackedUInt lastConfirmedId = default;
                DeltaPacker<PackedUInt>.Read(packer, readCache, ref lastConfirmedId);
                readCache = lastConfirmedId;

                bool changed = false;
                Packer<bool>.Read(packer, ref changed);

                var history = recvHistory.TryGetValue(p, out var h) ? h : recvHistory[p] = new Dictionary<uint, Vector3>();

                Vector3 decoded;
                if (changed)
                {
                    Vector3 oldValue = default;
                    if (lastConfirmedId.value != 0)
                        Assert.That(history.TryGetValue(lastConfirmedId.value, out oldValue), Is.True,
                            $"Sender referenced baseline {lastConfirmedId.value} the receiver never stored (player {p}, tick {tick})");

                    decoded = default;
                    DeltaPacker<Vector3>.Read(packer, oldValue, ref decoded);

                    PackedUInt valueId = default;
                    DeltaPacker<PackedUInt>.Read(packer, readCache, ref valueId);
                    history[valueId.value] = decoded;
                    pendingAck[p] = valueId.value;
                }
                else
                {
                    decoded = lastConfirmedId.value != 0 ? history[lastConfirmedId.value] : default;
                }

                Assert.That(decoded, Is.EqualTo(values[tick]),
                    $"Decoded value mismatch for player {p} at tick {tick}");
            }

            for (int p = 1; p <= 2; p++)
            {
                if (pendingAck.TryGetValue(p, out var id))
                    module.ConfirmDeliveryForTests<Vector3>(Player(p), keyHash, new PackedUInt(id));
            }
        }
    }

    [Test]
    public void Eligibility_GateIsAsDesigned()
    {
        Assert.That(DeltaSharedEncodeInfo<Vector3>.eligible, Is.True);
        Assert.That(DeltaSharedEncodeInfo<Quaternion>.eligible, Is.True);
        Assert.That(DeltaSharedEncodeInfo<int>.eligible, Is.True);
        Assert.That(DeltaSharedEncodeInfo<uint>.eligible, Is.True);
        Assert.That(DeltaSharedEncodeInfo<float>.eligible, Is.True);
        Assert.That(DeltaSharedEncodeInfo<PlainStruct>.eligible, Is.False);
        Assert.That(DeltaSharedEncodeInfo<string>.eligible, Is.False);
        Assert.That(DeltaSharedEncodeInfo<StructWithReference>.eligible, Is.False);
    }

    struct PlainStruct { public float x; }
#pragma warning disable CS0649
    struct StructWithReference : IEquatable<StructWithReference>
    {
        public string s;
        public bool Equals(StructWithReference other) => s == other.s;
    }
#pragma warning restore CS0649

    [Test]
    [TestCase(10)]
    [TestCase(50)]
    [TestCase(100)]
    public void Benchmark_SharedBaseline_HitVsMiss(int playerCount)
    {
        const int keys = 50;
        const int warmupTicks = 5;
        const int measureTicks = 50;

        double clusteredMs = RunFanoutBenchmark(playerCount, keys, warmupTicks, measureTicks, clusteredAcks: true);
        double divergentMs = RunFanoutBenchmark(playerCount, keys, warmupTicks, measureTicks, clusteredAcks: false);

        Debug.Log($"[DeltaModule Shared Baseline] {playerCount} players x {keys} keys | " +
                  $"clustered (shared) {clusteredMs:F3} ms/tick | " +
                  $"divergent (per-player) {divergentMs:F3} ms/tick | " +
                  $"{divergentMs / clusteredMs:F2}x");
    }

    static double RunFanoutBenchmark(int playerCount, int keys, int warmupTicks, int measureTicks, bool clusteredAcks)
    {
        var module = new DeltaModule(null, null);
        int tickCounter = 0;

        using var packer = BitPackerPool.Get();

        void Tick()
        {
            tickCounter++;
            for (uint k = 0; k < keys; k++)
            {
                var value = new Vector3(tickCounter * 0.1f, k, tickCounter);
                for (int p = 1; p <= playerCount; p++)
                {
                    packer.ResetPositionAndMode(false);
                    PackedUInt cachedKey = default;
                    module.Write(packer, Player(p), k, value, ref cachedKey);
                }
            }

            for (int p = 1; p <= playerCount; p++)
            {
                int ackTick = clusteredAcks ? tickCounter : tickCounter - p % 8;
                if (ackTick < 1)
                    continue;

                for (uint k = 0; k < keys; k++)
                    module.ConfirmDeliveryForTests<Vector3>(Player(p), k, new PackedUInt((uint)ackTick));
            }
        }

        for (int t = 0; t < warmupTicks; t++)
            Tick();

        var sw = Stopwatch.StartNew();
        for (int t = 0; t < measureTicks; t++)
            Tick();
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds / measureTicks;
    }
}
