using System;
using Cysharp.Threading.Tasks;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Two scenes register the same asset in their own scene scoped registries. Scene A is loaded privately, so
/// only the server has it; scene B is public. An identity in B sends the asset through an RPC. Without the
/// serialization scene hint the packer would pick scene A, the first loaded scene that knows the asset, and
/// clients that only hold B would receive null. With the hint the id is scoped to B and resolves everywhere.
/// </summary>
public class SceneScopedAssetHintScenario : Scenario
{
    private const string SceneAName = "SceneScopedSharedA";
    private const string SceneAPath = "Assets/PlayModeTestsScoped/SceneScopedSharedA.unity";
    private const string SceneBName = "SceneScopedSharedB";
    private const string SceneBPath = "Assets/PlayModeTestsScoped/SceneScopedSharedB.unity";
    private const string SharedAssetName = "SceneScopedSharedText";
    private const int BarrierBase = 9600;

    [SerializeField] private float _sceneTimeoutSeconds = 30f;
    [SerializeField] private float _syncTimeoutSeconds = 20f;
    [SerializeField] private float _barrierTimeoutSeconds = 60f;

    public override void Setup(ScenarioContext ctx, NetworkManager manager)
    {
        SceneScopedAssetSender.ResetReceived();
    }

    public override async UniTask<ScenarioResult> RunScenario(ScenarioContext ctx)
    {
        var buildIndexA = SceneUtility.GetBuildIndexByScenePath(SceneAPath);
        var buildIndexB = SceneUtility.GetBuildIndexByScenePath(SceneBPath);
        if (buildIndexA < 0 || buildIndexB < 0)
            return ScenarioResult.Fail("shared asset scenes missing from build settings");

        SceneScopedAssetSender.ResetReceived();
        string failure;

        try
        {
            failure = await Run(ctx, buildIndexA, buildIndexB);
        }
        finally
        {
            if (ctx.isServer)
                await Cleanup(ctx, buildIndexA, buildIndexB);
        }

        return failure == null
            ? ScenarioResult.Ok("asset shared by a private and a public scene resolved to the sender's scene")
            : ScenarioResult.Fail(failure);
    }

    private async UniTask<string> Run(ScenarioContext ctx, int buildIndexA, int buildIndexB)
    {
        if (ctx.isServer)
        {
            var loadA = await ServerLoad(ctx, SceneAName, buildIndexA, false);
            if (loadA != null)
                return loadA;

            var loadB = await ServerLoad(ctx, SceneBName, buildIndexB, true);
            if (loadB != null)
                return loadB;
        }
        else
        {
            var loadB = await ClientWaitLoaded(ctx, buildIndexB);
            if (loadB != null)
                return loadB;

            if (IsSceneLoaded(SceneAName))
                return "client loaded the private scene; the test premise no longer holds";
        }

        var spawned = await WaitSender(ctx);
        if (spawned != null)
            return spawned;

        if (!await WaitBarrier(ctx, BarrierBase + 1))
            return "peers never reached the post-load barrier";

        if (ctx.isServer)
        {
            var send = ServerSend(ctx, buildIndexA, buildIndexB);
            if (send != null)
                return send;
        }
        else
        {
            var received = await ClientWaitReceived(ctx);
            if (received != null)
                return received;
        }

        if (!await WaitBarrier(ctx, BarrierBase + 2))
            return "peers never reached the post-rpc barrier";

        return null;
    }

    private async UniTask<string> ServerLoad(ScenarioContext ctx, string sceneName, int buildIndex, bool isPublic)
    {
        var op = ctx.networkManager.sceneModule.LoadSceneAsync(sceneName, new PurrSceneSettings
        {
            mode = LoadSceneMode.Additive,
            physicsMode = LocalPhysicsMode.None,
            isPublic = isPublic
        });

        if (op == null)
            return $"LoadSceneAsync returned null for {sceneName}";

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => op.isDone && IsNetworkSceneLoaded(ctx, buildIndex),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return $"server load timeout for {sceneName}";
        }

