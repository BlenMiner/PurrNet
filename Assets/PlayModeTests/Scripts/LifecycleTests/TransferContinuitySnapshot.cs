using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine.SceneManagement;

/// <summary>Checks that migration retained the scene and the original Unity identity instances.</summary>
internal sealed class TransferContinuitySnapshot
{
    private readonly Scene _scene;
    private readonly List<(NetworkIdentity identity, GlobalNetworkID id)> _identities = new();

    private TransferContinuitySnapshot(Scene scene)
    {
        _scene = scene;
    }

    public static TransferContinuitySnapshot Capture<TRoot, TChild>(string sceneName)
        where TRoot : NetworkIdentity
        where TChild : NetworkIdentity
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        var snapshot = new TransferContinuitySnapshot(scene);
        if (!scene.IsValid() || !scene.isLoaded)
            return snapshot;

        foreach (var root in scene.GetRootGameObjects())
        foreach (var identity in root.GetComponentsInChildren<NetworkIdentity>(true))
        {
            if ((identity is TRoot || identity is TChild) && identity.isSpawned && identity.id.HasValue)
                snapshot._identities.Add((identity, new GlobalNetworkID(identity.sceneId, identity.id.Value)));
        }

        return snapshot;
    }

    public ScenarioResult Verify(string phase, bool asServer = false)
    {
        if (_identities.Count == 0)
            return ScenarioResult.Fail($"{phase}: no identities were captured before transfer");
        if (!_scene.IsValid() || !_scene.isLoaded)
            return ScenarioResult.Fail($"{phase}: the original Unity scene was unloaded");

        foreach (var (identity, id) in _identities)
        {
            if (!identity)
                return ScenarioResult.Fail($"{phase}: an original Unity identity was destroyed");
            if (identity.gameObject.scene.handle != _scene.handle)
                return ScenarioResult.Fail($"{phase}: {identity.name} moved out of the original scene");
            if (!identity.id.HasValue || !id.Equals(new GlobalNetworkID(identity.sceneId, identity.id.Value)))
                return ScenarioResult.Fail($"{phase}: {identity.name} changed its network id");
            if (!identity.IsSpawned(asServer))
                return ScenarioResult.Fail($"{phase}: {identity.name} did not complete its network spawn");
        }

        return ScenarioResult.Ok();
    }
}

internal static class PostMigrationSceneCycle
{
    public static async UniTask<ScenarioResult> RunServer(
        ScenarioContext ctx, string retainedSceneName, string newSceneName, float timeout,
        Func<int> loadedCount, Func<int> unloadedCount, int expectedClients)
    {
        var scenes = ctx.networkManager.sceneModule;
        var retainedScene = SceneManager.GetSceneByName(retainedSceneName);
        if (!scenes.TryGetSceneID(retainedScene, out var retainedId))
            return ScenarioResult.Fail("post-migration scene cycle: retained scene registration missing");
        var originalScenes = new Dictionary<SceneID, Scene>();
        foreach (var (id, state) in scenes.sceneStates)
            originalScenes.Add(id, state.scene);

        var load = scenes.LoadSceneAsync(newSceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = true
        });
        if (load == null)
            return ScenarioResult.Fail("post-migration scene cycle: new scene load did not start");

        try
        {
            await UniTaskUtils.WaitWithTimeout(() => load.isDone && IsRegistered(ctx, newSceneName), timeout, ctx.cancellationToken);
            var newScene = SceneManager.GetSceneByName(newSceneName);
            if (!scenes.TryGetSceneID(newScene, out var newId) || originalScenes.ContainsKey(newId))
                return ScenarioResult.Fail("post-migration scene cycle: new scene reused an existing scene id");

            await UniTaskUtils.WaitWithTimeout(() => loadedCount() >= expectedClients, timeout, ctx.cancellationToken);
            var unload = scenes.UnloadSceneAsync(newScene);
            if (unload == null)
                return ScenarioResult.Fail("post-migration scene cycle: new scene unload did not start");

            await UniTaskUtils.WaitWithTimeout(
                () => unload.isDone && !IsLoaded(newSceneName) && unloadedCount() >= expectedClients,
                timeout, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail($"post-migration scene cycle timed out: loaded={loadedCount()}/{expectedClients}, unloaded={unloadedCount()}/{expectedClients}");
        }

        if (!retainedScene.IsValid() || !retainedScene.isLoaded ||
            !scenes.TryGetSceneID(retainedScene, out var currentId) || !currentId.Equals(retainedId))
            return ScenarioResult.Fail("post-migration scene cycle changed the retained scene registration");
        foreach (var (id, scene) in originalScenes)
        {
            if (!scene.IsValid() || !scene.isLoaded ||
                !scenes.TryGetSceneID(scene, out currentId) || !currentId.Equals(id))
                return ScenarioResult.Fail("post-migration scene cycle changed an existing scene registration");
        }

        return ScenarioResult.Ok();
    }

    public static async UniTask<ScenarioResult> RunClient(
        ScenarioContext ctx, string retainedSceneName, string newSceneName, float timeout,
        Action signalLoaded, Action signalUnloaded)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => IsRegistered(ctx, newSceneName), timeout, ctx.cancellationToken);
            var scenes = ctx.networkManager.sceneModule;
            if (!scenes.TryGetSceneID(SceneManager.GetSceneByName(retainedSceneName), out var retainedId) ||
                !scenes.TryGetSceneID(SceneManager.GetSceneByName(newSceneName), out var newId) ||
                newId.Equals(retainedId))
                return ScenarioResult.Fail("post-migration scene cycle: client scene ids overlap or are missing");

            signalLoaded();
            await UniTaskUtils.WaitWithTimeout(() => !IsLoaded(newSceneName), timeout, ctx.cancellationToken);
            if (!IsRegistered(ctx, retainedSceneName))
                return ScenarioResult.Fail("post-migration scene cycle: retained client scene was removed");
            signalUnloaded();
        }
        catch (TimeoutException)
        {
            return ScenarioResult.Fail("post-migration scene cycle timed out on transferred client");
        }

        return ScenarioResult.Ok();
    }

    private static bool IsRegistered(ScenarioContext ctx, string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded && ctx.networkManager.sceneModule.TryGetSceneID(scene, out _);
    }

    private static bool IsLoaded(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded;
    }
}
