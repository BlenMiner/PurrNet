using System;
using NUnit.Framework;
using Unity.Profiling;

namespace PurrNet.Tests
{
    // Unity's embedded Mono can return zero unconditionally from
    // GC.GetAllocatedBytesForCurrentThread. Calibrate the actual profiler marker instead.
    internal sealed class AllocationRecorder : IDisposable
    {
        private static byte[] _calibration;
        private ProfilerRecorder _recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc", 1,
            ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

        public AllocationRecorder()
        {
            Assert.IsTrue(_recorder.Valid, "The GC.Alloc profiler marker must be available.");
            _recorder.Start();
            _calibration = new byte[4096];
            _recorder.Stop();
            Assert.Greater(_recorder.Count, 0, "The allocation recorder must detect a managed allocation.");
            _recorder.Reset();
        }

        public void Start()
        {
            _recorder.Reset();
            _recorder.Start();
        }

        public int Stop()
        {
            _recorder.Stop();
            return _recorder.Count;
        }

        public void Dispose() => _recorder.Dispose();
    }
}
