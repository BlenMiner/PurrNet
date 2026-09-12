using PurrNet;
using UnityEngine;

/// <summary>
/// Scene object used by SceneScopedAssetHintScenario: sends a scene scoped asset through an identity RPC
/// so the packer resolves it with the sending identity's scene as the hint.
/// </summary>
public class SceneScopedAssetSender : NetworkBehaviour
{
    public static string lastReceivedAssetName;
    public static int receivedCount;

    public static void ResetReceived()
    {
        lastReceivedAssetName = null;
        receivedCount = 0;
    }

    public void Send(TextAsset asset)
    {
        RpcAsset(asset);
    }

    [ObserversRpc]
    private void RpcAsset(TextAsset asset)
    {
        lastReceivedAssetName = asset ? asset.name : null;
        receivedCount++;
    }
}
