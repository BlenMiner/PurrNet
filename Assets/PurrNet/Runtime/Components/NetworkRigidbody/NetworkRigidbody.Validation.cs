using PurrNet.Logging;
using Unity.Mathematics;
using UnityEngine;

namespace PurrNet
{
    public partial class NetworkRigidbody
    {
        private const float HalfVectorMax = 65504f;
        private const float HalfVectorWarningThreshold = HalfVectorMax * 0.5f;

        private bool _invalidOutboundStateReported;
        private bool _invalidInboundStateReported;
        private bool _invalidCorrectionStateReported;
        private bool _largePackedVelocityReported;
        private bool _hasControllerStateSample;
        private Vector3 _previousControllerPosition;
        private double3 _previousControllerSyncPosition;
        private Vector3 _previousControllerLinearVelocity;
        private Vector3 _previousControllerAngularVelocity;
        private int _previousControllerFrame;
        private float _previousControllerFixedTime;

        private bool IsCurrentRigidbodyStateFinite()
        {
            return IsFinite(_rigidbody.position)
                   && IsFinite(_rigidbody.rotation)
                   && IsFinite(GetLinearVelocity())
                   && IsFinite(_rigidbody.angularVelocity);
        }

        private bool ValidateLocalStateForSync(string source)
        {
            var parentIdentity = GetSyncParentIdentity(out var isSoftParent);
            var parent = parentIdentity ? parentIdentity.transform : null;
            var localPosition = _rigidbody.position;
            var localRotation = _rigidbody.rotation;
            var localLinearVelocity = GetLinearVelocity();
            var localAngularVelocity = _rigidbody.angularVelocity;
            var syncPosition = ReadSyncPosition(parent);
            var syncRotation = ReadRotation(parent);
            var syncLinearVelocity = ReadLinearVelocity(parent);
            var syncAngularVelocity = ReadAngularVelocity(parent);

            bool finite = IsFinite(localPosition)
                          && IsFinite(localRotation)
                          && IsFinite(localLinearVelocity)
                          && IsFinite(localAngularVelocity)
                          && IsFinite(syncPosition)
                          && IsFinite(syncRotation)
                          && IsFinite(syncLinearVelocity)
                          && IsFinite(syncAngularVelocity);
            bool packable = IsHalfPackable(syncLinearVelocity) && IsHalfPackable(syncAngularVelocity);

            if (!finite || !packable)
            {
                if (!_invalidOutboundStateReported)
                {
                    string reason = finite
                        ? "velocity exceeds HalfVector3 range"
                        : "state contains NaN or Infinity";
                    PurrLogger.LogError(
                        $"Rejected NetworkRigidbody state before sync because {reason}.\n" +
                        $"source={source} {GetNetworkContext(parentIdentity, isSoftParent)}\n" +
                        $"local: pos={localPosition} rot={localRotation} velocity={localLinearVelocity} angularVelocity={localAngularVelocity}\n" +
                        $"sync: pos={syncPosition} rot={syncRotation} velocity={syncLinearVelocity} angularVelocity={syncAngularVelocity}\n" +
                        GetPreviousControllerSample(), this);
                }

                _invalidOutboundStateReported = true;
                return false;
            }

            _invalidOutboundStateReported = false;

            bool largePackedVelocity = ExceedsHalfWarningThreshold(syncLinearVelocity)
                                       || ExceedsHalfWarningThreshold(syncAngularVelocity);
            if (largePackedVelocity && !_largePackedVelocityReported)
            {
                PurrLogger.LogWarning(
                    $"NetworkRigidbody velocity is approaching HalfVector3 range.\n" +
                    $"source={source} {GetNetworkContext(parentIdentity, isSoftParent)}\n" +
                    $"local: pos={localPosition} velocity={localLinearVelocity} angularVelocity={localAngularVelocity}\n" +
                    $"sync: pos={syncPosition} velocity={syncLinearVelocity} angularVelocity={syncAngularVelocity}\n" +
                    GetPreviousControllerSample(), this);
            }

            _largePackedVelocityReported = largePackedVelocity;
            _hasControllerStateSample = true;
            _previousControllerPosition = localPosition;
            _previousControllerSyncPosition = syncPosition;
            _previousControllerLinearVelocity = localLinearVelocity;
            _previousControllerAngularVelocity = localAngularVelocity;
            _previousControllerFrame = Time.frameCount;
            _previousControllerFixedTime = Time.fixedTime;
            return true;
        }

