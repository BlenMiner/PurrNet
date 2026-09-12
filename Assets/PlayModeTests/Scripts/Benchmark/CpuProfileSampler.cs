using System.Collections.Generic;
using UnityEngine.Profiling;

public struct CpuMarker
{
    public string name;
    public double totalMs;     // total time across the window
    public double perFrameMs;  // average per sampled frame
    public long calls;         // total sample-block count
}

// Reads PurrNet's ProfilerMarkers at runtime via UnityEngine.Profiling.Recorder. Markers exist only
// in a Development build (ENABLE_PROFILER); on a release build GetNames yields nothing and this
// no-ops, so the CPU breakdown is simply omitted from the results.
public class CpuProfileSampler
{
    // Marker name prefixes we attribute (PurrNet netcode). Unity has thousands of built-in
    // samplers; we only want ours.
    private static readonly string[] Prefixes =
    {
        "NetworkManager.", "NetworkTransform.", "NetworkIdentity.", "RPCModule.", "RPCBatch", "DeltaModule.", "PurrNet"
    };

    private readonly List<string> _names = new();
    private readonly List<Recorder> _recorders = new();
    private readonly List<long> _ns = new();
    private readonly List<long> _calls = new();
    private int _frames;
    private bool _available;

    public void Begin()
    {
        _names.Clear();
        _recorders.Clear();
        _ns.Clear();
        _calls.Clear();
        _frames = 0;

        // No-op on a release build (profiler stripped); ensures samplers record on a dev build.
        Profiler.enabled = true;

        var all = new List<string>();
        Sampler.GetNames(all);

        for (int i = 0; i < all.Count; i++)
        {
            var n = all[i];
            if (!Matches(n))
                continue;

            var rec = Recorder.Get(n);
            if (rec == null || !rec.isValid)
                continue;

            rec.enabled = true;
            _names.Add(n);
            _recorders.Add(rec);
            _ns.Add(0);
            _calls.Add(0);
        }

        _available = _recorders.Count > 0;
    }

    public void Sample()
    {
        if (!_available)
            return;

        _frames++;
        for (int i = 0; i < _recorders.Count; i++)
        {
            var rec = _recorders[i];
            _ns[i] += rec.elapsedNanoseconds;
            _calls[i] += rec.sampleBlockCount;
        }
    }

    public CpuMarker[] End()
    {
        if (!_available)
            return System.Array.Empty<CpuMarker>();

        for (int i = 0; i < _recorders.Count; i++)
            _recorders[i].enabled = false;

        int frames = _frames > 0 ? _frames : 1;
        var list = new List<CpuMarker>(_names.Count);

        for (int i = 0; i < _names.Count; i++)
        {
            if (_ns[i] <= 0 && _calls[i] == 0)
                continue;

            double totalMs = _ns[i] / 1_000_000.0;
            list.Add(new CpuMarker
            {
                name = _names[i],
                totalMs = totalMs,
                perFrameMs = totalMs / frames,
                calls = _calls[i]
            });
        }

        list.Sort((a, b) => b.totalMs.CompareTo(a.totalMs));
        return list.ToArray();
    }

    private static bool Matches(string name)
    {
        for (int i = 0; i < Prefixes.Length; i++)
            if (name.StartsWith(Prefixes[i]))
                return true;
        return false;
    }
}
