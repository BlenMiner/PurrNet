using System;
using System.Diagnostics;
using System.Text;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using Debug = UnityEngine.Debug;

/// <summary>
/// Compares bit-accurate (lossless, determinism-safe) float delta compression strategies.
/// Every strategy round-trips to the exact same IEEE-754 bit pattern; the benchmark measures
/// size (avg bits/value) and speed (write+read ns/value) across realistic value streams.
/// Run from Unity Test Runner (Edit Mode).
/// </summary>
public class FloatDeltaBenchmark
{
    const int STREAM_LENGTH = 4096;
    const int MEASURE_PASSES = 50;

    [OneTimeSetUp]
    public void Init()
    {
        NetworkManager.LoadOrGenerateHashes();
    }

    delegate void WriteFn(BitPacker packer, float oldValue, float newValue);
    delegate float ReadFn(BitPacker packer, float oldValue);

    struct Strategy
    {
        public string name;
        public WriteFn write;
        public ReadFn read;
    }

    struct Stream
    {
        public string name;
        public float[] values;
    }

    static uint Bits(float value) => (uint)BitConverter.SingleToInt32Bits(value);
    static float FromBits(uint bits) => BitConverter.Int32BitsToSingle((int)bits);

    // ───────────────────────────────────────────────
    //  Strategies (all bit-exact)
    // ───────────────────────────────────────────────

    // Current PurrNet behaviour: change flag + raw 32 bits.
    static void Write_Raw32(BitPacker packer, float oldValue, float newValue)
    {
        uint oldBits = Bits(oldValue), newBits = Bits(newValue);
        if (oldBits == newBits)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        packer.WriteBits(newBits, 32);
    }

    static float Read_Raw32(BitPacker packer, float oldValue)
    {
        if (!packer.ReadBit()) return oldValue;
        return FromBits((uint)packer.ReadBits(32));
    }

    // Bit-pattern subtraction + zigzag + 7-bit varint (mirrors the existing double path).
    static void Write_SubVarint(BitPacker packer, float oldValue, float newValue)
    {
        uint oldBits = Bits(oldValue), newBits = Bits(newValue);
        if (oldBits == newBits)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        int diff = (int)(newBits - oldBits);
        PackingIntegers.Write(packer, new PackedUInt(PackingIntegers.ZigzagEncode(diff)));
    }

    static float Read_SubVarint(BitPacker packer, float oldValue)
    {
        if (!packer.ReadBit()) return oldValue;
        PackedUInt packed = default;
        PackingIntegers.Read(packer, ref packed);
        uint diff = (uint)PackingIntegers.ZigzagDecode(packed.value);
        return FromBits(Bits(oldValue) + diff);
    }

    // XOR of bit patterns + 7-bit varint.
    static void Write_XorVarint(BitPacker packer, float oldValue, float newValue)
    {
        uint oldBits = Bits(oldValue), newBits = Bits(newValue);
        if (oldBits == newBits)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        PackingIntegers.Write(packer, new PackedUInt(oldBits ^ newBits));
    }

    static float Read_XorVarint(BitPacker packer, float oldValue)
    {
        if (!packer.ReadBit()) return oldValue;
        PackedUInt packed = default;
        PackingIntegers.Read(packer, ref packed);
        return FromBits(Bits(oldValue) ^ packed.value);
    }

    // XOR + 5-bit significant-bit-count prefix + only the significant bits (Gorilla-style).
    static void Write_XorPrefix(BitPacker packer, float oldValue, float newValue)
    {
        uint oldBits = Bits(oldValue), newBits = Bits(newValue);
        if (oldBits == newBits)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        uint xor = oldBits ^ newBits;
        int bitCount = 64 - PackingIntegers.CountLeadingZeroBits(xor);
        packer.WriteBits((ulong)(bitCount - 1), 5);
        packer.WriteBits(xor, (byte)bitCount);
    }

    static float Read_XorPrefix(BitPacker packer, float oldValue)
    {
        if (!packer.ReadBit()) return oldValue;
        int bitCount = (int)packer.ReadBits(5) + 1;
        uint xor = (uint)packer.ReadBits((byte)bitCount);
        return FromBits(Bits(oldValue) ^ xor);
    }

    // Bit-pattern subtraction + zigzag + 5-bit prefix + significant bits.
    static void Write_SubPrefix(BitPacker packer, float oldValue, float newValue)
    {
        uint oldBits = Bits(oldValue), newBits = Bits(newValue);
        if (oldBits == newBits)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        uint zigzag = PackingIntegers.ZigzagEncode((int)(newBits - oldBits));
        int bitCount = 64 - PackingIntegers.CountLeadingZeroBits(zigzag);
        packer.WriteBits((ulong)(bitCount - 1), 5);
        packer.WriteBits(zigzag, (byte)bitCount);
    }