        private bool ValidateOutgoingSnapshot(in RigidbodyStateData data, string source)
        {
            return ValidateSnapshot(in data, source, false);
        }

        private bool ValidateIncomingSnapshot(in RigidbodyStateData data, string source)
        {
            return ValidateSnapshot(in data, source, true);
        }

        private bool ValidateSnapshot(in RigidbodyStateData data, string source, bool incoming)
        {
            bool validFrame = IsValidFrame(data.positionFrame);
            var syncPosition = ExtractSyncPosition(data.positionFrame, data.position, data.absolutePosition);
            Quaternion rotation = data.rotation;
            Vector3 linearVelocity = data.linearVelocity;
            Vector3 angularVelocity = data.angularVelocity;
            bool valid = validFrame
                         && IsFinite(syncPosition)
                         && IsFinite(rotation)
                         && IsFinite(linearVelocity)
                         && IsFinite(angularVelocity)
                         && IsFinite(data.time);

            if (valid)
            {
                if (incoming)
                    _invalidInboundStateReported = false;
                else
                    _invalidOutboundStateReported = false;
                return true;
            }

            bool reported = incoming ? _invalidInboundStateReported : _invalidOutboundStateReported;
            if (!reported)
            {
                string direction = incoming ? "incoming" : "outgoing";
                PurrLogger.LogError(
                    $"Rejected {direction} NetworkRigidbody state because it contains invalid data.\n" +
                    $"source={source} {GetNetworkContext(data.parent, data.isSoftParent)}\n" +
                    $"frame={data.positionFrame} position={syncPosition} rotation={rotation} velocity={linearVelocity} angularVelocity={angularVelocity} senderTime={data.time:F6}\n" +
                    GetCurrentRigidbodyState(), this);
            }

            if (incoming)
                _invalidInboundStateReported = true;
            else
                _invalidOutboundStateReported = true;
            return false;
        }

        private bool TryGetCorrectionErrors(
            Vector3 worldTargetPosition,
            Quaternion worldTargetRotation,
            Vector3 worldTargetLinearVelocity,
            Vector3 worldTargetAngularVelocity,
            out float positionError,
            out float rotationError)
        {
            positionError = 0f;
            rotationError = 0f;

            bool valid = IsFinite(_targetPosition)
                         && IsFinite(_targetRotation)
                         && IsFinite(_targetLinearVelocity)
                         && IsFinite(_targetAngularVelocity)
                         && IsFinite(worldTargetPosition)
                         && IsFinite(worldTargetRotation)
                         && IsFinite(worldTargetLinearVelocity)
                         && IsFinite(worldTargetAngularVelocity)
                         && IsFinite(_rigidbody.position)
                         && IsFinite(_rigidbody.rotation)
                         && IsFinite(GetLinearVelocity())
                         && IsFinite(_rigidbody.angularVelocity);

            if (valid)
            {
                positionError = GetPositionError(worldTargetPosition);
                rotationError = Quaternion.Angle(_rigidbody.rotation, NormalizeQuaternion(worldTargetRotation));
                valid = IsFinite(positionError) && IsFinite(rotationError);
            }

            if (valid)
            {
                _invalidCorrectionStateReported = false;
                return true;
            }

            if (!_invalidCorrectionStateReported)
            {
                PurrLogger.LogError(
                    $"Rejected NetworkRigidbody correction because its state contains NaN or Infinity.\n" +
                    $"{GetNetworkContext(_targetParent ? _targetParent.GetComponent<NetworkIdentity>() : null, _softParent != null)}\n" +
                    $"target sync: pos={_targetPosition} rot={_targetRotation} velocity={_targetLinearVelocity} angularVelocity={_targetAngularVelocity}\n" +
                    $"target world: pos={worldTargetPosition} rot={worldTargetRotation} velocity={worldTargetLinearVelocity} angularVelocity={worldTargetAngularVelocity}\n" +
                    $"buffer={_bufferCount}/{BUFFER_SIZE} sample={_bufferSampleMode} latestRaw={_latestRawSnapshotPos}\n" +
                    GetCurrentRigidbodyState(), this);
            }

            _invalidCorrectionStateReported = true;
            ResetCorrectionTargetToRigidbody();
            return false;
        }

