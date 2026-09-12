using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public sealed class SpawnChurnProbe : NetworkIdentity
{
    [SerializeField] private SyncVar<Vector3> _expected = new(default, sendIntervalInSeconds: 0f);

    static readonly Dictionary<NetworkID, SpawnChurnProbe> _alive = new();

    public static int aliveCount => _alive.Count;

    public static IEnumerable<SpawnChurnProbe> alive => _alive.Values;

    public Vector3 expected
    {
        get => _expected.value;
        set => _expected.value = value;
    }

    NetworkID? _tracked;

    public static void ResetAll() => _alive.Clear();

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned()
    {
        if (!id.HasValue)
            return;

        _tracked = id.Value;
        _alive[id.Value] = this;
    }

    protected override void OnDespawned()
    {
        if (_tracked.HasValue)
            _alive.Remove(_tracked.Value);
        _tracked = null;
    }
}
