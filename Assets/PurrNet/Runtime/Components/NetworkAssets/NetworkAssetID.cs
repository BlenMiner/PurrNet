using System;
using PurrNet.Packing;

namespace PurrNet
{
    public readonly struct NetworkAssetID : IEquatable<NetworkAssetID>, IPackedAuto
    {
        public readonly SceneID? scope;

        public readonly PackedInt value;

        public static NetworkAssetID invalid => new NetworkAssetID(-1);

        public bool isValid => value >= 0;

        public bool isSceneScoped => scope.HasValue;

        public NetworkAssetID(int value)
        {
            this.value = value;
            this.scope = null;
        }

        public NetworkAssetID(int value, SceneID scope)
        {
            this.value = value;
            this.scope = scope;
        }

        public static implicit operator NetworkAssetID(int value) => new NetworkAssetID(value);

        public static explicit operator int(NetworkAssetID id) => id.value;

        public bool Equals(NetworkAssetID other)
        {
            return value == other.value && scope == other.scope;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkAssetID other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(value.value, scope);
        }

        public static bool operator ==(NetworkAssetID a, NetworkAssetID b) => a.Equals(b);

        public static bool operator !=(NetworkAssetID a, NetworkAssetID b) => !a.Equals(b);

        public override string ToString()
        {
            return scope.HasValue ? $"{value}@{scope.Value}" : value.ToString();
        }
    }
}
