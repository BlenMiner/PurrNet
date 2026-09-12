using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Utils;

public class PurrActionTests
{
    static readonly Action NoOp = static () => { };

    static PurrAction<Action> Create(int capacity = 0)
    {
        return new PurrAction<Action>(static listener => listener(), capacity);
    }

    [Test]
    public void AddNull_ReturnsInvalidHandle()
    {
        var callbacks = Create();

        Assert.AreEqual(PurrAction<Action>.InvalidHandle, callbacks.Add(null));
        Assert.AreEqual(0, callbacks.count);
    }

    [Test]
    public void Invoke_CallsListenersInRegistrationOrder()
    {
        var calls = new List<int>();
        var callbacks = Create();
        callbacks.Add(() => calls.Add(1));
        callbacks.Add(() => calls.Add(2));
        callbacks.Add(() => calls.Add(3));

        callbacks.Invoke();

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, calls);
    }

    [Test]
    public void RemoveAt_RemovesExactRegistration()
    {
        var calls = new List<string>();
        var callbacks = Create(2);
        Action first = () => calls.Add("first");
        Action second = () => calls.Add("second");
        var firstHandle = callbacks.Add(first);
        callbacks.Add(second);

        callbacks.RemoveAt(firstHandle, first);
        callbacks.Invoke();

        CollectionAssert.AreEqual(new[] { "second" }, calls);
        Assert.AreEqual(1, callbacks.count);
    }

    [Test]
    public void Remove_UsesDelegateValueEquality()
    {
        var target = new CallbackTarget();
        var callbacks = Create(1);
        var registered = new Action(target.Invoke);
        var equivalent = new Action(target.Invoke);
        Assert.AreNotSame(registered, equivalent);
        callbacks.Add(registered);

        callbacks.Remove(equivalent);
        callbacks.Invoke();

        Assert.AreEqual(0, target.callCount);
        Assert.AreEqual(0, callbacks.count);
    }

    [Test]
    public void ReusedHandle_DoesNotRemoveDifferentReplacement()
    {
        var calls = new List<string>();
        var callbacks = Create(1);
        Action first = () => calls.Add("first");
        Action replacement = () => calls.Add("replacement");
        var staleHandle = callbacks.Add(first);
        callbacks.RemoveAt(staleHandle, first);

        var replacementHandle = callbacks.Add(replacement);
        Assert.AreEqual(staleHandle, replacementHandle);

        callbacks.RemoveAt(staleHandle, first);
        callbacks.Invoke();

        CollectionAssert.AreEqual(new[] { "replacement" }, calls);
        Assert.AreEqual(1, callbacks.count);
    }

    [Test]
    public void RemoveDuringInvoke_SkipsLaterListener()
    {
        var calls = new List<string>();
        var callbacks = Create(2);
        Action second = () => calls.Add("second");
        var secondHandle = PurrAction<Action>.InvalidHandle;
        Action first = () =>
        {
            calls.Add("first");
            callbacks.RemoveAt(secondHandle, second);
        };

        callbacks.Add(first);
        secondHandle = callbacks.Add(second);
        callbacks.Invoke();

        CollectionAssert.AreEqual(new[] { "first" }, calls);
        Assert.AreEqual(1, callbacks.count);
    }

    [Test]
    public void AddDuringInvoke_DefersListenerUntilNextInvoke()
    {
        var calls = new List<string>();
        var callbacks = Create(2);
        Action added = () => calls.Add("added");
        var subscribed = false;
        Action first = () =>
        {
            calls.Add("first");
            if (subscribed)
                return;

            subscribed = true;
            callbacks.Add(added);
        };
        Action second = () => calls.Add("second");

        callbacks.Add(first);
        callbacks.Add(second);
        callbacks.Invoke();

        CollectionAssert.AreEqual(new[] { "first", "second" }, calls);

        calls.Clear();
        callbacks.Invoke();

        CollectionAssert.AreEqual(new[] { "first", "second", "added" }, calls);
    }

    [Test]
    public void ClearDuringInvoke_SkipsRemainingAndKeepsAddedListener()
    {
        var calls = new List<string>();
        var callbacks = Create(2);
        Action added = () => calls.Add("added");
        Action first = () =>
        {
            calls.Add("first");
            callbacks.Clear();
            callbacks.Add(added);
        };
        Action second = () => calls.Add("second");

        callbacks.Add(first);
        callbacks.Add(second);
        callbacks.Invoke();

        CollectionAssert.AreEqual(new[] { "first" }, calls);
        Assert.AreEqual(1, callbacks.count);

        callbacks.Invoke();

        CollectionAssert.AreEqual(new[] { "first", "added" }, calls);
    }

    [Test]
    public void CompactNow_PreservesOrderAndHandleMappings()
    {
        var calls = new List<string>();
        var callbacks = Create(3);
        Action first = () => calls.Add("first");
        Action middle = () => calls.Add("middle");
        Action last = () => calls.Add("last");
        var firstHandle = callbacks.Add(first);
        var middleHandle = callbacks.Add(middle);
        var lastHandle = callbacks.Add(last);

        callbacks.RemoveAt(middleHandle, middle);
        callbacks.CompactNow();
        callbacks.RemoveAt(lastHandle, last);
        callbacks.Invoke();

        CollectionAssert.AreEqual(new[] { "first" }, calls);
        Assert.AreEqual(1, callbacks.count);

        callbacks.RemoveAt(firstHandle, first);
        Assert.AreEqual(0, callbacks.count);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void FirstRemoval_DoesNotAllocate(int capacity)
    {
        WarmAllocationPaths();

        var callbacks = Create(capacity);
        var handle = callbacks.Add(NoOp);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.GetAllocatedBytesForCurrentThread();
        var before = GC.GetAllocatedBytesForCurrentThread();
        callbacks.RemoveAt(handle, NoOp);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0, allocated, $"First removal allocated {allocated} managed bytes.");
    }

    [Test]
    public void AddAfterRemovalAtCapacity_DoesNotAllocateAndPreservesHandles()
    {
        WarmAllocationPaths();

        var calls = new List<int>(4);
        var callbacks = Create(4);
        Action first = () => calls.Add(0);
        Action removed = () => calls.Add(1);
        Action third = () => calls.Add(2);
        Action fourth = () => calls.Add(3);
        Action replacement = () => calls.Add(4);
        callbacks.Add(first);
        var removedHandle = callbacks.Add(removed);
        var thirdHandle = callbacks.Add(third);
        callbacks.Add(fourth);
        callbacks.RemoveAt(removedHandle, removed);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.GetAllocatedBytesForCurrentThread();
        var before = GC.GetAllocatedBytesForCurrentThread();
        callbacks.Add(replacement);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0, allocated, $"Adding into a tombstone at capacity allocated {allocated} managed bytes.");

        callbacks.Invoke();
        CollectionAssert.AreEqual(new[] { 0, 2, 3, 4 }, calls);

        callbacks.RemoveAt(thirdHandle, third);
        calls.Clear();
        callbacks.Invoke();
        CollectionAssert.AreEqual(new[] { 0, 3, 4 }, calls);
    }

    static void WarmAllocationPaths()
    {
        var callbacks = Create(1);
        var handle = callbacks.Add(NoOp);
        callbacks.RemoveAt(handle, NoOp);
        handle = callbacks.Add(NoOp);
        callbacks.RemoveAt(handle, NoOp);
    }

    sealed class CallbackTarget
    {
        public int callCount;

        public void Invoke()
        {
            callCount++;
        }
    }
}

public class UnityUpdateRegistryVisibilityTests
{
    [TestCase(typeof(UnityUpdate), "update")]
    [TestCase(typeof(UnityUpdate), "lateUpdate")]
    [TestCase(typeof(UnityLatestUpdate), "update")]
    [TestCase(typeof(UnityLatestUpdate), "fixedUpdate")]
    [TestCase(typeof(UnityLatestUpdate), "latestUpdate")]
    public void CallbackRegistry_IsInternal(Type owner, string propertyName)
    {
        var publicProperty = owner.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static);
        var internalProperty = owner.GetProperty(propertyName, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNull(publicProperty);
        Assert.IsNotNull(internalProperty);
        Assert.IsFalse(internalProperty.GetMethod.IsPublic);
    }
}
