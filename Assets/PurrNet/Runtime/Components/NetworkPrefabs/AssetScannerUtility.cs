#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet
{
    /// <summary>
    /// Shared editor utility for scanning folders for assets.
    /// Used by NetworkPrefabs, NetworkAssets, and AddressableNetworkPrefabs
    /// to provide consistent scanning, sorting, and sync behavior.
    /// Lives in the Runtime assembly so all types can reference it,
    /// but only compiles in the editor.
    /// </summary>
    public static class AssetScannerUtility
    {
        public struct ScanResult
        {
            public Object asset;
            public string guid;
            public string assetPath;
        }

        /// <summary>
        /// Resolves a folder Object to its AssetDatabase path.
        /// When folder is null and fallbackToAssetsRoot is true, returns "Assets".
        /// Returns null if the folder cannot be resolved and fallback is disabled.
        /// </summary>
        public static string ResolveFolderPath(Object folder, bool fallbackToAssetsRoot = true)
        {
            if (folder != null)
            {
                string path = AssetDatabase.GetAssetPath(folder);
                if (!string.IsNullOrEmpty(path))
                    return path;
            }

            return fallbackToAssetsRoot ? "Assets" : null;
        }

        /// <summary>
        /// Scans a folder for prefabs, optionally filtering to those with NetworkIdentity.
        /// Results are sorted deterministically by GUID.
        /// </summary>
        public static List<ScanResult> ScanPrefabs(Object folder, bool networkOnly, bool fallbackToAssetsRoot)
        {
            var results = new List<ScanResult>();
            string folderPath = ResolveFolderPath(folder, fallbackToAssetsRoot);

            if (string.IsNullOrEmpty(folderPath))
                return results;

            string[] guids = AssetDatabase.FindAssets("t:prefab", new[] { folderPath });
            var identities = new List<NetworkIdentity>();

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (!prefab) continue;

                if (networkOnly)
                {
                    identities.Clear();
                    prefab.GetComponentsInChildren(true, identities);
                    if (identities.Count == 0) continue;
                }

                results.Add(new ScanResult
                {
                    asset = prefab,
                    guid = guids[i],
                    assetPath = assetPath
                });
            }

            results.Sort(CompareByGuid);
            return results;
        }

        /// <summary>
        /// Walks a scene's dependencies without passing through registry assets. A scene that carries its
        /// own NetworkPrefabs or NetworkAssets references the registry, and the registry references every
        /// entry it already holds, so a plain recursive walk would re-find stale entries forever.
        /// </summary>
        private static string[] CollectSceneDependencies(string scenePath)
        {
            var collected = new List<string>();
            var visited = new HashSet<string> { scenePath };
            var pending = new Queue<string>(AssetDatabase.GetDependencies(scenePath, false));

            while (pending.Count > 0)
            {
                string path = pending.Dequeue();
                if (string.IsNullOrEmpty(path) || !visited.Add(path))
                    continue;

                if (IsRegistryAsset(path))
                    continue;

                collected.Add(path);

                string[] nested = AssetDatabase.GetDependencies(path, false);
                for (int i = 0; i < nested.Length; i++)
                    pending.Enqueue(nested[i]);
            }

            return collected.ToArray();
        }

        private static bool IsRegistryAsset(string path)
        {
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                return false;

            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null)
                return false;

            return typeof(PrefabProviderScriptable).IsAssignableFrom(type) || type == typeof(NetworkAssets);
        }

        public static List<ScanResult> ScanScenePrefabs(string scenePath, bool networkOnly)
        {
            var results = new List<ScanResult>();

            if (string.IsNullOrEmpty(scenePath))
                return results;

            string[] dependencies = CollectSceneDependencies(scenePath);
            var identities = new List<NetworkIdentity>();

            for (int i = 0; i < dependencies.Length; i++)
            {
                string assetPath = dependencies[i];
                if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (!prefab) continue;

                if (networkOnly)
                {
                    identities.Clear();
                    prefab.GetComponentsInChildren(true, identities);
                    if (identities.Count == 0) continue;
                }

                results.Add(new ScanResult
                {
                    asset = prefab,
                    guid = AssetDatabase.AssetPathToGUID(assetPath),
                    assetPath = assetPath
                });
            }

            results.Sort(CompareByGuid);
            return results;
        }

        /// <summary>
        /// Scans a folder for general assets, filtering by enabled types.
        /// Includes sub-assets (e.g. sprites inside a texture).
        /// Results are sorted deterministically by GUID.
        /// </summary>
        public static List<ScanResult> ScanAssets(Object folder, Type[] enabledTypes, bool fallbackToAssetsRoot)
        {
            var results = new List<ScanResult>();
            string folderPath = ResolveFolderPath(folder, fallbackToAssetsRoot);

            if (string.IsNullOrEmpty(folderPath))
                return results;

            string[] guids = AssetDatabase.FindAssets("", new[] { folderPath });
            var seen = new HashSet<Object>();

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                CollectAssetsAtPath(assetPath, guids[i], enabledTypes, seen, results);
            }

            results.Sort(CompareByGuid);
            return results;
        }

        public static List<ScanResult> ScanSceneAssets(string scenePath, Type[] enabledTypes)
        {
            var results = new List<ScanResult>();

            if (string.IsNullOrEmpty(scenePath))
                return results;

            string[] dependencies = CollectSceneDependencies(scenePath);
            var seen = new HashSet<Object>();

            for (int i = 0; i < dependencies.Length; i++)
            {
                string assetPath = dependencies[i];
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid)) continue;

                CollectAssetsAtPath(assetPath, guid, enabledTypes, seen, results);
            }

            results.Sort(CompareByGuid);
            return results;
        }

        private static void CollectAssetsAtPath(string assetPath, string guid, Type[] enabledTypes,
            HashSet<Object> seen, List<ScanResult> results)
        {
            if (string.IsNullOrEmpty(assetPath) || assetPath.EndsWith(".unity")) return;

            var allAtPath = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (var obj in allAtPath)
            {
                if (!obj) continue;

                var ns = obj.GetType().Namespace;
                if (ns != null && ns.Contains("UnityEditor")) continue;

                if (!seen.Add(obj)) continue;

                bool matchesType = false;
                for (int t = 0; t < enabledTypes.Length; t++)
                {
                    if (enabledTypes[t].IsAssignableFrom(obj.GetType()))
                    {
                        matchesType = true;
                        break;
                    }
                }

                if (!matchesType) continue;

                string objGuid = guid;
                // For sub-assets, append the local file ID to make the GUID unique
                if (AssetDatabase.IsSubAsset(obj))
                {
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string _, out long localId))
                        objGuid = $"{guid}_{localId}";
                }

                results.Add(new ScanResult
                {
                    asset = obj,
                    guid = objGuid,
                    assetPath = assetPath
                });
            }
        }

        /// <summary>
        /// Collects prefab GUIDs already owned by linked NetworkPrefabs.
        /// </summary>
        public static HashSet<string> CollectLinkedNetworkPrefabGuids(NetworkPrefabs root)
        {
            var guids = new HashSet<string>();
            var visited = new HashSet<NetworkPrefabs>();

            if (!root || root.linkedNetworkPrefabs == null)
                return guids;

            visited.Add(root);

            for (int i = 0; i < root.linkedNetworkPrefabs.Count; i++)
                Collect(root.linkedNetworkPrefabs[i]);

            return guids;

            void Collect(NetworkPrefabs networkPrefabs)
            {
                if (!networkPrefabs || !visited.Add(networkPrefabs))
                    return;

                for (int i = 0; i < networkPrefabs.prefabs.Count; i++)
                {
                    var entry = networkPrefabs.prefabs[i];
                    var guid = entry.guid;

                    if (string.IsNullOrEmpty(guid) && entry.prefab)
                    {
                        var path = AssetDatabase.GetAssetPath(entry.prefab);
                        guid = AssetDatabase.AssetPathToGUID(path);
                    }

                    if (!string.IsNullOrEmpty(guid))
                        guids.Add(guid);
                }

                if (networkPrefabs.linkedNetworkPrefabs == null)
                    return;

                for (int i = 0; i < networkPrefabs.linkedNetworkPrefabs.Count; i++)
                    Collect(networkPrefabs.linkedNetworkPrefabs[i]);
            }
        }

        /// <summary>
        /// Collects assets already owned by linked NetworkAssets.
        /// </summary>
        public static HashSet<Object> CollectLinkedNetworkAssets(NetworkAssets root)
        {
            var linkedAssets = new HashSet<Object>();
            var visited = new HashSet<NetworkAssets>();

            if (!root || root.linkedNetworkAssets == null)
                return linkedAssets;

            visited.Add(root);

            for (int i = 0; i < root.linkedNetworkAssets.Count; i++)
                Collect(root.linkedNetworkAssets[i]);

            return linkedAssets;

            void Collect(NetworkAssets networkAssets)
            {
                if (!networkAssets || !visited.Add(networkAssets))
                    return;

                for (int i = 0; i < networkAssets.assets.Count; i++)
                {
                    var asset = networkAssets.assets[i];
                    if (asset)
                        linkedAssets.Add(asset);
                }

                if (networkAssets.linkedNetworkAssets == null)
                    return;

                for (int i = 0; i < networkAssets.linkedNetworkAssets.Count; i++)
                    Collect(networkAssets.linkedNetworkAssets[i]);
            }
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        /// <summary>
        /// Collects prefab GUIDs already owned by linked AddressableNetworkPrefabs.
        /// </summary>
        public static HashSet<string> CollectLinkedAddressablePrefabGuids(AddressableNetworkPrefabs root)
        {
            var guids = new HashSet<string>();
            var visited = new HashSet<AddressableNetworkPrefabs>();

            if (!root || root.linkedAddressablePrefabs == null)
                return guids;

            visited.Add(root);

            for (int i = 0; i < root.linkedAddressablePrefabs.Count; i++)
                Collect(root.linkedAddressablePrefabs[i]);

            return guids;

            void Collect(AddressableNetworkPrefabs provider)
            {
                if (!provider || !visited.Add(provider))
                    return;

                var entries = provider.entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var asset = entries[i].asset;
                    if (asset != null && asset.RuntimeKeyIsValid() && !string.IsNullOrEmpty(asset.AssetGUID))
                        guids.Add(asset.AssetGUID);
                }

                if (provider.linkedAddressablePrefabs == null)
                    return;

                for (int i = 0; i < provider.linkedAddressablePrefabs.Count; i++)
                    Collect(provider.linkedAddressablePrefabs[i]);
            }
        }

        /// <summary>
        /// Removes addressable entries whose GUIDs are already owned by linked providers.
        /// </summary>
        public static bool RemoveAddressableEntriesByGuid(AddressableNetworkPrefabs target, HashSet<string> guids)
        {
            if (!target || guids.Count == 0)
                return false;

            var serializedTarget = new SerializedObject(target);
            var entries = serializedTarget.FindProperty("_entries");
            bool removed = false;

            for (int i = entries.arraySize - 1; i >= 0; i--)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var asset = entry.FindPropertyRelative("asset");
                var guid = asset.FindPropertyRelative("m_AssetGUID")?.stringValue;

                if (string.IsNullOrEmpty(guid) || !guids.Contains(guid))
                    continue;

                entries.DeleteArrayElementAtIndex(i);
                removed = true;
            }

            if (removed)
                serializedTarget.ApplyModifiedProperties();

            return removed;
        }
