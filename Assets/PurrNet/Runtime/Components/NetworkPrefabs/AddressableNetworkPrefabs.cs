#if ADDRESSABLES_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace PurrNet
{
    [CreateAssetMenu(fileName = "AddressableNetworkPrefabs", menuName = "PurrNet/Network Prefabs/Addressable Network Prefabs", order = -200)]
    public class AddressableNetworkPrefabs : PrefabProviderScriptable, IAsyncPrefabProvider, IPersistentPrefabProvider
    {
        [Serializable]
        public struct Entry
        {
            public AssetReferenceGameObject asset;
        }

        [SerializeField] private bool _preloadAtStartup = true;
        [SerializeField] private List<Entry> _entries = new();

        [Tooltip("Will also get entries from these linked AddressableNetworkPrefabs.")]
        public List<AddressableNetworkPrefabs> linkedAddressablePrefabs = new();

        public bool autoGenerate;
        public UnityEngine.Object folder;

        [Tooltip("When no folder is set, search all of Assets/ instead of doing nothing.")]
        public bool searchAllIfNoFolder;

        /// <summary>
        /// Whether all registered Addressable prefabs have been loaded and are ready for use.
        /// </summary>
        public bool isLoaded { get; private set; }

        private readonly Dictionary<int, PrefabData> _prefabLookup = new();
        private readonly Dictionary<string, int> _guidToId = new();
        private readonly Dictionary<int, string> _idToGuid = new();
        private readonly List<AsyncOperationHandle<GameObject>> _loadHandles = new();
        private readonly List<RuntimePrefabEntry> _runtimePrefabs = new();
        private readonly HashSet<string> _warnedNameFallbacks = new();
        private int _runtimePrefabStartId;

        private struct RuntimePrefabEntry
        {
            public string uniqueName;
            public GameObject prefab;
            public bool pooled;
            public int warmupCount;
        }

        /// <summary>
        /// The entries registered in this asset. Read-only access for external inspection.
        /// </summary>
        public IReadOnlyList<Entry> entries => _entries;

        /// <summary>
        /// Number of registered entries.
        /// </summary>
        public int count => _entries.Count;

        public bool preloadAtStartup
        {
            get => _preloadAtStartup;
            set => _preloadAtStartup = value;
        }

        public override IEnumerable<PrefabData> allPrefabs => _prefabLookup.Values;
        public IEnumerable<string> persistentIds => _guidToId.Keys;

        /// <summary>
        /// Tries to get the loaded prefab data for a given asset GUID.
        /// Returns false if the GUID is not registered or the prefab isn't loaded yet.
        /// </summary>
        public bool TryGetPrefabDataByGuid(string assetGuid, out PrefabData prefabData)
        {
            if (_guidToId.TryGetValue(assetGuid, out int id))
                return _prefabLookup.TryGetValue(id, out prefabData);

            prefabData = default;
            return false;
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            return TryGetPrefabDataByGuid(persistentId, out prefabData);
        }

        public override bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
        {
            return _prefabLookup.TryGetValue(prefabId, out prefabData);
        }

        public override bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            foreach (var data in _prefabLookup.Values)
            {
                if (data.prefab == prefab)
                {
                    prefabData = data;
                    return true;
                }
            }

            if (!TryMatchByName(_prefabLookup.Values, prefab, out prefabData))
                return false;

            if (_warnedNameFallbacks.Add(prefab.name))
            {
                PurrLogger.LogWarning(
                    $"Resolved addressable network prefab `{prefab.name}` by name because the given reference isn't the registered one.\n" +
                    "This happens when the same asset is duplicated across bundles; fix the Addressables group setup to avoid spawning the wrong prefab.");
            }

            return true;
        }

        public override void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5)
        {
            _runtimePrefabs.Add(new RuntimePrefabEntry
            {
                uniqueName = uniqueName,
                prefab = prefab,
                pooled = pooled,
                warmupCount = warmup
            });

            Refresh();
        }

        public bool TryGetGuid(int localPrefabId, out string assetGuid)
        {
            return _idToGuid.TryGetValue(localPrefabId, out assetGuid);
        }

        public bool TryGetPersistentId(int prefabId, out string persistentId)
        {
            return TryGetGuid(prefabId, out persistentId);
        }

        public bool TryGetPersistentId(GameObject prefab, out string persistentId)
        {
            if (TryGetPrefabData(prefab, out var prefabData))
                return TryGetPersistentId((int)prefabData.prefabId, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabByPersistentId(string persistentId, out GameObject prefab)
        {
            if (TryGetPrefabDataByPersistentId(persistentId, out var prefabData))
            {
                prefab = prefabData.prefab;
                return prefab;
            }

            prefab = null;
            return false;
        }

        public static event Action<string, bool> onLoadStateChanged;

        internal static void NotifyLoadStateChanged(string guid, bool loaded)
        {
            onLoadStateChanged?.Invoke(guid, loaded);
        }

        public IEnumerable<string> GetLoadedGuids()
        {
            foreach (var kvp in _prefabLookup)
            {
                if (kvp.Value.prefab != null && _idToGuid.TryGetValue(kvp.Key, out var g))
                    yield return g;
            }
        }

        public override void Refresh()
        {
            RebuildLookup();
        }

        private void OnEnable()
        {
            RebuildLookup();
        }

        /// <summary>
        /// Rebuilds the internal lookup tables from the entry list.
        /// Assigns deterministic IDs sorted by asset GUID.
        /// Includes entries from linked AddressableNetworkPrefabs (deduped by GUID).
        /// Note: PrefabData.prefab will be null until LoadAllAsync is called.
        /// The IDs assigned here are local to this provider and will be
        /// offset by the CompositePrefabProvider when combined with other providers.
        /// </summary>
        private void RebuildLookup()
        {
            _prefabLookup.Clear();
            _guidToId.Clear();
            _idToGuid.Clear();

            var seenGuids = new HashSet<string>();
            var sorted = new List<(string guid, Entry entry)>();
            var visited = new HashSet<AddressableNetworkPrefabs>();

            void CollectEntries(AddressableNetworkPrefabs provider)
            {
                if (!provider || !visited.Add(provider)) return;

                for (int i = 0; i < provider._entries.Count; i++)
                {
                    var entry = provider._entries[i];
                    if (entry.asset == null || !entry.asset.RuntimeKeyIsValid())
                        continue;

                    var guid = entry.asset.AssetGUID;
                    if (string.IsNullOrEmpty(guid) || !seenGuids.Add(guid))
                        continue;

                    sorted.Add((guid, entry));
                }

                if (provider.linkedAddressablePrefabs == null) return;
                for (int i = 0; i < provider.linkedAddressablePrefabs.Count; i++)
                {
                    var link = provider.linkedAddressablePrefabs[i];
                    if (link) CollectEntries(link);
                }
            }

            CollectEntries(this);

            for (int i = 0; i < sorted.Count; i++)
            {
                var (guid, _) = sorted[i];

                _guidToId[guid] = i;
                _idToGuid[i] = guid;
                _prefabLookup[i] = new PrefabData
                {
                    prefabId = i,
                    prefab = null,
                    pooled = false,
                    warmupCount = 0
                };
            }

            int runtimeStart = sorted.Count;
            _runtimePrefabStartId = runtimeStart;
            for (int i = 0; i < _runtimePrefabs.Count; i++)
            {
                var rt = _runtimePrefabs[i];
                if (!rt.prefab) continue;

                int id = runtimeStart + i;
                if (!string.IsNullOrEmpty(rt.uniqueName) && seenGuids.Add(rt.uniqueName))
                {
                    _guidToId[rt.uniqueName] = id;
                    _idToGuid[id] = rt.uniqueName;
                }

                _prefabLookup[id] = new PrefabData
                {
                    prefabId = id,
                    prefab = rt.prefab,
                    pooled = rt.pooled,
                    warmupCount = rt.warmupCount
                };
            }
        }

        public bool NeedsLoad(int prefabId)
        {
            if (!_prefabLookup.TryGetValue(prefabId, out var data))
                return false;
            return data.prefab == null;
        }

        public async Task<PrefabData> LoadPrefabByGuidAsync(string assetGuid)
        {
            if (!_guidToId.TryGetValue(assetGuid, out int localId))
            {
                PurrLogger.LogError($"LoadPrefabByGuidAsync: GUID '{assetGuid}' not registered.");
                return default;
            }
            return await LoadPrefabAsync(localId);
        }

        public async Task<PrefabData> LoadPrefabAsync(int prefabId)
        {
            if (!_prefabLookup.TryGetValue(prefabId, out var data))
            {
                PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} not found in AddressableNetworkPrefabs.");
                return default;
            }

            if (data.prefab != null)
                return data;

            if (!_idToGuid.TryGetValue(prefabId, out var guid))
            {
                PurrLogger.LogError($"LoadPrefabAsync: no GUID mapping for prefabId {prefabId}.");
                return default;
            }

            try
            {
                var handle = Addressables.LoadAssetAsync<GameObject>(guid);
                _loadHandles.Add(handle);
                await handle.Task;

                if (handle.Status != AsyncOperationStatus.Succeeded || !handle.Result)
                {
                    PurrLogger.LogError($"LoadPrefabAsync: failed to load Addressable prefab prefabId {prefabId} GUID '{guid}'.");
                    return default;
                }

                data.prefab = handle.Result;
                _prefabLookup[prefabId] = data;
                NotifyLoadStateChanged(guid, true);
                return data;
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"LoadPrefabAsync: exception loading prefabId {prefabId} GUID '{guid}': {e.Message}\n{e.StackTrace}");
                return default;
            }
        }

        /// <summary>
        /// Loads all registered Addressable prefabs into memory.
        /// Must be called (and awaited) before the network is started.
        /// After loading, PrefabData.prefab will contain valid references.
        /// </summary>
        public async Task LoadAllAsync()
        {
            RebuildLookup();
            ReleaseAll();

            var sortedGuids = new List<string>(_guidToId.Count);
            foreach (var kvp in _guidToId)
                sortedGuids.Add(kvp.Key);
            sortedGuids.Sort(StringComparer.Ordinal);

            for (int i = 0; i < sortedGuids.Count; i++)
            {
                var guid = sortedGuids[i];
                var id = _guidToId[guid];

                try
                {
                    var handle = Addressables.LoadAssetAsync<GameObject>(guid);
                    _loadHandles.Add(handle);

                    await handle.Task;

                    if (handle.Status != AsyncOperationStatus.Succeeded || !handle.Result)
                    {
                        PurrLogger.LogError($"Failed to load Addressable prefab with GUID '{guid}'.");
                        continue;
                    }

                    var prefab = handle.Result;

                    if (_prefabLookup.TryGetValue(id, out var data))
                    {
                        data.prefab = prefab;
                        _prefabLookup[id] = data;
                        NotifyLoadStateChanged(guid, true);
                    }
                }
                catch (Exception e)
                {
                    PurrLogger.LogError($"Exception loading Addressable prefab GUID '{guid}': {e.Message}");
                }
            }

            isLoaded = true;
        }

        /// <summary>
        /// Releases all loaded Addressable handles.
        /// Call this when the network shuts down or the provider is no longer needed.
        /// </summary>
        public void ReleaseAll()
        {
            for (int i = 0; i < _loadHandles.Count; i++)
            {
                if (_loadHandles[i].IsValid())
                    Addressables.Release(_loadHandles[i]);
            }

            _loadHandles.Clear();
            isLoaded = false;

            foreach (var guid in _idToGuid.Values)
                NotifyLoadStateChanged(guid, false);

            var keys = new List<int>(_prefabLookup.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (key >= _runtimePrefabStartId)
                    continue;
                if (_prefabLookup.TryGetValue(key, out var data))
                {
                    data.prefab = null;
                    _prefabLookup[key] = data;
                }
            }
        }

        private void OnDisable()
        {
            ReleaseAll();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (autoGenerate &&
                !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode &&
                !UnityEditor.EditorApplication.isCompiling &&
                !UnityEditor.EditorApplication.isUpdating)
            {
                UnityEditor.EditorApplication.delayCall += OnAutoGenerate;
            }
        }

        private void OnAutoGenerate()
        {
            // Actual generation is handled by the editor (AddressableNetworkPrefabsEditor)
            // because it requires UnityEditor.AddressableAssets which is in a separate editor assembly.
            // This triggers via onAutoGenerateRequested event.
            if (!this) return;
            onAutoGenerateRequested?.Invoke(this);
        }

        /// <summary>
        /// Event fired when auto-generation is triggered via OnValidate.
        /// The editor subscribes to this to perform the actual generation.
        /// </summary>
        public static event Action<AddressableNetworkPrefabs> onAutoGenerateRequested;

        /// <summary>
        /// Gets the set of GUIDs already registered as entries.
        /// Used by the editor during generation to avoid duplicates.
        /// </summary>
        public HashSet<string> GetExistingGuids()
        {
            var guids = new HashSet<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (entry.asset != null && entry.asset.RuntimeKeyIsValid())
                    guids.Add(entry.asset.AssetGUID);
            }
            return guids;
        }

        /// <summary>
        /// Adds an entry by asset reference. Used by the editor during generation.
        /// </summary>
        public void AddEntry(AssetReferenceGameObject assetRef)
        {
            _entries.Add(new Entry
            {
                asset = assetRef
            });
        }
#endif
    }
}
#endif
