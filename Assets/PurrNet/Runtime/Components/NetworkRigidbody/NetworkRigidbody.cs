using System;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Transports;
using Unity.Mathematics;
using UnityEngine;

namespace PurrNet
{
    public enum RigidbodyTransformSpace : byte
    {
        Local = 0,
        World = 1
    }

    /// <summary>
    /// How a receiver drives its rigidbody towards the synced rotation.
    /// </summary>
    public enum RigidbodyRotationCorrection : byte
    {
        /// <summary>
        /// Kinematic when the body has any rotation axis constrained, torque otherwise.
        /// Constraining a rotation axis means the rotation is authored rather than simulated,
        /// and torque cannot reproduce authored rotation without lag.
        /// </summary>
        Auto = 0,
        /// <summary>Chase the target rotation with a torque spring. Rotation stays physically reactive but lags behind fast authored turns.</summary>
        Torque = 1,
        /// <summary>Follow the target rotation exactly with MoveRotation. No lag and no jitter, but remote bodies no longer spin from local collisions.</summary>
        Kinematic = 2
    }

    /// <summary>
    /// Reference frame a wire position was encoded in. Travels with the state so
    /// the receiver decodes the correct field regardless of whether the parent
    /// identity reference has resolved yet, or whether a position transform is
    /// installed on the receiving peer.
    /// </summary>
    public enum RigidbodyPositionFrame : byte
    {
        /// <summary>Parent-local; the value lives in the <c>position</c> field.</summary>
        ParentLocal = 0,
        /// <summary>Peer-agnostic absolute (via a position transform); the value lives in <c>absolutePosition</c>.</summary>
        Absolute = 1,
        /// <summary>Raw Unity world space; the value lives in the <c>position</c> field.</summary>
        World = 2
    }

    public struct AppliedForce
    {
        public HalfVector3 force;
        /// <summary>Legacy Unity world-space application point, used without an origin converter.</summary>
        public CompressedVector3? position;
        public ForceMode mode;
        public bool isTorque;
        /// <summary>Origin-invariant application point. When present, takes precedence over
        /// <see cref="position"/> and is decoded using the receiving peer's current origin.</summary>
        public double3? absolutePosition;
    }

    public struct RigidbodyStateData
    {
        /// <summary>Quantized position. Carries the value for the ParentLocal and
        /// World frames; default otherwise.</summary>
        public CompressedVector3 position;
        /// <summary>Absolute peer-agnostic position. Carries the value for the
        /// Absolute frame; default otherwise. Delta-packed, so it costs nothing
        /// on the legacy path.</summary>
        public double3 absolutePosition;
        /// <summary>Frame the position fields were encoded in. Decode keys on this
        /// rather than on parent-reference resolution or receiver-side state.</summary>
        public RigidbodyPositionFrame positionFrame;
        public PackedQuaternion rotation;
        public HalfVector3 linearVelocity;
        public HalfVector3 angularVelocity;
        public NetworkIdentity parent;
        /// <summary>True when <see cref="parent"/> is a soft-parent override the receiver should
        /// adopt as its own (vs the real Unity parent). Delta-packs away when unused.</summary>
        public bool isSoftParent;
        /// <summary>Sender's unscaled clock at capture time. Receivers map it onto their
        /// own clock so snapshot spacing reflects the send cadence instead of arrival
        /// timing; relays forward it untouched. 0 means unstamped (legacy sender).</summary>
        public double time;
        /// <summary>Monotonic state order. Controllers assign source order and the server replaces it
        /// with canonical relay order. Zero is reserved for an authority anchor.</summary>
        public uint sequence;
        /// <summary>Canonical controller generation assigned by the server before relay.</summary>
        public uint authorityEpoch;
    }

    public struct RigidbodyTeleportData
    {
        public CompressedVector3 position;
        public double3 absolutePosition;
        public RigidbodyPositionFrame positionFrame;
        public PackedQuaternion rotation;
        public HalfVector3 linearVelocity;
        public HalfVector3 angularVelocity;
        public NetworkIdentity parent;
        public bool isSoftParent;
    }

    public struct RigidbodySettingsData
    {
        public Half mass;
        public Half drag;
        public Half angularDrag;
        public bool useGravity;
        public bool isKinematic;
    }

    struct TimestampedSnapshot
    {
        public double time;
        /// <summary>Position in the sync frame: parent-local when parented,
        /// otherwise the absolute peer-agnostic frame (origin-invariant either
        /// way, so it survives a local origin shift).</summary>
        public double3 position;
        public RigidbodyPositionFrame positionFrame;
        public Quaternion rotation;
        public Vector3 linearVelocity;
        public Vector3 angularVelocity;
        public Transform parent;
    }

    [AddComponentMenu("PurrNet/Network Rigidbody")]
    public partial class NetworkRigidbody : NetworkIdentity, ITick
    {
        [Header("Authority")]
        [Tooltip("If true, the client owning the object calculates physics (Client Auth). If false, the server calculates physics (Server Auth).")]
        [SerializeField] private bool _ownerAuth = true;

        [Tooltip("Space used to sync position and rotation. Local is relative to the current parent, World is absolute.")]
        [SerializeField] private RigidbodyTransformSpace _space = RigidbodyTransformSpace.Local;

        [Tooltip("Whether to sync parent changes (SetParent) through the hierarchy. Only works when the new parent has a NetworkIdentity.")]
        [SerializeField] private bool _syncParent = true;

        // Serialized only so existing prefabs keep their old data without being rewritten.
        // Parent-local velocity semantics are automatic and never branch on this value.
#pragma warning disable 0414
        [SerializeField, HideInInspector] private bool _syncVelocityRelativeToParent = true;
#pragma warning restore 0414

        [Tooltip("If true, position error for correction and hard-snap decisions is measured in the parent or soft-parent frame when active.")]
        [SerializeField] private bool _useParentFramePositionError;

        [Header("Settings Override")]
        [Tooltip("Optional. When assigned, this asset's Create() builds a per-instance correction object that controls all correction decisions. The fields below are passed as defaults via the correction context.")]
        [SerializeField] private NetworkRigidbodySettings _settingsOverride;

        private NetworkRigidbodySettingsInstance _settingsInstance;
        private NetworkRigidbodySettings _settingsInstanceSource;

        [Header("Correction")]
        [Tooltip("How far behind real-time (in seconds) the interpolation target sits. Higher values absorb more jitter but add latency.")]
        [SerializeField] private float _interpolationDelay = 0.05f;

        [Tooltip("Raises the interpolation delay to one send interval when the configured value is smaller than that, since below that there is never a newer snapshot to interpolate towards. This is a floor against misconfiguration, not a recommendation: budget roughly two send intervals if you want headroom for jitter.")]
        [SerializeField] private bool _autoInterpolationDelay = true;

        [Tooltip("Pushes the target position forward using velocity and estimated acceleration. The offset is interpolationDelay * predictionFactor, making it identical on all machines regardless of network role. 0 = no prediction, 1 = compensate for interpolation delay, >1 = predict further ahead.")]
        [SerializeField] private float _predictionFactor;

        [Tooltip("Maximum time in seconds a receiver can extrapolate from the newest snapshot before holding the target near that snapshot.")]
        [SerializeField] private float _maxExtrapolationDuration = 0.25f;

        [Tooltip("How aggressively the rigidbody chases the target position. Acts as the natural frequency of a critically-damped spring.")]
        [SerializeField] private float _positionStrength = 5f;

        [Tooltip("The distance over which position correction ramps from zero to full strength. Larger values give softer correction, letting local collisions play out before being pulled back.")]
        [SerializeField] private float _correctionRange = 2f;

        [Tooltip("How a receiver drives its rigidbody towards the synced rotation. Auto picks Kinematic for bodies with constrained rotation axes (characters) and Torque for freely rotating bodies (props).")]
        [SerializeField] private RigidbodyRotationCorrection _rotationCorrection = RigidbodyRotationCorrection.Auto;

        [Tooltip("How aggressively the rigidbody corrects rotation, as the natural frequency of a critically-damped spring in radians per second. Only used by the Torque rotation mode.")]
        [SerializeField] private float _rotationStrength = 12f;

        [Tooltip("If the position error exceeds this distance, teleport instead of using forces.")]
        [SerializeField] private float _hardSnapDistance = 3f;

        [Tooltip("If true, resets the rigidbody linear velocity once the hard snap distance is exceeded.")]
        [SerializeField] private bool _resetLinearVelocityOnSnap = false;

        [Tooltip("If the rotation error (degrees) exceeds this threshold, snap rotation instead of using torque. Rotation error can never exceed 180 degrees, so values above 175 are clamped to 175. Negative to disable.")]
        [SerializeField] private float _hardSnapAngle = 120f;

        [Tooltip("If true, resets the rigidbody angular velocity once the hard snap angle is exceeded.")]
        [SerializeField] private bool _resetAngularVelocityOnSnap = false;

        [Tooltip("Rotation error (degrees) below which rotation correction stops. Negative to disable rotation correction entirely.")]
        [SerializeField] private float _acceptableRotationError = 1f;

        [Tooltip("Master switch for stall recovery: the settle servo that eases a settling body onto the settled target, the kinematic recovery for rotation errors torque cannot close, and the opt-in settled hard snap (which uses this value as its delay). Negative to disable all of them.")]
        [SerializeField] private float _stallRecoveryDelay = 0.25f;

        [Tooltip("Position error (meters) above which a settled body is considered misaligned and recovered to the settled target.")]
        [SerializeField] private float _settledSnapPositionError = 0.01f;

        [Tooltip("If true, a body that stays settled in the wrong pose teleports straight to the settled target instead of relying on the settle servo. Enable only when instant realignment matters more than visual continuity.")]
        [SerializeField] private bool _hardSnapOnStall;

        [Header("Sync")]
        [Tooltip("Minimum distance moved required to trigger a network update.")]
        [SerializeField] private float _positionChangeThreshold = 0.001f;

        [Tooltip("Minimum angle rotated required to trigger a network update.")]
        [SerializeField] private float _rotationChangeThreshold = 0.001f;

        [Tooltip("If linear and angular velocities are below this value, the object is considered stopped and will stop sending updates.")]
        [SerializeField] private float _velocityStopThreshold = 0.001f;

        private Rigidbody _cachedRigidbody;
        private Rigidbody _rigidbody => _cachedRigidbody ? _cachedRigidbody : (_cachedRigidbody = GetComponent<Rigidbody>());

        private const int BUFFER_SIZE = 32;
        private const float MAX_HARD_SNAP_ANGLE = 175f;
        private const float SETTLED_LINEAR_SPEED_SQR = 0.0004f;
        private const float SETTLED_ANGULAR_SPEED_SQR = 0.01f;
        private const float ROTATION_STALL_MIN_ERROR = 20f;
        private const float ROTATION_RECOVERY_DEGREES_PER_SECOND = 540f;
        private const float ROTATION_RECOVERY_MAX_DURATION = 2f;
        private const float SETTLE_SERVO_CLOSING_TIME = 0.5f;
        private const float SETTLE_SERVO_MAX_SPEED = 1.5f;
        private const float SETTLE_SERVO_MAX_ANGULAR = 4f;
        private const float SETTLE_SERVO_RESPONSE = 0.1f;
        private const float SETTLE_SERVO_ENGAGE_SPEED = 0.75f;
        private const float SETTLE_SERVO_ENGAGE_ANGULAR = 3f;
        private const float SETTLE_SERVO_ROTATION_EPSILON = 0.25f;
        private readonly TimestampedSnapshot[] _snapshotBuffer = new TimestampedSnapshot[BUFFER_SIZE];
        private int _bufferHead;
        private int _bufferCount;

        private double3 _targetPosition;
        private RigidbodyPositionFrame _targetPositionFrame = RigidbodyPositionFrame.World;
        private Quaternion _targetRotation = Quaternion.identity;
        private Vector3 _targetLinearVelocity;
        private Vector3 _targetAngularVelocity;
        /// <summary>Parent required by a ParentLocal target, even if Unity later destroys it.</summary>
        private Transform _targetParent;

