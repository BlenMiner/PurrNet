using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;

public class Bootstrap : Scenario
{
    private static bool _runStarted;

    [SerializeField] private NetworkManager _networkManager;
    [SerializeField] private float _connectionTimeout = 15f;
    [SerializeField] private float _timeBetweenScenarios = 0.1f;

    [Header("Editor overrides (used when -role / -count are absent)")]
    [Tooltip("Role used by the main editor instance when no -role argument is provided. " +
             "Clones (Unity Multiplayer Playmode) always run as Client regardless of this.")]
    [SerializeField] private NetworkRole _editorRole = NetworkRole.Host;
    [Tooltip("Expected connection count when no -count argument is provided.")]
    [SerializeField] private int _editorExpectedConnections = 2;
    [Tooltip("Run benchmark mode in the editor (Host + Multiplayer Playmode clients) without the -bench arg.")]
    [SerializeField] private bool _editorBenchmarkMode;

    [Header("Benchmark")]
    [Tooltip("Object holding the BenchmarkScenario components. Reorder or disable its children to change which benchmarks run.")]
    [SerializeField] private Transform _benchmarkScenarios;
    [Tooltip("Steady-state measurement window in seconds (overridden by -benchSeconds).")]
    [SerializeField] private float _benchSeconds = 20f;

    private NetworkRole _role;
    private int _expectedConnections;
    private string _resultsPath;
    private string _scenarioFilter;

    private bool _benchmarkMode;
    private bool _measured = true;
    private int? _benchObjects;
    private float? _benchPingRate;
    private string _serverHost;
    private ushort? _port;

    private Scenario[] _scenarios;
    private ScenarioDetails?[] _results;

    private CancellationTokenSource _runCts;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRunState()
    {
        _runStarted = false;
    }

    private void Awake()
    {
        if (_runStarted)
        {
            enabled = false;
            return;
        }

        LoadArgs();

        if (_benchmarkMode)
        {
            _scenarios = DiscoverBenchmarkScenarios();
        }
        else
        {
            // Some migration variants end the run and are authored inactive for targeted execution.
            _scenarios = GetComponentsInChildren<Scenario>(includeInactive: !string.IsNullOrEmpty(_scenarioFilter));
        }

        ApplyScenarioFilter();
        _results = new ScenarioDetails?[_scenarios.Length];
    }

    private void ApplyScenarioFilter()
    {
        if (string.IsNullOrEmpty(_scenarioFilter))
            return;

        var filtered = new System.Collections.Generic.List<Scenario>();
        for (var i = 0; i < _scenarios.Length; i++)
        {
            var scenario = _scenarios[i];
            if (scenario == this || scenario.GetType().Name == _scenarioFilter)
                filtered.Add(scenario);
        }

        if (filtered.Count == 1)
        {
            Debug.LogError($"Scenario filter '{_scenarioFilter}' did not match any scenario.");
            Application.Quit(-1);
            return;
        }

        _scenarios = filtered.ToArray();
    }

    private Scenario[] DiscoverBenchmarkScenarios()
    {
        if (!_benchmarkScenarios)
        {
            Debug.LogError($"Benchmark mode requires '{nameof(_benchmarkScenarios)}' to be assigned on Bootstrap.");
            return new Scenario[] { this };
        }

        var authored = _benchmarkScenarios.GetComponentsInChildren<Scenario>();
        var scenarios = new Scenario[authored.Length + 1];
        scenarios[0] = this;

        for (var i = 0; i < authored.Length; i++)
        {
            scenarios[i + 1] = authored[i];
            if (authored[i] is IBenchmarkScenario bench)
                bench.ApplyOverrides(_benchObjects, _benchPingRate);
        }

        return scenarios;
    }

    private void Start()
    {
        if (_runStarted)
            return;

        _runStarted = true;

        if (!_networkManager.transport)
        {
            Debug.LogError($"No network transport found");
            Application.Quit(-1);
            return;
        }

        _runCts = new CancellationTokenSource();

        ConfigureTransport();

        if (CommandLineUtils.HasFlag("-dumpBuildCompat"))
            DumpBuildCompatibility();

        var ctx = new ScenarioContext
        {
            role = _role,
            expectedConnections = _expectedConnections,
            networkManager = _networkManager,
            cancellationToken = _runCts.Token,
            benchSeconds = _benchSeconds,
            measured = _measured
        };

        for (var i = 0; i < _scenarios.Length; i++)
            _scenarios[i].Setup(ctx, _networkManager);

        SubscribeToDataSent(_networkManager.transport.transport);

        RunScenarios();
    }

    private ITransport _lastTransport;

