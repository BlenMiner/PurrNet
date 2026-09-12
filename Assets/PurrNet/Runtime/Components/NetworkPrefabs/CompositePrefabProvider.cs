using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet
{
    public class CompositePrefabProvider : IPrefabProvider, IAsyncPrefabProvider, IPersistentPrefabProvider
    {
        private readonly List<IPrefabProvider> _providers = new();
        private readonly List<int> _offsets = new();
        private readonly List<int> _counts = new();
        private readonly Dictionary<int, PrefabData> _unified = new();
        private readonly Dictionary<GameObject, PrefabData> _prefabCache = new();

        public IEnumerable<PrefabData> allPrefabs => _unified.Values;
        public IEnumerable<string> persistentIds
        {
            get
            {
                for (int i = 0; i < _providers.Count; i++)
                {
                    if (_providers[i] is not IPersistentPrefabProvider persistentProvider)
                        continue;

                    foreach (var persistentId in persistentProvider.persistentIds)
                        yield return persistentId;
                }
            }
        }

        /// <summary>
        /// Adds a provider to the composite. Providers must be added in
        /// the same order on all network peers for deterministic ID assignment.
        /// </summary>
        public void AddProvider(IPrefabProvider provider)
        {
            _providers.Add(provider);
        }

        public void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5)
        {
            if (_providers.Count == 0)
            {
                PurrLogger.LogError("CompositePrefabProvider: no providers registered, cannot add runtime prefab.");
                return;
            }

            _providers[0].AddRuntimePrefab(uniqueName, prefab, pooled, warmup);
            Refresh();
        }

        /// <summary>
        /// Rebuilds the lookup from all added providers.
        /// Must be called after all providers are added and individually refreshed
        /// </summary>
        public void Refresh()
        {
            _unified.Clear();
            _offsets.Clear();
            _counts.Clear();
            _prefabCache.Clear();

            int offset = 0;

            for (int i = 0; i < _providers.Count; i++)
            {
                var provider = _providers[i];
                _offsets.Add(offset);

                int localMax = -1;

                foreach (var data in provider.allPrefabs)
                {
                    int unifiedId = (int)data.prefabId + offset;
                    _unified[unifiedId] = new PrefabData
                    {
                        prefabId = unifiedId,
                        prefab = data.prefab,
                        pooled = data.pooled,
                        warmupCount = data.warmupCount
                    };

                    if ((int)data.prefabId > localMax)
                        localMax = (int)data.prefabId;
                }

                int count = localMax + 1;
                _counts.Add(count);
                offset += count;
            }
        }

        public bool NeedsLoad(int prefabId)
        {
            return _unified.TryGetValue(prefabId, out var data) && data.prefab == null;
        }

        public async Task<PrefabData> LoadPrefabAsync(int prefabId)
        {
            if (!_unified.TryGetValue(prefabId, out var data))
            {
                PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} not found in CompositePrefabProvider.");
                return default;
            }

            if (data.prefab != null)
                return data;

            for (int i = 0; i < _providers.Count; i++)
            {
                int count = _counts[i];
                if (prefabId < _offsets[i] || prefabId >= _offsets[i] + count)
                    continue;

                int localId = prefabId - _offsets[i];
                var provider = _providers[i];

                if (provider is IAsyncPrefabProvider asyncProvider)
                {
                    try
                    {
                        var loaded = await asyncProvider.LoadPrefabAsync(localId);
                        if (loaded.prefab == null)
                            return default;

                        data.prefab = loaded.prefab;
                        _unified[prefabId] = data;
                        return data;
                    }
                    catch (System.Exception e)
                    {
                        PurrLogger.LogError($"LoadPrefabAsync: exception loading prefabId {prefabId} (provider {i} local {localId}): {e.Message}\n{e.StackTrace}");
                        return default;
                    }
                }

                PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} needs load but provider {i} does not support async loading.");
                return default;
            }

            PurrLogger.LogError($"LoadPrefabAsync: prefabId {prefabId} not in any provider range.");
            return default;
        }

        public bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
        {
            return _unified.TryGetValue(prefabId, out prefabData);
        }

        public bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            if (prefab)
            {
                if (_prefabCache.TryGetValue(prefab, out prefabData))
                    return true;
            }

            foreach (var data in _unified.Values)
            {
                if (data.prefab == prefab)
                {
                    prefabData = data;
                    return true;
                }
            }

            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i].TryGetPrefabData(prefab, out var pd) && pd.prefab != null)
                {
                    int unifiedId = _offsets[i] + (int)pd.prefabId;
                    prefabData = new PrefabData
                    {
                        prefabId = unifiedId,
                        prefab = pd.prefab,
                        pooled = pd.pooled,
                        warmupCount = pd.warmupCount
                    };
                    _unified[unifiedId] = prefabData;

                    if (prefab)
                        _prefabCache[prefab] = prefabData;

                    return true;
                }
            }

            prefabData = default;
            return false;
        }

        public bool TryGetPersistentId(int prefabId, out string persistentId)
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                int count = _counts[i];
                if (prefabId < _offsets[i] || prefabId >= _offsets[i] + count)
                    continue;

                if (_providers[i] is IPersistentPrefabProvider persistentProvider)
                    return persistentProvider.TryGetPersistentId(prefabId - _offsets[i], out persistentId);

                persistentId = null;
                return false;
            }

            persistentId = null;
            return false;
        }

        public bool TryGetPersistentId(GameObject prefab, out string persistentId)
        {
            if (TryGetPrefabData(prefab, out var prefabData))
                return TryGetPersistentId((int)prefabData.prefabId, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            if (string.IsNullOrEmpty(persistentId))
            {
                prefabData = default;
                return false;
            }

            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i] is not IPersistentPrefabProvider persistentProvider)
                    continue;

                if (!persistentProvider.TryGetPrefabDataByPersistentId(persistentId, out var localData))
                    continue;

                int unifiedId = _offsets[i] + (int)localData.prefabId;
                prefabData = new PrefabData
                {
                    prefabId = unifiedId,
                    prefab = localData.prefab,
                    pooled = localData.pooled,
                    warmupCount = localData.warmupCount
                };

                _unified[unifiedId] = prefabData;
                if (prefabData.prefab)
                    _prefabCache[prefabData.prefab] = prefabData;

                return true;
            }

            prefabData = default;
            return false;
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        public bool TryGetAddressableGuid(int prefabId, out string assetGuid)
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                int count = _counts[i];
                if (prefabId < _offsets[i] || prefabId >= _offsets[i] + count)
                    continue;

                int localId = prefabId - _offsets[i];
                if (_providers[i] is AddressableNetworkPrefabs addr)
                    return addr.TryGetGuid(localId, out assetGuid);

                assetGuid = null;
                return false;
            }

            assetGuid = null;
            return false;
        }
#endif
    }
}
