using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hash = PurrNet.Utils.Hasher;

namespace PurrNet.Modules
{
    public struct PendingSceneOperation
    {
        public int buildIndex;
        public uint scenePathHash;
        public SceneID idToAssign;
        public PurrSceneSettings settings;
        [UsedImplicitly]
        public AsyncOperation operation;
    }

    public struct SceneState
    {
        /// <summary>
        /// The unity scene object this ID is associated with
        /// </summary>
        public Scene scene;

        /// <summary>
        /// The network settings for this scene
        /// </summary>
        public PurrSceneSettings settings;

        public SceneState(Scene scene, PurrSceneSettings settings)
        {
            this.scene = scene;
            this.settings = settings;
        }
    }

    public struct PurrSceneSettings
    {
        public LoadSceneMode mode;
        public LocalPhysicsMode physicsMode;
        public bool isPublic;
    }

    public delegate void OnSceneActionEvent(SceneID scene, bool asServer);

    public delegate void OnSceneVisibilityEvent(SceneID scene, bool isVisible, bool asServer);

    public partial class ScenesModule : INetworkModule, IFixedUpdate, ICleanup, IConnectionStateListener, ITransferToNewServer, IPromoteToServerModule
    {
        private static readonly Dictionary<int, uint> _buildIndexToHash = new Dictionary<int, uint>();
        private static readonly Dictionary<uint, int> _hashToBuildIndex = new Dictionary<uint, int>();
        private static bool _sceneHashCacheBuilt;

        private readonly NetworkManager _networkManager;
        private readonly PlayersManager _players;

        private readonly SceneHistory _history;
        private bool _asServer;

        private readonly List<PendingSceneOperation> _pendingOperations = new List<PendingSceneOperation>();
        private readonly Queue<SceneAction> _actionsQueue = new Queue<SceneAction>();

        private readonly Dictionary<SceneID, SceneState> _scenes = new Dictionary<SceneID, SceneState>();
        private readonly Dictionary<Scene, SceneID> _idToScene = new Dictionary<Scene, SceneID>(SceneEqualityComparer.instance);
        private readonly List<SceneID> _rawScenes = new List<SceneID>();
        private readonly HashSet<SceneID> _sceneActionScenes = new HashSet<SceneID>();

        /// <summary>
        /// First callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onPreSceneLoaded;

        /// <summary>
        /// Callback for when a scene is loaded
        /// </summary>
        public event OnSceneActionEvent onSceneLoaded;

        /// <summary>
        /// Callback for after onSceneLoaded has been called
        /// </summary>
        public event OnSceneActionEvent onPostSceneLoaded;

        /// <summary>
        /// First callback for when a scene is unloaded
        /// </summary>
        public event OnSceneActionEvent onPreSceneUnloaded;

        /// <summary>
        /// Callback for when a scene is unloaded
        /// </summary>
        public event OnSceneActionEvent onSceneUnloaded;

        /// <summary>
        /// Callback for after onSceneUnloaded has been called
        /// </summary>
        public event OnSceneActionEvent onPostSceneUnloaded;

        /// <summary>
        /// Callback for when a scene's visibility changes
        /// </summary>
        public event OnSceneVisibilityEvent onSceneVisibilityChanged;

        private ushort _nextSceneID = 1;
        private ScenePlayersModule _scenePlayers;

        public IReadOnlyList<SceneID> scenes => _rawScenes;
        public IReadOnlyDictionary<SceneID, SceneState> sceneStates => _scenes;

        private SceneID GetNextID() => new(_nextSceneID++);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSceneHashCache()
        {
            _buildIndexToHash.Clear();
            _hashToBuildIndex.Clear();
            _sceneHashCacheBuilt = false;
        }

        public ScenesModule(NetworkManager manager, PlayersManager players)
        {
            _networkManager = manager;
            _players = players;
            _history = new SceneHistory();
        }

        internal void SetScenePlayers(ScenePlayersModule scenePlayersModule)
        {
            _scenePlayers = scenePlayersModule;
        }

        public bool TryGetSceneState(SceneID sceneID, out SceneState state)
        {
            return _scenes.TryGetValue(sceneID, out state);
        }

        private void AddScene(Scene scene, PurrSceneSettings settings, SceneID id)
        {
            if (_scenes.TryGetValue(id, out var state))
            {
                PurrLogger.LogError($"Scene with ID {id} already exists under {state.scene.name}");
                return;
            }

            _scenes.Add(id, new SceneState(scene, settings));
            _idToScene.Add(scene, id);
            _rawScenes.Add(id);

            PlayLoadEventsForScene(id);
        }

        private bool HasScene(Scene scene)
        {
            foreach (var state in _scenes.Values)
            {
                if (state.scene.handle == scene.handle)
                    return true;
            }

            return false;
        }

        private void PlayLoadEventsForScene(SceneID id)
        {
            onPreSceneLoaded?.Invoke(id, _asServer);
            onSceneLoaded?.Invoke(id, _asServer);
            onPostSceneLoaded?.Invoke(id, _asServer);
        }

        /// <summary>
        /// Used to modify whether the given scene is public or not
        /// </summary>
        /// <param name="scene">The SceneID of the scene to modify</param>
        /// <param name="isPublic">Whether the given scene should be public</param>
        public void UpdateSceneVisibility(SceneID scene, bool isPublic)
        {
            if (_asServer)
            {
                PurrLogger.LogError("Only clients can change scene visibility; for now at least ;)");
                return;
            }

            if (!_scenes.TryGetValue(scene, out var state))
            {
                PurrLogger.LogError($"Scene with ID {scene} not found");
                return;
            }

            state.settings.isPublic = isPublic;
            _scenes[scene] = state;

            onSceneVisibilityChanged?.Invoke(scene, isPublic, _asServer);
        }

        private readonly List<SceneID> _scenesToTriggerUnloadEvent = new List<SceneID>();

        private void RemoveScene(Scene scene)
        {
            RemoveScene(scene, false);
        }

