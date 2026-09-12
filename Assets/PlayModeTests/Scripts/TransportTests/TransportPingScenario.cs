using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet.Transports;
using UnityEngine;

public class TransportPingScenario : Scenario
{
    [SerializeField] private float _liveRttTimeoutSeconds = 10f;
    [SerializeField] private float _pingTimeoutSeconds = 5f;
    [SerializeField] private float _deadPingTimeoutSeconds = 3f;
    [SerializeField] private float _serverWatchSeconds = 8f;
    [SerializeField] private float _cleanupTimeoutSeconds = 15f;
    [SerializeField] private int _maxLocalLatencyMs = 1000;

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.networkManager.transport is not UDPTransport)
            return ScenarioResult.Ok("skipped: transport is not UDPTransport");

        return await RunSplit(ctx, RunAsClient, RunAsServer);
    }

    private static UDPTransport CreateProbe(string name, string address, ushort port)
    {
        var probe = new GameObject(name).AddComponent<UDPTransport>();
        probe.address = address;
        probe.serverPort = port;
        return probe;
    }

    private async UniTask<ScenarioResult> RunAsClient(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var udp = (UDPTransport)manager.transport;
        var live = udp.transport;
        var failures = new List<string>();

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => live.GetRoundTripTime(default, false) >= 0,
                _liveRttTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("live client connection never reported a round trip time");
        }

        var reachable = CreateProbe("PingProbe", udp.address, udp.serverPort);
        var unreachable = CreateProbe("DeadPingProbe", udp.address, (ushort)(udp.serverPort + 1000));

        bool wasClient = manager.isClient;
        int rtt = -1;

        try
        {
            var result = await UniTaskUtils.WithTimeout(
                reachable.Ping(_pingTimeoutSeconds, ctx.cancellationToken),
                _pingTimeoutSeconds + 5f,
                ctx.cancellationToken,
                "ping");

            if (!result.success)
                failures.Add($"ping failed: {result.error}");
            else
            {
                rtt = result.roundTripTimeMs;

                if (!result.hasRoundTripTime)
                    failures.Add($"ping did not measure a round trip time ({result})");
                else if (result.latencyMs > _maxLocalLatencyMs)
                    failures.Add($"ping latency {result.latencyMs}ms exceeds {_maxLocalLatencyMs}ms on loopback");
            }

            if (reachable.isPinging)
                failures.Add("isPinging still set after the ping completed");

            if (reachable.transport.clientState != ConnectionState.Disconnected)
                failures.Add($"probe transport still {reachable.transport.clientState} after the ping");

            if (manager.isClient != wasClient || manager.clientState != ConnectionState.Connected)
                failures.Add($"NetworkManager client state changed during the probe (isClient={manager.isClient}, state={manager.clientState})");

            var ranked = await UniTaskUtils.WithTimeout(
                TransportPing.PingAll(new GenericTransport[] { unreachable, reachable }, _deadPingTimeoutSeconds, ctx.cancellationToken),
                _deadPingTimeoutSeconds + 5f,
                ctx.cancellationToken,
                "PingAll");

            if (ranked.Count != 2)
                failures.Add($"PingAll returned {ranked.Count} results, expected 2");
            else
            {
                if (ranked[0].transport != reachable || !ranked[0].result.success)
                    failures.Add($"PingAll did not rank the reachable transport first ({ranked[0].result})");

                if (ranked[1].result.success)
                    failures.Add("PingAll reported success for a port nobody listens on");
            }
        }
        finally
        {
            Destroy(reachable.gameObject);
            Destroy(unreachable.gameObject);
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"probe rtt={rtt}ms, live rtt={live.GetRoundTripTime(default, false)}ms")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private async UniTask<ScenarioResult> RunAsServer(ScenarioContext ctx)
    {
        var manager = ctx.networkManager;
        var udp = (UDPTransport)manager.transport;
        var failures = new List<string>();

        int playersBefore = manager.playerCount;
        int connectionsBefore = udp.connections.Count;

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => AllConnectionsHaveRtt(udp),
                _liveRttTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("server never measured a round trip time for every live connection");
        }

        int peakConnections = connectionsBefore;
        var watchUntil = Time.realtimeSinceStartupAsDouble + _serverWatchSeconds;

        while (Time.realtimeSinceStartupAsDouble < watchUntil)
        {
            if (manager.playerCount != playersBefore)
            {
                failures.Add($"player count changed while clients were probing: {playersBefore} -> {manager.playerCount}");
                break;
            }

            peakConnections = Math.Max(peakConnections, udp.connections.Count);
            await UniTask.NextFrame(ctx.cancellationToken);
        }

        if (peakConnections <= connectionsBefore)
            failures.Add("no probe connection ever reached the server transport");

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => udp.connections.Count <= connectionsBefore,
                _cleanupTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add($"probe connections were not closed: {udp.connections.Count} > {connectionsBefore}");
        }

        return failures.Count == 0
            ? ScenarioResult.Ok($"players={playersBefore}, peak transport connections={peakConnections}")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static bool AllConnectionsHaveRtt(UDPTransport udp)
    {
        var connections = udp.connections;

        for (int i = 0; i < connections.Count; i++)
        {
            if (udp.GetRoundTripTime(connections[i], true) < 0)
                return false;
        }

        return true;
    }
}
