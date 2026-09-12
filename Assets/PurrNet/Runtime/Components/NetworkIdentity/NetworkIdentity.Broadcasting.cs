using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
#if UNITASK_PURRNET_SUPPORT
using Cysharp.Threading.Tasks;
#endif
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Pooling;
using PurrNet.Profiler;
using PurrNet.Transports;
using Unity.Profiling;
using Channel = PurrNet.Transports.Channel;

#if !UNITASK_PURRNET_SUPPORT
using RawTask = System.Threading.Tasks.Task;
#else
using RawTask = Cysharp.Threading.Tasks.UniTask;
#endif


namespace PurrNet
{
    public partial class NetworkIdentity
    {
        internal readonly struct InstanceGenericKey : IEquatable<InstanceGenericKey>
        {
            readonly string _methodName;
            readonly Type _caller;
            readonly Type[] _types;
            readonly int _typesHash;

            public InstanceGenericKey(string methodName, Type caller, Type[] types)
            {
                _methodName = methodName;
                _caller = caller;
                _types = types;

                var hash = new HashCode();
                for (int i = 0; i < types.Length; i++)
                    hash.Add(types[i]);
                _typesHash = hash.ToHashCode();
            }

            // Used when storing this key in a long-lived dictionary, since the source
            // `types` array comes from a pool and gets recycled after the call.
            public InstanceGenericKey CloneForStorage()
            {
                return new InstanceGenericKey(_methodName, _caller, (Type[])_types.Clone());
            }

            public override int GetHashCode() =>
                HashCode.Combine(_methodName, _caller, _typesHash);

            public bool Equals(InstanceGenericKey other)
            {
                if (_methodName != other._methodName || _caller != other._caller ||
                    _typesHash != other._typesHash)
                    return false;

                if (_types.Length != other._types.Length)
                    return false;

                for (int i = 0; i < _types.Length; i++)
                {
                    if (_types[i] != other._types[i])
                        return false;
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                return obj is InstanceGenericKey other && Equals(other);
            }
        }

        internal static readonly Dictionary<InstanceGenericKey, MethodInfo> genericMethods =
            new Dictionary<InstanceGenericKey, MethodInfo>();

        [UsedByIL]
        protected object CallGeneric(string methodName, GenericRPCHeader rpcHeader)
        {
            var key = new InstanceGenericKey(methodName, GetType(), rpcHeader.types);

            if (!genericMethods.TryGetValue(key, out var gmethod))
            {
                var method = GetType().GetMethod(methodName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                gmethod = method?.MakeGenericMethod(rpcHeader.types);

                genericMethods.Add(key.CloneForStorage(), gmethod);
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

        /// <summary>
        /// Used internally to get next RPC id.
        /// Do not use this method directly.
        /// </summary>
        [UsedByIL]
        public Task<T> GetNextId<T>(RPCType rpcType, PlayerID? target, float timeout, out RpcRequest request)
        {
            request = default;

            if (!networkManager)
            {
                return Task.FromException<T>(new InvalidOperationException(
                    "NetworkIdentity is not spawned."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule<RpcRequestResponseModule>(asServer, out var module))
            {
                return Task.FromException<T>(new InvalidOperationException(
                    "RpcRequestResponseModule module is missing."));
            }

            return module.GetNextId<T>(target, timeout, out request);
        }

        [UsedByIL]
        public RawTask GetNextIdUniTask(RPCType rpcType, PlayerID? target, float timeout, out RpcRequest request)
        {
            request = default;

            if (!networkManager)
            {
                return RawTask.FromException(new InvalidOperationException(
                    "NetworkIdentity is not spawned."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule<RpcRequestResponseModule>(asServer, out var module))
            {
                return RawTask.FromException(new InvalidOperationException(
                    "RpcRequestResponseModule module is missing."));
            }

            return module.GetNextIdUniTask(target, timeout, out request);
        }

        [UsedByIL]
#if !UNITASK_PURRNET_SUPPORT
        public Task<T>
#else
        public UniTask<T>
#endif
            GetNextIdUniTask<T>(RPCType rpcType, PlayerID? target, float timeout, out RpcRequest request)
        {
            request = default;

            if (!networkManager)
            {
                return RawTask.FromException<T>(new InvalidOperationException(
                    "NetworkIdentity is not spawned."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule<RpcRequestResponseModule>(asServer, out var module))
            {
                return RawTask.FromException<T>(new InvalidOperationException(
                    "RpcRequestResponseModule module is missing."));
            }

            return module.GetNextIdUniTask<T>(target, timeout, out request);
        }

        /// <summary>
        /// Used internally to get next RPC id.
        /// Do not use this method directly.
        /// </summary>
        [UsedByIL]
        public Task GetNextId(RPCType rpcType, PlayerID? target, float timeout, out RpcRequest request)
        {
            request = default;

            if (!networkManager)
            {
                return Task.FromException(new InvalidOperationException(
                    "NetworkIdentity is not spawned."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule<RpcRequestResponseModule>(asServer, out var module))
            {
                return Task.FromException(new InvalidOperationException(
                    "RpcRequestResponseModule module is missing."));
            }

            return module.GetNextId(target, timeout, out request);
        }

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
        private Type _myType;
#endif

        [UsedByIL]
        public DisposableList<PlayerID> GetObservers(RPCSignature signature)
        {
            var players = DisposableList<PlayerID>.Create(observers.Count);

            if (signature.targetPlayer != null)
            {
                players.Add(signature.targetPlayer.Value);
                return players;
            }

            var cachedOwner = owner;
            var cachedLocalPlayer = networkManager.localPlayer;

            for (var i = 0; i < observers.Count; i++)
            {
                var player = observers[i];
                bool isLocalPlayer = player == cachedLocalPlayer;

                if (signature.runLocally && isLocalPlayer)
                    continue;

                if (signature.excludeSender && isLocalPlayer)
                    continue;

                if (signature.excludeOwner && player == cachedOwner)
                    continue;

                players.Add(player);
            }
            return players;
        }

        public void SendRPCChild(Type statisticsParent, RPCModule rpcModule, ChildRPCPacket packet, RPCSignature signature,
            bool ownerOnly = false)
        {
            _sendRPCMarker.Begin();

            switch (signature.type)
            {
                case RPCType.ServerRPC:
                    if (networkManager.isServerOnly)
                        break;

                    if (signature.runLocally && isServer)
                        break;

                    var serverChildRpcModule = rpcModule;
                    if (isServer && !networkManager.TryGetRpcModule(false, out serverChildRpcModule))
                    {
                        PurrLogger.LogError("Failed to get client-side RPC module while sending ServerRPC from host.", this);
                        break;
                    }

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                    Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData,
                        this);
#endif
                    serverChildRpcModule.BatchToServer(packet, signature.channel, signature.mtuExceeded, signature.immediate);
                    break;
                case RPCType.ObserversRPC:
                {
                    if (isServer)
                    {
                        var cachedOwner = owner;

                        if (ownerOnly && !cachedOwner.HasValue)
                            break;

                        if (signature.targetPlayer != null)
                        {
                            if (ownerOnly && signature.targetPlayer.Value != cachedOwner.Value)
                                break;

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData, this);
#endif
                            rpcModule.BatchToTarget(signature.targetPlayer.Value, packet, signature.channel, signature.mtuExceeded, signature.immediate);
                        }
                        else if (signature.targetPlayerList is IReadOnlyList<PlayerID> groupTargets)
                        {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            if (Statistics.shouldTrack)
                            {
                                for (var i = groupTargets.Count - 1; i >= 0; --i)
                                    Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData, this);
                            }
#endif
                            rpcModule.BatchToTargets(groupTargets, packet, signature.channel, default, signature.mtuExceeded, signature.immediate);
                        }
                        else
                        {
                            if (observers.Count == 0)
                                break;

                            bool skipLocal = signature.runLocally || signature.excludeSender;
                            var filter = new ObserverFilter(
                                networkManager.localPlayer, skipLocal,
                                cachedOwner ?? default, signature.excludeOwner && cachedOwner.HasValue,
                                cachedOwner ?? default, ownerOnly
                            );

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            if (Statistics.shouldTrack)
                            {
                                for (var i = observers.Count - 1; i >= 0; --i)
                                {
                                    if (!filter.ShouldSkip(observers[i]))
                                    {
                                        Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName,
                                            packet.rpcData, this);
                                    }
                                }
                            }
#endif
                            rpcModule.BatchToTargets(observers, packet, signature.channel, filter, signature.mtuExceeded, signature.immediate);
                        }
                    }
                    else
                    {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName,
                            packet.rpcData, this);
#endif
                        rpcModule.BatchToServer(packet, signature.channel, signature.mtuExceeded, signature.immediate);
                    }

                    break;
                }
                case RPCType.TargetRPC:
                    if (isServer)
                    {
                        PlayerID ownerValue = default;

                        if (ownerOnly)
                        {
                            var cachedOwner = owner;

                            if (!cachedOwner.HasValue)
                                break;

                            ownerValue = cachedOwner.Value;
                        }

                        if (networkManager.isHost && signature.targetPlayer == PlayerID.Server &&
                            networkManager.TryGetRpcModule(false, out var hostClientRpcModule))
                        {
                            packet.targetPlayerId = PlayerID.Server;
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName,
                                packet.rpcData, this);
#endif
                            hostClientRpcModule.BatchToServer(packet, signature.channel, signature.mtuExceeded, signature.immediate);
                            break;
                        }

                        using var players = signature.GetTargets();

                        if (ownerOnly)
                        {
                            for (var i = players.Count - 1; i >= 0; --i)
                            {
                                if (players[i] != ownerValue)
                                    players.RemoveAt(i);
                            }
                        }

                        if (players.Count == 0)
                            break;

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        if (Statistics.shouldTrack)
                        {
                            for (var i = players.Count - 1; i >= 0; --i)
                                Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName,
                                    packet.rpcData, this);
                        }
#endif
                        rpcModule.BatchToTargets(players, packet, signature.channel, signature.mtuExceeded, signature.immediate);
                    }
                    else
                    {
                        using var targets = signature.GetTargets();

                        if (targets.Count == 0)
                            break;

                        // TODO: we should batch this into one packet to the server instead of N
                        for (int i = 0; i < targets.Count; i++)
                        {
                            packet.targetPlayerId = targets[i];
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName,
                                packet.rpcData, this);
#endif
                            rpcModule.BatchToServer(packet, signature.channel, signature.mtuExceeded, signature.immediate);
                        }
                    }

                    break;
                default: throw new ArgumentOutOfRangeException();
            }

            _sendRPCMarker.End();
        }

        public void SendRPCNormal(Type statisticsParent, RPCModule rpcModule, RPCPacket packet, RPCSignature signature)
        {
            _sendRPCMarker.Begin();

            switch (signature.type)
            {
                case RPCType.ServerRPC:
                    if (networkManager.isServerOnly)
                        break;

                    if (signature.runLocally && isServer)
                        break;

                    var serverRpcModule = rpcModule;
                    if (isServer && !networkManager.TryGetRpcModule(false, out serverRpcModule))
                    {
                        PurrLogger.LogError("Failed to get client-side RPC module while sending ServerRPC from host.", this);
                        break;
                    }

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                    Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData,
                        this);
#endif
                    serverRpcModule.BatchToServer(packet, signature.channel, signature.mtuExceeded, signature.immediate);
                    break;
                case RPCType.ObserversRPC:
                {
                    if (isServer)
                    {
                        if (signature.targetPlayer != null)
                        {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData, this);
#endif
                            rpcModule.BatchToTarget(signature.targetPlayer.Value, packet, signature.channel, signature.mtuExceeded, signature.immediate);
                        }
                        else if (signature.targetPlayerList is IReadOnlyList<PlayerID> groupTargets)
                        {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            if (Statistics.shouldTrack)
                            {
                                for (var i = groupTargets.Count - 1; i >= 0; --i)
                                    Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData, this);
                            }
#endif
                            rpcModule.BatchToTargets(groupTargets, packet, signature.channel, default, signature.mtuExceeded, signature.immediate);
                        }
                        else
                        {
                            if (observers.Count == 0)
                                break;

                            bool skipLocal = signature.runLocally || signature.excludeSender;
                            var filter = new ObserverFilter(
                                networkManager.localPlayer, skipLocal,
                                owner ?? default, signature.excludeOwner && owner.HasValue
                            );

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            if (Statistics.shouldTrack)
                            {
                                for (var i = observers.Count - 1; i >= 0; --i)
                                {
                                    if (!filter.ShouldSkip(observers[i]))
                                    {
                                        Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName,
                                            packet.rpcData, this);
                                    }
                                }
                            }
#endif
                            rpcModule.BatchToTargets(observers, packet, signature.channel, filter, signature.mtuExceeded, signature.immediate);
                        }
                    }
                    else
                    {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName, packet.rpcData, this);
#endif
                        rpcModule.BatchToServer(packet, signature.channel, signature.mtuExceeded, signature.immediate);
                    }

                    break;
                }
                case RPCType.TargetRPC:
                    if (isServer)
                    {
                        if (networkManager.isHost && signature.targetPlayer == PlayerID.Server &&
                            networkManager.TryGetRpcModule(false, out var hostClientRpcModule))
                        {
                            packet.targetPlayerId = PlayerID.Server;
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName,
                                packet.rpcData, this);
#endif
                            hostClientRpcModule.BatchToServer(packet, signature.channel, signature.mtuExceeded, signature.immediate);
                            break;
                        }

                        using var players = signature.GetTargets();

                        if (players.Count == 0)
                            break;

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                        if (Statistics.shouldTrack)
                        {
                            for (var i = players.Count - 1; i >= 0; --i)
                                Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName,
                                    packet.rpcData, this);
                        }
#endif
                        rpcModule.BatchToTargets(players, packet, signature.channel, signature.mtuExceeded, signature.immediate);
                    }
                    else
                    {
                        using var targets = signature.GetTargets();

                        if (targets.Count == 0)
                            break;

                        // TODO: we should batch this into one packet to the server instead of N
                        for (int i = 0; i < targets.Count; i++)
                        {
                            packet.targetPlayerId = targets[i];
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
                            Statistics.SentRPC(statisticsParent, signature.type, signature.rpcName,
                                packet.rpcData, this);
#endif
                            rpcModule.BatchToServer(packet, signature.channel, signature.mtuExceeded, signature.immediate);
                        }
                    }

