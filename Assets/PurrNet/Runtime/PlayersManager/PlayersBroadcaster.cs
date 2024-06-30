using System.Collections.Generic;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Transports;
using PurrNet.Utils;

namespace PurrNet
{
    public delegate void PlayerBroadcastDelegate<in T>(PlayerID player, T data, bool asServer);

    internal interface IPlayerBroadcastCallback
    {
        bool IsSame(object callback);
        
        void TriggerCallback(PlayerID playerId, object data, bool asServer);
    }
    
    internal readonly struct PlayerBroadcastCallback<T> : IPlayerBroadcastCallback
    {
        readonly PlayerBroadcastDelegate<T> callback;
        
        public PlayerBroadcastCallback(PlayerBroadcastDelegate<T> callback)
        {
            this.callback = callback;
        }

        public bool IsSame(object callbackToCmp)
        {
            return callbackToCmp is PlayerBroadcastDelegate<T> action && action == callback;
        }

        public void TriggerCallback(PlayerID playerId, object data, bool asServer)
        {
            if (data is T value)
                callback?.Invoke(playerId, value, asServer);
        }
    }
    
    public class PlayersBroadcaster : INetworkModule
    {
        private readonly BroadcastModule _broadcastModule;
        private readonly PlayersManager _playersManager;
        
        private readonly Dictionary<uint, List<IPlayerBroadcastCallback>> _actions = new();
        private readonly List<Connection> _connections = new();

        private bool _asServer;
        
        public PlayersBroadcaster(BroadcastModule broadcastModule, PlayersManager playersManager)
        {
            _broadcastModule = broadcastModule;
            _playersManager = playersManager;
        }

        public void Enable(bool asServer)
        {
            _asServer = asServer;
        }

        public void Disable(bool asServer) { }

        public void Subscribe<T>(PlayerBroadcastDelegate<T> callback, bool asServer) where T : new()
        {
            BroadcastModule.RegisterTypeForSerializer<T>();

            var hash = Hasher.GetStableHashU32(typeof(T));

            if (_actions.TryGetValue(hash, out var actions))
            {
                actions.Add(new PlayerBroadcastCallback<T>(callback));
                return;
            }
            
            _actions.Add(hash, new List<IPlayerBroadcastCallback>
            {
                new PlayerBroadcastCallback<T>(callback)
            });
        }

        public void Unsubscribe<T>(PlayerBroadcastDelegate<T> callback) where T : new()
        {
            var hash = Hasher.GetStableHashU32(typeof(T));
            if (!_actions.TryGetValue(hash, out var actions))
                return;
            
            object boxed = callback;

            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].IsSame(boxed))
                {
                    actions.RemoveAt(i);
                    return;
                }
            }
        }
        
        public void Send<T>(PlayerID player, T data, Channel method = Channel.ReliableOrdered)
        {
            if (player.isBot)
                return;

            if (_playersManager.TryGetConnection(player, out var conn))
                 _broadcastModule.Send(conn, data, method);
            else PurrLogger.LogWarning($"Player {player} is not connected. Can't send data '{data.GetType().FullName}'.");
        }
        
        public void Send<T>(IEnumerable<PlayerID> players, T data, Channel method = Channel.ReliableOrdered)
        {
            foreach (var player in players)
            {
                if (player.isBot)
                    continue;

                if (_playersManager.TryGetConnection(player, out var conn))
                    _connections.Add(conn);
            }
            
            _broadcastModule.Send(_connections, data, method);
        }
        
        public void SendToAll<T>(T data, Channel method = Channel.ReliableOrdered)
        {
            _broadcastModule.SendToAll(data, method);
        }
        
        public void SendToServer<T>(T data, Channel method = Channel.ReliableOrdered)
        {
            _broadcastModule.SendToServer(data, method);
        }
    }
}
