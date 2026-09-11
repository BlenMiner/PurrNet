using System;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(StatisticsManager), true)]
    public class StatisticsManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty _scriptProp;
        private SerializedProperty _placementProp;
        private SerializedProperty _displayTypeProp;
        private SerializedProperty _displayTargetProp;
        private SerializedProperty _fontSizeProp;
        private SerializedProperty _textColorProp;
        private SerializedProperty _highPingThresholdProp;
        private SerializedProperty _highPingRecoveryThresholdProp;
        private SerializedProperty _highJitterThresholdProp;
        private SerializedProperty _highJitterRecoveryThresholdProp;
        private SerializedProperty _highPacketLossThresholdProp;
        private SerializedProperty _highPacketLossRecoveryThresholdProp;
        private SerializedProperty _qualityChangeDurationProp;
        private SerializedProperty _connectionStallThresholdProp;
        private bool _displaySettingsFoldout = true;
        private bool _networkConditionsFoldout;

        private void OnEnable()
        {
            _scriptProp = serializedObject.FindProperty("m_Script");
            _placementProp = serializedObject.FindProperty("placement");
            _displayTypeProp = serializedObject.FindProperty("_displayType");
            _displayTargetProp = serializedObject.FindProperty("_displayTarget");
            _fontSizeProp = serializedObject.FindProperty("fontSize");
            _textColorProp = serializedObject.FindProperty("textColor");
            _highPingThresholdProp = serializedObject.FindProperty("_highPingThreshold");
            _highPingRecoveryThresholdProp = serializedObject.FindProperty("_highPingRecoveryThreshold");
            _highJitterThresholdProp = serializedObject.FindProperty("_highJitterThreshold");
            _highJitterRecoveryThresholdProp = serializedObject.FindProperty("_highJitterRecoveryThreshold");
            _highPacketLossThresholdProp = serializedObject.FindProperty("_highPacketLossThreshold");
            _highPacketLossRecoveryThresholdProp = serializedObject.FindProperty("_highPacketLossRecoveryThreshold");
            _qualityChangeDurationProp = serializedObject.FindProperty("_qualityChangeDuration");
            _connectionStallThresholdProp = serializedObject.FindProperty("_connectionStallThreshold");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var statisticsManager = (StatisticsManager)target;

            GUI.enabled = false;
            EditorGUILayout.PropertyField(_scriptProp, true);
            GUI.enabled = true;

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Collection Settings", EditorStyles.boldLabel);
            statisticsManager.checkInterval =
                EditorGUILayout.Slider("Check Rate In Seconds", statisticsManager.checkInterval, 0.05f, 1f);

            GUILayout.Space(10);
            _networkConditionsFoldout = EditorGUILayout.Foldout(_networkConditionsFoldout, "Network Condition Settings", true);
            if (_networkConditionsFoldout)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.HelpBox("These settings control when the network condition callbacks are triggered.", MessageType.Info);

                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(_highPingThresholdProp, new GUIContent("High Ping Threshold"));
                EditorGUILayout.PropertyField(_highPingRecoveryThresholdProp, new GUIContent("High Ping Recovery"));
                EditorGUILayout.PropertyField(_highJitterThresholdProp, new GUIContent("High Jitter Threshold"));
                EditorGUILayout.PropertyField(_highJitterRecoveryThresholdProp, new GUIContent("High Jitter Recovery"));
                EditorGUILayout.PropertyField(_highPacketLossThresholdProp, new GUIContent("Packet Loss Threshold"));
                EditorGUILayout.PropertyField(_highPacketLossRecoveryThresholdProp, new GUIContent("Packet Loss Recovery"));
                EditorGUILayout.PropertyField(_qualityChangeDurationProp, new GUIContent("Change Duration"));
                EditorGUILayout.PropertyField(_connectionStallThresholdProp, new GUIContent("Connection Stall Threshold"));

                if (EditorGUI.EndChangeCheck())
                    serializedObject.ApplyModifiedProperties();

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);
            _displaySettingsFoldout = EditorGUILayout.Foldout(_displaySettingsFoldout, "Display Settings", true);
            if (_displaySettingsFoldout)
            {
                EditorGUI.indentLevel++;

                EditorGUI.BeginChangeCheck();
                _placementProp.enumValueIndex =
                    (int)(StatisticsManager.StatisticsPlacement)EditorGUILayout.EnumPopup("Placement",
                        (StatisticsManager.StatisticsPlacement)_placementProp.enumValueIndex);
                _displayTypeProp.intValue =
                    (int)(StatisticsManager.StatisticsDisplayType)EditorGUILayout.EnumFlagsField("Display Type",
                        (StatisticsManager.StatisticsDisplayType)_displayTypeProp.intValue);
                _displayTargetProp.intValue =
                    (int)(StatisticsManager.StatisticsDisplayTarget)EditorGUILayout.EnumFlagsField("Display Target",
                        (StatisticsManager.StatisticsDisplayTarget)_displayTargetProp.intValue);

                float newFontSize = EditorGUILayout.Slider("Font Size", _fontSizeProp.floatValue, 8f, 32f);
                if (Math.Abs(newFontSize - _fontSizeProp.floatValue) > 0.01f)
                {
                    _fontSizeProp.floatValue = newFontSize;
                }

                EditorGUILayout.PropertyField(_textColorProp, new GUIContent("Text Color"));

                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                }

                EditorGUI.indentLevel--;
            }

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Statistics Preview", EditorStyles.boldLabel);
            RenderStatistics(statisticsManager);

            serializedObject.ApplyModifiedProperties();
            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }

            Repaint();
        }

        private void RenderStatistics(StatisticsManager statisticsManager)
        {
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("Client connection", statisticsManager.clientConnectionDescription,
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("Server connection", statisticsManager.serverConnectionDescription,
                    EditorStyles.wordWrappedLabel);
                GUILayout.Space(4);
            }

            if (!statisticsManager.connectedServer && !statisticsManager.connectedClient)
            {
                EditorGUILayout.LabelField("Awaiting connection");
                return;
            }

            if (statisticsManager.connectedClient)
            {
                GUILayout.BeginHorizontal();
                DrawLed(GetPingStatus(statisticsManager));
                EditorGUILayout.LabelField($"Ping:");
                EditorGUILayout.LabelField($"{statisticsManager.ping}ms");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                DrawLed(GetJitterStatus(statisticsManager));
                EditorGUILayout.LabelField($"Jitter:");
                EditorGUILayout.LabelField($"{statisticsManager.jitter}ms");
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                DrawLed(GetPacketLossStatus(statisticsManager));
                EditorGUILayout.LabelField($"Packet Loss:");
                EditorGUILayout.LabelField($"{statisticsManager.packetLoss}%");
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            DrawLed(Status.green);
            EditorGUILayout.LabelField($"Upload:");
            EditorGUILayout.LabelField($"{statisticsManager.upload}KB/s");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawLed(Status.green);
            EditorGUILayout.LabelField($"Download:");
            EditorGUILayout.LabelField($"{statisticsManager.download}KB/s");
            GUILayout.EndHorizontal();
        }

        private static Status GetPingStatus(StatisticsManager statisticsManager)
        {
            return statisticsManager.ping switch
            {
                < 50 => Status.green,
                < 100 => Status.yellow,
                < 200 => Status.orange,
                _ => Status.red
            };
        }

        private Status GetJitterStatus(StatisticsManager statisticsManager)
        {
            if (statisticsManager.jitter < 10)
                return Status.green;
            if (statisticsManager.jitter < 20)
                return Status.yellow;
            if (statisticsManager.jitter < 40)
                return Status.orange;
            return Status.red;
        }

        private Status GetPacketLossStatus(StatisticsManager statisticsManager)
        {
            if (statisticsManager.packetLoss < 11)
                return Status.green;
            if (statisticsManager.packetLoss < 21)
                return Status.yellow;
            if (statisticsManager.packetLoss < 31)
                return Status.orange;
            return Status.red;
        }

        static void DrawLed(Status status)
        {
            var white = Texture2D.whiteTexture;
            var color = status switch
            {
                Status.green => Color.green,
                Status.yellow => Color.yellow,
                Status.orange => new Color(1, 0.5f, 0),
                _ => Color.red
            };

            GUILayout.Space(EditorGUIUtility.singleLineHeight);
            var rect = GUILayoutUtility.GetLastRect();
            rect.height = EditorGUIUtility.singleLineHeight;

            const float padding = 5;

            rect.x += padding;
            rect.y += padding;

            rect.width -= padding * 2;
            rect.height -= padding * 2;

            GUI.DrawTexture(rect, white, ScaleMode.StretchToFill, true, 1f, color, 0, 10f);
        }

        private enum Status
        {
            green,
            yellow,
            orange,
            red
        }
    }
}
