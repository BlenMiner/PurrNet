using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative <see cref="SyncEvent{T}"/>. The owning client invokes the event and the payload
/// must survive the owner -> server -> other observers relay unchanged.
/// </summary>
public class SyncEventOwnerAuthIdentity : NetworkIdentity
{
    public const int Sentinel = 10;

    [SerializeField] private SyncEvent<int> _event = new(ownerAuth: true);

    public static SyncEventOwnerAuthIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ReceivedCount;
    public static int ServerDoneCount;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;
    public static bool PhaseDoneReceived;

    public int ReceivedValue { get; private set; } = int.MinValue;
    public int FireCount { get; private set; }
    public bool Received() => ReceivedValue == Sentinel;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ReceivedCount = 0;
        ServerDoneCount = 0;
        OwnerId = 0;
        OwnerIdReceived = false;
        PhaseDoneReceived = false;
    }

    public void Fire() => _event.Invoke(Sentinel);

    public string Describe() => $"value={ReceivedValue}, fires={FireCount}";

    private void OnEventFired(int value)
    {
        ReceivedValue = value;
        FireCount++;
    }

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
        _event.AddListener(OnEventFired);
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalReceived(RPCInfo info = default) => ReceivedCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ObserversRpc(runLocally: true)]
    public void BroadcastOwner(ulong ownerId)
    {
        OwnerId = ownerId;
        OwnerIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
