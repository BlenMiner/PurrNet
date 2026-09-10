using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    public partial class NetworkRigidbody
    {
        internal AppliedForce EncodeForceAtPosition(Vector3 force, Vector3 worldPosition, ForceMode mode)
        {
            var data = new AppliedForce { force = force, mode = mode };
            if (_positionTransform != null)
                data.absolutePosition = _positionTransform.ToAbsolute(this, worldPosition);
            else
                data.position = (CompressedVector3)worldPosition;
            return data;
        }

        internal bool TryResolveForcePosition(in AppliedForce force, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (force.absolutePosition.HasValue)
            {
                // Resolve on receipt: the origin can change while the request is in flight.
                if (_positionTransform == null || !IsFinite(force.absolutePosition.Value))
                    return false;
                worldPosition = _positionTransform.ToLocal(this, force.absolutePosition.Value);
            }
            else if (force.position.HasValue)
            {
                // A legacy caller's world-space point must never be decoded as absolute.
                worldPosition = force.position.Value;
            }
            else
            {
                return false;
            }

            return IsFinite(worldPosition);
        }
    }
}
