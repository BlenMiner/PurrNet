using System.Collections.Generic;
using PurrNet.Logging;

namespace PurrNet.Modules
{
    internal struct ClientFinishedLoadingScene
    {
        public SceneID scene;
    }

    public delegate void OnPlayerSceneEvent(PlayerID player, SceneID scene, bool asServer);

    public class ScenePlayersModule : INetworkModule, IPromoteToServerModule
    {
        private readonly Dictionary<SceneID, List<PlayerID>> _scenePlayers = new ();
        private readonly Dictionary<SceneID, List<PlayerID>> _sceneLoadedPlayers = new ();

        readonly ScenesModule _scenes;
        readonly PlayersManager _players;

        /// <summary>
        /// Called once the player has started joining the scene (before loading)
        /// </summary>
        public event OnPlayerSceneEvent onPlayerJoinedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPrePlayerLoadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLoadedScene;

        /// <summary>
        /// Called once the player has finished loading the scene
        /// </summary>
        public event OnPlayerSceneEvent onPostPlayerLoadedScene;

        public event OnPlayerSceneEvent onPlayerLeftScene;
        public event OnPlayerSceneEvent onPlayerUnloadedScene;

        private bool _asServer;

        private readonly NetworkManager _manager;

        public ScenePlayersModule(NetworkManager manager, ScenesModule scenes, PlayersManager players)
        {
            _manager = manager;
            _scenes = scenes;
            _players = players;
        }

        public void PromoteToServerModule()
        {
            Disable(false);
            _asServer = true;
            Enable(true);
        }

        public void PostPromoteToServerModule()
        {
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;

            if (asServer)
            {
                var scenes = _scenes.sceneStates;

                foreach (var (id, sceneState) in scenes)
                {
                    if (sceneState.scene.isLoaded)
                        OnSceneLoaded(id, true);
                }

                _scenes.onSceneLoaded += OnSceneLoaded;
                _scenes.onSceneUnloaded += OnSceneUnloaded;
                _scenes.onSceneVisibilityChanged += OnSceneVisibilityChanged;
                _players.onPlayerJoined += OnPlayerJoined;
                _players.onPlayerLeft += OnPlayerLeft;

                _players.Subscribe<ClientFinishedLoadingScene>(RemoteClientLoadedScene);
            }
            else
            {
                if (_players.localPlayerId.HasValue)
                    OnLocalPlayerReady(_players.localPlayerId.Value);
                else _players.onLocalPlayerReceivedID += OnLocalPlayerReady;

                _scenes.onSceneLoaded += OnClientSceneLoaded;
                _scenes.onSceneUnloaded += OnClientSceneUnloaded;
            }
        }

        private void OnLocalPlayerReady(PlayerID player)
        {
            var scenes = _scenes.sceneStates;

            foreach (var (id, sceneState) in scenes)
            {
                if (sceneState.scene.isLoaded)
                    OnClientSceneLoaded(id, _asServer);
            }

            _players.onLocalPlayerReceivedID -= OnLocalPlayerReady;
        }

        public void Disable(bool asServer)
        {
            if (asServer)
            {
                _scenes.onSceneLoaded -= OnSceneLoaded;
                _scenes.onSceneUnloaded -= OnSceneUnloaded;
                _scenes.onSceneVisibilityChanged -= OnSceneVisibilityChanged;
                _players.onPlayerJoined -= OnPlayerJoined;
                _players.onPlayerLeft -= OnPlayerLeft;

                _players.Unsubscribe<ClientFinishedLoadingScene>(RemoteClientLoadedScene);
            }
            else
            {
                _players.onLocalPlayerReceivedID -= OnLocalPlayerReady;
                _scenes.onSceneLoaded -= OnClientSceneLoaded;
                _scenes.onSceneUnloaded -= OnClientSceneUnloaded;
            }
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            if (!_manager.networkRules.ShouldRemovePlayerFromSceneOnLeave())
            {
                foreach (var (scene, players) in _sceneLoadedPlayers)
                {
                    if (players.Remove(player))
                        onPlayerUnloadedScene?.Invoke(player, scene, _asServer);
                }

                return;
            }

            foreach (var (scene, players) in _scenePlayers)
            {
                if (!players.Contains(player))
                    continue;

                RemovePlayerFromScene(player, scene);
            }
        }

        private void OnClientSceneLoaded(SceneID scene, bool asServer)
        {
            if (!_players.localPlayerId.HasValue)
                return;

            onPrePlayerLoadedScene?.Invoke(_players.localPlayerId.Value, scene, asServer);
            onPlayerLoadedScene?.Invoke(_players.localPlayerId.Value, scene, asServer);
            onPostPlayerLoadedScene?.Invoke(_players.localPlayerId.Value, scene, asServer);

            _players.SendToServer(new ClientFinishedLoadingScene { scene = scene });
        }

