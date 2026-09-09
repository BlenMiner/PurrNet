using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet.Modules
{
    public readonly struct PoolPair
    {
        public readonly HierarchyPool scenePool;
        public readonly HierarchyPool prefabPool;
        public readonly HierarchyPool scopedPrefabPool;

        public PoolPair(HierarchyPool scenePool, HierarchyPool prefabPool, HierarchyPool scopedPrefabPool = null)
        {
            this.scenePool = scenePool;
            this.prefabPool = prefabPool;
            this.scopedPrefabPool = scopedPrefabPool ?? scenePool;
        }

        /// <summary>
        /// Scene objects and scene scoped prefabs live with their scene; global prefabs live in the shared pool.
        /// </summary>
        public HierarchyPool GetPool(PrefabID id)
        {
            if (!id.isValid)
                return scenePool;

            return id.isSceneScoped ? scopedPrefabPool : prefabPool;
        }
    }

    public class HierarchyPool
    {
        private readonly Dictionary<PrefabPieceID, Queue<GameObject>> _pool = new();
        private readonly HashSet<GameObject> _pooledObjects = new();
        private readonly Dictionary<PrefabPieceID, Queue<GameObject>> _activeScenePieces = new();
        private readonly HashSet<GameObject> _activeScenePieceSet = new HashSet<GameObject>();

        private readonly Transform _parent;

        [UsedImplicitly] private readonly PrefabResolver _prefabs;
        private readonly bool _forceWarmupPieces;

        private static readonly Dictionary<PrefabID, GameObjectPrototype> _prefabPrototypes = new();

        private const int MAX_INTERNED_PATHS_PER_LENGTH = 64;

        private static readonly Dictionary<int, List<int[]>> _internedPaths = new();

        /// <summary>
        /// Drops cached prototypes of prefabs scoped to the given scene so nothing outlives it.
        /// </summary>
        public static void EvictPrototypes(SceneID scene)
        {
            EvictPrototypes(id => id.scope.HasValue && id.scope.Value == scene);
        }

        /// <summary>
        /// Drops cached prototypes of global prefabs. Global ids are plain indices into the active provider,
        /// so they must not survive a provider swap.
        /// </summary>
        public static void EvictGlobalPrototypes()
        {
            EvictPrototypes(id => !id.scope.HasValue);
        }

        private static void EvictPrototypes(Func<PrefabID, bool> isStale)
        {
            var stale = ListPool<PrefabID>.Instantiate();

            foreach (var (id, _) in _prefabPrototypes)
            {
                if (isStale(id))
                    stale.Add(id);
            }

            for (int i = 0; i < stale.Count; i++)
            {
                if (_prefabPrototypes.Remove(stale[i], out var prototype))
                    prototype.Dispose();
            }

            ListPool<PrefabID>.Destroy(stale);
        }

        public static bool HasPrototype(PrefabID prefabId)
        {
            return _prefabPrototypes.ContainsKey(prefabId);
        }

        /// <summary>
        /// Number of idle pooled pieces belonging to the given prefab, summed over all of its pieces.
        /// </summary>
        public int GetPooledCount(PrefabID prefabId)
        {
            int count = 0;

            foreach (var (pid, queue) in _pool)
            {
                if (pid.prefabId == prefabId)
                    count += queue.Count;
            }

            return count;
        }

        public static void ClearPrototypes()
        {
            foreach (var (_, prototype) in _prefabPrototypes)
                prototype.Dispose();
            _prefabPrototypes.Clear();
        }

        readonly HashSet<GameObject> _alreadyWarmedUp = new HashSet<GameObject>();

        public HierarchyPool(Transform parent, PrefabResolver prefabs = null, bool forceWarmupPieces = false)
        {
            _parent = parent;
            _prefabs = prefabs;
            _forceWarmupPieces = forceWarmupPieces;
        }

        /// <summary>
        /// Warmup all the prefabs that are marked as poolable.
        /// If a prefab was already warmed up, it will be skipped.
        /// </summary>
        public void Warmup()
        {
            if (_prefabs == null)
                return;

            Warmup(_prefabs.allPrefabs);
        }

        public void Warmup(IEnumerable<PrefabData> prefabs)
        {
            foreach (var prefabData in prefabs)
            {
                if (prefabData.pooled && _alreadyWarmedUp.Add(prefabData.prefab))
                {
                    for (int j = 0; j < prefabData.warmupCount; j++)
                        Warmup(prefabData);
                }
            }
        }

        private void Warmup(PrefabData prefabData)
        {
            var copy = UnityProxy.InstantiateDirectly(prefabData.prefab, _parent);
            NetworkManager.SetupPrefabInfo(copy, prefabData.prefabId, prefabData.pooled || _forceWarmupPieces);

            if (!_prefabPrototypes.ContainsKey(prefabData.prefabId))
            {
                var prototype = GetFullPrototype(copy.transform, null, true);
                _prefabPrototypes.Add(prefabData.prefabId, prototype);
            }

            PutBackInPool(copy, true);
        }

        public static void PutBackInPool(PoolPair pool, GameObject target, bool tagName = false)
        {
            var rootId = target.GetComponent<NetworkIdentity>();
            bool shouldDestroyGo = !rootId || !rootId.shouldBePooled;

            if (rootId)
            {
                var safeParent = rootId.transform.parent;
                PutBackInPoolFromNid(pool, rootId, safeParent, tagName);
            }

            if (shouldDestroyGo)
                UnityProxy.DestroyDirectly(target);
        }

        static void QueueVirtualNodesFromLeafToRoot(NetworkIdentity root, HashSet<NetworkIdentity> properNids)
        {
            var queue = QueuePool<NetworkIdentity>.Instantiate();

            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                properNids.Add(current);

                for (var i = 0; i < current.directChildren.Count; i++)
                {
                    var child = current.directChildren[i];
                    if (!child)
                        continue;
                    queue.Enqueue(child);
                }
            }

            QueuePool<NetworkIdentity>.Destroy(queue);
        }

        static void QueueRealNodesFromLeafToRoot(NetworkIdentity root, HashSet<NetworkIdentity> properNids)
        {
            var queue = QueuePool<NetworkIdentity>.Instantiate();

            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                properNids.Add(current);

                using var directChildren = DisposableList<TransformIdentityPair>.Create(16);
                GetDirectChildren(current.transform, directChildren);

                for (var i = 0; i < directChildren.Count; i++)
                {
                    var child = directChildren[i];
                    queue.Enqueue(child.identity);
                }
            }

            QueuePool<NetworkIdentity>.Destroy(queue);
        }

        static void PutBackInPoolFromNid(PoolPair pool, NetworkIdentity root, Transform safeParent,
            // ReSharper disable once UnusedParameter.Local
            bool tagName = false)
        {
            var toDestroy = ListPool<GameObject>.Instantiate();
            var virtualNodes = HashSetPool<NetworkIdentity>.Instantiate();
            var realNodes = HashSetPool<NetworkIdentity>.Instantiate();

            QueueVirtualNodesFromLeafToRoot(root, virtualNodes);
            QueueRealNodesFromLeafToRoot(root, realNodes);

            realNodes.ExceptWith(virtualNodes);

            // save the objects that should not be despawned
            foreach (var real in realNodes)
            {
                if (real.isSpawned)
                    real.transform.SetParent(safeParent, true);
            }

            foreach (var child in virtualNodes)
            {
                var pid = new PrefabPieceID(child.scopedPrefabId, child.componentIndex);
                var pair = pool.GetPool(pid.prefabId);

                // check if we should pool this object or not
                if (!child.shouldBePooled)
                {
                    toDestroy.Add(child.gameObject);
                    continue;
                }

#if PURRNET_DEBUG_POOLING
                // set the tag
                if (tagName)
                    child.gameObject.name += "-Warmup";
#endif
                // get or create the queue
                if (!pair._pool.TryGetValue(pid, out var queue))
                {
                    queue = QueuePool<GameObject>.Instantiate();
                    pair._pool.Add(pid, queue);
                }

                // put the object in the queue
                child.gameObject.SetActive(false);
                child.transform.SetParent(pair._parent, false);

                pair.Enqueue(child.gameObject, queue);
            }

            // destroy the objects that shouldn't be pooled
            for (var i = 0; i < toDestroy.Count; i++)
            {
                var id = toDestroy[i];
                if (id) UnityProxy.DestroyDirectly(id);
            }

            ListPool<GameObject>.Destroy(toDestroy);
            HashSetPool<NetworkIdentity>.Destroy(virtualNodes);
            HashSetPool<NetworkIdentity>.Destroy(realNodes);
        }

        readonly HashSet<GameObject> _toDestroy = new HashSet<GameObject>();

        public void PutBackInPool(GameObject target, bool tagName = false, bool respectSkipSceneAutoSpawning = false)
        {
            var children = ListPool<NetworkIdentity>.Instantiate();
            var pidSet = HashSetPool<PrefabPieceID>.Instantiate();

            target.GetComponentsInChildren(true, children);

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];

                if (!child || (respectSkipSceneAutoSpawning && child.skipSceneAutoSpawning))
                    continue;

                var pid = new PrefabPieceID(child.scopedPrefabId, child.componentIndex);

                if (!pidSet.Add(pid)) continue;

                // check if we should pool this object or not
                if (!child.shouldBePooled)
                    _toDestroy.Add(child.gameObject);

