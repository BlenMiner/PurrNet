#if UNITY_EDITOR
using UnityEditor;
#endif
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using PurrNet.Authentication;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Profiler;
using PurrNet.Transports;
using PurrNet.Utils;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet
{
    public delegate void OnTickDelegate(bool asServer);

    [DefaultExecutionOrder(-999)]
    [AddComponentMenu("PurrNet/Network Manager")]
    public sealed partial class NetworkManager : MonoBehaviour, IRegisterModules, INetworkManager
    {
        /// <summary>
        /// The main instance of the network manager.
        /// </summary>
        [UsedImplicitly]
        public static NetworkManager main { get; private set; }

        [Header("Misc Settings")]
        [Tooltip("Whether the client should stop playing when it disconnects from the server.")]
        [SerializeField]
        private bool _stopPlayingOnDisconnect;

        [Header("Auto Start Settings")]
        [Tooltip("The flags to determine when the server should automatically start.")]
        [SerializeField]
        private StartFlags _startServerFlags = StartFlags.ServerBuild | StartFlags.Editor;

        [Tooltip("The flags to determine when the client should automatically start.")] [SerializeField]
        private StartFlags _startClientFlags = StartFlags.ClientBuild | StartFlags.Editor | StartFlags.Clone;

        [Header("Persistence Settings")] [PurrDocs("systems-and-modules/network-manager"), PurrLock] [SerializeField]
        private CookieScope _cookieScope = CookieScope.LiveWithProcess;

        [Header("Network Settings")]
        [Tooltip("Whether the network manager should not be destroyed on load. " +
                 "If true, the network manager will be moved to the DontDestroyOnLoad scene.")]
        [SerializeField]
        private bool _dontDestroyOnLoad;

        [PurrDocs("systems-and-modules/transports")] [SerializeField]
        private GenericTransport _transport;

        [PurrDocs("systems-and-modules/network-manager/network-prefabs")] [SerializeField]
        private NetworkPrefabs _networkPrefabs;

#if ADDRESSABLES_PURRNET_SUPPORT
        //[PurrDocs("systems-and-modules/addressables/addressable-spawning-and-despawning")] //TODO: Add this in the future
        [SerializeField]
        private AddressableNetworkPrefabs _addressableNetworkPrefabs;
#endif

        [PurrDocs("systems-and-modules/network-manager/network-assets")] [SerializeField]
        private NetworkAssets _networkAssets;

        [PurrDocs("systems-and-modules/network-manager/network-rules")] [SerializeField]
        private NetworkRules _networkRules;

        [PurrDocs("systems-and-modules/network-manager/network-visibility")] [SerializeField]
        private NetworkVisibilityRuleSet _visibilityRules;

        [PurrDocs("systems-and-modules/network-manager/authentication")] [SerializeField]
        private AuthenticationLayer _authenticator;

        [Tooltip("Number of target ticks per second.")] [SerializeField]
        private int _tickRate = 20;

        [Tooltip("What to do when a packet exceeds the MTU on an unreliable channel.")]
        [SerializeField]
        private MTUExceededBehaviour _mtuExceededBehaviour = MTUExceededBehaviour.Fragment;

        [SerializeField, UsedImplicitly] private bool _patchLingeringProcessBug;

        /// <summary>
        /// The local client connection.
        /// Null if the client is not connected.
        /// </summary>
        public Connection? clientToServerConn { [UsedImplicitly] get; private set; }

        /// <summary>
        /// The cookie scope of the network manager.
        /// This is used to determine when the cookies should be cleared.
        /// This detemines the lifetime of the cookies which are used to remember connections and their PlayerID.
        /// </summary>
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

        /// <summary>
        /// What to do when a packet exceeds the MTU on an unreliable channel.
        /// </summary>
        public MTUExceededBehaviour mtuExceededBehaviour
        {
            get => _mtuExceededBehaviour;
            set => _mtuExceededBehaviour = value;
        }

        /// <summary>
        /// Number of target ticks per second.
        /// </summary>
        public int tickRate
        {
            get => _tickRate;
            set
            {
                if (value < 1)
                {
                    PurrLogger.LogError("Failed to update tick rate since it must be greater than zero.");
                    return;
                }

                if (_serverTickManager != null || _clientTickManager != null)
                {
                    PurrLogger.LogError("Failed to update tick rate since a tick manager is already running.");
                    return;
                }

                _tickRate = value;
            }
        }

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
        /// The Network Assets of the network manager.
        /// </summary>
        public NetworkAssets networkAssets
        {
            get => _networkAssets;
            set
            {
                if (isOffline)
                {
                    _networkAssets = value;
                }
                else PurrLogger.LogError("Failed to update network assets since a connection is active.");
            }
        }

        /// <summary>
        /// The prefab provider of the network manager.
        /// </summary>
        public IPrefabProvider prefabProvider { get; private set; }

        private PrefabResolver _prefabResolver;

        /// <summary>
        /// Resolves prefab ids and prefab references to prefab data. All runtime lookups go through this.
        /// </summary>
        public PrefabResolver prefabResolver => _prefabResolver ??= new PrefabResolver(this);

        public bool TryGetPrefabPersistentId(PrefabID prefabId, out string persistentId)
        {
            return prefabResolver.TryGetPersistentId(prefabId, out persistentId);
        }

        public bool TryGetPrefabPersistentId(GameObject prefab, out string persistentId)
        {
            return prefabResolver.TryGetPersistentId(prefab, out persistentId);
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            return prefabResolver.TryGetPrefabDataByPersistentId(persistentId, out prefabData);
        }

        private NetworkAssetResolver _networkAssetResolver;

        /// <summary>
        /// Resolves network asset ids and asset references to registered assets. All runtime lookups go through this.
        /// </summary>
        public NetworkAssetResolver networkAssetResolver => _networkAssetResolver ??= new NetworkAssetResolver(this);

        public bool TryGetNetworkAssetPersistentId(UnityEngine.Object asset, out string persistentId)
        {
            return networkAssetResolver.TryGetPersistentId(asset, out persistentId);
        }

        public bool TryGetNetworkAssetByPersistentId(string persistentId, out UnityEngine.Object asset)
        {
            return networkAssetResolver.TryGetAssetByPersistentId(persistentId, out asset);
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        /// <summary>
        /// The Addressable network prefabs configuration, if assigned.
        /// </summary>
        public AddressableNetworkPrefabs addressableNetworkPrefabs => _addressableNetworkPrefabs;
#endif

        /// <summary>
        /// The visibility rules of the network manager.
        /// </summary>
        public NetworkVisibilityRuleSet visibilityRules => _visibilityRules;

        /// <summary>
        /// The original scene of the network manager.
        /// This is the scene the network manager was created in.
        /// </summary>
        public Scene originalScene { get; private set; }

        public int originalSceneBuildIndex { get; private set; }

        /// <summary>
        /// Occurs when the server connection state changes.
        /// </summary>
        public event Action<ConnectionState> onServerConnectionState;

        /// <summary>
        /// Occurs when the client connection state changes.
        /// </summary>
        public event Action<ConnectionState> onClientConnectionState;

        /// <summary>
        /// Occurs when the server connection state changes.
        /// </summary>
        public static event Action<ConnectionState> onAnyServerConnectionState;

        /// <summary>
        /// Occurs when the client connection state changes.
        /// </summary>
        public static event Action<ConnectionState> onAnyClientConnectionState;

        /// <summary>
        /// Server-side: fires when a client is rejected by either a built-in denier
        /// (version mismatch, ack timeout, missing authenticator) or by the active
        /// <see cref="Authentication.AuthenticationLayer"/>. <c>reason</c> is <c>null</c> for
        /// built-ins and only carries bytes for typed denials produced by an
        /// <see cref="Authentication.AuthenticationBehaviour{TRequest,TDenial}"/>.
        /// </summary>
        public event Action<Connection, DenialKind, ByteData?> onAuthenticationDenied;

        private ITransport _transportLayer;

        public ITransport rawTransport
        {
            get
            {
                if (_transport)
                    return _transport.transport;
                return null;
            }
        }

        private bool _ready;

        /// <summary>
        /// Unsubscribes all listeners and any other internal state.
        /// This is meant to be called manually if you encounter any caching issues due to bad unsubscribing.
        /// </summary>
        public void ResetInternalState()
        {
            onServerConnectionState = null;
            onClientConnectionState = null;
            onAnyServerConnectionState = null;
            onAnyClientConnectionState = null;
            onAuthenticationDenied = null;

            onPreTick = null;
            onTick = null;
            onPostTick = null;

            onPlayerJoined = null;
            onPlayerLeft = null;
            onPlayerJoinedScene = null;
            onPlayerLeftScene = null;
            onPlayerLoadedScene = null;
            onPlayerUnloadedScene = null;
            onLocalPlayerReceivedID = null;

            onNetworkStarted = null;
            onNetworkShutdown = null;
            onNetworkStartedSimple = null;
            onNetworkShutdownSimple = null;

            _serverPendingSubscriptions.Clear();
            _clientPendingSubscriptions.Clear();
        }

        public ITransport currentTransport => _transport ? _transport.transport : null;

        /// <summary>
        /// The transport of the network manager.
        /// This is the main transport used when starting the server or client.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown when trying to change the transport while it is being used.</exception>
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
                        throw new InvalidOperationException(
                            PurrLogger.FormatMessage("Cannot change transport while it is being used."));
                    }

                    TeardownTransportLayer();
                }

                _transport = value;

                if (_transport)
                {
                    BuildTransportLayer();
                    _subscribed = true;
                }
                else
                {
                    _subscribed = false;
                }
            }
        }

        private void BuildTransportLayer()
        {
            _transportLayer = _transport.transport;
            if (_transportLayer == null)
                throw new InvalidOperationException(PurrLogger.FormatMessage("Transport is not set (null)."));

            _transportLayer.onConnected += OnNewConnection;
            _transportLayer.onDisconnected += OnLostConnection;
            _transportLayer.onConnectionState += OnConnectionState;
            _transportLayer.onDataReceived += OnDataReceived;
            _transport.externalPump = IsTickingTransport;
        }

        private bool IsTickingTransport() => _serverTickManager != null || _clientTickManager != null;

        private void TeardownTransportLayer()
        {
            if (_transportLayer == null)
                return;

            _transportLayer.onConnected -= OnNewConnection;
            _transportLayer.onDisconnected -= OnLostConnection;
            _transportLayer.onConnectionState -= OnConnectionState;
            _transportLayer.onDataReceived -= OnDataReceived;
            _transportLayer = null;

            if (_transport)
                _transport.externalPump = null;
        }

        private bool IsProbeEvent(bool asServer) => !asServer && _transport && _transport.isPinging;

        /// <summary>
        /// Whether the server should automatically start.
        /// </summary>
        public bool shouldAutoStartServer => transport && ShouldStart(_startServerFlags);

        /// <summary>
        /// Whether the client should automatically start.
        /// </summary>
        public bool shouldAutoStartClient => transport && ShouldStart(_startClientFlags);

        private bool _isCleaningClient;
        private bool _isCleaningServer;
        private bool _preserveClientStateForHostMigration;

        /// <summary>
        /// The state of the server connection.
        /// This is based on the transport listener state.
        /// </summary>
        public ConnectionState serverState
        {
            get
            {
                var state = _transportLayer?.listenerState ?? ConnectionState.Disconnected;
                var result = state == ConnectionState.Disconnected && _isCleaningServer
                    ? ConnectionState.Disconnecting
                    : state;
                return result;
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
                var state = _transportLayer?.clientState ?? ConnectionState.Disconnected;
                return state == ConnectionState.Disconnected && _isCleaningClient
                    ? ConnectionState.Disconnecting
                    : state;
            }
        }

        /// <summary>
        /// Whether the network manager is a server.
        /// </summary>
        public bool isServer { get; private set; }

        [UsedByIL] public static bool isServerStatic => main && main.isServer;

        [UsedByIL] public static bool isClientStatic => main && main.isClient;

        /// <summary>
        /// Whether the network manager is a client.
        /// </summary>
        public bool isClient { get; private set; }

        /// <summary>
        /// Whether the network manager is offline.
        /// Not a server or a client.
        /// </summary>
        public bool isOffline => !isServer && !isClient;

        /// <summary>
        /// Whether the network manager is a planned host.
        /// This is true even if the server or client is not yet connected or ready.
        /// </summary>
        public bool isPlannedHost => ShouldStart(_startServerFlags) && ShouldStart(_startClientFlags);

        /// <summary>
        /// Whether the network manager is a host.
        /// This is true only if the server and client are connected and ready.
        /// </summary>
        public bool isHost => isServer && isClient;

        /// <summary>
        /// Whether the network manager is a server only.
        /// </summary>
        public bool isServerOnly => isServer && !isClient;

        public bool pendingHost =>
            clientState != ConnectionState.Disconnected && serverState != ConnectionState.Disconnected;

        public bool isPlannedServerOnly => ShouldStart(_startServerFlags) && !ShouldStart(_startClientFlags);

        /// <summary>
        /// Whether the network manager is a client only.
        /// </summary>
        public bool isClientOnly => !isServer && isClient;

        /// <summary>
        /// The network rules of the network manager.
        /// </summary>
        public NetworkRules networkRules => _networkRules;

        private ModulesCollection _serverModules;
        private ModulesCollection _clientModules;

        private bool _subscribed;

        /// <summary>
        /// Sets the main instance of the network manager.
        /// This is used for convinience but also for static RPCs and other static functionality.
        /// </summary>
        /// <param name="instance">The instance to set as the main instance.</param>
        public static void SetMainInstance(NetworkManager instance)
        {
            if (instance)
                main = instance;
        }

        /// <summary>
        /// Overrides the manager's <see cref="NetworkRules"/> asset. Must be called while the
        /// manager is offline; runtime rule swaps mid-connection are not supported because
        /// in-flight authority checks would observe inconsistent rules across peers.
        /// </summary>
        public void SetNetworkRules(NetworkRules rules)
        {
            if (!isOffline)
            {
                PurrLogger.LogError("Failed to update network rules since a connection is active.");
                return;
            }

            _networkRules = rules;
        }

        /// <summary>
        /// Sets the prefab provider.
        /// </summary>
        /// <param name="provider">The provider to set.</param>
        public void SetPrefabProvider(IPrefabProvider provider)
        {
            if (!isOffline)
            {
                PurrLogger.LogError("Failed to update prefab provider since a connection is active.");
                return;
            }

            if (prefabProvider == provider)
                return;

            prefabProvider = provider;
            prefabProvider.Refresh();
            HierarchyPool.EvictGlobalPrototypes();
        }

        /// <summary>
        /// Prepares the prefab info for the given instance.
        /// This needs to be ready before the object is spawned.
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="pid">The prefab index in the network prefabs list.</param>
        /// <param name="shouldBePooled">Whether the object should be pooled.</param>
        public static void SetupPrefabInfo(GameObject instance, PrefabID pid, bool shouldBePooled)
        {
            var children = ListPool<NetworkIdentity>.Instantiate();

            if (!instance.GetComponent<NetworkIdentity>())
                instance.AddComponent<NetworkIdentity>();

            instance.GetComponentsInChildren(true, children);
            SetupPrefabInfo(instance, pid, shouldBePooled, children);
            ListPool<NetworkIdentity>.Destroy(children);
        }

        /// <summary>
        /// Prepares the prefab info for the given instance, reusing an already collected identity list.
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="pid">The prefab index in the network prefabs list.</param>
        /// <param name="shouldBePooled">Whether the object should be pooled.</param>
        /// <param name="children">The result of GetComponentsInChildren(true) on the instance.</param>
        public static void SetupPrefabInfo(GameObject instance, PrefabID pid, bool shouldBePooled,
            List<NetworkIdentity> children)
        {
            if (!instance.GetComponent<NetworkIdentity>())
            {
                instance.AddComponent<NetworkIdentity>();
                children.Clear();
                instance.GetComponentsInChildren(true, children);
            }

            Transform runTransform = null;
            int runStart = 0;

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var trs = child.transform;

                if (!ReferenceEquals(trs, runTransform))
                {
                    runTransform = trs;
                    runStart = i;
                }

                child.PreparePrefabInfo(
                    pid,
                    i == runStart ? i : children[runStart].componentIndex,
                    shouldBePooled,
                    false
                );
            }
        }

        static bool ReferencesAssembly(Assembly asm, string targetSimpleName)
        {
            try
            {
                if (asm == null) return false;

                // If it's the same assembly
                if (string.Equals(asm.GetName().Name, targetSimpleName,
                        StringComparison.Ordinal)) return true;

                // Check direct references
                var refs = asm.GetReferencedAssemblies();
                for (int i = 0; i < refs.Length; i++)
                {
                    if (string.Equals(refs[i].Name, targetSimpleName,
                            StringComparison.Ordinal))
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        static string GetSimpleNameOf(Type t) => t.Assembly.GetName().Name;

        struct TypeRegistrer
        {
            public MethodInfo method;
            public int priority;
        }

        public static void CallAllRegisters()
        {
            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            var attrAssemblyName = GetSimpleNameOf(typeof(RegisterPackersAttribute));
            using var methodsToCall = DisposableList<TypeRegistrer>.Create(128);

            for (var index = 0; index < allAssemblies.Length; index++)
            {
                var assembly = allAssemblies[index];

                if (!ReferencesAssembly(assembly, attrAssemblyName))
                    continue;

                Type[] types;

                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types ?? Array.Empty<Type>();
                }
                catch
                {
                    continue;
                }

                for (var j = 0; j < types.Length; j++)
                {
                    var type = types[j];
                    if (type == null)
                        continue;

                    if (!type.IsAbstract || !type.IsSealed)
                        continue;

                    MethodInfo[] methods;

                    try
                    {
                        methods = type.GetMethods(BindingFlags.Static |
                                                  BindingFlags.Public |
                                                  BindingFlags.NonPublic);
                    }
                    catch
                    {
                        continue; // skip bad type
                    }

                    for (var m = 0; m < methods.Length; m++)
                    {
                        var method = methods[m];
                        if (!method.IsStatic)
                            continue;

                        try
                        {
                            var attributes = method.GetCustomAttributes(false);
                            for (var i = 0; i < attributes.Length; i++)
                            {
                                var attribute = attributes[i];

                                if (attribute is not RegisterPackersAttribute registerPackersAttribute)
                                    continue;

                                methodsToCall.Add(new TypeRegistrer
                                {
                                    method = method,
                                    priority = registerPackersAttribute.priority
                                });

                                // method.Invoke(null, null);
                                break;
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }

            methodsToCall.list.Sort((a, b) => a.priority.CompareTo(b.priority));

            for (var i = 0; i < methodsToCall.Count; i++)
                methodsToCall[i].method.Invoke(null, null);
        }

        private static bool _hasGeneratedAlready;

        public static void LoadOrGenerateHashes()
        {
            CalculateHashes();
        }

        [UsedImplicitly]
        static void CalculateHashes()
        {
            if (_hasGeneratedAlready)
                return;

            _hasGeneratedAlready = true;

            Hasher.ClearState();
            Manifest.Clear();
            CallAllRegisters();
        }

#if !UNITY_EDITOR
        private void OnApplicationQuit()
        {
            if (_patchLingeringProcessBug)
                Environment.FailFast("Applying patch for lingering process bug.");
        }
#endif

        public static string version { get; private set; }

        public static bool VerifyVersion(string va)
        {
            if (va == "v?" || version == "v?")
                return true;
            return va == version;
        }

        private void Awake()
        {
            if (_ready)
                return;

            version ??= PurrMetadata.version;

            if (main && main != this)
            {
                if (main.isOffline)
                {
                    Destroy(gameObject);
                    return;
                }

                Destroy(this);
                return;
            }

            if (!networkRules)
                throw new InvalidOperationException(PurrLogger.FormatMessage("NetworkRules is not set (null)."));

            originalScene = gameObject.scene;
            originalSceneBuildIndex = originalScene.buildIndex;

            if (_visibilityRules)
            {
                var ogName = _visibilityRules.name;
                _visibilityRules = Instantiate(_visibilityRules);
                _visibilityRules.name = "Copy of " + ogName;
                _visibilityRules.Setup(this);
            }

            main = this;

            LoadOrGenerateHashes();

            Application.runInBackground = true;

            if (_networkPrefabs)
            {
                if (_networkPrefabs.autoGenerate)
                    _networkPrefabs.Generate();

                if (prefabProvider == null)
                    SetPrefabProvider(_networkPrefabs);
            }

            if (!_subscribed)
                transport = _transport;

            _serverModules = new ModulesCollection(this, true);
            _clientModules = new ModulesCollection(this, false);
            UnityLatestUpdate.onPostLatestUpdate += FlushImmediateRPCsLate;
            _ready = true;

            if (_dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }

#if UNITY_EDITOR
        private void Reset()
        {
            if (TryGetComponent(out GenericTransport _) || transport)
                return;
            transport = gameObject.AddComponent<UDPTransport>();
        }
#endif

        public bool HasModule<T>(bool asServer) where T : INetworkModule
        {
            return TryGetModule<T>(out _, asServer);
        }

        /// <summary>
        /// Gets the module of the given type.
        /// Throws an exception if the module is not found.
        /// </summary>
        /// <param name="asServer">Whether to get the server module or the client module.</param>
        /// <typeparam name="T">The type of the module.</typeparam>
        /// <returns>The module of the given type.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the module is not found.</exception>
        public T GetModule<T>(bool asServer) where T : INetworkModule
        {
            if (TryGetModule(out T module, asServer))
                return module;

            throw new InvalidOperationException(
                PurrLogger.FormatMessage($"Module {typeof(T).Name} not found - asServer : {asServer}."));
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

        /// <summary>
        /// Gets all the objects owned by the given player.
        /// Adds the result to the given list.
        /// </summary>
        public void GetAllPlayerOwnedIds(PlayerID player, bool asServer, List<NetworkIdentity> result)
        {
            var ownershipModule = GetModule<GlobalOwnershipModule>(asServer);
            ownershipModule.GetAllPlayerOwnedIds(player, result);
        }

        /// <summary>
        /// Gets the current player count.
        /// </summary>
        public int playerCount => playerModule?.players.Count ?? 0;

        /// <summary>
        /// Gets the current player list.
        /// This will be update every time a player joins or leaves.
        /// </summary>
        public IReadOnlyList<PlayerID> players
        {
            get
            {
                if (TryGetModule<PlayersManager>(isServer, out var playersManager))
                    return playersManager.players;
                return Array.Empty<PlayerID>();
            }
        }

        /// <summary>
        /// Enumerates all the objects owned by the given player.
        /// </summary>
        /// <param name="player">The player to enumerate the objects of.</param>
        /// <param name="asServer">Whether to get the server module or the client module.</param>
        /// <returns>An enumerable of all the objects owned by the given player.</returns>
        public IEnumerable<NetworkIdentity> EnumerateAllPlayerOwnedIds(PlayerID player, bool asServer)
        {
            if (!TryGetModule<GlobalOwnershipModule>(out var ownershipModule, asServer))
                return Array.Empty<NetworkIdentity>();
            return ownershipModule.EnumerateAllPlayerOwnedIds(player);
        }

        /// <summary>
        /// Adds a visibility rule to the rule set.
        /// </summary>
        /// <param name="manager">The network manager to add the rule to.</param>
        /// <param name="rule">The rule to add.</param>
        public void AddVisibilityRule(NetworkManager manager, INetworkVisibilityRule rule)
        {
            _visibilityRules.AddRule(manager, rule);
        }

        /// <summary>
        /// Removes a visibility rule from the rule set.
        /// </summary>
        /// <param name="rule">The rule to remove.</param>
        public void RemoveVisibilityRule(INetworkVisibilityRule rule)
        {
            _visibilityRules.RemoveRule(rule);
        }

        /// <summary>
        /// The scene module of the network manager.
        /// Defaults to the server scene module if the server is active.
        /// Otherwise it defaults to the client scene module.
        /// </summary>
        public ScenesModule sceneModule => _serverSceneModule ?? _clientSceneModule;

        /// <summary>
        /// The players manager of the network manager.
        /// Defaults to the server players manager if the server is active.
        /// Otherwise it defaults to the client players manager.
        /// </summary>
        public PlayersManager playerModule => _serverPlayersManager ?? _clientPlayersManager;

        /// <summary>
        /// The tick manager of the network manager.
        /// Defaults to the server tick manager if the server is active.
        /// Otherwise it defaults to the client tick manager.
        /// </summary>
        public TickManager tickModule => _serverTickManager ?? _clientTickManager;

        /// <summary>
        /// The players broadcaster of the network manager.
        /// Defaults to the server players broadcaster if the server is active.
        /// Otherwise it defaults to the client players broadcaster.
        /// </summary>
        public PlayersBroadcaster broadcastModule => _serverPlayersBroadcast ?? _clientPlayersBroadcast;

        public BroadcastModule connectionBroadcaster => _serverBroadcast ?? _clientBroadcast;

        public BroadcastModule GetConnectionBroadcaster(bool asServer)
        {
            return asServer ? _serverBroadcast : _clientBroadcast;
        }

        /// <summary>
        /// The scene players module of the network manager.
        /// Defaults to the server scene players module if the server is active.
        /// Otherwise it defaults to the client scene players module.
        /// </summary>
        public ScenePlayersModule scenePlayersModule => _serverScenePlayersModule ?? _clientScenePlayersModule;

        public DeltaModule deltaModule => _serverDeltaModule ?? _clientDeltaModule;

        /// <summary>
        /// The network LOD factory of the network manager.
        /// Defaults to the server module if the server is active.
        /// Otherwise it defaults to the client module.
        /// </summary>
        public NetworkLODFactory lodModule => _serverLODModule ?? _clientLODModule;

        /// <summary>
        /// The local player of the network manager.
        /// If the local player is not set, this will return the default value of the player id.
        /// </summary>
        public PlayerID localPlayer => _clientPlayersManager?.localPlayerId ?? default;

        public bool isLocalPlayerReady => _clientPlayersManager?.localPlayerId.HasValue == true;

        public AuthenticationLayer authenticator
        {
            get { return _authenticator; }
            set
            {
                if (!isOffline)
                {
                    PurrLogger.LogError("Failed to update authenticator since a connection is active");
                    return;
                }

                _authenticator = value;
            }
        }

        private ScenesModule _clientSceneModule;
        private ScenesModule _serverSceneModule;

        private PlayersManager _clientPlayersManager;
        private PlayersManager _serverPlayersManager;

        private TickManager _clientTickManager;
        private TickManager _serverTickManager;

        private BroadcastModule _clientBroadcast;
        private BroadcastModule _serverBroadcast;

        private PlayersBroadcaster _clientPlayersBroadcast;
        private PlayersBroadcaster _serverPlayersBroadcast;

        private ScenePlayersModule _clientScenePlayersModule;
        private ScenePlayersModule _serverScenePlayersModule;

        internal DeltaModule _clientDeltaModule;
        internal DeltaModule _serverDeltaModule;

        private NetworkLODFactory _clientLODModule;
        private NetworkLODFactory _serverLODModule;

        private AuthModule _serverAuthModule;

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

        /// <summary>
        /// This event is triggered when a player joins.
        /// Note that before a player joins it has a connection step.
        /// </summary>
        public event OnPlayerJoinedEvent onPlayerJoined;

        private bool _telemetrySentClient;

        void OnPlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            onPlayerJoined?.Invoke(player, isReconnect, asServer);

            if (!asServer && !_telemetrySentClient)
            {
                _telemetrySentClient = true;
                StartCoroutine(SendConnectionTelemetryDelayed());
            }
        }

        private IEnumerator SendConnectionTelemetryDelayed()
        {
            yield return null;
            PurrInternalTelemetry.SendConnectionEvent(this);
        }

        /// <summary>
        /// This event is triggered when a player leaves.
        /// </summary>
        public event OnPlayerLeftEvent onPlayerLeft;

        void OnPlayerLeft(PlayerID player, bool asServer) => onPlayerLeft?.Invoke(player, asServer);

        /// <summary>
        /// This event is triggered when the local player receives an ID.
        /// </summary>
        public event OnPlayerEvent onLocalPlayerReceivedID;

        void OnLocalPlayerReceivedID(PlayerID player) => onLocalPlayerReceivedID?.Invoke(player);

        void OnAuthenticationDenied(Connection conn, DenialKind kind, ByteData? reason) =>
            onAuthenticationDenied?.Invoke(conn, kind, reason);

        /// <summary>
        /// This event is triggered when a player joins the scene.
        /// It might not be triggered if the user reconnects but was already in the scene due to persistence.
        /// For that use onPlayerLoadedScene instead.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerJoinedScene;

        void OnPlayerJoinedScene(PlayerID player, SceneID scene, bool asServer) =>
            onPlayerJoinedScene?.Invoke(player, scene, asServer);

        /// <summary>
        /// This event is triggered when a player loads the scene.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLoadedScene;

        void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer) =>
            onPlayerLoadedScene?.Invoke(player, scene, asServer);

        /// <summary>
        /// This event is triggered when a player unloads the scene.
        /// Or when they leave the server and had it loaded.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerUnloadedScene;

        void OnPlayerUnloadedScene(PlayerID player, SceneID scene, bool asServer) =>
            onPlayerUnloadedScene?.Invoke(player, scene, asServer);

        /// <summary>
        /// This event is triggered when a player leaves the scene.
        /// This might not be triggered if the network rules keep the player in the scene.
        /// In that case, you want to use onPlayerUnloadedScene.
        /// </summary>
        public event OnPlayerSceneEvent onPlayerLeftScene;

        void OnPlayerLeftScene(PlayerID player, SceneID scene, bool asServer) =>
            onPlayerLeftScene?.Invoke(player, scene, asServer);

        private bool _isServerTicking;

        readonly List<ValidateSpawnAction> _clientSpawnValidators = new List<ValidateSpawnAction>();

        public event ValidateSpawnAction onClientSpawnValidate
        {
            add
            {
                if (value == null)
                    return;

                _clientSpawnValidators.Add(value);
                if (TryGetModule<HierarchyFactory>(true, out var hierarchyFactory))
                    hierarchyFactory.onClientSpawnValidate += value;
            }
            remove
            {
                if (value == null)
                    return;

                for (int i = _clientSpawnValidators.Count - 1; i >= 0; i--)
                {
                    if (_clientSpawnValidators[i] != value)
                        continue;

                    _clientSpawnValidators.RemoveAt(i);
                    break;
                }

                if (TryGetModule<HierarchyFactory>(true, out var hierarchyFactory))
                    hierarchyFactory.onClientSpawnValidate -= value;
            }
        }

        public void RegisterModules(ModulesCollection modules, bool asServer)
        {
            switch (asServer)
            {
                case true when isPromotingToServer:
                    RegisterPromotedServerModules(modules);
                    return;
                case false when isTranferingToNewServer && modules.hasModules:
                    modules.TransferToNewServer();
                    return;
            }

            var connBroadcaster = new BroadcastModule(this, asServer);
            var tickManager = new TickManager(_tickRate, this, connBroadcaster, asServer);

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
                _serverBroadcast = connBroadcaster;
            else _clientBroadcast = connBroadcaster;

            var networkCookies = new CookiesModule(_cookieScope, asServer);
            var authModule = new AuthModule(this, connBroadcaster, networkCookies);
            var playersManager = new PlayersManager(this, authModule, connBroadcaster);
            authModule.SetPlayerModule(playersManager);

            if (asServer)
            {
                if (_serverAuthModule != null)
                    _serverAuthModule.onAuthenticationDenied -= OnAuthenticationDenied;

                _serverAuthModule = authModule;
                _serverAuthModule.onAuthenticationDenied += OnAuthenticationDenied;

                if (_serverPlayersManager != null)
                {
                    _serverPlayersManager.onPlayerJoined -= OnPlayerJoined;
                    _serverPlayersManager.onPlayerLeft -= OnPlayerLeft;
                    _serverPlayersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
                }

                _serverPlayersManager = playersManager;

                _serverPlayersManager.onPlayerJoined += OnPlayerJoined;
                _serverPlayersManager.onPlayerLeft += OnPlayerLeft;
                _serverPlayersManager.onLocalPlayerReceivedID += OnLocalPlayerReceivedID;
            }
            else
            {
                if (_clientPlayersManager != null)
                {
                    _clientPlayersManager.onPlayerJoined -= OnPlayerJoined;
                    _clientPlayersManager.onPlayerLeft -= OnPlayerLeft;
                    _clientPlayersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
                }

                _clientPlayersManager = playersManager;

                _clientPlayersManager.onPlayerJoined += OnPlayerJoined;
                _clientPlayersManager.onPlayerLeft += OnPlayerLeft;
                _clientPlayersManager.onLocalPlayerReceivedID += OnLocalPlayerReceivedID;
            }

            var playersBroadcast = new PlayersBroadcaster(connBroadcaster, playersManager);

            if (asServer)
                _serverPlayersBroadcast = playersBroadcast;
            else _clientPlayersBroadcast = playersBroadcast;

            var scenesModule = new ScenesModule(this, playersManager);

            if (asServer)
                _serverSceneModule = scenesModule;
            else _clientSceneModule = scenesModule;

            var scenePlayers = new ScenePlayersModule(this, scenesModule, playersManager);

            if (asServer)
            {
                if (_serverScenePlayersModule != null)
                {
                    _serverScenePlayersModule.onPlayerJoinedScene -= OnPlayerJoinedScene;
                    _serverScenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
                    _serverScenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
                    _serverScenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;
                }

                _serverScenePlayersModule = scenePlayers;

                _serverScenePlayersModule.onPlayerJoinedScene += OnPlayerJoinedScene;
                _serverScenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;
                _serverScenePlayersModule.onPlayerUnloadedScene += OnPlayerUnloadedScene;
                _serverScenePlayersModule.onPlayerLeftScene += OnPlayerLeftScene;
            }
            else
            {
                if (_clientScenePlayersModule != null)
                {
                    _clientScenePlayersModule.onPlayerJoinedScene -= OnPlayerJoinedScene;
                    _clientScenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
                    _clientScenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
                    _clientScenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;
                }

                _clientScenePlayersModule = scenePlayers;

                _clientScenePlayersModule.onPlayerJoinedScene += OnPlayerJoinedScene;
                _clientScenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;
                _clientScenePlayersModule.onPlayerUnloadedScene += OnPlayerUnloadedScene;
                _clientScenePlayersModule.onPlayerLeftScene += OnPlayerLeftScene;
            }

            var newDeltaModule = new DeltaModule(playersManager, playersBroadcast);
            if (asServer) _serverDeltaModule = newDeltaModule;
            else _clientDeltaModule = newDeltaModule;

            scenesModule.SetScenePlayers(scenePlayers);
            playersManager.SetBroadcaster(playersBroadcast);

            modules.AddModule(playersManager);
            modules.AddModule(playersBroadcast);
            modules.AddModule(tickManager);
            modules.AddModule(connBroadcaster);
            modules.AddModule(authModule);
            modules.AddModule(networkCookies);
            modules.AddModule(newDeltaModule);

            modules.AddModule(scenesModule);
            modules.AddModule(scenePlayers);

            var hierarchyV2 = new HierarchyFactory(this, scenesModule, scenePlayers, playersManager);
            var ownershipModule =
                new GlobalOwnershipModule(this, hierarchyV2, playersManager, scenePlayers, scenesModule);
            var rpcModule = new RPCModule(this, playersManager, hierarchyV2, ownershipModule, scenesModule);
            var networkTransform =
                new NetworkTransformFactory(scenesModule, scenePlayers, playersBroadcast, this, hierarchyV2);
            var colliderRollback = new ColliderRollbackFactory(this, tickManager, scenesModule);
            var networkLOD = new NetworkLODFactory(this, scenesModule, scenePlayers);

            if (asServer) _serverLODModule = networkLOD;
            else _clientLODModule = networkLOD;

            if (asServer)
                _serverRpcModule = rpcModule;
            else _clientRpcModule = rpcModule;

            if (asServer)
                for (int i = 0; i < _clientSpawnValidators.Count; i++)
                    hierarchyV2.onClientSpawnValidate += _clientSpawnValidators[i];

            modules.AddModule(networkTransform);
            modules.AddModule(hierarchyV2);
            modules.AddModule(ownershipModule);
            modules.AddModule(rpcModule);
            modules.AddModule(new RpcRequestResponseModule(this, playersManager, asServer));
            modules.AddModule(colliderRollback);
            modules.AddModule(networkLOD);

#if ADDRESSABLES_PURRNET_SUPPORT
            if (_addressableNetworkPrefabs && _addressableNetworkPrefabs.count > 0 &&
                networkRules && networkRules.AddressablesSyncLoadState)
            {
                modules.AddModule(new AddressablesSyncModule(this, playersManager));
            }
#endif

            RenewSubscriptions(asServer);
        }

        private void RegisterPromotedServerModules(ModulesCollection modules)
        {
            modules.MigrateFrom(_clientModules);
            RebindPromotedServerModules();
            RenewSubscriptions(true);
        }

        private void RebindPromotedServerModules()
        {
            if (_clientTickManager != null)
            {
                _clientTickManager.onPreTick -= OnClientPreTick;
                _clientTickManager.onTick -= OnClientTick;
                _clientTickManager.onPostTick -= OnClientPostTick;
            }

            if (_serverTickManager != null)
            {
                _serverTickManager.onPreTick -= OnServerPreTick;
                _serverTickManager.onTick -= OnServerTick;
                _serverTickManager.onPostTick -= OnServerPostTick;
            }

            if (_serverModules.TryGetModule(out TickManager tickManager))
            {
                _serverTickManager = tickManager;
                _isServerTicking = true;
                _serverTickManager.onPreTick += OnServerPreTick;
                _serverTickManager.onTick += OnServerTick;
                _serverTickManager.onPostTick += OnServerPostTick;
            }
            else
            {
                _serverTickManager = null;
                _isServerTicking = false;
            }

            UnsubscribePlayerModuleEvents(_clientPlayersManager);
            UnsubscribePlayerModuleEvents(_serverPlayersManager);

            if (_serverModules.TryGetModule(out PlayersManager playersManager))
            {
                _serverPlayersManager = playersManager;
                SubscribePlayerModuleEvents(_serverPlayersManager);
            }
            else
            {
                _serverPlayersManager = null;
            }

            UnsubscribeScenePlayersModuleEvents(_clientScenePlayersModule);
            UnsubscribeScenePlayersModuleEvents(_serverScenePlayersModule);

            if (_serverModules.TryGetModule(out ScenePlayersModule scenePlayersModuleInstance))
            {
                _serverScenePlayersModule = scenePlayersModuleInstance;
                SubscribeScenePlayersModuleEvents(_serverScenePlayersModule);
            }
            else
            {
                _serverScenePlayersModule = null;
            }

            if (_serverAuthModule != null)
                _serverAuthModule.onAuthenticationDenied -= OnAuthenticationDenied;

            if (_serverModules.TryGetModule(out AuthModule authModule))
            {
                _serverAuthModule = authModule;
                _serverAuthModule.onAuthenticationDenied += OnAuthenticationDenied;
            }
            else
            {
                _serverAuthModule = null;
            }

            if (!_serverModules.TryGetModule(out _serverBroadcast))
                _serverBroadcast = null;
            if (!_serverModules.TryGetModule(out _serverPlayersBroadcast))
                _serverPlayersBroadcast = null;
            if (!_serverModules.TryGetModule(out _serverSceneModule))
                _serverSceneModule = null;
            if (!_serverModules.TryGetModule(out _serverDeltaModule))
                _serverDeltaModule = null;
            if (!_serverModules.TryGetModule(out _serverRpcModule))
                _serverRpcModule = null;
            if (!_serverModules.TryGetModule(out _serverLODModule))
                _serverLODModule = null;

            if (_clientSpawnValidators.Count > 0 &&
                _serverModules.TryGetModule(out HierarchyFactory hierarchyFactory))
            {
                for (int i = 0; i < _clientSpawnValidators.Count; i++)
                {
                    var validate = _clientSpawnValidators[i];
                    hierarchyFactory.onClientSpawnValidate -= validate;
                    hierarchyFactory.onClientSpawnValidate += validate;
                }
            }

            _clientTickManager = null;
            _clientBroadcast = null;
            _clientPlayersManager = null;
            _clientPlayersBroadcast = null;
            _clientSceneModule = null;
            _clientScenePlayersModule = null;
            _clientDeltaModule = null;
            _clientRpcModule = null;
            _clientLODModule = null;
            _isCleaningClient = false;
        }

        private void SubscribePlayerModuleEvents(PlayersManager playersManager)
        {
            if (playersManager == null)
                return;

            playersManager.onPlayerJoined -= OnPlayerJoined;
            playersManager.onPlayerLeft -= OnPlayerLeft;
            playersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;

            playersManager.onPlayerJoined += OnPlayerJoined;
            playersManager.onPlayerLeft += OnPlayerLeft;
            playersManager.onLocalPlayerReceivedID += OnLocalPlayerReceivedID;
        }

        private void UnsubscribePlayerModuleEvents(PlayersManager playersManager)
        {
            if (playersManager == null)
                return;

            playersManager.onPlayerJoined -= OnPlayerJoined;
            playersManager.onPlayerLeft -= OnPlayerLeft;
            playersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
        }

        private void SubscribeScenePlayersModuleEvents(ScenePlayersModule scenePlayersModule)
        {
            if (scenePlayersModule == null)
                return;

            scenePlayersModule.onPlayerJoinedScene -= OnPlayerJoinedScene;
            scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
            scenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            scenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;

            scenePlayersModule.onPlayerJoinedScene += OnPlayerJoinedScene;
            scenePlayersModule.onPlayerLoadedScene += OnPlayerLoadedScene;
            scenePlayersModule.onPlayerUnloadedScene += OnPlayerUnloadedScene;
            scenePlayersModule.onPlayerLeftScene += OnPlayerLeftScene;
        }

        private void UnsubscribeScenePlayersModuleEvents(ScenePlayersModule scenePlayersModule)
        {
            if (scenePlayersModule == null)
                return;

            scenePlayersModule.onPlayerJoinedScene -= OnPlayerJoinedScene;
            scenePlayersModule.onPlayerLoadedScene -= OnPlayerLoadedScene;
            scenePlayersModule.onPlayerUnloadedScene -= OnPlayerUnloadedScene;
            scenePlayersModule.onPlayerLeftScene -= OnPlayerLeftScene;
        }

        private void OnServerPreTick() => onPreTick?.Invoke(true);

        private void OnServerTick()
        {
            OnTick();
            onTick?.Invoke(true);
        }

        private void OnServerPostTick() => onPostTick?.Invoke(true);

        private void OnClientPreTick() => onPreTick?.Invoke(false);

        private void OnClientTick()
        {
            if (!_isServerTicking)
                OnTick();
            onTick?.Invoke(false);
        }

        private void OnClientPostTick() => onPostTick?.Invoke(false);

        private static int _flagsDisableCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFlagsDisableCount()
        {
            _flagsDisableCount = 0;
        }

        /// <summary>
        /// Whether auto start flags are currently globally disabled.
        /// While disabled, <see cref="ShouldStart"/> always returns false regardless of the provided flags.
        /// </summary>
        public static bool areFlagsDisabled => _flagsDisableCount > 0;

        /// <summary>
        /// Globally disables auto start flags from working. Each call increments a counter;
        /// a matching number of <see cref="EnableFlags"/> calls is required to re-enable.
        /// </summary>
        public static void DisableFlags()
        {
            _flagsDisableCount++;
        }

        /// <summary>
        /// Decrements the global auto start flags disable counter. Flags are only re-enabled
        /// once the counter reaches zero (matching every <see cref="DisableFlags"/> call).
        /// </summary>
        public static void EnableFlags()
        {
            if (_flagsDisableCount <= 0)
            {
                PurrLogger.LogWarning($"{nameof(EnableFlags)} called without a matching {nameof(DisableFlags)}; ignoring.");
                return;
            }

            _flagsDisableCount--;
        }

        public static bool ShouldStart(StartFlags flags)
        {
            if (_flagsDisableCount > 0)
                return false;

            return (flags.HasFlag(StartFlags.Editor) && ApplicationContext.isMainEditor) ||
                   (flags.HasFlag(StartFlags.Clone) && ApplicationContext.isClone) ||
                   (flags.HasFlag(StartFlags.ClientBuild) && ApplicationContext.isClientBuild) ||
                   (flags.HasFlag(StartFlags.ServerBuild) && ApplicationContext.isServerBuild);
        }

#if ADDRESSABLES_PURRNET_SUPPORT
        /// <summary>
        /// Sets up a composite prefab provider that merges the regular NetworkPrefabs
        /// and the AddressableNetworkPrefabs into a single unified provider.
        /// Called after Addressable prefabs have been loaded.
        /// </summary>
        private void SetupCompositePrefabProvider()
        {
            if (!_addressableNetworkPrefabs || _addressableNetworkPrefabs.count == 0)
                return;

            var composite = new CompositePrefabProvider();

            if (_networkPrefabs)
                composite.AddProvider(_networkPrefabs);

            composite.AddProvider(_addressableNetworkPrefabs);
            SetPrefabProvider(composite);
        }

        private async void Start()
        {
            try
            {
                if (_addressableNetworkPrefabs && _addressableNetworkPrefabs.count > 0)
                {
                    try
                    {
                        if (_addressableNetworkPrefabs.preloadAtStartup)
                        {
                            await _addressableNetworkPrefabs.LoadAllAsync();
                        }
                        SetupCompositePrefabProvider();
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogError($"Failed to load Addressable network prefabs: {e.Message}\n{e.StackTrace}");
                    }
                }

                AutoStart();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
#else
        private void Start()
        {
            AutoStart();
        }
#endif

        private void AutoStart()
        {
            bool shouldStartServer = transport && ShouldStart(_startServerFlags);
            bool shouldStartClient = transport && ShouldStart(_startClientFlags);

            if (shouldStartServer)
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

            if (shouldStartClient)
            {
#if !UNITY_EDITOR
                PurrLogger.Log("Auto-Starting client...");
#endif
                StartClient();
            }
        }

        private void Update()
        {
            _serverModules.TriggerOnUpdate();
            _clientModules.TriggerOnUpdate();

            if (_transportLayer == null)
                return;

            SetReceiveDeferral(true);

            try
            {
                _transportLayer.UnityUpdate(Time.deltaTime);
            }
            finally
            {
                SetReceiveDeferral(false);
            }
        }

        private void SetReceiveDeferral(bool defer)
        {
            _serverBroadcast?.SetDeferNonImmediate(defer);
            _clientBroadcast?.SetDeferNonImmediate(defer);
        }

        private bool _sendFlushRequested;
        private readonly List<(Connection conn, bool asServer)> _connectionFlushRequests = new();

        public void RequestSendFlushThisFrame()
        {
            _sendFlushRequested = true;
        }

        public void RequestSendFlushThisFrame(Connection conn, bool asServer)
        {
            if (_sendFlushRequested)
                return;

            var request = (conn, asServer);
            if (!_connectionFlushRequests.Contains(request))
                _connectionFlushRequests.Add(request);
        }

        // Runs from UnityLatestUpdate's post phase (execution order 32000, after every
        // onLatestUpdate subscriber) so immediate RPCs queued by gameplay LateUpdate or
        // latest-update callbacks still flush this frame; this manager's own LateUpdate
        // would run before them at -999.
        private void FlushImmediateRPCsLate()
        {
            bool flushedAny = _sendFlushRequested;
            _sendFlushRequested = false;

            if (serverState == ConnectionState.Connected)
                flushedAny |= _serverModules.FlushImmediateRPCs();

            if (clientState == ConnectionState.Connected)
                flushedAny |= _clientModules.FlushImmediateRPCs();

            if (flushedAny)
            {
                _connectionFlushRequests.Clear();
                SendMessagesNow();
                return;
            }

            if (_connectionFlushRequests.Count == 0)
                return;

            bool flushedAll = _transportLayer != null;
            for (int i = 0; flushedAll && i < _connectionFlushRequests.Count; i++)
            {
                var (conn, asServer) = _connectionFlushRequests[i];
                flushedAll = _transportLayer.FlushConnection(conn, asServer);
            }

            _connectionFlushRequests.Clear();

            if (!flushedAll)
                SendMessagesNow();
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

        static readonly ProfilerMarker _preFixedUpdateMarker = new ProfilerMarker($"NetworkManager.OnPreFixedUpdate");
        static readonly ProfilerMarker _receiveMessagesMarker = new ProfilerMarker($"NetworkManager.ReceiveMessages");
        static readonly ProfilerMarker _receiveFixedUpdateMarker = new ProfilerMarker($"NetworkManager.OnFixedUpdate");

        static readonly ProfilerMarker _receivePostFixedUpdateMarker =
            new ProfilerMarker($"NetworkManager.OnPostFixedUpdate");

        static readonly ProfilerMarker _onBatchMarker = new ProfilerMarker($"NetworkManager.OnBatch");
        static readonly ProfilerMarker _onPostBatchMarker = new ProfilerMarker($"NetworkManager.OnPostBatch");
        static readonly ProfilerMarker _onSendMessagesMarker = new ProfilerMarker($"NetworkManager.SendMessages");

        private double _lastSendTime;

        private void SendMessagesNow(float fallbackDelta = 0f)
        {
            if (_transportLayer == null)
                return;

            var now = Time.unscaledTimeAsDouble;
            var sendDelta = _lastSendTime > 0 ? (float)(now - _lastSendTime) : fallbackDelta;
            _lastSendTime = now;
            using (_onSendMessagesMarker.Auto())
                _transportLayer.SendMessages(sendDelta);
        }

        private void OnTick()
        {
            var delta = tickModule?.tickDelta ?? Time.fixedUnscaledDeltaTime;
            bool serverConnected = serverState == ConnectionState.Connected;
            bool clientConnected = clientState == ConnectionState.Connected;

            using (_preFixedUpdateMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnPreFixedUpdate();

                if (clientConnected)
                    _clientModules.TriggerOnPreFixedUpdate();
            }

            using (_receiveMessagesMarker.Auto())
            {
                _serverBroadcast?.DrainDeferred();
                _clientBroadcast?.DrainDeferred();

                if (_transportLayer != null)
                    _transportLayer.ReceiveMessages(delta);
            }

            using (_receiveFixedUpdateMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnFixedUpdate();

                if (clientConnected)
                    _clientModules.TriggerOnFixedUpdate();
            }

            using (_receivePostFixedUpdateMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnPostFixedUpdate();

                if (clientConnected)
                    _clientModules.TriggerOnPostFixedUpdate();
            }

            using (_onBatchMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnBatch();

                if (clientConnected)
                    _clientModules.TriggerOnBatch();
            }

            using (_onPostBatchMarker.Auto())
            {
                if (serverConnected)
                    _serverModules.TriggerOnPostBatch();

                if (clientConnected)
                    _clientModules.TriggerOnPostBatch();
            }

            SendMessagesNow(delta);

            if (_isCleaningClient)
            {
                if (_preserveClientStateForHostMigration || isPromotingToServer || isTranferingToNewServer)
                {
                    _isCleaningClient = false;
                }
                else if (_clientModules.Cleanup())
                {
                    _clientModules.UnregisterModules();
                    CleanupClientModules();
                    TriggerUnsubscribeEvents(false);
                    _isCleaningClient = false;
                }
            }

            if (_isCleaningServer)
            {
                if (isPromotingToServer)
                {
                    _isCleaningServer = false;
                }
                else if (_serverModules.Cleanup())
                {
                    _isServerTicking = false;
                    _serverModules.UnregisterModules();
                    CleanupServerModules();
                    TriggerUnsubscribeEvents(true);
                    _isCleaningServer = false;
                }
            }

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            Statistics.MarkEndOfSampling();
#endif
        }

        public void FlushBatchedRPCs()
        {
            bool serverConnected = serverState == ConnectionState.Connected;
            bool clientConnected = clientState == ConnectionState.Connected;

            if (serverConnected)
                _serverModules.FlushBatchRPCs();

            if (clientConnected)
                _clientModules.FlushBatchRPCs();
        }

        private void OnDestroy()
        {
            UnityLatestUpdate.onPostLatestUpdate -= FlushImmediateRPCsLate;

            if (_transport)
            {
                StopClient();
                StopServer();

                if (clientState != ConnectionState.Disconnected)
                {
                    // drain while the PlayersBroadcaster bridge is still attached;
                    // module Disable order would detach it before the broadcaster's own drain
                    _clientBroadcast?.DrainDeferred();
                    _clientModules.UnregisterModules();
                }

                if (serverState != ConnectionState.Disconnected)
                {
                    _serverBroadcast?.DrainDeferred();
                    _isServerTicking = false;
                    _serverModules.UnregisterModules();
                }
            }

#if ADDRESSABLES_PURRNET_SUPPORT
            if (_addressableNetworkPrefabs)
                _addressableNetworkPrefabs.ReleaseAll();
#endif

            TeardownTransportLayer();
        }

        /// <summary>
        /// Compares the scene with the scene ID.
        /// Scene is a unity scene and SceneID is a network scene.
        /// </summary>
        /// <param name="scene">Unity scene to compare.</param>
        /// <param name="sceneID">Network scene to compare.</param>
        /// <returns>Whether the sceneID is linked to the unity scene.</returns>
        public bool MatchesSceneID(Scene scene, SceneID sceneID)
        {
            if (sceneModule.TryGetSceneID(scene, out var id))
                return id == sceneID;
            return false;
        }

        /// <summary>
        /// Tries to get the scene ID of the given scene.
        /// </summary>
        /// <param name="scene">Unity scene to get the scene ID of.</param>
        /// <param name="sceneID">The scene ID if found.</param>
        /// <returns>Whether the scene ID was found.</returns>
        public bool TryGetSceneID(Scene scene, out SceneID sceneID)
        {
            return sceneModule.TryGetSceneID(scene, out sceneID);
        }

        /// <summary>
        /// Tries to get the scene of the given scene ID.
        /// </summary>
        /// <param name="sceneID">The scene ID to get the scene of.</param>
        /// <param name="scene">The scene if found.</param>
        /// <returns>Whether the scene was found.</returns>
        public bool TryGetScene(SceneID sceneID, out Scene scene)
        {
            if (sceneModule.TryGetSceneState(sceneID, out var state))
            {
                scene = state.scene;
                return true;
            }

            scene = default;
            return false;
        }

        /// <summary>
        /// Returns all the scenes of a given player
        /// </summary>
        /// <param name="playerId">PlayerID for the player whose scenes you want</param>
        /// <param name="scenes">An array of the scenes</param>
        /// <returns></returns>
        public bool TryGetPlayerScenes(PlayerID playerId, out SceneID[] scenes)
        {
            scenes = null;

            if (scenePlayersModule == null || playerId == default)
                return false;

            if (scenePlayersModule.TryGetScenesForPlayer(playerId, out scenes))
                return true;

            return false;
        }

        /// <summary>
        /// Tries to get the scene state of the given scene ID.
        /// </summary>
        /// <param name="sceneID">The scene ID to get the state of.</param>
        /// <param name="state">The state if found.</param>
        /// <returns>Whether the state was found.</returns>
        public bool TryGetSceneState(SceneID sceneID, out SceneState state)
        {
            return sceneModule.TryGetSceneState(sceneID, out state);
        }

        /// <summary>
        /// Starts the server.
        /// This will start the transport server.
        /// </summary>
        public void StartServer()
        {
            if (!_transport)
                PurrLogger.Throw<InvalidOperationException>("Transport is not set (null).");
            
            _lastSendTime = 0d;
            _transport.StartServer(this);
        }

        private const float PromoteToServerStartRetryIntervalSeconds = 0.1f;

        public bool isPromotingToServer { get; private set; }

        /// <summary>
        /// Keeps the current client modules alive while an external host migration system
        /// prepares transport state before calling PromoteToServer or TransferToNewServer.
        /// </summary>
        public void PreserveClientStateForHostMigration()
        {
            _preserveClientStateForHostMigration = true;
            _isCleaningClient = false;
        }

        /// <summary>
        /// Releases a pending host migration preservation request when migration preparation fails.
        /// </summary>
        public void ReleaseClientStateForHostMigration()
        {
            _preserveClientStateForHostMigration = false;

            if (_transportLayer != null && _transportLayer.clientState == ConnectionState.Disconnected)
                _isCleaningClient = true;
        }

        /// <summary>
        /// Transitions the current NetworkManager instance into acting as a server.
        /// This method is used to promote the local instance from a client state
        /// into a server state, enabling server-specific functionalities.
        /// Great for host migration.
        /// It's your responsibility to prepare the transport for this transition.
        /// </summary>
        [ContextMenu("Promote To Server"), PurrContextButton]
        public async void PromoteToServer()
        {
            try
            {
                if (isPromotingToServer)
                    return;

                if (serverState != ConnectionState.Disconnected)
                {
                    PurrLogger.LogError("Cannot promote to server, you already are a server.");
                    return;
                }

                isPromotingToServer = true;
                _preserveClientStateForHostMigration = false;

                StopServer();
                StopClient();

                while (clientState != ConnectionState.Disconnected ||
                       serverState != ConnectionState.Disconnected)
                    await UnityLatestUpdate.Yield();

                StartServer();

                while (serverState != ConnectionState.Connected)
                {
                    var nextRetryAt = Time.unscaledTimeAsDouble + PromoteToServerStartRetryIntervalSeconds;
                    while (serverState != ConnectionState.Connected && Time.unscaledTimeAsDouble < nextRetryAt)
                        await UnityLatestUpdate.Yield();

                    if (serverState == ConnectionState.Disconnected)
                        _transport.StartServerInternalOnly();
                }

                _serverModules.PostPromoteToServer();

                _isCleaningClient = false;
                isPromotingToServer = false;

                if (_networkRules && _networkRules.ShouldMigrateAsHost())
                    StartClient();
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
                isPromotingToServer = false;
                StopClient();
                StopServer();
            }
            finally
            {
                isPromotingToServer = false;
            }
        }

        public bool isTranferingToNewServer { get; private set; }

        private const float TransferToNewServerConnectRetryIntervalSeconds = 2f;

        /// <summary>
        /// Transfers the current connection to a new server. This operation is asynchronous
        /// and is typically used to migrate a client to a different server while maintaining
        /// the connection state and relevant session data.
        /// It's your responsiblity to prepare the transport for the new server.
        /// </summary>
        [ContextMenu("TransferToNewServer"), PurrContextButton]
        public async void TransferToNewServer()
        {
            try
            {
                if (isTranferingToNewServer)
                    return;

                isTranferingToNewServer = true;
                _preserveClientStateForHostMigration = false;

                StopClient();
                StopServer();

                while (clientState != ConnectionState.Disconnected ||
                       serverState != ConnectionState.Disconnected)
                    await UnityLatestUpdate.Yield();

                StartClient();

                while (clientState != ConnectionState.Connected || !isLocalPlayerReady)
                {
                    var nextRetryAt = Time.unscaledTimeAsDouble + TransferToNewServerConnectRetryIntervalSeconds;
                    while ((clientState != ConnectionState.Connected || !isLocalPlayerReady) &&
                           Time.unscaledTimeAsDouble < nextRetryAt)
                        await UnityLatestUpdate.Yield();

                    if (clientState == ConnectionState.Connected && isLocalPlayerReady)
                        break;

                    _transport.StopClientInternalOnly();
                    _isCleaningClient = false;

                    while (_transportLayer != null && _transportLayer.clientState != ConnectionState.Disconnected)
                        await UnityLatestUpdate.Yield();

                    _transport.StartClientInternalOnly();
                }

                _clientModules.PostTransferToNewServer();
            }
            catch (Exception e)
            {
                PurrLogger.LogException(e);
                isTranferingToNewServer = false;
                StopClient();
                StopServer();
            }
            finally
            {
                isTranferingToNewServer = false;
            }
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
        /// Internal method to register the server modules.
        /// Avoid calling this method directly if you're not sure what you're doing.
        /// </summary>
        public void InternalRegisterServerModules()
        {
            if (!_ready)
                Awake();

            _isServerTicking = false;
            _serverModules.RegisterModules();
            _isSubscribedServer = true;
            TriggerSubscribeEvents(true);
        }

        /// <summary>
        /// Internal method to register the client modules.
        /// Avoid calling this method directly if you're not sure what you're doing.
        /// </summary>
        public void InternalRegisterClientModules()
        {
            if (!_ready)
                Awake();

            _clientModules.RegisterModules();
            _isSubscribedClient = true;
            TriggerSubscribeEvents(false);
        }

        internal void TriggerConnectionLeft(Connection connection, bool asServer)
        {
            if (asServer)
                _serverModules.OnLostConnection(connection, true);
            else _clientModules.OnLostConnection(connection, false);
        }

        bool _isSubscribedClient;
        bool _isSubscribedServer;

        public void InternalUnregisterServerModules()
        {
            if (!_isSubscribedServer)
                return;

            _isSubscribedServer = false;
            TriggerUnsubscribeEvents(true);
        }

        private void CleanupServerModules()
        {
            if (_serverTickManager != null)
            {
                _serverTickManager.onPreTick -= OnServerPreTick;
                _serverTickManager.onTick -= OnServerTick;
                _serverTickManager.onPostTick -= OnServerPostTick;
                _serverTickManager = null;
            }

            if (_serverPlayersManager != null)
            {
                _serverPlayersManager.onPlayerJoined -= OnPlayerJoined;
                _serverPlayersManager.onPlayerLeft -= OnPlayerLeft;
                _serverPlayersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
                _serverPlayersManager = null;
            }

            _serverSceneModule = null;
            _serverScenePlayersModule = null;
            _serverDeltaModule = null;
            _serverRpcModule = null;
            _serverLODModule = null;
        }

        public void InternalUnregisterClientModules()
        {
            if (!_isSubscribedClient)
                return;

            _isSubscribedClient = false;
            TriggerUnsubscribeEvents(false);
        }

        private void CleanupClientModules()
        {
            if (_clientTickManager != null)
            {
                _clientTickManager.onPreTick -= OnClientPreTick;
                _clientTickManager.onTick -= OnClientTick;
                _clientTickManager.onPostTick -= OnClientPostTick;
                _clientTickManager = null;
            }

            if (_clientPlayersManager != null)
            {
                _clientPlayersManager.onPlayerJoined -= OnPlayerJoined;
                _clientPlayersManager.onPlayerLeft -= OnPlayerLeft;
                _clientPlayersManager.onLocalPlayerReceivedID -= OnLocalPlayerReceivedID;
                _clientPlayersManager = null;
            }

            _clientSceneModule = null;
            _clientScenePlayersModule = null;
            _clientDeltaModule = null;
            _clientRpcModule = null;
            _clientLODModule = null;
        }

        private Coroutine _clientCoroutine;

        /// <summary>
        /// Starts the client.
        /// This will start the transport client.
        /// </summary>
        public void StartClient()
        {
            clientToServerConn = null;
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
            yield return null;
            while (clientState is ConnectionState.Disconnecting or ConnectionState.Connecting)
                yield return null;
            while (_isCleaningClient)
                yield return null;
            
            _lastSendTime = 0d;
            _transport.StartClient(this);
        }

        private void OnNewConnection(Connection conn, bool asServer)
        {
            if (IsProbeEvent(asServer))
                return;

            if (asServer)
                _serverModules.OnNewConnection(conn, true);
            else
            {
                clientToServerConn = conn;
                _clientModules.OnNewConnection(conn, false);
            }
        }

        private void OnLostConnection(Connection conn, DisconnectReason reason, bool asServer)
        {
            if (IsProbeEvent(asServer))
                return;

            if (asServer)
            {
                _serverBroadcast?.DrainDeferred(conn);
                _serverModules.OnLostConnection(conn, true);
            }
            else
            {
                _clientBroadcast?.DrainDeferred();
                clientToServerConn = null;
                _clientModules.OnLostConnection(conn, false);
            }
#if UNITY_EDITOR
            if (isOffline && networkRules && _stopPlayingOnDisconnect)
                EditorApplication.isPlaying = false;
#endif
        }

        private void OnDataReceived(Connection conn, ByteData data, bool asServer)
        {
            if (IsProbeEvent(asServer))
                return;

            if (asServer)
                _serverModules.OnDataReceived(conn, data, true);
            else _clientModules.OnDataReceived(conn, data, false);
        }

        private void OnConnectionState(ConnectionState state, bool asServer)
        {
            if (IsProbeEvent(asServer))
                return;

            if (asServer)
            {
                isServer = state == ConnectionState.Connected;
                _serverModules.OnConnectionState(state, true);
                onServerConnectionState?.Invoke(state);
                onAnyServerConnectionState?.Invoke(state);
            }
            else
            {
                isClient = state == ConnectionState.Connected;
                _clientModules.OnConnectionState(state, false);
                onClientConnectionState?.Invoke(state);
                onAnyClientConnectionState?.Invoke(state);
            }

            if (state == ConnectionState.Disconnected)
            {
                if (!asServer)
                    _telemetrySentClient = false;

                switch (asServer)
                {
                    case false:
                        _isCleaningClient = !_preserveClientStateForHostMigration;
                        break;
                    case true:
                        _isCleaningServer = true;
                        break;
                }
            }
        }

        /// <summary>
        /// Tries to get the module of the given type.
        /// </summary>
        /// <param name="asServer">Whether to get the server module or the client module.</param>
        /// <param name="module">The module if found, otherwise the default value of the type.</param>
        /// <typeparam name="T">The type of the module.</typeparam>
        /// <returns>Whether the module was found.</returns>
        public bool TryGetModule<T>(bool asServer, out T module) where T : INetworkModule
        {
            return asServer ? _serverModules.TryGetModule(out module) : _clientModules.TryGetModule(out module);
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

        public void ResetOriginalScene(Scene activeScene)
        {
            originalScene = activeScene;
            originalSceneBuildIndex = activeScene.buildIndex;
        }

        public bool IsDontDestroyOnLoad()
        {
            var scene = gameObject.scene;
            if (scene.name == "DontDestroyOnLoad")
                return true;
            return false;
        }

        public void Spawn(GameObject entry)
        {
            if (!entry)
                return;

            if (TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                TryGetSceneID(entry.scene, out var sceneID) &&
                factory.TryGetHierarchy(sceneID, out var hierarchy))
            {
                hierarchy.InternalSpawn(entry);
            }
        }

        public void CloseConnection(Connection conn)
        {
            if (isServer && _transportLayer != null)
                _transportLayer.CloseConnection(conn);
        }

        private RPCModule _clientRpcModule;
        private RPCModule _serverRpcModule;

        public int GetMTU(PlayerID playerId, Channel channel, bool asServer)
        {
            return asServer ? _serverPlayersManager.GetMTU(playerId, channel, true) : _clientPlayersManager.GetMTU(playerId, channel, false);
        }

        public bool TryGetRpcModule(bool asServer, out RPCModule module)
        {
            module = asServer ? _serverRpcModule : _clientRpcModule;
            return module != null;
        }
    }
}
