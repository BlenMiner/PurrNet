using PurrNet;
using PurrNet.Packing;
using UnityEngine;

public class AsyncSpawnSerializeIdentity : NetworkIdentity
{
    public const int MagicSentinel = 69;
    public static readonly Vector3 VectorSentinel = new(7f, 8f, 9f);

    public static AsyncSpawnSerializeIdentity LocalInstance;
    public static int SerializeCount;
    public static int DeserializeCount;
    public static int ServerAckCount;
    public static int ServerBadCount;
    public static int LastBadMagic;
    public static int AliveCount;

    public int readMagic;
    public Vector3 readVector;

    public static void ResetCycle()
    {
        LocalInstance = null;
        SerializeCount = 0;
        DeserializeCount = 0;
        ServerAckCount = 0;
        ServerBadCount = 0;
        LastBadMagic = 0;
    }

    public static void ResetAll()
    {
        ResetCycle();
        AliveCount = 0;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned()
    {
        AliveCount++;
    }

    protected override void OnDespawned()
    {
        AliveCount--;
    }

    protected override void OnSpawned(bool asServer)
    {
        LocalInstance = this;
    }

    protected override void OnSerialize(BitPacker packer)
    {
        SerializeCount++;
        Packer<int>.Write(packer, MagicSentinel);
        Packer<Vector3>.Write(packer, VectorSentinel);
    }

    protected override void OnDeserialize(BitPacker packer)
    {
        DeserializeCount++;
        Packer<int>.Read(packer, ref readMagic);
        Packer<Vector3>.Read(packer, ref readVector);
    }

    public bool ReadOk => readMagic == MagicSentinel && readVector == VectorSentinel;

    [ServerRpc(requireOwnership: false)]
    public void SignalDeserialized(bool ok, int magic, RPCInfo info = default)
    {
        ServerAckCount++;
        if (ok)
            return;
        ServerBadCount++;
        LastBadMagic = magic;
    }
}
