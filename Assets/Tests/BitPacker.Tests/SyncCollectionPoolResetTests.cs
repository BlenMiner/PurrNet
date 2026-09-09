using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class SyncCollectionPoolResetTests
{
    [Test]
    public void PooledListRestoresInitialValuesAndAcceptsTheNextBaseline()
    {
        var list = new SyncList<int>(new List<int> { 5 }, ownerAuth: true);
        list.Add(10);
        list.Insert(0, 20);
        int notifications = 0;
        list.onChanged += _ => notifications++;
        SetField(list, "_wasLastDirty", true);

        list.OnPoolReset();

        CollectionAssert.AreEqual(new[] { 5 }, list);
        Assert.That(notifications, Is.Zero);
        AssertNoPendingChanges(list);
        Invoke(list, "HandleFullState", new List<int> { 30 });
        Assert.That(notifications, Is.Zero, "The previous lifetime's subscription was removed.");
        list.onChanged += _ => notifications++;
        Invoke(list, "HandleFullState", new List<int> { 31 });
        list.OnTick(1f);
        CollectionAssert.AreEqual(new[] { 31 }, list);
        Assert.That(notifications, Is.GreaterThan(0));
        AssertNoPendingChanges(list);

        list.Add(40);
        Assert.That(PendingChanges(list).Count, Is.EqualTo(1));
    }

    [Test]
    public void PooledListBaselinePrecedesOwnershipAndNewOwnerWrites()
    {
        var managerObject = new GameObject("List migration test manager");
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<NetworkManager>();
        var identityObject = new GameObject("List migration test owner");
        var identity = identityObject.AddComponent<NetworkIdentity>();
        var list = new SyncList<int>(ownerAuth: true);
        list.Add(10);

        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var player = new PlayerID(2, false);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.networkManager)).SetValue(identity, manager);
            typeof(NetworkIdentity).GetField("_isSpawnedClient", flags).SetValue(identity, true);
            typeof(NetworkIdentity).GetField("_localPlayer", flags).SetValue(identity, (PlayerID?)player);
            list.SetComponentParent(identity, 0, "items");
            list.OnPoolReset();
            Assert.That(list, Is.Empty);

            Assert.That(list.IsController(true), Is.False, "The host baseline arrives before ownership.");
            Invoke(list, "HandleFullState", new List<int> { 30 });

            CollectionAssert.AreEqual(new[] { 30 }, list);
            AssertNoPendingChanges(list);

            identity.internalOwnerClient = player;
            typeof(NetworkIdentity).GetField("_cachedHasConnectedOwner", flags).SetValue(identity, true);
            Assert.That(list.IsController(true), Is.True);
            Invoke(list, "HandleFullState", new List<int> { 99 });
            CollectionAssert.AreEqual(new[] { 30 }, list, "Normal owner authority applies after catch-up.");
            list.Add(40);
            Assert.That(list[list.Count - 1], Is.EqualTo(40));
            Assert.That(PendingChanges(list).Count, Is.EqualTo(1), "Future owner changes still queue normally.");
        }
        finally
        {
            typeof(NetworkIdentity).GetField("_isSpawnedClient", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(identity, false);
            Object.DestroyImmediate(identityObject);
            Object.DestroyImmediate(managerObject);
        }
    }

    [Test]
    public void PooledDictionaryDropsOldChangesAndAcceptsTheNextBaseline()
    {
        var dictionary = new SyncDictionary<int, int>(ownerAuth: true, useForceSend: true);
        dictionary.Add(1, 10);
        int notifications = 0;
        dictionary.onChanged += _ => notifications++;
        SetField(dictionary, "_wasLastDirty", true);

        dictionary.OnPoolReset();

        Assert.That(dictionary, Is.Empty);
        Assert.That(notifications, Is.Zero);
        AssertNoPendingChanges(dictionary);
        Invoke(dictionary, "HandleInitialState", new Dictionary<int, int> { { 2, 20 } });
        Assert.That(notifications, Is.Zero, "The previous lifetime's subscription was removed.");
        dictionary.onChanged += _ => notifications++;
        Invoke(dictionary, "HandleInitialState", new Dictionary<int, int> { { 2, 21 } });
        dictionary.OnTick(1f);
        Assert.That(dictionary.Count, Is.EqualTo(1));
        Assert.That(dictionary[2], Is.EqualTo(21));
        Assert.That(notifications, Is.GreaterThan(0));
        AssertNoPendingChanges(dictionary);

        dictionary.Add(3, 30);
        Assert.That(PendingChanges(dictionary).Count, Is.EqualTo(1));
    }

    [Test]
    public void PooledArrayRestoresSerializedSizeAndValuesBeforeTheNextBaseline()
    {
        var array = new SyncArray<int>(2, ownerAuth: true);
        array.Clear();
        array[0] = 10;
        array.OnBeforeSerialize();
        array.Length = 4;
        array[0] = 20;
        int notifications = 0;
        array.onChanged += _ => notifications++;
        SetField(array, "_wasLastDirty", true);
        Assert.That(PendingChanges(array).Count, Is.GreaterThan(0));

        array.OnPoolReset();

        CollectionAssert.AreEqual(new[] { 10, 0 }, array);
        Assert.That(notifications, Is.Zero);
        AssertNoPendingChanges(array);
        Invoke(array, "HandleInitialSize", 3);
        Assert.That(notifications, Is.Zero, "The previous lifetime's subscription was removed.");
        array.onChanged += _ => notifications++;
        Invoke(array, "HandleInitialSize", 4);
        array.OnTick(1f);
        Assert.That(array.Count, Is.EqualTo(4));
        Assert.That(notifications, Is.GreaterThan(0));
        AssertNoPendingChanges(array);

        array.Clear();
        Assert.That(PendingChanges(array).Count, Is.EqualTo(1));
    }

    [Test]
    public void PooledDeltaStreamDropsOldValuesAndDirtyIndices()
    {
        var stream = new ReliableDeltaStream<int>(new StreamKey());
        stream[2] = 10;

        stream.OnPoolReset();

        Assert.That(stream[2], Is.Zero);
        Assert.That(((ICollection)GetField(stream, "_dirtyIndices")).Count, Is.Zero);
        stream[2] = 20;
        Assert.That(((ICollection)GetField(stream, "_dirtyIndices")).Count, Is.EqualTo(1));
    }

    private static void AssertNoPendingChanges(object collection)
    {
        Assert.That(PendingChanges(collection).Count, Is.Zero);
        Assert.That(GetField(collection, "_isDirty"), Is.False);
        Assert.That(GetField(collection, "_wasLastDirty"), Is.False);
    }

    private static ICollection PendingChanges(object collection) =>
        (ICollection)GetField(collection, "_pendingChanges");

    private static object GetField(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string name, object value) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, new[] { value });

    private readonly struct StreamKey : IStableHashable
    {
        public uint GetStableHash() => 987654321U;
    }
}
