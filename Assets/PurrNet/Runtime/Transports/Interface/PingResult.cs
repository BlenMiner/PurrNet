using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PurrNet.Transports
{
    /// <summary>
    /// Outcome of a transport ping. Latency is measured over a short lived transport level
    /// connection that never reaches the NetworkManager or the authenticator.
    /// </summary>
    public readonly struct PingResult
    {
        /// <summary>Whether the transport managed to connect at all.</summary>
        public readonly bool success;

        /// <summary>Round trip time reported by the transport in milliseconds, or -1 if it could not be measured.</summary>
        public readonly int roundTripTimeMs;

        /// <summary>Time it took to establish the transport connection in milliseconds.</summary>
        public readonly int connectTimeMs;

        /// <summary>Why the ping failed, if it did.</summary>
        public readonly string error;

        /// <summary>Which link the latency was measured over, as reported by the transport. May be null.</summary>
        public readonly string link;

        /// <summary>Best available latency estimate: the measured round trip time, or the connect time as a fallback.</summary>
        public int latencyMs => roundTripTimeMs >= 0 ? roundTripTimeMs : connectTimeMs;

        /// <summary>Whether the transport measured an actual round trip time rather than falling back to connect time.</summary>
        public bool hasRoundTripTime => roundTripTimeMs >= 0;

        public PingResult(int roundTripTimeMs, int connectTimeMs, string link = null)
        {
            success = true;
            this.roundTripTimeMs = roundTripTimeMs;
            this.connectTimeMs = connectTimeMs;
            this.link = link;
            error = null;
        }

        private PingResult(string error)
        {
            success = false;
            roundTripTimeMs = -1;
            connectTimeMs = -1;
            link = null;
            this.error = error;
        }

        public static PingResult Failed(string error) => new PingResult(error);

        public override string ToString()
        {
            if (!success)
                return $"Ping failed: {error}";
            var text = hasRoundTripTime
                ? $"RTT {roundTripTimeMs}ms (connect {connectTimeMs}ms)"
                : $"Connect {connectTimeMs}ms (no RTT)";
            return string.IsNullOrEmpty(link) ? text : $"{text} via {link}";
        }
    }

    /// <summary>
    /// A transport paired with the result of pinging it.
    /// </summary>
    public readonly struct TransportPingResult
    {
        public readonly GenericTransport transport;
        public readonly PingResult result;

        public TransportPingResult(GenericTransport transport, PingResult result)
        {
            this.transport = transport;
            this.result = result;
        }
    }

    /// <summary>
    /// Helpers for pinging several transports at once, for example to pick the closest server before connecting.
    /// </summary>
    public static class TransportPing
    {
        /// <summary>
        /// Pings every transport in parallel using the address each one is configured with.
        /// Results are sorted fastest first with failures at the end.
        /// </summary>
        public static async Task<List<TransportPingResult>> PingAll(IReadOnlyList<GenericTransport> transports,
            float timeoutSeconds = GenericTransport.DEFAULT_PING_TIMEOUT, CancellationToken token = default)
        {
            var tasks = new List<Task<PingResult>>(transports.Count);

            for (int i = 0; i < transports.Count; i++)
            {
                var transport = transports[i];
                tasks.Add(transport
                    ? transport.Ping(timeoutSeconds, token)
                    : Task.FromResult(PingResult.Failed("Transport is null.")));
            }

            await Task.WhenAll(tasks);

            var results = new List<TransportPingResult>(transports.Count);
            for (int i = 0; i < transports.Count; i++)
                results.Add(new TransportPingResult(transports[i], tasks[i].Result));

            results.Sort(Compare);
            return results;
        }

        /// <summary>
        /// Pings every transport and returns the one with the lowest latency, or null if none could connect.
        /// </summary>
        public static async Task<GenericTransport> PingFastest(IReadOnlyList<GenericTransport> transports,
            float timeoutSeconds = GenericTransport.DEFAULT_PING_TIMEOUT, CancellationToken token = default)
        {
            var results = await PingAll(transports, timeoutSeconds, token);

            if (results.Count == 0 || !results[0].result.success)
                return null;

            return results[0].transport;
        }

        private static int Compare(TransportPingResult a, TransportPingResult b)
        {
            if (a.result.success != b.result.success)
                return a.result.success ? -1 : 1;
            return a.result.latencyMs.CompareTo(b.result.latencyMs);
        }
    }
}
