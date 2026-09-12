using System;
using System.Collections.Generic;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet.Transports
{
    internal delegate void OnCompositeDataReceived(int transportIdx, Connection conn, ByteData data, bool asServer);

    internal delegate void OnCompositeConnected(int transportIdx, Connection conn, bool asServer);

    internal delegate void OnCompositeDisconnected(int transportIdx, Connection conn, DisconnectReason reason, bool asServer);

    internal class CompositeTransportEvents
    {
        private int index { get; set; }
        private ITransport _transport;

        public bool isSubscribed => _transport != null;

        public event OnCompositeConnected onConnected;
        public event OnCompositeDisconnected onDisconnected;
        public event OnCompositeDataReceived onDataReceived;

        public void Subscribe(int idx, ITransport transport)
        {
            Unsubscribe();

            index = idx;
            _transport = transport;

            _transport.onConnected += OnConnected;
            _transport.onDisconnected += OnDisconnected;
            _transport.onDataReceived += OnDataReceived;
        }

        public void Unsubscribe()
        {
            if (_transport == null) return;

            _transport.onConnected -= OnConnected;
            _transport.onDisconnected -= OnDisconnected;
            _transport.onDataReceived -= OnDataReceived;
            _transport = null;
        }

        private void OnDataReceived(Connection conn, ByteData data, bool asServer)
        {
            onDataReceived?.Invoke(index, conn, data, asServer);
        }

        private void OnDisconnected(Connection conn, DisconnectReason reason, bool asServer)
        {
            onDisconnected?.Invoke(index, conn, reason, asServer);
        }

        private void OnConnected(Connection conn, bool asServer)
        {
            onConnected?.Invoke(index, conn, asServer);
        }
    }

    internal readonly struct RoutedConnection
    {
        public readonly int transportIdx;
        public readonly Connection originalConnection;

        public RoutedConnection(int transportIdx, Connection originalConnection)
        {
            this.transportIdx = transportIdx;
            this.originalConnection = originalConnection;
        }
    }

    internal readonly struct ConnectionPair : IEquatable<ConnectionPair>
    {
        private readonly int transportIdx;
        private readonly Connection connection;

        public ConnectionPair(int transportIdx, Connection connection)
        {
            this.transportIdx = transportIdx;
            this.connection = connection;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(transportIdx, connection);
        }

        public bool Equals(ConnectionPair other)
        {
            return transportIdx == other.transportIdx && connection.Equals(other.connection);
        }

        public override bool Equals(object obj)
        {
            return obj is ConnectionPair other && Equals(other);
        }
    }

    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("PurrNet/Transport/Composite Transport")]
    public partial class CompositeTransport : GenericTransport, ITransport
    {
        [SerializeField, HideInInspector] private bool _ensureAllServersStart;
        [SerializeField, HideInInspector] private GenericTransport[] _transports = { };

        private GenericTransport _clientTransport;
        private readonly HashSet<GenericTransport> _serverTransportFilter = new HashSet<GenericTransport>();
        private bool _hasServerTransportFilter;

        public GenericTransport clientTransport => _clientTransport;

        public event OnConnected onConnected;
        public event OnDisconnected onDisconnected;
        public event OnDataReceived onDataReceived;
        public event OnDataSent onDataSent;
        public event OnConnectionState onConnectionState;

        public IReadOnlyList<GenericTransport> transports => _transports;

        /// <summary>
        /// Limits which child transports the composite starts when listening.
        /// </summary>
        public void SetServerTransportFilter(IReadOnlyList<GenericTransport> transports)
        {
            _serverTransportFilter.Clear();

            if (transports == null)
            {
                _hasServerTransportFilter = false;
                return;
            }

            for (int i = 0; i < transports.Count; i++)
            {
                var transport = transports[i];
                if (transport)
                    _serverTransportFilter.Add(transport);
            }

            _hasServerTransportFilter = true;
        }

        /// <summary>
        /// Clears the server transport filter so every assigned child transport can listen again.
        /// </summary>
        public void ClearServerTransportFilter()
        {
            _serverTransportFilter.Clear();
            _hasServerTransportFilter = false;
        }

        public IReadOnlyList<Connection> connections => _connections;

        readonly Dictionary<ConnectionPair, Connection> _router = new Dictionary<ConnectionPair, Connection>();

        readonly List<RoutedConnection> _rawConnections = new List<RoutedConnection>();

        private bool _internalIsListening;

        public ConnectionState listenerState
        {
            get
            {
                bool anyConnecting = false;
                bool anyConnected = false;
                bool anyDisconnecting = false;

                for (int i = 0; i < _transports.Length; i++)
                {
                    if (_transports[i])
                    {
                        var state = _transports[i].transport.listenerState;

                        switch (state)
                        {
                            case ConnectionState.Connecting: anyConnecting = true; break;
                            case ConnectionState.Disconnecting: anyDisconnecting = true; break;
                            case ConnectionState.Connected: anyConnected = true; break;
                        }
                    }
                }

                if (_internalIsListening)
                {
                    if (_ensureAllServersStart)
                        return anyConnecting ? ConnectionState.Connecting : ConnectionState.Connected;
                    return anyConnected ? ConnectionState.Connected : ConnectionState.Connecting;
                }

                return anyDisconnecting ? ConnectionState.Disconnecting : ConnectionState.Disconnected;
            }
        }

        public ConnectionState clientState =>
            _clientTransport ? _clientTransport.transport.clientState : ConnectionState.Disconnected;

        public override ITransport transport => this;

        private readonly List<Connection> _connections = new List<Connection>();

        private readonly List<CompositeTransportEvents> _events = new List<CompositeTransportEvents>();

        private readonly CompositeTransportEvents _clientEvent = new CompositeTransportEvents();

        public override bool isSupported => true;

        public bool TryGetTransport<T>(out T result) where T : GenericTransport
        {
            for (int i = 0; i < _transports.Length; i++)
            {
                if (_transports[i] && _transports[i] is T t)
                {
                    result = t;
                    return true;
                }
            }

            result = null;
            return false;
        }

        Connection GetNextConnection(int transportIdx, Connection original)
        {
            var conn = new Connection(_rawConnections.Count);
            var routed = new RoutedConnection(transportIdx, original);
            _rawConnections.Add(routed);
            return conn;
        }

        private bool _wasAwakeCalled;

        public bool SupportsChannel(Channel channel)
        {
            return true;
        }

        public override bool isPinging
        {
            get
            {
                if (base.isPinging)
                    return true;

                for (int i = 0; i < _transports.Length; i++)
                {
                    if (_transports[i] && _transports[i].isPinging)
                        return true;
                }

                return false;
            }
        }

        private void PropagatePumping()
        {
            for (int i = 0; i < _transports.Length; i++)
            {
                if (_transports[i])
                    _transports[i].externalPump = () => isPumpedExternally;
            }
        }

        public string clientLinkDescription
        {
            get
            {
                if (!_clientTransport)
                    return null;
                var inner = _clientTransport.transport.clientLinkDescription;
                var name = _clientTransport.GetType().Name;
                return string.IsNullOrEmpty(inner) ? name : $"{name} ({inner})";
            }
        }

        public bool measuresRoundTripTime => _clientTransport && _clientTransport.transport.measuresRoundTripTime;

        public int GetRoundTripTime(Connection target, bool asServer)
        {
            if (asServer)
            {
                if (target.connectionId < 0 || target.connectionId >= _rawConnections.Count)
                    return -1;

                var pair = _rawConnections[target.connectionId];
                var protocol = _transports[pair.transportIdx];
                return protocol ? protocol.transport.GetRoundTripTime(pair.originalConnection, true) : -1;
            }

            return _clientTransport ? _clientTransport.transport.GetRoundTripTime(target, false) : -1;
        }

        public int GetMTU(Connection target, Channel channel, bool asServer)
        {
            if (asServer)
            {
                var pair = _rawConnections[target.connectionId];
                var protocol = _transports[pair.transportIdx];
                return protocol.transport.GetMTU(pair.originalConnection, channel, true);
            }

            if (_clientTransport)
                return _clientTransport.transport.GetMTU(target, channel, false);

            throw new NotSupportedException("No supported transport found for client.");
        }

        private void Awake()
        {
            if (_wasAwakeCalled)
                return;

            _wasAwakeCalled = true;

            PropagatePumping();

            if (!_clientTransport)
            {
                for (int i = 0; i < _transports.Length; i++)
                {
                    if (_transports[i] && _transports[i].isSupported)
                    {
                        _clientTransport = _transports[i];
                        break;
                    }
                }
            }

            SetupEvents();
        }

        public void ReceiveMessages(float delta)
        {
            for (int i = 0; i < _transports.Length; i++)
            {
                if (_transports[i])
                    _transports[i].transport.ReceiveMessages(delta);
            }

            TriggerConnectionStateEvent(true);
            TriggerConnectionStateEvent(false);
        }

        public void SendMessages(float delta)
        {
            for (int i = 0; i < _transports.Length; i++)
            {
                if (_transports[i])
                    _transports[i].transport.SendMessages(delta);
            }
        }

        public bool FlushConnection(Connection conn, bool asServer)
        {
            if (asServer)
            {
                if (!_internalIsListening || conn.connectionId < 0 || conn.connectionId >= _rawConnections.Count)
                    return false;

                var pair = _rawConnections[conn.connectionId];
                var protocol = _transports[pair.transportIdx];
                return protocol && protocol.transport.FlushConnection(pair.originalConnection, true);
            }

            return _clientTransport && _clientTransport.transport.FlushConnection(conn, false);
        }

        public void UnityUpdate(float delta)
        {
            for (int i = 0; i < _transports.Length; i++)
            {
                if (_transports[i])
                    _transports[i].transport.UnityUpdate(delta);
            }
        }

        private void SetupEvents()
        {
            _events.Clear();

            for (int i = 0; i < _transports.Length; i++)
            {
                var events = new CompositeTransportEvents();
                events.onConnected += OnTransportConnected;
                events.onDisconnected += OnTransportDisconnected;
                events.onDataReceived += OnTransportDataReceived;
                _events.Add(events);
            }

            _clientEvent.onConnected += OnTransportConnected;
            _clientEvent.onDisconnected += OnTransportDisconnected;
            _clientEvent.onDataReceived += OnTransportDataReceived;
        }

        private void OnTransportDataReceived(int transportidx, Connection conn, ByteData data, bool asServer)
        {
            switch (asServer)
            {
                case false when transportidx != -1:
                case true when transportidx == -1:
                    return;
                case true:
                {
                    var pair = new ConnectionPair(transportidx, conn);

                    if (_router.TryGetValue(pair, out var target))
                        onDataReceived?.Invoke(target, data, true);
                    else Debug.LogError($"Connection {conn} coming from transport {transportidx} is not routed.");
                    break;
                }
                default:
                    onDataReceived?.Invoke(conn, data, false);
                    break;
            }
        }

        private void OnTransportDisconnected(int transportidx, Connection conn, DisconnectReason reason, bool asServer)
        {
            switch (asServer)
            {
                case false when transportidx != -1:
                case true when transportidx == -1:
                    return;
            }

            TriggerConnectionStateEvent(asServer);

            if (asServer)
            {
                var pair = new ConnectionPair(transportidx, conn);

                if (_router.Remove(pair, out var target))
                {
                    _connections.Remove(target);
                    onDisconnected?.Invoke(target, reason, true);
                }
                else Debug.LogError($"Connection {conn} coming from transport {transportidx} is not routed.");
            }
            else
            {
                _clientEvent.Unsubscribe();
                onDisconnected?.Invoke(conn, reason, false);
            }
        }

        private void OnTransportConnected(int transportidx, Connection conn, bool asServer)
        {
            /*if (!asServer && transportidx != -1)
                return;*/

            switch (asServer)
            {
                case false when transportidx != -1:
                case true when transportidx == -1:
                    return;
            }

            TriggerConnectionStateEvent(asServer);

            if (asServer)
            {
                var fakedConnection = GetNextConnection(transportidx, conn);
                var realConnectionPair = new ConnectionPair(transportidx, conn);
                _router[realConnectionPair] = fakedConnection;
                _connections.Add(fakedConnection);

                onConnected?.Invoke(fakedConnection, true);
            }
            else
            {
                onConnected?.Invoke(conn, false);
            }
        }

        public void SetClientTransport(GenericTransport target)
        {
            if (_clientTransport && _clientTransport.transport.clientState != ConnectionState.Disconnected)
                throw new NotSupportedException("Cannot change client transport while connected.");

            _clientTransport = target;
        }

        public void SetClientTransport<T>() where T : GenericTransport
        {
            if (TryGetTransport<T>(out var t))
                SetClientTransport(t);
        }

        protected override void StartClientInternal()
        {
            Awake();

            if (!_clientTransport || !_clientTransport.isSupported)
                throw new NotSupportedException("No supported transport found for client.");

            _clientEvent.Subscribe(-1, _clientTransport.transport);
            _clientTransport.StartClientInternalOnly();

            TriggerConnectionStateEvent(false);
        }

        public void Connect(string ip, ushort port)
        {
            if (!_clientTransport || !_clientTransport.isSupported)
                throw new NotSupportedException("No supported transport found for client.");

            _clientTransport.transport.Connect(ip, port);
            TriggerConnectionStateEvent(false);
        }

        protected override void StartServerInternal()
        {
            Awake();

            if (_internalIsListening)
                return;

            _internalIsListening = true;

            TriggerConnectionStateEvent(true);

            for (int i = 0; i < _transports.Length; i++)
            {
                if (_transports[i] && ShouldStartServerTransport(_transports[i]))
                {
                    var e = _events[i];
                    if (!e.isSubscribed)
                    {
                        _events[i].Subscribe(i, _transports[i].transport);

                        try
                        {
                            _transports[i].StartServerInternalOnly();
                        }
                        catch (Exception ex)
                        {
                            PurrLogger.LogError(
                                $"Failed to start {_transports[i].GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                        }
                    }
                }
            }

            TriggerConnectionStateEvent(true);
        }

        private bool ShouldStartServerTransport(GenericTransport transport)
        {
            return !_hasServerTransportFilter || _serverTransportFilter.Contains(transport);
        }

        public void Listen(ushort port)
        {
            throw new NotSupportedException("Cannot listen on specific port with composite transport.");
        }

        public void StopListening()
        {
            if (!_internalIsListening)
                return;

            _internalIsListening = false;

            TriggerConnectionStateEvent(true);

            for (int i = 0; i < _transports.Length; i++)
            {
                if (_transports[i])
                {
                    _transports[i].transport.StopListening();

                    var e = _events[i];
                    e.Unsubscribe();
                }
            }

            _clientEvent.Unsubscribe();

            TriggerConnectionStateEvent(true);

            _connections.Clear();
            _router.Clear();
            _rawConnections.Clear();
            ClearServerTransportFilter();
        }

        public void Disconnect()
        {
            if (!_clientTransport)
                throw new NotSupportedException("No supported transport found for client.");

            _clientTransport.transport.Disconnect();
            _clientEvent.Unsubscribe();

            TriggerConnectionStateEvent(false);
        }

        public void RaiseDataReceived(Connection conn, ByteData data, bool asServer)
        {
            onDataReceived?.Invoke(conn, data, asServer);
        }

        public void RaiseDataSent(Connection conn, ByteData data, bool asServer)
        {
            onDataSent?.Invoke(conn, data, asServer);
        }

        public void SendToClient(Connection target, ByteData data, Channel method = Channel.Unreliable)
        {
            if (!_internalIsListening)
                return;

            var pair = _rawConnections[target.connectionId];
            var protocol = _transports[pair.transportIdx];
            protocol.transport.SendToClient(pair.originalConnection, data, method);
            RaiseDataSent(pair.originalConnection, data, true);
        }

        public void SendToServer(ByteData data, Channel method = Channel.Unreliable)
        {
            if (!_clientTransport)
                throw new NotSupportedException("No supported transport found for client.");

            _clientTransport.transport.SendToServer(data, method);
            RaiseDataSent(default, data, false);
        }

        public void CloseConnection(Connection conn)
        {
            var pair = _rawConnections[conn.connectionId];
            var protocol = _transports[pair.transportIdx];
            protocol.transport.CloseConnection(pair.originalConnection);
        }

        ConnectionState _prevClientState = ConnectionState.Disconnected;
        ConnectionState _prevServerState = ConnectionState.Disconnected;

        private void TriggerConnectionStateEvent(bool asServer)
        {
            if (asServer)
            {
                if (_prevServerState != listenerState)
                {
                    if (listenerState == ConnectionState.Disconnected &&
                        _prevServerState != ConnectionState.Disconnecting)
                        onConnectionState?.Invoke(ConnectionState.Disconnecting, true);

                    if (listenerState == ConnectionState.Connected && _prevServerState != ConnectionState.Connecting)
                        onConnectionState?.Invoke(ConnectionState.Connecting, true);

                    onConnectionState?.Invoke(listenerState, true);
                    _prevServerState = listenerState;
                }
            }
            else
            {
                if (_prevClientState != clientState)
                {
                    if (clientState == ConnectionState.Disconnected &&
                        _prevClientState != ConnectionState.Disconnecting)
                        onConnectionState?.Invoke(ConnectionState.Disconnecting, false);

                    if (clientState == ConnectionState.Connected && _prevClientState != ConnectionState.Connecting)
                        onConnectionState?.Invoke(ConnectionState.Connecting, false);

                    onConnectionState?.Invoke(clientState, false);
                    _prevClientState = clientState;
                }
            }
        }
    }
}
