using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using UnityEngine;

public class NetworkAnimatorPoolResetTests
{
    [Test]
    public void PoolResetDropsPreviousSpawnCommandsAndAllowsFreshWrites()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var setBool = typeof(NetworkAnimator).GetMethod("SetBool", new[] { typeof(int), typeof(bool) });
        if (setBool == null)
            Assert.Ignore("The Unity animation module is not enabled.");

        var managerRoot = new GameObject("Animator pool reset test manager");
        managerRoot.SetActive(false);
        var manager = managerRoot.AddComponent<NetworkManager>();
        typeof(NetworkManager).GetField("_clientModules", flags).SetValue(manager, new ModulesCollection(manager, false));
        typeof(NetworkManager).GetField("_serverModules", flags).SetValue(manager, new ModulesCollection(manager, true));
        var root = new GameObject("Animator pool reset test identity");
        root.SetActive(false);
        var animator = root.AddComponent<NetworkAnimator>();
        var owner = new PlayerID(2, false);
        try
        {
            typeof(NetworkIdentity).GetProperty(nameof(NetworkIdentity.networkManager)).SetValue(animator, manager);
            typeof(NetworkIdentity).GetField("_isSpawnedClient", flags).SetValue(animator, true);
            typeof(NetworkIdentity).GetField("_spawnedCount", flags).SetValue(animator, 1);
            typeof(NetworkIdentity).GetField("_localPlayer", flags).SetValue(animator, (PlayerID?)owner);
            animator.internalOwnerClient = owner;
            typeof(NetworkIdentity).GetField("_cachedHasConnectedOwner", flags).SetValue(animator, true);
            setBool.Invoke(animator, new object[] { 123, true });
            typeof(NetworkAnimator).GetMethod("SetTrigger", new[] { typeof(int) })
                .Invoke(animator, new object[] { 456 });
            var reconcilePlayers = (IList)typeof(NetworkAnimator).GetField("_reconcilePlayers", flags).GetValue(animator);
            reconcilePlayers.Add(new PlayerID(3, false));
            typeof(NetworkAnimator).GetField("_needsStateReconcile", flags).SetValue(animator, true);
            var pendingTriggers = (IList)typeof(NetworkAnimator).GetField("_pendingTriggerActions", flags).GetValue(animator);
            Assert.That(PendingCount(animator), Is.EqualTo(2), "The controller must have real queued parameter and trigger commands.");
            Assert.That(pendingTriggers.Count, Is.EqualTo(1), "An unavailable Animator keeps its trigger for later application.");

            animator.ResetIdentity();

            Assert.That(animator.isInPool, Is.True);
            Assert.That(PendingCount(animator), Is.Zero, "No command from the previous spawn may survive the pool reset.");
            Assert.That(reconcilePlayers, Is.Empty);
            Assert.That(pendingTriggers, Is.Empty);
            Assert.That(typeof(NetworkAnimator).GetField("_needsStateReconcile", flags).GetValue(animator), Is.False);
            Assert.That(typeof(NetworkAnimator).GetField("_hasPendingAnimatorParameterState", flags).GetValue(animator), Is.False);
            Assert.That(typeof(NetworkAnimator).GetMethod("GetBool", new[] { typeof(int) })
                .Invoke(animator, new object[] { 123 }), Is.True, "Resetting command queues retains the current animator parameter value.");

            animator.SetIdentity(manager, null, default, false, false);
            Assert.That(animator.isInPool, Is.False);
            typeof(NetworkIdentity).GetField("_localPlayer", flags).SetValue(animator, (PlayerID?)owner);
            animator.internalOwnerClient = owner;
            typeof(NetworkIdentity).GetField("_cachedHasConnectedOwner", flags).SetValue(animator, true);
            setBool.Invoke(animator, new object[] { 123, false });
            Assert.That(PendingCount(animator), Is.EqualTo(1), "Fresh writes in the next spawn must queue normally.");
        }
        finally
        {
            typeof(NetworkIdentity).GetField("_isSpawnedClient", flags).SetValue(animator, false);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(managerRoot);
        }
    }

    private static int PendingCount(NetworkAnimator animator)
    {
        var dirty = typeof(NetworkAnimator).GetField("_dirty", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(animator);
        return (int)dirty.GetType().GetProperty("Count").GetValue(dirty);
    }
}
