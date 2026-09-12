using System;
using System.Collections.Generic;
#if UNITASK_PURRNET_SUPPORT
using Cysharp.Threading.Tasks;
#else
using System.Threading.Tasks;
#endif
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PurrNet
{
    [UsedByIL]
    public static class UnityProxy
    {
        public delegate void AsyncInstantiateCompleted(Object original, Object instance);

        /// <summary>
        /// Invoked once for each network prefab instance successfully produced by
        /// <c>Object.InstantiateAsync</c>. Async instantiation always bypasses PurrNet pooling.
        /// </summary>
        public static event AsyncInstantiateCompleted onAsyncInstantiateCompleted;

        static GameObject GetGameObject<T>(T obj) where T : Object
        {
            return obj switch
            {
                Component component => component.gameObject,
                GameObject gameObject => gameObject,
                _ => null
            };
        }

        static T OnPreInstantiate<T>(PrefabData prefabData, InstantiateData<T> instantiateData, bool notifyCreated = true)
            where T : Object
        {
            var prefab = prefabData.prefab;

            if (!instantiateData.TryGetHierarchy(out var hierarchy))
                return null;

            if (!prefabData.pooled)
            {
                var instance = instantiateData.Instantiate();
                if (notifyCreated)
                {
                    var go = GetGameObject(instance);
                    PurrNetGameObjectUtils.NotifyGameObjectCreated(go, prefab);
                }
                return instance;
            }

            if (!HierarchyPool.TryGetPrefabPrototype(prefabData.prefabId, out var prototype))
            {
                var instance = instantiateData.Instantiate();
                if (notifyCreated)
                {
                    var go = GetGameObject(instance);
                    PurrNetGameObjectUtils.NotifyGameObjectCreated(go, prefab);
                }
                return instance;
            }

            prototype.position = instantiateData.position;
            prototype.rotation = instantiateData.rotation;

            var result = hierarchy.CreatePrototype(prototype, null);

            if (!result)
            {
                PurrLogger.LogError($"Failed to create prototype for `{prefab.name}`.\n" +
                                    "This usually happens when the provided prefab is invalid.");
                return null;
            }

            instantiateData.ApplyToExisting(result, prefab);

            if (notifyCreated)
                PurrNetGameObjectUtils.NotifyGameObjectCreated(result, prefab);

            if (result.TryGetComponent(out T component))
                return component;

            return (T)(Object)result;
        }

        static bool OnDestroy(Object instance)
        {
            if (ApplicationContext.isQuitting)
                return true;

            if (instance is not NetworkIdentity &&
                instance is not GameObject)
                return true;

            var go = GetGameObject(instance);

            if (!go)
                return true;

            if (!go.GetComponentInChildren<NetworkIdentity>())
                return true;

            var identities = ListPool<NetworkIdentity>.Instantiate();
            go.GetComponentsInChildren(true, identities);

            for (var i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                identity.Despawn();
            }

            ListPool<NetworkIdentity>.Destroy(identities);

            bool shouldDestroy = !go.GetComponent<NetworkIdentity>();
            return shouldDestroy;
        }

        static readonly HashSet<string> _warnedUnresolvedPrefabs = new();

        static bool TryGetPrefabData(Object prefab, out PrefabData prefabData)
        {
            var prefabGo = GetGameObject(prefab);

            if (!prefabGo)
            {
                prefabData = default;
                return false;
            }

            var manager = NetworkManager.main;

            if (!manager)
            {
                prefabData = default;
                return false;
            }

            if (manager.prefabResolver.TryGetPrefabData(prefabGo, out prefabData))
                return true;

            if (prefabGo.GetComponentInChildren<NetworkIdentity>(true) &&
                _warnedUnresolvedPrefabs.Add(prefabGo.name))
            {
                PurrLogger.LogWarning(
                    $"Instantiating `{prefabGo.name}` without networking: it has a NetworkIdentity but isn't a registered network prefab, so it won't be spawned.\n" +
                    "If this prefab is registered, the given reference is a different copy of the asset. " +
                    "This commonly happens when an addressable scene references a non-addressable prefab, duplicating it into the scene bundle; " +
                    "make the prefab addressable and register it via AddressableNetworkPrefabs.", prefabGo);
            }

            return false;
        }

#if PURRNET_UNITY_INSTANTIATE_ASYNC
        static AsyncInstantiateOperation<T> TrackAsyncInstantiate<T>(
            T original,
            AsyncInstantiateOperation<T> operation)
            where T : Object
        {
            // Non-network prefabs are a pure native pass-through. Network prefabs deliberately
            // do not consult HierarchyPool: choosing InstantiateAsync opts this instance out of pooling.
            if (!TryGetPrefabData(original, out _))
                return operation;

            operation.completed += _ =>
            {
                T[] results;
                try
                {
                    results = operation.Result;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    // Cancellation/failure can leave the operation without readable results.
                    PurrLogger.LogException(e);
                    return;
                }

                if (results == null)
                    return;

                for (var i = 0; i < results.Length; i++)
                {
                    var instance = results[i];
                    if (instance)
                        onAsyncInstantiateCompleted?.Invoke(original, instance);
                }
            };

            return operation;
        }

        /// <summary>
        /// Native async instantiation without registering a local-spawn notification.
        /// Intended for replicated receiver-side creation.
        /// </summary>
        public static AsyncInstantiateOperation<T> InstantiateAsyncDirectly<T>(T original)
            where T : Object
            => Object.InstantiateAsync(original);

        public static AsyncInstantiateOperation<T> InstantiateAsyncDirectly<T>(
            T original,
            Vector3 position,
            Quaternion rotation)
            where T : Object
            => Object.InstantiateAsync(original, position, rotation);

        public static AsyncInstantiateOperation<T> InstantiateAsyncDirectly<T>(T original, Transform parent)
            where T : Object
            => Object.InstantiateAsync(original, parent);

        public static AsyncInstantiateOperation<T> InstantiateAsyncDirectly<T>(
            T original,
            Transform parent,
            Vector3 position,
            Quaternion rotation)
            where T : Object
            => Object.InstantiateAsync(original, parent, position, rotation);

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, Transform parent)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, parent));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            Vector3 position,
            Quaternion rotation)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, position, rotation));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            Transform parent,
            Vector3 position,
            Quaternion rotation)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, parent, position, rotation));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, count));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, int count, Transform parent)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, count, parent));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            Vector3 position,
            Quaternion rotation)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, count, position, rotation));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            ReadOnlySpan<Vector3> positions,
            ReadOnlySpan<Quaternion> rotations)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, count, positions, rotations));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            Transform parent,
            Vector3 position,
            Quaternion rotation)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, count, parent, position, rotation));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            Transform parent,
            ReadOnlySpan<Vector3> positions,
            ReadOnlySpan<Quaternion> rotations)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, count, parent, positions, rotations));

