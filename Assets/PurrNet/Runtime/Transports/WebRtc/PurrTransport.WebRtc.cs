using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PurrNet.Transports
{
    public partial class PurrTransport : ISerializationCallbackReceiver
    {
        [SerializeField, HideInInspector] private bool _useWebRtcP2P;
        [SerializeField, HideInInspector] private string _webRtcStunServer = "stun:stun.cloudflare.com:3478";

        /// <summary>Compatibility alias for <see cref="attemptDirectConnection"/>.</summary>
        public bool useWebRtcP2P
        {
            get => attemptDirectConnection;
            set => attemptDirectConnection = value;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize() { }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Merge the former WebRTC opt-in into the existing serialized direct-connection setting.
            if (_useWebRtcP2P)
            {
                attemptDirectConnection = true;
                _useWebRtcP2P = false;
            }
        }

        public string webRtcStunServer
        {
            get => _webRtcStunServer;
            set => _webRtcStunServer = value;
        }

        private bool webRtcP2PAvailable => attemptDirectConnection && !_isPipeMode && !isPinging &&
            (PurrWebClient.supportsPeerConnections || PurrWebRtcPeerProvider.nativeFactory != null);

        private const int MaxWebRtcSignalBytes = 48 * 1024;
        private readonly Dictionary<int, DirectWebRtcSession> _webRtcPeers = new();
        private readonly HashSet<int> _rejectedWebRtcPeers = new();
        private readonly List<DirectWebRtcSession> _webRtcPeerScratch = new();
        private readonly WebRtcHostInbox _webRtcPeerInbox = new();
        private readonly WebRtcHostInbox _webRtcClientInbox = new();
        private DirectWebRtcSession _webRtcHost;

        [Serializable]
        private sealed class WebRtcSignal
        {
            public string type;
            public string token;
            public int clientId;
            public string signal;
            public bool direct;
        }

        [Serializable]
        private sealed class WebRtcIceServer
        {
            public string urls;
        }

        private sealed class DirectWebRtcSession
        {
            public int clientId;
            public string token;
            public IPurrWebRtcPeer peer;
            public bool asServer;
            public bool decided;
            public bool direct;
            public bool failed;
            public float deadline;
        }

        private bool clientUsesDirectWebRtc => _webRtcHost is { decided: true, direct: true };

        private bool HostUsesDirectWebRtc(int clientId) =>
            _webRtcPeers.TryGetValue(clientId, out var session) && session.decided && session.direct;

        private int WebRtcP2pConnectionCount
        {
            get
            {
                int count = 0;
                foreach (var connection in _connections)
                    if (HostUsesDirectWebRtc(connection.connectionId))
                        ++count;
                return count;
            }
        }

        public SessionLink GetSessionLink(Connection connection)
        {
            if (!_connections.Contains(connection))
                return SessionLink.None;
            return HostUsesDirectWebRtc(connection.connectionId) || _p2pSessionConns.Contains(connection.connectionId)
                ? SessionLink.P2P : SessionLink.Relay;
        }

        public ConnectionProtocol GetConnectionProtocol(Connection connection) =>
            HostUsesDirectWebRtc(connection.connectionId) ? ConnectionProtocol.WebRTC : hostConnectionProtocol;

        private void HandleWebRtcSignal(ArraySegment<byte> data, bool asServer)
        {
            if (data.Count <= 1 || data.Count - 1 > MaxWebRtcSignalBytes)
                return;
            WebRtcSignal message;
            try
            {
                message = JsonUtility.FromJson<WebRtcSignal>(Encoding.UTF8.GetString(data.Array, data.Offset + 1, data.Count - 1));
            }
            catch (ArgumentException)
            {
                return;
            }
            if (message == null || string.IsNullOrEmpty(message.token) || message.token.Length > 64 || message.clientId < 0)
                return;

            if (message.type == "introduce")
            {
                BeginWebRtcSession(message, asServer);
                return;
            }

            var session = asServer
                ? (_webRtcPeers.TryGetValue(message.clientId, out var peer) ? peer : null)
                : _webRtcHost;
            if (session == null || session.token != message.token || session.clientId != message.clientId || session.decided)
                return;

            if (message.type == "signal" && !session.failed && message.signal != null &&
                Encoding.UTF8.GetByteCount(message.signal) <= 32 * 1024)
            {
                try { session.peer.ReceiveSignal(message.signal); }
                catch (Exception) { FailWebRtcSession(session); }
            }
            else if (message.type == "commit")
            {
                session.decided = true;
                session.direct = message.direct;
                if (!message.direct)
                    RemoveWebRtcSession(session);
                else if (session.failed || session.peer?.isConnected != true)
                    FailWebRtcSession(session);
            }
        }

        private void BeginWebRtcSession(WebRtcSignal message, bool asServer)
        {
            if (asServer ? _webRtcPeers.ContainsKey(message.clientId) : _webRtcHost != null)
                return;
            if (!webRtcP2PAvailable || (asServer && (_webRtcPeers.Count >= 1024 ||
                _connections.Contains(new Connection(message.clientId)))))
            {
                SendWebRtcControl(message, asServer, "failed");
                return;
            }

            var session = new DirectWebRtcSession
            {
                clientId = message.clientId, token = message.token, asServer = asServer,
                deadline = Time.realtimeSinceStartup + 15f
            };
            if (asServer)
                _webRtcPeers.Add(session.clientId, session);
            else
            {
                _webRtcHost = session;
                _webRtcClientInbox.Clear();
            }

            try
            {
                session.peer = PurrWebClient.supportsPeerConnections
                    ? PurrWebClient.Create(ushort.MaxValue, 5000, _tcpConfig)
                    : PurrWebRtcPeerProvider.nativeFactory?.Invoke();
                if (session.peer == null)
                {
                    FailWebRtcSession(session);
                    return;
                }
                session.peer.onSignal += signal =>
                {
                    if (IsCurrentWebRtcSession(session) && !session.decided && !session.failed)
                        SendWebRtcControl(session, "signal", signal);
                };
                session.peer.onConnect += () =>
                {
                    if (IsCurrentWebRtcSession(session) && !session.decided && !session.failed)
                        SendWebRtcControl(session, "ready");
                };
                session.peer.onDisconnect += () => FailWebRtcSession(session);
                session.peer.onError += _ => FailWebRtcSession(session);
                session.peer.onData += data => ReceiveDirectWebRtc(session, data);
                var iceServers = string.IsNullOrWhiteSpace(_webRtcStunServer) ? "[]"
                    : "[" + JsonUtility.ToJson(new WebRtcIceServer { urls = _webRtcStunServer }) + "]";
                session.peer.ConnectPeer(!asServer, iceServers);
            }
            catch (Exception)
            {
                FailWebRtcSession(session);
            }
        }

        private bool IsCurrentWebRtcSession(DirectWebRtcSession session) => session.asServer
            ? _webRtcPeers.TryGetValue(session.clientId, out var current) && ReferenceEquals(current, session)
            : ReferenceEquals(_webRtcHost, session);

        private void SendWebRtcControl(DirectWebRtcSession session, string type, string signal = null) =>
            SendWebRtcControl(new WebRtcSignal
            {
                token = session.token, clientId = session.clientId, signal = signal
            }, session.asServer, type);

        private void SendWebRtcControl(WebRtcSignal message, bool asServer, string type)
        {
            message.type = type;
            var json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
            if (json.Length > MaxWebRtcSignalBytes)
                return;
            var frame = new byte[json.Length + 1];
            frame[0] = asServer ? (byte)HOST_PACKET_TYPE.WEBRTC_SIGNAL : byte.MaxValue;
            Buffer.BlockCopy(json, 0, frame, 1, json.Length);
            if (_isUsingUDP)
                (asServer ? _relayServerPeer : _relayClientPeer)?.Send(frame, LiteNetLib.DeliveryMethod.ReliableOrdered);
            else
                (asServer ? _server : _client)?.Send(new ArraySegment<byte>(frame), 2);
        }

        private void FailWebRtcSession(DirectWebRtcSession session)
        {
            if (!IsCurrentWebRtcSession(session))
                return;
            if (session.decided)
            {
                if (session.direct)
                {
                    if (session.asServer)
                    {
                        var conn = new Connection(session.clientId);
                        if (_connections.Remove(conn))
                            onDisconnected?.Invoke(conn, DisconnectReason.Timeout, true);
                        CloseConnection(new Connection(session.clientId));
                    }
                    else
                        Disconnect();
                }
                return;
            }
            if (session.failed)
                return;
            session.failed = true;
            session.peer?.Disconnect();
            SendWebRtcControl(session, "failed");
        }

        private void RemoveWebRtcSession(DirectWebRtcSession session)
        {
            if (!IsCurrentWebRtcSession(session))
                return;
            if (session.asServer)
            {
                _webRtcPeers.Remove(session.clientId);
                _webRtcPeerInbox.MarkDisconnected(session.clientId);
            }
            else
            {
                _webRtcHost = null;
                _webRtcClientInbox.Clear();
            }
            session.peer?.Disconnect();
        }

        private void RemoveHostWebRtcPeer(int clientId)
        {
            if (_webRtcPeers.TryGetValue(clientId, out var session))
                RemoveWebRtcSession(session);
        }

        private void ClearWebRtcPeers(bool asServer)
        {
            if (asServer)
            {
                var peers = new List<DirectWebRtcSession>(_webRtcPeers.Values);
                _webRtcPeers.Clear();
                _rejectedWebRtcPeers.Clear();
                _webRtcPeerInbox.Clear();
                foreach (var session in peers)
                    session.peer?.Disconnect();
            }
            else
            {
                var session = _webRtcHost;
                _webRtcHost = null;
                _webRtcClientInbox.Clear();
                session?.peer?.Disconnect();
            }
        }

        private void ReceiveDirectWebRtc(DirectWebRtcSession session, ArraySegment<byte> data)
        {
            if (!IsCurrentWebRtcSession(session) || session.failed)
                return;
            var conn = new Connection(session.asServer ? session.clientId : 0);
            bool connected = session.asServer ? _connections.Contains(conn) : clientState == ConnectionState.Connected;
            if (!session.decided || !connected)
            {
                var inbox = session.asServer ? _webRtcPeerInbox : _webRtcClientInbox;
                if (inbox.TryQueue(conn.connectionId, data, session.peer.receivedDeliveryMethod) == WebRtcHostInbox.QueueResult.Overflow)
                    FailWebRtcSession(session);
                return;
            }
            if (session.direct)
                RaiseDataReceived(conn, new ByteData(data), session.asServer);
        }

        private void DeliverPendingWebRtcClientData()
        {
            var pending = _webRtcClientInbox.MarkConnected(0);
            if (pending == null)
                return;
            foreach (var data in pending)
            {
                if (clientState != ConnectionState.Connected || !clientUsesDirectWebRtc)
                    break;
                RaiseDataReceived(new Connection(0), new ByteData(data), false);
            }
        }

        private List<ArraySegment<byte>> TakePendingHostWebRtcData(int clientId) =>
            HostUsesDirectWebRtc(clientId) ? _webRtcPeerInbox.MarkConnected(clientId)
                : hostUsesWebRtc ? _webRtcHostInbox.MarkConnected(clientId) : null;

        private void PollWebRtcPeers()
        {
            _webRtcPeerScratch.Clear();
            _webRtcPeerScratch.AddRange(_webRtcPeers.Values);
            if (_webRtcHost != null)
                _webRtcPeerScratch.Add(_webRtcHost);
            foreach (var session in _webRtcPeerScratch)
            {
                if (!IsCurrentWebRtcSession(session))
                    continue;
                session.peer?.ProcessMessageQueue();
                if (!IsCurrentWebRtcSession(session) || session.decided || Time.realtimeSinceStartup < session.deadline)
                    continue;
                // A missing relay decision cannot safely become a unilateral route change.
                if (session.asServer)
                    CloseConnection(new Connection(session.clientId));
                else
                    Disconnect();
            }
        }
    }
}
