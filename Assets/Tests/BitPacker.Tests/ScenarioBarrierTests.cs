using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class ScenarioBarrierTests
{
    const int BarrierId = 987650;
    const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;

    static readonly MethodInfo ResetRunStateMethod =
        typeof(ScenarioBarrier).GetMethod("ResetRunState", StaticPrivate);
    static readonly MethodInfo ReportArrivedMethod =
        typeof(ScenarioBarrier).GetMethod("ReportArrived", StaticPrivate | BindingFlags.Public);
    static readonly FieldInfo ArrivedByBarrierField =
        typeof(ScenarioBarrier).GetField("_arrivedByBarrier", StaticPrivate);
    static readonly FieldInfo ProceededField =
        typeof(ScenarioBarrier).GetField("_proceeded", StaticPrivate);

    readonly List<Object> _created = new();
    readonly List<Task> _pending = new();
    NetworkManager _manager;
    CancellationTokenSource _runCts;
    ScenarioContext _ctx;

    static Dictionary<int, int> Arrivals => (Dictionary<int, int>)ArrivedByBarrierField.GetValue(null);
    static HashSet<int> Proceeds => (HashSet<int>)ProceededField.GetValue(null);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        ResetRunState();
        _runCts = new CancellationTokenSource();

        var go = new GameObject("ScenarioBarrierTestHost");
        go.SetActive(false);
        _created.Add(go);
        var transport = go.AddComponent<LocalTransport>();
        _manager = go.AddComponent<NetworkManager>();
        _manager.startServerFlags = StartFlags.None;
        _manager.startClientFlags = StartFlags.None;

        var rules = ScriptableObject.CreateInstance<NetworkRules>();
        _created.Add(rules);
        _manager.SetNetworkRules(rules);
        var provider = ScriptableObject.CreateInstance<NetworkPrefabs>();
        provider.autoGenerate = false;
        _created.Add(provider);
        _manager.SetPrefabProvider(provider);
        _manager.transport = transport;
        go.SetActive(true);
        _manager.StartHost();

        var deadline = Time.realtimeSinceStartup + 10f;
        while (!_manager.isLocalPlayerReady && Time.realtimeSinceStartup < deadline)
            yield return null;

        Assert.That(_manager.isServer && _manager.isClient && _manager.isLocalPlayerReady,
            Is.True, "test host did not start");
        _ctx = new ScenarioContext
        {
            role = NetworkRole.Host,
            expectedConnections = 2,
            networkManager = _manager,
            cancellationToken = _runCts.Token
        };
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _runCts?.Cancel();
        var deadline = Time.realtimeSinceStartup + 5f;
        while (AnyPending(_pending) && Time.realtimeSinceStartup < deadline)
            yield return null;

        foreach (var task in _pending)
            _ = task.Exception;
        _pending.Clear();

        if (_manager)
        {
            _manager.StopClient();
            _manager.StopServer();
            for (int i = 0; i < 10; i++)
                yield return null;
        }

        for (int i = _created.Count - 1; i >= 0; i--)
        {
            if (_created[i])
                Object.DestroyImmediate(_created[i]);
        }
        _created.Clear();
        _manager = null;
        _runCts?.Dispose();
        _runCts = null;
        ResetRunState();
    }

    [UnityTest]
    public IEnumerator OverlappingHostCallers_ReportOneLocalArrival()
    {
        var first = Wait();
        var second = Wait();

        Assert.That(first.IsCompleted || second.IsCompleted, Is.False);
        Assert.That(ArrivalCount(BarrierId), Is.EqualTo(1));
        ReportRemoteArrival(BarrierId);
        yield return WaitForTasks(first, second);

        first.GetAwaiter().GetResult();
        second.GetAwaiter().GetResult();
        Assert.That(ArrivalCount(BarrierId), Is.Zero);
    }

    [UnityTest]
    public IEnumerator HostCallerAfterCompletion_DoesNotReportOrWaitAgain()
    {
        var first = Wait();
        ReportRemoteArrival(BarrierId);
        yield return WaitForTasks(first);
        first.GetAwaiter().GetResult();

        var late = Wait();

        Assert.That(late.IsCompleted, Is.True, "late host half restarted a completed barrier");
        late.GetAwaiter().GetResult();
        Assert.That(ArrivalCount(BarrierId), Is.Zero, "late host half reported a second arrival");
    }

    [UnityTest]
    public IEnumerator HostCallerAfterTimeout_ReceivesSameFailureWithoutAnotherArrival()
    {
        LogAssert.Expect(LogType.Error,
            $"[ScenarioBarrier] server timeout barrier={BarrierId} arrived=1/2 role=Host");
        var first = Wait(timeoutSeconds: 0f);
        yield return WaitForTasks(first);
        var firstFailure = Assert.Throws<TimeoutException>(() => first.GetAwaiter().GetResult());
        Assert.That(Proceeds.Contains(BarrierId), Is.True, "timeout must still release remote peers");

        var late = Wait();

        Assert.That(late.IsCompleted, Is.True, "late host half restarted a failed barrier");
        var lateFailure = Assert.Throws<TimeoutException>(() => late.GetAwaiter().GetResult());
        Assert.That(lateFailure, Is.SameAs(firstFailure));
        Assert.That(ArrivalCount(BarrierId), Is.Zero);
    }

    [UnityTest]
    public IEnumerator ResetRunState_ClearsCompletedWaitsProceedsAndEarlyArrivals()
    {
        var first = Wait();
        ReportRemoteArrival(BarrierId);
        yield return WaitForTasks(first);
        first.GetAwaiter().GetResult();
        ReportRemoteArrival(BarrierId + 1);
        Assert.That(Proceeds.Contains(BarrierId), Is.True);
        Assert.That(ArrivalCount(BarrierId + 1), Is.EqualTo(1));

        ResetRunState();

        Assert.That(Arrivals, Is.Empty);
        Assert.That(Proceeds, Is.Empty);
        var nextRun = Wait();
        Assert.That(nextRun.IsCompleted, Is.False, "new run reused a previous run's completion");
        Assert.That(ArrivalCount(BarrierId), Is.EqualTo(1));
        ReportRemoteArrival(BarrierId);
        yield return WaitForTasks(nextRun);
        nextRun.GetAwaiter().GetResult();
    }

    Task Wait(float timeoutSeconds = 5f)
    {
        var task = ScenarioBarrier.Wait(_ctx, BarrierId, timeoutSeconds).AsTask();
        _pending.Add(task);
        return task;
    }

    static void ResetRunState() => ResetRunStateMethod.Invoke(null, null);

    // Exercise the generated RPC wrapper's host shortcut to simulate the remote arrival.
    static void ReportRemoteArrival(int barrierId) => ReportArrivedMethod.Invoke(null, new object[] { barrierId });

    static int ArrivalCount(int barrierId) => Arrivals.TryGetValue(barrierId, out var count) ? count : 0;

    static bool AnyPending(IReadOnlyList<Task> tasks)
    {
        for (int i = 0; i < tasks.Count; i++)
        {
            if (!tasks[i].IsCompleted)
                return true;
        }
        return false;
    }

    static IEnumerator WaitForTasks(params Task[] tasks)
    {
        var deadline = Time.realtimeSinceStartup + 10f;
        while (AnyPending(tasks) && Time.realtimeSinceStartup < deadline)
            yield return null;
        Assert.That(AnyPending(tasks), Is.False, "barrier task did not finish within the test deadline");
    }
}
