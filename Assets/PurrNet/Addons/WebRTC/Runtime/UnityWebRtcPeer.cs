using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PurrNet.Transports;
using Unity.WebRTC;
using UnityEngine;

namespace PurrNet.WebRTC
{
    internal static class UnityWebRtcPeerRegistration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            var architecture = RuntimeInformation.ProcessArchitecture;
#if UNITY_EDITOR_OSX || (!UNITY_EDITOR && (UNITY_STANDALONE_OSX || UNITY_IOS || UNITY_ANDROID))
            if (architecture != Architecture.Arm64)
                return;
#else
            if (architecture != Architecture.X64)
                return;
#endif
            PurrWebRtcPeerProvider.nativeFactory = () => new UnityWebRtcPeer();
        }
    }

    /// <summary>Optional native data-channel backend. Call its methods on Unity's main thread.</summary>
    public sealed class UnityWebRtcPeer : IPurrWebRtcPeer
    {
        private const int MaxWireBytes = 65535;
        private const int MaxSignalBytes = 32 * 1024;
        private const int MaxCandidateBytes = 4096;
        private const int MaxCandidates = 64;
        private const int MaxSessionSignalBytes = 128 * 1024;
        private const int ReliableBufferBytes = 1024 * 1024;
        private const int UnreliableBufferBytes = 64 * 1024;
        private const int MaxQueuedMessages = 1024;
        private const int MaxEventsPerTick = 256;
        private const double SetupTimeoutSeconds = 8;
        private static readonly byte[] Methods = { 0, 1, 2, 4 };

        public event Action onConnect;
        public event Action onDisconnect;
        public event Action<ArraySegment<byte>> onData;
        public event Action<Exception> onError;
        public event Action<string> onSignal;

        public bool isConnected => _session != null && _session.connected;
        public byte receivedDeliveryMethod { get; private set; } = 2;

        private Session _session;
        private int _generation;
        private readonly Queue<PendingEvent> _events = new Queue<PendingEvent>();
        private int _queuedBytes;
        private int _queuedMessages;

        private enum EventKind { Connected, Disconnected, Data, Error, Signal }
        private enum Step { Idle, CreateDescription, SetLocalDescription, SetRemoteDescription }

        private struct PendingEvent
        {
            public int generation;
            public EventKind kind;
            public byte method;
            public byte[] data;
            public string signal;
            public Exception error;
        }

        private sealed class Session
        {
            public int generation;
            public bool initiator;
            public bool connected;
            public bool descriptionReceived;
            public bool descriptionSent;
            public bool remoteDescriptionSet;
            public RTCPeerConnection peer;
            public readonly RTCDataChannel[] channels = new RTCDataChannel[5];
            public readonly Queue<RTCIceCandidateInit> remoteCandidates = new Queue<RTCIceCandidateInit>();
            public readonly Queue<string> localCandidates = new Queue<string>();
            public readonly Queue<PendingEvent> earlyData = new Queue<PendingEvent>();
            public RTCSessionDescription? remoteDescription;
            public RTCSessionDescription localDescription;
            public AsyncOperationBase operation;
            public Step step;
            public int localCandidateCount;
            public int remoteCandidateCount;
            public int sentSignalBytes;
            public int receivedSignalBytes;
            public uint sendSequence;
            public uint receiveSequence;
            public bool receivedSequence;
            public readonly Stopwatch age = Stopwatch.StartNew();
        }

        public void ConnectPeer(bool initiator, string iceServersJson)
        {
            Release();
            _events.Clear();
            _queuedBytes = _queuedMessages = 0;
            var session = _session = new Session { generation = ++_generation, initiator = initiator };
            try
            {
                var configuration = ParseConfiguration(iceServersJson);
                session.peer = new RTCPeerConnection(ref configuration);
                session.peer.OnIceCandidate = candidate => LocalCandidate(session, candidate);
                session.peer.OnDataChannel = channel => AcceptChannel(session, channel);
                session.peer.OnConnectionStateChange = state =>
                {
                    if (state == RTCPeerConnectionState.Failed || state == RTCPeerConnectionState.Closed)
                        Fail(session, new IOException("WebRTC peer connection closed."));
                };
                session.peer.OnIceConnectionChange = state =>
                {
                    if (state == RTCIceConnectionState.Failed || state == RTCIceConnectionState.Closed)
                        Fail(session, new IOException("WebRTC ICE connection closed."));
                };
                if (initiator)
                {
                    foreach (var method in Methods)
                    {
                        var options = new RTCDataChannelInit
                        {
                            ordered = method == 2,
                            maxRetransmits = IsUnreliable(method) ? 0 : (int?)null
                        };
                        BindChannel(session, method, session.peer.CreateDataChannel("purr-" + method, options));
                    }
                    session.operation = session.peer.CreateOffer();
                    session.step = Step.CreateDescription;
                }
            }
            catch (Exception exception)
            {
                Fail(session, exception);
            }
        }

        public void ReceiveSignal(string signal)
        {
            var session = _session;
            if (session == null)
                return;
            try
            {
                int size = SignalSize(signal);
                if (size > MaxSessionSignalBytes - session.receivedSignalBytes)
                    throw new IOException("WebRTC peer signaling exceeds its size limit.");
                var value = ParseObject(signal);
                string type = RequiredString(value, "type");
                if (type == "candidate")
                {
                    if (++session.remoteCandidateCount > MaxCandidates || size > MaxCandidateBytes ||
                        !(value["candidate"] is JObject candidate))
                        throw new IOException("Invalid WebRTC ICE candidate.");
                    var index = candidate["sdpMLineIndex"];
                    var mid = candidate["sdpMid"];
                    if (index != null && index.Type != JTokenType.Null &&
                        (index.Type != JTokenType.Integer || (long)index < 0 || (long)index > ushort.MaxValue))
                        throw new IOException("Invalid WebRTC ICE candidate index.");
                    if (mid != null && mid.Type != JTokenType.Null && mid.Type != JTokenType.String)
                        throw new IOException("Invalid WebRTC ICE candidate media ID.");
                    session.remoteCandidates.Enqueue(new RTCIceCandidateInit
                    {
                        candidate = RequiredString(candidate, "candidate"),
                        sdpMid = (string)mid,
                        sdpMLineIndex = index == null || index.Type == JTokenType.Null ? null : (int?)index
                    });
                }
                else if (type == (session.initiator ? "answer" : "offer") && !session.descriptionReceived)
                {
                    session.remoteDescription = new RTCSessionDescription
                    {
                        type = session.initiator ? RTCSdpType.Answer : RTCSdpType.Offer,
                        sdp = RequiredString(value, "sdp")
                    };
                    session.descriptionReceived = true;
                }
                else
                    throw new IOException("Unexpected WebRTC peer description.");
                session.receivedSignalBytes += size;
            }
            catch (Exception exception)
            {
                Fail(session, exception);
            }
        }

        public void Send(ArraySegment<byte> data, byte deliveryMethod = 2)
        {
            var session = _session;
            if (session == null || !session.connected)
                return;
            byte method = deliveryMethod == 3 ? (byte)2 : deliveryMethod;
            bool unreliable = IsUnreliable(method);
            if (method > 4 || session.channels[method] == null)
                throw new ArgumentOutOfRangeException(nameof(deliveryMethod));
            int wireLength = data.Count + (method == 1 ? 4 : 0);
            if (wireLength > MaxWireBytes)
            {
                if (!unreliable)
                    Fail(session, new IOException("WebRTC message exceeds its size limit."));
                return;
            }
            try
            {
                var channel = session.channels[method];
                int limit = unreliable ? UnreliableBufferBytes : ReliableBufferBytes;
                if (channel.BufferedAmount + (ulong)wireLength > (ulong)limit)
                {
                    if (!unreliable)
                        Fail(session, new IOException("WebRTC reliable send buffer is full."));
                    return;
                }
                var wire = new byte[wireLength];
                if (method == 1)
                {
                    uint sequence = session.sendSequence;
                    session.sendSequence = unchecked(sequence + 1);
                    for (int i = 0; i < 4; ++i)
                        wire[i] = (byte)(sequence >> (i * 8));
                }
                if (data.Count != 0)
                    Buffer.BlockCopy(data.Array, data.Offset, wire, method == 1 ? 4 : 0, data.Count);
                channel.Send(wire);
            }
            catch (Exception exception)
            {
                Fail(session, exception);
            }
        }

        public void ProcessMessageQueue()
        {
            var session = _session;
            if (session != null)
            {
                try
                {
                    if (!session.connected && session.age.Elapsed.TotalSeconds >= SetupTimeoutSeconds)
                        throw new TimeoutException("WebRTC peer connection timed out.");
                    AdvanceNegotiation(session);
                    CheckOpen(session);
                }
                catch (Exception exception)
                {
                    Fail(session, exception);
                }
            }
            int generation = _generation;
            for (int i = 0; i < MaxEventsPerTick && _events.Count != 0 && generation == _generation; ++i)
            {
                var pending = _events.Dequeue();
                if (pending.kind == EventKind.Data)
                {
                    _queuedBytes -= pending.data.Length;
                    --_queuedMessages;
                }
                if (pending.generation != generation)
                    continue;
                switch (pending.kind)
                {
                    case EventKind.Connected: onConnect?.Invoke(); break;
                    case EventKind.Disconnected: onDisconnect?.Invoke(); break;
                    case EventKind.Error: onError?.Invoke(pending.error); break;
                    case EventKind.Signal: onSignal?.Invoke(pending.signal); break;
                    case EventKind.Data:
                        receivedDeliveryMethod = pending.method;
                        onData?.Invoke(new ArraySegment<byte>(pending.data));
                        break;
                }
            }
        }

        public void Disconnect()
        {
            var session = _session;
            if (session == null)
                return;
            Release();
            _events.Clear();
            _queuedBytes = _queuedMessages = 0;
            Enqueue(session, EventKind.Disconnected);
        }

        private void AdvanceNegotiation(Session session)
        {
            if (session.operation != null)
            {
                if (!session.operation.IsDone)
                    return;
                if (session.operation.IsError)
                    throw new IOException("WebRTC negotiation failed: " + session.operation.Error.message);
                var operation = session.operation;
                session.operation = null;
                switch (session.step)
                {
                    case Step.CreateDescription:
                        session.localDescription = ((RTCSessionDescriptionAsyncOperation)operation).Desc;
                        session.operation = session.peer.SetLocalDescription(ref session.localDescription);
                        session.step = Step.SetLocalDescription;
                        return;
                    case Step.SetLocalDescription:
                        Emit(session, JsonConvert.SerializeObject(new
                        {
                            type = session.initiator ? "offer" : "answer",
                            sdp = session.localDescription.sdp
                        }));
                        session.descriptionSent = true;
                        while (session.localCandidates.Count != 0)
                            Emit(session, session.localCandidates.Dequeue(), true);
                        break;
                    case Step.SetRemoteDescription:
                        session.remoteDescriptionSet = true;
                        if (!session.initiator)
                        {
                            session.operation = session.peer.CreateAnswer();
                            session.step = Step.CreateDescription;
                        }
                        break;
                }
            }
            if (session.remoteDescriptionSet)
            {
                while (session.remoteCandidates.Count != 0)
                {
                    using var candidate = new RTCIceCandidate(session.remoteCandidates.Dequeue());
                    if (!session.peer.AddIceCandidate(candidate))
                        throw new IOException("WebRTC rejected an ICE candidate.");
                }
            }
            if (session.operation == null && session.remoteDescription.HasValue && !session.remoteDescriptionSet)
            {
                var description = session.remoteDescription.Value;
                session.operation = session.peer.SetRemoteDescription(ref description);
                session.step = Step.SetRemoteDescription;
            }
        }

        private void LocalCandidate(Session session, RTCIceCandidate candidate)
        {
            using (candidate)
            {
                if (!IsCurrent(session) || candidate == null)
                    return;
                try
                {
                    if (string.IsNullOrEmpty(candidate.Candidate))
                        return;
                    string signal = JsonConvert.SerializeObject(new
                    {
                        type = "candidate",
                        candidate = new
                        {
                            candidate = candidate.Candidate,
                            sdpMid = candidate.SdpMid,
                            sdpMLineIndex = candidate.SdpMLineIndex,
                            usernameFragment = candidate.UserNameFragment
                        }
                    });
                    if (++session.localCandidateCount > MaxCandidates || SignalSize(signal) > MaxCandidateBytes)
                        throw new IOException("WebRTC peer has too many ICE candidates.");
                    if (session.descriptionSent)
                        Emit(session, signal);
                    else
                    {
                        ReserveSignalBytes(session, signal);
                        session.localCandidates.Enqueue(signal);
                    }
                }
                catch (Exception exception)
                {
                    Fail(session, exception);
                }
            }
        }

        private void AcceptChannel(Session session, RTCDataChannel channel)
        {
            if (!IsCurrent(session))
            {
                channel.Dispose();
                return;
            }
            try
            {
                int method = Array.FindIndex(Methods, item => channel.Label == "purr-" + item);
                if (session.initiator || method < 0)
                    throw new IOException("Unexpected WebRTC peer data channel.");
                byte delivery = Methods[method];
                // Unity represents unset retransmission limits as ushort.MaxValue.
                if (session.channels[delivery] != null || channel.Ordered != (delivery == 2) ||
                    channel.MaxRetransmits != (IsUnreliable(delivery) ? 0 : ushort.MaxValue) ||
                    channel.MaxRetransmitTime != ushort.MaxValue)
                    throw new IOException("Unexpected WebRTC peer data channel options.");
                BindChannel(session, delivery, channel);
            }
            catch (Exception exception)
            {
                channel.Dispose();
                Fail(session, exception);
            }
        }

        private void BindChannel(Session session, byte method, RTCDataChannel channel)
        {
            session.channels[method] = channel;
            channel.OnOpen = () => CheckOpen(session);
            channel.OnClose = () => Fail(session, new IOException("WebRTC peer data channel closed."));
            channel.OnMessage = data => ReceiveData(session, method, data);
        }

        private void CheckOpen(Session session)
        {
            if (!IsCurrent(session) || session.connected)
                return;
            foreach (var method in Methods)
                if (session.channels[method] == null || session.channels[method].ReadyState != RTCDataChannelState.Open)
                    return;
            session.connected = true;
            Enqueue(session, EventKind.Connected);
            while (session.earlyData.Count != 0)
                _events.Enqueue(session.earlyData.Dequeue());
        }

        private void ReceiveData(Session session, byte method, byte[] wire)
        {
            if (!IsCurrent(session))
                return;
            int prefix = method == 1 ? 4 : 0;
            if (wire.Length > MaxWireBytes || wire.Length < prefix)
            {
                Fail(session, new IOException("Invalid WebRTC peer message size."));
                return;
            }
            if (method == 1)
            {
                uint sequence = unchecked((uint)(wire[0] | wire[1] << 8 | wire[2] << 16 | wire[3] << 24));
                uint delta = unchecked(sequence - session.receiveSequence);
                if (session.receivedSequence && (delta == 0 || delta >= 0x80000000u))
                    return;
                session.receivedSequence = true;
                session.receiveSequence = sequence;
            }
            int length = wire.Length - prefix;
            if (!session.connected && IsUnreliable(method))
                return;
            if (_queuedMessages >= MaxQueuedMessages || length > ReliableBufferBytes - _queuedBytes)
            {
                if (!IsUnreliable(method))
                    Fail(session, new IOException("WebRTC reliable receive queue is full."));
                return;
            }
            var data = new byte[length];
            Buffer.BlockCopy(wire, prefix, data, 0, length);
            ++_queuedMessages;
            _queuedBytes += length;
            var pending = new PendingEvent
            {
                generation = session.generation, kind = EventKind.Data, method = method, data = data
            };
            if (session.connected)
                _events.Enqueue(pending);
            else
                session.earlyData.Enqueue(pending);
        }

        private void Emit(Session session, string signal, bool reserved = false)
        {
            if (!reserved)
                ReserveSignalBytes(session, signal);
            _events.Enqueue(new PendingEvent
            {
                generation = session.generation, kind = EventKind.Signal, signal = signal
            });
        }

        private static void ReserveSignalBytes(Session session, string signal)
        {
            int size = SignalSize(signal);
            if (size > MaxSessionSignalBytes - session.sentSignalBytes)
                throw new IOException("WebRTC peer signaling exceeds its size limit.");
            session.sentSignalBytes += size;
        }

        private void Fail(Session session, Exception exception)
        {
            if (!IsCurrent(session))
                return;
            Release();
            _events.Clear();
            _queuedBytes = _queuedMessages = 0;
            _events.Enqueue(new PendingEvent
            {
                generation = session.generation, kind = EventKind.Error, error = exception
            });
            Enqueue(session, EventKind.Disconnected);
        }

        private void Release()
        {
            var session = _session;
            _session = null;
            if (session == null)
                return;
            foreach (var channel in session.channels)
            {
                if (channel == null)
                    continue;
                channel.OnOpen = null;
                channel.OnClose = null;
                channel.OnMessage = null;
                channel.Dispose();
            }
            session.peer?.Dispose();
        }

        private bool IsCurrent(Session session) => ReferenceEquals(_session, session);
        private static bool IsUnreliable(byte method) => method == 1 || method == 4;
        private void Enqueue(Session session, EventKind kind) => _events.Enqueue(new PendingEvent
        {
            generation = session.generation, kind = kind
        });

        private static int SignalSize(string signal)
        {
            if (signal == null || signal.Length > MaxSignalBytes || Encoding.UTF8.GetByteCount(signal) > MaxSignalBytes)
                throw new IOException("WebRTC peer signaling exceeds its size limit.");
            return Encoding.UTF8.GetByteCount(signal);
        }

        private static JObject ParseObject(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 8, DateParseHandling = DateParseHandling.None };
            var value = JObject.Load(reader);
            if (reader.Read())
                throw new IOException("Unexpected data after WebRTC peer signal.");
            return value;
        }

        private static string RequiredString(JObject value, string name)
        {
            if (value[name]?.Type != JTokenType.String || string.IsNullOrEmpty((string)value[name]))
                throw new IOException("Invalid WebRTC " + name + ".");
            return (string)value[name];
        }

        private static RTCConfiguration ParseConfiguration(string json)
        {
            json = string.IsNullOrEmpty(json) ? "[]" : json;
            if (json.Length > 16 * 1024 || Encoding.UTF8.GetByteCount(json) > 16 * 1024)
                throw new IOException("WebRTC ICE configuration exceeds its size limit.");
            var configuration = ParseObject("{\"servers\":" + json + "}");
            if (!(configuration["servers"] is JArray servers) || servers.Count > 8)
                throw new IOException("Invalid WebRTC ICE configuration.");
            var parsed = new RTCIceServer[servers.Count];
            for (int i = 0; i < servers.Count; ++i)
            {
                if (!(servers[i] is JObject server))
                    throw new IOException("Invalid WebRTC ICE server.");
                var urls = server["urls"];
                var urlList = urls?.Type == JTokenType.String ? new JArray(urls) : urls as JArray;
                if (urlList == null || urlList.Count == 0 || urlList.Count > 16)
                    throw new IOException("Invalid WebRTC ICE server URLs.");
                var parsedUrls = new string[urlList.Count];
                for (int j = 0; j < parsedUrls.Length; ++j)
                {
                    if (urlList[j].Type != JTokenType.String || string.IsNullOrWhiteSpace((string)urlList[j]))
                        throw new IOException("Invalid WebRTC ICE server URL.");
                    parsedUrls[j] = (string)urlList[j];
                }
                parsed[i] = new RTCIceServer
                {
                    urls = parsedUrls,
                    username = (string)server["username"],
                    credential = (string)server["credential"]
                };
            }
            return new RTCConfiguration { iceServers = parsed, bundlePolicy = RTCBundlePolicy.BundlePolicyMaxBundle };
        }
    }
}
