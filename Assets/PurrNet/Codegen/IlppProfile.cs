#if UNITY_MONO_CECIL
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace PurrNet.Codegen
{
    /// <summary>
    /// Opt-in, per-invocation ILPP timings. Set PURRNET_ILPP_PROFILE_DIR to an output directory.
    /// Phase times are exclusive: entering a child phase pauses its parent. Total time includes
    /// unattributed work and instrumentation, but excludes serializing/writing the profile itself.
    /// The synchronous Process invocation owns this instance; no state is shared between runs.
    /// </summary>
    internal sealed class IlppProfile : IDisposable
    {
        internal enum Phase
        {
            SettingsRead, AssemblyRead, TypeDiscovery, ProxyScan, SerializerRegistration,
            RpcGeneration, RpcReferenceRewrite, UsedTypes, ReflectionTargets,
            ExpandNested, Serializers, Hashers, AssemblyWrite
        }

        private static readonly string[] PhaseNames =
        {
            "settings_read", "assembly_read", "type_discovery", "proxy_scan", "serializer_registration",
            "rpc_generation", "rpc_reference_rewrite", "used_types", "reflection_targets",
            "expand_nested", "serializers", "hashers", "assembly_write"
        };

        private readonly string _directory;
        private readonly string _assemblyName;
        private readonly string _target;
        private readonly string _runLabel;
        private readonly string _invocationId = Guid.NewGuid().ToString("N");
        private readonly DateTime _startedUtc = DateTime.UtcNow;
        private readonly int _processId;
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly long[] _phaseTicks = new long[PhaseNames.Length];
        private readonly int[] _phaseCounts = new int[PhaseNames.Length];
        private readonly Dictionary<string, long> _counters = new Dictionary<string, long>();
        private long _phaseStarted;
        private int _activePhase = -1;
        private int _errors;
        private int _warnings;
        private string _status = "error";
        private bool _disposed;

        internal static IlppProfile Start(ICompiledAssembly assembly)
        {
            try
            {
                var directory = Environment.GetEnvironmentVariable("PURRNET_ILPP_PROFILE_DIR");
                return string.IsNullOrWhiteSpace(directory) ? null : new IlppProfile(directory, assembly);
            }
            catch
            {
                // Profiling must never prevent compilation, including invalid environment settings.
                return null;
            }
        }

        private IlppProfile(string directory, ICompiledAssembly assembly)
        {
            _directory = directory;
            _assemblyName = assembly.Name;
            _runLabel = Environment.GetEnvironmentVariable("PURRNET_ILPP_PROFILE_LABEL");
            using (var process = Process.GetCurrentProcess())
                _processId = process.Id;
            bool editor = false;
            bool server = false;
            foreach (var define in assembly.Defines)
            {
                editor |= define == "UNITY_EDITOR";
                server |= define == "UNITY_SERVER";
            }
            _target = editor ? "editor" : server ? "server" : "player";
            SetCounter("inputPeBytes", assembly.InMemoryAssembly.PeData.LongLength);
            SetCounter("inputPdbBytes", assembly.InMemoryAssembly.PdbData.LongLength);
            _phaseStarted = _started;
        }

        internal Scope Measure(Phase phase)
        {
            int previous = _activePhase;
            ChangePhase((int)phase, Stopwatch.GetTimestamp());
            _phaseCounts[(int)phase]++;
            return new Scope(this, previous);
        }

        private void ChangePhase(int next, long now)
        {
            if (_activePhase >= 0)
                _phaseTicks[_activePhase] += now - _phaseStarted;
            _activePhase = next;
            _phaseStarted = now;
        }

        internal void SetCounter(string name, long value) => _counters[name] = value;

        internal void AddCounter(string name, long value)
        {
            _counters.TryGetValue(name, out long previous);
            _counters[name] = previous + value;
        }

        internal void Complete(IReadOnlyList<DiagnosticMessage> messages)
        {
            _errors = 0;
            _warnings = 0;
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].DiagnosticType == DiagnosticType.Error) _errors++;
                if (messages[i].DiagnosticType == DiagnosticType.Warning) _warnings++;
            }
            _status = _errors == 0 ? "success" : "error";
        }

        internal void Skip() => _status = "skipped";

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            long ended = Stopwatch.GetTimestamp();
            ChangePhase(-1, ended);
            try
            {
                double msPerTick = 1000.0 / Stopwatch.Frequency;
                long attributedTicks = 0;
                var phases = new Dictionary<string, object>();
                for (int i = 0; i < PhaseNames.Length; i++)
                {
                    attributedTicks += _phaseTicks[i];
                    phases.Add(PhaseNames[i], new
                    {
                        elapsedMs = _phaseTicks[i] * msPerTick,
                        count = _phaseCounts[i]
                    });
                }

                var result = new
                {
                    schemaVersion = 1,
                    assemblyName = _assemblyName,
                    target = _target,
                    status = _status,
                    startedUtc = _startedUtc.ToString("O"),
                    processId = _processId,
                    invocationId = _invocationId,
                    runLabel = _runLabel,
                    totalMs = (ended - _started) * msPerTick,
                    phaseTiming = "exclusive",
                    unattributedMs = (ended - _started - attributedTicks) * msPerTick,
                    phases,
                    counters = _counters,
                    diagnostics = new { errors = _errors, warnings = _warnings }
                };
                Directory.CreateDirectory(_directory);
                var name = $"{_startedUtc:yyyyMMddTHHmmssfffffffZ}-{_processId}-{_invocationId}.json";
                File.WriteAllText(Path.Combine(_directory, name), JsonConvert.SerializeObject(result, Formatting.Indented));
            }
            catch
            {
                // A missing/unwritable directory or serialization failure must not affect ILPP.
            }
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly IlppProfile _profile;
            private readonly int _previous;

            internal Scope(IlppProfile profile, int previous)
            {
                _profile = profile;
                _previous = previous;
            }

            public void Dispose() => _profile.ChangePhase(_previous, Stopwatch.GetTimestamp());
        }
    }
}
#endif
