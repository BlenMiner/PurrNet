using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class OrdinarySceneJoinTests
{
    private const string SceneX = "Assets/PlayModeTests/OrdinaryJoinSceneX.unity";
    private const string SceneY = "Assets/PlayModeTests/OrdinaryJoinSceneY.unity";
    private const string SceneZ = "Assets/PlayModeTests/OrdinaryJoinSceneZ.unity";
    private readonly HashSet<Scene> _originalScenes = new();
    private Scene _originalActive;
    private GameObject _managerRoot;
    private ScenesModule _module;
    private UnityAction<Scene, LoadSceneMode> _sceneLoaded;

    [SetUp]
    public void RememberLoadedScenes()
    {
        _originalScenes.Clear();
        for (var i = 0; i < SceneManager.sceneCount; i++)
            _originalScenes.Add(SceneManager.GetSceneAt(i));
        _originalActive = SceneManager.GetActiveScene();
    }

    [UnityTest]
    public IEnumerator MissingBootstrapCompletesBeforeReusedScenesInManifestOrder()
    {
        RequirePlayModeAndUnloaded(SceneX, SceneY, SceneZ);
        var extraScene = SceneManager.CreateScene("OrdinaryJoinExtraLocalW");
        Scene retainedZ = default;
        Scene retainedY = default;
        // The client happened to load Z before Y; authority requires X, Y, Z.
        yield return LoadFixture(SceneZ, scene => retainedZ = scene);
        yield return LoadFixture(SceneY, scene => retainedY = scene);
        CreateModule();
        var idX = new SceneID(5);
        var idY = new SceneID(6);
        var idZ = new SceneID(7);
        var loadedEvents = new List<SceneID>();
        _module.onSceneLoaded += (id, asServer) =>
        {
            loadedEvents.Add(id);
            if (id == idX)
            {
                Assert.That(_module.TryGetSceneState(idY, out _), Is.False);
                Assert.That(_module.TryGetSceneState(idZ, out _), Is.False);
            }
            else if (id == idY)
            {
                Assert.That(_module.TryGetSceneState(idX, out _), Is.True);
                Assert.That(_module.TryGetSceneState(idZ, out _), Is.False);
            }
        };

        Invoke(_module, "BeginInitialSceneReconciliation", new FirstSceneActionsBatch
        {
            bootstrapScenes = new List<SceneAction> { Load(SceneX, idX, LoadSceneMode.Single) },
            actions = new List<SceneAction> { Load(SceneY, idY), Load(SceneZ, idZ) }
        });
        Invoke(_module, "ProcessInitialSceneActions");

        Assert.That(_module.TryGetSceneState(idY, out _), Is.False,
            "A retained later scene must not ACK before the missing earlier scene has loaded.");
        Assert.That(_module.TryGetSceneState(idZ, out _), Is.False);
        Assert.That(loadedEvents, Is.Empty);
        Assert.That(_module.GetPendingOperations().Count, Is.EqualTo(1));
        yield return DriveInitialReconciliation(() => loadedEvents.Count == 3);

        CollectionAssert.AreEqual(new[] { idX, idY, idZ }, loadedEvents);
        Assert.That(_module.TryGetSceneState(idX, out var stateX), Is.True);
        Assert.That(stateX.scene.path, Is.EqualTo(SceneX));
        Assert.That(stateX.settings.mode, Is.EqualTo(LoadSceneMode.Single));
        Assert.That(_module.TryGetSceneState(idY, out var stateY), Is.True);
        Assert.That(stateY.scene.handle, Is.EqualTo(retainedY.handle));
        Assert.That(_module.TryGetSceneState(idZ, out var stateZ), Is.True);
        Assert.That(stateZ.scene.handle, Is.EqualTo(retainedZ.handle));
        Assert.That(extraScene.isLoaded, Is.True, "Initial reconciliation must not unload extra local scenes.");
        Assert.That(_module.TryGetSceneID(extraScene, out _), Is.False);
        var actionScenes = ActionScenes();
        Assert.That(actionScenes.Contains(idX), Is.False, "Bootstrap provenance must survive the asynchronous load.");
        Assert.That(actionScenes.Contains(idY), Is.True);
        Assert.That(actionScenes.Contains(idZ), Is.True);
        Assert.That(_module.GetPendingOperations(), Is.Empty);
    }

    [UnityTest]
    public IEnumerator DuplicateAssetDescriptorsReserveDistinctLoadedInstances()
    {
        RequirePlayModeAndUnloaded(SceneY);
        Scene first = default;
        Scene second = default;
        yield return LoadFixture(SceneY, scene => first = scene);
        yield return LoadFixture(SceneY, scene => second = scene);
        Assert.That(second.handle, Is.Not.EqualTo(first.handle));
        var loadedSceneCount = SceneManager.sceneCount;
        CreateModule();
        var bootstrapId = new SceneID(10);
        var actionId = new SceneID(11);
        var loadedEvents = new List<SceneID>();
        _module.onSceneLoaded += (id, _) => loadedEvents.Add(id);

        Invoke(_module, "BeginInitialSceneReconciliation", new FirstSceneActionsBatch
        {
            bootstrapScenes = new List<SceneAction> { Load(SceneY, bootstrapId, LoadSceneMode.Single) },
            actions = new List<SceneAction> { Load(SceneY, actionId) }
        });
        yield return DriveInitialReconciliation(() => loadedEvents.Count == 2);

        Assert.That(_module.TryGetSceneState(bootstrapId, out var bootstrap), Is.True);
        Assert.That(_module.TryGetSceneState(actionId, out var action), Is.True);
        Assert.That(bootstrap.scene.handle, Is.Not.EqualTo(action.scene.handle));
        CollectionAssert.AreEquivalent(new[] { first.handle, second.handle },
            new[] { bootstrap.scene.handle, action.scene.handle });
        CollectionAssert.AreEqual(new[] { bootstrapId, actionId }, loadedEvents);
        Assert.That(SceneManager.sceneCount, Is.EqualTo(loadedSceneCount));
        Assert.That(_module.GetPendingOperations(), Is.Empty);
        Assert.That(ActionScenes().Contains(bootstrapId), Is.False);
        Assert.That(ActionScenes().Contains(actionId), Is.True);
    }

    [UnityTest]
    public IEnumerator TransferCancelsInitialQueueButKeepsSubmittedBootstrapLoad()
    {
        RequirePlayModeAndUnloaded(SceneX, SceneY);
        Scene retainedY = default;
        yield return LoadFixture(SceneY, scene => retainedY = scene);
        CreateModule();
        var idX = new SceneID(5);
        var idY = new SceneID(6);
        var loadedEvents = new List<SceneID>();
        _module.onSceneLoaded += (id, _) => loadedEvents.Add(id);
        Invoke(_module, "BeginInitialSceneReconciliation", new FirstSceneActionsBatch
        {
            bootstrapScenes = new List<SceneAction> { Load(SceneX, idX, LoadSceneMode.Single) },
            actions = new List<SceneAction> { Load(SceneY, idY) }
        });
        Assert.That(_module.GetPendingOperations().Count, Is.EqualTo(1));
        var submittedX = _module.GetPendingOperations()[0];
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var matches = (Dictionary<SceneID, Scene>)typeof(ScenesModule)
            .GetField("_initialSceneMatches", flags).GetValue(_module);
        Assert.That(matches[idY], Is.EqualTo(retainedY), "Y must be reserved behind the submitted X load.");

        _module.TransferToNewServer();

        Assert.That(typeof(ScenesModule).GetField("_initialSceneActions", flags).GetValue(_module), Is.Null);
        Assert.That(typeof(ScenesModule).GetField("_initialSceneMatches", flags).GetValue(_module), Is.Null);
        Assert.That(typeof(ScenesModule).GetField("_initialSceneWaitingFor", flags).GetValue(_module), Is.Null);
        Assert.That(typeof(ScenesModule).GetField("_awaitingInitialSceneManifest", flags).GetValue(_module), Is.False);
        Assert.That(_module.GetPendingOperations().Count, Is.EqualTo(1), "Unity's submitted load must still be tracked.");
        Assert.That(_module.GetPendingOperations()[0].operation, Is.SameAs(submittedX.operation));
        Assert.That(_module.TryGetSceneState(idY, out _), Is.False);
        Assert.That(_module.isTransferComplete, Is.False, "The replacement authority has not supplied a manifest yet.");

        typeof(NetworkManager).GetProperty("preserveWorldOnTransfer", flags)
            .SetValue(_managerRoot.GetComponent<NetworkManager>(), true);
        typeof(ScenesModule).GetMethod("OnSceneActionsBatch", flags, null,
                new[] { typeof(PlayerID), typeof(FirstSceneActionsBatch), typeof(bool) }, null)
            .Invoke(_module, new object[]
            {
                default(PlayerID),
                new FirstSceneActionsBatch
                {
                    bootstrapScenes = new List<SceneAction> { Load(SceneX, idX, LoadSceneMode.Single) },
                    actions = new List<SceneAction>()
                },
                false
            });
        Assert.That(_module.GetPendingOperations().Count, Is.EqualTo(1));
        Assert.That(_module.GetPendingOperations()[0].operation, Is.SameAs(submittedX.operation),
            "A bootstrap scene already loaded on the authority must reuse the client's submitted operation.");
        Assert.That(_module.GetPendingOperations()[0].discardOnCompletion, Is.False,
            "Bootstrap descriptors must participate in pending-load reconciliation.");

        var deadline = Time.realtimeSinceStartup + 20f;
        while (_module.GetPendingOperations().Count > 0 && Time.realtimeSinceStartup < deadline)
        {
            _module.FixedUpdate();
            yield return null;
        }
        Assert.That(_module.GetPendingOperations(), Is.Empty, "The submitted bootstrap load did not complete.");
        // FixedUpdate is the real path that used to resume the superseded initial queue.
        _module.FixedUpdate();
        yield return null;
        _module.FixedUpdate();

        CollectionAssert.AreEqual(new[] { idX }, loadedEvents);
        Assert.That(_module.TryGetSceneState(idX, out var stateX), Is.True);
        Assert.That(stateX.scene.path, Is.EqualTo(SceneX));
        Assert.That(stateX.settings.mode, Is.EqualTo(LoadSceneMode.Single));
        Assert.That(ActionScenes().Contains(idX), Is.False,
            "An in-flight bootstrap load must not become a history-owned scene after transfer.");
        Assert.That(_module.TryGetSceneState(idY, out _), Is.False,
            "The old authority's queued Y registration must never resume after transfer.");
        Assert.That(retainedY.isLoaded, Is.True);
        Assert.That(_module.TryGetSceneID(retainedY, out _), Is.False);
        Assert.That(_module.isTransferComplete, Is.True, "The authoritative bootstrap load has completed reconciliation.");
    }

    private void CreateModule()
    {
        _managerRoot = new GameObject("Ordinary scene reconciliation test manager");
        _managerRoot.SetActive(false);
        var manager = _managerRoot.AddComponent<NetworkManager>();
        _module = new ScenesModule(manager, null);
        _sceneLoaded = (scene, mode) => Invoke(_module, "SceneManagerOnSceneLoaded", scene, mode);
        SceneManager.sceneLoaded += _sceneLoaded;
    }

    private IEnumerator DriveInitialReconciliation(Func<bool> completed)
    {
        var deadline = Time.realtimeSinceStartup + 20f;
        while (!completed() && Time.realtimeSinceStartup < deadline)
        {
            Invoke(_module, "ProcessInitialSceneActions");
            yield return null;
        }
        Assert.That(completed(), Is.True, "Initial scene reconciliation did not finish within 20 seconds.");
    }

    private static IEnumerator LoadFixture(string path, Action<Scene> loaded)
    {
        var existingScenes = new HashSet<Scene>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
            existingScenes.Add(SceneManager.GetSceneAt(i));
        yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.path == path && !existingScenes.Contains(scene))
            {
                loaded(scene);
                yield break;
            }
        }
        Assert.Fail($"No newly loaded scene instance found for {path}.");
    }

    private static void RequirePlayModeAndUnloaded(params string[] paths)
    {
        if (!Application.isPlaying)
            Assert.Ignore("Run this fixture in PlayMode to exercise asynchronous scene loading.");
        foreach (var path in paths)
            if (SceneManager.GetSceneByPath(path).isLoaded)
                Assert.Ignore($"Fixture scene {path} was already open before this test.");
    }

    private static SceneAction Load(string path, SceneID id, LoadSceneMode mode = LoadSceneMode.Additive) => new()
    {
        type = SceneActionType.Load,
        loadSceneAction = new LoadSceneAction
        {
            sceneID = id,
            scenePathHash = PurrNet.Utils.Hasher.Hash(path),
            parameters = new PurrSceneSettings { mode = mode, isPublic = true }
        }
    };

    private HashSet<SceneID> ActionScenes() => (HashSet<SceneID>)typeof(ScenesModule)
        .GetField("_sceneActionScenes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_module);

    private static void Invoke(ScenesModule module, string name, params object[] arguments) =>
        typeof(ScenesModule).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(module, arguments);

    [UnityTearDown]
    public IEnumerator RestoreLoadedScenes()
    {
        var deadline = Time.realtimeSinceStartup + 20f;
        while (_module != null && _module.GetPendingOperations().Count > 0 && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (_sceneLoaded != null)
            SceneManager.sceneLoaded -= _sceneLoaded;
        _sceneLoaded = null;
        _module = null;
        if (_managerRoot)
            UnityEngine.Object.DestroyImmediate(_managerRoot);
        _managerRoot = null;
        if (_originalActive.IsValid() && _originalActive.isLoaded)
            SceneManager.SetActiveScene(_originalActive);
        var created = new List<Scene>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!_originalScenes.Contains(scene))
                created.Add(scene);
        }
        foreach (var scene in created)
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
    }
}
