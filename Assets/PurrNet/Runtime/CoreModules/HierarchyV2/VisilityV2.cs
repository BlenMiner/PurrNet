using System.Collections.Generic;
using PurrNet.Pooling;
using Unity.Profiling;
using UnityEngine;

namespace PurrNet.Modules
{
    internal class VisilityV2
    {
        static readonly ProfilerMarker _refreshMarker = new ProfilerMarker("PurrNet.VisibilityV2.Refresh");
        static readonly ProfilerMarker _evaluateMarker = new ProfilerMarker("PurrNet.VisibilityV2.Evaluate");
        static readonly ProfilerMarker _evaluateAllMarker = new ProfilerMarker("PurrNet.VisibilityV2.EvaluateAll");
        static readonly ProfilerMarker _clearMarker = new ProfilerMarker("PurrNet.VisibilityV2.Clear");
        static readonly ProfilerMarker _clearPlayerMarker = new ProfilerMarker("PurrNet.VisibilityV2.ClearPlayer");
        static readonly ProfilerMarker _notifyMarker = new ProfilerMarker("PurrNet.VisibilityV2.Notify");

        readonly NetworkManager _manager;
        readonly NetworkVisibilityRuleSet _defaultRuleSet;

        public delegate void VisibilityChanged(PlayerID player, Transform scope, bool hasVisibility);

        public event VisibilityChanged visibilityChanged;

        public delegate void VisibilityCleared(Transform scope, HashSet<PlayerID> players);

        public event VisibilityCleared visibilityCleared;

        public VisilityV2(NetworkManager manager)
        {
            _manager = manager;
            _defaultRuleSet = manager.visibilityRules;
        }

        public void RefreshVisibilityForGameObject(PlayerID player, Transform transform)
        {
            if (transform && transform.TryGetComponent(out NetworkIdentity identity))
                RefreshVisibilityForGameObject(player, identity);
        }

        public void RefreshVisibilityForGameObject(PlayerID player, NetworkIdentity identity,
            NetworkIdentity parent = null)
        {
            using var marker = _refreshMarker.Auto();
            if (!identity)
                return;

            bool isParentVisible = !parent || parent.IsObserverOrPending(player);
            var frame = EvaluateNode(player, identity, _defaultRuleSet, isParentVisible, false);
            if (frame.childCount == 0)
            {
                if (frame.shouldTrigger)
                    Notify(player, frame.scope, frame.isVisible);
                return;
            }

            using var traversalLease = DisposableList<VisibilityFrame>.Create(16);
            var traversal = traversalLease.list;

            while (true)
            {
                if (TryGetNextChild(ref frame, out var child))
                {
                    traversal.Add(frame);
                    frame = EvaluateNode(player, child, frame.rules, frame.isVisible, frame.wasParentDirtied);
                    continue;
                }

                if (frame.shouldTrigger)
                    Notify(player, frame.scope, frame.isVisible);

                if (traversal.Count == 0)
                    break;

                int last = traversal.Count - 1;
                frame = traversal[last];
                traversal.RemoveAt(last);
            }
        }

        private struct VisibilityFrame
        {
            public Transform scope;
            public IReadOnlyList<NetworkIdentity> children;
            public int childCount;
            public int nextChild;
            public NetworkVisibilityRuleSet rules;
            public bool isVisible;
            public bool wasParentDirtied;
            public bool shouldTrigger;
        }

        private VisibilityFrame EvaluateNode(PlayerID player, NetworkIdentity identity,
            NetworkVisibilityRuleSet rules, bool isParentVisible, bool wasParentDirtied)
        {
            var identities = identity.siblingIdentities;
            if (identities.Length == 0)
                return default;

            var scope = identity.transform;
            bool isVisible = Evaluate(player, identities, ref rules, isParentVisible, out bool fullyChanged);
            bool shouldTrigger = !wasParentDirtied && fullyChanged;
            var children = identities[0].directChildren;
            return new VisibilityFrame
            {
                scope = scope,
                children = children,
                childCount = children?.Count ?? 0,
                rules = rules,
                isVisible = isVisible,
                wasParentDirtied = wasParentDirtied || shouldTrigger,
                shouldTrigger = shouldTrigger
            };
        }

        private static bool TryGetNextChild(ref VisibilityFrame frame, out NetworkIdentity child)
        {
            while (frame.nextChild < frame.childCount && frame.nextChild < frame.children.Count)
            {
                child = frame.children[frame.nextChild++];
                if (child)
                    return true;
            }

            child = null;
            return false;
        }

        public void ClearVisibilityForGameObject(NetworkIdentity identity)
        {
            using var marker = _clearMarker.Auto();
            if (!identity)
                return;

            var scope = identity.transform;
            var affectedPlayers = HashSetPool<PlayerID>.Instantiate();
            try
            {
                ClearObservers(identity, null, affectedPlayers);
                if (visibilityCleared != null)
                {
                    if (affectedPlayers.Count > 0)
                    {
                        using var notify = _notifyMarker.Auto();
                        visibilityCleared.Invoke(scope, affectedPlayers);
                    }
                }
                else
                {
                    foreach (var player in affectedPlayers)
                        Notify(player, scope, false);
                }
            }
            finally
            {
                HashSetPool<PlayerID>.Destroy(affectedPlayers);
            }
        }

        public void ClearVisibilityForGameObject(NetworkIdentity identity, PlayerID player)
        {
            using var marker = _clearPlayerMarker.Auto();
            if (!identity)
                return;

            var scope = identity.transform;
            ClearObservers(identity, player, null);
            Notify(player, scope, false);
        }

