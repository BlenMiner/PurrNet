using PurrNet;
using UnityEngine;

public sealed class OrdinaryJoinSceneProbe : NetworkIdentity
{
    [SerializeField] private int _authoredValue;
    private readonly SyncVar<int> _value = new(-1);

    public int expectedValue => _authoredValue;
    public int value => _value.value;
    public int clientSpawnCount { get; private set; }
    public int clientDespawnCount { get; private set; }

    protected override void OnSpawned(bool asServer)
    {
        if (asServer)
            _value.value = _authoredValue;
        else
            clientSpawnCount++;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (!asServer)
            clientDespawnCount++;
    }
}
