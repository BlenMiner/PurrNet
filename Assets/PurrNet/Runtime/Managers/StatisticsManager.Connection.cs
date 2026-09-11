using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    public partial class StatisticsManager
    {
        /// <summary>The local client's current route and protocol, or its connection state.</summary>
        public string clientConnectionDescription => GetConnectionDescription(
            _networkManager ? _networkManager.rawTransport : null, false);

        /// <summary>
        /// Active server transports. PurrTransport reports the local host-to-relay link.
        /// </summary>
        public string serverConnectionDescription => GetConnectionDescription(
            _networkManager ? _networkManager.rawTransport : null, true);

        private string _cachedClientConnectionText = "Client: Disconnected";
        private string _cachedServerConnectionText = "Server: Disconnected";
        private string _lastClientConnectionDescription;
        private string _lastServerConnectionDescription;
        private float _nextConnectionRefresh;
        private bool _cachedConnectedClient;
        private bool _cachedConnectedServer;
        private readonly GUIContent _connectionContent = new();
        private GUIStyle _connectionLabelStyle;
        private const float CONNECTION_WIDTH_MARGIN = 2f;

        private static string GetConnectionDescription(ITransport transport, bool asServer)
        {
            if (transport == null)
                return "Disconnected";

            var state = asServer ? transport.listenerState : transport.clientState;
            switch (state)
            {
                case ConnectionState.Connecting: return "Connecting";
                case ConnectionState.Disconnecting: return "Disconnecting";
                case ConnectionState.Disconnected: return "Disconnected";
            }

            if (!asServer)
            {
                var description = transport.clientLinkDescription;
                return string.IsNullOrEmpty(description) ? transport.GetType().Name : description;
            }

            switch (transport)
            {
                case PurrTransport purr:
                {
                    var description = purr.hostLinkDescription ?? "Disconnected";
                    int total = purr.connections.Count;
                    return total == 0 ? description
                        : $"{description}; clients: {purr.p2pConnectionCount} P2P / {total - purr.p2pConnectionCount} relay";
                }
                case CompositeTransport composite:
                {
                    string description = null;
                    foreach (var child in composite.transports)
                    {
                        var childTransport = child ? child.transport : null;
                        if (childTransport is not { listenerState: ConnectionState.Connected })
                            continue;
                        var childDescription = GetConnectionDescription(childTransport, true);
                        description = description == null ? childDescription : description + "; " + childDescription;
                    }

                    return description ?? "Disconnected";
                }
                default:
                    return transport.GetType().Name;
            }
        }

        private void UpdateConnectionStrings()
        {
            if (Time.unscaledTime < _nextConnectionRefresh &&
                connectedClient == _cachedConnectedClient && connectedServer == _cachedConnectedServer)
                return;

            _nextConnectionRefresh = Time.unscaledTime + Mathf.Max(0.05f, checkInterval);
            _cachedConnectedClient = connectedClient;
            _cachedConnectedServer = connectedServer;

            var clientDescription = clientConnectionDescription;
            if (clientDescription != _lastClientConnectionDescription)
            {
                _lastClientConnectionDescription = clientDescription;
                _cachedClientConnectionText = "Client: " + clientDescription;
            }

            var serverDescription = serverConnectionDescription;
            if (serverDescription != _lastServerConnectionDescription)
            {
                _lastServerConnectionDescription = serverDescription;
                _cachedServerConnectionText = "Server: " + serverDescription;
            }
        }

        private float GetStatsWidth()
        {
            float width = 200f;
            if (_displayType.HasFlag(StatisticsDisplayType.Connection))
            {
                _connectionLabelStyle ??= new GUIStyle(_labelStyle)
                {
                    wordWrap = true,
                    clipping = TextClipping.Clip
                };
                _connectionLabelStyle.fontSize = _labelStyle.fontSize;
                _connectionLabelStyle.alignment = _labelStyle.alignment;
                _connectionLabelStyle.normal.textColor = _labelStyle.normal.textColor;

                if (connectedClient)
                {
                    _connectionContent.text = _cachedClientConnectionText;
                    width = Mathf.Max(width, Mathf.Ceil(_connectionLabelStyle.CalcSize(_connectionContent).x) + CONNECTION_WIDTH_MARGIN);
                }

                if (connectedServer)
                {
                    _connectionContent.text = _cachedServerConnectionText;
                    width = Mathf.Max(width, Mathf.Ceil(_connectionLabelStyle.CalcSize(_connectionContent).x) + CONNECTION_WIDTH_MARGIN);
                }
            }

            return Mathf.Min(width, Mathf.Max(1f, Screen.width - 2 * PADDING));
        }

        private float GetConnectionHeight(string text, float width)
        {
            _connectionContent.text = text;
            var measureWidth = Mathf.Max(1f, width - CONNECTION_WIDTH_MARGIN);
            return Mathf.Ceil(Mathf.Max(LineHeight, _connectionLabelStyle.CalcHeight(_connectionContent, measureWidth)));
        }

        private void DrawConnectionLabel(ref Rect rect, string text)
        {
            rect.height = GetConnectionHeight(text, rect.width);
            GUI.Label(rect, text, _connectionLabelStyle);
            rect.y += rect.height;
            rect.height = LineHeight;
        }
    }
}
