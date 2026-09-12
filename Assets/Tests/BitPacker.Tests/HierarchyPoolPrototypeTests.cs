using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using PurrNet.Packing;
using UnityEngine;
using UnityEngine.TestTools;

public class HierarchyPoolPrototypeTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void PrototypePreservesBreadthFirstPiecesAndDepthFirstComponents(bool scoped, bool parented)
    {
        var parent = new GameObject("Parent");
        var root = new GameObject("Root");
        var player = new PlayerID(1, false);
        try
        {
            var parentIdentity = parent.AddComponent<NetworkIdentity>();
            parentIdentity.PreparePrefabInfo(456, 0, true, true);
            parentIdentity.SetID(new NetworkID(90));
            if (parented)
                root.transform.SetParent(parent.transform, false);
            var rootIdentity = root.AddComponent<NetworkIdentity>();
            var rootSibling = root.AddComponent<NetworkIdentity>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var wrapper = new GameObject("Wrapper");
            wrapper.transform.SetParent(visual.transform, false);
            var a = AddChild(wrapper, "A");
            var aSibling = a.AddComponent<NetworkIdentity>();
            var a1 = AddChild(a, "A1");
            var b = AddChild(root, "B");
            var bSibling = b.AddComponent<NetworkIdentity>();
            var b1 = AddChild(b, "B1");
            PrepareCapturePieces(root, player);
            a.SetActive(false);

            var captured = new List<NetworkIdentity> { parentIdentity };
            using (var prototype = Capture(root, scoped, player, captured))
            {
                AssertPieces(prototype,
                    new[] { rootIdentity, a.GetComponent<NetworkIdentity>(), b.GetComponent<NetworkIdentity>(),
                        a1.GetComponent<NetworkIdentity>(), b1.GetComponent<NetworkIdentity>() },
                    new[] { 2, 1, 1, 0, 0 },
                    new[] { parented ? new[] { 0 } : new int[0], new[] { 0, 0, 0 }, new[] { 1 }, new[] { 0 }, new[] { 0 } });
                Assert.That(prototype.framework[1].isActive, Is.False);
                Assert.That(prototype.framework[3].isActive, Is.True, "Capture preserves activeSelf under inactive parents.");
                Assert.That(prototype.parentID, Is.EqualTo(parented ? parentIdentity.id : null));
                CollectionAssert.AreEqual(new[] { parentIdentity, rootIdentity, rootSibling,
                    a.GetComponent<NetworkIdentity>(), aSibling, a1.GetComponent<NetworkIdentity>(),
                    b.GetComponent<NetworkIdentity>(), bSibling, b1.GetComponent<NetworkIdentity>() }, captured);
            }

            // Cached network-child lists and cached paths retain their original order here.
            b.transform.SetSiblingIndex(0);
            a1.transform.SetParent(b.transform, false);
            a1.transform.SetSiblingIndex(0);
            captured.Clear();
            using (var prototype = Capture(root, scoped, player, captured))
            {
                AssertPieces(prototype,
                    new[] { rootIdentity, b.GetComponent<NetworkIdentity>(), a.GetComponent<NetworkIdentity>(),
                        a1.GetComponent<NetworkIdentity>(), b1.GetComponent<NetworkIdentity>() },
                    new[] { 2, 2, 0, 0, 0 },
                    new[] { parented ? new[] { 0 } : new int[0], new[] { 0 }, new[] { 0, 0, 1 }, new[] { 0 }, new[] { 1 } });
                CollectionAssert.AreEqual(new[] { rootIdentity, rootSibling,
                    b.GetComponent<NetworkIdentity>(), bSibling, a1.GetComponent<NetworkIdentity>(), b1.GetComponent<NetworkIdentity>(),
                    a.GetComponent<NetworkIdentity>(), aSibling }, captured);
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(parent);
        }
    }

    [Test]
    public void ScopedPrototypeUsesEveryComponentForPendingVisibilityAndRejectsHiddenSubtrees()
    {
        var root = new GameObject("Root");
        var player = new PlayerID(1, false);
        try
        {
            var rootIdentity = root.AddComponent<NetworkIdentity>();
            var rootSibling = root.AddComponent<NetworkIdentity>();
            var visible = AddChild(root, "Visible");
            var visibleSibling = visible.AddComponent<NetworkIdentity>();
            var hidden = AddChild(root, "Hidden");
            var observedGrandchild = AddChild(hidden, "ObservedGrandchild");
            var unspawned = AddChild(root, "Unspawned");
            var unspawnedSibling = unspawned.AddComponent<NetworkIdentity>();
            var spawnedGrandchild = AddChild(unspawned, "SpawnedGrandchild");
            PrepareCapturePieces(root, player, unspawnedIdentity: unspawned.GetComponent<NetworkIdentity>());
            foreach (var identity in root.GetComponentsInChildren<NetworkIdentity>(true))
                identity.TryRemoveObserver(player);
            rootSibling.TryAddObserver(player);
            rootSibling.TryMoveObserverToPending(player);
            visibleSibling.TryAddObserver(player);
            visibleSibling.TryMoveObserverToPending(player);
            observedGrandchild.GetComponent<NetworkIdentity>().TryAddObserver(player);
            unspawnedSibling.TryAddObserver(player);
            spawnedGrandchild.GetComponent<NetworkIdentity>().TryAddObserver(player);
            visible.SetActive(false);

            var captured = new List<NetworkIdentity>();
            Assert.IsTrue(HierarchyPool.TryGetPrototype(root.transform, player, captured, out var prototype));
            using (prototype)
            {
                AssertPieces(prototype, new[] { rootIdentity, visible.GetComponent<NetworkIdentity>() },
                    new[] { 1, 0 }, new[] { new int[0], new[] { 0 } });
                CollectionAssert.AreEqual(new[] { rootIdentity, rootSibling, visible.GetComponent<NetworkIdentity>(), visibleSibling }, captured);
            }

            var unspawnedRoot = AddChild(root, "UnspawnedRoot");
            var observedRootSibling = unspawnedRoot.AddComponent<NetworkIdentity>();
            unspawnedRoot.GetComponent<NetworkIdentity>().PreparePrefabInfo(456, 99, true, true);
            observedRootSibling.PreparePrefabInfo(456, 100, true, true);
            observedRootSibling.SetID(new NetworkID(999));
            observedRootSibling.TryAddObserver(player);
            Assert.IsFalse(HierarchyPool.TryGetPrototype(unspawnedRoot.transform, player, captured, out var rejected));
            rejected.Dispose();
            Assert.That(captured.Count, Is.EqualTo(4), "Failed capture must leave the caller's existing list intact.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FullPrototypeOnlyIncludesAnUnspawnedSubtreeWhenRequested(bool includeUnspawned)
    {
        var root = new GameObject("Root");
        try
        {
            root.AddComponent<NetworkIdentity>();
            var child = AddChild(root, "Unspawned");
            var siblingComponent = child.AddComponent<NetworkIdentity>();
            var grandchild = AddChild(child, "SpawnedGrandchild");
            PrepareCapturePieces(root, new PlayerID(1, false), unspawnedIdentity: child.GetComponent<NetworkIdentity>());
            var captured = new List<NetworkIdentity>();
            using var prototype = HierarchyPool.GetFullPrototype(root.transform, captured, includeUnspawned);
            Assert.That(prototype.framework.Count, Is.EqualTo(includeUnspawned ? 3 : 1));
            CollectionAssert.AreEqual(includeUnspawned
                ? new[] { root.GetComponent<NetworkIdentity>(), child.GetComponent<NetworkIdentity>(), siblingComponent, grandchild.GetComponent<NetworkIdentity>() }
                : new[] { root.GetComponent<NetworkIdentity>() }, captured);
            if (includeUnspawned)
                Assert.That(prototype.framework[1].id, Is.EqualTo(default(NetworkID)), "The first identity owns the piece ID.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void PrototypeSkipsWholeSceneSubtreeButHonorsNonSceneParent(bool scoped, bool sceneParent)
    {
        var root = new GameObject("Root");
        var player = new PlayerID(1, false);
        try
        {
            root.AddComponent<NetworkIdentity>();
            var skipped = AddChild(root, "Skipped");
            var grandchild = AddChild(skipped, "Grandchild");
            var visible = AddChild(root, "Visible");
            PrepareCapturePieces(root, player, sceneParent);
            skipped.GetComponent<NetworkIdentity>().skipSceneAutoSpawning = true;
            var captured = new List<NetworkIdentity>();
            using var prototype = Capture(root, scoped, player, captured);
            Assert.That(prototype.framework.Count, Is.EqualTo(sceneParent ? 2 : 4));
            CollectionAssert.AreEqual(sceneParent
                ? new[] { root.GetComponent<NetworkIdentity>(), visible.GetComponent<NetworkIdentity>() }
                : new[] { root.GetComponent<NetworkIdentity>(), skipped.GetComponent<NetworkIdentity>(), grandchild.GetComponent<NetworkIdentity>(), visible.GetComponent<NetworkIdentity>() }, captured);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(15)]
    [TestCase(16)]
    [TestCase(31)]
    [TestCase(32)]
    [TestCase(63)]
    [TestCase(64)]
    public void ExcludedSubtreePreservesTheFollowingVisibleComponentGroup(int descendants)
    {
        var player = new PlayerID(1, false);
        // Exercise the three ways a network subtree is excluded, at and around search boundaries.
        for (int exclusion = 0; exclusion < 3; exclusion++)
        {
            var root = new GameObject("Root");
            try
            {
                var rootIdentity = root.AddComponent<NetworkIdentity>();
                var a = AddChild(root, "A");
                var aSibling = a.AddComponent<NetworkIdentity>();
                var a1 = AddChild(a, "A1");
                var hidden = AddChild(root, "Excluded");
                hidden.AddComponent<NetworkIdentity>();
                var hiddenNodes = new List<GameObject> { hidden };
                for (int i = 0; i < descendants; i++)
                {
                    var wrapper = new GameObject("Visual" + i);
                    wrapper.transform.SetParent(hiddenNodes[i / 3].transform, false);
                    var child = AddChild(wrapper, "Hidden" + i);
                    if ((i & 1) == 0) child.AddComponent<NetworkIdentity>();
                    hiddenNodes.Add(child);
                }
                var followingWrapper = new GameObject("FollowingVisual");
                followingWrapper.transform.SetParent(root.transform, false);
                var b = AddChild(followingWrapper, "B");
                var bSibling = b.AddComponent<NetworkIdentity>();
                var b1 = AddChild(b, "B1");
                var hiddenIdentity = hidden.GetComponent<NetworkIdentity>();
                PrepareCapturePieces(root, player, unspawnedIdentity: exclusion == 2 ? hiddenIdentity : null);
                hidden.SetActive(false);
                b.SetActive(false);
                if (exclusion == 0)
                {
                    foreach (var component in hidden.GetComponents<NetworkIdentity>())
                        component.TryRemoveObserver(player);
                }
                else if (exclusion == 1)
                    hiddenIdentity.skipSceneAutoSpawning = true;

                for (int atEnd = 0; atEnd < 2; atEnd++)
                {
                    if (atEnd != 0) hidden.transform.SetAsLastSibling();
                    var captured = new List<NetworkIdentity>();
                    GameObjectPrototype prototype;
                    if (exclusion == 0)
                        Assert.IsTrue(HierarchyPool.TryGetPrototype(root.transform, player, captured, out prototype));
                    else
                        prototype = HierarchyPool.GetFullPrototype(root.transform, captured, exclusion == 1);
                    using (prototype)
                    {
                        AssertPieces(prototype,
                            new[] { rootIdentity, a.GetComponent<NetworkIdentity>(), b.GetComponent<NetworkIdentity>(),
                                a1.GetComponent<NetworkIdentity>(), b1.GetComponent<NetworkIdentity>() },
                            new[] { 2, 1, 1, 0, 0 },
                            new[] { new int[0], new[] { 0 }, new[] { 0, atEnd == 0 ? 2 : 1 }, new[] { 0 }, new[] { 0 } });
                        CollectionAssert.AreEqual(new[] { rootIdentity, a.GetComponent<NetworkIdentity>(), aSibling,
                            a1.GetComponent<NetworkIdentity>(), b.GetComponent<NetworkIdentity>(), bSibling,
                            b1.GetComponent<NetworkIdentity>() }, captured);
                        Assert.That(prototype.framework[2].isActive, Is.False);
                        Assert.That(prototype.framework[4].isActive, Is.True);
                    }
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }

    private static GameObjectPrototype Capture(GameObject root, bool scoped, PlayerID player, List<NetworkIdentity> captured)
    {
        if (!scoped)
            return HierarchyPool.GetFullPrototype(root.transform, captured, true);
        Assert.IsTrue(HierarchyPool.TryGetPrototype(root.transform, player, captured, out var prototype));
        return prototype;
    }

    private static void PrepareCapturePieces(GameObject root, PlayerID player, bool scene = true, NetworkIdentity unspawnedIdentity = null)
    {
        var identities = root.GetComponentsInChildren<NetworkIdentity>(true);
        for (var i = 0; i < identities.Length; i++)
        {
            identities[i].PreparePrefabInfo(456, i, true, scene);
            if (identities[i] != unspawnedIdentity)
                identities[i].SetID(new NetworkID((uint)(100 + i)));
            identities[i].TryAddObserver(player);
        }
    }

    private static void AssertPieces(GameObjectPrototype prototype, NetworkIdentity[] identities, int[] childCounts, int[][] paths)
    {
        Assert.That(prototype.framework.Count, Is.EqualTo(identities.Length));
        for (var i = 0; i < identities.Length; i++)
        {
            var piece = prototype.framework[i];
            Assert.That(piece.id, Is.EqualTo(identities[i].id.Value), "Network ID at piece " + i);
            Assert.That(piece.pid, Is.EqualTo(new PrefabPieceID(identities[i].scopedPrefabId, identities[i].componentIndex)), "Prefab ID at piece " + i);
            Assert.That((int)piece.childCount, Is.EqualTo(childCounts[i]), "Child count at piece " + i);
            CollectionAssert.AreEqual(paths[i], piece.inversedRelativePath, "Relative path at piece " + i);
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SinglePiecePrototypeMatchesFilteredHierarchy(bool parented, bool observeSibling)
    {
        NetworkManager.LoadOrGenerateHashes();
        var parent = new GameObject("Parent");
        var root = new GameObject("Root");
        var player = new PlayerID(1, false);

        try
        {
            var parentIdentity = parent.AddComponent<NetworkIdentity>();
            parentIdentity.PreparePrefabInfo(122, 0, false, true);
            parentIdentity.SetID(new NetworkID(10));
            if (parented)
                root.transform.SetParent(parent.transform);
            var first = root.AddComponent<NetworkIdentity>();
            var sibling = root.AddComponent<NetworkIdentity>();
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform);
            var hidden = AddChild(visual, "UnobservedNetworkChild");
            first.PreparePrefabInfo(123, 0, false, true);
            sibling.PreparePrefabInfo(123, 1, false, true);
            first.SetID(new NetworkID(20));
            sibling.SetID(new NetworkID(21));
            hidden.GetComponent<NetworkIdentity>().SetID(new NetworkID(22));
            (observeSibling ? sibling : first).TryAddObserver(player);
            root.transform.localPosition = new Vector3(3, 4, 5);
            root.transform.localRotation = Quaternion.Euler(10, 20, 30);
            root.transform.localScale = new Vector3(2, 3, 4);
            root.SetActive(false);

            var expectedIdentities = new List<NetworkIdentity> { parentIdentity };
            Assert.IsTrue(HierarchyPool.TryGetPrototype(root.transform, player, expectedIdentities, out var expected));
            using (expected)
            {
                Object.DestroyImmediate(hidden);
                var actualIdentities = new List<NetworkIdentity> { parentIdentity };
                Assert.IsTrue(HierarchyPool.TryGetPrototype(root.transform, player, actualIdentities, out var actual));
                using (actual)
                {
                    Assert.That(actual.framework.Count, Is.EqualTo(1));
                    Assert.That(actual.parentID.HasValue, Is.EqualTo(parented));
                    Assert.That(actual.path, parented ? Is.EqualTo(new[] { root.transform.GetSiblingIndex() }) : Is.Null);
                    CollectionAssert.AreEqual(new[] { parentIdentity, first, sibling }, actualIdentities);
                    CollectionAssert.AreEqual(expectedIdentities, actualIdentities);
                    using var expectedBits = BitPackerPool.Get();
                    using var actualBits = BitPackerPool.Get();
                    Packer<GameObjectPrototype>.Write(expectedBits, expected);
                    Packer<GameObjectPrototype>.Write(actualBits, actual);
                    Assert.IsTrue(new BitData(expectedBits).Equals(new BitData(actualBits)),
                        "Single-piece capture must preserve the exact prototype wire data.");
                }
            }

            Assert.IsFalse(HierarchyPool.TryGetPrototype(root.transform, new PlayerID(2, false), null, out var invisible));
            invisible.Dispose();
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(parent);
        }
    }

    [Test]
    public void PrefabPrototypeIncludesUnspawnedNestedIdentities()
    {
        var root = CreateRootWithNestedChildren(nameof(PrefabPrototypeIncludesUnspawnedNestedIdentities));

        try
        {
            NetworkManager.SetupPrefabInfo(root, 123, true);

            using (var livePrototype = HierarchyPool.GetFullPrototype(root.transform))
            {
                Assert.That(livePrototype.framework.Count, Is.EqualTo(1));
            }

            using (var prefabPrototype = HierarchyPool.GetFullPrototype(root.transform, null, true))
            {
                Assert.That(prefabPrototype.framework.Count, Is.EqualTo(4));
            }
        }
        finally
        {
            if (root)
                Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void TryBuildPrototypeFailsWhenChildPieceIsMissing()
    {
        var poolRoot = new GameObject(nameof(TryBuildPrototypeFailsWhenChildPieceIsMissing) + "Pool");
        var rootOnly = new GameObject("RootOnly");
        var fullRoot = new GameObject("FullRoot");

        try
        {
            var prefabPool = new HierarchyPool(poolRoot.transform);
            var pair = new PoolPair(null, prefabPool);

            rootOnly.AddComponent<NetworkIdentity>();
            NetworkManager.SetupPrefabInfo(rootOnly, 321, true);
            prefabPool.PutBackInPool(rootOnly);
            rootOnly = null;

            fullRoot.AddComponent<NetworkIdentity>();
            AddChild(fullRoot, "MissingChild");
            NetworkManager.SetupPrefabInfo(fullRoot, 321, true);

            using var prototype = HierarchyPool.GetFullPrototype(fullRoot.transform, null, true);
            var created = new List<NetworkIdentity>();

            var missingPiece = prototype.framework[1].pid;
            LogAssert.Expect(LogType.Error, $"[HierarchyPool] Cannot warm up piece '{missingPiece}': this pool has no prefab resolver");
            LogAssert.Expect(LogType.Error, $"[HierarchyPool] Piece '{missingPiece}' is still missing from the pool after warmup");
            var success = HierarchyPool.TryBuildPrototype(pair, prototype, created, out var rebuilt, out _);

            Assert.IsFalse(success);
            Assert.IsFalse(rebuilt);
        }
        finally
        {
            if (rootOnly)
                Object.DestroyImmediate(rootOnly);
            if (fullRoot)
                Object.DestroyImmediate(fullRoot);
            if (poolRoot)
                Object.DestroyImmediate(poolRoot);
        }
    }

    [Test]
    public void SceneDefaultPrototypeRestoresNestedUnspawnedIdentities()
    {
        var poolRoot = new GameObject(nameof(SceneDefaultPrototypeRestoresNestedUnspawnedIdentities) + "Pool");
        var root = CreateRootWithNestedChildren(nameof(SceneDefaultPrototypeRestoresNestedUnspawnedIdentities));
        GameObject rebuilt = null;

        try
        {
            var scenePool = new HierarchyPool(poolRoot.transform);
            var pair = new PoolPair(scenePool, null);

            PrepareScenePieces(root, -42);

            var activePieces = new List<NetworkIdentity>();
            root.GetComponentsInChildren(true, activePieces);

            foreach (var piece in activePieces)
                scenePool.RegisterActiveScenePiece(piece);

            using var defaultPrototype = HierarchyPool.GetFullPrototype(root.transform, null, true);
            Assert.That(defaultPrototype.framework.Count, Is.EqualTo(activePieces.Count));

            var created = new List<NetworkIdentity>();
            var success = HierarchyPool.TryBuildPrototype(pair, defaultPrototype, created, out rebuilt, out _);

            Assert.IsTrue(success);
            Assert.IsTrue(rebuilt);

            var restoredPieces = new List<NetworkIdentity>();
            rebuilt.GetComponentsInChildren(true, restoredPieces);
            Assert.That(restoredPieces.Count, Is.EqualTo(activePieces.Count));
        }
        finally
        {
            if (rebuilt)
                Object.DestroyImmediate(rebuilt);
            else if (root)
                Object.DestroyImmediate(root);

            if (poolRoot)
                Object.DestroyImmediate(poolRoot);
        }
    }

    private static GameObject CreateRootWithNestedChildren(string name)
    {
        var root = new GameObject(name);
        root.AddComponent<NetworkIdentity>();
        AddChild(root, "A");
        var b = AddChild(root, "B");
        AddChild(b, "B1");
        return root;
    }

    private static GameObject AddChild(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform);
        child.AddComponent<NetworkIdentity>();
        return child;
    }

    private static void PrepareScenePieces(GameObject root, int prefabId)
    {
        var pieces = new List<NetworkIdentity>();
        root.GetComponentsInChildren(true, pieces);

        for (int i = 0; i < pieces.Count; i++)
            pieces[i].PreparePrefabInfo(prefabId, i, true, true);
    }
}
