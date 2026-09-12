using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet
{
    public struct SpawnPoint
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    public interface IProvideSpawnPoints
    {
        public SpawnPoint NextSpawnPoint(PlayerID player, SceneID scene);
    }

    public interface IProvidePrefabInstantiated
    {
        public void OnPrefabInstantiated(GameObject prefabInstance, PlayerID player, SceneID scene);
    }

    [AddComponentMenu("PurrNet/Player Spawner")]
    public class PlayerSpawner : PurrMonoBehaviour
    {
        [SerializeField] private GameObject _playerPrefab;
        [Tooltip("Even if rules are to not despawn on disconnect, this will ignore that and always spawn a player.")]
        [SerializeField] private bool _ignoreNetworkRules;

        [SerializeField] private List<Transform> spawnPoints = new List<Transform>();
        public IReadOnlyList<Transform> SpawnPoints => spawnPoints;
        private int _currentSpawnPoint;

        private IProvideSpawnPoints _spawnPointProvider;
        private IProvidePrefabInstantiated _prefabInstantiatedProvider;

        /// <summary>
        /// Sets a provider that will be used to provide spawn points for players.
        /// Spawn points lists will be ignored.
        /// </summary>
        public void SetRespawnPointProvider(IProvideSpawnPoints provider)
        {
            _spawnPointProvider = provider;
        }

        /// <summary>
        /// Resets the spawn point provider.
        /// Uses the spawn points list instead.
        /// </summary>
        public void ResetSpawnPointProvider()
        {
            _spawnPointProvider = null;
        }

        /// <summary>
        /// Sets a provider that will be used to notify when a player prefab has been instantiated.
        /// </summary>
        public void SetPrefabInstantiatedProvider(IProvidePrefabInstantiated provider)
        {
            _prefabInstantiatedProvider = provider;
        }

        /// <summary>
        /// Resets the prefab instantiated provider.
        /// </summary>
        public void ResetPrefabInstantiatedProvider()
        {
            _prefabInstantiatedProvider = null;
        }

        private void Awake()
        {
            CleanupSpawnPoints();
        }

        private void CleanupSpawnPoints()
        {
            bool hadNullEntry = false;
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                if (!spawnPoints[i])
                {
                    hadNullEntry = true;
                    spawnPoints.RemoveAt(i);
                    i--;
                }
            }

            if (hadNullEntry)
                PurrLogger.LogWarning($"Some spawn points were invalid and have been cleaned up.", this);
        }

        public override void Subscribe(NetworkManager manager, bool asServer)
        {
            if (asServer && manager.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
            {
                scenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;

                if (!manager.TryGetModule(out ScenesModule scenes, true))
                    return;

                if (!scenes.TryGetSceneID(gameObject.scene, out var sceneID))
                    return;

                if (scenePlayersModule.TryGetPlayersInScene(sceneID, out var players))
                {
                    foreach (var player in players)
                        OnPlayerLoadedScene(player, sceneID, true);
                }
            }
        }

        public override void Unsubscribe(NetworkManager manager, bool asServer)
        {
            if (asServer && manager.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
                scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
        }

        private void OnDestroy()
        {
            if (NetworkManager.main &&
                NetworkManager.main.TryGetModule(out ScenePlayersModule scenePlayersModule, true))
                scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
        }

        private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
        {
            var main = NetworkManager.main;

            if (!main || !main.TryGetModule(out ScenesModule scenes, true))
                return;

            var unityScene = gameObject.scene;

            if (!scenes.TryGetSceneID(unityScene, out var sceneID))
                return;

            if (sceneID != scene)
                return;

            if (!asServer)
                return;

            bool isDestroyOnDisconnectEnabled = main.networkRules.ShouldDespawnOnOwnerDisconnect();
            if (!_ignoreNetworkRules && !isDestroyOnDisconnectEnabled && PlayerOwnsPlayerPrefab(main, player))
                return;

            GameObject newPlayer;
            NetworkIdentity identity;

            CleanupSpawnPoints();

            if (_spawnPointProvider != null)
            {
                var point = _spawnPointProvider.NextSpawnPoint(player, scene);
                newPlayer = SpawnPlayer(point.position, point.rotation, unityScene, out identity);
            }
            else if (spawnPoints.Count > 0)
            {
                var spawnPoint = spawnPoints[_currentSpawnPoint];
                _currentSpawnPoint = (_currentSpawnPoint + 1) % spawnPoints.Count;
                newPlayer = SpawnPlayer(spawnPoint.position, spawnPoint.rotation, unityScene, out identity);
            }
            else
            {
                _playerPrefab.transform.GetPositionAndRotation(out var position, out var rotation);
                newPlayer = SpawnPlayer(position, rotation, unityScene, out identity);
            }

            if (!newPlayer)
                return;

            _prefabInstantiatedProvider?.OnPrefabInstantiated(newPlayer, player, scene);

            if (identity)
                identity.GiveOwnership(player);
        }

        private bool PlayerOwnsPlayerPrefab(NetworkManager main, PlayerID player)
        {
            if (!main.TryGetModule(out GlobalOwnershipModule ownership, true))
                return false;

            if (main.prefabProvider == null || !main.prefabProvider.TryGetPrefabData(_playerPrefab, out var prefabData))
                return false;

            foreach (var owned in ownership.EnumerateAllPlayerOwnedIds(player))
            {
                if (owned && owned.scopedPrefabId == prefabData.prefabId)
                    return true;
            }

            return false;
        }

        private GameObject SpawnPlayer(Vector3 position, Quaternion rotation, Scene unityScene,
            out NetworkIdentity identity)
        {
            identity = null;

            var newPlayer = UnityProxy.Instantiate(_playerPrefab, position, rotation, unityScene);

            if (!newPlayer)
                return null;

            if (!newPlayer.TryGetComponent(out identity))
                return newPlayer;

            var nm = manager ? manager : NetworkManager.main;

            if (nm && !identity.IsSpawned(nm.isServer))
                nm.Spawn(newPlayer);

            return newPlayer;
        }
    }
}
