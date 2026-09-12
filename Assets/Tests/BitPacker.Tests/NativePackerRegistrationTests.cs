using NUnit.Framework;
using PurrNet.Packing;

public sealed class NativePackerRegistrationTests
{
    private BitPacker _packer;
    private static int _firstDeltaWrites, _firstDeltaReads;
    private static int _replacementDeltaWrites, _replacementDeltaReads;

    // These types deliberately have no packing interface or registration attribute.
    // The fixture is not static, so codegen does not auto-register its callbacks.
    public struct Value { public uint number; }
    public struct RepeatedValue { public uint number; }
    public struct DeltaValue { public uint number; }
    public struct RepeatedDeltaValue { public uint number; }
    public struct ManagedValue { public string text; }

    [SetUp]
    public void SetUp()
    {
#if PURR_DELTA_CHECK
        // Delta diagnostics use the regular primitive serializers internally.
        PurrNet.NetworkManager.LoadOrGenerateHashes();
#endif
        _packer = BitPackerPool.Get();
        _firstDeltaWrites = _firstDeltaReads = 0;
        _replacementDeltaWrites = _replacementDeltaReads = 0;
    }

    [TearDown]
    public void TearDown() => _packer.Dispose();

    [Test]
    public void ManagedRegistrationInitializesNativeWriterAndReader()
    {
        Packer<Value>.RegisterWriter(WriteValue);
        Packer<Value>.RegisterReader(ReadValue);

        Assert.That(Packer<Value>.HasPacker(), Is.True);
        Assert.That(NativePacker<Value>.HasPacker(), Is.True);
        var expected = new Value { number = 0xfedcba98u };

        NativePacker<Value>.Write(_packer, expected);
        _packer.ResetPositionAndMode(true);
        var actual = default(Value);
        Packer<Value>.Read(_packer, ref actual);
        Assert.That(actual.number, Is.EqualTo(expected.number));

        _packer.ResetPositionAndMode(false);
        Packer<Value>.Write(_packer, expected);
        _packer.ResetPositionAndMode(true);
        actual = default;
        NativePacker<Value>.Read(_packer, ref actual);
        Assert.That(actual.number, Is.EqualTo(expected.number));
    }

