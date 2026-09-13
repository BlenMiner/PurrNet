using System;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    internal class PurrPackageManagerLoginWindow : EditorWindow
    {
        private const float DialogWidth = 440;

        [NonSerialized] private string _key = "";
        private string _error;
        private bool _isSubmitting;
        private bool _focusKey;
        private int _submission;
        private Vector2 _contentSize;

        internal static void Open()
        {
            var window = GetWindow<PurrPackageManagerLoginWindow>(true, "PurrNet API Key", true);
            window._focusKey = true;
            window.ShowUtility();
            if (window._contentSize == Vector2.zero)
                window.SetContentHeight(200);

            var windowPosition = window.position;
            windowPosition.center = EditorGUIUtility.GetMainWindowPosition().center;
            window.position = windowPosition;
        }

        private void OnEnable()
        {
            _contentSize = Vector2.zero;
            PurrPackageManagerAuth.onAuthChanged += Close;
        }

        private void OnDisable()
        {
            PurrPackageManagerAuth.onAuthChanged -= Close;
            _submission++;
            _isSubmitting = false;
            _key = "";
        }

        private void OnGUI()
        {
            var content = new EditorGUILayout.VerticalScope(EditorStyles.inspectorDefaultMargins, GUILayout.ExpandHeight(false));
            using (content)
            {
                GUILayout.Space(12);
                GUILayout.Label("Sign in with an API key", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Paste your Unity API key below, then click Sign in.", EditorStyles.wordWrappedLabel);
                GUILayout.Space(12);

                using (new EditorGUI.DisabledScope(_isSubmitting))
                {
                    GUI.SetNextControlName("PurrNetApiKey");
                    EditorGUI.BeginChangeCheck();
                    _key = EditorGUILayout.PasswordField("API key", _key);
                    if (EditorGUI.EndChangeCheck())
                        _error = null;

                    if (_focusKey)
                    {
                        EditorGUI.FocusTextInControl("PurrNetApiKey");
                        _focusKey = false;
                    }
                }

                if (!string.IsNullOrEmpty(_error))
                    EditorGUILayout.HelpBox(_error, MessageType.Error);

                GUILayout.Space(8);
                if (GUILayout.Button("Get API key on purrnet.dev", GUILayout.Height(24)))
                    Application.OpenURL("https://purrnet.dev/profile?tab=api-keys");

                GUILayout.Space(12);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Cancel", GUILayout.Height(26)))
                    {
                        Close();
                        GUIUtility.ExitGUI();
                    }

                    using (new EditorGUI.DisabledScope(_isSubmitting || string.IsNullOrWhiteSpace(_key)))
                    {
                        if (GUILayout.Button(_isSubmitting ? "Signing in..." : "Sign in", GUILayout.Height(26)))
                            SignIn();
                    }
                }
                GUILayout.Space(12);
            }

            if (Event.current.type == EventType.Repaint && content.rect.height > 0)
                SetContentHeight(Mathf.Ceil(content.rect.yMax));

            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
            {
                Event.current.Use();
                SignIn();
            }
        }

        private void SetContentHeight(float height)
        {
            var size = new Vector2(DialogWidth, height);
            if (_contentSize == size)
                return;

            _contentSize = size;
            // Changing size constraints can move the native window, so capture its center first.
            var targetPosition = new Rect(position.center - size * 0.5f, size);
            if (size.x > maxSize.x || size.y > maxSize.y)
            {
                maxSize = size;
                minSize = size;
            }
            else
            {
                minSize = size;
                maxSize = size;
            }
            position = targetPosition;
        }

        private async void SignIn()
        {
            string key = _key.Trim();
            if (_isSubmitting || string.IsNullOrEmpty(key))
                return;

            if (!System.Text.RegularExpressions.Regex.IsMatch(key, @"\Apk_[0-9a-fA-F]{64}\z"))
            {
                _error = "Enter a valid Unity API key from purrnet.dev.";
                Repaint();
                return;
            }

            PurrPackageManagerAuth.CancelLogin();
            int authAttempt = PurrPackageManagerAuth.LoginAttempt;
            int submission = ++_submission;
            _isSubmitting = true;
            _error = null;
            Repaint();

            var result = await PurrPackageManagerAPI.GetMe(key, 30);
            // Closing the dialog cancels its pending submission without changing the saved key.
            if (this == null || submission != _submission)
                return;

            _isSubmitting = false;
            if (authAttempt != PurrPackageManagerAuth.LoginAttempt)
            {
                _error = "Another login attempt was started. Click Sign in to try this key again.";
                Repaint();
                return;
            }

            if (!result.Success || result.Value == null || string.IsNullOrEmpty(result.Value.Id))
            {
                _error = "Could not sign in. Check your Unity API key and internet connection, then try again.";
                Repaint();
                return;
            }

            PurrPackageManagerAuth.SetApiKey(key);
        }
    }
}
