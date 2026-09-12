#if UNITY_PHYSICS_3D
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Packing;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class NetworkRigidbodyForceOriginTests
{
    private const double LargeOrigin = 1_000_000_000_000d;
    private readonly List<GameObject> _objects = new();
    private readonly List<Scene> _scenes = new();

    [OneTimeSetUp]
    public void InitializePackers() => NetworkManager.LoadOrGenerateHashes();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        foreach (var value in _objects)
            if (value)
                Object.DestroyImmediate(value);
        _objects.Clear();

        foreach (var scene in _scenes)
        {
            var unload = SceneManager.UnloadSceneAsync(scene);
            if (unload != null)
                yield return unload;
        }
        _scenes.Clear();
    }

    [Test]
    public void ForceAtPosition_WireRoundTripUsesReceiversOrigin()
    {
        var sender = CreateBody(new OriginFrame(new double3(LargeOrigin, 0, 0)));
        var receiver = CreateBody(new OriginFrame(new double3(LargeOrigin + 400, 0, 0)));
        var data = RoundTrip(sender.EncodeForceAtPosition(
            new Vector3(0, 8, 0), new Vector3(2, 3, 4), ForceMode.Impulse));

        Assert.That(data.position.HasValue, Is.False);
        Assert.That(data.absolutePosition, Is.EqualTo(new double3(LargeOrigin + 2, 3, 4)));
        Assert.That(data.mode, Is.EqualTo(ForceMode.Impulse));
        Assert.That(receiver.TryResolveForcePosition(in data, out var point), Is.True);
        AssertVector(new Vector3(-398, 3, 4), point);
    }

    [Test]
    public void ForceAtPosition_ReceiverShiftDuringTransitPreservesApplicationPoint()
    {
        var sender = CreateBody(new OriginFrame(new double3(LargeOrigin, 0, 0)));
        var receiverFrame = new OriginFrame(new double3(LargeOrigin + 400, 0, 0));
        var receiver = CreateBody(receiverFrame);
        var data = RoundTrip(sender.EncodeForceAtPosition(
            Vector3.up, new Vector3(2, 3, 4), ForceMode.Force));

        receiverFrame.origin += new double3(64, -8, 16);

        Assert.That(receiver.TryResolveForcePosition(in data, out var point), Is.True);
        AssertVector(new Vector3(-462, 11, -12), point);
        Assert.That(receiverFrame.ToAbsolute(receiver, point), Is.EqualTo(data.absolutePosition.Value));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ForceAtPosition_LegacyPointIgnoresReceiverOriginConverter(bool receiverHasConverter)
    {
        var sender = CreateBody(null);
        var receiverFrame = new OriginFrame(new double3(LargeOrigin, 0, 0));
        var receiver = CreateBody(receiverHasConverter ? receiverFrame : null);
        var data = RoundTrip(sender.EncodeForceAtPosition(
            Vector3.up, new Vector3(2, 3, 4), ForceMode.Acceleration));

        Assert.That(data.absolutePosition.HasValue, Is.False);
        Assert.That(receiver.TryResolveForcePosition(in data, out var point), Is.True);
        AssertVector(new Vector3(2, 3, 4), point);
        Assert.That(receiverFrame.decodeCount, Is.Zero);
    }

    [Test]
    public void ForceAtPosition_AbsolutePointNeedsReceiverConverter()
    {
        var sender = CreateBody(new OriginFrame(new double3(LargeOrigin, 0, 0)));
        var receiver = CreateBody(null);
        var data = RoundTrip(sender.EncodeForceAtPosition(Vector3.up, Vector3.right, ForceMode.Impulse));

        Assert.That(receiver.TryResolveForcePosition(in data, out _), Is.False);
    }

    [UnityTest]
    public IEnumerator ForceAtPosition_ReceivedImpulseKeepsTorqueAfterOriginShift()
    {
        var scene = SceneManager.CreateScene(
            $"RigidbodyForceOrigin-{Guid.NewGuid():N}", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        _scenes.Add(scene);

        var sender = CreateBody(new OriginFrame(new double3(LargeOrigin, 0, 0)));
        var receiverFrame = new OriginFrame(new double3(LargeOrigin + 256, 0, 0));
        var receiver = CreateBody(receiverFrame);
        var baseline = CreateBody(null);
        SceneManager.MoveGameObjectToScene(receiver.gameObject, scene);
        SceneManager.MoveGameObjectToScene(baseline.gameObject, scene);
        var receiverBody = receiver.GetComponent<Rigidbody>();
        var baselineBody = baseline.GetComponent<Rigidbody>();
        receiverBody.position = new Vector3(-256, 0, 0);
        baselineBody.position = new Vector3(0, 10, 0);
        var impulse = new Vector3(0, 8, 0);
        var data = RoundTrip(sender.EncodeForceAtPosition(impulse, Vector3.right * 2, ForceMode.Impulse));

        receiverFrame.origin += new double3(64, 0, 0);
        receiverBody.position -= Vector3.right * 64;

        typeof(NetworkRigidbody).GetMethod("ApplyForce", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(receiver, new object[] { data });
        baselineBody.AddForceAtPosition(impulse, baselineBody.position + Vector3.right * 2, ForceMode.Impulse);
        scene.GetPhysicsScene().Simulate(0.02f);

        Assert.That(baselineBody.angularVelocity.sqrMagnitude, Is.GreaterThan(0.1f));
        AssertVector(baseline.angularVelocity, receiver.angularVelocity);
        AssertVector(baseline.linearVelocity, receiver.linearVelocity);
        yield break;
    }

    private NetworkRigidbody CreateBody(OriginFrame frame)
    {
        var value = new GameObject(nameof(NetworkRigidbodyForceOriginTests));
        _objects.Add(value);
        value.AddComponent<BoxCollider>();
        var body = value.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.mass = 2f;
        body.maxAngularVelocity = 1000f;
        var networkRigidbody = value.AddComponent<NetworkRigidbody>();
        networkRigidbody.SetPositionTransform(frame);
        return networkRigidbody;
    }

    private static AppliedForce RoundTrip(AppliedForce data)
    {
        using var packer = BitPackerPool.Get();
        packer.ResetPositionAndMode(false);
        Packer<AppliedForce>.Write(packer, data);
        packer.ResetPositionAndMode(true);
        AppliedForce result = default;
        Packer<AppliedForce>.Read(packer, ref result);
        return result;
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.0001f));
    }

    private sealed class OriginFrame : INetworkRigidbodyPositionTransform
    {
        public double3 origin;
        public int decodeCount;

        public OriginFrame(double3 origin) => this.origin = origin;

        public double3 ToAbsolute(NetworkRigidbody self, Vector3 localWorldPos)
            => origin + new double3(localWorldPos.x, localWorldPos.y, localWorldPos.z);

        public Vector3 ToLocal(NetworkRigidbody self, double3 absolutePosition)
        {
            decodeCount++;
            var local = absolutePosition - origin;
            return new Vector3((float)local.x, (float)local.y, (float)local.z);
        }
    }
}
#endif
