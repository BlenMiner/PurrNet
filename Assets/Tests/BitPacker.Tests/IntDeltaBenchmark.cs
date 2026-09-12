using System;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using Debug = UnityEngine.Debug;

/// <summary>
/// Compares the live integer delta packers (prefix encoding) against the previous
/// varint wire format across realistic value streams. All strategies are exact;
/// measures size (avg bits/value) and speed (write+read ns/value).
/// Run from Unity Test Runner (Play Mode).
/// </summary>
public class IntDeltaBenchmark
{
    const int STREAM_LENGTH = 4096;
    const int MEASURE_PASSES = 50;

    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    delegate void WriteFn(BitPacker packer, long oldValue, long newValue);
    delegate long ReadFn(BitPacker packer, long oldValue);

    struct TypeCase
    {
        public string name;
        public WriteFn oldWrite;
        public ReadFn oldRead;
        public WriteFn liveWrite;
        public ReadFn liveRead;
        public WriteFn bucketWrite;
        public ReadFn bucketRead;
        public Func<long, long> truncate;
    }

    struct Stream
    {
        public string name;
        public long[] values;
    }

    // ───────────────────────────────────────────────
    //  Previous wire format (varint diffs), per type
    // ───────────────────────────────────────────────

    static void OldWriteByte(BitPacker packer, long oldValue, long newValue)
    {
        byte o = (byte)oldValue, n = (byte)newValue;
        bool hasChanged = o != n;
        packer.WriteBit(hasChanged);
        if (hasChanged)
            PackingIntegers.Write(packer, new PackedShort((short)(n - o)));
    }

    static long OldReadByte(BitPacker packer, long oldValue)
    {
        if (!packer.ReadBit()) return (byte)oldValue;
        PackedShort packed = default;
        PackingIntegers.Read(packer, ref packed);
        return (byte)((byte)oldValue + packed.value);
    }

    static void OldWriteUShort(BitPacker packer, long oldValue, long newValue)
    {
        ushort o = (ushort)oldValue, n = (ushort)newValue;
        bool hasChanged = o != n;
        packer.WriteBit(hasChanged);
        if (hasChanged)
            PackingIntegers.Write(packer, new PackedInt((int)((uint)n - o)));
    }

    static long OldReadUShort(BitPacker packer, long oldValue)
    {
        if (!packer.ReadBit()) return (ushort)oldValue;
        PackedInt packed = default;
        PackingIntegers.Read(packer, ref packed);
        return (ushort)((ushort)oldValue + packed.value);
    }

    static void OldWriteInt(BitPacker packer, long oldValue, long newValue)
    {
        int o = (int)oldValue, n = (int)newValue;
        bool hasChanged = o != n;
        packer.WriteBit(hasChanged);
        if (hasChanged)
            PackingIntegers.Write(packer, new PackedLong(n - (long)o));
    }

    static long OldReadInt(BitPacker packer, long oldValue)
    {
        if (!packer.ReadBit()) return (int)oldValue;
        PackedLong packed = default;
        PackingIntegers.Read(packer, ref packed);
        return (int)((int)oldValue + packed.value);
    }

    static void OldWriteLong(BitPacker packer, long oldValue, long newValue)
    {
        bool hasChanged = oldValue != newValue;
        packer.WriteBit(hasChanged);
        if (hasChanged)
            PackingIntegers.Write(packer, new PackedLong(newValue - oldValue));
    }

    static long OldReadLong(BitPacker packer, long oldValue)
    {
        if (!packer.ReadBit()) return oldValue;
        PackedLong packed = default;
        PackingIntegers.Read(packer, ref packed);
        return oldValue + packed.value;
    }

    // ───────────────────────────────────────────────
    //  Live packers, per type
    // ───────────────────────────────────────────────

    static void LiveWriteByte(BitPacker packer, long oldValue, long newValue) =>
        DeltaPacker<byte>.Write(packer, (byte)oldValue, (byte)newValue);

