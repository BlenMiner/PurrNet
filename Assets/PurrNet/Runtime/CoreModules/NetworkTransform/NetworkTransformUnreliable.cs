using System;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;

namespace PurrNet.Modules
{
    /// <summary>
    /// Legacy NetworkTransform packet retained for source and binary compatibility.
    /// The built-in NetworkTransform module now uses the acknowledged unreliable stream below.
    /// </summary>
    public struct NetworkTransformDelta : IPackedAuto
    {
        public SceneID scene;
        public readonly ByteData packet;

        public NetworkTransformDelta(SceneID context, BitPacker packer)
        {
            scene = context;
            packet = packer.ToByteData();
        }
    }

    internal struct NetworkTransformUnreliableDelta : IPackedAuto
    {
        public SceneID scene;
        public readonly ushort seq;
        public readonly ushort tick;
        public NetworkTransformUnreliableAckHeader? ack;
        public readonly ByteData packet;

        public NetworkTransformUnreliableDelta(SceneID context, ushort seq, ushort tick, BitPacker packer)
        {
            scene = context;
            this.seq = seq;
            this.tick = tick;
            ack = null;
            packet = packer.ToByteData();
        }
    }

    internal struct NetworkTransformUnreliableAckHeader : IPackedAuto
    {
        public ushort seq;
        public uint ackBits;
    }

    internal struct NetworkTransformUnreliableAck : IPackedAuto
    {
        public SceneID scene;
        public ushort seq;
        public uint ackBits;
    }

    internal struct NetworkTransformUnreliableNack : IPackedAuto
    {
        public SceneID scene;
        public NetworkID id;
    }

    internal struct NTUnreliableEntry
    {
        public NetworkID nid;
        public NetworkTransformState state;
        public NetworkTransformVelocity velocity;
        public ushort tick;
        public byte gen;
        // Send-side only: non-wrapping epoch behind the byte gen.
        public uint genEpoch;
        // Send-side only: revision of the captured state, used for a cheap unchanged check.
        public uint revision;
        // Send-side only: the transform that wrote this entry, so ack adoption skips the registry search.
        public NetworkTransform transform;
    }

    internal struct NTBaselineSlot
    {
        public bool has;
        public NetworkTransformState state;
        public NetworkTransformVelocity velocity;
        public ushort tick;
        public byte gen;
        public uint genEpoch;
        public uint revision;
        // Monotonic packet order — ushort seq wraps, so age math uses this instead.
        public uint order;
    }

    internal enum NTWriteResult
    {
        Written,
        SkipAcked,
        Hold
    }

    internal class NTLastAdaptiveWrite
    {
        public ushort tick;
        public ushort prevTick;
        public ushort prevPrevTick;
        public uint revision;
        public NetworkTransformState state;
        public NetworkTransformState prevState;
        public NetworkTransformState prevPrevState;
        public byte refreshInterval;
        public bool hasPrev;
        public bool hasPrevPrev;
        public bool restConfirmed;
        public byte redundancy;
    }

    internal struct NTUnreliableGeneration
    {
        public byte gen;
        public uint epoch;
    }

    internal struct NTUnreliableSlot
    {
        public bool used;
        public bool acked;
        public bool anchor;
        public ushort seq;
        public uint order;
        public List<NTUnreliableEntry> entries;
    }

    internal class NTUnreliableSendStream
    {
        public bool asServer;
        public ushort nextSeq = 1;
        public uint nextOrder = 1;
        public long budgetBits;
        // Sorted by NetworkID. Once initialized, only transforms with an unacknowledged
        // revision remain here, avoiding a full visible-transform scan every tick.
        public bool pendingInitialized;
        public readonly List<NetworkTransform> pending = new();
        // NACK barrier: only packets written AFTER the NACK may re-establish a baseline,
        // else the ack covering the NACKed packet resurrects the phantom (acks are cumulative).
        public readonly Dictionary<NetworkID, uint> nackFloor = new();
        public NTBaselineSlot[] baselines = System.Array.Empty<NTBaselineSlot>();
        public bool[] pendingByIndex = System.Array.Empty<bool>();
        public bool[] ackCompleted = System.Array.Empty<bool>();
        public NTLastAdaptiveWrite[] adaptiveByIndex = System.Array.Empty<NTLastAdaptiveWrite>();

