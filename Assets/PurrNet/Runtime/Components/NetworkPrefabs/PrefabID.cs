using System;
using PurrNet.Packing;

namespace PurrNet
{
    public readonly struct PrefabID : IEquatable<PrefabID>, IPackedAuto
    {
        public readonly SceneID? scope;

        public readonly PackedInt value;

        public static PrefabID invalid => new PrefabID(-1);

        public bool isValid => value >= 0;

        public bool isSceneScoped => scope.HasValue;

        public PrefabID(int value)
        {
            this.value = value;
            this.scope = null;
        }

        public PrefabID(int value, SceneID scope)
        {
            this.value = value;
            this.scope = scope;
        }

        public static implicit operator PrefabID(int value) => new PrefabID(value);

        public static explicit operator int(PrefabID id) => id.value;

        public bool Equals(PrefabID other)
        {
            return value == other.value && scope == other.scope;
        }

        public override bool Equals(object obj)
        {
            return obj is PrefabID other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(value.value, scope);
        }

        public static bool operator ==(PrefabID a, PrefabID b) => a.Equals(b);

        public static bool operator !=(PrefabID a, PrefabID b) => !a.Equals(b);

        public override string ToString()
        {
            return scope.HasValue ? $"{value}@{scope.Value}" : value.ToString();
        }
    }
}
