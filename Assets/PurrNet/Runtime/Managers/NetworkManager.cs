#if UNITY_EDITOR
using UnityEditor;
#endif

using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet
{
    [Flags]
    public enum StartFlags
    {
        None = 0,
        Editor = 1,
        Clone = 2,
        ClientBuild = 4,
        ServerBuild = 8
    }
    
    [DefaultExecutionOrder(-999)]
    public sealed partial class NetworkManager : MonoBehaviour
    {
        [UsedImplicitly]
        public static NetworkManager main { get; private set; }
        
        [Header("Auto Start Settings")]
        [SerializeField] private StartFlags _startServerFlags = StartFlags.ServerBuild | StartFlags.Editor;
        [SerializeField] private StartFlags _startClientFlags = StartFlags.ClientBuild | StartFlags.Editor | StartFlags.Clone;
        
        [Header("Persistence Settings")]
        [SerializeField] private CookieScope _cookieScope = CookieScope.LiveWithProcess;

        [Header("Network Settings")]
        [SerializeField] private bool _dontDestroyOnLoad = true;
        [SerializeField] private GenericTransport _transport;
        [SerializeField] private NetworkPrefabs _networkPrefabs;
        [SerializeField] private NetworkRules _networkRules;
        [SerializeField] private NetworkVisibilityRuleSet _visibilityRules;
        [SerializeField] private int _tickRate = 20;
        
        public Connection? localClientConnection { get; private set; }

        public CookieScope cookieScope
        {
            get => _cookieScope;
            set
            {
                if (isOffline)
                    _cookieScope = value;
                else
                    PurrLogger.LogError("Failed to update cookie scope since a connection is active.");
            }
        }

        public StartFlags startServerFlags { get => _startServerFlags; set => _startServerFlags = value; }

        public StartFlags startClientFlags { get => _startClientFlags; set => _startClientFlags = value; }

        public IPrefabProvider prefabProvider { get; private set; }
        
        public NetworkVisibilityRuleSet visibilityRules => _visibilityRules;
        
        public Scene originalScene { get; private set; }
        
        /// <summary>
        /// Occurs when the server connection state changes.
        /// </summary>
        public event Action<ConnectionState> onServerConnectionState;
        
        /// <summary>
        /// Occurs when the client connection state changes.
        /// </summary>
        public event Action<ConnectionState> onClientConnectionState;

        [NotNull]
        public GenericTransport transport
        {
            get => _transport;
            set
            {
                if (_transport)
                {
                    if (serverState != ConnectionState.Disconnected ||
                        clientState != ConnectionState.Disconnected)
                    {
                        throw new InvalidOperationException(PurrLogger.FormatMessage("Cannot change transport while it is being used."));
                    }
                    
                    _transport.transport.onConnected -= OnNewConnection;
                    _transport.transport.onDisconnected -= OnLostConnection;
                    _transport.transport.onConnectionState -= OnConnectionState;
                    _transport.transport.onDataReceived -= OnDataReceived;
                }

                _transport = value;
                
                if (_transport)
                {
                    _transport.transport.onConnected += OnNewConnection;
                    _transport.transport.onDisconnected += OnLostConnection;
                    _transport.transport.onConnectionState += OnConnectionState;
                    _transport.transport.onDataReceived += OnDataReceived;
                    _subscribed = true;
                }
            }
        }

        public bool shouldAutoStartServer => transport && ShouldStart(_startServerFlags);
        public bool shouldAutoStartClient => transport && ShouldStart(_startClientFlags);

        private bool _isCleaningClient;
        private bool _isCleaningServer;
        
        public ConnectionState serverState
        {
            get
            {
                var state = !_transport ? ConnectionState.Disconnected : _transport.transport.listenerState;
                return state == ConnectionState.Disconnected && _isCleaningServer ? ConnectionState.Disconnecting : state;
            }
        }

        public ConnectionState clientState
        {
            get
            {
                var state = !_transport ? ConnectionState.Disconnected : _transport.transport.clientState;
                return state == ConnectionState.Disconnected && _isCleaningClient ? ConnectionState.Disconnecting : state;
            }
        }

        public bool isServer => _transport.transport.listenerState == ConnectionState.Connected;
        
        public bool isClient => _transport.transport.clientState == ConnectionState.Connected;
        
        public bool isOffline => !isServer && !isClient;

        public bool isPlannedHost => ShouldStart(_startServerFlags) && ShouldStart(_startClientFlags);

        public bool isHost => isServer && isClient;
        
        public bool isServerOnly => isServer && !isClient;
        
        public bool isClientOnly => !isServer && isClient;
        
        public NetworkRules networkRules => _networkRules;
        
        private ModulesCollection _serverModules;
        private ModulesCollection _clientModules;
        
        private bool _subscribed;
        
        public static void SetMainInstance(NetworkManager instance)
        {
            if (instance)
                main = instance;
        }

        public void SetPrefabProvider(IPrefabProvider provider)
        {
            if (!isOffline)
            {
                PurrLogger.LogError("Failed to update prefab provider since a connection is active.");
                return;
            }

            prefabProvider = provider;
        }

        private void Awake()
        {
            if (main && main != this)
            {
                if (main.isOffline)
                {
                    Destroy(gameObject);
                }
                else
                {
                    Destroy(this);
                    return;
                }
            }
            
            if (!networkRules)
                throw new InvalidOperationException(PurrLogger.FormatMessage("NetworkRules is not set (null)."));

            originalScene = gameObject.scene;

            if (_visibilityRules)
            {
                var ogName = _visibilityRules.name;
                _visibilityRules = Instantiate(_visibilityRules);
                _visibilityRules.name = "Copy of " + ogName;
                _visibilityRules.Setup(this);
            }
            
            main = this;

            Time.fixedDeltaTime = 1f / _tickRate;
            Application.runInBackground = true;

            if (_networkPrefabs)
            {
                prefabProvider ??= _networkPrefabs;

                if (_networkPrefabs.autoGenerate)
                    _networkPrefabs.Generate();
                _networkPrefabs.PostProcess();
            }

            if (!_subscribed)
                transport = _transport;
            
            _serverModules = new ModulesCollection(this, true);
            _clientModules = new ModulesCollection(this, false);

            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }

        private void Reset()
        {
            if (TryGetComponent(out GenericTransport _) || transport)
                return;
            transport = gameObject.AddComponent<UDPTransport>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!gameObject.scene.isLoaded)
                return;
            
            float tickRate = 1f / _tickRate;
            
            if (Mathf.Approximately(Time.fixedDeltaTime, tickRate))
                return;

            Time.fixedDeltaTime = tickRate;
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
#endif

        public T GetModule<T>(bool asServer) where T : INetworkModule
        {
            if (TryGetModule(out T module, asServer))
                return module;
            
            throw new InvalidOperationException(PurrLogger.FormatMessage($"Module {typeof(T).Name} not found - asServer : {asServer}."));
        }

        public bool TryGetModule<T>(out T module, bool asServer) where T : INetworkModule
        {
            return asServer ?
                _serverModules.TryGetModule(out module) :
                _clientModules.TryGetModule(out module);
        }
        
        /// <summary>
        /// Gets all the objects owned by the given player.
        /// This creates a new list every time it's called.
        /// So it's recommended to cache the result if you're going to use it multiple times.
        /// </summary>
        public List<NetworkIdentity> GetAllPlayerOwnedIds(PlayerID player, bool asServer)
        {
            var ownershipModule = GetModule<GlobalOwnershipModule>(asServer);
            return ownershipModule.GetAllPlayerOwnedIds(player);
        }
        
        public int playerCount => GetModule<PlayersManager>(isServer).players.Count;
        
        public IReadOnlyList<PlayerID> players => GetModule<PlayersManager>(isServer).players;
        
        public IEnumerable<NetworkIdentity> EnumerateAllPlayerOwnedIds(PlayerID player, bool asServer)
        {
            var ownershipModule = GetModule<GlobalOwnershipModule>(asServer);
            return ownershipModule.EnumerateAllPlayerOwnedIds(player);
        }
        
        public void AddVisibilityRule(NetworkManager manager, INetworkVisibilityRule rule)
        {
            _visibilityRules.AddRule(manager, rule);
        }

        public void RemoveVisibilityRule(INetworkVisibilityRule rule)
        {
            _visibilityRules.RemoveRule(rule);
        }
        
        public TickManager clientTickManager { get; private set; }
        
        public TickManager serverTickManager { get; private set; }
        
        public TickManager GetTickManager(bool asServer) => asServer ? serverTickManager : clientTickManager;
        
        public ScenesModule sceneModule => _serverSceneModule ?? _clientSceneModule;
        
        public PlayersManager playerModule => _serverPlayersManager ?? _clientPlayersManager;
        
        public TickManager tickModule => serverTickManager ?? clientTickManager;
        
        public PlayersBroadcaster broadcastModule => _serverPlayersBroadcast ?? _clientPlayersBroadcast;
        
        public PlayerID localPlayer => playerModule.localPlayerId ?? default;
        
        private ScenesModule _clientSceneModule;
        private ScenesModule _serverSceneModule;
        
        private PlayersManager _clientPlayersManager;
        private PlayersManager _serverPlayersManager;
        
        private TickManager _clientTickManager;
        private TickManager _serverTickManager;
        
        private PlayersBroadcaster _clientPlayersBroadcast;
        private PlayersBroadcaster _serverPlayersBroadcast;
        
        internal void RegisterModules(ModulesCollection modules, bool asServer)
        {
            var tickManager = new TickManager(_tickRate);
            
            if (asServer)
                 serverTickManager = tickManager;
            else clientTickManager = tickManager;
            
            if (asServer)
                serverTickManager = tickManager;
            else clientTickManager = tickManager;

            var connBroadcaster = new BroadcastModule(this, asServer);
            var networkCookies = new CookiesModule(_cookieScope);
            var playersManager = new PlayersManager(this, networkCookies, connBroadcaster);
            
            if (asServer)
                 _serverPlayersManager = playersManager;
            else _clientPlayersManager = playersManager;
            
            var playersBroadcast = new PlayersBroadcaster(connBroadcaster, playersManager);
            
            if (asServer)
                _serverPlayersBroadcast = playersBroadcast;
            else _clientPlayersBroadcast = playersBroadcast;

            var scenesModule = new ScenesModule(this, playersManager);
            
            if (asServer)
                 _serverSceneModule = scenesModule;
            else _clientSceneModule = scenesModule;

            var scenePlayersModule = new ScenePlayersModule(this, scenesModule, playersManager);
            
            var hierarchyModule = new HierarchyModule(this, scenesModule, playersManager, scenePlayersModule, prefabProvider);
            var visibilityFactory = new VisibilityFactory(this, playersManager, hierarchyModule, scenePlayersModule);
            var ownershipModule = new GlobalOwnershipModule(visibilityFactory, hierarchyModule, playersManager, scenePlayersModule, scenesModule);
            var rpcModule = new RPCModule(playersManager, visibilityFactory, hierarchyModule, ownershipModule, scenesModule);
            var rpcRequestResponseModule = new RpcRequestResponseModule(playersManager);
            
            hierarchyModule.SetVisibilityFactory(visibilityFactory);
            scenesModule.SetScenePlayers(scenePlayersModule);
            playersManager.SetBroadcaster(playersBroadcast);
            
            modules.AddModule(playersManager);
            modules.AddModule(playersBroadcast);
            modules.AddModule(tickManager);
            modules.AddModule(connBroadcaster);
            modules.AddModule(networkCookies);
            modules.AddModule(scenesModule);
            modules.AddModule(scenePlayersModule);
            
            modules.AddModule(hierarchyModule);
            modules.AddModule(visibilityFactory);
            modules.AddModule(ownershipModule);
            
            modules.AddModule(rpcModule);
            modules.AddModule(rpcRequestResponseModule);
        }

        static bool ShouldStart(StartFlags flags)
        {
            return (flags.HasFlag(StartFlags.Editor) && ApplicationContext.isMainEditor) ||
                   (flags.HasFlag(StartFlags.Clone) && ApplicationContext.isClone) ||
                   (flags.HasFlag(StartFlags.ClientBuild) && ApplicationContext.isClientBuild) ||
                   (flags.HasFlag(StartFlags.ServerBuild) && ApplicationContext.isServerBuild);
        }

        private void Start()
        {
            bool shouldStartServer = transport && ShouldStart(_startServerFlags);
            bool shouldStartClient = transport && ShouldStart(_startClientFlags);
            
            if (shouldStartServer)
                StartServer();
            
            if (shouldStartClient)
                StartClient();
        }

        private void Update()
        {
            _serverModules.TriggerOnUpdate();
            _clientModules.TriggerOnUpdate();
        }

        private void FixedUpdate()
        {
            bool serverConnected = serverState == ConnectionState.Connected;
            bool clientConnected = clientState == ConnectionState.Connected;
            
            if (serverConnected)
                _serverModules.TriggerOnPreFixedUpdate();
            
            if (clientConnected)
                _clientModules.TriggerOnPreFixedUpdate();
            
            if (_transport)
                _transport.transport.UpdateEvents(Time.fixedDeltaTime);
            
            if (serverConnected)
                _serverModules.TriggerOnFixedUpdate();
            
            if (clientConnected)
                _clientModules.TriggerOnFixedUpdate();
            
            if (_isCleaningClient && _clientModules.Cleanup())
            {
                _clientModules.UnregisterModules();
                _isCleaningClient = false;
            }

            if (_isCleaningServer && _serverModules.Cleanup())
            {
                _serverModules.UnregisterModules();
                _isCleaningServer = false;
            }
        }

        private void OnDestroy()
        {
            if (_transport)
            {
                StopClient();
                StopServer();
            }
        }

        public void StartServer()
        {
            if (!_transport)
                PurrLogger.Throw<InvalidOperationException>("Transport is not set (null).");
            _serverModules.RegisterModules();
            _transport.StartServer();
        }
        
        public void StartClient()
        {
            localClientConnection = null;
            
            if (!_transport)
                PurrLogger.Throw<InvalidOperationException>("Transport is not set (null).");
            _clientModules.RegisterModules();
            _transport.StartClient();
        }

        private void OnNewConnection(Connection conn, bool asserver)
        {
            if (asserver)
                 _serverModules.OnNewConnection(conn, true);
            else
            {
                if (localClientConnection.HasValue)
                    PurrLogger.LogError($"A client connection already exists '{localClientConnection}', overwriting it with {conn}.");
                
                localClientConnection = conn;
                _clientModules.OnNewConnection(conn, false);
            }
        }

        private void OnLostConnection(Connection conn, DisconnectReason reason, bool asserver)
        {
            if (asserver)
                 _serverModules.OnLostConnection(conn, true);
            else
            {
                localClientConnection = null;
                _clientModules.OnLostConnection(conn, false);
            }
        }

        private void OnDataReceived(Connection conn, ByteData data, bool asserver)
        {
            if (asserver)
                 _serverModules.OnDataReceived(conn, data, true);
            else _clientModules.OnDataReceived(conn, data, false);
        }

        private void OnConnectionState(ConnectionState state, bool asserver)
        {
            if (asserver)
                 onServerConnectionState?.Invoke(state);
            else onClientConnectionState?.Invoke(state);

            if (state == ConnectionState.Disconnected)
            {
                switch (asserver)
                {
                    case false:
                        _isCleaningClient = true;
                        break;
                    case true:
                        _isCleaningServer = true;
                        break;
                }
            }
        }
        
        public bool TryGetModule<T>(bool asServer, out T module) where T : INetworkModule
        {
            return asServer ? 
                _serverModules.TryGetModule(out module) : 
                _clientModules.TryGetModule(out module);
        }

        public void StopServer() => _transport.StopServer();

        public void StopClient() => _transport.StopClient();

        public GameObject GetPrefabFromGuid(string guid)
        {
            return _networkPrefabs.GetPrefabFromGuid(guid);
        }
    }
}
