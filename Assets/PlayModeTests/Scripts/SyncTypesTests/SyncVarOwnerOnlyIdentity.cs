using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Mixes [OwnerOnly] and normal SyncVars on one identity. The scenario drives phases from the
/// server; clients sample their local values whenever <see cref="phaseToSample"/> advances and
/// report them back, so all assertions happen server-side with cross-process data.
/// </summary>
public class SyncVarOwnerOnlyIdentity : NetworkIdentity
{
    public const int PhaseCount = 7;

    [SerializeField] private SyncVar<int> _secret = new(0, ownerOnly: true);
    [SerializeField] private SyncVar<int> _ownerSecret = new(0, ownerAuth: true, ownerOnly: true);
    [SerializeField] private SyncVar<int> _shared = new(0);
    [SerializeField] private SyncVar<int> _phaseToSample = new(0);
    [SerializeField] private SyncVar<int> _ownerWriteRequest = new(0);

    public struct Report
    {
        public int secret;
        public int ownerSecret;
        public int shared;
    }

    public static SyncVarOwnerOnlyIdentity LocalInstance;
    public static int ServerReadyCount;
    public static readonly Dictionary<int, Dictionary<ulong, Report>> Reports = new();

    public static void ResetAll()
    {
        LocalInstance = null;
        ServerReadyCount = 0;
        Reports.Clear();
    }

    public int secret => _secret.value;
    public int ownerSecret => _ownerSecret.value;
    public int shared => _shared.value;
    public int phaseToSample => _phaseToSample.value;

    public void ServerSetSecret(int value) => _secret.value = value;
    public void ServerSetShared(int value) => _shared.value = value;
    public void ServerSetPhase(int value) => _phaseToSample.value = value;
    public void ServerRequestOwnerWrite(int value) => _ownerWriteRequest.value = value;

    public void TryOwnerWrite()
    {
        var request = _ownerWriteRequest.value;
        if (request != 0 && isOwner && _ownerSecret.value != request)
            _ownerSecret.value = request;
    }

    public static bool TryGetReport(int phase, ulong playerId, out Report report)
    {
        report = default;
        return Reports.TryGetValue(phase, out var perPlayer) && perPlayer.TryGetValue(playerId, out report);
    }

    public static int ReportCount(int phase) =>
        Reports.TryGetValue(phase, out var perPlayer) ? perPlayer.Count : 0;

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned(bool asServer) => LocalInstance = this;

    [ServerRpc(requireOwnership: false)]
    public void SignalReady(RPCInfo info = default) => ServerReadyCount++;

    [ServerRpc(requireOwnership: false)]
    public void ReportState(int phase, int secretValue, int ownerSecretValue, int sharedValue, RPCInfo info = default)
    {
        if (!Reports.TryGetValue(phase, out var perPlayer))
            Reports[phase] = perPlayer = new Dictionary<ulong, Report>();

        perPlayer[info.sender.id.value] = new Report
        {
            secret = secretValue,
            ownerSecret = ownerSecretValue,
            shared = sharedValue
        };
    }
}
