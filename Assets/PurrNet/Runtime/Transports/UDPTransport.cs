using System.Collections.Generic;
using LiteNetLib;
using PurrNet.Edgegap.Runtime;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet.Transports
{
    [AddComponentMenu("PurrNet/Transport/UDP Transport")]
    [DefaultExecutionOrder(-100)]
    public partial class UDPTransport : GenericTransport, ITransport, INetLogger
    {
        [SerializeField] private AutomaticCloudSetups _automaticCloudSetups;

        [Header("Server Settings")]
        [Tooltip("The port which the server will start on, and clients connect.")]
        [SerializeField]
        private ushort _serverPort = 5000;

        [Tooltip("The max amount of client connections allowed.")] [SerializeField]
        private int _maxConnections = 100;

        [Header("Client Settings")]
        [Tooltip("This is the IP the client will use to connect to the server.")]
        [SerializeField]
        private string _address = "127.0.0.1";

        [Header("Shared Settings")]
        [Tooltip("The amount of time in seconds before socket is disconnected due to no data being received.")]
        [SerializeField]
        private float _timeoutInSeconds = 5f;

        [Tooltip("Use native UDP sockets on supported Windows/Linux runtimes to avoid per-packet socket allocations. Other platforms use managed sockets. Applied when the transport is enabled.")]
        [SerializeField] private bool _useNativeSockets = true;

        [SerializeField] private NetworkSimulation _networkSimulation = NetworkSimulation.@default;

        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        public string address
        {
            get => _address;
            set => _address = value;
        }

        public ushort serverPort
        {
            get => _serverPort;
            set => _serverPort = value;
        }

        public int maxConnections
        {
            get => _maxConnections;
            set => _maxConnections = value;
        }

        public NetworkSimulation networkSimulation
        {
            get => _networkSimulation;
            set
            {
                _networkSimulation = value;
                ApplySimulationSettings();
            }
        }

        public IReadOnlyList<Connection> connections => _connections;

        public IReadOnlyDictionary<Connection, PeerInfo> peers => _peers;

        /// <summary>
        /// Toggles LiteNetLib's socket-level statistics on both the client and server managers.
        /// When enabled the counters below reflect the real bytes/packets handed to the OS socket,
        /// including LiteNetLib framing, ACKs and MTU pings (but excluding the UDP/IP headers).
        /// </summary>
        public void SetStatisticsEnabled(bool enabled)
        {
            if (_client != null) _client.EnableStatistics = enabled;
            if (_server != null) _server.EnableStatistics = enabled;
        }

        public bool statisticsEnabled => (_client != null && _client.EnableStatistics) ||
                                         (_server != null && _server.EnableStatistics);

        public long nativeBytesSent => (_client?.Statistics.BytesSent ?? 0) + (_server?.Statistics.BytesSent ?? 0);
        public long nativeBytesReceived => (_client?.Statistics.BytesReceived ?? 0) + (_server?.Statistics.BytesReceived ?? 0);
        public long nativePacketsSent => (_client?.Statistics.PacketsSent ?? 0) + (_server?.Statistics.PacketsSent ?? 0);
        public long nativePacketsReceived => (_client?.Statistics.PacketsReceived ?? 0) + (_server?.Statistics.PacketsReceived ?? 0);
        public long nativePacketLoss => (_client?.Statistics.PacketLoss ?? 0) + (_server?.Statistics.PacketLoss ?? 0);

        private EventBasedNetListener _clientListener;
        private EventBasedNetListener _serverListener;

        private NetManager _client;
        private NetManager _server;

        public ConnectionState clientState { get; private set; } = ConnectionState.Disconnected;

        public ConnectionState listenerState { get; private set; } = ConnectionState.Disconnected;

        readonly List<Connection> _connections = new List<Connection>();

        readonly Dictionary<Connection, PeerInfo> _peers = new Dictionary<Connection, PeerInfo>();
        readonly Dictionary<NetPeer, Connection> _peerToConnection = new Dictionary<NetPeer, Connection>();
        readonly Dictionary<Connection, NetPeer> _connectionToPeer = new Dictionary<Connection, NetPeer>();
        private int _nextConnectionId = 1;

        public override bool isSupported => Application.platform != RuntimePlatform.WebGLPlayer;

        public override ITransport transport => this;

        private void Awake()
        {
            NetDebug.Logger = this;
            SetupCloud();
        }

        public bool SupportsChannel(Channel channel)
        {
            return true;
        }

        public int GetMTU(Connection target, Channel channel, bool asServer)
        {
            try
            {
                if (asServer)
                    return _connectionToPeer[target].GetMaxSinglePacketSize(ToDeliveryMethod(channel));
                return _client.FirstPeer.GetMaxSinglePacketSize(ToDeliveryMethod(channel));
            }
            catch
            {
                return 1024;
            }
        }

        public bool measuresRoundTripTime => true;

        public int GetRoundTripTime(Connection conn, bool asServer)
        {
            if (asServer)
                return _connectionToPeer.TryGetValue(conn, out var peer) ? GetRoundTripTime(peer) : -1;

            return GetRoundTripTime(_client?.FirstPeer);
        }

        private static int GetRoundTripTime(NetPeer peer)
        {
            return peer != null && peer.HasRoundTripTime ? peer.RoundTripTime : -1;
        }

        private void SetupCloud()
        {
            if (_automaticCloudSetups == null)
                return;

            if (_automaticCloudSetups.adaptToEdgegap)
            {
                var arbitrium = EdgegapUtils.GetArbitrium();
                if (arbitrium.TryGetPort("UDP", 0, out var port))
                {
                    _serverPort = (ushort)port;
                    _address = "0.0.0.0";
                    PurrLogger.Log($"Edgegap Auto-Setup: 0.0.0.0:{port}");
                }
                else PurrLogger.Log("Edgegap Auto-Setup: No UDP port");
            }
        }

        private void OnEnable()
        {
            _clientListener = new EventBasedNetListener();
            _serverListener = new EventBasedNetListener();

            _client = new NetManager(_clientListener)
            {
                UnconnectedMessagesEnabled = true,
                PingInterval = 900,
                AutoRecycle = true,
                UseNativeSockets = _useNativeSockets,
                EnableStatistics = false,
                DisconnectTimeout = Mathf.RoundToInt(_timeoutInSeconds * 1000)
            };

            _server = new NetManager(_serverListener)
            {
                UnconnectedMessagesEnabled = true,
                PingInterval = 900,
                AutoRecycle = true,
                UseNativeSockets = _useNativeSockets,
                EnableStatistics = false,
                DisconnectTimeout = Mathf.RoundToInt(_timeoutInSeconds * 1000)
            };

            _clientListener.PeerConnectedEvent += OnClientConnected;
            _clientListener.PeerDisconnectedEvent += OnClientDisconnected;
            _clientListener.NetworkReceiveEvent += OnClientData;

            _serverListener.ConnectionRequestEvent += OnServerConnectionRequest;
            _serverListener.PeerConnectedEvent += OnServerConnected;
            _serverListener.PeerDisconnectedEvent += OnServerDisconnected;
            _serverListener.NetworkReceiveEvent += OnServerData;

            ApplySimulationSettings();
        }

        private void ApplySimulationSettings()
        {
            bool apply = _networkSimulation.ShouldApply();

            if (_client != null)
            {
                _client.SimulateLatency = apply && _networkSimulation.simulateLatency;
                _client.SimulationMinLatency = _networkSimulation.minLatency;
                _client.SimulationMaxLatency = _networkSimulation.maxLatency;
                _client.SimulatePacketLoss = apply && _networkSimulation.simulatePacketLoss;
                _client.SimulationPacketLossChance = _networkSimulation.packetLossChance;
            }

            if (_server != null)
            {
                _server.SimulateLatency = apply && _networkSimulation.simulateLatency;
                _server.SimulationMinLatency = _networkSimulation.minLatency;
                _server.SimulationMaxLatency = _networkSimulation.maxLatency;
                _server.SimulatePacketLoss = apply && _networkSimulation.simulatePacketLoss;
                _server.SimulationPacketLossChance = _networkSimulation.packetLossChance;
            }
        }

        private void OnValidate()
        {
            ApplySimulationSettings();
        }

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
        {
            onDataReceived?.Invoke(conn, data, asServer);
        }

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
        {
            onDataSent?.Invoke(conn, data, asServer);
        }

        private void OnServerData(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliverymethod)
        {
            if (!_peerToConnection.TryGetValue(peer, out var conn))
                return;

            var data = new ByteData(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
            onDataReceived?.Invoke(conn, data, true);
        }

        private void OnClientData(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliverymethod)
        {
            var data = new ByteData(reader.RawData, reader.UserDataOffset, reader.UserDataSize);
            onDataReceived?.Invoke(new Connection(peer.Id), data, false);
        }

        private void OnServerConnectionRequest(ConnectionRequest request)
        {
            if (_server.ConnectedPeersCount < _maxConnections)
                request.AcceptIfKey("PurrNet");
            else request.Reject();
        }

        protected override void StartServerInternal()
        {
            Listen(_serverPort);
        }

        protected override void StartClientInternal()
        {
            Connect(_address, _serverPort);
        }

        private void OnClientDisconnected(NetPeer peer, DisconnectInfo disconnectinfo)
        {
            clientState = ConnectionState.Disconnected;
            TriggerConnectionStateEvent(false);
            var reason = disconnectinfo.Reason switch
            {
                LiteNetLib.DisconnectReason.Timeout => DisconnectReason.Timeout,
                LiteNetLib.DisconnectReason.RemoteConnectionClose => DisconnectReason.ServerRequest,
                _ => DisconnectReason.ClientRequest
            };

            onDisconnected?.Invoke(new Connection(peer.Id), reason, false);
        }

        private void OnClientConnected(NetPeer peer)
        {
            var conn = new Connection(peer.Id);
            clientState = ConnectionState.Connected;
            TriggerConnectionStateEvent(false);
            onConnected?.Invoke(conn, false);
        }

        private void OnServerDisconnected(NetPeer peer, DisconnectInfo disconnectinfo)
        {
            if (!_peerToConnection.TryGetValue(peer, out var conn))
                return;

            for (int i = 0; i < _connections.Count; i++)
            {
                if (_connections[i] == conn)
                {
                    _peers.Remove(conn);
                    _connectionToPeer.Remove(conn);
                    _peerToConnection.Remove(peer);
                    _connections.RemoveAt(i);
                    break;
                }
            }

            var reason = disconnectinfo.Reason switch
            {
                LiteNetLib.DisconnectReason.Timeout => DisconnectReason.Timeout,
                LiteNetLib.DisconnectReason.RemoteConnectionClose => DisconnectReason.ClientRequest,
                _ => DisconnectReason.ServerRequest
            };

            onDisconnected?.Invoke(conn, reason, true);
        }

        private void OnServerConnected(NetPeer peer)
        {
            var conn = new Connection(_nextConnectionId++);

            _peerToConnection[peer] = conn;
            _connectionToPeer[conn] = peer;
            _peers[conn] = PeerInfo.Generate(peer);

            _connections.Add(conn);
            onConnected?.Invoke(conn, true);
        }

        /// In this mode you should use ManualReceive (without PollEvents) for receive packets
        /// and ManualUpdate(...) for update and send packets
        public void ReceiveMessages(float delta)
        {
            if (_server.IsRunning)
                _server.PollEvents();

            if (_client.IsRunning)
                _client.PollEvents();
        }

        public void SendMessages(float delta)
        {
            var dInMs = delta * 1000f;

            if (_server.IsRunning)
                _server.ManualUpdate(dInMs);

            if (_client.IsRunning)
                _client.ManualUpdate(dInMs);
        }

        public void UnityUpdate(float delta)
        {
            if (_server.IsRunning) _server.PollEvents();
            if (_client.IsRunning) _client.PollEvents();
        }

        public void Connect(string ip, ushort port)
        {
            if (clientState == ConnectionState.Connected)
                return;

            clientState = ConnectionState.Connecting;

            TriggerConnectionStateEvent(false);

            _client.StartInManualMode(0);
            _client.Connect(ip, port, "PurrNet");
            TriggerConnectionStateEvent(false);
        }

        public void Disconnect()
        {
            if (clientState is not (ConnectionState.Connected or ConnectionState.Connecting))
                return;

            clientState = ConnectionState.Disconnecting;
            TriggerConnectionStateEvent(false);

            _client.DisconnectAll();
            _client.Stop();

            clientState = ConnectionState.Disconnected;
            TriggerConnectionStateEvent(false);
        }

        public void Listen(ushort port)
        {
            NetDebug.Logger = this;

            if (listenerState is ConnectionState.Disconnected or ConnectionState.Disconnecting)
            {
                listenerState = ConnectionState.Connecting;
                TriggerConnectionStateEvent(true);

                if (_server.StartInManualMode(port))
                {
                    listenerState = ConnectionState.Connected;
                    TriggerConnectionStateEvent(true);
                }
                else
                {
                    listenerState = ConnectionState.Disconnecting;
                    TriggerConnectionStateEvent(true);
                    listenerState = ConnectionState.Disconnected;
                    TriggerConnectionStateEvent(true);
                }
            }
        }

        public void StopListening()
        {
            if (listenerState is ConnectionState.Connected or ConnectionState.Connecting)
            {
                listenerState = ConnectionState.Disconnecting;
                TriggerConnectionStateEvent(true);

                _server.Stop();

                listenerState = ConnectionState.Disconnected;
                TriggerConnectionStateEvent(true);

                _peerToConnection.Clear();
                _connectionToPeer.Clear();
                _peers.Clear();
                _connections.Clear();
                _nextConnectionId = 1;
            }
        }

        public static DeliveryMethod ToDeliveryMethod(Channel channel)
        {
            return channel switch
            {
                Channel.ReliableUnordered => DeliveryMethod.ReliableUnordered,
                Channel.UnreliableSequenced => DeliveryMethod.Sequenced,
                Channel.ReliableOrdered => DeliveryMethod.ReliableOrdered,
                _ => DeliveryMethod.Unreliable
            };
        }

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.Unreliable)
        {
            if (listenerState is not ConnectionState.Connected)
                return;

            if (!target.isValid)
                return;

            var deliveryMethod = ToDeliveryMethod(method);
            _connectionToPeer.TryGetValue(target, out var peer);
            peer?.Send(data.data, data.offset, data.length, deliveryMethod);
            RaiseDataSent(target, data, true);
        }

        public void SendToServer(ByteData data, Channel method = Channel.Unreliable)
        {
            if (clientState != ConnectionState.Connected)
                return;

            var deliveryMethod = ToDeliveryMethod(method);
            _client.SendToAll(data.data, data.offset, data.length, deliveryMethod);
            RaiseDataSent(default, data, false);
        }

        public bool FlushConnection(Connection conn, bool asServer)
        {
            if (asServer)
            {
                if (listenerState is not ConnectionState.Connected)
                    return false;

                if (_connectionToPeer.TryGetValue(conn, out var peer))
                    peer.FlushSends();
                return true;
            }

            if (clientState != ConnectionState.Connected)
                return false;

            _client.FirstPeer?.FlushSends();
            return true;
        }

        public void CloseConnection(Connection conn)
        {
            _connectionToPeer.TryGetValue(conn, out var peer);
            peer?.Disconnect();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void Cleanup()
        {
            _client?.Stop();
            _server?.Stop();

            listenerState = ConnectionState.Disconnected;
            clientState = ConnectionState.Disconnected;

            TriggerConnectionStateEvent(true);
            TriggerConnectionStateEvent(false);

            _peerToConnection.Clear();
            _connectionToPeer.Clear();
            _peers.Clear();
            _connections.Clear();
            _nextConnectionId = 1;
        }

        public void WriteNet(NetLogLevel level, string str, params object[] args)
        {
            switch (level)
            {
                case NetLogLevel.Trace: break;
                case NetLogLevel.Info:
                    PurrLogger.Log(string.Format(str, args));
                    break;
                case NetLogLevel.Warning:
                    PurrLogger.LogWarning(string.Format(str, args));
                    break;
                case NetLogLevel.Error:
                    PurrLogger.LogError(string.Format(str, args));
                    break;
            }
        }

        ConnectionState _prevClientState = ConnectionState.Disconnected;
        ConnectionState _prevServerState = ConnectionState.Disconnected;

        private void TriggerConnectionStateEvent(bool asServer)
        {
            if (asServer)
            {
                if (_prevServerState != listenerState)
                {
                    onConnectionState?.Invoke(listenerState, true);
                    _prevServerState = listenerState;
                }
            }
            else
            {
                if (_prevClientState != clientState)
                {
                    onConnectionState?.Invoke(clientState, false);
                    _prevClientState = clientState;
                }
            }
        }
    }
}
