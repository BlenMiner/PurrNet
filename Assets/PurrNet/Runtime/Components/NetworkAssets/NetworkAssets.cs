using System;
using System.Collections.Generic;
using System.Linq;
using PurrNet.Utils;
using UnityEngine;
using Object = UnityEngine.Object;
#if UNITY_6000_3_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

namespace PurrNet
{
    [CreateAssetMenu(fileName = "NetworkAssets", menuName = "PurrNet/Network Assets", order = -200)]
    public class NetworkAssets : ScriptableObject
    {
        public bool autoGenerate;
        public Object folder;

        [Tooltip("When no folder is set, search all of Assets/ instead of doing nothing.")]
        public bool searchAllIfNoFolder = true;

        [Tooltip("Will also get assets from these linked NetworkAssets. This is to allow further organization.")]
        public List<NetworkAssets> linkedNetworkAssets = new();

        [Serializable]
        public class TypeToggle
        {
            public string typeName;
            public bool enabled;
        }

        [SerializeField] private List<string> _enabledTypeNames = new();
        private HashSet<string> _enabledTypeLookup;

        public HashSet<string> enabledTypeNames
        {
            get
            {
                return _enabledTypeLookup ??= new HashSet<string>(_enabledTypeNames);
            }
        }

        public List<Object> assets = new();

        [SerializeField, HideInInspector]
        private List<string> _availableTypeNames = new();
        public IReadOnlyList<string> AvailableTypeNames => _availableTypeNames;

        /// <summary>
        /// GUIDs for each asset in the assets list, used for persistent ID lookup.
        /// Populated during GenerateAssets() in editor.
        /// </summary>
        [SerializeField, HideInInspector] private List<string> _assetGuids = new();

        private readonly Dictionary<int, Object> idToAsset = new();
        private readonly Dictionary<ObjectId, int> objectIdToId = new();
        private readonly Dictionary<string, Object> persistentIdToAsset = new();
        private readonly Dictionary<string, int> persistentIdToId = new();
        private readonly Dictionary<int, string> idToPersistentId = new();
        private readonly Dictionary<ObjectId, string> objectIdToPersistentId = new();

        private const int AmbiguousMarker = -2;
        private readonly Dictionary<(Type, string), int> typeNameToId = new();
        private HashSet<(Type, string)> _warnedAmbiguous;
        private HashSet<ObjectId> _warnedUnresolved;

        [SerializeField, HideInInspector] private List<int> _bakedIds = new();
        [SerializeField, HideInInspector] private List<Object> _bakedAssets = new();
        [SerializeField, HideInInspector] private List<string> _bakedPersistentIds = new();

        public Object GetAsset(int index) => idToAsset.GetValueOrDefault(index);
        public Object GetAssetByPersistentId(string persistentId)
        {
            return !string.IsNullOrEmpty(persistentId) &&
                   persistentIdToAsset.TryGetValue(persistentId, out var obj)
                ? obj
                : null;
        }

        public int GetIndex(Object obj)
        {
            if (!obj) return -1;

            if (TryGetIndex(obj, out int id))
                return id;

            WarnUnresolvedOnce(obj, GetObjectId(obj));
            return -1;
        }

        /// <summary>
        /// Same lookup as GetIndex but silent — no warning when the asset isn't registered.
        /// Use this to probe registration of assets that are allowed to be unregistered.
        /// </summary>
        public bool TryGetIndex(Object obj, out int id)
        {
            id = -1;
            if (!obj) return false;

            ObjectId objectId = GetObjectId(obj);
            if (objectIdToId.TryGetValue(objectId, out id))
                return true;

            if (TryResolveDuplicate(obj, out id))
            {
                objectIdToId[objectId] = id;
                return true;
            }

            id = -1;
            return false;
        }

        public bool TryGetPersistentId(Object obj, out string persistentId)
        {
            persistentId = null;
            if (!obj) return false;

            ObjectId objectId = GetObjectId(obj);
            if (objectIdToPersistentId.TryGetValue(objectId, out persistentId))
                return true;

            if (!TryResolveDuplicate(obj, out int id))
                return false;

            return TryGetPersistentId(id, out persistentId);
        }