    static long LiveReadByte(BitPacker packer, long oldValue)
    {
        byte value = default;
        DeltaPacker<byte>.Read(packer, (byte)oldValue, ref value);
        return value;
    }

    static void LiveWriteUShort(BitPacker packer, long oldValue, long newValue) =>
        DeltaPacker<ushort>.Write(packer, (ushort)oldValue, (ushort)newValue);

    static long LiveReadUShort(BitPacker packer, long oldValue)
    {
        ushort value = default;
        DeltaPacker<ushort>.Read(packer, (ushort)oldValue, ref value);
        return value;
    }

    static void LiveWriteInt(BitPacker packer, long oldValue, long newValue) =>
        DeltaPacker<int>.Write(packer, (int)oldValue, (int)newValue);

    static long LiveReadInt(BitPacker packer, long oldValue)
    {
        int value = default;
        DeltaPacker<int>.Read(packer, (int)oldValue, ref value);
        return value;
    }

    static void LiveWriteLong(BitPacker packer, long oldValue, long newValue) =>
        DeltaPacker<long>.Write(packer, oldValue, newValue);

    static long LiveReadLong(BitPacker packer, long oldValue)
    {
        long value = default;
        DeltaPacker<long>.Read(packer, oldValue, ref value);
        return value;
    }

    // ───────────────────────────────────────────────
    //  Candidate: 2-bit bucket selector + fixed-width payload
    // ───────────────────────────────────────────────

    static readonly byte[] bucketsByte = { 2, 4, 6, 8 };
    static readonly byte[] bucketsUShort = { 4, 8, 12, 16 };
    static readonly byte[] bucketsInt = { 4, 8, 16, 32 };
    static readonly byte[] bucketsLong = { 4, 8, 24, 64 };

    static void BucketWriteDiff(BitPacker packer, ulong zigzag, byte[] buckets)
    {
        int bits = 64 - PackingIntegers.CountLeadingZeroBits(zigzag);
        int selector = 0;
        while (bits > buckets[selector]) selector++;
        packer.WriteBits((ulong)selector, 2);
        packer.WriteBits(zigzag, buckets[selector]);
    }

    static ulong BucketReadDiff(BitPacker packer, byte[] buckets)
    {
        int selector = (int)packer.ReadBits(2);
        return packer.ReadBits(buckets[selector]);
    }

    static void BucketWriteByte(BitPacker packer, long oldValue, long newValue)
    {
        byte o = (byte)oldValue, n = (byte)newValue;
        if (o == n) { packer.WriteBit(false); return; }
        packer.WriteBit(true);
        BucketWriteDiff(packer, PackingIntegers.ZigzagEncode((sbyte)(n - o)), bucketsByte);
    }

    static long BucketReadByte(BitPacker packer, long oldValue)
    {
        if (!packer.ReadBit()) return (byte)oldValue;
        sbyte diff = PackingIntegers.ZigzagDecode((byte)BucketReadDiff(packer, bucketsByte));
        return (byte)((byte)oldValue + diff);
    }

    static void BucketWriteUShort(BitPacker packer, long oldValue, long newValue)
    {
        ushort o = (ushort)oldValue, n = (ushort)newValue;
        if (o == n) { packer.WriteBit(false); return; }
        packer.WriteBit(true);
        BucketWriteDiff(packer, PackingIntegers.ZigzagEncode((short)(n - o)), bucketsUShort);
    }

    static long BucketReadUShort(BitPacker packer, long oldValue)
    {
        if (!packer.ReadBit()) return (ushort)oldValue;
        short diff = PackingIntegers.ZigzagDecode((ushort)BucketReadDiff(packer, bucketsUShort));
        return (ushort)((ushort)oldValue + diff);
    }

