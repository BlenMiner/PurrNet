using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-authoritative <see cref="SyncBigData"/>. An owning client sets the payload, the server
/// downloads it from the owner and then proxies it out to every other observer.
/// Also records <see cref="SyncBigData.onSyncStatusChanged"/> traffic so the scenario can assert
/// that receivers are actually driven by the event and not only by polling the final bytes.
/// </summary>
public class SyncBigDataOwnerAuthIdentity : NetworkIdentity
{
    public const int PayloadLength = 16384;

    // High throughput so the throttled transfer completes quickly under test.
    [SerializeField] private SyncBigData _data = new(ownerAuth: true, maxKBPerSec: 4000);

    public static SyncBigDataOwnerAuthIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ReceivedCount;
    public static int ServerDoneCount;
    public static bool PhaseDoneReceived;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;

    public static int StatusEvents;
    public static bool SawDoneEvent;
    public static bool PercentWentBackwards;

    private static float _lastPercent;

    private bool _subscribed;

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ReceivedCount = 0;
        ServerDoneCount = 0;
        PhaseDoneReceived = false;
        OwnerId = 0;
        OwnerIdReceived = false;
        StatusEvents = 0;
        SawDoneEvent = false;
        PercentWentBackwards = false;
        _lastPercent = 0f;
    }

    public static byte[] BuildPayload()
    {
        var b = new byte[PayloadLength];
        uint state = 0x9E3779B9;
        for (int i = 0; i < PayloadLength; i++)
        {
            state = state * 1664525 + 1013904223;
            b[i] = (byte)(state >> 24);
        }
        return b;
    }

    public void Send() => _data.SetData(BuildPayload());

    public bool Received()
    {
        var d = _data.data;
        if (d.Count != PayloadLength) return false;

        uint state = 0x9E3779B9;
        for (int i = 0; i < PayloadLength; i++)
        {
            state = state * 1664525 + 1013904223;
            if (d.Array[d.Offset + i] != (byte)(state >> 24)) return false;
        }
        return true;
    }

    public float progress => _data.progress;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;

        // host runs this twice on the same instance; only one subscription must exist
        if (_subscribed)
            return;

        _subscribed = true;
        _data.onSyncStatusChanged += OnStatusChanged;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (!_subscribed)
            return;

        _subscribed = false;
        _data.onSyncStatusChanged -= OnStatusChanged;
    }

    private static void OnStatusChanged(SyncStatus status)
    {
        StatusEvents++;

        if (status.percent < _lastPercent)
            PercentWentBackwards = true;

        _lastPercent = status.percent;

        if (status.isDone)
            SawDoneEvent = true;
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
