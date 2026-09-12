using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class VisibilityEmptySiblingTests
{
    private readonly List<GameObject> _objects = new List<GameObject>();
    private readonly List<(PlayerID player, Transform scope, bool visible)> _changes =
        new List<(PlayerID, Transform, bool)>();
    private readonly List<(Transform scope, HashSet<PlayerID> players)> _clears =
        new List<(Transform, HashSet<PlayerID>)>();
    private VisilityV2 _visibility;
    private static readonly PlayerID Player = new PlayerID(1, false);
    private static readonly PlayerID OtherPlayer = new PlayerID(2, false);

    [SetUp]
    public void SetUp()
    {
        var managerObject = CreateObject("VisibilityManager");
        managerObject.SetActive(false);
        var manager = managerObject.AddComponent<NetworkManager>();
        manager.startServerFlags = StartFlags.None;
        manager.startClientFlags = StartFlags.None;
        _visibility = new VisilityV2(manager);
        _visibility.visibilityChanged += (player, scope, visible) => _changes.Add((player, scope, visible));
        _visibility.visibilityCleared += (scope, players) =>
            _clears.Add((scope, new HashSet<PlayerID>(players)));
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i])
                UnityEngine.Object.DestroyImmediate(_objects[i]);
        _objects.Clear();
        _changes.Clear();
        _clears.Clear();
    }

    [Test]
    public void RefreshEmptyScopeDoesNotAddObserversOrNotify()
    {
        var empty = CreateIdentity("EmptyScope");
        InjectEmptySiblings(empty);

        _visibility.RefreshVisibilityForGameObject(Player, empty);

        Assert.That(empty.observers, Is.Empty);
        Assert.That(_changes, Is.Empty);
    }

    [Test]
    public void RefreshSkipsEmptyChildAndTraversesFromFirstStackedIdentity()
    {
        var root = CreateIdentity("Root");
        var stacked = root.gameObject.AddComponent<NetworkIdentity>();
        var empty = CreateIdentity("EmptyChild", root.transform);
        var child = CreateIdentity("VisibleChild", root.transform);
        NetworkManager.SetupPrefabInfo(root.gameObject, 555, false);
        InjectEmptySiblings(empty);

        _visibility.RefreshVisibilityForGameObject(Player, stacked);

        Assert.That(root.IsObserver(Player), Is.True);
        Assert.That(stacked.IsObserver(Player), Is.True);
        Assert.That(child.IsObserver(Player), Is.True);
        Assert.That(empty.observers, Is.Empty);
        Assert.That(_changes, Is.EqualTo(new[] { (Player, root.transform, true) }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClearSkipsEmptyChildAndClearsOtherChildren(bool singlePlayer)
    {
        var root = CreateIdentity("Root");
        var empty = CreateIdentity("EmptyChild", root.transform);
        var child = CreateIdentity("VisibleChild", root.transform);
        NetworkManager.SetupPrefabInfo(root.gameObject, 555, false);
        _visibility.RefreshVisibilityForGameObject(Player, root);
        _visibility.RefreshVisibilityForGameObject(OtherPlayer, root);
        _changes.Clear();
        InjectEmptySiblings(empty);

        if (singlePlayer)
            _visibility.ClearVisibilityForGameObject(root, Player);
        else
            _visibility.ClearVisibilityForGameObject(root);

        Assert.That(root.IsObserver(Player), Is.False);
        Assert.That(child.IsObserver(Player), Is.False);
        Assert.That(root.IsObserver(OtherPlayer), Is.EqualTo(singlePlayer));
        Assert.That(child.IsObserver(OtherPlayer), Is.EqualTo(singlePlayer));
        if (singlePlayer)
        {
            Assert.That(_changes, Is.EqualTo(new[] { (Player, root.transform, false) }));
            Assert.That(_clears, Is.Empty);
        }
        else
        {
            Assert.That(_changes, Is.Empty);
            Assert.That(_clears, Has.Count.EqualTo(1));
            Assert.That(_clears[0].scope, Is.EqualTo(root.transform));
            Assert.That(_clears[0].players, Is.EquivalentTo(new[] { Player, OtherPlayer }));
        }
    }

    [Test]
    public void EvaluateAllSkipsEmptyRootAndStillEvaluatesValidRoot()
    {
        var empty = CreateIdentity("EmptyRoot");
        var root = CreateIdentity("ValidRoot");
        InjectEmptySiblings(empty);

        _visibility.EvaluateAll(new[] { Player }, new List<NetworkIdentity> { empty, root });

        Assert.That(empty.observers, Is.Empty);
        Assert.That(root.IsObserver(Player), Is.True);
        Assert.That(_changes, Is.EqualTo(new[] { (Player, root.transform, true) }));
    }

    private GameObject CreateObject(string name)
    {
        var result = new GameObject(name);
        _objects.Add(result);
        return result;
    }

    private NetworkIdentity CreateIdentity(string name, Transform parent = null)
    {
        var result = CreateObject(name);
        result.transform.SetParent(parent);
        return result.AddComponent<NetworkIdentity>();
    }

    private static void InjectEmptySiblings(NetworkIdentity identity)
    {
        // Fault injection for the reported empty-array condition; this does not reproduce
        // or assert which Unity lifecycle state can make GetComponents return an empty array.
        var field = typeof(NetworkIdentity).GetField("_siblingIdentities", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        field.SetValue(identity, Array.Empty<NetworkIdentity>());
    }
}
