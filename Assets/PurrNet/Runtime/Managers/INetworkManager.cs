using PurrNet.Modules;
using PurrNet.Transports;

namespace PurrNet
{
    public interface INetworkManager
    {
        bool isOffline { get; }

        bool isServer { get; }

        bool isClient { get; }

        ITransport rawTransport { get; }

        MTUExceededBehaviour mtuExceededBehaviour { get; }

        ConnectionState serverState  { get; }

        ConnectionState clientState { get; }

        bool shouldAutoStartServer { get; }

        bool shouldAutoStartClient { get; }

        NetworkRules networkRules { get; }

        void StartServer();

        void StartClient();

        void StopServer();

        void StopClient();

        void InternalRegisterClientModules();
        void InternalRegisterServerModules();
        void InternalUnregisterClientModules();
        void InternalUnregisterServerModules();

        bool HasModule<T>(bool asServer) where T : INetworkModule;

        /// <summary>
        /// Asks the manager to push queued transport data out before the next tick (this frame),
        /// so a latency-sensitive message does not wait for the tick's regular send.
        /// </summary>
        void RequestSendFlushThisFrame();

        /// <summary>
        /// Same as <see cref="RequestSendFlushThisFrame()"/> but scoped to one connection, so a
        /// transport that supports it can push just that peer instead of a full send pass.
        /// On the client side the connection argument is ignored.
        /// </summary>
        void RequestSendFlushThisFrame(Connection conn, bool asServer) => RequestSendFlushThisFrame();
    }
}