        public bool TryGetPersistentId(int id, out string persistentId)
        {
            return idToPersistentId.TryGetValue(id, out persistentId);
        }

        public bool TryGetAssetByPersistentId(string persistentId, out Object obj)
        {
            if (!string.IsNullOrEmpty(persistentId) &&
                persistentIdToAsset.TryGetValue(persistentId, out obj))
                return true;

            obj = null;
            return false;
        }

        public bool TryGetIdByPersistentId(string persistentId, out int id)
        {
            if (!string.IsNullOrEmpty(persistentId) &&
                persistentIdToId.TryGetValue(persistentId, out id))
                return true;

            id = -1;
            return false;
        }

        private bool TryResolveDuplicate(Object obj, out int id)
        {
            if (typeNameToId.TryGetValue((obj.GetType(), obj.name), out id))
            {
                if (id == AmbiguousMarker)
                {
                    WarnAmbiguousOnce(obj);
                    id = -1;
                    return false;
                }
                return true;
            }
            id = -1;
            return false;
        }

        public bool TryGetCanonical(Object maybeDuplicate, out Object canonical)
        {
            canonical = null;
            int id = GetIndex(maybeDuplicate);
            if (id < 0) return false;
            return idToAsset.TryGetValue(id, out canonical);
        }

        private void WarnAmbiguousOnce(Object obj)
        {
            _warnedAmbiguous ??= new HashSet<(Type, string)>();
            var key = (obj.GetType(), obj.name);
            if (_warnedAmbiguous.Add(key))
            {
                Debug.LogWarning(
                    $"NetworkAssets: cannot resolve duplicate managed instance of '{obj.name}' " +
                    $"({obj.GetType().Name}) — multiple registered assets share the same (Type, name). " +
                    $"Returning -1.", this);
            }
        }

