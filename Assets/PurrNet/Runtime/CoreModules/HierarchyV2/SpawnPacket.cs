using System;
using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet.Modules
{
    public struct SpawnPacket : IDisposable
    {
        public SceneID sceneId;
        public SpawnID packetIdx;
        public bool bypassPool;
        public bool isAsync;
        public GameObjectPrototype prototype;
        public BitData customData;

        [DontPack]
        internal List<NetworkIdentity> localcache;

        /// <summary>
        /// Checks if the prefab is a raw prefab, meaning it is not a mix between multiple prefabs or scene objects.
        /// And if it is, retrieves the prefab from the NetworkManager's prefab provider.
        /// </summary>
        /// <param name="manager">The NetworkManager containing the prefab provider.</param>
        /// <param name="prefab">The GameObject prefab if found.</param>
        /// <returns>True if the prefab is a raw prefab, false otherwise.</returns>
        public bool TryGetRawPrefab(NetworkManager manager, out GameObject prefab)
        {
            prefab = null;

            if (!manager || prototype.framework.Count == 0)
            {
                PurrLogger.LogError("NetworkManager is null or prototype framework is empty.");
                return false;
            }

            var rootPrefabId = prototype.framework[0].pid.prefabId;

            if (!manager.prefabResolver.TryGetPrefabData(rootPrefabId, out var prefabData))
            {
                PurrLogger.LogError($"Prefab with ID {rootPrefabId} not found in prefab provider.");
                return false;
            }

            if (!HierarchyPool.TryGetOrCreatePrefabPrototype(prefabData, out var prefabProto))
            {
                PurrLogger.LogError($"Prefab `{prefabData.prefab.name}` is not a valid prefab prototype.");
                return false;
            }

            if (prototype.framework.Count != prefabProto.framework.Count)
            {
                PurrLogger.LogError($"Prefab prototype framework count mismatch: {prototype.framework.Count} vs {prefabProto.framework.Count}");
                return false;
            }

            for (int i = 0; i < prototype.framework.Count; i++)
            {
                var pieceA = prototype.framework[i];
                var pieceB = prefabProto.framework[i];

                if (!pieceA.AreEqual(pieceB))
                {
                    PurrLogger.LogError($"Prefab prototype framework piece mismatch at index {i}: {pieceA} vs {pieceB}");
                    return false;
                }
            }

            prefab = prefabData.prefab;
            return true;
        }

        public override string ToString()
        {
            return $"SpawnPacket: {{ sceneId: {sceneId}, packetIdx: {packetIdx}, prototype: {prototype} }}";
        }

        public void Dispose()
        {
            if (customData.packer is { isWrapper: false })
                customData.Dispose();
            prototype.Dispose();
            if (localcache != null)
            {
                ListPool<NetworkIdentity>.Destroy(localcache);
                localcache = null;
            }
        }
    }
}
