using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

// Reproduces the observer bounce reported for ownerAuth NetworkTransform ownership handoffs
// while the object is moving: when authority moves server->client, client->client, and back
// to the server mid-motion, every non-controlling peer must see one smooth transition — no
// backward jump, no overshoot-and-return — and keep tracking the mover afterwards.
public class NTHandoffSmoothnessScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;
    [SerializeField] private float _warmupSeconds = 1.2f;
    [SerializeField] private float _phaseSeconds = 1.6f;

    protected virtual string prefabName => nameof(NTHandoffMover);
    protected virtual int barrierBase => 8800;

    private int BarrierSpawn => barrierBase;
    private int BarrierMotionEnd => barrierBase + 1;
    private int BarrierEnd => barrierBase + 2;

    // The handoff rewind under loopback is well above these limits; render wobble stays far below.
    private const float BackwardEpsilon = 0.05f;
    private const float PathExcessLimit = 0.2f;
    private const float MinProgress = 1.0f;
    private const float WindowSeconds = 1.25f;

    private NTHandoffMover _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(prefabName);
        go.SetActive(false);
        _prefab = go.AddComponent<NTHandoffMover>();
        go.AddComponent<NetworkTransform>();
        NTHandoffMover.ResetAll();
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.isServer)
        {
            // Ownerless spawn so the server drives the first motion phase.
            HierarchyV2.SupressAutoOwner();
            try { Instantiate(_prefab, new Vector3(0f, 1f, 0f), Quaternion.identity); }
            finally { HierarchyV2.ResumeAutoOwner(); }
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => NTHandoffMover.localInstance && NTHandoffMover.localInstance.isSpawned,
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("mover never spawned");
        }

        await ScenarioBarrier.Wait(ctx, BarrierSpawn, _barrierTimeoutSeconds);

        var mover = NTHandoffMover.localInstance;
        mover.Begin();

        if (ctx.isServer)
        {
            var clients = new List<PlayerID>();
            var players = ctx.networkManager.players;
            for (int i = 0; i < players.Count; i++)
            {
                if (!players[i].isServer && players[i] != ctx.networkManager.localPlayer)
                    clients.Add(players[i]);
            }
            clients.Sort((a, b) => a.id.value.CompareTo(b.id.value));

            if (clients.Count < 2)
                return ScenarioResult.Fail($"need at least 2 external clients, got {clients.Count}");

            var nt = mover.GetComponent<NetworkTransform>();

            await UniTask.WaitForSeconds(_warmupSeconds, cancellationToken: ctx.cancellationToken);
            nt.GiveOwnership(clients[0]);
            await UniTask.WaitForSeconds(_phaseSeconds, cancellationToken: ctx.cancellationToken);
            nt.GiveOwnership(clients[1]);
            await UniTask.WaitForSeconds(_phaseSeconds, cancellationToken: ctx.cancellationToken);
            nt.RemoveOwnership();
            await UniTask.WaitForSeconds(_phaseSeconds, cancellationToken: ctx.cancellationToken);
        }

        await ScenarioBarrier.Wait(ctx, BarrierMotionEnd, _barrierTimeoutSeconds);

        mover.End();

        var failures = new List<string>();
        EvaluateSmoothness(mover.samples, failures);

        if (ctx.isServer)
            Destroy(mover.gameObject);

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !NTHandoffMover.localInstance,
                _despawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            failures.Add("mover never despawned");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return failures.Count == 0
            ? ScenarioResult.Ok("handoffs stayed smooth on all remote views")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void EvaluateSmoothness(List<NTHandoffMover.Sample> samples, List<string> failures)
    {
        int transitions = 0;

        for (int i = 1; i < samples.Count; i++)
        {
            var prev = samples[i - 1];
            var cur = samples[i];

            if (prev.hasOwner == cur.hasOwner && (!cur.hasOwner || prev.ownerId == cur.ownerId))
                continue;

            transitions++;

            // The new controller snaps to its own pose by design.
            if (cur.controller)
                continue;

            EvaluateWindow(samples, i, transitions, failures);
        }

        if (transitions == 0)
            failures.Add("no ownership transitions observed");
    }

    private static void EvaluateWindow(List<NTHandoffMover.Sample> samples, int start, int transition,
        List<string> failures)
    {
        float t0 = samples[start].time;
        float minDx = 0f;
        int minDxIndex = -1;
        float path = 0f;
        float first = 0f;
        float last = 0f;
        bool started = false;
        int pairs = 0;

        for (int i = start + 1; i < samples.Count; i++)
        {
            var prev = samples[i - 1];
            var cur = samples[i];

            if (cur.time - t0 > WindowSeconds)
                break;

            if (prev.controller || cur.controller)
                continue;

            if (!started)
            {
                first = prev.x;
                started = true;
            }

            float dx = cur.x - prev.x;
            if (dx < minDx)
            {
                minDx = dx;
                minDxIndex = i;
            }
            path += Mathf.Abs(dx);
            last = cur.x;
            pairs++;
        }

        if (pairs < 10)
        {
            failures.Add($"transition {transition}: only {pairs} observer samples in the window");
            return;
        }

        float net = last - first;
        float excess = path - Mathf.Max(net, 0f);

        if (minDx < -BackwardEpsilon)
            failures.Add(
                $"transition {transition}: backward jump of {-minDx:F3}m (limit {BackwardEpsilon:F3}m) " +
                Trace(samples, minDxIndex, t0));

        if (excess > PathExcessLimit)
            failures.Add(
                $"transition {transition}: path excess {excess:F3}m (path={path:F3}, net={net:F3}, limit {PathExcessLimit:F3}m)");

        if (net < MinProgress)
            failures.Add(
                $"transition {transition}: only {net:F3}m of forward progress in {WindowSeconds:F2}s (limit {MinProgress:F3}m)");
    }

    private static string Trace(List<NTHandoffMover.Sample> samples, int center, float t0)
    {
        if (center < 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder("[trace ");
        int from = Mathf.Max(0, center - 8);
        int to = Mathf.Min(samples.Count - 1, center + 8);
        for (int i = from; i <= to; i++)
            sb.Append($"{(i == center ? "*" : "")}{samples[i].time - t0:F3}:{samples[i].x:F3} ");
        sb.Append(']');
        return sb.ToString();
    }
}
