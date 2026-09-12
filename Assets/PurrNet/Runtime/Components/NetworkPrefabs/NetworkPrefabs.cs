using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;
using PurrNet.Logging;
using Object = UnityEngine.Object;
#if UNITY_EDITOR
using PurrNet.Utils;
using UnityEditor;
#endif

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkPrefabs", menuName = "PurrNet/Network Prefabs/Prefabs", order = -201)]
    public class NetworkPrefabs : PrefabProviderScriptable, IPersistentPrefabProvider
    {
        public bool autoGenerate = true;
        public bool networkOnly = true;
        public bool poolByDefault;
        public Object folder;
        [Tooltip("When no folder is set, search all of Assets/ instead of doing nothing.")]
        public bool searchAllIfNoFolder = true;
        [Tooltip("Will also get prefabs from these nested prefabs. This is to allow further organization")]
        public List<NetworkPrefabs> linkedNetworkPrefabs = new();
        public List<UserPrefabData> prefabs = new List<UserPrefabData>();

        [Serializable]
        public struct UserPrefabData
        {
            public string guid;
            public GameObject prefab;
            public bool pooled;
            public int warmupCount;
        }

        public override IEnumerable<PrefabData> allPrefabs => prefabLookup.Values;
        public IEnumerable<string> persistentIds => persistentIdToPrefabData.Keys;

        public int count => prefabLookup.Count;

        private readonly Dictionary<int, PrefabData> prefabLookup = new();
        private readonly Dictionary<GameObject, PrefabData> prefabToData = new();
        private readonly Dictionary<string, PrefabData> persistentIdToPrefabData = new();
        private readonly Dictionary<int, string> prefabIdToPersistentId = new();
        private readonly Dictionary<GameObject, string> prefabToPersistentId = new();

        public override bool TryGetPrefabData(int prefabId, out PrefabData prefabData)
        {
            return this.prefabLookup.TryGetValue(prefabId, out prefabData);
        }

        public override bool TryGetPrefabData(GameObject prefab, out PrefabData prefabData)
        {
            if (prefab)
                return prefabToData.TryGetValue(prefab, out prefabData);

            prefabData = default;
            return false;
        }

        public bool TryGetPersistentId(int prefabId, out string persistentId)
        {
            return prefabIdToPersistentId.TryGetValue(prefabId, out persistentId);
        }

        public bool TryGetPersistentId(GameObject prefab, out string persistentId)
        {
            if (prefab && prefabToPersistentId.TryGetValue(prefab, out persistentId))
                return true;

            if (TryGetPrefabData(prefab, out var prefabData))
                return TryGetPersistentId((int)prefabData.prefabId, out persistentId);

            persistentId = null;
            return false;
        }

        public bool TryGetPrefabDataByPersistentId(string persistentId, out PrefabData prefabData)
        {
            if (!string.IsNullOrEmpty(persistentId) &&
                persistentIdToPrefabData.TryGetValue(persistentId, out prefabData))
                return true;

            prefabData = default;
            return false;
        }

        [PublicAPI]
        public bool TryGetPrefabByPersistentId(string persistentId, out GameObject prefab)
        {
            if (TryGetPrefabDataByPersistentId(persistentId, out var prefabData))
            {
                prefab = prefabData.prefab;
                return prefab;
            }

            prefab = null;
            return false;
        }

        public override void AddRuntimePrefab(string uniqueName, GameObject prefab, bool pooled = false, int warmup = 5)
        {
            prefabs.Add(new UserPrefabData
            {
                guid = uniqueName,
                pooled = pooled,
                prefab = prefab.gameObject,
                warmupCount = warmup
            });

            Refresh();
        }

        public override void Refresh()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                UpdatePrefabGuids();
#endif
            RegeneratePrefabLookup();
        }

#if UNITY_EDITOR
        private bool _generating;

        private void OnValidate()
        {
#if UNITY_EDITOR
            UpdatePrefabGuids();
#endif
            if (autoGenerate &&
                !EditorApplication.isPlayingOrWillChangePlaymode &&
                !EditorApplication.isCompiling &&
                !EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Generate;
            }
        }
