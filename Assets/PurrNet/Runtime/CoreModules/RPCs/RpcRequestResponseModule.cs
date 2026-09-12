using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Logging;
using PurrNet.Packing;
using PurrNet.Transports;
using UnityEngine;
using Channel = PurrNet.Transports.Channel;

#if !UNITASK_PURRNET_SUPPORT
using RawTask = System.Threading.Tasks.Task;
#else
using Cysharp.Threading.Tasks;
using RawTask = Cysharp.Threading.Tasks.UniTask;
#endif

namespace PurrNet.Modules
{
    public struct RpcRequest
    {
        [UsedByIL] public uint id;
        public PlayerID? target;

        public float timeSent;
        public float timeout;

        internal IRpcRequestCompleter completer;
    }

    internal interface IRpcRequestCompleter
    {
        void Respond(BitPacker stream);
        void Fail(Exception exception);
    }

    internal sealed class TaskRequestCompleter : IRpcRequestCompleter
    {
        private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();
        public Task task => _tcs.Task;
        public void Respond(BitPacker stream) => _tcs.SetResult(true);
        public void Fail(Exception exception) => _tcs.SetException(exception);
    }

    internal sealed class TaskRequestCompleter<T> : IRpcRequestCompleter
    {
        private readonly TaskCompletionSource<T> _tcs = new TaskCompletionSource<T>();
        public Task<T> task => _tcs.Task;

        public void Respond(BitPacker stream)
        {
            T response = default;
            Packer<T>.Read(stream, ref response);
            _tcs.SetResult(response);
        }

        public void Fail(Exception exception) => _tcs.SetException(exception);
    }

    internal sealed class UniTaskRequestCompleter : IRpcRequestCompleter
    {
#if !UNITASK_PURRNET_SUPPORT
        private readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();
#else
        private readonly UniTaskCompletionSource<bool> _tcs = new UniTaskCompletionSource<bool>();
#endif
        public RawTask task => _tcs.Task;
        public void Respond(BitPacker stream) => _tcs.TrySetResult(true);
        public void Fail(Exception exception) => _tcs.TrySetException(exception);
    }

    internal sealed class UniTaskRequestCompleter<T> : IRpcRequestCompleter
    {
#if !UNITASK_PURRNET_SUPPORT
        private readonly TaskCompletionSource<T> _tcs = new TaskCompletionSource<T>();
        public Task<T> task => _tcs.Task;
#else
        private readonly UniTaskCompletionSource<T> _tcs = new UniTaskCompletionSource<T>();
        public UniTask<T> task => _tcs.Task;
#endif

        public void Respond(BitPacker stream)
        {
            T response = default;
            Packer<T>.Read(stream, ref response);
            _tcs.TrySetResult(response);
        }

        public void Fail(Exception exception) => _tcs.TrySetException(exception);
    }

    public struct RpcResponse
    {
        public PlayerID? sender;
        public PlayerID? forward;
        public Channel? channel;
        public PackedUInt id;
        public ByteData data;
    }

    public struct RpcRejection
    {
        public PlayerID? sender;
        public PlayerID? forward;
        public Channel? channel;
        public PackedUInt id;
        public RpcError error;
    }

    /// <summary>
    /// Envelope for responses to immediate RPCs; registered as an immediate broadcast
    /// type so the response dispatches on arrival instead of waiting for the tick.
    /// </summary>
    public struct ImmediateRpcResponse
    {
        public RpcResponse response;
    }

    /// <inheritdoc cref="ImmediateRpcResponse"/>
    public struct ImmediateRpcRejection
    {
        public RpcRejection rejection;
    }

    public class RpcRequestResponseModule : INetworkModule, IFixedUpdate, IPromoteToServerModule
    {
        private readonly NetworkManager _manager;
        private readonly PlayersManager _playersManager;
        private readonly List<RpcRequest> _requests = new List<RpcRequest>();

        private uint _nextId;
        private bool _asServer;