    [Test]
    public void NativeRegistrationKeepsFirstWriterAndReader()
    {
        NativePacker<RepeatedValue>.RegisterWriter(WriteRepeatedValue);
        NativePacker<RepeatedValue>.RegisterReader(ReadRepeatedValue);
        NativePacker<RepeatedValue>.RegisterWriter(WriteReplacementValue);
        NativePacker<RepeatedValue>.RegisterReader(ReadReplacementValue);

        NativePacker<RepeatedValue>.Write(_packer, new RepeatedValue { number = 123u });
        _packer.ResetPositionAndMode(true);
        Assert.That(_packer.ReadBits(32), Is.EqualTo(123UL), "The replacement writer must not run.");

        _packer.ResetPositionAndMode(true);
        var actual = default(RepeatedValue);
        NativePacker<RepeatedValue>.Read(_packer, ref actual);
        Assert.That(actual.number, Is.EqualTo(123u), "The replacement reader must not run.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeltaRegistrationInitializesNativeRoundTrip(bool changed)
    {
        Packer<DeltaValue>.RegisterWriter(WriteDeltaValue);
        Packer<DeltaValue>.RegisterReader(ReadDeltaValue);
        DeltaPacker<DeltaValue>.Register(WriteDelta, ReadDelta);
        Assert.That(DeltaPacker<DeltaValue>.HasPacker(), Is.True);
        Assert.That(NativeDeltaPacker<DeltaValue>.HasPacker(), Is.True);

        var previous = new DeltaValue { number = 17u };
        var expected = new DeltaValue { number = changed ? 0xfedcba98u : previous.number };
        Assert.That(NativeDeltaPacker<DeltaValue>.Write(_packer, previous, expected), Is.EqualTo(changed));

        _packer.ResetPositionAndMode(true);
        var actual = default(DeltaValue);
        NativeDeltaPacker<DeltaValue>.Read(_packer, previous, ref actual);
        Assert.That(actual.number, Is.EqualTo(expected.number));
    }

    [Test]
    public void NativeDeltaRegistrationKeepsFirstWriterAndReader()
    {
        Packer<RepeatedDeltaValue>.RegisterWriter(WriteRepeatedDeltaValue);
        Packer<RepeatedDeltaValue>.RegisterReader(ReadRepeatedDeltaValue);
        NativeDeltaPacker<RepeatedDeltaValue>.Register(WriteFirstDelta, ReadFirstDelta);
        NativeDeltaPacker<RepeatedDeltaValue>.Register(WriteReplacementDelta, ReadReplacementDelta);

        var previous = new RepeatedDeltaValue { number = 12u };
        var expected = new RepeatedDeltaValue { number = 123u };
        Assert.That(NativeDeltaPacker<RepeatedDeltaValue>.Write(_packer, previous, expected), Is.True);
        _packer.ResetPositionAndMode(true);
        var actual = default(RepeatedDeltaValue);
        NativeDeltaPacker<RepeatedDeltaValue>.Read(_packer, previous, ref actual);

        Assert.That(actual.number, Is.EqualTo(expected.number));
        Assert.That(_firstDeltaWrites, Is.EqualTo(1));
        Assert.That(_firstDeltaReads, Is.EqualTo(1));
        Assert.That(_replacementDeltaWrites, Is.Zero);
        Assert.That(_replacementDeltaReads, Is.Zero);
    }

    [Test]
    public void ReferenceContainingValuesKeepTheirManagedSerializer()
    {
        Packer<ManagedValue>.RegisterWriter(WriteManagedValue);
        Packer<ManagedValue>.RegisterReader(ReadManagedValue);
        NativeDeltaPacker<ManagedValue>.Register(WriteManagedDelta, ReadManagedDelta);

        Assert.That(Packer<ManagedValue>.HasPacker(), Is.True);
        Assert.That(NativePacker<ManagedValue>.HasPacker(), Is.False);
        Assert.That(NativeDeltaPacker<ManagedValue>.HasPacker(), Is.False);
        Packer<ManagedValue>.Write(_packer, new ManagedValue { text = "A" });
        _packer.ResetPositionAndMode(true);
        var actual = default(ManagedValue);
        Packer<ManagedValue>.Read(_packer, ref actual);
        Assert.That(actual.text, Is.EqualTo("A"));
    }

    private static void WriteValue(BitPacker packer, Value value) => packer.WriteBits(value.number, 32);
    private static void ReadValue(BitPacker packer, ref Value value) => value.number = (uint)packer.ReadBits(32);
    private static void WriteRepeatedValue(BitPacker packer, RepeatedValue value) => packer.WriteBits(value.number, 32);
    private static void ReadRepeatedValue(BitPacker packer, ref RepeatedValue value) => value.number = (uint)packer.ReadBits(32);
    private static void WriteReplacementValue(BitPacker packer, RepeatedValue value) => packer.WriteBits(value.number + 1u, 32);
    private static void ReadReplacementValue(BitPacker packer, ref RepeatedValue value) => value.number = (uint)packer.ReadBits(32) + 2u;
    private static void WriteDeltaValue(BitPacker packer, DeltaValue value) => packer.WriteBits(value.number, 32);
    private static void ReadDeltaValue(BitPacker packer, ref DeltaValue value) => value.number = (uint)packer.ReadBits(32);

    private static bool WriteDelta(BitPacker packer, DeltaValue previous, DeltaValue value)
    {
        bool changed = previous.number != value.number;
        packer.WriteBit(changed);
        if (changed) packer.WriteBits(value.number, 32);
        return changed;
    }

    private static void ReadDelta(BitPacker packer, DeltaValue previous, ref DeltaValue value)
    {
        value.number = packer.ReadBit() ? (uint)packer.ReadBits(32) : previous.number;
    }

    private static void WriteRepeatedDeltaValue(BitPacker packer, RepeatedDeltaValue value) => packer.WriteBits(value.number, 32);
    private static void ReadRepeatedDeltaValue(BitPacker packer, ref RepeatedDeltaValue value) => value.number = (uint)packer.ReadBits(32);

    private static bool WriteFirstDelta(BitPacker packer, RepeatedDeltaValue previous, RepeatedDeltaValue value)
    {
        _firstDeltaWrites++;
        packer.WriteBits(value.number, 32);
        return true;
    }

    private static void ReadFirstDelta(BitPacker packer, RepeatedDeltaValue previous, ref RepeatedDeltaValue value)
    {
        _firstDeltaReads++;
        value.number = (uint)packer.ReadBits(32);
    }

    private static bool WriteReplacementDelta(BitPacker packer, RepeatedDeltaValue previous, RepeatedDeltaValue value)
    {
        _replacementDeltaWrites++;
        packer.WriteBits(value.number + 1u, 32);
        return false;
    }

    private static void ReadReplacementDelta(BitPacker packer, RepeatedDeltaValue previous, ref RepeatedDeltaValue value)
    {
        _replacementDeltaReads++;
        value.number = (uint)packer.ReadBits(32) + 2u;
    }

    private static void WriteManagedValue(BitPacker packer, ManagedValue value) => packer.WriteBits(value.text[0], 16);
    private static void ReadManagedValue(BitPacker packer, ref ManagedValue value) => value.text = ((char)packer.ReadBits(16)).ToString();
    private static bool WriteManagedDelta(BitPacker packer, ManagedValue previous, ManagedValue value)
    {
        WriteManagedValue(packer, value);
        return previous.text != value.text;
    }
    private static void ReadManagedDelta(BitPacker packer, ManagedValue previous, ref ManagedValue value) => ReadManagedValue(packer, ref value);

#if ENABLE_IL2CPP && !UNITY_EDITOR
    public struct CallbackValue { public uint number; }
    private static readonly CallbackTarget _callbackTarget = new CallbackTarget();

    [Test]
    public void Il2CppNativePackersInvokeRegisteredDelegateTargets()
    {
        // Exercise the player callback path directly, including the bound target.
        // No reflection or expected exceptions are needed with WebGL exceptions=None.
        _callbackTarget.writes = _callbackTarget.reads = 0;
        _callbackTarget.deltaWrites = _callbackTarget.deltaReads = 0;
        Packer<CallbackValue>.RegisterWriter(_callbackTarget.Write);
        Packer<CallbackValue>.RegisterReader(_callbackTarget.Read);
        NativeDeltaPacker<CallbackValue>.Register(_callbackTarget.WriteDelta, _callbackTarget.ReadDelta);

        var expected = new CallbackValue { number = 0xfedcba98u };
        NativePacker<CallbackValue>.Write(_packer, expected);
        _packer.ResetPositionAndMode(true);
        var actual = default(CallbackValue);
        NativePacker<CallbackValue>.Read(_packer, ref actual);
        Assert.That(actual.number, Is.EqualTo(expected.number));
        Assert.That(_callbackTarget.writes, Is.EqualTo(1));
        Assert.That(_callbackTarget.reads, Is.EqualTo(1));

        _packer.ResetPositionAndMode(false);
        Assert.That(NativeDeltaPacker<CallbackValue>.Write(_packer, default, expected), Is.True);
        _packer.ResetPositionAndMode(true);
        actual = default;
        NativeDeltaPacker<CallbackValue>.Read(_packer, default, ref actual);
        Assert.That(actual.number, Is.EqualTo(expected.number));
        Assert.That(_callbackTarget.deltaWrites, Is.EqualTo(1));
        Assert.That(_callbackTarget.deltaReads, Is.EqualTo(1));
    }

    private sealed class CallbackTarget
    {
        public int writes, reads, deltaWrites, deltaReads;

        public void Write(BitPacker packer, CallbackValue value)
        {
            writes++;
            packer.WriteBits(value.number, 32);
        }

        public void Read(BitPacker packer, ref CallbackValue value)
        {
            reads++;
            value.number = (uint)packer.ReadBits(32);
        }

        public bool WriteDelta(BitPacker packer, CallbackValue previous, CallbackValue value)
        {
            deltaWrites++;
            packer.WriteBits(value.number, 32);
            return previous.number != value.number;
        }

        public void ReadDelta(BitPacker packer, CallbackValue previous, ref CallbackValue value)
        {
            deltaReads++;
            value.number = (uint)packer.ReadBits(32);
        }
    }
#endif
}