        private void ResetCorrectionTargetToRigidbody()
        {
            ClearBuffer();

            var parentIdentity = GetSyncParentIdentity(out _);
            var parent = parentIdentity ? parentIdentity.transform : null;
            _targetPosition = ReadSyncPosition(parent);
            _targetRotation = ReadRotation(parent);
            _targetLinearVelocity = ReadLinearVelocity(parent);
            _targetAngularVelocity = ReadAngularVelocity(parent);
            _targetParent = parent;
            _targetPositionFrame = GetPositionFrame(parent);
            _latestRawSnapshotPos = _targetPosition;
            _latestRawSnapshotParent = parent;
            _latestRawSnapshotFrame = _targetPositionFrame;
            _prePredictionTarget = _targetPosition;
            _predictionOffset = 0f;
            _bufferSampleMode = "Invalid state rejected";
            _lastCorrectionReason = "Invalid state rejected";
        }

        private string GetNetworkContext(NetworkIdentity parent, bool isSoftParent)
        {
            string networkId = id.HasValue ? id.Value.ToString() : "<null>";
            string ownerId = owner.HasValue ? owner.Value.ToString() : "<null>";
            string localId = localPlayer.HasValue ? localPlayer.Value.ToString() : "<null>";
            string positionTransformName = _positionTransform?.GetType().FullName ?? "<null>";
            string parentName = parent ? parent.name : "<null>";
            return $"object={name} scene={sceneId} networkId={networkId} objectId={objectId} owner={ownerId} local={localId} server={isServer} controller={IsController(_ownerAuth)} ownerAuth={_ownerAuth} parent={parentName} softParent={isSoftParent} positionTransform={positionTransformName} frame={Time.frameCount} fixedTime={Time.fixedTime:F3}";
        }

        private string GetCurrentRigidbodyState()
        {
            if (!_rigidbody)
                return "rigidbody=<null>";

            return $"rigidbody: pos={_rigidbody.position} rot={_rigidbody.rotation} velocity={GetLinearVelocity()} angularVelocity={_rigidbody.angularVelocity} kinematic={_rigidbody.isKinematic} sleeping={_rigidbody.IsSleeping()}";
        }

        private string GetPreviousControllerSample()
        {
            if (!_hasControllerStateSample)
                return "previous controller sample=<none>";

            return $"previous controller sample: frame={_previousControllerFrame} fixedTime={_previousControllerFixedTime:F3} localPos={_previousControllerPosition} syncPos={_previousControllerSyncPosition} velocity={_previousControllerLinearVelocity} angularVelocity={_previousControllerAngularVelocity}";
        }

        private static bool IsValidFrame(RigidbodyPositionFrame frame)
        {
            return frame == RigidbodyPositionFrame.ParentLocal
                   || frame == RigidbodyPositionFrame.Absolute
                   || frame == RigidbodyPositionFrame.World;
        }

        private static bool IsHalfPackable(Vector3 value)
        {
            return Mathf.Abs(value.x) <= HalfVectorMax
                   && Mathf.Abs(value.y) <= HalfVectorMax
                   && Mathf.Abs(value.z) <= HalfVectorMax;
        }

        private static bool ExceedsHalfWarningThreshold(Vector3 value)
        {
            return Mathf.Abs(value.x) > HalfVectorWarningThreshold
                   || Mathf.Abs(value.y) > HalfVectorWarningThreshold
                   || Mathf.Abs(value.z) > HalfVectorWarningThreshold;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(double3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
