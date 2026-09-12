using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JamesFrowen.SimpleWeb;
using JetBrains.Annotations;
using LiteNetLib;
using PurrNet.Logging;
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet.Transports
{
    [AddComponentMenu("PurrNet/Transport/Purr Transport")]
    public partial class PurrTransport : GenericTransport, ITransport
    {
        enum SERVER_PACKET_TYPE : byte
        {
            SERVER_CLIENT_CONNECTED = 0,
            SERVER_CLIENT_DISCONNECTED = 1,
            SERVER_CLIENT_DATA = 2,
            SERVER_AUTHENTICATED = 3,
            SERVER_AUTHENTICATION_FAILED = 4,
            SERVER_PIPE_AUTHENTICATED = 5,
            SERVER_NAT_INTRODUCE = 6,
            SERVER_WEBRTC_SIGNAL = 7
        }

        enum HOST_PACKET_TYPE : byte
        {
            SEND_KEEPALIVE = 0,
            SEND_ONE = 1,
            KICK_PLAYER = 2,
            WEBRTC_SIGNAL = 3
        }

        [Serializable, UsedImplicitly]
        private struct ClientAuthenticate
        {
            public string roomName;
            public string clientSecret;
            public bool nat;
            public bool webRtcP2P;
        }

        [Header("Remote Settings")]
        [SerializeField, HideInInspector] private string _masterServer = "https://purrtransport.purrservers.com/";
        [SerializeField, HideInInspector] private string _roomName;
        [SerializeField, HideInInspector] private string _region = "eu-central";
        [SerializeField, HideInInspector] private string _host;

        [Header("Shared Settings")]
        [Tooltip("The amount of time in seconds before socket is disconnected due to no data being received.")]
        [SerializeField, HideInInspector] private float _timeoutInSeconds = 5f;

        // Keep the serialized name so existing scenes and prefab overrides retain their setting.
        [Tooltip("Connect players straight to the host when possible, so game traffic skips the relay. " +
                 "If a direct connection cannot be made, use the relay automatically.")]
        [SerializeField, HideInInspector] private bool _useNat;
        [SerializeField, HideInInspector] private float _natResolveTimeout = 8f;

        [SerializeField, HideInInspector] private NetworkSimulation _networkSimulation = NetworkSimulation.@default;

        public string region
        {
            get => _region;
            [Obsolete("Use SetServer() instead")]
            set => _region = value;
        }

        public string host
        {
            get => _host;
            [Obsolete("Use SetServer() instead")]
            set => _host = value;
        }

        public string masterServer
        {
            get => _masterServer;
            set => _masterServer = value;
        }

        public string roomName
        {
            get => _roomName;
            set => _roomName = value;
        }

        /// <summary>Attempt a direct connection when both players enable it. Configure before connecting.</summary>
        public bool attemptDirectConnection
        {
            get => _useNat;
            set => _useNat = value;
        }

        /// <summary>Compatibility alias for <see cref="attemptDirectConnection"/>.</summary>
        public bool useNat
        {
            get => attemptDirectConnection;
            set => attemptDirectConnection = value;
        }

        /// <summary>Whether a session uses the relay or a direct P2P connection.</summary>
        public enum SessionLink
        {
            /// <summary>No active session.</summary>
            None,
            /// <summary>Waiting for direct connection setup to choose P2P or relay.</summary>
            Resolving,
            /// <summary>Session is running over the relay.</summary>
            Relay,
            /// <summary>Session is running over a direct P2P link.</summary>
            P2P
        }

        public enum ConnectionProtocol
        {
            None,
            UDP,
            WebSocket,
            WebRTC
        }

        /// <summary>Client protocol, or None unless connected.</summary>
        public ConnectionProtocol clientConnectionProtocol => _clientState == ConnectionState.Connected
            ? (clientUsesDirectWebRtc ? ConnectionProtocol.WebRTC
                : GetConnectionProtocol(_clientP2pSession ? _p2pHostPeer : _relayClientPeer, _client))
            : ConnectionProtocol.None;

        /// <summary>Protocol of the local host-to-relay link, or None unless connected.</summary>
        public ConnectionProtocol hostConnectionProtocol => _listenerState == ConnectionState.Connected
            ? GetConnectionProtocol(_relayServerPeer, _server)
            : ConnectionProtocol.None;

        private ConnectionProtocol GetConnectionProtocol(NetPeer udpPeer, PurrWebClient webClient)
        {
            if (_isUsingUDP)
                return udpPeer?.ConnectionState == LiteNetLib.ConnectionState.Connected
                    ? ConnectionProtocol.UDP : ConnectionProtocol.None;
            if (webClient?.ConnectionState != ClientState.Connected)
                return ConnectionProtocol.None;
            return webClient.isWebRtc ? ConnectionProtocol.WebRTC : ConnectionProtocol.WebSocket;
        }

        /// <summary>Which link the local client's session is running over (relay vs direct P2P).</summary>
        public SessionLink clientSessionLink
        {
            get
            {
                if (_clientConnPending || (_webRtcHost != null && !_webRtcHost.decided))
                    return SessionLink.Resolving;
                if (_clientState != ConnectionState.Connected)
                    return SessionLink.None;
                return _clientP2pSession || clientUsesDirectWebRtc ? SessionLink.P2P : SessionLink.Relay;
            }
        }

        /// <summary>Number of host-side connections currently running over a direct P2P link.</summary>
        public int p2pConnectionCount => _p2pSessionConns.Count + WebRtcP2pConnectionCount;

        /// <summary>Remote endpoint of a native UDP P2P link to the host, or null for other routes.</summary>
        public string p2pHostEndpoint =>
            _clientP2pSession && _p2pHostPeer != null ? _p2pHostPeer.ToString() : null;

        /// <summary>Remote endpoint of a host-side native UDP P2P link, or null for other routes.</summary>
        public string GetP2pEndpoint(Connection conn)
        {
            return _p2pSessionConns.Contains(conn.connectionId) &&
                   _p2pPeersByConnId.TryGetValue(conn.connectionId, out var peer)
                ? peer.ToString()
                : null;
        }

        public void SetServer(RelayServer server)
        {
            _region = server.region;
            _host = server.host;
        }

        private bool _hasPreparedHostMigration;
        private RelayServer _preparedHostMigrationServer;
        private HostJoinInfo _preparedHostMigrationJoinInfo;
        private string _preparedHostMigrationRoomName;

        public void PrepareHostMigration(RelayServer server, HostJoinInfo joinInfo, string roomName = null)
        {
            _preparedHostMigrationServer = server;
            _preparedHostMigrationJoinInfo = joinInfo;
            _preparedHostMigrationRoomName = string.IsNullOrWhiteSpace(roomName) ? _roomName : roomName;
            _hasPreparedHostMigration = true;
        }

        public bool hasRegionAndHost => !string.IsNullOrEmpty(_region) && !string.IsNullOrEmpty(_host);

        public NetworkSimulation networkSimulation
        {
            get => _networkSimulation;
            set
            {
                _networkSimulation = value;
                ApplySimulationSettings();
            }
        }

        public override bool isSupported => true;

        public override ITransport transport => this;

        private bool _isPipeMode;
        private int _pipeConnId;

        /// <summary>
        /// This client's connection ID on the relay (pipe mode only).
        /// </summary>
        public int pipeConnId => _pipeConnId;

        /// <summary>
        /// Whether this transport is connected in pipe mode.
        /// </summary>
        public bool isPipeMode => _isPipeMode;

        /// <summary>
        /// Data received from a pipe peer. Args: senderConnId, data.
        /// </summary>
        public event Action<int, ByteData> onPipeDataReceived;

        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        private ConnectionState _listenerState = ConnectionState.Disconnected;
        private ConnectionState _clientState = ConnectionState.Disconnected;

        public bool shouldServerSendKeepAlive => true;

        public bool shouldClientSendKeepAlive => true;

        public ConnectionState listenerState
        {
            get => _listenerState;
            private set
            {
                if (_listenerState == value)
                    return;

                _listenerState = value;
                onConnectionState?.Invoke(value, true);
            }
        }

        public ConnectionState clientState
        {
            get => _clientState;
            private set
            {
                if (_clientState == value)
                    return;

                _clientState = value;
                onConnectionState?.Invoke(value, false);
            }
        }

        public bool SupportsChannel(Channel channel)
        {
            return true;
        }

        public int GetMTU(Connection target, Channel channel, bool asServer)
        {
            if (asServer ? hostUsesWebRtc : clientUsesWebRtc)
                return channel == Channel.ReliableOrdered ? 8192 * 2 : 1024;

            if (_isUsingUDP)
            {
                try
                {
                    var method = UDPTransport.ToDeliveryMethod(channel);
                    var result = asServer ?
                        _udpServer.FirstPeer.GetMaxSinglePacketSize(method) :
                        _udpClient.FirstPeer.GetMaxSinglePacketSize(method);
                    return result - 16; // give the relay some space for metadata
                }
                catch
                {
                    return 1024;
                }
            }

            return 8192 * 2;
        }


        public bool measuresRoundTripTime => _isUsingUDP && !clientUsesDirectWebRtc;

        public int GetRoundTripTime(Connection conn, bool asServer)
        {
            if (asServer ? HostUsesDirectWebRtc(conn.connectionId) : clientUsesDirectWebRtc)
                return -1;
            if (asServer)
                return _p2pPeersByConnId.TryGetValue(conn.connectionId, out var p2p) ? GetRoundTripTime(p2p) : -1;

            var peer = _clientP2pSession && _p2pHostPeer != null ? _p2pHostPeer : _relayClientPeer;
            return GetRoundTripTime(peer);
        }

        /// <summary>
        /// Pinging a PurrTransport joins the room as a throwaway client when a host is running it, which measures
        /// the real link (direct P2P or relay). Without a room it pings the relay of the configured region over HTTP.
        /// </summary>
        protected override async Task<PingResult> PingInternal(string address, ushort port, bool useAddress, float timeoutSeconds, CancellationToken token)
        {
            if (!string.IsNullOrEmpty(_roomName))
            {
                var (checkedRoom, roomExists) = await WithDeadline(PurrTransportUtils.RoomExistsAsync(_masterServer, _roomName), timeoutSeconds);

                if (checkedRoom && roomExists)
                    return await ProbeConnection(address, port, useAddress, timeoutSeconds, token);
            }

            return await PingRelay(timeoutSeconds);
        }

        private async Task<PingResult> PingRelay(float timeoutSeconds)
        {
            try
            {
                var (found, server) = await WithDeadline(FindRelayServer(), timeoutSeconds);

                if (!found)
                    return PingResult.Failed($"Timed out after {timeoutSeconds:0.#}s while looking up relay servers.");

                if (string.IsNullOrEmpty(server.apiEndpoint))
                    return PingResult.Failed("No relay server available.");

                var (pinged, ms) = await WithDeadline(PurrTransportUtils.PingRelayAsync(server), timeoutSeconds);

                if (!pinged)
                    return PingResult.Failed($"Timed out after {timeoutSeconds:0.#}s while pinging relay {server.region}.");

                int rtt = Mathf.RoundToInt(ms);
                return new PingResult(rtt, rtt, $"Relay {server.region} (HTTP)");
            }
            catch (Exception e)
            {
                return PingResult.Failed(e.Message);
            }
        }

        private async Task<RelayServer> FindRelayServer()
        {
            var relayers = await PurrTransportUtils.GetRelayServersAsync(_masterServer);
            var servers = relayers.servers;

            if (servers == null || servers.Length == 0)
                return default;

            if (!string.IsNullOrEmpty(_region))
            {
                for (int i = 0; i < servers.Length; i++)
                {
                    if (servers[i].region == _region)
                        return servers[i];
                }
            }

            return await PurrTransportUtils.ActualGetRelayServerAsync(_masterServer);
        }

        private static async Task<(bool completed, T result)> WithDeadline<T>(Task<T> task, float seconds)
        {
            var winner = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));

            if (winner != task)
                return (false, default);

            return (true, await task);
        }

        /// <summary>Client route and protocol, Connecting while pending, or null when inactive.</summary>
        public string clientLinkDescription
        {
            get
            {
                if (_clientState == ConnectionState.Connecting)
                    return _webRtcHost != null ? "Resolving P2P (WebRTC)"
                        : _clientConnPending ? "Resolving NAT punch (UDP)" : "Connecting";
                var protocol = clientConnectionProtocol;
                if (protocol == ConnectionProtocol.None)
                    return null;
                if (clientUsesDirectWebRtc)
                    return "P2P (WebRTC)";
                if (_clientP2pSession && _p2pHostPeer != null)
                    return $"P2P {_p2pHostPeer} (UDP)";
                if (_isPipeMode)
                    return $"Pipe relay ({protocol})";
                var relay = string.IsNullOrEmpty(_region) ? "Relay" : $"Relay {_region}";
                return $"{relay} ({protocol})";
            }
        }

        /// <summary>Local host-to-relay route and protocol, Connecting while pending, or null when inactive.</summary>
        public string hostLinkDescription
        {
            get
            {
                if (_listenerState == ConnectionState.Connecting)
                    return "Connecting";
                var protocol = hostConnectionProtocol;
                if (protocol == ConnectionProtocol.None)
                    return null;
                var relay = string.IsNullOrEmpty(_region) ? "Relay" : $"Relay {_region}";
                return $"{relay} ({protocol})";
            }
        }

        /// <summary>Round trip time in ms between the local host and the relay, or -1 when not hosting over UDP.</summary>
        public int hostRelayRoundTripTime => GetRoundTripTime(_relayServerPeer);

        /// <summary>Round trip time in ms between the local client and the relay, or -1 when not connected over UDP.</summary>
        public int clientRelayRoundTripTime => GetRoundTripTime(_relayClientPeer);

        private static int GetRoundTripTime(NetPeer peer)
        {
            return peer != null && peer.HasRoundTripTime ? peer.RoundTripTime : -1;
        }

        public IReadOnlyList<Connection> connections => _connections;
        private readonly List<Connection> _connections = new List<Connection>();

        private void Reset()
        {
            _roomName = Guid.NewGuid().ToString().Replace("-", "");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_masterServer == "https://purrbalancer.riten.dev:8080/")
            {
                _masterServer = "https://purrtransport.purrservers.com/";
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.AssetDatabase.SaveAssets();
                UnityEditor.AssetDatabase.Refresh();
            }

            ApplySimulationSettings();
        }
