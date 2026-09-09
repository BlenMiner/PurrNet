using System;
using System.Collections.Generic;
using PurrNet.Logging;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public class HierarchyFactory : INetworkModule, IFixedUpdate, IPreFixedUpdate, ICleanup, IPromoteToServerModule, ITransferToNewServer
    {
        readonly ScenesModule _scenes;

        readonly NetworkManager _manager;

        readonly ScenePlayersModule _scenePlayersModule;

        readonly Dictionary<SceneID, HierarchyV2> _hierarchies = new();

        readonly List<HierarchyV2> _rawHierarchies = new();

        readonly PlayersManager _playersManager;

        public HierarchyFactory(NetworkManager manager, ScenesModule scenes, ScenePlayersModule scenePlayersModule,
            PlayersManager playersManager)
        {
            _manager = manager;
            _scenes = scenes;
            _scenePlayersModule = scenePlayersModule;
            _playersManager = playersManager;
        }

        readonly List<ValidateSpawnAction> _clientSpawnValidators = new();

        public event ValidateSpawnAction onClientSpawnValidate
        {
            add
            {
                if (value == null)
                    return;

                _clientSpawnValidators.Add(value);
                foreach (var hierarchy in _rawHierarchies)
                    hierarchy.onClientSpawnValidate += value;
            }
            remove
            {
                if (value == null)
                    return;

                for (int i = _clientSpawnValidators.Count - 1; i >= 0; i--)
                {
                    if (_clientSpawnValidators[i] != value)
                        continue;

                    _clientSpawnValidators.RemoveAt(i);
                    break;
                }

                foreach (var hierarchy in _rawHierarchies)
                    hierarchy.onClientSpawnValidate -= value;
            }
        }

        public event IdentityAction onEarlyIdentityAdded;

        public event IdentityAction onIdentityAdded;

        public event IdentityAction onIdentityRemoved;

        public event ObserverAction onObserverAdded;

        public event ObserverAction onLateObserverAdded;

        public event SpawnedAction onSentSpawnPacket;

        public event Action<SceneID> onPreFinishSpawn;

        public void Enable(bool asServer)
        {
            var scenes = _scenes.sceneStates;

            foreach (var (id, sceneState) in scenes)
            {
                if (sceneState.scene.isLoaded)
                    OnPreSceneLoaded(id, asServer);
            }

            _scenes.onPreSceneLoaded += OnPreSceneLoaded;
            _scenes.onSceneUnloaded += OnSceneUnloaded;
        }

        public void Disable(bool asServer)
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].Disable();

            _scenes.onPreSceneLoaded -= OnPreSceneLoaded;
            _scenes.onSceneUnloaded -= OnSceneUnloaded;
        }

        private void OnPreSceneLoaded(SceneID scene, bool asServer)
        {
            if (_hierarchies.ContainsKey(scene))
                return;

            if (!_scenes.TryGetSceneState(scene, out var sceneState))
            {
                PurrLogger.LogError($"Scene {scene} doesn't exist; trying to create hierarchy module for it?");
                return;
            }

            var hierarchy = new HierarchyV2(_manager, scene, sceneState.scene, _scenePlayersModule, _playersManager,
                asServer);

            hierarchy.onEarlyIdentityAdded += OnEarlyIdentityAdded;
            hierarchy.onObserverAdded += OnObserverAdded;
            hierarchy.onLateObserverAdded += OnLateObserverAdded;
            hierarchy.onIdentityAdded += OnIdentityAdded;
            hierarchy.onIdentityRemoved += OnIdentityRemoved;
            hierarchy.onSentSpawnPacket += OnSentSpawnPacket;
            hierarchy.onPreFinishSpawn += OnPreFinishSpawn;

            for (int i = 0; i < _clientSpawnValidators.Count; i++)
                hierarchy.onClientSpawnValidate += _clientSpawnValidators[i];

            hierarchy.Enable();

            _rawHierarchies.Add(hierarchy);
            _hierarchies.Add(scene, hierarchy);
        }

        private void OnSentSpawnPacket(PlayerID player, SceneID scene, NetworkID identity) =>
            onSentSpawnPacket?.Invoke(player, scene, identity);

        private void OnPreFinishSpawn(SceneID scene) => onPreFinishSpawn?.Invoke(scene);

        private void OnLateObserverAdded(PlayerID player, NetworkIdentity identity) =>
            onLateObserverAdded?.Invoke(player, identity);

        private void OnEarlyIdentityAdded(NetworkIdentity identity) =>
            onEarlyIdentityAdded?.Invoke(identity);

        private void OnObserverAdded(PlayerID player, NetworkIdentity identity) =>
            onObserverAdded?.Invoke(player, identity);

        private void OnIdentityAdded(NetworkIdentity identity) =>
            onIdentityAdded?.Invoke(identity);

        private void OnIdentityRemoved(NetworkIdentity identity) =>
            onIdentityRemoved?.Invoke(identity);

        private void OnSceneUnloaded(SceneID scene, bool asserver)
        {
            if (!_hierarchies.TryGetValue(scene, out var hierarchy))
            {
                PurrLogger.LogError($"Hierarchy module for scene {scene} doesn't exist; trying to unload it?");
                return;
            }

            if (_manager.preserveWorldOnTransfer && !asserver)
                hierarchy.DiscardTransferredScene();
            hierarchy.Disable();

            hierarchy.onEarlyIdentityAdded -= OnEarlyIdentityAdded;
            hierarchy.onObserverAdded -= OnObserverAdded;
            hierarchy.onLateObserverAdded -= OnLateObserverAdded;
            hierarchy.onIdentityAdded -= OnIdentityAdded;
            hierarchy.onIdentityRemoved -= OnIdentityRemoved;
            hierarchy.onSentSpawnPacket -= OnSentSpawnPacket;
            hierarchy.onPreFinishSpawn -= OnPreFinishSpawn;

            for (int i = 0; i < _clientSpawnValidators.Count; i++)
                hierarchy.onClientSpawnValidate -= _clientSpawnValidators[i];

            _rawHierarchies.Remove(hierarchy);
            _hierarchies.Remove(scene);
        }

        public void FixedUpdate()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PostNetworkMessages();
        }

        public void PreFixedUpdate()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PreNetworkMessages();
        }

        public bool TryGetHierarchy(SceneID sceneId, out HierarchyV2 o)
        {
            return _hierarchies.TryGetValue(sceneId, out o);
        }

        public bool TryGetHierarchy(Scene scene, out HierarchyV2 o)
        {
            if (_scenes.TryGetSceneID(scene, out var sceneId))
                return _hierarchies.TryGetValue(sceneId, out o);
            o = null;
            return false;
        }

        public bool TryGetIdentity(SceneID scene, NetworkID id, out NetworkIdentity result)
        {
            if (_hierarchies.TryGetValue(scene, out var hierarchy))
                return hierarchy.TryGetIdentity(id, out result);
            result = null;
            return false;
        }

        public bool Cleanup()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
            {
                if (!_rawHierarchies[i].Cleanup())
                    return false;
            }

            return true;
        }

        public void PromoteToServerModule()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PromoteToServerModule();
        }

        public void PostPromoteToServerModule()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].PostPromoteToServerModule();
        }

        public void TransferToNewServer()
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].TransferToNewServer();
        }

        internal bool isTransferComplete
        {
            get
            {
                for (var i = 0; i < _rawHierarchies.Count; i++)
                    if (!_rawHierarchies[i].isTransferComplete)
                        return false;
                return true;
            }
        }

        public void EvaluateVisibilityForPlayer(PlayerID player)
        {
            for (var i = 0; i < _rawHierarchies.Count; i++)
                _rawHierarchies[i].EvaluateVisibilityForPlayer(player);
        }
    }
}