    static float Read_SubPrefix(BitPacker packer, float oldValue)
    {
        if (!packer.ReadBit()) return oldValue;
        int bitCount = (int)packer.ReadBits(5) + 1;
        uint zigzag = (uint)packer.ReadBits((byte)bitCount);
        uint diff = (uint)PackingIntegers.ZigzagDecode(zigzag);
        return FromBits(Bits(oldValue) + diff);
    }

    // 4-bit mask of changed bytes + the changed bytes (Quake-style).
    static void Write_ByteMask(BitPacker packer, float oldValue, float newValue)
    {
        uint oldBits = Bits(oldValue), newBits = Bits(newValue);
        if (oldBits == newBits)
        {
            packer.WriteBit(false);
            return;
        }

        packer.WriteBit(true);
        uint xor = oldBits ^ newBits;
        uint mask = 0;
        for (int i = 0; i < 4; i++)
            if (((xor >> (i * 8)) & 0xFF) != 0)
                mask |= 1u << i;

        packer.WriteBits(mask, 4);
        for (int i = 0; i < 4; i++)
            if ((mask & (1u << i)) != 0)
                packer.WriteBits((newBits >> (i * 8)) & 0xFF, 8);
    }

    static float Read_ByteMask(BitPacker packer, float oldValue)
    {
        if (!packer.ReadBit()) return oldValue;
        uint oldBits = Bits(oldValue);
        uint mask = (uint)packer.ReadBits(4);
        uint result = oldBits;
        for (int i = 0; i < 4; i++)
        {
            if ((mask & (1u << i)) == 0) continue;
            uint b = (uint)packer.ReadBits(8);
            result = (result & ~(0xFFu << (i * 8))) | (b << (i * 8));
        }
        return FromBits(result);
    }

    static void Write_Live(BitPacker packer, float oldValue, float newValue) =>
        DeltaPacker<float>.Write(packer, oldValue, newValue);

    static float Read_Live(BitPacker packer, float oldValue)
    {
        float value = default;
        DeltaPacker<float>.Read(packer, oldValue, ref value);
        return value;
    }

    static Strategy[] GetStrategies() => new[]
    {
        new Strategy { name = "Live", write = Write_Live, read = Read_Live },
        new Strategy { name = "Raw32", write = Write_Raw32, read = Read_Raw32 },
        new Strategy { name = "Sub+Varint", write = Write_SubVarint, read = Read_SubVarint },
        new Strategy { name = "Xor+Varint", write = Write_XorVarint, read = Read_XorVarint },
        new Strategy { name = "Xor+Prefix", write = Write_XorPrefix, read = Read_XorPrefix },
        new Strategy { name = "Sub+Prefix", write = Write_SubPrefix, read = Read_SubPrefix },
        new Strategy { name = "ByteMask", write = Write_ByteMask, read = Read_ByteMask },
    };

    // ───────────────────────────────────────────────
    //  Value streams (deterministic seeds)
    // ───────────────────────────────────────────────

    static Stream[] GetStreams()
    {
        return new[]
        {
            new Stream { name = "SmoothPos 60Hz", values = SmoothPosition(0.05f) },
            new Stream { name = "FastPos 60Hz", values = SmoothPosition(1.5f) },
            new Stream { name = "SineRot", values = Sine() },
            new Stream { name = "MostlyIdle", values = MostlyIdle() },
            new Stream { name = "RandomFull", values = RandomFull() },
        };
    }

    // integrated velocity walk, like a networked transform coordinate
    static float[] SmoothPosition(float speedScale)
    {
        var rng = new Random(1234 + (int)(speedScale * 100));
        var values = new float[STREAM_LENGTH];
        float pos = 10f, vel = 0f;
        const float dt = 1f / 60f;
        for (int i = 0; i < values.Length; i++)
        {
            vel += ((float)rng.NextDouble() - 0.5f) * speedScale;
            pos += vel * dt;
            values[i] = pos;
        }
        return values;
    }

    // smooth oscillation, like a quaternion component during rotation
    static float[] Sine()
    {
        var values = new float[STREAM_LENGTH];
        for (int i = 0; i < values.Length; i++)
            values[i] = (float)Math.Sin(i * (1.0 / 60.0) * 0.7);
        return values;
    }

