using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using UnityEngine;

public class MixedHierarchySerializeScenario : Scenario
{
    [SerializeField] private float _spawnTimeoutSeconds = 15f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    private const int IdentityCount = 8;
    private const int BarrierBase = 7800;
    private MixedHierarchySerializeIdentity _prefab;

    // Paths contain only ordinary wrappers: pooling may tag network GameObject names.
    private static readonly string[] ParentPaths =
    {
        "", "", "Wrapper/Deep", "Wrapper/Deep", "Between", "Wrapper/Deep", "Dormant", ""
    };
    private static readonly int[] ComponentIndices = { 0, 0, 2, 2, 4, 5, 6, 7 };

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        var root = new GameObject(nameof(MixedHierarchySerializeScenario));
        _prefab = AddIdentity(root.transform, 1);
        AddIdentity(root.transform, 2);
        AddChild(AddChild(AddChild(root.transform, "VisualOnly"), "Detail"), "Leaf");
        var wrapper = AddChild(root.transform, "Wrapper");
        AddChild(wrapper, "Before");
        var deep = AddChild(wrapper, "Deep");
        AddChild(deep, "Before");
        var a = AddChild(deep, "A");
        AddIdentity(a, 3);
        AddIdentity(a, 4);
        AddIdentity(AddChild(AddChild(a, "Between"), "Grandchild"), 5);
        AddChild(deep, "Middle");
        AddIdentity(AddChild(deep, "B"), 6);
        AddChild(wrapper, "After");
        var inactive = AddChild(AddChild(root.transform, "Dormant"), "Inactive");
        AddIdentity(inactive, 7);
        inactive.gameObject.SetActive(false);
        AddIdentity(AddChild(root.transform, "Tail"), 8);

        root.SetActive(false);
        MixedHierarchySerializeIdentity.Spawned.Clear();
        manager.prefabProvider.AddRuntimePrefab(_prefab.name, root, true);
    }

    private static Transform AddChild(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static MixedHierarchySerializeIdentity AddIdentity(Transform target, int slot)
    {
        var identity = target.gameObject.AddComponent<MixedHierarchySerializeIdentity>();
        identity.slot = slot;
        return identity;
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var failures = new List<string>();
        for (int cycle = 0; cycle < 2; cycle++)
        {
            MixedHierarchySerializeIdentity instance = null;
            if (ctx.isServer)
                instance = Instantiate(_prefab);

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => MixedHierarchySerializeIdentity.Spawned.Count == IdentityCount,
                    _spawnTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"cycle {cycle}: spawned {MixedHierarchySerializeIdentity.Spawned.Count}/{IdentityCount} identities");
            }

            Verify(ctx, cycle, failures);
            await ScenarioBarrier.Wait(ctx, BarrierBase + cycle * 10 + 1, _barrierTimeoutSeconds);

            if (ctx.isServer)
                instance.Despawn();

            try
            {
                await UniTaskUtils.WaitWithTimeout(
                    () => MixedHierarchySerializeIdentity.Spawned.Count == 0,
                    _spawnTimeoutSeconds, ctx.cancellationToken);
            }
            catch (TimeoutException)
            {
                return ScenarioResult.Fail($"cycle {cycle}: {MixedHierarchySerializeIdentity.Spawned.Count} identities remained after despawn");
            }

            await ScenarioBarrier.Wait(ctx, BarrierBase + cycle * 10 + 2, _barrierTimeoutSeconds);
        }

        return failures.Count == 0
            ? ScenarioResult.Ok("two pooled cycles: wrappers, stacked identities, inactive child, reordered siblings and custom payloads")
            : ScenarioResult.Fail(string.Join(" | ", failures));
    }

    private static void Verify(ScenarioContext ctx, int cycle, List<string> failures)
    {
        var spawned = MixedHierarchySerializeIdentity.Spawned;
        var root = spawned.Find(identity => identity.slot == 1);
        if (!root)
        {
            failures.Add($"cycle {cycle}: missing root identity");
            return;
        }

        var a = spawned.Find(identity => identity.slot == 3);
        var b = spawned.Find(identity => identity.slot == 6);
        var ids = new HashSet<NetworkID>();
        for (int i = 0; i < IdentityCount; i++)
        {
            var identity = spawned.Find(candidate => candidate.slot == i + 1);
            if (!identity)
            {
                failures.Add($"cycle {cycle}: missing slot {i + 1}");
                continue;
            }

            var expectedParent = i < 2 ? null : i == 4 ? a : root;
            bool correctTransform = identity.transform == root.transform;
            if (i >= 2)
            {
                var targetParent = !expectedParent ? null : ParentPaths[i].Length == 0
                    ? expectedParent.transform : expectedParent.transform.Find(ParentPaths[i]);
                correctTransform = targetParent && identity.transform.parent == targetParent;
            }
            if (!correctTransform || identity.parent != expectedParent ||
                (i == 3 && (!a || identity.transform != a.transform)))
                failures.Add($"cycle {cycle}, slot {i + 1}: wrong transform or network parent");
            if (identity.componentIndex != ComponentIndices[i])
                failures.Add($"cycle {cycle}, slot {i + 1}: component index {identity.componentIndex}, expected {ComponentIndices[i]}");
            if (!identity.id.HasValue || identity.id.Value == default || !ids.Add(identity.id.Value))
                failures.Add($"cycle {cycle}, slot {i + 1}: missing, default or duplicate network id");
            if (identity.gameObject.activeSelf != (i != 6))
                failures.Add($"cycle {cycle}, slot {i + 1}: wrong active state");

            int expectedReads = ctx.role == NetworkRole.Client ? 1 : 0;
            if (identity.DeserializeCount != expectedReads ||
                (expectedReads != 0 && identity.ReadToken != MixedHierarchySerializeIdentity.Token(i + 1)))
                failures.Add($"cycle {cycle}, slot {i + 1}: reads={identity.DeserializeCount}, token={identity.ReadToken}");
        }

        var deep = root.transform.Find("Wrapper/Deep");
        if (!deep || !a || !b || deep.childCount != 4 || deep.GetChild(1) != b.transform || deep.GetChild(2) != a.transform)
            failures.Add($"cycle {cycle}: live sibling order was not preserved");
        if (!root.transform.Find("VisualOnly/Detail/Leaf") || !root.transform.Find("Wrapper/Before") ||
            !root.transform.Find("Wrapper/After") || !root.transform.Find("Wrapper/Deep/Middle"))
            failures.Add($"cycle {cycle}: non-network visual branch was lost");
    }
}