    private void SubscribeToDataSent(ITransport transport)
    {
        if (_lastTransport != null)
        {
            _lastTransport.onDataSent -= OnDataSentCallback;
            _lastTransport.onDataReceived -= OnDataReceivedCallback;
        }
        transport.onDataSent += OnDataSentCallback;
        transport.onDataReceived += OnDataReceivedCallback;
        _lastTransport = transport;
    }

    private ulong _dataSent = 0;
    private ulong _dataReceived = 0;

    private void OnDataSentCallback(Connection conn, ByteData data, bool asServer)
    {
        _dataSent += (ulong)data.length;
    }

    private void OnDataReceivedCallback(Connection conn, ByteData data, bool asServer)
    {
        _dataReceived += (ulong)data.length;
    }

    private void LoadArgs()
    {
        if (!TryResolveRole(out _role))
        {
            Application.Quit(-1);
            return;
        }

        if (_role != NetworkRole.Client)
        {
            if (CommandLineUtils.TryGetArgument("-count", out var countString))
            {
                if (!int.TryParse(countString, out _expectedConnections))
                {
                    Debug.LogError($"Could not parse -count value '{countString}'");
                    Application.Quit(-1);
                    return;
                }
            }
            else
            {
#if UNITY_EDITOR
                _expectedConnections = _editorExpectedConnections;
#else
                Debug.LogError($"Expected -count argument");
                Application.Quit(-1);
                return;
#endif
            }
        }

        CommandLineUtils.TryGetArgument("-results", out _resultsPath);
        CommandLineUtils.TryGetArgument("-scenario", out _scenarioFilter);

        LoadBenchmarkArgs();
    }

    private void LoadBenchmarkArgs()
    {
        _benchmarkMode = CommandLineUtils.HasFlag("-bench");
#if UNITY_EDITOR
        _benchmarkMode |= _editorBenchmarkMode;
#endif
        _measured = !CommandLineUtils.HasFlag("-loadgen");

        if (CommandLineUtils.TryGetArgument("-benchSeconds", out var seconds)
            && float.TryParse(seconds, out var parsedSeconds))
            _benchSeconds = parsedSeconds;

        if (CommandLineUtils.TryGetArgument("-benchObjects", out var objects)
            && int.TryParse(objects, out var parsedObjects))
            _benchObjects = parsedObjects;

        if (CommandLineUtils.TryGetArgument("-benchPingRate", out var pingRate)
            && float.TryParse(pingRate, out var parsedPingRate))
            _benchPingRate = parsedPingRate;

        if (CommandLineUtils.TryGetArgument("-connectTimeout", out var connectTimeout)
            && float.TryParse(connectTimeout, out var parsedConnectTimeout))
            _connectionTimeout = parsedConnectTimeout;

        CommandLineUtils.TryGetArgument("-serverHost", out _serverHost);

        if (CommandLineUtils.TryGetArgument("-port", out var port)
            && ushort.TryParse(port, out var parsedPort))
            _port = parsedPort;
    }

    private void ConfigureTransport()
    {
        if (_networkManager.transport is not UDPTransport udp)
            return;

        if (_port.HasValue)
            udp.serverPort = _port.Value;

        if (_role == NetworkRole.Client && !string.IsNullOrEmpty(_serverHost))
            udp.address = _serverHost;

        if (_role != NetworkRole.Client)
            udp.maxConnections = Mathf.Max(udp.maxConnections, _expectedConnections + 8);
    }

    private bool TryResolveRole(out NetworkRole resolved)
    {
        resolved = default;

        if (CommandLineUtils.TryGetArgument("-role", out var role))
        {
            switch (role)
            {
                case "server": resolved = NetworkRole.Server; return true;
                case "client": resolved = NetworkRole.Client; return true;
                case "host":   resolved = NetworkRole.Host;   return true;
                default:
                    Debug.LogError($"Unknown role '{role}'");
                    return false;
            }
        }

#if UNITY_EDITOR
        // Clones (Unity Multiplayer Playmode) always join as plain clients; the main editor
        // uses the inspector-configured override so individual test scenes can opt in/out
        // of host mode without re-baking command-line scripts.
        resolved = PurrNet.Utils.ApplicationContext.isClone ? NetworkRole.Client : _editorRole;
        return true;
#else
        Debug.LogError($"Expected -role argument");
        return false;
#endif
    }