        public RpcRequestResponseModule(NetworkManager manager, PlayersManager playersManager, bool asServer)
        {
            _asServer = asServer;
            _playersManager = playersManager;
            _manager = manager;
        }

        public void PromoteToServerModule()
        {
            _asServer = true;
        }

        public void PostPromoteToServerModule() { }

        public void Enable(bool asServer)
        {
            _playersManager.Subscribe<RpcResponse>(OnRpcResponse);
            _playersManager.Subscribe<RpcRejection>(OnRpcRejection);
            _playersManager.RegisterImmediateType<ImmediateRpcResponse>();
            _playersManager.Subscribe<ImmediateRpcResponse>(OnImmediateRpcResponse);
            _playersManager.RegisterImmediateType<ImmediateRpcRejection>();
            _playersManager.Subscribe<ImmediateRpcRejection>(OnImmediateRpcRejection);
            _playersManager.onPlayerLeft += OnPlayerLeft;
        }

        public void Disable(bool asServer)
        {
            _playersManager.Unsubscribe<RpcResponse>(OnRpcResponse);
            _playersManager.Unsubscribe<RpcRejection>(OnRpcRejection);
            _playersManager.Unsubscribe<ImmediateRpcResponse>(OnImmediateRpcResponse);
            _playersManager.UnregisterImmediateType<ImmediateRpcResponse>();
            _playersManager.Unsubscribe<ImmediateRpcRejection>(OnImmediateRpcRejection);
            _playersManager.UnregisterImmediateType<ImmediateRpcRejection>();
            _playersManager.onPlayerLeft -= OnPlayerLeft;
        }

        private void OnPlayerLeft(PlayerID player, bool asServer)
        {
            for (int i = 0; i < _requests.Count; i++)
            {
                var request = _requests[i];
                if (!request.target.HasValue || request.target.Value.isServer || request.target.Value != player)
                    continue;

                _requests.RemoveAt(i);
                i--;

                try
                {
                    request.completer.Fail(new RpcTargetDisconnectedException(player,
                        $"Async RPC with request id of '{request.id}' failed because target player '{player}' disconnected."));
                }
                catch (Exception e)
                {
                    PurrLogger.LogError($"Error while failing RPC request after target disconnect: {e}");
                }
            }
        }

        private void OnRpcResponse(PlayerID conn, RpcResponse data, bool asServer)
            => HandleRpcResponse(conn, data, asServer, false);

        private void OnImmediateRpcResponse(PlayerID conn, ImmediateRpcResponse data, bool asServer)
            => HandleRpcResponse(conn, data.response, asServer, true);

        private void HandleRpcResponse(PlayerID conn, RpcResponse data, bool asServer, bool immediate)
        {
            var localPlayer = _manager.localPlayer;

            switch (asServer)
            {
                case true when data.forward.HasValue && data.forward != localPlayer:
                    data.sender = conn;
                    var relayChannel = data.channel ?? Channel.ReliableOrdered;
                    if (immediate)
                        SendImmediateResponse(data.forward.Value, data, relayChannel, null);
                    else
                        _playersManager.Send(data.forward.Value, data, relayChannel);
                    return;
                case true:
                    data.sender = null;
                    break;
            }

            var actualSender = data.sender ?? conn;

            for (int i = 0; i < _requests.Count; i++)
            {
                var request = _requests[i];
                if (request.id == data.id && (!request.target.HasValue || request.target == actualSender || request.target.Value.isServer))
                {
                    _requests.RemoveAt(i);

                    try
                    {
                        using var stream = RPCModule.AllocStream(false);
                        stream.WriteBytes(data.data);
                        stream.ResetPositionAndMode(true);

                        request.completer.Respond(stream);
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogError($"Error while processing RPC response: {e}");
                    }

                    break;
                }
            }
        }

        private void OnRpcRejection(PlayerID conn, RpcRejection data, bool asServer)
            => HandleRpcRejection(conn, data, asServer, false);

