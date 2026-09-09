using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public partial class ScenesModule
    {
        private bool _awaitingInitialSceneManifest;
        private Queue<SceneAction> _initialSceneActions;
        private Dictionary<SceneID, Scene> _initialSceneMatches;
        private HashSet<SceneID> _initialBootstrapSceneIds;
        private SceneID? _initialSceneWaitingFor;

        private void ClearInitialSceneReconciliation(bool preservePendingBootstrap = false)
        {
            _initialSceneActions = null;
            _initialSceneMatches = null;
            if (preservePendingBootstrap && _initialBootstrapSceneIds != null)
                _initialBootstrapSceneIds.RemoveWhere(id => !IsScenePending(id));
            else
                _initialBootstrapSceneIds = null;
            _initialSceneWaitingFor = null;
            _awaitingInitialSceneManifest = false;
        }

        private List<SceneAction> GetBootstrapScenesForJoin(PlayerID player)
        {
            var result = new List<SceneAction>();
            foreach (var id in _rawScenes)
            {
                var state = _scenes[id];
                if (_sceneActionScenes.Contains(id) || !state.scene.IsValid() || !state.scene.isLoaded ||
                    (!state.settings.isPublic && !_scenePlayers.IsPlayerInScene(player, id)))
                    continue;

#if ADDRESSABLES_PURRNET_SUPPORT
                if (!IsDontDestroyOnLoadScene(state.scene) && state.scene.buildIndex < 0 &&
                    TryGetBootstrapAddressableAction(id, state, out var addressable))
                {
                    result.Add(addressable);
                    continue;
                }
#endif
                result.Add(new SceneAction
                {
                    type = IsDontDestroyOnLoadScene(state.scene)
                        ? SceneActionType.LoadDontDestroyOnLoad : SceneActionType.Load,
                    loadSceneAction = new LoadSceneAction
                    {
                        sceneID = id,
                        scenePathHash = Hasher.Hash(state.scene.path),
                        parameters = state.settings,
                        loadAdditively = true
                    }
                });
            }
            return result;
        }

        private void BeginInitialSceneReconciliation(FirstSceneActionsBatch manifest)
        {
            _awaitingInitialSceneManifest = false;
            _initialSceneActions = new Queue<SceneAction>();
            _initialSceneMatches = new Dictionary<SceneID, Scene>();
            _initialBootstrapSceneIds = new HashSet<SceneID>();
            _initialSceneWaitingFor = null;
            if (manifest.bootstrapScenes != null)
                foreach (var action in manifest.bootstrapScenes)
                {
                    _initialBootstrapSceneIds.Add(GetInitialSceneID(action));
                    _initialSceneActions.Enqueue(action);
                }
            if (manifest.actions != null)
                foreach (var action in manifest.actions)
                    _initialSceneActions.Enqueue(action);

            // Reserve every existing instance before starting any loads. Duplicate
            // copies of one asset each need a distinct local scene and hierarchy.
            var claimed = new HashSet<Scene>();
            foreach (var pair in _scenes)
                claimed.Add(pair.Value.scene);
            foreach (var action in _initialSceneActions)
            {
                var id = GetInitialSceneID(action);
                if (_scenes.ContainsKey(id) || _initialSceneMatches.ContainsKey(id))
                    continue;
                Scene match = default;
                switch (action.type)
                {
                    case SceneActionType.Load:
                        var index = BuildIndexFromScenePathHash(action.loadSceneAction.scenePathHash);
                        if (index >= 0)
                            match = FindUnclaimedBuildScene(index, action.loadSceneAction.parameters, claimed);
                        break;
                    case SceneActionType.LoadDontDestroyOnLoad:
                        var ddol = GetDontDestroyOnLoadScene();
                        if (!claimed.Contains(ddol))
                            match = ddol;
                        break;
#if ADDRESSABLES_PURRNET_SUPPORT
                    case SceneActionType.LoadAddressable:
                        match = FindUnclaimedAddressableSceneForJoin(action.loadAddressableSceneAction, claimed);
                        break;
#endif
                }
                if (match.IsValid())
                {
                    _initialSceneMatches.Add(id, match);
                    claimed.Add(match);
                }
            }
            ProcessInitialSceneActions();
        }

        private static SceneID GetInitialSceneID(SceneAction action)
        {
            return action.type switch
            {
                SceneActionType.LoadAddressable => action.loadAddressableSceneAction.sceneID,
                SceneActionType.Unload => action.unloadSceneAction.sceneID,
                SceneActionType.SetActive => action.setActiveSceneAction.sceneID,
                _ => action.loadSceneAction.sceneID
            };
        }

        private void ProcessInitialSceneActions()
        {
            if (_initialSceneActions == null)
                return;
            while (_initialSceneActions.Count > 0)
            {
                var action = _initialSceneActions.Peek();
                var id = GetInitialSceneID(action);
                if (_initialSceneWaitingFor.HasValue)
                {
                    if (IsScenePending(_initialSceneWaitingFor.Value))
                        return;
                    if (!_scenes.ContainsKey(_initialSceneWaitingFor.Value))
                    {
                        FailInitialSceneReconciliation($"Scene {id} did not finish loading.");
                        return;
                    }
                    _initialSceneWaitingFor = null;
                }

                if (_scenes.ContainsKey(id))
                {
                    _initialSceneActions.Dequeue();
                    continue;
                }

                if (action.type != SceneActionType.Load && action.type != SceneActionType.LoadAddressable &&
                    action.type != SceneActionType.LoadDontDestroyOnLoad)
                {
                    _initialSceneActions.Dequeue();
                    _actionsQueue.Enqueue(action);
                    continue;
                }

                if (_initialSceneMatches.TryGetValue(id, out var match) && match.IsValid() && match.isLoaded)
                {
                    var settings = action.type == SceneActionType.LoadAddressable
                        ? action.loadAddressableSceneAction.parameters : action.loadSceneAction.parameters;
#if ADDRESSABLES_PURRNET_SUPPORT
                    if (action.type == SceneActionType.LoadAddressable)
                        // Register metadata only; the external loader keeps its handle.
                        RegisterAddressableSceneGuid(action.loadAddressableSceneAction.sceneID,
                            action.loadAddressableSceneAction.guid.value);
#endif
                    RegisterReceivedScene(match, settings, id);
                    _initialSceneActions.Dequeue();
                    continue;
                }

                // Initial history describes scenes that coexist now. Even an old
                // Single action must preserve the client's other matched/local scenes.
                _initialSceneWaitingFor = id;
                if (action.type == SceneActionType.Load)
                {
                    var load = action.loadSceneAction;
                    var index = BuildIndexFromScenePathHash(load.scenePathHash);
                    if (index < 0)
                    {
                        FailInitialSceneReconciliation($"Scene {id} with path hash '{load.scenePathHash}' is not in build settings.");
                        return;
                    }
                    load.loadAdditively = true;
                    try
                    {
                        var operation = SceneManager.LoadSceneAsync(index, load.GetLoadSceneParameters());
                        if (operation == null)
                            throw new InvalidOperationException("Unity did not create a scene load operation.");
                        _pendingOperations.Add(new PendingSceneOperation
                        {
                            buildIndex = index,
                            scenePathHash = load.scenePathHash,
                            idToAssign = id,
                            settings = load.parameters,
                            loadAdditively = true,
                            operation = operation
                        });
                    }
                    catch (Exception e)
                    {
                        FailInitialSceneReconciliation($"Could not load scene {id}: {e.Message}");
                    }
                }
#if ADDRESSABLES_PURRNET_SUPPORT
                else if (action.type == SceneActionType.LoadAddressable)
                {
                    var load = action.loadAddressableSceneAction;
                    load.loadAdditively = true;
                    ProcessLoadAddressableAction(load);
                }
#endif
                else
                    FailInitialSceneReconciliation($"Cannot resolve initial scene {id} ({action.type}).");
                return;
            }
            ClearInitialSceneReconciliation();
        }

        private void RegisterReceivedScene(Scene scene, PurrSceneSettings settings, SceneID id)
        {
            RegisterReceivedSceneProvenance(id);
            // Registration initializes scene-scoped factories before readiness is
            // acknowledged. Advancing the initial queue waits for these callbacks,
            // but does not require every asynchronously spawned object to finish.
            AddScene(scene, settings, id);
        }

        private void RegisterReceivedSceneProvenance(SceneID id)
        {
            if (_initialBootstrapSceneIds?.Remove(id) == true)
                _sceneActionScenes.Remove(id);
            else
                _sceneActionScenes.Add(id);
        }

        private void FailInitialSceneReconciliation(string reason)
        {
            ClearInitialSceneReconciliation();
            _actionsQueue.Clear();
            _awaitingInitialSceneManifest = true;
            PurrLogger.LogError($"Initial scene synchronization failed: {reason}");
            _networkManager.StopClient();
        }

        private static LocalPhysicsMode GetScenePhysicsMode(Scene scene)
        {
            var mode = LocalPhysicsMode.None;
            if (!scene.IsValid())
                return mode;
#if UNITY_PHYSICS_3D
            if (scene.GetPhysicsScene() != Physics.defaultPhysicsScene)
                mode |= LocalPhysicsMode.Physics3D;
#endif
#if UNITY_PHYSICS_2D
            if (scene.GetPhysicsScene2D() != Physics2D.defaultPhysicsScene)
                mode |= LocalPhysicsMode.Physics2D;
#endif
            return mode;
        }

        private static bool HasMatchingScenePhysics(Scene scene, LocalPhysicsMode mode)
        {
            return scene.IsValid() && scene.isLoaded && GetScenePhysicsMode(scene) == mode;
        }
    }
}
