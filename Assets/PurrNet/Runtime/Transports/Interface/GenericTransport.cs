using System;
using System.Threading;
using System.Threading.Tasks;
using PurrNet.Logging;
using UnityEngine;

namespace PurrNet.Transports
{
    [DefaultExecutionOrder(-100)]
    public abstract class GenericTransport : MonoBehaviour
    {
        public const float DEFAULT_PING_TIMEOUT = 15f;
        private const float CONNECT_GRACE_SECONDS = 1f;
        private const float RTT_SETTLE_SECONDS = 0.3f;

        /// <summary>
        /// True while <see cref="Ping(CancellationToken)"/> is running a probe connection on this transport.
        /// The NetworkManager ignores client events from the transport while this is set.
        /// </summary>
        public virtual bool isPinging { get; private set; }

        internal Func<bool> externalPump;

        internal bool isPumpedExternally => externalPump != null && externalPump();

        /// <summary>
        /// Returns true if the transport is supported on the current platform.
        /// For example, WebGL does not support UDP or SteamTransport.
        /// This will return false if the transport is not supported.
        /// </summary>
        public abstract bool isSupported { get; }

        /// <summary>
        /// Access the underlying transport interface.
        /// This is used for low-level operations and should not be used directly.
        /// Unless you know what you are doing.
        /// </summary>
        public abstract ITransport transport { get; }

        bool TryGetNetworkManager(INetworkManager manager, out INetworkManager networkManager)
        {
            if (manager != null)
            {
                networkManager = manager;
                return true;
            }

            if (TryGetComponent<INetworkManager>(out networkManager))
                return true;

            var parentNm = GetComponentInParent<INetworkManager>();

            if (parentNm != null)
            {
                networkManager = parentNm;
                return true;
            }

            var childNm = GetComponentInChildren<INetworkManager>();

            if (childNm != null)
            {
                networkManager = childNm;
                return true;
            }

            if (NetworkManager.main)
            {
                networkManager = NetworkManager.main;
                return true;
            }

            networkManager = null;
            return false;
        }

        /// <summary>
        /// Starts the server.
        /// Optionally, you can pass a NetworkManager to register server modules.
        /// If you do not pass a NetworkManager, it will try to find one in the hierarchy.
        /// </summary>
        public void StartServer()
        {
            if (TryGetNetworkManager(NetworkManager.main, out var networkManager))
                networkManager.StartServer();
        }

        internal void StartServer(INetworkManager manager)
        {
            if (TryGetNetworkManager(manager, out var networkManager))
            {
                if (networkManager.serverState != ConnectionState.Disconnected)
                {
                    Debug.LogError($"[{GetType().Name}] Cannot start server since it is already running ({networkManager.serverState}).");
                    return;
                }
                networkManager.InternalRegisterServerModules();
            }

            StartServerInternal();
        }

        /// <summary>
        /// Stops the server.
        /// This will disconnect all clients.
        /// </summary>
        public void StopServer()
        {
            if (TryGetNetworkManager(NetworkManager.main, out var networkManager))
                networkManager.StopServer();
        }

        internal void StopServer(INetworkManager manager)
        {
            if (TryGetNetworkManager(manager, out var networkManager))
                networkManager.InternalUnregisterServerModules();

            StopServerInternal();
        }

        /// <summary>
        /// Starts the client.
        /// Optionally, you can pass a NetworkManager to register client modules.
        /// If you do not pass a NetworkManager, it will try to find one in the hierarchy.
        /// </summary>
        public void StartClient()
        {
            if (TryGetNetworkManager(NetworkManager.main, out var networkManager))
                networkManager.StartClient();
        }

        internal void StartClient(INetworkManager manager)
        {
            if (TryGetNetworkManager(manager, out var networkManager))
            {
                if (networkManager.clientState != ConnectionState.Disconnected)
                {
                    Debug.LogError($"[{GetType().Name}] Cannot start client since it is already running ({networkManager.clientState}).");
                    return;
                }
                networkManager.InternalRegisterClientModules();
            }

            StartClientInternal();
        }

        /// <summary>
        /// Stops the client.
        /// This will disconnect from the server.
        /// Optionally, you can pass a NetworkManager to register client modules.
        /// If you do not pass a NetworkManager, it will try to find one in the hierarchy.
        /// </summary>
        public void StopClient()
        {
            if (TryGetNetworkManager(NetworkManager.main, out var networkManager))
                networkManager.StopClient();
        }

        internal void StopClient(INetworkManager manager)
        {
            if (TryGetNetworkManager(manager, out var networkManager))
                networkManager.InternalUnregisterClientModules();

            StopClientInternal();
        }

        internal void StartClientInternalOnly()
        {
            StartClientInternal();
        }

        internal void StopClientInternalOnly()
        {
            StopClientInternal();
        }

        internal void StartServerInternalOnly()
        {
            StartServerInternal();
        }

        /// <summary>
        /// Measures latency to the server this transport is configured to connect to.
        /// Opens a transport level connection, reads the round trip time, then disconnects.
        /// The probe never registers with the NetworkManager or authenticates, so it never becomes a player.
        /// </summary>
        public Task<PingResult> Ping(CancellationToken token = default)
        {
            return Ping(DEFAULT_PING_TIMEOUT, token);
        }