        private double3 _lastSyncedPosition;
        private Quaternion _lastSyncedRotation;
        private Vector3 _lastSyncedLinearVelocity;
        private Vector3 _lastSyncedAngularVelocity;
        private Transform _lastSyncedParent;
        private bool _lastSyncedWasSettled;

        private bool _hasPendingTeleport;
        private bool _isIgnoringParentChanges;

        private double _forceSyncWindowEndTime = double.NegativeInfinity;
        private bool _forceSyncOneShot;
        private bool _wasInForceSyncWindow;

        private string _lastCorrectionReason = "No";
        private double3 _latestRawSnapshotPos;
        private RigidbodyPositionFrame _latestRawSnapshotFrame = RigidbodyPositionFrame.World;
        private Transform _latestRawSnapshotParent;
        private string _bufferSampleMode = "None";
        private double _lastLogTime;
        private float _predictionOffset;

        private float _stallSettledTimer;
        private float _rotationStallTimer;
        private float _rotationRecoveryRemaining;
        private bool _settleServoActive;

        /// <summary>
        /// Process-wide fallback used when a NetworkRigidbody has no runtime override
        /// and no sibling component implementing <see cref="INetworkRigidbodyPositionTransform"/>.
        /// Default null preserves legacy wire behaviour.
        /// </summary>
        public static INetworkRigidbodyPositionTransform defaultPositionTransform { get; set; }

        /// <summary>
        /// Fired after this rigidbody applies a hard position correction teleport.
        /// </summary>
        public event Action<RigidbodyCorrectionContext> onTeleportCorrection;

        private INetworkRigidbodyPositionTransform _positionTransform;
        private bool _positionTransformExplicit;

        private NetworkIdentity _softParent;

        private Transform _parentPoseTransform;
        private Rigidbody _parentPoseRigidbody;

        private RigidbodySettingsData _lastBroadcastSettings;
        private bool _hasBroadcastSettings;

        private double _senderTimeOffset;
        private bool _hasSenderTimeOffset;

        private void Awake()
        {
            _cachedRigidbody = GetComponent<Rigidbody>();
        }

        protected override void OnInitializeModules()
        {
            base.OnInitializeModules();
            InitializeStateOrdering();
        }

        protected override void OnEarlySpawn()
        {
            base.OnEarlySpawn();
            ResolvePositionTransform();
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            ResolvePositionTransform();

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var pos = ReadSyncPosition(parentTrs);
            var rot = ReadRotation(parentTrs);
            var linVel = ReadLinearVelocity(parentTrs);
            var angVel = ReadAngularVelocity(parentTrs);

            _targetPosition = pos;
            _targetRotation = rot;
            _targetLinearVelocity = linVel;
            _targetAngularVelocity = angVel;
            _targetParent = parentTrs;
            _targetPositionFrame = GetPositionFrame(parentTrs);

            _lastSyncedPosition = pos;
            _lastSyncedRotation = rot;
            _lastSyncedLinearVelocity = linVel;
            _lastSyncedAngularVelocity = angVel;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledForSync();

            _latestRawSnapshotPos = pos;
            _latestRawSnapshotParent = parentTrs;
            _latestRawSnapshotFrame = _targetPositionFrame;
            ClearBuffer();

            EnsureSettingsInstance();

            if (_softParent && IsController(_ownerAuth) && isActiveAndEnabled)
                SendCurrentState(true, IsSettledForSync());
        }

        protected override void OnObserverAdded(PlayerID player)
        {
            if (!_rigidbody)
                return;

            if (player == localPlayer)
                return;

            if (_ownerAuth && owner.HasValue && player == owner.Value)
                return;

            var parentIdentity = GetSyncParentIdentity(out var isSoft);
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            var stateData = new RigidbodyStateData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = ReadRotation(parentTrs),
                linearVelocity = ReadLinearVelocity(parentTrs),
                angularVelocity = ReadAngularVelocity(parentTrs),
                parent = parentIdentity,
                isSoftParent = isSoft,
                time = Time.unscaledTimeAsDouble
            };

            bool validatedServerAnchor = false;
            if (TryGetLatestServerState(out var latestState))
                stateData = latestState;
            else if (isServer)
            {
                if (!TryStampAndCacheServerAuthorityAnchor(ref stateData, "initial observer state"))
                    return;
                validatedServerAnchor = true;
            }

            if (!validatedServerAnchor && !ValidateOutgoingSnapshot(in stateData, "initial observer state"))
                return;

            SendInitialStateToObserver(player, stateData, GetCurrentSettings());
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();
            DisposeSettingsInstance();
            _positionTransform = null;
            _positionTransformExplicit = false;
            _softParent = null;
            _parentPoseTransform = null;
            _parentPoseRigidbody = null;
            _hasBroadcastSettings = false;
            ResetStateOrdering();
        }

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            base.OnOwnerChanged(oldOwner, newOwner, asServer);
            DisposeSettingsInstance();
            EnsureSettingsInstance();

            if (!isSpawned || !_rigidbody)
                return;

            if (!_ownerAuth)
                return;

            if (asServer)
            {
                BroadcastServerAuthorityTransition(newOwner, oldOwner);
                return;
            }

            if (newOwner == localPlayer && !isServer)
            {
                AdoptControllerStateFromRigidbody();
                return;
            }