        private static void ClearObservers(NetworkIdentity root, PlayerID? player, HashSet<PlayerID> affectedPlayers)
        {
            var children = ClearNode(root, player, affectedPlayers);
            if (children == null || children.Count == 0)
                return;

            using var traversalLease = DisposableList<NetworkIdentity>.Create(16);
            var traversal = traversalLease.list;
            for (var i = children.Count - 1; i >= 0; i--)
                traversal.Add(children[i]);

            while (traversal.Count > 0)
            {
                int last = traversal.Count - 1;
                var current = traversal[last];
                traversal.RemoveAt(last);

                if (!current)
                    continue;

                children = ClearNode(current, player, affectedPlayers);

                if (children == null)
                    continue;

                for (var i = children.Count - 1; i >= 0; i--)
                    traversal.Add(children[i]);
            }
        }

        private static IReadOnlyList<NetworkIdentity> ClearNode(NetworkIdentity current, PlayerID? player,
            HashSet<PlayerID> affectedPlayers)
        {
            var identities = current.siblingIdentities;
            if (identities.Length == 0)
                return null;

            for (var i = 0; i < identities.Length; i++)
            {
                var identity = identities[i];
                if (!identity)
                    continue;

                if (player.HasValue)
                {
                    identity.TryRemoveObserver(player.Value);
                }
                else
                {
                    AddAll(affectedPlayers, identity.observers);
                    if (identity.hasPendingObservers)
                        AddAll(affectedPlayers, identity.pendingObservers);
                    identity.ClearObservers();
                }
            }

            return identities[0].directChildren;
        }

        private static void AddAll(HashSet<PlayerID> target, IReadOnlyList<PlayerID> players)
        {
            for (var i = 0; i < players.Count; i++)
                target.Add(players[i]);
        }

        private void Notify(PlayerID player, Transform scope, bool isVisible)
        {
            using var marker = _notifyMarker.Auto();
            visibilityChanged?.Invoke(player, scope, isVisible);
        }

        public void EvaluateAll(IReadOnlyList<PlayerID> players, List<NetworkIdentity> identities)
        {
            using var marker = _evaluateAllMarker.Auto();
            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            try
            {
                for (var i = 0; i < identities.Count; i++)
                {
                    var identity = identities[i];
                    if (!identity)
                        continue;
                    var root = identity.GetRootIdentity();
                    if (root)
                    {
                        var siblings = root.siblingIdentities;
                        if (siblings.Length > 0)
                            roots.Add(siblings[0]);
                    }
                }

                for (var i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    foreach (var root in roots)
                        RefreshVisibilityForGameObject(player, root);
                }
            }
            finally
            {
                HashSetPool<NetworkIdentity>.Destroy(roots);
            }
        }

        /// <summary>
        /// Evaluate visibility of the object.
        /// Also adds/removes observers based on the visibility.
        /// </summary>
        private bool Evaluate(PlayerID player, NetworkIdentity[] identities,
            ref NetworkVisibilityRuleSet rules, bool isParentVisible, out bool fullyChanged)
        {
            using var marker = _evaluateMarker.Auto();
            fullyChanged = false;

            if (!isParentVisible)
            {
                for (var i = 0; i < identities.Length; i++)
                    if (identities[i])
                        identities[i].TryRemoveObserver(player);
                return false;
            }

            bool isAnyVisible = false;

            for (var i = 0; i < identities.Length; i++)
            {
                var identity = identities[i];
                if (!identity)
                    continue;

                if (identity.whitelist.Contains(player))
                {
                    isAnyVisible = true;
                    if (ShouldAddObserver(player, identity) && identity.TryAddObserver(player))
                        fullyChanged = true;
                    continue;
                }

                if (identity.blacklist.Contains(player))
                {
                    if (identity.TryRemoveObserver(player))
                        fullyChanged = true;
                    continue;
                }

                var r = identity.GetOverrideOrDefault(rules);

                if (r && r.childrenInherit)
                    rules = r;

                if (!r)
                {
                    isAnyVisible = true;
                    if (ShouldAddObserver(player, identity) && identity.TryAddObserver(player))
                        fullyChanged = true;
                    continue;
                }

                if (identity.owner == player)
                {
                    isAnyVisible = true;
                    if (ShouldAddObserver(player, identity) && identity.TryAddObserver(player))
                        fullyChanged = true;
                    continue;
                }

                if (!r.CanSee(player, identity))
                {
                    if (identity.TryRemoveObserver(player))
                        fullyChanged = true;
                }
                else
                {
                    isAnyVisible = true;
                    if (ShouldAddObserver(player, identity) && identity.TryAddObserver(player))
                        fullyChanged = true;
                }
            }

            return isAnyVisible;
        }

        private bool ShouldAddObserver(PlayerID player, NetworkIdentity identity)
        {
#if ADDRESSABLES_PURRNET_SUPPORT
            return ShouldAddObserverAddressables(player, identity);
#else
            return true;
#endif
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        private bool ShouldAddObserverAddressables(PlayerID player, NetworkIdentity identity)
        {
            if (!_manager.networkRules)
                return true;

            if (!_manager.networkRules.AddressablesWaitForLoadBeforeObserver)
                return true;

            if (!_manager.prefabResolver.TryGetAddressableGuid(identity.scopedPrefabId, out var guid))
                return true;

            if (!_manager.TryGetModule<AddressablesSyncModule>(true, out var sync))
                return true;

            if (sync.ClientHasLoaded(player, guid))
                return true;

            sync.RequestPlayerToLoad(player, guid);
            return false;
        }
#endif
    }
}
