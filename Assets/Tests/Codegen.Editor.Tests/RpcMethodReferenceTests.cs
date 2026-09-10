using System;
using System.Collections;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace PurrNet.Codegen.Tests
{
    [TestFixture(false)]
    [TestFixture(true)]
    public sealed class RpcMethodReferenceTests
    {
        private readonly bool _useIndex;
        private ModuleDefinition _module;
        private TypeDefinition _rpcType;
        private MethodDefinition _original;
        private MethodDefinition _wrapper;
        private MethodDefinition _enterLocal;
        private MethodDefinition _exitLocal;
        private MethodInfo _rewrite;
        private IList _messages;
        private object _index;
        private MethodInfo _indexedRewrite;
        private MethodInfo _invalidate;

        public RpcMethodReferenceTests(bool useIndex) => _useIndex = useIndex;

        [SetUp]
        public void SetUp()
        {
            _module = ModuleDefinition.CreateModule(nameof(RpcMethodReferenceTests), ModuleKind.Dll);
            var flags = AddType(typeof(PurrCompilerFlags).Namespace, nameof(PurrCompilerFlags));
            _enterLocal = AddMethod(flags, nameof(PurrCompilerFlags.EnterLocalExecution));
            _exitLocal = AddMethod(flags, nameof(PurrCompilerFlags.ExitLocalExecution));
            _rpcType = AddType("Tests", "RpcOwner");
            _original = AddMethod(_rpcType, "Send_Original_0");
            _wrapper = AddMethod(_rpcType, "Send");

            // Use the actual private rewrite without exposing an implementation API or
            // adding compilation-pipeline assembly dependencies to this test assembly.
            var processor = typeof(RegisterSerializersProcessor).Assembly.GetType("PurrNet.Codegen.PostProcessor");
            Assert.That(processor, Is.Not.Null);
            _rewrite = processor.GetMethod("UpdateMethodReferences", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(_rewrite, Is.Not.Null);
            _messages = (IList)Activator.CreateInstance(_rewrite.GetParameters()[3].ParameterType);
            var indexType = processor.GetNestedType("RpcMethodReferenceIndex", BindingFlags.NonPublic);
            Assert.That(indexType, Is.Not.Null);
            _index = Activator.CreateInstance(indexType, new object[] { _module });
            _indexedRewrite = indexType.GetMethod("Update");
            _invalidate = indexType.GetMethod("Invalidate");
        }

        [TearDown]
        public void TearDown()
        {
            _module.Dispose();
        }

        [Test]
        public void RewritesNestedCallerWithoutReplacingBranchTargetInstruction()
        {
            var nested = new TypeDefinition("", "NestedCaller", TypeAttributes.NestedPublic, _module.TypeSystem.Object);
            _rpcType.NestedTypes.Add(nested);
            var caller = AddMethod(nested, "CallRpc");
            var call = Call(caller, _original);
            var branch = Instruction.Create(OpCodes.Br, call);
            caller.Body.Instructions.Insert(0, branch);

            AssertRewriteSucceeded();

            Assert.That(branch.Operand, Is.SameAs(call));
            Assert.That(caller.Body.Instructions[1], Is.SameAs(call));
            Assert.That(call.OpCode, Is.EqualTo(OpCodes.Call));
            AssertWrapper(call);
        }

        [TestCase(nameof(PurrCompilerFlags.EnterLocalExecution))]
        [TestCase(nameof(PurrCompilerFlags.ExitLocalExecution))]
        public void SameNamedMethodOnAnotherTypeIsNotALocalExecutionFlag(string name)
        {
            var unrelatedType = AddType("Tests", "UnrelatedFlags");
            var unrelatedFlag = AddMethod(unrelatedType, name);
            var caller = AddMethod(_rpcType, "CallRpc");
            var unrelatedCall = Call(caller, unrelatedFlag);
            var rpcCall = Call(caller, _original);

            AssertRewriteSucceeded();

            Assert.That(unrelatedCall.Operand, Is.SameAs(unrelatedFlag));
            AssertWrapper(rpcCall);
        }

        [Test]
        public void BalancedLocalSectionKeepsOriginalCallAndRewritesCallsOutsideIt()
        {
            var caller = AddMethod(_rpcType, "CallRpc");
            var before = Call(caller, _original);
            var enter = Call(caller, _enterLocal);
            var inside = Call(caller, _original);
            var exit = Call(caller, _exitLocal);
            var after = Call(caller, _original);

            AssertRewriteSucceeded();

            AssertWrapper(before);
            Assert.That(inside.Operand, Is.SameAs(_original));
            AssertWrapper(after);
            Assert.That(enter.Operand, Is.SameAs(_enterLocal));
            Assert.That(exit.Operand, Is.SameAs(_exitLocal));
        }

        [Test]
        public void LocalModeAttributeKeepsOriginalCall()
        {
            var attribute = AddType(typeof(LocalModeAttribute).Namespace, nameof(LocalModeAttribute));
            var constructor = AddMethod(attribute, ".ctor");
            constructor.IsStatic = false;
            var caller = AddMethod(_rpcType, "CallRpc");
            caller.CustomAttributes.Add(new CustomAttribute(constructor));
            var call = Call(caller, _original);

            AssertRewriteSucceeded();

            Assert.That(call.Operand, Is.SameAs(_original));
        }

        [TestCase("nested", "Local mode flag was already set")]
        [TestCase("exit", "Local mode flag was not set")]
        [TestCase("unclosed", "Local mode flag was not unset")]
        public void InvalidLocalSectionsKeepTheirDiagnostics(string mode, string expectedMessage)
        {
            var caller = AddMethod(_rpcType, "CallRpc");
            if (mode == "exit")
                Call(caller, _exitLocal);
            else
                Call(caller, _enterLocal);
            if (mode == "nested")
                Call(caller, _enterLocal);
            Call(caller, _original);

            Assert.That(Rewrite(), Is.False);
            Assert.That(_messages.Count, Is.EqualTo(1));
            var diagnostic = _messages[0];
            var message = (string)GetDiagnosticMember(diagnostic, "MessageData");
            Assert.That(message, Does.Contain(expectedMessage));
            var severity = GetDiagnosticMember(diagnostic, "DiagnosticType");
            Assert.That(severity.ToString(), Is.EqualTo("Error"));
        }

        [Test]
        public void GenericCallRetainsTypeArguments()
        {
            _original.GenericParameters.Add(new GenericParameter("T", _original));
            _wrapper.GenericParameters.Add(new GenericParameter("T", _wrapper));
            var originalReference = new GenericInstanceMethod(_original);
            originalReference.GenericArguments.Add(_module.TypeSystem.Int32);
            var caller = AddMethod(_rpcType, "CallRpc");
            var call = Call(caller, originalReference);

            AssertRewriteSucceeded();

            Assert.That(call.Operand, Is.TypeOf<GenericInstanceMethod>());
            var rewritten = (GenericInstanceMethod)call.Operand;
            Assert.That(rewritten.GenericArguments.Count, Is.EqualTo(1));
            Assert.That(rewritten.GenericArguments[0], Is.SameAs(_module.TypeSystem.Int32));
            Assert.That(rewritten.ElementMethod.Resolve(), Is.SameAs(_wrapper));
        }

        [Test]
        public void WrapperKeepsItsOriginalCallWhileAnotherRpcBodyUsesTheWrapper()
        {
            var wrapperCall = Call(_wrapper, _original);
            var anotherRpc = AddMethod(_rpcType, "Another_Original_1");
            var crossRpcCall = Call(anotherRpc, _original);

            AssertRewriteSucceeded();

            Assert.That(wrapperCall.Operand, Is.SameAs(_original));
            AssertWrapper(crossRpcCall);
        }

        [TestCase("RpcSendState_1")]
        [TestCase("RpcReceiveState_1")]
        public void GeneratedAsyncStateKeepsItsOriginalCall(string name)
        {
            var state = new TypeDefinition("", name, TypeAttributes.NestedPrivate, _module.TypeSystem.Object);
            _rpcType.NestedTypes.Add(state);
            var caller = AddMethod(state, "RunLocal");
            var call = Call(caller, _original);

            AssertRewriteSucceeded();

            Assert.That(call.Operand, Is.SameAs(_original));
        }

        [Test]
        public void LaterRpcRewritesCrossCallsInEarlierWrapperAndRetainsRenamedIdentity()
        {
            var laterOriginal = AddMethod(_rpcType, "Later");
            var crossCall = Call(_wrapper, laterOriginal);
            var ownCall = Call(_wrapper, _original);
            var caller = AddMethod(_rpcType, "Caller");
            var laterCall = Call(caller, laterOriginal);
            AssertRewriteSucceeded();

            Invalidate(_rpcType);
            laterOriginal.Name = "Later_Original_1";
            var laterWrapper = AddMethod(_rpcType, "Later");
            var laterOwnCall = Call(laterWrapper, laterOriginal);

            Assert.That(Rewrite(laterOriginal, laterWrapper), Is.True);

            Assert.That(((MethodReference)crossCall.Operand).Resolve(), Is.SameAs(laterWrapper));
            Assert.That(((MethodReference)laterCall.Operand).Resolve(), Is.SameAs(laterWrapper));
            Assert.That(ownCall.Operand, Is.SameAs(_original));
            Assert.That(laterOwnCall.Operand, Is.SameAs(laterOriginal));
        }

        [Test]
        public void RefreshFindsGeneratedReceiverAndNestedCallerWithoutRewrappingEarlierOriginal()
        {
            var laterOriginal = AddMethod(_rpcType, "Later_Original_1");
            AssertRewriteSucceeded();

            Invalidate(_rpcType);
            var receiver = AddMethod(_rpcType, "GeneratedReceiver");
            var receivedOriginal = Call(receiver, _original);
            var nested = new TypeDefinition("", "GeneratedCaller", TypeAttributes.NestedPrivate, _module.TypeSystem.Object);
            _rpcType.NestedTypes.Add(nested);
            var nestedCall = Call(AddMethod(nested, "InvokeLater"), laterOriginal);
            var laterWrapper = AddMethod(_rpcType, "Later");

            Assert.That(Rewrite(laterOriginal, laterWrapper), Is.True);

            Assert.That(receivedOriginal.Operand, Is.SameAs(_original));
            Assert.That(((MethodReference)nestedCall.Operand).Resolve(), Is.SameAs(laterWrapper));
        }

        [Test]
        public void RefreshFindsNewModuleRootTypes()
        {
            var laterOriginal = AddMethod(_rpcType, "Later_Original_1");
            AssertRewriteSucceeded();

            var generatedRoot = AddType("Tests", "GeneratedRoot");
            var call = Call(AddMethod(generatedRoot, "InvokeLater"), laterOriginal);
            Invalidate(_rpcType);
            var laterWrapper = AddMethod(_rpcType, "Later");

            Assert.That(Rewrite(laterOriginal, laterWrapper), Is.True);

            Assert.That(((MethodReference)call.Operand).Resolve(), Is.SameAs(laterWrapper));
        }

        [Test]
        public void RefreshDoesNotRescanDetachedDirtyNestedTypes()
        {
            var laterOriginal = AddMethod(_rpcType, "Later_Original_1");
            var nested = new TypeDefinition("", "RemovedNested", TypeAttributes.NestedPrivate, _module.TypeSystem.Object);
            _rpcType.NestedTypes.Add(nested);
            var detachedCall = Call(AddMethod(nested, "InvokeLater"), laterOriginal);
            AssertRewriteSucceeded();

            Invalidate(_rpcType);
            Invalidate(nested);
            _rpcType.NestedTypes.Remove(nested);
            var laterWrapper = AddMethod(_rpcType, "Later");

            Assert.That(Rewrite(laterOriginal, laterWrapper), Is.True);

            Assert.That(detachedCall.Operand, Is.SameAs(laterOriginal));
        }

        [Test]
        public void RefreshDropsReplacedBodyAndFindsNewInstructions()
        {
            var laterOriginal = AddMethod(_rpcType, "Later_Original_1");
            var callerType = AddType("Tests", "ChangingCaller");
            var caller = AddMethod(callerType, "InvokeLater");
            var detachedCall = Call(caller, laterOriginal);
            AssertRewriteSucceeded();

            Invalidate(callerType);
            caller.Body = new Mono.Cecil.Cil.MethodBody(caller);
            caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            var newCall = Call(caller, laterOriginal);
            Invalidate(_rpcType);
            var laterWrapper = AddMethod(_rpcType, "Later");

            Assert.That(Rewrite(laterOriginal, laterWrapper), Is.True);

            Assert.That(detachedCall.Operand, Is.SameAs(laterOriginal));
            Assert.That(((MethodReference)newCall.Operand).Resolve(), Is.SameAs(laterWrapper));
        }

        [Test]
        public void RefreshDropsRemovedMethodsAndFindsChangedOperandsInExistingBodies()
        {
            var laterOriginal = AddMethod(_rpcType, "Later_Original_1");
            var unrelated = AddMethod(_rpcType, "Unrelated");
            var callerType = AddType("Tests", "ChangingCaller");
            var removed = AddMethod(callerType, "Removed");
            var removedCall = Call(removed, laterOriginal);
            var changedCall = Call(AddMethod(callerType, "Changed"), unrelated);
            AssertRewriteSucceeded();

            Invalidate(callerType);
            callerType.Methods.Remove(removed);
            changedCall.Operand = laterOriginal;
            Invalidate(_rpcType);
            var laterWrapper = AddMethod(_rpcType, "Later");

            Assert.That(Rewrite(laterOriginal, laterWrapper), Is.True);

            Assert.That(removedCall.Operand, Is.SameAs(laterOriginal));
            Assert.That(((MethodReference)changedCall.Operand).Resolve(), Is.SameAs(laterWrapper));
        }

        [Test]
        public void RefreshSeesLocalModeAttributeMoveFromOriginalToWrapper()
        {
            var attribute = AddType(typeof(LocalModeAttribute).Namespace, nameof(LocalModeAttribute));
            var constructor = AddMethod(attribute, ".ctor");
            constructor.IsStatic = false;
            var laterOriginal = AddMethod(_rpcType, "Later_Original_1");
            var localMode = new CustomAttribute(constructor);
            laterOriginal.CustomAttributes.Add(localMode);
            var originalCall = Call(laterOriginal, laterOriginal);
            AssertRewriteSucceeded();

            Invalidate(_rpcType);
            laterOriginal.CustomAttributes.Clear();
            var laterWrapper = AddMethod(_rpcType, "Later");
            laterWrapper.CustomAttributes.Add(localMode);
            var wrapperCall = Call(laterWrapper, laterOriginal);

            Assert.That(Rewrite(laterOriginal, laterWrapper), Is.True);

            Assert.That(((MethodReference)originalCall.Operand).Resolve(), Is.SameAs(laterWrapper));
            Assert.That(wrapperCall.Operand, Is.SameAs(laterOriginal));
        }

        [Test]
        public void RefreshSeesLocalFlagsAddedWithoutReplacingBody()
        {
            var laterOriginal = AddMethod(_rpcType, "Later_Original_1");
            var caller = AddMethod(_rpcType, "InvokeLater");
            var localCall = Call(caller, laterOriginal);
            AssertRewriteSucceeded();

            Invalidate(_rpcType);
            caller.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, _enterLocal));
            Call(caller, _exitLocal);
            var normalCall = Call(caller, laterOriginal);
            var laterWrapper = AddMethod(_rpcType, "Later");

            Assert.That(Rewrite(laterOriginal, laterWrapper), Is.True);

            Assert.That(localCall.Operand, Is.SameAs(laterOriginal));
            Assert.That(((MethodReference)normalCall.Operand).Resolve(), Is.SameAs(laterWrapper));
        }

        [Test]
        public void NewInvalidSectionKeepsPriorRewritesAndReportsTheFirstModuleOrderedError()
        {
            var laterOriginal = AddMethod(_rpcType, "Later_Original_1");
            var caller = AddMethod(_rpcType, "Caller");
            var earlierCall = Call(caller, _original);
            var beforeError = Call(caller, laterOriginal);
            AssertRewriteSucceeded();

            Invalidate(_rpcType);
            Call(caller, _exitLocal);
            var secondInvalid = AddMethod(_rpcType, "AnotherInvalid");
            Call(secondInvalid, _enterLocal);
            var laterWrapper = AddMethod(_rpcType, "Later");

            Assert.That(Rewrite(laterOriginal, laterWrapper), Is.False);

            AssertWrapper(earlierCall);
            Assert.That(((MethodReference)beforeError.Operand).Resolve(), Is.SameAs(laterWrapper));
            Assert.That(_messages.Count, Is.EqualTo(1));
            Assert.That((string)GetDiagnosticMember(_messages[0], "MessageData"), Does.Contain("Local mode flag was not set"));
        }

        [Test]
        public void EquivalentMethodReferenceIsNotBroadenedToADefinitionMatch()
        {
            var reference = new MethodReference(_original.Name, _original.ReturnType, _rpcType)
            {
                HasThis = _original.HasThis,
                CallingConvention = _original.CallingConvention
            };
            Assert.That(reference.Resolve(), Is.SameAs(_original));
            var call = Call(AddMethod(_rpcType, "CallByMemberReference"), reference);

            AssertRewriteSucceeded();

            Assert.That(call.Operand, Is.SameAs(reference));
        }

        private bool Rewrite()
        {
            return Rewrite(_original, _wrapper);
        }

        private bool Rewrite(MethodDefinition original, MethodDefinition wrapper)
        {
            return _useIndex
                ? (bool)_indexedRewrite.Invoke(_index, new object[] { original, wrapper, _messages })
                : (bool)_rewrite.Invoke(null, new object[] { _module, original, wrapper, _messages });
        }

        private void Invalidate(TypeDefinition type)
        {
            _invalidate.Invoke(_index, new object[] { type });
        }

        private static object GetDiagnosticMember(object diagnostic, string name)
        {
            var type = diagnostic.GetType();
            var property = type.GetProperty(name);
            if (property != null)
                return property.GetValue(diagnostic);

            var field = type.GetField(name);
            Assert.That(field, Is.Not.Null, $"Diagnostic type {type.FullName} has no public member {name}.");
            return field.GetValue(diagnostic);
        }

        private void AssertRewriteSucceeded()
        {
            Assert.That(Rewrite(), Is.True);
            Assert.That(_messages, Is.Empty);
        }

        private void AssertWrapper(Instruction instruction)
        {
            Assert.That(((MethodReference)instruction.Operand).Resolve(), Is.SameAs(_wrapper));
        }

        private TypeDefinition AddType(string typeNamespace, string name)
        {
            var type = new TypeDefinition(typeNamespace, name, TypeAttributes.Public, _module.TypeSystem.Object);
            _module.Types.Add(type);
            return type;
        }

        private MethodDefinition AddMethod(TypeDefinition type, string name)
        {
            var method = new MethodDefinition(name, MethodAttributes.Public | MethodAttributes.Static, _module.TypeSystem.Void);
            type.Methods.Add(method);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            return method;
        }

        private static Instruction Call(MethodDefinition caller, MethodReference target)
        {
            var call = Instruction.Create(OpCodes.Call, target);
            caller.Body.Instructions.Insert(caller.Body.Instructions.Count - 1, call);
            return call;
        }
    }
}
