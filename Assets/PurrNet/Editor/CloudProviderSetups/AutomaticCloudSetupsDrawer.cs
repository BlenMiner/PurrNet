#pragma warning disable CS0162 // Unreachable code detected

using PurrNet.Transports;
using UnityEditor;
using UnityEngine;
// ReSharper disable HeuristicUnreachableCode

namespace PurrNet.Editor
{
    [CustomPropertyDrawer(typeof(AutomaticCloudSetups))]
    public class AutomaticCloudSetupsDrawer : PropertyDrawer
    {
        const float PADDING_TOP = 5f;
        const float PADDING_LEFT = 5f;
        const float PADDING_RIGHT = 5f;
        const float PADDING_TOP_BOTTOM = 5f;

        private const int PROVIDER_COUNT = 0
#if EDGEGAP_PURRNET_SUPPORT
                                           + 1
#endif
            ;

        private Texture2D _edgegapLogo;
        private GUIContent _edgegapLabel;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (PROVIDER_COUNT == 0)
                return 0;
            return PADDING_TOP + EditorGUIUtility.singleLineHeight * PROVIDER_COUNT + PADDING_TOP_BOTTOM * 2;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (PROVIDER_COUNT == 0)
                return;

            position.y += PADDING_TOP;
            position.height -= PADDING_TOP;

            EditorGUI.HelpBox(position, string.Empty, MessageType.None);

            position.y += PADDING_TOP_BOTTOM;
            position.x += PADDING_LEFT;
            position.width -= PADDING_LEFT + PADDING_RIGHT;
            position.height -= PADDING_TOP_BOTTOM * 2;

#if EDGEGAP_PURRNET_SUPPORT
            if (!_edgegapLogo)
                _edgegapLogo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    AssetDatabase.GUIDToAssetPath("3a588301050c6db448986e8ba1c2139f"));
            _edgegapLabel ??= new GUIContent("Auto Setup Edgegap Server Port", tooltip:
                "When inside edgegap, automatically setup port.\n" +
                "Edgegap is detected via environment variables.");
            var adaptToEdegegap = property.FindPropertyRelative("_adaptToEdgegap");
            DrawToggle(position, _edgegapLogo, _edgegapLabel, adaptToEdegegap);
#endif
        }

        private static void DrawToggle(
            Rect position,
            Texture2D logo,
            GUIContent label,
            SerializedProperty booleanProp
        )
        {
            const float iconSize = 16f;
            const float padding = 4f;
            const float toggleWidth = 16f;

            // Reserve top/bottom halves
            float lineHeight = position.height;
            var topRect = new Rect(position.x, position.y, position.width, lineHeight);

            // --- Layout (top line) ---
            var iconRect = new Rect(
                topRect.x,
                topRect.y + (topRect.height - iconSize) / 2,
                iconSize,
                iconSize
            );

            var toggleRect = new Rect(
                topRect.xMax - toggleWidth,
                topRect.y,
                toggleWidth,
                topRect.height
            );

            float labelStart = iconRect.xMax + padding;
            float labelWidth = toggleRect.x - labelStart - padding;
            var labelRect = new Rect(labelStart, topRect.y, labelWidth, topRect.height);

            // --- Draw top line (icon + label + toggle) ---
            EditorGUI.BeginProperty(topRect, label, booleanProp);

            if (logo)
                GUI.DrawTexture(iconRect, logo, ScaleMode.ScaleToFit);

            GUI.Label(labelRect, label);

            EditorGUI.BeginChangeCheck();
            bool newValue = EditorGUI.Toggle(toggleRect, booleanProp.boolValue);
            if (EditorGUI.EndChangeCheck())
                booleanProp.boolValue = newValue;

            EditorGUI.EndProperty();
        }
    }
}

#pragma warning restore CS0162 // Unreachable code detected
