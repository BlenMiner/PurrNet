using System;
using PurrNet.Packing;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet.Modules
{
    public class TickManager : INetworkModule, IUpdate, IPromoteToServerModule
    {
        /// <summary>
        /// Tracks local ticks starting from client connection to the server for synchronization.
        /// </summary>
        public uint localTick { get; private set; }

        /// <summary>
        /// Tracks the ticks aligned with the servers ticks for synchronization.
        /// </summary>
        public uint syncedTick
        {
            get
            {
                if (_asServer)
                    return localTick;
                return _syncedTick;
            }
            private set => _syncedTick = value;
        }

        /// <summary>
        /// Round trip time in seconds, measured by the tick-sync ping: local time from sending the
        /// request to receiving the server's response. The ping bypasses tick batching on both ends
        /// (immediate receive, same-frame send flush), so this is network round trip plus at most a
        /// frame of scheduling on each side rather than the tick interval.
        /// </summary>
        public double rtt { get; private set; }

        /// <summary>
        /// Uses floating point values for ticks to allow fractional updates, allowing to get precise tick timing within update
        /// </summary>
        public double floatingPoint { get; private set; }

        public double syncedPreciseTick => syncedTick + floatingPoint;

        public double rollbackTick
        {
            get
            {
                var halfRttInTicks = TimeToPreciseTick(rtt) / 2;
                return syncedPreciseTick - halfRttInTicks;
            }
        }

        /// <summary>
        /// Gives the exact step of the tick, including the floating point.
        /// </summary>
        public double localPreciseTick => localTick + floatingPoint;

        /// <summary>
        /// The amount of ticks per second
        /// </summary>
        public int tickRate { get; private set; }

        /// <summary>
        /// Time between each tick as a float
        /// </summary>
        public readonly float tickDelta;

        /// <summary>
        /// Time between each tick as a double
        /// </summary>
        public readonly double tickDeltaDouble;

        public event Action onPreTick, onTick, onPostTick;
        public event Action onReliablePreTick, onReliableTick, onReliablePostTick;

        private readonly PurrAction<ITickListener> _tickListeners = new(static listener => listener.OnTick(), 256);
        private readonly PurrAction<NetworkIdentity> _identityTicks;

        /// <summary>
        /// Lower clamp for <see cref="tickPacingScale"/>.
        /// </summary>
        public const double minTickPacingScale = 0.98d;

        /// <summary>
        /// Upper clamp for <see cref="tickPacingScale"/>.
        /// </summary>
        public const double maxTickPacingScale = 1.02d;

        private double _tickPacingScale = 1d;

        /// <summary>
        /// Wall-clock multiplier applied to the tick accumulation interval. Values below 1 fire
        /// ticks slightly faster, values above 1 slightly slower. Clamped to
        /// [<see cref="minTickPacingScale"/>, <see cref="maxTickPacingScale"/>]. The simulation
        /// tick delta is unaffected; only the real time between ticks bends.
        /// </summary>
        public double tickPacingScale
        {
            get => _tickPacingScale;
            set
            {
                if (double.IsNaN(value))
                    return;

                var clamped = Math.Clamp(value, minTickPacingScale, maxTickPacingScale);
                if (clamped == _tickPacingScale)
                    return;

                // rebase the current fractional phase onto the new interval so the
                // precise tick clocks stay monotonic across a scale change
                var now = Time.unscaledTimeAsDouble;
                var elapsed = now - _lastTickTime;
                if (elapsed > 0)
                    _lastTickTime = now - elapsed * (clamped / _tickPacingScale);

                _tickPacingScale = clamped;
            }
        }

        /// <summary>
        /// Wall-clock time of the most recent tick boundary, in Time.unscaledTimeAsDouble seconds.
        /// </summary>
        public double lastTickTime => _lastTickTime;

        private readonly INetworkManager _networkManager;
        private bool _asServer;

        private uint _syncedTick;
        private float _lastSyncTime = -99;
        private double _lastTickTime;
        private const int MaxTickPerFrame = 5;

        private readonly BroadcastModule _broadcaster;

        public TickManager(int tickRate, INetworkManager nm, BroadcastModule broadcaster, bool asServer)
        {
            _asServer = asServer;
            _identityTicks = new PurrAction<NetworkIdentity>(
                asServer ? static identity => identity.ServerTick() : static identity => identity.ClientTick(), 256);
            _lastTickTime = Time.unscaledTimeAsDouble;
            _networkManager = nm;
            tickDelta = 1f / tickRate;
            tickDeltaDouble = 1d / tickRate;
            _broadcaster = broadcaster;
            this.tickRate = tickRate;
        }

        public void Enable(bool asServer)
        {
            _broadcaster.RegisterImmediateType<TickManagerRequestLocalTick>();
            _broadcaster.RegisterImmediateType<TickManagerResponseLocalTick>();

            if (asServer)
                _broadcaster.Subscribe<TickManagerRequestLocalTick>(OnClientRequestedPing);
            else
                _broadcaster.Subscribe<TickManagerResponseLocalTick>(OnServerRespondedPing);
        }

        public void Disable(bool asServer)
        {
            if (asServer)
                _broadcaster.Unsubscribe<TickManagerRequestLocalTick>(OnClientRequestedPing);
            else
                _broadcaster.Unsubscribe<TickManagerResponseLocalTick>(OnServerRespondedPing);

            _broadcaster.UnregisterImmediateType<TickManagerRequestLocalTick>();
            _broadcaster.UnregisterImmediateType<TickManagerResponseLocalTick>();
        }

        public void PromoteToServerModule()
        {
            Disable(false);
            _asServer = true;
            Enable(true);
        }

        public void PostPromoteToServerModule() { }

        public void Update()
        {
            HandleTick();
            floatingPoint = (Time.unscaledTimeAsDouble - _lastTickTime) / (tickDeltaDouble * _tickPacingScale);

            if (_networkManager.isServer || !_networkManager.isClient)
                return;

            var rules = _networkManager.networkRules;

            if (!rules)
                return;

            if (_lastSyncTime + rules.GetSyncedTickUpdateInterval() < Time.unscaledTime)
            {
                _lastSyncTime = Time.unscaledTime;
                HandleTickSync();
            }
        }

        private void HandleTick()
        {
            int ticksHandled = 0;
            double interval = tickDeltaDouble * _tickPacingScale;

            while (_lastTickTime + interval <= Time.unscaledTimeAsDouble)
            {
                _lastTickTime += interval;
                _syncedTick++;
                localTick++;
                floatingPoint = 0;

                bool triggerNormalTicks = ticksHandled < MaxTickPerFrame;

                if (triggerNormalTicks)
                    onPreTick?.Invoke();
                onReliablePreTick?.Invoke();

                if (triggerNormalTicks)
                {
                    onTick?.Invoke();
                    _identityTicks.Invoke();
                    _tickListeners.Invoke();
                }
                onReliableTick?.Invoke();

                if (triggerNormalTicks)
                    onPostTick?.Invoke();
                onReliablePostTick?.Invoke();

                ticksHandled++;
            }
        }
        
        internal int AddIdentityTick(NetworkIdentity identity) => _identityTicks.Add(identity);

        internal void RemoveIdentityTick(int handle, NetworkIdentity identity) => _identityTicks.RemoveAt(handle, identity);

        public void AddTickListener(ITickListener listener)
        {
            _tickListeners.Add(listener);
        }
        
        public void RemoveTickListener(ITickListener listener)
        {
            _tickListeners.Remove(listener);
        }

        /// <summary>
        /// Converts the input tick to float time
        /// </summary>
        /// <param name="tick">The amount of ticks to convert to time</param>
        public float TickToTime(uint tick)
        {
            return tick / (float)tickRate;
        }

        /// <summary>
        /// Converts the precise input tick to float time
        /// </summary>
        /// <param name="preciseTick">The precise tick to convert</param>
        public float PreciseTickToTime(double preciseTick)
        {
            return (float)(preciseTick / tickRate);
        }

        /// <summary>
        /// Converts the input float time to ticks
        /// </summary>
        /// <param name="time">The amount of time to convert</param>
        public uint TimeToTick(float time)
        {
            return (uint)Math.Round(time * tickRate);
        }

        /// <summary>
        /// Converts the input float time to precise ticks (double)
        /// </summary>
        /// <param name="time">And amount of time to convert</param>
        public double TimeToPreciseTick(float time)
        {
            return time * tickRate;
        }

        /// <summary>
        /// Converts the input float time to precise ticks (double)
        /// </summary>
        /// <param name="time">And amount of time to convert</param>
        public double TimeToPreciseTick(double time)
        {
            return time * tickRate;
        }

        private void HandleTickSync()
        {
            try
            {
                float requestSendTime = Time.unscaledTime;

                _broadcaster.SendToServer(new TickManagerRequestLocalTick
                {
                    requestTime = requestSendTime
                });
            }
            catch
            {
                //PurrLogger.LogError($"Failed to sync tick: {e}");
            }
        }

        private void OnServerRespondedPing(Connection conn, TickManagerResponseLocalTick data, bool asServer)
        {
            rtt = Time.unscaledTime - data.requestTime;
            float halfRTT = (float)rtt / 2;
            syncedTick = data.tick + TimeToTick(halfRTT);
        }

        private void OnClientRequestedPing(Connection conn, TickManagerRequestLocalTick data, bool asServer)
        {
            var packet = new TickManagerResponseLocalTick
            {
                requestTime = data.requestTime,
                tick = localTick
            };

            _broadcaster.Send(conn, packet);
        }
    }

    public struct TickManagerRequestLocalTick : IPackedAuto
    {
        public float requestTime;
    }

    public struct TickManagerResponseLocalTick : IPackedAuto
    {
        public float requestTime;
        public uint tick;
    }
}