        public void EnsureBaselineCapacity(int count)
        {
            if (baselines.Length >= count)
                return;
            int size = System.Math.Max(count, System.Math.Max(16, baselines.Length * 2));
            System.Array.Resize(ref baselines, size);
            System.Array.Resize(ref pendingByIndex, size);
            System.Array.Resize(ref ackCompleted, size);
            System.Array.Resize(ref adaptiveByIndex, size);
        }

        public NTLastAdaptiveWrite GetAdaptive(NetworkTransform nt)
        {
            int index = nt.GetNTIndex(asServer);
            return index >= 0 && index < adaptiveByIndex.Length ? adaptiveByIndex[index] : null;
        }

        public void SetAdaptive(NetworkTransform nt, NTLastAdaptiveWrite value)
        {
            int index = nt.GetNTIndex(asServer);
            if (index >= 0 && index < adaptiveByIndex.Length)
                adaptiveByIndex[index] = value;
        }

        public bool IsPending(NetworkTransform nt)
        {
            int index = nt.GetNTIndex(asServer);
            return index >= 0 && index < pendingByIndex.Length && pendingByIndex[index];
        }

        public void SetPending(NetworkTransform nt, bool value)
        {
            int index = nt.GetNTIndex(asServer);
            if (index >= 0 && index < pendingByIndex.Length)
                pendingByIndex[index] = value;
        }
        // A targeted reliable reset advances the NetworkTransform's global generation, while
        // unaffected peers remain on their existing wire generation until the next global reset.
        public readonly Dictionary<NetworkID, NTUnreliableGeneration> generationOverrides = new();
        public readonly NTUnreliableSlot[] ring = new NTUnreliableSlot[NTUnreliable.RING_SIZE];
    }

    internal class NTUnreliableRecvStream
    {
        public bool ackInit;
        public ushort latestSeq;
        public long latestOrder;
        public uint ackBits;
        public bool ackDirty;
        public byte ackDelayTicks;
        public byte packetsSinceAck;
        public bool offsetInit;
        public ushort offsetRef;
        public short devMaxA = short.MinValue;
        public short devMaxB = short.MinValue;
        public uint bucketStart;
        public short frozenDevMax;
        public bool hasFrozenDev;
        public ushort vouchedTick;
        public bool vouchedValid;
        public readonly NTUnreliableSlot[] ring = new NTUnreliableSlot[NTUnreliable.RING_SIZE];
    }

    internal static class NTUnreliable
    {
        public const int DISTANCE_BITS = 8;
        public const int RING_SIZE = 1 << DISTANCE_BITS;
        // A baseline remains usable for the full receive history. Prediction has a smaller bound:
        // beyond it, rotation extrapolation can overflow NormalizedFloat's delta prefix budget, so
        // both peers deterministically encode against the raw baseline instead.
        public const int MAX_BASELINE_AGE = RING_SIZE;
        public const int MAX_PREDICTED_BASELINE_AGE = 48;
        // Low-volume streams do not need an application-level ACK every network tick. High-volume
        // streams flush before the 32-packet selective-ACK window can leave a permanent blind spot.
        public const int ACK_INTERVAL_TICKS = 4;
        public const int ACK_PACKET_THRESHOLD = 24;

        /// <summary>
        /// Packets sent on ticks that are a multiple of this become the only ones that move a
        /// peer's delta baseline forward once it has one. Every peer therefore converges on the
        /// same baseline packet for every transform, so the per-peer share keys match and one
        /// encode serves the whole audience instead of one encode per peer. Acks arrive at
        /// most one ack interval after an anchor, so this equals the ack cadence.
        /// </summary>
        public const int BASELINE_ANCHOR_TICKS = ACK_INTERVAL_TICKS;

        public static bool IsAnchorTick(ushort tick) => tick % BASELINE_ANCHOR_TICKS == 0;

        public const int ADAPTIVE_MAX_BACKFILL = 34;
        public const int ADAPTIVE_POS_TOLERANCE = 2;
        public const int ADAPTIVE_ROT_TOLERANCE = 4;
        public const int ADAPTIVE_SCALE_TOLERANCE = 2;

        public const byte BREAK_REDUNDANCY = 2;
        public const int REDUNDANCY_INTERVAL = 2;
        public const int VOUCH_SLACK_TICKS = 1;