    static void BucketWriteInt(BitPacker packer, long oldValue, long newValue)
    {
        int o = (int)oldValue, n = (int)newValue;
        if (o == n) { packer.WriteBit(false); return; }
        packer.WriteBit(true);
        BucketWriteDiff(packer, PackingIntegers.ZigzagEncode(n - o), bucketsInt);
    }

    static long BucketReadInt(BitPacker packer, long oldValue)
    {
        if (!packer.ReadBit()) return (int)oldValue;
        int diff = PackingIntegers.ZigzagDecode((uint)BucketReadDiff(packer, bucketsInt));
        return (int)oldValue + diff;
    }

    static void BucketWriteLong(BitPacker packer, long oldValue, long newValue)
    {
        if (oldValue == newValue) { packer.WriteBit(false); return; }
        packer.WriteBit(true);
        BucketWriteDiff(packer, PackingIntegers.ZigzagEncode(newValue - oldValue), bucketsLong);
    }

    static long BucketReadLong(BitPacker packer, long oldValue)
    {
        if (!packer.ReadBit()) return oldValue;
        long diff = PackingIntegers.ZigzagDecode(BucketReadDiff(packer, bucketsLong));
        return oldValue + diff;
    }

    static TypeCase[] GetTypeCases() => new[]
    {
        new TypeCase { name = "byte", oldWrite = OldWriteByte, oldRead = OldReadByte, liveWrite = LiveWriteByte, liveRead = LiveReadByte, bucketWrite = BucketWriteByte, bucketRead = BucketReadByte, truncate = v => (byte)v },
        new TypeCase { name = "ushort", oldWrite = OldWriteUShort, oldRead = OldReadUShort, liveWrite = LiveWriteUShort, liveRead = LiveReadUShort, bucketWrite = BucketWriteUShort, bucketRead = BucketReadUShort, truncate = v => (ushort)v },
        new TypeCase { name = "int", oldWrite = OldWriteInt, oldRead = OldReadInt, liveWrite = LiveWriteInt, liveRead = LiveReadInt, bucketWrite = BucketWriteInt, bucketRead = BucketReadInt, truncate = v => (int)v },
        new TypeCase { name = "long", oldWrite = OldWriteLong, oldRead = OldReadLong, liveWrite = LiveWriteLong, liveRead = LiveReadLong, bucketWrite = BucketWriteLong, bucketRead = BucketReadLong, truncate = v => v },
    };

    // ───────────────────────────────────────────────
    //  Value streams (deterministic seeds)
    // ───────────────────────────────────────────────

    static Stream[] GetStreams()
    {
        return new[]
        {
            new Stream { name = "Counter+1", values = Counter() },
            new Stream { name = "SmallSteps", values = SmallSteps() },
            new Stream { name = "MostlyIdle", values = MostlyIdle() },
            new Stream { name = "RandomFull", values = RandomFull() },
        };
    }

    static long[] Counter()
    {
        var values = new long[STREAM_LENGTH];
        for (int i = 0; i < values.Length; i++)
            values[i] = i + 1;
        return values;
    }

    static long[] SmallSteps()
    {
        var rng = new Random(2001);
        var values = new long[STREAM_LENGTH];
        long current = 500;
        for (int i = 0; i < values.Length; i++)
        {
            current += rng.Next(1, 51) * (rng.Next(2) == 0 ? 1 : -1);
            values[i] = current;
        }
        return values;
    }

    static long[] MostlyIdle()
    {
        var rng = new Random(2002);
        var values = new long[STREAM_LENGTH];
        long current = 100;
        for (int i = 0; i < values.Length; i++)
        {
            if (rng.Next(20) == 0)
                current += rng.Next(1, 11) * (rng.Next(2) == 0 ? 1 : -1);
            values[i] = current;
        }
        return values;
    }

    static long[] RandomFull()
    {
        var rng = new Random(2003);
        var values = new long[STREAM_LENGTH];
        for (int i = 0; i < values.Length; i++)
            values[i] = ((long)rng.Next(int.MinValue, int.MaxValue) << 32) | (uint)rng.Next(int.MinValue, int.MaxValue);
        return values;
    }

