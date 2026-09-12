using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

// Per-player avatar for DuckPickupScenario. Carries two holding points:
// "Hand" has its own NetworkIdentity (the recommended setup for parent syncing),
// "BareHand" is a plain child transform (exercises path-based parenting under
// the nearest NetworkIdentity, which is this root).
public class DuckPickupPlayer : NetworkIdentity
{
    public static readonly List<DuckPickupPlayer> Instances = new();

    public static void ResetAll() => Instances.Clear();

    // Prefix match instead of Find: pooled instances get their names tagged
    // (e.g. "Hand-Warmup") when PURRNET_DEBUG_POOLING is enabled in the build.
    public Transform hand => FindChildByPrefix("Hand");
    public Transform bareHand => FindChildByPrefix("BareHand");

    private Transform FindChildByPrefix(string prefix)
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child.name.StartsWith(prefix, StringComparison.Ordinal))
                return child;
        }

        return null;
    }

    public static DuckPickupPlayer FindByOwner(ulong ownerId)
    {
        for (int i = 0; i < Instances.Count; i++)
        {
            var instance = Instances[i];
            if (instance && instance.owner.HasValue && instance.owner.Value.id.value == ownerId)
                return instance;
        }

        return null;
    }

    protected override void OnEarlySpawn()
    {
        gameObject.SetActive(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!Instances.Contains(this))
            Instances.Add(this);
    }

    protected override void OnDespawned()
    {
        Instances.Remove(this);
    }
}
