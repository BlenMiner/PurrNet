#if UNITY_MONO_CECIL
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PurrNet.Packets;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace PurrNet.Codegen
{
    [UsedImplicitly]
    public class PostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            var isEditorDll = compiledAssembly.Name.EndsWith(".Editor");
            return !isEditorDll;
        }
        
        private static int GetIDOffset(TypeDefinition type)
        {
            var baseType = type.BaseType?.Resolve();
            
            if (baseType == null)
                return 0;
            
            return GetIDOffset(baseType) + baseType.Methods.Count(m => HasCustomAttribute(m, typeof(ServerRPCAttribute).FullName));
        }
        
        private static bool InheritsFrom(TypeDefinition type, string baseTypeName)
        {
            if (type.BaseType == null)
                return false;

            if (type.BaseType.FullName == baseTypeName)
            {
                return true;
            }
    
            var btype = type.BaseType.Resolve();
            return btype != null && InheritsFrom(btype, baseTypeName);
        }
        
        private static bool HasCustomAttribute(MethodDefinition method, string attributeName)
        {
            try
            {
                foreach (var attribute in method.CustomAttributes)
                {
                    if (attribute.AttributeType.FullName == attributeName)
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
        
        private static void Error(ICollection<DiagnosticMessage> messages, string message, MethodDefinition method)
        {
            if (method.DebugInformation.HasSequencePoints)
            {
                var first = method.DebugInformation.SequencePoints[0];
                messages.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Error,
                    MessageData = message,
                    Column = first.StartColumn,
                    Line = first.StartLine,
                    File = first.Document.Url
                });
            }
        }

        private static void HandleRPCReceiver(ModuleDefinition module, TypeDefinition type, IReadOnlyList<MethodDefinition> originalRpcs, int offset)
        {
            for (var i = 0; i < originalRpcs.Count; i++)
            {
                var newMethod = new MethodDefinition($"HandleRPCGenerated_{offset + i}",
                    MethodAttributes.Public | MethodAttributes.HideBySig,
                    module.TypeSystem.Void);

                var streamType = module.GetTypeReference<NetworkStream>();
                var packetType = module.GetTypeReference<RPCPacket>();

                var stream = new ParameterDefinition("stream", ParameterAttributes.None, streamType);
                var packet = new ParameterDefinition("packet", ParameterAttributes.None, packetType);
                
                newMethod.Parameters.Add(stream);
                newMethod.Parameters.Add(packet);

                newMethod.Body.InitLocals = true;
                
                var code = newMethod.Body.GetILProcessor();
                var getId = packetType.GetMethod(RPCPacket.GET_ID_METHOD);
                
                if (getId == null)
                    throw new Exception("Failed to resolve GetID method.");

                var rpc = originalRpcs[i];

                var serializeMethod = streamType.GetMethod("Serialize", true).Import(module);

                foreach (var param in rpc.Parameters)
                {
                    var variable = new VariableDefinition(param.ParameterType);
                    newMethod.Body.Variables.Add(variable);
                    
                    var serialize = new GenericInstanceMethod(serializeMethod);
                    serialize.GenericArguments.Add(param.ParameterType);
                    
                    code.Append(Instruction.Create(OpCodes.Ldarga, stream));
                    code.Append(Instruction.Create(OpCodes.Ldloca, variable));
                    code.Append(Instruction.Create(OpCodes.Call, serialize));
                }
                

                // load this
                code.Append(Instruction.Create(OpCodes.Ldarg_0));
                
                var vars = newMethod.Body.Variables;

                for (var j = 0; j < vars.Count; j++)
                    code.Append(Instruction.Create(OpCodes.Ldloc, vars[j]));
                
                code.Append(Instruction.Create(OpCodes.Call, rpc));
                code.Append(Instruction.Create(OpCodes.Ret));
                
                type.Methods.Add(newMethod);
            }
        }

        private MethodDefinition HandleRPC(int id, MethodDefinition method, [UsedImplicitly] List<DiagnosticMessage> messages)
        {
            if (method.ReturnType.FullName != typeof(void).FullName)
            {
                Error(messages, "ServerRPC method must return void", method);
                return null;
            }
            
            string ogName = method.Name;
            method.Name = ogName + "_Original";
            
            var newMethod = new MethodDefinition(ogName, MethodAttributes.Public | MethodAttributes.HideBySig, method.ReturnType);
            
            foreach (var param in method.CustomAttributes)
                newMethod.CustomAttributes.Add(param);
            
            method.CustomAttributes.Clear();
            
            foreach (var param in method.Parameters)
                newMethod.Parameters.Add(new ParameterDefinition(param.Name, param.Attributes, param.ParameterType));
            
            var code = newMethod.Body.GetILProcessor();
            
            var module = method.Module;
            var streamType = method.Module.GetTypeReference<NetworkStream>();
            var rpcType = method.Module.GetTypeReference<RPCModule>();
            var identityType = method.Module.GetTypeReference<NetworkIdentity>();
            var packetType = method.Module.GetTypeReference<RPCPacket>();
            
            var allocStreamMethod = rpcType.GetMethod("AllocStream").Import(module);
            var serializeMethod = streamType.GetMethod("Serialize", true).Import(module);
            var buildRawRPCMethod = rpcType.GetMethod("BuildRawRPC").Import(module);
            var freeStreamMethod = rpcType.GetMethod("FreeStream").Import(module);
            var sendToServerMethod = identityType.GetMethod("SendToServer", true).Import(module);
            var getId = identityType.GetProperty("id");
            var getSceneId = identityType.GetProperty("sceneId");
            
            // Declare local variables
            newMethod.Body.InitLocals = true;
            
            var streamVariable = new VariableDefinition(streamType);
            var rpcDataVariable = new VariableDefinition(packetType);
            
            newMethod.Body.Variables.Add(streamVariable);
            newMethod.Body.Variables.Add(rpcDataVariable);
            
            code.Append(Instruction.Create(OpCodes.Ldc_I4, 0));
            code.Append(Instruction.Create(OpCodes.Call, allocStreamMethod));
            code.Append(Instruction.Create(OpCodes.Stloc, streamVariable));

            for (var i = 0; i < newMethod.Parameters.Count; i++)
            {
                var serializeGenericMethod = new GenericInstanceMethod(serializeMethod);
                serializeGenericMethod.GenericArguments.Add(newMethod.Parameters[i].ParameterType);
                
                code.Append(Instruction.Create(OpCodes.Ldloca, streamVariable));
                code.Append(Instruction.Create(OpCodes.Ldarga, newMethod.Parameters[i]));
                code.Append(Instruction.Create(OpCodes.Call, serializeGenericMethod));
            }

            code.Append(Instruction.Create(OpCodes.Ldarg_0));
            code.Append(Instruction.Create(OpCodes.Call, getId.GetMethod.Import(module))); // id
            code.Append(Instruction.Create(OpCodes.Ldarg_0));
            code.Append(Instruction.Create(OpCodes.Call, getSceneId.GetMethod.Import(module))); // sceneId
            code.Append(Instruction.Create(OpCodes.Ldc_I4, id)); // rpcId
            code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable)); // stream
            code.Append(Instruction.Create(OpCodes.Call, buildRawRPCMethod));
            code.Append(Instruction.Create(OpCodes.Stloc, rpcDataVariable)); // rpcData

            var sendToServerMethodGeneric = new GenericInstanceMethod(sendToServerMethod);
            sendToServerMethodGeneric.GenericArguments.Add(packetType);

            code.Append(Instruction.Create(OpCodes.Ldarg_0)); // this
            code.Append(Instruction.Create(OpCodes.Ldloc, rpcDataVariable)); // rpcData
            code.Append(Instruction.Create(OpCodes.Ldc_I4, 2)); // default channel
            code.Append(Instruction.Create(OpCodes.Call, sendToServerMethodGeneric));

            code.Append(Instruction.Create(OpCodes.Ldloc, streamVariable));
            code.Append(Instruction.Create(OpCodes.Call, freeStreamMethod));

            code.Append(Instruction.Create(OpCodes.Ret));

            return newMethod;
        }
        
        private static void UpdateMethodReferences(ModuleDefinition module, MethodReference old, MethodReference @new, [UsedImplicitly] List<DiagnosticMessage> messages)
        {
            foreach (var type in module.Types)
            {
                foreach (var method in type.Methods)
                {
                    if (method.Body == null) continue;
                    
                    var processor = method.Body.GetILProcessor();

                    for (var i = 0; i < method.Body.Instructions.Count; i++)
                    {
                        var instruction = method.Body.Instructions[i];

                        if (instruction.OpCode == OpCodes.Call &&
                            instruction.Operand is MethodReference methodReference && methodReference == old)
                            processor.Replace(instruction, Instruction.Create(OpCodes.Call, @new));
                    }
                }
            }
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            try
            {
                if (!WillProcess(compiledAssembly))
                    return default!;

                var messages = new List<DiagnosticMessage>();

                using var peStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PeData);
                using var pdbStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData);

                var assemblyDefinition = AssemblyDefinition.ReadAssembly(peStream, new ReaderParameters
                {
                    ReadSymbols = true,
                    SymbolStream = pdbStream,
                    SymbolReaderProvider = new PortablePdbReaderProvider(),
                    AssemblyResolver = new AssemblyResolver(compiledAssembly)
                });

                for (var m = 0; m < assemblyDefinition.Modules.Count; m++)
                {
                    var module = assemblyDefinition.Modules[m];
                    for (var t = 0; t < module.Types.Count; t++)
                    {
                        var type = module.Types[t];
                        if (!type.IsClass)
                            continue;

                        var idFullName = typeof(NetworkIdentity).FullName;
                        if (!InheritsFrom(type, idFullName) && type.FullName != idFullName)
                            continue;
                        
                        List<MethodDefinition> rpcMethods = new();

                        int idOffset = GetIDOffset(type);

                        for (var i = 0; i < type.Methods.Count; i++)
                        {
                            var method = type.Methods[i];

                            if (HasCustomAttribute(method, typeof(ServerRPCAttribute).FullName))
                                rpcMethods.Add(method);
                        }

                        int index = 0;
                        try
                        {
                            for (index = 0; index < rpcMethods.Count; index++)
                            {
                                var method = rpcMethods[index];
                                var newMethod = HandleRPC(idOffset + index, method, messages);

                                if (newMethod != null)
                                {
                                    type.Methods.Add(newMethod);
                                    UpdateMethodReferences(module, method, newMethod, messages);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Error(messages, e.Message, rpcMethods[index]);
                        }

                        try
                        {
                            if (rpcMethods.Count > 0)     
                                HandleRPCReceiver(module, type, rpcMethods, idOffset);
                        }
                        catch (Exception e)
                        {
                            messages.Add(new DiagnosticMessage
                            {
                                DiagnosticType = DiagnosticType.Error,
                                MessageData = $"[{type.Name}]: {e.Message}"
                            });
                        }
                    }
                }

                var pe = new MemoryStream();
                var pdb = new MemoryStream();

                var writerParameters = new WriterParameters
                {
                    WriteSymbols = true,
                    SymbolStream = pdb,
                    SymbolWriterProvider = new PortablePdbWriterProvider()
                };

                assemblyDefinition.Write(pe, writerParameters);

                return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), messages);
            }
            catch (Exception e)
            {
                
                var messages = new List<DiagnosticMessage> {
                    new()
                    {
                        DiagnosticType = DiagnosticType.Error,
                        MessageData = e.Message,
                    }
                };
                
                return new ILPostProcessResult(compiledAssembly.InMemoryAssembly, messages);
            }
        }
    }
}
#endif