        private void RemoveScene(Scene scene, bool playUnloadEventsImmediately)
        {
            if (!_idToScene.TryGetValue(scene, out var id))
                return;

            _scenes.Remove(id);
            _idToScene.Remove(scene);
            _rawScenes.Remove(id);
            _sceneActionScenes.Remove(id);
            _scenesToTriggerUnloadEvent.Remove(id);

            if (playUnloadEventsImmediately)
                PlayUnloadEventsForScene(id);
            else _scenesToTriggerUnloadEvent.Add(id);
        }

        private void PlayUnloadEventsForScene(SceneID id)
        {
            onPreSceneUnloaded?.Invoke(id, _asServer);
            onSceneUnloaded?.Invoke(id, _asServer);
            onPostSceneUnloaded?.Invoke(id, _asServer);
        }

        public void OnConnectionState(ConnectionState state, bool asServer)
        {
            if (state != ConnectionState.Connected)
                return;

            if (!_wasSetup)
                Setup(asServer);
        }

        private bool _wasSetup;
        private bool _isDisabled;

        static GameObject _dontDestroyOnLoad;

        private static Scene GetDontDestroyOnLoadScene()
        {
            if (_dontDestroyOnLoad)
                return _dontDestroyOnLoad.scene;
            _dontDestroyOnLoad = new GameObject("PurrNet:DontDestroyOnLoad")
            {
                hideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy
            };
            Object.DontDestroyOnLoad(_dontDestroyOnLoad);
            return _dontDestroyOnLoad.scene;
        }

        static bool IsDontDestroyOnLoadScene(Scene scene)
        {
            return scene.name is "DontDestroyOnLoad";
        }


        public void PromoteToServerModule()
        {
            _asServer = true;
            RemoveUnloadedSceneStates();
            _rebuildHistoryOnNextPlayerJoin = true;
            _players.Unsubscribe<SceneActionsBatch>(OnSceneActionsBatch);
            _players.Unsubscribe<FirstSceneActionsBatch>(OnSceneActionsBatch);
            _players.onPrePlayerJoined += OnPlayerJoined;
            _scenePlayers.onPlayerJoinedScene += OnPlayerJoinedScene;
            _scenePlayers.onPlayerLeftScene += OnPlayerLeftScene;
        }

        private bool _rebuildHistoryOnNextPlayerJoin;

        private void RemoveUnloadedSceneStates()
        {
            for (var i = _rawScenes.Count - 1; i >= 0; i--)
            {
                var id = _rawScenes[i];

                if (!_scenes.TryGetValue(id, out var state))
                {
                    _rawScenes.RemoveAt(i);
                    _sceneActionScenes.Remove(id);
                    _scenesToTriggerUnloadEvent.Remove(id);
                    continue;
                }

                if (state.scene.IsValid() && state.scene.isLoaded)
                    continue;

                _scenes.Remove(id);
                _rawScenes.RemoveAt(i);
                _sceneActionScenes.Remove(id);
                _scenesToTriggerUnloadEvent.Remove(id);

                if (_idToScene.TryGetValue(state.scene, out var mappedId) && mappedId == id)
                    _idToScene.Remove(state.scene);
            }
        }

        private void RebuildHistoryFromLoadedBuildScenes()
        {
            _history.Clear();

            for (var i = 0; i < _rawScenes.Count; i++)
            {
                var id = _rawScenes[i];
                if (!_sceneActionScenes.Contains(id))
                    continue;

                if (!_scenes.TryGetValue(id, out var state))
                    continue;

                if (!state.scene.IsValid() || !state.scene.isLoaded)
                    continue;

                var buildIndex = state.scene.buildIndex;
                if (buildIndex < 0)
                    continue;

                _history.AddLoadAction(new LoadSceneAction
                {
                    scenePathHash = ScenePathHashFromBuildIndex(buildIndex),
                    sceneID = id,
                    parameters = state.settings
                });
            }

            RebuildAddressableHistoryFromLoadedScenes();
            _history.Flush();
        }

        private bool _isTransferingToNewServer;

        public void PostPromoteToServerModule()
        {

        }

        public void TransferToNewServer()
        {
            _isTransferingToNewServer = true;
        }

        private void Setup(bool asServer)
        {
            _wasSetup = true;
            _asServer = asServer;

            var currentScene = _networkManager.gameObject.scene;
            var originalScene = _networkManager.originalScene;

            var hasDontDestroyOnLoadScene = IsDontDestroyOnLoadScene(currentScene) ||
                                          IsDontDestroyOnLoadScene(originalScene);

            AddScene(currentScene, new PurrSceneSettings
            {
                mode = LoadSceneMode.Single,
                isPublic = true,
                physicsMode = LocalPhysicsMode.None
            }, GetNextID());

            if (currentScene != originalScene && originalScene.IsValid())
            {
                AddScene(originalScene, new PurrSceneSettings
                {
                    mode = LoadSceneMode.Additive,
                    isPublic = true,
                    physicsMode = LocalPhysicsMode.None
                }, GetNextID());
            }

            var rules = _networkManager.networkRules;

            if (!hasDontDestroyOnLoadScene && rules && rules.ShouldAlwaysIncludeDontDestroyOnLoadScene())
            {
                var dontDestroyScene = GetDontDestroyOnLoadScene();
                AddScene(dontDestroyScene, new PurrSceneSettings
                {
                    mode = LoadSceneMode.Additive,
                    isPublic = true,
                    physicsMode = LocalPhysicsMode.None
                }, GetNextID());
            }

            if (!asServer)
                MirrorAlreadyLoadedHostScenes();

            if (!asServer)
            {
                _players.Subscribe<SceneActionsBatch>(OnSceneActionsBatch);
                _players.Subscribe<FirstSceneActionsBatch>(OnSceneActionsBatch);
            }
            else
            {
                _players.onPrePlayerJoined += OnPlayerJoined;
                _scenePlayers.onPlayerJoinedScene += OnPlayerJoinedScene;
                _scenePlayers.onPlayerLeftScene += OnPlayerLeftScene;
            }

            SceneManager.sceneLoaded += SceneManagerOnSceneLoaded;
        }

