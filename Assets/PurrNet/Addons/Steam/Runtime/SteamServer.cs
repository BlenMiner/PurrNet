#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

#if STEAMWORKS_NET
#define STEAMWORKS_NET_PACKAGE
#endif

using System;
using PurrNet.Transports;
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
using System.Collections.Generic;
using System.Runtime.InteropServices;
using PurrNet.Logging;
using Steamworks;
#endif

namespace PurrNet.Steam
{
    public class SteamServer
    {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        const int MAX_MESSAGES = 256;

        private HSteamListenSocket _listenSocket;

        private bool _isDedicated;
        static byte[] buffer = new byte[1024];

        Callback<SteamNetConnectionStatusChangedCallback_t> _connectionStatusChanged;

        private readonly List<HSteamNetConnection> _connections = new List<HSteamNetConnection>();
        private readonly Dictionary<int, HSteamNetConnection> _connectionById =
 new Dictionary<int, HSteamNetConnection>();
        private readonly Dictionary<HSteamNetConnection, int> _idByConnection =
 new Dictionary<HSteamNetConnection, int>();
        private readonly Dictionary<int, ulong> _steamIdByConnection = new Dictionary<int, ulong>();

        readonly IntPtr[] _messages = new IntPtr[MAX_MESSAGES];
#endif

#pragma warning disable CS0067 // Event is never used
        public event Action<int> onRemoteConnected;
        public event Action<int> onRemoteDisconnected;
        public event Action<int, ByteData> onDataReceived;
#pragma warning restore CS0067 // Event is never used

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        public bool listening => _listenSocket != HSteamListenSocket.Invalid;
#else
        public bool listening => false;
#endif

        public void Listen(ushort port, bool dedicated = false)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            _isDedicated = dedicated;

            var localAddress = new SteamNetworkingIPAddr();
            localAddress.Clear();
            localAddress.SetIPv4(0, port);

            if (dedicated)
            {
                _listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketIP(
                    ref localAddress,
                    0,
                    null
                );
            }
            else
            {
                _listenSocket = SteamNetworkingSockets.CreateListenSocketIP(
                    ref localAddress,
                    0,
                    null
                );
            }

            PostListen();
#endif
        }

        public void ListenP2P(bool dedicated = false)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            _isDedicated = dedicated;

            if (dedicated)
            {
                _listenSocket = SteamGameServerNetworkingSockets.CreateListenSocketP2P(
                    0,
                    0,
                    null
                );
            }
            else
            {
                _listenSocket = SteamNetworkingSockets.CreateListenSocketP2P(
                    0,
                    0,
                    null
                );
            }

            PostListen();
#endif
        }

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        private void PostListen()
        {
            if (_listenSocket == HSteamListenSocket.Invalid)
            {
                PurrLogger.LogError("Failed to create listen socket.");
                return;
            }

            _connectionStatusChanged = _isDedicated ?
                Callback<SteamNetConnectionStatusChangedCallback_t>.CreateGameServer(OnRemoteConnectionState) :
                Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnRemoteConnectionState);
        }
#endif

        public void SendMessages()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            for (var i = 0; i < _connections.Count; i++)
            {
                var conn = _connections[i];

                if (conn == HSteamNetConnection.Invalid)
                    continue;

                if (_isDedicated)
                     SteamGameServerNetworkingSockets.FlushMessagesOnConnection(conn);
                else SteamNetworkingSockets.FlushMessagesOnConnection(conn);
            }
#endif
        }

        public void ReceiveMessages()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            for (var i = 0; i < _connections.Count; i++)
            {
                var conn = _connections[i];

                if (!_idByConnection.TryGetValue(conn, out var connId))
                    continue;

                int receivedCount = _isDedicated ?
                    SteamGameServerNetworkingSockets.ReceiveMessagesOnConnection(conn, _messages, MAX_MESSAGES) :
                    SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, _messages, MAX_MESSAGES);

                for (int j = 0; j < receivedCount; j++)
                {
                    var ptr = _messages[j];
                    var data = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptr);

                    int packetLength = data.m_cbSize;
                    MakeSureBufferCanFit(packetLength);

                    Marshal.Copy(data.m_pData, buffer, 0, packetLength);
                    var byteData = new ByteData(buffer, 0, packetLength);

                    SteamNetworkingMessage_t.Release(ptr);
                    onDataReceived?.Invoke(connId, byteData);
                }
            }
#endif
        }

        public void Kick(int id)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (!_connectionById.TryGetValue(id, out var conn))
                return;

            if (_isDedicated)
                 SteamGameServerNetworkingSockets.CloseConnection(conn, 0, null, false);
            else SteamNetworkingSockets.CloseConnection(conn, 0, null, false);