#endif

        readonly List<CancellationTokenSource> _cancellationTokenSourcesServer = new List<CancellationTokenSource>();
        readonly List<CancellationTokenSource> _cancellationTokenSourcesClient = new List<CancellationTokenSource>();

        private void CancelAll(bool asServer)
        {
            var sources = asServer ? _cancellationTokenSourcesServer : _cancellationTokenSourcesClient;
            for (var i = 0; i < sources.Count; i++)
                sources[i].Cancel();
            sources.Clear();
        }

        private void AddCancellation(CancellationTokenSource token, bool asServer)
        {
            if (asServer)
                _cancellationTokenSourcesServer.Add(token);
            else _cancellationTokenSourcesClient.Add(token);
        }

        protected override void StartClientInternal()
        {
            Connect(null, 0);
        }

        private EventBasedNetListener _serverListener;
        private EventBasedNetListener _clientListener;
        private NetManager _udpClient;
        private NetManager _udpServer;

        private bool _isUsingUDP;

        private NetPeer _relayServerPeer;
        private NetPeer _relayClientPeer;
        private NetPeer _p2pHostPeer;

        private EventBasedNatPunchListener _clientNatListener;
        private EventBasedNatPunchListener _serverNatListener;
        private IPEndPoint _relayUdpEndPoint;

        private readonly Dictionary<int, NetPeer> _p2pPeersByConnId = new();
        private readonly Dictionary<NetPeer, int> _connIdByP2pPeer = new();

        private readonly Dictionary<string, PunchSession> _serverPunches = new();
        private PunchSession _clientPunch;
        private readonly List<string> _punchScratch = new();

        private bool _clientConnPending;
        private bool _clientP2pSession;
        private bool _p2pHostEstablished;
        private float _clientConnDeadline;

        private readonly Dictionary<int, float> _pendingHostConns = new();
        private readonly HashSet<int> _p2pSessionConns = new();
        private readonly List<int> _hostConnScratch = new();

        /// <summary>
        /// How long to wait for a NAT punch to produce a usable P2P link before falling back
        /// to relay. Inspector-configurable via <see cref="_natResolveTimeout"/>; clamped to a
        /// sane minimum so a punch always gets a few retries.
        /// </summary>
        private float P2PResolveTimeout => Mathf.Max(1f, _natResolveTimeout);

        /// <summary>NAT punching is only relevant on the (non-pipe) UDP transport.</summary>
        private bool clientNatEnabled => _isUsingUDP && !_isPipeMode && _udpClient?.NatPunchEnabled == true;
        private bool serverNatEnabled => _isUsingUDP && !_isPipeMode && _udpServer?.NatPunchEnabled == true;

        /// <summary>Tracks an in-flight NAT introduce handshake keyed by the relay-issued token.</summary>
        private sealed class PunchSession
        {
            public string token;
            public int clientConnId;
            public float nextSendTime;
            public float deadline;
            public bool done;
        }

        /// <summary>True if a server-side punch handshake is currently expecting the given connId.</summary>
        private bool HasServerPunchFor(int connId)
        {
            foreach (var s in _serverPunches.Values)
                if (s.clientConnId == connId)
                    return true;
            return false;
        }

        /// <summary>Host-side: commit a pending connection to the relay and raise onConnected.</summary>
        private void ResolveHostConnAsRelay(int connId)
        {
            _pendingHostConns.Remove(connId);
            DropP2pPeer(connId);

            var conn = new Connection(connId);
            if (!_connections.Contains(conn))
                _connections.Add(conn);
            onConnected?.Invoke(conn, true);
        }

        /// <summary>Host-side: commit a pending connection to its direct P2P link and raise onConnected.</summary>
        private void ResolveHostConnAsP2p(int connId)
        {
            _pendingHostConns.Remove(connId);
            _p2pSessionConns.Add(connId);

            var conn = new Connection(connId);
            if (!_connections.Contains(conn))
                _connections.Add(conn);
            onConnected?.Invoke(conn, true);
        }

        /// <summary>Drops and disconnects the direct P2P peer for the given connId, if any.</summary>
        private void DropP2pPeer(int connId)
        {
            if (_p2pPeersByConnId.Remove(connId, out var peer))
            {
                _connIdByP2pPeer.Remove(peer);
                peer.Disconnect();
            }
        }

        /// <summary>Client-side: commit the pending session to P2P or relay and raise onConnected.</summary>
        private void ResolveClientConn(bool p2p)
        {
            if (!_clientConnPending)
                return;

            _clientConnPending = false;
            _clientP2pSession = p2p;

            clientState = ConnectionState.Connected;
            onConnected?.Invoke(new Connection(0), false);
        }

        private void OnEnable()
        {
            _serverListener = new EventBasedNetListener();
            _clientListener = new EventBasedNetListener();

            _udpClient = new NetManager(_clientListener)
            {
                UnconnectedMessagesEnabled = true,
                PingInterval = 900,
                AutoRecycle = true,
                EnableStatistics = false,
                IPv6Enabled = false,
                DisconnectTimeout = Mathf.RoundToInt(_timeoutInSeconds * 1000)
            };

            _udpServer = new NetManager(_serverListener)
            {
                UnconnectedMessagesEnabled = true,
                PingInterval = 900,
                AutoRecycle = true,
                EnableStatistics = false,
                IPv6Enabled = false,
                DisconnectTimeout = Mathf.RoundToInt(_timeoutInSeconds * 1000)
            };

            _clientListener.PeerConnectedEvent += OnClientOrPipeConnectedUDP;
            _clientListener.PeerDisconnectedEvent += OnClientOrPipeDisconnectedUDP;
            _clientListener.NetworkReceiveEvent += OnClientOrPipeDataUDP;

            _serverListener.PeerConnectedEvent += OnHostConnectedUDP;
            _serverListener.PeerDisconnectedEvent += OnHostDisconnectedUDP;
            _serverListener.NetworkReceiveEvent += OnHostDataUDP;
            _serverListener.ConnectionRequestEvent += OnServerConnectionRequestUDP;

            _clientNatListener = new EventBasedNatPunchListener();
            _clientNatListener.NatIntroductionSuccess += OnClientNatPunchSuccess;
            _udpClient.NatPunchModule.UnsyncedEvents = false;
            _udpClient.NatPunchModule.Init(_clientNatListener);

            _serverNatListener = new EventBasedNatPunchListener();
            _serverNatListener.NatIntroductionSuccess += OnServerNatPunchSuccess;
            _udpServer.NatPunchModule.UnsyncedEvents = false;
            _udpServer.NatPunchModule.Init(_serverNatListener);

            ApplySimulationSettings();
        }

        private void ApplySimulationSettings()
        {
            bool apply = _networkSimulation.ShouldApply();

            if (_udpClient != null)
            {
                _udpClient.SimulateLatency = apply && _networkSimulation.simulateLatency;
                _udpClient.SimulationMinLatency = _networkSimulation.minLatency;
                _udpClient.SimulationMaxLatency = _networkSimulation.maxLatency;
                _udpClient.SimulatePacketLoss = apply && _networkSimulation.simulatePacketLoss;
                _udpClient.SimulationPacketLossChance = _networkSimulation.packetLossChance;
            }

            if (_udpServer != null)
            {
                _udpServer.SimulateLatency = apply && _networkSimulation.simulateLatency;
                _udpServer.SimulationMinLatency = _networkSimulation.minLatency;
                _udpServer.SimulationMaxLatency = _networkSimulation.maxLatency;
                _udpServer.SimulatePacketLoss = apply && _networkSimulation.simulatePacketLoss;
                _udpServer.SimulationPacketLossChance = _networkSimulation.packetLossChance;
            }
        }

        private void CleanupUdp()
        {
            _udpClient.Stop();
            _udpServer?.Stop();
        }

        private PurrWebClient _server;
        private PurrWebClient _client;
        private readonly WebRtcHostInbox _webRtcHostInbox = new();

        /// <summary>Whether the local host's connection to the relay uses WebRTC.</summary>
        public bool hostUsesWebRtc => _server?.isWebRtc == true;

        /// <summary>Whether the local client's connection to the relay uses WebRTC.</summary>
        public bool clientUsesWebRtc => _client?.isWebRtc == true;
        private HostJoinInfo _hostJoinInfo;
        readonly TcpConfig _tcpConfig = new(noDelay: true, sendTimeout: 0, receiveTimeout: 0);

        protected override void StartServerInternal()
        {
            Listen(0);
        }

        private void OnHostDataUDP(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            var data = new ByteData(reader.RawData, reader.UserDataOffset, reader.UserDataSize);

            // Any non-relay peer is a P2P peer. If it is still mapped, deliver its game
            // data; if it was already dropped (relay won the race, or the session ended)
            // ignore the stale packets — raw P2P bytes must never reach OnHostData, which
            // would misparse them as a relay control frame.
            if (serverNatEnabled && !ReferenceEquals(peer, _relayServerPeer))
            {
                if (_connIdByP2pPeer.TryGetValue(peer, out var p2pConnId))
                {
                    if (_pendingHostConns.ContainsKey(p2pConnId))
                        ResolveHostConnAsP2p(p2pConnId);

                    RaiseDataReceived(new Connection(p2pConnId), data, true);
                }
                return;
            }

            OnHostData(data.segment);
        }

        private void OnHostData(ArraySegment<byte> data)
        {
            if (data.Array == null || data.Count == 0 || listenerState == ConnectionState.Disconnected ||
                listenerState == ConnectionState.Disconnecting)
                return;

            var type = (SERVER_PACKET_TYPE)data.Array[data.Offset];

            switch (type)
            {
                case SERVER_PACKET_TYPE.SERVER_AUTHENTICATED:
                    _hasPreparedHostMigration = false;
                    listenerState = ConnectionState.Connected;
                    break;
                case SERVER_PACKET_TYPE.SERVER_AUTHENTICATION_FAILED:
                    StopListening();
                    break;
                case SERVER_PACKET_TYPE.SERVER_CLIENT_CONNECTED:
                {
                    _packer.ResetPositionAndMode(false);
                    _packer.WriteBytes(new ByteData(data.Array, data.Offset + 1, data.Count - 1));
                    _packer.ResetPositionAndMode(true);

                    int clientId = default;
                    int connectionCount = (data.Count - 1) / 4;

                    for (var i = 0; i < connectionCount; i++)
                    {
                        Packer<int>.Read(_packer, ref clientId);
                        if (_rejectedWebRtcPeers.Contains(clientId))
                            continue;

                        if (serverNatEnabled && !HostUsesDirectWebRtc(clientId) &&
                            (HasServerPunchFor(clientId) || _p2pPeersByConnId.ContainsKey(clientId)))
                        {
                            if (_p2pPeersByConnId.TryGetValue(clientId, out var p2p) &&
                                p2p.ConnectionState == LiteNetLib.ConnectionState.Connected)
                            {
                                ResolveHostConnAsP2p(clientId);
                            }
                            else
                            {
                                _pendingHostConns[clientId] =
                                    Time.realtimeSinceStartup + P2PResolveTimeout;
                            }
                        }
                        else
                        {
                            var conn = new Connection(clientId);
                            var pending = TakePendingHostWebRtcData(clientId);
                            _connections.Add(conn);
                            onConnected?.Invoke(conn, true);
                            if (pending != null)
                            {
                                foreach (var packet in pending)
                                {
                                    if (!_connections.Contains(conn))
                                        break;
                                    RaiseDataReceived(conn, new ByteData(packet), true);
                                }
                            }
                        }
                    }

                    break;
                }
                case SERVER_PACKET_TYPE.SERVER_CLIENT_DISCONNECTED:
                {
                    var subdata = new ByteData(data.Array, data.Offset + 1, data.Count - 1);

                    _packer.ResetPositionAndMode(false);
                    _packer.WriteBytes(subdata);
                    _packer.ResetPositionAndMode(true);

                    int clientId = default;
                    Packer<int>.Read(_packer, ref clientId);

                    var conn = new Connection(clientId);

                    if (hostUsesWebRtc || HostUsesDirectWebRtc(clientId))
                        _webRtcHostInbox.MarkDisconnected(clientId);
                    RemoveHostWebRtcPeer(clientId);
                    _rejectedWebRtcPeers.Remove(clientId);

                    if (_connections.Remove(conn))
                        onDisconnected?.Invoke(conn, DisconnectReason.ClientRequest, true);

                    _pendingHostConns.Remove(clientId);
                    _p2pSessionConns.Remove(clientId);
                    DropP2pPeer(clientId);
                    break;
                }
                case SERVER_PACKET_TYPE.SERVER_CLIENT_DATA:
                {
                    if (data.Count <= 5)
                        return;

                    int connId = data.Array[data.Offset + 1] |
                                 data.Array[data.Offset + 2] << 8 |
                                 data.Array[data.Offset + 3] << 16 |
                                 data.Array[data.Offset + 4] << 24;

                    var conn = new Connection(connId);
                    if (HostUsesDirectWebRtc(connId) || _rejectedWebRtcPeers.Contains(connId))
                        return;
                    var payload = new ArraySegment<byte>(data.Array, data.Offset + 5, data.Count - 5);
                    if (hostUsesWebRtc && !_connections.Contains(conn))
                    {
                        // Data channels can overtake join notifications; buffer reliable data until onConnected.
                        if (_webRtcHostInbox.TryQueue(connId, payload, _server.receivedDeliveryMethod) ==
                            WebRtcHostInbox.QueueResult.Overflow)
                        {
                            PurrLogger.LogError("WebRTC received too much reliable data before client join notifications.");
                            StopListening();
                        }
                        return;
                    }

                    if (serverNatEnabled && _pendingHostConns.ContainsKey(connId))
                        ResolveHostConnAsRelay(connId);

                    RaiseDataReceived(conn, new ByteData(payload), true);
                    break;
                }
                case SERVER_PACKET_TYPE.SERVER_WEBRTC_SIGNAL:
                    HandleWebRtcSignal(data, true);
                    break;
                case SERVER_PACKET_TYPE.SERVER_NAT_INTRODUCE:
                {
                    if (serverNatEnabled && data.Count > 1)
                    {
                        var token = Encoding.UTF8.GetString(data.Array, data.Offset + 1, data.Count - 1);
                        int underscore = token.LastIndexOf('_');
                        if (underscore >= 0 && underscore < token.Length - 1 &&
                            int.TryParse(token.Substring(underscore + 1), out var clientConnId))
                        {
                            BeginPunch(token, true, clientConnId);
                        }
                    }
                    break;
                }
                default:
                    PurrLogger.LogError($"Unexpected packet type {type} from server");
                    break;
            }
        }

        private void OnClientOrPipeConnectedUDP(NetPeer peer)
        {
            if (_isPipeMode)
            {
                OnPipeConnected();
                return;
            }

            if (clientNatEnabled && ReferenceEquals(peer, _p2pHostPeer))
            {
                _clientPunch = null;

                if (_clientConnPending)
                {
                    _p2pHostEstablished = true;
                    ResolveClientConn(true);
                }
                else
                {
                    var stale = _p2pHostPeer;
                    _p2pHostPeer = null;
                    stale.Disconnect();
                }
                return;
            }

            OnClientConnectedUDP(peer);
        }

        private void OnClientOrPipeDisconnectedUDP(NetPeer peer, DisconnectInfo info)
        {
            if (_isPipeMode)
            {
                OnPipeDisconnectedUDP();
                return;
            }

            if (clientNatEnabled && !ReferenceEquals(peer, _relayClientPeer))
            {
                if (!ReferenceEquals(peer, _p2pHostPeer))
                    return;

                _p2pHostPeer = null;

                if (_p2pHostEstablished)
                {
                    _p2pHostEstablished = false;
                    Disconnect();
                }
                else if (_clientConnPending)
                {
                    ResolveClientConn(false);
                }
                return;
            }

            OnClientDisconnectedUDP();
        }

        private void OnClientOrPipeDataUDP(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            var data = reader.GetRemainingBytesSegment();
            if (_isPipeMode)
            {
                OnPipeData(data);
                return;
            }

            // Any non-relay peer is the P2P link. Deliver while it is the live host
            // peer; ignore stale packets from a dropped P2P peer so they never reach
            // OnClientData and get misparsed as a relay control frame.
            if (clientNatEnabled && !ReferenceEquals(peer, _relayClientPeer))
            {
                if (ReferenceEquals(peer, _p2pHostPeer))
                {
                    if (_clientConnPending)
                    {
                        _p2pHostEstablished = true;
                        ResolveClientConn(true);
                    }

                    if (clientState == ConnectionState.Connected)
                        RaiseDataReceived(new Connection(0), new ByteData(data.Array, data.Offset, data.Count), false);
                }
                return;
            }

            OnClientData(data);
        }

        private void OnClientData(ArraySegment<byte> data)
        {
            if (data.Array == null || data.Count == 0 || clientState == ConnectionState.Disconnected ||
                clientState == ConnectionState.Disconnecting)
                return;

            if (_clientConnPending)
                ResolveClientConn(false);

            if (clientState == ConnectionState.Connected)
            {
                if (clientUsesDirectWebRtc)
                    return;
                var bdata = new ByteData(data.Array, data.Offset, data.Count);
                RaiseDataReceived(new Connection(0), bdata, false);
                return;
            }

            var type = (SERVER_PACKET_TYPE)data.Array[data.Offset];

            switch (type)
            {
                case SERVER_PACKET_TYPE.SERVER_AUTHENTICATED:
                    if (clientNatEnabled && !clientUsesDirectWebRtc && _clientPunch != null && !isPinging)
                    {
                        _clientConnPending = true;
                        _clientConnDeadline = Time.realtimeSinceStartup + P2PResolveTimeout;
                    }
                    else
                    {
                        clientState = ConnectionState.Connected;
                        onConnected?.Invoke(new Connection(0), false);
                        if (clientUsesDirectWebRtc)
                            DeliverPendingWebRtcClientData();
                    }
                    break;
                case SERVER_PACKET_TYPE.SERVER_AUTHENTICATION_FAILED:
                    Disconnect();
                    break;
                case SERVER_PACKET_TYPE.SERVER_WEBRTC_SIGNAL:
                    HandleWebRtcSignal(data, false);
                    break;
                case SERVER_PACKET_TYPE.SERVER_NAT_INTRODUCE:
                    if (clientNatEnabled && data.Count > 1)
                    {
                        var token = Encoding.UTF8.GetString(data.Array, data.Offset + 1, data.Count - 1);
                        BeginPunch(token, false, 0);
                    }
                    break;
                default:
                    PurrLogger.LogError($"Unexpected packet type {type} from server");
                    break;
            }
        }

        private void OnHostConnected()
        {
            var authenticate = new ClientAuthenticate()
            {
                roomName = _roomName,
                clientSecret = _hostJoinInfo.secret,
                webRtcP2P = webRtcP2PAvailable && hostUsesWebRtc
            };

            string json = JsonUtility.ToJson(authenticate);
            var data = Encoding.UTF8.GetBytes(json);

            _server.Send(data);
        }

        private void OnHostConnectedUDP(NetPeer peer)
        {
            if (serverNatEnabled && !ReferenceEquals(peer, _relayServerPeer))
            {
                if (_connIdByP2pPeer.TryGetValue(peer, out var p2pConnId))
                {
                    if (_pendingHostConns.ContainsKey(p2pConnId))
                        ResolveHostConnAsP2p(p2pConnId);
                    else if (!_p2pSessionConns.Contains(p2pConnId) &&
                             _connections.Contains(new Connection(p2pConnId)))
                        DropP2pPeer(p2pConnId);
                }
                return;
            }

            var authenticate = new ClientAuthenticate()
            {
                roomName = _roomName,
                clientSecret = _hostJoinInfo.secret,
                nat = serverNatEnabled,
                webRtcP2P = webRtcP2PAvailable
            };

            string json = JsonUtility.ToJson(authenticate);
            var data = Encoding.UTF8.GetBytes(json);

            peer.Send(data, DeliveryMethod.ReliableOrdered);
        }

        private void OnClientConnected()
        {
            var authenticate = new ClientAuthenticate()
            {
                roomName = _roomName,
                clientSecret = _clientJoinInfo.secret,
                webRtcP2P = webRtcP2PAvailable && clientUsesWebRtc
            };

            string json = JsonUtility.ToJson(authenticate);
            var data = Encoding.UTF8.GetBytes(json);

            _client.Send(data);
        }

        private void OnClientConnectedUDP(NetPeer peer)
        {
            var authenticate = new ClientAuthenticate
            {
                roomName = _roomName,
                clientSecret = _clientJoinInfo.secret,
                nat = clientNatEnabled,
                webRtcP2P = webRtcP2PAvailable
            };

            string json = JsonUtility.ToJson(authenticate);
            var data = Encoding.UTF8.GetBytes(json);

            peer.Send(data, DeliveryMethod.ReliableOrdered);
        }

        private void OnHostDisconnectedUDP(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (serverNatEnabled && !ReferenceEquals(peer, _relayServerPeer))
            {
                if (!_connIdByP2pPeer.Remove(peer, out var p2pConnId))
                    return;

                _p2pPeersByConnId.Remove(p2pConnId);

                if (_p2pSessionConns.Remove(p2pConnId))
                {
                    var conn = new Connection(p2pConnId);
                    if (_connections.Remove(conn))
                        onDisconnected?.Invoke(conn, DisconnectReason.Timeout, true);
                    CloseConnection(conn);
                }
                else if (_pendingHostConns.ContainsKey(p2pConnId))
                {
                    ResolveHostConnAsRelay(p2pConnId);
                }
                return;
            }

            StopListening();
        }

        private void OnHostDisconnected()
        {
            StopListening();
        }

        private void OnClientDisconnected()
        {
            Disconnect();
        }

        private void OnClientDisconnectedUDP()
        {
            Disconnect();
        }

        public async void Listen(ushort port)
        {
            try
            {
                if (listenerState != ConnectionState.Disconnected)
                    StopListening();

                listenerState = ConnectionState.Connecting;

                _server = PurrWebClient.Create(ushort.MaxValue, 5000, _tcpConfig);

                _server.onConnect += OnHostConnected;
                _server.onData += OnHostData;
                _server.onDisconnect += OnHostDisconnected;
                _server.onError += OnError;

                try
                {
                    var token = new CancellationTokenSource();
                    AddCancellation(token, true);

                    if (_hasPreparedHostMigration)
                    {
                        if (!string.IsNullOrWhiteSpace(_preparedHostMigrationRoomName))
                            _roomName = _preparedHostMigrationRoomName;

                        SetServer(_preparedHostMigrationServer);
                        _hostJoinInfo = _preparedHostMigrationJoinInfo;
                    }
                    else
                    {
                        if (!hasRegionAndHost)
                        {
                            var relayServer = await PurrTransportUtils.GetRelayServerAsync(_masterServer, token);
                            _region = relayServer.region;
                            _host = relayServer.host;
                        }

                        if (token.IsCancellationRequested)
                            return;

                        _hostJoinInfo = await PurrTransportUtils.Alloc(_masterServer, _region, _roomName, token);
                    }

                    if (token.IsCancellationRequested)
                        return;

                    if (Application.platform != RuntimePlatform.WebGLPlayer)
                    {
                        _isUsingUDP = true;

                        _udpServer.NatPunchEnabled = attemptDirectConnection;
                        _udpServer.StartInManualMode(0);
                        var addresses = await Dns.GetHostAddressesAsync(_host);
                        var ipv4 = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                                   ?? IPAddress.Any;
                        _relayUdpEndPoint = new IPEndPoint(ipv4, _hostJoinInfo.udpPortV2);
                        _relayServerPeer = _udpServer.Connect(_relayUdpEndPoint, "PurrNet");
                    }
                    else
                    {
                        _isUsingUDP = false;
                        var builder = new UriBuilder
                        {
                            Scheme = _hostJoinInfo.ssl ? "wss" : "ws",
                            Host = _host,
                            Port = _hostJoinInfo.port,
                            Query = string.Empty,
                            Path = string.Empty
                        };

                        _server.Connect(builder.Uri, _hostJoinInfo.webRtcUrl);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    StopListening();
                    PurrLogger.LogException(e);
                }
            }
            catch (Exception e)
            {
                StopListening();
                PurrLogger.LogException(e.Message);
            }
        }

        private static void OnError(Exception obj)
        {
            PurrLogger.LogException(obj);
        }

        public void StopListening()
        {
            ClearWebRtcPeers(true);
            _connections.Clear();
            _webRtcHostInbox.Clear();
            CancelAll(true);

            if (_server != null)
            {
                _server.onConnect -= OnHostConnected;
                _server.onData -= OnHostData;
                _server.onDisconnect -= OnHostDisconnected;
                _server.onError -= OnError;
                _server.Disconnect();
            }

            _udpServer?.Stop();

            _server = null;
            _relayServerPeer = null;
            _p2pPeersByConnId.Clear();
            _connIdByP2pPeer.Clear();
            _serverPunches.Clear();
            _pendingHostConns.Clear();
            _p2pSessionConns.Clear();

            if (listenerState is ConnectionState.Connecting or ConnectionState.Connected)
                listenerState = ConnectionState.Disconnecting;
            listenerState = ConnectionState.Disconnected;
        }

        public void Disconnect()
        {
            ClearWebRtcPeers(false);
            if (clientState != ConnectionState.Disconnected)
                onDisconnected?.Invoke(default, DisconnectReason.ClientRequest, false);

            CancelAll(false);

            if (_client != null)
            {
                _client.onConnect -= OnClientConnected;
                _client.onData -= OnClientData;
                _client.onDisconnect -= OnClientDisconnected;
                _client.onError -= OnError;
                _client.Disconnect();
            }

            _udpClient?.Stop();

            _client = null;
            _relayClientPeer = null;
            _p2pHostPeer = null;
            _clientPunch = null;
            _clientConnPending = false;
            _clientP2pSession = false;
            _p2pHostEstablished = false;

            if (clientState is ConnectionState.Connecting or ConnectionState.Connected)
                clientState = ConnectionState.Disconnecting;
            clientState = ConnectionState.Disconnected;
        }

        private ClientJoinInfo _clientJoinInfo;

        public async void Connect(string ip, ushort port)
        {
            try
            {
                if (clientState != ConnectionState.Disconnected)
                    Disconnect();

                clientState = ConnectionState.Connecting;

                var token = new CancellationTokenSource();

                while (listenerState == ConnectionState.Connecting)
                    await UnityLatestUpdate.Yield();

                if (token.IsCancellationRequested)
                    return;

                _client = PurrWebClient.Create(ushort.MaxValue, 5000, _tcpConfig);
                _client.onConnect += OnClientConnected;
                _client.onData += OnClientData;
                _client.onDisconnect += OnClientDisconnected;
                _client.onError += OnError;

                AddCancellation(token, false);

                _clientJoinInfo = await PurrTransportUtils.Join(_masterServer, _roomName, token);

                if (token.IsCancellationRequested)
                    return;

                if (Application.platform != RuntimePlatform.WebGLPlayer)
                {
                    _isUsingUDP = true;
                    _udpClient.NatPunchEnabled = attemptDirectConnection;
                    _udpClient.StartInManualMode(0);

                    var addresses = await Dns.GetHostAddressesAsync(_clientJoinInfo.host);
                    var ipv4 = addresses.FirstOrDefault(ipArd => ipArd.AddressFamily == AddressFamily.InterNetwork)
                               ?? IPAddress.Any;

                    _relayUdpEndPoint = new IPEndPoint(ipv4, _clientJoinInfo.udpPortV2);
                    _relayClientPeer = _udpClient.Connect(_relayUdpEndPoint, "PurrNet");
                }
                else
                {
                    _isUsingUDP = false;
                    var builder = new UriBuilder
                    {
                        Scheme = _clientJoinInfo.ssl ? "wss" : "ws",
                        Host = _clientJoinInfo.host,
                        Port = _clientJoinInfo.port
                    };

                    _client.Connect(builder.Uri, _clientJoinInfo.webRtcUrl);
                }
            }
            catch (OperationCanceledException)
            {
                Disconnect();
            }
            catch (Exception e)
            {
                Disconnect();
                PurrLogger.LogException(e.Message);
            }
        }

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
        {
            onDataReceived?.Invoke(conn, data, asServer);
        }

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
        {
            onDataSent?.Invoke(conn, data, asServer);
        }

        static readonly BitPacker _packer = new BitPacker();

        public void SendServerKeepAlive()
        {
            if (_server == null)
                return;

            _packer.ResetPositionAndMode(false);
            Packer<byte>.Write(_packer, (byte)HOST_PACKET_TYPE.SEND_KEEPALIVE);
            var data = _packer.ToByteData();
            _server.Send(new ArraySegment<byte>(data.data, data.offset, data.length));
        }

        public void SendToClient(Connection target, ByteData odata, Channel method = Channel.ReliableOrdered)
        {
            if (listenerState != ConnectionState.Connected)
                return;

            if (!target.isValid)
                return;

            if (HostUsesDirectWebRtc(target.connectionId))
            {
                var session = _webRtcPeers[target.connectionId];
                if (!session.peer.isConnected)
                {
                    FailWebRtcSession(session);
                    return;
                }
                session.peer.Send(odata.segment, (byte)UDPTransport.ToDeliveryMethod(method));
                RaiseDataSent(target, odata, true);
                return;
            }

            if (serverNatEnabled && _p2pSessionConns.Contains(target.connectionId))
            {
                if (_p2pPeersByConnId.TryGetValue(target.connectionId, out var p2pPeer))
                    p2pPeer.Send(odata.data, odata.offset, odata.length, UDPTransport.ToDeliveryMethod(method));
                RaiseDataSent(target, odata, true);
                return;
            }

            _packer.ResetPositionAndMode(false);

            Packer<byte>.Write(_packer, (byte)HOST_PACKET_TYPE.SEND_ONE);
            Packer<int>.Write(_packer, target.connectionId);

            if (_isUsingUDP || hostUsesWebRtc)
                Packer<byte>.Write(_packer, (byte)UDPTransport.ToDeliveryMethod(method));

            _packer.WriteBytes(odata);

            var data = _packer.ToByteData();

            if (_isUsingUDP)
            {
                var deliveryMethod = UDPTransport.ToDeliveryMethod(method);
                _relayServerPeer.Send(data.data, data.offset, data.length, deliveryMethod);
            }
            else _server.Send(data.segment, (byte)UDPTransport.ToDeliveryMethod(method));
            RaiseDataSent(target, data, true);
        }

        public void SendToServer(ByteData data, Channel method = Channel.ReliableOrdered)
        {
            if (clientState != ConnectionState.Connected)
                return;

            if (clientUsesDirectWebRtc)
            {
                var session = _webRtcHost;
                if (!session.peer.isConnected)
                {
                    FailWebRtcSession(session);
                    return;
                }
                session.peer.Send(data.segment, (byte)UDPTransport.ToDeliveryMethod(method));
                RaiseDataSent(default, data, false);
                return;
            }

            if (_isUsingUDP || clientUsesWebRtc)
            {
                var deliveryMethod = UDPTransport.ToDeliveryMethod(method);

                if (clientNatEnabled && _clientP2pSession)
                {
                    if (_p2pHostPeer != null)
                        _p2pHostPeer.Send(data.data, data.offset, data.length, deliveryMethod);
                    RaiseDataSent(default, data, false);
                    return;
                }

                _packer.ResetPositionAndMode(false);
                Packer<byte>.Write(_packer, (byte)deliveryMethod);
                _packer.WriteBytes(data);
                var byteData = _packer.ToByteData();
                if (_isUsingUDP)
                    _relayClientPeer.Send(byteData.data, byteData.offset, byteData.length, deliveryMethod);
                else
                    _client.Send(byteData.segment, (byte)deliveryMethod);
            }
            else
            {
                _client.Send(new ArraySegment<byte>(data.data, data.offset, data.length));
            }

            RaiseDataSent(default, data, false);
        }

        /// <summary>
        /// Connect to a relay server in pipe mode. No rooms, no host — just
        /// connId-based forwarding. Use SendPipeData to send to specific peers.
        /// </summary>
        public void ConnectAsPipe(string relayHost, int udpPort, int wsPort)
        {
            ConnectAsPipe(relayHost, udpPort, wsPort, null);
        }

        /// <summary>Connect in pipe mode with optional WebRTC and WebSocket fallback for browsers.</summary>
        public async void ConnectAsPipe(string relayHost, int udpPort, int wsPort, string webRtcUrl)
        {
            try
            {
                if (_isPipeMode && clientState != ConnectionState.Disconnected)
                    DisconnectPipe();

                _isPipeMode = true;
                clientState = ConnectionState.Connecting;

                var token = new CancellationTokenSource();
                AddCancellation(token, false);

                _client = PurrWebClient.Create(ushort.MaxValue, 5000, _tcpConfig);
                _client.onConnect += OnPipeConnected;
                _client.onData += OnPipeData;
                _client.onDisconnect += OnPipeDisconnected;
                _client.onError += OnError;

                if (Application.platform != RuntimePlatform.WebGLPlayer)
                {
                    _isUsingUDP = true;
                    _udpClient.NatPunchEnabled = false;
                    _udpClient.StartInManualMode(0);

                    var addresses = await Dns.GetHostAddressesAsync(relayHost);
                    var ipv4 = addresses.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                               ?? IPAddress.Any;

                    if (token.IsCancellationRequested)
                        return;

                    _relayUdpEndPoint = new IPEndPoint(ipv4, udpPort);
                    _relayClientPeer = _udpClient.Connect(_relayUdpEndPoint, "PurrNet");
                }
                else
                {
                    _isUsingUDP = false;
                    var builder = new UriBuilder
                    {
                        Scheme = Uri.TryCreate(webRtcUrl, UriKind.Absolute, out var rtcUri) &&
                                 rtcUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
                        Host = relayHost,
                        Port = wsPort
                    };
                    _client.Connect(builder.Uri, webRtcUrl);
                }
            }
            catch (Exception e)
            {
                DisconnectPipe();
                PurrLogger.LogException(e.Message);
            }
        }

        private void OnPipeConnected()
        {
            string json = "{\"pipe\":true}";
            var data = Encoding.UTF8.GetBytes(json);

            if (_isUsingUDP)
                _udpClient.SendToAll(data, DeliveryMethod.ReliableOrdered);
            else
                _client.Send(data);
        }

        private void OnPipeData(ArraySegment<byte> data)
        {
            if (data.Array == null || data.Count == 0)
                return;

            if (clientState != ConnectionState.Connected)
            {
                var type = (SERVER_PACKET_TYPE)data.Array[data.Offset];

                if (type == SERVER_PACKET_TYPE.SERVER_PIPE_AUTHENTICATED && data.Count >= 5)
                {
                    _pipeConnId = data.Array[data.Offset + 1]
                                | data.Array[data.Offset + 2] << 8
                                | data.Array[data.Offset + 3] << 16
                                | data.Array[data.Offset + 4] << 24;

                    clientState = ConnectionState.Connected;
                    onConnected?.Invoke(new Connection(_pipeConnId), false);
                }
                else if (type == SERVER_PACKET_TYPE.SERVER_AUTHENTICATION_FAILED)
                {
                    DisconnectPipe();
                }
            }
            else
            {
                if (data.Count < 5) return;

                int senderConnId = data.Array[data.Offset]
                                 | data.Array[data.Offset + 1] << 8
                                 | data.Array[data.Offset + 2] << 16
                                 | data.Array[data.Offset + 3] << 24;

                var payload = new ByteData(data.Array, data.Offset + 4, data.Count - 4);
                onPipeDataReceived?.Invoke(senderConnId, payload);
            }
        }

        private void OnPipeDisconnected()
        {
            if (clientState != ConnectionState.Disconnected)
            {
                clientState = ConnectionState.Disconnecting;
                onDisconnected?.Invoke(new Connection(_pipeConnId), DisconnectReason.ServerRequest, false);
                clientState = ConnectionState.Disconnected;
            }
        }

        private void OnPipeDisconnectedUDP()
        {
            OnPipeDisconnected();
        }

        /// <summary>
        /// Send data to a specific peer via the relay (pipe mode only).
        /// </summary>
        public void SendPipeData(int targetConnId, ByteData data, Channel channel = Channel.ReliableOrdered)
        {
            if (!_isPipeMode || clientState != ConnectionState.Connected)
                return;

            _packer.ResetPositionAndMode(false);

            if (_isUsingUDP || clientUsesWebRtc)
                Packer<byte>.Write(_packer, (byte)UDPTransport.ToDeliveryMethod(channel));

            Packer<int>.Write(_packer, targetConnId);
            _packer.WriteBytes(data);

            var byteData = _packer.ToByteData();

            if (_isUsingUDP)
            {
                var deliveryMethod = UDPTransport.ToDeliveryMethod(channel);
                _udpClient.SendToAll(byteData.data, byteData.offset, byteData.length, deliveryMethod);
            }
            else
            {
                _client.Send(byteData.segment, (byte)UDPTransport.ToDeliveryMethod(channel));
            }
        }

        /// <summary>
        /// Disconnect from pipe mode.
        /// </summary>
        public void DisconnectPipe()
        {
            _isPipeMode = false;
            _pipeConnId = 0;

            CancelAll(false);

            if (_client != null)
            {
                _client.onConnect -= OnPipeConnected;
                _client.onData -= OnPipeData;
                _client.onDisconnect -= OnPipeDisconnected;
                _client.onError -= OnError;
                _client.Disconnect();
            }

            _udpClient?.Stop();
            _client = null;

            if (clientState is ConnectionState.Connecting or ConnectionState.Connected)
                clientState = ConnectionState.Disconnecting;
            clientState = ConnectionState.Disconnected;
        }

        public void CloseConnection(Connection conn)
        {
            if (_webRtcPeers.ContainsKey(conn.connectionId))
                _rejectedWebRtcPeers.Add(conn.connectionId);
            RemoveHostWebRtcPeer(conn.connectionId);
            if (serverNatEnabled && _p2pPeersByConnId.Remove(conn.connectionId, out var kickP2pPeer))
            {
                _connIdByP2pPeer.Remove(kickP2pPeer);
                kickP2pPeer.Disconnect();
            }

            _packer.ResetPositionAndMode(false);

            Packer<byte>.Write(_packer, (byte)HOST_PACKET_TYPE.KICK_PLAYER);
            Packer<int>.Write(_packer, conn.connectionId);

            var data = _packer.ToByteData();

            if (_isUsingUDP)
                _relayServerPeer?.Send(data.data, data.offset, data.length, DeliveryMethod.ReliableSequenced);
            else _server?.Send(new ArraySegment<byte>(data.data, data.offset, data.length));
            RaiseDataSent(conn, data, true);
        }

        /// <summary>
        /// Host-side accept handler for inbound P2P connections. A punched client connects
        /// using the relay-issued token as the connection key; we accept only tokens we are
        /// expecting and map the resulting peer to that client's relay connection ID.
        /// </summary>
        private void OnServerConnectionRequestUDP(ConnectionRequest request)
        {
            string token = null;
            try { token = request.Data.GetString(); }
            catch { /* ignored */ }

            if (!string.IsNullOrEmpty(token) &&
                _serverPunches.TryGetValue(token, out var session))
            {
                var peer = request.Accept();
                _p2pPeersByConnId[session.clientConnId] = peer;
                _connIdByP2pPeer[peer] = session.clientConnId;
                _serverPunches.Remove(token);
            }
            else
            {
                request.Reject();
            }
        }

        /// <summary>
        /// Starts (or no-ops if already running) a NAT introduce handshake for the given token.
        /// PollNatPunch resends the introduce request until it succeeds or the deadline passes;
        /// failure is harmless — the relay link keeps carrying the traffic.
        /// </summary>
        private void BeginPunch(string token, bool isServer, int clientConnId)
        {
            if (!(isServer ? serverNatEnabled : clientNatEnabled) || string.IsNullOrEmpty(token))
                return;

            var session = new PunchSession
            {
                token = token,
                clientConnId = clientConnId,
                nextSendTime = 0f,
                deadline = Time.realtimeSinceStartup + 6f
            };

            if (isServer)
            {
                _serverPunches.TryAdd(token, session);
            }
            else
            {
                _clientPunch ??= session;
            }
        }

        /// <summary>Client side of a successful NAT introduction — connect directly to the host.</summary>
        private void OnClientNatPunchSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
        {
            if (!clientNatEnabled || _clientPunch == null || _clientPunch.token != token ||
                Time.realtimeSinceStartup >= _clientPunch.deadline || _p2pHostPeer != null)
                return;

            _clientPunch.done = true;
            _p2pHostPeer = _udpClient.Connect(targetEndPoint, token);
        }

        /// <summary>
        /// Host side of a successful NAT introduction. The punch opened our NAT mapping;
        /// we simply wait for the client's inbound connection (OnServerConnectionRequestUDP).
        /// </summary>
        private void OnServerNatPunchSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
        {
            if (_serverPunches.TryGetValue(token, out var session))
                session.done = true;
        }

        /// <summary>
        /// Pumps NAT punch events and resends pending introduce requests to the relay mediator.
        /// </summary>
        private void PollNatPunch()
        {
            if (!clientNatEnabled && !serverNatEnabled)
                return;

            var clientNat = clientNatEnabled && _udpClient.IsRunning ? _udpClient.NatPunchModule : null;
            var serverNat = serverNatEnabled && _udpServer.IsRunning ? _udpServer.NatPunchModule : null;
            clientNat?.PollEvents();
            serverNat?.PollEvents();

            float now = Time.realtimeSinceStartup;

            if (_clientConnPending && now >= _clientConnDeadline)
                ResolveClientConn(false);

            if (_pendingHostConns.Count > 0)
            {
                _hostConnScratch.Clear();
                foreach (var kv in _pendingHostConns)
                    if (now >= kv.Value)
                        _hostConnScratch.Add(kv.Key);

                for (var i = 0; i < _hostConnScratch.Count; i++)
                    ResolveHostConnAsRelay(_hostConnScratch[i]);
            }

            if (_relayUdpEndPoint == null)
                return;

            if (_serverPunches.Count > 0)
            {
                _punchScratch.Clear();

                foreach (var kv in _serverPunches)
                {
                    var session = kv.Value;

                    if (now >= session.deadline)
                    {
                        _punchScratch.Add(kv.Key);
                        continue;
                    }

                    if (session.done || now < session.nextSendTime)
                        continue;

                    serverNat?.SendNatIntroduceRequest(_relayUdpEndPoint, kv.Key);
                    session.nextSendTime = now + 0.5f;
                }

                for (var i = 0; i < _punchScratch.Count; i++)
                    _serverPunches.Remove(_punchScratch[i]);
            }

            if (_clientPunch != null)
            {
                if (now >= _clientPunch.deadline)
                {
                    _clientPunch = null;
                }
                else if (!_clientPunch.done && now >= _clientPunch.nextSendTime)
                {
                    clientNat?.SendNatIntroduceRequest(_relayUdpEndPoint, _clientPunch.token);
                    _clientPunch.nextSendTime = now + 0.5f;
                }
            }
        }

        public void ReceiveMessages(float delta)
        {
            if (_isUsingUDP)
            {
                if (_udpClient.IsRunning)
                    _udpClient.PollEvents();
                if (_udpServer.IsRunning)
                    _udpServer.PollEvents();
                PollNatPunch();
            }
            else
            {
                _server?.ProcessMessageQueue();
                _client?.ProcessMessageQueue();
            }
            PollWebRtcPeers();
        }

        public void UnityUpdate(float delta)
        {
            ReceiveMessages(delta);
        }

        public void SendMessages(float delta)
        {
            if (_isUsingUDP)
            {
                var dInMs = delta * 1000f;

                if (_udpClient.IsRunning)
                    _udpClient.ManualUpdate(dInMs);
                if (_udpServer.IsRunning)
                    _udpServer.ManualUpdate(dInMs);
            }
        }

        public bool FlushConnection(Connection conn, bool asServer)
        {
            if (asServer)
            {
                if (listenerState != ConnectionState.Connected)
                    return false;

                if (!_isUsingUDP)
                    return true;

                if (serverNatEnabled && _p2pSessionConns.Contains(conn.connectionId))
                {
                    if (_p2pPeersByConnId.TryGetValue(conn.connectionId, out var p2pPeer))
                        p2pPeer.FlushSends();
                    return true;
                }

                _relayServerPeer?.FlushSends();
                return true;
            }

            if (clientState != ConnectionState.Connected)
                return false;

            if (!_isUsingUDP)
                return true;

            if (clientNatEnabled && _clientP2pSession && _p2pHostPeer != null)
            {
                _p2pHostPeer.FlushSends();
                return true;
            }

            _relayClientPeer?.FlushSends();
            return true;
        }

        private void OnDisable()
        {
            StopListening();
            Disconnect();
            CleanupUdp();
        }
    }
}