        private void MirrorAlreadyLoadedHostScenes()
        {
            if (!_networkManager.isServer)
                return;

            if (!_networkManager.TryGetModule<ScenesModule>(true, out var serverModule))
                return;

            foreach (var pair in serverModule.sceneStates)
            {
                var sceneId = pair.Key;
                var state = pair.Value;

                if (!serverModule._sceneActionScenes.Contains(sceneId))
                    continue;

                if (!state.scene.IsValid() || !state.scene.isLoaded)
                    continue;

                if (_scenes.ContainsKey(sceneId) || HasScene(state.scene))
                    continue;

                _sceneActionScenes.Add(sceneId);
                AddScene(state.scene, state.settings, sceneId);

                var nextSceneId = (int)sceneId.id + 1;
                if (nextSceneId > _nextSceneID && nextSceneId <= ushort.MaxValue)
                    _nextSceneID = (ushort)nextSceneId;
            }
        }

        public void Enable(bool asServer)
        {
            // Setup(asServer);
            _isDisabled = false;
        }

        public void Disable(bool asServer)
        {
            _isDisabled = true;

#if ADDRESSABLES_PURRNET_SUPPORT
            // This module is about to be discarded; any addressable load still in flight would
            // otherwise complete into a registry nobody owns and never be unloaded by anyone.
            // Unless the rules keep scenes around on disconnect: dropping a scene that just happened
            // to still be loading, while every scene that finished in time is kept, is worse.
            var sceneRules = _networkManager.networkRules;
            var unloadPendingScenes = !sceneRules || sceneRules.ShouldCleanupScenesOnDisconnect();

            DiscardPendingAddressableOperations(unloadPendingScenes);
#endif

            if (!asServer)
            {
                _players.Unsubscribe<SceneActionsBatch>(OnSceneActionsBatch);
                _players.Unsubscribe<FirstSceneActionsBatch>(OnSceneActionsBatch);
            }
            else
            {
                _players.onPrePlayerJoined -= OnPlayerJoined;
                _scenePlayers.onPlayerJoinedScene -= OnPlayerJoinedScene;
                _scenePlayers.onPlayerLeftScene -= OnPlayerLeftScene;
            }

            SceneManager.sceneLoaded -= SceneManagerOnSceneLoaded;
        }

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (!asServer)
                return;

            if (_rebuildHistoryOnNextPlayerJoin)
            {
                _rebuildHistoryOnNextPlayerJoin = false;
                RebuildHistoryFromLoadedBuildScenes();
            }

            var history = _history.GetFullHistory();

            _playerFilteredActions.Clear();

            for (var i = 0; i < history.actions.Count; i++)
            {
                var action = history.actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.LoadAddressable => action.loadAddressableSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (ShouldSendSceneActionOnJoin(player, target, isReconnect))
                    _playerFilteredActions.Add(action);
            }

            _players.Send(player, new FirstSceneActionsBatch { actions = _playerFilteredActions });
        }

        private bool ShouldSendSceneActionOnJoin(PlayerID player, SceneID target, bool isReconnect)
        {
            if (_scenePlayers.IsPlayerInScene(player, target))
                return true;

            if (!isReconnect)
                return false;

            return _scenes.TryGetValue(target, out var state) && state.settings.isPublic;
        }

        private void OnPlayerLeftScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (!asServer)
                return;

            bool isSceneStillValid = _scenes.TryGetValue(scene, out var state) && state.scene.IsValid();

            if (!isSceneStillValid)
                return;

            _playerFilteredActions.Clear();
            _playerFilteredActions.Add(new SceneAction
            {
                type = SceneActionType.Unload,
                unloadSceneAction = new UnloadSceneAction
                {
                    sceneID = scene,
                    options = UnloadSceneOptions.None
                }
            });