        private void OnImmediateRpcRejection(PlayerID conn, ImmediateRpcRejection data, bool asServer)
            => HandleRpcRejection(conn, data.rejection, asServer, true);

        private void HandleRpcRejection(PlayerID conn, RpcRejection data, bool asServer, bool immediate)
        {
            var localPlayer = _manager.localPlayer;

            switch (asServer)
            {
                case true when data.forward.HasValue && data.forward != localPlayer:
                    data.sender = conn;
                    var relayChannel = data.channel ?? Channel.ReliableOrdered;
                    if (immediate)
                        SendImmediateRejection(data.forward.Value, data, relayChannel);
                    else
                        _playersManager.Send(data.forward.Value, data, relayChannel);
                    return;
                case true:
                    data.sender = null;
                    break;
            }

            var actualSender = data.sender ?? conn;

            for (int i = 0; i < _requests.Count; i++)
            {
                var request = _requests[i];
                if (request.id == data.id && (!request.target.HasValue || request.target == actualSender || request.target.Value.isServer))
                {
                    _requests.RemoveAt(i);

                    try
                    {
                        request.completer.Fail(new RpcRejectedException(data.error));
                    }
                    catch (Exception e)
                    {
                        PurrLogger.LogError($"Error while processing RPC rejection: {e}");
                    }

                    break;
                }
            }
        }

        public void SendRejection(PlayerID originalSender, uint requestId, RpcError error, Channel channel,
            bool immediate = false)
        {
            var rejection = new RpcRejection
            {
                id = requestId,
                error = error,
                channel = channel
            };

            if (_asServer)
            {
                if (originalSender.isServer || _manager.isHost && originalSender == _manager.localPlayer)
                {
                    OnRpcRejection(_manager.localPlayer, rejection, true);
                    return;
                }

                if (immediate)
                    SendImmediateRejection(originalSender, rejection, channel);
                else
                    _playersManager.Send(originalSender, rejection, channel);
                return;
            }

            rejection.forward = originalSender;

            if (immediate)
                SendImmediateRejection(null, rejection, channel);
            else
                _playersManager.SendToServer(rejection, channel);
        }

        private void SendImmediateRejection(PlayerID? target, RpcRejection rejection, Channel channel)
        {
            var wrapped = new ImmediateRpcRejection { rejection = rejection };

            if (target.HasValue)
                _playersManager.Send(target.Value, wrapped, channel);
            else
                _playersManager.SendToServer(wrapped, channel);

            if (_manager.TryGetModule<RPCModule>(_asServer, out var rpcModule))
                rpcModule.MarkImmediateContentPending();
        }

