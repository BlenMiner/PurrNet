using Microsoft.CodeAnalysis;

namespace PurrNet.Analyzers
{
    internal static class PurrNetDiagnostics
    {
        private const string RpcCategory = "PurrNet.RPC";
        private const string NetworkModuleCategory = "PurrNet.NetworkModule";
        private const string SerializationCategory = "PurrNet.Serialization";

        public static readonly DiagnosticDescriptor MultipleRpcAttributes = Error(
            "PN0001",
            "Method cannot have multiple RPC attributes",
            "Method '{0}' cannot have multiple RPC attributes");

        public static readonly DiagnosticDescriptor BufferLastWithDeltaPacked = Error(
            "PN0002",
            "RPC cannot be both buffered and delta packed",
            "{0} cannot have both bufferLast and deltaPacked enabled");

        public static readonly DiagnosticDescriptor InstanceRpcOutsideNetworkType = Error(
            "PN0003",
            "Instance RPC must be declared on a network type",
            "RPC '{0}' must be static because '{1}' does not inherit NetworkIdentity or NetworkModule");

        public static readonly DiagnosticDescriptor InvalidRpcReturnType = Error(
            "PN0004",
            "RPC return type is not supported",
            "RPC '{0}' must return void, Task, Task<T>, UniTask, or UniTask<T>");

        public static readonly DiagnosticDescriptor ObserversRpcCoroutine = Error(
            "PN0005",
            "ObserversRpc cannot return IEnumerator",
            "ObserversRpc '{0}' cannot return IEnumerator");

        public static readonly DiagnosticDescriptor ObserversRpcAwaitable = Error(
            "PN0006",
            "ObserversRpc cannot return an awaitable",
            "ObserversRpc '{0}' cannot return Task or UniTask because there is no single response target");

        public static readonly DiagnosticDescriptor AwaitableTargetRpcMultipleTargets = Error(
            "PN0007",
            "Awaitable TargetRpc requires a single target",
            "TargetRpc '{0}' returns an awaitable and must target a single PlayerID, not multiple players");

        public static readonly DiagnosticDescriptor InvalidTargetRpcFirstParameter = Error(
            "PN0008",
            "TargetRpc first parameter must identify the target",
            "TargetRpc '{0}' must have a PlayerID, PlayerID[], IList<PlayerID>, or IEnumerable<PlayerID> first parameter");

        public static readonly DiagnosticDescriptor RpcInfoMustBeLast = Error(
            "PN0009",
            "RPCInfo must be the final RPC parameter",
            "RPCInfo parameter on RPC '{0}' must be the final parameter");

        public static readonly DiagnosticDescriptor OwnerRequiresNetworkIdentity = Error(
            "PN0010",
            "Owner guard requires NetworkIdentity",
            "[Owner] requires '{0}' to inherit NetworkIdentity");

        public static readonly DiagnosticDescriptor LocalModeNested = Error(
            "PN0011",
            "Local execution flag is already set",
            "Local execution flag was already set; avoid nesting EnterLocalExecution calls");

        public static readonly DiagnosticDescriptor LocalModeExitWithoutEnter = Error(
            "PN0012",
            "Local execution flag was not set",
            "ExitLocalExecution must be preceded by PurrCompilerFlags.EnterLocalExecution");

        public static readonly DiagnosticDescriptor LocalModeMissingExit = Error(
            "PN0013",
            "Local execution flag was not unset",
            "PurrCompilerFlags.EnterLocalExecution must be paired with ExitLocalExecution");

        public static readonly DiagnosticDescriptor RegisterNetworkTypeForeignType = Warning(
            "PN0014",
            "RegisterNetworkType target is declared in another assembly",
            "[RegisterNetworkType] for '{0}' is ignored because '{0}' is declared in assembly '{1}'; apply the attribute to a type in that assembly",
            SerializationCategory);

        public static readonly DiagnosticDescriptor NetworkModuleOutsideNetworkType = Warning(
            "PN0101",
            "NetworkModule member is not declared on a network type",
            "NetworkModule member '{0}' is not registered because '{1}' does not inherit NetworkIdentity or NetworkModule");

        public static readonly DiagnosticDescriptor StaticNetworkModuleField = Warning(
            "PN0102",
            "Static NetworkModule member is not registered",
            "Static NetworkModule member '{0}' is not registered");

        public static readonly DiagnosticDescriptor NetworkModuleProperty = Warning(
            "PN0103",
            "NetworkModule property is not registered",
            "NetworkModule property '{0}' is not registered; use a field or auto-property initialized before spawn");

        public static readonly DiagnosticDescriptor UninitializedNetworkModuleField = Warning(
            "PN0104",
            "NetworkModule member may be null when modules are initialized",
            "NetworkModule member '{0}' has no initializer or assignment in a constructor, Awake, or OnInitializeModules");

        public static readonly DiagnosticDescriptor NetworkModuleFieldReassignedAfterInitialization = Warning(
            "PN0105",
            "NetworkModule member is assigned after module initialization",
            "Assign NetworkModule member '{0}' in an initializer, constructor, Awake, or OnInitializeModules so it is registered correctly");

        public static readonly DiagnosticDescriptor ObserversBypassesOwnerOnly = Warning(
            "PN0109",
            "parent.observers bypasses owner-only scoping",
            "'{0}' reads parent.observers directly; use the module's own 'observers' so [OwnerOnly] is honoured");

        public static readonly DiagnosticDescriptor MtuOverrideOnSequencedChannel = Warning(
            "PN0106",
            "mtuExceeded override is ignored on UnreliableSequenced",
            "mtuExceeded override on '{0}' is ignored on UnreliableSequenced; the NetworkManager setting governs the whole channel",
            RpcCategory);

        public static readonly DiagnosticDescriptor ImmediateOnNonUnreliableChannel = Error(
            "PN0107",
            "immediate requires Channel.Unreliable",
            "immediate on '{0}' requires Channel.Unreliable; ordered and sequenced delivery cannot span an independently flushed lane");

        private static DiagnosticDescriptor Error(string id, string title, string messageFormat, string category = RpcCategory) =>
            new DiagnosticDescriptor(id, title, messageFormat, category, DiagnosticSeverity.Error, true);

        private static DiagnosticDescriptor Warning(string id, string title, string messageFormat, string category = NetworkModuleCategory) =>
            new DiagnosticDescriptor(id, title, messageFormat, category, DiagnosticSeverity.Warning, true);
    }
}
