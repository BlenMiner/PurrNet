using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Packing;
using UnityEngine;

/// <summary>
/// Owner-authoritative SyncVar used to reproduce reconnect packet-id drift. The owner sends a burst
/// before disconnecting, waits for the reconnect copy to restore that value, then sends one more
/// owner-auth value.
/// </summary>
public class SyncVarOwnerReconnectIdentity : NetworkIdentity
{
    [SerializeField] private SyncVar<int> _value = new(0, sendIntervalInSeconds: 0f, ownerAuth: true);

    public static SyncVarOwnerReconnectIdentity LocalInstance;
    public static int ServerReadyCount;
    public static int ServerDoneCount;
    public static int VictimReturnedCount;
    public static ulong OwnerId;
    public static bool OwnerIdReceived;
    public static bool DisconnectCommandReceived;
    public static bool PostReconnectBurstReceived;
    public static bool PhaseDoneReceived;
    public static bool RestoredAfterReconnect;
    public static int BurstReportCount;
    public static ulong BurstReportSender;
    public static int BurstReportValueBefore;
    public static int BurstReportValueAfter;
    public static ulong BurstReportPacketIdBefore;
    public static ulong BurstReportPacketIdAfter;
    public static bool BurstReportIgnoreServerUpdatesAfter;
    public static int OwnerReconnectCheckpointCount;
    public static ulong OwnerReconnectCheckpointSender;
    public static int OwnerReconnectCheckpointValue;
    public static ulong OwnerReconnectCheckpointPacketId;
    public static bool OwnerReconnectCheckpointIgnoreServerUpdates;
    public static int UnchangedResendReportCount;
    public static ulong UnchangedResendPacketId;
    public static string UnchangedResendError;

    private static readonly FieldInfo PacketIdField = typeof(SyncVar<int>).GetField(
        "_id", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo IgnoreServerUpdatesField = typeof(SyncVar<int>).GetField(
        "_ignoreServerUpdates", BindingFlags.Instance | BindingFlags.NonPublic);

    // Codegen preserves the sending wrapper under the original name and makes it public.
    // Invoke it on the server so replay still uses the real serialized TargetRpc path.
    private static readonly MethodInfo SendLatestStateMethod = typeof(SyncVar<int>).GetMethod(
        "SendLatestState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        ServerDoneCount = 0;
        VictimReturnedCount = 0;
        OwnerId = 0;
        OwnerIdReceived = false;
        DisconnectCommandReceived = false;
        PostReconnectBurstReceived = false;
        PhaseDoneReceived = false;
        RestoredAfterReconnect = false;
        BurstReportCount = 0;
        BurstReportSender = 0;
        BurstReportValueBefore = 0;
        BurstReportValueAfter = 0;
        BurstReportPacketIdBefore = 0;
        BurstReportPacketIdAfter = 0;
        BurstReportIgnoreServerUpdatesAfter = false;
        OwnerReconnectCheckpointCount = 0;
        OwnerReconnectCheckpointSender = 0;
        OwnerReconnectCheckpointValue = 0;
        OwnerReconnectCheckpointPacketId = 0;
        OwnerReconnectCheckpointIgnoreServerUpdates = false;
        UnchangedResendReportCount = 0;
        UnchangedResendPacketId = 0;
        UnchangedResendError = null;
    }

    public int currentValue => _value.value;

    public ulong debugPacketId => PacketIdField != null ? (ulong)PacketIdField.GetValue(_value) : ulong.MaxValue;

    public bool debugIgnoreServerUpdates =>
        IgnoreServerUpdatesField != null && (bool)IgnoreServerUpdatesField.GetValue(_value);

    public string DescribeLocalSyncVar() =>
        $"value={currentValue}, packetId={debugPacketId}, ignoreServerUpdates={debugIgnoreServerUpdates}";

    public static string DescribeBurstReport() =>
        $"count={BurstReportCount}, sender={BurstReportSender}, beforeValue={BurstReportValueBefore}, " +
        $"afterValue={BurstReportValueAfter}, packetIdBefore={BurstReportPacketIdBefore}, " +
        $"packetIdAfter={BurstReportPacketIdAfter}, ignoreServerUpdatesAfter={BurstReportIgnoreServerUpdatesAfter}";

    public static string DescribeReconnectCheckpoint() =>
        $"count={OwnerReconnectCheckpointCount}, sender={OwnerReconnectCheckpointSender}, " +
        $"value={OwnerReconnectCheckpointValue}, packetId={OwnerReconnectCheckpointPacketId}, " +
        $"ignoreServerUpdates={OwnerReconnectCheckpointIgnoreServerUpdates}";

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    public void RunOwnerBurst(int firstValue, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _value.value = firstValue + i;
            _value.FlushImmediately();
        }
    }

