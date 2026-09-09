using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

public class SceneReconnectReadinessTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ExistingBootstrapMembershipRequiresRemoteLoadAcknowledgement(bool isBot)
    {
        var managerRoot = new GameObject(nameof(ExistingBootstrapMembershipRequiresRemoteLoadAcknowledgement));
        managerRoot.SetActive(false);
        try
        {
            var manager = managerRoot.AddComponent<NetworkManager>();
            var scenes = new ScenesModule(manager, null);
            var membership = new ScenePlayersModule(manager, scenes, null);
            var sceneId = new SceneID(7);
            var player = new PlayerID(2, isBot);
            Invoke(scenes, "AddScene", managerRoot.scene, new PurrSceneSettings { isPublic = true }, sceneId);
            SetField(membership, "_asServer", true);
            GetScenePlayers(membership, "_scenePlayers").Add(sceneId, new List<PlayerID> { player });
            GetScenePlayers(membership, "_sceneLoadedPlayers").Add(sceneId, new List<PlayerID>());
            int loadedEvents = 0;
            membership.onPlayerLoadedScene += (_, _, _) => loadedEvents++;

            membership.AddPlayerToScene(player, sceneId);

            Assert.That(membership.IsPlayerInScene(player, sceneId), Is.True);
            Assert.That(membership.IsPlayerLoadedInScene(player, sceneId), Is.EqualTo(isBot));
            Assert.That(loadedEvents, Is.EqualTo(isBot ? 1 : 0),
                "A remote client may not have registered or loaded the authoritative bootstrap scene yet.");

            Invoke(membership, "RemoteClientLoadedScene", player,
                new ClientFinishedLoadingScene { scene = sceneId }, true);
            Assert.That(membership.IsPlayerLoadedInScene(player, sceneId), Is.True);
            Assert.That(loadedEvents, Is.EqualTo(1), "The matching ACK completes readiness exactly once.");
        }
        finally
        {
            Object.DestroyImmediate(managerRoot);
        }
    }

    private static Dictionary<SceneID, List<PlayerID>> GetScenePlayers(object target, string field) =>
        (Dictionary<SceneID, List<PlayerID>>)target.GetType()
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static void SetField(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
}