        private void OnClientSceneUnloaded(SceneID scene, bool asServer)
        {
            if (!_players.localPlayerId.HasValue)
                return;

            onPlayerLeftScene?.Invoke(_players.localPlayerId.Value, scene, asServer);
            onPlayerUnloadedScene?.Invoke(_players.localPlayerId.Value, scene, asServer);
        }

        private void RemoteClientLoadedScene(PlayerID player, ClientFinishedLoadingScene data, bool asServer)
        {
            if (!_scenePlayers.TryGetValue(data.scene, out var playersInScene))
                return;

            if (!playersInScene.Contains(player))
                return;

            if (_sceneLoadedPlayers.TryGetValue(data.scene, out var loadedPlayers))
            {
                if (loadedPlayers.Contains(player))
                    return;

                loadedPlayers.Add(player);
            }
            else
            {
                PurrLogger.LogError($"SceneID '{data.scene}' not found in scene loaded players dictionary");
            }

            onPrePlayerLoadedScene?.Invoke(player, data.scene, asServer);
            onPlayerLoadedScene?.Invoke(player, data.scene, asServer);
            onPostPlayerLoadedScene?.Invoke(player, data.scene, asServer);
        }

        /// <summary>
        /// Notify bot has loaded in a scene (matches RemoteClientLoadedScene behavior)
        /// </summary>
        private void NotifyBotSceneLoaded(PlayerID bot, SceneID scene)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("NotifyBotSceneLoaded can only be called on server");
                return;
            }