    public void SendCapturedSnapshot(PlayerID player, ulong packetId, int capturedValue)
    {
        if (!isServer)
            throw new InvalidOperationException("Snapshot replay must be sent by the server.");
        if (SendLatestStateMethod == null)
            throw new MissingMethodException("SyncVar<int>.SendLatestState sending wrapper was not found.");

        SendLatestStateMethod.Invoke(_value, new object[] { player, (PackedULong)packetId, capturedValue });
    }

    [TargetRpc]
    public void RequestUnchangedOwnerResend(PlayerID player, ulong snapshotPacketId)
    {
        ResendUnchangedValue(snapshotPacketId).Forget();
    }

    private async UniTask ResendUnchangedValue(ulong snapshotPacketId)
    {
        try
        {
            if (!isOwner || debugIgnoreServerUpdates)
            {
                ReportUnchangedOwnerResend(debugPacketId,
                    $"expected restored owner before local edits: isOwner={isOwner}, {DescribeLocalSyncVar()}");
                return;
            }

            // Ownership restoration marks the SyncVar dirty without assigning a new value.
            // Schedule that same public operation explicitly so the test does not depend on
            // whether the automatic ownership tick happened before the reconnect checkpoint.
            _value.SetDirty();
            await UniTaskUtils.WaitWithTimeout(
                () => debugPacketId > snapshotPacketId,
                10f,
                this.GetCancellationTokenOnDestroy());

            ReportUnchangedOwnerResend(debugPacketId, debugIgnoreServerUpdates
                ? "unchanged resend unexpectedly enabled ignoreServerUpdates"
                : null);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            ReportUnchangedOwnerResend(debugPacketId, e.Message);
        }
    }

    [ServerRpc]
    private void ReportUnchangedOwnerResend(ulong packetId, string error)
    {
        UnchangedResendReportCount++;
        UnchangedResendPacketId = packetId;
        UnchangedResendError = error;
    }

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalDone(RPCInfo info = default) => ServerDoneCount++;

    [ServerRpc(requireOwnership: false)]
    public void SignalVictimReturned(RPCInfo info = default) => VictimReturnedCount++;

    [ServerRpc(requireOwnership: false)]
    private void ReportOwnerBurstSent(
        int valueBefore, int valueAfter, ulong packetIdBefore, ulong packetIdAfter,
        bool ignoreServerUpdatesAfter, RPCInfo info = default)
    {
        BurstReportCount++;
        BurstReportSender = info.sender.id.value;
        BurstReportValueBefore = valueBefore;
        BurstReportValueAfter = valueAfter;
        BurstReportPacketIdBefore = packetIdBefore;
        BurstReportPacketIdAfter = packetIdAfter;
        BurstReportIgnoreServerUpdatesAfter = ignoreServerUpdatesAfter;
    }

    public void ReportOwnerReconnectCheckpoint()
    {
        ReportOwnerReconnectCheckpoint(
            currentValue,
            debugPacketId,
            debugIgnoreServerUpdates);
    }

    [ServerRpc(requireOwnership: false)]
    private void ReportOwnerReconnectCheckpoint(
        int value, ulong packetId, bool ignoreServerUpdates, RPCInfo info = default)
    {
        OwnerReconnectCheckpointCount++;
        OwnerReconnectCheckpointSender = info.sender.id.value;
        OwnerReconnectCheckpointValue = value;
        OwnerReconnectCheckpointPacketId = packetId;
        OwnerReconnectCheckpointIgnoreServerUpdates = ignoreServerUpdates;
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    public void BroadcastOwner(ulong ownerId)
    {
        OwnerId = ownerId;
        OwnerIdReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastDisconnectCommand()
    {
        DisconnectCommandReceived = true;
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPostReconnectBurst(int firstValue, int count)
    {
        PostReconnectBurstReceived = true;

        if (!networkManager.isLocalPlayerReady || networkManager.localPlayer.id.value != OwnerId)
            return;

        int valueBefore = currentValue;
        ulong packetIdBefore = debugPacketId;
        RunOwnerBurst(firstValue, count);
        ReportOwnerBurstSent(
            valueBefore, currentValue, packetIdBefore, debugPacketId, debugIgnoreServerUpdates);
    }

    [ObserversRpc(runLocally: true)]
    public void BroadcastPhaseDone() => PhaseDoneReceived = true;
}
