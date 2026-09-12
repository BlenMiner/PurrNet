using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;

/// <summary>
/// Tests for Packer.AreEqual and PurrEquality (equality checker used by delta packing and elsewhere).
/// </summary>
public class PurrEqualityTests
{
    public struct CustomEquatableValue : IPurrEquatable<CustomEquatableValue>
    {
        public int key;
        // Keep a reference field so equality uses the comparer rather than MemEquals.
        public string ignored;

        public bool PurrEquals(CustomEquatableValue other) => key == other.key;
    }

    public sealed class CustomEquatableReference : IPurrEquatable<CustomEquatableReference>
    {
        public int key;

        public bool PurrEquals(CustomEquatableReference other) => key == other.key;
    }

    [SetUp]
    public void Setup()
    {
        Hasher.ClearState();
        NetworkManager.CallAllRegisters();
    }

    [Test]
    public void PurrEquality_CustomValue_PreservesPurrEqualsWithReferenceFields()
    {
        var previous = PurrEquality<CustomEquatableValue>.Default;
        try
        {
            PurrEquality.Override<CustomEquatableValue>();
            var value = new CustomEquatableValue { key = 7, ignored = "left" };

            Assert.IsTrue(PurrEquality<CustomEquatableValue>.Equals(value,
                new CustomEquatableValue { key = 7, ignored = "right" }));
            Assert.IsTrue(PurrEquality<CustomEquatableValue>.Equals(value,
                new CustomEquatableValue { key = 7, ignored = null }));
            Assert.IsFalse(PurrEquality<CustomEquatableValue>.Equals(value,
                new CustomEquatableValue { key = 8, ignored = "left" }));
        }
        finally
        {
            PurrEquality<CustomEquatableValue>.OverrideDefault(previous);
        }
    }

    [Test]
    public void PurrEquality_CustomReference_PreservesEqualityAndNullGuards()
    {
        var previous = PurrEquality<CustomEquatableReference>.Default;
        try
        {
            PurrEquality.Override<CustomEquatableReference>();
            var value = new CustomEquatableReference { key = 7 };

            Assert.IsTrue(PurrEquality<CustomEquatableReference>.Equals(null, null));
            Assert.IsFalse(PurrEquality<CustomEquatableReference>.Equals(null, value));
            Assert.IsFalse(PurrEquality<CustomEquatableReference>.Equals(value, null));
            Assert.IsTrue(PurrEquality<CustomEquatableReference>.Equals(value,
                new CustomEquatableReference { key = 7 }));
            Assert.IsFalse(PurrEquality<CustomEquatableReference>.Equals(value,
                new CustomEquatableReference { key = 8 }));
        }
        finally
        {
            PurrEquality<CustomEquatableReference>.OverrideDefault(previous);
        }
    }

    [Test]
    public void PackerAreEqual_Int()
    {
        Assert.IsTrue(Packer.AreEqual(0, 0));
        Assert.IsTrue(Packer.AreEqual(1, 1));
        Assert.IsTrue(Packer.AreEqual(-1, -1));
        Assert.IsFalse(Packer.AreEqual(0, 1));
        Assert.IsFalse(Packer.AreEqual(1, -1));
    }

    [Test]
    public void PackerAreEqual_String()
    {
        Assert.IsTrue(Packer.AreEqual("hello", "hello"));
        Assert.IsTrue(Packer.AreEqual("", ""));
        Assert.IsFalse(Packer.AreEqual("hello", "world"));
        Assert.IsFalse(Packer.AreEqual("a", "A"));
    }

    [Test]
    public void PackerAreEqual_StringNull()
    {
        Assert.IsTrue(Packer.AreEqual<string>(null, null));
        Assert.IsFalse(Packer.AreEqual<string>(null, "x"));
        Assert.IsFalse(Packer.AreEqual<string>("x", null));
    }

    [Test]
    public void PackerAreEqual_Bool()
    {
        Assert.IsTrue(Packer.AreEqual(true, true));
        Assert.IsTrue(Packer.AreEqual(false, false));
        Assert.IsFalse(Packer.AreEqual(true, false));
    }

