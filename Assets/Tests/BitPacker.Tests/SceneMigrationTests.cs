using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

public class SceneMigrationTests
{
    [Test]
    public void TransferDoesNotRunQueuedActionsBeforeFirstBatch()
    {
        var managerScene = SceneManager.CreateScene(nameof(TransferDoesNotRunQueuedActionsBeforeFirstBatch));
        var retainedScene = SceneManager.CreateScene("BeforeManifestRetainedScene");
        var managerRoot = new GameObject("BeforeManifestManager");
        managerRoot.SetActive(false);
        SceneManager.MoveGameObjectToScene(managerRoot, managerScene);
        try
        {
            var manager = managerRoot.AddComponent<NetworkManager>();
            var module = new ScenesModule(manager, null);
            var retainedId = new SceneID(2);
            Invoke(module, "AddScene", managerScene, new PurrSceneSettings(), new SceneID(1));
            Invoke(module, "AddScene", retainedScene, new PurrSceneSettings(), retainedId);
            var unload = new SceneAction
            {
                type = SceneActionType.Unload,
                unloadSceneAction = new UnloadSceneAction { sceneID = retainedId }
            };
            var queued = (Queue<SceneAction>)typeof(ScenesModule)
                .GetField("_actionsQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            queued.Enqueue(unload);
            module.TransferToNewServer();
            Assert.That(queued, Is.Empty, "Transfer must discard old-authority commands that have not started.");

            var unloadedEvents = 0;
            module.onSceneUnloaded += (_, _) => unloadedEvents++;
            Receive(module, new SceneActionsBatch { actions = new List<SceneAction> { unload } });
            Invoke(module, "HandleNextSceneAction");
            Assert.That(queued.Count, Is.EqualTo(1), "A delta arriving before the first batch must wait for that inventory.");
            Assert.That(module.TryGetSceneState(retainedId, out _), Is.True);
            Assert.That(unloadedEvents, Is.Zero);
            Assert.That(retainedScene.isLoaded, Is.True);
            Assert.That(module.isTransferComplete, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(managerRoot);
            CloseScene(retainedScene);
            CloseScene(managerScene);
        }
    }

    [UnityTest]
    public IEnumerator DisconnectCleanupKeepsWaitingForTransferUnloads()
    {
        var managerScene = SceneManager.CreateScene(nameof(DisconnectCleanupKeepsWaitingForTransferUnloads));
        var transferScene = SceneManager.CreateScene("TransferUnloadStillInFlight");
        var cleanupScene = SceneManager.CreateScene("DisconnectCleanupScene");
        var managerRoot = new GameObject("SharedSceneUnloadManager");
        managerRoot.SetActive(false);
        SceneManager.MoveGameObjectToScene(managerRoot, managerScene);
        try
        {
            var module = new ScenesModule(managerRoot.AddComponent<NetworkManager>(), null);
            Invoke(module, "AddScene", managerScene, new PurrSceneSettings(), new SceneID(1));
            Invoke(module, "AddScene", transferScene, new PurrSceneSettings(), new SceneID(2));
            Invoke(module, "ReconcileTransferScenes", new List<SceneAction>());
            var pending = (List<AsyncOperation>)typeof(ScenesModule)
                .GetField("_pendingUnloads", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            Assert.That(pending.Count, Is.EqualTo(1));
            var transferUnload = pending[0];
            Assert.That(transferUnload.isDone, Is.False);
            Assert.That(module.isTransferComplete, Is.False);

            // Disconnect cleanup can start while a scene omitted by the new host
            // is still unloading. Starting its own unload must retain that operation.
            Invoke(module, "AddScene", cleanupScene, new PurrSceneSettings(), new SceneID(3));
            Assert.That((bool)Invoke(module, "UnloadAllScenesCleanup", true), Is.False);
            Assert.That(pending, Does.Contain(transferUnload));
            Assert.That(pending.Count, Is.EqualTo(2));
            var cleanupUnload = pending[1];
            yield return transferUnload;
            yield return cleanupUnload;

            Assert.That(module.isTransferComplete, Is.True);
            Assert.That(pending, Is.Empty, "Completed operations must not survive readiness polling.");
            Assert.That((bool)Invoke(module, "UnloadAllScenesCleanup", true), Is.True);
        }
        finally
        {
            Object.DestroyImmediate(managerRoot);
            if (transferScene.IsValid() && transferScene.isLoaded)
                CloseScene(transferScene);
            if (cleanupScene.IsValid() && cleanupScene.isLoaded)
                CloseScene(cleanupScene);
            CloseScene(managerScene);
        }
    }

    [Test]
    public void FirstBatchWaitsForSubmittedSingleLoadAndBuffersLaterDeltas()
    {
        var scene = SceneManager.CreateScene(nameof(FirstBatchWaitsForSubmittedSingleLoadAndBuffersLaterDeltas));
        var managerRoot = new GameObject("MigrationManifestManager");
        managerRoot.SetActive(false);
        SceneManager.MoveGameObjectToScene(managerRoot, scene);
        try
        {
            var manager = managerRoot.AddComponent<NetworkManager>();
            typeof(NetworkManager).GetProperty("preserveWorldOnTransfer", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, true);
            var module = new ScenesModule(manager, null);
            var id = new SceneID(1);
            Invoke(module, "AddScene", scene, new PurrSceneSettings { isPublic = true }, id);
            module.TransferToNewServer();
            var loadedEvents = 0;
            module.onSceneLoaded += (_, _) => loadedEvents++;
            var pending = (List<PendingSceneOperation>)module.GetPendingOperations();
            pending.Add(new PendingSceneOperation
            {
                idToAssign = new SceneID(99),
                settings = new PurrSceneSettings { mode = LoadSceneMode.Single }
            });
            Receive(module, new FirstSceneActionsBatch
            {
                actions = new List<SceneAction>(),
                bootstrapScenes = new List<SceneAction>
                {
                    new SceneAction
                    {
                        type = SceneActionType.Load,
                        loadSceneAction = new LoadSceneAction { sceneID = id }
                    }
                }
            });
            Assert.That(module.isTransferComplete, Is.False, "A submitted Single load must finish before reconciliation.");
            Assert.That(loadedEvents, Is.Zero, "The first batch cannot acknowledge retained scenes while Single may still replace them.");

            var laterId = new SceneID(2);
            Receive(module, new SceneActionsBatch
            {
                actions = new List<SceneAction>
                {
                    new SceneAction { type = SceneActionType.Unload, unloadSceneAction = new UnloadSceneAction { sceneID = id } },
                    new SceneAction { type = SceneActionType.Load, loadSceneAction = new LoadSceneAction { sceneID = laterId } }
                }
            });
            var queued = (Queue<SceneAction>)typeof(ScenesModule)
                .GetField("_actionsQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            Assert.That(queued.Count, Is.Zero, "Later deltas must remain behind the pending first-batch reconciliation.");
            Assert.That(module.TryGetSceneState(id, out _), Is.True);
            Assert.That(loadedEvents, Is.Zero);

            // Model the already-submitted operation completing without launching a
            // destructive Single load against the test runner's own scene.
            pending.Clear();
            Invoke(module, "TryApplyTransferManifest");
            Assert.That(loadedEvents, Is.EqualTo(1), "The first batch must restore bootstrap authority before later deltas.");
            Assert.That(module.TryGetSceneState(id, out _), Is.True);
            var laterActions = queued.ToArray();
            Assert.That(laterActions.Length, Is.EqualTo(2));
            Assert.That(laterActions[0].type, Is.EqualTo(SceneActionType.Unload));
            Assert.That(laterActions[0].unloadSceneAction.sceneID, Is.EqualTo(id));
            Assert.That(laterActions[1].type, Is.EqualTo(SceneActionType.Load));
            Assert.That(laterActions[1].loadSceneAction.sceneID, Is.EqualTo(laterId));
            Assert.That(module.isTransferComplete, Is.False, "Queued deltas remain part of transfer readiness.");
            Assert.That(scene.isLoaded, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(managerRoot);
            CloseScene(scene);
        }
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public void BootstrapRegistrationFollowsAuthorityOnlyForPreservedTransfer(bool preserveWorld, bool authoritative)
    {
        var scene = SceneManager.CreateScene($"{nameof(BootstrapRegistrationFollowsAuthorityOnlyForPreservedTransfer)}_{preserveWorld}_{authoritative}");
        var managerRoot = new GameObject("MigrationBootstrapManager");
        managerRoot.SetActive(false);
        SceneManager.MoveGameObjectToScene(managerRoot, scene);
        try
        {
            var manager = managerRoot.AddComponent<NetworkManager>();
            typeof(NetworkManager).GetProperty("preserveWorldOnTransfer", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(manager, preserveWorld);
            var module = new ScenesModule(manager, null);
            var id = new SceneID(1);
            Invoke(module, "AddScene", scene,
                new PurrSceneSettings { mode = LoadSceneMode.Single, isPublic = true }, id);
            var bootstrapScenes = new List<SceneAction>();
            if (authoritative)
                bootstrapScenes.Add(new SceneAction
                {
                    type = SceneActionType.Load,
                    loadSceneAction = new LoadSceneAction { sceneID = id }
                });
            var loadedEvents = 0;
            var unloadedEvents = 0;
            module.onSceneLoaded += (_, _) => loadedEvents++;
            module.onSceneUnloaded += (_, _) => unloadedEvents++;

            module.TransferToNewServer();
            var queued = (Queue<SceneAction>)typeof(ScenesModule)
                .GetField("_actionsQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            queued.Enqueue(new SceneAction { type = SceneActionType.Unload, unloadSceneAction = new UnloadSceneAction { sceneID = id } });
            Receive(module, new FirstSceneActionsBatch
            {
                actions = new List<SceneAction>(),
                bootstrapScenes = bootstrapScenes
            });

            var shouldRetain = !preserveWorld || authoritative;
            Assert.That(module.TryGetSceneState(id, out _), Is.EqualTo(shouldRetain));
            Assert.That(unloadedEvents, Is.EqualTo(shouldRetain ? 0 : 1));
            Assert.That(loadedEvents, Is.EqualTo(shouldRetain ? 1 : 0),
                "Both transfer modes must acknowledge retained bootstrap scenes after reconciliation.");
            Assert.That(scene.isLoaded, Is.True, "The live manager scene must stay loaded even if its old network registration is absent.");
            Assert.That(module.isTransferComplete, Is.True);
            Assert.That(queued, Is.Empty, "The new first batch supersedes the old authority's queued actions.");
        }
        finally
        {
            Object.DestroyImmediate(managerRoot);
            CloseScene(scene);
        }
    }

#if UNITY_EDITOR
    [UnityTest]
    public IEnumerator ReconcileMissingSingleKeepsLoadedAdditiveInstance()
    {
        const string missingPath = "Assets/PlayModeTests/SceneMembershipTargetA.unity";
        const string retainedPath = "Assets/PlayModeTests/SceneMembershipTargetB.unity";
        if (SceneManager.GetSceneByPath(missingPath).isLoaded)
            Assert.Ignore("The missing-scene fixture is already open in the editor.");

        var previousActive = SceneManager.GetActiveScene();
        var managerScene = SceneManager.CreateScene(nameof(ReconcileMissingSingleKeepsLoadedAdditiveInstance));
        var managerRoot = new GameObject("MigrationTestManager");
        managerRoot.SetActive(false);
        SceneManager.MoveGameObjectToScene(managerRoot, managerScene);
        var retained = SceneManager.GetSceneByPath(retainedPath);
        var retainedWasLoaded = retained.isLoaded;
        try
        {
            if (!retainedWasLoaded)
            {
                if (Application.isPlaying)
                {
                    yield return SceneManager.LoadSceneAsync(retainedPath, LoadSceneMode.Additive);
                    retained = SceneManager.GetSceneByPath(retainedPath);
                }
                else
                    retained = EditorSceneManager.OpenScene(retainedPath, OpenSceneMode.Additive);
            }
            var originalHandle = retained.handle;
            var manager = managerRoot.AddComponent<NetworkManager>();
            var module = new ScenesModule(manager, null);
            Invoke(module, "AddScene", retained,
                new PurrSceneSettings { mode = LoadSceneMode.Additive, isPublic = true }, new SceneID(91));

            Invoke(module, "ReconcileTransferScenes", new List<SceneAction>
            {
                new SceneAction
                {
                    type = SceneActionType.Load,
                    loadSceneAction = new LoadSceneAction
                    {
                        sceneID = new SceneID(90),
                        scenePathHash = PurrNet.Utils.Hasher.Hash(missingPath),
                        parameters = new PurrSceneSettings { mode = LoadSceneMode.Single, isPublic = true }
                    }
                },
                new SceneAction
                {
                    type = SceneActionType.Load,
                    loadSceneAction = new LoadSceneAction
                    {
                        sceneID = new SceneID(91),
                        scenePathHash = PurrNet.Utils.Hasher.Hash(retainedPath),
                        parameters = new PurrSceneSettings { mode = LoadSceneMode.Additive, isPublic = true }
                    }
                }
            });

            Assert.That(module.TryGetSceneState(new SceneID(91), out var state), Is.True);
            Assert.That(state.scene.handle, Is.EqualTo(originalHandle));
            Assert.That(state.scene.isLoaded, Is.True);
            var queued = (Queue<SceneAction>)typeof(ScenesModule)
                .GetField("_actionsQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
            Assert.That(queued.Count, Is.EqualTo(1));
            var missing = queued.Peek().loadSceneAction;
            Assert.That(missing.sceneID, Is.EqualTo(new SceneID(90)));
            Assert.That(missing.GetLoadSceneParameters().loadSceneMode, Is.EqualTo(LoadSceneMode.Additive));
            Assert.That(missing.parameters.mode, Is.EqualTo(LoadSceneMode.Single));
            Assert.That(module.isTransferComplete, Is.False);
        }
        finally
        {
            Object.DestroyImmediate(managerRoot);
            if (!retainedWasLoaded && retained.IsValid())
                CloseScene(retained);
            CloseScene(managerScene);
            if (previousActive.IsValid() && previousActive.isLoaded)
                SceneManager.SetActiveScene(previousActive);
        }
    }
#endif

    [Test]
    public void ManifestKeepsBuildSceneWhenSingleAddressableAppearsLater()
    {
        var history = new SceneHistory();
        history.AddLoadAction(new LoadSceneAction
        {
            sceneID = new SceneID(4),
            parameters = new PurrSceneSettings { mode = LoadSceneMode.Additive },
            loadAdditively = true
        });
        history.AddLoadAddressableAction(new LoadAddressableSceneAction
        {
            sceneID = new SceneID(3),
            guid = "addressable-scene",
            parameters = new PurrSceneSettings { mode = LoadSceneMode.Single },
            loadAdditively = true
        });

        history.Flush();

        Assert.That(history.GetFullHistory().actions.Count, Is.EqualTo(2));
        Assert.That(history.GetFullHistory().actions[1].loadAddressableSceneAction.parameters.mode,
            Is.EqualTo(LoadSceneMode.Single));
    }

    [Test]
    public void OrdinarySingleLoadStillReplacesEarlierHistory()
    {
        var history = new SceneHistory();
        history.AddLoadAction(new LoadSceneAction
        {
            sceneID = new SceneID(3),
            parameters = new PurrSceneSettings { mode = LoadSceneMode.Additive }
        });
        history.AddLoadAction(new LoadSceneAction
        {
            sceneID = new SceneID(4),
            parameters = new PurrSceneSettings { mode = LoadSceneMode.Single }
        });
        history.Flush();
        Assert.That(history.GetFullHistory().actions.Count, Is.EqualTo(1));
        Assert.That(history.GetFullHistory().actions[0].loadSceneAction.sceneID, Is.EqualTo(new SceneID(4)));
    }

    [Test]
    public void ManifestLoadsAdditivelyWithoutChangingDeclaredPhysicsOrMode()
    {
        var action = new LoadSceneAction
        {
            parameters = new PurrSceneSettings { mode = LoadSceneMode.Single, physicsMode = LocalPhysicsMode.Physics3D },
            loadAdditively = true
        };
        Assert.That(action.GetLoadSceneParameters().loadSceneMode, Is.EqualTo(LoadSceneMode.Additive));
        Assert.That(action.GetLoadSceneParameters().localPhysicsMode, Is.EqualTo(LocalPhysicsMode.Physics3D));
        Assert.That(action.parameters.mode, Is.EqualTo(LoadSceneMode.Single));
        var addressable = new LoadAddressableSceneAction { parameters = action.parameters, loadAdditively = true };
        Assert.That(addressable.GetLoadSceneParameters().loadSceneMode, Is.EqualTo(LoadSceneMode.Additive));
        Assert.That(addressable.parameters.mode, Is.EqualTo(LoadSceneMode.Single));
    }

    [Test]
    public void ReceivedSceneRegistrationAdvancesAllocator()
    {
        var scene = SceneManager.CreateScene(nameof(ReceivedSceneRegistrationAdvancesAllocator));
        try
        {
            var module = new ScenesModule(null, null);
            Invoke(module, "AddScene", scene, new PurrSceneSettings(), new SceneID(90));
            Assert.That((SceneID)Invoke(module, "GetNextID"), Is.EqualTo(new SceneID(91)));
        }
        finally { CloseScene(scene); }
    }

    [Test]
    public void AllocatorDoesNotReusePendingSceneId()
    {
        var module = new ScenesModule(null, null);
        var pending = (List<PendingSceneOperation>)module.GetPendingOperations();
        pending.Add(new PendingSceneOperation { idToAssign = new SceneID(1) });
        Assert.That((SceneID)Invoke(module, "GetNextID"), Is.EqualTo(new SceneID(2)));
    }

    [Test]
    public void BuildSceneFallbackDoesNotReuseClaimedInstance()
    {
        var claimed = new HashSet<Scene>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
            claimed.Add(SceneManager.GetSceneAt(i));
        var first = SceneManager.CreateScene("MigrationRepeatedSceneA");
        var second = SceneManager.CreateScene("MigrationRepeatedSceneB");
        try
        {
            var module = new ScenesModule(null, null);
            claimed.Add(first);
            var match = (Scene)Invoke(module, "FindUnclaimedBuildScene", -1, new PurrSceneSettings(), claimed);
            Assert.That(match, Is.EqualTo(second));
            claimed.Add(match);
            Assert.That(((Scene)Invoke(module, "FindUnclaimedBuildScene", -1, new PurrSceneSettings(), claimed)).IsValid(), Is.False);
        }
        finally
        {
            CloseScene(first);
            CloseScene(second);
        }
    }

    private static object Invoke(ScenesModule module, string name, params object[] args)
    {
        return typeof(ScenesModule).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(module, args);
    }

    private static void Receive<T>(ScenesModule module, T batch)
    {
        typeof(ScenesModule).GetMethod("OnSceneActionsBatch", BindingFlags.NonPublic | BindingFlags.Instance, null,
                new[] { typeof(PlayerID), typeof(T), typeof(bool) }, null)
            .Invoke(module, new object[] { default(PlayerID), batch, false });
    }

    private static void CloseScene(Scene scene)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorSceneManager.CloseScene(scene, true);
            return;
        }
#endif
        SceneManager.UnloadSceneAsync(scene);
    }
}
