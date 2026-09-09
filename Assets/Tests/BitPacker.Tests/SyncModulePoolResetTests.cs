using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using UnityEngine;

public class SyncModulePoolResetTests
{
    [Test]
    public void PooledValidatedValueResetsOptimisticDisplayBeforeAnUnchangedBaseline()
    {
        var value = new ValidatedSyncVar<int>(10);
        SetField(value, "_display", 20);
        SetField(value, "_pendingId", 7U);
        SetField(value, "_hasPending", true);
        int changes = 0;
        value.onChanged += (_, validated) => { if (validated) changes++; };

        value.OnDespawned();
        value.OnPoolReset();
        var authoritative = GetField<SyncVar<int>>(value, "_authoritative");
        authoritative.OnPoolReset();
        value.OnEarlySpawn();

        Assert.That(value.value, Is.EqualTo(10));
        Assert.That(GetField<bool>(value, "_hasPending"), Is.False);
        Assert.That(changes, Is.Zero, "Pool reset clears subscriptions from the previous spawn.");
        value.onChanged += (_, validated) => { if (validated) changes++; };
        ReceiveBaseline(authoritative, 0, 10);
        Assert.That(value.value, Is.EqualTo(10));
        Assert.That(changes, Is.Zero, "The unchanged baseline need not emit a change to repair the old display.");
        ReceiveBaseline(authoritative, 1, 30);
        Assert.That(value.value, Is.EqualTo(30));
        Assert.That(changes, Is.EqualTo(1), "The new spawn's subscription receives normal updates.");
    }