    // value that only changes occasionally (health, ammo-style)
    static float[] MostlyIdle()
    {
        var rng = new Random(777);
        var values = new float[STREAM_LENGTH];
        float current = 100f;
        for (int i = 0; i < values.Length; i++)
        {
            if (rng.Next(20) == 0)
                current -= (float)rng.NextDouble() * 15f;
            values[i] = current;
        }
        return values;
    }

    // uncorrelated full-range floats, worst case
    static float[] RandomFull()
    {
        var rng = new Random(4242);
        var values = new float[STREAM_LENGTH];
        for (int i = 0; i < values.Length; i++)
        {
            var bits = (uint)rng.Next(int.MinValue, int.MaxValue);
            // avoid NaN payload ambiguity concerns in generation; NaNs are still valid for the codecs
            values[i] = FromBits(bits);
        }
        return values;
    }

    // ───────────────────────────────────────────────
    //  Correctness: every strategy must round-trip bit-exact
    // ───────────────────────────────────────────────

    [Test]
    public void AllStrategies_AreBitExact()
    {
        var strategies = GetStrategies();
        var streams = GetStreams();

        foreach (var stream in streams)
        {
            foreach (var strategy in strategies)
            {
                using var packer = BitPackerPool.Get();
                packer.ResetPositionAndMode(false);

                float previous = 0f;
                for (int i = 0; i < stream.values.Length; i++)
                {
                    strategy.write(packer, previous, stream.values[i]);
                    previous = stream.values[i];
                }

                packer.ResetPositionAndMode(true);
                previous = 0f;
                for (int i = 0; i < stream.values.Length; i++)
                {
                    float decoded = strategy.read(packer, previous);
                    Assert.That(Bits(decoded), Is.EqualTo(Bits(stream.values[i])),
                        $"{strategy.name} not bit-exact on '{stream.name}' at index {i}");
                    previous = decoded;
                }
            }
        }
    }

    // ───────────────────────────────────────────────
    //  Size + speed comparison
    // ───────────────────────────────────────────────

    [Test]
    public void Benchmark_FloatDelta_Compare()
    {
        var strategies = GetStrategies();
        var streams = GetStreams();

        var sizeTable = new double[streams.Length, strategies.Length];
        var speedTable = new double[streams.Length, strategies.Length];

        for (int s = 0; s < streams.Length; s++)
        {
            var stream = streams[s];
            for (int k = 0; k < strategies.Length; k++)
            {
                var strategy = strategies[k];
                using var packer = BitPackerPool.Get();

                // size (single pass)
                packer.ResetPositionAndMode(false);
                float previous = 0f;
                for (int i = 0; i < stream.values.Length; i++)
                {
                    strategy.write(packer, previous, stream.values[i]);
                    previous = stream.values[i];
                }
                sizeTable[s, k] = (double)packer.positionInBits / stream.values.Length;

                // warmup
                RunPass(packer, stream.values, strategy);

                // speed (write + read)
                var sw = Stopwatch.StartNew();
                for (int pass = 0; pass < MEASURE_PASSES; pass++)
                    RunPass(packer, stream.values, strategy);
                sw.Stop();

                long ops = (long)stream.values.Length * MEASURE_PASSES;
                speedTable[s, k] = sw.Elapsed.TotalMilliseconds * 1_000_000.0 / ops;
            }
        }

        Debug.Log(RenderTable("avg bits/value", streams, strategies, sizeTable, "F1"));
        Debug.Log(RenderTable("write+read ns/value", streams, strategies, speedTable, "F0"));
    }

    static void RunPass(BitPacker packer, float[] values, Strategy strategy)
    {
        packer.ResetPositionAndMode(false);
        float previous = 0f;
        for (int i = 0; i < values.Length; i++)
        {
            strategy.write(packer, previous, values[i]);
            previous = values[i];
        }

        packer.ResetPositionAndMode(true);
        previous = 0f;
        for (int i = 0; i < values.Length; i++)
            previous = strategy.read(packer, previous);
    }

    static string RenderTable(string title, Stream[] streams, Strategy[] strategies,
        double[,] table, string format)
    {
        var sb = new StringBuilder();
        sb.Append($"[FloatDelta] {title}\n");
        sb.Append($"{"stream",-16}");
        foreach (var strategy in strategies)
            sb.Append($"{strategy.name,16}");
        sb.Append('\n');

        for (int s = 0; s < streams.Length; s++)
        {
            sb.Append($"{streams[s].name,-16}");
            for (int k = 0; k < strategies.Length; k++)
                sb.Append($"{table[s, k].ToString(format),16}");
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