                    break;
                default: throw new ArgumentOutOfRangeException();
            }

            _sendRPCMarker.End();
        }

        static readonly ProfilerMarker _sendRPCMarker = new ProfilerMarker($"NetworkIdentity.Broadcasting.SendRPC");
        static readonly ProfilerMarker _validatingRPCMarker = new ProfilerMarker($"NetworkIdentity.Broadcasting.ValidateSendingRPC");
        static readonly ProfilerMarker _validatingRRPCMarker = new ProfilerMarker($"NetworkIdentity.Broadcasting.ValidateIncomingRPC");

        [UsedByIL]
        protected void SendRPC(RPCPacket packet, RPCSignature signature)
        {
            NetworkAssetResolver.serializationSceneHint = null;
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            _myType ??= GetType();
#endif
            if (!ValidateSendingRPC(signature, out var module))
                return;

            if (signature.bufferLast)
                module.AppendToBufferedRPCs(packet, signature);

#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            SendRPCNormal(_myType, module, packet, signature);
#else
            SendRPCNormal(null, module, packet, signature);
#endif
        }

        public bool ValidateSendingRPC(RPCSignature signature, out RPCModule module)
        {
            _validatingRPCMarker.Begin();

            if (!_isSpawnedServer && !_isSpawnedClient)
            {
                if (signature is { runLocally: false, channel: Channel.ReliableOrdered or Channel.ReliableUnordered })
                {
                    PurrLogger.LogError(
                        $"Trying to send RPC `{signature.rpcName}` from '{GetType().Name}' which is not spawned.",
                        this);
                }
                module = null;
                _validatingRPCMarker.End();
                return false;
            }

            if (!networkManager.TryGetRpcModule(networkManager.isServer, out module))
            {
                if (signature is { runLocally: false, channel: Channel.ReliableOrdered or Channel.ReliableUnordered })
                {
                    PurrLogger.LogError(
                        $"Trying to send RPC `{signature.rpcName}` from `{GetType().Name}` but RPCModule is missing for `{(networkManager.isServer ? "server" : "client")}`.",
                        this);
                }
                _validatingRPCMarker.End();
                return false;
            }

            var rules = networkManager.networkRules;
            bool shouldIgnoreOwnership = rules && rules.ShouldIgnoreRequireOwner();

            if (!shouldIgnoreOwnership && signature.requireOwnership && !isOwner)
            {
                if (signature is
                    { runLocally: false, channel: Channel.ReliableOrdered or Channel.ReliableUnordered })
                    PurrLogger.LogError(
                        $"Trying to send RPC '{signature.rpcName}' from '{GetType().Name}' without ownership.",
                        this);
                _validatingRPCMarker.End();
                return false;
            }

            bool shouldIgnore = rules && rules.ShouldIgnoreRequireServer();

            if (!shouldIgnore && signature.requireServer && !networkManager.isServer)
            {
                if (signature is
                    { runLocally: false, channel: Channel.ReliableOrdered or Channel.ReliableUnordered })
                    PurrLogger.LogError(
                        $"Trying to send RPC '{signature.rpcName}' from '{GetType().Name}' without server.",
                        this);
                _validatingRPCMarker.End();
                return false;
            }

            _validatingRPCMarker.End();
            return true;
        }

        [UsedByIL]
        public bool ValidateReceivingRPC<T>(RPCInfo info, RPCSignature signature, T data, bool asServer, uint requestId, bool isAwaitable) where T : struct, IRpc
        {
#if UNITY_EDITOR || PURR_RUNTIME_PROFILING
            _myType ??= GetType();
            Statistics.ReceivedRPC(_myType, signature.type, signature.rpcName, data.rpcData, this);
#endif
            return ValidateIncomingRPC(info, signature, data, asServer, requestId, isAwaitable);
        }

        internal bool ValidateIncomingRPC<T>(RPCInfo info, RPCSignature signature, T data, bool asServer, uint requestId, bool isAwaitable,
            bool ownerOnly = false) where T : struct, IRpc
        {
            using (_validatingRRPCMarker.Auto())
            {
                if (info.receivedImmediate && !signature.immediate)
                {
                    PurrLogger.LogError(
                        $"Rejected RPC '{signature.rpcName}' on '{name}': it arrived on the immediate lane but is not marked immediate.",
                        this);
                    return false;
                }

                var rules = networkManager.networkRules;
                bool shouldIgnoreOwnership = rules && rules.ShouldIgnoreRequireOwner();

                if (!networkManager.TryGetRpcModule(networkManager.isServer, out var module))
                    return false;

                if (!shouldIgnoreOwnership && signature.requireOwnership && info.sender != owner)
                {
                    RPCModule.TrySendRejection(networkManager, info, signature, requestId, isAwaitable, asServer, RpcError.RequireOwnership);
                    return false;
                }

                if (signature.excludeOwner && isOwner)
                    return false;

                if (signature.type == RPCType.ServerRPC)
                {
                    if (!asServer)
                    {
                        PurrLogger.LogError(
                            $"Trying to receive server RPC '{signature.rpcName}' from '{name}' on client. Aborting RPC call.",
                            this);
                        return false;
                    }

                    var idObservers = observers;

                    if (idObservers == null)
                    {
                        if (!isAwaitable)
                            PurrLogger.LogError(
                                $"Trying to receive server RPC '{signature.rpcName}' from '{name}' but failed to get observers.",
                                this);
                        RPCModule.TrySendRejection(networkManager, info, signature, requestId, isAwaitable, asServer, RpcError.NotObserver);
                        return false;
                    }

                    if (!IsObserver(info.sender))
                    {
                        if (!isAwaitable && signature.channel == Channel.ReliableOrdered)
                        {
                            PurrLogger.LogError(
                                $"Trying to receive server RPC '{signature.rpcName}' from '{name}' by player '{info.sender}' which is not an observer. Aborting RPC call.",
                                this);
                        }

                        RPCModule.TrySendRejection(networkManager, info, signature, requestId, isAwaitable, asServer, RpcError.NotObserver);
                        return false;
                    }

                    return true;
                }

                if (!asServer)
                {
                    return true;
                }

                bool shouldIgnore = rules && rules.ShouldIgnoreRequireServer();

                if (!shouldIgnore && signature.requireServer)
                {
                    if (!isAwaitable)
                        PurrLogger.LogError(
                            $"Trying to receive client RPC '{signature.rpcName}' from '{name}' on server. " +
                            "If you want automatic forwarding use 'requireServer: false'.", this);
                    RPCModule.TrySendRejection(networkManager, info, signature, requestId, isAwaitable, asServer, RpcError.ServerRequired);
                    return false;
                }

                switch (signature.type)
                {
                    case RPCType.ServerRPC:
                        throw new InvalidOperationException("ServerRPC should be handled by server.");

                    case RPCType.ObserversRPC:
                    {
                        var cachedOwner = owner;

                        if (ownerOnly && !cachedOwner.HasValue)
                            return !isClient;

                        using var players = DisposableList<PlayerID>.Create(observers.Count);

                        for (var i = 0; i < observers.Count; ++i)
                        {
                            var observer = observers[i];

                            bool ignoreSender = observer == info.sender &&
                                                (signature.excludeSender || signature.runLocally);
                            bool ignoreOwner = signature.excludeOwner && observer == cachedOwner;

                            if (ignoreSender || ignoreOwner)
                                continue;

                            if (ownerOnly && observer != cachedOwner.Value)
                                continue;

                            players.Add(observer);
                        }

                        if (signature.immediate)
                            BatchForwardToTargets(module, players, data, signature);
                        else
                            Send(players, data, signature.channel, signature.mtuExceeded.AsOverride());
                        AppendToBufferedRPCs(signature, data, module);
                        return !isClient;
                    }
                    case RPCType.TargetRPC:
                    {
                        if (ownerOnly && data.targetPlayerId != owner)
                            return !isClient;

                        bool shouldExecute =
                            SendToTargetOrServer(rules, module, data.targetPlayerId, data, signature, info, requestId, isAwaitable, asServer);
                        AppendToBufferedRPCs(signature, data, module);
                        return shouldExecute;
                    }
                    default: throw new ArgumentOutOfRangeException(nameof(signature.type));
                }
            }
        }

        // Forwarded client RPCs must ride the immediate batch too, otherwise the
        // second hop loses the per-frame send/receive lane.
        private static void BatchForwardToTargets<T>(RPCModule module, DisposableList<PlayerID> players, T data,
            RPCSignature signature) where T : struct, IRpc
        {
            switch (data)
            {
                case RPCPacket rpcPacket:
                    module.BatchToTargets(players, rpcPacket, signature.channel, signature.mtuExceeded, true);
                    break;
                case ChildRPCPacket childRpcPacket:
                    module.BatchToTargets(players, childRpcPacket, signature.channel, signature.mtuExceeded, true);
                    break;
            }
        }

        private static void BatchForwardToTarget<T>(RPCModule module, PlayerID player, T data,
            RPCSignature signature) where T : struct, IRpc
        {
            switch (data)
            {
                case RPCPacket rpcPacket:
                    module.BatchToTarget(player, rpcPacket, signature.channel, signature.mtuExceeded, true);
                    break;
                case ChildRPCPacket childRpcPacket:
                    module.BatchToTarget(player, childRpcPacket, signature.channel, signature.mtuExceeded, true);
                    break;
            }
        }

        private static void AppendToBufferedRPCs(RPCSignature signature, IRpc data, RPCModule module)
        {
            switch (data)
            {
                case RPCPacket rpcPacket:
                    module.AppendToBufferedRPCs(rpcPacket, signature);
                    break;
                case ChildRPCPacket childRpcPacket:
                    module.AppendToBufferedRPCs(childRpcPacket, signature);
                    break;
            }
        }

        public void Send<T>(PlayerID player, T packet, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            if (networkManager.isServer)
                networkManager.GetModule<PlayersManager>(true).Send(player, packet, method, mtuOverride);
        }

        bool SendToTargetOrServer<T>(NetworkRules rules, RPCModule module, PlayerID player, T data, RPCSignature signature, RPCInfo info, uint requestId, bool isAwaitable, bool asServer) where T : struct, IRpc
        {
            if (player == PlayerID.Server)
            {
                if (rules.CanTargetServerWithTargetRpc())
                    return true;

                if (!isAwaitable)
                    PurrLogger.LogError($"Trying to send TargetRPC to server `{name}`" +
                                        $" but `NetworkRules` don't allow for this.", this);
                RPCModule.TrySendRejection(networkManager, info, signature, requestId, isAwaitable, asServer, RpcError.TargetServerNotAllowed);
                return false;
            }

            if (!IsObserver(player))
            {
                if (!isAwaitable)
                    PurrLogger.LogError($"Trying to send TargetRPC to player '{player}' which is not observing '{name}'.",
                        this);
                RPCModule.TrySendRejection(networkManager, info, signature, requestId, isAwaitable, asServer, RpcError.NotObserver);
                return false;
            }

            if (signature.immediate)
                BatchForwardToTarget(module, player, data, signature);
            else
                Send<T>(player, data, signature.channel, signature.mtuExceeded.AsOverride());
            return false;
        }

        public void Send<T>(IReadOnlyList<PlayerID> players, T data, Channel method = Channel.ReliableOrdered,
            MTUExceededBehaviour? mtuOverride = null)
        {
            if (networkManager.isServer)
                networkManager.GetModule<PlayersManager>(true).Send(players, data, method, mtuOverride);
        }
    }
}