        /// <inheritdoc cref="Ping(CancellationToken)"/>
        public Task<PingResult> Ping(float timeoutSeconds, CancellationToken token = default)
        {
            return PingInternal(null, 0, false, timeoutSeconds, token);
        }

        /// <summary>
        /// Measures latency to a specific address, using the same address semantics as <see cref="IConnectable.Connect"/>.
        /// </summary>
        public Task<PingResult> Ping(string address, ushort port, CancellationToken token = default)
        {
            return Ping(address, port, DEFAULT_PING_TIMEOUT, token);
        }

        /// <inheritdoc cref="Ping(string, ushort, CancellationToken)"/>
        public Task<PingResult> Ping(string address, ushort port, float timeoutSeconds, CancellationToken token = default)
        {
            return PingInternal(address, port, true, timeoutSeconds, token);
        }

        [ContextMenu("PurrNet/Test Ping Connection")]
        private async void TestPingConnection()
        {
            if (!Application.isPlaying)
            {
                PurrLogger.LogWarning($"[{GetType().Name}] Test Ping Connection only works in play mode.", this);
                return;
            }

            PurrLogger.Log($"[{GetType().Name}] Pinging with the current settings...", this);
            var result = await Ping();
            PurrLogger.Log($"[{GetType().Name}] {result}", this);
        }

        /// <summary>
        /// Runs the ping. Transports can override this when a plain probe connection is not the right way
        /// to measure latency, and call <see cref="ProbeConnection"/> for the default behaviour.
        /// </summary>
        protected virtual Task<PingResult> PingInternal(string address, ushort port, bool useAddress, float timeoutSeconds, CancellationToken token)
        {
            return ProbeConnection(address, port, useAddress, timeoutSeconds, token);
        }

        /// <summary>
        /// Default ping: connect at the transport level, read the round trip time, then disconnect.
        /// </summary>
        protected async Task<PingResult> ProbeConnection(string address, ushort port, bool useAddress, float timeoutSeconds, CancellationToken token)
        {
            if (!isSupported)
                return PingResult.Failed($"{GetType().Name} is not supported on this platform.");

            var layer = transport;

            if (layer == null)
                return PingResult.Failed("Transport layer is null.");

            if (isPinging)
                return PingResult.Failed("A ping is already in progress.");

            if (layer.clientState != ConnectionState.Disconnected)
                return PingResult.Failed("Client is already connected or connecting.");

            isPinging = true;

            try
            {
                var startedAt = Time.realtimeSinceStartupAsDouble;
                var deadline = startedAt + timeoutSeconds;

                if (useAddress)
                    layer.Connect(address, port);
                else StartClientInternal();

                bool leftDisconnected = false;
                var graceUntil = startedAt + CONNECT_GRACE_SECONDS;

                while (layer.clientState != ConnectionState.Connected)
                {
                    if (layer.clientState == ConnectionState.Disconnected)
                    {
                        if (leftDisconnected)
                            return PingResult.Failed("Transport disconnected while connecting.");

                        if (Time.realtimeSinceStartupAsDouble > graceUntil)
                            return PingResult.Failed("Transport never started connecting.");
                    }
                    else leftDisconnected = true;

                    if (token.IsCancellationRequested)
                        return PingResult.Failed("Cancelled.");

                    if (Time.realtimeSinceStartupAsDouble > deadline)
                        return PingResult.Failed($"Timed out after {timeoutSeconds:0.#}s while connecting.");

                    await PumpAndYield(layer);
                }

                int connectMs = (int)((Time.realtimeSinceStartupAsDouble - startedAt) * 1000);
                int rtt = -1;
                double settleUntil = 0;

                while (layer.measuresRoundTripTime && layer.clientState == ConnectionState.Connected)
                {
                    int sample = layer.GetRoundTripTime(default, false);
                    var now = Time.realtimeSinceStartupAsDouble;

                    if (sample >= 0)
                    {
                        if (rtt < 0)
                            settleUntil = now + RTT_SETTLE_SECONDS;

                        if (rtt < 0 || sample < rtt)
                            rtt = sample;

                        if (now >= settleUntil)
                            break;
                    }

                    if (token.IsCancellationRequested || now > deadline)
                        break;

                    await PumpAndYield(layer);
                }

                return new PingResult(rtt, connectMs, layer.clientLinkDescription);
            }
            catch (System.Exception e)
            {
                return PingResult.Failed(e.Message);
            }
            finally
            {
                await StopProbe(layer);
                isPinging = false;
            }
        }

        private async Task StopProbe(ITransport layer)
        {
            layer.Disconnect();

            var deadline = Time.realtimeSinceStartupAsDouble + 1f;

            while (layer.clientState != ConnectionState.Disconnected && Time.realtimeSinceStartupAsDouble < deadline)
                await PumpAndYield(layer);
        }

        private async Task PumpAndYield(ITransport layer)
        {
            if (!isPumpedExternally)
            {
                float delta = Time.unscaledDeltaTime;
                layer.UnityUpdate(delta);
                layer.ReceiveMessages(delta);
                layer.SendMessages(delta);
            }

            await UnityLatestUpdate.Yield();
        }

        protected abstract void StartClientInternal();

        protected abstract void StartServerInternal();

        protected void StopClientInternal() => transport.Disconnect();

        protected void StopServerInternal() => transport.StopListening();
    }
}
