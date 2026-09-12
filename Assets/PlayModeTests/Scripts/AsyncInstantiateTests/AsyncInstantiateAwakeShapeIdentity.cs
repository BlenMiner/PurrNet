using System;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;

public enum AsyncInstantiateAwakeMutation
{
    AddNetworkIdentity = 0,
    ReparentNetworkIdentity = 1,
    RemoveNetworkIdentity = 2,
    SwapNetworkSiblings = 3,
}

/// <summary>
/// Network root for the unsupported-shape-mutation test. A successful test never invokes
/// OnSpawned: the proxy must diagnose the Awake mutation and leave the local clone unspawned.
/// </summary>
public sealed class AsyncInstantiateAwakeShapeIdentity : NetworkIdentity
{
    private static readonly HashSet<NetworkID> _spawned = new();
    private NetworkID? _trackedId;

    public static int spawnedCount => _spawned.Count;

    public static void ResetAll() => _spawned.Clear();

    protected override void OnEarlySpawn() => gameObject.SetActive(true);

    protected override void OnSpawned()
    {
        if (!id.HasValue)
            return;

        _trackedId = id.Value;
        _spawned.Add(id.Value);
    }

    protected override void OnDespawned()
    {
        if (_trackedId.HasValue)
            _spawned.Remove(_trackedId.Value);
        _trackedId = null;
    }
}

/// <summary>
/// Configurable Awake-time topology mutation. It only mutates clones, so activating the
/// runtime template before InstantiateAsync is safe and guarantees Awake runs during native
/// async integration.
/// </summary>
public sealed class AsyncInstantiateAwakeShapeMutator : MonoBehaviour
{
    private static readonly List<GameObject> _mutatedObjects = new();

    public static bool mutationEnabled;
    public static AsyncInstantiateAwakeMutation mutation;
    public static int mutatedCloneCount { get; private set; }

    public static bool anyMutatedIdentitySpawned
    {
        get
        {
            for (var i = 0; i < _mutatedObjects.Count; i++)
            {
                var obj = _mutatedObjects[i];
                if (obj && obj.TryGetComponent<NetworkIdentity>(out var identity) && identity.isSpawned)
                    return true;
            }
            return false;
        }
    }

    public static void ResetAll()
    {
        mutationEnabled = false;
        mutation = AsyncInstantiateAwakeMutation.AddNetworkIdentity;
        mutatedCloneCount = 0;
        _mutatedObjects.Clear();
    }

    public static void CleanupDetachedObjects()
    {
        for (int i = 0; i < _mutatedObjects.Count; i++)
        {
            var go = _mutatedObjects[i];
            if (!go || go.transform.parent)
                continue;

            var identity = go.GetComponent<NetworkIdentity>();
            if (identity)
                identity.Despawn();
            else
                UnityProxy.DestroyDirectly(go);
        }

        _mutatedObjects.Clear();
    }

    private void Awake()
    {
        if (!mutationEnabled || !gameObject.name.EndsWith("(Clone)", StringComparison.Ordinal))
            return;

        mutatedCloneCount++;

        switch (mutation)
        {
            case AsyncInstantiateAwakeMutation.AddNetworkIdentity:
            {
                var child = new GameObject("AwakeAddedNetworkIdentity");
                child.transform.SetParent(transform, false);
                child.AddComponent<NetworkIdentity>();
                _mutatedObjects.Add(child);
                break;
            }
            case AsyncInstantiateAwakeMutation.ReparentNetworkIdentity:
            {
                var child = transform.Find("ExpectedNetworkChild");
                if (!child)
                    break;

                child.SetParent(null, false);
                _mutatedObjects.Add(child.gameObject);
                break;
            }
            case AsyncInstantiateAwakeMutation.RemoveNetworkIdentity:
            {
                var child = transform.Find("ExpectedNetworkChild");
                if (child)
                    // The shape must already be different when the async completion callback
                    // validates it. Destroy is deferred by Unity until the end of the frame.
                    UnityProxy.DestroyImmediateDirectly(child.gameObject);
                break;
            }
            case AsyncInstantiateAwakeMutation.SwapNetworkSiblings:
            {
                // Same identity count, same parents — only the sibling order of two networked
                // children changes. Silent acceptance would desync child order across peers.
                var expected = transform.Find("ExpectedNetworkChild");
                var second = transform.Find("SecondNetworkChild");
                if (!expected || !second)
                    break;

                second.SetSiblingIndex(expected.GetSiblingIndex());
                break;
            }
        }
    }
}