        private void WarnUnresolvedOnce(Object obj, ObjectId objectId)
        {
            _warnedUnresolved ??= new HashSet<ObjectId>();
            if (_warnedUnresolved.Add(objectId))
            {
                Debug.LogWarning(
                    $"NetworkAssets: could not resolve '{obj.name}' ({obj.GetType().Name}, " +
                    $"id={objectId}) - not registered and no (Type, name) fallback matched.",
                    this);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (autoGenerate &&
                !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode &&
                !UnityEditor.EditorApplication.isCompiling &&
                !UnityEditor.EditorApplication.isUpdating)
            {
                UnityEditor.EditorApplication.delayCall += GenerateAssets;
            }
        }
#endif

        private void OnEnable()
        {
            ClearLookups();

            if (_bakedAssets.Count == 0 &&
                (assets.Count > 0 || linkedNetworkAssets is { Count: > 0 }))
            {
                Refresh();
                return;
            }

            for (int i = 0; i < _bakedAssets.Count; i++)
            {
                var obj = _bakedAssets[i];
                int id = _bakedIds[i];
                string persistentId = i < _bakedPersistentIds.Count ? _bakedPersistentIds[i] : null;
                if (!obj) continue;

                try
                {
                    idToAsset[id] = obj;
                    _count = Math.Max(_count, id + 1);
                    objectIdToId[GetObjectId(obj)] = id;
                    RegisterTypeNameFallback(obj, id);
                    RegisterPersistentId(obj, id, persistentId);
                }
                catch
                {
                    idToAsset.Remove(id);
                }
            }

            if (IsBakeStale() || _bakedPersistentIds.Count != _bakedAssets.Count)
                Refresh();
        }

        private void ClearLookups()
        {
            _count = 0;
            idToAsset.Clear();
            objectIdToId.Clear();
            persistentIdToAsset.Clear();
            persistentIdToId.Clear();
            idToPersistentId.Clear();
            objectIdToPersistentId.Clear();
            typeNameToId.Clear();
            _warnedAmbiguous?.Clear();
            _warnedUnresolved?.Clear();
        }

        private void RegisterPersistentId(Object obj, int id, string persistentId)
        {
            if (!obj || string.IsNullOrEmpty(persistentId))
                return;

            if (!persistentIdToAsset.ContainsKey(persistentId))
            {
                persistentIdToAsset.Add(persistentId, obj);
                persistentIdToId.Add(persistentId, id);
            }

            idToPersistentId[id] = persistentId;
            objectIdToPersistentId[GetObjectId(obj)] = persistentId;
        }

        private void RegisterTypeNameFallback(Object obj, int id)
        {
            var key = (obj.GetType(), obj.name);
            if (typeNameToId.TryGetValue(key, out int existing))
            {
                if (existing != id)
                    typeNameToId[key] = AmbiguousMarker;
            }
            else
            {
                typeNameToId[key] = id;
            }
        }

        private bool IsBakeStale()
        {
            var visited = new HashSet<NetworkAssets>();
            return Check(this);

            bool Check(NetworkAssets na)
            {
                if (!na || !visited.Add(na)) return false;

                for (int i = 0; i < na.assets.Count; i++)
                {
                    var obj = na.assets[i];
                    if (obj && !objectIdToId.ContainsKey(GetObjectId(obj)))
                        return true;
                }

                if (na.linkedNetworkAssets == null) return false;
                for (int i = 0; i < na.linkedNetworkAssets.Count; i++)
                {
                    if (Check(na.linkedNetworkAssets[i]))
                        return true;
                }

                return false;
            }
        }

        public IReadOnlyList<Object> AllAssets => assets;
        public IReadOnlyDictionary<int, Object> IndexToAsset => idToAsset;
        public IReadOnlyDictionary<string, Object> PersistentIdToAsset => persistentIdToAsset;

        public void Refresh()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (RebuildGuids())
                    UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
            ClearLookups();
            _bakedIds.Clear();
            _bakedAssets.Clear();
            _bakedPersistentIds.Clear();

            var visited = new HashSet<NetworkAssets>();
            var seenObjectIds = new HashSet<ObjectId>();
            var buffer = new List<(Object asset, string guid)>();

            Collect(this);

            for (int i = 0; i < buffer.Count; i++)
            {
                var obj = buffer[i].asset;
                ObjectId objectId = GetObjectId(obj);
                if (objectIdToId.ContainsKey(objectId)) continue;

                idToAsset[i] = obj;
                _count = Math.Max(_count, i + 1);
                objectIdToId[objectId] = i;

                RegisterTypeNameFallback(obj, i);
                RegisterPersistentId(obj, i, buffer[i].guid);

                _bakedIds.Add(i);
                _bakedAssets.Add(obj);
                _bakedPersistentIds.Add(buffer[i].guid);
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            return;

            void Collect(NetworkAssets na)
            {
                if (!na || !visited.Add(na)) return;

#if UNITY_EDITOR
                if (!Application.isPlaying && na.RebuildGuids())
                    UnityEditor.EditorUtility.SetDirty(na);
#endif

                for (int i = 0; i < na.assets.Count; i++)
                {
                    var obj = na.assets[i];
                    if (!obj || !seenObjectIds.Add(GetObjectId(obj))) continue;

                    string guid = (i < na._assetGuids.Count) ? na._assetGuids[i] : null;
                    buffer.Add((obj, guid));
                }

                if (na.linkedNetworkAssets == null) return;
                for (int i = 0; i < na.linkedNetworkAssets.Count; i++)
                {
                    var link = na.linkedNetworkAssets[i];
                    if (link) Collect(link);
                }
            }
        }

        public void AddAsset(Object obj, bool logIfDuplicate = true)
        {
            if (!obj) return;

            if (objectIdToId.ContainsKey(GetObjectId(obj)))
            {
                if (logIfDuplicate)
                    Debug.LogWarning($"Asset already exists in NetworkAssets: {obj.name}");
                return;
            }

            assets.Add(obj);
            Refresh();
        }

        public void CacheAvailableTypes(IEnumerable<Type> types)
        {
            _availableTypeNames = types.Select(t => t.AssemblyQualifiedName).Distinct().ToList();
        }

        public void SetEnabledType(string typeName, bool enable)
        {
            if (enable)
            {
                if (!_enabledTypeNames.Contains(typeName))
                    _enabledTypeNames.Add(typeName);
            }
            else
            {
                _enabledTypeNames.Remove(typeName);
            }

            _enabledTypeLookup = null;
        }

#if UNITY_EDITOR
        public void GenerateAssets()
        {
            if (Application.isPlaying || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;

            var enabledTypes = enabledTypeNames.Select(Type.GetType).Where(t => t != null).ToArray();
            bool isSceneSource = folder is UnityEditor.SceneAsset;
            var found = isSceneSource
                ? AssetScannerUtility.ScanSceneAssets(UnityEditor.AssetDatabase.GetAssetPath(folder), enabledTypes)
                : AssetScannerUtility.ScanAssets(folder, enabledTypes, searchAllIfNoFolder);
            var linkedAssets = AssetScannerUtility.CollectLinkedNetworkAssets(this);
            if (linkedAssets.Count > 0)
                found.RemoveAll(scan => linkedAssets.Contains(scan.asset));

            if (found.Count == 0 && folder == null && !searchAllIfNoFolder)
                return;

            var existingSet = new HashSet<Object>(assets);
            bool changed = false;

            if (linkedAssets.Count > 0)
            {
                int removed = assets.RemoveAll(asset => asset && linkedAssets.Contains(asset));
                if (removed > 0)
                {
                    existingSet = new HashSet<Object>(assets);
                    changed = true;
                }
            }

            // The registry mirrors its source: anything the scan no longer finds is dropped.
            var foundSet = new HashSet<Object>(found.Select(scan => scan.asset));
            int pruned = assets.RemoveAll(asset => !asset || !foundSet.Contains(asset));
            if (pruned > 0)
            {
                existingSet = new HashSet<Object>(assets);
                changed = true;
            }

            foreach (var scan in found)
            {
                if (existingSet.Add(scan.asset))
                {
                    assets.Add(scan.asset);
                    changed = true;
                }
            }

            // Update GUIDs list to match assets list
            bool guidsChanged = RebuildGuids();

            if (changed || guidsChanged)
            {
                Refresh();
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(this);
            }

            CleanupNullEntries();
        }

        /// <summary>
        /// Rebuilds the _assetGuids list from the current assets list using AssetDatabase.
        /// </summary>
        private bool RebuildGuids()
        {
            var previous = _assetGuids;
            var rebuilt = new List<string>(assets.Count);

            for (int i = 0; i < assets.Count; i++)
            {
                var obj = assets[i];
                if (!obj)
                {
                    rebuilt.Add(null);
                    continue;
                }

                string path = UnityEditor.AssetDatabase.GetAssetPath(obj);
                string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);

                // For sub-assets, append local file ID to make GUID unique
                if (UnityEditor.AssetDatabase.IsSubAsset(obj))
                {
                    if (UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string _, out long localId))
                        guid = $"{guid}_{localId}";
                }

                rebuilt.Add(guid);
            }

            bool changed = previous.Count != rebuilt.Count;
            if (!changed)
            {
                for (int i = 0; i < rebuilt.Count; i++)
                {
                    if (previous[i] == rebuilt[i]) continue;
                    changed = true;
                    break;
                }
            }

            if (changed)
                _assetGuids = rebuilt;

            return changed;
        }

        private void CleanupNullEntries()
        {
            int count = assets.Count;
            assets.RemoveAll(a => a == null);
            if (assets.Count != count)
            {
                RebuildGuids();
                Refresh();
            }
        }
#endif

        private int _count;

        /// <summary>
        /// Number of ids this registry hands out; the highest id is count - 1.
        /// </summary>
        public int count => _count;

        public bool TryGetAsset(int id, out Object obj) => idToAsset.TryGetValue(id, out obj);

        public bool TryGetId(Object obj, out int id)
        {
            id = GetIndex(obj);
            return id >= 0;
        }

        private static ObjectId GetObjectId(Object obj) => PurrObjectId.Of(obj);
    }

}