#if PURRNET_UNITY_INSTANTIATE_ASYNC_CANCELLATION
        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            System.Threading.CancellationToken cancellationToken)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, count, parent, position, rotation, cancellationToken));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            Transform parent,
            ReadOnlySpan<Vector3> positions,
            ReadOnlySpan<Quaternion> rotations,
            System.Threading.CancellationToken cancellationToken)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, count, parent, positions, rotations, cancellationToken));
#endif

#if PURRNET_UNITY_INSTANTIATE_ASYNC_PARAMETERS_CANCELLATION
        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            InstantiateParameters parameters,
            System.Threading.CancellationToken cancellationToken = default)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, parameters, cancellationToken));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            InstantiateParameters parameters,
            System.Threading.CancellationToken cancellationToken = default)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, count, parameters, cancellationToken));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            Vector3 position,
            Quaternion rotation,
            InstantiateParameters parameters,
            System.Threading.CancellationToken cancellationToken = default)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, position, rotation, parameters, cancellationToken));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            Vector3 position,
            Quaternion rotation,
            InstantiateParameters parameters,
            System.Threading.CancellationToken cancellationToken = default)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, count, position, rotation, parameters, cancellationToken));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            ReadOnlySpan<Vector3> positions,
            ReadOnlySpan<Quaternion> rotations,
            InstantiateParameters parameters,
            System.Threading.CancellationToken cancellationToken = default)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, count, positions, rotations, parameters, cancellationToken));
#endif

#if PURRNET_UNITY_INSTANTIATE_ASYNC_PARAMETERS_LEGACY
        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(T original, InstantiateParameters parameters)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, parameters));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            InstantiateParameters parameters)
            where T : Object
            => TrackAsyncInstantiate(original, Object.InstantiateAsync(original, count, parameters));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            Vector3 position,
            Quaternion rotation,
            InstantiateParameters parameters)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, position, rotation, parameters));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            Vector3 position,
            Quaternion rotation,
            InstantiateParameters parameters)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, count, position, rotation, parameters));

        [UsedByIL]
        public static AsyncInstantiateOperation<T> InstantiateAsync<T>(
            T original,
            int count,
            ReadOnlySpan<Vector3> positions,
            ReadOnlySpan<Quaternion> rotations,
            InstantiateParameters parameters)
            where T : Object
            => TrackAsyncInstantiate(original,
                Object.InstantiateAsync(original, count, positions, rotations, parameters));
