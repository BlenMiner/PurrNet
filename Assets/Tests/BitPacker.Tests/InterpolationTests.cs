using System;
using System.Collections.Generic;
using NUnit.Framework;
using PurrNet;

public class InterpolationTests
{
    [Test]
    public void OverflowKeepsRenderedValueMonotone()
    {
        var interp = new Interpolated<float>((a, b, t) => a + (b - a) * t, 0.1f);

        float last = float.MinValue;
        float next = 1f;

        // Overflow the buffer repeatedly; a monotone input stream must render monotone.
        for (int i = 0; i < 200; i++)
        {
            interp.Add(next);
            next += 1f;
            if (i % 3 == 0)
                interp.Add(next);

            float value = interp.Advance(0.06f);
            Assert.That(value, Is.GreaterThanOrEqualTo(last),
                $"rendered value went backwards at iteration {i}");
            last = value;
        }
    }

    private class Tracked : IDisposable
    {
        public readonly float value;
        public int disposeCount;
        public readonly bool ownedByBuffer;

        public static readonly List<Tracked> owned = new();

        public Tracked(float value, bool ownedByBuffer)
        {
            this.value = value;
            this.ownedByBuffer = ownedByBuffer;
            if (ownedByBuffer)
                owned.Add(this);
        }

        public void Dispose()
        {
            disposeCount++;
            Assert.That(ownedByBuffer, Is.True, "buffer disposed a caller-owned lerp result");
            Assert.That(disposeCount, Is.EqualTo(1), $"double dispose of {value}");
        }
    }

    [Test]
    public void DisposeVariantOverflowIsMonotoneAndDisposesEachEntryOnce()
    {
        Tracked.owned.Clear();

        // Lerp results are caller-owned by contract; the buffer must never dispose them.
        var interp = new InterpolatedWithDispose<Tracked>(
            (a, b, t) => new Tracked(a.value + (b.value - a.value) * t, false),
            0.1f, new Tracked(0f, true));

        float last = float.MinValue;
        float next = 1f;

        for (int i = 0; i < 200; i++)
        {
            interp.Add(new Tracked(next, true));
            next += 1f;
            if (i % 3 == 0)
            {
                interp.Add(new Tracked(next, true));
                next += 1f;
            }

            var value = interp.Advance(0.06f);
            Assert.That(value.value, Is.GreaterThanOrEqualTo(last),
                $"rendered value went backwards at iteration {i}");
            last = value.value;
        }

        var final = new Tracked(9999f, true);
        interp.Teleport(final);

        foreach (var tracked in Tracked.owned)
        {
            if (ReferenceEquals(tracked, final))
                continue;
            Assert.That(tracked.disposeCount, Is.EqualTo(1),
                $"instance {tracked.value} disposed {tracked.disposeCount} times");
        }
    }
}
