using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet
{
    /// <summary>
    /// Single entry point for turning a <see cref="PrefabID"/> (or a prefab reference) into <see cref="PrefabData"/>.
    /// Every runtime lookup goes through here so the resolution strategy can be changed in one place.
    /// Global prefabs live in the manager's provider; scene scoped prefabs come from the
    /// <see cref="SceneRegistry{T}"/> of the scene named by <see cref="PrefabID.scope"/>.
    /// </summary>
    public sealed class PrefabResolver
    {
        private readonly NetworkManager _manager;
        private readonly HashSet<string> _warnedFallback = new();

        internal PrefabResolver(NetworkManager manager)
        {
            _manager = manager;
        }

        private IPrefabProvider provider => _manager.prefabProvider;

        public IEnumerable<PrefabData> allPrefabs => provider?.allPrefabs ?? Array.Empty<PrefabData>();

        public bool TryGetPrefabData(PrefabID id, out PrefabData prefabData)
        {
            if (id.scope.HasValue)
            {
                if (TryGetSceneProvider(id.scope.Value, (int)id, out var sceneProvider, out var localId) &&
                    sceneProvider.TryGetPrefabData(localId, out prefabData))
                {
                    prefabData.prefabId = id;
                    return true;
                }

                prefabData = default;
                return false;
            }

            var current = provider;
            if (current != null && id.isValid)
                return current.TryGetPrefabData((int)id, out prefabData);

            prefabData = default;
            return false;
        }

        public bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            return TryGetPrefabData(prefab, null, out prefabData);
        }

        public bool TryGetPrefabData(GameObject prefab, SceneID? sceneHint, out PrefabData prefabData)
        {
            prefabData = default;

            if (!prefab)
                return false;

            var current = provider;
            if (current != null && current.TryGetPrefabData(prefab, out prefabData))
                return true;

            if (sceneHint.HasValue && TryGetSceneScopedPrefab(sceneHint.Value, prefab, out prefabData))
                return true;

            var sceneModule = _manager.sceneModule;
            if (sceneModule == null)
                return false;

            var scenes = sceneModule.scenes;
            for (int i = 0; i < scenes.Count; i++)
            {
                var scope = scenes[i];
                if (sceneHint.HasValue && scope == sceneHint.Value)
                    continue;

                if (!TryGetSceneScopedPrefab(scope, prefab, out prefabData))
                    continue;

                if (sceneHint.HasValue && _warnedFallback.Add(prefab.name))
                {
                    PurrLogger.LogWarning(
                        $"Prefab '{prefab.name}' is used in scene {sceneHint.Value} but is only registered in scene {scope}; " +
                        "peers that don't have that scene loaded won't be able to spawn it.", prefab);
                }

                return true;
            }

            return false;
        }

        private bool TryGetSceneScopedPrefab(SceneID scope, GameObject prefab, out PrefabData prefabData)
        {
            prefabData = default;

            if (!TryGetSceneProviders(scope, out var providers))
                return false;

            int offset = 0;
            for (int i = 0; i < providers.Count; i++)
            {
                var sceneProvider = providers[i];
                if (sceneProvider.TryGetPrefabData(prefab, out prefabData))
                {
                    prefabData.prefabId = new PrefabID(offset + (int)prefabData.prefabId, scope);
                    return true;
                }

                offset += sceneProvider.count;
            }

            return false;
        }

        private bool TryGetSceneProviders(SceneID scope, out List<NetworkPrefabs> providers)
        {
            providers = null;
            return _manager.TryGetScene(scope, out var scene) &&
                   SceneRegistry<NetworkPrefabs>.TryGetEntries(scene.handle, out providers);
        }

        private bool TryGetSceneProvider(SceneID scope, int id, out NetworkPrefabs sceneProvider, out int localId)
        {
            sceneProvider = null;
            localId = -1;

            if (id < 0 || !TryGetSceneProviders(scope, out var providers))
                return false;

            int index = id;
            for (int i = 0; i < providers.Count; i++)
            {
                var candidate = providers[i];
                if (index < candidate.count)
                {
                    sceneProvider = candidate;
                    localId = index;
                    return true;
                }

                index -= candidate.count;
            }

            return false;
        }

        /// <summary>
        /// Every prefab registered by the scene's registries, with ids stamped with the scene scope.
        /// </summary>
        public IEnumerable<PrefabData> GetScenePrefabs(SceneID scope)
        {
            if (!TryGetSceneProviders(scope, out var providers))
                yield break;

            int offset = 0;
            for (int i = 0; i < providers.Count; i++)
            {
                var sceneProvider = providers[i];
                foreach (var data in sceneProvider.allPrefabs)
                {
                    var scoped = data;
                    scoped.prefabId = new PrefabID(offset + (int)data.prefabId, scope);
                    yield return scoped;
                }

                offset += sceneProvider.count;
            }
        }

        /// <summary>
        /// True when the id is known but its prefab isn't loaded yet and the provider can load it asynchronously.
        /// </summary>
        public bool NeedsLoad(PrefabID id)
        {
            return !id.scope.HasValue && provider is IAsyncPrefabProvider &&
                   TryGetPrefabData(id, out var prefabData) && !prefabData.prefab;
        }

        public async Task<PrefabData> LoadPrefabAsync(PrefabID id)
        {
            if (!id.scope.HasValue && provider is IAsyncPrefabProvider asyncProvider)
                return await asyncProvider.LoadPrefabAsync((int)id);

            return TryGetPrefabData(id, out var prefabData) ? prefabData : default;
        }

        public bool TryGetPersistentId(PrefabID id, out string persistentId)
        {
            if (id.scope.HasValue)
            {
                if (TryGetSceneProvider(id.scope.Value, (int)id, out var sceneProvider, out var localId))
                    return sceneProvider.TryGetPersistentId(localId, out persistentId);

                persistentId = null;
                return false;
            }

            if (provider is IPersistentPrefabProvider persistentProvider)
                return persistentProvider.TryGetPersistentId((int)id, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPersistentId(GameObject prefab, out string persistentId)
        {
            if (TryGetPrefabData(prefab, null, out var prefabData))
                return TryGetPersistentId(prefabData.prefabId, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            if (provider is IPersistentPrefabProvider persistentProvider &&
                persistentProvider.TryGetPrefabDataByPersistentId(persistentId, out prefabData))
                return true;

            prefabData = default;

            var sceneModule = _manager.sceneModule;
            if (sceneModule == null || string.IsNullOrEmpty(persistentId))
                return false;

            var scenes = sceneModule.scenes;
            for (int s = 0; s < scenes.Count; s++)
            {
                var scope = scenes[s];
                if (!TryGetSceneProviders(scope, out var providers))
                    continue;

                int offset = 0;
                for (int i = 0; i < providers.Count; i++)
                {
                    var sceneProvider = providers[i];
                    if (sceneProvider.TryGetPrefabDataByPersistentId(persistentId, out prefabData))
                    {
                        prefabData.prefabId = new PrefabID(offset + (int)prefabData.prefabId, scope);
                        return true;
                    }

                    offset += sceneProvider.count;
                }
            }

            return false;
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        public bool TryGetAddressableGuid(PrefabID id, out string assetGuid)
        {
            if (!id.scope.HasValue && provider is CompositePrefabProvider composite)
                return composite.TryGetAddressableGuid((int)id, out assetGuid);

            assetGuid = null;
            return false;
        }
#endif
    }
}
