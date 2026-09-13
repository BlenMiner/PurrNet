using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace PurrNet.Editor
{
    /// <summary>
    /// Reusable cached user profile (avatar + username) fetched from /api/me.
    /// Subscribe to <see cref="PurrPackageManagerAuth.onAuthChanged"/> to call <see cref="Refresh"/>.
    /// </summary>
    public class PurrUserProfile
    {
        private UserInfo _userInfo;
        private Texture2D _avatar;
        private bool _isFetching;

        public UserInfo Info => _userInfo;
        public Texture2D Avatar => _avatar;
        public bool IsLoggedIn => PurrPackageManagerAuth.HasApiKey();

        private readonly Action _repaint;

        public PurrUserProfile(Action repaint)
        {
            _repaint = repaint;
        }

        public void Refresh()
        {
            _userInfo = null;
            _avatar = null;

            if (IsLoggedIn)
                FetchProfile();
        }

        public void Clear()
        {
            _userInfo = null;
            _avatar = null;
        }

        private async void FetchProfile()
        {
            if (_isFetching) return;
            _isFetching = true;

            try
            {
                var result = await PurrPackageManagerAPI.GetMe(PurrPackageManagerAuth.GetApiKey());
                if (!result.Success)
                    return;

                _userInfo = result.Value;
                _repaint?.Invoke();

                if (!string.IsNullOrEmpty(_userInfo.AvatarUrl))
                    FetchAvatar(_userInfo.AvatarUrl);
            }
            finally
            {
                _isFetching = false;
            }
        }

        private async void FetchAvatar(string url)
        {
            try
            {
                var request = UnityWebRequestTexture.GetTexture(url);
                await request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success) return;

                _avatar = DownloadHandlerTexture.GetContent(request);
                _repaint?.Invoke();
            }
            catch
            {
                // avatar is non-critical
            }
        }

        /// <summary>
        /// Draws a compact user profile bar: [avatar] username | Logout
        /// Returns the total width consumed so callers can layout around it.
        /// </summary>
        public float DrawProfileBar(Rect anchor, GUIStyle smallLabel = null)
        {
            if (!IsLoggedIn)
            {
                var loginRect = new Rect(anchor.xMax - 58, anchor.y, 58, anchor.height);
                if (GUI.Button(loginRect, "Login"))
                    PurrPackageManagerAuth.Login();
                var keyRect = new Rect(loginRect.x - 108, anchor.y, 104, anchor.height);
                if (GUI.Button(keyRect, new GUIContent("Paste API Key", "Open a field to enter your Unity API key")))
                    PurrPackageManagerAuth.PasteApiKey();
                return 166;
            }

            const float logoutW = 58;
            const float avatarSize = 20;
            const float padding = 4;

            float usedWidth = logoutW;

            // Logout button (rightmost)
            var logoutRect = new Rect(anchor.xMax - logoutW, anchor.y, logoutW, anchor.height);
            if (GUI.Button(logoutRect, "Logout"))
                PurrPackageManagerAuth.Logout();

            if (_userInfo != null)
            {
                var style = smallLabel ?? EditorStyles.miniLabel;
                var nameContent = new GUIContent(_userInfo.Username ?? _userInfo.Id);
                float nameW = style.CalcSize(nameContent).x;

                float nameX = logoutRect.x - nameW - padding;
                var nameRect = new Rect(nameX, anchor.y, nameW, anchor.height);
                GUI.Label(nameRect, nameContent, style);
                usedWidth += nameW + padding;

                if (_avatar != null)
                {
                    float avatarX = nameX - avatarSize - padding;
                    var avatarRect = new Rect(avatarX, anchor.y + (anchor.height - avatarSize) * 0.5f, avatarSize, avatarSize);
                    float radius = avatarSize * 0.5f;
                    GUI.DrawTexture(avatarRect, _avatar, ScaleMode.ScaleToFit, true, 1f, Color.white, Vector4.zero, new Vector4(radius, radius, radius, radius));
                    usedWidth += avatarSize + padding;
                }
            }

            return usedWidth;
        }
    }
}
