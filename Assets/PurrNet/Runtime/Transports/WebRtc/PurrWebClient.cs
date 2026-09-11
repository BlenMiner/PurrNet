using System;
using System.Collections.Generic;
using JamesFrowen.SimpleWeb;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif

namespace PurrNet.Transports
{
    /// <summary>
    /// Uses browser data channels when a relay advertises WebRTC, falling back to
    /// the existing WebSocket client if negotiation fails before connecting.
    /// </summary>
    public sealed class PurrWebClient
    {
        public event Action onConnect;
        public event Action onDisconnect;
        public event Action<ArraySegment<byte>> onData;
        public event Action<Exception> onError;

        public bool isWebRtc { get; private set; }
        internal byte receivedDeliveryMethod { get; private set; } = 2;
        public ClientState ConnectionState { get; private set; } = ClientState.NotConnected;

        private readonly int _maxMessageSize;
        private readonly int _maxMessagesPerTick;
        private readonly TcpConfig _tcpConfig;
        private readonly Queue<PendingEvent> _events = new Queue<PendingEvent>();
        private SimpleWebClient _webSocket;
        private Uri _webSocketAddress;
        private int _generation;
        private bool _active;
        private bool _discardQueuedData;

        private enum EventKind { Connected, Disconnected, Data, Error, Fallback }

        private struct PendingEvent
        {
            public int generation;
            public EventKind kind;
            public byte[] data;
            public byte deliveryMethod;
            public Exception error;
        }