    [Test]
    public void PooledInputRestoresItsInitialValueAndClearsAcknowledgedHistory()
    {
        var input = new SyncInput<int>(42);
        SetField(input, "_currentId", 12);
        SetField(input, "_lastAckId", 12);
        SetField(input, "_value", 17);
        SetField(input, "_isDirty", true);
        var history = (Dictionary<int, int>)typeof(SyncInput<int>)
            .GetField("_history", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(input);
        history.Add(12, 17);

        input.OnPoolReset();

        Assert.That(input.value, Is.EqualTo(42));
        Assert.That(history, Is.Empty);
        Assert.That(GetField<int>(input, "_currentId"), Is.Zero);
        Assert.That(GetField<int>(input, "_lastAckId"), Is.Zero);
        Assert.That(typeof(SyncInput<int>).GetField("_isDirty", BindingFlags.NonPublic | BindingFlags.Instance)
            .GetValue(input), Is.False, "The next spawn starts with no input from the previous lifetime queued.");
    }

    [OneTimeSetUp]
    public void RegisterSerializers()
    {
        NetworkManager.CallAllRegisters();
    }

    [TestCase(0UL)]
    [TestCase(1UL)]
    public void EachPooledLifetimeAcceptsItsOwnInitialSequence(ulong nextHostSequence)
    {
        var sync = new SyncVar<int>(10);
        SetField(sync, "_id", 55UL);
        int changes = 0;
        sync.onChanged += _ => changes++;

        sync.OnPoolReset();
        Assert.That(sync.value, Is.EqualTo(10));
        sync.onChanged += _ => changes++;
        ReceiveBaseline(sync, 5, 20);

        Assert.That(sync.value, Is.EqualTo(20));
        Assert.That(GetField<ulong>(sync, "_id"), Is.EqualTo(5UL));
        Assert.That(changes, Is.EqualTo(1), "Only the new lifetime's subscription receives the baseline.");

        sync.OnPoolReset();
        Assert.That(sync.value, Is.EqualTo(10));
        sync.onChanged += _ => changes++;
        ReceiveBaseline(sync, nextHostSequence, 20);

        Assert.That(sync.value, Is.EqualTo(20));
        Assert.That(GetField<ulong>(sync, "_id"), Is.EqualTo(nextHostSequence),
            "An unchanged baseline must still replace the previous host's sequence.");
        Assert.That(changes, Is.EqualTo(2), "The next lifetime applies its baseline after restoring the initial value.");

        Receive(sync, nextHostSequence + 1, 30);
        Receive(sync, nextHostSequence, 99);
        Assert.That(sync.value, Is.EqualTo(30), "Normal sequence ordering resumes against the latest host.");
        Assert.That(changes, Is.EqualTo(3));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PooledBaselinePrecedesOwnershipAndNewOwnerWrites(bool retainsOwnership)
    {
        var go = new GameObject(nameof(PooledBaselinePrecedesOwnershipAndNewOwnerWrites));
        var managerObject = new GameObject("Pooled state test manager");
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<NetworkManager>();
        SetField(manager, "_clientTickManager", new TickManager(30, manager, null, false));
        var identity = go.AddComponent<NetworkIdentity>();
        var player = new PlayerID(2, false);
        var sync = new SyncVar<int>(0, ownerAuth: true);

        try
        {
            SetField(identity, "_isSpawnedClient", true);
            SetField(identity, "_localPlayer", (PlayerID?)player);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.networkManager))
                .SetValue(identity, manager);
            identity.internalOwnerClient = player;
            SetField(identity, "_cachedHasConnectedOwner", true);
            sync.SetComponentParent(identity, 0, "value");
            sync.OnInitializeModules();
            sync.value = 10;
            SetField(sync, "_id", 55UL);
            SetField(sync, "_wasLastDirty", true);
            SetField(sync, "_sentLastTick", true);
            Assert.That(GetField<bool>(sync, "_isDirty"), Is.True);
            Assert.That(GetField<bool>(sync, "_isSubscribedToTickManager"), Is.True);
            int changes = 0;
            sync.onChanged += _ => changes++;

            identity.internalOwnerClient = null;
            SetField(identity, "_cachedHasConnectedOwner", false);
            sync.OnOwnerChanged(player, null, false, false);
            sync.OnDespawned();
            sync.OnPoolReset();
            Assert.That(sync.value, Is.Zero);
            sync.OnInitializeModules();
            sync.OnEarlySpawn();
            sync.OnSpawn();
            sync.onChanged += _ => changes++;
            ReceiveBaseline(sync, 5, 20);
            Assert.That(sync.value, Is.EqualTo(20), "The host baseline arrives before ownership catch-up.");
            Assert.That(sync.isControllingSyncVar, Is.False);
            Assert.That(GetField<bool>(sync, "_isDirty"), Is.False);
            Assert.That(GetField<bool>(sync, "_isSubscribedToTickManager"), Is.False,
                "No write from the old connection remains queued before ownership is restored.");

            if (retainsOwnership)
            {
                identity.internalOwnerClient = player;
                SetField(identity, "_cachedHasConnectedOwner", true);
                sync.OnOwnerChanged(null, player, false, false);
            }

            Assert.That(sync.value, Is.EqualTo(20));
            Assert.That(GetField<ulong>(sync, "_id"), Is.EqualTo(5UL));
            Assert.That(changes, Is.EqualTo(1), "Apply the replacement host's value exactly once.");
            Assert.That(sync.isControllingSyncVar, Is.EqualTo(retainsOwnership));
            Assert.That(GetField<bool>(sync, "_isDirty"), Is.EqualTo(retainsOwnership),
                "Normal ownership catch-up queues the already-applied host value.");
            Assert.That(GetField<bool>(sync, "_wasLastDirty"), Is.False);
            Assert.That(GetField<bool>(sync, "_sentLastTick"), Is.False);
            Assert.That(GetField<bool>(sync, "_isSubscribedToTickManager"), Is.EqualTo(retainsOwnership));

            if (retainsOwnership)
            {
                sync.value = 30;
                Assert.That(sync.value, Is.EqualTo(30));
                Assert.That(GetField<bool>(sync, "_isDirty"), Is.True,
                    "The owner can author a new value after the pooled spawn's baseline.");
                Assert.That(GetField<bool>(sync, "_isSubscribedToTickManager"), Is.True);
                Assert.That(changes, Is.EqualTo(2));
            }
        }
        finally
        {
            SetField(identity, "_isSpawnedClient", false);
            sync.OnDespawned();
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(managerObject);
        }
    }

    private static void Receive(SyncVar<int> sync, ulong sequence, int value)
    {
        typeof(SyncVar<int>).GetMethod("OnReceivedValue", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(sync, new object[] { new PackedULong(sequence), value });
    }

    private static void ReceiveBaseline(SyncVar<int> sync, ulong sequence, int value)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        // Unity's RPC postprocessor renames the receive body and puts a sending wrapper
        // at the original name. Invoke the receive body in either assembly form.
        MethodInfo receive = null;
        foreach (var method in typeof(SyncVar<int>).GetMethods(flags))
            if (method.Name.StartsWith("SendLatestState_Original_", System.StringComparison.Ordinal))
                receive = method;
        receive ??= typeof(SyncVar<int>).GetMethod("SendLatestState", flags);
        Assert.That(receive, Is.Not.Null);
        receive.Invoke(sync, new object[] { new PlayerID(2, false), new PackedULong(sequence), value });
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }
}
