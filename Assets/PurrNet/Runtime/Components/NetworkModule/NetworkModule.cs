using System;
using System.Collections.Generic;
using System.Reflection;
using JetBrains.Annotations;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Profiler;

namespace PurrNet
{
    [Serializable]
    public class NetworkModule
    {
        public NetworkIdentity parent { get; private set; }

        [UsedImplicitly] public string name { get; private set; }

        [UsedImplicitly] public byte index { get; private set; } = 255;

        [UsedImplicitly] public NetworkManager networkManager => parent ? parent.networkManager : null;

        [UsedImplicitly] public bool isSceneObject => parent && parent.isSceneObject;

        [UsedImplicitly] public bool isOwner => parent && parent.isOwner;

        [UsedImplicitly] public bool isClient => parent && parent.isClient;

        [UsedImplicitly] public bool isServer => parent && parent.isServer;

        [UsedImplicitly] public bool isServerOnly => parent && parent.isServerOnly;

        [UsedImplicitly] public bool isHost => parent && parent.isHost;

        [UsedImplicitly] public bool isSpawned => parent && parent.isSpawned;

        public bool hasOwner => parent.hasOwner;

        public bool hasConnectedOwner => parent && parent.hasConnectedOwner;

        [UsedImplicitly] public PlayerID? localPlayer => parent ? parent.localPlayer : null;

        [UsedByIL] protected PlayerID localPlayerForced => parent ? parent.localPlayerForced : default;

        public PlayerID? owner => parent ? parent.owner : null;

        /// <summary>
        /// When true, this module's state is only ever sent to the owner of the parent identity.
        /// Non-owner observers still see the object, they just never receive this module's data.
        /// Override in your module (or pass ownerOnly: true to a sync type's constructor) to enable.
        /// </summary>
        public virtual bool ownerOnly => false;

        private List<PlayerID> _ownerOnlyObservers;

        /// <summary>
        /// The observers this module is allowed to send to.
        /// Same as the parent's observer list, unless the module is <see cref="ownerOnly"/>,
        /// in which case it contains only the owner (or nothing when there is no owner).
        /// Server only.
        /// </summary>
        public IReadOnlyList<PlayerID> observers
        {
            get
            {
                if (!parent)
                    return Array.Empty<PlayerID>();

                if (!ownerOnly)
                    return parent.observers;

                RefreshOwnerOnlyObservers();
                return _ownerOnlyObservers;
            }
        }

        private void RefreshOwnerOnlyObservers()
        {
            _ownerOnlyObservers ??= new List<PlayerID>(1);
            _ownerOnlyObservers.Clear();

            var cachedOwner = parent.owner;
            if (!cachedOwner.HasValue)
                return;

            var all = parent.observers;

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != cachedOwner.Value)
                    continue;

