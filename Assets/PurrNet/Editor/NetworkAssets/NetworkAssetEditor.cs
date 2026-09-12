#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

namespace PurrNet
{
    [CustomEditor(typeof(NetworkAssets))]
    public class NetworkAssetsEditor : UnityEditor.Editor
    {
        private NetworkAssets _target;
        private SerializedProperty _folderProp;
        private SerializedProperty _assetsProp;
        private SerializedProperty _linkedProp;
        private ReorderableList _reorderableList;
        private string _searchFilter = "";

        private const float SPACING = 8f;
        private const float INDEX_WIDTH = 30f;

        private bool _showTypeList;
        private string _typeSearch = "";
        private List<Type> _cachedTypes;
        private int _typePage = 0;
        private const int TypesPerPage = 10;

        private void OnEnable()
        {
            _cachedTypes = null;
            _target = (NetworkAssets)target;
            _folderProp = serializedObject.FindProperty("folder");
            _assetsProp = serializedObject.FindProperty("assets");
            _linkedProp = serializedObject.FindProperty("linkedNetworkAssets");

            if (_target.autoGenerate)
                _target.GenerateAssets();

            _cachedTypes = _target.AvailableTypeNames
                .Select(Type.GetType)
                .Where(t => t != null)
                .OrderByDescending(t => _target.enabledTypeNames.Contains(t.AssemblyQualifiedName))
                .ThenBy(t => t.Name)
                .ToList();

            SetupReorderableList();
        }

        private void SetupReorderableList()
        {
            _reorderableList = new ReorderableList(serializedObject, _assetsProp, true, true, true, true);
            _reorderableList.elementHeight = EditorGUIUtility.singleLineHeight;

            _reorderableList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(new Rect(rect.x, rect.y, INDEX_WIDTH, rect.height), "ID");
                EditorGUI.LabelField(new Rect(rect.x + INDEX_WIDTH + SPACING, rect.y, rect.width - INDEX_WIDTH - SPACING, rect.height), "Asset");
            };

            _reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = _assetsProp.GetArrayElementAtIndex(index);

                float x = rect.x;
                EditorGUI.LabelField(new Rect(x, rect.y, INDEX_WIDTH, rect.height), index.ToString());
                x += INDEX_WIDTH + SPACING;

                EditorGUI.BeginDisabledGroup(_target.autoGenerate);
                EditorGUI.PropertyField(new Rect(x, rect.y, rect.width - INDEX_WIDTH - SPACING, rect.height), element, GUIContent.none);
                EditorGUI.EndDisabledGroup();
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SharedAssetEditorUI.DrawHeader(
                "Network Assets",
                "This asset is used to store Unity objects (e.g., Materials, Sprites, Scriptables) " +
                "to reference them by index for efficient networking.");

            SharedAssetEditorUI.DrawGenerationSettingsTop(_folderProp, _target);

            GUILayout.BeginHorizontal();
            DrawToggleButton("Auto generate", ref _target.autoGenerate);
            GUILayout.EndHorizontal();

            SharedAssetEditorUI.DrawGenerateButton(() =>
            {
                _target.GenerateAssets();
                serializedObject.Update();
                _assetsProp = serializedObject.FindProperty("assets");
            });

            if (GUILayout.Button("Refresh Type List"))
            {
                _cachedTypes = GetTypesWithAssetsSorted(_target);
            }

            DrawTypeToggleFoldout();

            SharedAssetEditorUI.DrawLinkedField(_linkedProp);

            SharedAssetEditorUI.DrawEntryList(_reorderableList, _target.autoGenerate,
                ref _searchFilter, i =>
                {
                    if (i >= _assetsProp.arraySize) return null;
                    var obj = _assetsProp.GetArrayElementAtIndex(i).objectReferenceValue;
                    return obj ? obj.name : null;
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
            value = SharedAssetEditorUI.DrawToggleButton(label, value, _target);
        }

        private void DrawTypeToggleFoldout()
        {
            try
            {
                EditorGUILayout.BeginVertical("box");
                _showTypeList = EditorGUILayout.Foldout(_showTypeList, "Filter Types", true);
                if (!_showTypeList)
                {
                    EditorGUILayout.EndVertical();
                    return;
                }

                _typeSearch = EditorGUILayout.TextField("Search", _typeSearch);

                if (_cachedTypes == null)
                {
                    if (_target.AvailableTypeNames != null && _target.AvailableTypeNames.Count > 0)
                    {
                        _cachedTypes = _target.AvailableTypeNames
                            .Select(Type.GetType)
                            .Where(t => t != null)
                            .ToList();
                    }
                    else
                    {
                        _cachedTypes = GetTypesWithAssetsSorted(_target);
                    }
                }

                var filtered = string.IsNullOrEmpty(_typeSearch)
                    ? _cachedTypes
                    : _cachedTypes.Where(t => t.Name.IndexOf(_typeSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                int totalPages = Mathf.CeilToInt(filtered.Count / (float)TypesPerPage);
                _typePage = Mathf.Clamp(_typePage, 0, Mathf.Max(0, totalPages - 1));

                int start = _typePage * TypesPerPage;
                int end = Mathf.Min(start + TypesPerPage, filtered.Count);

                for (int i = start; i < end; i++)
                {
                    var type = filtered[i];
                    string typeName = type.AssemblyQualifiedName;
                    bool enabled = _target.enabledTypeNames.Contains(typeName);
                    bool newValue = EditorGUILayout.ToggleLeft(type.Name, enabled);

                    if (newValue != enabled)
                    {
                        _target.SetEnabledType(typeName, newValue);

                        _cachedTypes = _cachedTypes
                            .OrderByDescending(t => _target.enabledTypeNames.Contains(t.AssemblyQualifiedName))
                            .ThenBy(t => t.Name)
                            .ToList();

                        EditorUtility.SetDirty(_target);
                    }
                }

                EditorGUILayout.BeginHorizontal();
                GUI.enabled = _typePage > 0;
                if (GUILayout.Button("Prev")) _typePage--;
                GUI.enabled = _typePage < totalPages - 1;
                if (GUILayout.Button("Next")) _typePage++;
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                throw;
            }
        }

        private static List<Type> GetTypesWithAssetsSorted(NetworkAssets target)
        {
            string[] guids = AssetDatabase.FindAssets("t:Object");
            HashSet<Type> types = new();

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || path.StartsWith("Assets/") == false || path.EndsWith(".unity"))
                    continue;

                var all = AssetDatabase.LoadAllAssetsAtPath(path);

                foreach (var obj in all)
                {
                    if (!obj) continue;

                    Type type = obj.GetType();
                    while (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type))
                    {
                        types.Add(type);
                        type = type.BaseType;
                    }
                }
            }

            var sorted = types
                .OrderByDescending(t => target.enabledTypeNames.Contains(t.AssemblyQualifiedName))
                .ThenBy(t => t.Name)
                .ToList();

            target.CacheAvailableTypes(sorted);
            EditorUtility.SetDirty(target);

            return sorted;
        }
    }
}
#endif
