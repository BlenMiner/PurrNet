using System;

namespace PurrNet.Transports
{
    public interface IPurrWebRtcPeer
    {
        event Action onConnect;
        event Action onDisconnect;
        event Action<ArraySegment<byte>> onData;
        event Action<Exception> onError;
        event Action<string> onSignal;

        bool isConnected { get; }
        byte receivedDeliveryMethod { get; }
        void ConnectPeer(bool initiator, string iceServersJson);
        void ReceiveSignal(string signal);
        void Send(ArraySegment<byte> data, byte deliveryMethod = 2);
        void ProcessMessageQueue();
        void Disconnect();
    }

    public static class PurrWebRtcPeerProvider
    {
        /// <summary>Installed by an optional native WebRTC integration on supported platforms.</summary>
        public static Func<IPurrWebRtcPeer> nativeFactory { get; set; }
    }
}
