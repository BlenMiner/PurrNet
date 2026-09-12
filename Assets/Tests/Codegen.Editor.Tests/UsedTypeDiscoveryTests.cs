using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using PurrNet.Modules;
using PurrNet.Pooling;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace PurrNet.Codegen.Tests
{
    public sealed class UsedTypeDiscoveryTests
    {
        private ModuleDefinition _module;
        private TypeDefinition _holder;
        private TypeDefinition _networkModule;
        private MethodDefinition _caller;
        private MethodDefinition _rpcAttributeConstructor;
        private MethodInfo _discover;

        [SetUp]
        public void SetUp()
        {
            _module = ModuleDefinition.CreateModule(nameof(UsedTypeDiscoveryTests), ModuleKind.Dll);
            AddType(typeof(PlayersBroadcaster));
            AddType(typeof(PlayersManager));
            AddType(typeof(BroadcastModule));
            _networkModule = AddType(typeof(NetworkModule));
            var attribute = AddType(typeof(ServerRpcAttribute));
            _rpcAttributeConstructor = AddMethod(attribute, ".ctor");
            _rpcAttributeConstructor.IsStatic = false;
            _holder = AddType("Tests", "Caller");
            _caller = AddMethod(_holder, "Run");

            var processor = typeof(RegisterSerializersProcessor).Assembly.GetType("PurrNet.Codegen.PostProcessor");
            Assert.That(processor, Is.Not.Null);
            _discover = processor.GetMethod("FindUsedTypes", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(_discover, Is.Not.Null);
        }

        [TearDown]
        public void TearDown() => _module.Dispose();

        [Test]
        public void GenericRpcOnNongenericOwnerDiscoversArgumentsAndClosedSignatures()
        {
            var owner = AddType("Tests", "RpcOwner");
            var packet = GenericType("Packet");
            var reply = GenericType("Reply");
            var rpc = AddMethod(owner, "Echo");
            rpc.GenericParameters.Add(new GenericParameter("T", rpc));
            rpc.Parameters.Add(new ParameterDefinition(Close(packet, rpc.GenericParameters[0])));
            rpc.ReturnType = Close(reply, rpc.GenericParameters[0]);
            MarkRpc(rpc);
            var call = new GenericInstanceMethod(rpc);
            call.GenericArguments.Add(_module.TypeSystem.Int32);
            Call(call);

            AssertTypes(Discover(), _module.TypeSystem.Int32,
                Close(packet, _module.TypeSystem.Int32), Close(reply, _module.TypeSystem.Int32));
        }

        [Test]
        public void GenericOwnerRpcIsClassifiedOncePerPassAndRecheckedOnTheNextPass()
        {
            var owner = GenericType("RpcOwner");
            var rpc = AddMethod(owner, "Send");
            MarkRpc(rpc);
            var closedOwner = Close(owner, _module.TypeSystem.Int32);
            var reference = new CountingMethodReference(rpc.Name, _module.TypeSystem.Void, closedOwner, () => rpc);
            Call(reference);
            Call(reference, true);
            Call(reference);

            AssertTypes(Discover(), closedOwner, _module.TypeSystem.Int32);
            Assert.That(reference.ResolveCount, Is.EqualTo(1));

            // A later discovery invocation must observe changed attributes.
            rpc.CustomAttributes.Clear();
            AssertTypes(Discover());
            Assert.That(reference.ResolveCount, Is.EqualTo(2));
        }

        [Test]
        public void UnresolvedRpcReferenceCanSucceedOnALaterCallInTheSamePass()
        {
            var owner = GenericType("LateOwner");
            var rpc = AddMethod(owner, "Send");
            MarkRpc(rpc);
            var closedOwner = Close(owner, _module.TypeSystem.String);
            int attempts = 0;
            var reference = new CountingMethodReference(rpc.Name, _module.TypeSystem.Void, closedOwner,
                () => ++attempts == 1 ? null : rpc);
            Call(reference);
            Call(reference);
            Call(reference);

            AssertTypes(Discover(), closedOwner, _module.TypeSystem.String);
            Assert.That(reference.ResolveCount, Is.EqualTo(2));
        }

        [Test]
        public void AllBroadcasterSubscriptionsCollectPayloadsWithoutRpcAttributes()
        {
            var owners = new[] { typeof(PlayersBroadcaster), typeof(PlayersManager), typeof(BroadcastModule) };
            var payloads = new[] { _module.TypeSystem.Int32, _module.TypeSystem.String, _module.TypeSystem.Boolean };
            for (int i = 0; i < owners.Length; i++)
            {
                var owner = _module.Types.Single(t => t.FullName == owners[i].FullName);
                var subscribe = AddMethod(owner, "Subscribe");
                subscribe.GenericParameters.Add(new GenericParameter("T", subscribe));
                var call = new GenericInstanceMethod(subscribe);
                call.GenericArguments.Add(payloads[i]);
                Call(call, i == 1);
            }

            AssertTypes(Discover(), payloads);
        }

        [Test]
        public void GenericNetworkModuleFieldCollectsItsClosedTypeAndPayload()
        {
            var cell = GenericType("NetworkCell");
            cell.BaseType = _networkModule;
            var closedCell = Close(cell, _module.TypeSystem.String);
            _holder.Fields.Add(new FieldDefinition("State", FieldAttributes.Public, closedCell));
            var openCell = Close(cell, cell.GenericParameters[0]);
            _holder.Fields.Add(new FieldDefinition("OpenState", FieldAttributes.Public, openCell));

            AssertTypes(Discover(), closedCell, _module.TypeSystem.String);
        }

        [Test]
        public void OrdinaryCallsAndNongenericFieldsDoNotResolveUnrelatedDependencies()
        {
            var unrelated = new CountingMethodReference("Ordinary", _module.TypeSystem.Void, _holder,
                () => throw new InvalidOperationException("Unrelated method must not be resolved."));
            var fieldType = new CountingTypeReference(_module);
            _holder.Fields.Add(new FieldDefinition("Unrelated", FieldAttributes.Public, fieldType));
            Call(unrelated);
            Call(unrelated, true);

            AssertTypes(Discover());
            Assert.That(unrelated.ResolveCount, Is.Zero);
            Assert.That(fieldType.ResolveCount, Is.Zero);
        }

        private HashSet<TypeReference> Discover()
        {
            using var allTypes = DisposableList<TypeDefinition>.Create(16);
            allTypes.AddRange(_module.Types);
            var collected = new HashSet<TypeReference>(TypeReferenceEqualityComparer.Default);
            _discover.Invoke(null, new object[] { _module, allTypes, collected });
            return collected;
        }

        private static void AssertTypes(IEnumerable<TypeReference> actual, params TypeReference[] expected)
        {
            Assert.That(actual.Select(t => t.FullName), Is.EquivalentTo(expected.Select(t => t.FullName)));
        }

        private TypeDefinition AddType(Type type) => AddType(type.Namespace, type.Name);

        private TypeDefinition AddType(string typeNamespace, string name)
        {
            var type = new TypeDefinition(typeNamespace, name, TypeAttributes.Public | TypeAttributes.Class,
                _module.TypeSystem.Object);
            _module.Types.Add(type);
            return type;
        }

        private TypeDefinition GenericType(string name)
        {
            var type = AddType("Tests", name);
            type.GenericParameters.Add(new GenericParameter("T", type));
            return type;
        }

        private static GenericInstanceType Close(TypeReference type, TypeReference argument)
        {
            var result = new GenericInstanceType(type);
            result.GenericArguments.Add(argument);
            return result;
        }

        private MethodDefinition AddMethod(TypeDefinition owner, string name)
        {
            var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static,
                _module.TypeSystem.Void);
            owner.Methods.Add(method);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return method;
        }

        private void MarkRpc(MethodDefinition method) =>
            method.CustomAttributes.Add(new CustomAttribute(_rpcAttributeConstructor));

        private void Call(MethodReference target, bool virtualCall = false)
        {
            _caller.Body.Instructions.Insert(_caller.Body.Instructions.Count - 1,
                Instruction.Create(virtualCall ? OpCodes.Callvirt : OpCodes.Call, target));
        }

        private sealed class CountingMethodReference : MethodReference
        {
            private readonly Func<MethodDefinition> _resolve;
            internal int ResolveCount { get; private set; }

            internal CountingMethodReference(string name, TypeReference returnType, TypeReference owner,
                Func<MethodDefinition> resolve) : base(name, returnType, owner) => _resolve = resolve;

            public override MethodDefinition Resolve()
            {
                ResolveCount++;
                return _resolve();
            }
        }

        private sealed class CountingTypeReference : TypeReference
        {
            internal int ResolveCount { get; private set; }

            internal CountingTypeReference(ModuleDefinition module) : base("Missing", "Unrelated", module,
                new AssemblyNameReference("MissingDependency", new Version(1, 0))) { }

            public override TypeDefinition Resolve()
            {
                ResolveCount++;
                throw new InvalidOperationException("Unrelated field type must not be resolved.");
            }
        }
    }
}
