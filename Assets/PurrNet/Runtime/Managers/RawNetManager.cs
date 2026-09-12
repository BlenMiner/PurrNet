using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Raw Net Manager")]
    public class RawNetManager : MonoBehaviour, IRegisterModules, INetworkManager
    {
        [Header("Auto Start Settings")]
        [Tooltip("The flags to determine when the server should automatically start.")]
        [SerializeField]
        private StartFlags _startServerFlags = StartFlags.ServerBuild | StartFlags.Editor;

        [Tooltip("The flags to determine when the client should automatically start.")]
        [SerializeField]
        private StartFlags _startClientFlags = StartFlags.ClientBuild | StartFlags.Editor | StartFlags.Clone;

        [Header("Network Settings")]
        [SerializeField] private GenericTransport _transport;

        [Tooltip("Number of target ticks per second.")]
        [Range(1, 128)]
        [SerializeField, PurrLock]
        private int _tickRate = 20;

        [Tooltip("What to do when a packet exceeds the MTU on an unreliable channel.")]
        [SerializeField]
        private MTUExceededBehaviour _mtuExceededBehaviour = MTUExceededBehaviour.Fragment;

        private ModulesCollection _serverModules;
        private ModulesCollection _clientModules;

        /// <summary>
        /// The start flags of the server.
        /// This is used to determine when the server should automatically start.
        /// </summary>
        public StartFlags startServerFlags
        {
            get => _startServerFlags;
            set => _startServerFlags = value;
        }

        /// <summary>
        /// The start flags of the client.
        /// This is used to determine when the client should automatically start.
        /// </summary>
        public StartFlags startClientFlags
        {
            get => _startClientFlags;
            set => _startClientFlags = value;
        }

        /// <summary>
        /// Whether the server should automatically start.
        /// </summary>
        public bool shouldAutoStartServer => _transport && NetworkManager.ShouldStart(_startServerFlags);

        /// <summary>
        /// Whether the client should automatically start.
        /// </summary>
        public bool shouldAutoStartClient => _transport && NetworkManager.ShouldStart(_startClientFlags);

        private bool _isCleaningClient;
        private bool _isCleaningServer;

        private ITransport _transportLayer;

        public ITransport rawTransport
        {
            get
            {
                EnsureTransportLayer();
                return _transportLayer;
            }
        }

        private void EnsureTransportLayer()
        {
            if (_transportLayer == null && _transport)
            {
                _transportLayer = _transport.transport;
                if (_transportLayer == null)
                    throw new InvalidOperationException(PurrLogger.FormatMessage("Transport is not set (null)."));
            }
        }

        /// <summary>
        /// What to do when a packet exceeds the MTU on an unreliable channel.
        /// </summary>
        public MTUExceededBehaviour mtuExceededBehaviour
        {
            get => _mtuExceededBehaviour;
            set => _mtuExceededBehaviour = value;
        }

        /// <summary>
        /// The state of the server connection.
        /// This is based on the transport listener state.
        /// </summary>
        public ConnectionState serverState
        {
            get
            {
                var state = _transportLayer == null ? ConnectionState.Disconnected : _transportLayer.listenerState;
                return state == ConnectionState.Disconnected && _isCleaningServer
                    ? ConnectionState.Disconnecting
                    : state;
            }
        }

        /// <summary>
        /// The state of the client connection.
        /// This is based on the transport client state.
        /// </summary>
        public ConnectionState clientState
        {
            get
            {
                var state = _transportLayer == null ? ConnectionState.Disconnected : _transportLayer.clientState;
                return state == ConnectionState.Disconnected && _isCleaningClient
                    ? ConnectionState.Disconnecting
                    : state;
            }
        }

        public bool isOffline => !isServer && !isClient;
        public bool isServer => _transportLayer != null && _transportLayer.listenerState == ConnectionState.Connected;
        public bool isClient => _transportLayer != null && _transportLayer.clientState == ConnectionState.Connected;
        public NetworkRules networkRules => null;
        public TickManager tickModule => _serverTickManager ?? _clientTickManager;

        private readonly Dictionary<uint, List<IBroadcastCallback>> _serverActions =
            new Dictionary<uint, List<IBroadcastCallback>>();

        private readonly Dictionary<uint, List<IBroadcastCallback>> _clientActions =
            new Dictionary<uint, List<IBroadcastCallback>>();

        /// <summary>
        /// This event is triggered before the tick.
        /// It may be triggered multiple times if you are both a server and a client.
        /// The parameter is true if the network manager is a server.
        /// </summary>
        public event OnTickDelegate onPreTick;

        /// <summary>
        /// This event is triggered on tick.
        /// It may be triggered multiple times if you are both a server and a client.
        /// The parameter is true if the network manager is a server.
        /// </summary>
        public event OnTickDelegate onTick;

        /// <summary>
        /// This event is triggered after the tick.
        /// It may be triggered multiple times if you are both a server and a client.
        /// The parameter is true if the network manager is a server.
        /// </summary>
        public event OnTickDelegate onPostTick;

        private TickManager _clientTickManager;
        private TickManager _serverTickManager;

        private BroadcastModule _clientBroadcastModule;
        private BroadcastModule _serverBroadcastModule;

        /// <summary>
        /// The players broadcaster of the network manager.
        /// Defaults to the server players broadcaster if the server is active.
        /// Otherwise it defaults to the client players broadcaster.
        /// </summary>
        public BroadcastModule broadcastModule => _serverBroadcastModule ?? _clientBroadcastModule;

        private bool _isServerTicking;

        private void Awake()
        {
            _serverModules = new ModulesCollection(this, true);
            _clientModules = new ModulesCollection(this, false);
            EnsureTransportLayer();
        }

        private void OnDestroy()
        {
            _transportLayer = null;
        }

        private void Start()
        {
            if (shouldAutoStartServer)
            {
#if !UNITY_EDITOR
                PurrLogger.Log("Auto-Starting server...");
#endif
                if (ApplicationContext.isServerBuild)
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = _tickRate;
                }
                StartServer();
            }

            if (shouldAutoStartClient)
            {
#if !UNITY_EDITOR
                PurrLogger.Log("Auto-Starting client...");
#endif
                StartClient();
            }
        }

        public void Subscribe<T>(BroadcastDelegate<T> callback) where T : new()
        {
            Subscribe(callback, true);
            Subscribe(callback, false);
        }

        public void Subscribe<T>(BroadcastDelegate<T> callback, bool asServer)
        {
            var pendingDict = asServer ? _serverActions : _clientActions;
            var type = Hasher.GetStableHashU32<T>();

            if (!pendingDict.TryGetValue(type, out var subscriptions))
            {
                subscriptions = new List<IBroadcastCallback>();
                pendingDict[type] = subscriptions;
            }

            subscriptions.Add(new BroadcastCallback<T>(callback));

            if (TryGetModule(out BroadcastModule broadcaster, asServer))
                broadcaster.Subscribe(callback);
        }

        public void Unsubscribe<T>(BroadcastDelegate<T> callback)
        {
            Unsubscribe(callback, true);
            Unsubscribe(callback, false);
        }

        public void Unsubscribe<T>(BroadcastDelegate<T> callback, bool asServer)
        {
            var pendingDict = asServer ? _serverActions : _clientActions;
            var type = Hasher.GetStableHashU32<T>();

            if (pendingDict.TryGetValue(type, out var actions))
            {
                object boxed = callback;
                for (int i = 0; i < actions.Count; i++)
                {
                    if (actions[i].IsSame(boxed))
                    {
                        actions.RemoveAt(i);
                        break;
                    }
                }
            }

            if (TryGetModule(out BroadcastModule broadcaster, asServer))
                broadcaster.Unsubscribe(callback);
        }

        /// <summary>
        /// Starts the server.
        /// This will start the transport server.
        /// </summary>
        public void StartServer()
        {
            if (!_transport)
                PurrLogger.Throw<InvalidOperationException>("Transport is not set (null).");
            _transport.StartServer(this);
        }

        private Coroutine _clientCoroutine;

        /// <summary>
        /// Starts the client.
        /// This will start the transport client.
        /// </summary>
        public void StartClient()
        {
            if (!_transport)
                PurrLogger.Throw<InvalidOperationException>("Transport is not set (null).");

            if (_clientCoroutine != null)
            {
                StopCoroutine(_clientCoroutine);
                _clientCoroutine = null;
            }

            _clientCoroutine = StartCoroutine(StartClientCoroutine());
        }

        IEnumerator StartClientCoroutine()
        {
            while (serverState is ConnectionState.Disconnecting or ConnectionState.Connecting)
                yield return null;

            _transport.StartClient(this);
        }

        /// <summary>
        /// Starts as both a server and a client.
        /// isServer and isClient will both be true after connection is established.
        /// </summary>
        public void StartHost()
        {
            StartServer();
            StartClient();
        }

        /// <summary>
        /// Stops the server.
        /// This will stop the transport server.
        /// </summary>
        public void StopServer()
        {
            _transport.StopServer(this);
        }

        /// <summary>
        /// Stops the client.
        /// This will stop the transport client.
        /// </summary>
        public void StopClient()
        {
            if (_clientCoroutine != null)
            {
                StopCoroutine(_clientCoroutine);
                _clientCoroutine = null;
            }

            _transport.StopClient(this);
        }

        public void InternalRegisterClientModules()
        {
            _clientModules.RegisterModules();
        }

        public void InternalRegisterServerModules()
        {
            _isServerTicking = false;
            _serverModules.RegisterModules();
        }

        public void InternalUnregisterClientModules()
        {
        }

        public void InternalUnregisterServerModules()
        {

        }

        /// <summary>
        /// Tries to get the module of the given type.
        /// </summary>
        /// <param name="module">The module if found, otherwise the default value of the type.</param>
        /// <param name="asServer">Whether to get the server module or the client module.</param>
        public bool TryGetModule<T>(out T module, bool asServer) where T : INetworkModule
        {
            return asServer ? _serverModules.TryGetModule(out module) : _clientModules.TryGetModule(out module);
        }

        public bool HasModule<T>(bool asServer) where T : INetworkModule
        {
            return TryGetModule<T>(out _, asServer);
        }

        private void Update()
        {
            _serverModules.TriggerOnUpdate();
            _clientModules.TriggerOnUpdate();

            if (_sendFlushRequested)
            {
                _sendFlushRequested = false;
                SendMessagesNow();
            }

            // No Update-phase transport poll here: RawNetManager has no receive deferral
            // layer, so polling per frame would dispatch transport events outside the tick.
        }

        public void RegisterModules(ModulesCollection modules, bool asServer)
        {
            var broadcasting = new BroadcastModule(this, asServer);
            var tickManager = new TickManager(_tickRate, this, broadcasting, asServer);

            if (asServer)
            {
                if (_serverTickManager != null)
                {
                    _serverTickManager.onPreTick -= OnServerPreTick;
                    _serverTickManager.onTick -= OnServerTick;
                    _serverTickManager.onPostTick -= OnServerPostTick;
                }

                _serverTickManager = tickManager;
                _isServerTicking = true;

                _serverTickManager.onPreTick += OnServerPreTick;
                _serverTickManager.onTick += OnServerTick;
                _serverTickManager.onPostTick += OnServerPostTick;
            }
            else
            {
                if (_clientTickManager != null)
                {
                    _clientTickManager.onPreTick -= OnClientPreTick;
                    _clientTickManager.onTick -= OnClientTick;
                    _clientTickManager.onPostTick -= OnClientPostTick;
                }

                _clientTickManager = tickManager;
                _clientTickManager.onPreTick += OnClientPreTick;
                _clientTickManager.onTick += OnClientTick;
                _clientTickManager.onPostTick += OnClientPostTick;
            }

            if (asServer)
                 _serverBroadcastModule = broadcasting;
            else _clientBroadcastModule = broadcasting;

            modules.AddModule(tickManager);
            modules.AddModule(broadcasting);

            RenewSubscriptions(asServer);
        }

        public bool isPromotingToServer => false;

        public bool isTranferingToNewServer => false;

        public ITransport currentTransport => _transport ? _transport.transport : null;

        private void RenewSubscriptions(bool asServer)
        {
            if (!TryGetModule(out BroadcastModule broadcaster, asServer))
                return;

            var pendingDict = asServer ? _serverActions : _clientActions;

            foreach (var subscriptionList in pendingDict.Values)
            {
                for (var i = 0; i < subscriptionList.Count; i++)
                    subscriptionList[i].Subscribe(broadcaster);
            }
        }

        private double _lastSendTime;

        private void SendMessagesNow()
        {
            if (_transportLayer == null)
                return;

            var now = UnityEngine.Time.unscaledTimeAsDouble;
            var sendDelta = _lastSendTime > 0 ? (float)(now - _lastSendTime) : tickModule.tickDelta;
            _lastSendTime = now;
            _transportLayer.SendMessages(sendDelta);
        }

        private bool _sendFlushRequested;

        public void RequestSendFlushThisFrame() => _sendFlushRequested = true;

        private void OnTick()
        {
            bool serverConnected = serverState == ConnectionState.Connected;
            bool clientConnected = clientState == ConnectionState.Connected;

            if (serverConnected)
                _serverModules.TriggerOnPreFixedUpdate();

            if (clientConnected)
                _clientModules.TriggerOnPreFixedUpdate();

            if (_transportLayer != null)
                _transportLayer.ReceiveMessages(tickModule.tickDelta);

            if (serverConnected)
                _serverModules.TriggerOnFixedUpdate();

            if (clientConnected)
                _clientModules.TriggerOnFixedUpdate();

            if (serverConnected)
                _serverModules.TriggerOnPostFixedUpdate();

            if (clientConnected)
                _clientModules.TriggerOnPostFixedUpdate();

            SendMessagesNow();

            if (_isCleaningClient && _clientModules.Cleanup())
            {
                _clientModules.UnregisterModules();
                _isCleaningClient = false;
            }

            if (_isCleaningServer && _serverModules.Cleanup())
            {
                _isServerTicking = false;
                _serverModules.UnregisterModules();
                _isCleaningServer = false;
            }
        }

        private void OnDrawGizmos()
        {
            bool serverConnected = serverState == ConnectionState.Connected;
            bool clientConnected = clientState == ConnectionState.Connected;

            if (serverConnected)
                _serverModules.TriggerOnDrawGizmos();

            if (clientConnected)
                _clientModules.TriggerOnDrawGizmos();
        }

        private void OnServerPreTick() => onPreTick?.Invoke(true);

        private void OnServerPostTick() => onPostTick?.Invoke(true);

        private void OnClientPreTick() => onPreTick?.Invoke(false);

        private void OnServerTick()
        {
            OnTick();
            onTick?.Invoke(true);
        }

        private void OnClientTick()
        {
            if (!_isServerTicking)
                OnTick();
            onTick?.Invoke(false);
        }

        private void OnClientPostTick() => onPostTick?.Invoke(false);
    }
}