#endif
#endif

        [UsedByIL]
        public static void DontDestroyOnLoadDirectly(Object target) => Object.DontDestroyOnLoad(target);

        [UsedByIL]
        public static void DontDestroyOnLoad(Object target)
        {
            Object.DontDestroyOnLoad(target);

            if (!target)
                return;

            var go = GetGameObject(target);

            if (!go || go.transform.parent)
                return;

            bool isNetworked = go.GetComponentInChildren<NetworkIdentity>() != null;

            if (!isNetworked)
            {
                DontDestroyOnLoadDirectly(target);
                return;
            }

            int sceneBuildIndex = go.scene.buildIndex;
            UnityLatestUpdate.ExecuteAsap(() =>
            {
                if (go)
                    go.transform.SetAsLastSibling();
            }, sceneBuildIndex, go.transform.GetSiblingIndex());
        }


        [UsedByIL]
        public static Object InstantiateDirectly(Object original) => Object.Instantiate(original);

        public static Object InstantiateDirectlyFromPool(Object original)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original);
            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original), false);
        }

        [UsedByIL]
        public static Object Instantiate(Object original)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original);
            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original));
        }

        [UsedByIL]
        public static Object Instantiate(Object original, Transform parent, bool instantiateInWorldSpace)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parent, instantiateInWorldSpace);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, parent, instantiateInWorldSpace));
        }

        public static Object InstantiateDirectly(Object original, Transform parent, bool instantiateInWorldSpace)
            => Object.Instantiate(original, parent, instantiateInWorldSpace);

        public static Object InstantiateDirectlyFromPool(Object original, Transform parent, bool instantiateInWorldSpace)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parent, instantiateInWorldSpace);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, parent, instantiateInWorldSpace),
                false);
        }

        [UsedByIL]
        public static Object Instantiate(Object original, Vector3 position, Quaternion rotation)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, position, rotation);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, position, rotation));
        }

        public static Object InstantiateDirectly(Object original, Vector3 position, Quaternion rotation)
            => Object.Instantiate(original, position, rotation);

        public static Object InstantiateDirectlyFromPool(Object original, Vector3 position, Quaternion rotation)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, position, rotation);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, position, rotation), false);
        }

        [UsedByIL]
        public static Object Instantiate(
            Object original,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, position, rotation, parent);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, position, rotation, parent));
        }

        public static Object InstantiateDirectly(
            Object original,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            return Object.Instantiate(original, position, rotation, parent);
        }

        public static Object InstantiateDirectlyFromPool(
            Object original,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, position, rotation, parent);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, position, rotation, parent),
                false);
        }

#if UNITY_6000_0_OR_NEWER
        [UsedByIL]
        public static Object Instantiate(Object original, Scene scene)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, scene);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, scene));
        }

        public static Object InstantiateDirectly(Object original, Scene scene)
            => Object.Instantiate(original, scene);

        public static Object InstantiateDirectlyFromPool(Object original, Scene scene)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, scene);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, scene), false);
        }

        public static T InstantiateDirectly<T>(T original, Scene scene) where T : Object
            => (T)Object.Instantiate(original, scene);

        public static T InstantiateDirectlyFromPool<T>(T original, Scene scene) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return InstantiateDirectly(original, scene);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, scene), false);
        }
#endif

        [UsedByIL]
        public static Object Instantiate(Object original, Transform parent)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parent);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, parent));
        }

        public static Object InstantiateDirectly(Object original, Transform parent)
            => Object.Instantiate(original, parent);

        public static Object InstantiateDirectlyFromPool(Object original, Transform parent)
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parent);

            return OnPreInstantiate(prefabData, new InstantiateData<Object>(original, parent), false);
        }

        [UsedByIL]
        public static T Instantiate<T>(T original) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original));
        }

#if PURRNET_UNITY_INSTANTIATE_PARAMETERS
        [UsedByIL]
        public static T Instantiate<T>(T original, InstantiateParameters parameters) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parameters);
            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, parameters));
        }

        [UsedByIL]
        public static T Instantiate<T>(T original, Vector3 pos, Quaternion rot, InstantiateParameters parameters) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, pos, rot, parameters);
            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, pos, rot, parameters));
        }

        [UsedByIL]
        public static T InstantiateDirectly<T>(T original, InstantiateParameters parameters) where T : Object
            => Object.Instantiate(original, parameters);

        public static T InstantiateDirectlyFromPool<T>(T original, InstantiateParameters parameters) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parameters);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, parameters), false);
        }

        [UsedByIL]
        public static T InstantiateDirectly<T>(T original, Vector3 pos, Quaternion rot, InstantiateParameters parameters) where T : Object
            => Object.Instantiate(original, pos, rot, parameters);

        public static T InstantiateDirectlyFromPool<T>(T original, Vector3 pos, Quaternion rot, InstantiateParameters parameters) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, pos, rot, parameters);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, pos, rot, parameters), false);
        }