            if (_sceneLoadedPlayers.TryGetValue(scene, out var loadedPlayers))
            {
                if (loadedPlayers.Contains(bot))
                    return;

                loadedPlayers.Add(bot);
            }
            else
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scene loaded players dictionary");
                return;
            }

            onPrePlayerLoadedScene?.Invoke(bot, scene, _asServer);
            onPlayerLoadedScene?.Invoke(bot, scene, _asServer);
            onPostPlayerLoadedScene?.Invoke(bot, scene, _asServer);
        }

        /// <summary>
        /// Get all players that are both part of the scene and have finished loading the scene
        /// </summary>
        public bool TryGetPlayersInScene(SceneID scene, out IReadOnlyList<PlayerID> players)
        {
            if (_sceneLoadedPlayers.TryGetValue(scene, out var data))
            {
                players = data;
                return true;
            }

            players = null;
            return false;
        }

        /// <summary>
        /// Get all players attached to a scene, regardless of whether they have finished loading the scene or not
        /// </summary>
        public bool TryGetPlayersAttachedToScene(SceneID scene, out IReadOnlyList<PlayerID> players)
        {
            if (_scenePlayers.TryGetValue(scene, out var data))
            {
                players = data;
                return true;
            }

            players = null;
            return false;
        }

        private void OnSceneVisibilityChanged(SceneID scene, bool isPublic, bool asServer)
        {
            if (!isPublic) return;

            if (!_scenePlayers.TryGetValue(scene, out var playersInScene))
                return;

            // if the scene is public, add all connected players to the scene
            int connectedPlayersCount = _players.players.Count;

            for (int i = 0; i < connectedPlayersCount; i++)
            {
                var player = _players.players[i];

                if (!playersInScene.Contains(player))
                    playersInScene.Add(player);

                onPlayerJoinedScene?.Invoke(player, scene, asServer);

                if(player.isBot)
                    NotifyBotSceneLoaded(player, scene);
            }
        }

        private void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            for (var i = 0; i < _scenes.scenes.Count; i++)
            {
                var scene = _scenes.scenes[i];

                if (!_scenes.TryGetSceneState(scene, out var state))
                    continue;

                if (!state.settings.isPublic)
                    continue;

                if (!state.scene.IsValid() || !state.scene.isLoaded)
                    continue;

                AddPlayerToScene(player, scene);
            }
        }

        public bool IsPlayerLoadedInScene(PlayerID player, SceneID scene)
        {
            return _sceneLoadedPlayers.TryGetValue(scene, out var playersInScene) && playersInScene.Contains(player);
        }

        public bool IsPlayerInScene(PlayerID player, SceneID scene)
        {
            return _scenePlayers.TryGetValue(scene, out var playersInScene) && playersInScene.Contains(player);
        }

        public IEnumerator<SceneID> GetPlayerScenes(PlayerID player)
        {
            foreach (var (scene, players) in _scenePlayers)
            {
                if (players.Contains(player))
                    yield return scene;
            }
        }

        /// <summary>
        /// Remove the player from all scenes and add them to the new scene
        /// </summary>
        public void MovePlayerToSingleScene(PlayerID player, SceneID scene)
        {
            if (_scenePlayers.TryGetValue(scene, out var playersInScene) && !playersInScene.Contains(player))
                AddPlayerToScene(player, scene);

            foreach (var (existingScene, players) in _scenePlayers)
            {
                if (scene == existingScene)
                    continue;

                if (!players.Contains(player))
                    continue;

                RemovePlayerFromScene(player, existingScene);
            }
        }

        public void AddPlayerToScene(PlayerID player, SceneID scene)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("AddPlayerToScene can only be called on the server; for now ;)");
                return;
            }

            if (!_scenePlayers.TryGetValue(scene, out var playersInScene))
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scenes module; aborting AddPlayerToScene");
                return;
            }

            if (playersInScene.Contains(player))
            {
                if (!IsPlayerLoadedInScene(player, scene))
                {
                    if (CanRestoreLoadedStateWithoutSceneAction(player, scene))
                        MarkPlayerLoadedInScene(player, scene);
                }

                return;
            }

            playersInScene.Add(player);
            onPlayerJoinedScene?.Invoke(player, scene, _asServer);
            if(player.isBot)
                NotifyBotSceneLoaded(player, scene);
        }

        private bool CanRestoreLoadedStateWithoutSceneAction(PlayerID player, SceneID scene)
        {
            if (!_scenes.TryGetSceneState(scene, out var state))
                return false;

            if (!state.settings.isPublic)
                return false;

            if (!state.scene.IsValid() || !state.scene.isLoaded)
                return false;

            if (!player.isBot)
            {
                if (!_manager.isHost || !_manager.isLocalPlayerReady || _manager.localPlayer != player)
                    return false;

                if (!_manager.TryGetModule<ScenesModule>(false, out var clientScenes) ||
                    !clientScenes.TryGetSceneState(scene, out var clientState) ||
                    !clientState.scene.IsValid() || !clientState.scene.isLoaded ||
                    clientState.scene.handle != state.scene.handle)
                    return false;
            }

            if (_manager.gameObject.scene.handle == state.scene.handle)
                return true;

            var originalScene = _manager.originalScene;
            return originalScene.IsValid() && originalScene.handle == state.scene.handle;
        }

        private void MarkPlayerLoadedInScene(PlayerID player, SceneID scene)
        {
            if (!_sceneLoadedPlayers.TryGetValue(scene, out var loadedPlayers))
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scene loaded players dictionary");
                return;
            }

            if (loadedPlayers.Contains(player))
                return;

            loadedPlayers.Add(player);
            onPrePlayerLoadedScene?.Invoke(player, scene, _asServer);
            onPlayerLoadedScene?.Invoke(player, scene, _asServer);
            onPostPlayerLoadedScene?.Invoke(player, scene, _asServer);
        }

        public bool TryGetScenesForPlayer(PlayerID playerId, out SceneID[] scenes)
        {
            var playerScenes = new List<SceneID>();

            foreach (var (scene, players) in _scenePlayers)
            {
                if (players.Contains(playerId))
                    playerScenes.Add(scene);
            }

            if (playerScenes.Count > 0)
            {
                scenes = playerScenes.ToArray();
                return true;
            }

            scenes = null;
            return false;
        }

        public void RemovePlayerFromScene(PlayerID player, SceneID scene)
        {
            if (!_asServer)
            {
                PurrLogger.LogError("RemovePlayerFromScene can only be called on the server; for now ;)");
                return;
            }

            RemovePlayerFromLoadedScene(player, scene);

            if (!_scenePlayers.TryGetValue(scene, out var playersInScene))
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scenes module; aborting RemovePlayerFromScene");
                return;
            }

            if (playersInScene.Remove(player))
                onPlayerLeftScene?.Invoke(player, scene, _asServer);
        }

        private void RemovePlayerFromLoadedScene(PlayerID player, SceneID scene)
        {
            if (!_sceneLoadedPlayers.TryGetValue(scene, out var playersInScene))
            {
                PurrLogger.LogError(
                    $"SceneID '{scene}' not found in scene loaded players dictionary; aborting RemovePlayerFromLoadedScene");
                return;
            }

            if (playersInScene.Remove(player))
                onPlayerUnloadedScene?.Invoke(player, scene, _asServer);
        }

        private void OnSceneLoaded(SceneID scene, bool asServer)
        {
            if (!_scenes.TryGetSceneState(scene, out var state))
            {
                PurrLogger.LogError($"SceneID '{scene}' not found in scenes module");
                return;
            }

            _scenePlayers.Add(scene, new List<PlayerID>());
            _sceneLoadedPlayers.Add(scene, new List<PlayerID>());

            OnSceneVisibilityChanged(scene, state.settings.isPublic, asServer);
        }

        private void OnSceneUnloaded(SceneID scene, bool asServer)
        {
            if (_scenePlayers.TryGetValue(scene, out var playersInScene))
            {
                // remove all players from the scene
                foreach (var player in playersInScene)
                {
                    onPlayerLeftScene?.Invoke(player, scene, asServer);
                    onPlayerUnloadedScene?.Invoke(player, scene, asServer);
                }

                _scenePlayers.Remove(scene);
                _sceneLoadedPlayers.Remove(scene);
            }
        }
    }
}
