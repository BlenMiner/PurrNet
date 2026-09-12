using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// Spawn/despawn churn under load: the server keeps N NetworkTransform objects alive while
// despawning the oldest and spawning a new one every frame for a while. Every peer must end up
// with exactly N objects, each sitting at the position the server spawned it at (carried by a
// SyncVar set before spawn), and everything must despawn cleanly. This pins the spawn packet,
// the initial NetworkTransform state, per-player observer bookkeeping and despawn delivery.
public class SpawnChurnScenario : Scenario
{
    [SerializeField] private int _aliveCount = 30;
    [SerializeField] private int _churnFrames = 180;
    [SerializeField] private float _spawnTimeoutSeconds = 20f;
    [SerializeField] private float _convergeTimeoutSeconds = 30f;
    [SerializeField] private float _despawnTimeoutSeconds = 30f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int BarrierSpawn = 9700;
    private const int BarrierChurned = 9701;
    private const int BarrierConverged = 9702;
    private const int BarrierEnd = 9703;

    private const float PositionEpsilon = 0.05f;

    private SpawnChurnProbe _prefab;
    private readonly List<SpawnChurnProbe> _serverAlive = new();
    private int _spawnedTotal;

    private void CreatePrefab()
    {
        var go = new GameObject(nameof(SpawnChurnProbe));
        go.SetActive(false);
        _prefab = go.AddComponent<SpawnChurnProbe>();
        go.AddComponent<NetworkTransform>();

        SpawnChurnProbe.ResetAll();
        _serverAlive.Clear();
        _spawnedTotal = 0;
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
            for (int i = 0; i < _aliveCount; i++)
                SpawnOne();
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => SpawnChurnProbe.aliveCount >= _aliveCount,
                _spawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"initial spawn incomplete: {SpawnChurnProbe.aliveCount}/{_aliveCount}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierSpawn, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            for (int f = 0; f < _churnFrames; f++)
            {
                var oldest = _serverAlive[0];
                _serverAlive.RemoveAt(0);
                if (oldest)
                    Destroy(oldest.gameObject);
                SpawnOne();
                await UniTask.Yield(ctx.cancellationToken);
            }
        }

        await ScenarioBarrier.Wait(ctx, BarrierChurned, _barrierTimeoutSeconds);

        try
        {
            await UniTaskUtils.WaitWithTimeout(IsConverged, _convergeTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"churn never converged: alive={SpawnChurnProbe.aliveCount}/{_aliveCount}, " +
                $"worst offset={WorstOffset():F3}, spawnedTotal={_spawnedTotal}, mismatches: {DescribeMismatches()}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierConverged, _barrierTimeoutSeconds);

        if (ctx.isServer)
        {
            for (int i = 0; i < _serverAlive.Count; i++)
            {
                if (_serverAlive[i])
                    Destroy(_serverAlive[i].gameObject);
            }

            _serverAlive.Clear();
        }

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => SpawnChurnProbe.aliveCount == 0,
                _despawnTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"despawn incomplete: alive={SpawnChurnProbe.aliveCount}");
        }

        await ScenarioBarrier.Wait(ctx, BarrierEnd, _barrierTimeoutSeconds);

        return ScenarioResult.Ok($"{_spawnedTotal} spawns churned, {_aliveCount} alive converged, all despawned");
    }

    private void SpawnOne()
    {
        var pos = new Vector3(UnityEngine.Random.Range(-20f, 20f), UnityEngine.Random.Range(0f, 5f),
            UnityEngine.Random.Range(-20f, 20f));
        var probe = Instantiate(_prefab, pos, Quaternion.identity);
        probe.expected = pos;
        _serverAlive.Add(probe);
        _spawnedTotal++;
    }

    private bool IsConverged()
    {
        if (SpawnChurnProbe.aliveCount != _aliveCount)
            return false;

        foreach (var probe in SpawnChurnProbe.alive)
        {
            if (!probe || (probe.transform.position - probe.expected).magnitude > PositionEpsilon)
                return false;
        }

        return true;
    }

    private static string DescribeMismatches()
    {
        var sb = new System.Text.StringBuilder();
        int shown = 0;
        foreach (var probe in SpawnChurnProbe.alive)
        {
            if (!probe)
            {
                sb.Append("[destroyed] ");
                continue;
            }

            var offset = (probe.transform.position - probe.expected).magnitude;
            if (offset <= PositionEpsilon)
                continue;

            if (shown++ < 4)
                sb.Append($"[{probe.id} pos={probe.transform.position:F2} expected={probe.expected:F2} active={probe.gameObject.activeInHierarchy}] ");
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    private static float WorstOffset()
    {
        float worst = 0f;
        foreach (var probe in SpawnChurnProbe.alive)
        {
            if (!probe)
                continue;
            worst = Mathf.Max(worst, (probe.transform.position - probe.expected).magnitude);
        }

        return worst;
    }
}