                _ownerOnlyObservers.Add(cachedOwner.Value);
                return;
            }
        }

        public bool isController => parent && parent.isController;

        [UsedImplicitly]
        public bool IsController(bool ownerHasAuthority) => parent && parent.IsController(ownerHasAuthority);

        [UsedImplicitly]
        public bool IsController(IOwnerAuth auth) => parent && parent.IsController(auth.ownerAuth);

        [UsedImplicitly]
        public bool IsController(bool ownerHasAuthority, bool asServer) =>
            parent && parent.IsController(asServer, ownerHasAuthority);

        [UsedByIL]
        public void Error(string message)
        {
            PurrLogger.LogWarning($"Module in {parent.GetType().Name} is null: <i>{message}</i>\n" +
                                  $"You can initialize it on Awake or override OnInitializeModules.", parent);
        }

        [UsedByIL]
        public virtual void OnReceivedRpc(int id, ChildRPCPacket packet, RPCInfo info, bool asServer) { }

        [UsedByIL]
        public static void OnReceivedRpc(int id, StaticRPCPacket packet, RPCInfo info, bool asServer) { }

        public virtual void OnSpawn()
        {
        }

        public virtual void OnSpawn(bool asServer)
        {
        }

        /// <summary>
        /// Called only on the spawning peer, at the end of the frame the object was spawned in,
        /// right before batched RPCs flush. Anything queued here departs in the same flush as the
        /// spawn, ordered right behind the spawn packet.
        /// </summary>
        public virtual void OnSpawnSent()
        {
        }

        /// <summary>
        /// Called only on peers that created the object from a spawn packet, once the entire
        /// packet has been deserialized and early-spawned. Everything that spawned alongside
        /// this object exists at this point; safe to react to spawn data.
        /// </summary>
        public virtual void OnSpawnReceived()
        {
        }

        public virtual void OnDespawned()
        {
        }

        public virtual void OnDespawned(bool asServer)
        {
        }

        /// <summary>
        /// Called on the spawner to attach custom data to the parent's spawn packet.
        /// Whatever you write here travels with the object and is read back in OnDeserialize.
        /// </summary>
        /// <param name="packer">The packer to write your data into</param>
        public virtual void OnSerialize(BitPacker packer)
        {
        }

        /// <summary>
        /// Called on peers that create the object, after the spawn packet arrives.
        /// Read the data back in the same order you wrote it in OnSerialize.
        /// </summary>
        /// <param name="packer">The packer to read your data from</param>
        public virtual void OnDeserialize(BitPacker packer)
        {
        }

        /// <summary>
        /// Called when an observer is added.
        /// Server only.
        /// </summary>
        /// <param name="player">The observer player id</param>
        public virtual void OnPreObserverAdded(PlayerID player)
        {
        }

        /// <summary>
        /// Called when an observer is added.
        /// Server only.
        /// </summary>
        /// <param name="player">The observer player id</param>
        /// <param name="isSpawner">If this object was just spawned and the observer is the spawner</param>
        public virtual void OnPreObserverAdded(PlayerID player, bool isSpawner)
        {
        }

        /// <summary>
        /// Called when an observer is added.
        /// Server only.
        /// </summary>
        /// <param name="player">The observer player id</param>
        public virtual void OnObserverAdded(PlayerID player)
        {
        }

        /// <summary>
        /// Called when an observer is added.
        /// Server only.
        /// </summary>
        /// <param name="player">The observer player id</param>
        /// <param name="isSpawner">If this object was just spawned and the observer is the spawner</param>
        public virtual void OnObserverAdded(PlayerID player, bool isSpawner)
        {
        }

        /// <summary>
        /// Called when an observer is removed.
        /// Server only.
        /// </summary>
        /// <param name="player"></param>
        public virtual void OnObserverRemoved(PlayerID player)
        {
        }

        public virtual void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
        }

        /// <summary>
        /// Called when the owner of this object changes.
        /// </summary>
        /// <param name="oldOwner">The old owner of this object</param>
        /// <param name="newOwner">The new owner of this object</param>
        /// <param name="isSpawnEvent">If this object was just spawned and the newOwner is the spawner</param>
        /// <param name="asServer">Is this on the server</param>
        public virtual void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool isSpawnEvent, bool asServer)
        {
        }

        public virtual void OnOwnerDisconnected(PlayerID ownerId)
        {
        }

        public virtual void OnOwnerReconnected(PlayerID ownerId)
        {
        }

        public void SetComponentParent(NetworkIdentity p, byte i, string moduleName)
        {
            parent = p;
            index = i;
            name = moduleName;
        }

        [UsedByIL]
        public void RegisterModuleInternal(string moduleName, string type, NetworkModule module, bool isNetworkIdentity)
        {
            var parentRef = this.parent;

            if (parentRef)
                parentRef.RegisterModuleInternal(moduleName, type, module, isNetworkIdentity);
            else PurrLogger.LogError($"Registering module '{moduleName}' failed since it is not spawned.");
        }

        [UsedByIL]
        protected void SendRPC(ChildRPCPacket packet, RPCSignature signature)
        {
            NetworkAssetResolver.serializationSceneHint = null;
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            _myType ??= GetType();
#endif

            if (!parent.ValidateSendingRPC(signature, out var module))
                return;

            if (signature.bufferLast)
                module.AppendToBufferedRPCs(packet, signature);

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            parent.SendRPCChild(_myType, module, packet, signature, ownerOnly);
#else
            parent.SendRPCChild(null, module, packet, signature, ownerOnly);
#endif
        }

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
        private Type _myType;
