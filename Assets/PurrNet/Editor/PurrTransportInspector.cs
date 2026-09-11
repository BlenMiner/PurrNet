using System;
using PurrNet.Transports;
using UnityEditor;
using UnityEngine;

namespace PurrNet.Editor
{
    [CustomEditor(typeof(PurrTransport), true)]
    public class PurrTransportInspector : UnityEditor.Editor
    {
        private static readonly GUIContent _hostRelayLabel = new("Host relay", "This host's connection to the relay.");

        private SerializedProperty _masterServer;
        private SerializedProperty _roomName;
        private SerializedProperty _region;
        private SerializedProperty _host;
        private SerializedProperty _timeoutInSeconds;
        private SerializedProperty _attemptDirectConnection;
        private SerializedProperty _natResolveTimeout;
        private SerializedProperty _webRtcStunServer;
        private bool _showDirectConnectionSettings;
        private SerializedProperty _networkSimulation;

        private bool _lookingForBestRegion;
        string[] _regions = Array.Empty<string>();
        string[] _hosts = Array.Empty<string>();

        void OnEnable()
        {
            _masterServer = serializedObject.FindProperty("_masterServer");
            _roomName = serializedObject.FindProperty("_roomName");
            _region = serializedObject.FindProperty("_region");
            _host = serializedObject.FindProperty("_host");
            _timeoutInSeconds = serializedObject.FindProperty("_timeoutInSeconds");
            _attemptDirectConnection = serializedObject.FindProperty("_useNat");
            _natResolveTimeout = serializedObject.FindProperty("_natResolveTimeout");
            _webRtcStunServer = serializedObject.FindProperty("_webRtcStunServer");
            _networkSimulation = serializedObject.FindProperty("_networkSimulation");

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                LoadRegions();
        }

        bool _loadingRegions;

        async void LoadRegions()
        {
            try
            {
                if (_loadingRegions)
                    return;

                _loadingRegions = true;
                var servers = await PurrTransportUtils.ActualGetRelayServersAsync(_masterServer.stringValue);

                if (servers.servers == null)
                {
                    _loadingRegions = false;
                    return;
                }

                _hosts = new string[servers.servers.Length];
                _regions = new string[servers.servers.Length];

                for (var i = 0; i < servers.servers.Length; i++)
                {
                    _hosts[i] = servers.servers[i].host;
                    _regions[i] = servers.servers[i].region;
                }

                _loadingRegions = false;
            }
            catch (Exception e)
            {
                _loadingRegions = false;
                Debug.LogException(e);
            }
        }

        int RegionId(string region, string host)
        {
            for (var i = 0; i < _regions.Length; i++)
            {
                if (_regions[i] == region)
                {
                    if (_hosts[i] != host)
                        return -1;
                    return i;
                }
            }

            return -1;
        }

        private async void FindBestRegion()
        {
            try
            {
                if (_lookingForBestRegion)
                    return;

                _lookingForBestRegion = true;

                var server = await PurrTransportUtils.ActualGetRelayServerAsync(_masterServer.stringValue);

                _region.stringValue = server.region;
                serializedObject.ApplyModifiedProperties();

                _lookingForBestRegion = false;
            }
            catch (Exception e)
            {
                _lookingForBestRegion = false;
                Debug.LogException(e);
            }
        }

        float _lastMasterServerUpdate;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var transport = (PurrTransport)target;

            var oldMasterServer = _masterServer.stringValue;
            EditorGUILayout.PropertyField(_masterServer);

            if (oldMasterServer != _masterServer.stringValue)
                _lastMasterServerUpdate = Time.realtimeSinceStartup;

            if (_lastMasterServerUpdate != 0 && Time.realtimeSinceStartup - _lastMasterServerUpdate > 1)
            {
                LoadRegions();
                _lastMasterServerUpdate = 0;
            }

            var server = _masterServer.stringValue;
            if (Uri.TryCreate(server, UriKind.Absolute, out var url) && url.Host.EndsWith("purrtransport.purrservers.com"))
            {
                EditorGUILayout.HelpBox("This server is meant for development use only.\n" +
                                        "Usage in production is strictly prohibited.\n" +
                                        "You need to host your own relay servers for production.", MessageType.Warning);
            }

