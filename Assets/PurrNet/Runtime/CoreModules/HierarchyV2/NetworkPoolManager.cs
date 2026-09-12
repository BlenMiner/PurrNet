using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PurrNet.Modules
{
    [AddComponentMenu("")]
    internal sealed class PurrNetPoolRoot : MonoBehaviour
    {
    }

    public static class NetworkPoolManager
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearPools()
        {
            foreach (var pool in _pools.Values)
                pool.Dispose();

            foreach (var pool in _scenePools.Values)
                pool.Dispose();

            _pools.Clear();
            _scenePools.Clear();
            HierarchyPool.ClearPrototypes();
        }

        private static readonly Dictionary<IPrefabProvider, HierarchyPool> _pools = new();
        private static readonly Dictionary<SceneID, HierarchyPool> _scenePools = new();

        public static bool TryGetScenePool(SceneID scene, out HierarchyPool pool)
        {
            return _scenePools.TryGetValue(scene, out pool);
        }

        public static HierarchyPool GetScenePool(Scene unityScene, SceneID scene, NetworkManager manager)
        {
            if (_scenePools.TryGetValue(scene, out var pool))
                return pool;

            var poolParent = new GameObject($"PurrNetPool-{scene.ToString()}")
            {
#if PURRNET_DEBUG_POOLING
                hideFlags = HideFlags.DontSave
#else
                hideFlags = HideFlags.HideAndDontSave
#endif
            };
            poolParent.AddComponent<PurrNetPoolRoot>();

            SceneManager.MoveGameObjectToScene(poolParent, unityScene);

            pool = new HierarchyPool(poolParent.transform, manager.prefabResolver);
            _scenePools.Add(scene, pool);
            return pool;
        }

        public static HierarchyPool GetPool(NetworkManager manager)
        {
            var prefabs = manager.prefabProvider;

            if (prefabs == null)
                return null;

            if (_pools.TryGetValue(prefabs, out var pool))
                return pool;

            var poolParent = new GameObject($"PurrNetPool-{_pools.Count}")
            {
#if PURRNET_DEBUG_POOLING
                hideFlags = HideFlags.DontSave
#else
                hideFlags = HideFlags.HideAndDontSave
#endif
            };
            poolParent.AddComponent<PurrNetPoolRoot>();

            Object.DontDestroyOnLoad(poolParent);
            pool = new HierarchyPool(poolParent.transform, manager.prefabResolver);
            _pools.Add(prefabs, pool);
            pool.Warmup();
            return pool;
        }

        public static void RemovePool(IPrefabProvider prefabs)
        {
            if (_pools.Remove(prefabs, out var pool))
                pool.Dispose();
        }

        public static void RemovePool(SceneID scene)
        {
            if (_scenePools.Remove(scene, out var pool))
                pool.Dispose();

            HierarchyPool.EvictPrototypes(scene);
        }
    }
}
