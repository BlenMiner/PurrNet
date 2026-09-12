using System.Collections.Generic;
using PurrNet;
using Unity.Mathematics;
using UnityEngine;

// Per-process origin shift used by NTHandoffOriginShiftScenario to route positions through
// the absolute double3 frame, as large-world origin shifting does.
public class NTHandoffOriginFrame : INetworkTransformPositionTransform
{
    public static readonly NTHandoffOriginFrame instance = new();

    // Origins differ per process but only by a few meters: origin shifting keeps local
    // coordinates near zero, and large local coordinates would starve float precision for
    // the tiny per-frame moves this test drives. The shared 1e6 component exercises the
    // double3 wire path at a magnitude where float math would visibly quantize.
    static readonly double3 _origin = new(
        1_000_000 + System.Diagnostics.Process.GetCurrentProcess().Id % 7 * 8, 0, 0);

    public double3 ToAbsolute(NetworkTransform self, Vector3 localWorldPos) =>
        _origin + new double3(localWorldPos.x, localWorldPos.y, localWorldPos.z);

    public Vector3 ToLocal(NetworkTransform self, double3 absolutePosition)
    {
        var d = absolutePosition - _origin;
        return new Vector3((float)d.x, (float)d.y, (float)d.z);
    }
}

// Drives its NetworkTransform at constant velocity while this process is the controller and
// records the rendered position every frame, so the scenario can assert observers see smooth
// motion across ownership handoffs.
public class NTHandoffMover : NetworkIdentity
{
    public struct Sample
    {
        public float time;
        public float x;
        public bool controller;
        public bool hasOwner;
        public ulong ownerId;
    }

    public const float Speed = 3.5f;

    public static NTHandoffMover localInstance;

    public readonly List<Sample> samples = new(4096);

    private NetworkTransform _nt;
    private bool _active;

    public static void ResetAll() => localInstance = null;

    protected override void OnEarlySpawn()
    {
        // Runs before the sibling NetworkTransform's OnEarlySpawn (component order), so the
        // origin variant can inject its frame before the transform resolves it.
        if (name.Contains("Origin"))
            GetComponent<NetworkTransform>().SetPositionTransform(NTHandoffOriginFrame.instance);

        gameObject.SetActive(true);
    }

    protected override void OnSpawned() => localInstance = this;

    protected override void OnDespawned()
    {
        if (localInstance == this)
            localInstance = null;
    }

    private void Awake() => _nt = GetComponent<NetworkTransform>();

    public void Begin()
    {
        samples.Clear();
        _active = true;
    }

    public void End() => _active = false;

    private void Update()
    {
        if (_active && _nt && _nt.isController)
            transform.position += Speed * Time.deltaTime * Vector3.right;
    }

    // LateUpdate so the NetworkTransform's Update-time interpolation has already run this frame.
    private void LateUpdate()
    {
        if (!_active || !_nt)
            return;

        var owner = _nt.owner;
        samples.Add(new Sample
        {
            time = Time.unscaledTime,
            x = transform.position.x,
            controller = _nt.isController,
            hasOwner = owner.HasValue,
            ownerId = owner?.id.value ?? 0
        });
    }
}
