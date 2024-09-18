using System;
using System.Reflection;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packets;
using PurrNet.Transports;

namespace PurrNet
{
    public class NetworkModule
    {
        protected NetworkIdentity parent { get; private set; }

        private byte index { get; set; } = 255;

        protected NetworkManager networkManager => parent.networkManager;
        
        protected bool isSceneObject => parent.isSceneObject;
        
        protected bool isOwner => parent.isOwner;
        
        protected bool isClient => parent.isClient;

        protected bool isServer => parent.isServer;
        
        protected bool isHost => parent.isHost;
        
        protected bool isSpawned => parent.isSpawned;
        
        protected bool hasOwner => parent.hasOwner;
        
        protected bool hasConnectedOwner => parent.hasConnectedOwner;
        
        protected PlayerID? localPlayer => parent.localPlayer;
        
        protected PlayerID? owner => parent.owner;

        protected virtual void OnSpawn() { }

        protected virtual void OnSpawn(bool asServer) { }

        protected virtual void OnDespawned() { }
        
        protected virtual void OnDespawned(bool asServer) { }

        [UsedByIL]
        public void SetParent(NetworkIdentity p, byte i)
        {
            parent = p;
            index = i;
        }

        [UsedByIL]
        protected void SendRPC(ChildRPCPacket packet, RPCSignature signature)
        {
            if (!parent)
            {
                if (signature.channel is Channel.ReliableOrdered or Channel.ReliableSequenced)
                    PurrLogger.LogError($"Trying to send RPC from '{GetType().Name}' which is not initialized.");
                return;
            }

            if (!parent.isSpawned)
            {
                if (signature.channel is Channel.ReliableOrdered or Channel.ReliableSequenced)
                    PurrLogger.LogError($"Trying to send RPC from '{parent.name}' which is not spawned.", parent);
                return;
            }

            var nm = parent.networkManager;

            if (!nm.TryGetModule<RPCModule>(nm.isServer, out var module))
            {
                PurrLogger.LogError("Failed to get RPC module.", parent);
                return;
            }

            if (signature.requireOwnership && !parent.isOwner)
            {
                PurrLogger.LogError($"Trying to send RPC '{signature.rpcName}' from '{parent.name}' without ownership.",
                    parent);
                return;
            }

            if (signature.requireServer && !nm.isServer)
            {
                PurrLogger.LogError($"Trying to send RPC '{signature.rpcName}' from '{parent.name}' without server.",
                    parent);
                return;
            }

            module.AppendToBufferedRPCs(packet, signature);

            Func<PlayerID, bool> predicate = null;

            if (signature.excludeOwner)
                predicate = parent.IsNotOwnerPredicate;

            switch (signature.type)
            {
                case RPCType.ServerRPC:
                    parent.SendToServer(packet, signature.channel);
                    break;
                case RPCType.ObserversRPC:
                    parent.SendToObservers(packet, predicate, signature.channel);
                    break;
                case RPCType.TargetRPC:
                    parent.SendToTarget(signature.targetPlayer!.Value, packet, signature.channel);
                    break;
                default: throw new ArgumentOutOfRangeException();
            }
        }
        
        [UsedByIL]
        protected bool ValidateReceivingRPC(RPCInfo info, RPCSignature signature, bool asServer)
        {
            return parent && parent.ValidateReceivingRPC(info, signature, asServer);
        }
        
        [UsedByIL]
        protected void CallGeneric(string methodName, GenericRPCHeader rpcHeader)
        {
            var key = new NetworkIdentity.InstanceGenericKey(methodName, GetType(), rpcHeader.types);
            
            if (!NetworkIdentity.genericMethods.TryGetValue(key, out var gmethod))
            {
                var method = GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                gmethod = method?.MakeGenericMethod(rpcHeader.types);
                
                NetworkIdentity.genericMethods.Add(key, gmethod);
            }
        
            if (gmethod == null)
            {
                PurrLogger.LogError($"Calling generic RPC failed. Method '{methodName}' not found.");
                return;
            }

            gmethod.Invoke(this, rpcHeader.values);
        }
        
        [UsedByIL]
        protected ChildRPCPacket BuildRPC(byte rpcId, NetworkStream data)
        {
            var rpc = new ChildRPCPacket
            {
                networkId = parent.id!.Value,
                sceneId = parent.sceneId,
                childId = index,
                rpcId = rpcId,
                data = data.buffer.ToByteData()
            };

            return rpc;
        }
    }
}
