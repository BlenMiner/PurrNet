using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public static class ScenarioBarrier
{
    private static readonly Dictionary<int, int> _arrivedByBarrier = new();
    // Shared handle for both concurrent and late in-process callers (host's RunSplit halves).
    // System.Threading.Task is multi-awaitable; UniTask.Preserve()/MemoizeSource is not
    // before completion — it forwards OnCompleted to the underlying state-machine source,
    // which only allows one continuation.
    private static readonly Dictionary<int, Task> _waitsByBarrier = new();
    // Exact-match per-id proceeds (NOT a high-water mark): barrier ids are scenario-local constants
    // with no global ordering, so "any id <= last proceeded" would let clients skip barriers in any
    // scenario that runs after one with higher ids. The set also covers proceeds that arrive before
    // a client starts waiting. Ids must still be globally unique across scenarios.
    private static readonly HashSet<int> _proceeded = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRunState()
    {
        _arrivedByBarrier.Clear();
        _waitsByBarrier.Clear();
        _proceeded.Clear();
    }

    public static async UniTask Wait(ScenarioContext ctx, int barrierId, float timeoutSeconds)
    {
        if (_waitsByBarrier.TryGetValue(barrierId, out var existing))
        {
            await existing;
            return;
        }

        var task = WaitImpl(ctx, barrierId, timeoutSeconds).AsTask();
        // Each id is used once per run. Keep the terminal result, including failures:
        // a host half arriving after the other completed must not report and wait again.
        _waitsByBarrier[barrierId] = task;
        await task;
    }

    private static async UniTask WaitImpl(ScenarioContext ctx, int barrierId, float timeoutSeconds)
    {
        if (ctx.isClient)
            ReportArrived(barrierId);

        if (ctx.isServer)
        {
            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => _arrivedByBarrier.TryGetValue(barrierId, out var c) && c >= ctx.expectedConnections,
                    timeoutSeconds,
                    ctx.cancellationToken);
            }
            catch (System.TimeoutException)
            {
                _arrivedByBarrier.TryGetValue(barrierId, out var arrived);
                Debug.LogError(
                    $"[ScenarioBarrier] server timeout barrier={barrierId} arrived={arrived}/{ctx.expectedConnections} role={ctx.role}");
                throw;
            }
            finally
            {
                // Always release clients, even on timeout, so a single missing
                // process doesn't strand the rest of the run.
                _arrivedByBarrier.Remove(barrierId);
                BroadcastProceed(barrierId);
            }
        }

        if (ctx.isClient)
        {
            await UniTaskUtils.WaitWithTimeout(
                () => _proceeded.Contains(barrierId),
                timeoutSeconds,
                ctx.cancellationToken);
        }
    }

    [ServerRpc(requireOwnership: false)]
    private static void ReportArrived(int barrierId)
    {
        _arrivedByBarrier.TryGetValue(barrierId, out var count);
        _arrivedByBarrier[barrierId] = count + 1;
    }

    [ObserversRpc(runLocally: true)]
    private static void BroadcastProceed(int barrierId)
    {
        _proceeded.Add(barrierId);
    }
}