#endif
        }
        
        public int GetRoundTripTime(int connectionId)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (!_connectionById.TryGetValue(connectionId, out var conn))
                return -1;

            var status = new SteamNetConnectionRealTimeStatus_t();
            var lanes = new SteamNetConnectionRealTimeLaneStatus_t();

            var result = _isDedicated
                ? SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref lanes)
                : SteamNetworkingSockets.GetConnectionRealTimeStatus(conn, ref status, 0, ref lanes);

            return result == EResult.k_EResultOK ? status.m_nPing : -1;
#else
            return -1;
#endif
        }

        public ulong GetSteamID(int connectionId)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_steamIdByConnection.TryGetValue(connectionId, out var steamID))
                return steamID;
#endif
            return 0;
        }

#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        private static void MakeSureBufferCanFit(int packetLength)
        {
            if (buffer.Length < packetLength)
                Array.Resize(ref buffer, packetLength);
        }
#endif
        public void FlushConnection(int connId)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (!_connectionById.TryGetValue(connId, out var conn))
                return;

            if (_isDedicated)
                SteamGameServerNetworkingSockets.FlushMessagesOnConnection(conn);
            else SteamNetworkingSockets.FlushMessagesOnConnection(conn);
#endif
        }

        public void SendToConnection(int connId, ByteData data, Channel channel)
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (!_connectionById.TryGetValue(connId, out var conn))
                return;

            MakeSureBufferCanFit(data.length);

            var pinnedArray = GCHandle.Alloc(data.data, GCHandleType.Pinned);
            var ptr = pinnedArray.AddrOfPinnedObject() + data.offset;

            byte sendFlag = channel switch {
                Channel.Unreliable => Constants.k_nSteamNetworkingSend_Unreliable,
                Channel.UnreliableSequenced => Constants.k_nSteamNetworkingSend_Reliable,

                Channel.ReliableOrdered => Constants.k_nSteamNetworkingSend_Reliable,
                Channel.ReliableUnordered => Constants.k_nSteamNetworkingSend_Reliable,
                _ => 0
            };

            try
            {
                if (_isDedicated)
                    SteamGameServerNetworkingSockets.SendMessageToConnection(conn, ptr, (uint)data.length, sendFlag,
                        out _);
                else SteamNetworkingSockets.SendMessageToConnection(conn, ptr, (uint)data.length, sendFlag, out _);
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
            }
#endif
        }


#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
        private int _nextConnectionId;

        private void AddConnection(HSteamNetConnection connection, ulong steamId)
        {
            int id = _nextConnectionId++;
            _connections.Add(connection);
            _connectionById.Add(id, connection);
            _idByConnection.Add(connection, id);
            _steamIdByConnection.Add(id, steamId);

            onRemoteConnected?.Invoke(id);
        }

        private void RemoveConnection(HSteamNetConnection connection)
        {
            if (_connections.Remove(connection) && _idByConnection.Remove(connection, out var _id))
            {
                _connectionById.Remove(_id);
                onRemoteDisconnected?.Invoke(_id);
                _steamIdByConnection.Remove(_id);
            }
        }

        private void OnRemoteConnectionState(SteamNetConnectionStatusChangedCallback_t args)
        {
            if (args.m_info.m_hListenSocket != _listenSocket)
                return;

            var state = args.m_info.m_eState;

            switch (state)
            {
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                {
                    var res = _isDedicated
                        ? SteamGameServerNetworkingSockets.AcceptConnection(args.m_hConn)
                        : SteamNetworkingSockets.AcceptConnection(args.m_hConn);

                    if (res != EResult.k_EResultOK)
                        PurrLogger.LogError($"Failed to accept connection: {res}");
                    break;
                }
                case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                {
                    AddConnection(args.m_hConn, args.m_info.m_identityRemote.GetSteamID64());
                    break;
                }
                default:
                {
                    RemoveConnection(args.m_hConn);
                    break;
                }
            }
        }
#endif

        public void Stop()
        {
#if STEAMWORKS_NET_PACKAGE && !DISABLESTEAMWORKS
            if (_connectionStatusChanged != null)
            {
                _connectionStatusChanged.Dispose();
                _connectionStatusChanged = null;
            }

            if (_listenSocket == HSteamListenSocket.Invalid)
                return;

            for (var o = 0; o < _connections.Count; o++)
            {
                try
                {
                    var conn = _connections[o];
                    if (_isDedicated)
                         SteamGameServerNetworkingSockets.CloseConnection(conn, 0, null, false);
                    else SteamNetworkingSockets.CloseConnection(conn, 0, null, false);
                }
                catch
                {
                    // ignored
                }
            }

            _connections.Clear();
            _connectionById.Clear();
            _idByConnection.Clear();
            _steamIdByConnection.Clear();

            try
            {
                if (_isDedicated)
                    SteamGameServerNetworkingSockets.CloseListenSocket(_listenSocket);
                else SteamNetworkingSockets.CloseListenSocket(_listenSocket);
            }
            catch
            {
                // ignored
            }

            _listenSocket = HSteamListenSocket.Invalid;
#endif
        }
    }
}