#endif

        public static T InstantiateDirectly<T>(T original) where T : Object
            => Object.Instantiate(original);

        public static T InstantiateDirectlyFromPool<T>(T original) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original), false);
        }

        [UsedByIL]
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, position, rotation);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, position, rotation));
        }

        public static T InstantiateDirectly<T>(T original, Vector3 position, Quaternion rotation) where T : Object
            => Object.Instantiate(original, position, rotation);

        public static T InstantiateDirectlyFromPool<T>(T original, Vector3 position, Quaternion rotation) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, position, rotation);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, position, rotation), false);
        }

        [UsedByIL]
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, Scene scene) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
            {
                var obj = Object.Instantiate(original, position, rotation);
                var go = GetGameObject(obj);

                if (go && go.scene.handle != scene.handle)
                    SceneManager.MoveGameObjectToScene(go, scene);
                return obj;
            }

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, position, rotation, scene));
        }

        public static T InstantiateDirectly<T>(T original, Vector3 position, Quaternion rotation, Scene scene)
            where T : Object
        {
            var obj = Object.Instantiate(original, position, rotation);
            var go = GetGameObject(obj);
            if (go)
                SceneManager.MoveGameObjectToScene(go, scene);
            return obj;
        }

        public static T InstantiateDirectlyFromPool<T>(T original, Vector3 position, Quaternion rotation, Scene scene)
            where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return InstantiateDirectly(original, position, rotation, scene);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, position, rotation, scene), false);
        }

        [UsedByIL]
        public static T Instantiate<T>(
            T original,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
            where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, position, rotation, parent);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, position, rotation, parent));
        }

        public static T InstantiateDirectly<T>(
            T original,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
            where T : Object
            => Object.Instantiate(original, position, rotation, parent);

        public static T InstantiateDirectlyFromPool<T>(
            T original,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
            where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, position, rotation, parent);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, position, rotation, parent), false);
        }

        [UsedByIL]
        public static T Instantiate<T>(T original, Transform parent) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parent);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, parent));
        }

        public static T InstantiateDirectly<T>(T original, Transform parent) where T : Object
            => Object.Instantiate(original, parent);

        public static T InstantiateDirectlyFromPool<T>(T original, Transform parent) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parent);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, parent), false);
        }

        [UsedByIL]
        public static T Instantiate<T>(T original, Transform parent, bool worldPositionStays) where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parent, worldPositionStays);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, parent, worldPositionStays));
        }

        public static T InstantiateDirectly<T>(T original, Transform parent, bool worldPositionStays) where T : Object
            => Object.Instantiate(original, parent, worldPositionStays);

        public static T InstantiateDirectlyFromPool<T>(T original, Transform parent, bool worldPositionStays)
            where T : Object
        {
            if (!TryGetPrefabData(original, out var prefabData))
                return Object.Instantiate(original, parent, worldPositionStays);

            return OnPreInstantiate(prefabData, new InstantiateData<T>(original, parent, worldPositionStays), false);
        }

        [UsedByIL]
        public static void Destroy(Object obj)
        {
            if (OnDestroy(obj))
                Object.Destroy(obj);
        }

        public static void DestroyDirectly(Object obj)
            => Object.Destroy(obj);

        [UsedByIL]
        public static async void Destroy(Object obj, float t)
        {
            try
            {
#if UNITASK_PURRNET_SUPPORT
                await UniTask.WaitForSeconds(t);
#else
                await Task.Delay(TimeSpan.FromSeconds(t));
#endif
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    return;
#endif

                if (obj)
                    Destroy(obj);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static void DestroyDirectly(Object obj, float t)
            => Object.Destroy(obj, t);

        [UsedByIL]
        public static void DestroyImmediate(Object obj)
        {
            if (OnDestroy(obj))
                Object.DestroyImmediate(obj);
        }

        public static void DestroyImmediateDirectly(Object obj)
            => Object.DestroyImmediate(obj);

        [UsedByIL]
        public static void DestroyImmediate(Object obj, bool allowDestroyingAssets)
        {
            if (OnDestroy(obj))
                Object.DestroyImmediate(obj, allowDestroyingAssets);
        }

        public static void DestroyImmediateDirectly(Object obj, bool allowDestroyingAssets)
            => Object.DestroyImmediate(obj, allowDestroyingAssets);
    }
}
