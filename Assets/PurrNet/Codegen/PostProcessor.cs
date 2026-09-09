#if UNITY_MONO_CECIL
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
#if UNITASK_PURRNET_SUPPORT
using Cysharp.Threading.Tasks;
#endif
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Editor;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Utils;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;
using UnityEngine.Scripting;
using Channel = PurrNet.Transports.Channel;
using MTUBehaviour = PurrNet.Transports.MTUBehaviour;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace PurrNet.Codegen
{
    public enum SpecialParamType
    {
        SenderId,
        RPCInfo
    }

    public struct RPCMethod
    {
        public RPCSignature Signature;
        public MethodDefinition originalMethod;
        public string ogName;
    }

    [UsedImplicitly]
    public class PostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            var name = compiledAssembly.Name;

            if (name.Contains("NuGetForUnity"))
                return false;

            if (name.EndsWith(".Analyzers", StringComparison.Ordinal) ||
                name.EndsWith(".Analyzer", StringComparison.Ordinal))
                return false;

            if (name.StartsWith("Unity."))
                return false;

            if (name.StartsWith("UnityEngine."))
                return false;

            return !name.Contains("Editor");
        }

        private static int GetIDOffset(TypeDefinition type, ICollection<DiagnosticMessage> messages)
        {
            try
            {
                var baseType = type.BaseType?.Resolve();

                if (baseType == null)
                    return 0;

                return GetIDOffset(baseType, messages) +
                       baseType.Methods.Count(m => GetMethodRPCType(m, messages).HasValue);
            }
            catch (Exception e)
            {
                messages.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Error,
                    MessageData = "Failed to get ID offset: " + e.Message + "\n" + e.StackTrace
                });

                return 0;
            }
        }

        public static bool InheritsFrom(TypeDefinition type, string baseTypeName)
        {
            try
            {
                if (type?.FullName == baseTypeName)
                    return true;

                if (type?.BaseType == null)
                    return false;

                if (type.BaseType.FullName == baseTypeName)
                    return true;

                var btype = type.BaseType.Resolve();
                return btype != null && InheritsFrom(btype, baseTypeName);
            }
            catch
            {
                return false;
            }
        }

        private static RPCSignature? GetMethodRPCType(MethodDefinition method, ICollection<DiagnosticMessage> messages)
        {
            RPCSignature data = default;
            int rpcCount = 0;

            foreach (var attribute in method.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == typeof(ServerRpcAttribute).FullName)
                {
                    if (attribute.ConstructorArguments.Count != 9)
                    {
                        Error(messages, "ServerRPC attribute must have 9 arguments", method);
                        return null;
                    }

                    var channel = (Channel)attribute.ConstructorArguments[0].Value;
                    var runLocally = (bool)attribute.ConstructorArguments[1].Value;
                    var requireOwnership = (bool)attribute.ConstructorArguments[2].Value;
                    var compressionLevel = (CompressionLevel)attribute.ConstructorArguments[3].Value;
                    var asyncTimeoutInSec = (float)attribute.ConstructorArguments[4].Value;
                    var stripCode = (StripCodeModeOverride)attribute.ConstructorArguments[5].Value;
                    var deltaPacked = (bool)attribute.ConstructorArguments[6].Value;
                    var mtuExceeded = (MTUBehaviour)attribute.ConstructorArguments[7].Value;
                    var immediate = (bool)attribute.ConstructorArguments[8].Value;

                    data = new RPCSignature
                    {
                        type = RPCType.ServerRPC,
                        channel = channel,
                        runLocally = runLocally,
                        requireOwnership = requireOwnership,
                        requireServer = false,
                        bufferLast = false,
                        excludeOwner = false,
                        isStatic = method.IsStatic,
                        asyncTimeoutInSec = asyncTimeoutInSec,
                        compressionLevel = compressionLevel,
                        stripCodeMode = stripCode,
                        deltaPacked = deltaPacked,
                        mtuExceeded = mtuExceeded,
                        immediate = immediate
                    };
                    rpcCount++;
                }
                else if (attribute.AttributeType.FullName == typeof(ObserversRpcAttribute).FullName)
                {
                    if (attribute.ConstructorArguments.Count != 11)
                    {
                        Error(messages, "ObserversRPC attribute must have 11 arguments", method);
                        return null;
                    }

                    var channel = (Channel)attribute.ConstructorArguments[0].Value;
                    var runLocally = (bool)attribute.ConstructorArguments[1].Value;
                    var bufferLast = (bool)attribute.ConstructorArguments[2].Value;
                    var requireServer = (bool)attribute.ConstructorArguments[3].Value;
                    var excludeOwner = (bool)attribute.ConstructorArguments[4].Value;
                    var excludeSender = (bool)attribute.ConstructorArguments[5].Value;
                    var compressionLevel = (CompressionLevel)attribute.ConstructorArguments[6].Value;
                    var asyncTimeoutInSec = (float)attribute.ConstructorArguments[7].Value;
                    var deltaPacked = (bool)attribute.ConstructorArguments[8].Value;
                    var mtuExceeded = (MTUBehaviour)attribute.ConstructorArguments[9].Value;
                    var immediate = (bool)attribute.ConstructorArguments[10].Value;

                    if (bufferLast && deltaPacked)
                    {
                        Error(messages, "ObserversRPC cannot have both bufferLast and deltaPacked enabled", method);
                        return null;
                    }

                    data = new RPCSignature
                    {
                        type = RPCType.ObserversRPC,
                        channel = channel,
                        runLocally = runLocally,
                        bufferLast = bufferLast,
                        requireServer = requireServer,
                        requireOwnership = false,
                        excludeOwner = excludeOwner,
                        excludeSender = excludeSender,
                        isStatic = method.IsStatic,
                        asyncTimeoutInSec = asyncTimeoutInSec,
                        compressionLevel = compressionLevel,
                        deltaPacked = deltaPacked,
                        mtuExceeded = mtuExceeded,
                        immediate = immediate
                    };
                    rpcCount++;
                }
                else if (attribute.AttributeType.FullName == typeof(TargetRpcAttribute).FullName)
                {
                    if (attribute.ConstructorArguments.Count != 9)
                    {
                        Error(messages, "TargetRPC attribute must have 9 arguments", method);
                        return null;
                    }

                    var channel = (Channel)attribute.ConstructorArguments[0].Value;
                    var runLocally = (bool)attribute.ConstructorArguments[1].Value;
                    var bufferLast = (bool)attribute.ConstructorArguments[2].Value;
                    var requireServer = (bool)attribute.ConstructorArguments[3].Value;
                    var compressionLevel = (CompressionLevel)attribute.ConstructorArguments[4].Value;
                    var asyncTimeoutInSec = (float)attribute.ConstructorArguments[5].Value;
                    var deltaPacked = (bool)attribute.ConstructorArguments[6].Value;
                    var mtuExceeded = (MTUBehaviour)attribute.ConstructorArguments[7].Value;
                    var immediate = (bool)attribute.ConstructorArguments[8].Value;

                    if (bufferLast && deltaPacked)
                    {
                        Error(messages, "TargetRPC cannot have both bufferLast and deltaPacked enabled", method);
                        return null;
                    }

                    data = new RPCSignature
                    {
                        type = RPCType.TargetRPC,
                        channel = channel,
                        runLocally = runLocally,
                        bufferLast = bufferLast,
                        requireServer = requireServer,
                        requireOwnership = false,
                        excludeOwner = false,
                        excludeSender = false,
                        isStatic = method.IsStatic,
                        asyncTimeoutInSec = asyncTimeoutInSec,
                        compressionLevel = compressionLevel,
                        deltaPacked = deltaPacked,
                        mtuExceeded = mtuExceeded,
                        immediate = immediate
                    };
                    rpcCount++;
                }
            }

            switch (rpcCount)
            {
                case 0:
                    return null;
                case > 1:
                    Error(messages, "Method cannot have multiple RPC attributes", method);
                    return null;
            }

            if (data.channel == Channel.UnreliableSequenced &&
                data.mtuExceeded != MTUBehaviour.NetworkManager)
            {
                Warning(messages,
                    "mtuExceeded override is ignored on UnreliableSequenced; the NetworkManager setting governs the whole channel",
                    method);
            }

            if (data.immediate && data.channel != Channel.Unreliable)
            {
                Error(messages,
                    "immediate requires Channel.Unreliable; ordered and sequenced delivery cannot span an independently flushed lane",
                    method);
                return null;
            }

            return data;
        }

        public static void Warning(ICollection<DiagnosticMessage> messages, string message, MethodDefinition method)
        {
            AddDiagnostic(messages, message, method, DiagnosticType.Warning);
        }

        public static void Error(ICollection<DiagnosticMessage> messages, string message, MethodDefinition method)
        {
            AddDiagnostic(messages, message, method, DiagnosticType.Error);
        }

        static void AddDiagnostic(ICollection<DiagnosticMessage> messages, string message, MethodDefinition method,
            DiagnosticType type)
        {
            try
            {
                if (method.DebugInformation.HasSequencePoints)
                {
                    var first = method.DebugInformation.SequencePoints[0];
                    string file = first.Document.Url;
                    if (!string.IsNullOrEmpty(file))
                        file = '/' + file[file.IndexOf("Assets", StringComparison.Ordinal)..].Replace('\\', '/');
                    else file = string.Empty;

                    messages.Add(new DiagnosticMessage
                    {
                        DiagnosticType = type,
                        MessageData = message,
                        Column = first.StartColumn,
                        Line = first.StartLine,
                        File = file
                    });
                }
                else
                {
                    messages.Add(new DiagnosticMessage
                    {
                        DiagnosticType = type,
                        MessageData = $"[{method.DeclaringType.FullName}] {message}"
                    });
                }
            }
            catch
            {
                messages.Add(new DiagnosticMessage
                {
                    DiagnosticType = type,
                    MessageData = $"[{method.DeclaringType.FullName}] {message}"
                });
            }
        }

        static bool ShouldIgnore(RPCType rpcType, ParameterReference param, int index, int count,
            out SpecialParamType type)
        {
            if (index == count - 1 && param.ParameterType.FullName == typeof(RPCInfo).FullName)
            {
                type = SpecialParamType.RPCInfo;
                return true;
            }

            if (index == 0 && rpcType == RPCType.TargetRPC && GetArgType(param.ParameterType) != TargetArgType.None)
            {
                type = SpecialParamType.SenderId;
                return true;
            }

            type = default;
            return false;
        }

        private static void HandleRPCReceiver(ModuleDefinition module, TypeDefinition type,
            DisposableList<RPCMethod> originalRpcs, bool isNetworkClass, int offset)
        {
            for (var i = 0; i < originalRpcs.Count; i++)
            {
                var attributes = MethodAttributes.Private | MethodAttributes.HideBySig;

                if (originalRpcs[i].Signature.isStatic)
                    attributes |= MethodAttributes.Static;

                bool isValidReturn = ValidateReturnType(originalRpcs[i].originalMethod, out var returnMode);

                if (!isValidReturn)
                    continue;

                var voidType = module.TypeSystem.Void;
                var newMethod = new MethodDefinition($"HandleRPCGenerated_{offset + i}", attributes, voidType)
                {
                    IsPublic = true
                };

                var preserveAttribute = module.GetTypeDefinition<PreserveAttribute>();
                var constructor = preserveAttribute.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters)
                    .Import(module);
                newMethod.CustomAttributes.Add(new CustomAttribute(constructor));

                var streamType = module.GetTypeDefinition<BitPacker>().Import(module);
                var bitDataType = module.GetTypeDefinition<BitData>().Import(module);

                var packetType = originalRpcs[i].Signature.isStatic ? module.GetTypeDefinition<StaticRPCPacket>() :
                    isNetworkClass ? module.GetTypeDefinition<ChildRPCPacket>() : module.GetTypeDefinition<RPCPacket>();

                var rpcInfo = module.GetTypeDefinition<RPCInfo>();

                var packet = new ParameterDefinition("packet", ParameterAttributes.None, packetType.Import(module));
                var info = new ParameterDefinition("info", ParameterAttributes.None, rpcInfo.Import(module));
                var asServer = new ParameterDefinition("asServer", ParameterAttributes.None, module.TypeSystem.Boolean);

                newMethod.Parameters.Add(packet);
                newMethod.Parameters.Add(info);
                newMethod.Parameters.Add(asServer);
                newMethod.Body.InitLocals = true;

                var code = newMethod.Body.GetILProcessor();
                var end = Instruction.Create(OpCodes.Ret);

                EmitSetCompileTimeSignature(module, isNetworkClass, originalRpcs[i], code, info);

                // Add debug information pointing to the original method
                try
                {
                    if (originalRpcs[i].originalMethod.DebugInformation.HasSequencePoints)
                    {
                        var firstSequencePoint = originalRpcs[i].originalMethod.DebugInformation.SequencePoints[0];
                        var document = firstSequencePoint.Document;

                        // Get the first instruction of the new method
                        var firstInstruction = newMethod.Body.Instructions[0];

                        var newSequencePoint = new SequencePoint(firstInstruction, document)
                        {
                            StartLine = firstSequencePoint.StartLine,
                            StartColumn = firstSequencePoint.StartColumn,
                            EndLine = firstSequencePoint.EndLine,
                            EndColumn = firstSequencePoint.EndColumn
                        };

                        newMethod.DebugInformation.SequencePoints.Add(newSequencePoint);
                    }
                }
                catch
                {
                    // ignore
                }

                // call RPCModule.PostProcessRPC(RPCSignature signature, ref BitPacker packer)
                var rpcModule = module.GetTypeDefinition<RPCModule>();
                var postProcessRPC = rpcModule.GetMethod("PostProcessRpc").Import(module);

                var bitDataVariable = new VariableDefinition(bitDataType);
                var streamVariable = new VariableDefinition(streamType);

                newMethod.Body.Variables.Add(bitDataVariable);
                newMethod.Body.Variables.Add(streamVariable);

                // store packet.data into bitData
                var dataProperty = packetType.GetField("data").Import(module);
                code.Append(Instruction.Create(OpCodes.Ldarga, packet));
                code.Append(Instruction.Create(OpCodes.Ldfld, dataProperty));
                code.Append(Instruction.Create(OpCodes.Stloc, bitDataVariable));

                // get packet.data field
                code.Append(Instruction.Create(OpCodes.Ldarg, info));
                code.Append(Instruction.Create(OpCodes.Ldloca_S, bitDataVariable));
                code.Append(Instruction.Create(OpCodes.Call, postProcessRPC));

                // stream = bitData.packer
                var packerField = bitDataType.GetField("packer").Import(module);
                code.Append(Instruction.Create(OpCodes.Ldloca_S, bitDataVariable));
                code.Append(Instruction.Create(OpCodes.Ldfld, packerField));
                code.Append(Instruction.Create(OpCodes.Stloc, streamVariable));

                bool useDeltaPacking = originalRpcs[i].Signature.deltaPacked && originalRpcs[i].Signature.type != RPCType.ServerRPC;
                bool isAwaitable = returnMode != ReturnMode.Void;

                VariableDefinition rpcPacker = null;
                VariableDefinition reqId = null;

                if (useDeltaPacking)
                    rpcPacker = EmitRpcPackerCreation(module, code, newMethod, originalRpcs[i], isNetworkClass);

                if (isAwaitable)
                    reqId = EmitRequestIdParsing(module, code, newMethod, streamVariable, rpcPacker, useDeltaPacking);

                EmitValidateCall(module, isNetworkClass, originalRpcs[i], code, info, packet, asServer, reqId, isAwaitable, end);

                try
                {
                    if (originalRpcs[i].originalMethod.DeclaringType != null)
                    {
                        if (originalRpcs[i].originalMethod.HasGenericParameters)
                        {
                            HandleGenericRPCReceiver(module, originalRpcs[i], newMethod, streamVariable, info, isNetworkClass, rpcPacker, reqId);
                        }
                        else
                            HandleNonGenericRPCReceiver(module, originalRpcs[i], newMethod, streamVariable, info, returnMode,
                                isNetworkClass, rpcPacker, reqId);
                    }
                }
                catch (Exception e)
                {
                    throw new Exception($"Failed to handle RPC: {e.Message}\n{e.StackTrace}");
                }

                code.Append(end);
                type.Methods.Add(newMethod);
            }
        }

        private static VariableDefinition EmitRpcPackerCreation(ModuleDefinition module, ILProcessor code,
            MethodDefinition newMethod, RPCMethod rpcMethod, bool isNetworkClass)
        {
            var RPCPacketPackerType = module.GetTypeDefinition<RPCPacketPacker>();
            var rpcPacker = new VariableDefinition(RPCPacketPackerType.Import(module));
            newMethod.Body.Variables.Add(rpcPacker);

            var createPackerForRPC = RPCPacketPackerType.GetMethod(GetCreateWithInfoName(rpcMethod, isNetworkClass))
                .Import(module);

            // NetworkManager manager, RPCPacket context, RPCInfo info
            PushNetworkManager(module, code, isNetworkClass, newMethod.IsStatic);

            if (rpcMethod.Signature.isStatic)
            {
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldarg_1));
            }
            else
            {
                code.Append(Instruction.Create(OpCodes.Ldarg_1));
                code.Append(Instruction.Create(OpCodes.Ldarg_2));
            }

            code.Append(Instruction.Create(OpCodes.Call, createPackerForRPC));
            code.Append(Instruction.Create(OpCodes.Stloc, rpcPacker));

            return rpcPacker;
        }

        private static VariableDefinition EmitRequestIdParsing(ModuleDefinition module, ILProcessor code,
            MethodDefinition newMethod, VariableDefinition streamVariable, VariableDefinition rpcPacker, bool useDeltaPacking)
        {
            var reqId = new VariableDefinition(module.TypeSystem.UInt32);
            newMethod.Body.Variables.Add(reqId);

            MethodReference serializer;
            if (useDeltaPacking)
            {
                serializer = CreateDeltaSerializer(module, module.TypeSystem.UInt32, rpcPacker, false);
                code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
            }
            else serializer = CreateSerializer(module, module.TypeSystem.UInt32, false);

            code.Append(Instruction.Create(OpCodes.Ldloc_S, streamVariable));
            code.Append(Instruction.Create(OpCodes.Ldloca, reqId));
            code.Append(Instruction.Create(OpCodes.Call, serializer));

            return reqId;
        }

        private static void HandleRPCReceiverHandler(ModuleDefinition module, TypeDefinition type,
            DisposableList<RPCMethod> originalRpcs, bool isNetworkModule, int offset, bool isStaticPass)
        {
            var rpcInfoType = module.GetTypeDefinition<RPCInfo>().Import(module);
            var packetType = isStaticPass
                ? module.GetTypeDefinition<StaticRPCPacket>().Import(module)
                : isNetworkModule
                    ? module.GetTypeDefinition<ChildRPCPacket>().Import(module)
                    : module.GetTypeDefinition<RPCPacket>().Import(module);

            var attributes = isStaticPass ?
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Static :
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.ReuseSlot;

            var newMethod = new MethodDefinition($"OnReceivedRpc", attributes,
                module.TypeSystem.Void)
            {
                IsPublic = true,
                HasThis = !isStaticPass
            };

            var id = new ParameterDefinition("id", ParameterAttributes.None, module.TypeSystem.Int32);
            var packet = new ParameterDefinition("packet", ParameterAttributes.None, packetType);
            var info = new ParameterDefinition("info", ParameterAttributes.None, rpcInfoType);
            var asServer = new ParameterDefinition("asServer", ParameterAttributes.None, module.TypeSystem.Boolean);

            // int id, BitPacker stream, StaticRPCPacket packet, RPCInfo info, bool asServer
            newMethod.Parameters.Add(id);
            newMethod.Parameters.Add(packet);
            newMethod.Parameters.Add(info);
            newMethod.Parameters.Add(asServer);

            type.Methods.Add(newMethod);

            var body = newMethod.Body.GetILProcessor();

            for (int i = 0; i < originalRpcs.Count; i++)
            {
                var rpc = originalRpcs[i];

                if (rpc.Signature.isStatic != isStaticPass)
                    continue;

                var next = Instruction.Create(OpCodes.Nop);

                // if (id == $i)
                body.Append(Instruction.Create(OpCodes.Ldarg_S, id));
                body.Append(Instruction.Create(OpCodes.Ldc_I4, offset + i));
                body.Append(Instruction.Create(OpCodes.Bne_Un, next));

                // HandleRPCGenerated_$i(packer, packet, info, asServer);
                string methodName = $"HandleRPCGenerated_{offset + i}";

                var methodReference = new MethodReference(methodName, module.TypeSystem.Void, type);

                methodReference.Parameters.Add(new ParameterDefinition(packetType));
                methodReference.Parameters.Add(new ParameterDefinition(rpcInfoType));
                methodReference.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));

                methodReference.HasThis = !isStaticPass;
                methodReference = GetOriginalMethod(methodReference);

                if (!isStaticPass)
                    body.Append(Instruction.Create(OpCodes.Ldarg_0)); // this

                body.Append(Instruction.Create(OpCodes.Ldarg_S, packet));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, info));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, asServer));
                body.Append(Instruction.Create(OpCodes.Call, methodReference));
                body.Append(Instruction.Create(OpCodes.Ret));

                body.Append(next);
            }

            if (type.BaseType != null)
            {
                var parent = type.BaseType;
                string methodName = $"OnReceivedRpc";
                var reference = new MethodReference(methodName, module.TypeSystem.Void, parent);

                reference.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
                reference.Parameters.Add(new ParameterDefinition(packetType));
                reference.Parameters.Add(new ParameterDefinition(rpcInfoType));
                reference.Parameters.Add(new ParameterDefinition(module.TypeSystem.Boolean));

                reference.HasThis = !isStaticPass;

                if (!isStaticPass)
                    body.Append(Instruction.Create(OpCodes.Ldarg_0)); // this

                body.Append(Instruction.Create(OpCodes.Ldarg_S, id));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, packet));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, info));
                body.Append(Instruction.Create(OpCodes.Ldarg_S, asServer));

                body.Append(Instruction.Create(OpCodes.Call, reference));

                if (!isStaticPass)
                    newMethod.Overrides.Add(reference);
            }


            body.Append(Instruction.Create(OpCodes.Ret));
        }

        private static void EmitSetCompileTimeSignature(ModuleDefinition module, bool isNetworkClass, RPCMethod originalRpc,
            ILProcessor code, ParameterDefinition info)
        {
            var compileTimeSignatureField = info.ParameterType.GetField("compileTimeSignature").Import(module);

            code.Append(Instruction.Create(OpCodes.Ldarga, info));
            PushRPCSignature(module, code, originalRpc, true, isNetworkClass);
            code.Append(Instruction.Create(OpCodes.Stfld, compileTimeSignatureField));
        }

        private static void EmitValidateCall(ModuleDefinition module, bool isNetworkClass, RPCMethod originalRpc,
            ILProcessor code, ParameterDefinition info, ParameterDefinition data, ParameterDefinition asServer,
            VariableDefinition reqId, bool isAwaitable, Instruction end)
        {
            var compileTimeSignatureField = info.ParameterType.GetField("compileTimeSignature").Import(module);

            MethodReference validateReceivingRPCGeneric;
            if (originalRpc.Signature.isStatic)
            {
                var rpcModule = module.GetTypeDefinition<RPCModule>();
                validateReceivingRPCGeneric = rpcModule.GetMethod("ValidateReceivingStaticRPC", true).Import(module);
            }
            else if (isNetworkClass)
            {
                var nclass = module.GetTypeDefinition<NetworkModule>();
                validateReceivingRPCGeneric = nclass.GetMethod("ValidateReceivingRPC", true).Import(module);

                // Call validateReceivingRPC(this, RPCInfo, RPCSignature, asServer, requestId, isAwaitable)
                code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this
            }
            else
            {
                var identityType = module.GetTypeDefinition<NetworkIdentity>();
                validateReceivingRPCGeneric = identityType.GetMethod("ValidateReceivingRPC", true).Import(module);

                // Call validateReceivingRPC(this, RPCInfo, RPCSignature, asServer, requestId, isAwaitable)
                code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this
            }

            // make generic of validateReceivingRPC of rpc
            var validateReceivingRPC = new GenericInstanceMethod(validateReceivingRPCGeneric);
            validateReceivingRPC.GenericArguments.Add(data.ParameterType);

            // RPCInfo info, RPCSignature signature, INetworkedData data, bool asServer, uint requestId, bool isAwaitable
            code.Append(Instruction.Create(OpCodes.Ldarg, info)); // info

            code.Append(Instruction.Create(OpCodes.Ldarga, info));
            code.Append(Instruction.Create(OpCodes.Ldfld, compileTimeSignatureField));
            code.Append(Instruction.Create(OpCodes.Ldarg, data)); // data
            code.Append(Instruction.Create(OpCodes.Ldarg, asServer)); // asServer

            if (reqId != null)
                code.Append(Instruction.Create(OpCodes.Ldloc, reqId));
            else
                code.Append(Instruction.Create(OpCodes.Ldc_I4_0));

            code.Append(Instruction.Create(isAwaitable ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));

            code.Append(Instruction.Create(OpCodes.Call, validateReceivingRPC));

            // if returned false, return early
            code.Append(Instruction.Create(OpCodes.Brfalse, end));
        }

        private static void HandleNonGenericRPCReceiver(
            ModuleDefinition module,
            RPCMethod rpcMethod,
            MethodDefinition newMethod,
            VariableDefinition streamVariable,
            ParameterDefinition info,
            ReturnMode returnMode,
            bool isNetworkClass,
            VariableDefinition rpcPacker,
            VariableDefinition reqId)
        {
            var originalMethod = rpcMethod.originalMethod;
            int paramCount = originalMethod.Parameters.Count;

            var hasAsyncPackableParam = false;
            for (var p = 0; p < paramCount; p++)
            {
                var param = originalMethod.Parameters[p];
                if (ShouldIgnore(rpcMethod.Signature.type, param, p, paramCount, out _)) continue;
                if (param.ParameterType is GenericParameter) continue;
                var def = param.ParameterType.Resolve();
                if (def != null && GenerateSerializersProcessor.HasInterface(def, typeof(IAsyncPackable)))
                {
                    hasAsyncPackableParam = true;
                    break;
                }
            }

            if (hasAsyncPackableParam)
            {
                HandleNonGenericRPCReceiverAsyncPackable(module, rpcMethod, newMethod, streamVariable, info,
                    returnMode, isNetworkClass, rpcPacker, reqId);
                return;
            }

            var code = newMethod.Body.GetILProcessor();

            var managerType = module.GetTypeDefinition<NetworkManager>();
            var networkModule = module.GetTypeDefinition<NetworkModule>();
            var identityType = module.GetTypeDefinition<NetworkIdentity>();
            var rpcReqRespType = module.GetTypeDefinition<RpcRequestResponseModule>();
            var rpcModule = originalMethod.DeclaringType.Module.GetTypeDefinition<RPCModule>();
            var getLocalPlayer = rpcModule.GetMethod("GetLocalPlayer").Import(module);
            var responder = rpcReqRespType.GetMethod("CompleteRequestWithResponse", true).Import(module);
            var responderUniTask = rpcReqRespType.GetMethod("CompleteRequestWithUniTask", true).Import(module);
            var responderWithoutResponse = rpcReqRespType.GetMethod("CompleteRequestWithEmptyResponse").Import(module);
            var responderCoroutine = rpcReqRespType.GetMethod("CompleteRequestWithCoroutine").Import(module);
            var responderUniTaskWithoutResponse =
                rpcReqRespType.GetMethod("CompleteRequestWithUniTaskEmptyResponse").Import(module);

            var localPlayerProp = identityType.GetProperty("localPlayerForced");
            var localPlayerGetter = localPlayerProp.GetMethod.Import(module);

            var localPlayerPropModule = networkModule.GetProperty("localPlayerForced");
            var localPlayerGetterModule = localPlayerPropModule.GetMethod.Import(module);

            var networkManagerProp = identityType.GetProperty("networkManager");
            var getNetworkManager = networkManagerProp.GetMethod.Import(module);

            var networkManagerModuleProp = networkModule.GetProperty("networkManager");
            var getNetworkManagerModule = networkManagerModuleProp.GetMethod.Import(module);

            var mainManagerProp = managerType.GetProperty("main");
            var mainManagerGetter = mainManagerProp.GetMethod.Import(module);

            bool useDeltaPacking = rpcPacker != null;

            for (var p = 0; p < originalMethod.Parameters.Count; p++)
            {
                var param = originalMethod.Parameters[p];
                var variable = new VariableDefinition(param.ParameterType);
                newMethod.Body.Variables.Add(variable);

                if (ShouldIgnore(rpcMethod.Signature.type, param, p, paramCount, out var specialType))
                {
                    switch (specialType)
                    {
                        case SpecialParamType.RPCInfo:
                            code.Append(Instruction.Create(OpCodes.Ldarg, info));
                            code.Append(Instruction.Create(OpCodes.Stloc, variable));
                            break;
                        case SpecialParamType.SenderId:
                            if (GetArgType(param.ParameterType) == TargetArgType.Player)
                            {
                                if (!rpcMethod.Signature.isStatic)
                                {
                                    code.Append(Instruction.Create(OpCodes.Ldarg_0));
                                    code.Append(isNetworkClass
                                        ? Instruction.Create(OpCodes.Call, localPlayerGetterModule)
                                        : Instruction.Create(OpCodes.Call, localPlayerGetter));
                                }
                                else
                                {
                                    code.Append(Instruction.Create(OpCodes.Call, getLocalPlayer));
                                }

                                code.Append(Instruction.Create(OpCodes.Stloc, variable));
                            }

                            break;
                    }

                    continue;
                }

                MethodReference serialize;

                if (useDeltaPacking)
                {
                    serialize = CreateDeltaSerializer(module, param.ParameterType, rpcPacker, false);
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                }
                else serialize = CreateSerializer(module, param.ParameterType, false);

                code.Append(Instruction.Create(OpCodes.Ldloc_S, streamVariable));
                code.Append(Instruction.Create(OpCodes.Ldloca, variable));
                code.Append(Instruction.Create(OpCodes.Call, serialize));

                var paramDef = param.ParameterType.Resolve();
                if (paramDef != null && GenerateSerializersProcessor.HasInterface(paramDef, typeof(IAsyncPackable)))
                {
                    var prepareAfterUnpack = CreatePrepareAfterUnpackMethod(module, param.ParameterType);
                    code.Append(Instruction.Create(OpCodes.Ldloca, variable));
                    code.Append(Instruction.Create(OpCodes.Call, prepareAfterUnpack));
                }
            }

            if (!originalMethod.IsStatic)
                code.Append(Instruction.Create(OpCodes.Ldarg_0));

            var vars = newMethod.Body.Variables;
            int startIdx;
            if (useDeltaPacking)
                startIdx = (reqId == null ? 1 : 2);
            else startIdx = (reqId == null ? 0 : 1);

            for (var j = startIdx + 2; j < vars.Count; j++)
            {
                code.Append(Instruction.Create(OpCodes.Ldloc, vars[j]));
            }

            code.Append(Instruction.Create(OpCodes.Call, GetOriginalMethod(originalMethod)));

            if (reqId != null)
            {
                if (returnMode is ReturnMode.Task or ReturnMode.UniTask &&
                    originalMethod.ReturnType is GenericInstanceType genericInstance)
                {
                    if (genericInstance.GenericArguments.Count != 1)
                    {
                        code.Append(Instruction.Create(OpCodes.Pop));
                        return;
                    }

                    code.Append(Instruction.Create(OpCodes.Ldarg, info));
                    code.Append(Instruction.Create(OpCodes.Ldloc, reqId));
                    // load networkManager
                    PushNetworkManager(module, code, isNetworkClass, newMethod.IsStatic);

                    var genericResponse =
                        new GenericInstanceMethod(returnMode is ReturnMode.Task ? responder : responderUniTask);
                    genericResponse.GenericArguments.Add(genericInstance.GenericArguments[0]);
                    code.Append(Instruction.Create(OpCodes.Call, genericResponse));
                }
                else
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg, info));
                    code.Append(Instruction.Create(OpCodes.Ldloc, reqId));

                    // load networkManager
                    if (newMethod.IsStatic)
                    {
                        code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
                    }
                    else
                    {
                        code.Append(Instruction.Create(OpCodes.Ldarg_0));
                        code.Append(isNetworkClass
                            ? Instruction.Create(OpCodes.Call, getNetworkManagerModule)
                            : Instruction.Create(OpCodes.Call, getNetworkManager));
                    }

                    code.Append(returnMode switch
                    {
                        ReturnMode.IEnumerator => Instruction.Create(OpCodes.Call, responderCoroutine),
                        ReturnMode.UniTask => Instruction.Create(OpCodes.Call, responderUniTaskWithoutResponse),
                        ReturnMode.Task => Instruction.Create(OpCodes.Call, responderWithoutResponse),
                        _ => throw new ArgumentOutOfRangeException()
                    });
                }
            }
        }

        private static void HandleNonGenericRPCReceiverAsyncPackable(
            ModuleDefinition module,
            RPCMethod rpcMethod,
            MethodDefinition newMethod,
            VariableDefinition streamVariable,
            ParameterDefinition info,
            ReturnMode returnMode,
            bool isNetworkClass,
            VariableDefinition rpcPacker,
            VariableDefinition reqId)
        {
            var code = newMethod.Body.GetILProcessor();
            var originalMethod = rpcMethod.originalMethod;
            int paramCount = originalMethod.Parameters.Count;

            var packetType = rpcMethod.Signature.isStatic ? module.GetTypeDefinition<StaticRPCPacket>() :
                isNetworkClass ? module.GetTypeDefinition<ChildRPCPacket>() : module.GetTypeDefinition<RPCPacket>();
            var managerType = module.GetTypeDefinition<NetworkManager>();
            var networkModule = module.GetTypeDefinition<NetworkModule>();
            var identityType = module.GetTypeDefinition<NetworkIdentity>();
            var rpcReqRespType = module.GetTypeDefinition<RpcRequestResponseModule>();
            var rpcModule = originalMethod.DeclaringType.Module.GetTypeDefinition<RPCModule>();
            var getLocalPlayer = rpcModule.GetMethod("GetLocalPlayer").Import(module);
            var responder = rpcReqRespType.GetMethod("CompleteRequestWithResponse", true).Import(module);
            var responderUniTask = rpcReqRespType.GetMethod("CompleteRequestWithUniTask", true).Import(module);
            var responderWithoutResponse = rpcReqRespType.GetMethod("CompleteRequestWithEmptyResponse").Import(module);
            var responderCoroutine = rpcReqRespType.GetMethod("CompleteRequestWithCoroutine").Import(module);
            var responderUniTaskWithoutResponse =
                rpcReqRespType.GetMethod("CompleteRequestWithUniTaskEmptyResponse").Import(module);

            var localPlayerProp = identityType.GetProperty("localPlayerForced");
            var localPlayerGetter = localPlayerProp.GetMethod.Import(module);
            var localPlayerPropModule = networkModule.GetProperty("localPlayerForced");
            var localPlayerGetterModule = localPlayerPropModule.GetMethod.Import(module);
            var mainManagerProp = managerType.GetProperty("main");
            var mainManagerGetter = mainManagerProp.GetMethod.Import(module);

            bool useDeltaPacking = rpcPacker != null;

            ResolveTaskTypes(module, out var taskType, out var taskArrayType, out var taskOfTOpen,
                out var actionOfTOpen, out _, out var actionCtor);

            var actionOfTOpenResolved = actionOfTOpen.Resolve();
            var actionOfTaskArray = new GenericInstanceType(actionOfTOpen) { GenericArguments = { taskArrayType } };
            var actionOfTaskArrayCtorDef = actionOfTOpenResolved.Methods.First(m =>
                m.IsConstructor && m.Parameters.Count == 2);
            var actionOfTaskArrayCtor = new MethodReference(".ctor", module.TypeSystem.Void, actionOfTaskArray)
                { HasThis = true };
            actionOfTaskArrayCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            actionOfTaskArrayCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));

            var stateType = new TypeDefinition("", $"RpcReceiveState_{rpcMethod.originalMethod.MetadataToken.RID}",
                TypeAttributes.NestedPrivate | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                module.TypeSystem.Object);
            originalMethod.DeclaringType.NestedTypes.Add(stateType);

            var stateCtorBase = new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object)
                { HasThis = true };
            var objectCtor = module.ImportReference(
                module.TypeSystem.Object.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters));
            var stateCtor = new MethodDefinition(".ctor", MethodAttributes.Public, module.TypeSystem.Void);
            stateCtor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldarg_0));
            stateCtor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Call, objectCtor));
            stateCtor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));
            stateType.Methods.Add(stateCtor);

            var thisField = !originalMethod.IsStatic
                ? new FieldDefinition("_this", FieldAttributes.Public, originalMethod.DeclaringType.Import(module))
                : null;
            if (thisField != null) stateType.Fields.Add(thisField);

            var infoField = new FieldDefinition("_info", FieldAttributes.Public, info.ParameterType);
            stateType.Fields.Add(infoField);

            FieldDefinition reqIdStateField = null;
            if (reqId != null)
            {
                reqIdStateField = new FieldDefinition("_reqId", FieldAttributes.Public, module.TypeSystem.UInt32);
                stateType.Fields.Add(reqIdStateField);
            }

            var paramFields = new List<FieldDefinition>();
            var asyncParamParamIndices = new List<int>();
            for (var p = 0; p < paramCount; p++)
            {
                var param = originalMethod.Parameters[p];
                var field = new FieldDefinition($"_p{p}", FieldAttributes.Public, param.ParameterType);
                stateType.Fields.Add(field);
                paramFields.Add(field);

                if (!ShouldIgnore(rpcMethod.Signature.type, param, p, paramCount, out _) &&
                    param.ParameterType is not GenericParameter)
                {
                    var def = param.ParameterType.Resolve();
                    if (def != null && GenerateSerializersProcessor.HasInterface(def, typeof(IAsyncPackable)))
                        asyncParamParamIndices.Add(p);
                }
            }

            var invokeMethod = new MethodDefinition("InvokeAfterPrepare", MethodAttributes.Public, module.TypeSystem.Void);
            invokeMethod.Parameters.Add(new ParameterDefinition(taskArrayType));
            stateType.Methods.Add(invokeMethod);
            var invokeIl = invokeMethod.Body.GetILProcessor();

            var asyncIdx = 0;
            foreach (var p in asyncParamParamIndices)
            {
                var paramType = paramFields[p].FieldType;
                var taskOfT = new GenericInstanceType(taskOfTOpen) { GenericArguments = { paramType } };
                var getTaskResult = CreateGetTaskResultMethod(module, paramType);
                invokeIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                invokeIl.Append(Instruction.Create(OpCodes.Ldarg_1));
                invokeIl.Append(Instruction.Create(OpCodes.Ldc_I4, asyncIdx++));
                invokeIl.Append(Instruction.Create(OpCodes.Ldelem_Ref));
                invokeIl.Append(Instruction.Create(OpCodes.Castclass, taskOfT.Import(module)));
                invokeIl.Append(Instruction.Create(OpCodes.Call, getTaskResult));
                invokeIl.Append(Instruction.Create(OpCodes.Stfld, paramFields[p].Import(module)));
            }

            if (!originalMethod.IsStatic)
            {
                invokeIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                invokeIl.Append(Instruction.Create(OpCodes.Ldfld, thisField.Import(module)));
            }
            for (var p = 0; p < paramCount; p++)
            {
                invokeIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                invokeIl.Append(Instruction.Create(OpCodes.Ldfld, paramFields[p].Import(module)));
            }
            invokeIl.Append(Instruction.Create(OpCodes.Call, GetOriginalMethod(originalMethod)));

            if (reqIdStateField != null)
            {
                invokeIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                invokeIl.Append(Instruction.Create(OpCodes.Ldfld, infoField.Import(module)));
                invokeIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                invokeIl.Append(Instruction.Create(OpCodes.Ldfld, reqIdStateField.Import(module)));
                var getNetworkManager = identityType.GetProperty("networkManager").GetMethod.Import(module);
                var getNetworkManagerModule = networkModule.GetProperty("networkManager").GetMethod.Import(module);
                if (originalMethod.IsStatic)
                    invokeIl.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
                else
                {
                    invokeIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                    invokeIl.Append(Instruction.Create(OpCodes.Ldfld, thisField.Import(module)));
                    invokeIl.Append(Instruction.Create(OpCodes.Call,
                        isNetworkClass ? getNetworkManagerModule : getNetworkManager));
                }
                if (returnMode is ReturnMode.Task or ReturnMode.UniTask &&
                    originalMethod.ReturnType is GenericInstanceType genericInstance &&
                    genericInstance.GenericArguments.Count == 1)
                {
                    var genericResponse = new GenericInstanceMethod(returnMode is ReturnMode.Task ? responder : responderUniTask);
                    genericResponse.GenericArguments.Add(genericInstance.GenericArguments[0]);
                    invokeIl.Append(Instruction.Create(OpCodes.Call, genericResponse.Import(module)));
                }
                else
                {
                    invokeIl.Append(Instruction.Create(OpCodes.Call, returnMode switch
                    {
                        ReturnMode.IEnumerator => responderCoroutine,
                        ReturnMode.UniTask => responderUniTaskWithoutResponse,
                        ReturnMode.Task => responderWithoutResponse,
                        _ => responderWithoutResponse
                    }));
                }
            }
            invokeIl.Append(Instruction.Create(OpCodes.Ret));

            var packetParam = newMethod.Parameters[0];
            var infoParam = newMethod.Parameters[1];

            var stateVar = new VariableDefinition(stateType.Import(module));
            newMethod.Body.Variables.Add(stateVar);

            code.Append(Instruction.Create(OpCodes.Newobj, module.ImportReference(stateCtor)));
            code.Append(Instruction.Create(OpCodes.Stloc, stateVar));

            if (thisField != null)
            {
                code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Stfld, thisField.Import(module)));
            }
            code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
            code.Append(Instruction.Create(OpCodes.Ldarg, infoParam));
            code.Append(Instruction.Create(OpCodes.Stfld, infoField.Import(module)));
            if (reqIdStateField != null)
            {
                code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                code.Append(Instruction.Create(OpCodes.Ldloc, reqId));
                code.Append(Instruction.Create(OpCodes.Stfld, reqIdStateField.Import(module)));
            }

            var taskList = new List<VariableDefinition>();
            for (var p = 0; p < paramCount; p++)
            {
                var param = originalMethod.Parameters[p];
                var variable = new VariableDefinition(param.ParameterType);
                newMethod.Body.Variables.Add(variable);

                if (ShouldIgnore(rpcMethod.Signature.type, param, p, paramCount, out var specialType))
                {
                    switch (specialType)
                    {
                        case SpecialParamType.RPCInfo:
                            code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                            code.Append(Instruction.Create(OpCodes.Ldarg, infoParam));
                            code.Append(Instruction.Create(OpCodes.Stfld, paramFields[p].Import(module)));
                            break;
                        case SpecialParamType.SenderId:
                            if (GetArgType(param.ParameterType) == TargetArgType.Player)
                            {
                                code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                                if (!originalMethod.IsStatic)
                                {
                                    code.Append(Instruction.Create(OpCodes.Ldarg_0));
                                    code.Append(isNetworkClass
                                        ? Instruction.Create(OpCodes.Call, localPlayerGetterModule)
                                        : Instruction.Create(OpCodes.Call, localPlayerGetter));
                                }
                                else
                                    code.Append(Instruction.Create(OpCodes.Call, getLocalPlayer));
                                code.Append(Instruction.Create(OpCodes.Stfld, paramFields[p].Import(module)));
                            }
                            break;
                    }
                    continue;
                }

                var serialize = useDeltaPacking
                    ? CreateDeltaSerializer(module, param.ParameterType, rpcPacker, false)
                    : CreateSerializer(module, param.ParameterType, false);
                if (useDeltaPacking)
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                code.Append(Instruction.Create(OpCodes.Ldloc_S, streamVariable));
                code.Append(Instruction.Create(OpCodes.Ldloca, variable));
                code.Append(Instruction.Create(OpCodes.Call, serialize));

                var def = param.ParameterType.Resolve();
                if (def != null && GenerateSerializersProcessor.HasInterface(def, typeof(IAsyncPackable)))
                {
                    var prepareAsync = CreatePrepareAfterUnpackAsyncMethod(module, param.ParameterType);
                    code.Append(Instruction.Create(OpCodes.Ldloc, variable));
                    code.Append(Instruction.Create(OpCodes.Call, prepareAsync));
                    var taskVar = new VariableDefinition(taskType);
                    newMethod.Body.Variables.Add(taskVar);
                    taskList.Add(taskVar);
                    code.Append(Instruction.Create(OpCodes.Stloc, taskVar));
                }
                else
                {
                    var prepareSync = CreatePrepareAfterUnpackMethod(module, param.ParameterType);
                    code.Append(Instruction.Create(OpCodes.Ldloca, variable));
                    code.Append(Instruction.Create(OpCodes.Call, prepareSync));
                    code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                    code.Append(Instruction.Create(OpCodes.Ldloc, variable));
                    code.Append(Instruction.Create(OpCodes.Stfld, paramFields[p].Import(module)));
                }
            }

            code.Append(Instruction.Create(OpCodes.Ldc_I4, taskList.Count));
            code.Append(Instruction.Create(OpCodes.Newarr, taskType));
            var tasksArrayVar = new VariableDefinition(taskArrayType);
            newMethod.Body.Variables.Add(tasksArrayVar);
            code.Append(Instruction.Create(OpCodes.Stloc, tasksArrayVar));
            for (var t = 0; t < taskList.Count; t++)
            {
                code.Append(Instruction.Create(OpCodes.Ldloc, tasksArrayVar));
                code.Append(Instruction.Create(OpCodes.Ldc_I4, t));
                code.Append(Instruction.Create(OpCodes.Ldloc, taskList[t]));
                code.Append(Instruction.Create(OpCodes.Stelem_Ref));
            }

            var executeAfterPrepare = module.GetTypeDefinition(typeof(AsyncPackableHelper))
                .Methods.First(m => m.Name == "ExecuteAfterPrepareAsync" && m.Parameters.Count == 2 &&
                    m.Parameters[0].ParameterType.IsArray)
                .Import(module);

            code.Append(Instruction.Create(OpCodes.Ldloc, tasksArrayVar));
            code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
            code.Append(Instruction.Create(OpCodes.Ldftn, invokeMethod.Import(module)));
            code.Append(Instruction.Create(OpCodes.Newobj, module.ImportReference(actionOfTaskArrayCtor)));
            code.Append(Instruction.Create(OpCodes.Call, executeAfterPrepare));
            code.Append(Instruction.Create(OpCodes.Pop));
            code.Append(Instruction.Create(OpCodes.Ret));
        }

        private static string GetCreateWithInfoName(RPCMethod rpcMethod, bool isNetworkClass)
        {
            string methodName;

            if (isNetworkClass)
            {
                methodName = "CreateChildWithInfo";
            }
            else if (rpcMethod.Signature.isStatic)
            {
                methodName = "CreateStaticWithInfo";
            }
            else methodName = "CreateWithInfo";

            return methodName;
        }

        private static string GetBeginFanoutName(RPCMethod rpcMethod, bool isNetworkClass)
        {
            if (isNetworkClass)
                return "BeginFanoutChild";

            return rpcMethod.Signature.isStatic ? "BeginFanoutStatic" : "BeginFanout";
        }

        private static MethodReference MakeGenericInstance(ModuleDefinition module, MethodDefinition method, TypeReference type)
        {
            var generic = new GenericInstanceMethod(method.Import(module));
            generic.GenericArguments.Add(type);
            return generic.Import(module);
        }

        private static string GetCreateName(RPCMethod rpcMethod, bool isNetworkClass)
        {
            string methodName;

            if (isNetworkClass)
            {
                methodName = "CreateChild";
            }
            else if (rpcMethod.Signature.isStatic)
            {
                methodName = "CreateStatic";
            }
            else methodName = "Create";

            return methodName;
        }

        private static void PushNetworkManager(ModuleDefinition module, ILProcessor code, bool isNetworkClass, bool isStatic)
        {
            var managerType = module.GetTypeDefinition<NetworkManager>();
            var networkModule = module.GetTypeDefinition<NetworkModule>();
            var identityType = module.GetTypeDefinition<NetworkIdentity>();

            var networkManagerProp = identityType.GetProperty("networkManager");
            var getNetworkManager = networkManagerProp.GetMethod.Import(module);

            var networkManagerModuleProp = networkModule.GetProperty("networkManager");
            var getNetworkManagerModule = networkManagerModuleProp.GetMethod.Import(module);

            var mainManagerProp = managerType.GetProperty("main");
            var mainManagerGetter = mainManagerProp.GetMethod.Import(module);

            if (isStatic)
            {
                code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
            }
            else
            {
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(isNetworkClass
                    ? Instruction.Create(OpCodes.Call, getNetworkManagerModule)
                    : Instruction.Create(OpCodes.Call, getNetworkManager));
            }
        }

        private static MethodReference GetOriginalMethod(MethodReference originalMethod)
        {
            if (!originalMethod.DeclaringType.HasGenericParameters)
                return originalMethod;

            var declaringType = new GenericInstanceType(originalMethod.DeclaringType);

            foreach (var t in originalMethod.DeclaringType.GenericParameters)
                declaringType.GenericArguments.Add(t);

            var methodToCall = new MethodReference(
                originalMethod.Name,
                originalMethod.ReturnType,
                declaringType)
            {
                HasThis = originalMethod.HasThis,
                ExplicitThis = originalMethod.ExplicitThis,
                CallingConvention = originalMethod.CallingConvention,
            };

            foreach (var parameter in originalMethod.Parameters)
            {
                methodToCall.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes,
                    parameter.ParameterType));
            }

            foreach (var parameter in originalMethod.GenericParameters)
            {
                methodToCall.GenericParameters.Add(new GenericParameter(parameter.Name, parameter.Owner));
            }

            return methodToCall;
        }

        private static void HandleGenericRPCReceiver(ModuleDefinition module, RPCMethod rpcMethod,
            MethodDefinition newMethod,
            VariableDefinition streamVariable, ParameterDefinition info, bool isNetworkClass,
            VariableDefinition rpcPacker, VariableDefinition requestId)
        {
            var genericRpcHeaderType = module.GetTypeDefinition<GenericRPCHeader>();
            var identityType = module.GetTypeDefinition<NetworkIdentity>();
            var rpcReqRespType = module.GetTypeDefinition<RpcRequestResponseModule>();
            var managerType = module.GetTypeDefinition<NetworkManager>();
            var networkModule = module.GetTypeDefinition<NetworkModule>();

            var responderWithoutResponse = rpcReqRespType.GetMethod("CompleteRequestWithEmptyResponse").Import(module);
            var responderCoroutine = rpcReqRespType.GetMethod("CompleteRequestWithCoroutine").Import(module);
            var responderUniTaskWithoutResponse =
                rpcReqRespType.GetMethod("CompleteRequestWithUniTaskEmptyResponse").Import(module);

            var responderTask = rpcReqRespType.GetMethod("CompleteRequestWithResponseObject", true).Import(module);
            var responderUniTask = rpcReqRespType.GetMethod("CompleteRequestWithUniTaskObject", true).Import(module);

            var responderTaskObject = rpcReqRespType.GetMethod("CompleteRequestWithResponseObject", false).Import(module);
            var responderUniTaskObject = rpcReqRespType.GetMethod("CompleteRequestWithUniTaskObject", false).Import(module);

            var localPlayerProp = identityType.GetProperty("localPlayerForced");
            var localPlayerGetter = localPlayerProp.GetMethod.Import(module);

            var localPlayerPropModule = networkModule.GetProperty("localPlayerForced");
            var localPlayerGetterModule = localPlayerPropModule.GetMethod.Import(module);

            var code = newMethod.Body.GetILProcessor();
            var createGenericHeader = genericRpcHeaderType.GetMethod("CreateGenericHeader").Import(module);
            var saveReadHashMethod = genericRpcHeaderType.GetMethod("SaveReadHash").Import(module);

            var setInfo = genericRpcHeaderType.GetMethod("SetInfo").Import(module);
            var readGeneric = genericRpcHeaderType.GetMethod("Read").Import(module);
            var readT = genericRpcHeaderType.GetMethod("Read", true).Import(module);
            var readTypeMethod = genericRpcHeaderType.GetMethod("ReadType").Import(module);
            var setPlayerId = genericRpcHeaderType.GetMethod("SetPlayerId").Import(module);
            var getGenericTypeAt = genericRpcHeaderType.GetMethod("GetTypeAt").Import(module);
            var SaveReadValueNonGeneric = genericRpcHeaderType.GetMethod("SaveReadValue").Import(module);
            var SaveReadValueGeneric = genericRpcHeaderType.GetMethod("SaveReadValue", true).Import(module);

            var typeTypeRef = module.ImportReference(typeof(Type));
            var getTypeFromHandle = module.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle"));
            var makeGenericType = module.ImportReference(
                typeof(Type).GetMethod("MakeGenericType", new[] { typeof(Type[]) }));
            var makeArrayType = module.ImportReference(
                typeof(Type).GetMethod("MakeArrayType", Type.EmptyTypes));

            var mainManagerProp = managerType.GetProperty("main");
            var mainManagerGetter = mainManagerProp.GetMethod.Import(module);

            var getNetworkManagerModule = networkModule.GetProperty("networkManager").GetMethod.Import(module);
            var getNetworkManager = identityType.GetProperty("networkManager").GetMethod.Import(module);
            var RPCPacketPackerType = module.GetTypeDefinition<RPCPacketPacker>();
            var ReadObjectMethod = RPCPacketPackerType.GetMethod("ReadObject").Import(module);
            var ReadObjectGenMethod = RPCPacketPackerType.GetMethod("ReadObject", true).Import(module);

            var rpcModule = module.GetTypeDefinition<RPCModule>();
            var nclassType = module.GetTypeDefinition<NetworkModule>();

            var callGenericMethod = rpcMethod.Signature.isStatic
                ?
                rpcModule.GetMethod("CallStaticGeneric").Import(module)
                :
                isNetworkClass
                    ? nclassType.GetMethod("CallGeneric").Import(module)
                    :
                    identityType.GetMethod("CallGeneric").Import(module);

            var originalMethod = rpcMethod.originalMethod;
            int paramCount = originalMethod.Parameters.Count;
            int genericParamCount = originalMethod.GenericParameters.Count;

            bool useDeltaPacking = rpcPacker != null;

            var genericParamCountValue = new VariableDefinition(module.TypeSystem.UInt32);

            var headerValue = new VariableDefinition(genericRpcHeaderType.Import(module));
            newMethod.Body.Variables.Add(headerValue);
            newMethod.Body.Variables.Add(genericParamCountValue);

            bool isValidReturn = ValidateReturnType(originalMethod, out var returnMode);
            MethodReference serializeUint;

            serializeUint = useDeltaPacking ?
                CreateDeltaSerializer(module, module.TypeSystem.UInt32, rpcPacker, false) :
                CreateSerializer(module, module.TypeSystem.UInt32, false);

            if (!isValidReturn)
            {
                return;
            }

            // read header value
            code.Append(Instruction.Create(OpCodes.Ldloc_S, streamVariable));
            code.Append(Instruction.Create(OpCodes.Ldarg, info));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, genericParamCount));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, paramCount));
            code.Append(Instruction.Create(OpCodes.Call, createGenericHeader));
            code.Append(Instruction.Create(OpCodes.Stloc, headerValue));

            for (int i = 0; i < genericParamCount; i++)
            {
                // read uint hash
                if (useDeltaPacking)
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                code.Append(Instruction.Create(OpCodes.Ldloc_S, streamVariable));
                code.Append(Instruction.Create(OpCodes.Ldloca, genericParamCountValue));
                code.Append(Instruction.Create(OpCodes.Call, serializeUint));

                // call GenericRPCHeader.SaveReadHash(uint hash, int index)
                code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                code.Append(Instruction.Create(OpCodes.Ldloc, genericParamCountValue));
                code.Append(Instruction.Create(OpCodes.Ldc_I4, i));
                code.Append(Instruction.Create(OpCodes.Call, saveReadHashMethod));
            }

            // read generic parameters
            for (var p = 0; p < paramCount; p++)
            {
                var param = originalMethod.Parameters[p];

                if (ShouldIgnore(rpcMethod.Signature.type, param, p, paramCount, out var specialType))
                {
                    switch (specialType)
                    {
                        case SpecialParamType.RPCInfo:
                            code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                            code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                            code.Append(Instruction.Create(OpCodes.Call, setInfo));
                            break;
                        case SpecialParamType.SenderId:
                            if (GetArgType(param.ParameterType) == TargetArgType.Player)
                            {
                                code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));

                                if (!rpcMethod.Signature.isStatic)
                                {
                                    code.Append(Instruction.Create(OpCodes.Ldarg_0));
                                    code.Append(isNetworkClass
                                        ? Instruction.Create(OpCodes.Call, localPlayerGetterModule)
                                        : Instruction.Create(OpCodes.Call, localPlayerGetter));
                                }
                                else
                                {
                                    var getLocalPlayer = rpcModule.GetMethod("GetLocalPlayer").Import(module);
                                    code.Append(Instruction.Create(OpCodes.Call, getLocalPlayer));
                                }

                                code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                                code.Append(Instruction.Create(OpCodes.Call, setPlayerId));
                            }
                            break;
                    }

                    continue;
                }

                var genericIdx = param.ParameterType.IsGenericParameter
                    ? originalMethod.GenericParameters.IndexOf((GenericParameter)param.ParameterType)
                    : -1;

                if (genericIdx != -1)
                {
                    code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                    if (useDeltaPacking)
                    {
                        // OBJ in stack
                        {
                            code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                            code.Append(Instruction.Create(OpCodes.Ldloc_S, streamVariable));

                            // GetTypeAt(genericIdx)
                            code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                            code.Append(Instruction.Create(OpCodes.Ldc_I4, genericIdx));
                            code.Append(Instruction.Create(OpCodes.Call, getGenericTypeAt));

                            code.Append(Instruction.Create(OpCodes.Call, ReadObjectMethod));
                        }

                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, SaveReadValueNonGeneric));
                    }
                    else
                    {
                        code.Append(Instruction.Create(OpCodes.Ldc_I4, genericIdx));
                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, readGeneric));
                    }
                }
                else if (ContainsMethodGenericParameter(param.ParameterType, originalMethod))
                {
                    // Parameter type contains a method-level generic parameter that's not a raw `T`
                    // (e.g. GenericPair<T>, List<T>, T[]). The strongly-typed Packer<T>/Read<T> path
                    // is unusable here because the receiver IL has no method-generic in scope (`!!0`
                    // would be invalid). Build the closed Type at runtime from rpcHeader.types[] and
                    // route through the runtime-typed reader. Class-level generics (`!0`) are still
                    // valid in scope inside a generic-class handler, so they fall through to the
                    // strongly-typed branch below.
                    if (useDeltaPacking)
                    {
                        code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));

                        // OBJ on stack via RPCPacketPacker.ReadObject(BitPacker, Type)
                        code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                        code.Append(Instruction.Create(OpCodes.Ldloc_S, streamVariable));
                        EmitBuildRuntimeType(code, module, param.ParameterType, originalMethod,
                            headerValue, getGenericTypeAt, getTypeFromHandle, makeGenericType,
                            makeArrayType, typeTypeRef);
                        code.Append(Instruction.Create(OpCodes.Call, ReadObjectMethod));

                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, SaveReadValueNonGeneric));
                    }
                    else
                    {
                        code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                        EmitBuildRuntimeType(code, module, param.ParameterType, originalMethod,
                            headerValue, getGenericTypeAt, getTypeFromHandle, makeGenericType,
                            makeArrayType, typeTypeRef);
                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, readTypeMethod));
                    }
                }
                else
                {
                    if (useDeltaPacking)
                    {
                        code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));

                        // OBJ in stack
                        {
                            var readAny = new GenericInstanceMethod(ReadObjectGenMethod);
                            readAny.GenericArguments.Add(param.ParameterType);

                            code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                            code.Append(Instruction.Create(OpCodes.Ldloc_S, streamVariable));
                            code.Append(Instruction.Create(OpCodes.Call, readAny));
                        }

                        var saveAny = new GenericInstanceMethod(SaveReadValueGeneric);
                        saveAny.GenericArguments.Add(param.ParameterType);

                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, saveAny));
                    }
                    else
                    {
                        var readAny = new GenericInstanceMethod(readT);
                        readAny.GenericArguments.Add(param.ParameterType);

                        code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                        code.Append(Instruction.Create(OpCodes.Ldc_I4, p));
                        code.Append(Instruction.Create(OpCodes.Call, readAny));
                    }
                }
            }

            // call 'CallGeneric'
            code.Append(!rpcMethod.Signature.isStatic
                ? Instruction.Create(OpCodes.Ldarg_0)
                : Instruction.Create(OpCodes.Ldtoken, originalMethod.DeclaringType));

            code.Append(Instruction.Create(OpCodes.Ldstr, originalMethod.Name)); // methodName
            code.Append(Instruction.Create(OpCodes.Ldloc, headerValue)); // rpcHeader
            code.Append(Instruction.Create(OpCodes.Call, callGenericMethod)); // CallGeneric

            if (requestId != null)
            {
                if (returnMode is ReturnMode.Task or ReturnMode.UniTask &&
                    originalMethod.ReturnType is GenericInstanceType genericInstance)
                {
                    if (genericInstance.GenericArguments.Count != 1)
                    {
                        code.Append(Instruction.Create(OpCodes.Pop));
                        return;
                    }

                    code.Append(Instruction.Create(OpCodes.Ldarg, info));
                    code.Append(Instruction.Create(OpCodes.Ldloc, requestId));
                    PushNetworkManager(newMethod, isNetworkClass, code, mainManagerGetter, getNetworkManagerModule, getNetworkManager);

                    var taskType = genericInstance.GenericArguments[0];
                    bool isTaskTypeConcrete = IsConcreteType(taskType, out var concreteTaskType);

                    if (returnMode == ReturnMode.Task)
                    {
                        if (isTaskTypeConcrete)
                        {
                            var genericResponse = new GenericInstanceMethod(responderTask);
                            genericResponse.GenericArguments.Add(concreteTaskType);
                            code.Append(Instruction.Create(OpCodes.Call, genericResponse.Import(module)));
                        }
                        else code.Append(Instruction.Create(OpCodes.Call, responderTaskObject));
                    }
                    else
                    {
                        //code.Append(Instruction.Create(OpCodes.Call, responderUniTaskObject));

                        if (isTaskTypeConcrete)
                        {
                            var genericResponse = new GenericInstanceMethod(responderUniTask);
                            genericResponse.GenericArguments.Add(concreteTaskType);
                            code.Append(Instruction.Create(OpCodes.Call, genericResponse.Import(module)));
                        }
                        else code.Append(Instruction.Create(OpCodes.Call, responderUniTaskObject));
                    }
                }
                else if (originalMethod.ReturnType != module.TypeSystem.Void)
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg, info));
                    code.Append(Instruction.Create(OpCodes.Ldloc, requestId));

                    // load networkManager
                    if (newMethod.IsStatic)
                    {
                        code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
                    }
                    else
                    {
                        code.Append(Instruction.Create(OpCodes.Ldarg_0));
                        code.Append(isNetworkClass
                            ? Instruction.Create(OpCodes.Call, getNetworkManagerModule)
                            : Instruction.Create(OpCodes.Call, getNetworkManager));
                    }

                    code.Append(returnMode switch
                    {
                        ReturnMode.IEnumerator => Instruction.Create(OpCodes.Call, responderCoroutine),
                        ReturnMode.UniTask => Instruction.Create(OpCodes.Call, responderUniTaskWithoutResponse),
                        ReturnMode.Task => Instruction.Create(OpCodes.Call, responderWithoutResponse),
                        _ => throw new ArgumentOutOfRangeException()
                    });
                }
            }
            else
            {
                code.Append(Instruction.Create(OpCodes.Pop));
            }
        }

        // True when `typeRef` references any GenericParameter whose Owner is `method` (i.e. a
        // method-level generic, distinguished from class-level generics which are still in scope
        // inside the generic-class handler IL).
        private static bool ContainsMethodGenericParameter(TypeReference typeRef, MethodReference method)
        {
            if (typeRef == null) return false;
            if (typeRef is GenericParameter gp)
                return gp.Type == GenericParameterType.Method && gp.Owner == method;
            if (typeRef is GenericInstanceType git)
            {
                foreach (var ga in git.GenericArguments)
                    if (ContainsMethodGenericParameter(ga, method))
                        return true;
                return false;
            }
            if (typeRef is TypeSpecification ts)
                return ContainsMethodGenericParameter(ts.ElementType, method);
            return false;
        }

        // Emits IL that produces a `System.Type` on the evaluation stack representing the closed
        // form of `typeRef`. Method-level GenericParameters are substituted at runtime via
        // `headerValue.GetTypeAt(idx)`. Supports nested generics and arrays.
        private static void EmitBuildRuntimeType(ILProcessor code, ModuleDefinition module,
            TypeReference typeRef, MethodDefinition originalMethod, VariableDefinition headerValue,
            MethodReference getGenericTypeAt, MethodReference getTypeFromHandle,
            MethodReference makeGenericType, MethodReference makeArrayType,
            TypeReference typeTypeRef)
        {
            if (typeRef is GenericParameter gp)
            {
                if (gp.Type == GenericParameterType.Method && gp.Owner == originalMethod)
                {
                    int idx = originalMethod.GenericParameters.IndexOf(gp);
                    code.Append(Instruction.Create(OpCodes.Ldloca, headerValue));
                    code.Append(Instruction.Create(OpCodes.Ldc_I4, idx));
                    code.Append(Instruction.Create(OpCodes.Call, getGenericTypeAt));
                    return;
                }
                // Class-level generic — `!0` is in scope inside a generic-class handler.
                code.Append(Instruction.Create(OpCodes.Ldtoken, gp));
                code.Append(Instruction.Create(OpCodes.Call, getTypeFromHandle));
                return;
            }

            if (typeRef is ArrayType arr)
            {
                EmitBuildRuntimeType(code, module, arr.ElementType, originalMethod, headerValue,
                    getGenericTypeAt, getTypeFromHandle, makeGenericType, makeArrayType, typeTypeRef);
                code.Append(Instruction.Create(OpCodes.Callvirt, makeArrayType));
                return;
            }

            if (typeRef is GenericInstanceType git)
            {
                var openDef = module.ImportReference(git.ElementType);
                code.Append(Instruction.Create(OpCodes.Ldtoken, openDef));
                code.Append(Instruction.Create(OpCodes.Call, getTypeFromHandle));

                code.Append(Instruction.Create(OpCodes.Ldc_I4, git.GenericArguments.Count));
                code.Append(Instruction.Create(OpCodes.Newarr, typeTypeRef));

                for (int i = 0; i < git.GenericArguments.Count; i++)
                {
                    code.Append(Instruction.Create(OpCodes.Dup));
                    code.Append(Instruction.Create(OpCodes.Ldc_I4, i));
                    EmitBuildRuntimeType(code, module, git.GenericArguments[i], originalMethod,
                        headerValue, getGenericTypeAt, getTypeFromHandle, makeGenericType,
                        makeArrayType, typeTypeRef);
                    code.Append(Instruction.Create(OpCodes.Stelem_Ref));
                }

                code.Append(Instruction.Create(OpCodes.Callvirt, makeGenericType));
                return;
            }

            code.Append(Instruction.Create(OpCodes.Ldtoken, module.ImportReference(typeRef)));
            code.Append(Instruction.Create(OpCodes.Call, getTypeFromHandle));
        }

        private static void PushNetworkManager(MethodDefinition newMethod, bool isNetworkClass, ILProcessor code,
            MethodReference mainManagerGetter, MethodReference getNetworkManagerModule, MethodReference getNetworkManager)
        {
            // load networkManager
            if (newMethod.IsStatic)
            {
                code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
            }
            else
            {
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(isNetworkClass
                    ? Instruction.Create(OpCodes.Call, getNetworkManagerModule)
                    : Instruction.Create(OpCodes.Call, getNetworkManager));
            }
        }

        public enum ReturnMode
        {
            Void,
            Task,
            UniTask,
            IEnumerator
        }

        private static bool IsGeneric(TypeReference typeRef, Type type)
        {
            // Ensure method has a generic return type
            if (typeRef is GenericInstanceType genericReturnType)
            {
                // Resolve the element type to compare against Task<>
                var resolvedType = genericReturnType.ElementType.Resolve();

                // Check if the resolved type matches Task<>
                return resolvedType != null && resolvedType.FullName == type.FullName;
            }

            return false;
        }

        private static bool IsGeneric(MethodReference method, Type type)
        {
            // Ensure method has a generic return type
            if (method.ReturnType is GenericInstanceType genericReturnType)
            {
                // Resolve the element type to compare against Task<>
                var resolvedType = genericReturnType.ElementType.Resolve();

                // Check if the resolved type matches Task<>
                return resolvedType != null && resolvedType.FullName == type.FullName;
            }

            return false;
        }

        static bool ValidateReturnType(MethodDefinition method, out ReturnMode mode)
        {
            mode = ReturnMode.Void;

            if (method.ReturnType.FullName == typeof(void).FullName)
                return true;

            bool isIEnumerator = method.ReturnType.FullName == typeof(IEnumerator).FullName;

            if (isIEnumerator)
            {
                mode = ReturnMode.IEnumerator;
                return true;
            }

            bool isTask = method.ReturnType.FullName == typeof(Task).FullName;

            if (isTask)
            {
                mode = ReturnMode.Task;
                return true;
            }

            if (IsGeneric(method, typeof(Task<>)))
            {
                mode = ReturnMode.Task;
                return true;
            }

#if UNITASK_PURRNET_SUPPORT
            bool isUniTask = method.ReturnType.FullName == typeof(UniTask).FullName;

            if (isUniTask)
            {
                mode = ReturnMode.UniTask;
                return true;
            }

            if (IsGeneric(method, typeof(UniTask<>)))
            {
                mode = ReturnMode.UniTask;
                return true;
            }
#endif
            return false;
        }

        static bool IsTaskOrInheritsFromTask(TypeReference type)
        {
            if (type.FullName == typeof(Task).FullName)
                return true;

            return type.Resolve().BaseType?.FullName == typeof(Task).FullName;
        }

        static bool IsAwaitableGenericWrapper(TypeReference type)
        {
            if (IsTaskOrInheritsFromTask(type))
                return true;

#if UNITASK_PURRNET_SUPPORT
            var resolved = type.Resolve();
            if (resolved is { FullName: "Cysharp.Threading.Tasks.UniTask`1" })
                return true;
#endif

            return false;
        }

        public static bool IsConcreteType(TypeReference type, out TypeReference concreteType)
        {
            try
            {
                concreteType = type;

                if (type.ContainsGenericParameter)
                    return false;

                if (type is GenericInstanceType genericInstanceType && IsAwaitableGenericWrapper(type))
                    concreteType = genericInstanceType.GenericArguments[0];

                return true;
            }
            catch
            {
                concreteType = type;
                return false;
            }
        }

        static StripCodeMode GetMode(PurrNetSettings settings, StripCodeModeOverride overrideMode)
        {
            switch (overrideMode)
            {
                case StripCodeModeOverride.DoNotStrip:
                    return StripCodeMode.DoNotStrip;
                case StripCodeModeOverride.StripAll:
                    return StripCodeMode.StripAll;
                case StripCodeModeOverride.ReplaceWithEmptyMethod:
                    return StripCodeMode.ReplaceWithEmptyMethod;
                case StripCodeModeOverride.ReplaceWithLogWarning:
                    return StripCodeMode.ReplaceWithLogWarning;
                case StripCodeModeOverride.ReplaceWithLogError:
                    return StripCodeMode.ReplaceWithLogError;
                case StripCodeModeOverride.ThrowNotSupportedException:
                    return StripCodeMode.ThrowNotSupportedException;
                case StripCodeModeOverride.Settings:
                default:
                    return settings.stripCodeMode;
            }
        }

        static GuardFailureAction GetMode(PurrNetSettings settings, GuardFailureActionOverride overrideAction)
        {
            switch (overrideAction)
            {
                case GuardFailureActionOverride.ReturnDefault:
                    return GuardFailureAction.ReturnDefault;
                case GuardFailureActionOverride.ThrowException:
                    return GuardFailureAction.ThrowException;
                case GuardFailureActionOverride.LogWarning:
                    return GuardFailureAction.LogWarning;
                case GuardFailureActionOverride.LogError:
                    return GuardFailureAction.LogError;
                case GuardFailureActionOverride.Ignore:
                    return GuardFailureAction.Ignore;
                case GuardFailureActionOverride.Settings:
                default:
                    return settings.guardFailureAction;
            }
        }

        private static void StripBody(MethodDefinition method, string methodName, PurrNetSettings settings, StripCodeModeOverride overrideMode)
        {
            var mode = GetMode(settings, overrideMode);

            if (mode == StripCodeMode.DoNotStrip)
                return;

            var il = method.Body.GetILProcessor();
            method.Body.ExceptionHandlers.Clear();
            il.Clear();

            AppendStripAction(method, methodName, mode, il, "stripped");
        }

        /// <summary>
        /// Widens every short branch in a body we modified. Cecil writes a short branch operand as a truncated
        /// sbyte and never widens it, and Instruction.Offset is stale until the next write, so measuring the
        /// distance here is unreliable once instructions have been inserted. Costs 3 bytes per branch.
        /// </summary>
        static void FixShortFormJumps(MethodDefinition method)
        {
            var instructions = method.Body.Instructions;

            for (var i = 0; i < instructions.Count; i++)
            {
                var inst = instructions[i];

                if (inst.OpCode.OperandType != OperandType.ShortInlineBrTarget)
                    continue;

                inst.OpCode = inst.OpCode.Code switch
                {
                    Code.Br_S => OpCodes.Br,
                    Code.Brfalse_S => OpCodes.Brfalse,
                    Code.Brtrue_S => OpCodes.Brtrue,
                    Code.Beq_S => OpCodes.Beq,
                    Code.Bne_Un_S => OpCodes.Bne_Un,
                    Code.Bge_S => OpCodes.Bge,
                    Code.Bge_Un_S => OpCodes.Bge_Un,
                    Code.Bgt_S => OpCodes.Bgt,
                    Code.Bgt_Un_S => OpCodes.Bgt_Un,
                    Code.Ble_S => OpCodes.Ble,
                    Code.Ble_Un_S => OpCodes.Ble_Un,
                    Code.Blt_S => OpCodes.Blt,
                    Code.Blt_Un_S => OpCodes.Blt_Un,
                    Code.Leave_S => OpCodes.Leave,
                    _ => inst.OpCode
                };
            }
        }

        private static void AppendStripAction(MethodDefinition method, string methodName, StripCodeMode mode, ILProcessor il, string action)
        {
            switch (mode)
            {
                case StripCodeMode.StripAll:
                    method.DeclaringType.Methods.Remove(method);
                    break;

                case StripCodeMode.ReplaceWithLogWarning:
                    var logWarningMethod = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("LogSimplerWarning", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{methodName}' is {action} and cannot be called."));
                    il.Append(il.Create(OpCodes.Call, logWarningMethod));
                    break;

                case StripCodeMode.ReplaceWithLogError:
                    var logErrorMethod = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("LogSimplerError", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{methodName}' is {action} and cannot be called."));
                    il.Append(il.Create(OpCodes.Call, logErrorMethod));
                    break;

                case StripCodeMode.ReplaceWithEmptyMethod:
                    break;

                case StripCodeMode.ThrowNotSupportedException:
                    var throwExcep = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("ThrowUnsupportedException", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{methodName}' is {action} and cannot be called."));
                    il.Append(il.Create(OpCodes.Call, throwExcep));
                    break;
                case StripCodeMode.DoNotStrip:
                default:
                    throw new ArgumentOutOfRangeException();
            }

            ProperlyEndMethod(method, il);
        }

        private static void ProperlyEndMethod(MethodDefinition method, ILProcessor il)
        {
            var returnType = method.ReturnType;
            if (returnType.MetadataType != MetadataType.Void)
                EmitDefaultValue(method, il, returnType);
            il.Append(il.Create(OpCodes.Ret));
        }

        private static void EmitDefaultValue(MethodDefinition method, ILProcessor il, TypeReference type)
        {
            switch (type.MetadataType)
            {
                case MetadataType.Boolean:
                case MetadataType.Byte:
                case MetadataType.SByte:
                case MetadataType.Int16:
                case MetadataType.UInt16:
                case MetadataType.Int32:
                case MetadataType.UInt32:
                case MetadataType.Int64:
                case MetadataType.UInt64:
                case MetadataType.IntPtr:
                case MetadataType.UIntPtr:
                case MetadataType.Char:
                    il.Append(il.Create(OpCodes.Ldc_I4_0));
                    break;
                case MetadataType.Single:
                    il.Append(il.Create(OpCodes.Ldc_R4, 0f));
                    break;
                case MetadataType.Double:
                    il.Append(il.Create(OpCodes.Ldc_R8, 0d));
                    break;
                case MetadataType.String:
                case MetadataType.Class:
                case MetadataType.Object:
                case MetadataType.Array:
                case MetadataType.ByReference:
                    il.Append(il.Create(OpCodes.Ldnull));
                    break;
                case MetadataType.GenericInstance:
                case MetadataType.ValueType:
                {
                    // For structs, emit a local, initobj, ldloc
                    var varDef = new VariableDefinition(type);
                    method.Body.Variables.Add(varDef);
                    il.Append(il.Create(OpCodes.Ldloca_S, varDef));
                    il.Append(il.Create(OpCodes.Initobj, type));
                    il.Append(il.Create(OpCodes.Ldloc, varDef));
                    break;
                }
                case MetadataType.Void:
                case MetadataType.Pointer:
                case MetadataType.Var:
                case MetadataType.TypedByReference:
                case MetadataType.FunctionPointer:
                case MetadataType.MVar:
                case MetadataType.RequiredModifier:
                case MetadataType.OptionalModifier:
                case MetadataType.Sentinel:
                case MetadataType.Pinned:
                default:
                    il.Append(il.Create(OpCodes.Ldnull));
                    break;
            }
        }

        private MethodDefinition HandleRPC(ModuleDefinition module, int id, RPCMethod methodRpc, bool isNetworkClass, bool isServerBuild, PurrNetSettings settings,
            HashSet<TypeReference> usedTypes, [UsedImplicitly] List<DiagnosticMessage> messages)
        {
            var method = methodRpc.originalMethod;

            if (method.DeclaringType == null)
                return null;

            bool isValidReturn = ValidateReturnType(method, out var returnMode);

            if (!isValidReturn)
            {
                Error(messages,
                    method.ReturnType.Name.Contains("UniTask")
                        ? $"RPC '{method.Name}' uses <b>UniTask</b>, you need to enable support under `Tools/PurrNet/Packages/Install UniTask` or define the `UNITASK_PURRNET_SUPPORT` symbol."
                        : $"RPC '{method.Name}' RPC must return <b>void</b>, <b>Task</b> or <b>UniTask</b>",
                    method);
                return null;
            }

            if (returnMode == ReturnMode.IEnumerator && methodRpc.Signature.type == RPCType.ObserversRPC)
            {
                Error(messages, $"ObserversRPC '{method.Name}' method cannot return IEnumerator", method);
                return null;
            }

            var hasAsyncPackableParams = HasAsyncPackableParam(methodRpc);
            if (returnMode == ReturnMode.Task && methodRpc.Signature.type == RPCType.ObserversRPC && !hasAsyncPackableParams)
            {
                Error(messages, $"ObserversRPC '{method.Name}' method cannot return Task", method);
                return null;
            }

            // Don't upgrade void->Task for async packable: we fire-and-forget, caller gets void.
            // Upgrading would break compiled call sites and require returning a Task we never produce.

            if (IsConcreteType(method.ReturnType, out var concreteType))
                usedTypes.Add(concreteType);

            string ogName = method.Name;
            method.Name = ogName + "_Original_" + id;

            var attributes = MethodAttributes.Public | MethodAttributes.HideBySig;

            if (methodRpc.Signature.isStatic)
                attributes |= MethodAttributes.Static;

            if (method.IsVirtual)
                attributes |= MethodAttributes.Virtual;

            // Keep original return type for async packable: we fire-and-forget internally.
            // Changing void->Task would break callers compiled against the original signature.
            var effectiveReturnType = method.ReturnType;
            var newMethod = new MethodDefinition(ogName, attributes, effectiveReturnType);

            foreach (var t in method.GenericParameters)
                newMethod.GenericParameters.Add(new GenericParameter(t.Name, newMethod));

            foreach (var param in method.CustomAttributes)
                newMethod.CustomAttributes.Add(param);

            if (method.HasGenericParameters)
                method.IsPublic = true;

            // add preserve attribute to newMethod
            var preserveAttribute = module.GetTypeDefinition<PreserveAttribute>();
            var constructor = preserveAttribute.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters)
                .Import(module);
            newMethod.CustomAttributes.Add(new CustomAttribute(constructor));

            newMethod.CallingConvention = method.CallingConvention;
            method.CustomAttributes.Clear();

            foreach (var param in method.Parameters)
            {
                if (IsConcreteType(param.ParameterType, out var concreteParam))
                    usedTypes.Add(concreteParam);

                var p = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType);
                newMethod.Parameters.Add(p);

                foreach (var t in param.CustomAttributes)
                    p.CustomAttributes.Add(t);

                p.HasDefault = param.HasDefault;
                p.IsLcid = param.IsLcid;
                if (p.HasDefault)
                    p.Constant = param.Constant;
                p.IsIn = param.IsIn;
                p.IsOptional = param.IsOptional;
                p.IsOut = param.IsOut;
                p.IsReturnValue = param.IsReturnValue;
                p.HasFieldMarshal = param.HasFieldMarshal;
                p.MarshalInfo = param.MarshalInfo;
            }

            var code = newMethod.Body.GetILProcessor();

            var streamType = module.GetTypeDefinition<BitPacker>();
            var rpcType = module.GetTypeDefinition<RPCModule>();
            var identityType = module.GetTypeDefinition<NetworkIdentity>();
            var moduleType = module.GetTypeDefinition<NetworkModule>();
            var hahserType = module.GetTypeDefinition<Hasher>();
            var rpcRequestType = module.GetTypeDefinition<RpcRequest>();
            var rpcSignatureType = module.GetTypeDefinition<RPCSignature>();
            var reqRespModule = module.GetTypeDefinition<RpcRequestResponseModule>();
            var packerType = module.GetTypeDefinition(typeof(Packer)).Import(module);
            var RPCPacketPackerType = module.GetTypeDefinition<RPCPacketPacker>();

            var allocStreamMethod = rpcType.GetMethod("AllocStream").Import(module);
            var freeStreamMethod = rpcType.GetMethod("FreeStream").Import(module);

            var getId = identityType.GetProperty("id");
            var getSceneId = identityType.GetProperty("sceneId");
            var getStableHashU32 = hahserType.GetMethod("GetStableHashU32", true).Import(module);
            var getParent = moduleType.GetProperty("parent").GetMethod.Import(module);
            var targetPlayerField = rpcSignatureType.GetField("targetPlayer").Import(module);

            var getNextId = identityType.GetMethod("GetNextId", true).Import(module);
            var getNextIdNonGeneric = identityType.GetMethod("GetNextId").Import(module);
            var getNextIdStatic = reqRespModule.GetMethod("GetNextIdStatic", true).Import(module);
            var getNextIdStaticNonGeneric = reqRespModule.GetMethod("GetNextIdStatic").Import(module);
            var getNextIdUniTaskNonGeneric = identityType.GetMethod("GetNextIdUniTask").Import(module);
            var getNextIdUniTask = identityType.GetMethod("GetNextIdUniTask", true).Import(module);

            var getNextIdUniTaskStatic = reqRespModule.GetMethod("GetNextIdUniTaskStatic", true).Import(module);
            var getNextIdUniTaskStaticNonGeneric = reqRespModule.GetMethod("GetNextIdUniTaskStatic").Import(module);

            var waitForTask = reqRespModule.GetMethod("WaitForTask").Import(module);

            var reqIdField = rpcRequestType.GetField("id").Import(module);

            // Declare local variables
            newMethod.Body.InitLocals = true;

            var packetType = methodRpc.Signature.isStatic ? module.GetTypeDefinition<StaticRPCPacket>() :
                isNetworkClass ? module.GetTypeDefinition<ChildRPCPacket>() : module.GetTypeDefinition<RPCPacket>();

            var streamVariable = new VariableDefinition(streamType.Import(module));
            var rpcDataVariable = new VariableDefinition(packetType.Import(module));
            var typeHash = new VariableDefinition(module.TypeSystem.UInt32);
            var rpcRequest = new VariableDefinition(rpcRequestType.Import(module));
            var taskWithReturnType = new VariableDefinition(newMethod.ReturnType);
            var rpcSignature = new VariableDefinition(rpcSignatureType.Import(module));

            if (returnMode != ReturnMode.Void)
            {
                newMethod.Body.Variables.Add(taskWithReturnType);
                newMethod.Body.Variables.Add(rpcRequest);
            }

            newMethod.Body.Variables.Add(streamVariable);
            newMethod.Body.Variables.Add(rpcDataVariable);

            if (newMethod.GenericParameters.Count > 0)
                newMethod.Body.Variables.Add(typeHash);
            newMethod.Body.Variables.Add(rpcSignature);

            var paramCount = newMethod.Parameters.Count;

            var endOfRunLocallyCheck = Instruction.Create(OpCodes.Nop);
            var executeRunLocally = Instruction.Create(OpCodes.Nop);

            PushRPCSignature(module, code, methodRpc, false, isNetworkClass);
            code.Append(Instruction.Create(OpCodes.Stloc, rpcSignature));

            var playerType = module.GetTypeDefinition<PlayerID>().Import(module);
            var disposableListType = module.GetTypeDefinition(typeof(DisposableList<>)).Import(module);

            var playersListType = new GenericInstanceType(disposableListType);
            playersListType.GenericArguments.Add(playerType);

            var playersList = new VariableDefinition(playersListType.Import(module));
            var iterator = new VariableDefinition(module.TypeSystem.Int32);
            VariableDefinition currentDeltaPlayerTarget = null;
            VariableDefinition rpcPacker = null;

            Instruction playersLoopStart = null;
            Instruction playersLoopEnd = null;

            bool hasMultipleTargets = methodRpc.Signature.type == RPCType.ObserversRPC ||
                                      methodRpc.Signature.type == RPCType.TargetRPC;

            bool useDeltaPacking = methodRpc.Signature.deltaPacked && methodRpc.Signature.type != RPCType.ServerRPC;
            bool useFanoutGrouping = useDeltaPacking && hasMultipleTargets && returnMode == ReturnMode.Void;
            VariableDefinition fanoutGrouper = null;

            if (useDeltaPacking)
            {
                rpcPacker = new VariableDefinition(RPCPacketPackerType.Import(module));
                currentDeltaPlayerTarget = new VariableDefinition(playerType.Import(module));

                var rpcSig_getTargets = rpcSignatureType.GetMethod("GetTargets").Import(module);

                newMethod.Body.Variables.Add(playersList);
                newMethod.Body.Variables.Add(currentDeltaPlayerTarget);
                newMethod.Body.Variables.Add(iterator);
                newMethod.Body.Variables.Add(rpcPacker);

                if (hasMultipleTargets)
                {
                    playersLoopEnd = Instruction.Create(OpCodes.Nop);

                    // playersList = rpcSignature.GetTargets() or GetObservers(signature);
                    if (methodRpc.Signature.type == RPCType.ObserversRPC)
                    {
                        var parentType = GetParentType(module, isNetworkClass, methodRpc.Signature.isStatic);
                        var getObservers = parentType.GetMethod("GetObservers").Import(module);
                        // signature
                        if (!methodRpc.Signature.isStatic)
                            code.Append(Instruction.Create(OpCodes.Ldarg_0));
                        code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
                        code.Append(Instruction.Create(OpCodes.Call, getObservers));
                    }
                    else
                    {
                        code.Append(Instruction.Create(OpCodes.Ldloca, rpcSignature));
                        code.Append(Instruction.Create(OpCodes.Call, rpcSig_getTargets));
                    }

                    code.Append(Instruction.Create(OpCodes.Stloc, playersList));

                    if (useFanoutGrouping)
                    {
                        var grouperType = module.GetTypeDefinition<DeltaFanoutGrouper>();
                        fanoutGrouper = new VariableDefinition(grouperType.Import(module));
                        newMethod.Body.Variables.Add(fanoutGrouper);

                        // The packet header is what keys the delta history; build it once for the
                        // key pass, the per-group loop rebuilds it with a real stream.
                        code.Append(Instruction.Create(OpCodes.Ldc_I4, 0));
                        code.Append(Instruction.Create(OpCodes.Call, allocStreamMethod));
                        code.Append(Instruction.Create(OpCodes.Stloc, streamVariable));
                        CreateAndSetPacket(module, id, methodRpc, isNetworkClass, rpcType, method, code, streamVariable, getId, getSceneId, rpcDataVariable);

                        // grouper = RPCPacketPacker.BeginFanout*(manager, packet, signature, players);
                        var beginFanout = RPCPacketPackerType.GetMethod(GetBeginFanoutName(methodRpc, isNetworkClass)).Import(module);
                        PushNetworkManager(module, code, isNetworkClass, methodRpc.Signature.isStatic);
                        code.Append(Instruction.Create(OpCodes.Ldloc, rpcDataVariable));
                        code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
                        code.Append(Instruction.Create(OpCodes.Ldloc, playersList));
                        code.Append(Instruction.Create(OpCodes.Call, beginFanout));
                        code.Append(Instruction.Create(OpCodes.Stloc, fanoutGrouper));

                        // Same key order as the serialization below: generic type hashes, then params.
                        var keyMethod = grouperType.GetMethod("Key", true);

                        for (var g = 0; g < newMethod.GenericParameters.Count; g++)
                        {
                            var getStableHashU32Generic = new GenericInstanceMethod(getStableHashU32);
                            getStableHashU32Generic.GenericArguments.Add(newMethod.GenericParameters[g]);

                            code.Append(Instruction.Create(OpCodes.Ldloc, fanoutGrouper));
                            code.Append(Instruction.Create(OpCodes.Call, getStableHashU32Generic));
                            code.Append(Instruction.Create(OpCodes.Call, MakeGenericInstance(module, keyMethod, module.TypeSystem.UInt32)));
                        }

                        for (var p = 0; p < paramCount; p++)
                        {
                            var param = newMethod.Parameters[p];

                            if (methodRpc.Signature.type == RPCType.TargetRPC && p == 0)
                                continue;

                            if (ShouldIgnore(methodRpc.Signature.type, param, p, paramCount, out _))
                                continue;

                            code.Append(Instruction.Create(OpCodes.Ldloc, fanoutGrouper));
                            code.Append(Instruction.Create(OpCodes.Ldarg, param));
                            code.Append(Instruction.Create(OpCodes.Call, MakeGenericInstance(module, keyMethod, param.ParameterType)));
                        }

                        code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
                        code.Append(Instruction.Create(OpCodes.Call, freeStreamMethod));

                        // playersList.Dispose(); playersList = grouper.BuildRepresentatives();
                        var disposePlayers = playersListType.GetMethodRef("Dispose");
                        code.Append(Instruction.Create(OpCodes.Ldloca, playersList));
                        code.Append(Instruction.Create(OpCodes.Call, disposePlayers.Import(module)));
                        code.Append(Instruction.Create(OpCodes.Ldloc, fanoutGrouper));
                        code.Append(Instruction.Create(OpCodes.Call, grouperType.GetMethod("BuildRepresentatives").Import(module)));
                        code.Append(Instruction.Create(OpCodes.Stloc, playersList));
                    }

                    // i = targets.Count;
                    code.Append(Instruction.Create(OpCodes.Ldloca, playersList));
                    var prop = disposableListType.GetProperty("Count");
                    var methodDef = prop.GetMethod;
                    var methodRef = new MethodReference(methodDef.Name, methodDef.ReturnType, playersListType)
                    {
                        HasThis = true
                    };

                    code.Append(Instruction.Create(OpCodes.Call, methodRef.Import(module)));
                    code.Append(Instruction.Create(OpCodes.Ldc_I4_1));
                    code.Append(Instruction.Create(OpCodes.Sub));
                    code.Append(Instruction.Create(OpCodes.Stloc, iterator));

                    // while (i >= 0)
                    playersLoopStart = Instruction.Create(OpCodes.Ldloc, iterator);
                    code.Append(playersLoopStart);
                    code.Append(Instruction.Create(OpCodes.Ldc_I4_0));
                    code.Append(Instruction.Create(OpCodes.Blt, playersLoopEnd));
                    // loop content

                    var rpcModule = module.GetTypeDefinition<RPCModule>();
                    var getAtConcrete = playersListType.GetMethodRef("GetAt");

                    if (useFanoutGrouping)
                    {
                        // currentTarget = players.GetAt(i); RPCModule.ModifyManyToGroup(ref signature, grouper, i);
                        var modifyManyToGroup = rpcModule.GetMethod("ModifyManyToGroup").Import(module);
                        code.Append(Instruction.Create(OpCodes.Ldloca, playersList));
                        code.Append(Instruction.Create(OpCodes.Ldloc, iterator));
                        code.Append(Instruction.Create(OpCodes.Call, getAtConcrete.Import(module)));
                        code.Append(Instruction.Create(OpCodes.Stloc, currentDeltaPlayerTarget));
                        code.Append(Instruction.Create(OpCodes.Ldloca, rpcSignature));
                        code.Append(Instruction.Create(OpCodes.Ldloc, fanoutGrouper));
                        code.Append(Instruction.Create(OpCodes.Ldloc, iterator));
                        code.Append(Instruction.Create(OpCodes.Call, modifyManyToGroup));
                    }
                    else
                    {
                        // this.ModifyManyToOne(ref signature, players.GetAt(i));
                        var modifyManyToOne = rpcModule.GetMethod("ModifyManyToOne").Import(module);
                        // ref signature
                        code.Append(Instruction.Create(OpCodes.Ldloca, rpcSignature));
                        // players.GetAt(i)
                        code.Append(Instruction.Create(OpCodes.Ldloca, playersList));
                        code.Append(Instruction.Create(OpCodes.Ldloc, iterator));
                        code.Append(Instruction.Create(OpCodes.Call, getAtConcrete.Import(module)));
                        code.Append(Instruction.Create(OpCodes.Dup));
                        code.Append(Instruction.Create(OpCodes.Stloc, currentDeltaPlayerTarget));
                        // ();
                        code.Append(Instruction.Create(OpCodes.Call, modifyManyToOne));
                    }
                }
            }

            switch (methodRpc.Signature.type)
            {
                case RPCType.ServerRPC:
                    // Host shortcut: when isServer, call the original directly. We must not
                    // take this shortcut when IAsyncPackable params are present — it bypasses
                    // PrepareForPackAsync/PrepareAfterUnpackAsync, leaving the params in their
                    // pre-prepare state. Routing through the network loopback (BatchToServer)
                    // makes the host behave like a real client and runs the full lifecycle.
                    if (!hasAsyncPackableParams)
                    {
                        PutIsServerOnStack(module, methodRpc, isNetworkClass, code, moduleType, identityType);
                        code.Append(Instruction.Create(OpCodes.Brtrue, executeRunLocally));
                    }
                    break;
            }

            if (returnMode != ReturnMode.Void)
            {
                // Task<bool> nextId = GetNextId<bool>(base.networkManager.localClientConnection, 5f, out request);

                if (methodRpc.Signature.isStatic)
                {
                    var getNextIdRef = returnMode == ReturnMode.UniTask
                        ? getNextIdUniTaskStaticNonGeneric
                        : getNextIdStaticNonGeneric;

                    if (returnMode is ReturnMode.Task or ReturnMode.UniTask &&
                        newMethod.ReturnType is GenericInstanceType genericInstance)
                    {
                        if (genericInstance.GenericArguments.Count != 1)
                        {
                            Error(messages, "Task must have a single generic argument", method);
                            return null;
                        }

                        var param = genericInstance.GenericArguments[0];

                        if (returnMode == ReturnMode.Task)
                        {
                            var newGetNextId = new GenericInstanceMethod(getNextIdStatic);
                            newGetNextId.GenericArguments.Add(param);
                            getNextIdRef = newGetNextId;
                        }
                        else
                        {
                            var newGetNextId = new GenericInstanceMethod(getNextIdUniTaskStatic);
                            newGetNextId.GenericArguments.Add(param);
                            getNextIdRef = newGetNextId;
                        }
                    }

                    // get targetPlayerField
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcSignature));
                    code.Append(Instruction.Create(OpCodes.Ldfld, targetPlayerField));

                    code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)methodRpc.Signature.type));
                    code.Append(Instruction.Create(OpCodes.Ldc_R4, methodRpc.Signature.asyncTimeoutInSec)); // timeout
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcRequest)); // out request
                    code.Append(Instruction.Create(OpCodes.Call, getNextIdRef)); // GetNextIdStatic
                }
                else
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this

                    if (isNetworkClass)
                        code.Append(Instruction.Create(OpCodes.Call, getParent)); // parent

                    // this
                    code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)methodRpc.Signature.type));

                    // get targetPlayerField
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcSignature));
                    code.Append(Instruction.Create(OpCodes.Ldfld, targetPlayerField));

                    // localClientConnection
                    code.Append(Instruction.Create(OpCodes.Ldc_R4, methodRpc.Signature.asyncTimeoutInSec)); // timeout
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcRequest)); // out request

                    var getNextIdRef = returnMode == ReturnMode.UniTask
                        ? getNextIdUniTaskNonGeneric
                        : getNextIdNonGeneric;

                    if (returnMode is ReturnMode.Task or ReturnMode.UniTask &&
                        newMethod.ReturnType is GenericInstanceType genericInstance)
                    {
                        if (genericInstance.GenericArguments.Count != 1)
                        {
                            Error(messages, "Task must have a single generic argument", method);
                            return null;
                        }

                        var param = genericInstance.GenericArguments[0];

                        if (returnMode == ReturnMode.Task)
                        {
                            var newGetNextId = new GenericInstanceMethod(getNextId);
                            newGetNextId.GenericArguments.Add(param);
                            getNextIdRef = newGetNextId;
                        }
                        else
                        {
                            var newGetNextId = new GenericInstanceMethod(getNextIdUniTask);
                            newGetNextId.GenericArguments.Add(param);
                            getNextIdRef = newGetNextId;
                        }
                    }

                    code.Append(Instruction.Create(OpCodes.Call, getNextIdRef)); // GetNextId
                }

                if (returnMode == ReturnMode.IEnumerator)
                    code.Append(Instruction.Create(OpCodes.Call, waitForTask));

                code.Append(Instruction.Create(OpCodes.Stloc, taskWithReturnType)); // taskWithReturnType
            }

            code.Append(Instruction.Create(OpCodes.Ldc_I4, 0));
            code.Append(Instruction.Create(OpCodes.Call, allocStreamMethod));
            code.Append(Instruction.Create(OpCodes.Stloc, streamVariable));

            CreateAndSetPacket(module, id, methodRpc, isNetworkClass, rpcType, method, code, streamVariable, getId, getSceneId, rpcDataVariable);

            // create delta packer
            if (useDeltaPacking)
            {
                var createPackerForRPC = RPCPacketPackerType.GetMethod(GetCreateName(methodRpc, isNetworkClass))
                    .Import(module);
                //NetworkManager manager, RPCPacket context, RPCSignature signature, PlayerID target
                PushNetworkManager(module, code, isNetworkClass, methodRpc.Signature.isStatic);
                code.Append(Instruction.Create(OpCodes.Ldloc, rpcDataVariable));
                code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
                code.Append(Instruction.Create(OpCodes.Ldloc, currentDeltaPlayerTarget));
                code.Append(Instruction.Create(OpCodes.Call, createPackerForRPC));
                code.Append(Instruction.Create(OpCodes.Stloc, rpcPacker));
            }

            if (returnMode != ReturnMode.Void)
            {
                MethodReference serializeGenericMethod;

                if (useDeltaPacking)
                {
                    serializeGenericMethod = CreateDeltaSerializer(module, module.TypeSystem.UInt32, rpcPacker, true);
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                }
                else serializeGenericMethod = CreateSerializer(module, module.TypeSystem.UInt32, true);

                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
                code.Append(Instruction.Create(OpCodes.Ldloc, rpcRequest));
                code.Append(Instruction.Create(OpCodes.Ldfld, reqIdField));
                code.Append(Instruction.Create(OpCodes.Call, serializeGenericMethod));
            }

            for (var i = 0; i < newMethod.GenericParameters.Count; i++)
            {
                var param = newMethod.GenericParameters[i];
                var getStableHashU32Generic = new GenericInstanceMethod(getStableHashU32);
                getStableHashU32Generic.GenericArguments.Add(param);

                code.Append(Instruction.Create(OpCodes.Call, getStableHashU32Generic));
                code.Append(Instruction.Create(OpCodes.Stloc, typeHash));

                MethodReference serializeGenericMethod;

                if (useDeltaPacking)
                {
                    serializeGenericMethod = CreateDeltaSerializer(module, module.TypeSystem.UInt32, rpcPacker, true);
                    code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                }
                else serializeGenericMethod = CreateSerializer(module, module.TypeSystem.UInt32, true);

                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
                code.Append(Instruction.Create(OpCodes.Ldloc, typeHash));
                code.Append(Instruction.Create(OpCodes.Call, serializeGenericMethod));
            }

            var useAsyncPreparePath = hasAsyncPackableParams && !useDeltaPacking && returnMode != ReturnMode.IEnumerator;
            var skipSyncPathLabel = Instruction.Create(OpCodes.Nop);

            if (useAsyncPreparePath)
            {
                GenerateAsyncPrepareAndSendPath(module, methodRpc, id, isNetworkClass, newMethod, code, paramCount,
                    streamVariable, rpcDataVariable, rpcSignature, packetType, rpcType, identityType, allocStreamMethod,
                    freeStreamMethod, streamType);
                code.Append(Instruction.Create(OpCodes.Br, skipSyncPathLabel));
            }

            {
                for (var i = 0; i < paramCount; i++)
                {
                    var param = newMethod.Parameters[i];

                    if (methodRpc.Signature.type == RPCType.TargetRPC && i == 0)
                    {
                        var argType = GetArgType(param.ParameterType);
                        if (argType == TargetArgType.None)
                        {
                            Error(messages, "TargetRPC method must have a 'PlayerID' as the first parameter", method);
                            return null;
                        }

                        continue;
                    }

                    if (ShouldIgnore(methodRpc.Signature.type, param, i, paramCount, out _))
                        continue;

                    var paramLocal = new VariableDefinition(param.ParameterType);
                    newMethod.Body.Variables.Add(paramLocal);

                    code.Append(Instruction.Create(OpCodes.Ldarg, param));
                    code.Append(Instruction.Create(OpCodes.Stloc, paramLocal));

                    var paramDef = param.ParameterType.Resolve();
                    if (paramDef != null && GenerateSerializersProcessor.HasInterface(paramDef, typeof(IAsyncPackable)))
                    {
                        var prepareForPack = CreatePrepareForPackMethod(module, param.ParameterType);
                        code.Append(Instruction.Create(OpCodes.Ldloca, paramLocal));
                        code.Append(Instruction.Create(OpCodes.Call, prepareForPack));
                    }

                    MethodReference serializeGenericMethod;

                    if (useDeltaPacking)
                    {
                        serializeGenericMethod = CreateDeltaSerializer(module, param.ParameterType, rpcPacker, true);
                        code.Append(Instruction.Create(OpCodes.Ldloca, rpcPacker));
                    }
                    else serializeGenericMethod = CreateSerializer(module, param.ParameterType, true);

                    code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
                    code.Append(Instruction.Create(OpCodes.Ldloc, paramLocal));
                    code.Append(Instruction.Create(OpCodes.Call, serializeGenericMethod));
                }

            // Call RPCModule.PreProcessRpc(RPCPacket packet, RPCSignature signature, ref BitPacker packer)
            var preProcessRpc = rpcType.GetMethod("PreProcessRpc").Import(module);

            // get rpcDataVariable.data field
            code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature)); // stream
            code.Append(Instruction.Create(OpCodes.Ldloca, streamVariable));

            var dataField = packetType.GetField("data").Import(module);
            code.Append(Instruction.Create(OpCodes.Ldloca, rpcDataVariable));
            code.Append(Instruction.Create(OpCodes.Ldflda, dataField));

            code.Append(Instruction.Create(OpCodes.Call, preProcessRpc));

            if (!methodRpc.Signature.isStatic)
                code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this

            code.Append(Instruction.Create(OpCodes.Ldloc, rpcDataVariable)); // rpcPacket
            code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature)); // rpcDetails

            if (methodRpc.Signature.isStatic)
            {
                var sendRpc = rpcType.GetMethod("SendStaticRPC").Import(module);
                code.Append(Instruction.Create(OpCodes.Call, sendRpc));
            }
            else if (isNetworkClass)
            {
                var sendRpc = module.GetTypeDefinition<NetworkModule>().GetMethod("SendRPC").Import(module);
                code.Append(Instruction.Create(OpCodes.Call, sendRpc));
            }
            else
            {
                var sendRpc = identityType.GetMethod("SendRPC").Import(module);
                code.Append(Instruction.Create(OpCodes.Call, sendRpc));
            }

            code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
            code.Append(Instruction.Create(OpCodes.Call, freeStreamMethod));

            code.Append(skipSyncPathLabel);

            if (useDeltaPacking && hasMultipleTargets)
            {
                // i -= 1;
                code.Append(Instruction.Create(OpCodes.Ldloc, iterator));
                code.Append(Instruction.Create(OpCodes.Ldc_I4_1));
                code.Append(Instruction.Create(OpCodes.Sub));
                code.Append(Instruction.Create(OpCodes.Stloc, iterator));

                // jump back up
                code.Append(Instruction.Create(OpCodes.Br, playersLoopStart));
                code.Append(playersLoopEnd);

                var disposeConcret = playersListType.GetMethodRef("Dispose");

                // playersList.Dispose();
                code.Append(Instruction.Create(OpCodes.Ldloca, playersList));
                code.Append(Instruction.Create(OpCodes.Call, disposeConcret.Import(module)));

                if (useFanoutGrouping)
                {
                    // grouper.End();
                    var grouperType = module.GetTypeDefinition<DeltaFanoutGrouper>();
                    code.Append(Instruction.Create(OpCodes.Ldloc, fanoutGrouper));
                    code.Append(Instruction.Create(OpCodes.Call, grouperType.GetMethod("End").Import(module)));
                }
            }

            code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
            code.Append(Instruction.Create(OpCodes.Ldfld,
                module.GetTypeDefinition<RPCSignature>().GetField("runLocally").Import(module)));
            code.Append(Instruction.Create(OpCodes.Brtrue, executeRunLocally));

            /*if (methodRpc.Signature.type == RPCType.ServerRPC)
            {
                PutIsServerOnStack(module, methodRpc, isNetworkClass, code, moduleType, identityType);
                code.Append(Instruction.Create(OpCodes.Brtrue, executeRunLocally));
            }*/

            code.Append(Instruction.Create(OpCodes.Br, endOfRunLocallyCheck));
            code.Append(executeRunLocally);

            bool hasRpcInfoParam = paramCount > 0 && newMethod.Parameters[^1].ParameterType.FullName == typeof(RPCInfo).FullName;
            if (hasRpcInfoParam)
            {
                var RPCInfo_compileTimeSignatureField = module.GetTypeDefinition<RPCInfo>()
                    .GetField("compileTimeSignature").Import(module);

                var RPCInfo_manager = module.GetTypeDefinition<RPCInfo>()
                    .GetField("manager").Import(module);

                var RPCInfo_sender = module.GetTypeDefinition<RPCInfo>()
                    .GetField("sender").Import(module);

                var RPCInfo_asServer = module.GetTypeDefinition<RPCInfo>()
                    .GetField("asServer").Import(module);

                code.Append(Instruction.Create(OpCodes.Ldarga, newMethod.Parameters[^1]));
                code.Append(Instruction.Create(OpCodes.Dup));
                code.Append(Instruction.Create(OpCodes.Dup));
                code.Append(Instruction.Create(OpCodes.Dup));

                code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
                code.Append(Instruction.Create(OpCodes.Stfld, RPCInfo_compileTimeSignatureField));

                PushNetworkManager(module, code, isNetworkClass, methodRpc.Signature.isStatic);
                code.Append(Instruction.Create(OpCodes.Stfld, RPCInfo_manager));

                PushLocalPlayerProp(module, code, isNetworkClass, methodRpc.Signature.isStatic);
                code.Append(Instruction.Create(OpCodes.Stfld, RPCInfo_sender));

                code.Append(Instruction.Create(methodRpc.Signature.type == RPCType.ServerRPC ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0));
                code.Append(Instruction.Create(OpCodes.Stfld, RPCInfo_asServer));
            }

            var callMethod = GetOriginalMethod(method);

            if (method.HasGenericParameters)
            {
                var genericInstanceMethod = new GenericInstanceMethod(callMethod);

                for (var i = 0; i < method.GenericParameters.Count; i++)
                {
                    var gp = method.GenericParameters[i];
                    genericInstanceMethod.GenericArguments.Add(gp);
                }

                callMethod = genericInstanceMethod;
            }

            if (!methodRpc.Signature.isStatic)
                code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this

            // Packer.Copy<T>
            var copyMethod = packerType.GetMethod("Copy", true).Import(module);

            for (int i = 0; i < paramCount; ++i)
            {
                var param = newMethod.Parameters[i];

                bool shouldIgnore = ShouldIgnore(methodRpc.Signature.type, param, i, paramCount, out _);
                var resolved = param.ParameterType.Resolve();

                if (shouldIgnore || resolved != null && resolved.IsUnmanaged())
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg, param));
                    continue;
                }

                code.Append(Instruction.Create(OpCodes.Ldarga, param)); // param

                var copyMethodGeneric = new GenericInstanceMethod(copyMethod);
                copyMethodGeneric.GenericArguments.Add(param.ParameterType);
                code.Append(Instruction.Create(OpCodes.Call, copyMethodGeneric)); // Packer.Copy<T>(param)
            }

            code.Append(Instruction.Create(OpCodes.Call, callMethod)); // Call original method

            // Pop return value if not void for now
            code.Append(Instruction.Create(OpCodes.Ret));
            code.Append(endOfRunLocallyCheck);

            if (returnMode != ReturnMode.Void)
                code.Append(Instruction.Create(OpCodes.Ldloc, taskWithReturnType));

            code.Append(Instruction.Create(OpCodes.Ret));

            if (!isServerBuild)
            {
                if (settings.stripServerCode && methodRpc.Signature is { runLocally: false, type: RPCType.ServerRPC })
                {
                    StripBody(method, methodRpc.ogName, settings, methodRpc.Signature.stripCodeMode);
                    FixShortFormJumps(method);
                }
            }

            }

            return newMethod;
        }

        private static void CreateAndSetPacket(ModuleDefinition module, int id, RPCMethod methodRpc, bool isNetworkClass,
            TypeDefinition rpcType, MethodDefinition method, ILProcessor code, VariableDefinition streamVariable,
            PropertyDefinition getId, PropertyDefinition getSceneId, VariableDefinition rpcDataVariable)
        {
            if (methodRpc.Signature.isStatic)
            {
                var buildRawRPCMethod = rpcType.GetMethod("BuildStaticRawRPC", true).Import(module);
                var genericInstanceMethod = new GenericInstanceMethod(buildRawRPCMethod);
                genericInstanceMethod.GenericArguments.Add(method.DeclaringType);

                // rpcId, stream
                code.Append(Instruction.Create(OpCodes.Ldc_I4, id));
                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));

                // BuildStaticRawRPC(int rpcId, NetworkStream stream)
                code.Append(Instruction.Create(OpCodes.Call, genericInstanceMethod));
            }
            else if (isNetworkClass)
            {
                var buildChildRpc = module.GetTypeDefinition<NetworkModule>().GetMethod("BuildRPC").Import(module);

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldc_I4, id));
                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
                code.Append(Instruction.Create(OpCodes.Call, buildChildRpc));
            }
            else
            {
                var buildRawRPCMethod = rpcType.GetMethod("BuildRawRPC").Import(module);

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, getId.GetMethod.Import(module))); // id
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, getSceneId.GetMethod.Import(module))); // sceneId
                code.Append(Instruction.Create(OpCodes.Ldc_I4, id)); // rpcId
                code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable)); // stream

                // BuildRawRPC(int networkId, SceneID sceneId, byte rpcId, NetworkStream stream, RPCDetails details)
                code.Append(Instruction.Create(OpCodes.Call, buildRawRPCMethod));
            }

            code.Append(Instruction.Create(OpCodes.Stloc, rpcDataVariable)); // rpcPacket
        }

        private static void PutIsServerOnStack(ModuleDefinition module, RPCMethod methodRpc, bool isNetworkClass,
            ILProcessor code, TypeDefinition moduleType, TypeDefinition identityType)
        {
            if (methodRpc.Signature.isStatic)
            {
                // NetworkManager.main.isServerOnly
                var managerType = module.GetTypeDefinition<NetworkManager>();
                var mainManagerProp = managerType.GetProperty("main");
                var mainManagerGetter = mainManagerProp.GetMethod.Import(module);
                var getIsServerOnly = module.GetTypeDefinition<NetworkManager>().GetProperty("isServer")
                    .GetMethod.Import(module);

                code.Append(Instruction.Create(OpCodes.Call, mainManagerGetter));
                code.Append(Instruction.Create(OpCodes.Call, getIsServerOnly));
            }
            else if (isNetworkClass)
            {
                var getIsServerOnly = moduleType.GetProperty("isServer");
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, getIsServerOnly.GetMethod.Import(module)));
            }
            else
            {
                var getIsServerOnly = identityType.GetProperty("isServer");
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, getIsServerOnly.GetMethod.Import(module)));
            }
        }

        private static MethodReference CreateDeltaSerializer(ModuleDefinition module, TypeReference type, VariableDefinition packer, bool isWrite)
        {
            var method = packer.VariableType.GetMethod(isWrite ? "Write" : "Read", true).Import(module);

            var genericMethod = new GenericInstanceMethod(method);
            genericMethod.GenericArguments.Add(type);

            try
            {
                return genericMethod.Import(module);
            }
            catch (Exception e)
            {
                throw new Exception(
                    $"Failed to import method '{genericMethod.FullName}'. Module: {module.Name}, Target: {type.FullName}",
                    e);
            }
        }

        private static MethodReference CreateSerializer(ModuleDefinition module, TypeReference type, bool isWrite)
        {
            var packerType = module.GetTypeDefinition(typeof(Packer<>)).Import(module);
            var method = packerType.GetMethod(isWrite ? "Write" : "Read").Import(module);

            var genericPackerType = new GenericInstanceType(packerType);
            genericPackerType.GenericArguments.Add(type);

            var genericWriteMethod =
                new MethodReference(method.Name, method.ReturnType, genericPackerType.Import(module))
                {
                    HasThis = method.HasThis
                };

            foreach (var param in method.Parameters)
                genericWriteMethod.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes,
                    param.ParameterType));

            try
            {
                return genericWriteMethod.Import(module);
            }
            catch (Exception e)
            {
                throw new Exception(
                    $"Failed to import method '{genericWriteMethod.FullName}'. Module: {module.Name}, Target: {type.FullName}",
                    e);
            }
        }

        private static bool HasAsyncPackableParam(RPCMethod rpcMethod)
        {
            var paramCount = rpcMethod.originalMethod.Parameters.Count;
            for (var i = 0; i < paramCount; i++)
            {
                var param = rpcMethod.originalMethod.Parameters[i];
                if (ShouldIgnore(rpcMethod.Signature.type, param, i, paramCount, out _))
                    continue;
                if (param.ParameterType is GenericParameter)
                    continue;
                var def = param.ParameterType.Resolve();
                if (def != null && GenerateSerializersProcessor.HasInterface(def, typeof(IAsyncPackable)))
                    return true;
            }
            return false;
        }

        private static MethodReference CreatePrepareForPackMethod(ModuleDefinition module, TypeReference type)
        {
            var helperType = module.GetTypeDefinition(typeof(AsyncPackableHelper)).Import(module);
            var method = helperType.Resolve().Methods.First(m =>
                m.Name == "PrepareForPack" && m.HasGenericParameters && m.GenericParameters.Count == 1);
            var methodRef = method.Import(module);

            var genericMethod = new GenericInstanceMethod(methodRef);
            genericMethod.GenericArguments.Add(type);
            return genericMethod.Import(module);
        }

        private static MethodReference CreatePrepareForPackAsyncMethod(ModuleDefinition module, TypeReference type)
        {
            var helperType = module.GetTypeDefinition(typeof(AsyncPackableHelper)).Import(module);
            var method = helperType.Resolve().Methods.First(m =>
                m.Name == "PrepareForPackAsync" && m.HasGenericParameters && m.GenericParameters.Count == 1);
            var methodRef = method.Import(module);

            var genericMethod = new GenericInstanceMethod(methodRef);
            genericMethod.GenericArguments.Add(type);
            return genericMethod.Import(module);
        }

        private static MethodReference CreateGetTaskResultMethod(ModuleDefinition module, TypeReference type)
        {
            var helperType = module.GetTypeDefinition(typeof(AsyncPackableHelper)).Import(module);
            var method = helperType.Resolve().Methods.First(m =>
                m.Name == "GetTaskResult" && m.HasGenericParameters && m.GenericParameters.Count == 1);
            var methodRef = method.Import(module);

            var genericMethod = new GenericInstanceMethod(methodRef);
            genericMethod.GenericArguments.Add(type);
            return genericMethod.Import(module);
        }

        private static MethodReference CreatePrepareAfterUnpackMethod(ModuleDefinition module, TypeReference type)
        {
            var helperType = module.GetTypeDefinition(typeof(AsyncPackableHelper)).Import(module);
            var method = helperType.Resolve().Methods.First(m =>
                m.Name == "PrepareAfterUnpack" && m.HasGenericParameters && m.GenericParameters.Count == 1);
            var methodRef = method.Import(module);

            var genericMethod = new GenericInstanceMethod(methodRef);
            genericMethod.GenericArguments.Add(type);
            return genericMethod.Import(module);
        }

        private static MethodReference CreatePrepareAfterUnpackAsyncMethod(ModuleDefinition module, TypeReference type)
        {
            var helperType = module.GetTypeDefinition(typeof(AsyncPackableHelper)).Import(module);
            var method = helperType.Resolve().Methods.First(m =>
                m.Name == "PrepareAfterUnpackAsync" && m.HasGenericParameters && m.GenericParameters.Count == 1);
            var methodRef = method.Import(module);

            var genericMethod = new GenericInstanceMethod(methodRef);
            genericMethod.GenericArguments.Add(type);
            return genericMethod.Import(module);
        }

        private static TypeReference GetTaskTypeFromModule(ModuleDefinition module)
        {
            var refsToCheck = new List<AssemblyNameReference>(module.AssemblyReferences);
            var resolver = module.AssemblyResolver;
            foreach (var r in module.AssemblyReferences)
            {
                try
                {
                    var a = resolver.Resolve(r);
                    if (a != null)
                    {
                        foreach (var sub in a.MainModule.AssemblyReferences)
                        {
                            if (sub.Name != "System.Private.CoreLib" && !refsToCheck.Any(x => x.FullName == sub.FullName))
                                refsToCheck.Add(sub);
                        }
                    }
                }
                catch { }
            }
            foreach (var asmRef in refsToCheck)
            {
                if (asmRef.Name == "System.Private.CoreLib") continue;
                try
                {
                    var asm = resolver.Resolve(asmRef);
                    if (asm == null) continue;
                    var taskDef = asm.MainModule.GetType("System.Threading.Tasks", "Task");
                    if (taskDef != null)
                        return module.ImportReference(taskDef);
                }
                catch { }
            }
            throw new InvalidOperationException("Could not resolve System.Threading.Tasks.Task from module references.");
        }

        /// <summary>
        /// Resolves Task types from an assembly the module already references (netstandard/mscorlib),
        /// avoiding System.Private.CoreLib which Unity does not have.
        /// </summary>
        private static void ResolveTaskTypes(ModuleDefinition module, out TypeReference taskType,
            out TypeReference taskArrayType, out TypeReference taskOfTOpen, out TypeReference actionOfTOpen,
            out MethodReference completedTaskGetter, out MethodReference actionCtor)
        {
            var refsToCheck = new List<AssemblyNameReference>(module.AssemblyReferences);
            var resolver = module.AssemblyResolver;
            foreach (var r in module.AssemblyReferences)
            {
                try
                {
                    var a = resolver.Resolve(r);
                    if (a != null)
                    {
                        foreach (var sub in a.MainModule.AssemblyReferences)
                        {
                            if (sub.Name != "System.Private.CoreLib" && !refsToCheck.Any(x => x.FullName == sub.FullName))
                                refsToCheck.Add(sub);
                        }
                    }
                }
                catch { }
            }

            foreach (var asmRef in refsToCheck)
            {
                if (asmRef.Name == "System.Private.CoreLib")
                    continue;
                try
                {
                    var asm = resolver.Resolve(asmRef);
                    if (asm == null) continue;
                    var taskDef = asm.MainModule.GetType("System.Threading.Tasks", "Task");
                    var taskOfTDef = asm.MainModule.GetType("System.Threading.Tasks", "Task`1");
                    if (taskDef == null || taskOfTDef == null) continue;

                    taskType = module.ImportReference(taskDef);
                    taskOfTOpen = module.ImportReference(taskOfTDef);
                    taskArrayType = new ArrayType(module.ImportReference(taskDef));

                    var completedTask = taskDef.Methods.FirstOrDefault(m => m.Name == "get_CompletedTask" && m.IsStatic);
                    if (completedTask == null) continue;
                    completedTaskGetter = module.ImportReference(completedTask);

                    var actionDef = asm.MainModule.GetType("System", "Action");
                    if (actionDef == null) continue;
                    var actionCtorDef = actionDef.Methods.FirstOrDefault(m =>
                        m.IsConstructor && m.Parameters.Count == 2);
                    if (actionCtorDef == null) continue;
                    actionCtor = module.ImportReference(actionCtorDef);

                    var actionOfTDef = asm.MainModule.GetType("System", "Action`1");
                    if (actionOfTDef == null) continue;
                    actionOfTOpen = module.ImportReference(actionOfTDef);
                    return;
                }
                catch
                {
                    // try next assembly
                }
            }

            throw new InvalidOperationException(
                "Could not resolve System.Threading.Tasks.Task from module references. " +
                "Ensure the assembly references netstandard or mscorlib.");
        }

        private static void GenerateAsyncPrepareAndSendPath(ModuleDefinition module, RPCMethod methodRpc, int id,
            bool isNetworkClass, MethodDefinition newMethod, ILProcessor code, int paramCount,
            VariableDefinition streamVariable, VariableDefinition rpcDataVariable, VariableDefinition rpcSignature,
            TypeReference packetType, TypeDefinition rpcType, TypeDefinition identityType,
            MethodReference allocStreamMethod, MethodReference freeStreamMethod, TypeReference streamType)
        {
            ResolveTaskTypes(module, out var taskType, out var taskArrayType, out var taskOfTOpen,
                out var actionOfTOpen, out var completedTaskGetter, out var actionCtor);

            var stateType = new TypeDefinition("", $"RpcSendState_{id}",
                TypeAttributes.NestedPrivate | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                module.TypeSystem.Object);
            methodRpc.originalMethod.DeclaringType.NestedTypes.Add(stateType);

            var paramFields = new List<FieldDefinition>();
            for (var i = 0; i < paramCount; i++)
            {
                var param = methodRpc.originalMethod.Parameters[i];
                if (methodRpc.Signature.type == RPCType.TargetRPC && i == 0) continue;
                if (ShouldIgnore(methodRpc.Signature.type, param, i, paramCount, out _)) continue;

                var field = new FieldDefinition($"param{i}", FieldAttributes.Public, param.ParameterType);
                stateType.Fields.Add(field);
                paramFields.Add(field);
            }

            var streamField = new FieldDefinition("stream", FieldAttributes.Public, streamType.Import(module));
            var rpcDataField = new FieldDefinition("rpcData", FieldAttributes.Public, rpcDataVariable.VariableType);
            var rpcSigField = new FieldDefinition("rpcSig", FieldAttributes.Public, rpcSignature.VariableType);
            stateType.Fields.Add(streamField);
            stateType.Fields.Add(rpcDataField);
            stateType.Fields.Add(rpcSigField);

            FieldDefinition thisField = null;
            if (!methodRpc.Signature.isStatic)
            {
                thisField = new FieldDefinition("_this", FieldAttributes.Public,
                    methodRpc.originalMethod.DeclaringType.Import(module));
                stateType.Fields.Add(thisField);
            }

            var doSend = new MethodDefinition("DoSend", MethodAttributes.Public, module.TypeSystem.Void);
            stateType.Methods.Add(doSend);
            var doSendIl = doSend.Body.GetILProcessor();

            var paramIdx = 0;
            for (var i = 0; i < paramCount; i++)
            {
                var param = methodRpc.originalMethod.Parameters[i];
                if (methodRpc.Signature.type == RPCType.TargetRPC && i == 0) continue;
                if (ShouldIgnore(methodRpc.Signature.type, param, i, paramCount, out _)) continue;

                var serialize = CreateSerializer(module, param.ParameterType, true);
                doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                doSendIl.Append(Instruction.Create(OpCodes.Ldfld, streamField));
                doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                doSendIl.Append(Instruction.Create(OpCodes.Ldfld, paramFields[paramIdx]));
                doSendIl.Append(Instruction.Create(OpCodes.Call, serialize));
                paramIdx++;
            }

            var preProcessRpc = rpcType.GetMethod("PreProcessRpc").Import(module);
            var dataField = packetType.Resolve().GetField("data").Import(module);
            doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
            doSendIl.Append(Instruction.Create(OpCodes.Ldfld, rpcSigField));
            doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
            doSendIl.Append(Instruction.Create(OpCodes.Ldflda, streamField));
            doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
            doSendIl.Append(Instruction.Create(OpCodes.Ldflda, rpcDataField));
            doSendIl.Append(Instruction.Create(OpCodes.Ldflda, dataField));
            doSendIl.Append(Instruction.Create(OpCodes.Call, preProcessRpc));

            if (!methodRpc.Signature.isStatic)
            {
                doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                doSendIl.Append(Instruction.Create(OpCodes.Ldfld, thisField));
            }
            doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
            doSendIl.Append(Instruction.Create(OpCodes.Ldfld, rpcDataField));
            doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
            doSendIl.Append(Instruction.Create(OpCodes.Ldfld, rpcSigField));
            if (methodRpc.Signature.isStatic)
            {
                var sendRpc = rpcType.GetMethod("SendStaticRPC").Import(module);
                doSendIl.Append(Instruction.Create(OpCodes.Call, sendRpc));
            }
            else if (isNetworkClass)
            {
                var sendRpc = module.GetTypeDefinition<NetworkModule>().GetMethod("SendRPC").Import(module);
                doSendIl.Append(Instruction.Create(OpCodes.Call, sendRpc));
            }
            else
            {
                var sendRpc = identityType.GetMethod("SendRPC").Import(module);
                doSendIl.Append(Instruction.Create(OpCodes.Call, sendRpc));
            }
            doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
            doSendIl.Append(Instruction.Create(OpCodes.Ldfld, streamField));
            doSendIl.Append(Instruction.Create(OpCodes.Call, freeStreamMethod));

            var runLocally = methodRpc.Signature.runLocally;
            MethodDefinition runLocal = null;
            if (runLocally)
            {
                runLocal = new MethodDefinition("RunLocal", MethodAttributes.Public, module.TypeSystem.Void);
                stateType.Methods.Add(runLocal);
                var runLocalIl = runLocal.Body.GetILProcessor();
                var callOriginal = GetOriginalMethod(methodRpc.originalMethod).Import(module);
                if (thisField != null)
                {
                    runLocalIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                    runLocalIl.Append(Instruction.Create(OpCodes.Ldfld, thisField));
                }
                paramIdx = 0;
                for (var i = 0; i < paramCount; i++)
                {
                    if (methodRpc.Signature.type == RPCType.TargetRPC && i == 0) { paramIdx++; continue; }
                    if (ShouldIgnore(methodRpc.Signature.type, methodRpc.originalMethod.Parameters[i], i, paramCount, out _))
                    { paramIdx++; continue; }
                    runLocalIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                    runLocalIl.Append(Instruction.Create(OpCodes.Ldfld, paramFields[paramIdx].Import(module)));
                    paramIdx++;
                }
                runLocalIl.Append(Instruction.Create(OpCodes.Call, callOriginal));
                runLocalIl.Append(Instruction.Create(OpCodes.Ret));
            }

            if (runLocally && runLocal != null)
            {
                doSendIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                doSendIl.Append(Instruction.Create(OpCodes.Call, runLocal.Import(module)));
            }
            doSendIl.Append(Instruction.Create(OpCodes.Ret));

            var asyncParamIndices = new List<int>();
            var asyncParamTypes = new List<TypeReference>();
            paramIdx = 0;
            for (var i = 0; i < paramCount; i++)
            {
                var param = methodRpc.originalMethod.Parameters[i];
                if (methodRpc.Signature.type == RPCType.TargetRPC && i == 0) continue;
                if (ShouldIgnore(methodRpc.Signature.type, param, i, paramCount, out _)) continue;
                if (param.ParameterType is GenericParameter) { paramIdx++; continue; }
                var def = param.ParameterType.Resolve();
                if (def != null && GenerateSerializersProcessor.HasInterface(def, typeof(IAsyncPackable)))
                {
                    asyncParamIndices.Add(paramIdx);
                    asyncParamTypes.Add(param.ParameterType);
                }
                paramIdx++;
            }

            MethodDefinition storeResultsAndSend = null;
            if (asyncParamIndices.Count > 0)
            {
                storeResultsAndSend = new MethodDefinition("StoreResultsAndSend",
                    MethodAttributes.Public, module.TypeSystem.Void);
                storeResultsAndSend.Parameters.Add(new ParameterDefinition(taskArrayType));
                stateType.Methods.Add(storeResultsAndSend);
                var storeIl = storeResultsAndSend.Body.GetILProcessor();
                for (var j = 0; j < asyncParamIndices.Count; j++)
                {
                    var paramFieldIdx = asyncParamIndices[j];
                    var paramType = asyncParamTypes[j];
                    var taskOfT = new GenericInstanceType(taskOfTOpen) { GenericArguments = { paramType } };
                    var getTaskResult = CreateGetTaskResultMethod(module, paramType);
                    storeIl.Append(Instruction.Create(OpCodes.Ldarg_0)); // obj for stfld (must be under value)
                    storeIl.Append(Instruction.Create(OpCodes.Ldarg_1));
                    storeIl.Append(Instruction.Create(OpCodes.Ldc_I4, j));
                    storeIl.Append(Instruction.Create(OpCodes.Ldelem_Ref));
                    storeIl.Append(Instruction.Create(OpCodes.Castclass, taskOfT.Import(module)));
                    storeIl.Append(Instruction.Create(OpCodes.Call, getTaskResult)); // value on stack
                    storeIl.Append(Instruction.Create(OpCodes.Stfld, paramFields[paramFieldIdx].Import(module)));
                }
                storeIl.Append(Instruction.Create(OpCodes.Ldarg_0));
                storeIl.Append(Instruction.Create(OpCodes.Call, doSend.Import(module)));
                storeIl.Append(Instruction.Create(OpCodes.Ret));
            }

            var stateVar = new VariableDefinition(stateType.Import(module));
            newMethod.Body.Variables.Add(stateVar);

            var stateCtor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName |
                MethodAttributes.RTSpecialName, module.TypeSystem.Void);
            stateType.Methods.Add(stateCtor);
            stateCtor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ldarg_0));
            stateCtor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Call,
                module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes))));
            stateCtor.Body.GetILProcessor().Append(Instruction.Create(OpCodes.Ret));

            code.Append(Instruction.Create(OpCodes.Newobj, stateCtor.Import(module)));
            code.Append(Instruction.Create(OpCodes.Stloc, stateVar));

            paramIdx = 0;
            for (var i = 0; i < paramCount; i++)
            {
                var param = newMethod.Parameters[i];
                if (methodRpc.Signature.type == RPCType.TargetRPC && i == 0) continue;
                if (ShouldIgnore(methodRpc.Signature.type, param, i, paramCount, out _)) continue;

                code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                code.Append(Instruction.Create(OpCodes.Ldarg, param));
                code.Append(Instruction.Create(OpCodes.Stfld, paramFields[paramIdx].Import(module)));
                paramIdx++;
            }
            code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
            code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
            code.Append(Instruction.Create(OpCodes.Stfld, streamField.Import(module)));
            code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
            code.Append(Instruction.Create(OpCodes.Ldloc, rpcDataVariable));
            code.Append(Instruction.Create(OpCodes.Stfld, rpcDataField.Import(module)));
            code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
            code.Append(Instruction.Create(OpCodes.Ldloc, rpcSignature));
            code.Append(Instruction.Create(OpCodes.Stfld, rpcSigField.Import(module)));
            if (thisField != null)
            {
                code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Stfld, thisField.Import(module)));
            }

            List<VariableDefinition> taskVars = null;
            if (asyncParamIndices.Count == 0)
            {
                var executeAfterPrepareSingle = module.GetTypeDefinition(typeof(AsyncPackableHelper))
                    .Methods.First(m => m.Name == "ExecuteAfterPrepareAsync" && m.Parameters.Count == 2 &&
                        !m.Parameters[0].ParameterType.IsArray)
                    .Import(module);
                code.Append(Instruction.Create(OpCodes.Call, completedTaskGetter));
                code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                code.Append(Instruction.Create(OpCodes.Ldftn, doSend.Import(module)));
                code.Append(Instruction.Create(OpCodes.Newobj, actionCtor));
                code.Append(Instruction.Create(OpCodes.Call, executeAfterPrepareSingle));
            }
            else
            {
                taskVars = new List<VariableDefinition>();
                paramIdx = 0;
                for (var i = 0; i < paramCount; i++)
                {
                    var param = methodRpc.originalMethod.Parameters[i];
                    if (methodRpc.Signature.type == RPCType.TargetRPC && i == 0) continue;
                    if (ShouldIgnore(methodRpc.Signature.type, param, i, paramCount, out _)) continue;
                    if (param.ParameterType is GenericParameter) { paramIdx++; continue; }
                    var def = param.ParameterType.Resolve();
                    if (def == null || !GenerateSerializersProcessor.HasInterface(def, typeof(IAsyncPackable)))
                    { paramIdx++; continue; }

                    var prepareAsync = CreatePrepareForPackAsyncMethod(module, param.ParameterType);
                    code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                    code.Append(Instruction.Create(OpCodes.Ldfld, paramFields[paramIdx].Import(module)));
                    code.Append(Instruction.Create(OpCodes.Call, prepareAsync));
                    var taskVar = new VariableDefinition(taskType);
                    newMethod.Body.Variables.Add(taskVar);
                    taskVars.Add(taskVar);
                    code.Append(Instruction.Create(OpCodes.Stloc, taskVar));
                    paramIdx++;
                }

                code.Append(Instruction.Create(OpCodes.Ldc_I4, taskVars.Count));
                code.Append(Instruction.Create(OpCodes.Newarr, taskType));
                var tasksArrayVar = new VariableDefinition(taskArrayType);
                newMethod.Body.Variables.Add(tasksArrayVar);
                code.Append(Instruction.Create(OpCodes.Stloc, tasksArrayVar));
                for (var t = 0; t < taskVars.Count; t++)
                {
                    code.Append(Instruction.Create(OpCodes.Ldloc, tasksArrayVar));
                    code.Append(Instruction.Create(OpCodes.Ldc_I4, t));
                    code.Append(Instruction.Create(OpCodes.Ldloc, taskVars[t]));
                    code.Append(Instruction.Create(OpCodes.Stelem_Ref));
                }

                var executeAfterPrepareMulti = module.GetTypeDefinition(typeof(AsyncPackableHelper))
                    .Methods.First(m => m.Name == "ExecuteAfterPrepareAsync" && m.Parameters.Count == 2 &&
                        m.Parameters[0].ParameterType.IsArray)
                    .Import(module);
                var actionTaskArray = new GenericInstanceType(actionOfTOpen) { GenericArguments = { taskArrayType } };
                var actionTaskArrayCtorDef = actionOfTOpen.Resolve().Methods.First(m =>
                    m.IsConstructor && m.Parameters.Count == 2);
                var actionTaskArrayCtor = new MethodReference(".ctor", module.TypeSystem.Void, actionTaskArray)
                    { HasThis = true };
                actionTaskArrayCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
                actionTaskArrayCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
                code.Append(Instruction.Create(OpCodes.Ldloc, tasksArrayVar));
                code.Append(Instruction.Create(OpCodes.Ldloc, stateVar));
                code.Append(Instruction.Create(OpCodes.Ldftn, storeResultsAndSend.Import(module)));
                code.Append(Instruction.Create(OpCodes.Newobj, module.ImportReference(actionTaskArrayCtor)));
                code.Append(Instruction.Create(OpCodes.Call, executeAfterPrepareMulti));
            }
            // ExecuteAfterPrepareAsync returns Task — discard it; the outer method appends a
            // `Br skipSyncPathLabel` after this call which routes to the join point that handles
            // the request/response Task return (or void) consistently with the sync send path.
            code.Append(Instruction.Create(OpCodes.Pop));
        }

        enum TargetArgType
        {
            None,
            Player,
            Enumerator,
            List
        }

        static TargetArgType GetArgType(TypeReference type)
        {
            if (type == null)
                return TargetArgType.None;

            if (type.FullName == typeof(PlayerID).FullName)
                return TargetArgType.Player;

            if (type.IsArray && type.GetElementType().FullName == typeof(PlayerID).FullName)
                return TargetArgType.List;

            if (!(type is GenericInstanceType { HasGenericArguments: true } genType) ||
                genType.GenericArguments[0].FullName != typeof(PlayerID).FullName)
            {
                return TargetArgType.None;
            }

            var resolved = type.Resolve();

            if (GenerateSerializersProcessor.HasInterfaceRaw(resolved, typeof(IList<>)))
                return TargetArgType.List;

            if (GenerateSerializersProcessor.HasInterfaceRaw(resolved, typeof(IEnumerable<>)))
                return TargetArgType.Enumerator;

            return TargetArgType.None;
        }

        internal static void PushRPCSignatureMakeArgs(ILProcessor code, RPCMethod rpc)
        {
            code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)rpc.Signature.type));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)rpc.Signature.channel));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.runLocally ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.requireOwnership ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.bufferLast ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.requireServer ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.excludeOwner ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldstr, rpc.ogName ?? string.Empty));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.isStatic ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_R4, rpc.Signature.asyncTimeoutInSec));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)rpc.Signature.compressionLevel));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.excludeSender ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.deltaPacked ? 1 : 0));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, (int)rpc.Signature.mtuExceeded));
            code.Append(Instruction.Create(OpCodes.Ldc_I4, rpc.Signature.immediate ? 1 : 0));
        }

        private static void PushRPCSignature(ModuleDefinition module, ILProcessor code, RPCMethod rpc,
            bool isReceiving, bool isNetworkModule)
        {
            var rpcDetails = module.GetTypeDefinition<RPCSignature>();
            var makeRpcDetails = rpcDetails.GetMethod("Make").Import(module);
            var makeRpcDetailsTarget = rpcDetails.GetMethod("MakeWithTarget").Import(module);

            PushRPCSignatureMakeArgs(code, rpc);

            if (rpc.Signature.type == RPCType.TargetRPC)
            {
                if (!isReceiving)
                {
                    var argType = rpc.originalMethod.Parameters[0].ParameterType;

                    var type = GetArgType(argType);

                    switch (type)
                    {
                        case TargetArgType.Player:
                        {
                            code.Append(rpc.Signature.isStatic
                                ? Instruction.Create(OpCodes.Ldarg_0)
                                : Instruction.Create(OpCodes.Ldarg_1));
                            ConvertPlayerIDToNullable(module, code);

                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            break;
                        }
                        case TargetArgType.List:
                        {
                            LoadDefaultPlayerId(module, code);
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            code.Append(rpc.Signature.isStatic
                                ? Instruction.Create(OpCodes.Ldarg_0)
                                : Instruction.Create(OpCodes.Ldarg_1));
                            break;
                        }
                        case TargetArgType.Enumerator:
                        {
                            LoadDefaultPlayerId(module, code);
                            code.Append(rpc.Signature.isStatic
                                ? Instruction.Create(OpCodes.Ldarg_0)
                                : Instruction.Create(OpCodes.Ldarg_1));
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            break;
                        }
                        case TargetArgType.None:
                        default:
                        {
                            LoadDefaultPlayerId(module, code);
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            code.Append(Instruction.Create(OpCodes.Ldnull));
                            break;
                        }
                    }
                }
                else
                {
                    bool isStatic = rpc.Signature.isStatic;
                    PushLocalPlayerProp(module, code, isNetworkModule, isStatic);
                    ConvertPlayerIDToNullable(module, code);
                    code.Append(Instruction.Create(OpCodes.Ldnull));
                    code.Append(Instruction.Create(OpCodes.Ldnull));
                }

                code.Append(Instruction.Create(OpCodes.Call, makeRpcDetailsTarget));
            }
            else
            {
                code.Append(Instruction.Create(OpCodes.Call, makeRpcDetails));
            }
        }

        private static void LoadDefaultPlayerId(ModuleDefinition module, ILProcessor code)
        {
            var playdIdType = module.GetTypeDefinition<PlayerID>();
            playdIdType.Import(module);
            var getNullableMethod = playdIdType.GetMethod("GetDefaultNullable").Import(module);
            code.Append(Instruction.Create(OpCodes.Call, getNullableMethod));
        }

        private static void ConvertPlayerIDToNullable(ModuleDefinition module, ILProcessor code)
        {
            var playdIdType = module.GetTypeDefinition<PlayerID>();
            playdIdType.Import(module);
            var getNullableMethod = playdIdType.GetMethod("GetNullable").Import(module);
            code.Append(Instruction.Create(OpCodes.Call, getNullableMethod));
        }

        private static TypeReference GetParentType(ModuleDefinition module, bool isNetworkModule, bool isStatic)
        {
            if (!isStatic)
            {
                return isNetworkModule
                    ? module.GetTypeDefinition<NetworkModule>().Import(module)
                    : module.GetTypeDefinition<NetworkIdentity>().Import(module);
            }

            return module.GetTypeDefinition<RPCModule>().Import(module);
        }

        private static void PushLocalPlayerProp(ModuleDefinition module, ILProcessor code, bool isNetworkModule, bool isStatic)
        {
            if (!isStatic)
            {
                var localPlayerProp =
                    isNetworkModule
                        ? module.GetTypeDefinition<NetworkModule>().GetProperty("localPlayerForced").GetMethod
                            .Import(module)
                        : module.GetTypeDefinition<NetworkIdentity>().GetProperty("localPlayerForced").GetMethod
                            .Import(module);

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Call, localPlayerProp));
            }
            else
            {
                var rpcModule = module.GetTypeDefinition<RPCModule>();
                var getLocalPlayer = rpcModule.GetMethod("GetLocalPlayer").Import(module);
                code.Append(Instruction.Create(OpCodes.Call, getLocalPlayer));
            }
        }

        private static bool UpdateMethodReferences(ModuleDefinition module, MethodReference old, MethodReference @new,
            [UsedImplicitly] List<DiagnosticMessage> messages)
        {
            using var types =  DisposableList<TypeDefinition>.Create(32);
            var startLocalExecutionFlag = module.GetTypeDefinition(typeof(PurrCompilerFlags))
                .GetMethod("EnterLocalExecution").FullName;
            var exitLocalExecutionFlag = module.GetTypeDefinition(typeof(PurrCompilerFlags))
                .GetMethod("ExitLocalExecution").FullName;

            types.AddRange(module.Types);
            for (var i = 0; i < types.Count; i++)
                types.AddRange(types[i].NestedTypes);

            bool isSkipping = false;

            for (var tidx = 0; tidx < types.Count; tidx++)
            {
                var type = types[tidx];
                foreach (var method in type.Methods)
                {
                    if (method == @new || method.GetElementMethod() == @new) continue;

                    if (method.Body == null) continue;

                    var processor = method.Body.GetILProcessor();

                    bool hasLocalModeAttribute = method.CustomAttributes.Any(a =>
                        a.AttributeType.FullName == typeof(LocalModeAttribute).FullName);

                    if (hasLocalModeAttribute)
                        continue;

                    for (var i = 0; i < method.Body.Instructions.Count; i++)
                    {
                        var instruction = method.Body.Instructions[i];

                        if (instruction.OpCode == OpCodes.Call && instruction.Operand is MethodReference
                            {
                                DeclaringType: not null
                            } flag)
                        {
                            if (flag.FullName == startLocalExecutionFlag)
                            {
                                //processor.Replace(instruction, Instruction.Create(OpCodes.Nop));
                                if (isSkipping)
                                {
                                    Error(messages, "Local mode flag was already set, avoid nesting these flags.",
                                        method);
                                    return false;
                                }

                                isSkipping = true;
                                continue;
                            }

                            if (flag.FullName == exitLocalExecutionFlag)
                            {
                                //processor.Replace(instruction, Instruction.Create(OpCodes.Nop));
                                if (!isSkipping)
                                {
                                    Error(messages,
                                        "Local mode flag was not set, you should first call <b>PurrCompilerFlags.EnterLocalExecution()</b>",
                                        method);
                                    return false;
                                }

                                isSkipping = false;
                                continue;
                            }
                        }

                        if (isSkipping)
                            continue;

                        if (instruction.Operand is MethodReference methodReference &&
                            methodReference.GetElementMethod() == old)
                        {
                            // RpcSendState.RunLocal and RpcReceiveState.InvokeAfterPrepare intentionally
                            // call the _Original method, not the wrapper. Skip updating refs in those types.
                            if (type.Name.StartsWith("RpcSendState", StringComparison.Ordinal) ||
                                type.Name.StartsWith("RpcReceiveState", StringComparison.Ordinal))
                                continue;
                            var newRef = GenerateNewRef(@new, methodReference);
                            // in place: the call may be a branch target, Replace() would leave a dangling branch
                            instruction.Operand = newRef;
                        }
                    }

                    if (isSkipping)
                    {
                        Error(messages,
                            "Local mode flag was not unset, you should call <b>PurrCompilerFlags.ExitLocalExecution()</b>",
                            method);
                        return false;
                    }
                }
            }

            return true;
        }

        private static MethodReference GenerateNewRef(MethodReference @new, MethodReference methodReference)
        {
            // Check if methodReference is a MethodDefinition for a deeper copy if possible
            var methodDefinition = methodReference.Resolve();

            // Start with a MethodReference pointing to the new definition, copying name and return type
            var newRef = new MethodReference(@new.Name, @new.ReturnType, @new.DeclaringType)
            {
                HasThis = methodReference.HasThis,
                ExplicitThis = methodReference.ExplicitThis,
                CallingConvention = methodReference.CallingConvention,
            };

            // Clone parameters with exact types and attributes
            foreach (var parameter in methodDefinition.Parameters)
            {
                var newParameterType = parameter.ParameterType;
                if (newParameterType is GenericParameter && newRef.GenericParameters.Count > 0)
                {
                    // Ensure matching GenericParameter
                    var matchedParameter =
                        newRef.GenericParameters.FirstOrDefault(p => p.Name == newParameterType.Name);
                    if (matchedParameter != null) newParameterType = matchedParameter;
                }

                newRef.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, newParameterType));
            }

            // Handle generic parameters exactly as defined in the MethodDefinition
            foreach (var genericParameter in methodDefinition.GenericParameters)
            {
                var newGenericParameter = new GenericParameter(genericParameter.Name, newRef);
                newRef.GenericParameters.Add(newGenericParameter);
            }

            // If the methodReference is a GenericInstanceMethod, convert newRef to match
            if (methodReference is GenericInstanceMethod ogGenericMethodRef)
            {
                var newGenericInstanceMethod = new GenericInstanceMethod(newRef);

                // Match each generic argument exactly
                foreach (var argument in ogGenericMethodRef.GenericArguments)
                    newGenericInstanceMethod.GenericArguments.Add(argument);

                // Assign the generic instance method back to newRef
                newRef = newGenericInstanceMethod;
            }

            return newRef;
        }

        static DisposableList<TypeDefinition> GetAllTypes(ModuleDefinition module)
        {
            var types = DisposableList<TypeDefinition>.Create(32);

            types.AddRange(module.Types);
            for (var i = 0; i < types.Count; i++)
                types.AddRange(types[i].NestedTypes);

            return types;
        }


        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            try
            {
                if (!WillProcess(compiledAssembly))
                    return null!;

                var settings = PurrNetSettings.GetOrCreateSettings();
                bool isEditor = false;
                bool isServerBuild = false;

                foreach (var define in compiledAssembly.Defines)
                {
                    switch (define)
                    {
                        case "UNITY_EDITOR":
                            isEditor = true;
                            break;
                        case "UNITY_SERVER":
                            isServerBuild = true;
                            break;
                    }
                }

                if (isEditor)
                    isServerBuild = true;

                var visitedTypes = new HashSet<string>(128);
                var typesToGenerateSerializer = new HashSet<TypeReference>(128, TypeReferenceEqualityComparer.Default);
                var typesToPrepareHasher = new HashSet<TypeReference>(128, TypeReferenceEqualityComparer.Default);
                var typesToIgnoreForDelta = new HashSet<TypeReference>(128, TypeReferenceEqualityComparer.Default);
                var typesToIgnoreForSerialization = new HashSet<TypeReference>(128, TypeReferenceEqualityComparer.Default);

                var messages = new List<DiagnosticMessage>(32);

                using var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData);
                using var pdbStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData);
                var resolver = new AssemblyResolver(compiledAssembly);

                var assemblyDefinition = AssemblyDefinition.ReadAssembly(peStream, new ReaderParameters
                {
                    ReadSymbols = true,
                    SymbolStream = pdbStream,
                    SymbolReaderProvider = new PortablePdbReaderProvider(),
                    AssemblyResolver = resolver
                });

                resolver.SetSelf(assemblyDefinition);

                for (var m = 0; m < assemblyDefinition.Modules.Count; m++)
                {
                    var module = assemblyDefinition.Modules[m];

                    var hasPurrNetAsReference = HasPurrNetAsReference(compiledAssembly.Name, module);

                    using var types = GetAllTypes(module);
                    var usedTypes = new HashSet<TypeReference>(TypeReferenceEqualityComparer.Default);

                    for (var t = 0; t < types.Count; t++)
                    {
                        if (types[t].FullName == typeof(ApplicationConstants).FullName)
                            BakeApplicationConstants.Process(types[t], isEditor, messages);

                        if (types[t].FullName == typeof(PurrMetadata).FullName)
                            BakePurrVersion.Process(types[t], isEditor, messages);

                        if (types[t].HasInterfaces)
                        {
                            try
                            {
                                var mathInterfaceName = typeof(IMath<>).FullName;

                                for (var i = 0; i < types[t].Interfaces.Count; i++)
                                {
                                    var reference = types[t].Interfaces[i].InterfaceType;
                                    var resolved = reference.Resolve();

                                    if (resolved == null)
                                        continue;

                                    if (resolved.FullName == mathInterfaceName &&
                                        reference is GenericInstanceType genRef &&
                                        genRef.GenericArguments.Count == 1)
                                    {
                                        GenerateAutoMathProcessor.HandleType(types[t], genRef.GenericArguments[0],
                                            reference, messages);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                messages.Add(new DiagnosticMessage
                                {
                                    DiagnosticType = DiagnosticType.Error,
                                    MessageData = $"IMath: {e.Message}\n{e.StackTrace}"
                                });
                            }
                        }

                        UnityProxyProcessor.Process(types[t], messages);
                        RegisterSerializersProcessor.HandleType(types[t], module, types[t], typesToIgnoreForDelta,
                            typesToIgnoreForSerialization);

                        var type = types[t];

                        if (!hasPurrNetAsReference)
                            continue;

                        // check if it has RegisterNetworkTypeAttribute
                        foreach (var customAttribute in type.CustomAttributes)
                        {
                            if (customAttribute.AttributeType.FullName == typeof(RegisterNetworkTypeAttribute).FullName)
                            {
                                foreach (var field in customAttribute.ConstructorArguments)
                                {
                                    if (field.Value is TypeReference tref)
                                        typesToGenerateSerializer.Add(tref);
                                }
                            }
                        }

                        if (GenerateSerializersProcessor.HasInterface(type, typeof(IPackedAuto)) ||
                            GenerateSerializersProcessor.HasInterface(type, typeof(IPacked)) ||
                            GenerateSerializersProcessor.HasInterface(type, typeof(IPackedSimple)))
                        {
                            typesToGenerateSerializer.Add(type);
                        }


                        if (!type.IsClass)
                        {
                            for (var i = 0; i < type.Methods.Count; i++)
                            {
                                var method = type.Methods[i];
                                ProcessServerOnlyMethods(method, settings, isServerBuild, isEditor);
                                ProcessRunContextGuardedMethods(method, settings, false);
                            }
                            continue;
                        }

                        var idFullName = typeof(NetworkIdentity).FullName;
                        var classFullName = typeof(NetworkModule).FullName;

                        bool inheritsFromNetworkIdentity =
                            type.FullName == idFullName || InheritsFrom(type, idFullName);
                        bool inheritsFromNetworkClass =
                            type.FullName == classFullName || InheritsFrom(type, classFullName);

                        using var _rpcMethods = DisposableList<RPCMethod>.Create(32);

                        int idOffset = GetIDOffset(type, messages);

                        if (inheritsFromNetworkIdentity || inheritsFromNetworkClass)
                        {
                            var _networkFields = new List<FieldDefinition>();

                            // add the Preserve attribute
                            if (type.CustomAttributes.All(x =>
                                    x.AttributeType.FullName != typeof(PreserveAttribute).FullName))
                            {
                                var preserveAttribute = module.GetTypeDefinition<PreserveAttribute>();
                                var constructor = preserveAttribute.Resolve().Methods
                                    .First(md => md.IsConstructor && !md.HasParameters).Import(module);
                                type.CustomAttributes.Add(new CustomAttribute(constructor));
                            }

                            IncludeAnyConcreteGenericParameters(type, typesToGenerateSerializer);
                            FindNetworkModules(type, classFullName, _networkFields);
                            CreateSyncVarInitMethod(inheritsFromNetworkIdentity, module, type, _networkFields);
                        }

                        for (var i = 0; i < type.Methods.Count; i++)
                        {
                            try
                            {
                                var method = type.Methods[i];
                                ProcessServerOnlyMethods(method, settings, isServerBuild, isEditor);
                                ProcessRunContextGuardedMethods(method, settings, inheritsFromNetworkIdentity);

                                if (inheritsFromNetworkIdentity && MakeSureOverrideIsCalled.ShouldProcess(method))
                                    MakeSureOverrideIsCalled.Process(method, messages);

                                if (method.DeclaringType.FullName != type.FullName)
                                    continue;

                                var rpcType = GetMethodRPCType(method, messages);

                                if (rpcType == null)
                                    continue;

                                if (!rpcType.Value.isStatic && !inheritsFromNetworkIdentity &&
                                    !inheritsFromNetworkClass)
                                {
                                    string inheritsFrom = type.BaseType?.FullName ?? "null";
                                    Error(messages,
                                        $"RPC must be static if not inheriting from NetworkIdentity or NetworkClass | {inheritsFrom}",
                                        method);
                                    continue;
                                }

                                _rpcMethods.Add(new RPCMethod
                                {
                                    Signature = rpcType.Value, originalMethod = method, ogName = method.Name
                                });
                            }
                            catch (Exception e)
                            {
                                Error(messages, e.Message + "\n" + e.StackTrace, type.Methods[i]);
                            }
                        }

                        switch (inheritsFromNetworkIdentity)
                        {
                            case false when !inheritsFromNetworkClass && _rpcMethods.Count == 0:
                                continue;
                            case false when !inheritsFromNetworkClass && !inheritsFromNetworkIdentity:
                                typesToPrepareHasher.Add(type);
                                break;
                        }

                        if (inheritsFromNetworkIdentity || inheritsFromNetworkClass)
                            typesToGenerateSerializer.Add(type);

                        for (var index = 0; index < _rpcMethods.Count; index++)
                        {
                            var method = _rpcMethods[index].originalMethod;

                            try
                            {
                                var newMethod = HandleRPC(module, idOffset + index, _rpcMethods[index],
                                    inheritsFromNetworkClass, isServerBuild, settings, usedTypes, messages);

                                if (newMethod != null && method.DeclaringType != null)
                                {
                                    type.Methods.Add(newMethod);
                                    if (!UpdateMethodReferences(module, method, newMethod, messages))
                                        return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, messages);
                                }
                            }
                            catch (Exception e)
                            {
                                Error(messages, "SendRPCFailed: " + e.Message + "\n" + e.StackTrace, method);
                            }
                        }

                        try
                        {
                            if (_rpcMethods.Count > 0)
                                HandleRPCReceiver(module, type, _rpcMethods, inheritsFromNetworkClass, idOffset);

                            if (type.FullName != typeof(NetworkIdentity).FullName &&
                                type.FullName != typeof(NetworkModule).FullName &&
                                (inheritsFromNetworkIdentity || inheritsFromNetworkClass))
                            {
                                HandleRPCReceiverHandler(module, type, _rpcMethods, inheritsFromNetworkClass,
                                    idOffset,
                                    false);
                            }

                            if (_rpcMethods.Count > 0)
                                GenerateRPCManifestProcessor.EmitRegistration(module, type, _rpcMethods, idOffset);
                        }
                        catch (Exception e)
                        {
                            messages.Add(new DiagnosticMessage
                            {
                                DiagnosticType = DiagnosticType.Error,
                                MessageData = $"HandleRPCReceiver [{type.Name}]: {e.Message}\n{e.StackTrace}"
                            });
                        }
                    }

                    if (hasPurrNetAsReference)
                    {
                        try
                        {
                            FindUsedTypes(module, types, usedTypes);

                            foreach (var usedType in usedTypes)
                            {
                                if (IsTypeInOwnModule(usedType, module))
                                    typesToGenerateSerializer.Add(usedType);
                            }
                        }
                        catch (Exception e)
                        {
                            messages.Add(new DiagnosticMessage
                            {
                                DiagnosticType = DiagnosticType.Error,
                                MessageData = $"FindUsedTypes {e.Message}\n{e.StackTrace}"
                            });
                        }

                        try
                        {
                            ProcessReflectionRPCTargets(module, compiledAssembly, messages);
                        }
                        catch (Exception e)
                        {
                            messages.Add(new DiagnosticMessage
                            {
                                DiagnosticType = DiagnosticType.Error,
                                MessageData = $"ProcessReflectionRPCTargets: {e.Message}\n{e.StackTrace}"
                            });
                        }
                    }
                }

                ExpandNested(assemblyDefinition, typesToGenerateSerializer);

                // remove any typesToGenerateSerializer from typesToPrepareHasher
                typesToPrepareHasher.ExceptWith(typesToGenerateSerializer);

                foreach (var typeRef in typesToGenerateSerializer)
                    GenerateSerializersProcessor.HandleType(false, assemblyDefinition, typeRef, visitedTypes,
                        typesToIgnoreForSerialization, typesToIgnoreForDelta);

                foreach (var typeRef in typesToPrepareHasher)
                    GenerateSerializersProcessor.HandleType(true, assemblyDefinition, typeRef, visitedTypes,
                        typesToIgnoreForSerialization, typesToIgnoreForDelta);

                var pe = new MemoryStream();
                var pdb = new MemoryStream();

                var writerParameters = new WriterParameters
                {
                    WriteSymbols = true,
                    SymbolStream = pdb,
                    SymbolWriterProvider = new PortablePdbWriterProvider()
                };

                try
                {
                    foreach (var mod in assemblyDefinition.Modules)
                    {
                        RedirectSystemPrivateCoreLibToNetStandard(mod);
                        RemoveSelfReferences(mod);
                    }
                    assemblyDefinition.Write(pe, writerParameters);
                }
                catch (Exception e)
                {
                    messages.Add(new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData =
                            $"Failed to write assembly ({compiledAssembly.Name}): {e.Message}\n{e.StackTrace}",
                    });
                }

                return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), messages);
            }
            catch (Exception e)
            {
                var messages = new List<DiagnosticMessage>
                {
                    new()
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData = $"Unhandled exception {e.Message}\n{e.StackTrace}",
                    }
                };

                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, messages);
            }
        }

        /// <summary>
        /// Redirects System.Private.CoreLib references to netstandard/System.Runtime so Unity can load the assembly.
        /// The .NET 6 compiler adds System.Private.CoreLib for Task/async, but Unity doesn't have that assembly.
        /// </summary>
        private static void RedirectSystemPrivateCoreLibToNetStandard(ModuleDefinition module)
        {
            var coreLibRef = module.AssemblyReferences.FirstOrDefault(r => r.Name == "System.Private.CoreLib");
            if (coreLibRef == null)
                return;

            var replacementRef = module.AssemblyReferences.FirstOrDefault(r =>
                r.Name == "netstandard" || r.Name == "System.Runtime" || r.Name == "mscorlib");
            if (replacementRef == null)
                return;

            WalkModuleTypeReferences(module, typeRef =>
            {
                if (typeRef.Scope == coreLibRef)
                    typeRef.Scope = replacementRef;
            });
            module.AssemblyReferences.Remove(coreLibRef);
        }

        private static void RemoveSelfReferences(ModuleDefinition module)
        {
            var selfName = module.Assembly.Name.Name;
            var selfRefs = module.AssemblyReferences.Where(r => r.Name == selfName).ToList();
            if (selfRefs.Count == 0)
                return;

            WalkModuleTypeReferences(module, typeRef =>
            {
                if (typeRef.Scope is AssemblyNameReference anr && anr.Name == selfName)
                    typeRef.Scope = module;
            });

            foreach (var selfRef in selfRefs)
                module.AssemblyReferences.Remove(selfRef);
        }

        private static void WalkModuleTypeReferences(ModuleDefinition module, Action<TypeReference> patch)
        {
            void PatchTypeRef(TypeReference typeRef)
            {
                if (typeRef == null) return;
                if (typeRef is GenericInstanceType genType)
                {
                    PatchTypeRef(genType.ElementType);
                    foreach (var ga in genType.GenericArguments)
                        PatchTypeRef(ga);
                    return;
                }
                if (typeRef is ArrayType arrType)
                {
                    PatchTypeRef(arrType.ElementType);
                    return;
                }
                if (typeRef is ByReferenceType byRefType)
                {
                    PatchTypeRef(byRefType.ElementType);
                    return;
                }
                if (typeRef is OptionalModifierType optType)
                {
                    PatchTypeRef(optType.ElementType);
                    return;
                }
                if (typeRef is RequiredModifierType reqType)
                {
                    PatchTypeRef(reqType.ElementType);
                    return;
                }
                if (typeRef is PinnedType pinnedType)
                {
                    PatchTypeRef(pinnedType.ElementType);
                    return;
                }
                patch(typeRef);
            }

            void ProcessType(TypeDefinition type)
            {
                PatchTypeRef(type.BaseType);
                foreach (var iface in type.Interfaces)
                    PatchTypeRef(iface.InterfaceType);
                foreach (var attr in type.CustomAttributes)
                {
                    PatchTypeRef(attr.AttributeType);
                    foreach (var arg in attr.ConstructorArguments)
                        PatchTypeRef(arg.Type);
                }
                foreach (var field in type.Fields)
                {
                    PatchTypeRef(field.FieldType);
                    foreach (var attr in field.CustomAttributes)
                    {
                        PatchTypeRef(attr.AttributeType);
                        foreach (var arg in attr.ConstructorArguments)
                            PatchTypeRef(arg.Type);
                    }
                }
                foreach (var method in type.Methods)
                {
                    foreach (var attr in method.CustomAttributes)
                    {
                        PatchTypeRef(attr.AttributeType);
                        foreach (var arg in attr.ConstructorArguments)
                            PatchTypeRef(arg.Type);
                    }
                    PatchTypeRef(method.ReturnType);
                    foreach (var p in method.Parameters)
                        PatchTypeRef(p.ParameterType);
                    foreach (var gp in method.GenericParameters)
                        foreach (var c in gp.Constraints)
                            PatchTypeRef(c.ConstraintType);
                    if (method.Body != null)
                    {
                        foreach (var v in method.Body.Variables)
                            PatchTypeRef(v.VariableType);
                        foreach (var eh in method.Body.ExceptionHandlers)
                            PatchTypeRef(eh.CatchType);
                        foreach (var instr in method.Body.Instructions)
                        {
                            if (instr.Operand is TypeReference tr)
                                PatchTypeRef(tr);
                            else if (instr.Operand is MethodReference mr)
                            {
                                PatchTypeRef(mr.DeclaringType);
                                PatchTypeRef(mr.ReturnType);
                                foreach (var p in mr.Parameters)
                                    PatchTypeRef(p.ParameterType);
                                if (mr is GenericInstanceMethod genMethod)
                                    foreach (var ga in genMethod.GenericArguments)
                                        PatchTypeRef(ga);
                            }
                            else if (instr.Operand is FieldReference fr)
                                PatchTypeRef(fr.DeclaringType);
                        }
                    }
                }
                foreach (var nested in type.NestedTypes)
                    ProcessType(nested);
            }

            foreach (var type in module.Types)
                ProcessType(type);
        }

        private static bool HasPurrNetAsReference(string myName, ModuleDefinition module)
        {
            if (myName == "PurrNet.Runtime")
                return true;

            bool hasPurrNetAsReference = false;

            foreach (var reference in module.AssemblyReferences)
            {
                if (reference.Name == "PurrNet.Runtime")
                {
                    hasPurrNetAsReference = true;
                    break;
                }
            }

            return hasPurrNetAsReference;
        }

        private static void ProcessServerOnlyMethods(MethodDefinition method, PurrNetSettings settings, bool serverBuild, bool isEditor)
        {
            CustomAttribute serverOnlyAttribute = null;

            foreach (var attribute in method.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == typeof(ServerOnlyAttribute).FullName)
                {
                    serverOnlyAttribute = attribute;
                    break;
                }
            }

            if (serverOnlyAttribute == null)
                return;

            if (serverBuild && !isEditor)
                return;

            var stripCodeMode = (StripCodeModeOverride)serverOnlyAttribute.ConstructorArguments[0].Value;
            PutServerCheck(method, settings, stripCodeMode);
            if (!serverBuild)
                StripBody(method, method.Name, settings, stripCodeMode);
            FixShortFormJumps(method);
        }

        static void PutServerCheck(MethodDefinition method, PurrNetSettings settings, StripCodeModeOverride modeOverride)
        {
            var il = method.Body.GetILProcessor();
            var instructions = method.Body.Instructions;
            var module = method.Module;

            var getIsServer = GetIsServerMethod(module);
            var firstInstruction = instructions[0];
            var ogInstructionCount = instructions.Count;

            // at the end, return default value
            var mode = GetMode(settings, modeOverride);
            if (mode == StripCodeMode.DoNotStrip)
                mode = StripCodeMode.ReplaceWithEmptyMethod;
            AppendStripAction(method, method.Name, mode, il, "server only");

            var returnDefaultInst = instructions[ogInstructionCount];

            il.InsertBefore(firstInstruction, getIsServer);
            il.InsertAfter(getIsServer, Instruction.Create(OpCodes.Brfalse, returnDefaultInst));
        }

        private static Instruction GetIsServerMethod(ModuleDefinition module)
        {
            var isServerOnlyProp = module.GetTypeDefinition<NetworkManager>()
                .GetProperty("isServerStatic")
                .GetMethod.Import(module);
            var getIsServer = Instruction.Create(OpCodes.Call, isServerOnlyProp);
            return getIsServer;
        }

        private static Instruction GetIsClientMethod(ModuleDefinition module)
        {
            var isClientProp = module.GetTypeDefinition<NetworkManager>()
                .GetProperty("isClientStatic")
                .GetMethod.Import(module);
            var getIsClient = Instruction.Create(OpCodes.Call, isClientProp);
            return getIsClient;
        }

        private static void ProcessRunContextGuardedMethods(MethodDefinition method, PurrNetSettings settings, bool inheritsFromNetworkIdentity)
        {
            if (method.Body == null) return;

            bool hasServer = false;
            bool hasClient = false;
            bool hasOwner = false;

            var guardFailureActionOverride = GuardFailureActionOverride.Settings;

            foreach (var attribute in method.CustomAttributes)
            {
                string fullName = attribute.AttributeType.FullName;
                if (!hasServer && fullName == typeof(ServerAttribute).FullName)
                {
                    hasServer = true;
                }
                else if (!hasClient && fullName == typeof(ClientAttribute).FullName)
                {
                    hasClient = true;
                }
                else if (!hasOwner && fullName == typeof(OwnerAttribute).FullName)
                {
                    hasOwner = true;
                }
                else if (fullName == typeof(GuardFailureActionAttribute).FullName)
                {
                    guardFailureActionOverride = (GuardFailureActionOverride)attribute.ConstructorArguments[0].Value;
                }
            }

            if (!hasServer && !hasClient && !hasOwner)
                return;

            var guardFailureAction = GetMode(settings, guardFailureActionOverride);
            if (guardFailureAction == GuardFailureAction.Ignore) return;

            // insert guard
            var il = method.Body.GetILProcessor();
            var firstInstruction = method.Body.Instructions[0];
            var module = method.Module;

            var failureHandler = CreateGuardFailureActionHandler(method, il, guardFailureAction);

            if (hasServer)
            {
                var callIsServer = GetIsServerMethod(module);
                il.InsertBefore(firstInstruction, callIsServer);
                il.InsertAfter(callIsServer, il.Create(OpCodes.Brtrue, firstInstruction));
            }

            if (hasClient)
            {
                var callIsClient = GetIsClientMethod(module);
                il.InsertBefore(firstInstruction, callIsClient);
                il.InsertAfter(callIsClient, il.Create(OpCodes.Brtrue, firstInstruction));
            }

            if (hasOwner)
            {
                if (!inheritsFromNetworkIdentity)
                    throw new InvalidOperationException($"[Owner] requires {method.DeclaringType.Name} to inherit NetworkIdentity.");

                CheckNetworkIdentityProperty("isOwner", module, il, firstInstruction);
            }

            il.InsertBefore(firstInstruction, il.Create(OpCodes.Br, failureHandler));
        }

        private static Instruction CreateGuardFailureActionHandler(
            MethodDefinition method,
            ILProcessor il,
            GuardFailureAction action)
        {
            var handler = il.Create(OpCodes.Nop);
            il.Append(handler);

            switch (action)
            {
                case GuardFailureAction.ReturnDefault:
                    break;
                case GuardFailureAction.ThrowException:
                    var throwExcep = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("ThrowUnsupportedException", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{method.Name}' cannot be called from this context."));
                    il.Append(il.Create(OpCodes.Call, throwExcep));
                    break;
                case GuardFailureAction.LogWarning:
                    var logWarningMethod = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("LogSimplerWarning", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{method.Name}' cannot be called from this context."));
                    il.Append(il.Create(OpCodes.Call, logWarningMethod));
                    break;
                case GuardFailureAction.LogError:
                    var logErrorMethod = method.Module.GetTypeDefinition(typeof(PurrLogger))
                        .GetMethod("LogSimplerError", false).Import(method.Module);
                    il.Append(il.Create(OpCodes.Ldstr, $"Method '{method.DeclaringType.Name}.{method.Name}' cannot be called from this context."));
                    il.Append(il.Create(OpCodes.Call, logErrorMethod));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            ProperlyEndMethod(method, il);

            return handler;
        }

        private static void CheckNetworkIdentityProperty(string property, ModuleDefinition module, ILProcessor il, Instruction target)
        {
            var networkIdentityType = module.GetTypeDefinition<NetworkIdentity>().Resolve();
            var isOwnerGetter = networkIdentityType
                .GetProperty(property)
                .GetMethod.Import(module);

            var ldarg0 = il.Create(OpCodes.Ldarg_0);
            il.InsertBefore(target, ldarg0);
            il.InsertAfter(ldarg0, il.Create(OpCodes.Callvirt, isOwnerGetter));
            il.InsertAfter(ldarg0.Next, il.Create(OpCodes.Brtrue, target));
        }

        private static void IncludeAnyConcreteGenericParameters(TypeDefinition type,
            HashSet<TypeReference> typesToGenerateSerializer)
        {
            // Walk the inheritance chain. For each generic base instantiation (e.g.
            // `IdentityGenericRpcs<int>` from `IdentityGenericRpcsInt`), record the closed args
            // and also substitute them into any RPC method's parameter/return types so that
            // closed nested types like `IdentityGenericRpcs<int>.GenericPair<int>` end up in the
            // serializer set.
            var current = type.BaseType;
            while (current is GenericInstanceType genericType)
            {
                foreach (var genericParameter in genericType.GenericArguments)
                {
                    if (IsConcreteType(genericParameter, out var concreteType))
                    {
                        if (concreteType != null)
                            typesToGenerateSerializer.Add(concreteType);
                    }
                }

                AddClosedRpcMemberTypesFromOpenDef(genericType, typesToGenerateSerializer);

                TypeDefinition resolved;
                try { resolved = genericType.Resolve(); }
                catch { break; }
                current = resolved?.BaseType;
            }

            // Closed-generic NetworkModule instantiations also enter the type graph via fields
            // (e.g. `readonly ModuleGenericRpcsModule<int> intModule` on the host identity), with
            // no concrete subclass in sight. Walk fields and apply the same substitution.
            for (int i = 0; i < type.Fields.Count; i++)
            {
                var ft = type.Fields[i].FieldType;
                if (ft is GenericInstanceType git && !ft.ContainsGenericParameter)
                    AddClosedRpcMemberTypesFromOpenDef(git, typesToGenerateSerializer);
            }
        }

        // Substitute the closed generic arguments into each RPC method's parameter and return
        // types declared on the open type, so that types like `GenericPair<T>` show up as
        // `GenericPair<int>` in the serializer set whenever the open def gets closed — either by
        // a sealed subclass (`IdentityGenericRpcsInt : IdentityGenericRpcs<int>`) or by a closed
        // field type on a host identity (`ModuleGenericRpcsModule<int> intModule`).
        private static void AddClosedRpcMemberTypesFromOpenDef(GenericInstanceType closedRef,
            HashSet<TypeReference> typesToGenerateSerializer)
        {
            TypeDefinition openDef;
            try { openDef = closedRef.Resolve(); }
            catch { return; }
            if (openDef == null) return;

            int argCount = Math.Min(openDef.GenericParameters.Count, closedRef.GenericArguments.Count);
            if (argCount == 0) return;

            var mapping = new Dictionary<string, TypeReference>(argCount);
            for (int i = 0; i < argCount; i++)
            {
                var arg = closedRef.GenericArguments[i];
                if (arg.ContainsGenericParameter) return;
                mapping[openDef.GenericParameters[i].Name] = arg;
            }

            for (int m = 0; m < openDef.Methods.Count; m++)
            {
                var method = openDef.Methods[m];
                if (!HasRpcAttribute(method)) continue;

                foreach (var param in method.Parameters)
                {
                    var pt = param.ParameterType;
                    if (!pt.ContainsGenericParameter) continue;
                    var closed = SubstituteGenericParameters(pt, mapping);
                    if (closed == null || closed.ContainsGenericParameter) continue;
                    typesToGenerateSerializer.Add(closed);
                }

                var rt = method.ReturnType;
                if (rt != null && rt.ContainsGenericParameter)
                {
                    var closedRt = SubstituteGenericParameters(rt, mapping);
                    if (closedRt != null && !closedRt.ContainsGenericParameter)
                    {
                        if (IsConcreteType(closedRt, out var concreteRt) && concreteRt != null)
                            typesToGenerateSerializer.Add(concreteRt);
                    }
                }
            }
        }

        private static bool HasRpcAttribute(MethodDefinition method)
        {
            if (!method.HasCustomAttributes) return false;
            foreach (var attr in method.CustomAttributes)
            {
                var fn = attr.AttributeType.FullName;
                if (fn == typeof(ServerRpcAttribute).FullName ||
                    fn == typeof(TargetRpcAttribute).FullName ||
                    fn == typeof(ObserversRpcAttribute).FullName)
                    return true;
            }
            return false;
        }

        private static void FindNetworkModules(TypeDefinition type, string classFullName,
            List<FieldDefinition> _networkFields)
        {
            for (var i = 0; i < type.Fields.Count; i++)
            {
                var field = type.Fields[i];

                if (field.IsStatic) continue;

                var fieldType = field.FieldType.Resolve();

                if (fieldType == null) continue;

                var isNetworkClass = fieldType.FullName == classFullName || InheritsFrom(fieldType, classFullName);

                if (!isNetworkClass) continue;

                _networkFields.Add(field);
            }
        }

        private static void ExpandNested(AssemblyDefinition assembly, HashSet<TypeReference> typesToHandle)
        {
            HashSet<TypeReference> visited = new HashSet<TypeReference>(TypeReferenceEqualityComparer.Default);
            HashSet<TypeReference> visited2 = new HashSet<TypeReference>(TypeReferenceEqualityComparer.Default);
            var copy = typesToHandle.ToArray();

            for (var i = 0; i < copy.Length; i++)
            {
                var type = copy[i];
                if (type is GenericInstanceType genericInstance)
                    AddNestedGenerics(assembly, genericInstance, typesToHandle, visited2);
                AddNestedFields(assembly, type, typesToHandle, visited);
            }
        }

        private static void AddNestedGenerics(AssemblyDefinition assembly, GenericInstanceType type,
            HashSet<TypeReference> typesToHandle, HashSet<TypeReference> visited)
        {
            for (int i = 0; i < type.GenericArguments.Count; i++)
            {
                var argument = type.GenericArguments[i];

                if (!visited.Add(argument))
                    continue;

                if (argument is GenericInstanceType genericInstance)
                {
                    AddNestedGenerics(assembly, genericInstance, typesToHandle, visited);
                }
                else if (IsTypeInOwnModule(argument, assembly.MainModule))
                {
                    typesToHandle.Add(argument);
                }
            }
        }

        private static void AddNestedFields(AssemblyDefinition assembly, TypeReference reference,
            HashSet<TypeReference> typesToHandle, HashSet<TypeReference> visited)
        {
            var fields = GetConcreteFields(reference);

            foreach (var field in fields)
            {
                if (!visited.Add(field))
                    continue;

                if (field is GenericInstanceType genericInstance)
                {
                    bool containsRelevantTypes = false;

                    // Add the GenericInstanceType itself if fully concrete
                    if (genericInstance.GenericArguments.All(IsResolved))
                    {
                        typesToHandle.Add(field);
                        containsRelevantTypes = true;
                    }

                    // Check the generic arguments
                    for (int i = 0; i < genericInstance.GenericArguments.Count; i++)
                    {
                        var argument = genericInstance.GenericArguments[i];
                        var resolvedArg = argument.Resolve();

                        if (resolvedArg != null)
                        {
                            typesToHandle.Add(argument);
                            containsRelevantTypes = true;
                        }

                        AddNestedFields(assembly, argument, typesToHandle, visited);
                    }

                    // If the GenericInstanceType contains relevant arguments, add it
                    if (containsRelevantTypes)
                    {
                        typesToHandle.Add(field);
                    }

                    AddNestedFields(assembly, field, typesToHandle, visited);
                }
                else if (IsTypeInOwnModule(field, assembly.MainModule))
                {
                    // Handle non-generic field types
                    typesToHandle.Add(field);
                    AddNestedFields(assembly, field, typesToHandle, visited);
                }
            }
        }

        static List<TypeReference> GetConcreteFields(TypeReference typeReference)
        {
            List<TypeReference> concreteFields = new List<TypeReference>();

            if (typeReference is GenericInstanceType genericInstance)
            {
                // Resolve the type definition
                TypeDefinition typeDef = typeReference.Resolve();
                if (typeDef == null)
                {
                    throw new InvalidOperationException($"Could not resolve type: {typeReference.FullName}");
                }

                // Map generic parameters to concrete arguments
                Dictionary<string, TypeReference> genericMapping = new Dictionary<string, TypeReference>();
                for (int i = 0; i < genericInstance.GenericArguments.Count; i++)
                {
                    string paramName = typeDef.GenericParameters[i].Name;
                    TypeReference concreteType = genericInstance.GenericArguments[i];
                    genericMapping[paramName] = concreteType;
                }

                // Process each field
                foreach (var field in typeDef.Fields)
                {
                    TypeReference fieldType = field.FieldType;

                    // Substitute generic parameters with concrete arguments
                    TypeReference concreteFieldType = SubstituteGenericParameters(fieldType, genericMapping);
                    concreteFields.Add(concreteFieldType);
                }
            }
            else
            {
                TypeDefinition typeDef = typeReference.Resolve();

                if (typeDef != null)
                {
                    foreach (var field in typeDef.Fields)
                        concreteFields.Add(field.FieldType);
                }
            }

            return concreteFields;
        }

        static TypeReference SubstituteGenericParameters(TypeReference type,
            Dictionary<string, TypeReference> genericMapping)
        {
            if (type is GenericInstanceType genericInstance)
            {
                // Substitute each generic argument recursively
                GenericInstanceType concreteGenericInstance = new GenericInstanceType(genericInstance.ElementType);
                foreach (var argument in genericInstance.GenericArguments)
                {
                    concreteGenericInstance.GenericArguments.Add(SubstituteGenericParameters(argument, genericMapping));
                }

                return concreteGenericInstance;
            }
            else if (type is GenericParameter genericParameter)
            {
                // Replace the generic parameter with its mapped concrete type
                if (genericMapping.TryGetValue(genericParameter.Name, out var concreteType))
                {
                    return concreteType;
                }
            }

            // Return the type as is if no substitution is needed
            return type;
        }

        private static bool IsResolved(TypeReference type)
        {
            return type.Resolve() != null;
        }

        private static void CreateSyncVarInitMethod(bool isNetworkIdentity, ModuleDefinition module,
            TypeDefinition type, List<FieldDefinition> networkFields)
        {
            var methodName = GenerateSerializersProcessor.MakeFullNameValidCSharp(type.Name);
            var newMethod = new MethodDefinition($"__{methodName}_CodeGen_Initialize",
                MethodAttributes.Public | MethodAttributes.HideBySig, module.TypeSystem.Void);

            var preserveAttribute = module.GetTypeDefinition<PreserveAttribute>();
            var constructor = preserveAttribute.Resolve().Methods.First(m => m.IsConstructor && !m.HasParameters)
                .Import(module);
            newMethod.CustomAttributes.Add(new CustomAttribute(constructor));

            var parentStr = new ParameterDefinition("parent", ParameterAttributes.None, module.TypeSystem.String);

            if (!isNetworkIdentity)
                newMethod.Parameters.Add(parentStr);

            type.Methods.Add(newMethod);

            newMethod.Body.InitLocals = true;

            var code = newMethod.Body.GetILProcessor();

            // For generic types, field references must go through a GenericInstanceType
            // so that the runtime sees them as belonging to the same type context as the method.
            // Without this, private fields cause FieldAccessException.
            TypeReference selfType = type;
            if (type.HasGenericParameters)
            {
                var git = new GenericInstanceType(type);
                foreach (var gp in type.GenericParameters)
                    git.GenericArguments.Add(gp);
                selfType = git;
            }

            var parentType =
                (isNetworkIdentity
                    ? module.GetTypeDefinition<NetworkIdentity>()
                    : module.GetTypeDefinition<NetworkModule>()).Import(module);

            var registerModule = parentType.GetMethod("RegisterModuleInternal").Import(module);
            var concatMethod = module.TypeSystem.String.Resolve()
                .GetMethod("Concat", module.TypeSystem.String, module.TypeSystem.String).Import(module);

            // Chain to base type's init so base class's network fields get registered too.
            // NetworkIdentity uses reflection to discover inherited inits, so only chain for NetworkModule.
            if (!isNetworkIdentity && type.BaseType != null &&
                type.FullName != typeof(NetworkModule).FullName)
            {
                var baseTypeRef = type.BaseType.Import(module);
                var baseInitName =
                    $"__{GenerateSerializersProcessor.MakeFullNameValidCSharp(baseTypeRef.Name)}_CodeGen_Initialize";
                var baseInitRef = new MethodReference(baseInitName, module.TypeSystem.Void, baseTypeRef)
                {
                    HasThis = true
                };
                baseInitRef.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldarg_1));
                code.Append(Instruction.Create(OpCodes.Call, baseInitRef));
            }

            for (int i = 0; i < networkFields.Count; i++)
            {
                var field = networkFields[i];

                // add the Preserve attribute to field
                if (field.CustomAttributes.All(x => x.AttributeType.FullName != typeof(PreserveAttribute).FullName))
                    type.CustomAttributes.Add(new CustomAttribute(constructor));

                // For generic types, construct a FieldReference through the GenericInstanceType
                FieldReference fieldRef = type.HasGenericParameters
                    ? new FieldReference(field.Name, field.FieldType, selfType)
                    : field;

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldstr, field.Name));
                code.Append(Instruction.Create(OpCodes.Ldstr, field.FieldType.Name));
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldfld, fieldRef));
                code.Append(Instruction.Create(OpCodes.Ldc_I4, isNetworkIdentity ? 1 : 0));
                code.Append(Instruction.Create(OpCodes.Call, registerModule));

                var endInstruction = Instruction.Create(OpCodes.Nop);

                // if not null
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldfld, fieldRef));
                code.Append(Instruction.Create(OpCodes.Brfalse, endInstruction));

                // call init method
                var initMethodName =
                    $"__{GenerateSerializersProcessor.MakeFullNameValidCSharp(field.FieldType.Name)}_CodeGen_Initialize";
                var codeGenInitRef = new MethodReference(initMethodName, module.TypeSystem.Void, field.FieldType)
                {
                    HasThis = true
                };

                codeGenInitRef.Parameters.Add(parentStr);

                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                code.Append(Instruction.Create(OpCodes.Ldfld, fieldRef));
                if (isNetworkIdentity)
                {
                    code.Append(Instruction.Create(OpCodes.Ldstr, field.Name));
                }
                else
                {
                    code.Append(Instruction.Create(OpCodes.Ldarg_1));
                    code.Append(Instruction.Create(OpCodes.Ldstr, '.' + field.Name));
                    code.Append(Instruction.Create(OpCodes.Call, concatMethod));
                }

                code.Append(Instruction.Create(OpCodes.Call, codeGenInitRef));
                code.Append(endInstruction);

                if (!isNetworkIdentity)
                {
                    // if null
                    var endInstruction2 = Instruction.Create(OpCodes.Nop);
                    code.Append(Instruction.Create(OpCodes.Ldarg_0));
                    code.Append(Instruction.Create(OpCodes.Ldfld, fieldRef));
                    code.Append(Instruction.Create(OpCodes.Brtrue, endInstruction2));

                    // call error
                    var errorMethod = module.GetTypeDefinition<NetworkModule>().GetMethod("Error").Import(module);

                    code.Append(Instruction.Create(OpCodes.Ldarg_0));
                    code.Append(Instruction.Create(OpCodes.Ldarg_1));
                    code.Append(Instruction.Create(OpCodes.Ldstr, '.' + field.Name));
                    code.Append(Instruction.Create(OpCodes.Call, concatMethod));
                    code.Append(Instruction.Create(OpCodes.Call, errorMethod));

                    code.Append(endInstruction2);
                }
            }

            code.Append(Instruction.Create(OpCodes.Ret));

            if (isNetworkIdentity && type.FullName != typeof(NetworkIdentity).FullName)
                CreateInitDispatchOverride(module, type, newMethod);
        }

        private static void CreateInitDispatchOverride(ModuleDefinition module, TypeDefinition type,
            MethodDefinition initMethod)
        {
            const string DISPATCH_NAME = "CallGeneratedInitMethods";

            var dispatch = new MethodDefinition(DISPATCH_NAME,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual |
                MethodAttributes.ReuseSlot, module.TypeSystem.Void)
            {
                HasThis = true
            };

            type.Methods.Add(dispatch);

            var body = dispatch.Body.GetILProcessor();

            if (type.BaseType != null)
            {
                var baseRef = new MethodReference(DISPATCH_NAME, module.TypeSystem.Void, type.BaseType.Import(module))
                {
                    HasThis = true
                };

                body.Append(Instruction.Create(OpCodes.Ldarg_0));
                body.Append(Instruction.Create(OpCodes.Call, baseRef));

                dispatch.Overrides.Add(baseRef);
            }

            MethodReference selfInit = initMethod;

            if (type.HasGenericParameters)
            {
                var git = new GenericInstanceType(type);
                foreach (var gp in type.GenericParameters)
                    git.GenericArguments.Add(gp);

                selfInit = new MethodReference(initMethod.Name, module.TypeSystem.Void, git)
                {
                    HasThis = true
                };
            }

            body.Append(Instruction.Create(OpCodes.Ldarg_0));
            body.Append(Instruction.Create(OpCodes.Call, selfInit));
            body.Append(Instruction.Create(OpCodes.Ret));
        }

        private static void FindUsedTypes(ModuleDefinition module, DisposableList<TypeDefinition> allTypes,
            HashSet<TypeReference> types)
        {
            var playersBroadcasterSubscribe = module.GetTypeDefinition<PlayersBroadcaster>();
            var playersManagerSubscribe = module.GetTypeDefinition<PlayersManager>();
            var broadcastModuleSubscribe = module.GetTypeDefinition<BroadcastModule>();
            var networkModule = module.GetTypeDefinition<NetworkModule>();

            for (int i = 0; i < allTypes.Count; i++)
            {
                var type = allTypes[i];

                foreach (var field in type.Fields)
                {
                    var resolved = field.FieldType.Resolve();
                    if (resolved == null) continue;

                    AddAnySyncVarOrGenericNetworkModulesType(types, field.FieldType, resolved, networkModule);
                }

                for (int j = 0; j < type.Methods.Count; j++)
                {
                    var method = type.Methods[j];

                    if (method.Body == null) continue;

                    var body = method.Body;

                    for (int k = 0; k < body.Instructions.Count; k++)
                    {
                        var instruction = body.Instructions[k];

                        if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                            continue;

                        if (instruction.Operand is MethodReference normalMethod)
                        {
                            if (IsRpcMethod(normalMethod) && normalMethod.DeclaringType is GenericInstanceType genericInstanceType)
                            {
                                if (IsConcreteType(genericInstanceType, out var gconcreteType))
                                    types.Add(gconcreteType);

                                for (var index = 0; index < genericInstanceType.GenericArguments.Count; index++)
                                {
                                    var ga = genericInstanceType.GenericArguments[index];
                                    if (IsConcreteType(ga, out var concreteType))
                                        types.Add(concreteType);
                                }
                            }
                        }

                        if (instruction.Operand is GenericInstanceMethod currentMethod)
                        {
                            if (IsRpcMethod(currentMethod))
                                FindUsedGenericRpcTypes(types, currentMethod);

                            var isSubscribeMethod =
                                currentMethod.GenericArguments.Count == 1 && currentMethod.Name.Equals("Subscribe") &&
                                (currentMethod.DeclaringType.FullName == playersManagerSubscribe.FullName ||
                                 currentMethod.DeclaringType.FullName == broadcastModuleSubscribe.FullName ||
                                 currentMethod.DeclaringType.FullName == playersBroadcasterSubscribe.FullName);

                            if (isSubscribeMethod &&
                                IsConcreteType(currentMethod.GenericArguments[0], out var concreteType))
                                types.Add(concreteType);
                        }
                    }
                }
            }
        }

        private static void AddAnySyncVarOrGenericNetworkModulesType(HashSet<TypeReference> types, TypeReference type,
            TypeDefinition resolved,
            TypeDefinition networkModule)
        {
            if (networkModule == null)
                return;

            bool inheritsNetworkModule = InheritsFrom(resolved, networkModule.FullName);
            if (inheritsNetworkModule && type is GenericInstanceType genericInstance)
            {
                bool allConcrete = true;

                if (genericInstance.GenericArguments == null)
                    return;

                foreach (var genericArg in genericInstance.GenericArguments)
                {
                    if (genericArg == null)
                        continue;

                    if (IsConcreteType(genericArg, out var concreteType))
                    {
                        types.Add(concreteType);
                    }
                    else allConcrete = false;
                }

                if (allConcrete)
                    types.Add(type);
            }
        }

        private static void FindUsedGenericRpcTypes(HashSet<TypeReference> types, GenericInstanceMethod currentMethod)
        {
            foreach (var argument in currentMethod.GenericArguments)
            {
                if (!argument.IsGenericParameter)
                    types.Add(argument);
            }

            // Substitute the call's generic arguments into each parameter (and the return type)
            // to capture closed types like GenericPair<int> when calling Echo_GenericPair<int>(p).
            // Without this, the send path's `Packer<GenericPair<int>>.Write` falls back to
            // the runtime hasher, which throws because no serializer was registered.
            MethodDefinition resolved;
            try
            {
                resolved = currentMethod.Resolve();
            }
            catch
            {
                return;
            }

            if (resolved == null)
                return;

            int genericCount = Math.Min(resolved.GenericParameters.Count, currentMethod.GenericArguments.Count);
            if (genericCount == 0)
                return;

            var mapping = new Dictionary<string, TypeReference>(genericCount);
            for (int i = 0; i < genericCount; i++)
            {
                var arg = currentMethod.GenericArguments[i];
                if (arg.ContainsGenericParameter)
                    return;
                mapping[resolved.GenericParameters[i].Name] = arg;
            }

            for (int i = 0; i < resolved.Parameters.Count; i++)
            {
                var paramType = resolved.Parameters[i].ParameterType;
                if (!paramType.ContainsGenericParameter)
                    continue;

                var closed = SubstituteGenericParameters(paramType, mapping);
                if (closed == null || closed.ContainsGenericParameter)
                    continue;

                types.Add(closed);
            }

            var returnType = resolved.ReturnType;
            if (returnType != null && returnType.ContainsGenericParameter)
            {
                var closedReturn = SubstituteGenericParameters(returnType, mapping);
                if (closedReturn != null && !closedReturn.ContainsGenericParameter)
                {
                    if (IsConcreteType(closedReturn, out var concreteReturn) && concreteReturn != null)
                        types.Add(concreteReturn);
                }
            }
        }

        private static bool IsRpcMethod(MethodReference currentMethod)
        {
            try
            {
                var resolved = currentMethod.Resolve();

                if (resolved == null)
                    return false;

                foreach (var attribute in resolved.CustomAttributes)
                {
                    if (attribute.AttributeType.FullName == typeof(ServerRpcAttribute).FullName ||
                        attribute.AttributeType.FullName == typeof(TargetRpcAttribute).FullName ||
                        attribute.AttributeType.FullName == typeof(ObserversRpcAttribute).FullName)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsTypeInOwnModule(TypeReference typeReference, ModuleDefinition ownModule)
        {
            if (IsGeneric(typeReference, typeof(Dictionary<,>)))
                return true;

            if (IsGeneric(typeReference, typeof(HashSet<>)))
                return true;

            if (IsGeneric(typeReference, typeof(List<>)))
                return true;

            if (IsGeneric(typeReference, typeof(DisposableList<>)))
                return true;

            if (IsGeneric(typeReference, typeof(DisposableArray<>)))
                return true;

            if (IsGeneric(typeReference, typeof(DisposableHashSet<>)))
                return true;

            if (IsGeneric(typeReference, typeof(DisposableDictionary<,>)))
                return true;

            if (IsGeneric(typeReference, typeof(Queue<>)))
                return true;

            if (IsGeneric(typeReference, typeof(Stack<>)))
                return true;

            if (IsGeneric(typeReference, typeof(Nullable<>)))
                return true;

            if (typeReference is GenericInstanceType unityCollection &&
                (unityCollection.ElementType.FullName == "Unity.Collections.NativeArray`1" ||
                 unityCollection.ElementType.FullName == "Unity.Collections.NativeList`1"))
                return true;

            if (typeReference is ArrayType)
                return true;

            // Check if the type's module matches our own module
            if (typeReference.Module != ownModule)
                return false;

            // Check if the type is primitive or belongs to the core library (e.g., System, mscorlib)
            if (typeReference.IsPrimitive || typeReference.Scope.Name == "mscorlib" ||
                typeReference.Scope.Name == "System.Private.CoreLib")
                return false;

            // Check if the type is an external reference by comparing the assembly name
            if (typeReference.Scope is AssemblyNameReference assemblyRef &&
                assemblyRef.Name != ownModule.Assembly.Name.Name)
                return false;

            return true;
        }

        private static string ReadCachePathFromSettings(string projectRoot)
        {
            const string defaultPath = "Assets/PurrNet/ReflectionRPCTargets.txt";
            const string settingsFile = "ProjectSettings/PurrNetSettings.asset";

            var settingsPath = string.IsNullOrEmpty(projectRoot)
                ? settingsFile
                : Path.Combine(projectRoot, settingsFile);

            if (!File.Exists(settingsPath))
                return defaultPath;

            try
            {
                var json = File.ReadAllText(settingsPath);
                const string key = "\"reflectionCachePath\"";
                var idx = json.IndexOf(key, StringComparison.Ordinal);
                if (idx < 0)
                    return defaultPath;

                var colonIdx = json.IndexOf(':', idx + key.Length);
                if (colonIdx < 0)
                    return defaultPath;

                var quoteStart = json.IndexOf('"', colonIdx + 1);
                if (quoteStart < 0)
                    return defaultPath;

                var quoteEnd = json.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0)
                    return defaultPath;

                var value = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                return string.IsNullOrEmpty(value) ? defaultPath : value;
            }
            catch
            {
                return defaultPath;
            }
        }

        private static string FindReflectionTargetsCache(ICompiledAssembly compiledAssembly)
        {
            var relativePath = ReadCachePathFromSettings(null);

            if (File.Exists(relativePath))
                return relativePath;

            foreach (var reference in compiledAssembly.References)
            {
                var normalized = reference.Replace('\\', '/');
                var idx = normalized.IndexOf("Library/", StringComparison.Ordinal);
                if (idx <= 0)
                    continue;

                var root = reference.Substring(0, idx);
                var cachePath = ReadCachePathFromSettings(root);
                var fullPath = Path.Combine(root, cachePath);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return null;
        }

        private static void ProcessReflectionRPCTargets(ModuleDefinition module,
            ICompiledAssembly compiledAssembly, List<DiagnosticMessage> messages)
        {
            var processedTypes = new HashSet<string>();

            var cacheFile = FindReflectionTargetsCache(compiledAssembly);
            if (cacheFile != null)
            {
                var lines = File.ReadAllLines(cacheFile);
                foreach (var line in lines)
                {
                    var typeName = line.Trim();
                    if (string.IsNullOrEmpty(typeName))
                        continue;

                    var targetType = module.GetType(typeName);
                    if (targetType == null)
                        continue;

                    if (!processedTypes.Add(targetType.FullName))
                        continue;

                    try
                    {
                        ProcessReflectionRPCType(module, targetType, messages);
                    }
                    catch (Exception e)
                    {
                        messages.Add(new DiagnosticMessage
                        {
                            DiagnosticType = DiagnosticType.Error,
                            MessageData = $"ReflectionRPC [{targetType.Name}]: {e.Message}\n{e.StackTrace}"
                        });
                    }
                }
            }

            var attrFullName = typeof(ReflectionRPCTargetAttribute).FullName;

            foreach (var attr in module.Assembly.CustomAttributes)
            {
                if (attr.AttributeType.FullName != attrFullName)
                    continue;

                if (attr.ConstructorArguments.Count != 1)
                    continue;

                var targetTypeRef = attr.ConstructorArguments[0].Value as TypeReference;
                if (targetTypeRef == null)
                    continue;

                var targetType = targetTypeRef.Resolve();
                if (targetType == null)
                    continue;

                if (targetType.Module != module)
                    continue;

                if (!processedTypes.Add(targetType.FullName))
                    continue;

                try
                {
                    ProcessReflectionRPCType(module, targetType, messages);
                }
                catch (Exception e)
                {
                    messages.Add(new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData = $"ReflectionRPC [{targetType.Name}]: {e.Message}\n{e.StackTrace}"
                    });
                }
            }
        }

        private static void ProcessReflectionRPCType(ModuleDefinition module, TypeDefinition targetType,
            List<DiagnosticMessage> messages)
        {
            var networkReflectionTypeDef = module.GetTypeDefinition<NetworkReflection>();
            var reflectionFieldRef = module.ImportReference(
                new FieldReference("__purrnet_reflection", networkReflectionTypeDef.Import(module), targetType));

            bool fieldAdded = false;
            for (int i = 0; i < targetType.Fields.Count; i++)
            {
                if (targetType.Fields[i].Name == "__purrnet_reflection")
                {
                    fieldAdded = true;
                    reflectionFieldRef = targetType.Fields[i].Import(module);
                    break;
                }
            }

            if (!fieldAdded)
            {
                var field = new FieldDefinition("__purrnet_reflection",
                    FieldAttributes.Public | FieldAttributes.NotSerialized,
                    networkReflectionTypeDef.Import(module));
                targetType.Fields.Add(field);
                reflectionFieldRef = field.Import(module);
            }

            var bypassFieldRef = networkReflectionTypeDef.GetField("__bypassMethodDispatch").Import(module);
            var tryDispatchRef = networkReflectionTypeDef.GetMethod("TryDispatchMethod").Import(module);
            var objectTypeRef = module.ImportReference(module.TypeSystem.Object);

            for (int i = 0; i < targetType.Methods.Count; i++)
            {
                var method = targetType.Methods[i];

                if (method.IsStatic) continue;
                if (method.IsConstructor) continue;
                if (method.IsAbstract) continue;
                if (!method.HasBody) continue;
                if (method.ReturnType.FullName != module.TypeSystem.Void.FullName) continue;
                if (method.IsGetter || method.IsSetter) continue;
                if (method.HasGenericParameters) continue;

                try
                {
                    InjectReflectionRPCDispatch(module, method, reflectionFieldRef, bypassFieldRef,
                        tryDispatchRef, objectTypeRef);
                }
                catch (Exception e)
                {
                    messages.Add(new DiagnosticMessage
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData =
                            $"ReflectionRPC inject [{targetType.Name}.{method.Name}]: {e.Message}\n{e.StackTrace}"
                    });
                }
            }
        }

        private static void InjectReflectionRPCDispatch(ModuleDefinition module, MethodDefinition method,
            FieldReference reflectionFieldRef, FieldReference bypassFieldRef,
            MethodReference tryDispatchRef, TypeReference objectTypeRef)
        {
            var body = method.Body;
            var il = body.GetILProcessor();
            var originalFirst = body.Instructions[0];

            var objectArrayType = new ArrayType(objectTypeRef);
            var reflLocal = new VariableDefinition(reflectionFieldRef.FieldType);
            var argsLocal = new VariableDefinition(objectArrayType);
            body.Variables.Add(reflLocal);
            body.Variables.Add(argsLocal);

            var paramCount = method.Parameters.Count;

            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldsfld, bypassFieldRef));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Brtrue, originalFirst));

            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldarg_0));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldfld, reflectionFieldRef));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Stloc, reflLocal));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldloc, reflLocal));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Brfalse, originalFirst));

            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldc_I4, paramCount));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Newarr, objectTypeRef));

            for (int i = 0; i < paramCount; i++)
            {
                il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Dup));
                il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldc_I4, i));
                il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldarg, method.Parameters[i]));

                var paramType = method.Parameters[i].ParameterType;
                if (paramType.IsValueType || paramType.IsGenericParameter)
                    il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Box, module.ImportReference(paramType)));

                il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Stelem_Ref));
            }

            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Stloc, argsLocal));

            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldloc, reflLocal));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldstr, method.Name));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ldloc, argsLocal));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Callvirt, tryDispatchRef));
            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Brfalse, originalFirst));

            il.InsertBefore(originalFirst, Instruction.Create(OpCodes.Ret));
        }
    }
}
#endif
