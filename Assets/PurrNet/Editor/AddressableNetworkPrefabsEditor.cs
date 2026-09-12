#if UNITY_EDITOR && ADDRESSABLES_PURRNET_SUPPORT
using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace PurrNet
{
    [CustomEditor(typeof(AddressableNetworkPrefabs))]
    public class AddressableNetworkPrefabsEditor : UnityEditor.Editor
    {
        private AddressableNetworkPrefabs _target;
        private SerializedProperty _entriesProp;
        private SerializedProperty _linkedProp;
        private SerializedProperty _folderProp;
        private ReorderableList _reorderableList;
        private string _searchFilter = "";

        private const float SPACING = 8f;
        private const float INDEX_WIDTH = 30f;

        private static bool _generating;

        [InitializeOnLoadMethod]
        private static void SubscribeAutoGenerate()
        {
            AddressableNetworkPrefabs.onAutoGenerateRequested += OnAutoGenerateRequested;
        }

        private static void OnAutoGenerateRequested(AddressableNetworkPrefabs target)
        {
            if (target && target.autoGenerate)
                Generate(target);
        }

        private void OnEnable()
        {
            _target = (AddressableNetworkPrefabs)target;
            _entriesProp = serializedObject.FindProperty("_entries");
            _linkedProp = serializedObject.FindProperty("linkedAddressablePrefabs");
            _folderProp = serializedObject.FindProperty("folder");

            if (_target.autoGenerate)
                Generate(_target);

            SetupReorderableList();
        }

        private void SetupReorderableList()
        {
            _reorderableList = new ReorderableList(serializedObject, _entriesProp, true, true, true, true);
            _reorderableList.elementHeight = EditorGUIUtility.singleLineHeight;

            _reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(new Rect(rect.x, rect.y, INDEX_WIDTH, rect.height), "ID");
                EditorGUI.LabelField(new Rect(rect.x + INDEX_WIDTH + SPACING, rect.y, rect.width - INDEX_WIDTH - SPACING, rect.height), "Prefab");
            };

            _reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = _entriesProp.GetArrayElementAtIndex(index);
                var assetProp = element.FindPropertyRelative("asset");

                float x = rect.x;
                EditorGUI.LabelField(new Rect(x, rect.y, INDEX_WIDTH, rect.height), index.ToString());
                x += INDEX_WIDTH + SPACING;

                EditorGUI.BeginDisabledGroup(_target.autoGenerate);
                DrawAddressablePrefabField(new Rect(x, rect.y, rect.width - INDEX_WIDTH - SPACING, rect.height), assetProp);
                EditorGUI.EndDisabledGroup();
            };

            _reorderableList.onAddDropdownCallback = (Rect buttonRect, ReorderableList list) =>
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Add Empty Entry"), false, () =>
                {
                    int index = list.count;
                    list.serializedProperty.arraySize++;
                    var element = list.serializedProperty.GetArrayElementAtIndex(index);
                    SetAssetReferenceGuid(element.FindPropertyRelative("asset"), string.Empty);
                    serializedObject.ApplyModifiedProperties();
                });

                menu.AddItem(new GUIContent("Add Selected Addressable Prefabs"), false, () =>
                {
                    bool addedAny = false;
                    foreach (var obj in Selection.gameObjects)
                    {
                        if (!PrefabUtility.IsPartOfPrefabAsset(obj))
                            continue;

                        var path = AssetDatabase.GetAssetPath(obj);
                        var guid = AssetDatabase.AssetPathToGUID(path);
                        var settings = AddressableAssetSettingsDefaultObject.Settings;
                        if (settings == null || settings.FindAssetEntry(guid) == null)
                            continue;

                        addedAny = true;
                        int index = list.count;
                        list.serializedProperty.arraySize++;
                        var element = list.serializedProperty.GetArrayElementAtIndex(index);
                        SetAssetReferenceGuid(element.FindPropertyRelative("asset"), guid);
                    }

                    if (addedAny)
                        serializedObject.ApplyModifiedProperties();
                });

                menu.ShowAsContext();
            };
        }

        private static void DrawAddressablePrefabField(Rect rect, SerializedProperty assetProp)
        {
            var guidProp = assetProp.FindPropertyRelative("m_AssetGUID");
            var current = LoadPrefabFromGuid(guidProp?.stringValue);

            EditorGUI.BeginChangeCheck();
            var selected = (GameObject)EditorGUI.ObjectField(rect, current, typeof(GameObject), false);
            if (!EditorGUI.EndChangeCheck())
                return;

            if (!selected)
            {
                SetAssetReferenceGuid(assetProp, string.Empty);
                return;
            }

            var path = AssetDatabase.GetAssetPath(selected);
            var guid = AssetDatabase.AssetPathToGUID(path);
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || settings.FindAssetEntry(guid) == null)
            {
                PurrNet.Logging.PurrLogger.LogWarning($"`{selected.name}` is not marked as Addressable and was not added.", selected);
                return;
            }

            SetAssetReferenceGuid(assetProp, guid);
        }

        private static GameObject LoadPrefabFromGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;

            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static void SetAssetReferenceGuid(SerializedProperty assetProp, string guid)
        {
            assetProp.FindPropertyRelative("m_AssetGUID").stringValue = guid;
            assetProp.FindPropertyRelative("m_SubObjectName").stringValue = string.Empty;
            assetProp.FindPropertyRelative("m_SubObjectType").stringValue = string.Empty;
            assetProp.FindPropertyRelative("m_SubObjectGUID").stringValue = string.Empty;

            var changedProp = assetProp.FindPropertyRelative("m_EditorAssetChanged");
            if (changedProp != null)
            {
                if (changedProp.propertyType == SerializedPropertyType.Boolean)
                    changedProp.boolValue = true;
                else
                    changedProp.intValue = 1;
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // 1. Header
            SharedAssetEditorUI.DrawHeader(
                "Addressable Network Prefabs",
                "This asset stores Addressable prefab references for network spawning. " +
                "Prefabs can be added manually or auto-generated from a folder containing Addressable assets.");

            // 2. Generation Settings
            SharedAssetEditorUI.DrawGenerationSettingsTop(_folderProp, _target);

            // 3. Toggle buttons row
            GUILayout.BeginHorizontal();
            DrawToggleButton("Auto generate", ref _target.autoGenerate);
            _target.preloadAtStartup = SharedAssetEditorUI.DrawToggleButton("Preload at startup", _target.preloadAtStartup, _target);
            GUILayout.EndHorizontal();

            // 4. Generate button (full-width, own row)
            SharedAssetEditorUI.DrawGenerateButton(() =>
            {
                Generate(_target);
                serializedObject.Update();
                _entriesProp = serializedObject.FindProperty("_entries");
            });

            // 5. Linked prefabs
            SharedAssetEditorUI.DrawLinkedField(_linkedProp);

            // 6. Entry list
            SharedAssetEditorUI.DrawEntryList(_reorderableList, _target.autoGenerate,
                ref _searchFilter, i =>
                {
                    if (i >= _entriesProp.arraySize) return null;
                    var assetProp = _entriesProp.GetArrayElementAtIndex(i).FindPropertyRelative("asset");
                    string guid = assetProp.FindPropertyRelative("m_AssetGUID")?.stringValue;
                    if (string.IsNullOrEmpty(guid)) return null;
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) return null;
                    var obj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    return obj ? obj.name : System.IO.Path.GetFileNameWithoutExtension(path);
                });

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                _target.Refresh();
                EditorUtility.SetDirty(_target);
            }
        }

        private void DrawToggleButton(string label, ref bool value)
        {
            value = SharedAssetEditorUI.DrawToggleButton(label, value, _target, () =>
            {
                if (_target.autoGenerate)
                {
                    Generate(_target);
                    serializedObject.Update();
                    _entriesProp = serializedObject.FindProperty("_entries");
                }
            });
        }

        /// <summary>
        /// Scans the configured folder for Addressable prefabs and adds them as entries.
        /// Only prefabs that are marked as Addressable will be added.
        /// </summary>
        public static void Generate(AddressableNetworkPrefabs target)
        {
            if (!target) return;
            if (_generating) return;
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) return;

            _generating = true;
            try
            {
                var found = target.folder is SceneAsset
                    ? AssetScannerUtility.ScanScenePrefabs(AssetDatabase.GetAssetPath(target.folder), true)
                    : AssetScannerUtility.ScanPrefabs(target.folder, true, target.searchAllIfNoFolder);
                var linkedGuids = AssetScannerUtility.CollectLinkedAddressablePrefabGuids(target);
                if (linkedGuids.Count > 0)
                    found.RemoveAll(scan => linkedGuids.Contains(scan.guid));

                if (found.Count == 0 && target.folder == null && !target.searchAllIfNoFolder)
                    return;

                bool changed = AssetScannerUtility.RemoveAddressableEntriesByGuid(target, linkedGuids);
                var existingGuids = target.GetExistingGuids();

                foreach (var scan in found)
                {
                    if (existingGuids.Contains(scan.guid)) continue;

                    var settings = AddressableAssetSettingsDefaultObject.Settings;
                    if (settings == null) continue;

                    var addressableEntry = settings.FindAssetEntry(scan.guid);
                    if (addressableEntry == null) continue;

                    var assetRef = new AssetReferenceGameObject(scan.guid);
                    target.AddEntry(assetRef);
                    changed = true;
                }

                if (changed)
                {
                    target.Refresh();
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssetIfDirty(target);
                }
            }
            catch (Exception e)
            {
                PurrNet.Logging.PurrLogger.LogError($"An error occurred during addressable prefab generation: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                _generating = false;
            }
        }

    }
}
#endif