        public static NetworkTransformState GetDeltaPrediction(in NetworkTransformState baseline,
            in NetworkTransformVelocity velocity, int distance)
        {
            return distance <= MAX_PREDICTED_BASELINE_AGE
                ? NetworkTransformVelocity.Predict(baseline, velocity, distance)
                : baseline;
        }

        public static bool ShouldApplyOrder(bool hasApplied, long lastApplied, long incoming)
        {
            return !hasApplied || incoming > lastApplied;
        }

        private static long ScaledTolerance(long baseTolerance, long velocityComponent, int shift, long capMultiplier,
            int driftSteps)
        {
            long scaled = (Math.Abs(velocityComponent) >> (NetworkTransformVelocity.FRACTION_BITS + shift)) *
                          driftSteps;

            if (capMultiplier > 0)
            {
                long cap = baseTolerance * capMultiplier;
                if (scaled > cap)
                    scaled = cap;
            }

            return scaled > baseTolerance ? scaled : baseTolerance;
        }

        public static bool PredictionMatches(in NetworkTransformState predicted, in NetworkTransformState current,
            in NetworkTransformVelocity velocity, int toleranceShift, long toleranceCap, int driftSteps = 1)
        {
            if (driftSteps < 1)
                driftSteps = 1;

            var p = predicted.data;
            var c = current.data;

            if (p.absolutePosition.HasValue || c.absolutePosition.HasValue)
            {
                if (!p.absolutePosition.HasValue || !c.absolutePosition.HasValue)
                    return false;
                if (!p.absolutePosition.Value.Equals(c.absolutePosition.Value))
                    return false;
            }
            else
            {
                if (p.position.HasValue != c.position.HasValue)
                    return false;

                if (p.position.HasValue)
                {
                    var pp = p.position.Value;
                    var cp = c.position.Value;
                    if (Math.Abs(pp.x.rounded - (long)cp.x.rounded) > ScaledTolerance(ADAPTIVE_POS_TOLERANCE, velocity.posX, toleranceShift, toleranceCap, driftSteps) ||
                        Math.Abs(pp.y.rounded - (long)cp.y.rounded) > ScaledTolerance(ADAPTIVE_POS_TOLERANCE, velocity.posY, toleranceShift, toleranceCap, driftSteps) ||
                        Math.Abs(pp.z.rounded - (long)cp.z.rounded) > ScaledTolerance(ADAPTIVE_POS_TOLERANCE, velocity.posZ, toleranceShift, toleranceCap, driftSteps))
                        return false;
                }
            }

            if (Math.Abs(p.rotation.x.value - (long)c.rotation.x.value) > ScaledTolerance(ADAPTIVE_ROT_TOLERANCE, velocity.rotX, toleranceShift, toleranceCap, driftSteps) ||
                Math.Abs(p.rotation.y.value - (long)c.rotation.y.value) > ScaledTolerance(ADAPTIVE_ROT_TOLERANCE, velocity.rotY, toleranceShift, toleranceCap, driftSteps) ||
                Math.Abs(p.rotation.z.value - (long)c.rotation.z.value) > ScaledTolerance(ADAPTIVE_ROT_TOLERANCE, velocity.rotZ, toleranceShift, toleranceCap, driftSteps) ||
                Math.Abs(p.rotation.w.value - (long)c.rotation.w.value) > ScaledTolerance(ADAPTIVE_ROT_TOLERANCE, velocity.rotW, toleranceShift, toleranceCap, driftSteps))
                return false;

            if (Math.Abs(p.scale.x.rounded - (long)c.scale.x.rounded) > ScaledTolerance(ADAPTIVE_SCALE_TOLERANCE, velocity.scaleX, toleranceShift, toleranceCap, driftSteps) ||
                Math.Abs(p.scale.y.rounded - (long)c.scale.y.rounded) > ScaledTolerance(ADAPTIVE_SCALE_TOLERANCE, velocity.scaleY, toleranceShift, toleranceCap, driftSteps) ||
                Math.Abs(p.scale.z.rounded - (long)c.scale.z.rounded) > ScaledTolerance(ADAPTIVE_SCALE_TOLERANCE, velocity.scaleZ, toleranceShift, toleranceCap, driftSteps))
                return false;

            return true;
        }

        public static void Release(NTUnreliableSlot[] ring)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                if (ring[i].entries != null)
                    ListPool<NTUnreliableEntry>.Destroy(ring[i].entries);
                ring[i] = default;
            }
        }
    }
}