#if PURRNET_DEBUG_POOLING
                // set the tag
                if (tagName)
                    child.gameObject.name += "-Warmup";
#endif

                // get or create the queue
                if (!_pool.TryGetValue(pid, out var queue))
                {
                    queue = QueuePool<GameObject>.Instantiate();
                    _pool.Add(pid, queue);
                }

                // put the object in the queue
                if (child.shouldBePooled)
                    child.gameObject.SetActive(false);

                child.transform.SetParent(_parent, false);

                Enqueue(child.gameObject, queue);
            }

            ListPool<NetworkIdentity>.Destroy(children);
            HashSetPool<PrefabPieceID>.Destroy(pidSet);
        }

        public void RegisterActiveScenePiece(NetworkIdentity identity)
        {
            if (!identity)
                return;

            var pieceIdentity = identity.transform.GetComponent<NetworkIdentity>();
            if (!pieceIdentity || pieceIdentity.prefabId >= 0)
                return;

            var target = pieceIdentity.gameObject;
            if (!_activeScenePieceSet.Add(target))
                return;

            var pid = new PrefabPieceID(pieceIdentity.scopedPrefabId, pieceIdentity.componentIndex);
            if (!_activeScenePieces.TryGetValue(pid, out var queue))
            {
                queue = QueuePool<GameObject>.Instantiate();
                _activeScenePieces.Add(pid, queue);
            }

            queue.Enqueue(target);
        }

        public void ReconcileActiveScenePieces()
        {
            if (_activeScenePieceSet.Count == 0)
            {
                ClearActiveScenePieceQueues();
                return;
            }

            var pieces = ListPool<GameObject>.Instantiate();
            foreach (var piece in _activeScenePieceSet)
            {
                if (piece)
                    pieces.Add(piece);
            }

            pieces.Sort((left, right) => GetDepth(right.transform).CompareTo(GetDepth(left.transform)));

            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (!piece || !_activeScenePieceSet.Remove(piece))
                    continue;

                PutActiveScenePieceBackInPool(piece);
            }

            ListPool<GameObject>.Destroy(pieces);
            _activeScenePieceSet.Clear();
            ClearActiveScenePieceQueues();
        }

        private static int GetDepth(Transform transform)
        {
            var depth = 0;
            while (transform)
            {
                depth++;
                transform = transform.parent;
            }

            return depth;
        }

        private bool TryGetActiveScenePiece(PrefabPieceID pid, out GameObject instance)
        {
            if (!_activeScenePieces.TryGetValue(pid, out var queue))
            {
                instance = null;
                return false;
            }

            while (queue.Count > 0)
            {
                if (queue.TryDequeue(out instance) && instance && _activeScenePieceSet.Remove(instance))
                    return true;
            }

            instance = null;
            return false;
        }

        private void PutActiveScenePieceBackInPool(GameObject target)
        {
            if (!target || !target.TryGetComponent<NetworkIdentity>(out var identity))
                return;

            DetachAdoptedDirectChildren(identity.transform, identity.transform.parent);

            if (!identity.shouldBePooled)
            {
                UnityProxy.DestroyDirectly(target);
                return;
            }

            var pid = new PrefabPieceID(identity.scopedPrefabId, identity.componentIndex);
            if (!_pool.TryGetValue(pid, out var queue))
            {
                queue = QueuePool<GameObject>.Instantiate();
                _pool.Add(pid, queue);
            }

            if (target.activeSelf)
                target.SetActive(false);

            identity.transform.SetParent(_parent, false);
            Enqueue(target, queue);
        }

        private void Enqueue(GameObject instance, Queue<GameObject> queue)
        {
            if (!instance || !_pooledObjects.Add(instance))
                return;

            queue.Enqueue(instance);
        }

        private void DetachAdoptedDirectChildren(Transform root, Transform safeParent)
        {
            using var directChildren = DisposableList<TransformIdentityPair>.Create(16);
            GetDirectChildren(root, directChildren);

            for (var i = 0; i < directChildren.Count; i++)
            {
                var child = directChildren[i].identity;
                if (!child)
                    continue;

                var target = child.gameObject;
                if (_activeScenePieceSet.Contains(target))
                    continue;

                child.transform.SetParent(safeParent, true);
            }
        }

        private void ClearActiveScenePieceQueues()
        {
            foreach (var (_, queue) in _activeScenePieces)
                QueuePool<GameObject>.Destroy(queue);

            _activeScenePieces.Clear();
        }

        void ClearToDestroy()
        {
            int c = _toDestroy.Count;

            if (c == 0)
                return;

            foreach (var go in _toDestroy)
            {
                if (go)
                    UnityProxy.DestroyImmediateDirectly(go);
            }

            _toDestroy.Clear();
        }

        private static bool TryGetFromPool(PoolPair pair, PrefabPieceID pid, out GameObject instance)
        {
            while (true)
            {
                var pool = pair.GetPool(pid.prefabId);

                if (pool == null)
                {
                    PurrLogger.LogError($"No pool available for piece '{pid}'; is the prefab registered on this peer?");
                    instance = null;
                    return false;
                }

                if (!pid.prefabId.isValid && pool.TryGetActiveScenePiece(pid, out instance))
                    return true;

                if (!pool._pool.TryGetValue(pid, out var queue))
                {
                    pool.Warmup(pid);

                    if (!pool._pool.TryGetValue(pid, out queue))
                    {
                        PurrLogger.LogError($"Piece '{pid}' is still missing from the pool after warmup");
                        instance = null;
                        return false;
                    }
                }

                if (queue.Count == 0)
                {
                    pool.Warmup(pid);

                    if (queue.Count == 0)
                    {
                        PurrLogger.LogError($"Pool for piece '{pid}' is empty after warmup");
                        instance = null;
                        return false;
                    }
                }

                while (queue.Count > 0)
                {
                    if (!queue.TryDequeue(out instance))
                        continue;

                    pool._pooledObjects.Remove(instance);

                    if (instance)
                    {
                        pool._toDestroy.Remove(instance);
                        return true;
                    }
                }

                if (queue.Count == 0)
                {
                    continue;
                }

                instance = null;
                return false;
            }
        }

        private void Warmup(PrefabPieceID pid)
        {
            if (!pid.prefabId.isValid)
                return;

            if (_prefabs == null)
            {
                PurrLogger.LogError($"Cannot warm up piece '{pid}': this pool has no prefab resolver");
                return;
            }

            if (_prefabs.TryGetPrefabData(pid.prefabId, out var prefabData))
                Warmup(prefabData);
            else PurrLogger.LogError($"Prefab with piece id of '{pid}' was not found");
        }

        public static int[] InternPath(DisposableList<int> path)
        {
            if (path.Count == 0)
                return Array.Empty<int>();

            if (!_internedPaths.TryGetValue(path.Count, out var candidates))
            {
                candidates = new List<int[]>();
                _internedPaths.Add(path.Count, candidates);
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var matches = true;

                for (var j = 0; j < candidate.Length; j++)
                {
                    if (candidate[j] == path[j])
                        continue;

                    matches = false;
                    break;
                }

                if (matches)
                    return candidate;
            }

            var interned = new int[path.Count];

            for (var i = 0; i < interned.Length; i++)
                interned[i] = path[i];

            if (candidates.Count < MAX_INTERNED_PATHS_PER_LENGTH)
                candidates.Add(interned);

            return interned;
        }

        public static DisposableList<int> GetInvPath(Transform parent, Transform transform)
        {
            var depth = DisposableList<int>.Create(16);
            var current = transform;

            if (parent == null)
                return depth;

            while (current != parent)
            {
                depth.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            return depth;
        }

        private static void GetNids(GameObject go, NetworkID baseNid, List<NetworkIdentity> createdNids)
        {
            var children = ListPool<NetworkIdentity>.Instantiate();

            go.GetComponents(children);

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                createdNids?.Add(child);
                child.SetID(new NetworkID(baseNid, (ulong)i));
            }

            ListPool<NetworkIdentity>.Destroy(children);
        }

        public static bool TryGetPrefabPrototype(PrefabID prefabId, out GameObjectPrototype prototype)
        {
            return _prefabPrototypes.TryGetValue(prefabId, out prototype);
        }

        public static bool TryGetOrCreatePrefabPrototype(PrefabData prefabData, out GameObjectPrototype prototype)
        {
            if (_prefabPrototypes.TryGetValue(prefabData.prefabId, out prototype))
                return true;

            var copy = UnityProxy.InstantiateDirectly(prefabData.prefab);
            NetworkManager.SetupPrefabInfo(copy, prefabData.prefabId, prefabData.pooled);
            prototype = GetFullPrototype(copy.transform, null, true);
            _prefabPrototypes.Add(prefabData.prefabId, prototype);
            UnityProxy.DestroyDirectly(copy);
            return true;
        }

        public static bool TryGetPrototype(Transform transform, PlayerID scope, List<NetworkIdentity> allChildren,
            out GameObjectPrototype prototype)
        {
            return TryGetPrototype(transform, scope, allChildren, out prototype, null);
        }

        internal static bool TryGetPrototype(Transform transform, PlayerID scope, List<NetworkIdentity> allChildren,
            out GameObjectPrototype prototype, List<NetworkIdentity> componentsOut, List<NetworkIdentity> prefetched = null)
        {
            var framework = DisposableList<GameObjectFrameworkPiece>.Create(16);
            if (!transform.TryGetComponent<NetworkIdentity>(out var rootId) || !rootId.id.HasValue ||
                !CapturePrototype(transform, rootId, scope, false, framework, allChildren, prefetched, componentsOut))
            {
                prototype = default;
                framework.Dispose();
                return false;
            }

            prototype = FinishPrototype(transform, rootId, framework, transform.parent == rootId.defaultParent);
            return true;
        }

        /// <summary>
        /// True when two observers see exactly the same identities of a hierarchy, so a prototype
        /// captured for one is valid for the other. Every other input of the capture is observer
        /// independent.
        /// </summary>
        internal static bool SameObserverPattern(List<NetworkIdentity> components, PlayerID a, PlayerID b)
        {
            for (int i = 0; i < components.Count; i++)
            {
                var identity = components[i];
                if (!identity)
                    continue;
                if (identity.IsObserverOrPending(a) != identity.IsObserverOrPending(b))
                    return false;
            }

            return true;
        }

        private struct PrototypeNode
        {
            public Transform transform;
            public NetworkIdentity identity;
            public int parent;
            public int firstChild;
            public int lastChild;
            public int nextSibling;
            public int childCount;
        }

        private static bool CapturePrototype(Transform root, NetworkIdentity rootId, PlayerID? observer,
            bool includeUnspawnedChildren, DisposableList<GameObjectFrameworkPiece> framework,
            List<NetworkIdentity> allChildren, List<NetworkIdentity> prefetched = null,
            List<NetworkIdentity> componentsOut = null)
        {
            using var componentLease = DisposableList<NetworkIdentity>.Create();
            var components = prefetched ?? componentLease.list;
            if (prefetched == null)
                root.GetComponentsInChildren(true, components);
            if (componentsOut != null)
                componentsOut.AddRange(components);

            int rootEnd = GetComponentGroupEnd(components, 0, root);
            if (observer.HasValue && !HasObserver(components, 0, rootEnd, observer.Value))
                return false;

            AppendComponents(components, 0, rootEnd, allChildren);
            if (rootEnd == components.Count)
            {
                AddPrototypePiece(framework, root, rootId, null, 0);
                return true;
            }

            using var nodeLease = DisposableList<PrototypeNode>.Create();
            using var traversalLease = DisposableList<int>.Create();
            var nodes = nodeLease.list;
            var traversal = traversalLease.list;
            nodes.Add(new PrototypeNode { transform = root, identity = rootId, parent = -1 });
            traversal.Add(0);
            Transform excludedRoot = null;

            // Unity returns components depth-first. Group siblings on the same GameObject and
            // use live ancestry to recover network parents without scanning any subtree again.
            for (int start = rootEnd; start < components.Count;)
            {
                var component = components[start];
                if (!component)
                {
                    start++;
                    continue;
                }

                var current = component.transform;
                int end = GetComponentGroupEnd(components, start, current);
                if (excludedRoot && current.IsChildOf(excludedRoot))
                {
                    start = SkipExcludedSubtree(components, end, ref excludedRoot);
                    continue;
                }
                excludedRoot = null;

                while (traversal.Count > 1 && !current.IsChildOf(nodes[traversal[^1]].transform))
                    traversal.RemoveAt(traversal.Count - 1);

                int parentIndex = traversal[^1];
                var parent = nodes[parentIndex];
                // Match the existing canonical component selection; every sibling still participates
                // in visibility and serialization through its range in the original component list.
                if (!current.TryGetComponent<NetworkIdentity>(out var identity) ||
                    (parent.identity.isSceneObject && identity.skipSceneAutoSpawning) ||
                    (!includeUnspawnedChildren && !identity.id.HasValue) ||
                    (observer.HasValue && !HasObserver(components, start, end, observer.Value)))
                {
                    excludedRoot = current;
                    start = end;
                    continue;
                }

                int index = nodes.Count;
                if (parent.childCount == 0)
                    parent.firstChild = index;
                else
                {
                    var previous = nodes[parent.lastChild];
                    previous.nextSibling = index;
                    nodes[parent.lastChild] = previous;
                }
                parent.lastChild = index;
                parent.childCount++;
                nodes[parentIndex] = parent;
                nodes.Add(new PrototypeNode { transform = current, identity = identity, parent = parentIndex });
                traversal.Add(index);
                AppendComponents(components, start, end, allChildren);
                start = end;
            }

            // The wire framework is breadth-first; custom serialization follows the depth-first
            // component order appended above. Reuse the ancestry stack as the breadth-first queue.
            traversal.Clear();
            traversal.Add(0);
            for (int i = 0; i < traversal.Count; i++)
            {
                var node = nodes[traversal[i]];
                var parent = node.parent < 0 ? null : nodes[node.parent].transform;
                AddPrototypePiece(framework, node.transform, node.identity, parent, node.childCount);
                int child = node.firstChild;
                for (int c = 0; c < node.childCount; c++)
                {
                    traversal.Add(child);
                    child = nodes[child].nextSibling;
                }
            }
            return true;
        }

        private static int SkipExcludedSubtree(List<NetworkIdentity> components, int start, ref Transform root)
        {
            // A subtree occupies one contiguous range in Unity's depth-first result. Grow a
            // search window before bisecting it so small excluded branches stay cheap.
            int lower = start;
            int upper = start;
            for (long step = 1; upper < components.Count; step *= 2)
            {
                var component = components[upper];
                if (!component)
                    return lower;
                if (!component.transform.IsChildOf(root))
                    break;
                lower = upper + 1;
                upper += (int)Math.Min(step, components.Count - upper);
            }

            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                var component = components[middle];
                // A missing entry cannot establish a boundary. Keep the outer exclusion
                // guard and resume from the last lower bound that was proven safe.
                if (!component)
                    return lower;
                if (component.transform.IsChildOf(root))
                    lower = middle + 1;
                else
                    upper = middle;
            }
            // The boundary is proven, so the caller need not check this root again.
            root = null;
            return lower;
        }

        private static int GetComponentGroupEnd(List<NetworkIdentity> components, int start, Transform transform)
        {
            int end = start + 1;
            while (end < components.Count && (!components[end] || components[end].transform == transform))
                end++;
            return end;
        }

        private static bool HasObserver(List<NetworkIdentity> components, int start, int end, PlayerID observer)
        {
            for (int i = start; i < end; i++)
            {
                if (components[i] && components[i].IsObserverOrPending(observer))
                    return true;
            }
            return false;
        }

        private static void AppendComponents(List<NetworkIdentity> components, int start, int end,
            List<NetworkIdentity> allChildren)
        {
            if (allChildren == null)
                return;
            for (int i = start; i < end; i++)
            {
                if (components[i])
                    allChildren.Add(components[i]);
            }
        }

        private static void AddPrototypePiece(DisposableList<GameObjectFrameworkPiece> framework,
            Transform transform, NetworkIdentity identity, Transform parent, int childCount)
        {
            transform.GetLocalPositionAndRotation(out var localPos, out var localRot);
            framework.Add(new GameObjectFrameworkPiece(
                new LocalTransform(localPos, localRot, transform.localScale),
                new PrefabPieceID(identity.scopedPrefabId, identity.componentIndex),
                identity.id ?? default, childCount, identity.gameObject.activeSelf,
                GetLiveRelativePath(parent, identity)));
        }

        private static GameObjectPrototype FinishPrototype(Transform transform, NetworkIdentity rootId,
            DisposableList<GameObjectFrameworkPiece> framework, bool isDefaultParent)
        {
            var parentNid = rootId.parent ? rootId.parent : default;
            var parentID = parentNid?.id;
            int[] path = null;

            if (parentNid)
            {
                using var invPath = GetInvPath(parentNid.transform, transform);
                path = invPath.list.ToArray();
            }

            return new GameObjectPrototype(transform.localPosition, transform.localRotation, transform.localScale, parentID, path,
                framework, isDefaultParent ? transform.GetSiblingIndex() : null);
        }

        public static GameObjectPrototype GetFullPrototype(Transform transform, List<NetworkIdentity> allChildren = null,
            bool includeUnspawnedChildren = false)
        {
            var framework = DisposableList<GameObjectFrameworkPiece>.Create(16);
            if (!transform.TryGetComponent<NetworkIdentity>(out var rootId))
            {
                return new GameObjectPrototype(transform.localPosition, transform.localRotation, transform.localScale, null, null, framework,
                    null);
            }

            CapturePrototype(transform, rootId, null, includeUnspawnedChildren, framework, allChildren);
            return FinishPrototype(transform, rootId, framework, transform.parent == rootId.defaultParent);
        }

        public static bool TryBuildPrototype(PoolPair pair, GameObjectPrototype prototype,
            List<NetworkIdentity> createdNids, out GameObject result, out bool shouldBeActive)
        {
            try
            {
                if (prototype.framework.Count == 0)
                {
                    result = null;
                    shouldBeActive = false;
                    return false;
                }

                return TryBuildPrototypeHelper(pair, prototype, createdNids, null, 0, out result,
                    out shouldBeActive);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Build prototype exception: {e.Message}\n{e.StackTrace}");
                result = null;
                shouldBeActive = false;
                return false;
            }
            finally
            {
                pair.prefabPool?.ClearToDestroy();
            }
        }

        private static bool TryBuildPrototypeHelper(PoolPair pair, GameObjectPrototype prototype,
            List<NetworkIdentity> createdNids, Transform parent, int currentIdx, out GameObject result, out bool shouldBeActive)
        {
            var framework = prototype.framework;
            var current = framework[currentIdx];
            var childCount = current.childCount;

            if (!TryGetFromPool(pair, current.pid, out var instance))
            {
                result = null;
                shouldBeActive = false;
                return false;
            }

            var trs = instance.transform;
            using var siblings = DisposableList<NetworkIdentity>.Create(16);
            instance.GetComponents(siblings.list);
            var nid = siblings.Count > 0 ? siblings[0] : null;

            shouldBeActive = current.isActive;
            GetNids(instance, current.id, createdNids);

            if (parent)
            {
                WalkThePath(parent, trs, current.inversedRelativePath, false);
                instance.SetActive(shouldBeActive);

                var p = parent.TryGetComponent(out NetworkIdentity parentId) ? parentId : null;

                foreach (var sib in siblings)
                {
                    sib.parent = p;
                    sib.invertedPathToNearestParent = current.inversedRelativePath;
                }
            }
            else
            {
                if (!shouldBeActive && instance.activeSelf)
                    instance.SetActive(false);

                foreach (var sib in siblings)
                {
                    sib.parent = null;
                    sib.invertedPathToNearestParent = current.inversedRelativePath;
                }
            }

            if (nid)
                nid.ClearDirectChildren();

            int childScopeStart = 1;

            for (int i = 0; i < currentIdx; ++i)
                childScopeStart += framework[i].childCount;

            current.localTransform.Apply(trs);

            // Process each child in sequence - children start after all siblings
            for (var j = 0; j < childCount; j++)
            {
                if (!TryBuildPrototypeHelper(
                    pair,
                    prototype,
                    createdNids,
                    trs,
                    childScopeStart + j,
                    out var childGo,
                    out _))
                {
                    PutBackInPool(pair, instance);
                    result = null;
                    shouldBeActive = false;
                    return false;
                }

                if (nid && childGo && childGo.TryGetComponent<NetworkIdentity>(out var childNid))
                    nid.AddDirectChild(childNid);
            }

            result = instance;
            return true;
        }

        public static void WalkThePath(Transform parent, Transform instance, int[] inversedPath,
            bool worldPositionStays)
        {
            if (inversedPath == null || inversedPath.Length == 0)
            {
                instance.SetParent(parent, worldPositionStays);
                return;
            }

            int len = inversedPath.Length;
            for (var i = len - 1; i >= 1; i--)
            {
                var siblingIndex = inversedPath[i];

                if (parent.childCount <= siblingIndex)
                {
                    PurrLogger.LogWarning($"Parent {parent} doesn't have child with index {siblingIndex}");
                    break;
                }

                var sibling = parent.GetChild(siblingIndex);
                parent = sibling;
            }

            instance.SetParent(parent, worldPositionStays);

            var targetSiblingIndex = inversedPath[0];

            if (parent.childCount <= targetSiblingIndex)
                targetSiblingIndex = parent.childCount;

            instance.SetSiblingIndex(targetSiblingIndex);
        }

        /// <summary>
        /// Live replacement for the cached invertedPathToNearestParent: the cache is refreshed on
        /// reparents but not when siblings are destroyed or reordered, so captured prototypes must
        /// re-read the sibling indices from the transform at capture time.
        /// </summary>
        private static int[] GetLiveRelativePath(Transform parent, NetworkIdentity identity)
        {
            var pathParent = parent;
            if (!pathParent && identity.parent)
                pathParent = identity.parent.transform;
            if (!pathParent)
                return Array.Empty<int>();

            using var invPath = GetInvPath(pathParent, identity.transform);
            return invPath.list.ToArray();
        }

        public static void GetDirectChildren(Transform root, DisposableList<TransformIdentityPair> children)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                GetDirectChildrenHelper(child, children);
            }
        }

        public static void GetDirectChildrenWithRoot(Transform root, DisposableList<TransformIdentityPair> children)
        {
            if (GetDirectChildrenHelper(root, children))
                return;

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                GetDirectChildrenHelper(child, children);
            }
        }

        private static bool GetDirectChildrenHelper(Transform root, DisposableList<TransformIdentityPair> children)
        {
            if (root.TryGetComponent<NetworkIdentity>(out var identity))
            {
                children.Add(new TransformIdentityPair(root, identity));
                return true;
            }

            for (var i = 0; i < root.transform.childCount; i++)
            {
                var child = root.transform.GetChild(i);
                GetDirectChildrenHelper(child, children);
            }

            return false;
        }

        public void Dispose()
        {
            foreach (var (_, queue) in _pool)
                QueuePool<GameObject>.Destroy(queue);
            _pool.Clear();
            _pooledObjects.Clear();
            _alreadyWarmedUp.Clear();
            ClearActiveScenePieceQueues();
            _activeScenePieceSet.Clear();

            if (_parent)
                UnityProxy.DestroyDirectly(_parent.gameObject);
        }
    }
}
