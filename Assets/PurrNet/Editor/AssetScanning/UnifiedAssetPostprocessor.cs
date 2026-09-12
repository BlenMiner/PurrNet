#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace PurrNet
{
    /// <summary>
    /// Unified asset postprocessor that monitors folder changes and triggers
    /// regeneration for all auto-generating asset registries (NetworkPrefabs,
    /// NetworkAssets, and AddressableNetworkPrefabs).
    /// Replaces the individual NetworkAssetsPostprocessor.
    /// </summary>
    public class UnifiedAssetPostprocessor : AssetPostprocessor
    {
        private static readonly List<AssetWatcher> _watchers = new();
        private static bool _cacheDirty = true;

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
#if PURRNET_DISABLE_ASSET_REGISTRY_AUTO_GENERATION
            return;
#else
            if (HasRegistryChange(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
                _cacheDirty = true;

            if (!HasProcessableChange(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
                return;

            RebuildCacheIfNeeded();

            for (int i = 0; i < _watchers.Count; i++)
            {
                var watcher = _watchers[i];
                if (HasRelevantChange(watcher, importedAssets, deletedAssets, movedAssets, movedFromAssetPaths))
                    watcher.Generate();
            }
#endif
        }

        private static bool HasRelevantChange(AssetWatcher watcher, string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            return HasMatch(importedAssets, watcher.Matches) ||
                   HasMatch(deletedAssets, watcher.Matches) ||
                   HasMatch(movedAssets, watcher.Matches) ||
                   HasMatch(movedFromAssetPaths, watcher.Matches);
        }

        private static void RebuildCacheIfNeeded()
        {
            if (!_cacheDirty)
                return;

            _watchers.Clear();
            AddNetworkPrefabWatchers();
            AddNetworkAssetWatchers();
#if ADDRESSABLES_PURRNET_SUPPORT
            AddAddressableNetworkPrefabWatchers();
#endif
            _cacheDirty = false;
        }

        private static void AddNetworkPrefabWatchers()
        {
            string[] guids = AssetDatabase.FindAssets("t:NetworkPrefabs");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var networkPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(assetPath);
                if (!networkPrefabs || !networkPrefabs.autoGenerate || !networkPrefabs.folder)
                    continue;

                string folderPath = GetFolderPath(networkPrefabs.folder);
                if (folderPath == null)
                    continue;

                _watchers.Add(new AssetWatcher(guids[i], assetPath, folderPath, ".prefab", RegistryType.NetworkPrefabs));
            }
        }

        private static void AddNetworkAssetWatchers()
        {
            string[] guids = AssetDatabase.FindAssets("t:NetworkAssets");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var networkAssets = AssetDatabase.LoadAssetAtPath<NetworkAssets>(assetPath);
                if (!networkAssets || !networkAssets.autoGenerate || !networkAssets.folder)
                    continue;

                string folderPath = GetFolderPath(networkAssets.folder);
                if (folderPath == null)
                    continue;

                _watchers.Add(new AssetWatcher(guids[i], assetPath, folderPath, null, RegistryType.NetworkAssets));
            }
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        private static void AddAddressableNetworkPrefabWatchers()
        {
            string[] guids = AssetDatabase.FindAssets("t:AddressableNetworkPrefabs");
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var addressable = AssetDatabase.LoadAssetAtPath<AddressableNetworkPrefabs>(assetPath);
                if (!addressable || !addressable.autoGenerate || !addressable.folder)
                    continue;

                string folderPath = GetFolderPath(addressable.folder);
                if (folderPath == null)
                    continue;

                _watchers.Add(new AssetWatcher(guids[i], assetPath, folderPath, ".prefab",
                    RegistryType.AddressableNetworkPrefabs));
            }
        }
#endif

        /// <summary>
        /// Folder sources watch everything under them; a scene source watches the scene file itself,
        /// so saving the scene regenerates from its updated references.
        /// </summary>
        private static string GetFolderPath(UnityEngine.Object folder)
        {
            string folderPath = AssetDatabase.GetAssetPath(folder);
            if (string.IsNullOrEmpty(folderPath))
                return null;

            if (folder is SceneAsset)
                return folderPath;

            return folderPath.EndsWith("/", StringComparison.Ordinal) ? folderPath : folderPath + "/";
        }

        private static bool HasProcessableChange(params string[][] assetGroups)
        {
            for (int i = 0; i < assetGroups.Length; i++)
            {
                if (HasMatch(assetGroups[i], IsProcessablePath))
                    return true;
            }

            return false;
        }

        private static bool IsProcessablePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (AssetDatabase.IsValidFolder(path))
                return false;

            return !string.IsNullOrEmpty(Path.GetExtension(path));
        }

        private static bool HasRegistryChange(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            return HasMatch(importedAssets, IsRegistryAsset) ||
                   HasMatch(movedAssets, IsRegistryAsset) ||
                   HasMatch(deletedAssets, IsCachedRegistryPath) ||
                   HasMatch(movedFromAssetPaths, IsCachedRegistryPath);
        }

        private static bool IsRegistryAsset(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                return false;

            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type == typeof(NetworkPrefabs) ||
                   type == typeof(NetworkAssets)
#if ADDRESSABLES_PURRNET_SUPPORT
                   || type == typeof(AddressableNetworkPrefabs)
#endif
                   ;
        }

        private static bool IsCachedRegistryPath(string path)
        {
            for (int i = 0; i < _watchers.Count; i++)
            {
                if (string.Equals(_watchers[i].AssetPath, path, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool HasMatch(string[] paths, Func<string, bool> match)
        {
            if (paths == null)
                return false;

            for (int i = 0; i < paths.Length; i++)
            {
                if (match(paths[i]))
                    return true;
            }

            return false;
        }

        private enum RegistryType
        {
            NetworkPrefabs,
            NetworkAssets,
#if ADDRESSABLES_PURRNET_SUPPORT
            AddressableNetworkPrefabs,
#endif
        }

        private readonly struct AssetWatcher
        {
            public readonly string AssetPath;
            private readonly string _assetGuid;
            private readonly string _folderPath;
            private readonly string _extension;
            private readonly RegistryType _registryType;

            public AssetWatcher(string assetGuid, string assetPath, string folderPath, string extension,
                RegistryType registryType)
            {
                AssetPath = assetPath;
                _assetGuid = assetGuid;
                _folderPath = folderPath;
                _extension = extension;
                _registryType = registryType;
            }

            public void Generate()
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                // Cache identifiers rather than instance delegates: a delegate would keep the
                // registry and its referenced assets alive for the lifetime of this static cache.
                string assetPath = AssetDatabase.GUIDToAssetPath(_assetGuid);
                if (string.IsNullOrEmpty(assetPath))
                    return;

                switch (_registryType)
                {
                    case RegistryType.NetworkPrefabs:
                        var networkPrefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabs>(assetPath);
                        if (networkPrefabs && networkPrefabs.autoGenerate && networkPrefabs.folder)
                            networkPrefabs.Generate();
                        break;
                    case RegistryType.NetworkAssets:
                        var networkAssets = AssetDatabase.LoadAssetAtPath<NetworkAssets>(assetPath);
                        if (networkAssets && networkAssets.autoGenerate && networkAssets.folder)
                            networkAssets.GenerateAssets();
                        break;
#if ADDRESSABLES_PURRNET_SUPPORT
                    case RegistryType.AddressableNetworkPrefabs:
                        var addressable = AssetDatabase.LoadAssetAtPath<AddressableNetworkPrefabs>(assetPath);
                        if (addressable && addressable.autoGenerate && addressable.folder)
                            AddressableNetworkPrefabsEditor.Generate(addressable);
                        break;
#endif
                }
            }

            public bool Matches(string path)
            {
                if (!IsProcessablePath(path))
                    return false;

                if (!_folderPath.EndsWith("/", StringComparison.Ordinal))
                    return string.Equals(path, _folderPath, StringComparison.Ordinal);

                if (!path.StartsWith(_folderPath, StringComparison.Ordinal))
                    return false;

                return _extension == null || path.EndsWith(_extension, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
#endif
