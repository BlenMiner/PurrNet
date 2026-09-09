using System;
using System.Collections.Generic;
using PurrNet.Transports;

// A transport that accepts start requests but never finishes them. It is installed
// only on an already disconnected external client for a bounded failure case.
public sealed class MigrationFailurePendingTransport : GenericTransport, ITransport
{
    public event OnConnected onConnected { add { } remove { } }
    public event OnDisconnected onDisconnected { add { } remove { } }
    public event OnDataReceived onDataReceived;
    public event OnDataSent onDataSent;
    public event OnConnectionState onConnectionState;

    public override bool isSupported => true;
    public override ITransport transport => this;
    public IReadOnlyList<Connection> connections => Array.Empty<Connection>();
    public ConnectionState clientState { get; private set; } = ConnectionState.Disconnected;
    public ConnectionState listenerState { get; private set; } = ConnectionState.Disconnected;
    public int connectAttempts { get; private set; }
    public int listenAttempts { get; private set; }

    public void ResetAttempts() => connectAttempts = listenAttempts = 0;
    protected override void StartClientInternal() => Connect(null, 0);
    protected override void StartServerInternal() => Listen(0);

    public void Connect(string address, ushort port)
    {
        connectAttempts++;
        clientState = ConnectionState.Connecting;
        onConnectionState?.Invoke(clientState, false);
    }

    public void Listen(ushort port)
    {
        listenAttempts++;
        listenerState = ConnectionState.Connecting;
        onConnectionState?.Invoke(listenerState, true);
    }

    public void Disconnect()
    {
        if (clientState == ConnectionState.Disconnected)
            return;
        clientState = ConnectionState.Disconnected;
        onConnectionState?.Invoke(clientState, false);
    }

    public void StopListening()
    {
        if (listenerState == ConnectionState.Disconnected)
            return;
        listenerState = ConnectionState.Disconnected;
        onConnectionState?.Invoke(listenerState, true);
    }

    public void RaiseDataReceived(Connection connection, ByteData data, bool asServer) =>
        onDataReceived?.Invoke(connection, data, asServer);
    public void RaiseDataSent(Connection connection, ByteData data, bool asServer) =>
        onDataSent?.Invoke(connection, data, asServer);
    public void SendToClient(Connection connection, ByteData data, Channel channel = Channel.ReliableOrdered) { }
    public void SendToServer(ByteData data, Channel channel = Channel.ReliableOrdered) { }
    public void CloseConnection(Connection connection) { }
    public void ReceiveMessages(float delta) { }
    public void SendMessages(float delta) { }
}
