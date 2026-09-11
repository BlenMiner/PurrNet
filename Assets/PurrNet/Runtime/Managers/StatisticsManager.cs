using System;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Statistics Manager")]
    public partial class StatisticsManager : MonoBehaviour
    {
        [Range(0.05f, 1f)] public float checkInterval = 0.33f;
        [SerializeField] private StatisticsPlacement placement = StatisticsPlacement.None;
        [SerializeField] private StatisticsDisplayType _displayType = StatisticsDisplayType.Ping | StatisticsDisplayType.Usage;
        [SerializeField] private StatisticsDisplayTarget _displayTarget = StatisticsDisplayTarget.Editor | StatisticsDisplayTarget.Build;
        [SerializeField] private float fontSize = 13f;
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private int _highPingThreshold = 250;
        [SerializeField] private int _highPingRecoveryThreshold = 180;
        [SerializeField] private int _highJitterThreshold = 80;
        [SerializeField] private int _highJitterRecoveryThreshold = 50;
        [SerializeField, Range(0, 100)] private int _highPacketLossThreshold = 10;
        [SerializeField, Range(0, 100)] private int _highPacketLossRecoveryThreshold = 5;
        [SerializeField] private float _qualityChangeDuration = 2f;
        [SerializeField] private float _connectionStallThreshold = 2f;

        public int ping { get; private set; }
        public int jitter { get; private set; }
        public int packetLoss { get; private set; }
        public float upload { get; private set; }
        public float download { get; private set; }
        /// <summary>
        /// False until the warmup has enough clean samples to publish a ping. While it is false
        /// <see cref="ping"/> and <see cref="jitter"/> are 0 and no quality flag can raise, so a UI
        /// can hide a readout rather than show a zero it would have to explain.
        /// </summary>
        [PublicAPI] public bool hasPingEstimate { get; private set; }

        [PublicAPI] public bool isHighPing => _isHighPing;
        [PublicAPI] public bool isHighJitter => _isHighJitter;
        [PublicAPI] public bool isHighPacketLoss => _isHighPacketLoss;
        [PublicAPI] public bool isConnectionStalled { get; private set; }

        public delegate void HighPingChanged(bool isHigh, int ping);
        public delegate void HighJitterChanged(bool isHigh, int jitter);
        public delegate void HighPacketLossChanged(bool isHigh, int packetLoss);
        public delegate void ConnectionStalledChanged(bool isStalled, float secondsSinceLastReceived);

        [PublicAPI] public event HighPingChanged onHighPingChanged;
        [PublicAPI] public event HighJitterChanged onHighJitterChanged;
        [PublicAPI] public event HighPacketLossChanged onHighPacketLossChanged;
        [PublicAPI] public event ConnectionStalledChanged onConnectionStalledChanged;

        private NetworkManager _networkManager;
        private PlayersBroadcaster _playersClientBroadcaster;
        private PlayersBroadcaster _playersServerBroadcaster;
        private TickManager _tickManager;
        private GUIStyle _labelStyle;
        private const int PADDING = 10;
        private float LineHeight => fontSize * 1.25f;

        public bool connectedServer { get; private set; }
        public bool connectedClient { get; private set; }

        private const float PING_EMA_RISE_ALPHA = 0.15f;
        private const float PING_EMA_FALL_ALPHA = 0.1f;
        private const float JITTER_EMA_ALPHA = 0.0625f;
        private const float WARMUP_DURATION = 1.0f;

        private const int PING_WARMUP_SAMPLES = 5;
        private const float STALL_FRAME_SECONDS = 0.25f;

        private float _emaPing;
        private float _connectionTime;
        private int _lastRawPing;
        private float _emaJitter;

        private readonly int[] _warmupSamples = new int[PING_WARMUP_SAMPLES];
        private readonly int[] _warmupScratch = new int[PING_WARMUP_SAMPLES];
        private int _warmupWrites;
        private float _lastFrameRealtime = -1f;
        private float _lastStallRealtime = -1f;

        private const int MAX_SEQUENCE_TRACKING = 256;
        private const float PACKET_LOSS_WINDOW = 5f;
        private const float MIN_INFLIGHT_GRACE = 0.5f;
        private const float PACKET_LOSS_WARMUP = 3f;
        private const int DEFAULT_PACKETS_PER_SEC = 20;
        private const uint MAX_VALID_PING_MS = 60000;

        private readonly uint[] _seqIds = new uint[MAX_SEQUENCE_TRACKING];
        private readonly float[] _seqSendTimes = new float[MAX_SEQUENCE_TRACKING];
        private readonly bool[] _seqAcked = new bool[MAX_SEQUENCE_TRACKING];
        private int _seqHead;
        private int _seqCount;
        private uint _packetSequence;

        private int _packetsToSendPerSec = DEFAULT_PACKETS_PER_SEC;
        private float _lastPacketSendTime;
        private float _lastPingSendTime;

        private float _totalDataReceived;
        private float _totalDataSent;
        private float _lastDataCheckTime;
        private float _lastClientDataReceivedTime;

        private bool _isHighPing;
        private bool _isHighJitter;
        private bool _isHighPacketLoss;
        private float _highPingTransitionStarted = -1f;
        private float _highJitterTransitionStarted = -1f;
        private float _highPacketLossTransitionStarted = -1f;
        private int _highPingTransitionTicks;
        private int _highJitterTransitionTicks;
        private int _highPacketLossTransitionTicks;

        private const int MIN_QUALITY_TICKS = 3;

        private int _cachedPing = -1;
        private int _cachedJitter = -1;
        private int _cachedPacketLoss = -1;
        private float _cachedUpload = -1f;
        private float _cachedDownload = -1f;

        private readonly char[] _charBuffer = new char[64];
        private string _cachedPingText = "Ping: 0ms";
        private string _cachedJitterText = "Jitter: 0ms";
        private string _cachedPacketLossText = "Packet Loss: 0%";
        private string _cachedUploadText = "Upload: 0.000KB/s";
        private string _cachedDownloadText = "Download: 0.000KB/s";

        private bool _labelStyleInitialized;

        private void Awake()
        {
            _networkManager = NetworkManager.main;
            _networkManager.onServerConnectionState += OnServerConnectionState;
            _networkManager.onClientConnectionState += OnClientConnectionState;
        }

        private void OnEnable()
        {
            PurrOnGUI.Subscribe(DrawStatisticsGUI);
        }

        private void OnDisable()
        {
            PurrOnGUI.Unsubscribe(DrawStatisticsGUI);
        }

        private void Start()
        {
            if (!_networkManager)
            {
                PurrLogger.LogError($"StatisticsManager failed to find a NetworkManager in the scene. Disabling...");
                enabled = false;
                return;
            }

            EnsureLabelStyle();
        }

        private void OnValidate()
        {
            _highPingThreshold = Mathf.Max(0, _highPingThreshold);
            _highPingRecoveryThreshold = Mathf.Clamp(_highPingRecoveryThreshold, 0, _highPingThreshold);
            _highJitterThreshold = Mathf.Max(0, _highJitterThreshold);
            _highJitterRecoveryThreshold = Mathf.Clamp(_highJitterRecoveryThreshold, 0, _highJitterThreshold);
            _highPacketLossThreshold = Mathf.Clamp(_highPacketLossThreshold, 0, 100);
            _highPacketLossRecoveryThreshold = Mathf.Clamp(_highPacketLossRecoveryThreshold, 0, _highPacketLossThreshold);
            _qualityChangeDuration = Mathf.Max(0f, _qualityChangeDuration);
            _connectionStallThreshold = Mathf.Max(0f, _connectionStallThreshold);

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                _labelStyleInitialized = false;
                EnsureLabelStyle();
            }
#endif
        }

        private void EnsureLabelStyle()
        {
            if (_labelStyleInitialized && _labelStyle != null)
            {
                _labelStyle.fontSize = Mathf.RoundToInt(fontSize);
                _labelStyle.normal.textColor = textColor;
                _labelStyle.alignment = (placement == StatisticsPlacement.TopRight || placement == StatisticsPlacement.BottomRight)
                    ? TextAnchor.UpperRight
                    : TextAnchor.UpperLeft;
                return;
            }

            _labelStyle = new GUIStyle
            {
                fontSize = Mathf.RoundToInt(fontSize),
                normal = { textColor = textColor },
                alignment = (placement == StatisticsPlacement.TopRight || placement == StatisticsPlacement.BottomRight)
                    ? TextAnchor.UpperRight
                    : TextAnchor.UpperLeft
            };
            _labelStyleInitialized = true;
        }

        private void OnDestroy()
        {
            if (_networkManager)
            {
                _networkManager.onServerConnectionState -= OnServerConnectionState;
                _networkManager.onClientConnectionState -= OnClientConnectionState;
                var rt = _networkManager.rawTransport;
                if (rt != null)
                {
                    rt.onDataReceived -= OnDataReceived;
                    rt.onDataSent -= OnDataSent;
                }
            }

            if (_playersServerBroadcaster != null)
            {
                _playersServerBroadcaster.Unsubscribe<PingMessage>(ReceivePing);
                _playersServerBroadcaster.Unsubscribe<PacketMessage>(ReceivePacket);
            }

            if (_playersClientBroadcaster != null)
            {
                _playersClientBroadcaster.Unsubscribe<PingMessage>(ReceivePing);
                _playersClientBroadcaster.Unsubscribe<PacketMessage>(ReceivePacket);
            }

            ServerUnsubscribe_ServerStats();
            ClientUnsubscribe_ServerStats();
        }

        private void DrawStatisticsGUI()
        {
            var requiredTarget = Application.isEditor ? StatisticsDisplayTarget.Editor : StatisticsDisplayTarget.Build;
            if (!_displayTarget.HasFlag(requiredTarget))
                return;

            if (placement == StatisticsPlacement.None || (!connectedClient && !connectedServer))
                return;

            EnsureLabelStyle();
            UpdateCachedStrings();

            var labelWidth = GetStatsWidth();
            var position = GetPosition(labelWidth);
            Rect rect = new(position.x, position.y, labelWidth, LineHeight);

            if (_displayType.HasFlag(StatisticsDisplayType.Connection))
            {
                if (connectedClient)
                    DrawConnectionLabel(ref rect, _cachedClientConnectionText);

                if (connectedServer)
                    DrawConnectionLabel(ref rect, _cachedServerConnectionText);
            }

            if (connectedClient && _displayType.HasFlag(StatisticsDisplayType.Ping))
            {
                GUI.Label(rect, _cachedPingText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedJitterText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedPacketLossText, _labelStyle);
                rect.y += LineHeight;
            }

            if (_displayType.HasFlag(StatisticsDisplayType.Usage))
            {
                GUI.Label(rect, _cachedUploadText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedDownloadText, _labelStyle);
                rect.y += LineHeight;
            }

            if (connectedClient && _displayType.HasFlag(StatisticsDisplayType.ServerStats))
            {
                GUI.Label(rect, "Server Stats:", _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedServerMaxFpsText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedServerAvgFpsText, _labelStyle);
                rect.y += LineHeight;
                GUI.Label(rect, _cachedServerMinFpsText, _labelStyle);
                rect.y += LineHeight;
            }

            if (_displayType.HasFlag(StatisticsDisplayType.Version))
                GUI.Label(rect, "Version: " + NetworkManager.version, _labelStyle);
        }

        private void UpdateCachedStrings()
        {
            if (_displayType.HasFlag(StatisticsDisplayType.Connection))
                UpdateConnectionStrings();

            if (ping != _cachedPing)
            {
                _cachedPing = ping;
                _cachedPingText = FormatStat("Ping: ", ping, "ms");
            }

            if (jitter != _cachedJitter)
            {
                _cachedJitter = jitter;
                _cachedJitterText = FormatStat("Jitter: ", jitter, "ms");
            }

            if (packetLoss != _cachedPacketLoss)
            {
                _cachedPacketLoss = packetLoss;
                _cachedPacketLossText = FormatStat("Packet Loss: ", packetLoss, "%");
            }

            if (!Mathf.Approximately(upload, _cachedUpload))
            {
                _cachedUpload = upload;
                _cachedUploadText = FormatStatFloat("Upload: ", upload, "KB/s");
            }

            if (!Mathf.Approximately(download, _cachedDownload))
            {
                _cachedDownload = download;
                _cachedDownloadText = FormatStatFloat("Download: ", download, "KB/s");
            }

            UpdateCachedStrings_ServerStats();
        }

        private string FormatStat(string prefix, int value, string suffix)
        {
            int pos = 0;

            for (int i = 0; i < prefix.Length; i++)
                _charBuffer[pos++] = prefix[i];

            pos = WriteInt(_charBuffer, pos, value);

            for (int i = 0; i < suffix.Length; i++)
                _charBuffer[pos++] = suffix[i];

            return new string(_charBuffer, 0, pos);
        }

        private string FormatStatFloat(string prefix, float value, string suffix)
        {
            int pos = 0;

            for (int i = 0; i < prefix.Length; i++)
                _charBuffer[pos++] = prefix[i];

            int intPart = (int)value;
            int fracPart = Mathf.Abs((int)((value - intPart) * 1000));

            pos = WriteInt(_charBuffer, pos, intPart);
            _charBuffer[pos++] = '.';

            if (fracPart < 100) _charBuffer[pos++] = '0';
            if (fracPart < 10) _charBuffer[pos++] = '0';
            pos = WriteInt(_charBuffer, pos, fracPart);

            for (int i = 0; i < suffix.Length; i++)
                _charBuffer[pos++] = suffix[i];

            return new string(_charBuffer, 0, pos);
        }

        private static int WriteInt(char[] buffer, int pos, int value)
        {
            if (value < 0)
            {
                buffer[pos++] = '-';
                value = -value;
            }

            if (value == 0)
            {
                buffer[pos++] = '0';
                return pos;
            }

            int start = pos;
            while (value > 0)
            {
                buffer[pos++] = (char)('0' + value % 10);
                value /= 10;
            }

            for (int i = start, j = pos - 1; i < j; i++, j--)
                (buffer[i], buffer[j]) = (buffer[j], buffer[i]);

            return pos;
        }

        private Vector2 GetPosition(float labelWidth)
        {
            var x = placement switch
            {
                StatisticsPlacement.TopLeft or StatisticsPlacement.BottomLeft => PADDING,
                _ => Screen.width - labelWidth - PADDING
            };

            var y = placement switch
            {
                StatisticsPlacement.TopLeft or StatisticsPlacement.TopRight => PADDING,
                _ => Screen.height - GetStatsHeight(labelWidth) - PADDING
            };

            return new Vector2(x, y);
        }

        private int GetStatsHeight(float labelWidth)
        {
            int lines = 0;
            float connectionHeight = 0f;

            if (_displayType.HasFlag(StatisticsDisplayType.Connection))
            {
                if (connectedClient)
                    connectionHeight += GetConnectionHeight(_cachedClientConnectionText, labelWidth);
                if (connectedServer)
                    connectionHeight += GetConnectionHeight(_cachedServerConnectionText, labelWidth);
            }

            if (connectedClient && _displayType.HasFlag(StatisticsDisplayType.Ping))
                lines += 3;

            if (_displayType.HasFlag(StatisticsDisplayType.Usage))
                lines += 2;

            if (connectedClient && _displayType.HasFlag(StatisticsDisplayType.ServerStats))
                lines += 4;

            if (_displayType.HasFlag(StatisticsDisplayType.Version))
                lines++;

            return Mathf.CeilToInt(connectionHeight + LineHeight * lines);
        }

        private void Update()
        {
            TrackLocalStalls();

            if (Time.unscaledTime - _lastDataCheckTime >= 1f)
            {
                download = _totalDataReceived / 1024f;
                upload = _totalDataSent / 1024f;
                _totalDataReceived = 0;
                _totalDataSent = 0;
                _lastDataCheckTime = Time.unscaledTime;
            }

            if (connectedClient)
            {
                CalculatePacketLoss();
                UpdateNetworkQuality();
            }

            ServerStatsUpdate();
        }

        private void OnServerConnectionState(ConnectionState state)
        {
            connectedServer = state == ConnectionState.Connected;

            switch (state)
            {
                case ConnectionState.Disconnected:
                    if (_playersServerBroadcaster == null)
                        return;
                    _playersServerBroadcaster.Unsubscribe<PingMessage>(ReceivePing);
                    _playersServerBroadcaster.Unsubscribe<PacketMessage>(ReceivePacket);
                    _playersServerBroadcaster = null;
                    if (_networkManager.TryGetModule<PlayersManager>(true, out var serverPlayersCleanup))
                    {
                        serverPlayersCleanup.UnregisterImmediateType<PingMessage>();
                        serverPlayersCleanup.UnregisterImmediateType<PacketMessage>();
                    }
                    var rt = _networkManager.rawTransport;
                    if (rt != null)
                    {
                        rt.onDataReceived -= OnDataReceived;
                        rt.onDataSent -= OnDataSent;
                    }
                    ServerUnsubscribe_ServerStats();
                    return;
                case ConnectionState.Connected:
                    _playersServerBroadcaster = _networkManager.GetModule<PlayersBroadcaster>(true);
                    _playersServerBroadcaster.Subscribe<PingMessage>(ReceivePing);
                    _playersServerBroadcaster.Subscribe<PacketMessage>(ReceivePacket);
                    if (_networkManager.TryGetModule<PlayersManager>(true, out var serverPlayers))
                    {
                        serverPlayers.RegisterImmediateType<PingMessage>();
                        serverPlayers.RegisterImmediateType<PacketMessage>();
                    }
                    _networkManager.rawTransport.onDataReceived += OnDataReceived;
                    _networkManager.rawTransport.onDataSent += OnDataSent;
                    ServerSubscribe_ServerStats();
                    break;
                case ConnectionState.Connecting:
                case ConnectionState.Disconnecting:
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        private void OnClientConnectionState(ConnectionState state)
        {
            if (!_networkManager.TryGetModule<TickManager>(false, out _tickManager))
                return;

            _playersClientBroadcaster = _networkManager.GetModule<PlayersBroadcaster>(false);

            connectedClient = state == ConnectionState.Connected;

            if (state != ConnectionState.Connected)
            {
                _playersClientBroadcaster.Unsubscribe<PingMessage>(ReceivePing);
                _playersClientBroadcaster.Unsubscribe<PacketMessage>(ReceivePacket);
                if (_networkManager.TryGetModule<PlayersManager>(false, out var clientPlayersCleanup))
                {
                    clientPlayersCleanup.UnregisterImmediateType<PingMessage>();
                    clientPlayersCleanup.UnregisterImmediateType<PacketMessage>();
                }
                if (!connectedServer)
                {
                    var rt = _networkManager.rawTransport;
                    if (rt != null)
                    {
                        rt.onDataReceived -= OnDataReceived;
                        rt.onDataSent -= OnDataSent;
                    }
                }

                ClientUnsubscribe_ServerStats();
                ResetStatistics();
                return;
            }

            _playersClientBroadcaster.Subscribe<PingMessage>(ReceivePing);
            _playersClientBroadcaster.Subscribe<PacketMessage>(ReceivePacket);
            if (_networkManager.TryGetModule<PlayersManager>(false, out var clientPlayers))
            {
                clientPlayers.RegisterImmediateType<PingMessage>();
                clientPlayers.RegisterImmediateType<PacketMessage>();
            }

            if (!connectedServer)
            {
                _networkManager.rawTransport.onDataReceived += OnDataReceived;
                _networkManager.rawTransport.onDataSent += OnDataSent;
            }

            _packetsToSendPerSec = DEFAULT_PACKETS_PER_SEC;
            if (_tickManager.tickRate < _packetsToSendPerSec)
                _packetsToSendPerSec = Mathf.Max(5, _tickManager.tickRate / 2);

            ClientSubscribe_ServerStats();
            ResetStatistics();
        }

        private void ResetStatistics()
        {
            ping = 0;
            jitter = 0;
            packetLoss = 0;
            _emaPing = 0;
            hasPingEstimate = false;
            _warmupWrites = 0;
            _connectionTime = Time.unscaledTime;
            _lastRawPing = 0;
            _emaJitter = 0;
            _seqHead = 0;
            _seqCount = 0;
            _packetSequence = 0;
            _lastPingSendTime = 0;
            _lastPacketSendTime = 0;
            _lastClientDataReceivedTime = Time.unscaledTime;

            for (int i = 0; i < MAX_SEQUENCE_TRACKING; i++)
            {
                _seqSendTimes[i] = 0;
                _seqAcked[i] = false;
            }

            _cachedPing = -1;
            _cachedJitter = -1;
            _cachedPacketLoss = -1;
            _cachedUpload = -1f;
            _cachedDownload = -1f;

            ResetNetworkQuality();
            ResetStatistics_ServerStats();
        }

        private void LateUpdate()
        {
            if (!connectedClient || _playersClientBroadcaster == null)
                return;

            float now = Time.unscaledTime;

            if (now - _lastPingSendTime >= checkInterval)
                SendPingCheck(now);

            if (now - _lastPacketSendTime >= 1f / _packetsToSendPerSec)
                SendPacketCheck(now);
        }

        private static uint NowMilliseconds()
        {
            return (uint)(Time.unscaledTimeAsDouble * 1000.0);
        }

        private void SendPingCheck(float now)
        {
            _playersClientBroadcaster.SendToServer(
                new PingMessage {
                    sendTime = NowMilliseconds(),
                    realSendTime = Time.realtimeSinceStartup
                },
                Channel.Unreliable);
            _lastPingSendTime = now;
        }

        private void ReceivePing(PlayerID sender, PingMessage msg, bool asServer)
        {
            if (asServer)
            {
                _playersServerBroadcaster.Send(sender,
                    new PingMessage {
                        sendTime = msg.sendTime,
                        realSendTime = msg.realSendTime
                    },
                    Channel.Unreliable);
                return;
            }

            if (_lastStallRealtime >= msg.realSendTime)
                return;

            uint elapsedMs = NowMilliseconds() - msg.sendTime;
            if (elapsedMs > MAX_VALID_PING_MS)
                return;

            int currentPing = (int)elapsedMs;

            if (!hasPingEstimate)
            {
                CollectWarmupSample(currentPing);
                return;
            }

            int diff = Mathf.Abs(currentPing - _lastRawPing);
            _emaJitter += JITTER_EMA_ALPHA * (diff - _emaJitter);
            _lastRawPing = currentPing;

            float alpha = currentPing > _emaPing ? PING_EMA_RISE_ALPHA : PING_EMA_FALL_ALPHA;
            _emaPing = alpha * currentPing + (1f - alpha) * _emaPing;

            ping = Mathf.RoundToInt(_emaPing);
            jitter = Mathf.RoundToInt(_emaJitter);
        }

        private void CollectWarmupSample(int currentPing)
        {
            _warmupSamples[_warmupWrites % PING_WARMUP_SAMPLES] = currentPing;
            _warmupWrites++;

            if (_warmupWrites < PING_WARMUP_SAMPLES ||
                Time.unscaledTime - _connectionTime < WARMUP_DURATION)
            {
                return;
            }

            _emaPing = MedianWarmupSample();
            _lastRawPing = Mathf.RoundToInt(_emaPing);
            _emaJitter = 0f;
            hasPingEstimate = true;

            ping = Mathf.RoundToInt(_emaPing);
            jitter = 0;
        }

        private int MedianWarmupSample()
        {
            Array.Copy(_warmupSamples, _warmupScratch, PING_WARMUP_SAMPLES);

            for (int i = 1; i < PING_WARMUP_SAMPLES; i++)
            {
                int value = _warmupScratch[i];
                int j = i - 1;

                while (j >= 0 && _warmupScratch[j] > value)
                {
                    _warmupScratch[j + 1] = _warmupScratch[j];
                    j--;
                }

                _warmupScratch[j + 1] = value;
            }

            return _warmupScratch[PING_WARMUP_SAMPLES / 2];
        }

        private void TrackLocalStalls()
        {
            float realtime = Time.realtimeSinceStartup;

            if (_lastFrameRealtime >= 0f && realtime - _lastFrameRealtime >= STALL_FRAME_SECONDS)
                _lastStallRealtime = realtime;

            _lastFrameRealtime = realtime;
        }

        private void SendPacketCheck(float now)
        {
            _lastPacketSendTime = now;

            int idx = _seqHead;
            _seqIds[idx] = _packetSequence;
            _seqSendTimes[idx] = now;
            _seqAcked[idx] = false;
            _seqHead = (_seqHead + 1) % MAX_SEQUENCE_TRACKING;
            if (_seqCount < MAX_SEQUENCE_TRACKING)
                _seqCount++;

            _playersClientBroadcaster.SendToServer(new PacketMessage { sequenceId = _packetSequence++ }, Channel.Unreliable);
        }

        private void CalculatePacketLoss()
        {
            float now = Time.unscaledTime;
            float gracePeriod = Mathf.Max(MIN_INFLIGHT_GRACE, (_emaPing / 1000f) * 3f);
            float graceThreshold = now - gracePeriod;
            float windowStart = now - PACKET_LOSS_WINDOW;

            if (now - _connectionTime < PACKET_LOSS_WARMUP)
            {
                packetLoss = 0;
                return;
            }

            int totalSettled = 0;
            int totalLost = 0;

            for (int i = 0; i < _seqCount; i++)
            {
                float sendTime = _seqSendTimes[i];

                if (sendTime < windowStart || sendTime > graceThreshold)
                    continue;

                totalSettled++;
                if (!_seqAcked[i])
                    totalLost++;
            }

            packetLoss = totalSettled > 0 ? Mathf.Clamp(totalLost * 100 / totalSettled, 0, 100) : 0;
        }

        private void UpdateNetworkQuality()
        {
            float now = Time.unscaledTime;

            if (hasPingEstimate)
            {
                if (UpdateQualityState(ping, _highPingThreshold, _highPingRecoveryThreshold, now, ref _isHighPing, ref _highPingTransitionStarted, ref _highPingTransitionTicks))
                    onHighPingChanged?.Invoke(_isHighPing, ping);

                if (UpdateQualityState(jitter, _highJitterThreshold, _highJitterRecoveryThreshold, now, ref _isHighJitter, ref _highJitterTransitionStarted, ref _highJitterTransitionTicks))
                    onHighJitterChanged?.Invoke(_isHighJitter, jitter);
            }

            if (UpdateQualityState(packetLoss, _highPacketLossThreshold, _highPacketLossRecoveryThreshold, now, ref _isHighPacketLoss, ref _highPacketLossTransitionStarted, ref _highPacketLossTransitionTicks))
                onHighPacketLossChanged?.Invoke(_isHighPacketLoss, packetLoss);

            UpdateConnectionStall(now);
        }

        private bool UpdateQualityState(int value, int threshold, int recoveryThreshold, float now, ref bool isActive, ref float transitionStarted, ref int transitionTicks)
        {
            bool shouldChange = isActive ? value <= recoveryThreshold : value >= threshold;

            if (!shouldChange)
            {
                transitionStarted = -1f;
                transitionTicks = 0;
                return false;
            }

            if (_qualityChangeDuration <= 0f)
            {
                isActive = !isActive;
                transitionStarted = -1f;
                transitionTicks = 0;
                return true;
            }

            if (transitionStarted < 0f)
            {
                transitionStarted = now;
                transitionTicks = 0;
            }

            transitionTicks++;

            if (now - transitionStarted < _qualityChangeDuration || transitionTicks < MIN_QUALITY_TICKS)
                return false;

            isActive = !isActive;
            transitionStarted = -1f;
            transitionTicks = 0;
            return true;
        }

        private void UpdateConnectionStall(float now)
        {
            if (_connectionStallThreshold <= 0f)
            {
                SetConnectionStalled(false, 0f);
                return;
            }

            float secondsSinceLastReceived = now - _lastClientDataReceivedTime;

            if (!isConnectionStalled && secondsSinceLastReceived >= _connectionStallThreshold)
                SetConnectionStalled(true, secondsSinceLastReceived);
        }

        private void SetConnectionStalled(bool isStalled, float secondsSinceLastReceived)
        {
            if (isConnectionStalled == isStalled)
                return;

            isConnectionStalled = isStalled;
            onConnectionStalledChanged?.Invoke(isStalled, secondsSinceLastReceived);
        }

        private void ResetNetworkQuality()
        {
            _highPingTransitionStarted = -1f;
            _highJitterTransitionStarted = -1f;
            _highPacketLossTransitionStarted = -1f;
            _highPingTransitionTicks = 0;
            _highJitterTransitionTicks = 0;
            _highPacketLossTransitionTicks = 0;

            if (_isHighPing)
            {
                _isHighPing = false;
                onHighPingChanged?.Invoke(false, ping);
            }

            if (_isHighJitter)
            {
                _isHighJitter = false;
                onHighJitterChanged?.Invoke(false, jitter);
            }

            if (_isHighPacketLoss)
            {
                _isHighPacketLoss = false;
                onHighPacketLossChanged?.Invoke(false, packetLoss);
            }

            SetConnectionStalled(false, 0f);
        }

        private void ReceivePacket(PlayerID sender, PacketMessage msg, bool asServer)
        {
            if (asServer)
            {
                _playersServerBroadcaster.Send(sender, new PacketMessage { sequenceId = msg.sequenceId }, Channel.Unreliable);
                return;
            }

            for (int i = 0; i < _seqCount; i++)
            {
                if (_seqIds[i] == msg.sequenceId)
                {
                    _seqAcked[i] = true;
                    break;
                }
            }
        }

        private void OnDataReceived(Connection conn, ByteData data, bool asServer)
        {
            _totalDataReceived += data.length;

            if (asServer)
                return;

            _lastClientDataReceivedTime = Time.unscaledTime;
            SetConnectionStalled(false, 0f);
        }

        private void OnDataSent(Connection conn, ByteData data, bool asServer)
        {
            _totalDataSent += data.length;
        }

        public struct PingMessage : Packing.IPackedAuto
        {
            public uint sendTime;
            public float realSendTime;
        }

        public struct PacketMessage : Packing.IPackedAuto
        {
            public uint sequenceId;
        }

        public enum StatisticsPlacement
        {
            None,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        [Flags]
        public enum StatisticsDisplayType
        {
            Ping = 1 << 0,
            Usage = 1 << 1,
            ServerStats = 1 << 2,
            Version = 1 << 3,
            Connection = 1 << 4,
        }

        [Flags]
        public enum StatisticsDisplayTarget
        {
            Editor = 1 << 1,
            Build = 1 << 2,
        }
    }
}
