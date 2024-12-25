﻿using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    public struct SpawnPacket : IPackedAuto
    {
        public SceneID sceneId;
        public GameObjectPrototype prototype;
    }
        
    public struct DespawnPacket : IPackedAuto
    {
        public SceneID sceneId;
        public NetworkID parentId;
    }
    
    public class HierarchyV2
    {
        private readonly NetworkManager _manager;
        private readonly bool _asServer;
        private readonly SceneID _sceneId;
        private readonly Scene _scene;
        private readonly ScenePlayersModule _scenePlayers;
        private readonly PlayersManager _playersManager;
        private readonly VisilityV2 _visibility;
        
        private readonly HierarchyPool _scenePool;
        private readonly HierarchyPool _prefabsPool;
        
        private readonly List<NetworkIdentity> _spawnedIdentities = new();
        private readonly HashSet<NetworkIdentity> _sceneObjects = new();
        private readonly Dictionary<NetworkID, NetworkIdentity> _spawnedIdentitiesMap = new();
        
        private int _nextId;

        public bool areSceneObjectsReady { get; private set; }
        
        public HierarchyV2(NetworkManager manager, SceneID sceneId, Scene scene, 
            ScenePlayersModule players, PlayersManager playersManager, bool asServer)
        {
            _manager = manager;
            _sceneId = sceneId;
            _scene = scene;
            _scenePlayers = players;
            _visibility = new VisilityV2(_manager);
            _asServer = asServer;
            _playersManager = playersManager;
            
            _scenePool = NetworkPoolManager.GetScenePool(sceneId);
            _prefabsPool = NetworkPoolManager.GetPool(manager);
            
            SetupSceneObjects(scene);
        }

        private void SetupSceneObjects(Scene scene)
        {
            if (_manager.TryGetModule<HierarchyFactory>(!_asServer, out var factory) &&
                factory.TryGetHierarchy(_sceneId, out var other))
            {
                if (other.areSceneObjectsReady)
                {
                    areSceneObjectsReady = true;
                    return;
                }
            }
            
            if (areSceneObjectsReady)
                return;
            
            var allSceneIdentities = ListPool<NetworkIdentity>.Instantiate();
            SceneObjectsModule.GetSceneIdentities(scene, allSceneIdentities);
            
            var roots = HashSetPool<NetworkIdentity>.Instantiate();

            var count = allSceneIdentities.Count;
            for (int i = 0; i < count; i++)
            {
                var identity = allSceneIdentities[i];
                var root = identity.GetRootIdentity();
                
                if (!roots.Add(root))
                    continue;
                
                var children = ListPool<NetworkIdentity>.Instantiate();
                root.GetComponentsInChildren(true, children);
                
                var cc = children.Count;
                var pid = -i - 2;
                var rootDepth = root.transform.GetTransformDepth();
                
                for (int j = 0; j < cc; j++)
                {
                    var child = children[j];
                    var trs = child.transform;
                    int depth = trs.GetTransformDepth() - rootDepth;
                    child.PreparePrefabInfo(pid, trs.parent ? trs.GetSiblingIndex() : 0, depth, true, true);
                }

                SpawnSceneObject(children);
                ListPool<NetworkIdentity>.Destroy(children);
            }
            
            ListPool<NetworkIdentity>.Destroy(allSceneIdentities);
            areSceneObjectsReady = true;
        }

        public void Enable()
        {
            PurrNetGameObjectUtils.onGameObjectCreated += OnGameObjectCreated;
            _visibility.visibilityChanged += OnVisibilityChanged;
            _scenePlayers.onPrePlayerloadedScene += OnPlayerLoadedScene;
            
            _playersManager.Subscribe<SpawnPacket>(OnSpawnPacket);
            _playersManager.Subscribe<DespawnPacket>(OnDespawnPacket);
        }

        public void Disable()
        {
            PurrNetGameObjectUtils.onGameObjectCreated -= OnGameObjectCreated;
            _visibility.visibilityChanged -= OnVisibilityChanged;
            _scenePlayers.onPrePlayerloadedScene -= OnPlayerLoadedScene;
            
            _playersManager.Unsubscribe<SpawnPacket>(OnSpawnPacket);
            _playersManager.Unsubscribe<DespawnPacket>(OnDespawnPacket);
        }

        private void OnSpawnPacket(PlayerID player, SpawnPacket data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;
            
            // when in host mode, let the server handle the spawning on their module
            if (!_asServer && _manager.isServer)
                return;
            
            var obj = CreatePrototype(data.prototype);
            
            if (!obj)
                return;
            
            PurrLogger.Log($"Spawned object '{obj.name}'", obj);
        }

        private void OnDespawnPacket(PlayerID player, DespawnPacket data, bool asServer)
        {
            if (data.sceneId != _sceneId)
                return;
            
            if (!TryGetIdentity(data.parentId, out var identity))
                return;
            
            Despawn(identity.gameObject);
        }

        private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asserver)
        {
            if (scene != _sceneId)
                return;

            // we default to new players being observers of all scene objects, evaluation will be done later
            foreach (var sceneObj in _sceneObjects)
                sceneObj.TryAddObserver(player);
            
            var roots = HashSetPool<NetworkIdentity>.Instantiate();
            var count = _spawnedIdentities.Count;

            for (var i = 0; i < count; i++)
            {
                var id = _spawnedIdentities[i];
                
                if (!id) continue;
                
                var root = id.GetRootIdentity();
                
                if (!roots.Add(root))
                    continue;
                
                _visibility.RefreshVisibilityForGameObject(player, root.transform);
            }

            HashSetPool<NetworkIdentity>.Destroy(roots);
        }
        
        private void OnVisibilityChanged(PlayerID player, Transform scope, bool isVisible)
        {
            if (isVisible)
            {
                if (HierarchyPool.TryGetPrototype(scope, player, out var prototype))
                {
                    var packet = new SpawnPacket
                    {
                        sceneId = _sceneId,
                        prototype = prototype
                    };

                    _playersManager.Send(player, packet);
                }
                
                return;
            }
            
            if (scope.TryGetComponent<NetworkIdentity>(out var identity) && identity.id.HasValue)
            {
                var packet = new DespawnPacket
                {
                    sceneId = _sceneId,
                    parentId = identity.id.Value
                };
                
                _playersManager.Send(player, packet);
            }
        }

        private void OnGameObjectCreated(GameObject obj, GameObject prefab)
        {
            if (!_asServer && _manager.isServer)
                return;

            if (obj.scene.handle != _scene.handle)
                return;

            if (!_manager.TryGetPrefabData(prefab, out var data, out var idx))
                return;
            
            var rootOffset = obj.transform.GetTransformDepth();

            NetworkManager.SetupPrefabInfo(obj, idx, data.pool, false, -rootOffset);

            Spawn(obj);
        }

        public void Spawn(GameObject gameObject)
        {
            if (!gameObject)
                return;
            
            if (!gameObject.TryGetComponent<NetworkIdentity>(out var id))
            {
                PurrLogger.LogError($"Failed to spawn object '{gameObject.name}'. No NetworkIdentity found.", gameObject);
                return;
            }

            if (id.isSpawned)
                return;
            
            if (!id.HasSpawnAuthority(_manager, !_asServer))
            {
                PurrLogger.LogError($"Spawn failed from for '{gameObject.name}' due to lack of permissions.", gameObject);
                return;
            }

            PlayerID scope = default;
            
            if (!_asServer)
            {
                if (!_playersManager.localPlayerId.HasValue)
                {
                    PurrLogger.LogError($"Failed to spawn object '{gameObject.name}'. No local player id found.", gameObject);
                    return;
                }
                
                scope = _playersManager.localPlayerId.Value;
            }
            
            var baseNid = new NetworkID(_nextId++, scope);
            SetupIdsLocally(id, ref baseNid);

            if (_scenePlayers.TryGetPlayersInScene(_sceneId, out var players))
            {
                foreach (var player in players)
                    _visibility.RefreshVisibilityForGameObject(player, gameObject.transform);
            }
        }
        
        public void Despawn(GameObject gameObject)
        {
            _visibility.ClearVisibilityForGameObject(gameObject.transform);
            
            var children = ListPool<NetworkIdentity>.Instantiate();
            gameObject.GetComponentsInChildren(true, children);

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                
                UnregisterIdentity(child);
                
                if (child.shouldBePooled)
                    child.ResetIdentity();
            }

            ListPool<NetworkIdentity>.Destroy(children);
            
            if (gameObject.TryGetComponent<NetworkIdentity>(out var id) && id.isSceneObject)
            {
                _scenePool.PutBackInPool(gameObject);
            }
            else
            {
                _prefabsPool.PutBackInPool(gameObject);
            }
        }

        private void SetupIdsLocally(NetworkIdentity root, ref NetworkID baseNid)
        {
            using var siblings = new DisposableList<NetworkIdentity>(16);
            root.GetComponents(siblings.list);
            
            // handle root
            for (var i = 0; i < siblings.Count; i++)
            {
                var sibling = siblings[i];
                sibling.SetIdentity(_manager, this, _sceneId, new NetworkID(baseNid, i), _asServer);
                RegisterIdentity(sibling);
            }

            // update next id
            _nextId += siblings.list.Count;
            baseNid = new NetworkID(_nextId, baseNid.scope);
            
            // handle children
            if (root.directChildren == null)
                return;
            
            for (var i = 0; i < root.directChildren.Length; i++)
            {
                SetupIdsLocally(root.directChildren[i], ref baseNid);
            }
        }

        private void SpawnSceneObject(List<NetworkIdentity> children)
        {
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.isSceneObject)
                {
                    var id = new NetworkID(default, _nextId++);
                    child.SetIdentity(_manager, this, _sceneId, id, _asServer);
                    RegisterIdentity(child);
                }
            }
        }
        
        public void PreNetworkMessages()
        {
        }

        public void PostNetworkMessages()
        {
            
        }

        public GameObject CreatePrototype(GameObjectPrototype prototype)
        {
            var pool = prototype.isScenePrototype ? _scenePool : _prefabsPool;

            if (!pool.TryBuildPrototype(prototype, out var result, out var shouldActivate))
            {
                PurrLogger.LogError("Failed to create prototype");
                return null;
            }
            
            result.transform.SetParent(null, false);
            
            SceneManager.MoveGameObjectToScene(result, _scene);
            
            if (shouldActivate)
                result.SetActive(true);

            return result;
        }

        private void RegisterIdentity(NetworkIdentity identity)
        {
            if (identity.id.HasValue)
            {
                _spawnedIdentities.Add(identity);
                _spawnedIdentitiesMap.Add(identity.id.Value, identity);
                
                if (identity.isSceneObject)
                    _sceneObjects.Add(identity);
            }
        }
        
        private void UnregisterIdentity(NetworkIdentity identity)
        {
            if (identity.id.HasValue)
            {
                _spawnedIdentities.Remove(identity);
                _spawnedIdentitiesMap.Remove(identity.id.Value);
                _sceneObjects.Remove(identity);
            }
        }
        
        public bool TryGetIdentity(NetworkID id, out NetworkIdentity identity)
        {
            return _spawnedIdentitiesMap.TryGetValue(id, out identity);
        }
    }
}