    // ───────────────────────────────────────────────
    //  Size + speed comparison (verifies round-trips inline)
    // ───────────────────────────────────────────────

    [Test]
    public void Benchmark_IntDelta_Compare()
    {
        var typeCases = GetTypeCases();
        var streams = GetStreams();

        var sb = new StringBuilder();
        sb.Append("[IntDelta] avg bits/value (old varint / live / bucket)\n");
        sb.Append($"{"case",-22}{"old",10}{"live",10}{"bucket",10}\n");

        var speed = new StringBuilder();
        speed.Append("[IntDelta] write+read ns/value (old varint / live / bucket)\n");
        speed.Append($"{"case",-22}{"old",10}{"live",10}{"bucket",10}\n");

        foreach (var typeCase in typeCases)
        {
            foreach (var stream in streams)
            {
                using var packer = BitPackerPool.Get();

                double oldBits = MeasureBits(packer, stream.values, typeCase.oldWrite, typeCase.oldRead, typeCase.truncate);
                double liveBits = MeasureBits(packer, stream.values, typeCase.liveWrite, typeCase.liveRead, typeCase.truncate);
                double bucketBits = MeasureBits(packer, stream.values, typeCase.bucketWrite, typeCase.bucketRead, typeCase.truncate);

                double oldNs = MeasureSpeed(packer, stream.values, typeCase.oldWrite, typeCase.oldRead);
                double liveNs = MeasureSpeed(packer, stream.values, typeCase.liveWrite, typeCase.liveRead);
                double bucketNs = MeasureSpeed(packer, stream.values, typeCase.bucketWrite, typeCase.bucketRead);

                string label = $"{typeCase.name} {stream.name}";
                sb.Append($"{label,-22}{oldBits,10:F1}{liveBits,10:F1}{bucketBits,10:F1}\n");
                speed.Append($"{label,-22}{oldNs,10:F0}{liveNs,10:F0}{bucketNs,10:F0}\n");
            }
        }

        Debug.Log(sb.ToString());
        Debug.Log(speed.ToString());
    }

    double MeasureBits(BitPacker packer, long[] values, WriteFn write, ReadFn read, Func<long, long> truncate)
    {
        packer.ResetPositionAndMode(false);
        long previous = 0;
        for (int i = 0; i < values.Length; i++)
        {
            write(packer, previous, values[i]);
            previous = values[i];
        }
        double bitsPerValue = (double)packer.positionInBits / values.Length;

        packer.ResetPositionAndMode(true);
        previous = 0;
        for (int i = 0; i < values.Length; i++)
        {
            long decoded = read(packer, previous);
            Assert.That(decoded, Is.EqualTo(truncate(values[i])), $"Round-trip failed at index {i}");
            previous = values[i];
        }

        return bitsPerValue;
    }

    double MeasureSpeed(BitPacker packer, long[] values, WriteFn write, ReadFn read)
    {
        RunPass(packer, values, write, read);

        var sw = Stopwatch.StartNew();
        for (int pass = 0; pass < MEASURE_PASSES; pass++)
            RunPass(packer, values, write, read);
        sw.Stop();

        long ops = (long)values.Length * MEASURE_PASSES;
        return sw.Elapsed.TotalMilliseconds * 1_000_000.0 / ops;
    }

    static void RunPass(BitPacker packer, long[] values, WriteFn write, ReadFn read)
    {
        packer.ResetPositionAndMode(false);
        long previous = 0;
        for (int i = 0; i < values.Length; i++)
        {
            write(packer, previous, values[i]);
            previous = values[i];
        }

        packer.ResetPositionAndMode(true);
        previous = 0;
        for (int i = 0; i < values.Length; i++)
        {
            read(packer, previous);
            previous = values[i];
        }
    }
}
