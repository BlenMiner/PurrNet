using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using Object = UnityEngine.Object;

public class OwnershipBaselineOrderingTests
{
    private static readonly SceneID Scene = new(83);
    private static readonly PlayerID Player = new(4, false);

    [Test]
    public void EarlyObserverEventCannotQueueAuthorityBeforeModuleBaselines()
    {
        using var fixture = new Fixture();
        fixture.SceneOwnership.GiveOwnership(fixture.Identity, Player);

        fixture.ObserverEvent("onObserverAdded");
        fixture.Ownership.PreFixedUpdate();
        fixture.Ownership.FixedUpdate();
        Assert.That(fixture.Outbound.Count, Is.Zero);

        fixture.Identity.TriggerOnObserverAdded(Player, false);
        Assert.That(fixture.Probe.ObserverCallbacks, Is.EqualTo(1));
        fixture.ObserverEvent("onLateObserverAdded");
        Assert.That(fixture.Outbound.Count, Is.EqualTo(1),
            "Ownership catch-up becomes eligible only after the module baseline callbacks.");

        // Resolve the current owner at flush, rather than replaying a stale queued owner.
        fixture.SceneOwnership.RemoveOwnership(fixture.Identity);
        fixture.Ownership.PreFixedUpdate();
        Assert.That(fixture.Outbound.Count, Is.Zero);
    }

    [Test]
    public void SceneLoadedNotifiesOwnerReconnectedWithoutSendingAnEarlyOwnershipSnapshot()
    {
        using var fixture = new Fixture();
        fixture.SceneOwnership.GiveOwnership(fixture.Identity, Player);

        fixture.SceneLoaded();

        Assert.That(fixture.Probe.Reconnections, Is.EqualTo(1));
        Assert.That(fixture.Outbound.Count, Is.Zero);
        Assert.That(fixture.Probe.ObserverCallbacks, Is.Zero);
    }

    private sealed class Probe : NetworkModule
    {
        public int ObserverCallbacks;
        public int Reconnections;

        public override void OnObserverAdded(PlayerID player) => ObserverCallbacks++;
        public override void OnOwnerReconnected(PlayerID player) => Reconnections++;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GameObject _managerObject;
        private readonly HierarchyFactory _factory;
        public readonly NetworkIdentity Identity;
        public readonly Probe Probe = new();
        public readonly GlobalOwnershipModule Ownership;
        public readonly SceneOwnership SceneOwnership;
        public IDictionary Outbound => GetField<IDictionary>(Ownership, "_pendingOwnershipChanges");

        public Fixture()
        {
            _managerObject = new GameObject("Ownership baseline manager");
            _managerObject.SetActive(false);
            var manager = _managerObject.AddComponent<NetworkManager>();
            SetField(manager, "_clientModules", new ModulesCollection(manager, false));
            SetField(manager, "_serverModules", new ModulesCollection(manager, true));

            // Subscription-only broadcaster: any premature network send fails this fixture.
            var players = (PlayersManager)FormatterServices.GetUninitializedObject(typeof(PlayersManager));
            players.SetBroadcaster(new PlayersBroadcaster(null, players));
            var scenes = new ScenesModule(manager, players);
            var scenePlayers = new ScenePlayersModule(manager, scenes, players);
            _factory = new HierarchyFactory(manager, scenes, scenePlayers, players);
            var hierarchy = (HierarchyV2)FormatterServices.GetUninitializedObject(typeof(HierarchyV2));
            SetField(hierarchy, "_manager", manager);
            SetField(hierarchy, "_asServer", true);
            SetField(hierarchy, "_spawnedIdentitiesMap", new Dictionary<NetworkID, NetworkIdentity>());
            GetField<Dictionary<SceneID, HierarchyV2>>(_factory, "_hierarchies").Add(Scene, hierarchy);

            var go = new GameObject("Ownership baseline identity");
            go.SetActive(false);
            Identity = go.AddComponent<NetworkIdentity>();
            Identity.SetID(new NetworkID(17, Player));
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.networkManager)).SetValue(Identity, manager);
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.sceneId)).SetValue(Identity, Scene);
            SetField(Identity, "_isSpawnedServer", true);
            SetField(Identity, "_spawnedCount", 1);
            Identity.RegisterModuleInternal("ownershipProbe", nameof(Probe), Probe, false);
            GetField<Dictionary<NetworkID, NetworkIdentity>>(hierarchy, "_spawnedIdentitiesMap")
                .Add(Identity.id.Value, Identity);

            Ownership = new GlobalOwnershipModule(manager, _factory, players, scenePlayers, scenes);
            Ownership.Enable(true);
            Invoke(Ownership, "OnSceneLoaded", Scene, true);
            SceneOwnership = GetField<Dictionary<SceneID, SceneOwnership>>(Ownership, "_sceneOwnerships")[Scene];
        }

        public void ObserverEvent(string name) => GetField<ObserverAction>(_factory, name)?.Invoke(Player, Identity);
        public void SceneLoaded() => Invoke(Ownership, "OnPlayerLoadedScene", Player, Scene, true);

        public void Dispose()
        {
            Ownership.Disable(true);
            foreach (IDisposable pending in Outbound.Values)
                pending.Dispose();
            Outbound.Clear();
            SetField(Identity, "_isSpawnedServer", false);
            SetField(Identity, "_isSpawnedClient", false);
            SetField(Identity, "_spawnedCount", 0);
            Object.DestroyImmediate(Identity.gameObject);
            Object.DestroyImmediate(_managerObject);
        }
    }

    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static T GetField<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
}