            _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
        }

        private void OnPlayerJoinedScene(PlayerID player, SceneID scene, bool asServer)
        {
            if (!asServer)
                return;

            var history = _history.GetFullHistory();

            _playerFilteredActions.Clear();

            // send all actions for the scene
            FilterActionsForPlayerBySceneID(player, scene, history.actions, _playerFilteredActions);

            if (_playerFilteredActions.Count > 0)
                _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
        }

        /// <summary>
        /// Returns the pending operations for this module.
        /// This allows you to check if a scene is still loading or unloading and the progress of the operation.
        /// </summary>
        /// <returns>List of pending operations</returns>
        public IReadOnlyList<PendingSceneOperation> GetPendingOperations()
        {
            return _pendingOperations;
        }

        private void SceneManagerOnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var loadedHash = Hash.Hash(scene.path);

            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                var operation = _pendingOperations[i];

                if (operation.scenePathHash == loadedHash && operation.settings.mode == mode)
                {
                    _sceneActionScenes.Add(operation.idToAssign);
                    AddScene(scene, operation.settings, operation.idToAssign);
                    _pendingOperations.RemoveAt(i);
                    break;
                }
            }
        }

        private bool IsScenePending(SceneID sceneId)
        {
            for (int i = 0; i < _pendingOperations.Count; i++)
            {
                if (_pendingOperations[i].idToAssign == sceneId)
                    return true;
            }
#if ADDRESSABLES_PURRNET_SUPPORT
            if (IsScenePendingAddressable(sceneId))
                return true;
#endif
            return false;
        }

        int GetCurrentLoadedScenes()
        {
            int count = 0;

            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (IsDontDestroyOnLoadScene(state.scene))
                        continue;

                    if (state.scene.isLoaded)
                        count++;
                }
            }

            return count;
        }

        private void HandleNextSceneAction()
        {
            if (_actionsQueue.Count == 0) return;

            var action = _actionsQueue.Peek();
            switch (action.type)
            {
                case SceneActionType.Load:
                    {
                        if (_networkManager.isHost && !_asServer)
                        {
                            _actionsQueue.Dequeue();
                            break;
                        }

                        var loadAction = action.loadSceneAction;

                        // A reconnect delivers the same load action twice: once in the
                        // first-join batch and once when the player is re-added to the
                        // public scene. Loading again would duplicate the unity scene
                        // and clash with the already assigned SceneID.
                        if (_scenes.ContainsKey(loadAction.sceneID) || IsScenePending(loadAction.sceneID))
                        {
                            _sceneActionScenes.Add(loadAction.sceneID);
                            _actionsQueue.Dequeue();
                            break;
                        }

                        var localBuildIndex = BuildIndexFromScenePathHash(loadAction.scenePathHash);

                        if (localBuildIndex == -1)
                        {
                            PurrLogger.LogError($"Scene with path hash '{loadAction.scenePathHash}' not found in build settings");
                            _actionsQueue.Dequeue();
                            break;
                        }

                        AsyncOperation operation;

                        try
                        {
                            operation = SceneManager.LoadSceneAsync(localBuildIndex, loadAction.GetLoadSceneParameters());
                        }
                        catch (System.Exception e)
                        {
                            PurrLogger.LogError($"Error loading scene: {e}");
                            break;
                        }

                        if (loadAction.parameters.mode == LoadSceneMode.Single)
                        {
                            for (int i = 0; i < _rawScenes.Count; i++)
                            {
                                if (!IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene))
                                    RemoveScene(_scenes[_rawScenes[i]].scene);
                            }
                        }

                        _pendingOperations.Add(new PendingSceneOperation
                        {
                            buildIndex = localBuildIndex,
                            scenePathHash = loadAction.scenePathHash,
                            settings = loadAction.parameters,
                            idToAssign = loadAction.sceneID,
                            operation = operation
                        });
                        _sceneActionScenes.Add(loadAction.sceneID);

                        _actionsQueue.Dequeue();
                        break;
                    }
                case SceneActionType.LoadAddressable:
                    {
                        if (_networkManager.isHost && !_asServer)
                        {
                            _actionsQueue.Dequeue();
                            break;
                        }

#if ADDRESSABLES_PURRNET_SUPPORT
                        ProcessLoadAddressableAction(action.loadAddressableSceneAction);
#else
                        PurrLogger.LogError("Received LoadAddressable scene action but Addressables support is not available");
#endif
                        _actionsQueue.Dequeue();
                        break;
                    }
                case SceneActionType.Unload:
                    {
                        var currentlyLoadedCount = GetCurrentLoadedScenes();
                        if (currentlyLoadedCount == 1)
                        {
                            // wait for the next load action
                            break;
                        }

                        var idx = action.unloadSceneAction.sceneID;

                        if (_networkManager.isHost && !_asServer)
                        {
                            _scenesToTriggerUnloadEvent.Add(idx);
                            _actionsQueue.Dequeue();
                            break;
                        }

                        // if the scene is pending, don't do anything for now
                        if (IsScenePending(idx)) break;
#if ADDRESSABLES_PURRNET_SUPPORT
                        if (TryUnloadAddressableScene(idx, action.unloadSceneAction.options))
                        {
                            _actionsQueue.Dequeue();
                            break;
                        }
#endif

                        if (!_scenes.TryGetValue(idx, out var sceneState))
                        {
                            PurrLogger.LogError($"Couldn't find scene with index {idx} to unload");
                            break;
                        }

                        SceneManager.UnloadSceneAsync(sceneState.scene, action.unloadSceneAction.options);
                        RemoveScene(sceneState.scene);
                        _actionsQueue.Dequeue();
                        break;
                    }
            }
        }

        private void OnSceneActionsBatch(PlayerID player, FirstSceneActionsBatch data, bool asServer)
        {
            if (!_isTransferingToNewServer)
            {
                HandleScenes(data.actions);
                return;
            }

            _isTransferingToNewServer = false;

            ReconcileTransferScenes(data.actions);
        }

        private void ReconcileTransferScenes(List<SceneAction> actions)
        {
            _actionsQueue.Clear();

            if (actions == null)
                return;

            var targetScenes = new HashSet<SceneID>();
            var targetBuildScenes = new Dictionary<SceneID, uint>();
#if ADDRESSABLES_PURRNET_SUPPORT
            var targetAddressableScenes = new Dictionary<SceneID, string>();
#endif
            var missingActions = new List<SceneAction>();
            var replayLoadEvents = new List<SceneID>();

            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                switch (action.type)
                {
                    case SceneActionType.Load:
                    {
                        var loadAction = action.loadSceneAction;
                        targetScenes.Add(loadAction.sceneID);
                        targetBuildScenes[loadAction.sceneID] = loadAction.scenePathHash;
                        _sceneActionScenes.Add(loadAction.sceneID);

                        var buildIndex = BuildIndexFromScenePathHash(loadAction.scenePathHash);
                        if (buildIndex == -1)
                        {
                            missingActions.Add(action);
                            break;
                        }

                        if (TryReconcileLoadedTransferScene(loadAction, buildIndex, replayLoadEvents))
                            break;

                        if (!IsBuildScenePending(loadAction.sceneID, loadAction.scenePathHash))
                            missingActions.Add(action);

                        break;
                    }
                    case SceneActionType.LoadAddressable:
                    {
                        var loadAction = action.loadAddressableSceneAction;
                        targetScenes.Add(loadAction.sceneID);
                        _sceneActionScenes.Add(loadAction.sceneID);
#if ADDRESSABLES_PURRNET_SUPPORT
                        var guid = loadAction.guid.value;
                        targetAddressableScenes[loadAction.sceneID] = guid;

                        if (TryReconcileLoadedAddressableTransferScene(loadAction, replayLoadEvents))
                            break;

                        if (!IsAddressableScenePending(loadAction.sceneID, guid))
                            missingActions.Add(action);
#else
                        missingActions.Add(action);
#endif
                        break;
                    }
                    case SceneActionType.Unload:
                    case SceneActionType.SetActive:
                    default:
                        missingActions.Add(action);
                        break;
                }
            }

