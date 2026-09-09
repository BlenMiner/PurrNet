using System.Collections.Generic;
using PurrNet;

/// <summary>Separate identities for candidate-only and old-host-only replication state.</summary>
public sealed class MigrationReconcileProbe : NetworkIdentity
{
    public const int OldHostOnly = 1;
    public const int CandidateOnly = 2;

    private readonly SyncVar<int> _kind = new(0);
    private static readonly HashSet<MigrationReconcileProbe> Clients = new();
    private static readonly HashSet<MigrationReconcileProbe> Servers = new();

    public void SetKind(int kind) => _kind.value = kind;

    public static void ResetAll()
    {
        Clients.Clear();
        Servers.Clear();
    }

    public static MigrationReconcileProbe Find(int kind, bool asServer = false)
    {
        foreach (var probe in asServer ? Servers : Clients)
            if (probe && probe.IsSpawned(asServer) && probe._kind.value == kind)
                return probe;
        return null;
    }

    public static int Count(int kind, bool asServer = false)
    {
        int count = 0;
        foreach (var probe in asServer ? Servers : Clients)
            if (probe && probe.IsSpawned(asServer) && probe._kind.value == kind)
                count++;
        return count;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        (asServer ? Servers : Clients).Add(this);
    }

    protected override void OnDespawned(bool asServer)
    {
        (asServer ? Servers : Clients).Remove(this);
    }
}
