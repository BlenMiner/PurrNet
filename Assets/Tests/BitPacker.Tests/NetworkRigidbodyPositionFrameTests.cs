using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using Unity.Mathematics;
using UnityEngine;

public class NetworkRigidbodyPositionFrameTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> _objects = new();
    private NetworkRigidbody _networkBody;
    private Rigidbody _body;
    private OriginTransform _origin;

    private sealed class OriginTransform : INetworkRigidbodyPositionTransform
    {
        public double3 offset = new double3(10000, 0, 0);
        public double3 ToAbsolute(NetworkRigidbody self, Vector3 position) => offset + new double3(position.x, position.y, position.z);
        public Vector3 ToLocal(NetworkRigidbody self, double3 position)
        {
            var local = position - offset;
            return new Vector3((float)local.x, (float)local.y, (float)local.z);
        }
    }

    [SetUp]
    public void SetUp()
    {
        var go = CreateObject("cargo");
        _body = go.AddComponent<Rigidbody>();
        _body.position = new Vector3(25, 2, 3);
        _networkBody = go.AddComponent<NetworkRigidbody>();
        _origin = new OriginTransform();
        _networkBody.SetPositionTransform(_origin);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var go in _objects)
            if (go) Object.DestroyImmediate(go);
        _objects.Clear();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MissingWireParent_DoesNotUsePreviousSoftParentOrUnityHierarchy(bool soft)
    {
        var oldParent = CreateObject("old parent").AddComponent<NetworkRigidbody>();
        _networkBody.SetSoftParent(oldParent);
        _networkBody.transform.SetParent(oldParent.transform, true);
        var originalPosition = _body.position;

        Push(State(RigidbodyPositionFrame.ParentLocal, new double3(1, 2, 3), null, soft));

        Assert.That(Field<int>("_bufferCount"), Is.Zero);
        Assert.That(_body.position, Is.EqualTo(originalPosition));
        Assert.That(_networkBody.softParentInstance, Is.SameAs(oldParent));

        // A subsequent packet can be used as soon as its actual parent resolves.
        var newParent = CreateObject("new parent").AddComponent<NetworkRigidbody>();
        Push(State(RigidbodyPositionFrame.ParentLocal, new double3(1, 2, 3), newParent, soft));
        Invoke("SampleBuffer");
        Assert.That(Field<int>("_bufferCount"), Is.EqualTo(1));
        Assert.That(Field<Transform>("_targetParent"), Is.SameAs(newParent.transform));
        Assert.That(Field<RigidbodyPositionFrame>("_targetPositionFrame"), Is.EqualTo(RigidbodyPositionFrame.ParentLocal));
    }

    [Test]
    public void DestroyedParent_InvalidatesBufferedAndAlreadySampledLocalPositions()
    {
        var parent = CreateObject("parent").AddComponent<NetworkRigidbody>();
        Push(State(RigidbodyPositionFrame.ParentLocal, new double3(1, 2, 3), parent, true));
        Invoke("SampleBuffer");
        Object.DestroyImmediate(parent.gameObject);

        Assert.That(TryDecodeTarget(out _), Is.False, "A sampled target must remain parent-local after its Transform is destroyed.");
        Invoke("SampleBuffer");
        Assert.That(Field<int>("_bufferCount"), Is.Zero);
        Assert.That(TryDecodeTarget(out _), Is.False);

        var anchor = (RigidbodyStateData)Invoke("CaptureTargetState");
        Assert.That(anchor.positionFrame, Is.EqualTo(RigidbodyPositionFrame.Absolute));
        Assert.That(math.distance(anchor.absolutePosition, _origin.ToAbsolute(_networkBody, _body.position)), Is.LessThan(0.0001));
    }

    [TestCase("SendInitialStateToObserver")]
    [TestCase("SendHandoffState")]
    [TestCase("Teleport")]
    public void ImmediateReceivePaths_RejectUnresolvedParentBeforeApplyingPose(string rpc)
    {
        var originalPosition = _body.position;
        var state = State(RigidbodyPositionFrame.ParentLocal, new double3(1, 2, 3), null, true);

        ReceiveRpc(rpc, state);

        Assert.That(_body.position, Is.EqualTo(originalPosition));
        Assert.That(Field<int>("_bufferCount"), Is.Zero);
        Assert.That(Field<bool>("_hasPendingTeleport"), Is.False);
        if (rpc == "SendInitialStateToObserver")
            Assert.That(_body.mass, Is.EqualTo(2f), "Initial settings must survive rejection of an unresolved pose.");
    }

    [TestCase(RigidbodyPositionFrame.ParentLocal, 103f)]
    [TestCase(RigidbodyPositionFrame.Absolute, 3f)]
    [TestCase(RigidbodyPositionFrame.World, 3f)]
    public void ReceivedTeleport_DecodesTheTransmittedFrame(RigidbodyPositionFrame frame, float expectedX)
    {
        var parent = CreateObject("parent").AddComponent<NetworkRigidbody>();
        parent.transform.position = new Vector3(100, 0, 0);
        var position = new double3(frame == RigidbodyPositionFrame.Absolute ? 10003 : 3, 2, 1);

        ReceiveRpc("Teleport", State(frame, position, parent, frame == RigidbodyPositionFrame.ParentLocal));

        AssertPosition(new Vector3(expectedX, 2, 1), _body.position);
        Assert.That(Field<RigidbodyPositionFrame>("_targetPositionFrame"), Is.EqualTo(frame));
        Assert.That(TryDecodeTarget(out var target), Is.True);
        Assert.That(target, Is.EqualTo(_body.position));
    }

    [Test]
    public void DestroyedOldParent_KeepsLaterUnparentedSnapshot()
    {
        var parent = CreateObject("parent").AddComponent<NetworkRigidbody>();
        Push(State(RigidbodyPositionFrame.ParentLocal, new double3(1, 2, 3), parent, true));
        Push(State(RigidbodyPositionFrame.World, new double3(30, 4, 5)));
        Object.DestroyImmediate(parent.gameObject);

        Invoke("SampleBuffer");

        Assert.That(Field<int>("_bufferCount"), Is.EqualTo(1));
        Assert.That(TryDecodeTarget(out var target), Is.True);
        AssertPosition(new Vector3(30, 4, 5), target);
    }

    [Test]
    public void WorldSnapshot_BypassesOriginDriverAndUnrelatedParentReference()
    {
        var parent = CreateObject("unrelated parent").AddComponent<NetworkRigidbody>();
        parent.transform.position = new Vector3(100, 0, 0);
        Push(State(RigidbodyPositionFrame.World, new double3(10, 2, 3), parent));
        Invoke("SampleBuffer");

        Assert.That(Field<Transform>("_targetParent"), Is.Null);
        Assert.That(TryDecodeTarget(out var target), Is.True);
        AssertPosition(new Vector3(10, 2, 3), target);
        var anchor = (RigidbodyStateData)Invoke("CaptureTargetState");
        Assert.That(anchor.positionFrame, Is.EqualTo(RigidbodyPositionFrame.World));
    }

    [Test]
    public void InterpolationAcrossWorldAndAbsoluteFrames_ConvertsBothEndpoints()
    {
        Push(State(RigidbodyPositionFrame.World, new double3(10, 2, 3)));
        Push(State(RigidbodyPositionFrame.Absolute, new double3(10012, 2, 3)));
        var first = Invoke("GetSnapshot", 0);
        var second = Invoke("GetSnapshot", 1);

        Invoke("HermiteInterpolate", first, second, 0.1f, 0.5f);

        Assert.That(Field<RigidbodyPositionFrame>("_targetPositionFrame"), Is.EqualTo(RigidbodyPositionFrame.Absolute));
        Assert.That(TryDecodeTarget(out var target), Is.True);
        AssertPosition(new Vector3(11, 2, 3), target);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void BufferedPosition_UsesCurrentOriginOrParentPhysicsPoseAfterShift(bool parented)
    {
        var parentObject = CreateObject("car");
        var parentBody = parentObject.AddComponent<Rigidbody>();
        parentBody.position = new Vector3(20, 0, 0);
        var parent = parentObject.AddComponent<NetworkRigidbody>();
        Push(parented
            ? State(RigidbodyPositionFrame.ParentLocal, new double3(1, 2, 3), parent, true)
            : State(RigidbodyPositionFrame.Absolute, new double3(10021, 2, 3)));
        Invoke("SampleBuffer");

        _origin.offset += new double3(100, 0, 0);
        parentBody.position -= new Vector3(100, 0, 0);

        Assert.That(TryDecodeTarget(out var target), Is.True);
        AssertPosition(new Vector3(-79, 2, 3), target);
    }

    private GameObject CreateObject(string name)
    {
        var go = new GameObject(name);
        _objects.Add(go);
        return go;
    }

    private static RigidbodyStateData State(RigidbodyPositionFrame frame, double3 position,
        NetworkIdentity parent = null, bool soft = false)
    {
        return new RigidbodyStateData
        {
            positionFrame = frame,
            position = new Vector3((float)position.x, (float)position.y, (float)position.z),
            absolutePosition = position,
            rotation = Quaternion.identity,
            parent = parent,
            isSoftParent = soft
        };
    }

    private void Push(RigidbodyStateData state) => Invoke("PushSnapshot", state, true);
    private static void AssertPosition(Vector3 expected, Vector3 actual)
    {
        Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.0001f),
            "Allow float rounding when decoding CompressedVector3.");
    }
    private T Field<T>(string name) => (T)typeof(NetworkRigidbody).GetField(name, PrivateInstance)!.GetValue(_networkBody);
    private object Invoke(string name, params object[] args) => typeof(NetworkRigidbody).GetMethod(name, PrivateInstance)!.Invoke(_networkBody, args);

    private void ReceiveRpc(string name, RigidbodyStateData state)
    {
        // Exercise the actual receiver body after IL postprocessing, bypassing the send wrapper.
        var receiver = typeof(NetworkRigidbody).GetMethods(PrivateInstance | BindingFlags.Public)
            .Single(method => method.Name.StartsWith(name + "_Original_", System.StringComparison.Ordinal));
        if (name == "Teleport")
        {
            receiver.Invoke(_networkBody, new object[] { new RigidbodyTeleportData
            {
                position = state.position,
                absolutePosition = state.absolutePosition,
                positionFrame = state.positionFrame,
                rotation = state.rotation,
                parent = state.parent,
                isSoftParent = state.isSoftParent
            } });
        }
        else if (name == "SendInitialStateToObserver")
            receiver.Invoke(_networkBody, new object[] { default(PlayerID), state, new RigidbodySettingsData { mass = (PurrNet.Packing.Half)2f } });
        else
            receiver.Invoke(_networkBody, new object[] { default(PlayerID), state });
    }

    private bool TryDecodeTarget(out Vector3 position)
    {
        object[] args = { Field<double3>("_targetPosition"), Field<Transform>("_targetParent"), Field<RigidbodyPositionFrame>("_targetPositionFrame"), null };
        bool valid = (bool)Invoke("TryToWorldPosition", args);
        position = (Vector3)args[3];
        return valid;
    }
}
