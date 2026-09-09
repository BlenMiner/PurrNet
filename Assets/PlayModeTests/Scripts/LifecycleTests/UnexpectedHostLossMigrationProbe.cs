using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public sealed class UnexpectedHostLossMigrationProbe : NetworkIdentity
{
    private static readonly HashSet<UnexpectedHostLossMigrationProbe> Instances = new();

    [SerializeField] private bool _isChild;
    [SerializeField] private SyncVar<int> _kind = new(0, sendIntervalInSeconds: 0f);
    [SerializeField] private SyncVar<int> _serverValue = new(0, sendIntervalInSeconds: 0f);
    [SerializeField] private SyncVar<int> _ownerValue = new(0, sendIntervalInSeconds: 0f, ownerAuth: true);
    [SerializeField] private SyncList<int> _ownerValues = new(new List<int> { 0 }, ownerAuth: true);

    public int clientSpawnCount { get; private set; }
    public int clientDespawnCount { get; private set; }
    public bool isChild => _isChild;
    public int migrationSpawnChecks { get; private set; }
    public int migrationDespawnChecks { get; private set; }
    public string migrationLifecycleError { get; private set; }

    private bool _checkMigrationLifecycle;
    private bool _expectServerSpawn;
    private int _expectedServerValue;
    private int _expectedOwnerValue;

    public void ExpectMigrationLifecycle(int serverValue, int ownerValue, bool asServer)
    {
        _expectedServerValue = serverValue;
        _expectedOwnerValue = ownerValue;
        _checkMigrationLifecycle = true;
        _expectServerSpawn = asServer;
        migrationSpawnChecks = 0;
        migrationDespawnChecks = 0;
        migrationLifecycleError = null;
    }

    public void ConfigureChild() => _isChild = true;
    public void SetKind(int value) => _kind.value = value;
    public void SetServerValue(int value) => _serverValue.value = value;
    public void SetOwnerValue(int value)
    {
        _ownerValue.value = value;
        _ownerValues[0] = value;
        // Only the root has a NetworkTransform. Moving the child too would apply
        // the same owner motion twice through the Unity parent hierarchy.
        if (!_isChild)
            transform.localPosition = OwnerPosition(value);
    }

    public bool HasState(int serverValue, int ownerValue) =>
        _serverValue.value == serverValue && _ownerValue.value == ownerValue &&
        _ownerValues.Count == 1 && _ownerValues[0] == ownerValue &&
        (_isChild || Vector3.Distance(transform.localPosition, OwnerPosition(ownerValue)) < 0.05f);

    private static Vector3 OwnerPosition(int value) => new(value * 0.01f, value * 0.005f, -value * 0.002f);

    public static void ResetAll() => Instances.Clear();

    public static UnexpectedHostLossMigrationProbe Find(int kind, bool child, bool asServer)
    {
        foreach (var probe in Instances)
            if (probe && probe._kind.value == kind && probe._isChild == child && probe.IsSpawned(asServer))
                return probe;
        return null;
    }

    public static int Count(int kind, bool asServer)
    {
        var count = 0;
        foreach (var probe in Instances)
            if (probe && probe._kind.value == kind && probe.IsSpawned(asServer))
                count++;
        return count;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    private void CheckMigrationFlag()
    {
        if (!isMigratingServer || !_ownerValue.isMigratingServer)
            migrationLifecycleError = $"{name}: migration flag was false inside a migration callback.";
    }

    protected override void OnSpawned(bool asServer)
    {
        Instances.Add(this);
        if (!asServer)
            clientSpawnCount++;
        if (_checkMigrationLifecycle && asServer == _expectServerSpawn)
        {
            migrationSpawnChecks++;
            CheckMigrationFlag();
            if (!HasState(_expectedServerValue, _expectedOwnerValue))
                migrationLifecycleError = $"{name}: host state missing in OnSpawned; " +
                                          $"server={_serverValue.value}, owner={_ownerValue.value}, " +
                                          $"list=[{string.Join(",", _ownerValues)}], pose={transform.localPosition}";
        }
        else if (isMigratingServer || _ownerValue.isMigratingServer)
            migrationLifecycleError = $"{name}: migration flag was true inside an ordinary spawn.";
    }

    protected override void OnDespawned(bool asServer)
    {
        if (!asServer)
            clientDespawnCount++;
        if (_checkMigrationLifecycle && isMigratingServer)
        {
            migrationDespawnChecks++;
            CheckMigrationFlag();
        }
    }
}
