using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace PurrNet
{
    internal static class PurrInternalTelemetry
    {
        const string ENDPOINT = "https://purrnet.dev/api/telemetry/event";
        const string INSTALL_ID_KEY = "PurrNet_InstallId";
        static string _installationId;
        static bool _projectIdLoaded;

        internal static string ProjectId { get; set; }

        internal static Dictionary<string, string> TransportMetadata { get; } = new();

        internal static bool IsEnabled
        {
            get
            {
#if PURRNET_NO_TELEMETRY
                return false;
#else
                if (Application.isBatchMode)
                    return false;

                if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true",
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                return true;
#endif
            }
        }

        static string GetInstallationId()
        {
            if (!string.IsNullOrEmpty(_installationId))
                return _installationId;

            _installationId = PlayerPrefs.GetString(INSTALL_ID_KEY, "");

            if (string.IsNullOrEmpty(_installationId))
            {
                _installationId = Guid.NewGuid().ToString();
                PlayerPrefs.SetString(INSTALL_ID_KEY, _installationId);
                PlayerPrefs.Save();
            }

            return _installationId;
        }

        internal static async void SendEvent(string eventType, Dictionary<string, object> metadata)
        {
            try
            {
                if (!IsEnabled)
                    return;

                LoadProjectIdFromResources();

                var payload = new Dictionary<string, object>
                {
                    ["installation_id"] = GetInstallationId(),
                    ["event_type"] = eventType,
#if UNITY_EDITOR
                    ["source"] = "editor",
#else
                    ["source"] = "build",
#endif
                    ["metadata"] = metadata
                };

                if (!string.IsNullOrEmpty(ProjectId))
                    payload["project_id"] = ProjectId;

                var json = PurrJson.Serialize(payload);
                var bodyRaw = Encoding.UTF8.GetBytes(json);

                var request = new UnityWebRequest(ENDPOINT, "POST");
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                var tcs = new TaskCompletionSource<bool>();
                var op = request.SendWebRequest();
                op.completed += _ => tcs.TrySetResult(true);
                await tcs.Task;

                request.Dispose();
            }
            catch
            {
                // Silent failure, we don't want to disrupt Unity because of telemetry :-)
            }
        }

        static void LoadProjectIdFromResources()
        {
            if (_projectIdLoaded || !string.IsNullOrEmpty(ProjectId))
                return;

            _projectIdLoaded = true;

            try
            {
                if (ApplicationConstants.TryGet("projectId", out var projectId))
                    ProjectId = projectId.Trim();
            }
            catch
            {
                // Not critical
            }
        }

        internal static void SendConnectionEvent(NetworkManager manager)
        {
            if (!IsEnabled)
                return;

            var transportName = manager.transport ? manager.transport.GetType().Name : "Unknown";

            if (transportName == nameof(LocalTransport))
                return;

            var metadata = new Dictionary<string, object>
            {
                ["purrnet_version"] = NetworkManager.version,
                ["player_count"] = manager.playerCount,
                ["transport"] = transportName
            };

            foreach (var kvp in TransportMetadata)
                metadata[kvp.Key] = kvp.Value;

            SendEvent("connection", metadata);
        }
    }
}