#endif

        [UsedByIL]
        protected bool ValidateReceivingRPC<T>(RPCInfo info, RPCSignature signature, T data, bool asServer, uint requestId, bool isAwaitable) where T : struct, IRpc
        {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            _myType ??= GetType();
            Statistics.ReceivedRPC(_myType, signature.type, signature.rpcName, data.rpcData, parent);
#endif
            return parent && parent.ValidateIncomingRPC(info, signature, data, asServer, requestId, isAwaitable, ownerOnly);
        }

        [UsedByIL]
        public DisposableList<PlayerID> GetObservers(RPCSignature signature)
        {
            if (!ownerOnly)
                return parent.GetObservers(signature);

            var players = DisposableList<PlayerID>.Create(1);
            var cachedOwner = parent.owner;

            if (!cachedOwner.HasValue || signature.excludeOwner)
                return players;

            if (signature.targetPlayer != null)
            {
                if (signature.targetPlayer.Value == cachedOwner.Value)
                    players.Add(cachedOwner.Value);
                return players;
            }

            if (signature.runLocally && cachedOwner.Value == networkManager.localPlayer)
                return players;

            var all = parent.observers;

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != cachedOwner.Value)
                    continue;

                players.Add(cachedOwner.Value);
                break;
            }

            return players;
        }

        [UsedByIL]
        protected object CallGeneric(string methodName, GenericRPCHeader rpcHeader)
        {
            var key = new NetworkIdentity.InstanceGenericKey(methodName, GetType(), rpcHeader.types);

            if (!NetworkIdentity.genericMethods.TryGetValue(key, out var gmethod))
            {
                var method = GetType().GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                gmethod = method?.MakeGenericMethod(rpcHeader.types);

                NetworkIdentity.genericMethods.Add(key.CloneForStorage(), gmethod);
            }

            if (gmethod == null)
            {
                PurrLogger.LogError($"Calling generic RPC failed. Method '{methodName}' not found.");
                return null;
            }

            try
            {
                var res = gmethod.Invoke(this, rpcHeader.values);
                PreciseArrayPool<Type>.Return(rpcHeader.types);
                PreciseArrayPool<object>.Return(rpcHeader.values);
                return res;
            }
            catch (TargetInvocationException e)
            {
                var actualException = e.InnerException;

                if (actualException != null)
                {
                    PurrLogger.LogException(actualException);
                    throw BypassLoggingException.instance;
                }

                throw;
            }
        }

        [UsedByIL]
        protected ChildRPCPacket BuildRPC(int rpcId, BitPacker data)
        {
            if (!parent)
                throw new InvalidOperationException(
                    $"Trying to send RPC from '<b>{GetType().FullName}</b> {name}' which is not spawned.");

            NetworkAssetResolver.serializationSceneHint = parent.sceneId;

            var rpc = new ChildRPCPacket
            {
                header = new NetworkModuleRPCHeader
                {
                    networkId = parent.id!.Value,
                    sceneId = parent.sceneId,
                    childId = (int)index,
                    rpcId = rpcId,
                    senderId = RPCModule.GetLocalPlayer(networkManager)
                },
                data = new BitData(data)
            };

            return rpc;
        }

        public virtual void OnInitializeModules()
        {
        }

        /// <summary>
        /// Called when this object is spawned but before any other data is received.
        /// At this point you might be missing ownership data, module data, etc.
        /// This is only called once even if in host mode.
        /// </summary>
        public virtual void OnEarlySpawn()
        {
        }

        /// <summary>
        /// Called when this object is spawned but before any other data is received.
        /// At this point you might be missing ownership data, module data, etc.
        /// This is called twice in host mode, once for the server and once for the client.
        /// </summary>
        public virtual void OnEarlySpawn(bool asServer)
        {
        }

        /// <summary>
        /// Called when this object is put back into the pool.
        /// Use this to reset any values for the next spawn.
        /// </summary>
        public virtual void OnPoolReset()
        {
        }

        protected string GetPermissionErrorDetails(IOwnerAuth auth)
        {
            return GetPermissionErrorDetails(
                auth.ownerAuth,
                isServer,
                owner,
                localPlayer
            );
        }

        protected static string GetPermissionErrorDetails(bool ownerAuth, NetworkModule module)
        {
            return GetPermissionErrorDetails(
                ownerAuth,
                module.isServer,
                module.owner,
                module.localPlayer
            );
        }

        static string GetPermissionErrorDetails(bool ownerAuth, bool isServer, PlayerID? owner, PlayerID? local)
        {
            return ownerAuth switch
            {
                true when isServer =>
                    $"Server is trying to act on module that is `<b>ownerAuth</b>` but the owner is `<b>{owner}</b>` (not you).",
                true =>
                    $"Client is trying to act on module that is `<b>ownerAuth</b>` but the owner is `<b>{owner}</b>` (not you: `{local}`).",
                _ => "Client is trying to act on module that is not `<b>ownerAuth</b>`, only server can act on it."
            };
        }

        /// <summary>
        /// Promotes the NetworkIdentity instance to function as a server entity.
        /// This is used for host-migration, when a client is promoted to host.
        /// Use this to ensure client has everything it needs to function as server.
        /// </summary>
        public virtual void PromoteToServer() {}
    }
}