#if ADDRESSABLES_PURRNET_SUPPORT
            RemoveStaleAddressableTransferScenes(targetAddressableScenes);
#endif
            RemoveStaleTransferScenes(targetScenes, targetBuildScenes);

            for (var i = 0; i < replayLoadEvents.Count; i++)
                PlayLoadEventsForScene(replayLoadEvents[i]);

            if (missingActions.Count > 0)
                HandleScenes(missingActions);
        }

        private bool TryReconcileLoadedTransferScene(
            LoadSceneAction loadAction,
            int buildIndex,
            ICollection<SceneID> replayLoadEvents)
        {
            if (_scenes.TryGetValue(loadAction.sceneID, out var existing))
            {
                if (existing.scene.IsValid() && existing.scene.isLoaded && existing.scene.buildIndex == buildIndex)
                {
                    _scenes[loadAction.sceneID] = new SceneState(existing.scene, loadAction.parameters);
                    _sceneActionScenes.Add(loadAction.sceneID);
                    replayLoadEvents.Add(loadAction.sceneID);
                    return true;
                }

                RemoveScene(existing.scene, true);
            }

            var loadedScene = SceneManager.GetSceneByBuildIndex(buildIndex);
            if (!loadedScene.IsValid() || !loadedScene.isLoaded)
                return false;

            BindLoadedTransferScene(loadedScene, loadAction.parameters, loadAction.sceneID);
            replayLoadEvents.Add(loadAction.sceneID);
            return true;
        }

        private bool IsBuildScenePending(SceneID sceneId, uint scenePathHash)
        {
            for (var i = 0; i < _pendingOperations.Count; i++)
            {
                var operation = _pendingOperations[i];
                if (operation.idToAssign == sceneId && operation.scenePathHash == scenePathHash)
                    return true;
            }

            return false;
        }

        private void BindLoadedTransferScene(Scene scene, PurrSceneSettings settings, SceneID id)
        {
            if (_idToScene.TryGetValue(scene, out var oldId))
            {
                if (oldId == id)
                {
                    _scenes[id] = new SceneState(scene, settings);
                    _sceneActionScenes.Add(id);
                    _scenesToTriggerUnloadEvent.Remove(id);
                    return;
                }

                RemoveScene(scene, true);
            }

            if (_scenes.TryGetValue(id, out var oldState))
                RemoveScene(oldState.scene, true);

            _scenes[id] = new SceneState(scene, settings);
            _idToScene[scene] = id;
            _sceneActionScenes.Add(id);
            if (!_rawScenes.Contains(id))
                _rawScenes.Add(id);

            _scenesToTriggerUnloadEvent.Remove(id);
        }

        private void RemoveStaleTransferScenes(
            HashSet<SceneID> targetScenes,
            IReadOnlyDictionary<SceneID, uint> targetBuildScenes)
        {
            for (var i = _pendingOperations.Count - 1; i >= 0; i--)
            {
                var operation = _pendingOperations[i];
                if (!targetBuildScenes.TryGetValue(operation.idToAssign, out var scenePathHash) ||
                    operation.scenePathHash != scenePathHash)
                {
                    _pendingOperations.RemoveAt(i);
                }
            }

            for (var i = _rawScenes.Count - 1; i >= 0; i--)
            {
                var id = _rawScenes[i];
                if (targetScenes.Contains(id))
                    continue;

                if (!_scenes.TryGetValue(id, out var state))
                    continue;

                if (ShouldKeepLocalSceneDuringTransfer(state.scene))
                    continue;

                RemoveScene(state.scene, true);

                if (state.scene.IsValid() && state.scene.isLoaded)
                    SceneManager.UnloadSceneAsync(state.scene);
            }
        }

        private bool ShouldKeepLocalSceneDuringTransfer(Scene scene)
        {
            if (!scene.IsValid())
                return false;

            if (IsDontDestroyOnLoadScene(scene))
                return true;

            if (_networkManager.gameObject.scene.handle == scene.handle)
                return true;

            var originalScene = _networkManager.originalScene;
            return originalScene.IsValid() && originalScene.handle == scene.handle;
        }

        private void OnSceneActionsBatch(PlayerID player, SceneActionsBatch data, bool asServer)
        {
            HandleScenes(data.actions);
        }

        private void HandleScenes(List<SceneAction> actions)
        {
            if (_networkManager.isServer || _asServer)
            {
                var serverModule = _networkManager.GetModule<ScenesModule>(true);
                for (var i = 0; i < actions.Count; i++)
                {
                    var action = actions[i];

                    switch (action.type)
                    {
                        case SceneActionType.Load:
                        {
                            if (_scenes.ContainsKey(action.loadSceneAction.sceneID))
                                continue;

                            if (serverModule.TryGetSceneState(action.loadSceneAction.sceneID, out var state))
                            {
                                _sceneActionScenes.Add(action.loadSceneAction.sceneID);
                                AddScene(state.scene, state.settings, action.loadSceneAction.sceneID);
                            }
                            break;
                        }
                        case SceneActionType.LoadAddressable:
                        {
                            if (_scenes.ContainsKey(action.loadAddressableSceneAction.sceneID))
                                continue;

                            if (serverModule.TryGetSceneState(action.loadAddressableSceneAction.sceneID, out var state))
                            {
                                _sceneActionScenes.Add(action.loadAddressableSceneAction.sceneID);
                                AddScene(state.scene, state.settings, action.loadAddressableSceneAction.sceneID);
                            }
                            break;
                        }
                        case SceneActionType.Unload:
                        {
                            if (!_scenes.ContainsKey(action.unloadSceneAction.sceneID))
                                continue;

                            if (serverModule.TryGetSceneState(action.unloadSceneAction.sceneID, out var state))
                                RemoveScene(state.scene);
                            break;
                        }

                        case SceneActionType.SetActive:
                        default:
                            break;
                    }
                }

                return;
            }

            for (var i = 0; i < actions.Count; i++)
                _actionsQueue.Enqueue(actions[i]);

            HandleNextSceneAction();
        }

        private static int SceneNameToBuildIndex(string name)
        {
            var bIdxCount = SceneManager.sceneCountInBuildSettings;

            for (int i = 0; i < bIdxCount; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                var sceneName = System.IO.Path.GetFileNameWithoutExtension(path);

                if (sceneName == name)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void EnsureSceneHashCacheBuilt()
        {
            if (_sceneHashCacheBuilt)
                return;

            _sceneHashCacheBuilt = true;

            var count = SceneManager.sceneCountInBuildSettings;

            for (int i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                var hash = Hash.Hash(path);
                _buildIndexToHash[i] = hash;
                _hashToBuildIndex[hash] = i;
            }
        }

        private static uint ScenePathHashFromBuildIndex(int buildIndex)
        {
            EnsureSceneHashCacheBuilt();
            return _buildIndexToHash[buildIndex];
        }

        private static int BuildIndexFromScenePathHash(uint scenePathHash)
        {
            EnsureSceneHashCacheBuilt();
            return _hashToBuildIndex.GetValueOrDefault(scenePathHash, -1);
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="mode">What UnityEngine scene load mode to use</param>
        public AsyncOperation LoadSceneAsync(int sceneIndex, LoadSceneMode mode = LoadSceneMode.Single)
        {
            var parameters = new LoadSceneParameters(mode);
            return LoadSceneAsync(sceneIndex, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="mode">What UnityEngine scene load mode to use</param>
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            var parameters = new LoadSceneParameters(mode);
            return LoadSceneAsync(idx, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="parameters">The UnityEngine LoadSceneParameters to use</param>
        public AsyncOperation LoadSceneAsync(string sceneName, LoadSceneParameters parameters)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            return LoadSceneAsync(idx, parameters);
        }

        /// <summary>
        /// Loads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">The name of the scene to load</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        public AsyncOperation LoadSceneAsync(string sceneName, PurrSceneSettings settings)
        {
            var idx = SceneNameToBuildIndex(sceneName);

            if (idx == -1)
            {
                PurrLogger.LogError($"Scene {sceneName} not found in build settings");
                return null;
            }

            return LoadSceneAsync(idx, settings);
        }

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="parameters">The UnityEngine LoadSceneParameters to use</param>
        /// <returns></returns>
        public AsyncOperation LoadSceneAsync(int sceneIndex, LoadSceneParameters parameters)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes; for now at least ;)");
                return null;
            }

            return LoadSceneAsync(sceneIndex, new PurrSceneSettings
            {
                mode = parameters.loadSceneMode,
                physicsMode = parameters.localPhysicsMode,
                isPublic = true
            });
        }

        public SceneID lastSceneId => new((ushort)(_nextSceneID - 1));

        /// <summary>
        /// Loads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="sceneIndex">Build index of the scene</param>
        /// <param name="settings">The PurrSceneSettings to use when loading the scene</param>
        /// <returns></returns>
        public AsyncOperation LoadSceneAsync(int sceneIndex, PurrSceneSettings settings)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can load scenes; for now at least ;)");
                return null;
            }

            var idToAssign = GetNextID();
            var parameters = new LoadSceneParameters(settings.mode, settings.physicsMode);

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

                for (int i = 0; i < _rawScenes.Count; i++)
                {
                    bool isDontDestroyOnLoad = IsDontDestroyOnLoadScene(_scenes[_rawScenes[i]].scene);
                    if (!isDontDestroyOnLoad)
                        RemoveScene(_scenes[_rawScenes[i]].scene);
                }
            }

            var scenePathHash = ScenePathHashFromBuildIndex(sceneIndex);

            _history.AddLoadAction(new LoadSceneAction
            {
                scenePathHash = scenePathHash,
                sceneID = idToAssign,
                parameters = settings
            });
            _sceneActionScenes.Add(idToAssign);

            var op = SceneManager.LoadSceneAsync(sceneIndex, parameters);
            var operation = new PendingSceneOperation
            {
                buildIndex = sceneIndex,
                scenePathHash = scenePathHash,
                settings = settings,
                idToAssign = idToAssign,
                operation = op
            };

            _pendingOperations.Add(operation);

            if (_asServer && _networkManager.isHost)
            {
                var clientModule = _networkManager.GetModule<ScenesModule>(false);
                clientModule._pendingOperations.Add(operation);
            }

            return op;
        }

        /// <summary>
        /// Unloads a scene asynchronously by its name - Must be in build settings
        /// </summary>
        /// <param name="sceneName">Name of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(string sceneName, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            var scene = SceneManager.GetSceneByName(sceneName);

            if (!scene.IsValid())
            {
                PurrLogger.LogError($"Scene with name '{sceneName}' not found");
                return null;
            }

            return UnloadSceneAsync(scene, options);
        }

        /// <summary>
        /// Unloads a scene asynchronously by its build index - Must be in build settings
        /// </summary>
        /// <param name="buildIndex">Build index of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(int buildIndex, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            var scene = SceneManager.GetSceneByBuildIndex(buildIndex);

            if (!scene.IsValid())
            {
                PurrLogger.LogError($"Scene with build index {buildIndex} not found");
                return null;
            }

            return UnloadSceneAsync(scene, options);
        }

        /// <summary>
        /// Unloads a scene asynchronously by its Scene object - Must be in build settings
        /// </summary>
        /// <param name="scene">The Scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public AsyncOperation UnloadSceneAsync(Scene scene, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return null;
            }

            if (_networkManager.gameObject.scene == scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return null;
            }

            if (!_idToScene.TryGetValue(scene, out var sceneIndex))
            {
                PurrLogger.LogError($"Scene {scene.name} not found in scenes list");
                return null;
            }

            _history.AddUnloadAction(new UnloadSceneAction { sceneID = sceneIndex, options = options });
