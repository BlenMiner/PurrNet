#if ADDRESSABLES_PURRNET_SUPPORT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

public class AddressableSceneJoinTests
{
    private const string SCENE_PATH = "Assets/PlayModeTests/SceneMembershipTargetA.unity";
    private const string SCENE_GUID = "49f7594ed87e4fef9b1db3935a09333e";

    [UnityTest]
    public IEnumerator CatalogMatchedPreloadedSceneIsClaimedOnceWithoutTakingHandleOwnership()
    {
        var locator = new ResourceLocationMap(nameof(CatalogMatchedPreloadedSceneIsClaimedOnceWithoutTakingHandleOwnership));
        locator.Add(SCENE_GUID, SceneLocation(SCENE_GUID, SCENE_PATH));
        Addressables.AddResourceLocator(locator);
        var scene = SceneManager.GetSceneByPath(SCENE_PATH);
        var wasLoaded = scene.isLoaded;
        var root = new GameObject("AddressableJoinTestManager");
        root.SetActive(false);
        AsyncOperation unload = null;
        try
        {
            if (!wasLoaded)
            {
                if (Application.isPlaying)
                {
                    yield return SceneManager.LoadSceneAsync(SCENE_PATH, LoadSceneMode.Additive);
                    scene = SceneManager.GetSceneByPath(SCENE_PATH);
                }
#if UNITY_EDITOR
                else
                    scene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Additive);
#endif
            }

            var module = new ScenesModule(root.AddComponent<NetworkManager>(), null);
            var action = new LoadAddressableSceneAction
            {
                guid = SCENE_GUID,
                sceneID = new SceneID(91),
                parameters = new PurrSceneSettings { mode = LoadSceneMode.Single, isPublic = true },
                loadAdditively = true
            };
            var claimed = new HashSet<Scene>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i) != scene)
                    claimed.Add(SceneManager.GetSceneAt(i));
            var originalCount = SceneManager.sceneCount;
            var match = (Scene)Invoke(module, "FindUnclaimedAddressableSceneForJoin", action, claimed);
            Assert.That(match.handle, Is.EqualTo(scene.handle));

            claimed.Add(match);
            Assert.That(((Scene)Invoke(module, "FindUnclaimedAddressableSceneForJoin", action, claimed)).IsValid(), Is.False,
                "One locally loaded instance cannot satisfy two server scene instances.");

            Invoke(module, "RegisterAddressableSceneGuid", action.sceneID, action.guid.value);
            Invoke(module, "AddScene", match, action.parameters, action.sceneID);
            Assert.That(module.IsAddressableScene(action.sceneID), Is.True);
            Assert.That(module.TryGetSceneState(action.sceneID, out var state), Is.True);
            Assert.That(state.scene.handle, Is.EqualTo(scene.handle));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(originalCount));
            var handles = (Dictionary<SceneID, AsyncOperationHandle<SceneInstance>>)typeof(ScenesModule)
                .GetField("_addressableSceneHandles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            Assert.That(handles.ContainsKey(action.sceneID), Is.False,
                "Catalog matching must not invent or take ownership of an external load handle.");

            var freshServer = new ScenesModule(null, null);
            Invoke(freshServer, "AddScene", state.scene, state.settings, new SceneID(37));
            object[] descriptorArgs = { new SceneID(37), state, default(SceneAction) };
            Assert.That((bool)Invoke(freshServer, "TryGetBootstrapAddressableAction", descriptorArgs), Is.True);
            var descriptor = (SceneAction)descriptorArgs[2];
            Assert.That(descriptor.type, Is.EqualTo(SceneActionType.LoadAddressable));
            Assert.That(descriptor.loadAddressableSceneAction.guid.value, Is.EqualTo(SCENE_GUID));
            Assert.That(descriptor.loadAddressableSceneAction.sceneID, Is.EqualTo(new SceneID(37)));
            Assert.That(descriptor.loadAddressableSceneAction.parameters.mode, Is.EqualTo(LoadSceneMode.Single));
            Assert.That(freshServer.IsAddressableScene(new SceneID(37)), Is.True);
            Addressables.RemoveResourceLocator(locator);
            descriptorArgs[2] = default(SceneAction);
            Assert.That((bool)Invoke(freshServer, "TryGetBootstrapAddressableAction", descriptorArgs), Is.True,
                "Later joins reuse the proven GUID even if its catalog locator has since been removed.");
            Assert.That(((SceneAction)descriptorArgs[2]).loadAddressableSceneAction.guid.value, Is.EqualTo(SCENE_GUID));
            var serverHandles = (Dictionary<SceneID, AsyncOperationHandle<SceneInstance>>)typeof(ScenesModule)
                .GetField("_addressableSceneHandles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(freshServer);
            Assert.That(serverHandles, Is.Empty, "Caching scene identity must not acquire an external load handle.");
        }
        finally
        {
            Addressables.RemoveResourceLocator(locator);
            UnityEngine.Object.DestroyImmediate(root);
            if (!wasLoaded && scene.IsValid() && scene.isLoaded)
            {
                if (Application.isPlaying)
                    unload = SceneManager.UnloadSceneAsync(scene);
#if UNITY_EDITOR
                else
                    EditorSceneManager.CloseScene(scene, true);
#endif
            }
        }
        if (unload != null)
            yield return unload;
    }

    [UnityTest]
    public IEnumerator PromotedHistoryPreservesMixedBuildAndAddressableSceneOrder()
    {
        var buildScene = SceneManager.GetSceneByPath(SCENE_PATH);
        var wasLoaded = buildScene.isLoaded;
        var firstScene = SceneManager.CreateScene("PromotedFirstAddressableScene");
        var lastScene = SceneManager.CreateScene("PromotedLastAddressableScene");
        var unloads = new List<AsyncOperation>();
        try
        {
            if (!wasLoaded)
            {
                if (Application.isPlaying)
                {
                    yield return SceneManager.LoadSceneAsync(SCENE_PATH, LoadSceneMode.Additive);
                    buildScene = SceneManager.GetSceneByPath(SCENE_PATH);
                }
#if UNITY_EDITOR
                else
                    buildScene = EditorSceneManager.OpenScene(SCENE_PATH, OpenSceneMode.Additive);
#endif
            }
            var module = new ScenesModule(null, null);
            var first = new SceneID(7);
            var middle = new SceneID(3);
            var last = new SceneID(8);
            var settings = new PurrSceneSettings { mode = LoadSceneMode.Single, isPublic = true };
            Invoke(module, "AddScene", firstScene, settings, first);
            Invoke(module, "AddScene", buildScene, settings, middle);
            Invoke(module, "AddScene", lastScene, settings, last);
            Invoke(module, "RegisterAddressableSceneGuid", first, "first-addressable");
            Invoke(module, "RegisterAddressableSceneGuid", last, "last-addressable");
            var sceneActions = (HashSet<SceneID>)typeof(ScenesModule)
                .GetField("_sceneActionScenes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            sceneActions.UnionWith(new[] { first, middle, last });

            Invoke(module, "RebuildSceneHistory");

            var history = (SceneHistory)typeof(ScenesModule)
                .GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            var actions = history.GetFullHistory().actions;
            Assert.That(actions.Count, Is.EqualTo(3));
            Assert.That(actions[0].type, Is.EqualTo(SceneActionType.LoadAddressable));
            Assert.That(actions[0].loadAddressableSceneAction.sceneID, Is.EqualTo(first));
            Assert.That(actions[1].type, Is.EqualTo(SceneActionType.Load));
            Assert.That(actions[1].loadSceneAction.sceneID, Is.EqualTo(middle));
            Assert.That(actions[2].type, Is.EqualTo(SceneActionType.LoadAddressable));
            Assert.That(actions[2].loadAddressableSceneAction.sceneID, Is.EqualTo(last));
            Assert.That(actions[0].loadAddressableSceneAction.parameters.mode, Is.EqualTo(LoadSceneMode.Single));
            Assert.That(actions[0].loadAddressableSceneAction.loadAdditively, Is.True);
            Assert.That(actions[1].loadSceneAction.loadAdditively, Is.True);
            Assert.That(actions[2].loadAddressableSceneAction.loadAdditively, Is.True);
        }
        finally
        {
            foreach (var scene in new[] { firstScene, lastScene, wasLoaded ? default : buildScene })
            {
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;
                if (Application.isPlaying)
                    unloads.Add(SceneManager.UnloadSceneAsync(scene));
#if UNITY_EDITOR
                else
                    EditorSceneManager.CloseScene(scene, true);
#endif
            }
        }
        foreach (var unload in unloads)
            yield return unload;
    }

    [TestCase("SceneMembershipTargetA")]
    [TestCase("https://example.invalid/scenes/SceneMembershipTargetA.unity")]
    public void ExternalSceneMatchRejectsNamesAndOpaqueLocations(string internalId)
    {
        var guid = Guid.NewGuid().ToString("N");
        var locator = new ResourceLocationMap(nameof(ExternalSceneMatchRejectsNamesAndOpaqueLocations));
        locator.Add(guid, SceneLocation(guid, internalId));
        Addressables.AddResourceLocator(locator);
        try
        {
            object[] args = { guid, null };
            Assert.That((bool)Invoke(null, "TryGetAddressableScenePath", args), Is.False);
        }
        finally
        {
            Addressables.RemoveResourceLocator(locator);
        }
    }

    [Test]
    public void ExternalSceneMatchRejectsConflictingCatalogPaths()
    {
        var guid = Guid.NewGuid().ToString("N");
        var locator = new ResourceLocationMap(nameof(ExternalSceneMatchRejectsConflictingCatalogPaths));
        locator.Add(guid, SceneLocation(guid, SCENE_PATH));
        locator.Add(guid, SceneLocation(guid, "Assets/PlayModeTests/SceneMembershipTargetB.unity"));
        Addressables.AddResourceLocator(locator);
        try
        {
            object[] args = { guid, null };
            Assert.That((bool)Invoke(null, "TryGetAddressableScenePath", args), Is.False);
        }
        finally
        {
            Addressables.RemoveResourceLocator(locator);
        }
    }

    private static ResourceLocationBase SceneLocation(string guid, string path)
    {
        return new ResourceLocationBase(guid, path, typeof(SceneProvider).FullName, typeof(SceneInstance));
    }

    private static object Invoke(ScenesModule module, string method, params object[] args)
    {
        return typeof(ScenesModule).GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(module, args);
    }
}
#endif
