#if ADDRESSABLES_PURRNET_SUPPORT
using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public partial class ScenesModule
    {
        public struct PendingAddressableSceneOperation
        {
            public string guid;
            public AsyncOperationHandle<SceneInstance> handle;
            public SceneID idToAssign;
            public PurrSceneSettings settings;
            public bool ownsHandle;
            public bool discardOnCompletion;
            public bool loadAdditively;
        }

        private readonly List<PendingAddressableSceneOperation> _pendingAddressableOperations =
            new List<PendingAddressableSceneOperation>();

        private readonly List<AsyncOperationHandle<SceneInstance>> _pendingAddressableUnloads =
            new List<AsyncOperationHandle<SceneInstance>>();

        private readonly Dictionary<SceneID, AsyncOperationHandle<SceneInstance>> _addressableSceneHandles =
            new Dictionary<SceneID, AsyncOperationHandle<SceneInstance>>();

        private readonly Dictionary<SceneID, string> _addressableSceneIdToGuid =
            new Dictionary<SceneID, string>();

        private readonly Dictionary<string, List<SceneID>> _addressableSceneGuidToIds =
            new Dictionary<string, List<SceneID>>();

        public delegate void OnAddressableSceneEvent(SceneID sceneId, string guid, bool asServer);

        /// <summary>
        /// Fired when an Addressable scene begins loading.
        /// </summary>
        public event OnAddressableSceneEvent onAddressableSceneStartLoading;

        /// <summary>
        /// Fired when an Addressable scene has finished loading and is registered.
        /// </summary>
        public event OnAddressableSceneEvent onAddressableSceneLoaded;

        /// <summary>
        /// Registers a completion callback on the addressable scene handle so that the
        /// scene is processed as soon as it loads, rather than waiting for the next
        /// FixedUpdate. This prevents a race condition where scene objects' Start()
        /// fires before PurrNet has processed the loaded scene.
        /// </summary>
        private void RegisterAddressableCompletionCallback(AsyncOperationHandle<SceneInstance> handle)
        {
            handle.Completed += _ => ProcessCompletedAddressableLoads();
        }

        partial void ProcessCompletedAddressableLoads()
        {
            if (_isDisabled)
                return;

            for (var i = _pendingAddressableOperations.Count - 1; i >= 0; i--)
            {
                var op = _pendingAddressableOperations[i];

                if (!op.handle.IsDone)
                    continue;

                _pendingAddressableOperations.RemoveAt(i);
                if (op.discardOnCompletion)
                {
                    if (op.ownsHandle && op.handle.IsValid())
                    {
                        if (op.handle.Status == AsyncOperationStatus.Succeeded)
                            _pendingAddressableUnloads.Add(Addressables.UnloadSceneAsync(op.handle, UnloadSceneOptions.None, true));
                        else Addressables.Release(op.handle);
                    }
                    if (!_scenes.ContainsKey(op.idToAssign) && !IsScenePending(op.idToAssign))
                        _sceneActionScenes.Remove(op.idToAssign);
                    continue;
                }

                if (op.handle.Status == AsyncOperationStatus.Succeeded)
                {
                    var scene = op.handle.Result.Scene;
                    RegisterAddressableSceneHandle(op.idToAssign, op.guid, op.handle);
                    RegisterReceivedScene(scene, op.settings, op.idToAssign);
                    
                    onAddressableSceneLoaded?.Invoke(op.idToAssign, op.guid, _asServer);
                }
                else
                {
                    PurrLogger.LogError($"Addressable scene load failed: {op.handle.OperationException}");
                }

            }
        }

        private void ProcessLoadAddressableAction(LoadAddressableSceneAction action)
        {
            // A reconnect delivers the same load action twice: once in the
            // first-join batch and once when the player is re-added to the
            // public scene. Loading again would duplicate the addressable scene
            // and clash with the already assigned SceneID.
            if (_scenes.ContainsKey(action.sceneID) || IsScenePending(action.sceneID))
            {
                _sceneActionScenes.Add(action.sceneID);
                return;
            }

            var guid = action.guid.value;
            if (string.IsNullOrEmpty(guid))
            {
                PurrLogger.LogError("LoadAddressableSceneAction has empty GUID");
                return;
            }

            var parameters = action.GetLoadSceneParameters();

            AsyncOperationHandle<SceneInstance> handle;

            try
            {
                handle = Addressables.LoadSceneAsync(guid, parameters, true, 100);
            }
            catch (System.Exception e)
            {
                PurrLogger.LogError($"Error loading addressable scene: {e}");
                return;
            }

            _pendingAddressableOperations.Add(new PendingAddressableSceneOperation
            {
                guid = guid,
                handle = handle,
                idToAssign = action.sceneID,
                settings = action.parameters,
                loadAdditively = action.loadAdditively,
                ownsHandle = true
            });
            _sceneActionScenes.Add(action.sceneID);

            RegisterAddressableCompletionCallback(handle);

            if (_asServer && _networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    guid = guid,
                    handle = handle,
                    idToAssign = action.sceneID,
                    settings = action.parameters,
                    loadAdditively = action.loadAdditively,
                    ownsHandle = false
                });
                clientModule._sceneActionScenes.Add(action.sceneID);
                clientModule.RegisterAddressableCompletionCallback(handle);
            }

            onAddressableSceneStartLoading?.Invoke(action.sceneID, guid, _asServer);
        }

        private bool IsScenePendingAddressable(SceneID sceneId)
        {
            for (var i = 0; i < _pendingAddressableOperations.Count; i++)
            {
                if (!_pendingAddressableOperations[i].discardOnCompletion && _pendingAddressableOperations[i].idToAssign == sceneId)
                    return true;
            }

            return false;
        }

        private bool IsAddressableScenePending(SceneID sceneId, string guid, PurrSceneSettings settings)
        {
            for (var i = 0; i < _pendingAddressableOperations.Count; i++)
            {
                var operation = _pendingAddressableOperations[i];
                if (operation.discardOnCompletion)
                    continue;
                if (operation.idToAssign != sceneId)
                    continue;

                return (string.IsNullOrEmpty(guid) || operation.guid == guid) && operation.settings.physicsMode == settings.physicsMode;
            }

            return false;
        }

        private bool TryUnloadAddressableScene(SceneID sceneId, UnloadSceneOptions options)
        {
            return TryRemoveAddressableScene(sceneId, options, false, false, out _);
        }

        private bool TryUnloadAddressableSceneOnCleanup(SceneID sceneId)
        {
            if (!_addressableSceneHandles.TryGetValue(sceneId, out var handle))
                return false;

            UnregisterAddressableScene(sceneId);

            if (!handle.IsValid())
                return false;

            _pendingAddressableUnloads.Add(Addressables.UnloadSceneAsync(handle, UnloadSceneOptions.None, true));
            return true;
        }

        private bool ArePendingAddressableUnloadsDone()
        {
            for (var i = 0; i < _pendingAddressableUnloads.Count; i++)
            {
                var handle = _pendingAddressableUnloads[i];

                // The handle releases itself once it completes, which also invalidates it.
                if (handle.IsValid() && !handle.IsDone)
                    return false;
            }

            _pendingAddressableUnloads.Clear();
            return true;
        }

        private void DiscardPendingAddressableOperations(bool unloadPendingScenes)
        {
            for (var i = 0; unloadPendingScenes && i < _pendingAddressableOperations.Count; i++)
            {
                var operation = _pendingAddressableOperations[i];

                if (operation.ownsHandle)
                    ReleaseAddressableSceneHandle(operation.handle);
            }

            _pendingAddressableOperations.Clear();
            _pendingAddressableUnloads.Clear();
        }

        private static void ReleaseAddressableSceneHandle(AsyncOperationHandle<SceneInstance> handle)
        {
            if (!handle.IsValid())
                return;

            if (!handle.IsDone)
            {
                // Static callback on purpose: a closure here would keep the discarded module alive.
                handle.Completed += ReleaseCompletedAddressableSceneHandle;
                return;
            }

            ReleaseCompletedAddressableSceneHandle(handle);
        }

        private static void ReleaseCompletedAddressableSceneHandle(AsyncOperationHandle<SceneInstance> handle)
        {
            if (!handle.IsValid())
                return;

            if (handle.Status == AsyncOperationStatus.Succeeded)
                Addressables.UnloadSceneAsync(handle, UnloadSceneOptions.None, true);
            else Addressables.Release(handle);
        }

        /// <summary>
        /// Unloads an Addressable scene asynchronously by its SceneID.
        /// Use this instead of UnloadSceneAsync when you need to await an Addressable scene unload,
        /// since Addressables doesn't expose a Unity AsyncOperation for unloading.
        /// The returned handle is not auto-released, so it stays valid while you await it;
        /// call Addressables.Release on it once you are done.
        /// </summary>
        /// <param name="sceneId">The SceneID of the Addressable scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        /// <returns>The AsyncOperationHandle for the unload, or a default handle if invalid</returns>
        public AsyncOperationHandle<SceneInstance> UnloadAddressableSceneAsync(
            SceneID sceneId,
            UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return default;
            }

            if (!IsAddressableScene(sceneId))
            {
                PurrLogger.LogError($"Scene with ID {sceneId} is not a loaded Addressable scene");
                return default;
            }

            if (_scenes.TryGetValue(sceneId, out var state) && _networkManager.gameObject.scene == state.scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return default;
            }

            _history.AddUnloadAction(new UnloadSceneAction { sceneID = sceneId, options = options });
            TryRemoveAddressableScene(sceneId, options, false, true, out var handle);

            return handle;
        }

        /// <summary>
        /// Returns true if the given SceneID was loaded through Addressables.
        /// </summary>
        public bool IsAddressableScene(SceneID sceneId)
        {
            return _addressableSceneHandles.ContainsKey(sceneId) || _addressableSceneIdToGuid.ContainsKey(sceneId);
        }

        private bool TryGetLoadedAddressableSceneAction(SceneID id, out LoadAddressableSceneAction action)
        {
            action = default;
            if (!_scenes.TryGetValue(id, out var state) || !state.scene.IsValid() || !state.scene.isLoaded ||
                !_addressableSceneIdToGuid.TryGetValue(id, out var guid))
                return false;

            action = new LoadAddressableSceneAction
            {
                guid = guid,
                sceneID = id,
                parameters = state.settings,
                loadAdditively = true
            };
            return true;
        }

        private bool TryGetBootstrapAddressableAction(SceneID id, SceneState state, out SceneAction action)
        {
            action = default;
            if (!TryGetLoadedAddressableSceneAction(id, out var load))
            {
                if (!state.scene.IsValid() || !state.scene.isLoaded ||
                    !TryGetAddressableGuidForScenePath(state.scene.path, out var guid))
                    return false;

                // The loaded scene now has a proven GUID. Reuse the normal registry
                // for later joins without acquiring the external loader's handle.
                RegisterAddressableSceneGuid(id, guid);
                load = new LoadAddressableSceneAction
                {
                    guid = guid,
                    sceneID = id,
                    parameters = state.settings,
                    loadAdditively = true
                };
            }

            action = new SceneAction { type = SceneActionType.LoadAddressable, loadAddressableSceneAction = load };
            return true;
        }

        private Scene FindUnclaimedAddressableSceneForJoin(LoadAddressableSceneAction action, HashSet<Scene> claimed)
        {
            var guid = action.guid.value;
            // Already registered scenes were claimed before planning this join.
            if (!TryGetAddressableScenePath(guid, out var path))
                return default;

            // A pending load still owns its completion callback and assigned ID.
            foreach (var operation in _pendingAddressableOperations)
                if (operation.guid == guid)
                    return default;

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded || claimed.Contains(scene) ||
                    !string.Equals(scene.path, path, StringComparison.OrdinalIgnoreCase) ||
                    !HasMatchingScenePhysics(scene, action.parameters.physicsMode))
                    continue;
                return scene;
            }
            return default;
        }

        private static bool TryGetAddressableScenePath(string guid, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(guid))
                return false;

            foreach (var locator in Addressables.ResourceLocators)
            {
                if (!locator.Locate(guid, typeof(SceneInstance), out var locations))
                    continue;
                foreach (var location in locations)
                {
                    var candidate = Addressables.ResourceManager.TransformInternalId(location);
                    // Do not guess from a scene name, a bundle URL, or a custom
                    // provider's opaque ID. Only a complete asset path proves a match.
                    if (string.IsNullOrEmpty(candidate) ||
                        (!candidate.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                         !candidate.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) ||
                        !candidate.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                        return false;
                    if (path != null && !string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase))
                        return false;
                    path = candidate;
                }
            }
            return path != null;
        }

        private static bool TryGetAddressableGuidForScenePath(string path, out string guid)
        {
            guid = null;
            if (string.IsNullOrEmpty(path))
                return false;

            foreach (var locator in Addressables.ResourceLocators)
                foreach (var key in locator.Keys)
                {
                    if (!(key is string candidate) || !Guid.TryParseExact(candidate, "N", out _) ||
                        !TryGetAddressableScenePath(candidate, out var candidatePath) ||
                        !string.Equals(path, candidatePath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (guid != null && !string.Equals(guid, candidate, StringComparison.OrdinalIgnoreCase))
                        return false;
                    guid = candidate;
                }
            return guid != null;
        }

        private struct AddressableTransferRegistration
        {
            public string guid;
            public AsyncOperationHandle<SceneInstance> handle;
            public PurrSceneSettings settings;
        }

        private Dictionary<Scene, AddressableTransferRegistration> CaptureAddressableTransferRegistrations()
        {
            var registrations = new Dictionary<Scene, AddressableTransferRegistration>();
            foreach (var pair in _addressableSceneIdToGuid)
                if (_scenes.TryGetValue(pair.Key, out var state))
                {
                    _addressableSceneHandles.TryGetValue(pair.Key, out var handle);
                    registrations[state.scene] = new AddressableTransferRegistration
                    {
                        guid = pair.Value,
                        handle = handle,
                        settings = state.settings
                    };
                }
            return registrations;
        }

        private static Scene FindUnclaimedAddressableScene(string guid, PurrSceneSettings settings,
            HashSet<Scene> claimed, Dictionary<Scene, AddressableTransferRegistration> registrations)
        {
            foreach (var pair in registrations)
                if (pair.Value.guid == guid && pair.Value.settings.physicsMode == settings.physicsMode &&
                    pair.Key.IsValid() && pair.Key.isLoaded && !claimed.Contains(pair.Key))
                    return pair.Key;
            return default;
        }

        private void RestoreAddressableTransferRegistration(SceneID id, Scene scene, string guid,
            Dictionary<Scene, AddressableTransferRegistration> registrations)
        {
            if (registrations.TryGetValue(scene, out var registration))
                RegisterAddressableSceneHandle(id, guid, registration.handle);
        }

        private bool IsLoadedAddressableScene(SceneID sceneId, string guid, SceneState state)
        {
            if (!state.scene.IsValid() || !state.scene.isLoaded)
                return false;

            return _addressableSceneIdToGuid.TryGetValue(sceneId, out var existingGuid) && existingGuid == guid;
        }

        private void DiscardStalePendingAddressableTransfers(
            IReadOnlyDictionary<SceneID, string> targetAddressableScenes,
            IReadOnlyDictionary<SceneID, PurrSceneSettings> targetSettings,
            IReadOnlyDictionary<SceneID, SceneState> matches)
        {
            for (var i = _pendingAddressableOperations.Count - 1; i >= 0; i--)
            {
                var operation = _pendingAddressableOperations[i];
                if (targetAddressableScenes.TryGetValue(operation.idToAssign, out var guid) &&
                    operation.guid == guid && !matches.ContainsKey(operation.idToAssign) &&
                    operation.settings.physicsMode == targetSettings[operation.idToAssign].physicsMode)
                {
                    var actualMode = operation.loadAdditively ? LoadSceneMode.Additive : operation.settings.mode;
                    operation.settings = targetSettings[operation.idToAssign];
                    operation.loadAdditively = actualMode == LoadSceneMode.Additive && operation.settings.mode == LoadSceneMode.Single;
                    _pendingAddressableOperations[i] = operation;
                    continue;
                }

                operation.discardOnCompletion = true;
                _pendingAddressableOperations[i] = operation;
            }
        }

        partial void RebuildPendingAddressableHistory()
        {
            foreach (var operation in _pendingAddressableOperations)
                if (!operation.discardOnCompletion && !_scenes.ContainsKey(operation.idToAssign))
                    _history.AddLoadAddressableAction(new LoadAddressableSceneAction
                    {
                        guid = operation.guid,
                        sceneID = operation.idToAssign,
                        parameters = operation.settings,
                        loadAdditively = true
                    });
        }

        partial void ReservePendingAddressableSceneIDs()
        {
            foreach (var operation in _pendingAddressableOperations)
                ReserveSceneID(operation.idToAssign);
        }

        partial void HasPendingAddressableTransfers(ref bool pending)
        {
            pending = _pendingAddressableOperations.Count > 0 || !ArePendingAddressableUnloadsDone();
        }

        partial void HasPendingSingleAddressableLoad(ref bool pending)
        {
            foreach (var operation in _pendingAddressableOperations)
                if (!operation.loadAdditively && operation.settings.mode == LoadSceneMode.Single)
                {
                    pending = true;
                    return;
                }
        }

        private bool TryRemoveAddressableScene(
            SceneID sceneId,
            UnloadSceneOptions options,
            bool playUnloadEventsImmediately,
            bool keepUnloadHandleAlive,
            out AsyncOperationHandle<SceneInstance> unloadHandle)
        {
            unloadHandle = default;

            var hasHandle = _addressableSceneHandles.TryGetValue(sceneId, out var handle);

            if (!hasHandle && !_addressableSceneIdToGuid.ContainsKey(sceneId))
                return false;

            var hasState = _scenes.TryGetValue(sceneId, out var state);

            if (hasHandle && handle.IsValid())
            {
                unloadHandle = Addressables.UnloadSceneAsync(handle, options, !keepUnloadHandleAlive);
                if (_isReconcilingTransferScenes)
                    _pendingAddressableUnloads.Add(unloadHandle);
            }
            else if (hasState && (!_isReconcilingTransferScenes || !ShouldKeepLocalSceneDuringTransfer(state.scene)) &&
                     state.scene.IsValid() && state.scene.isLoaded)
            {
                var operation = SceneManager.UnloadSceneAsync(state.scene, options);
                if (_isReconcilingTransferScenes)
                    _pendingUnloads.Add(operation);
            }

            UnregisterAddressableScene(sceneId);
            if (hasState)
                RemoveScene(state.scene, playUnloadEventsImmediately);

            return true;
        }

        private void RegisterAddressableSceneHandle(
            SceneID sceneId,
            string guid,
            AsyncOperationHandle<SceneInstance> handle)
        {
            if (handle.IsValid())
                _addressableSceneHandles[sceneId] = handle;

            RegisterAddressableSceneGuid(sceneId, guid);
        }

        private void RegisterAddressableSceneGuid(SceneID sceneId, string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return;

            _addressableSceneIdToGuid[sceneId] = guid;
            if (!_addressableSceneGuidToIds.TryGetValue(guid, out var list))
            {
                list = new List<SceneID>();
                _addressableSceneGuidToIds[guid] = list;
            }

            if (!list.Contains(sceneId))
                list.Add(sceneId);
        }

        private void UnregisterAddressableScene(SceneID sceneId)
        {
            _addressableSceneHandles.Remove(sceneId);
            UnregisterAddressableSceneGuid(sceneId);
        }

        private void UnregisterAddressableSceneGuid(SceneID sceneId)
        {
            if (!_addressableSceneIdToGuid.TryGetValue(sceneId, out var guid))
                return;

            _addressableSceneIdToGuid.Remove(sceneId);
            if (!_addressableSceneGuidToIds.TryGetValue(guid, out var list))
                return;

            list.Remove(sceneId);
            if (list.Count == 0)
                _addressableSceneGuidToIds.Remove(guid);
        }

        /// <summary>
        /// Loads an Addressable scene asynchronously by AssetReference (or AssetReferenceScene).
        /// Only the server can load scenes.
        /// </summary>
        /// <param name="sceneRef">The AssetReference pointing to the Addressable scene</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        /// <returns>The AsyncOperationHandle for the load, or a default handle if invalid</returns>
        public AsyncOperationHandle<SceneInstance> LoadAddressableSceneAsync(
            AssetReference sceneRef,
            PurrSceneSettings settings)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes");
                return default;
            }

            if (sceneRef == null || !sceneRef.RuntimeKeyIsValid())
            {
                PurrLogger.LogError("LoadAddressableSceneAsync failed: AssetReference is null or invalid");
                return default;
            }

            var idToAssign = GetNextID();
            var guid = sceneRef.AssetGUID;

            if (settings.mode == LoadSceneMode.Single)
            {
                if (TryGetSceneID(_networkManager.gameObject.scene, out var nmId) &&
                    TryGetSceneState(nmId, out var nmScene))
                {
                    if (!IsDontDestroyOnLoadScene(nmScene.scene))
                    {
                        PurrLogger.LogError("Network manager scene is not DontDestroyOnLoad and you are trying to" +
                                            " load a new scene with LoadSceneMode.Single");
                    }
                }

                for (var i = _rawScenes.Count - 1; i >= 0; i--)
                {
                    var isDontDestroyOnLoadScene = IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene);
                    if (!isDontDestroyOnLoadScene)
                        RemoveScene(_scenes[_rawScenes[i]].scene);
                }
            }

            _history.AddLoadAddressableAction(new LoadAddressableSceneAction
            {
                guid = guid,
                sceneID = idToAssign,
                parameters = settings
            });
            _sceneActionScenes.Add(idToAssign);

            var parameters = new LoadSceneParameters(settings.mode, settings.physicsMode);
            var handle = Addressables.LoadSceneAsync(sceneRef, parameters, true, 100);

            _pendingAddressableOperations.Add(new PendingAddressableSceneOperation
            {
                guid = guid,
                handle = handle,
                idToAssign = idToAssign,
                settings = settings,
                ownsHandle = true
            });

            RegisterAddressableCompletionCallback(handle);

            if (_networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    guid = guid,
                    handle = handle,
                    idToAssign = idToAssign,
                    settings = settings,
                    ownsHandle = false
                });
                clientModule._sceneActionScenes.Add(idToAssign);
                clientModule.RegisterAddressableCompletionCallback(handle);
            }

            return handle;
        }

        /// <summary>
        /// Loads an Addressable scene asynchronously by GUID.
        /// Only the server can load scenes.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        /// <returns>The AsyncOperationHandle for the load, or a default handle if invalid</returns>
        public AsyncOperationHandle<SceneInstance> LoadAddressableSceneAsync(string guid, PurrSceneSettings settings)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes");
                return default;
            }

            if (string.IsNullOrEmpty(guid))
            {
                PurrLogger.LogError("LoadAddressableSceneAsync failed: GUID is null or empty");
                return default;
            }

            var idToAssign = GetNextID();

            if (settings.mode == LoadSceneMode.Single)
            {
                if (TryGetSceneID(_networkManager.gameObject.scene, out var nmId) &&
                    TryGetSceneState(nmId, out var nmScene))
                {
                    if (!IsDontDestroyOnLoadScene(nmScene.scene))
                    {
                        PurrLogger.LogError("Network manager scene is not DontDestroyOnLoad and you are trying to" +
                                            " load a new scene with LoadSceneMode.Single");
                    }
                }

                for (var i = _rawScenes.Count - 1; i >= 0; i--)
                {
                    var isDontDestroyOnLoadScene = IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene);
                    if (!isDontDestroyOnLoadScene)
                        RemoveScene(_scenes[_rawScenes[i]].scene);
                }
            }

            _history.AddLoadAddressableAction(new LoadAddressableSceneAction
            {
                guid = guid,
                sceneID = idToAssign,
                parameters = settings
            });
            _sceneActionScenes.Add(idToAssign);

            var parameters = new LoadSceneParameters(settings.mode, settings.physicsMode);
            var handle = Addressables.LoadSceneAsync(guid, parameters, true, 100);

            _pendingAddressableOperations.Add(new PendingAddressableSceneOperation
            {
                guid = guid,
                handle = handle,
                idToAssign = idToAssign,
                settings = settings,
                ownsHandle = true
            });

            RegisterAddressableCompletionCallback(handle);

            if (_networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingAddressableOperations.Add(new PendingAddressableSceneOperation
                {
                    guid = guid,
                    handle = handle,
                    idToAssign = idToAssign,
                    settings = settings,
                    ownsHandle = false
                });
                clientModule._sceneActionScenes.Add(idToAssign);
                clientModule.RegisterAddressableCompletionCallback(handle);
            }

            return handle;
        }

        /// <summary>
        /// Returns the pending addressable operations for this module.
        /// This allows you to check if a scene is still loading or unloading and the progress of the operation.
        /// </summary>
        /// <returns>List of pending operations</returns>
        public IReadOnlyList<PendingAddressableSceneOperation> GetPendingAddressableOperations()
        {
            return _pendingAddressableOperations;
        }

        /// <summary>
        /// Returns true if an Addressable scene with the given GUID is currently loaded (or loading).
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <returns>True if at least one instance of the scene is loaded or currently loading</returns>
        public bool IsAddressableSceneLoaded(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return false;

            if (_addressableSceneGuidToIds.TryGetValue(guid, out var list) && list.Count > 0)
                return true;

            return IsAddressableSceneLoading(guid);
        }

        /// <summary>
        /// Returns true if an Addressable scene with the given GUID is currently loading.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <returns>True if the scene is currently loading</returns>
        public bool IsAddressableSceneLoading(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return false;

            for (var i = 0; i < _pendingAddressableOperations.Count; i++)
            {
                if (_pendingAddressableOperations[i].guid == guid)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to get the first SceneID for an Addressable scene loaded by the given GUID.
        /// Use GetSceneIdsByAddressableGuid when multiple instances may exist.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <param name="sceneId">The SceneID if found</param>
        /// <returns>True if the scene is loaded and a SceneID was found</returns>
        public bool TryGetSceneIdByAddressableGuid(string guid, out SceneID sceneId)
        {
            if (string.IsNullOrEmpty(guid))
            {
                sceneId = default;
                return false;
            }

            if (_addressableSceneGuidToIds.TryGetValue(guid, out var list) && list.Count > 0)
            {
                sceneId = list[0];
                return true;
            }

            sceneId = default;
            return false;
        }

        /// <summary>
        /// Gets all SceneIDs for Addressable scenes loaded by the given GUID.
        /// Returns an empty list if none are loaded.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene</param>
        /// <returns>A list of SceneIDs (may be empty, never null). Do not modify the returned list.</returns>
        public IReadOnlyList<SceneID> GetSceneIdsByAddressableGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return System.Array.Empty<SceneID>();

            if (_addressableSceneGuidToIds.TryGetValue(guid, out var list))
                return list;

            return System.Array.Empty<SceneID>();
        }

        /// <summary>
        /// Unloads all instances of an Addressable scene by its asset GUID.
        /// Only the server can unload scenes. Returns the number of instances unloaded.
        /// </summary>
        /// <param name="guid">The Addressable asset GUID of the scene to unload</param>
        /// <param name="options">The UnloadSceneOptions to use</param>
        /// <returns>The number of scene instances that were unloaded</returns>
        public int UnloadAddressableSceneByGuid(
            string guid,
            UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return 0;
            }

            var ids = GetSceneIdsByAddressableGuid(guid);
            var count = ids.Count;

            for (var i = ids.Count - 1; i >= 0; i--)
                UnloadSceneAsync(ids[i], options);

            return count;
        }
    }
}
#endif
