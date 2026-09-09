using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Pooling;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class HostMigrationHierarchyTests
{
    private static readonly SceneID Scene = new(73);
    private static readonly PlayerID Player = new(4, false);

    [Test]
    public void TransferDespawnsAndResetsAllAutomaticIdentitiesWithoutChangingUnityHierarchy()
    {
        using var fixture = new Fixture();
        var root = fixture.CreateIdentity();
        var sibling = fixture.CreateIdentity(root.gameObject);
        var child = fixture.CreateIdentity();
        child.transform.SetParent(root.transform, false);
        fixture.PrepareHierarchy(root);
        var manual = fixture.CreateIdentity();
        manual.isManualSpawn = true;
        var rootID = root.id.Value;
        var manualID = manual.id;
        var previousPosition = root.transform.position = new Vector3(3, 5, 8);
        bool allDespawnedBeforeReset = true;
        foreach (var identity in new[] { root, sibling, child })
            identity.Probe.BeforeReset = () => allDespawnedBeforeReset &=
                !root.isSpawned && !sibling.isSpawned && !child.isSpawned;

        fixture.Hierarchy.TransferToNewServer();

        Assert.That(allDespawnedBeforeReset, Is.True);
        Assert.That(fixture.Retained.Count, Is.EqualTo(1));
        Assert.That(fixture.Retained[rootID], Is.SameAs(root));
        foreach (var identity in new[] { root, sibling, child })
        {
            Assert.That(identity.isSpawned, Is.False);
            Assert.That(identity.isFullySpawned, Is.False);
            Assert.That(identity.id, Is.Null);
            Assert.That(identity.networkManager, Is.Null);
            Assert.That(identity.modules, Is.Empty);
            Assert.That(identity.Probe.Despawns, Is.EqualTo(1));
            Assert.That(identity.Probe.Resets, Is.EqualTo(1));
            Assert.That(identity.gameObject.activeSelf, Is.True);
        }
        Assert.That(root.directChildren, Does.Contain(child));
        Assert.That(child.transform.parent, Is.SameAs(root.transform));
        Assert.That(root.transform.position, Is.EqualTo(previousPosition));
        Assert.That(manual.id, Is.EqualTo(manualID));
        Assert.That(manual.isFullySpawned, Is.True);
        Assert.That(manual.Probe.Despawns, Is.Zero);
        Assert.That(manual.Probe.Resets, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnfinishedHierarchyCompletesSpawnBeforeDespawnAndPoolReset(bool remoteFinishPending)
    {
        using var fixture = new Fixture();
        var root = fixture.CreateIdentity(complete: false);
        var child = fixture.CreateIdentity(complete: false);
        child.transform.SetParent(root.transform, false);
        fixture.PrepareHierarchy(root);
        if (remoteFinishPending)
        {
            var pending = DisposableList<NetworkIdentity>.Create(2);
            pending.Add(root);
            pending.Add(child);
            fixture.PendingSpawns.Add(new SpawnID(8, Player, null), pending);
        }
        else
        {
            var pending = GetField<HashSet<NetworkIdentity>>(fixture.Hierarchy, "_toSpawnNextFrame");
            pending.Add(root);
            pending.Add(child);
        }
        bool completedBeforeDespawn = true;
        foreach (var identity in new[] { root, child })
            identity.Probe.DuringDespawn = () => completedBeforeDespawn &=
                root.Probe.Spawns == 1 && child.Probe.Spawns == 1;

        fixture.Hierarchy.TransferToNewServer();

        Assert.That(completedBeforeDespawn, Is.True);
        foreach (var identity in new[] { root, child })
        {
            Assert.That(identity.Probe.Spawns, Is.EqualTo(1));
            Assert.That(identity.Probe.Despawns, Is.EqualTo(1));
            Assert.That(identity.Probe.Resets, Is.EqualTo(1));
            Assert.That(identity.isSpawned, Is.False);
        }
        Assert.That(fixture.PendingSpawns, Is.Empty);
        Assert.That(root.directChildren, Does.Contain(child));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void ReusedHierarchyCompletesThroughOrdinarySpawnAndFinish(bool isAsync, bool finishFirst)
    {
        using var fixture = new Fixture();
        var root = fixture.CreateIdentity();
        var sibling = fixture.CreateIdentity(root.gameObject);
        var child = fixture.CreateIdentity();
        child.transform.SetParent(root.transform, false);
        fixture.PrepareHierarchy(root);
        var rootID = root.id.Value;
        var childID = child.id.Value;
        using var prototype = HierarchyPool.GetFullPrototype(root.transform);
        var packet = new SpawnPacket
        {
            sceneId = Scene, packetIdx = new SpawnID(4, Player, null),
            prototype = prototype, isAsync = isAsync
        };
        fixture.Hierarchy.TransferToNewServer();
        if (finishFirst)
            fixture.Finish(packet.packetIdx);

        Assert.That(fixture.Reuse(packet), Is.True);
        Assert.That(fixture.Retained, Is.Empty);
        Assert.That(fixture.Hierarchy.TryGetIdentity(rootID, out var receivedRoot), Is.True);
        Assert.That(receivedRoot, Is.SameAs(root));
        Assert.That(fixture.Hierarchy.TryGetIdentity(childID, out var receivedChild), Is.True);
        Assert.That(receivedChild, Is.SameAs(child));
        foreach (var identity in new[] { root, sibling, child })
        {
            Assert.That(identity.modules.Count, Is.EqualTo(1));
            Assert.That(identity.modules[0], Is.SameAs(identity.Probe));
            Assert.That(identity.Probe.Initializations, Is.EqualTo(2));
            Assert.That(identity.Probe.EarlySpawns, Is.EqualTo(2));
            Assert.That(identity.Probe.SpawnReceived, Is.EqualTo(2));
            Assert.That(identity.Probe.Spawns, Is.EqualTo(finishFirst ? 2 : 1));
            Assert.That(identity.Probe.Despawns, Is.EqualTo(1));
            Assert.That(identity.gameObject.activeSelf, Is.True);
        }
        if (!finishFirst)
        {
            Assert.That(fixture.PendingSpawns.ContainsKey(packet.packetIdx), Is.True);
            Assert.That(fixture.AsyncPending.Contains(packet.packetIdx), Is.EqualTo(isAsync));
            Assert.That(root.isFullySpawned, Is.False);
            fixture.Finish(packet.packetIdx);
        }
        Assert.That(root.Probe.Spawns, Is.EqualTo(2));
        Assert.That(sibling.Probe.Spawns, Is.EqualTo(2));
        Assert.That(child.Probe.Spawns, Is.EqualTo(2));
        Assert.That(fixture.PendingSpawns, Is.Empty);
        Assert.That(fixture.AsyncPending, Is.Empty);
        Assert.That(fixture.PendingFinishes, Is.Empty);
        fixture.CompleteInventory();
        Assert.That(fixture.Hierarchy.isTransferComplete, Is.True);
        Assert.That(root.gameObject.activeSelf, Is.True);
    }

    [Test]
    public void FinishArrivingBeforeItsSpawnKeepsTransferPending()
    {
        using var fixture = new Fixture();
        Assert.That(fixture.Hierarchy.isTransferComplete, Is.True);
        fixture.Finish(new SpawnID(1, Player, null));
        Assert.That(fixture.PendingFinishes.Count, Is.EqualTo(1));
        Assert.That(fixture.Hierarchy.isTransferComplete, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AsyncCancellationUsesOrdinaryPendingSpawnTransaction(bool cancelChild)
    {
        using var fixture = new Fixture();
        var root = fixture.CreateIdentity();
        var child = fixture.CreateIdentity();
        child.transform.SetParent(root.transform, false);
        fixture.PrepareHierarchy(root);
        using var prototype = HierarchyPool.GetFullPrototype(root.transform);
        var packet = new SpawnPacket
        {
            sceneId = Scene, packetIdx = new SpawnID(2, Player, null),
            prototype = prototype, isAsync = true
        };
        fixture.Hierarchy.TransferToNewServer();
        Assert.That(fixture.Reuse(packet), Is.True);
        fixture.PendingFinishes.Add((packet.packetIdx, Player, false));

        fixture.Cancel(cancelChild ? child : root);

        Assert.That(fixture.PendingSpawns.ContainsKey(packet.packetIdx), Is.EqualTo(cancelChild));
        Assert.That(fixture.AsyncPending.Contains(packet.packetIdx), Is.EqualTo(cancelChild));
        Assert.That(fixture.PendingFinishes.Count, Is.EqualTo(cancelChild ? 1 : 0));
        Assert.That(root.Probe.Spawns, Is.EqualTo(1),
            "Cancellation must not synthesize a completed spawn callback.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnusedRetainedRootsReturnToPoolOnInventoryCompletionOrRetry(bool retry)
    {
        using var fixture = new Fixture();
        var root = fixture.CreateIdentity();
        fixture.Hierarchy.TransferToNewServer();
        Assert.That(root.gameObject.activeSelf, Is.True);
        if (retry)
            fixture.Hierarchy.TransferToNewServer();
        else
            fixture.CompleteInventory();
        Assert.That(fixture.Retained, Is.Empty);
        Assert.That(root.gameObject.activeSelf, Is.False);
        Assert.That(root.transform.parent, Is.SameAs(fixture.PoolParent));
        Assert.That(root.Probe.Despawns, Is.EqualTo(1));
        Assert.That(root.Probe.Resets, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DestroyedRetainedRootFallsBackToOrdinarySpawn(bool destroyInDespawnCallback)
    {
        using var fixture = new Fixture();
        var root = fixture.CreateIdentity();
        using var prototype = HierarchyPool.GetFullPrototype(root.transform);
        var rootID = root.id.Value;
        if (destroyInDespawnCallback)
            root.Probe.DuringDespawn = () => Object.DestroyImmediate(root.gameObject);
        fixture.Hierarchy.TransferToNewServer();
        if (!destroyInDespawnCallback)
            Object.DestroyImmediate(root.gameObject);
        Assert.That(fixture.Hierarchy.TryGetIdentity(rootID, out _), Is.False);
        Assert.That(fixture.Reuse(new SpawnPacket { prototype = prototype }), Is.False);
        Assert.That(fixture.Retained, Is.Empty);
    }

    [Test]
    public void IncompatibleRetainedRootReturnsToPoolInsteadOfReusingItsNetworkIdentity()
    {
        using var fixture = new Fixture();
        var root = fixture.CreateIdentity();
        using var prototype = HierarchyPool.GetFullPrototype(root.transform);
        fixture.Hierarchy.TransferToNewServer();
        root.PreparePrefabInfo(999, 0, true, false);
        Assert.That(fixture.Reuse(new SpawnPacket { prototype = prototype }), Is.False);
        Assert.That(fixture.Retained, Is.Empty);
        Assert.That(root.gameObject.activeSelf, Is.False);
        Assert.That(root.Probe.Spawns, Is.EqualTo(1));
    }

    [Test]
    public void FailedAsyncReuseSendsOrdinaryFailureInsteadOfLeavingAuthorityWaiting()
    {
        using var fixture = new Fixture();
        var root = fixture.CreateIdentity();
        using var prototype = HierarchyPool.GetFullPrototype(root.transform);
        var packet = new SpawnPacket
        {
            sceneId = Scene, packetIdx = new SpawnID(9, Player, null),
            prototype = prototype, isAsync = true
        };
        fixture.Hierarchy.TransferToNewServer();
        SetField(fixture.Hierarchy, "_enabled", true);
        var hold = typeof(HierarchyV2).GetField("debugHoldAsyncSpawnReadySeconds",
            BindingFlags.Static | BindingFlags.NonPublic);
        var previousHold = hold.GetValue(null);
        SpawnDelegate fail = (_, _) => throw new InvalidOperationException("expected retained spawn failure");
        try
        {
            hold.SetValue(null, 60f);
            HierarchyV2.onPreSpawn += fail;
            LogAssert.Expect(LogType.Error, new Regex("CompleteSpawn: exception for packet.*expected retained spawn failure"));
            Assert.That(fixture.Reuse(packet), Is.True, "The failed packet is consumed after rollback.");
            var replies = GetField<List<(float dueTime, AsyncSpawnReadyPacket packet)>>(
                fixture.Hierarchy, "_heldAsyncSpawnReadies");
            Assert.That(replies.Count, Is.EqualTo(1));
            Assert.That(replies[0].packet.packetIdx, Is.EqualTo(packet.packetIdx));
            Assert.That(replies[0].packet.success, Is.False);
            Assert.That(fixture.PendingSpawns, Is.Empty);
            Assert.That(fixture.AsyncPending, Is.Empty);
        }
        finally
        {
            HierarchyV2.onPreSpawn -= fail;
            hold.SetValue(null, previousHold);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly HierarchyV2 Hierarchy = (HierarchyV2)FormatterServices.GetUninitializedObject(typeof(HierarchyV2));
        private readonly List<HostMigrationLifecycleIdentity> _identities = new();
        private readonly GameObject _managerObject;
        private readonly GameObject _poolObject;
        private readonly NetworkManager _manager;
        public Transform PoolParent => _poolObject.transform;
        public Dictionary<NetworkID, NetworkIdentity> Retained =>
            GetField<Dictionary<NetworkID, NetworkIdentity>>(Hierarchy, "_retainedTransferRoots");
        public Dictionary<SpawnID, DisposableList<NetworkIdentity>> PendingSpawns =>
            GetField<Dictionary<SpawnID, DisposableList<NetworkIdentity>>>(Hierarchy, "_pendingSpawns");
        public HashSet<SpawnID> AsyncPending => GetField<HashSet<SpawnID>>(Hierarchy, "_asyncPendingSpawns");
        public List<(SpawnID packetIdx, PlayerID player, bool asServer)> PendingFinishes =>
            GetField<List<(SpawnID, PlayerID, bool)>>(Hierarchy, "_pendingFinishSpawns");

        public Fixture()
        {
            // Use the real transfer/packet methods without starting a transport or registering
            // unrelated test scene objects. Their normal collection initializers are reproduced.
            foreach (var field in typeof(HierarchyV2).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
                if (field.FieldType.Namespace == "System.Collections.Generic" &&
                    field.FieldType.GetConstructor(Type.EmptyTypes) != null)
                    field.SetValue(Hierarchy, Activator.CreateInstance(field.FieldType));
            _managerObject = new GameObject("Hierarchy migration fixture manager");
            _managerObject.SetActive(false);
            _manager = _managerObject.AddComponent<NetworkManager>();
            SetField(_manager, "<preserveWorldOnTransfer>k__BackingField", true);
            _poolObject = new GameObject("Hierarchy migration fixture pool");
            _poolObject.SetActive(false);
            var pool = new HierarchyPool(_poolObject.transform);
            SetField(Hierarchy, "_scenePool", pool);
            SetField(Hierarchy, "_prefabsPool", pool);
            SetField(Hierarchy, "_sceneId", Scene);
            SetField(Hierarchy, "_scene", _managerObject.scene);
            SetField(Hierarchy, "_manager", _manager);
            SetField(Hierarchy, "_playersManager",
                FormatterServices.GetUninitializedObject(typeof(PlayersManager)));
        }

        public HostMigrationLifecycleIdentity CreateIdentity(GameObject existingObject = null, bool complete = true)
        {
            var go = existingObject ? existingObject : new GameObject("Retained hierarchy fixture identity");
            var identity = go.AddComponent<HostMigrationLifecycleIdentity>();
            identity.PreparePrefabInfo(456, _identities.Count, true, false);
            identity.SetID(new NetworkID((uint)_identities.Count + 1));
            Invoke(identity, "SetIdentity", _manager, Hierarchy, Scene, false, false);
            Invoke(Hierarchy, "RegisterIdentity", identity, false, true);
            Invoke(identity, "TriggerOnSpawnReceived");
            if (complete)
                Invoke(identity, "TriggerSpawnEvent", false);
            _identities.Add(identity);
            return identity;
        }

        public void PrepareHierarchy(NetworkIdentity root)
        {
            foreach (var identity in root.GetComponentsInChildren<HostMigrationLifecycleIdentity>(true))
                identity.PreparePrefabInfo(456, _identities.IndexOf(identity), true, false);
        }

        public bool Reuse(SpawnPacket packet) =>
            (bool)Invoke(Hierarchy, "TryReconcileTransferredSpawn", packet, false);
        public void Finish(SpawnID packet) => Invoke(Hierarchy, "OnFinishSpawnPacket", Player,
            new FinishSpawnPacket { sceneId = Scene, packetIdx = packet }, false);
        public void Cancel(NetworkIdentity identity) => Invoke(Hierarchy, "CancelPendingAsyncSpawnRoot", identity);
        public void CompleteInventory() => Invoke(Hierarchy, "OnSceneSpawnReconcilePacket", Player,
            new SceneSpawnReconcilePacket { sceneId = Scene }, false);

        public void Dispose()
        {
            foreach (var pending in PendingSpawns.Values)
                pending.Dispose();
            PendingSpawns.Clear();
            Retained?.Clear();
            foreach (var identity in _identities)
            {
                if (!identity) continue;
                SetField(identity, "_isSpawnedClient", false);
                SetField(identity, "_spawnedCount", 0);
                Object.DestroyImmediate(identity.gameObject);
            }
            Object.DestroyImmediate(_poolObject);
            GetField<HierarchyPool>(Hierarchy, "_scenePool").Dispose();
            Object.DestroyImmediate(_managerObject);
        }
    }

    private static object Invoke(object target, string name, params object[] args)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method != null) return method.Invoke(target, args);
        }
        throw new MissingMethodException(target.GetType().Name, name);
    }

    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static T GetField<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
}

public sealed class HostMigrationLifecycleIdentity : NetworkIdentity
{
    // Registered explicitly, as an application-supplied module would be. Keeping the storage
    // typed as object avoids also registering it through the generated field initializer.
    private readonly object _probe = new HostMigrationLifecycleModule();
    public HostMigrationLifecycleModule Probe => (HostMigrationLifecycleModule)_probe;

    protected override void OnInitializeModules() =>
        RegisterModuleInternal("probe", nameof(HostMigrationLifecycleModule), Probe, false);
}

public sealed class HostMigrationLifecycleModule : NetworkModule
{
    public int Initializations;
    public int EarlySpawns;
    public int SpawnReceived;
    public int Spawns;
    public int Despawns;
    public int Resets;
    public Action BeforeReset;
    public Action DuringDespawn;
    public override void OnInitializeModules() => Initializations++;
    public override void OnEarlySpawn() => EarlySpawns++;
    public override void OnSpawnReceived() => SpawnReceived++;
    public override void OnSpawn() => Spawns++;
    public override void OnDespawned()
    {
        Despawns++;
        var callback = DuringDespawn;
        DuringDespawn = null;
        callback?.Invoke();
    }
    public override void OnPoolReset()
    {
        BeforeReset?.Invoke();
        Resets++;
    }
}