        private PurrWebClient(int maxMessageSize, int maxMessagesPerTick, TcpConfig tcpConfig)
        {
            if (maxMessageSize <= 0 || maxMessageSize > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxMessageSize));
            if (maxMessagesPerTick <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxMessagesPerTick));
            _maxMessageSize = maxMessageSize;
            _maxMessagesPerTick = maxMessagesPerTick;
            _tcpConfig = tcpConfig;
        }

        public static PurrWebClient Create(int maxMessageSize, int maxMessagesPerTick, TcpConfig tcpConfig)
        {
            return new PurrWebClient(maxMessageSize, maxMessagesPerTick, tcpConfig);
        }

        public void Connect(Uri websocketAddress, string webRtcUrl = null)
        {
            if (websocketAddress == null)
                throw new ArgumentNullException(nameof(websocketAddress));

            Stop();
            ++_generation;
            _events.Clear();
            _discardQueuedData = false;
            _webSocketAddress = websocketAddress;
            _active = true;
            isWebRtc = false;
            ConnectionState = ClientState.Connecting;

#if UNITY_WEBGL && !UNITY_EDITOR
            _queuedDataBytes = _queuedDataMessages = 0;
            if (!string.IsNullOrWhiteSpace(webRtcUrl))
            {
                try
                {
                    _rtcId = PurrRtc_Connect(webRtcUrl, _maxMessageSize, OpenCallback, CloseCallback,
                        DataCallback, ErrorCallback, FallbackCallback);
                    Instances.Add(_rtcId, this);
                    return;
                }
                catch (Exception)
                {
                    // Fall back if the browser bridge is unavailable.
                    ReleaseRtc();
                }
            }
#endif
            StartWebSocket();
        }

        private void StartWebSocket()
        {
            if (!_active || ConnectionState != ClientState.Connecting)
                return;

            var generation = _generation;
            try
            {
                _webSocket = SimpleWebClient.Create(_maxMessageSize, _maxMessagesPerTick, _tcpConfig);
                _webSocket.onConnect += () =>
                {
                    if (IsCurrent(generation))
                    {
                        ConnectionState = ClientState.Connected;
                        onConnect?.Invoke();
                    }
                };
                _webSocket.onDisconnect += () =>
                {
                    if (IsCurrent(generation))
                    {
                        _active = false;
                        ConnectionState = ClientState.NotConnected;
                        onDisconnect?.Invoke();
                    }
                };
                _webSocket.onData += segment =>
                {
                    // SimpleWeb pumps these callbacks with its pooled bytes still alive.
                    if (IsCurrent(generation))
                    {
                        receivedDeliveryMethod = 2;
                        onData?.Invoke(segment);
                    }
                };
                _webSocket.onError += exception =>
                {
                    if (IsCurrent(generation))
                        onError?.Invoke(exception);
                };
                _webSocket.Connect(_webSocketAddress);
            }
            catch (Exception exception)
            {
                Enqueue(EventKind.Error, error: exception);
                Disconnect();
            }
        }

        private bool IsCurrent(int generation) => _active && generation == _generation;

        private void Disconnected()
        {
            if (!_active)
                return;
            _active = false;
            ConnectionState = ClientState.NotConnected;
            Enqueue(EventKind.Disconnected);
        }

        public void Disconnect()
        {
            if (!_active)
                return;
            _discardQueuedData = true;
            Disconnected();
            Stop();
        }

        private void Stop()
        {
            _active = false;
#if UNITY_WEBGL && !UNITY_EDITOR
            ReleaseRtc();
#endif
            var socket = _webSocket;
            _webSocket = null;
            socket?.Disconnect();
        }

        public void Send(ArraySegment<byte> data, byte deliveryMethod = 2)
        {
            if (!_active || ConnectionState != ClientState.Connected)
                return;
            if (data.Count > _maxMessageSize)
            {
                Enqueue(EventKind.Error, error: new ArgumentException("Message exceeds the transport size limit."));
                Disconnect();
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            if (isWebRtc)
            {
                PurrRtc_Send(_rtcId, data.Array, data.Offset, data.Count, deliveryMethod);
                return;
            }
#endif
            _webSocket?.Send(data);
        }

        public void ProcessMessageQueue() => ProcessMessageQueue(null);

        public void ProcessMessageQueue(MonoBehaviour behaviour)
        {
            _webSocket?.ProcessMessageQueue(behaviour);
            int count = 0;
            while ((behaviour == null || behaviour.enabled) && count < _maxMessagesPerTick && _events.Count > 0)
            {
                var next = _events.Dequeue();
#if UNITY_WEBGL && !UNITY_EDITOR
                if (next.kind == EventKind.Data)
                {
                    _queuedDataBytes -= next.data.Length;
                    --_queuedDataMessages;
                }
#endif
                if (next.generation != _generation)
                    continue;
                ++count;
                switch (next.kind)
                {
                    case EventKind.Connected:
                        if (!_discardQueuedData)
                            onConnect?.Invoke();
                        break;
                    case EventKind.Disconnected:
                        onDisconnect?.Invoke();
                        break;
                    case EventKind.Data:
                        if (!_discardQueuedData)
                        {
                            receivedDeliveryMethod = next.deliveryMethod;
                            onData?.Invoke(new ArraySegment<byte>(next.data));
                        }
                        break;
                    case EventKind.Error:
                        onError?.Invoke(next.error);
                        break;
                    case EventKind.Fallback:
                        StartWebSocket();
                        break;
                }
            }
        }

        private void Enqueue(EventKind kind, byte[] data = null, Exception error = null, byte deliveryMethod = 2)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (kind == EventKind.Data)
            {
                _queuedDataBytes += data.Length;
                ++_queuedDataMessages;
            }
#endif
            _events.Enqueue(new PendingEvent
            {
                generation = _generation, kind = kind, data = data, error = error, deliveryMethod = deliveryMethod
            });
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        private static readonly Dictionary<int, PurrWebClient> Instances = new Dictionary<int, PurrWebClient>();
        private int _rtcId;
        private int _queuedDataBytes;
        private int _queuedDataMessages;
        private const int MaxQueuedDataBytes = 4 * 1024 * 1024;
        private const int MaxQueuedDataMessages = 10000;

        [DllImport("__Internal")]
        private static extern int PurrRtc_Connect(string endpoint, int maxMessageSize,
            Action<int> opened, Action<int> closed, Action<int, IntPtr, int, int> data,
            Action<int, IntPtr> error, Action<int> fallback);

        [DllImport("__Internal")]
        private static extern void PurrRtc_Disconnect(int id);

        [DllImport("__Internal")]
        private static extern void PurrRtc_Send(int id, byte[] data, int offset, int count, int deliveryMethod);

        private void Connected()
        {
            if (!_active || ConnectionState != ClientState.Connecting)
                return;
            isWebRtc = true;
            ConnectionState = ClientState.Connected;
            Enqueue(EventKind.Connected);
        }

        private void ReleaseRtc()
        {
            if (_rtcId == 0)
                return;
            var id = _rtcId;
            _rtcId = 0;
            Instances.Remove(id);
            PurrRtc_Disconnect(id);
        }

        [MonoPInvokeCallback(typeof(Action<int>))]
        private static void OpenCallback(int id)
        {
            if (Instances.TryGetValue(id, out var client))
                client.Connected();
        }

        [MonoPInvokeCallback(typeof(Action<int>))]
        private static void CloseCallback(int id)
        {
            if (!Instances.TryGetValue(id, out var client))
                return;
            Instances.Remove(id);
            client._rtcId = 0;
            client.Disconnected();
        }

        [MonoPInvokeCallback(typeof(Action<int, IntPtr, int, int>))]
        private static void DataCallback(int id, IntPtr pointer, int count, int deliveryMethod)
        {
            if (!Instances.TryGetValue(id, out var client) || !client._active)
                return;
            if (count < 0 || count > client._maxMessageSize)
            {
                client.Enqueue(EventKind.Error, error: new InvalidOperationException("WebRTC message exceeds the transport size limit."));
                client.Disconnect();
                return;
            }
            if (client._queuedDataBytes + count > MaxQueuedDataBytes || client._queuedDataMessages >= MaxQueuedDataMessages)
            {
                if (deliveryMethod == 1 || deliveryMethod == 4)
                    return;
                client.Enqueue(EventKind.Error, error: new InvalidOperationException("WebRTC receive queue is full."));
                client.Disconnect();
                return;
            }
            var copy = new byte[count];
            if (count > 0)
                Marshal.Copy(pointer, copy, 0, count);
            client.Enqueue(EventKind.Data, copy, deliveryMethod: (byte)deliveryMethod);
        }

        [MonoPInvokeCallback(typeof(Action<int, IntPtr>))]
        private static void ErrorCallback(int id, IntPtr pointer)
        {
            if (Instances.TryGetValue(id, out var client) && client._active)
                client.Enqueue(EventKind.Error, error: new InvalidOperationException(Marshal.PtrToStringAnsi(pointer)));
        }

        [MonoPInvokeCallback(typeof(Action<int>))]
        private static void FallbackCallback(int id)
        {
            if (!Instances.TryGetValue(id, out var client) || !client._active)
                return;
            Instances.Remove(id);
            client._rtcId = 0;
            client.Enqueue(EventKind.Fallback);
        }
#endif
    }
}