#endif

        /// <summary>
        /// Deterministic comparator: by GUID (ordinal), then by name, then by instance ID.
        /// </summary>
        public static int CompareByGuid(ScanResult a, ScanResult b)
        {
            int c = string.CompareOrdinal(a.guid, b.guid);
            if (c != 0) return c;

            string na = a.asset ? a.asset.name : string.Empty;
            string nb = b.asset ? b.asset.name : string.Empty;
            c = string.CompareOrdinal(na, nb);
            if (c != 0) return c;

            return string.CompareOrdinal(a.assetPath, b.assetPath);
        }

        /// <summary>
        /// Synchronizes an existing entry list with scan results.
        /// Preserves user settings (e.g. pooling) on existing entries.
        /// Returns the number of entries added and removed.
        /// </summary>
        public static (int added, int removed) SyncEntries<T>(
            List<T> existing,
            List<ScanResult> found,
            Func<T, string> getGuid,
            Func<T, Object> getAsset,
            Func<ScanResult, T> createNew,
            Func<T, bool> isValid)
        {
            int removed = existing.RemoveAll(e => !isValid(e));

            var existingGuids = new HashSet<string>();
            for (int i = 0; i < existing.Count; i++)
            {
                string g = getGuid(existing[i]);
                if (!string.IsNullOrEmpty(g))
                    existingGuids.Add(g);
            }

            var foundGuids = new HashSet<string>();
            for (int i = 0; i < found.Count; i++)
                foundGuids.Add(found[i].guid);

            // Remove entries not in found set
            for (int i = existing.Count - 1; i >= 0; i--)
            {
                string g = getGuid(existing[i]);
                if (string.IsNullOrEmpty(g) || !foundGuids.Contains(g))
                {
                    existing.RemoveAt(i);
                    removed++;
                }
            }

            // Add new entries
            int added = 0;
            for (int i = 0; i < found.Count; i++)
            {
                if (!existingGuids.Contains(found[i].guid))
                {
                    existing.Add(createNew(found[i]));
                    added++;
                }
            }

            return (added, removed);
        }
    }
}
#endif