    [Test]
    public void PurrEquality_Dictionary_SameAndDifferentValues()
    {
        PackCollections.RegisterDictionary<int, string>();
        var a = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } };
        var b = new Dictionary<int, string> { { 1, "one" }, { 2, "two" } };

        Assert.IsTrue(PurrEquality<Dictionary<int, string>>.Equals(a, b));

        b[2] = "deux";
        Assert.IsFalse(PurrEquality<Dictionary<int, string>>.Equals(a, b));
    }

    [Test]
    public void PurrEquality_HashSet_SameAndDifferentValues()
    {
        PackCollections.RegisterHashSet<int>();
        var a = new HashSet<int> { 1, 2, 3 };
        var b = new HashSet<int> { 1, 2, 3 };

        Assert.IsTrue(PurrEquality<HashSet<int>>.Equals(a, b));

        b.Remove(2);
        b.Add(4);
        Assert.IsFalse(PurrEquality<HashSet<int>>.Equals(a, b));
    }

    [Test]
    public void PurrEquality_Default_IsNonNull_AfterUse()
    {
        _ = PurrEquality<int>.Default;
        Assert.IsNotNull(PurrEquality<int>.Default);
        _ = PurrEquality<string>.Default;
        Assert.IsNotNull(PurrEquality<string>.Default);
    }

    [Test]
    public void PackerAreEqual_DisposableListInt_SameContent()
    {
        var a = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var b = DisposableList<int>.Create(new[] { 1, 2, 3 });
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListInt_DifferentContent()
    {
        var a = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var b = DisposableList<int>.Create(new[] { 1, 2, 4 });
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListInt_DifferentCount()
    {
        var a = DisposableList<int>.Create(new[] { 1, 2 });
        var b = DisposableList<int>.Create(new[] { 1, 2, 3 });
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListInt_Empty()
    {
        var a = DisposableList<int>.Create();
        var b = DisposableList<int>.Create();
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_SameContent()
    {
        var a = DisposableList<string>.Create(new[] { "hello", "world" });
        var b = DisposableList<string>.Create(new[] { "hello", "world" });
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_DifferentContent()
    {
        var a = DisposableList<string>.Create(new[] { "hello", "world" });
        var b = DisposableList<string>.Create(new[] { "hello", "beautiful", "world" });
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_Empty()
    {
        var a = DisposableList<string>.Create();
        var b = DisposableList<string>.Create();
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_WithNulls()
    {
        var a = DisposableList<string>.Create(new[] { "a", null, "b" });
        var b = DisposableList<string>.Create(new[] { "a", null, "b" });
        try
        {
            Assert.IsTrue(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableListString_OneNullElementMismatch()
    {
        var a = DisposableList<string>.Create(new[] { "a", null, "b" });
        var b = DisposableList<string>.Create(new[] { "a", "x", "b" });
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableList_DefaultBoth()
    {
        var a = default(DisposableList<int>);
        var b = default(DisposableList<int>);
        Assert.IsTrue(Packer.AreEqual(a, b));
    }

    [Test]
    public void PackerAreEqual_DisposableList_DefaultVsAllocated()
    {
        var a = default(DisposableList<int>);
        var b = DisposableList<int>.Create();
        try
        {
            Assert.IsFalse(Packer.AreEqual(a, b));
        }
        finally
        {
            b.Dispose();
        }
    }

    [Test]
    public void PackerAreEqual_DisposableList_DisposedVsDisposed()
    {
        var a = DisposableList<int>.Create(new[] { 1 });
        var b = DisposableList<int>.Create(new[] { 1 });
        a.Dispose();
        b.Dispose();
        Assert.IsTrue(Packer.AreEqual(a, b));
    }

    [Test]
    public void PurrEquality_DisposableListInt_EqualsDirect()
    {
        var a = DisposableList<int>.Create(new[] { 1, 2, 3 });
        var b = DisposableList<int>.Create(new[] { 1, 2, 3 });
        try
        {
            Assert.IsTrue(PurrEquality<DisposableList<int>>.Equals(a, b));
            b[1] = 99;
            Assert.IsFalse(PurrEquality<DisposableList<int>>.Equals(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }

    [Test]
    public void PurrEquality_DisposableListString_EqualsDirect()
    {
        var a = DisposableList<string>.Create(new[] { "hello", "world" });
        var b = DisposableList<string>.Create(new[] { "hello", "world" });
        try
        {
            Assert.IsTrue(PurrEquality<DisposableList<string>>.Equals(a, b));
        }
        finally
        {
            a.Dispose();
            b.Dispose();
        }
    }
}
