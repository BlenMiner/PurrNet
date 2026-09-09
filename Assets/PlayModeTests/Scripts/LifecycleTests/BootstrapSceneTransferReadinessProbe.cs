using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public sealed class BootstrapSceneTransferReadinessProbe : NetworkIdentity
{
    private static readonly HashSet<BootstrapSceneTransferReadinessProbe> ServerInstances = new();
    private static readonly HashSet<BootstrapSceneTransferReadinessProbe> ClientInstances = new();

    [SerializeField] private bool _isChild;
    private readonly SyncVar<int> _value = new(-1);

    public static int ClientSpawnCount { get; private set; }
    public static int ClientDespawnCount { get; private set; }
    public static int ServerSpawnCount { get; private set; }
    public bool isChild => _isChild;
    public int expectedValue => _isChild ? 33102 : 33101;
    public int value => _value.value;

    public void Configure(bool child) => _isChild = child;

    public static void ResetAll()
    {
        ServerInstances.Clear();
        ClientInstances.Clear();
        ClientSpawnCount = 0;
        ClientDespawnCount = 0;
        ServerSpawnCount = 0;
    }

    public static int AliveCount(bool asServer)
    {
        int count = 0;
        foreach (var probe in asServer ? ServerInstances : ClientInstances)
            if (probe && probe.IsSpawned(asServer))
                count++;
        return count;
    }

    public static bool HasCompleteHierarchy(bool asServer, SceneID expectedScene)
    {
        BootstrapSceneTransferReadinessProbe root = null;
        BootstrapSceneTransferReadinessProbe child = null;
        int count = 0;
        foreach (var probe in asServer ? ServerInstances : ClientInstances)
        {
            if (!probe || !probe.IsSpawned(asServer))
                continue;
            count++;
            if (!probe.id.HasValue || !probe.sceneId.Equals(expectedScene) || probe.value != probe.expectedValue)
                return false;
            if (probe.isChild)
                child = probe;
            else
                root = probe;
        }

        return count == 2 && root && child && child.transform.IsChildOf(root.transform) &&
               root.gameObject.scene.name == "Bootstrap" && child.gameObject.scene == root.gameObject.scene;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (asServer)
        {
            ServerInstances.Add(this);
            ServerSpawnCount++;
            _value.value = expectedValue;
        }
        else
        {
            ClientInstances.Add(this);
            ClientSpawnCount++;
        }
    }

    protected override void OnDespawned(bool asServer)
    {
        if (asServer)
            ServerInstances.Remove(this);
        else
        {
            ClientInstances.Remove(this);
            ClientDespawnCount++;
        }
    }
}
