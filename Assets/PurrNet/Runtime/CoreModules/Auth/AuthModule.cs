using System;
using System.Collections.Generic;
using PurrNet.Authentication;
using PurrNet.Logging;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet.Modules
{
    internal struct WaitingConnectionAuth
    {
        public Connection conn;
        public float addedTimeStamp;
    }

    internal struct PendingDenialAck
    {
        public Connection conn;
        public float addedTimeStamp;
    }

    public class AuthModule : INetworkModule, IConnectionListener, IFixedUpdate, IPromoteToServerModule
    {
        private const float FALLBACK_DENIAL_TIMEOUT = 5f;
        public const string CONNECTION_COOKIE_KEY = "client_connection_session";

        private readonly NetworkManager _manager;
        private readonly BroadcastModule _broadcastModule;
        private readonly CookiesModule _cookiesModule;
        private PlayersManager _playersManager;

        private AuthenticationLayer _authenticator;
        private readonly List<WaitingConnectionAuth> _waitingConnections = new List<WaitingConnectionAuth>();
        private readonly List<PendingDenialAck> _pendingDenialAcks = new List<PendingDenialAck>();

        public event Action<Connection, AuthenticationResponse> onConnection;

        /// <summary>
        /// Server-side event raised whenever a client is rejected, including by built-in deniers
        /// (version mismatch, ack timeout, missing authenticator) and by the active
        /// <see cref="AuthenticationLayer"/>. Fires before the denial packet is shipped, so handlers
        /// can record the rejection regardless of whether the client acks. <c>reason</c> is
        /// <c>null</c> for built-in deniers (no payload) and non-null only when an
        /// <see cref="AuthenticationBehaviour{TRequest,TDenial}"/> attached a typed denial reason.
        /// </summary>
        public event Action<Connection, DenialKind, ByteData?> onAuthenticationDenied;

        public string clientConnectionCookie => _cookiesModule.Get(CONNECTION_COOKIE_KEY);

        internal void SetClientConnectionCookie(string cookie)
        {
            _cookiesModule.Set(CONNECTION_COOKIE_KEY, cookie, false);
        }

        public AuthModule(NetworkManager manager, BroadcastModule broadcastModule, CookiesModule cookiesModule)
        {
            _cookiesModule = cookiesModule;
            _manager = manager;
            _broadcastModule = broadcastModule;
        }

        public void SetPlayerModule(PlayersManager players)
        {
            _playersManager = players;
        }

        public void PromoteToServerModule()
        {
            Enable(true);
        }

        public void PostPromoteToServerModule()
        {
        }

        public void Enable(bool asServer)
        {
            _authenticator = _manager.authenticator;

            if (!asServer)
            {
                _broadcastModule.Subscribe<AuthenticationDenialPacket>(OnDenialPacket);
                return;
            }

            _broadcastModule.Subscribe<AuthenticationRequest>(OnNonAuthRequest);
            _broadcastModule.Subscribe<AuthenticationDenialAck>(OnDenialAck);

            if (!_authenticator)
                return;

            _authenticator.onAuthenticationComplete += OnAuthenticationComplete;
            _authenticator.onAuthenticationDeniedWithReason += OnAuthenticatorDeniedWithReason;
            _authenticator.Subscribe(_manager, _broadcastModule, _playersManager);
        }

        public void Disable(bool asServer)
        {
            if (!asServer)
            {
                _broadcastModule.Unsubscribe<AuthenticationDenialPacket>(OnDenialPacket);
                return;
            }

            _broadcastModule.Unsubscribe<AuthenticationRequest>(OnNonAuthRequest);
            _broadcastModule.Unsubscribe<AuthenticationDenialAck>(OnDenialAck);

            if (!_authenticator)
                return;

            _authenticator.onAuthenticationComplete -= OnAuthenticationComplete;
            _authenticator.onAuthenticationDeniedWithReason -= OnAuthenticatorDeniedWithReason;
            _authenticator.Unsubscribe(_manager, _broadcastModule, _playersManager);
        }

        public void OnConnected(Connection conn, bool asServer)
        {
            if (!asServer)
            {
                if (_authenticator)
                {
                    _authenticator.SendClientPayload(_broadcastModule, _cookiesModule);
                }
                else
                {
                    var cookie = _cookiesModule.GetOrSet(CONNECTION_COOKIE_KEY, Guid.NewGuid().ToString());
                    _broadcastModule.SendToServer(new AuthenticationRequest
                    {
                        cookie = cookie,
                        version = NetworkManager.version
                    });
                }

                return;
            }

            _waitingConnections.Add(new WaitingConnectionAuth
            {
                conn = conn,
                addedTimeStamp = Time.time
            });
        }

        public void OnDisconnected(Connection conn, bool asServer)
        {
            if (!asServer)
                return;

            RemoveFromWaitingList(conn);

            for (int i = _pendingDenialAcks.Count - 1; i >= 0; i--)
            {
                if (_pendingDenialAcks[i].conn == conn)
                    _pendingDenialAcks.RemoveAt(i);
            }
        }

        private void OnNonAuthRequest(Connection conn, AuthenticationRequest data, bool asserver)
        {
            if (!asserver)
                return;

            RemoveFromWaitingList(conn);

            if (_authenticator)
            {
                PurrLogger.LogError("Authenticator is enabled, but non-auth request received");
                DenyConnection(conn, DenialKind.AuthenticatorRejected, default);
                return;
            }

            if (!NetworkManager.VerifyVersion(data.version))
            {
                var behaviour = _manager.networkRules
                    ? _manager.networkRules.GetVersionMismatchBehaviour()
                    : VersionMismatchBehaviour.Warning;

                if (behaviour == VersionMismatchBehaviour.Deny)
                {
                    PurrLogger.LogError($"Client version mismatch. Client version: {data.version}, Server version: {NetworkManager.version}");
                    DenyConnection(conn, DenialKind.VersionMismatch, default);
                    return;
                }

                PurrLogger.LogWarning($"Client version mismatch. Client version: {data.version}, Server version: {NetworkManager.version}");
            }

            onConnection?.Invoke(conn, new AuthenticationResponse
            {
                success = true,
                cookie = data.cookie
            });
        }

        private void OnAuthenticationComplete(Connection conn, AuthenticationResponse response)
        {
            RemoveFromWaitingList(conn);

            if (!response.success)
            {
                PurrLogger.LogError($"Authentication failed for conn `{conn}`");
                // Idempotent: if the typed-denial path already queued a denial for this conn, this
                // is a no-op; otherwise we ship an empty-reason AuthenticatorRejected.
                DenyConnection(conn, DenialKind.AuthenticatorRejected, default);
                return;
            }

            onConnection?.Invoke(conn, response);
        }

        private void OnAuthenticatorDeniedWithReason(Connection conn, ByteData? reason)
        {
            DenyConnection(conn, DenialKind.AuthenticatorRejected, reason);
        }

        private void RemoveFromWaitingList(Connection conn)
        {
            for (int i = _waitingConnections.Count - 1; i >= 0; i--)
            {
                var waitingConnection = _waitingConnections[i];
                if (waitingConnection.conn != conn)
                    continue;

                _waitingConnections.RemoveAt(i);
            }
        }

        private bool IsDenialPending(Connection conn)
        {
            for (int i = 0; i < _pendingDenialAcks.Count; i++)
            {
                if (_pendingDenialAcks[i].conn == conn)
                    return true;
            }
            return false;
        }

        private float denialAckTimeout => _authenticator ? _authenticator.timeout : FALLBACK_DENIAL_TIMEOUT;

        private void DenyConnection(Connection conn, DenialKind kind, ByteData? reason)
        {
            if (IsDenialPending(conn))
                return;

            RemoveFromWaitingList(conn);

            try
            {
                onAuthenticationDenied?.Invoke(conn, kind, reason);
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }

            try
            {
                // Wire format: zero-length ByteData stands in for "no reason payload"; the
                // ByteData? at the API boundary preserves the null/empty distinction for handlers.
                _broadcastModule.Send(conn, new AuthenticationDenialPacket
                {
                    kind = kind,
                    reason = reason ?? default
                });
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
                _manager.CloseConnection(conn);
                return;
            }

            _pendingDenialAcks.Add(new PendingDenialAck
            {
                conn = conn,
                addedTimeStamp = Time.time
            });
        }

        private void OnDenialAck(Connection conn, AuthenticationDenialAck _, bool asServer)
        {
            if (!asServer)
                return;

            for (int i = _pendingDenialAcks.Count - 1; i >= 0; i--)
            {
                if (_pendingDenialAcks[i].conn != conn)
                    continue;

                _pendingDenialAcks.RemoveAt(i);
                _manager.CloseConnection(conn);
                return;
            }
        }

        private void OnDenialPacket(Connection conn, AuthenticationDenialPacket data, bool asServer)
        {
            if (asServer)
                return;

            if (_authenticator)
            {
                try
                {
                    ByteData? reason = data.reason.length > 0 ? data.reason : null;
                    _authenticator.OnDenialReceived(data.kind, reason);
                }
                catch (Exception e)
                {
                    PurrLogger.LogException(e);
                }
            }

            try
            {
                _broadcastModule.SendToServer(new AuthenticationDenialAck());
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
        }

        public void FixedUpdate()
        {
            float now = Time.time;
            float ackTimeout = denialAckTimeout;

            for (int i = _pendingDenialAcks.Count - 1; i >= 0; i--)
            {
                var pending = _pendingDenialAcks[i];
                if (now - pending.addedTimeStamp <= ackTimeout)
                    continue;

                _pendingDenialAcks.RemoveAt(i);
                _manager.CloseConnection(pending.conn);
            }

            if (!_authenticator)
                return;

            for (int i = _waitingConnections.Count - 1; i >= 0; i--)
            {
                var waitingConnection = _waitingConnections[i];
                if (now - waitingConnection.addedTimeStamp <= _authenticator.timeout)
                    continue;

                PurrLogger.LogError($"Authentication failed for conn `{waitingConnection.conn}` (timed out)");
                _waitingConnections.RemoveAt(i);
                DenyConnection(waitingConnection.conn, DenialKind.Timeout, default);
            }
        }
    }
}