#endif

        private void OnEnable()
        {
            RegeneratePrefabLookup();
        }

        private void RegeneratePrefabLookup()
        {
            prefabLookup.Clear();
            prefabToData.Clear();
            persistentIdToPrefabData.Clear();
            prefabIdToPersistentId.Clear();
            prefabToPersistentId.Clear();

            var visited = new HashSet<NetworkPrefabs>();
            var seenGuid = new HashSet<string>();
            var seenGO = new HashSet<GameObject>();
            var buffer = new List<UserPrefabData>();

            Collect(this);

            for (int i = 0; i < buffer.Count; i++)
            {
                var ud = buffer[i];
                var data = new PrefabData
                {
                    prefabId = i,
                    prefab = ud.prefab,
                    pooled = ud.pooled,
                    warmupCount = ud.warmupCount
                };

                prefabLookup.Add(i, data);

                if (!prefabToData.ContainsKey(ud.prefab))
                    prefabToData.Add(ud.prefab, data);

                RegisterPersistentId(ud.guid, data);
            }

            return;

            void Collect(NetworkPrefabs np)
            {
                if (!np || !visited.Add(np)) return;

                var list = np.prefabs;
                for (int i = 0; i < list.Count; i++)
                {
                    var ud = list[i];
                    if (!ud.prefab) continue;

                    var hasGuid = !string.IsNullOrEmpty(ud.guid);
                    if (hasGuid)
                    {
                        if (!seenGuid.Add(ud.guid)) continue;
                    }
                    else
                    {
                        if (!seenGO.Add(ud.prefab)) continue;
                    }

                    buffer.Add(ud);
                }

                var links = np.linkedNetworkPrefabs;
                if (links == null) return;
                for (int i = 0; i < links.Count; i++)
                {
                    var link = links[i];
                    if (link) Collect(link);
                }
            }
        }

        private void RegisterPersistentId(string persistentId, PrefabData prefabData)
        {
            if (string.IsNullOrEmpty(persistentId))
                return;

            persistentIdToPrefabData.TryAdd(persistentId, prefabData);
            prefabIdToPersistentId[(int)prefabData.prefabId] = persistentId;

            if (prefabData.prefab)
                prefabToPersistentId[prefabData.prefab] = persistentId;
        }

#if UNITY_EDITOR
        private void UpdatePrefabGuids()
        {
            bool changed = false;

            for (int i = 0; i < prefabs.Count; i++)
            {
                var entry = prefabs[i];
                if (!entry.prefab)
                    continue;

                string path = AssetDatabase.GetAssetPath(entry.prefab);
                if (string.IsNullOrEmpty(path))
                    continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid) || entry.guid == guid)
                    continue;

                entry.guid = guid;
                prefabs[i] = entry;
                changed = true;
            }

            if (changed)
                EditorUtility.SetDirty(this);
        }
#endif

        /// <summary>
        /// Editor only method to generate network prefabs from a specified folder.
        /// </summary>
        public void Generate()
        {
        #if UNITY_EDITOR
            if (ApplicationContext.isClone) return;
            if (!this) return;
            if (_generating) return;
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            _generating = true;
            try
            {
                if (folder is SceneAsset scene)
                     GenerateFromScene(scene);
                else GenerateFromFolder();

            }
            catch (Exception e)
            {
                PurrLogger.LogError($"An error occurred during prefab generation: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                _generating = false;
            }
        #endif
        }

#if UNITY_EDITOR
        private void GenerateFromScene(SceneAsset scene)
        {
            string scenePath = AssetDatabase.GetAssetPath(scene);
            if (string.IsNullOrEmpty(scenePath))
                return;

            var found = AssetScannerUtility.ScanScenePrefabs(scenePath, networkOnly);
            SyncWithScanResults(found);
        }

        private void GenerateFromFolder()
        {
            string resolvedPath = AssetScannerUtility.ResolveFolderPath(folder, searchAllIfNoFolder);

            if (string.IsNullOrEmpty(resolvedPath))
            {
                if (autoGenerate && prefabs.Count > 0)
                {
                    prefabs.Clear();
                    EditorUtility.SetDirty(this);
                    AssetDatabase.SaveAssetIfDirty(this);
                }

                return;
            }

            var found = AssetScannerUtility.ScanPrefabs(folder, networkOnly, searchAllIfNoFolder);
            SyncWithScanResults(found);
        }

        private void SyncWithScanResults(List<AssetScannerUtility.ScanResult> found)
        {
            var linkedGuids = AssetScannerUtility.CollectLinkedNetworkPrefabGuids(this);
            if (linkedGuids.Count > 0)
                found.RemoveAll(scan => linkedGuids.Contains(scan.guid));

            // Update GUIDs on existing entries
            for (int i = 0; i < prefabs.Count; i++)
            {
                if (!prefabs[i].prefab) continue;
                var path = AssetDatabase.GetAssetPath(prefabs[i].prefab);
                var g = AssetDatabase.AssetPathToGUID(path);
                if (prefabs[i].guid != g)
                {
                    var p = prefabs[i];
                    p.guid = g;
                    prefabs[i] = p;
                    EditorUtility.SetDirty(this);
                }
            }

            var (added, removed) = AssetScannerUtility.SyncEntries(
                prefabs,
                found,
                e => e.guid,
                e => e.prefab,
                scan => new UserPrefabData
                {
                    prefab = (GameObject)scan.asset,
                    pooled = poolByDefault,
                    warmupCount = 5,
                    guid = scan.guid
                },
                e => e.prefab);

            if (removed > 0 || added > 0)
            {
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssetIfDirty(this);
            }
        }
#endif
    }
}