        [UsedByIL]
        public static Task GetNextIdStatic(PlayerID? target, RPCType rpcType, float timeout, out RpcRequest request)
        {
            var networkManager = NetworkManager.main;
            request = default;

            if (!networkManager)
            {
                return Task.FromException(new InvalidOperationException(
                    "NetworkManager is not initialized. Make sure you have a NetworkManager active."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule(out RpcRequestResponseModule rpcModule, asServer))
            {
                return Task.FromException(new InvalidOperationException(
                    "RpcRequestResponseModule is not initialized.."));
            }

            return rpcModule.GetNextId(target, timeout, out request);
        }

        [UsedByIL]
        public static Task<T> GetNextIdStatic<T>(PlayerID? target, RPCType rpcType, float timeout,
            out RpcRequest request)
        {
            var networkManager = NetworkManager.main;
            request = default;

            if (!networkManager)
            {
                return Task.FromException<T>(new InvalidOperationException(
                    "NetworkManager is not initialized. Make sure you have a NetworkManager active."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule(out RpcRequestResponseModule rpcModule, asServer))
            {
                return Task.FromException<T>(new InvalidOperationException(
                    "RpcRequestResponseModule is not initialized.."));
            }

            return rpcModule.GetNextId<T>(target, timeout, out request);
        }

        [UsedByIL]
        public static RawTask GetNextIdUniTaskStatic(PlayerID? target, RPCType rpcType, float timeout,
            out RpcRequest request)
        {
            var networkManager = NetworkManager.main;
            request = default;

            if (!networkManager)
            {
                return RawTask.FromException(new InvalidOperationException(
                    "NetworkManager is not initialized. Make sure you have a NetworkManager active."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule(out RpcRequestResponseModule rpcModule, asServer))
            {
                return RawTask.FromException(new InvalidOperationException(
                    "RpcRequestResponseModule is not initialized.."));
            }

            return rpcModule.GetNextIdUniTask(target, timeout, out request);
        }

        [UsedByIL]
#if !UNITASK_PURRNET_SUPPORT
        public static Task<T>
#else
        public static UniTask<T>
#endif
        GetNextIdUniTaskStatic<T>(PlayerID? target, RPCType rpcType, float timeout,
            out RpcRequest request)
        {
            var networkManager = NetworkManager.main;
            request = default;

            if (!networkManager)
            {
                return RawTask.FromException<T>(new InvalidOperationException(
                    "NetworkManager is not initialized. Make sure you have a NetworkManager active."));
            }

            bool asServer = rpcType switch
            {
                RPCType.ServerRPC => !networkManager.isClient,
                RPCType.TargetRPC => networkManager.isServer,
                RPCType.ObserversRPC => networkManager.isServer,
                _ => throw new ArgumentOutOfRangeException(nameof(rpcType), rpcType, null)
            };

            if (!networkManager.TryGetModule(out RpcRequestResponseModule rpcModule, asServer))
            {
                return RawTask.FromException<T>(new InvalidOperationException(
                    "RpcRequestResponseModule is not initialized.."));
            }

            return rpcModule.GetNextIdUniTask<T>(target, timeout, out request);
        }

        public Task GetNextId(PlayerID? target, float timeout, out RpcRequest request)
        {
            var completer = new TaskRequestCompleter();
            request = NewRequest(target, timeout, completer);
            _requests.Add(request);
            return completer.task;
        }

        public RawTask GetNextIdUniTask(PlayerID? target, float timeout, out RpcRequest request)
        {
            var completer = new UniTaskRequestCompleter();
            request = NewRequest(target, timeout, completer);
            _requests.Add(request);
            return completer.task;
        }

#if !UNITASK_PURRNET_SUPPORT
        public Task<T>
#else
        public UniTask<T>
#endif
        GetNextIdUniTask<T>(PlayerID? target, float timeout, out RpcRequest request)
        {
            var completer = new UniTaskRequestCompleter<T>();
            request = NewRequest(target, timeout, completer);
            _requests.Add(request);
            return completer.task;
        }

        public Task<T> GetNextId<T>(PlayerID? target, float timeout, out RpcRequest request)
        {
            var completer = new TaskRequestCompleter<T>();
            request = NewRequest(target, timeout, completer);
            _requests.Add(request);
            return completer.task;
        }

        private RpcRequest NewRequest(PlayerID? target, float timeout, IRpcRequestCompleter completer)
        {
            return new RpcRequest
            {
                id = _nextId++,
                target = target,
                timeSent = Time.unscaledTime,
                timeout = timeout,
                completer = completer
            };
        }

        public void FixedUpdate()
        {
            for (int i = 0; i < _requests.Count; i++)
            {
                var request = _requests[i];
                if (Time.unscaledTime - request.timeSent > request.timeout)
                {
                    _requests.RemoveAt(i);
                    i--;
                    request.completer.Fail(new TimeoutException(
                        $"Async RPC with request id of '{request.id}' timed out after {request.timeout} seconds."));
                }
            }
        }

        private void SendResponseToSender(RpcResponse responsePacket, RPCInfo info, Channel channel)
        {
            // Server responding to its own RPC - direct loopback since
            // BroadcastModule.SendToServer is a no-op on the server side
            if (info.asServer && info.sender.isServer)
            {
                OnRpcResponse(info.sender, responsePacket, true);
                return;
            }

            var mtuOverride = info.compileTimeSignature.mtuExceeded.AsOverride();

            if (info.compileTimeSignature.immediate)
            {
                SendImmediateResponse(info.asServer ? info.sender : (PlayerID?)null, responsePacket, channel, mtuOverride);
                return;
            }

            if (info.asServer)
                _playersManager.Send(info.sender, responsePacket, channel, mtuOverride);
            else
                _playersManager.SendToServer(responsePacket, channel, mtuOverride);
        }

        private void SendImmediateResponse(PlayerID? target, RpcResponse responsePacket, Channel channel,
            MTUExceededBehaviour? mtuOverride)
        {
            var wrapped = new ImmediateRpcResponse { response = responsePacket };

            if (target.HasValue)
                _playersManager.Send(target.Value, wrapped, channel, mtuOverride);
            else
                _playersManager.SendToServer(wrapped, channel, mtuOverride);

            // the response bypasses the immediate RPC batch, so poke the module
            // to still force a transport send this frame
            if (_manager.TryGetModule<RPCModule>(_asServer, out var rpcModule))
                rpcModule.MarkImmediateContentPending();
        }

        private static void SendEmptyResponse(RPCInfo info, uint reqId, NetworkManager manager)
        {
            try
            {
                if (!manager.TryGetModule<RpcRequestResponseModule>(info.asServer, out var rpcModule))
                    return;

                var isForwarded = info is { asServer: false, sender: { isServer: false } };

                var responsePacket = new RpcResponse
                {
                    id = reqId,
                    data = ByteData.empty,
                    forward = isForwarded ? info.sender : null,
                    channel = isForwarded ? info.compileTimeSignature.channel : null
                };

                var channel = info.compileTimeSignature.channel;
                rpcModule.SendResponseToSender(responsePacket, info, channel);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to send empty RPC response: {e}");
            }
        }

        static IEnumerator WaitThen(IEnumerator coroutine, Action action, Action onError)
        {
            Exception caught = null;

            while (true)
            {
                try
                {
                    if (!coroutine.MoveNext())
                        break;
                }
                catch (Exception ex)
                {
                    caught = ex;
                    break;
                }

                yield return coroutine.Current;
            }

            if (caught != null)
            {
                PurrLogger.LogError($"Error while processing RPC coroutine: {caught}");
                onError?.Invoke();
                yield break;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                PurrLogger.LogError($"Error while processing RPC response: {ex}");
                onError?.Invoke();
            }
        }

        [UsedByIL]
        public static void CompleteRequestWithCoroutine(IEnumerator response, RPCInfo info, uint reqId,
            NetworkManager manager)
        {
            try
            {
                manager.StartCoroutine(WaitThen(response, () =>
                {
                    SendEmptyResponse(info, reqId, manager);
                },
                () =>
                {
                    SendEmptyResponse(info, reqId, manager);
                }));
            }
            catch (Exception ex)
            {
                PurrLogger.LogError($"Error while processing RPC response: {ex}");
                SendEmptyResponse(info, reqId, manager);
            }
        }

        [UsedByIL]
        public static async void CompleteRequestWithEmptyResponse(Task response, RPCInfo info, uint reqId,
            NetworkManager manager)
        {
            try
            {
                await response;
                SendEmptyResponse(info, reqId, manager);
            }
            catch (Exception ex)
            {
                PurrLogger.LogError($"Error while processing RPC response: {ex}");
                SendEmptyResponse(info, reqId, manager);
            }
        }

        private static Type GetTaskResultType(object maybeTask)
        {
            if (maybeTask == null)
                return null;

            var type = maybeTask.GetType();

            // Handle generic like Task<T>, UniTask<T>
            if (type.IsGenericType)
                return type.GetGenericArguments()[0];

            // Non-generic Task/UniTask (void)
            return null;
        }

        static async Task<object> AwaitAnyTaskAsync(object maybeTask)
        {
            if (maybeTask == null)
                return null;

            var type = maybeTask.GetType();
            var getAwaiter = type.GetMethod("GetAwaiter");
            if (getAwaiter == null)
                return null;

            var awaiter = getAwaiter.Invoke(maybeTask, null);
            if (awaiter == null)
                return null;

            var isCompletedProp = awaiter.GetType().GetProperty("IsCompleted");
            var isCompleted = (bool)(isCompletedProp?.GetValue(awaiter) ?? false);

            var getResult = awaiter.GetType().GetMethod("GetResult");
            var onCompleted = awaiter.GetType().GetMethod("OnCompleted");

            if (!isCompleted)
            {
                var tcs = new TaskCompletionSource<bool>();
                onCompleted?.Invoke(awaiter, new object[]
                {
                    (Action)(() => tcs.TrySetResult(true))
                });
                await tcs.Task;
            }

            // This will throw if the awaited task failed — which is what we want
            return getResult?.Invoke(awaiter, null);
        }

        [UsedByIL]
        public static void CompleteRequestWithResponseObject(object task, RPCInfo info, uint reqId, NetworkManager manager)
        {
            CompleteRequestWithResponse(task, info, reqId, manager);
        }

        [UsedByIL]
        public static async void CompleteRequestWithResponse(object task, RPCInfo info, uint reqId, NetworkManager manager)
        {
            try
            {
                var taskResultType = GetTaskResultType(task);
                object result = await AwaitAnyTaskAsync(task);

                if (manager.TryGetModule<RpcRequestResponseModule>(info.asServer, out var rpcModule))
                {
                    var isForwarded = info is { asServer: false, sender: { isServer: false } };

                    using var tmpStream = RPCModule.AllocStream(false);

                    Packer.Write(tmpStream, taskResultType, result);

                    var responsePacket = new RpcResponse
                    {
                        id = reqId,
                        data = tmpStream.ToByteData(),
                        forward = isForwarded ? info.sender : null,
                        channel = isForwarded ? info.compileTimeSignature.channel : null
                    };

                    var channel = info.compileTimeSignature.channel;
                    rpcModule.SendResponseToSender(responsePacket, info, channel);
                }
                else
                {
                    PurrLogger.LogError("Failed to get module, response won't be sent and receiver will timeout.");
                }
            }
            catch (Exception ex)
            {
                PurrLogger.LogError($"Error while processing RPC response: {ex}");
            }
        }

        [UsedByIL]
        public static void CompleteRequestWithUniTaskObject(object task, RPCInfo info, uint reqId, NetworkManager manager)
        {
            CompleteRequestWithUniTask(task, info, reqId, manager);
        }

        [UsedByIL]
        public static async void CompleteRequestWithUniTask(object task, RPCInfo info, uint reqId, NetworkManager manager)
        {
            try
            {
                var taskResultType = GetTaskResultType(task);
                object result = await AwaitAnyTaskAsync(task);

                if (manager.TryGetModule<RpcRequestResponseModule>(info.asServer, out var rpcModule))
                {
                    var isForwarded = info is { asServer: false, sender: { isServer: false } };
                    using var tmpStream = RPCModule.AllocStream(false);

                    Packer.Write(tmpStream, taskResultType, result);

                    var responsePacket = new RpcResponse
                    {
                        id = reqId,
                        data = tmpStream.ToByteData(),
                        forward = isForwarded ? info.sender : null,
                        channel = isForwarded ? info.compileTimeSignature.channel : null
                    };

                    var channel = info.compileTimeSignature.channel;
                    rpcModule.SendResponseToSender(responsePacket, info, channel);
                }
                else
                {
                    PurrLogger.LogError("Failed to get module, response won't be sent and receiver will timeout.");
                }
            }
            catch (Exception ex)
            {
                PurrLogger.LogError($"Error while processing RPC response: {ex}");
            }
        }

        [UsedByIL]
        public static void CompleteRequestWithResponseObject<T>(object task, RPCInfo info, uint reqId,
            NetworkManager manager)
        {
            if (task is not Task<T> response)
            {
                PurrLogger.LogError("Task is not UniTask<T>, response won't be sent and receiver will timeout.");
                return;
            }

            CompleteRequestWithResponse(response, info, reqId, manager);
        }

        [UsedByIL]
        public static async void CompleteRequestWithResponse<T>(Task<T> response, RPCInfo info, uint reqId,
            NetworkManager manager)
        {
            try
            {
                var result = await response;

                if (manager.TryGetModule<RpcRequestResponseModule>(info.asServer, out var rpcModule))
                {
                    var isForwarded = info is { asServer: false, sender: { isServer: false } };
                    using var tmpStream = RPCModule.AllocStream(false);

                    Packer<T>.Write(tmpStream, result);

                    var responsePacket = new RpcResponse
                    {
                        id = reqId,
                        data = tmpStream.ToByteData(),
                        forward = isForwarded ? info.sender : null,
                        channel = isForwarded ? info.compileTimeSignature.channel : null
                    };

                    var channel = info.compileTimeSignature.channel;
                    rpcModule.SendResponseToSender(responsePacket, info, channel);
                }
                else
                {
                    PurrLogger.LogError("Failed to get module, response won't be sent and receiver will timeout.");
                }
            }
            catch (Exception ex)
            {
                PurrLogger.LogError($"Error while processing RPC response: {ex}");
            }
        }

        [UsedByIL]
        public static async void CompleteRequestWithUniTaskEmptyResponse(RawTask response, RPCInfo info, uint reqId,
            NetworkManager manager)
        {
            try
            {
                await response;
                SendEmptyResponse(info, reqId, manager);
            }
            catch (Exception ex)
            {
                PurrLogger.LogError($"Error while processing RPC response: {ex}");
                SendEmptyResponse(info, reqId, manager);
            }
        }

        [UsedByIL]
        public static void CompleteRequestWithUniTaskObject<T>(object task, RPCInfo info, uint reqId,
            NetworkManager manager)
        {
#if !UNITASK_PURRNET_SUPPORT
            if (task is not Task<T> response)
#else
            if (task is not UniTask<T> response)
#endif
            {
                PurrLogger.LogError("Task is not UniTask<T>, response won't be sent and receiver will timeout.");
                return;
            }

            CompleteRequestWithUniTask(response, info, reqId, manager);
        }

        [UsedByIL]
        public static async void CompleteRequestWithUniTask<T>(
#if !UNITASK_PURRNET_SUPPORT
            Task<T> response
#else
            UniTask<T> response
#endif
            , RPCInfo info, uint reqId,
            NetworkManager manager)
        {
            try
            {
                var result = await response;

                if (manager.TryGetModule<RpcRequestResponseModule>(info.asServer, out var rpcModule))
                {
                    var isForwarded = info is { asServer: false, sender: { isServer: false } };
                    using var tmpStream = RPCModule.AllocStream(false);

                    Packer<T>.Write(tmpStream, result);

                    var responsePacket = new RpcResponse
                    {
                        id = reqId,
                        data = tmpStream.ToByteData(),
                        forward = isForwarded ? info.sender : null,
                        channel = isForwarded ? info.compileTimeSignature.channel : null
                    };

                    var channel = info.compileTimeSignature.channel;
                    rpcModule.SendResponseToSender(responsePacket, info, channel);
                }
                else
                {
                    PurrLogger.LogError("Failed to get module, response won't be sent and receiver will timeout.");
                }
            }
            catch (Exception ex)
            {
                PurrLogger.LogError($"Error while processing RPC response: {ex}");
            }
        }

        [UsedByIL]
        public static IEnumerator WaitForTask(Task task)
        {
            while (task is { IsCompleted: false })
                yield return null;

            if (task is { IsFaulted: true, Exception: not null })
                throw task.Exception;
        }
    }
}
