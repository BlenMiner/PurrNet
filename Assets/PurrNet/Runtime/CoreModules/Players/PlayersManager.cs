using System;
using System.Collections.Generic;
using PurrNet.Authentication;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    [Serializable]
    public struct ServerLoginResponse : IPackedAuto
    {
        public PlayerID playerId { get; }

        public NetworkID lastNidId { get; }

        public string cookie { get; }

        public ServerLoginResponse(PlayerID playerId, NetworkID lastNidId, string cookie = null)
        {
            this.playerId = playerId;
            this.lastNidId = lastNidId;
            this.cookie = cookie;
        }
    }

    [Serializable]
    public struct PlayerJoinedEvent : IPackedAuto
    {
        public PlayerID playerId { get; }

        public Connection connection { get; }

        public NetworkID? lastNidId { get; }

        public string cookie { get; }

        public PlayerJoinedEvent(PlayerID playerId, Connection connection, NetworkID? lastNid, string cookie)
        {
            this.playerId = playerId;
            this.connection = connection;
            this.lastNidId = lastNid;
            this.cookie = cookie;
        }
    }

    [Serializable]
    public struct PlayerLeftEvent : IPackedAuto
    {
        public PlayerID playerId { get; }

        public PlayerLeftEvent(PlayerID playerId)
        {
            this.playerId = playerId;
        }
    }

    [Serializable]
    public struct PlayerSnapshotEvent : IPackedAuto
    {
        public DisposableList<PlayerJoinedEvent> events { get; }

        public PlayerSnapshotEvent(DisposableList<PlayerJoinedEvent> snapshot)
        {
            this.events = snapshot;
        }
    }

    public delegate void OnPlayerJoinedEvent(PlayerID player, bool isReconnect, bool asServer);

    public delegate void OnPlayerLeftEvent(PlayerID player, bool asServer);

    public delegate void OnPlayerEvent(PlayerID player);

    public class PlayersManager : INetworkModule, IConnectionListener, IConnectionStateListener, IPlayerBroadcaster, IPromoteToServerModule, ITransferToNewServer, IPostTransferToNewServer
    {
        private readonly AuthModule _authModule;
        private readonly BroadcastModule _broadcastModule;
        private readonly ITransport _transport;
        private readonly NetworkManager _networkManager;

        private readonly Dictionary<string, PlayerID> _cookieToPlayerId = new Dictionary<string, PlayerID>();
        private readonly Dictionary<PlayerID, string> _playerIdToCookie = new Dictionary<PlayerID, string>();
        private ulong _playerIdCounter;

        private readonly Dictionary<Connection, PlayerID>
            _connectionToPlayerId = new Dictionary<Connection, PlayerID>();

        private readonly Dictionary<PlayerID, Connection> _playerToConnection = new Dictionary<PlayerID, Connection>();

        private readonly List<PlayerID> _players = new List<PlayerID>();
        private readonly HashSet<PlayerID> _allSeenPlayers = new HashSet<PlayerID>();
        private readonly HashSet<int> _promotedStaleConnectionIds = new HashSet<int>();
        private PlayerID? _promotedLocalPlayerId;

        public IReadOnlyList<PlayerID> players => _players;

        public PlayerID? localPlayerId { get; private set; }

        public NetworkID? lastNid { get; private set; }

        public MTUExceededBehaviour mtuExceededBehaviour => _networkManager.mtuExceededBehaviour;

        public int GetMTU(PlayerID player, Channel channel, bool asServer)
        {
            if (!asServer)
            {
                return _networkManager.rawTransport.GetMTU(default, channel, false);
            }

            if (_playerToConnection.TryGetValue(player, out var p))
                return _networkManager.rawTransport.GetMTU(p, channel, true);

            return 500;
        }

        /// <summary>
        /// First callback for whne a new player has joined
        /// </summary>
        public event OnPlayerJoinedEvent onPrePlayerJoined;

        /// <summary>
        /// Callback for when a new player has joined
        /// </summary>
        public event OnPlayerJoinedEvent onPlayerJoined;

        /// <summary>
        /// Last callback for when a new player has joined
        /// </summary>
        public event OnPlayerJoinedEvent onPostPlayerJoined;

        /// <summary>
        /// First callback for when a player has left
        /// </summary>
        public event OnPlayerLeftEvent onPrePlayerLeft;

        /// <summary>
        /// Callback for when a player has left
        /// </summary>
        public event OnPlayerLeftEvent onPlayerLeft;

        /// <summary>
        /// Last callback for when a player has left
        /// </summary>
        public event OnPlayerLeftEvent onPostPlayerLeft;

        /// <summary>
        /// Callback for when the local player has received their PlayerID
        /// </summary>
        public event OnPlayerEvent onLocalPlayerReceivedID;

        public event Action<NetworkID> onNetworkIDReceived;

        private bool _asServer;

        private PlayersBroadcaster _playerBroadcaster;

        internal void SetBroadcaster(PlayersBroadcaster broadcaster)
        {
            _playerBroadcaster = broadcaster;
        }

        public void Send<T>(PlayerID player, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.Send(player, data, method, mtuOverride);

        public void Send<T>(IReadOnlyList<PlayerID> collection, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.Send(collection, data, method, mtuOverride);

        public void SendList<T>(IList<PlayerID> collection, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.Send(collection, data, method, mtuOverride);

        public void Send<T>(IEnumerable<PlayerID> collection, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.Send(collection, data, method, mtuOverride);

        public void SendToServer<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.SendToServer(data, method, mtuOverride);

        public void SendToAll<T>(T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
            => _playerBroadcaster.SendToAll(data, method, mtuOverride);

        public void Unsubscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new()
            => _playerBroadcaster.Unsubscribe(callback);

        public void Subscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new()
            => _playerBroadcaster.Subscribe(callback);

        internal void RegisterImmediateType<T>()
            => _broadcastModule.RegisterImmediateType<T>();

        internal void UnregisterImmediateType<T>()
            => _broadcastModule.UnregisterImmediateType<T>();

        public PlayersManager(NetworkManager nm, AuthModule auth, BroadcastModule broadcaster)
        {
            _networkManager = nm;
            _transport = nm.transport.transport;
            _authModule = auth;
            _broadcastModule = broadcaster;
        }

        /// <summary>
        /// Try to get the connection of a playerId.
        /// For bots, this will always return false.
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="conn"></param>
        /// <returns>The network connection tied to this player</returns>
        public bool TryGetConnection(PlayerID playerId, out Connection conn)
        {
            if (playerId.isBot)
            {
                conn = default;
                return false;
            }

            return _playerToConnection.TryGetValue(playerId, out conn);
        }

        /// <summary>
        /// Check if a playerId is connected to the server.
        /// </summary>
        /// <param name="playerId">PlayerID to check</param>
        /// <returns>Whether the player is connected</returns>
        public bool IsPlayerConnected(PlayerID playerId)
        {
            return _playerToConnection.ContainsKey(playerId);
        }

        /// <summary>
        /// Try to get the playerId of a connection.
        /// </summary>
        public bool TryGetPlayer(Connection conn, out PlayerID playerId)
        {
            return _connectionToPlayerId.TryGetValue(conn, out playerId);
        }

        /// <summary>
        /// Check if a playerId is the local player.
        /// </summary>
        public bool IsLocalPlayer(PlayerID playerId)
        {
            return localPlayerId == playerId;
        }

        /// <summary>
        /// Check if a playerId is the local player.
        /// </summary>
        public bool IsLocalPlayer(PlayerID? playerId)
        {
            return localPlayerId == playerId;
        }

        /// <summary>
        /// Check if a playerId is a valid player.
        /// A valid player is a player that is connected to the server.
        /// </summary>
        public bool IsValidPlayer(PlayerID playerId)
        {
            return _players.Contains(playerId);
        }

        /// <summary>
        /// Check if a playerId is a valid player.
        /// A valid player is a player that is connected to the server.
        /// </summary>
        public bool IsValidPlayer(PlayerID? playerId)
        {
            if (!playerId.HasValue)
                return false;
            return _players.Contains(playerId.Value);
        }

        /// <summary>
        /// Create a new bot player and add it to the connected players list.
        /// </summary>
        /// <returns>The playerId of the new bot player</returns>
        public PlayerID CreateBot()
        {
            if (!_asServer)
                throw new InvalidOperationException("Cannot create a bot from a client.");

            var playerId = new PlayerID(++_playerIdCounter, true);
            if (RegisterPlayer(default, playerId, out var isReconnect))
            {
                SendNewUserToAllClients(default, playerId);
                TriggerOnJoinedEvent(playerId, isReconnect);
            }
            return playerId;
        }

        /// <summary>
        /// Kick a player from the server.
        /// If the user has a connection, it will be closed.
        /// </summary>
        /// <param name="playerId"></param>
        public void KickPlayer(PlayerID playerId)
        {
            if (_playerToConnection.TryGetValue(playerId, out var conn))
                _transport.CloseConnection(conn);
            UnregisterPlayer(playerId);
            SendUserLeftToAllClients(playerId);
        }

        public void PromoteToServerModule()
        {
            _promotedLocalPlayerId = localPlayerId;
            Disable(false);
            _asServer = true;
            Enable(true);

            lastNid = null;
            localPlayerId = null;
        }

        public void TransferToNewServer()
        {
            lastNid = null;
            localPlayerId = null;
            for (var i = _players.Count - 1; i >= 0; i--)
                UnregisterPlayer(_players[i]);
        }

        public void PostTransferToNewServer()
        {
            /*for (var i = _players.Count - 1; i >= 0; i--)
                UnregisterPlayer(_players[i]);*/
        }

        public void PostPromoteToServerModule()
        {
            using var keys = DisposableList<Connection>.Create(_connectionToPlayerId.Keys);
            for (var i = 0; i < keys.Count; i++)
            {
                if (_promotedLocalPlayerId.HasValue &&
                    _connectionToPlayerId.TryGetValue(keys[i], out var playerId) &&
                    playerId == _promotedLocalPlayerId.Value)
                {
                    _connectionToPlayerId.Remove(keys[i]);
                    _playerToConnection.Remove(playerId);
                    _promotedStaleConnectionIds.Add(keys[i].connectionId);
                    continue;
                }

                _networkManager.TriggerConnectionLeft(keys[i], true);
            }

            _connectionToPlayerId.Clear();
            _promotedLocalPlayerId = null;
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;

            if (asServer)
            {
                _authModule.onConnection += OnClientAuthed;
            }
            else
            {
                _broadcastModule.Subscribe<ServerLoginResponse>(OnClientLoginResponse);
                _broadcastModule.Subscribe<PlayerSnapshotEvent>(OnPlayerSnapshotEvent);
                _broadcastModule.Subscribe<PlayerJoinedEvent>(OnPlayerJoinedEvent);
                _broadcastModule.Subscribe<PlayerLeftEvent>(OnPlayerLeftEvent);
            }
        }

        public void Disable(bool asServer)
        {
            if (asServer)
            {
                _authModule.onConnection -= OnClientAuthed;
            }
            else
            {
                _broadcastModule.Unsubscribe<ServerLoginResponse>(OnClientLoginResponse);
                _broadcastModule.Unsubscribe<PlayerSnapshotEvent>(OnPlayerSnapshotEvent);
                _broadcastModule.Unsubscribe<PlayerJoinedEvent>(OnPlayerJoinedEvent);
                _broadcastModule.Unsubscribe<PlayerLeftEvent>(OnPlayerLeftEvent);
            }
        }

        /// <summary>
        /// Try to get the cookie of a playerId.
        /// Good for session management.
        /// </summary>
        public bool TryGetCookie(PlayerID playerId, out string cookie)
        {
            return _playerIdToCookie.TryGetValue(playerId, out cookie);
        }

        private void OnClientAuthed(Connection conn, AuthenticationResponse data)
        {
            if (data.cookie == null || !_cookieToPlayerId.TryGetValue(data.cookie, out var playerId))
            {
                playerId = new PlayerID(++_playerIdCounter, false);

                if (data.cookie != null)
                {
                    _cookieToPlayerId.Add(data.cookie, playerId);
                    _playerIdToCookie.Add(playerId, data.cookie);
                }
            }

            if (_players.Contains(playerId))
            {
                if (_playerToConnection.TryGetValue(playerId, out var oldConn) && oldConn != conn)
                {
                    PurrLogger.LogWarning(
                        "Client reconnected with the cookie of a still-connected player; closing their previous connection.");
                    UnregisterPlayer(playerId);
                    SendUserLeftToAllClients(playerId);
                    _transport.CloseConnection(oldConn);
                }
                else if (_playerToConnection.ContainsKey(playerId))
                {
                    _transport.CloseConnection(conn);
                    PurrLogger.LogError(
                        "Client connected using a cookie from an already connected player; closing their connection.");
                    return;
                }
            }

            var lastNidId = new NetworkID(0, playerId);
            if (_lastNidId.TryGetValue(playerId, out var lastNidRes))
                lastNidId = lastNidRes;

            _broadcastModule.Send(conn, new ServerLoginResponse(playerId, lastNidId, data.cookie));

            SendSnapshotToClient(conn);
            if (IsPlayerConnection(conn, playerId))
            {
                SendNewUserToAllClients(conn, playerId);
                TriggerOnJoinedEvent(playerId, true);
            }
            else if (RegisterPlayer(conn, playerId, out var isReconnect))
            {
                SendNewUserToAllClients(conn, playerId);
                TriggerOnJoinedEvent(playerId, isReconnect);
            }
        }

        private void OnPlayerJoinedEvent(Connection conn, PlayerJoinedEvent data, bool asServer)
        {
            if (RegisterPlayer(data.connection, data.playerId, out var isReconnect))
            {
                if (data.cookie != null)
                {
                    _playerIdToCookie[data.playerId] = data.cookie;
                    _cookieToPlayerId[data.cookie] = data.playerId;
                }

                if (data.lastNidId.HasValue)
                    _lastNidId[data.playerId] = data.lastNidId.Value;

                _playerIdCounter = Math.Max(_playerIdCounter, data.playerId.id.value);

                TriggerOnJoinedEvent(data.playerId, isReconnect);
            }
        }

        private void OnPlayerLeftEvent(Connection conn, PlayerLeftEvent data, bool asServer)
        {
            UnregisterPlayer(data.playerId);
        }

        private void OnPlayerSnapshotEvent(Connection conn, PlayerSnapshotEvent data, bool asServer)
        {
            using (data.events)
            {
                for (var i = 0; i < data.events.Count; i++)
                {
                    var evt = data.events[i];
                    OnPlayerJoinedEvent(conn, evt, asServer);
                }
            }
        }

        private void OnClientLoginResponse(Connection conn, ServerLoginResponse data, bool asServer)
        {
            if (!string.IsNullOrEmpty(data.cookie))
                _authModule.SetClientConnectionCookie(data.cookie);

            localPlayerId = data.playerId;
            lastNid = data.lastNidId;
            onLocalPlayerReceivedID?.Invoke(data.playerId);
            onNetworkIDReceived?.Invoke(data.lastNidId);
        }

        private void SendNewUserToAllClients(Connection conn, PlayerID playerId)
        {
            _broadcastModule.SendToAll(GetPlayerJoinEvent(playerId, conn));
        }

        private PlayerJoinedEvent GetPlayerJoinEvent(PlayerID playerId, Connection conn)
        {
            string cookie = null;
            NetworkID? playerLastNid = null;

            if (_networkManager.networkRules && _networkManager.networkRules.ShouldSharePlayerCookiesWithPeers())
            {
                if (_playerIdToCookie.TryGetValue(playerId, out var playerCookie))
                    cookie = playerCookie;

                if (_lastNidId.TryGetValue(playerId, out var lastNidId))
                    playerLastNid = lastNidId;
            }

            return new PlayerJoinedEvent(playerId, conn, playerLastNid, cookie);
        }

        private void SendUserLeftToAllClients(PlayerID playerId)
        {
            _broadcastModule.SendToAll(new PlayerLeftEvent(playerId));
        }

        private void SendSnapshotToClient(Connection conn)
        {
            using var batch = DisposableList<PlayerJoinedEvent>.Create(_players.Count);
            foreach (var (playerId, playerConn) in _playerToConnection)
                batch.Add(GetPlayerJoinEvent(playerId, playerConn));
            _broadcastModule.Send(conn, new PlayerSnapshotEvent(batch));
        }

        private bool IsPlayerConnection(Connection conn, PlayerID playerId)
        {
            return _connectionToPlayerId.TryGetValue(conn, out var registeredPlayer) &&
                   registeredPlayer == playerId;
        }

        private bool RegisterPlayer(Connection conn, PlayerID player, out bool isReconnect)
        {
            if (_connectionToPlayerId.ContainsKey(conn))
            {
                isReconnect = false;
                return false;
            }

            if (!_players.Contains(player))
                _players.Add(player);

            if (conn.isValid)
            {
                if (_playerToConnection.TryGetValue(player, out var staleConn) && staleConn != conn)
                    _connectionToPlayerId.Remove(staleConn);

                _connectionToPlayerId[conn] = player;
                _playerToConnection[player] = conn;
            }

            isReconnect = !_allSeenPlayers.Add(player);
            return true;
        }

        private void TriggerOnJoinedEvent(PlayerID player, bool isReconnect)
        {
            try
            {
                onPrePlayerJoined?.Invoke(player, isReconnect, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            try
            {
                onPlayerJoined?.Invoke(player, isReconnect, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            try
            {
                onPostPlayerJoined?.Invoke(player, isReconnect, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }
        }

        private void UnregisterPlayer(Connection conn)
        {
            if (!_connectionToPlayerId.TryGetValue(conn, out var playerID))
                return;

            try
            {
                onPrePlayerLeft?.Invoke(playerID, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            _players.Remove(playerID);
            _playerToConnection.Remove(playerID);
            _connectionToPlayerId.Remove(conn);

            try
            {
                onPlayerLeft?.Invoke(playerID, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            try
            {
                onPostPlayerLeft?.Invoke(playerID, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }
        }

        private void UnregisterPlayer(PlayerID playerId)
        {
            try
            {
                onPrePlayerLeft?.Invoke(playerId, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            if (_playerToConnection.TryGetValue(playerId, out var conn))
                _connectionToPlayerId.Remove(conn);
            _players.Remove(playerId);
            _playerToConnection.Remove(playerId);

            try
            {
                onPlayerLeft?.Invoke(playerId, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }

            try
            {
                onPostPlayerLeft?.Invoke(playerId, _asServer);
            }
            catch (Exception e) { PurrLogger.LogException(e); }
        }

        public void OnConnected(Connection conn, bool asServer)
        {
        }

        public void OnConnectionState(ConnectionState state, bool asServer)
        {
            if (!asServer || state != ConnectionState.Disconnected)
                return;

            for (var i = _players.Count - 1; i >= 0; i--)
                UnregisterPlayer(_players[i]);
        }

        public void OnDisconnected(Connection conn, bool asServer)
        {
            if (!asServer) return;

            if (_promotedStaleConnectionIds.Remove(conn.connectionId))
                return;

            if (_connectionToPlayerId.TryGetValue(conn, out var playerId))
                SendUserLeftToAllClients(playerId);

            UnregisterPlayer(conn);
        }

        readonly Dictionary<PlayerID, NetworkID> _lastNidId = new Dictionary<PlayerID, NetworkID>();

        public void RegisterClientLastId(PlayerID player, NetworkID lastNidID)
        {
            _lastNidId[player] = lastNidID;
        }
    }
}
