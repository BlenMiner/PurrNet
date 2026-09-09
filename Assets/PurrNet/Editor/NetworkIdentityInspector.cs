using System.Collections.Generic;
using System.Reflection;
using PurrNet.Contributors;
using PurrNet.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PurrNet.Editor
{
    struct Contributor
    {
        public string name;
        public string url;
    }

    [CustomEditor(typeof(NetworkIdentity), true)]
    [CanEditMultipleObjects]
#if TRI_INSPECTOR_PACKAGE
    public class NetworkIdentityInspector : TriInspector.Editors.TriEditor
#elif ODIN_INSPECTOR
    public class NetworkIdentityInspector : Sirenix.OdinInspector.Editor.OdinEditor
#else
    public class NetworkIdentityInspector : UnityEditor.Editor
#endif
    {
        private SerializedProperty _networkRules;
        private SerializedProperty _visitiblityRules;
        private readonly List<Contributor> _contributors = new ();
        private readonly List<Contributor> _courtesyOf = new ();
        private string _docsUrl;
        private static Texture2D _helpIcon;

#if TRI_INSPECTOR_PACKAGE || ODIN_INSPECTOR
        protected override void OnEnable()
#else
        protected virtual void OnEnable()
#endif
        {
#if TRI_INSPECTOR_PACKAGE
            base.OnEnable();
#endif
            try
            {
                _networkRules = serializedObject.FindProperty("_networkRules");
                _visitiblityRules = serializedObject.FindProperty("_visitiblityRules");
            }
            catch
            {
                // ignored
            }

            if (target)
            {
                try
                {
                    var targetType = target.GetType();
                    var attributes = targetType.GetCustomAttributes(typeof(ContributorAttribute), true);
                    var courtesy = targetType.GetCustomAttributes(typeof(CourtesyOfAttribute), true);

                    for (var i = 0; i < attributes.Length; i++)
                    {
                        var attr = (ContributorAttribute)attributes[i];

                        if (attr == null)
                            continue;

                        _contributors.Add(new Contributor
                        {
                            name = attr.name,
                            url = attr.url
                        });
                    }

                    for (var i = 0; i < courtesy.Length; i++)
                    {
                        var attr = (CourtesyOfAttribute)courtesy[i];
                        if (attr == null)
                            continue;
                        _courtesyOf.Add(new Contributor
                        {
                            name = attr.name,
                            url = attr.url
                        });
                    }

                    var docsAttr = targetType.GetCustomAttribute<PurrDocsAttribute>();
                    if (docsAttr != null)
                        _docsUrl = docsAttr.url;
                }
                catch (System.IO.FileNotFoundException)
                {
                    // Reflection on types that reference .NET 6 assemblies can fail in Unity's runtime
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                }
            }
        }

        public override VisualElement CreateInspectorGUI()
        {
            return null;
        }

        public override void OnInspectorGUI()
        {
            var identity = target as NetworkIdentity;

            if (identity == null)
            {
                DrawDefaultInspector();
                return;
            }

            bool hasNetworkManagerAsChild = identity.GetComponentInChildren<NetworkManager>();

            if (hasNetworkManagerAsChild)
                EditorGUILayout.HelpBox("NetworkIdentity is a child of a NetworkManager. This is not supported.",
                    MessageType.Error);

            try
            {
                base.OnInspectorGUI();
            }
            catch (System.IO.FileNotFoundException)
            {
                DrawDefaultInspectorFallback();
            }
            catch (System.Reflection.ReflectionTypeLoadException)
            {
                DrawDefaultInspectorFallback();
            }

            DrawIdentityInspector();
            GUI.enabled = true;
            DrawPurrButtons(identity);
            DrawContributors();

            serializedObject.ApplyModifiedProperties();

            /*if (!string.IsNullOrEmpty(_docsUrl))
            {
                _helpIcon ??= Resources.Load("purricon") as Texture2D;
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent(" Open Docs", _helpIcon), GUILayout.ExpandWidth(false), GUILayout.Height(20)))
                    Application.OpenURL("https://purrnet.dev/docs/" + _docsUrl);
                GUILayout.EndHorizontal();
            }*/
        }

        private void DrawContributors()
        {
            if (_courtesyOf.Count > 0)
            {
                GUILayout.BeginVertical("helpbox");

                foreach (var contributor in _courtesyOf)
                {
                    if (string.IsNullOrEmpty(contributor.url))
                        continue;
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Courtesy of", EditorStyles.label, GUILayout.ExpandWidth(false)))
                        Application.OpenURL(contributor.url);
                    GUI.color = new Color(0.6901961f, 0.8784314f, 0.9019608f, 1f);
                    if (GUILayout.Button(contributor.name, EditorStyles.label, GUILayout.ExpandWidth(false)))
                        Application.OpenURL(contributor.url);
                    GUI.color = Color.white;
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }

            if (_contributors.Count == 0)
                return;

            GUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Contributors");
            GUILayout.FlexibleSpace();
            foreach (var contributor in _contributors)
            {
                if (string.IsNullOrEmpty(contributor.url))
                    continue;

                GUILayout.Label("@", GUILayout.ExpandWidth(false));
                if (GUILayout.Button(contributor.name, EditorStyles.linkLabel, GUILayout.ExpandWidth(false)))
                    Application.OpenURL(contributor.url);
            }
            EditorGUILayout.EndHorizontal();
        }

        protected void DrawIdentityInspector()
        {
            GUILayout.Space(5);

            var identities = targets.Length;
            var identity = (NetworkIdentity)target;

            if (!identity)
            {
                EditorGUILayout.LabelField("Invalid identity");
                return;
            }

            HandleOverrides(identity, identities > 1);
            HandleStatus(identity, identities > 1);
        }

        private bool _foldoutVisible;

        private void HandleOverrides(NetworkIdentity identity, bool multi)
        {
            bool anySpawned = false;
            if (multi)
            {
                foreach (var t in targets)
                {
                    if (t is NetworkIdentity ni && ni.isSpawned)
                    {
                        anySpawned = true;
                        break;
                    }
                }
            }
            else
            {
                anySpawned = identity.isSpawned;
            }

            string label = "Override Defaults";

            if (multi)
            {
                bool networkRulesMixed = _networkRules.hasMultipleDifferentValues;
                bool visibilityRulesMixed = _visitiblityRules.hasMultipleDifferentValues;

                bool isNetworkRulesOverridden = _networkRules.objectReferenceValue != null;
                bool isVisibilityRulesOverridden = _visitiblityRules.objectReferenceValue != null;

                int overriddenCount = (isNetworkRulesOverridden ? 1 : 0) + (isVisibilityRulesOverridden ? 1 : 0);

                if (networkRulesMixed || visibilityRulesMixed || overriddenCount > 0)
                {
                    label += " (";
                    label += networkRulesMixed ? "P*" : (isNetworkRulesOverridden ? "P" : "");
                    if ((networkRulesMixed || isNetworkRulesOverridden) && (visibilityRulesMixed || isVisibilityRulesOverridden))
                        label += ",";
                    label += visibilityRulesMixed ? "V*" : (isVisibilityRulesOverridden ? "V" : "");
                    label += ")";
                }
            }
            else
            {
                bool isNetworkRulesOverridden = _networkRules.objectReferenceValue != null;
                bool isVisibilityRulesOverridden = _visitiblityRules.objectReferenceValue != null;

                int overridenCount = (isNetworkRulesOverridden ? 1 : 0) + (isVisibilityRulesOverridden ? 1 : 0);

                if (overridenCount > 0)
                {
                    label += " (";

                    if (isNetworkRulesOverridden)
                        label += overridenCount > 1 ? "P," : "P";

                    if (isVisibilityRulesOverridden)
                        label += "V";

                    label += ")";
                }
            }

            var old = GUI.enabled;
            _foldoutVisible = EditorGUILayout.BeginFoldoutHeaderGroup(_foldoutVisible, label);

            if (_foldoutVisible)
            {
                GUI.enabled = !anySpawned;
                EditorGUI.indentLevel++;

                EditorGUI.showMixedValue = _networkRules.hasMultipleDifferentValues;
                EditorGUILayout.PropertyField(_networkRules, new GUIContent("Permissions Override"));
                EditorGUI.showMixedValue = false;

                EditorGUI.showMixedValue = _visitiblityRules.hasMultipleDifferentValues;
                EditorGUILayout.PropertyField(_visitiblityRules, new GUIContent("Visibility Override"));
                EditorGUI.showMixedValue = false;

                EditorGUI.indentLevel--;
                GUI.enabled = old;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private bool _debuggingVisible;
        private bool _observersVisible;

        private void HandleStatus(NetworkIdentity identity, bool multi)
        {
            if (multi)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.LabelField("...");
                EditorGUILayout.EndHorizontal();
            }
            else if (identity.isSpawned)
            {
                if (identity.isServer)
                {
                    var old = GUI.enabled;
                    GUI.enabled = true;
                    PrintObserversDropdown(identity);
                    GUI.enabled = old;
                }

                EditorGUILayout.BeginHorizontal("box", GUILayout.ExpandWidth(false));
                GUILayout.Label($"ID: {identity.id}", GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Owner ID: {(identity.owner.HasValue ? identity.owner.Value.ToString() : "None")}",
                    GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    $"Local Player: {(identity.localPlayer.HasValue ? identity.localPlayer.Value.ToString() : "None")}",
                    GUILayout.ExpandWidth(false));
                EditorGUILayout.EndHorizontal();
            }
            else if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal("box");
                EditorGUILayout.LabelField("Not Spawned");
                EditorGUILayout.EndHorizontal();
            }

#if PURRNET_DEBUG_NETWORK_IDENTITY
            var old2 = GUI.enabled;
            GUI.enabled = false;

            EditorGUILayout.BeginVertical("box", GUILayout.ExpandWidth(false));

            EditorGUILayout.LabelField($"prefabId: {identity.scopedPrefabId}");
            EditorGUILayout.LabelField($"componentIndex: {identity.componentIndex}");
            EditorGUILayout.LabelField($"shouldBePooled: {identity.shouldBePooled}");
            EditorGUILayout.ObjectField("parent", identity.parent, typeof(NetworkIdentity), true);

            string path = "";

            var invertedPath = identity.invertedPathToNearestParent;

            for (var index = 0; index < invertedPath.Length; index++)
            {
                var parent = invertedPath[index];
                bool isLast = index == invertedPath.Length - 1;
                path += parent + (isLast ? ";" : " -> ");
            }

            EditorGUILayout.LabelField($"pathToNearestParent: {path}");
            EditorGUILayout.LabelField($"Direct Children ({identity.directChildren?.Count ?? 0}):");

            if (identity.directChildren != null)
            {
                EditorGUI.indentLevel++;
                foreach (var child in identity.directChildren)
                {
                    EditorGUILayout.ObjectField(child, typeof(NetworkIdentity), true);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            GUI.enabled = old2;
#endif
        }

        private void PrintObserversDropdown(NetworkIdentity identity)
        {
            _observersVisible =
                EditorGUILayout.BeginFoldoutHeaderGroup(_observersVisible, $"Observers ({identity.observers.Count})");

            if (_observersVisible)
            {
                EditorGUI.indentLevel++;
                foreach (var observer in identity.observers)
                {
                    EditorGUILayout.BeginHorizontal("box");
                    EditorGUILayout.LabelField(observer.ToString());
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDefaultInspectorFallback()
        {
            serializedObject.UpdateIfRequiredOrScript();
            var iterator = serializedObject.GetIterator();
            for (bool enterChildren = true; iterator.NextVisible(enterChildren); enterChildren = false)
            {
                try
                {
                    EditorGUILayout.PropertyField(iterator, true);
                }
                catch (System.IO.FileNotFoundException)
                {
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                }
            }
        }

        protected void DrawPurrButtons(NetworkIdentity targetIdentity)
        {
            if (targetIdentity == null)
                return;

            try
            {
                DrawPurrButtonsInternal(targetIdentity);
            }
            catch (System.IO.FileNotFoundException)
            {
            }
            catch (System.Reflection.ReflectionTypeLoadException)
            {
            }
        }

        private void DrawPurrButtonsInternal(NetworkIdentity targetIdentity)
        {
            GUILayout.Space(5);

            var methods = targetIdentity.GetType().GetMethods(
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            bool foundAnyButtons = false;

            foreach (var method in methods)
            {
                PurrButtonAttribute buttonAttr;
                try
                {
                    buttonAttr = method.GetCustomAttribute<PurrButtonAttribute>();
                }
                catch (System.IO.FileNotFoundException)
                {
                    continue;
                }
                catch (System.Reflection.ReflectionTypeLoadException)
                {
                    continue;
                }

                if (buttonAttr != null)
                {
                    if (!foundAnyButtons)
                    {
                        EditorGUILayout.LabelField("PurrButtons", EditorStyles.boldLabel);
                        foundAnyButtons = true;
                    }

                    string buttonName = !string.IsNullOrEmpty(buttonAttr.ButtonName)
                        ? buttonAttr.ButtonName
                        : ObjectNames.NicifyVariableName(method.Name);

                    if (GUILayout.Button(buttonName))
                    {
                        var parameters = method.GetParameters();

                        if (parameters.Length == 0)
                        {
                            method.Invoke(targetIdentity, null);
                        }
                        else
                        {
                            Debug.LogWarning($"Cannot invoke method '{method.Name}' with PurrButton attribute because it has parameters. PurrButton only works with parameterless methods.");
                        }
                    }
                }
            }
        }
    }
}