    private static void DumpBuildCompatibility()
    {
        Debug.Log(
            "[BuildCompat] " +
            $"unity={Application.unityVersion}, " +
            $"platform={Application.platform}, " +
            $"product={Application.productName}, " +
            $"identifier={Application.identifier}, " +
            $"purrnet={NetworkManager.version}");

        var sceneCount = SceneManager.sceneCountInBuildSettings;
        for (var i = 0; i < sceneCount; i++)
        {
            var path = SceneUtility.GetScenePathByBuildIndex(i);
            Debug.Log($"[BuildCompat] scene[{i}] path='{path}' hash={PurrNet.Utils.Hasher.Hash(path)}");
        }

        DumpTypeIdentity("PurrNet.Modules.SceneActionsBatch, PurrNet.Runtime");
        DumpTypeIdentity("PurrNet.Modules.FirstSceneActionsBatch, PurrNet.Runtime");
        DumpTypeIdentity("PurrNet.Modules.LoadSceneAction, PurrNet.Runtime");
        DumpTypeIdentity("PurrNet.Modules.ClientFinishedLoadingScene, PurrNet.Runtime");
    }

    private static void DumpTypeIdentity(string assemblyQualifiedName)
    {
        var type = Type.GetType(assemblyQualifiedName);
        if (type == null)
        {
            Debug.LogWarning($"[BuildCompat] type '{assemblyQualifiedName}' not found");
            return;
        }

        Debug.Log(
            "[BuildCompat] type " +
            $"fullName='{type.FullName}', " +
            $"assembly='{type.Assembly.GetName().Name}', " +
            $"assemblyFullName='{type.Assembly.FullName}', " +
            $"hash={PurrNet.Utils.Hasher.Hash(type)}");
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        Assert.IsTrue(_networkManager.isOffline);
        Assert.AreEqual(_networkManager.clientState, ConnectionState.Disconnected, "Client is not connected");
        Assert.AreEqual(_networkManager.serverState, ConnectionState.Disconnected, "Server is not started");

        for (var i = 0; i < _scenarios.Length; i++)
        {
            if (_scenarios[i] is IScenarioConnectionPreparation preparation)
                await preparation.BeforeConnection(ctx);
        }

        if (ctx.isServer)
        {
            _networkManager.StartServer();
            for (var i = 0; i < _scenarios.Length; i++)
            {
                if (_scenarios[i] is IScenarioConnectionPreparation preparation)
                    await preparation.AfterServerStarted(ctx);
            }
        }
        if (ctx.isClient)
            _networkManager.StartClient();

        await UniTaskUtils.WaitWithTimeout(IsConnected, _connectionTimeout, ctx.cancellationToken);

        if (!ctx.isServer)
            return ScenarioResult.Ok();

        try
        {
            await UniTaskUtils.WaitWithTimeout(AllConnected, _connectionTimeout, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            int transportConnections = _networkManager.transport is UDPTransport udp
                ? udp.connections.Count
                : -1;
            Debug.LogError(
                $"[Bootstrap] connection timeout: players={_networkManager.playerCount}/{_expectedConnections}, " +
                $"transportConnections={transportConnections}, " +
                $"role={_role}, timeout={_connectionTimeout:F0}s");
            throw;
        }
        return ScenarioResult.Ok();
    }

    bool IsConnected()
    {
        bool isServer = _networkManager.isServer;
        bool isClient = _networkManager.isClient;
        switch (_role)
        {
            case NetworkRole.Server or NetworkRole.Host when !isServer:
            case NetworkRole.Client or NetworkRole.Host when !isClient:
                return false;
            default:
                return isServer || isClient;
        }
    }

    bool AllConnected()
    {
        return _networkManager.playerCount >= _expectedConnections;
    }

    private async void RunScenarios()
    {
        bool anyFailed = false;

        try
        {
            var ctx = new ScenarioContext
            {
                role = _role,
                expectedConnections = _expectedConnections,
                networkManager = _networkManager,
                cancellationToken = _runCts.Token,
                benchSeconds = _benchSeconds,
                measured = _measured
            };

            // Scenario 0 is Bootstrap itself (the connection scenario). It must run
            // unconditionally on every peer before the sequencer can drive anything —
            // clients don't exist on the server yet, so there's no one to coordinate with.
            if (_scenarios.Length > 0)
            {
                bool bootstrapFailed = await RunOne(0, ctx);
                if (bootstrapFailed)
                {
                    anyFailed = true;

                    // No later scenario can make progress without the complete peer set.
                    // Tell already-connected clients to leave their start wait, then let
                    // every process write its failure result and exit.
                    if (ctx.isServer && _networkManager.isServer)
                    {
                        ScenarioSequencer.IssueSequenceComplete();
                        await ScenarioSequencer.WaitForEndOfRunHandshake(ctx);
                    }
                    return;
                }
            }

            if (ctx.isServer)
            {
                for (var i = 1; i < _scenarios.Length; i++)
                {
                    ScenarioSequencer.IssueStart(i);

                    if (await RunOne(i, ctx))
                        anyFailed = true;

                    if (!_networkManager.isServer)
                        break;

                    await ScenarioSequencer.WaitForAllAcks(ctx, i);

                    if (i == _scenarios.Length - 1)
                        break;

                    await UniTask.WaitForSeconds(_timeBetweenScenarios);
                }

                if (_networkManager.isServer)
                {
                    ScenarioSequencer.IssueSequenceComplete();
                    // End-of-run handshake: bounded wait so a crashed/already-exited peer
                    // can't strand the server in the finally-then-Quit path.
                    await ScenarioSequencer.WaitForEndOfRunHandshake(ctx);
                }
            }
            else
            {
                for (var i = 1; i < _scenarios.Length; i++)
                {
                    await ScenarioSequencer.WaitForStart(ctx, i);

                    // Defensive: server signalled the run is over (or crashed). Stop
                    // running scenarios the server isn't tracking.
                    if (ScenarioSequencer.SequenceComplete || !_networkManager.isClient)
                        break;

                    if (await RunOne(i, ctx))
                        anyFailed = true;

                    ScenarioSequencer.AckLocalDone(ctx, i);

                    if (i == _scenarios.Length - 1)
                        break;

                    await UniTask.WaitForSeconds(_timeBetweenScenarios);
                }

                // Wait for the server's sequence-complete broadcast, then ack so
                // the server can exit its handshake wait cleanly. This is best-effort:
                // a terminal scenario may intentionally stop the original server.
                if (_networkManager.isClient && await TryWaitForSequenceComplete(ctx))
                {
                    ScenarioSequencer.AckEndOfRun(ctx);
                    // Give the ack a couple frames to flush through transport before
                    // the finally block quits the process.
                    await UniTask.NextFrame();
                    await UniTask.NextFrame();
                }
            }
        }
        catch (Exception e)
        {
            anyFailed = true;
            Debug.LogException(e);
        }
        finally
        {
            bool anyResultFailed = false;
            for (var i = 0; i < _results.Length; i++)
            {
                if (!_results[i].HasValue || !_results[i].Value.result.success)
                {
                    anyResultFailed = true;
                    break;
                }
            }

            WriteResults();

#if UNITY_EDITOR
            if (anyResultFailed || anyFailed)
                WriteFailedResults();
#else
            Application.Quit(anyResultFailed || anyFailed ? -1 : 0);
#endif
        }
    }

    private async UniTask<bool> TryWaitForSequenceComplete(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => ScenarioSequencer.SequenceComplete,
                30f,
                ctx.cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private async UniTask<bool> RunOne(int i, ScenarioContext ctx)
    {
        _dataSent = 0;
        _dataReceived = 0;

        var scenario = _scenarios[i];
        long startTick = DateTime.Now.Ticks;
        var result = await GetResult(scenario, ctx, i);
        var elapsedTick = DateTime.Now.Ticks - startTick;
        var elapsedMs = elapsedTick / (double)TimeSpan.TicksPerMillisecond;

        var details = new ScenarioDetails
        {
            name = scenario.GetType().Name,
            result = result,
            durationInMs = elapsedMs,
            dataSent = _dataSent,
            dataReceived = _dataReceived,
            measured = _measured
        };

        if (scenario is IBenchmarkScenario bench && bench.LastMetrics.HasValue)
            details.benchmark = bench.LastMetrics;

        _results[i] = details;

        if (!result.success)
            Debug.LogError($"Scenario [{i}] `{details.name}` failed: {result.message}");

        return !result.success;
    }

    private static async Task<ScenarioResult> GetResult(Scenario scenario, ScenarioContext ctx, int i)
    {
        ScenarioResult details;

        try
        {
            details = await scenario.RunScenario(ctx);
        }
        catch (Exception e)
        {
            details = ScenarioResult.Fail(e.Message);
            Debug.LogError($"Scenario [{i}] `{scenario.name}` failed with: {e}");
        }

        return details;
    }

    private void WriteResults()
    {
        var json = JArray.FromObject(_results).ToString(Formatting.Indented);
        Debug.Log(json);

        if (string.IsNullOrEmpty(_resultsPath))
            return;

        try
        {
            var dir = Path.GetDirectoryName(_resultsPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_resultsPath, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write results to '{_resultsPath}': {e}");
        }
    }

    private void WriteFailedResults()
    {
        var failedResults = new JArray();

        for (var i = 0; i < _results.Length; i++)
        {
            if (_results[i].HasValue)
            {
                var details = _results[i].Value;
                if (!details.result.success)
                    failedResults.Add(JObject.FromObject(details));

                continue;
            }

            failedResults.Add(JObject.FromObject(new ScenarioDetails
            {
                name = _scenarios[i].GetType().Name,
                result = ScenarioResult.Fail("Scenario did not run."),
                durationInMs = 0,
                dataSent = 0,
                dataReceived = 0,
                measured = _measured
            }));
        }

        Debug.LogError("Failed test results:\n" + failedResults.ToString(Formatting.Indented));
    }
}