            if (oldOwner == localPlayer && newOwner != localPlayer)
            {
                ClearBuffer();
                _forceSyncOneShot = false;
                _forceSyncWindowEndTime = double.NegativeInfinity;
                _wasInForceSyncWindow = false;
            }
        }

        protected override void OnOwnerDisconnected(PlayerID ownerId)
        {
            base.OnOwnerDisconnected(ownerId);
            BroadcastServerAuthorityTransition(null, null);
        }

        protected override void OnOwnerReconnected(PlayerID ownerId)
        {
            base.OnOwnerReconnected(ownerId);
            BroadcastServerAuthorityTransition(ownerId, null);
        }

        private RigidbodyStateData CaptureTargetState()
        {
            if (!IsPositionFrameAvailable(_targetPositionFrame, _targetParent))
                return CaptureCurrentState();

            var parentIdentity = _targetParent ? _targetParent.GetComponent<NetworkIdentity>() : null;
            var isSoft = parentIdentity && _softParent == parentIdentity;

            var frame = _targetPositionFrame;

            return new RigidbodyStateData
            {
                position = frame == RigidbodyPositionFrame.Absolute ? default : (CompressedVector3)ToV3(_targetPosition),
                absolutePosition = frame == RigidbodyPositionFrame.Absolute ? _targetPosition : default,
                positionFrame = frame,
                rotation = _targetRotation,
                linearVelocity = _targetLinearVelocity,
                angularVelocity = _targetAngularVelocity,
                parent = parentIdentity,
                isSoftParent = isSoft,
                time = Time.unscaledTimeAsDouble
            };
        }

        private RigidbodyStateData CaptureCurrentState()
        {
            var parentIdentity = GetSyncParentIdentity(out var isSoft);
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            return new RigidbodyStateData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = ReadRotation(parentTrs),
                linearVelocity = ReadLinearVelocity(parentTrs),
                angularVelocity = ReadAngularVelocity(parentTrs),
                parent = parentIdentity,
                isSoftParent = isSoft,
                time = Time.unscaledTimeAsDouble
            };
        }

        private void AdoptControllerStateFromRigidbody()
        {
            var parentIdentity = GetSyncParentIdentity(out var isSoft);
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var pos = WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            var rot = ReadRotation(parentTrs);
            var linVel = ReadLinearVelocity(parentTrs);
            var angVel = ReadAngularVelocity(parentTrs);

            _targetPosition = pos;
            _targetRotation = rot;
            _targetLinearVelocity = linVel;
            _targetAngularVelocity = angVel;
            _targetParent = parentTrs;
            _targetPositionFrame = wireFrame;

            _lastSyncedPosition = pos;
            _lastSyncedRotation = rot;
            _lastSyncedLinearVelocity = linVel;
            _lastSyncedAngularVelocity = angVel;
            _lastSyncedParent = parentTrs;

            _latestRawSnapshotPos = pos;
            _latestRawSnapshotParent = parentTrs;
            _latestRawSnapshotFrame = wireFrame;
            ClearBuffer();

            if (!isActiveAndEnabled)
                return;

            var stateData = new RigidbodyStateData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = rot,
                linearVelocity = linVel,
                angularVelocity = angVel,
                parent = parentIdentity,
                isSoftParent = isSoft,
                time = Time.unscaledTimeAsDouble,
                sequence = NextStateSequence()
            };

            if (ValidateOutgoingSnapshot(in stateData, "controller adoption"))
                SendStateToServer(stateData);
        }

        private void EnsureSettingsInstance()
        {
            if (!_settingsOverride)
            {
                if (_settingsInstance != null)
                    DisposeSettingsInstance();
                return;
            }

            if (_settingsInstance != null && _settingsInstanceSource == _settingsOverride)
                return;

            DisposeSettingsInstance();
            _settingsInstance = _settingsOverride.Create(this);
            _settingsInstanceSource = _settingsOverride;
        }

        private void DisposeSettingsInstance()
        {
            if (_settingsInstance == null)
                return;

            _settingsInstance.OnDespawned();
            _settingsInstance = null;
            _settingsInstanceSource = null;
        }

        public void OnTick(float delta)
        {
            if (!isActiveAndEnabled)
                return;

            if (IsController(_ownerAuth))
                ControllerTick();
            else
                NonControllerTick();

            TickForceSyncWindow();
        }

        private void ControllerTick()
        {
            if (!_rigidbody)
                return;

            if (!IsCurrentRigidbodyStateFinite())
            {
                ValidateLocalStateForSync("controller tick");
                return;
            }

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;
            bool isSettled = IsSettledForSync();
            bool shouldSendSettledState = isSettled && !_lastSyncedWasSettled;

            if (!isInForceSyncWindow && !shouldSendSettledState && !HasStateChanged(parentTrs) && !ShouldSyncWhenStopped())
                return;

            SendCurrentState(shouldSendSettledState, isSettled);
        }

        private void SendCurrentState(bool reliable, bool zeroVelocities)
        {
            if (!_rigidbody)
                return;

            if (!ValidateLocalStateForSync("state send"))
                return;

            var parentIdentity = GetSyncParentIdentity(out var isSoft);
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var pos = WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            var rot = ReadRotation(parentTrs);
            var linVel = zeroVelocities ? Vector3.zero : ReadLinearVelocity(parentTrs);
            var angVel = zeroVelocities ? Vector3.zero : ReadAngularVelocity(parentTrs);

            _targetPosition = pos;
            _targetRotation = rot;
            _targetLinearVelocity = linVel;
            _targetAngularVelocity = angVel;
            _targetParent = parentTrs;
            _targetPositionFrame = wireFrame;

            var stateData = new RigidbodyStateData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = rot,
                linearVelocity = linVel,
                angularVelocity = angVel,
                parent = parentIdentity,
                isSoftParent = isSoft,
                time = Time.unscaledTimeAsDouble,
                sequence = NextStateSequence()
            };

            bool sent = reliable
                ? SendReliableState(stateData)
                : SendUnreliableState(stateData);

            if (!sent)
                return;

            _lastSyncedPosition = pos;
            _lastSyncedRotation = rot;
            _lastSyncedLinearVelocity = linVel;
            _lastSyncedAngularVelocity = angVel;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledState(linVel, angVel);
        }

        private bool SendUnreliableState(RigidbodyStateData stateData)
        {
            if (!ValidateOutgoingSnapshot(in stateData, "unreliable state"))
                return false;

            if (isServer)
            {
                if (!TryPrepareStateForServerRelay(ref stateData))
                    return false;
                SyncState(stateData);
            }
            else
                SendStateToServer(stateData);
            return true;
        }

        private bool SendReliableState(RigidbodyStateData stateData)
        {
            if (!ValidateOutgoingSnapshot(in stateData, "reliable state"))
                return false;

            if (isServer)
            {
                if (!TryPrepareStateForServerRelay(ref stateData))
                    return false;
                SyncReliableState(stateData);
            }
            else
                SendReliableStateToServer(stateData);
            return true;
        }

        private void NonControllerTick()
        {
            if (_hasPendingTeleport)
                return;

            SampleBuffer();
            _prePredictionTarget = _targetPosition;

            if (_predictionFactor > 0f)
            {
                float compensation = interpolationDelay * _predictionFactor;
                _targetPosition += ToD3(_targetLinearVelocity * compensation);
                _predictionOffset = compensation;
            }
            else
            {
                _predictionOffset = 0f;
            }
        }

        private void FixedUpdate()
        {
            if (!isFullySpawned || IsController(_ownerAuth) || _hasPendingTeleport)
                return;

            if (!_rigidbody)
                return;

            if (!IsPositionFrameAvailable(_targetPositionFrame, _targetParent))
            {
                _lastCorrectionReason = "Waiting for valid position frame";
                return;
            }
            // Keep non-finite converter output in the existing correction validation below.
            TryToWorldPosition(_targetPosition, _targetParent, _targetPositionFrame, out var worldTargetPos);
            Quaternion worldTargetRot = ToWorldRotation(_targetRotation, _targetParent);
            Vector3 worldTargetLinVel = ToWorldLinearVelocity(_targetLinearVelocity, _targetParent, worldTargetPos);
            Vector3 worldTargetAngVel = ToWorldAngularVelocity(_targetAngularVelocity, _targetParent);

            if (!TryGetCorrectionErrors(
                    worldTargetPos,
                    worldTargetRot,
                    worldTargetLinVel,
                    worldTargetAngVel,
                    out float positionError,
                    out float rotationError))
                return;

            EnsureSettingsInstance();

            var ctx = BuildCorrectionContext(worldTargetPos, worldTargetRot, worldTargetLinVel, worldTargetAngVel, positionError, rotationError);

            if (TryStallSnapRecovery(in ctx))
                return;

            bool rotationRecovery = UpdateRotationStallRecovery(in ctx);

            if (_settingsInstance != null)
            {
                if (_settingsInstance.ShouldTeleport(in ctx))
                {
                    _lastCorrectionReason = "Hard (Distance)";
                    _settingsInstance.ApplyHardCorrection(in ctx);
                    _settingsInstance.OnReset(in ctx);
                    onTeleportCorrection?.Invoke(ctx);
                    return;
                }

                bool hardSnapRotation = !rotationRecovery && _settingsInstance.ShouldSnapRotation(in ctx);
                if (hardSnapRotation)
                {
                    _lastCorrectionReason = "Hard (Rotation)";
                    _rigidbody.MoveRotation(NormalizeQuaternion(worldTargetRot));
                    SetAngularVelocity(worldTargetAngVel);
                    _settingsInstance.OnReset(in ctx);
                }

                _settingsInstance.ApplyPositionCorrection(in ctx);

                if (!hardSnapRotation && !rotationRecovery && _settingsInstance.ShouldCorrectRotation(in ctx))
                    _settingsInstance.ApplyRotationCorrection(in ctx);

                if (rotationRecovery)
                {
                    _lastCorrectionReason = "Rotation (Stall Recovery)";
                }
                else if (!hardSnapRotation)
                {
                    bool correctingRot = _settingsInstance.ShouldCorrectRotation(in ctx);
                    _lastCorrectionReason = correctingRot
                        ? "Position+Rotation (Override)"
                        : positionError > 0.001f ? "Position (Override)" : "No";
                }
                else if (positionError > 0.001f)
                {
                    _lastCorrectionReason = "Hard (Rotation) + Position";
                }
            }
            else
            {
                if (positionError >= _hardSnapDistance)
                {
                    _lastCorrectionReason = "Hard (Distance)";
                    HardCorrect(worldTargetPos, worldTargetRot, worldTargetLinVel, worldTargetAngVel);
                    onTeleportCorrection?.Invoke(ctx);
                    return;
                }

                bool kinematicRotation = useKinematicRotationCorrection;

                float snapAngle = effectiveHardSnapAngle;
                bool hardSnapRotation = !kinematicRotation
                                      && !rotationRecovery
                                      && snapAngle >= 0
                                      && _acceptableRotationError >= 0
                                      && rotationError > snapAngle;
                if (hardSnapRotation)
                {
                    _lastCorrectionReason = "Hard (Rotation)";
                    _rigidbody.MoveRotation(NormalizeQuaternion(worldTargetRot));
                    SetAngularVelocity(_resetAngularVelocityOnSnap ? Vector3.zero : worldTargetAngVel);
                }

                bool correctRotation = !rotationRecovery
                                     && (kinematicRotation
                                         || (!hardSnapRotation
                                             && _acceptableRotationError >= 0
                                             && rotationError > _acceptableRotationError));

                _lastCorrectionReason = rotationRecovery
                    ? "Rotation (Stall Recovery)"
                    : hardSnapRotation
                        ? (positionError > 0.001f ? "Hard (Rotation) + Position" : "Hard (Rotation)")
                        : correctRotation
                            ? kinematicRotation ? "Position+Rotation (Kinematic)" : "Position+Rotation"
                            : positionError > 0.001f ? "Position" : "No";

                ApplyCorrection(worldTargetPos, worldTargetRot, worldTargetLinVel, worldTargetAngVel, positionError, correctRotation);
            }

            ApplySettleServo(in ctx, rotationRecovery);
        }

        private float effectiveHardSnapAngle => _hardSnapAngle < 0f ? _hardSnapAngle : Mathf.Min(_hardSnapAngle, MAX_HARD_SNAP_ANGLE);

        private bool TryStallSnapRecovery(in RigidbodyCorrectionContext ctx)
        {
            if (!_hardSnapOnStall || _stallRecoveryDelay < 0f || !CanApplyDynamicMotion())
            {
                _stallSettledTimer = 0f;
                return false;
            }

            bool targetSettled = _targetLinearVelocity.sqrMagnitude <= SETTLED_LINEAR_SPEED_SQR
                              && _targetAngularVelocity.sqrMagnitude <= SETTLED_ANGULAR_SPEED_SQR;

            bool bodySettled = (GetLinearVelocity() - ctx.targetLinearVelocity).sqrMagnitude <= SETTLED_LINEAR_SPEED_SQR
                            && (_rigidbody.angularVelocity - ctx.targetAngularVelocity).sqrMagnitude <= SETTLED_ANGULAR_SPEED_SQR;

            bool rotationMatters = useKinematicRotationCorrection || _acceptableRotationError >= 0f;
            float positionEpsilon = Mathf.Max(_settledSnapPositionError, 0.001f);
            float rotationEpsilon = Mathf.Max(_acceptableRotationError, 1f);
            bool poseMismatch = ctx.positionError > positionEpsilon
                             || (rotationMatters && ctx.rotationError > rotationEpsilon);

            if (!targetSettled || !bodySettled || !poseMismatch)
            {
                _stallSettledTimer = 0f;
                return false;
            }

            _stallSettledTimer += Time.fixedDeltaTime;
            if (_stallSettledTimer < _stallRecoveryDelay)
                return false;

            _stallSettledTimer = 0f;
            _rotationStallTimer = 0f;
            _rotationRecoveryRemaining = 0f;
            _settleServoActive = false;
            _lastCorrectionReason = "Hard (Settled Stall)";

            if (_settingsInstance != null)
            {
                _settingsInstance.ApplyHardCorrection(in ctx);
                _settingsInstance.OnReset(in ctx);
            }
            else
            {
                HardCorrect(ctx.targetPosition, ctx.targetRotation, ctx.targetLinearVelocity, ctx.targetAngularVelocity);
            }

            onTeleportCorrection?.Invoke(ctx);
            return true;
        }

        private void ApplySettleServo(in RigidbodyCorrectionContext ctx, bool rotationRecovery)
        {
            if (_stallRecoveryDelay < 0f || !CanApplyDynamicMotion())
            {
                _settleServoActive = false;
                return;
            }

            bool targetSettled = _targetLinearVelocity.sqrMagnitude <= SETTLED_LINEAR_SPEED_SQR
                              && _targetAngularVelocity.sqrMagnitude <= SETTLED_ANGULAR_SPEED_SQR;
            if (!targetSettled)
            {
                _settleServoActive = false;
                return;
            }

            var velocity = GetLinearVelocity();
            var angularVelocity = _rigidbody.angularVelocity;
            var relativeVelocity = velocity - ctx.targetLinearVelocity;
            var relativeAngularVelocity = angularVelocity - ctx.targetAngularVelocity;

            var toTarget = ctx.targetPosition - _rigidbody.position;
            float distance = toTarget.magnitude;

            bool rotationMatters = !rotationRecovery && !ctx.useKinematicRotation && _acceptableRotationError >= 0f;
            float positionEpsilon = Mathf.Max(_settledSnapPositionError * 0.25f, 0.001f);
            bool positionDone = distance <= positionEpsilon;
            bool rotationDone = !rotationMatters || ctx.rotationError <= SETTLE_SERVO_ROTATION_EPSILON;

            if (positionDone && rotationDone)
            {
                _settleServoActive = false;
                return;
            }

            if (!_settleServoActive)
            {
                if (relativeVelocity.magnitude > SETTLE_SERVO_ENGAGE_SPEED
                    || relativeAngularVelocity.magnitude > SETTLE_SERVO_ENGAGE_ANGULAR)
                    return;

                _settleServoActive = true;
            }
            else if (relativeVelocity.magnitude > SETTLE_SERVO_ENGAGE_SPEED + SETTLE_SERVO_MAX_SPEED
                     || relativeAngularVelocity.magnitude > SETTLE_SERVO_ENGAGE_ANGULAR + SETTLE_SERVO_MAX_ANGULAR)
            {
                _settleServoActive = false;
                return;
            }

            float blend = Mathf.Clamp01(Time.fixedDeltaTime / SETTLE_SERVO_RESPONSE);
            _lastCorrectionReason = "Settle Servo";

            if (!positionDone)
            {
                var desiredVelocity = ctx.targetLinearVelocity
                    + Vector3.ClampMagnitude(toTarget * (1f / SETTLE_SERVO_CLOSING_TIME), SETTLE_SERVO_MAX_SPEED);
                SetLinearVelocity(Vector3.Lerp(velocity, desiredVelocity, blend));
            }

            if (!rotationDone)
            {
                var rotationDelta = NormalizeQuaternion(ctx.targetRotation) * Quaternion.Inverse(_rigidbody.rotation);
                rotationDelta.ToAngleAxis(out float angle, out Vector3 axis);

                if (!float.IsNaN(axis.x) && axis.sqrMagnitude > 0.001f)
                {
                    if (angle > 180f)
                        angle -= 360f;

                    var desiredAngularVelocity = ctx.targetAngularVelocity
                        + Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / SETTLE_SERVO_CLOSING_TIME), SETTLE_SERVO_MAX_ANGULAR);
                    SetAngularVelocity(Vector3.Lerp(angularVelocity, desiredAngularVelocity, blend));
                }
            }
        }

        private bool UpdateRotationStallRecovery(in RigidbodyCorrectionContext ctx)
        {
            if (_stallRecoveryDelay < 0f || !CanApplyDynamicMotion() || ctx.useKinematicRotation || _acceptableRotationError < 0f)
            {
                _rotationStallTimer = 0f;
                _rotationRecoveryRemaining = 0f;
                return false;
            }

            if (_rotationRecoveryRemaining <= 0f)
            {
                bool rotationStalled = ctx.rotationError > ROTATION_STALL_MIN_ERROR
                    && (_rigidbody.angularVelocity - ctx.targetAngularVelocity).sqrMagnitude <= SETTLED_ANGULAR_SPEED_SQR;

                if (!rotationStalled)
                {
                    _rotationStallTimer = 0f;
                    return false;
                }

                _rotationStallTimer += Time.fixedDeltaTime;
                if (_rotationStallTimer < _stallRecoveryDelay)
                    return false;

                _rotationStallTimer = 0f;
                _rotationRecoveryRemaining = ROTATION_RECOVERY_MAX_DURATION;
            }

            _rotationRecoveryRemaining -= Time.fixedDeltaTime;

            float rotationEpsilon = Mathf.Max(_acceptableRotationError, 1f);
            if (ctx.rotationError <= rotationEpsilon || _rotationRecoveryRemaining <= 0f)
            {
                _rotationRecoveryRemaining = 0f;
                return false;
            }

            var step = Quaternion.RotateTowards(
                _rigidbody.rotation,
                NormalizeQuaternion(ctx.targetRotation),
                ROTATION_RECOVERY_DEGREES_PER_SECOND * Time.fixedDeltaTime);
            _rigidbody.MoveRotation(step);
            SetAngularVelocity(ctx.targetAngularVelocity);
            return true;
        }

        private void ApplyCorrection(Vector3 worldTargetPos, Quaternion worldTargetRot, Vector3 worldTargetLinVel, Vector3 worldTargetAngVel, float positionError, bool correctRotation)
        {
            if (!CanApplyDynamicMotion())
                return;

            NetworkRigidbodyPhysics.ApplyPositionSpring(
                _rigidbody,
                worldTargetPos,
                worldTargetLinVel,
                positionError,
                _positionStrength,
                _correctionRange,
                GetDrag());

            if (correctRotation)
            {
                NetworkRigidbodyPhysics.ApplyRotationSpring(
                    _rigidbody,
                    NormalizeQuaternion(worldTargetRot),
                    worldTargetAngVel,
                    _rotationStrength,
                    useKinematicRotationCorrection);
            }
        }

        /// <summary>
        /// True when this receiver should follow the target rotation with MoveRotation instead of
        /// torque. In Auto, any constrained rotation axis means the rotation is authored by the
        /// controller rather than simulated, so torque can only ever lag behind it.
        /// </summary>
        private bool useKinematicRotationCorrection
        {
            get
            {
                switch (_rotationCorrection)
                {
                    case RigidbodyRotationCorrection.Kinematic:
                        return true;
                    case RigidbodyRotationCorrection.Torque:
                        return false;
                    default:
                        return _rigidbody && (_rigidbody.constraints & RigidbodyConstraints.FreezeRotation) != 0;
                }
            }
        }

        private RigidbodyCorrectionContext BuildCorrectionContext(Vector3 worldTargetPos, Quaternion worldTargetRot, Vector3 worldTargetLinVel, Vector3 worldTargetAngVel, float positionError, float rotationError)
        {
            return BuildCorrectionContext(
                worldTargetPos,
                worldTargetRot,
                worldTargetLinVel,
                worldTargetAngVel,
                positionError,
                rotationError,
                _rigidbody.position,
                _rigidbody.rotation
            );
        }

        private RigidbodyCorrectionContext BuildCorrectionContext(
            Vector3 worldTargetPos,
            Quaternion worldTargetRot,
            Vector3 worldTargetLinVel,
            Vector3 worldTargetAngVel,
            float positionError,
            float rotationError,
            Vector3 previousPosition,
            Quaternion previousRotation
        )
        {
            return new RigidbodyCorrectionContext
            {
                rigidbody = _rigidbody,
                previousPosition = previousPosition,
                previousRotation = previousRotation,
                targetPosition = worldTargetPos,
                targetRotation = worldTargetRot,
                targetLinearVelocity = worldTargetLinVel,
                targetAngularVelocity = worldTargetAngVel,
                positionError = positionError,
                rotationError = rotationError,
                drag = GetDrag(),
                positionStrength = _positionStrength,
                correctionRange = _correctionRange,
                rotationStrength = _rotationStrength,
                hardSnapDistance = _hardSnapDistance,
                hardSnapAngle = effectiveHardSnapAngle,
                acceptableRotationError = _acceptableRotationError,
                useKinematicRotation = useKinematicRotationCorrection
            };
        }

        private void HardCorrect(Vector3 worldTargetPos, Quaternion worldTargetRot, Vector3 worldTargetLinVel, Vector3 worldTargetAngVel)
        {
            _rigidbody.position = worldTargetPos;
            _rigidbody.rotation = NormalizeQuaternion(worldTargetRot);
            SetLinearVelocity(_resetLinearVelocityOnSnap ? Vector3.zero : worldTargetLinVel);
            SetAngularVelocity(_resetAngularVelocityOnSnap ? Vector3.zero : worldTargetAngVel);
        }

        private static Quaternion NormalizeQuaternion(Quaternion q)
        {
            float dot = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (dot < 0.0001f)
                return Quaternion.identity;
            float inv = 1f / Mathf.Sqrt(dot);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        #region Snapshot Buffer

        private void ClearBuffer()
        {
            _bufferHead = 0;
            _bufferCount = 0;
            _hasSenderTimeOffset = false;
            _stallSettledTimer = 0f;
            _rotationStallTimer = 0f;
            _rotationRecoveryRemaining = 0f;
            _settleServoActive = false;
        }

        private double MapToLocalTimeline(double senderTime, double now)
        {
            if (senderTime <= 0)
                return now;

            bool hadOffset = _hasSenderTimeOffset;
            double previousOffset = _senderTimeOffset;
            double sample = now - senderTime;

            if (!_hasSenderTimeOffset || Math.Abs(sample - _senderTimeOffset) > 0.5)
            {
                _senderTimeOffset = sample;
                _hasSenderTimeOffset = true;
            }
            else if (sample < _senderTimeOffset)
            {
                _senderTimeOffset = sample;
            }
            else
            {
                _senderTimeOffset += (sample - _senderTimeOffset) * 0.02;
            }

            if (hadOffset && _senderTimeOffset != previousOffset)
            {
                // A clock estimate changes the origin of the whole timeline, not the
                // spacing between captures. Rebasing only the incoming snapshot can
                // collapse a queued batch into the monotonic timestamp clamp below.
                double adjustment = _senderTimeOffset - previousOffset;
                for (int i = 0; i < _bufferCount; i++)
                {
                    int index = (_bufferHead - _bufferCount + i + BUFFER_SIZE) % BUFFER_SIZE;
                    _snapshotBuffer[index].time += adjustment;
                }
            }

            return senderTime + _senderTimeOffset;
        }

        private void PushSnapshot(RigidbodyStateData data, bool orderAlreadyAccepted = false)
        {
            if (!ValidateIncomingSnapshot(in data, "snapshot"))
                return;

            if (!TryResolveReceivedParent(data.parent, data.positionFrame, out var parentTrs))
                return;

            if (!orderAlreadyAccepted && !TryAcceptStateOrder(in data))
                return;

            ApplyReceivedSoftParent(data.parent, data.isSoftParent);

            var now = Time.unscaledTimeAsDouble;

            if (_bufferCount > 0)
            {
                int lastIndex = (_bufferHead - 1 + BUFFER_SIZE) % BUFFER_SIZE;
                double maxGap = Math.Max(0.5, interpolationDelay * 4.0);
                if (now - _snapshotBuffer[lastIndex].time > maxGap)
                    ClearBuffer();
            }

            // Legacy captures use arrival time, which cannot be rebased with the
            // sender clock. Start a fresh buffer when the timing convention changes.
            if (_bufferCount > 0 && (data.time > 0) != _hasSenderTimeOffset)
                ClearBuffer();

            var snapshotTime = MapToLocalTimeline(data.time, now);

            if (_bufferCount > 0)
            {
                int lastIndex = (_bufferHead - 1 + BUFFER_SIZE) % BUFFER_SIZE;
                double lastTime = _snapshotBuffer[lastIndex].time;
                if (snapshotTime <= lastTime)
                    snapshotTime = lastTime + 0.0001;
            }

            var syncPos = ExtractSyncPosition(data.positionFrame, data.position, data.absolutePosition);
            _snapshotBuffer[_bufferHead] = new TimestampedSnapshot
            {
                time = snapshotTime,
                position = syncPos,
                positionFrame = data.positionFrame,
                rotation = data.rotation,
                linearVelocity = data.linearVelocity,
                angularVelocity = data.angularVelocity,
                parent = parentTrs
            };

            _bufferHead = (_bufferHead + 1) % BUFFER_SIZE;
            if (_bufferCount < BUFFER_SIZE)
                _bufferCount++;

            _latestRawSnapshotPos = syncPos;
            _latestRawSnapshotFrame = data.positionFrame;
            _latestRawSnapshotParent = parentTrs;
        }

        private TimestampedSnapshot GetSnapshot(int logicalIndex)
        {
            int start = (_bufferHead - _bufferCount + BUFFER_SIZE) % BUFFER_SIZE;
            int actual = (start + logicalIndex) % BUFFER_SIZE;
            return _snapshotBuffer[actual];
        }

        private void DiscardUnavailableSnapshots()
        {
            // A Transform can become Unity-null after its snapshot was accepted. Drop the
            // unusable history, keeping any later states that already use a resolved frame.
            for (int i = _bufferCount - 1; i >= 0; i--)
            {
                var snapshot = GetSnapshot(i);
                if (IsPositionFrameAvailable(snapshot.positionFrame, snapshot.parent))
                    continue;

                _bufferCount -= i + 1;
                if (_bufferCount == 0)
                    ClearBuffer();
                _bufferSampleMode = "Waiting for valid position frame";
                break;
            }
        }

        private void SampleBuffer()
        {
            DiscardUnavailableSnapshots();

            if (_bufferCount == 0)
                return;

            if (_bufferCount == 1)
            {
                var only = GetSnapshot(0);
                AdoptSnapshot(only);
                _bufferSampleMode = "Single";
                return;
            }

            double renderTime = Time.unscaledTimeAsDouble - interpolationDelay;

            var oldest = GetSnapshot(0);
            var newest = GetSnapshot(_bufferCount - 1);

            if (renderTime <= oldest.time)
            {
                AdoptSnapshot(oldest);
                _bufferSampleMode = "Clamp-Old";
                return;
            }

            if (renderTime >= newest.time)
            {
                float overshoot = (float)(renderTime - newest.time);
                float maxExtrapolation = Mathf.Max(0f, _maxExtrapolationDuration);
                float extrapolationTime = Mathf.Min(overshoot, maxExtrapolation);
                bool clamped = overshoot > maxExtrapolation;

                _targetPosition = newest.position + ToD3(newest.linearVelocity * extrapolationTime);
                _targetRotation = IntegrateRotation(newest.rotation, GetExtrapolationAngularVelocity(newest), extrapolationTime);
                _targetLinearVelocity = clamped ? Vector3.zero : newest.linearVelocity;
                _targetAngularVelocity = clamped ? Vector3.zero : newest.angularVelocity;
                _targetParent = newest.parent;
                _targetPositionFrame = newest.positionFrame;
                _bufferSampleMode = clamped ? $"Extrap-Clamped ({overshoot:F3}s)" : $"Extrap ({overshoot:F3}s)";
                _predictionOffset = extrapolationTime;
                return;
            }

            for (int i = 0; i < _bufferCount - 1; i++)
            {
                var a = GetSnapshot(i);
                var b = GetSnapshot(i + 1);

                if (renderTime >= a.time && renderTime <= b.time)
                {
                    float span = (float)(b.time - a.time);
                    if (span < 0.0001f)
                    {
                        AdoptSnapshot(b);
                        return;
                    }

                    float t = (float)(renderTime - a.time) / span;
                    HermiteInterpolate(a, b, span, t);
                    _bufferSampleMode = a.parent == b.parent ? $"Interp ({t:F2})" : $"Interp-Reparent ({t:F2})";
                    _predictionOffset = 0f;
                    return;
                }
            }

            AdoptSnapshot(newest);
        }

        /// <summary>
        /// Angular velocity to extrapolate the newest snapshot with. Bodies rotated kinematically
        /// (MoveRotation, direct rotation writes) report a zero angular velocity, so the rotation
        /// derivative is recovered from the snapshot stream instead of stalling until the next packet.
        /// </summary>
        private Vector3 GetExtrapolationAngularVelocity(TimestampedSnapshot newest)
        {
            if (newest.angularVelocity.sqrMagnitude > 1e-6f)
                return newest.angularVelocity;

            if (_bufferCount < 2)
                return Vector3.zero;

            var previous = GetSnapshot(_bufferCount - 2);
            if (previous.parent != newest.parent || previous.positionFrame != newest.positionFrame)
                return Vector3.zero;

            float span = (float)(newest.time - previous.time);
            if (span < 0.0001f)
                return Vector3.zero;

            return GetRotationDerivative(previous.rotation, newest.rotation, span);
        }

        /// <summary>
        /// Target angular velocity for the correction spring's feedforward term. Kinematically
        /// rotated bodies report zero, which would leave the spring chasing a moving target on
        /// the proportional term alone, so it falls back to the snapshot pair's rotation derivative.
        /// </summary>
        private static Vector3 ResolveInterpolatedAngularVelocity(TimestampedSnapshot a, TimestampedSnapshot b, float dt, float t)
        {
            var lerped = Vector3.Lerp(a.angularVelocity, b.angularVelocity, t);
            if (lerped.sqrMagnitude > 1e-6f || dt < 0.0001f)
                return lerped;

            return GetRotationDerivative(a.rotation, b.rotation, dt);
        }

        private static Vector3 GetRotationDerivative(Quaternion from, Quaternion to, float dt)
        {
            var delta = to * Quaternion.Inverse(from);
            delta.ToAngleAxis(out float angle, out Vector3 axis);

            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 0.001f)
                return Vector3.zero;

            if (angle > 180f)
                angle -= 360f;

            return axis * (angle * Mathf.Deg2Rad / dt);
        }

        private static Quaternion IntegrateRotation(Quaternion rotation, Vector3 angularVelocity, float time)
        {
            if (time <= 0f)
                return rotation;

            float magnitude = angularVelocity.magnitude;
            if (magnitude < 1e-5f)
                return rotation;

            return Quaternion.AngleAxis(magnitude * time * Mathf.Rad2Deg, angularVelocity / magnitude) * rotation;
        }

        private void AdoptSnapshot(TimestampedSnapshot snap)
        {
            _targetPosition = snap.position;
            _targetRotation = snap.rotation;
            _targetLinearVelocity = snap.linearVelocity;
            _targetAngularVelocity = snap.angularVelocity;
            _targetParent = snap.parent;
            _targetPositionFrame = snap.positionFrame;
        }

        private void HermiteInterpolate(TimestampedSnapshot a, TimestampedSnapshot b, float dt, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;

            if (a.parent == b.parent && a.positionFrame == b.positionFrame)
            {
                _targetPosition = h00 * a.position
                                + ToD3(a.linearVelocity * (h10 * dt))
                                + h01 * b.position
                                + ToD3(b.linearVelocity * (h11 * dt));

                _targetLinearVelocity = Vector3.Lerp(a.linearVelocity, b.linearVelocity, t);
                _targetRotation = Quaternion.Slerp(a.rotation, b.rotation, t);
                _targetAngularVelocity = ResolveInterpolatedAngularVelocity(a, b, dt, t);
                _targetParent = a.parent;
                _targetPositionFrame = a.positionFrame;
            }
            else
            {
                if (!TryToWorldPosition(a.position, a.parent, a.positionFrame, out var aWorldPos) ||
                    !TryToWorldPosition(b.position, b.parent, b.positionFrame, out var bWorldPos))
                    return;
                Vector3 aWorldLinVel = ToWorldLinearVelocity(a.linearVelocity, a.parent, aWorldPos);
                Vector3 bWorldLinVel = ToWorldLinearVelocity(b.linearVelocity, b.parent, bWorldPos);
                Quaternion aWorldRot = ToWorldRotation(a.rotation, a.parent);
                Quaternion bWorldRot = ToWorldRotation(b.rotation, b.parent);
                Vector3 aWorldAngVel = ToWorldAngularVelocity(a.angularVelocity, a.parent);
                Vector3 bWorldAngVel = ToWorldAngularVelocity(b.angularVelocity, b.parent);

                Vector3 worldResult = h00 * aWorldPos
                                    + aWorldLinVel * (h10 * dt)
                                    + h01 * bWorldPos
                                    + bWorldLinVel * (h11 * dt);
                _targetPosition = WorldToSyncNoParent(worldResult);

                _targetLinearVelocity = Vector3.Lerp(aWorldLinVel, bWorldLinVel, t);
                _targetRotation = Quaternion.Slerp(aWorldRot, bWorldRot, t);
                _targetAngularVelocity = Vector3.Lerp(aWorldAngVel, bWorldAngVel, t);
                _targetParent = null;
                _targetPositionFrame = GetPositionFrame(null);
            }
        }

        #endregion

        #region Parent sync

        public bool syncParent => _syncParent;
        public bool ownerAuth => _ownerAuth;
        public new bool isController => IsController(_ownerAuth);

        /// <summary>Configured sync space. Local is relative to the current parent, World is absolute.</summary>
        public RigidbodyTransformSpace space => _space;

        /// <summary>Configured rotation correction mode. See <see cref="RigidbodyRotationCorrection"/>.</summary>
        public RigidbodyRotationCorrection rotationCorrection
        {
            get => _rotationCorrection;
            set => _rotationCorrection = value;
        }

        /// <summary>
        /// True when this receiver follows the synced rotation with MoveRotation rather than torque,
        /// after resolving <see cref="rotationCorrection"/> against the rigidbody's constraints.
        /// </summary>
        public bool isRotationKinematic => useKinematicRotationCorrection;

        /// <summary>
        /// Parent-frame velocity is now always relative to the parent motion. This compatibility
        /// property remains temporarily so existing integrations continue to compile.
        /// </summary>
        [Obsolete("Parent-relative velocity is automatic whenever NetworkRigidbody uses a parent-local position frame. This property no longer changes behaviour.")]
        public bool syncVelocityRelativeToParent
        {
            get => true;
            set { }
        }

        /// <summary>
        /// When enabled, position error used by correction and hard-snap decisions is
        /// measured in the active parent or soft-parent frame. The correction target remains world-space.
        /// </summary>
        public bool useParentFramePositionError
        {
            get => _useParentFramePositionError;
            set => _useParentFramePositionError = value;
        }

        /// <summary>
        /// Active soft-parent: the identity this rigidbody syncs relative to without being a
        /// Unity child of it, or null when the sync frame comes from <see cref="space"/> and the
        /// real Unity parent (legacy behaviour). See <see cref="SetSoftParent"/>.
        /// </summary>
        public NetworkIdentity softParent => (_softParent && _softParent.isSpawned) ? _softParent : null;

        /// <summary>
        /// The assigned or received soft-parent instance, even when it is not currently spawned.
        /// Use <see cref="softParent"/> when only the active sync parent matters.
        /// </summary>
        public NetworkIdentity softParentInstance => _softParent;

        /// <summary>
        /// Soft-parent this rigidbody to <paramref name="identity"/>: its position, rotation and
        /// velocity sync in that identity's local frame, exactly as if parented there, but the
        /// Unity transform is left untouched — no real reparenting, no hierarchy sync. Overrides
        /// <see cref="space"/> while active. Pass null (or call <see cref="ClearSoftParent"/>) to revert.
        /// Linear and angular velocity are always relative to the parent Rigidbody's motion while
        /// this parent-local frame is active, so they remain valid derivatives for interpolation and
        /// extrapolation. With no sync parent, position and velocity remain in world/absolute space.
        /// </summary>
        public void SetSoftParent(NetworkIdentity identity)
        {
            if (identity == this)
            {
                PurrLogger.LogWarning($"Cannot soft-parent {gameObject.name} to itself. Ignored.", this);
                return;
            }

            if (_softParent == identity)
                return;

            if (isSpawned && !IsController(_ownerAuth))
            {
                PurrLogger.LogWarning($"SetSoftParent called on {gameObject.name} from a non-controller. Ignored.", this);
                return;
            }

            _softParent = identity;

            if (isSpawned && _rigidbody && isActiveAndEnabled)
                SendCurrentState(true, IsSettledForSync());
        }

        /// <summary>Clears the soft-parent set via <see cref="SetSoftParent"/>, reverting to the <see cref="space"/>-derived frame.</summary>
        public void ClearSoftParent() => SetSoftParent(null);

        public void StartIgnoringParentChanges()
        {
            _isIgnoringParentChanges = true;
        }

        public void StopIgnoringParentChanges()
        {
            _isIgnoringParentChanges = false;
        }

        private void OnTransformParentChanged()
        {
            if (!isSpawned)
                return;

            if (_isIgnoringParentChanges)
                return;

            if (!_syncParent)
                return;

            if (networkManager.TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                factory.TryGetHierarchy(sceneId, out var hierarchy))
            {
                hierarchy.OnParentChanged(this, transform.parent);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Interpolation delay actually used for sampling. With <c>_autoInterpolationDelay</c> the
        /// configured value is floored at one send interval, since below that the render time is
        /// past the newest snapshot on every frame and the receiver can only ever extrapolate.
        /// </summary>
        private float interpolationDelay
        {
            get
            {
                if (!_autoInterpolationDelay)
                    return _interpolationDelay;

                var manager = networkManager;
                if (!manager || manager.tickRate <= 0)
                    return _interpolationDelay;

                return Mathf.Max(_interpolationDelay, 1f / manager.tickRate);
            }
        }

        private NetworkIdentity GetSyncParentIdentity() => GetSyncParentIdentity(out _);

        private NetworkIdentity GetSyncParentIdentity(out bool isSoft)
        {
            if (_softParent && _softParent.isSpawned)
            {
                isSoft = true;
                return _softParent;
            }

            isSoft = false;
            if (_space != RigidbodyTransformSpace.Local)
                return null;
            var parentTrs = transform.parent;
            if (!parentTrs)
                return null;
            return parentTrs.GetComponent<NetworkIdentity>();
        }

        private static double3 ToD3(Vector3 v) => new double3(v.x, v.y, v.z);
        private static Vector3 ToV3(double3 v) => new Vector3((float)v.x, (float)v.y, (float)v.z);

        private Rigidbody ResolveParentRigidbody(Transform parent)
        {
            if (_parentPoseTransform != parent)
            {
                _parentPoseTransform = parent;
                _parentPoseRigidbody = parent ? parent.GetComponentInParent<Rigidbody>() : null;
                if (_parentPoseRigidbody == _rigidbody)
                    _parentPoseRigidbody = null;
            }

            return _parentPoseRigidbody;
        }

        private void GetParentPose(Transform parent, out Vector3 position, out Quaternion rotation)
        {
            var rb = ResolveParentRigidbody(parent);
            if (!rb)
            {
                position = parent.position;
                rotation = parent.rotation;
                return;
            }

            var rbTransform = rb.transform;
            if (rbTransform == parent)
            {
                position = rb.position;
                rotation = rb.rotation;
                return;
            }

            var localRotation = Quaternion.Inverse(rbTransform.rotation) * parent.rotation;
            var localPosition = rbTransform.InverseTransformPoint(parent.position);
            rotation = rb.rotation * localRotation;
            position = rb.position + rb.rotation * Vector3.Scale(rbTransform.lossyScale, localPosition);
        }

        private Vector3 ParentTransformPoint(Transform parent, Vector3 local)
        {
            GetParentPose(parent, out var position, out var rotation);
            return position + rotation * Vector3.Scale(parent.lossyScale, local);
        }

        private Vector3 ParentInverseTransformPoint(Transform parent, Vector3 world)
        {
            GetParentPose(parent, out var position, out var rotation);
            return NetworkRigidbodyFrameMath.InverseScale(
                Quaternion.Inverse(rotation) * (world - position),
                parent.lossyScale);
        }

        private Quaternion ParentRotation(Transform parent)
        {
            GetParentPose(parent, out _, out var rotation);
            return rotation;
        }

        /// <summary>
        /// Reads the rigidbody position into the origin-invariant sync frame:
        /// parent-local when parented, absolute (via the position transform) when
        /// unparented and a transform is installed, otherwise raw Unity world space.
        /// </summary>
        private double3 ReadSyncPosition(Transform parent)
        {
            var p = _rigidbody ? _rigidbody.position : transform.position;
            if (parent)
                return ToD3(ParentInverseTransformPoint(parent, p));
            if (_positionTransform != null)
                return _positionTransform.ToAbsolute(this, p);
            return ToD3(p);
        }

        /// <summary>
        /// Reads the rigidbody position and fills the wire fields of a state struct.
        /// Exactly one of <paramref name="wirePos"/> / <paramref name="wireAbs"/>
        /// carries the value; the other stays default so it delta-packs away.
        /// <paramref name="frame"/> records which one, making the payload
        /// self-describing. Returns the same value in the sync frame.
        /// </summary>
        private double3 WriteWirePosition(Transform parent, out CompressedVector3 wirePos, out double3 wireAbs, out RigidbodyPositionFrame frame)
        {
            var p = _rigidbody ? _rigidbody.position : transform.position;
            if (parent)
            {
                var local = ParentInverseTransformPoint(parent, p);
                wirePos = local;
                wireAbs = default;
                frame = RigidbodyPositionFrame.ParentLocal;
                return ToD3(local);
            }
            if (_positionTransform != null)
            {
                wirePos = default;
                wireAbs = _positionTransform.ToAbsolute(this, p);
                frame = RigidbodyPositionFrame.Absolute;
                return wireAbs;
            }
            wirePos = p;
            wireAbs = default;
            frame = RigidbodyPositionFrame.World;
            return ToD3(p);
        }

        /// <summary>Converts a Unity world-space position into the wire fields (unparented).</summary>
        private void WorldToWire(Vector3 worldPos, out CompressedVector3 wirePos, out double3 wireAbs, out RigidbodyPositionFrame frame)
        {
            if (_positionTransform != null)
            {
                wirePos = default;
                wireAbs = _positionTransform.ToAbsolute(this, worldPos);
                frame = RigidbodyPositionFrame.Absolute;
            }
            else
            {
                wirePos = worldPos;
                wireAbs = default;
                frame = RigidbodyPositionFrame.World;
            }
        }

        /// <summary>
        /// Decodes the wire fields of a received state into the sync frame, keyed
        /// purely on the encoded <paramref name="frame"/> so it never depends on
        /// the parent reference resolving or on receiver-side state.
        /// </summary>
        private double3 ExtractSyncPosition(RigidbodyPositionFrame frame, CompressedVector3 wirePos, double3 wireAbs)
        {
            return frame == RigidbodyPositionFrame.Absolute ? wireAbs : ToD3(wirePos);
        }

        private void ApplyReceivedSoftParent(NetworkIdentity parent, bool isSoftParent)
        {
            _softParent = isSoftParent ? parent : null;
        }

        /// <summary>
        /// A parent-local payload is usable only with its own resolved wire parent.
        /// Neither the previous soft parent nor the local hierarchy identifies an
        /// unresolved reference in a new packet. Wait for a subsequent valid state.
        /// </summary>
        private static bool TryResolveReceivedParent(NetworkIdentity wireParent, RigidbodyPositionFrame frame, out Transform parent)
        {
            parent = frame == RigidbodyPositionFrame.ParentLocal && wireParent ? wireParent.transform : null;
            return IsPositionFrameAvailable(frame, parent);
        }

        private static bool IsPositionFrameAvailable(RigidbodyPositionFrame frame, Transform parent)
        {
            return frame == RigidbodyPositionFrame.ParentLocal ? parent :
                frame == RigidbodyPositionFrame.Absolute || frame == RigidbodyPositionFrame.World;
        }

        private RigidbodyPositionFrame GetPositionFrame(Transform parent)
        {
            return parent ? RigidbodyPositionFrame.ParentLocal :
                _positionTransform != null ? RigidbodyPositionFrame.Absolute : RigidbodyPositionFrame.World;
        }

        /// <summary>Converts an unparented Unity world-space position into the sync frame.</summary>
        private double3 WorldToSyncNoParent(Vector3 worldPos)
        {
            if (_positionTransform != null)
                return _positionTransform.ToAbsolute(this, worldPos);
            return ToD3(worldPos);
        }

        private Quaternion ReadRotation(Transform parent)
        {
            var r = _rigidbody ? _rigidbody.rotation : transform.rotation;
            return parent ? Quaternion.Inverse(ParentRotation(parent)) * r : r;
        }

        private Vector3 ReadLinearVelocity(Transform parent)
        {
            if (!_rigidbody)
                return Vector3.zero;
            var v = GetLinearVelocity();
            if (!parent)
                return v;

            GetParentPose(parent, out _, out var parentRotation);
            return NetworkRigidbodyFrameMath.ToParentLinearVelocity(
                v,
                GetParentPointVelocity(parent, _rigidbody.position),
                parentRotation,
                parent.lossyScale);
        }

        private Vector3 ReadAngularVelocity(Transform parent)
        {
            if (!_rigidbody)
                return Vector3.zero;
            var v = _rigidbody.angularVelocity;
            if (!parent)
                return v;

            return NetworkRigidbodyFrameMath.ToParentAngularVelocity(
                v,
                GetParentAngularVelocity(parent),
                ParentRotation(parent));
        }

        /// <summary>
        /// Decodes the explicit frame; a missing parent must never turn its local
        /// offset into an absolute/world position. Raw World payloads bypass drivers.
        /// </summary>
        private bool TryToWorldPosition(double3 pos, Transform parent, RigidbodyPositionFrame frame, out Vector3 worldPosition)
        {
            worldPosition = default;
            if (!IsPositionFrameAvailable(frame, parent))
                return false;

            worldPosition = frame == RigidbodyPositionFrame.ParentLocal
                ? ParentTransformPoint(parent, ToV3(pos))
                : frame == RigidbodyPositionFrame.Absolute && _positionTransform != null
                    ? _positionTransform.ToLocal(this, pos)
                    : ToV3(pos);
            return IsFinite(worldPosition);
        }

        private float GetPositionError(Vector3 worldTargetPos)
        {
            if (_useParentFramePositionError && _targetParent)
            {
                Vector3 localCurrent = ParentInverseTransformPoint(_targetParent, _rigidbody.position);
                return Vector3.Distance(localCurrent, ToV3(_targetPosition));
            }

            return Vector3.Distance(_rigidbody.position, worldTargetPos);
        }

        private Quaternion ToWorldRotation(Quaternion rot, Transform parent)
        {
            return parent ? ParentRotation(parent) * rot : rot;
        }

        private Vector3 ToWorldLinearVelocity(Vector3 v, Transform parent, Vector3 worldPoint)
        {
            if (!parent)
                return v;

            GetParentPose(parent, out _, out var parentRotation);
            return NetworkRigidbodyFrameMath.ToWorldLinearVelocity(
                v,
                GetParentPointVelocity(parent, worldPoint),
                parentRotation,
                parent.lossyScale);
        }

        private Vector3 ToWorldAngularVelocity(Vector3 v, Transform parent)
        {
            if (!parent)
                return v;

            return NetworkRigidbodyFrameMath.ToWorldAngularVelocity(
                v,
                GetParentAngularVelocity(parent),
                ParentRotation(parent));
        }

        private Vector3 GetParentPointVelocity(Transform parent, Vector3 worldPoint)
        {
            if (!TryGetParentRigidbody(parent, out var parentRigidbody))
                return Vector3.zero;

            var linear = NetworkRigidbodyPhysics.GetLinearVelocity(parentRigidbody);
            var angular = parentRigidbody.angularVelocity;
            return NetworkRigidbodyFrameMath.GetPointVelocity(
                linear,
                angular,
                parentRigidbody.worldCenterOfMass,
                worldPoint);
        }

        private Vector3 GetParentAngularVelocity(Transform parent)
        {
            return TryGetParentRigidbody(parent, out var parentRigidbody)
                ? parentRigidbody.angularVelocity
                : Vector3.zero;
        }

        private bool TryGetParentRigidbody(Transform parent, out Rigidbody parentRigidbody)
        {
            parentRigidbody = null;

            if (!parent)
                return false;

            parentRigidbody = ResolveParentRigidbody(parent);
            return parentRigidbody;
        }

        private Vector3 GetLinearVelocity()
        {
            return NetworkRigidbodyPhysics.GetLinearVelocity(_rigidbody);
        }

        private void SetLinearVelocity(Vector3 value)
        {
            NetworkRigidbodyPhysics.SetLinearVelocity(_rigidbody, value);
        }

        private void SetAngularVelocity(Vector3 value)
        {
            NetworkRigidbodyPhysics.SetAngularVelocity(_rigidbody, value);
        }

        private void ApplyForceToRigidbody(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            NetworkRigidbodyPhysics.AddForce(_rigidbody, force, mode);
        }

        private void ApplyForceAtPositionToRigidbody(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force)
        {
            NetworkRigidbodyPhysics.AddForceAtPosition(_rigidbody, force, position, mode);
        }

        private void ApplyTorqueToRigidbody(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            NetworkRigidbodyPhysics.AddTorque(_rigidbody, torque, mode);
        }

        private bool CanApplyDynamicMotion()
        {
            return NetworkRigidbodyPhysics.CanApplyDynamicMotion(_rigidbody);
        }

        private float GetDrag()
        {
#if UNITY_6000_0_OR_NEWER
            return _rigidbody.linearDamping;
#else
            return _rigidbody.drag;
#endif
        }

        private void SetDrag(float value)
        {
#if UNITY_6000_0_OR_NEWER
            _rigidbody.linearDamping = value;
#else
            _rigidbody.drag = value;
#endif
        }

        private float GetAngularDrag()
        {
#if UNITY_6000_0_OR_NEWER
            return _rigidbody.angularDamping;
#else
            return _rigidbody.angularDrag;
#endif
        }

        private void SetAngularDrag(float value)
        {
#if UNITY_6000_0_OR_NEWER
            _rigidbody.angularDamping = value;
#else
            _rigidbody.angularDrag = value;
#endif
        }

        private bool HasStateChanged(Transform parent)
        {
            if (parent != _lastSyncedParent)
                return true;

            double positionDelta = math.distance(ReadSyncPosition(parent), _lastSyncedPosition);
            float rotationDelta = Quaternion.Angle(ReadRotation(parent), _lastSyncedRotation);
            float linearVelocityDelta = Vector3.Distance(ReadLinearVelocity(parent), _lastSyncedLinearVelocity);
            float angularVelocityDelta = Vector3.Distance(ReadAngularVelocity(parent), _lastSyncedAngularVelocity);

            return positionDelta > _positionChangeThreshold
                || rotationDelta > _rotationChangeThreshold
                || linearVelocityDelta > _velocityStopThreshold
                || angularVelocityDelta > _velocityStopThreshold;
        }

        private bool IsSettledForSync()
        {
            if (!_rigidbody)
                return true;

            if (_rigidbody.isKinematic || _rigidbody.IsSleeping())
                return true;

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            return IsSettledState(ReadLinearVelocity(parentTrs), ReadAngularVelocity(parentTrs));
        }

        private bool IsSettledState(Vector3 linearVelocity, Vector3 angularVelocity)
        {
            float threshold = Mathf.Max(0f, _velocityStopThreshold);
            float thresholdSqr = threshold * threshold;
            return linearVelocity.sqrMagnitude < thresholdSqr
                && angularVelocity.sqrMagnitude < thresholdSqr;
        }

        private bool ShouldSyncWhenStopped()
        {
            if (!_rigidbody)
                return false;

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            return IsSettledState(ReadLinearVelocity(parentTrs), ReadAngularVelocity(parentTrs))
                && !_rigidbody.IsSleeping();
        }

        /// <summary>
        /// Broadcasts the rigidbody settings only when they differ from what was last put on the
        /// wire. Compared against the last broadcast rather than the live rigidbody so a local
        /// write from outside this component cannot suppress a needed broadcast, while code that
        /// re-asserts the same value every frame no longer floods the reliable channel.
        /// </summary>
        private void SyncSettingsIfChanged()
        {
            if (!_rigidbody)
                return;

            if (!IsController(_ownerAuth) || !isActiveAndEnabled)
                return;

            var settings = GetCurrentSettings();
            if (_hasBroadcastSettings && SettingsEqual(in _lastBroadcastSettings, in settings))
                return;

            _lastBroadcastSettings = settings;
            _hasBroadcastSettings = true;
            SyncSettings(settings);
        }

        private static bool SettingsEqual(in RigidbodySettingsData a, in RigidbodySettingsData b)
        {
            return a.mass.rawValue == b.mass.rawValue
                && a.drag.rawValue == b.drag.rawValue
                && a.angularDrag.rawValue == b.angularDrag.rawValue
                && a.useGravity == b.useGravity
                && a.isKinematic == b.isKinematic;
        }

        private RigidbodySettingsData GetCurrentSettings()
        {
            if (!_rigidbody)
                return default;
            return new RigidbodySettingsData
            {
                mass = (Half)_rigidbody.mass,
                drag = (Half)GetDrag(),
                angularDrag = (Half)GetAngularDrag(),
                useGravity = _rigidbody.useGravity,
                isKinematic = _rigidbody.isKinematic
            };
        }

        private void ApplyForce(AppliedForce force)
        {
            if (!_rigidbody)
                return;

            if (force.isTorque)
                ApplyTorqueToRigidbody(force.force, force.mode);
            else if (force.absolutePosition.HasValue || force.position.HasValue)
            {
                if (TryResolveForcePosition(in force, out var position))
                    ApplyForceAtPositionToRigidbody(force.force, position, force.mode);
                else
                    PurrLogger.LogError(
                        "Rejected NetworkRigidbody force because its application point could not be decoded. " +
                        "Absolute positions require an INetworkRigidbodyPositionTransform, and decoded positions must be finite.", this);
            }
            else
                ApplyForceToRigidbody(force.force, force.mode);
        }

        #endregion

        #region Public API

        public NetworkRigidbodySettings settingsOverride
        {
            get => _settingsOverride;
            set
            {
                if (_settingsOverride == value)
                    return;
                _settingsOverride = value;
                DisposeSettingsInstance();
                EnsureSettingsInstance();
            }
        }

        public NetworkRigidbodySettingsInstance settingsInstance => _settingsInstance;

        /// <summary>
        /// Active position transform for this rigidbody, or null when positions
        /// travel on the wire in this peer's own Unity world space (legacy behaviour).
        /// </summary>
        public INetworkRigidbodyPositionTransform positionTransform => _positionTransform;

        /// <summary>
        /// Install a position transform at runtime, overriding any sibling component
        /// or static default. Pass null to fall back to the resolution chain on the
        /// next spawn.
        /// </summary>
        public void SetPositionTransform(INetworkRigidbodyPositionTransform transform)
        {
            _positionTransform = transform;
            _positionTransformExplicit = transform != null;
        }

        private void ResolvePositionTransform()
        {
            if (_positionTransformExplicit)
                return;

            var sibling = GetComponent<INetworkRigidbodyPositionTransform>();
            _positionTransform = sibling ?? defaultPositionTransform;
        }

        public Vector3 linearVelocity
        {
            get => _rigidbody ? GetLinearVelocity() : Vector3.zero;
            set { if (_rigidbody) SetLinearVelocity(value); }
        }

        /// <summary>Pre-Unity 6 alias for linearVelocity.</summary>
        public Vector3 velocity
        {
            get => linearVelocity;
            set => linearVelocity = value;
        }

        public Vector3 angularVelocity
        {
            get => _rigidbody ? _rigidbody.angularVelocity : Vector3.zero;
            set { if (_rigidbody) SetAngularVelocity(value); }
        }

        public Vector3 position
        {
            get => _rigidbody ? _rigidbody.position : transform.position;
            set { if (_rigidbody) _rigidbody.position = value; }
        }

        public Quaternion rotation
        {
            get => _rigidbody ? _rigidbody.rotation : transform.rotation;
            set { if (_rigidbody) _rigidbody.rotation = value; }
        }

        public float mass
        {
            get => _rigidbody ? _rigidbody.mass : 0f;
            set
            {
                if (!_rigidbody)
                    return;
                _rigidbody.mass = value;
                SyncSettingsIfChanged();
            }
        }

        public float drag
        {
            get => _rigidbody ? GetDrag() : 0f;
            set
            {
                if (!_rigidbody)
                    return;
                SetDrag(value);
                SyncSettingsIfChanged();
            }
        }

        public float linearDamping
        {
            get => drag;
            set => drag = value;
        }

        public float angularDrag
        {
            get => _rigidbody ? GetAngularDrag() : 0f;
            set
            {
                if (!_rigidbody)
                    return;
                SetAngularDrag(value);
                SyncSettingsIfChanged();
            }
        }

        public float angularDamping
        {
            get => angularDrag;
            set => angularDrag = value;
        }

        public bool useGravity
        {
            get => _rigidbody && _rigidbody.useGravity;
            set
            {
                if (!_rigidbody)
                    return;
                _rigidbody.useGravity = value;
                SyncSettingsIfChanged();
            }
        }

        public bool isKinematic
        {
            get => _rigidbody && _rigidbody.isKinematic;
            set
            {
                if (!_rigidbody)
                    return;
                _rigidbody.isKinematic = value;
                SyncSettingsIfChanged();
            }
        }

        public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
        {
            if (!isSpawned || !_rigidbody)
                return;

            var appliedForce = new AppliedForce { force = force, mode = mode };

            if (IsController(_ownerAuth))
            {
                ApplyForceToRigidbody(force, mode);
            }
            else if (isActiveAndEnabled)
            {
                BroadcastForce(appliedForce);
            }
        }

        public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force)
        {
            if (!isSpawned || !_rigidbody)
                return;

            if (IsController(_ownerAuth))
            {
                ApplyForceAtPositionToRigidbody(force, position, mode);
            }
            else if (isActiveAndEnabled)
            {
                BroadcastForce(EncodeForceAtPosition(force, position, mode));
            }
        }

        public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
        {
            if (!isSpawned || !_rigidbody)
                return;

            var appliedForce = new AppliedForce { force = torque, mode = mode, isTorque = true };

            if (IsController(_ownerAuth))
            {
                ApplyTorqueToRigidbody(torque, mode);
            }
            else if (isActiveAndEnabled)
            {
                BroadcastForce(appliedForce);
            }
        }

        public void MovePosition(Vector3 position)
        {
            if (!_rigidbody)
                return;
            _rigidbody.MovePosition(position);
        }

        public void MoveRotation(Quaternion rotation)
        {
            if (!_rigidbody)
                return;
            _rigidbody.MoveRotation(rotation);
        }

        /// <summary>
        /// Instantly teleports the local rigidbody to a new position and rotation, zeroing its
        /// linear and angular velocity, clearing the interpolation buffer, and syncing the teleport.
        /// A non-controller also forwards a teleport request to the controller. Use this for respawns, portals,
        /// or any instant repositioning. For regular physics movement, use position/rotation
        /// setters, MovePosition/MoveRotation, or AddForce instead.
        /// </summary>
        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            if (!_rigidbody)
                return;

            ApplyLocalTeleport(position, rotation);

            if (IsController(_ownerAuth))
            {
                if (isActiveAndEnabled)
                    BroadcastTeleport();
            }
            else if (isActiveAndEnabled)
            {
                WorldToWire(position, out var wirePos, out var wireAbs, out var wireFrame);
                RequestTeleport(wirePos, wireAbs, wireFrame, rotation);
            }
        }

        /// <summary>
        /// Instantly teleports the rigidbody to a new position, clearing the interpolation
        /// buffer and syncing the teleport. Preserves current rotation and zeros the local
        /// rigidbody's linear and angular velocity, as the position-and-rotation overload does.
        /// </summary>
        public void TeleportTo(Vector3 position)
        {
            if (!_rigidbody)
                return;
            TeleportTo(position, _rigidbody.rotation);
        }

        /// <summary>
        /// Locally repositions the rigidbody and resets all interpolation/correction state
        /// (target pose, lastSynced mirrors, snapshot buffer) without sending any RPCs.
        /// Zeros the rigidbody's linear and angular velocity.
        /// Use this when the caller is already handling network sync separately, or to fix
        /// up a single peer's view (e.g. a late-joining client snapping to a known pose).
        /// </summary>
        public void TeleportLocal(Vector3 position, Quaternion rotation)
        {
            if (!_rigidbody)
                return;

            ApplyLocalTeleport(position, rotation);
        }

        /// <summary>
        /// Locally repositions the rigidbody and resets interpolation/correction state without
        /// sending any RPCs. Preserves current rotation and zeros linear and angular velocity.
        /// </summary>
        public void TeleportLocal(Vector3 position)
        {
            if (!_rigidbody)
                return;
            TeleportLocal(position, _rigidbody.rotation);
        }

        private void ApplyLocalTeleport(Vector3 position, Quaternion rotation)
        {
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;
            SetLinearVelocity(Vector3.zero);
            SetAngularVelocity(Vector3.zero);

            var parentIdentity = GetSyncParentIdentity();
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            var syncPos = WriteWirePosition(parentTrs, out _, out _, out _);
            var syncRot = ReadRotation(parentTrs);
            var syncLinVel = ReadLinearVelocity(parentTrs);
            var syncAngVel = ReadAngularVelocity(parentTrs);

            _targetPosition = syncPos;
            _targetRotation = syncRot;
            _targetLinearVelocity = syncLinVel;
            _targetAngularVelocity = syncAngVel;
            _targetParent = parentTrs;
            _targetPositionFrame = GetPositionFrame(parentTrs);

            _lastSyncedPosition = syncPos;
            _lastSyncedRotation = syncRot;
            _lastSyncedLinearVelocity = syncLinVel;
            _lastSyncedAngularVelocity = syncAngVel;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledState(syncLinVel, syncAngVel);

            ClearBuffer();
        }

        /// <summary>
        /// True while a force-sync window is active. The controller uses it to bypass
        /// change-threshold gating on outgoing state; receivers can query it from their
        /// correction code (e.g. to bypass weak-axis logic) for the duration.
        /// </summary>
        public bool isInForceSyncWindow
        {
            get
            {
                if (_forceSyncOneShot)
                    return true;
                return Time.unscaledTimeAsDouble < _forceSyncWindowEndTime;
            }
        }

        /// <summary>Seconds remaining in the active force-sync window, or 0 if inactive / one-shot.</summary>
        public float forceSyncWindowRemaining
        {
            get
            {
                if (_forceSyncOneShot || _forceSyncWindowEndTime <= 0)
                    return 0f;
                return Mathf.Max(0f, (float)(_forceSyncWindowEndTime - Time.unscaledTimeAsDouble));
            }
        }

        /// <summary>Fired when <see cref="isInForceSyncWindow"/> transitions from false to true.</summary>
        public event Action onForceSyncWindowOpened;

        /// <summary>Fired when <see cref="isInForceSyncWindow"/> transitions from true to false.</summary>
        public event Action onForceSyncWindowClosed;

        /// <summary>
        /// Opens a force-sync window. While the window is open, the controller bypasses
        /// the change thresholds and ships state every tick, and observers can query
        /// <see cref="isInForceSyncWindow"/> to adjust local correction behaviour.
        /// Opening the window also sends the current state reliably once.
        /// </summary>
        /// <param name="seconds">Window duration in seconds. Pass -1 (default) for a one-tick window.</param>
        public void ForceSyncFor(float seconds = -1f)
        {
            if (!isSpawned)
                return;

            if (!IsController(_ownerAuth))
            {
                PurrLogger.LogWarning($"ForceSyncFor called on {gameObject.name} from a non-controller. Ignored.", this);
                return;
            }

            OpenForceSyncWindowLocal(seconds);

            if (isActiveAndEnabled)
            {
                SyncForceSyncWindow(seconds);
                SendCurrentState(true, IsSettledForSync());
            }
        }

        private void OpenForceSyncWindowLocal(float seconds)
        {
            bool wasOpen = isInForceSyncWindow;

            if (seconds < 0f)
            {
                _forceSyncOneShot = true;
            }
            else
            {
                double newEnd = Time.unscaledTimeAsDouble + seconds;
                if (newEnd > _forceSyncWindowEndTime)
                    _forceSyncWindowEndTime = newEnd;
            }

            bool isOpen = isInForceSyncWindow;
            if (!wasOpen && isOpen)
                onForceSyncWindowOpened?.Invoke();
            _wasInForceSyncWindow = isOpen;
        }

        private void TickForceSyncWindow()
        {
            bool wasOpen = _wasInForceSyncWindow;

            if (_forceSyncOneShot)
                _forceSyncOneShot = false;

            bool isOpen = isInForceSyncWindow;

            if (wasOpen && !isOpen)
                onForceSyncWindowClosed?.Invoke();

            _wasInForceSyncWindow = isOpen;
        }

        #endregion

        #region RPCs

        [TargetRpc(channel: Channel.ReliableOrdered, deltaPacked: true)]
        private void SendInitialStateToObserver(PlayerID player, RigidbodyStateData data, RigidbodySettingsData settings)
        {
            if (IsController(_ownerAuth))
                return;

            if (!_rigidbody)
                return;

            if (!ValidateIncomingSnapshot(in data, "initial observer state"))
                return;

            _rigidbody.mass = settings.mass;
            SetDrag(settings.drag);
            SetAngularDrag(settings.angularDrag);
            _rigidbody.useGravity = settings.useGravity;
            _rigidbody.isKinematic = settings.isKinematic;

            // Settings remain usable even if this first pose arrives before its parent.
            if (!TryResolveReceivedParent(data.parent, data.positionFrame, out var parentTrs))
                return;
            var syncPos = ExtractSyncPosition(data.positionFrame, data.position, data.absolutePosition);
            if (!TryToWorldPosition(syncPos, parentTrs, data.positionFrame, out var worldPos))
                return;

            if (!TryAcceptStateOrder(in data))
                return;

            ApplyReceivedSoftParent(data.parent, data.isSoftParent);

            _rigidbody.position = worldPos;
            _rigidbody.rotation = NormalizeQuaternion(ToWorldRotation(data.rotation, parentTrs));
            SetLinearVelocity(ToWorldLinearVelocity(data.linearVelocity, parentTrs, worldPos));
            SetAngularVelocity(ToWorldAngularVelocity(data.angularVelocity, parentTrs));

            _targetPosition = syncPos;
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;
            _targetParent = parentTrs;
            _targetPositionFrame = data.positionFrame;

            _lastSyncedPosition = syncPos;
            _lastSyncedRotation = data.rotation;
            _lastSyncedLinearVelocity = data.linearVelocity;
            _lastSyncedAngularVelocity = data.angularVelocity;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledState(data.linearVelocity, data.angularVelocity);

            ClearBuffer();
            PushSnapshot(data, true);
        }

        [TargetRpc(channel: Channel.ReliableOrdered, deltaPacked: true)]
        private void SendHandoffState(PlayerID player, RigidbodyStateData data)
        {
            if (IsController(_ownerAuth))
                return;

            if (!_rigidbody)
                return;

            if (!ValidateIncomingSnapshot(in data, "ownership handoff"))
                return;

            if (!TryResolveReceivedParent(data.parent, data.positionFrame, out var parentTrs))
                return;

            if (!TryAcceptStateOrder(in data))
                return;

            ApplyReceivedSoftParent(data.parent, data.isSoftParent);

            var syncPos = ExtractSyncPosition(data.positionFrame, data.position, data.absolutePosition);

            _targetPosition = syncPos;
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;
            _targetParent = parentTrs;
            _targetPositionFrame = data.positionFrame;

            _lastSyncedPosition = syncPos;
            _lastSyncedRotation = data.rotation;
            _lastSyncedLinearVelocity = data.linearVelocity;
            _lastSyncedAngularVelocity = data.angularVelocity;
            _lastSyncedParent = parentTrs;
            _lastSyncedWasSettled = IsSettledState(data.linearVelocity, data.angularVelocity);

            ClearBuffer();
            PushSnapshot(data, true);
        }

        [ObserversRpc(channel: Channel.Unreliable, deltaPacked: true, runLocally: true)]
        private void SyncState(RigidbodyStateData data)
        {
            if (IsController(_ownerAuth))
                return;

            PushSnapshot(data);
        }

        [ServerRpc(channel: Channel.Unreliable, deltaPacked: true)]
        private void SendStateToServer(RigidbodyStateData data, RPCInfo info = default)
        {
            if (!ValidateIncomingSnapshot(in data, "server receive"))
                return;

            if (!IsCurrentControllerSender(info))
                return;

            if (!TryPrepareStateForServerRelay(ref data))
                return;

            SyncState(data);
        }

        [ObserversRpc(channel: Channel.ReliableOrdered, deltaPacked: true, runLocally: true)]
        private void SyncReliableState(RigidbodyStateData data)
        {
            if (IsController(_ownerAuth))
                return;

            PushSnapshot(data);
        }

        [ServerRpc(channel: Channel.ReliableOrdered, deltaPacked: true)]
        private void SendReliableStateToServer(RigidbodyStateData data, RPCInfo info = default)
        {
            if (!ValidateIncomingSnapshot(in data, "reliable server receive"))
                return;

            if (!IsCurrentControllerSender(info))
                return;

            if (!TryPrepareStateForServerRelay(ref data))
                return;

            SyncReliableState(data);
        }

        [ObserversRpc(runLocally: true, channel: Channel.Unreliable)]
        private void BroadcastForce(AppliedForce force)
        {
            ApplyForce(force);
        }

        [ObserversRpc(excludeOwner: true, channel: Channel.Unreliable)]
        private void BroadcastForceToOthers(AppliedForce force)
        {
            ApplyForce(force);
        }

        [ObserversRpc(deltaPacked: true, runLocally: true)]
        private void Teleport(RigidbodyTeleportData data)
        {
            if (IsController(_ownerAuth))
                return;

            if (!_rigidbody)
                return;

            if (!TryResolveReceivedParent(data.parent, data.positionFrame, out var parentTrs))
                return;
            var syncPos = ExtractSyncPosition(data.positionFrame, data.position, data.absolutePosition);
            if (!TryToWorldPosition(syncPos, parentTrs, data.positionFrame, out var worldPos))
                return;

            var previousPosition = _rigidbody.position;
            var previousRotation = _rigidbody.rotation;

            _lastCorrectionReason = "Teleport";
            _hasPendingTeleport = true;

            ApplyReceivedSoftParent(data.parent, data.isSoftParent);

            _rigidbody.position = worldPos;
            _rigidbody.rotation = NormalizeQuaternion(ToWorldRotation(data.rotation, parentTrs));
            SetLinearVelocity(ToWorldLinearVelocity(data.linearVelocity, parentTrs, worldPos));
            SetAngularVelocity(ToWorldAngularVelocity(data.angularVelocity, parentTrs));

            _targetPosition = syncPos;
            _targetRotation = data.rotation;
            _targetLinearVelocity = data.linearVelocity;
            _targetAngularVelocity = data.angularVelocity;
            _targetParent = parentTrs;
            _targetPositionFrame = data.positionFrame;

            ClearBuffer();
            _hasPendingTeleport = false;

            if (_settingsInstance != null)
            {
                Vector3 worldTargetPos = worldPos;
                Quaternion worldTargetRot = ToWorldRotation(_targetRotation, _targetParent);
                Vector3 worldTargetLinVel = ToWorldLinearVelocity(_targetLinearVelocity, _targetParent, worldTargetPos);
                Vector3 worldTargetAngVel = ToWorldAngularVelocity(_targetAngularVelocity, _targetParent);
                var ctx = BuildCorrectionContext(
                    worldTargetPos,
                    worldTargetRot,
                    worldTargetLinVel,
                    worldTargetAngVel,
                    0f,
                    0f,
                    previousPosition,
                    previousRotation
                );
                _settingsInstance.OnReset(in ctx);
            }
        }

        [ServerRpc(deltaPacked: true)]
        private void SyncSettings(RigidbodySettingsData data)
        {
            SyncSettings_Internal(data);
            SyncSettings_Observer(data);
        }

        [ObserversRpc(bufferLast: true, excludeSender: true)]
        private void SyncSettings_Observer(RigidbodySettingsData data)
        {
            SyncSettings_Internal(data);
        }

        private void SyncSettings_Internal(RigidbodySettingsData data)
        {
            if (IsController(_ownerAuth))
                return;

            if (!_rigidbody)
                return;

            _rigidbody.mass = data.mass;
            SetDrag(data.drag);
            SetAngularDrag(data.angularDrag);
            _rigidbody.useGravity = data.useGravity;
            _rigidbody.isKinematic = data.isKinematic;
        }

        [ServerRpc(requireOwnership: false, deltaPacked: true)]
        private void RequestTeleport(CompressedVector3 position, double3 absolutePosition, RigidbodyPositionFrame frame, PackedQuaternion rotation)
        {
            if (_ownerAuth && owner.HasValue)
            {
                // position stays in the peer-agnostic wire frame; the owner converts it.
                ForwardTeleportRequest(owner.Value, position, absolutePosition, frame, rotation);
                return;
            }

            if (!_rigidbody)
                return;

            if (!TryToWorldPosition(ExtractSyncPosition(frame, position, absolutePosition), null, frame, out var worldPosition))
                return;
            _rigidbody.position = worldPosition;
            _rigidbody.rotation = rotation;
            BroadcastTeleport();
        }

        [TargetRpc(deltaPacked: true)]
        private void ForwardTeleportRequest(PlayerID target, CompressedVector3 position, double3 absolutePosition, RigidbodyPositionFrame frame, PackedQuaternion rotation)
        {
            if (!_rigidbody)
                return;

            if (!TryToWorldPosition(ExtractSyncPosition(frame, position, absolutePosition), null, frame, out var worldPosition))
                return;
            _rigidbody.position = worldPosition;
            _rigidbody.rotation = rotation;
            BroadcastTeleport();
        }

        private void BroadcastTeleport()
        {
            var parentIdentity = GetSyncParentIdentity(out var isSoft);
            var parentTrs = parentIdentity ? parentIdentity.transform : null;

            WriteWirePosition(parentTrs, out var wirePos, out var wireAbs, out var wireFrame);
            Teleport(new RigidbodyTeleportData
            {
                position = wirePos,
                absolutePosition = wireAbs,
                positionFrame = wireFrame,
                rotation = ReadRotation(parentTrs),
                linearVelocity = ReadLinearVelocity(parentTrs),
                angularVelocity = ReadAngularVelocity(parentTrs),
                parent = parentIdentity,
                isSoftParent = isSoft
            });
        }

        [ServerRpc(channel: Channel.ReliableOrdered)]
        private void SyncForceSyncWindow(float seconds)
        {
            SyncForceSyncWindow_Internal(seconds);
            SyncForceSyncWindow_Observer(seconds);
        }

        [ObserversRpc(channel: Channel.ReliableOrdered, excludeSender: true)]
        private void SyncForceSyncWindow_Observer(float seconds)
        {
            SyncForceSyncWindow_Internal(seconds);
        }

        private void SyncForceSyncWindow_Internal(float seconds)
        {
            if (IsController(_ownerAuth))
                return;

            OpenForceSyncWindowLocal(seconds);
        }

        #endregion
    }
}