            EditorGUILayout.PropertyField(_roomName);

            bool oldEnabled = GUI.enabled;
            if (_lookingForBestRegion || _loadingRegions || _lastMasterServerUpdate != 0)
                GUI.enabled = false;

            EditorGUILayout.BeginHorizontal();

            if (_regions.Length == 0)
            {
                bool enabled = GUI.enabled;
                GUI.enabled = false;
                EditorGUILayout.PropertyField(_region);
                GUI.enabled = enabled;
            }
            else
            {
                int region = RegionId(transport.region, transport.host);
                var newRegion = EditorGUILayout.Popup("Region", region, _regions);

                if (newRegion < 0 && _regions.Length > 0)
                    newRegion = 0;

                if (region != newRegion && newRegion >= 0 && newRegion < _regions.Length)
                {
                    _region.stringValue = _regions[newRegion];
                    _host.stringValue = _hosts[newRegion];
                }
            }

            if (GUILayout.Button("Find Best Region", GUILayout.ExpandWidth(false)))
                FindBestRegion();

            GUI.enabled = oldEnabled;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            GUILayout.Label(_host.stringValue);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(_timeoutInSeconds);
            EditorGUILayout.PropertyField(_attemptDirectConnection, new GUIContent("Connect Players Directly (P2P)",
                "Connect players straight to the host when possible, so game traffic skips the relay. " +
                "If a direct connection cannot be made, use the relay automatically. " +
                "Enable on both host and clients before connecting. " +
                "If an established direct connection is lost, that player disconnects."));

            if (_attemptDirectConnection.boolValue)
            {
                EditorGUI.indentLevel++;
                _showDirectConnectionSettings = EditorGUILayout.Foldout(_showDirectConnectionSettings,
                    "Advanced Direct Connection Settings", true);
                if (_showDirectConnectionSettings)
                {
                    EditorGUILayout.PropertyField(_natResolveTimeout, new GUIContent("UDP Connection Timeout",
                        "Seconds to attempt a direct UDP connection before using the relay. " +
                        "Keep this below the game's connection/authentication timeout."));
                    if (_natResolveTimeout.floatValue < 1f)
                        _natResolveTimeout.floatValue = 1f;
                    EditorGUILayout.PropertyField(_webRtcStunServer, new GUIContent("STUN Server",
                        "Helps WebRTC peers behind routers find a direct route. Leave empty for local-network discovery only."));
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(_networkSimulation, new GUIContent("Network Simulation (UDP)"));

            if (GUILayout.Button("Refresh"))
                LoadRegions();

            TransportInspector.DrawTransportStatus(transport);

            DrawConnectionMode(transport);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawConnectionMode(PurrTransport transport)
        {
            if (!Application.isPlaying)
                return;

            var clientLine = transport.clientLinkDescription;
            var hostLine = transport.hostLinkDescription;

            int total = transport.connections.Count;

            if (clientLine == null && hostLine == null && total == 0)
                return;

            EditorGUILayout.Space(4);

            if (clientLine != null)
                EditorGUILayout.LabelField("Client session", clientLine, EditorStyles.wordWrappedLabel);

            if (hostLine != null)
                EditorGUILayout.LabelField(_hostRelayLabel,
                    new GUIContent(hostLine), EditorStyles.wordWrappedLabel);

            if (total > 0)
            {
                EditorGUILayout.LabelField("Host links",
                    $"{transport.p2pConnectionCount} P2P / {total - transport.p2pConnectionCount} relay");

                foreach (var conn in transport.connections)
                {
                    var isP2p = transport.GetSessionLink(conn) == PurrTransport.SessionLink.P2P;
                    EditorGUILayout.LabelField($"    conn {conn.connectionId}",
                        isP2p ? $"P2P ({transport.GetConnectionProtocol(conn)})" : "Relay");
                }
            }
        }

        public override bool RequiresConstantRepaint() => Application.isPlaying;
    }
}
