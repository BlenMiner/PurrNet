using System;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using UnityEngine;
using Object = UnityEngine.Object;

public class NetworkRigidbodyTimingTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const double SendInterval = 0.05;
    private GameObject _gameObject;
    private NetworkRigidbody _networkRigidbody;

    [SetUp]
    public void SetUp()
    {
        _gameObject = new GameObject(nameof(NetworkRigidbodyTimingTests));
        _gameObject.AddComponent<Rigidbody>();
        _networkRigidbody = _gameObject.AddComponent<NetworkRigidbody>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_gameObject);
    }

    [TestCase(5)]
    [TestCase(40)]
    public void QueuedSnapshots_PreserveCaptureSpacingAndRotationSpeed(int snapshotCount)
    {
        // All captures arrive in one Unity frame, including a batch that wraps the ring.
        for (int i = 0; i < snapshotCount; i++)
            PushSnapshot(100d + i * SendInterval, i * 5f);

        int retainedCount = Math.Min(snapshotCount, 32);
        Assert.That(BufferCount, Is.EqualTo(retainedCount));
        AssertCaptureSpacing(retainedCount);

        var angularVelocity = (Vector3)Invoke(
            "GetExtrapolationAngularVelocity", GetSnapshot(retainedCount - 1));
        // PackedQuaternion quantizes each component; allow its small angular error.
        Assert.That(angularVelocity.y, Is.EqualTo(100f * Mathf.Deg2Rad).Within(0.04f),
            "Five degrees per 50 ms must not turn into the rotation speed of a 0.1 ms interval.");
    }

    [Test]
    public void FirstBatchAfterStall_PreservesEveryIncreasingSenderInterval()
    {
        PushSnapshot(100d);
        SetSnapshotTime(0, Time.unscaledTimeAsDouble - 2d);

        PushSnapshot(101d);
        Assert.That(BufferCount, Is.EqualTo(1), "The stale buffer should be discarded.");

        for (int i = 1; i < 5; i++)
            PushSnapshot(101d + i * SendInterval);

        Assert.That(BufferCount, Is.EqualTo(5));
        AssertCaptureSpacing(5);
    }

    [TestCase(-0.04)]
    [TestCase(0.20)]
    [TestCase(0.75)]
    public void ClockOffsetChanges_RebaseRetainedSnapshotsTogether(double arrivalOffsetChange)
    {
        for (int i = 0; i < 3; i++)
            PushSnapshot(100d + i * SendInterval);

        double previousNewestTime = SnapshotTime(2);
        double mappedTime = (double)Invoke(
            "MapToLocalTimeline", 100d + 3 * SendInterval,
            previousNewestTime + SendInterval + arrivalOffsetChange);

        Assert.That(SnapshotTime(2), Is.Not.EqualTo(previousNewestTime),
            "This test must exercise an actual offset adjustment.");
        AssertCaptureSpacing(3);
        Assert.That(mappedTime - SnapshotTime(2), Is.EqualTo(SendInterval).Within(1e-8),
            "Latency changes must not change the capture interval used by interpolation.");
    }

    [Test]
    public void SwitchingBetweenStampedAndLegacyCaptures_StartsSeparateTimelines()
    {
        PushSnapshot(100d);
        PushSnapshot(100d + SendInterval);

        PushSnapshot(0d);
        Assert.That(BufferCount, Is.EqualTo(1));
        Assert.That(SnapshotTime(0), Is.EqualTo(Time.unscaledTimeAsDouble).Within(1e-8),
            "An unstamped legacy capture uses its arrival time.");
        Assert.That(Field<bool>("_hasSenderTimeOffset"), Is.False);

        PushSnapshot(0d);
        Assert.That(BufferCount, Is.EqualTo(2), "Legacy captures can share an arrival timeline.");

        PushSnapshot(101d);
        Assert.That(BufferCount, Is.EqualTo(1),
            "Arrival-timed captures must not be shifted into the sender's clock.");
        Assert.That(Field<bool>("_hasSenderTimeOffset"), Is.True);

        PushSnapshot(101d + SendInterval);
        AssertCaptureSpacing(2);
    }

    [TestCase(100d)]
    [TestCase(99.9d)]
    public void NonIncreasingSenderTime_KeepsTheMonotonicFallback(double senderTime)
    {
        PushSnapshot(100d);
        PushSnapshot(senderTime);

        Assert.That(BufferCount, Is.EqualTo(2));
        Assert.That(SnapshotTime(1), Is.GreaterThan(SnapshotTime(0)),
            "A duplicate or regressed clock must not create an invalid interpolation span.");
    }

    private int BufferCount => Field<int>("_bufferCount");

    private void PushSnapshot(double senderTime, float rotationDegrees = 0f)
    {
        var state = new RigidbodyStateData
        {
            positionFrame = RigidbodyPositionFrame.World,
            rotation = Quaternion.AngleAxis(rotationDegrees, Vector3.up),
            time = senderTime
        };
        // Bypass authority ordering only; exercise the real receive buffer and time mapping.
        Invoke("PushSnapshot", state, true);
    }

    private void AssertCaptureSpacing(int count)
    {
        double firstTime = SnapshotTime(0);
        for (int i = 1; i < count; i++)
        {
            Assert.That(SnapshotTime(i) - firstTime,
                Is.EqualTo(i * SendInterval).Within(1e-8), $"Capture {i} lost its sender spacing.");
        }
    }

    private object GetSnapshot(int index) => Invoke("GetSnapshot", index);

    private double SnapshotTime(int index)
    {
        object snapshot = GetSnapshot(index);
        return (double)snapshot.GetType().GetField("time").GetValue(snapshot);
    }

    private void SetSnapshotTime(int logicalIndex, double time)
    {
        var buffer = Field<Array>("_snapshotBuffer");
        int actualIndex = (Field<int>("_bufferHead") - BufferCount + logicalIndex + buffer.Length) % buffer.Length;
        object snapshot = buffer.GetValue(actualIndex);
        snapshot.GetType().GetField("time").SetValue(snapshot, time);
        buffer.SetValue(snapshot, actualIndex);
    }

    private T Field<T>(string name)
    {
        return (T)typeof(NetworkRigidbody).GetField(name, PrivateInstance).GetValue(_networkRigidbody);
    }

    private object Invoke(string name, params object[] arguments)
    {
        return typeof(NetworkRigidbody).GetMethod(name, PrivateInstance).Invoke(_networkRigidbody, arguments);
    }
}