        return null;
    }

    private async UniTask<string> ClientWaitLoaded(ScenarioContext ctx, int buildIndexB)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => IsSceneLoaded(SceneBName) && IsNetworkSceneLoaded(ctx, buildIndexB),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return "client never saw the public scene load";
        }

        return null;
    }

    private async UniTask<string> WaitSender(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(() => FindSender() != null, _syncTimeoutSeconds, ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return "sender identity in the public scene never spawned";
        }

        return null;
    }

    private static SceneScopedAssetSender FindSender()
    {
        var senders = FindObjectsByType<SceneScopedAssetSender>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (var i = 0; i < senders.Length; i++)
        {
            var sender = senders[i];
            if (sender && sender.isSpawned && sender.gameObject.scene.name == SceneBName)
                return sender;
        }

        return null;
    }

    private static string ServerSend(ScenarioContext ctx, int buildIndexA, int buildIndexB)
    {
        var manager = ctx.networkManager;
        var scenes = manager.sceneModule;

        if (!scenes.TryGetScene(buildIndexA, out var sceneA) || !scenes.TryGetScene(buildIndexB, out var sceneB))
            return "scene ids missing on the server";

        if (!manager.TryGetScene(sceneA, out var unityA) || !manager.TryGetScene(sceneB, out var unityB))
            return "unity scenes missing on the server";

        if (!SceneRegistry<NetworkAssets>.TryGetEntries(unityA.handle, out var registriesA) || registriesA.Count != 1)
            return "private scene registry did not register on the server";

        if (!SceneRegistry<NetworkAssets>.TryGetEntries(unityB.handle, out var registriesB) || registriesB.Count != 1)
            return "public scene registry did not register on the server";

        Object asset = null;
        foreach (var candidate in registriesB[0].AllAssets)
        {
            if (candidate && candidate.name == SharedAssetName)
                asset = candidate;
        }

        if (!asset)
            return "shared asset missing from the public scene registry";

        if (!registriesA[0].TryGetIndex(asset, out _))
            return "shared asset missing from the private scene registry; both must register it";

        var resolver = manager.networkAssetResolver;

        if (!resolver.TryGetId(asset, null, out var unhinted) || !unhinted.isSceneScoped ||
            unhinted.scope.Value != sceneA)
            return $"without a hint the asset should resolve to the first loaded scene {sceneA}, got {unhinted}";

        if (!resolver.TryGetId(asset, sceneB, out var hinted) || !hinted.isSceneScoped || hinted.scope.Value != sceneB)
            return $"with a hint the asset should resolve to scene {sceneB}, got {hinted}";

        var sender = FindSender();
        if (!sender)
            return "sender identity vanished before sending";

        if (asset is not TextAsset text)
            return "shared asset is not a TextAsset";

        sender.Send(text);
        return null;
    }

    private async UniTask<string> ClientWaitReceived(ScenarioContext ctx)
    {
        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => SceneScopedAssetSender.receivedCount > 0,
                _syncTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
            return "client never received the asset RPC";
        }

        if (SceneScopedAssetSender.lastReceivedAssetName != SharedAssetName)
            return $"client resolved the shared asset as '{SceneScopedAssetSender.lastReceivedAssetName ?? "null"}'; " +
                   "the id was scoped to a scene this client does not have";

        return null;
    }

    private async UniTask Cleanup(ScenarioContext ctx, int buildIndexA, int buildIndexB)
    {
        var scenes = ctx.networkManager.sceneModule;

        if (IsNetworkSceneLoaded(ctx, buildIndexB))
            _ = scenes.UnloadSceneAsync(SceneBName);
        else if (IsSceneLoaded(SceneBName))
            _ = SceneManager.UnloadSceneAsync(SceneManager.GetSceneByName(SceneBName));

        if (IsNetworkSceneLoaded(ctx, buildIndexA))
            _ = scenes.UnloadSceneAsync(SceneAName);
        else if (IsSceneLoaded(SceneAName))
            _ = SceneManager.UnloadSceneAsync(SceneManager.GetSceneByName(SceneAName));

        try
        {
            await UniTaskUtils.WaitWithTimeout(
                () => !IsSceneLoaded(SceneAName) && !IsSceneLoaded(SceneBName)
                      && !IsNetworkSceneLoaded(ctx, buildIndexA) && !IsNetworkSceneLoaded(ctx, buildIndexB),
                _sceneTimeoutSeconds,
                ctx.cancellationToken);
        }
        catch (TimeoutException)
        {
        }
    }

    private async UniTask<bool> WaitBarrier(ScenarioContext ctx, int barrierId)
    {
        try
        {
            await ScenarioBarrier.Wait(ctx, barrierId, _barrierTimeoutSeconds);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static bool IsSceneLoaded(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded;
    }

    private static bool IsNetworkSceneLoaded(ScenarioContext ctx, int buildIndex)
    {
        return buildIndex >= 0
               && ctx.networkManager.sceneModule != null
               && ctx.networkManager.sceneModule.IsSceneLoaded(buildIndex);
    }
}
