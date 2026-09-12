using System.Collections.Generic;
using PurrNet.Logging;
using Object = UnityEngine.Object;

namespace PurrNet
{
    /// <summary>
    /// Single entry point for turning a <see cref="NetworkAssetID"/> (or an asset reference) into a registered asset.
    /// Every runtime lookup goes through here so the resolution strategy can be changed in one place.
    /// Global assets live in the manager's registry; scene scoped assets come from the
    /// <see cref="SceneRegistry{T}"/> of the scene named by <see cref="NetworkAssetID.scope"/>.
    /// </summary>
    public sealed class NetworkAssetResolver
    {
        private readonly NetworkManager _manager;
        private readonly HashSet<string> _warnedFallback = new();

        /// <summary>
        /// Scene to prefer while serializing assets that have no context of their own, such as RPC arguments.
        /// Armed by the RPC build path with the sending identity's scene and cleared once the RPC is sent,
        /// so an asset registered by several scenes resolves to the scene the RPC actually comes from.
        /// </summary>
        public static SceneID? serializationSceneHint { get; set; }

        internal NetworkAssetResolver(NetworkManager manager)
        {
            _manager = manager;
        }

        private NetworkAssets registry => _manager.networkAssets;

        public bool TryGetAsset(NetworkAssetID id, out Object asset)
        {
            if (id.scope.HasValue)
            {
                if (TryGetSceneRegistry(id.scope.Value, (int)id, out var sceneRegistry, out var localId))
                    return sceneRegistry.TryGetAsset(localId, out asset);

                asset = null;
                return false;
            }

            var current = registry;
            if (current && id.isValid)
                return current.TryGetAsset((int)id, out asset);

            asset = null;
            return false;
        }

        public bool TryGetId(Object asset, out NetworkAssetID id, bool warnIfUnregistered = false)
        {
            return TryGetId(asset, null, out id, warnIfUnregistered);
        }

        /// <summary>
        /// Resolves an asset reference to its id. Global registration wins. Otherwise the hinted scene is
        /// searched first, since that's the scene most likely to be loaded on the receiving peers; any other
        /// loaded scene is a last resort and is warned about. Pass warnIfUnregistered to log once per asset
        /// when nothing knows it.
        /// </summary>
        public bool TryGetId(Object asset, SceneID? sceneHint, out NetworkAssetID id, bool warnIfUnregistered = false)
        {
            id = NetworkAssetID.invalid;

            if (!asset)
                return false;

            var current = registry;
            if (current && current.TryGetIndex(asset, out var index))
            {
                id = index;
                return true;
            }

            if (sceneHint.HasValue && TryGetSceneScopedId(sceneHint.Value, asset, out id))
                return true;

            var sceneModule = _manager.sceneModule;
            if (sceneModule != null)
            {
                var scenes = sceneModule.scenes;
                for (int i = 0; i < scenes.Count; i++)
                {
                    var scope = scenes[i];
                    if (sceneHint.HasValue && scope == sceneHint.Value)
                        continue;

                    if (!TryGetSceneScopedId(scope, asset, out id))
                        continue;

                    if (sceneHint.HasValue && _warnedFallback.Add(asset.name))
                    {
                        PurrLogger.LogWarning(
                            $"Asset '{asset.name}' is used in scene {sceneHint.Value} but is only registered in scene {scope}; " +
                            "peers that don't have that scene loaded won't be able to resolve it.", asset);
                    }

                    return true;
                }
            }

            if (warnIfUnregistered && current)
                current.GetIndex(asset);

            return false;
        }

        private bool TryGetSceneScopedId(SceneID scope, Object asset, out NetworkAssetID id)
        {
            id = NetworkAssetID.invalid;

            if (!TryGetSceneRegistries(scope, out var registries))
                return false;

            int offset = 0;
            for (int i = 0; i < registries.Count; i++)
            {
                var sceneRegistry = registries[i];
                if (sceneRegistry.TryGetIndex(asset, out var local))
                {
                    id = new NetworkAssetID(offset + local, scope);
                    return true;
                }

                offset += sceneRegistry.count;
            }

            return false;
        }

        private bool TryGetSceneRegistries(SceneID scope, out List<NetworkAssets> registries)
        {
            registries = null;
            return _manager.TryGetScene(scope, out var scene) &&
                   SceneRegistry<NetworkAssets>.TryGetEntries(scene.handle, out registries);
        }

        /// <summary>
        /// Scene scoped ids index into the scene's registries in registration order,
        /// each registry occupying <see cref="NetworkAssets.count"/> consecutive ids.
        /// </summary>
        private bool TryGetSceneRegistry(SceneID scope, int id, out NetworkAssets sceneRegistry, out int localId)
        {
            sceneRegistry = null;
            localId = -1;

            if (id < 0 || !TryGetSceneRegistries(scope, out var registries))
                return false;

            int index = id;
            for (int i = 0; i < registries.Count; i++)
            {
                var candidate = registries[i];
                if (index < candidate.count)
                {
                    sceneRegistry = candidate;
                    localId = index;
                    return true;
                }

                index -= candidate.count;
            }

            return false;
        }

        public bool TryGetPersistentId(Object asset, out string persistentId)
        {
            var current = registry;
            if (current && current.TryGetPersistentId(asset, out persistentId))
                return true;

            persistentId = null;

            var sceneModule = _manager.sceneModule;
            if (sceneModule == null || !asset)
                return false;

            var scenes = sceneModule.scenes;
            for (int s = 0; s < scenes.Count; s++)
            {
                if (!TryGetSceneRegistries(scenes[s], out var registries))
                    continue;

                for (int i = 0; i < registries.Count; i++)
                {
                    if (registries[i].TryGetPersistentId(asset, out persistentId))
                        return true;
                }
            }

            return false;
        }

        public bool TryGetAssetByPersistentId(string persistentId, out Object asset)
        {
            var current = registry;
            if (current && current.TryGetAssetByPersistentId(persistentId, out asset))
                return true;

            asset = null;

            var sceneModule = _manager.sceneModule;
            if (sceneModule == null || string.IsNullOrEmpty(persistentId))
                return false;

            var scenes = sceneModule.scenes;
            for (int s = 0; s < scenes.Count; s++)
            {
                if (!TryGetSceneRegistries(scenes[s], out var registries))
                    continue;

                for (int i = 0; i < registries.Count; i++)
                {
                    if (registries[i].TryGetAssetByPersistentId(persistentId, out asset))
                        return true;
                }
            }

            return false;
        }
    }
}
