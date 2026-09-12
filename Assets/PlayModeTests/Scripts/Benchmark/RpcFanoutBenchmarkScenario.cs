using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

// RPC packing fan-out load: the server sends, per object per tick, one delta-packed unreliable
// observers RPC (per-player DeltaModule encode) plus a plain reliable observers RPC every
// _reliableEveryNthTick ticks (shared-entry RPCBatch fan-out). Objects never move, so the
// CPU-by-marker breakdown isolates RPC/delta packing cost from NetworkTransform replication.
// Total queue ops per tick scale as objects x connected players — the O(players) cost this
// benchmark exists to track.
public class RpcFanoutBenchmarkScenario : BenchmarkScenarioBase
{
    [SerializeField] private int _objectCount = 50;
    [SerializeField] private int _reliableEveryNthTick = 8;

    private readonly List<RpcFanoutEmitter> _emitters = new();
    private int _tick;

    public override void ApplyOverrides(int? objectCount, float? pingsPerSecond)
    {
        base.ApplyOverrides(objectCount, pingsPerSecond);
        if (objectCount is > 0)
            _objectCount = objectCount.Value;
    }

    protected override void OnSetup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab(manager, nameof(RpcFanoutBenchmarkScenario) + "_Obj", typeof(RpcFanoutEmitter));
    }

    protected override UniTask Spawn(ScenarioContext ctx)
    {
        RpcFanoutEmitter.ResetCounters();
        _tick = 0;

        if (ctx.isServer)
        {
            SpawnSuppressed(() =>
            {
                for (int i = 0; i < _objectCount; i++)
                {
                    var inst = Instantiate(_prefab);
                    inst.gameObject.SetActive(true);
                    inst.transform.position = new Vector3(i, 0, 0);
                    _spawned.Add(inst);
                    _emitters.Add(inst.GetComponent<RpcFanoutEmitter>());
                }
            });
        }

        return UniTask.CompletedTask;
    }

    protected override void Tick(ScenarioContext ctx, float elapsed, float dt)
    {
        if (!ctx.isServer)
            return;

        _tick++;
        bool sendReliable = _reliableEveryNthTick > 0 && _tick % _reliableEveryNthTick == 0;

        for (int i = 0; i < _emitters.Count; i++)
        {
            var emitter = _emitters[i];
            if (!emitter)
                continue;

            // Smooth, small per-tick movement so the delta-packed path sees realistic
            // "mostly similar to last acked value" inputs rather than random noise.
            float phase = i * 0.3f;
            var position = new Vector3(
                Mathf.Sin(elapsed + phase) * 5f,
                Mathf.Cos(elapsed * 0.5f + phase) * 5f,
                i);
            int health = 100 - (_tick >> 6) % 50;

            emitter.SendState(position, health, (uint)_tick);
            if (sendReliable)
                emitter.SendEvent(position, i);
        }
    }

    protected override void Despawn(ScenarioContext ctx)
    {
        base.Despawn(ctx);
        _emitters.Clear();
    }

    protected override int ObjectCount(ScenarioContext ctx) => ctx.isServer ? _spawned.Count : _objectCount;
}