#if ADDRESSABLES_PURRNET_SUPPORT
            if (TryUnloadAddressableScene(sceneIndex, options))
                return null;
#endif
            var op = SceneManager.UnloadSceneAsync(scene, options);
            RemoveScene(scene);

            return op;
        }

        /// <summary>
        /// Unloads a scene asynchronously by its SceneID.
        /// Use this when you have the SceneID from onSceneLoaded or sceneStates.
        /// </summary>
        /// <param name="sceneId">The SceneID of the scene to unload</param>
        /// <param name="options">The UnityEngine UnloadSceneOptions to use for the unloading</param>
        public void UnloadSceneAsync(SceneID sceneId, UnloadSceneOptions options = UnloadSceneOptions.None)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("Only server can unload scenes; for now at least ;)");
                return;
            }

            if (!_scenes.TryGetValue(sceneId, out var state))
            {
                PurrLogger.LogError($"Scene with ID {sceneId} not found in scenes list");
                return;
            }

            if (_networkManager.gameObject.scene == state.scene)
            {
                PurrLogger.LogError("Can't unload the network manager scene");
                return;
            }

            UnloadSceneAsync(state.scene, options);
        }

        static readonly List<SceneAction> _playerFilteredActions = new List<SceneAction>();

        private void FilterActionsForPlayer(PlayerID player, IReadOnlyList<SceneAction> actions,
            ICollection<SceneAction> destination)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.LoadAddressable => action.loadAddressableSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (_scenePlayers.IsPlayerInScene(player, target))
                    destination.Add(action);
            }
        }

        private void FilterActionsForPlayerBySceneID(PlayerID player, SceneID id, IReadOnlyList<SceneAction> actions,
            ICollection<SceneAction> destination)
        {
            for (var i = 0; i < actions.Count; i++)
            {
                var action = actions[i];

                var target = action.type switch
                {
                    SceneActionType.Load => action.loadSceneAction.sceneID,
                    SceneActionType.LoadAddressable => action.loadAddressableSceneAction.sceneID,
                    SceneActionType.Unload => action.unloadSceneAction.sceneID,
                    SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                    _ => default
                };

                if (target != id)
                    continue;

                if (_scenePlayers.IsPlayerInScene(player, target))
                    destination.Add(action);
            }
        }

        partial void ProcessCompletedAddressableLoads();
        partial void RebuildAddressableHistoryFromLoadedScenes();

        public void FixedUpdate()
        {
            ProcessCompletedAddressableLoads();
            HandleNextSceneAction();

            if (_history.hasUnflushedActions)
                FlushActions();

            if (_scenesToTriggerUnloadEvent.Count > 0)
            {
                for (var i = 0; i < _scenesToTriggerUnloadEvent.Count; i++)
                {
                    var scene = _scenesToTriggerUnloadEvent[i];
                    PlayUnloadEventsForScene(scene);
                }
                _scenesToTriggerUnloadEvent.Clear();
            }
        }

        private void FlushActions()
        {
            var delta = _history.GetDelta();

            for (var i = 0; i < _players.players.Count; i++)
            {
                var player = _players.players[i];

                _playerFilteredActions.Clear();

                FilterActionsForPlayer(player, delta.actions, _playerFilteredActions);

                if (_playerFilteredActions.Count > 0)
                {
                    _players.Send(player, new SceneActionsBatch { actions = _playerFilteredActions });
                }
            }

            _history.Flush();
        }

        private readonly List<AsyncOperation> _pendingUnloads = new List<AsyncOperation>();
        private CleanupStage _cleanupStage;

        enum CleanupStage
        {
            None,
            Skip,
            LoadEmptyScene,
            WaitOneFrame,
            UnloadScenes,
            UnloadScenesOnly,
            LoadOGScene,
            LoadOGSceneOnly,
            UnloadEmptyScene,
            ResetScene,
            Done
        }

        private Scene? _emptyScene;
        private AsyncOperation _ogSceneLoad;

        public bool Cleanup()
        {
            if (!_wasSetup)
                return true;

            var rules = _networkManager.networkRules;

            if (rules && !rules.ShouldCleanupScenesOnDisconnect())
                return true;

            if (ApplicationContext.isQuitting)
                return true;

            if (!_networkManager.isOffline)
                return true;

            if (_pendingOperations.Count > 0)
                return false;

#if ADDRESSABLES_PURRNET_SUPPORT
            // Addressable loads are driven by their completion callback, but FixedUpdate no longer
            // runs while disconnecting, so drain them here as well. They need to land in _scenes
            // before UnloadAllScenesCleanup runs, otherwise nothing would ever unload them.
            ProcessCompletedAddressableLoads();

            if (_pendingAddressableOperations.Count > 0)
                return false;
#endif

            switch (_cleanupStage)
            {
                case CleanupStage.None:
                    {
                        if (rules.SceneCleanupModeOnDisconnect() == SceneCleanupMode.All)
                        {
                            if (_networkManager.originalSceneBuildIndex == -1)
                            {
                                PurrLogger.LogError("Unable to load original scene on cleanup because its index is invalid");
                                _cleanupStage = CleanupStage.Skip;
                            }
                            else
                                _cleanupStage = CleanupStage.LoadOGSceneOnly;
                        }
                        else
                        {
                            _cleanupStage = _networkManager.IsDontDestroyOnLoad()
                                ? CleanupStage.LoadEmptyScene
                                : CleanupStage.UnloadScenesOnly;
                        }

                        if (_networkManager.TryGetModule(!_asServer, out ScenesModule module) && module._wasSetup)
                            module._cleanupStage = CleanupStage.Skip;

                        return false;
                    }
                case CleanupStage.Skip: return false;
                case CleanupStage.Done: return true;
                case CleanupStage.LoadEmptyScene:
                    {
                        _cleanupStage = CleanupStage.WaitOneFrame;
                        _emptyScene = SceneManager.CreateScene("EmptyScene");
                        return false;
                    }
                case CleanupStage.WaitOneFrame:
                    {
                        _cleanupStage = CleanupStage.UnloadScenes;
                        return false;
                    }
                case CleanupStage.UnloadScenes:
                    {
                        if (UnloadAllScenesCleanup(false))
                            _cleanupStage = CleanupStage.LoadOGScene;
                        return false;
                    }
                case CleanupStage.UnloadScenesOnly:
                    {
                        if (UnloadAllScenesCleanup(true))
                        {
                            if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
                                module._cleanupStage = CleanupStage.Done;
                            _cleanupStage = CleanupStage.Done;
                        }

                        return false;
                    }
                case CleanupStage.LoadOGScene:
                    {
                        if (_ogSceneLoad == null)
                        {
                            if (_networkManager.originalSceneBuildIndex != -1)
                            {
                                _ogSceneLoad = SceneManager.LoadSceneAsync(_networkManager.originalSceneBuildIndex,
                                    LoadSceneMode.Additive);

                                if (_ogSceneLoad != null)
                                    _ogSceneLoad.allowSceneActivation = true;
                            }
                            else
                            {
                                _cleanupStage = CleanupStage.UnloadEmptyScene;
                            }
                        }

                        if (_ogSceneLoad is { isDone: true })
                        {
                            _cleanupStage = CleanupStage.ResetScene;
                        }

                        return false;
                    }
                case CleanupStage.LoadOGSceneOnly:
                    {
                        if (_ogSceneLoad == null)
                        {
                            _ogSceneLoad = SceneManager.LoadSceneAsync(_networkManager.originalSceneBuildIndex);

                            if (_ogSceneLoad != null)
                                _ogSceneLoad.allowSceneActivation = true;
                        }

                        if (_ogSceneLoad is { isDone: true })
                        {
                            _scenes.Clear();

                            if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
                                module._cleanupStage = CleanupStage.Done;

                            _cleanupStage = CleanupStage.Done;
                        }

                        return false;
                    }
                case CleanupStage.ResetScene:
                    {
                        var activeScene = SceneManager.GetSceneByBuildIndex(_networkManager.originalSceneBuildIndex);
                        _networkManager.ResetOriginalScene(activeScene);
                        _cleanupStage = CleanupStage.UnloadEmptyScene;
                        return false;
                    }
                case CleanupStage.UnloadEmptyScene:
                    {
                        if (_emptyScene != null)
                        {
                            if (_emptyScene.Value.IsValid())
                                SceneManager.UnloadSceneAsync(_emptyScene.Value);
                            _emptyScene = null;
                            return false;
                        }

                        if (_networkManager.TryGetModule(!_asServer, out ScenesModule module))
                            module._cleanupStage = CleanupStage.Done;
                        _cleanupStage = CleanupStage.Done;
                        return false;
                    }
                default: return true;
            }
        }

        private bool UnloadAllScenesCleanup(bool keepNetworkManager)
        {
            // unload all scenes that aren't the network manager scene
            if (_scenes.Count > 0)
            {
                _pendingUnloads.Clear();

                foreach (var (id, scene) in _scenes)
                {
                    var unityScene = scene.scene;

                    if (keepNetworkManager && _networkManager.gameObject.scene.handle == unityScene.handle)
                        continue;

                    if (!unityScene.IsValid())
                        continue;

                    if (!unityScene.isLoaded)
                        continue;

                    if (IsDontDestroyOnLoadScene(unityScene))
                        continue;

#if ADDRESSABLES_PURRNET_SUPPORT
                    // Addressable scenes must go through Addressables so the handle is released
                    // as well, instead of leaking next to an unloaded scene.
                    if (TryUnloadAddressableSceneOnCleanup(id))
                        continue;
#endif

                    _pendingUnloads.Add(SceneManager.UnloadSceneAsync(unityScene));
                }

                _scenes.Clear();
            }

            if (_pendingUnloads.Count > 0)
            {
                for (int i = 0; i < _pendingUnloads.Count; i++)
                {
                    if (_pendingUnloads[i] != null && !_pendingUnloads[i].isDone)
                        return false;
                }
            }

#if ADDRESSABLES_PURRNET_SUPPORT
            if (!ArePendingAddressableUnloadsDone())
                return false;
#endif

            return true;
        }

        /// <summary>
        /// Attempts to get the Networked SceneId of a scene
        /// </summary>
        /// <param name="scene">Scene to try and get</param>
        /// <param name="sceneId">Networked SceneID of the scene</param>
        /// <returns>Whether it successfully retrieved a scene or not</returns>
        public bool TryGetSceneID(Scene scene, out SceneID sceneId)
        {
            return _idToScene.TryGetValue(scene, out sceneId);
        }


        /// <summary>
        /// Attempts to get the Networked SceneId of a scene
        /// </summary>
        /// <param name="buildIndex">BuildIndex of Scene to try and get</param>
        /// <param name="sceneId">Networked SceneID of the scene</param>
        /// <returns>Whether it successfully retrieved a scene or not</returns>
        public bool TryGetScene(int buildIndex, out SceneID sceneId)
        {
            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (state.scene.buildIndex == buildIndex)
                    {
                        sceneId = _rawScenes[i];
                        return true;
                    }
                }
            }

            sceneId = default;
            return false;
        }

        /// <summary>
        /// Checks whether a scene is loaded on the network
        /// </summary>
        /// <param name="buildIndex">Build index of scene to check</param>
        /// <returns>Whether the scene is loaded on the network or not</returns>
        public bool IsSceneLoaded(int buildIndex)
        {
            for (int i = 0; i < _rawScenes.Count; i++)
            {
                if (_scenes.TryGetValue(_rawScenes[i], out var state))
                {
                    if (state.scene.buildIndex == buildIndex)
                        return true;
                }
            }

            return false;
        }
    }
}
