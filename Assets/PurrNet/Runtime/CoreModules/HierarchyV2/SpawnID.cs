using System;
using PurrNet.Packing;

namespace PurrNet.Modules
{
    public struct SpawnID : IEquatable<SpawnID>, IPackedAuto
    {
        readonly PackedULong packetIdx;
        [DontPack] public readonly PlayerID target;
        public PlayerID scope;

        public SpawnID(PackedULong packetIdx, PlayerID target, PlayerID? scope)
        {
            this.packetIdx = packetIdx;
            this.target = target;
            this.scope = scope.GetValueOrDefault();
        }

        public SpawnID WithTarget(PlayerID newTarget) => new SpawnID(packetIdx, newTarget, scope);

        public bool SameWire(in SpawnID other) => packetIdx == other.packetIdx && scope.Equals(other.scope);

        public bool Equals(SpawnID other)
        {
            return packetIdx == other.packetIdx && target.Equals(other.target) && scope.Equals(other.scope);
        }

        public override bool Equals(object obj)
        {
            return obj is SpawnID other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(packetIdx, target, scope);
        }

        public override string ToString()
        {
            return $"SpawnID: {{ packetIdx: {packetIdx}, player: {target}, scope: {scope} }}";
        }
    }
}
