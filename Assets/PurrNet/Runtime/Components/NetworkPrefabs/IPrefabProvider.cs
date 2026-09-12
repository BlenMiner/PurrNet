using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet
{
    public interface IAsyncPrefabProvider : IPrefabProvider
    {
        bool NeedsLoad(int prefabId);
        Task<PrefabData> LoadPrefabAsync(int prefabId);
    }

    public struct PrefabData
    {
        public PrefabID prefabId;
        public GameObject prefab;
        public PoolingConfig pooling;

        /// <summary>Shim for backwards compatibility. Use pooling.pooled instead.</summary>
        public bool pooled
        {
            get => pooling.pooled;
            set => pooling.pooled = value;
        }

        /// <summary>Shim for backwards compatibility. Use pooling.warmupCount instead.</summary>
        public int warmupCount
        {
            get => pooling.warmupCount;
            set => pooling.warmupCount = value;
        }
    }

    public interface IPersistentPrefabProvider
    {
        IEnumerable<string> persistentIds { get; }

        bool TryGetPersistentId(int prefabId, out string persistentId);

        bool TryGetPersistentId(GameObject prefab, out string persistentId);

        bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData);
    }

    public interface IPrefabProvider
    {
        IEnumerable<PrefabData> allPrefabs { get; }

        bool TryGetPrefabData(int prefabId, out PrefabData prefabData);

        bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData);

        void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5);

        void Refresh();
    }

    public abstract class PrefabProviderScriptable : ScriptableObject, IPrefabProvider
    {
        protected static bool TryMatchByName(IEnumerable<PrefabData> candidates, GameObject prefab,
            out PrefabData prefabData)
        {
            prefabData = default;

            if (!prefab)
                return false;

            var prefabName = prefab.name;
            int identityCount = -1;
            bool found = false;

            foreach (var data in candidates)
            {
                if (!data.prefab || data.prefab.name != prefabName)
                    continue;

                if (identityCount < 0)
                    identityCount = CountIdentities(prefab);

                if (CountIdentities(data.prefab) != identityCount)
                    continue;

                if (found)
                    return false;

                prefabData = data;
                found = true;
            }

            if (!found)
                prefabData = default;

            return found;
        }

        static int CountIdentities(GameObject prefab)
        {
            var identities = ListPool<NetworkIdentity>.Instantiate();
            prefab.GetComponentsInChildren(true, identities);
            int count = identities.Count;
            ListPool<NetworkIdentity>.Destroy(identities);
            return count;
        }

        public abstract IEnumerable<PrefabData> allPrefabs { get; }

        public abstract bool TryGetPrefabData(int prefabId, out PrefabData prefabData);

        public abstract bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData);

        public abstract void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5);

        public abstract void Refresh();
    }
}
