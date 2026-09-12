using PurrNet;
using PurrNet.Transports;
using UnityEngine;

// Gameplay-style RPC load generator. The server calls both RPCs; the transform never moves, so
// this scenario isolates the RPC packing paths from NetworkTransform replication:
// - RpcDeltaState: unreliable + deltaPacked => per-recipient DeltaModule.Write per argument
//   (the O(players) delta-encode path).
// - RpcReliableEvent: plain reliable observers RPC => shared-entry RPCBatch fan-out path.
public class RpcFanoutEmitter : NetworkIdentity
{
    public static long unreliableReceived;
    public static long reliableReceived;

    public static void ResetCounters()
    {
        unreliableReceived = 0;
        reliableReceived = 0;
    }

    public void SendState(Vector3 position, int health, uint tick)
        => RpcDeltaState(position, health, tick);

    public void SendEvent(Vector3 position, int payload)
        => RpcReliableEvent(position, payload);

    [ObserversRpc(Channel.Unreliable, deltaPacked: true)]
    private void RpcDeltaState(Vector3 position, int health, uint tick)
    {
        unreliableReceived++;
    }

    [ObserversRpc]
    private void RpcReliableEvent(Vector3 position, int payload)
    {
        reliableReceived++;
    }
}
