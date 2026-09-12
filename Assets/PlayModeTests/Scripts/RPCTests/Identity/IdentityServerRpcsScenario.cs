using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class IdentityServerRpcsScenario : Scenario
{
    [SerializeField] private float _doneTimeoutSeconds = 30f;
    [SerializeField] private float _spawnTimeoutSeconds = 15f;

    private IdentityServerRpcs _prefab;

    void CreatePrefab()
    {
        var go = new GameObject(nameof(IdentityServerRpcsScenario));
        _prefab = go.AddComponent<IdentityServerRpcs>();
        go.SetActive(false);
    }

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        CreatePrefab();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, _prefab.gameObject);
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        if (ctx.role == NetworkRole.Server)
        {
            Instantiate(_prefab);
            return await RunAsServerOnly(ctx);
        }

        if (ctx.isServer)
            Instantiate(_prefab);

        await UniTaskUtils.WaitWithTimeout(
            () => IdentityServerRpcs.LocalInstance != null,
            _spawnTimeoutSeconds,
            ctx.cancellationToken);

        var clientResult = await RunClientChecks(ctx);
        IdentityServerRpcs.LocalInstance.SignalDone();
        return clientResult;
    }

    private async UniTask<ScenarioResult> RunAsServerOnly(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IdentityServerRpcs.DoneCount >= ctx.expectedConnections,
                _doneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail(
                $"Server timed out waiting for client done signals; got {IdentityServerRpcs.DoneCount}/{ctx.expectedConnections}");
        }

        if (IdentityServerRpcs.FireAndForgetReceivedCount < ctx.expectedConnections)
        {
            return ScenarioResult.Fail(
                $"Fire-and-forget RPC received only {IdentityServerRpcs.FireAndForgetReceivedCount}/{ctx.expectedConnections} times");
        }

        return ScenarioResult.Ok($"Done={IdentityServerRpcs.DoneCount}, FireAndForget={IdentityServerRpcs.FireAndForgetReceivedCount}");
    }

    private static async UniTask<ScenarioResult> RunClientChecks(ScenarioContext ctx)
    {
        var failures = new List<string>();
        var inst = IdentityServerRpcs.LocalInstance;

        await Try(failures, "Echo_Int", async () =>
        {
            var r = await inst.Echo_Int(42);
            if (r != 42) throw new Exception($"expected 42, got {r}");
        });

        await Try(failures, "Echo_String", async () =>
        {
            var r = await inst.Echo_String("hello world");
            if (r != "hello world") throw new Exception($"expected 'hello world', got '{r}'");
        });

        await Try(failures, "Echo_Bool", async () =>
        {
            var r = await inst.Echo_Bool(true);
            if (!r) throw new Exception("expected true");
        });

        await Try(failures, "Echo_FloatUni", async () =>
        {
            var r = await inst.Echo_FloatUni(3.14f);
            if (Mathf.Abs(r - 3.14f) > 0.0001f) throw new Exception($"expected ~3.14, got {r}");
        });

        await Try(failures, "Echo_VoidUni", async () =>
        {
            await inst.Echo_VoidUni(123);
        });

        await Try(failures, "Echo_GenericInt", async () =>
        {
            var r = await inst.Echo_Generic<int>(7);
            if (r != 7) throw new Exception($"expected 7, got {r}");
        });

        await Try(failures, "Echo_GenericString", async () =>
        {
            var r = await inst.Echo_Generic<string>("generic");
            if (r != "generic") throw new Exception($"expected 'generic', got '{r}'");
        });

        await Try(failures, "Echo_Struct", async () =>
        {
            var input = new IdentityServerRpcs.TestPayload { id = 9, label = "payload", weight = 1.5f };
            var r = await inst.Echo_Struct(input);
            if (r.id != input.id || r.label != input.label || Mathf.Abs(r.weight - input.weight) > 0.0001f)
                throw new Exception($"struct round-trip mismatch: got id={r.id}, label='{r.label}', weight={r.weight}");
        });

        await Try(failures, "Echo_SimplePacked", async () =>
        {
            var r = await inst.Echo_SimplePacked(12345);
            if (!r.success || r.integerVar != 12345 || Mathf.Abs(r.floatVar - 42.5f) > 0.0001f)
                throw new Exception($"IPackedSimple round-trip mismatch: got success={r.success}, integerVar={r.integerVar}, floatVar={r.floatVar}");
        });

        await Try(failures, "Echo_SimplePackedUni", async () =>
        {
            var r = await inst.Echo_SimplePackedUni(54321);
            if (!r.success || r.integerVar != 54321 || Mathf.Abs(r.floatVar - 42.5f) > 0.0001f)
                throw new Exception($"IPackedSimple round-trip mismatch: got success={r.success}, integerVar={r.integerVar}, floatVar={r.floatVar}");
        });

        await Try(failures, "Echo_MultiArg", async () =>
        {
            var sum = await inst.Echo_Sum(2, 3, 5);
            if (sum != 10) throw new Exception($"expected 10, got {sum}");
        });

        await Try(failures, "Echo_Unreliable", async () =>
        {
            var r = await inst.Echo_Unreliable(99);
            if (r != 99) throw new Exception($"expected 99, got {r}");
        });

        await Try(failures, "Echo_Compression_None", async () =>
        {
            var r = await inst.Echo_CompNone(3001);
            if (r != 3001) throw new Exception($"expected 3001, got {r}");
        });

        await Try(failures, "Echo_Compression_Fast", async () =>
        {
            var r = await inst.Echo_CompFast(3002);
            if (r != 3002) throw new Exception($"expected 3002, got {r}");
        });

        await Try(failures, "Echo_Compression_Balanced", async () =>
        {
            var r = await inst.Echo_CompBalanced(3003);
            if (r != 3003) throw new Exception($"expected 3003, got {r}");
        });

        await Try(failures, "Echo_Compression_Best", async () =>
        {
            var r = await inst.Echo_CompBest(3004);
            if (r != 3004) throw new Exception($"expected 3004, got {r}");
        });

        await Try(failures, "Echo_WithInfo", async () =>
        {
            var seenSenderId = await inst.Echo_WithInfo(0);

            if (ctx.role == NetworkRole.Client)
            {
                var localId = ctx.networkManager.localPlayer.id.value;
                if (seenSenderId == 0UL)
                    throw new Exception($"server observed sender id 0 (default/Server) for a client RPC; expected client id {localId}");
                if (localId != 0UL && seenSenderId != localId)
                    throw new Exception($"server observed sender id {seenSenderId}; expected client localPlayer id {localId}");
            }
        });

        await Try(failures, "DeltaPacked_Off_Sequence", async () =>
        {
            int[] seq = { 5, 5, 6, 6, 11 };
            for (var i = 0; i < seq.Length; i++)
            {
                var r = await inst.Echo_DeltaOff(seq[i]);
                if (r != seq[i]) throw new Exception($"echo[{i}] expected {seq[i]}, got {r}");
            }
        });

        await Try(failures, "DeltaPacked_On_Sequence", async () =>
        {
            int[] seq = { 300, 300, 301, 301, 400 };
            for (var i = 0; i < seq.Length; i++)
            {
                var r = await inst.Echo_DeltaOn(seq[i]);
                if (r != seq[i]) throw new Exception($"echo[{i}] expected {seq[i]}, got {r}");
            }
        });

        await Try(failures, "Echo_AsyncPackable", async () =>
        {
            var observed = await inst.Echo_AsyncPackable(new IdentityServerRpcs.AsyncPayload { seed = 42 });
            if (observed == null || observed.Length != 3)
                throw new Exception("server did not return all three stamps");
            if (observed[0] != 42) throw new Exception($"seed: expected 42, got {observed[0]}");
            if (observed[1] != 43) throw new Exception($"PrepareForPackAsync did not run on sender: expected packStamp=43, got {observed[1]}");
            if (observed[2] != 53) throw new Exception($"PrepareAfterUnpackAsync did not run on receiver: expected unpackStamp=53, got {observed[2]}");
        });

        await Try(failures, "Echo_IEnumerator", async () =>
        {
            var before = await inst.Echo_IEnumeratorRunCount();
            await inst.Echo_IEnumerator(555);
            var after = await inst.Echo_IEnumeratorRunCount();
            if (after <= before)
                throw new Exception($"server IEnumerator counter did not advance: {before} -> {after}");
        });

        await Try(failures, "FireAndForget", async () =>
        {
            inst.FireAndForget(123);
            await UniTask.CompletedTask;
        });

        if (failures.Count > 0)
            return ScenarioResult.Fail(string.Join(" | ", failures));

        return ScenarioResult.Ok();
    }

    private static async UniTask Try(List<string> failures, string label, Func<UniTask> action)
    {
        try
        {
            await action();
        }
        catch (Exception e)
        {
            failures.Add($"{label}: {e.Message}");
            Debug.LogError($"[IdentityServerRpcsScenario] {label} failed: {e}");
        }
    }
}
