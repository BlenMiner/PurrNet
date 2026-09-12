using System.Collections.Generic;
using PurrNet;
using PurrNet.Packing;

public class MixedHierarchySerializeIdentity : NetworkIdentity
{
    public static readonly List<MixedHierarchySerializeIdentity> Spawned = new();

    public int slot;
    public int ReadToken { get; private set; }
    public int DeserializeCount { get; private set; }

    public static int Token(int slot) => 7919 * slot + 31;

    protected override void OnEarlySpawn(bool asServer)
    {
        if (slot != 1)
            return;

        // Reorder after prefab indices are assigned, before the server captures the live tree.
        if (asServer)
        {
            MixedHierarchySerializeIdentity a = null;
            MixedHierarchySerializeIdentity b = null;
            foreach (var identity in GetComponentsInChildren<MixedHierarchySerializeIdentity>(true))
            {
                if (identity.slot == 3) a = identity;
                if (identity.slot == 6) b = identity;
            }

            if (a && b && a.transform.parent == b.transform.parent &&
                b.transform.GetSiblingIndex() > a.transform.GetSiblingIndex())
                b.transform.SetSiblingIndex(a.transform.GetSiblingIndex());
        }

        gameObject.SetActive(true);
    }

    protected override void OnSpawned() => Spawned.Add(this);

    protected override void OnDespawned()
    {
        Spawned.Remove(this);
        ReadToken = 0;
        DeserializeCount = 0;
    }

    protected override void OnSerialize(BitPacker packer) => Packer<int>.Write(packer, Token(slot));

    protected override void OnDeserialize(BitPacker packer)
    {
        int value = 0;
        Packer<int>.Read(packer, ref value);
        ReadToken = value;
        DeserializeCount++;
    }
}
