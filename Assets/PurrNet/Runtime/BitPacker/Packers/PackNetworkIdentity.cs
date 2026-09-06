using System;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet
{
    [RegisterNetworkType(typeof(DisposableList<int>))]
    public static class PackNetworkIdentity
    {
        [UsedByIL]
        public static bool WriteNetworkIDDelta(BitPacker packer, NetworkID oldvalue, NetworkID newvalue)
        {
            bool changed = DeltaPacker<PlayerID>.Write(packer, oldvalue.scope, newvalue.scope);
            return DeltaPackInteger.WriteUInt64(packer, oldvalue.id, newvalue.id) || changed;
        }

        [UsedByIL]
        public static void ReadNetworkIDDelta(BitPacker packer, NetworkID oldvalue, ref NetworkID value)
        {
            var scope = oldvalue.scope;
            DeltaPacker<PlayerID>.Read(packer, oldvalue.scope, ref scope);
            var id = oldvalue.id;
            DeltaPackInteger.ReadUInt64(packer, oldvalue.id, ref id);
            value = new NetworkID(id, scope);
        }

        [UsedByIL]
        public static void WriteIdentityConcrete(this BitPacker packer, NetworkIdentity identity)
        {
            WriteIdentity(packer, identity);
        }

        [UsedByIL]
        public static void ReadIdentityConcrete(this BitPacker packer, ref NetworkIdentity identity)
        {
            ReadIdentity(packer, ref identity);
        }

        [UsedByIL]
        public static void WriteGameObject(this BitPacker packer, GameObject go)
        {
            Transform trs = null;
            if (go)
                trs = go.transform;
            WriteTransform(packer, trs);
        }

        [UsedByIL]
        public static void ReadGameObject(this BitPacker packer, ref GameObject go)
        {
            Transform trs = null;
            ReadTransform(packer, ref trs);
            go = trs ? trs.gameObject : null;
        }

        static NetworkIdentity GetSpawnedParent(Transform trs)
        {
            if (!trs)
                return null;

            if (trs.TryGetComponent<NetworkIdentity>(out var identity) && identity.isSpawned)
                return identity;

            while (true)
            {
                if (!trs)
                    return null;

                if (!trs.parent)
                    return null;

                var parent = trs.parent.GetComponentInParent<NetworkIdentity>(true);

                if (!parent)
                    return null;

                if (parent.isSpawned)
                    return parent;

                trs = parent.transform;
            }
        }

        static Transform WalkThePath(Transform parent, DisposableList<int> inversedPath)
        {
            try
            {
                if (inversedPath.rawList == null || inversedPath.Count == 0)
                    return parent;

                int len = inversedPath.Count;
                for (var i = len - 1; i >= 0; i--)
                {
                    var siblingIndex = inversedPath[i];

                    if (parent.childCount <= siblingIndex)
                    {
                        PurrLogger.LogWarning($"Parent {parent} doesn't have child with index {siblingIndex}");
                        break;
                    }

                    parent = parent.GetChild(siblingIndex);
                }

                return parent;
            }
            catch
            {
                return null;
            }
        }

        [UsedByIL]
        public static void WriteTransform(this BitPacker packer, Transform trs)
        {
            if (!trs)
            {
                packer.WriteBit(false);
                return;
            }

            var parent = GetSpawnedParent(trs);

            if (!parent || !parent.isSpawned || !parent.id.HasValue)
            {
                packer.WriteBit(true);
                packer.WriteBit(false);

                if (NetworkManager.main == null || !NetworkManager.main.prefabResolver.TryGetPrefabData(trs.gameObject, out var data))
                {
                    packer.WriteBit(true);
                    if (NetworkManager.main != null && NetworkManager.main.networkAssetResolver.TryGetId(trs, out _))
                    {
                        packer.WriteBit(true);
                        Packer.WriteAsNetworkAsset(packer, trs);
                    }
                    else if (NetworkManager.main != null && NetworkManager.main.networkAssetResolver.TryGetId(trs.gameObject, out _))
                    {
                        packer.WriteBit(false);
                        Packer.WriteAsNetworkAsset(packer, trs.gameObject);
                    }

                    return;
                }

                packer.WriteBit(false);
                Packer<PrefabID>.Write(packer, data.prefabId);
                return;
            }

            packer.WriteBit(true);
            packer.WriteBit(true);
            using var invPath = HierarchyPool.GetInvPath(parent.transform, trs);

            Packer<SceneID>.Write(packer, parent.sceneId);
            Packer<NetworkID>.Write(packer, parent.id.Value);
            Packer<DisposableList<int>>.Write(packer, invPath);
        }

        [UsedByIL]
        public static void ReadTransform(this BitPacker packer, ref Transform trs)
        {
            if (!packer.ReadBit())
            {
                trs = null;
                return;
            }
            var isSpawned = Packer<bool>.Read(packer);

            if (!isSpawned)
            {
                var useFallback = Packer<bool>.Read(packer);
                if (useFallback)
                {
                    var isTrs = Packer<bool>.Read(packer);
                    if (isTrs)
                    {
                        Packer.ReadAsNetworkAsset(packer, ref trs);
                        return;
                    }

                    GameObject g = null;
                    Packer.ReadAsNetworkAsset(packer, ref g);
                    if (g)
                        trs = g.transform;
                    return;
                }

                var prefabId = Packer<PrefabID>.Read(packer);
                if (NetworkManager.main && NetworkManager.main.prefabResolver.TryGetPrefabData(prefabId, out var prefabData))
                    trs = prefabData.prefab.transform;
                return;
            }

            SceneID sceneId = default;
            NetworkID id = default;
            DisposableList<int> invPath = default;

            Packer<SceneID>.Read(packer, ref sceneId);
            Packer<NetworkID>.Read(packer, ref id);
            Packer<DisposableList<int>>.Read(packer, ref invPath);

            var networkManager = NetworkManager.main;

            if (!networkManager)
            {
                trs = null;
                return;
            }

            if (!networkManager.TryGetModule<HierarchyFactory>(networkManager.isServer, out var module) ||
                !module.TryGetIdentity(sceneId, id, out var result))
            {
                trs = null;
                return;
            }

            var root = result.transform;
            trs = WalkThePath(root, invPath);
            invPath.Dispose();
        }

        [UsedByIL]
        public static void RegisterIdentity<T>() where T : NetworkIdentity
        {
            Packer<T>.RegisterWriter(WriteIdentity);
            Packer<T>.RegisterReader(ReadIdentity);
        }

        [UsedByIL]
        public static void WriteIdentity<T>(this BitPacker packer, T value) where T : NetworkIdentity
        {
            if (value == null || !value.id.HasValue)
            {
                packer.WriteBit(false);
                return;
            }

            packer.WriteBit(true);
            Packer<NetworkID>.Write(packer, value.id.Value);
            Packer<SceneID>.Write(packer, value.sceneId);
        }

        [UsedByIL]
        public static void ReadIdentity<T>(this BitPacker packer, ref T value) where T : NetworkIdentity
        {
            bool hasValue = false;

            Packer<bool>.Read(packer, ref hasValue);

            if (!hasValue)
            {
                value = null;
                return;
            }

            NetworkID id = default;
            SceneID sceneId = default;

            Packer<NetworkID>.Read(packer, ref id);
            Packer<SceneID>.Read(packer, ref sceneId);

            var networkManager = NetworkManager.main;

            if (!networkManager)
            {
                value = null;
                return;
            }

            if (!networkManager.TryGetModule<HierarchyFactory>(networkManager.isServer, out var module) ||
                !module.TryGetIdentity(sceneId, id, out var result) || result is not T identity)
            {
                value = null;
                return;
            }

            value = identity;
        }
    }
}
