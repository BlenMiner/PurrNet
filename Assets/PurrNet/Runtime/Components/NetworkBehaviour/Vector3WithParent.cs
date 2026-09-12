using Unity.Mathematics;
using UnityEngine;

namespace PurrNet
{
    public readonly struct Vector3WithParent
    {
        readonly Transform parent;
        readonly bool isLocalPos;
        readonly bool isAbsolute;
        readonly INetworkTransformPositionTransform posTransform;
        readonly NetworkTransform self;

        readonly double3 value;
        public readonly Vector3 position;

        /// <summary>World or parent-local-frame sample.</summary>
        public Vector3WithParent(Transform parent, bool isLocalPos, Vector3 position)
        {
            this.parent = parent;
            this.isLocalPos = isLocalPos;
            this.isAbsolute = false;
            this.posTransform = null;
            this.self = null;
            this.value = new double3(position.x, position.y, position.z);
            this.position = position;
        }

        /// <summary>Absolute peer-agnostic sample (origin-invariant).</summary>
        public Vector3WithParent(NetworkTransform self, INetworkTransformPositionTransform posTransform, double3 absolutePosition)
        {
            this.parent = null;
            this.isLocalPos = false;
            this.isAbsolute = true;
            this.posTransform = posTransform;
            this.self = self;
            this.value = absolutePosition;
            this.position = posTransform.ToLocal(self, absolutePosition);
        }

        Vector3 ResolveWorld()
        {
            if (isAbsolute)
                return posTransform.ToLocal(self, value);

            var p = new Vector3((float)value.x, (float)value.y, (float)value.z);
            if (isLocalPos)
                return parent ? parent.TransformPoint(p) : p;
            return p;
        }

        public static Vector3WithParent Lerp(Vector3WithParent a, Vector3WithParent b, float t)
        {
            if (b.isAbsolute)
            {
                // a.value is only comparable when a is also absolute; a mixed pair snaps to b.
                var lerpedAbs = a.isAbsolute ? math.lerp(a.value, b.value, t) : b.value;
                return new Vector3WithParent(b.self, b.posTransform, lerpedAbs);
            }

            var aWorldPos = a.ResolveWorld();
            var bWorldPos = b.ResolveWorld();
            return new Vector3WithParent(default, false, Vector3.Lerp(aWorldPos, bWorldPos, t));
        }

        public static Vector3WithParent NoLerp(Vector3WithParent a, Vector3WithParent b, float t)
        {
            if (b.isAbsolute)
                return b;

            return new Vector3WithParent(default, false, b.ResolveWorld());
        }
    }
}
