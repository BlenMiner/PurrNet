using System;
using System.Buffers;
using System.ComponentModel;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Network Transform")]
    public sealed class NetworkTransform : NetworkIdentity, INetworkTransform
    {
        public static INetworkTransformPositionTransform defaultPositionTransform { get; set; }

        [Header("What to Sync")]
        [Tooltip("Whether to sync the position of the transform. And if so, in what space.")]
        [SerializeField, PurrLock]
        private SyncMode _syncPosition = SyncMode.Local;

        [Tooltip("Whether to sync the rotation of the transform. And if so, in what space.")]
        [SerializeField, PurrLock]
        private SyncMode _syncRotation = SyncMode.Local;

        [Tooltip("Whether to sync the scale of the transform.")]
        [SerializeField, PurrLock]
        private bool _syncScale = true;

        [Tooltip("Whether to sync the parent of the transform. Only works if the parent is a NetworkIdentity.")]
        [SerializeField, PurrLock]
        private bool _syncParent = true;

        [Header("How to Sync")]
        [Tooltip("What to interpolate when syncing the transform.")]
        [SerializeField, PurrLock]
        private TransformSyncMode _interpolateSettings =
            TransformSyncMode.Position | TransformSyncMode.Rotation | TransformSyncMode.Scale;
        [Tooltip("The minimum amount of buffered ticks to store.\nThis is used for interpolation.")]
        [SerializeField, PurrLock, Min(1)] private int _minBufferSize = 1;
        [Tooltip("The maximum amount of buffered ticks to store.\nThis is used for interpolation.")]
        [SerializeField, PurrLock, Min(1)] private int _maxBufferSize = 2;
#if UNITY_PHYSICS_3D
        [Tooltip("Will enforce the character controller getting enabled and disabled when attempting to sync the transform - CAUTION - Physics events can/will be called multiple times")]
        [SerializeField]
        private bool _characterControllerPatch;
#endif
        [Header("When to Sync")]
        [FormerlySerializedAs("_clientAuth")]
        [Tooltip(
            "If true, the client can send transform data to the server. If false, the client can't send transform data to the server.")]
        [SerializeField, PurrLock]
        private bool _ownerAuth = true;

        [SerializeField]
        private InterpolationTiming _interpolationTiming = InterpolationTiming.Update;

        [Tooltip("Skips sends while motion stays reconstructible by receivers, reducing bandwidth " +
                 "for steady motion (linear or curved) without adding render delay. More aggressive " +
                 "levels skip more at the cost of reconstruction precision on observers. Erratic " +
                 "motion falls back to normal per-tick syncing automatically. Can be changed at " +
                 "runtime; inspector changes during play replicate to all peers.")]
        [SerializeField, InspectorName("Adaptive Sync")]
        private AdaptiveSyncLevel _adaptiveSynchronization = AdaptiveSyncLevel.Balanced;

        private bool _adaptiveDebugDump;

        private NetworkTransformSyncStrategy _customStrategy;
        private NetworkTransformSyncStrategy _activeStrategy;
        private bool _hasStrategy;

        private static readonly NetworkTransformDefaultStrategy[] _defaultStrategies = CreateDefaultStrategies();

        private static NetworkTransformDefaultStrategy[] CreateDefaultStrategies()
        {
            var strategies = new NetworkTransformDefaultStrategy[4];
            for (int i = 0; i < strategies.Length; i++)
            {
                strategies[i] = new NetworkTransformDefaultStrategy();
                strategies[i].ApplyLevel((AdaptiveSyncLevel)(i + 1));
            }

            return strategies;
        }

        /// <summary>
        /// Whether to sync the parent of the transform. Only works if the parent is a NetworkIdentity.
        /// </summary>
        public bool syncParent => _syncParent;

        public int ticksBehind
        {
            get
            {
                if (syncPosition)
                    return _position.bufferSize;
                if (syncRotation)
                    return _rotation.bufferSize;
                if (syncScale)
                    return _scale.bufferSize;
                return 0;
            }
        }

        /// <summary>
        /// Whether to sync the position of the transform.
        /// </summary>
        public bool syncPosition => _syncPosition != SyncMode.No;

        /// <summary>
        /// Whether to sync the rotation of the transform.
        /// </summary>
        public bool syncRotation => _syncRotation != SyncMode.No;

        /// <summary>
        /// Whether to sync the scale of the transform.
        /// </summary>
        public bool syncScale => _syncScale;

        /// <summary>
        /// Whether to interpolate the position of the transform.
        /// </summary>
        public bool interpolatePosition => _interpolateSettings.HasFlag(TransformSyncMode.Position);

        /// <summary>
        /// Whether to interpolate the rotation of the transform.
        /// </summary>
        public bool interpolateRotation => _interpolateSettings.HasFlag(TransformSyncMode.Rotation);

        /// <summary>
        /// Whether to interpolate the scale of the transform.
        /// </summary>
        public bool interpolateScale => _interpolateSettings.HasFlag(TransformSyncMode.Scale);

        /// <summary>
        /// Whether the client controls the transform if they are the owner.
        /// </summary>
        public bool ownerAuth => _ownerAuth;

        /// <summary>
        /// Whether adaptive reduced-rate syncing is active.
        /// </summary>
        public bool hasSyncStrategy => _hasStrategy;

        internal NetworkTransformSyncStrategy activeStrategy => _activeStrategy;

        /// <summary>
        /// Whether adaptive reduced-rate syncing is enabled. Setting true selects
        /// <see cref="AdaptiveSyncLevel.Balanced"/>; use <see cref="adaptiveSyncLevel"/> for
        /// finer control.
        /// </summary>
        public bool adaptiveSync
        {
            get => _adaptiveSynchronization != AdaptiveSyncLevel.Off;
            set => adaptiveSyncLevel = value ? AdaptiveSyncLevel.Balanced : AdaptiveSyncLevel.Off;
        }

        /// <summary>
        /// How aggressively adaptive sync skips sends. Uses the built-in default strategy tuned
        /// for the level unless a custom strategy is injected via <see cref="SetSyncStrategy"/>;
        /// injected strategies keep their own tuning (see
        /// <see cref="NetworkTransformSyncStrategy.ApplyLevel"/>) and any level other than
        /// <see cref="AdaptiveSyncLevel.Off"/> activates them unchanged.
        /// Setting this at runtime re-initializes the sync stream locally; it is NOT replicated
        /// to other peers — set it everywhere yourself. Changing the value in the inspector
        /// during play mode does replicate, so all peers stay consistent while tuning.
        /// </summary>
        public AdaptiveSyncLevel adaptiveSyncLevel
        {
            get => _adaptiveSynchronization;
            set => ApplyAdaptiveLevelInternal(value);
        }

        private void ApplyAdaptiveLevelInternal(AdaptiveSyncLevel level)
        {
            if (_adaptiveSynchronization == level)
                return;

            _adaptiveSynchronization = level;
#if UNITY_EDITOR
            _inspectorAdaptiveSync = level;
#endif
            ApplyStrategySettings();

            if (isSpawned)
                ResetUnreliableStream();
        }

        [ServerRpc(requireOwnership: false)]
        private void RequestAdaptiveLevelChange(AdaptiveSyncLevel level)
        {
            SyncAdaptiveLevelChange(level);
        }

        [ObserversRpc(runLocally: true, bufferLast: true)]
        private void SyncAdaptiveLevelChange(AdaptiveSyncLevel level)
        {
            ApplyAdaptiveLevelInternal(level);
        }

#if UNITY_EDITOR
        private AdaptiveSyncLevel _inspectorAdaptiveSync;

        private void OnValidate()
        {
            if (!Application.isPlaying || !isSpawned)
            {
                _inspectorAdaptiveSync = _adaptiveSynchronization;
                return;
            }

            if (_adaptiveSynchronization == _inspectorAdaptiveSync)
                return;

            var level = _adaptiveSynchronization;
            _adaptiveSynchronization = _inspectorAdaptiveSync;

            if (isServer)
                SyncAdaptiveLevelChange(level);
            else
                RequestAdaptiveLevelChange(level);
        }
#endif

        private ushort _skipCacheFrom;
        private ushort _skipCacheCurrent;
        private ushort _skipCachePrev;
        private bool _skipCacheHasPrev;
        private bool _skipCacheResult;
        private bool _hasSkipCache;

        internal ushort extrapVerifyBase;
        internal ushort extrapVerifyPrev;
        internal ushort extrapVerifyPrevPrev;
        internal byte extrapVerifyFlags;
        internal ushort extrapVerifyThrough;
        internal bool hasExtrapVerify;

        internal bool CanSkipCached(NTLastAdaptiveWrite lastWrite, ushort currentTick,
            in NetworkTransformState current)
        {
            if (_hasSkipCache && _skipCacheFrom == lastWrite.tick && _skipCacheCurrent == currentTick &&
                _skipCacheHasPrev == lastWrite.hasPrev && _skipCachePrev == lastWrite.prevTick)
                return _skipCacheResult;

            _skipCacheResult = _activeStrategy.CanSkip(this, lastWrite, currentTick, current);
            _skipCacheFrom = lastWrite.tick;
            _skipCacheCurrent = currentTick;
            _skipCachePrev = lastWrite.prevTick;
            _skipCacheHasPrev = lastWrite.hasPrev;
            _hasSkipCache = true;
            return _skipCacheResult;
        }

        /// <summary>
        /// Injects a custom sync strategy. Pass null to revert to the built-in default strategy.
        /// Call before spawning for full effect; when called on a spawned transform the new
        /// strategy applies immediately. Sharing one instance across transforms is safe as
        /// long as any strategy state is input-keyed memoization, as in the built-in strategies.
        /// </summary>
        public void SetSyncStrategy(NetworkTransformSyncStrategy strategy)
        {
            _customStrategy = strategy;
            ApplyStrategySettings();
        }

        private void ApplyStrategySettings()
        {
            _activeStrategy = _adaptiveSynchronization == AdaptiveSyncLevel.Off
                ? null
                : _customStrategy ?? _defaultStrategies[Mathf.Clamp((int)_adaptiveSynchronization, 1, 4) - 1];
            _hasStrategy = _activeStrategy != null;

            var nm = networkManager;
            if (!nm || nm.tickModule == null)
                return;

            int minSize;
            int maxSize;

            if (_hasStrategy)
            {
                minSize = 1;
                maxSize = 2;
                _adaptiveSpacing = Mathf.Clamp(
                    Mathf.RoundToInt(nm.tickModule.tickRate * _activeStrategy!.maxSendInterval), 2,
                    CAPTURE_HISTORY_SIZE - 2);
            }
            else
            {
                minSize = _minBufferSize;
                maxSize = Mathf.CeilToInt(nm.tickModule.tickRate * 0.15f) * 2;
            }

            if (_position != null)
            {
                _position.minBufferSize = minSize;
                _position.maxBufferSize = maxSize;
            }

            if (_rotation != null)
            {
                _rotation.minBufferSize = minSize;
                _rotation.maxBufferSize = maxSize;
            }

            if (_scale != null)
            {
                _scale.minBufferSize = minSize;
                _scale.maxBufferSize = maxSize;
            }
        }

        Interpolated<Vector3WithParent> _position;
        Interpolated<QuaternionWithParent> _rotation;
        Interpolated<ScaleWithParent> _scale;

        public Vector3 latestReadPosition
        {
            get
            {
                if (_lastReadData.absolutePosition.HasValue &&
                    TryResolvePositionTransform(out var trs))
                    return trs.ToLocal(this, _lastReadData.absolutePosition.Value);
                return _lastReadData.position.GetValueOrDefault();
            }
        }

        public Quaternion latestReadRotation => _lastReadData.rotation;

        public Vector3 latestReadScale => _lastReadData.scale;

        private Transform _trs;
#if UNITY_PHYSICS_3D
        private Rigidbody _rb;
        private bool _hasRigidbody;
#endif
#if UNITY_PHYSICS_2D
        private Rigidbody2D _rb2d;
        private bool _hasRigidbody2D;
#endif
        private const float POSE_VECTOR_EPSILON_SQR = 1e-8f;
#if UNITY_PHYSICS_2D
        private const float RIGIDBODY_2D_ROTATION_EPSILON = 0.005f;
#endif
#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
        private Transform _networkPosePositionParent;
        private Vector3 _networkPosePositionAnchor;
        private Transform _networkPoseRotationParent;
        private Quaternion _networkPoseRotationAnchor;
#endif
#if UNITY_PHYSICS_3D
        private CharacterController _controller;
#endif

        public Vector3 position { get; private set; }
        public Quaternion rotation { get; private set; }
        public Vector3 localScale { get; private set; }

        private Action _onUpdate;
        private Action _onLateUpdate;
        private Action _onLateLateUpdate;
#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
        private Action _onLateFixedUpdate;
        private int _lateFixedUpdateHandle = PurrAction<Action>.InvalidHandle;
#endif
        private int _updateHandle = PurrAction<Action>.InvalidHandle;
        private int _lateUpdateHandle = PurrAction<Action>.InvalidHandle;
        private int _lateLateUpdateHandle = PurrAction<Action>.InvalidHandle;

        private bool _positionTransformExplicit;
        private bool _useAbsoluteFrame;

        public INetworkTransformPositionTransform positionTransform { get; private set; }

        public void SetPositionTransform(INetworkTransformPositionTransform transform)
        {
            positionTransform = transform;
            _positionTransformExplicit = transform != null;
        }

        private void ResolvePositionTransform()
        {
            if (!_positionTransformExplicit)
                positionTransform = defaultPositionTransform;

            _useAbsoluteFrame = positionTransform != null &&
                                (_syncPosition == SyncMode.World ||
                                 (_syncPosition == SyncMode.Local && _trs && !_trs.parent));
        }

        private void Awake()
        {
            _onUpdate = OnUpdate;
            _onLateUpdate = OnLateUpdate;
            _onLateLateUpdate = LateLateUpdate;
#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
            _onLateFixedUpdate = LateFixedUpdate;
#endif
            _trs = transform;
#if UNITY_PHYSICS_3D
            _rb = GetComponent<Rigidbody>();
            _hasRigidbody = _rb;
            _controller = GetComponent<CharacterController>();
#endif
#if UNITY_PHYSICS_2D
            _rb2d = GetComponent<Rigidbody2D>();
            _hasRigidbody2D = _rb2d;
#if UNITY_PHYSICS_3D
            if (_hasRigidbody)
                _hasRigidbody2D = false;
#endif
#endif
            CacheCurrentPose();
        }

        private void OnEnable()
        {
            CacheCurrentPose();
            _updateHandle = UnityUpdate.update.Add(_onUpdate);
            _lateUpdateHandle = UnityUpdate.lateUpdate.Add(_onLateUpdate);
            _lateLateUpdateHandle = UnityLatestUpdate.latestUpdate.Add(_onLateLateUpdate);
#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
            _lateFixedUpdateHandle = UnityLatestUpdate.fixedUpdate.Add(_onLateFixedUpdate);
#endif

            if (!_trs)
                return;

            if (_wasOnSpawnedCalled)
            {
                if (_cachedIsController)
                {
                    ForceSync();
                }
                else
                {
                    RefreshCurrentState();
                    TeleportToData(_currentData);
                }
            }
        }

        private void OnDisable()
        {
            UnityUpdate.update.RemoveAt(_updateHandle, _onUpdate);
            UnityUpdate.lateUpdate.RemoveAt(_lateUpdateHandle, _onLateUpdate);
            UnityLatestUpdate.latestUpdate.RemoveAt(_lateLateUpdateHandle, _onLateLateUpdate);
            _updateHandle = PurrAction<Action>.InvalidHandle;
            _lateUpdateHandle = PurrAction<Action>.InvalidHandle;
            _lateLateUpdateHandle = PurrAction<Action>.InvalidHandle;
#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
            UnityLatestUpdate.fixedUpdate.RemoveAt(_lateFixedUpdateHandle, _onLateFixedUpdate);
            _lateFixedUpdateHandle = PurrAction<Action>.InvalidHandle;
#endif
        }

        protected override void OnEarlySpawn()
        {
            _trs = transform;
            _hasLivePose = false;
            CacheCurrentPose();
            ReCacheIsController();
            ResolvePositionTransform();

            float sendDelta = networkManager.tickModule.tickDelta;
            var p = _trs.parent;

            var data = GetCurrentTransformData();

            if (syncPosition)
            {
                var currentPos = MakePositionSample(p, data);
                _position = new Interpolated<Vector3WithParent>(interpolatePosition ? Vector3WithParent.Lerp : Vector3WithParent.NoLerp,
                    sendDelta, currentPos, _maxBufferSize, _minBufferSize);
            }

            if (syncRotation)
            {
                var currentRot = _syncRotation == SyncMode.World ?
                    new QuaternionWithParent(p, false, _trs.rotation) :
                    new QuaternionWithParent(p, true, _trs.localRotation);
                _rotation = new Interpolated<QuaternionWithParent>(
                    interpolateRotation ? QuaternionWithParent.Lerp : QuaternionWithParent.NoLerp,
                    sendDelta, currentRot, _maxBufferSize, _minBufferSize);
            }

            if (syncScale)
            {
                var currentScale = new ScaleWithParent(p, _trs.localScale);
                _scale = new Interpolated<ScaleWithParent>(interpolateScale ? ScaleWithParent.Lerp : ScaleWithParent.NoLerp,
                    sendDelta, currentScale, _maxBufferSize, _minBufferSize);
            }

            _currentData = data;
            _latestData = data;
            _lastReadData = data;
            _lastSentDelta = data;

            ResetUnreliableRecvState();
            BumpSendGen();
            RefreshLatestFrame();
            _currentFrame = _latestFrame;
            _currentParentId = _latestParentId;
            CaptureUnreliableState();
        }

        private Vector3WithParent MakePositionSample(Transform p, NetworkTransformData data)
        {
            if (data.absolutePosition.HasValue)
            {
                if (TryResolvePositionTransform(out var trs))
                    return new Vector3WithParent(this, trs, data.absolutePosition.Value);

                PurrLogger.LogError(
                    $"'{name}' received an absolute-frame position but has no {nameof(INetworkTransformPositionTransform)} " +
                    $"to decode it. Assign one via {nameof(SetPositionTransform)} or {nameof(defaultPositionTransform)}.", this);
            }
            if (!data.position.HasValue)
            {
                PurrLogger.LogError(
                    $"'{name}' received a {nameof(NetworkTransformData)} with no position in either frame. " +
                    $"Holding the current transform instead of snapping to the parent origin.", this);
                bool local = _syncPosition == SyncMode.Local;
                return new Vector3WithParent(p, local, local ? _trs.localPosition : _trs.position);
            }

            return new Vector3WithParent(p, _syncPosition == SyncMode.Local, data.position.Value);
        }

        private bool TryResolvePositionTransform(out INetworkTransformPositionTransform transform)
        {
            transform = positionTransform ?? defaultPositionTransform;
            return transform != null;
        }

        protected override void OnOwnerReconnected(PlayerID ownerId)
        {
            OnOwnerChanged(ownerId, ownerId, isServer);
        }

        private bool _cachedIsController;

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            _cachedConnectedOwner = hasConnectedOwner;
            ReCacheIsController();
            BumpSendGen();

            if (asServer)
                ResetUnreliableRecvState();

            if (!enabled)
            {
                return;
            }

            if (!_wasOnSpawnedCalled)
                return;

            if (!_ownerAuth)
                return;

            if (asServer)
            {
                var state = currentState;

                if (newOwner.HasValue && newOwner != localPlayer)
                    SendLatestState(newOwner.Value, state, false, _sendGen);

                if (oldOwner.HasValue && newOwner != oldOwner && oldOwner != localPlayer)
                    SendLatestState(oldOwner.Value, state, false, _sendGen);
            }
            else if (newOwner == localPlayer && !isServer)
            {
                RefreshCurrentState();
                SendLatestStateToServer(currentState, _sendGen);
            }
        }

        private void ReCacheIsController()
        {
            var wasController = _cachedIsController;
            _cachedIsController = IsController(_ownerAuth);
            if (wasController != _cachedIsController)
                OnIsControlledChanged(_cachedIsController);
        }

        private bool _wasOnSpawnedCalled;

        protected override void OnSpawned(bool asServer)
        {
            var wasController = _cachedIsController;
            _cachedIsController = IsController(_ownerAuth);
            if (wasController != _cachedIsController)
                OnIsControlledChanged(_cachedIsController);
            _wasOnSpawnedCalled = true;

            if (!networkManager.TryGetModule<NetworkTransformFactory>(asServer, out var factory))
                return;

            if (!factory.TryGetModule(sceneId, out var ntModule))
                return;

            if (!asServer && !isServer && IsController(localPlayerForced, _ownerAuth, false))
            {
                RefreshCurrentState();
                SendLatestStateToServer(currentState, _sendGen);
            }

            ntModule.Register(this);
        }

        protected override void OnDespawned(bool asServer)
        {
            _wasOnSpawnedCalled = false;
            ReleaseUnreliableHistory();

            if (!networkManager.TryGetModule<NetworkTransformFactory>(asServer, out var factory))
            {
                if (!networkManager.TryGetModule<NetworkTransformFactory>(true, out factory))
                    return;
            }

            if (!factory.TryGetModule(sceneId, out var ntModule))
                return;

            ntModule.Unregister(this);
        }

        private int _adaptiveSpacing = 2;

        internal int adaptiveSendSpacing => _adaptiveSpacing;

        protected override void OnSpawned()
        {
            ApplyStrategySettings();
        }

        protected override void OnObserverAdded(PlayerID player)
        {
            InvalidateObserverBaseline(player, true);

            if (!enabled)
            {
                return;
            }

            if (player == localPlayer)
                return;

            if (!_ownerAuth || player != owner)
                SendLatestState(player, currentState, true, _sendGen);
        }

        protected override void OnObserverRemoved(PlayerID player)
        {
            InvalidateObserverBaseline(player, false);
        }

        private void InvalidateObserverBaseline(PlayerID player, bool enqueue)
        {
            if (!networkManager || !id.HasValue)
                return;

            if (networkManager.TryGetModule<NetworkTransformFactory>(isServer, out var factory) &&
                factory.TryGetModule(sceneId, out var ntModule))
                ntModule.InvalidateSendBaseline(player, id.Value, enqueue);
        }

        /// <summary>
        /// Forces the latest NT state to target player, voiding compression and other optimizations
        /// </summary>
        public void ForceSync(PlayerID target)
        {
            if (target == localPlayer)
                return;

            BumpSendGen(target);
            RefreshCurrentState();
            SendLatestState(target, currentState, true, _sendGen);
        }

        /// <summary>
        /// Forces the latest NT state to everyone, voiding compression and other optimizations
        /// </summary>
        public void ForceSync()
        {
            if (!_cachedIsController)
                return;

            BumpSendGen();
            RefreshCurrentState();
            _lastSentDelta = _currentData;
            var state = currentState;

            if (isServer)
            {
                int obCount = observers.Count;
                var localP = localPlayer;

                for (var i = 0; i < obCount; i++)
                {
                    var observer = observers[i];

                    if ((_ownerAuth && owner == observer) || observer == localP)
                        continue;

                    SendLatestState(observer, state, true, _sendGen);
                }
            }
            else
            {
                ForceSyncServer(state, _sendGen);
            }
        }

        [ServerRpc]
        private void ForceSyncServer(NetworkTransformState state, byte gen, RPCInfo info = default)
        {
            if (!_ownerAuth || !IsControlling(info.sender, false))
                return;

            if (!ForceAdoptRecvGen(gen))
                return;

            BumpSendGen();
            AdoptState(state);
            _lastSentDelta = state.data;
            TeleportToState(state);
            ApplyLerpedPosition();

            int obCount = observers.Count;
            var localP = localPlayer;

            for (var i = 0; i < obCount; i++)
            {
                var observer = observers[i];

                if (owner == observer || observer == localP)
                    continue;

                SendLatestState(observer, state, true, _sendGen);
            }
        }

        /// <summary>
        /// Clears interpolation and teleports the transform to the target position, rotation and scale.
        /// Works on both owner and non-owner clients. Also cancels any in-flight seam blending
        /// (e.g. from an ownership handoff) so the snap is not smoothed away.
        /// </summary>
        public void ClearInterpolation(Vector3? targetPos, Quaternion? targetRot, Vector3? targetScale)
        {
            _hasCorrOffset = false;
            _corrPending = false;
            _hasPrevAnchor = false;
            _hasCorrPrevTarget = false;
            _hasLastSample = false;
            _seamOffset = Vector3.zero;

            var p = _trs.parent;
            if (syncPosition && targetPos.HasValue)
            {
                if (_useAbsoluteFrame)
                    _position.Teleport(new Vector3WithParent(this, positionTransform,
                        positionTransform.ToAbsolute(this, targetPos.Value)));
                else
                    _position.Teleport(new Vector3WithParent(p, _syncPosition == SyncMode.Local, targetPos.Value));
            }
            if (syncRotation && targetRot.HasValue)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, targetRot.Value));
            if (syncScale && targetScale.HasValue)
                _scale.Teleport(new ScaleWithParent(p, targetScale.Value));
        }

        [ServerRpc]
        private void SendLatestStateToServer(NetworkTransformState state, byte gen, RPCInfo info = default)
        {
            if (!_ownerAuth || !IsControlling(info.sender, false))
                return;

            if (!ForceAdoptRecvGen(gen))
                return;

            if (_hasStrategy && !_cachedIsController && _hasLivePose)
            {
                // Teleporting to the new owner's render-delayed pose would bake a rewind into
                // the relayed stream; keep rendering from the current pose instead. The stream
                // stays continuous, so observer baselines stay valid without a gen bump.
                _lastReadData = state.data;
                return;
            }

            BumpSendGen();
            AdoptState(state);
            _lastSentDelta = state.data;
            TeleportToState(state);
            ApplyLerpedPosition();
        }

        [TargetRpc]
        private void SendLatestState(PlayerID player, NetworkTransformState state, bool applyPosition, byte gen)
        {
            TryApplyTargetedState(state, applyPosition, gen);
        }

        internal bool TryApplyTargetedState(in NetworkTransformState state, bool applyPosition, byte gen)
        {
            if (!ForceAdoptRecvGen(gen))
                return false;

            // Adopting on the controller would briefly replace the outgoing capture with a stale pose.
            if (_cachedIsController)
                return true;

            AdoptState(state);
            _hasLivePose = true;

            if (applyPosition)
            {
                TeleportToState(state);
                ApplyLerpedPosition();
            }

            return true;
        }

#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
        private void LateFixedUpdate()
        {
            if (!isSpawned || _cachedIsController)
                return;

#if UNITY_PHYSICS_3D
            StabilizeObserverRigidbody();
#endif
#if UNITY_PHYSICS_2D
            StabilizeObserverRigidbody2D();
#endif
        }
#endif

#if UNITY_PHYSICS_3D
        private void StabilizeObserverRigidbody()
        {
            if (_cachedIsController || !_hasRigidbody)
                return;

            var targetPosition = syncPosition ? ResolveNetworkPosePosition() : position;
            var targetRotation = syncRotation ? ResolveNetworkPoseRotation() : rotation;

            ApplyObserverRigidbodyPose(
                _rb, syncPosition, targetPosition, syncRotation, targetRotation);
        }

        internal static void ApplyObserverRigidbodyPose(
            Rigidbody body, bool applyPosition, Vector3 targetPosition,
            bool applyRotation, Quaternion targetRotation)
        {
            if (!body)
                return;

            if (applyPosition)
            {
                if ((body.position - targetPosition).sqrMagnitude > POSE_VECTOR_EPSILON_SQR)
                    body.position = targetPosition;
            }

            if (applyRotation)
            {
                if (HasRotationChanged(body.rotation, targetRotation))
                    body.rotation = targetRotation;
            }

            if (body.isKinematic)
                return;

            if (applyPosition)
            {
                NetworkRigidbodyPhysics.SetLinearVelocity(body, Vector3.zero);

                var accumulatedForce = body.GetAccumulatedForce();
                body.AddForce(-accumulatedForce, ForceMode.Force);
                if (body.useGravity)
                    body.AddForce(-Physics.gravity, ForceMode.Acceleration);
            }

            if (applyRotation)
            {
                NetworkRigidbodyPhysics.SetAngularVelocity(body, Vector3.zero);

                var accumulatedTorque = body.GetAccumulatedTorque();
                body.AddTorque(-accumulatedTorque, ForceMode.Force);
            }
        }
#endif

#if UNITY_PHYSICS_2D
        private void StabilizeObserverRigidbody2D()
        {
            if (_cachedIsController || !_hasRigidbody2D || !_rb2d)
                return;

            var targetPosition = syncPosition ? ResolveNetworkPosePosition() : position;
            var targetRotation = syncRotation ? ResolveNetworkPoseRotation() : rotation;

            ApplyObserverRigidbodyPose(
                _rb2d, syncPosition, targetPosition, syncRotation, targetRotation);
        }

        private static void ApplyObserverRigidbodyPose(
            Rigidbody2D body, bool applyPosition, Vector3 targetPosition,
            bool applyRotation, Quaternion targetRotation)
        {
            if (!body)
                return;

            if (applyPosition)
            {
                var worldPosition = new Vector2(targetPosition.x, targetPosition.y);
                if ((body.position - worldPosition).sqrMagnitude > POSE_VECTOR_EPSILON_SQR)
                    body.position = worldPosition;
            }

            if (applyRotation)
            {
                float worldRotation = targetRotation.eulerAngles.z;
                if (Mathf.Abs(Mathf.DeltaAngle(body.rotation, worldRotation)) >
                    RIGIDBODY_2D_ROTATION_EPSILON)
                    body.rotation = worldRotation;
            }

            if (body.bodyType != RigidbodyType2D.Dynamic)
                return;

            if (applyPosition)
            {
                body.totalForce = Vector2.zero;
#if UNITY_6000_0_OR_NEWER
                body.linearVelocity = Vector2.zero;
#else
                body.velocity = Vector2.zero;
#endif
                if (!Mathf.Approximately(body.gravityScale, 0f))
                    body.AddForce(
                        -Physics2D.gravity * (body.gravityScale * body.mass),
                        ForceMode2D.Force);
            }

            if (applyRotation)
            {
                body.angularVelocity = 0f;
                body.totalTorque = 0f;
            }
        }
#endif

        private void OnUpdate()
        {
            if (_interpolationTiming == InterpolationTiming.Update)
                UpdateNT();

            if (_adaptiveDebugDump && _trs)
            {
                var wp = _trs.position;
                DebugDumpLine($"frame,time={Time.unscaledTime:F4},controller={_cachedIsController}," +
                              $"pos={wp.x:F4}|{wp.y:F4}|{wp.z:F4}");
            }
        }

        private void OnLateUpdate()
        {
            if (_interpolationTiming == InterpolationTiming.LateUpdate)
                UpdateNT();
        }

        private void LateLateUpdate()
        {
            if (_interpolationTiming == InterpolationTiming.LateLateUpdate)
                UpdateNT();
        }

        private void OnIsControlledChanged(bool isController)
        {
            if (isController)
            {
                _hasLivePose = true;
                // The receive pipeline is inert while controlling; stale anchors and history
                // must not survive into a later return to observer rendering.
                ResetUnreliableRecvState();
            }
            else
            {
                _latestData = GetCurrentTransformData();
                RefreshLatestFrame();
                TeleportToData(_latestData);
                CacheCurrentPose();
            }
        }

        private void CacheCurrentPose()
        {
            if (!_trs)
                return;

            position = _trs.position;
            rotation = _trs.rotation;
            localScale = _trs.localScale;

#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
            CaptureNetworkPosePosition(position);
            CaptureNetworkPoseRotation(rotation);
#endif
        }

#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
        private void CaptureNetworkPosePosition(Vector3 worldPosition)
        {
            var nparent = _syncPosition == SyncMode.Local ? _trs.parent : null;
            _networkPosePositionParent = nparent;
            _networkPosePositionAnchor = nparent
                ? nparent.InverseTransformPoint(worldPosition)
                : worldPosition;
        }

        private Vector3 ResolveNetworkPosePosition()
        {
            var nparent = _networkPosePositionParent;
            return _syncPosition == SyncMode.Local && nparent && _trs.parent == nparent
                ? nparent.TransformPoint(_networkPosePositionAnchor)
                : position;
        }

        private void CaptureNetworkPoseRotation(Quaternion worldRotation)
        {
            var nparent = _syncRotation == SyncMode.Local ? _trs.parent : null;
            _networkPoseRotationParent = nparent;
            _networkPoseRotationAnchor = nparent
                ? Quaternion.Inverse(nparent.rotation) * worldRotation
                : worldRotation;
        }

        private Quaternion ResolveNetworkPoseRotation()
        {
            var nparent = _networkPoseRotationParent;
            return _syncRotation == SyncMode.Local && nparent && _trs.parent == nparent
                ? nparent.rotation * _networkPoseRotationAnchor
                : rotation;
        }
#endif

        private void UpdateNT()
        {
            if (!isSpawned)
                return;

            bool isLocalController = _cachedIsController;

            if (!isLocalController)
                ApplyLerpedPosition();
            _latestData = GetCurrentTransformData();
            RefreshLatestFrame();
        }

        private void ApplyLerpedPosition()
        {
#if UNITY_PHYSICS_3D
            bool hasRigidbody = _hasRigidbody && _rb;
            bool disableController = !hasRigidbody && _controller && _controller.enabled;

            if (disableController && _characterControllerPatch)
                _controller.enabled = false;
#endif
#if UNITY_PHYSICS_2D
            bool hasRigidbody2D = _hasRigidbody2D && _rb2d;
#endif

            var worldPos = position;
            var worldRot = rotation;
            bool applyTransformPosition = false;
            bool applyTransformRotation = false;

            if (syncPosition)
            {
                worldPos = _position.Advance(Time.unscaledDeltaTime).position;
#if UNITY_PHYSICS_3D
                if (hasRigidbody)
                {
                    if ((_rb.position - worldPos).sqrMagnitude > POSE_VECTOR_EPSILON_SQR)
                        _rb.position = worldPos;
                }
#endif
#if UNITY_PHYSICS_2D
                if (hasRigidbody2D)
                {
                    var worldPosition2D = new Vector2(worldPos.x, worldPos.y);
                    if ((_rb2d.position - worldPosition2D).sqrMagnitude > POSE_VECTOR_EPSILON_SQR)
                        _rb2d.position = worldPosition2D;
                }
#endif
                applyTransformPosition =
                    (_trs.position - worldPos).sqrMagnitude > POSE_VECTOR_EPSILON_SQR;
                position = worldPos;
#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
                CaptureNetworkPosePosition(worldPos);
#endif
            }

            if (syncRotation)
            {
                worldRot = _rotation.Advance(Time.unscaledDeltaTime).rotation;
#if UNITY_PHYSICS_3D
                if (hasRigidbody)
                {
                    if (HasRotationChanged(_rb.rotation, worldRot))
                        _rb.rotation = worldRot;
                }
#endif
#if UNITY_PHYSICS_2D
                if (hasRigidbody2D)
                {
                    float worldRotation2D = worldRot.eulerAngles.z;
                    if (Mathf.Abs(Mathf.DeltaAngle(_rb2d.rotation, worldRotation2D)) >
                        RIGIDBODY_2D_ROTATION_EPSILON)
                        _rb2d.rotation = worldRotation2D;
                }
#endif
                applyTransformRotation = HasRotationChanged(_trs.rotation, worldRot);
                rotation = worldRot;
#if UNITY_PHYSICS_3D || UNITY_PHYSICS_2D
                CaptureNetworkPoseRotation(worldRot);
#endif
            }

            switch (applyTransformPosition)
            {
                case true when applyTransformRotation:
                    _trs.SetPositionAndRotation(worldPos, worldRot);
                    break;
                case true:
                    _trs.position = worldPos;
                    break;
                default:
                {
                    if (applyTransformRotation)
                        _trs.rotation = worldRot;
                    break;
                }
            }

            if (syncScale)
            {
                var worldScale = _scale.Advance(Time.unscaledDeltaTime).scale;
                var parentTrs = _trs.parent;
                var ls = parentTrs ? parentTrs.GetLocalScale(worldScale) : worldScale;
                if (!_trs.localScale.Equals(ls))
                    _trs.localScale = ls;
                this.localScale = ls;
            }

#if UNITY_PHYSICS_3D
            if (disableController && _characterControllerPatch)
                _controller.enabled = true;
#endif
        }

        private static bool HasRotationChanged(Quaternion current, Quaternion target)
        {
            return Quaternion.Angle(current, target) > 0f;
        }

        private NetworkTransformData GetCurrentTransformData()
        {
            Vector3 pos;
            Quaternion rot;

            if (_syncPosition == _syncRotation)
            {
                switch (_syncPosition)
                {
                    case SyncMode.World:
                        _trs.GetPositionAndRotation(out pos, out rot);
                        break;
                    case SyncMode.Local:
                        _trs.GetLocalPositionAndRotation(out pos, out rot);
                        break;
                    case SyncMode.No:
                    default:
                        pos = Vector3.zero;
                        rot = Quaternion.identity;
                        break;
                }
            }
            else
            {
                pos = _syncPosition switch
                {
                    SyncMode.World => _trs.position,
                    SyncMode.Local => _trs.localPosition,
                    _ => Vector3.zero
                };

                rot = _syncRotation switch
                {
                    SyncMode.World => _trs.rotation,
                    SyncMode.Local => _trs.localRotation,
                    _ => Quaternion.identity
                };
            }

            var ntScale = _syncScale ? _trs.localScale : default;

            if (_useAbsoluteFrame)
                return new NetworkTransformData(null, positionTransform.ToAbsolute(this, pos), rot, ntScale);

            return new NetworkTransformData((CompressedVector3)pos, null, rot, ntScale);
        }

        void OnTransformParentChanged()
        {
            if (!isSpawned)
                return;

            if (_isIgnoringParentChanges)
                return;

            if (_cachedIsController)
            {
                _latestData = GetCurrentTransformData();
                RefreshLatestFrame();
            }

            if (_syncPosition == SyncMode.Local && positionTransform != null)
            {
                bool wasAbsolute = _useAbsoluteFrame;
                ResolvePositionTransform();
                if (wasAbsolute != _useAbsoluteFrame)
                    ForceSync();
            }

            if (!_syncParent)
                return;

            HandleParentChanged(_trs.parent);
        }

        private void HandleParentChanged(Transform parent)
        {
            if (networkManager.TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                factory.TryGetHierarchy(sceneId, out var hierarchy))
            {
                hierarchy.OnParentChanged(this, parent);
            }
        }

        private bool _isIgnoringParentChanges;

        public void StartIgnoringParentChanges()
        {
            _isIgnoringParentChanges = true;
        }

        public void StopIgnoringParentChanges()
        {
            _isIgnoringParentChanges = false;
        }

        private void TeleportToData(NetworkTransformData data)
        {
            var p = _trs.parent;

            if (syncPosition)
                _position.Teleport(MakePositionSample(p, data));

            if (syncRotation)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, data.rotation));

            if (syncScale)
                _scale.Teleport(new ScaleWithParent(p, data.scale));
        }

        private NetworkTransformData _latestData;
        private NetworkTransformData _currentData;
        private NetworkTransformData _lastReadData;
        private NetworkTransformData _lastSentDelta;

        public void GatherState()
        {
            _currentData = _latestData;
            _currentFrame = _latestFrame;
            _currentParentId = _latestParentId;
        }

        public bool HasChanges()
        {
            return !_currentData.Equals(_lastSentDelta);
        }

        public void DeltaSave()
        {
            _lastSentDelta = _currentData;
        }

        private NetworkTransformState _capturedState;
        private bool _hasCapturedState;
        private uint _capturedRevision;
        private byte _sendGen;
        private byte _recvGen;
        private bool _hasRecvGen;
        private bool _hasAuthoritativeRecvGen;
        private long _lastAppliedOrder;
        private bool _hasAppliedSeq;

        internal ref readonly NetworkTransformState capturedState => ref _capturedState;

        internal uint capturedRevision => _capturedRevision;

        internal byte sendGen => _sendGen;

        private uint _sendGenEpoch;

        internal uint sendGenEpoch => _sendGenEpoch;

        internal NTEncodeCache unreliableEncodeCache;

        internal int ntIndex = -1;

        internal NetworkID ntNid;

        internal bool ntRegistered;

        internal int ntServerIndex = -1;

        internal int GetNTIndex(bool asServer) => asServer ? ntServerIndex : ntIndex;

        internal void SetNTIndex(bool asServer, int index)
        {
            if (asServer)
                ntServerIndex = index;
            else
                ntIndex = index;
        }

        internal void PromoteNTRegistrationToServer()
        {
            ntServerIndex = ntIndex;
            ntIndex = -1;
        }

        private void BumpSendGen()
        {
            _sendGen++;
            _sendGenEpoch++;

            if (TryGetNetworkTransformModule(out var ntModule) && id.HasValue)
                ntModule.ClearGenerationOverrides(id.Value);
        }

        private void BumpSendGen(PlayerID target)
        {
            if (TryGetNetworkTransformModule(out var ntModule) && id.HasValue)
                ntModule.PrepareTargetedReset(target, id.Value, _sendGen, _sendGenEpoch);

            _sendGen++;
            _sendGenEpoch++;
        }

        private bool TryGetNetworkTransformModule(out NetworkTransformModule ntModule)
        {
            ntModule = null;

            return networkManager &&
                   networkManager.TryGetModule<NetworkTransformFactory>(isServer, out var factory) &&
                   factory.TryGetModule(sceneId, out ntModule);
        }

        private void ResetUnreliableRecvState()
        {
            _hasRecvGen = false;
            _hasAuthoritativeRecvGen = false;
            _hasAppliedSeq = false;
            _hasLastAppliedState = false;
            ClearAdaptiveAnchors();
        }

        private NetworkTransformState _lastAppliedState;
        private ushort _lastAppliedSenderTick;
        private bool _hasLastAppliedState;

        private bool _hasLivePose;

        private const float CORRECTION_DECAY = 0.65f;
        private const float RENDER_RATE_GAIN = 0.2f;
        private const float RENDER_RATE_MAX_SLOWDOWN = 0.5f;
        private const float RENDER_RATE_MAX_CATCHUP = 1f;
        private const int ADAPTIVE_RENDER_BUFFER_TICKS = 1;

        private float _renderRel;
        private bool _hasRenderTimeline;

        private Vector3 _corrPosOffset;
        private Quaternion _corrRotOffset = Quaternion.identity;
        private Vector3 _corrScaleOffset;
        private float _corrWeight;
        private NetworkTransformFrame _corrFrame;
        private NetworkID _corrParentId;
        private bool _hasCorrOffset;
        private bool _corrPending;
        private double3 _corrPrevTarget;
        private bool _hasCorrPrevTarget;

        private NetworkTransformState _anchorState;
        private NetworkTransformVelocity _anchorVelocity;
        private uint _anchorLocalTick;
        private ushort _anchorSenderTick;
        private bool _hasAdaptiveAnchor;

        private NetworkTransformState _prevAnchorState;
        private NetworkTransformVelocity _prevAnchorVelocity;
        private ushort _prevAnchorSenderTick;
        private bool _hasPrevAnchor;

        private uint _lastAdaptiveTick;

        private const int RECV_HISTORY_SIZE = 32;

        private NetworkTransformState[] _recvStates;
        private ushort[] _recvTicks;
        private int _recvCount;
        private int _recvHead;

        private void PushReceivedSample(ushort senderTick, in NetworkTransformState state)
        {
            if (_recvStates == null)
            {
                _recvStates = ArrayPool<NetworkTransformState>.Shared.Rent(RECV_HISTORY_SIZE);
                _recvTicks = ArrayPool<ushort>.Shared.Rent(RECV_HISTORY_SIZE);
            }

            _recvHead = (_recvHead + 1) % RECV_HISTORY_SIZE;
            _recvStates[_recvHead] = state;
            _recvTicks[_recvHead] = senderTick;
            if (_recvCount < RECV_HISTORY_SIZE)
                _recvCount++;
        }

        private NetworkTransformState SampleReceivedHistory(ushort targetTick)
        {
            NetworkTransformState upperState = default;
            NetworkTransformState lowerState = default;
            ushort upperTick = 0;
            ushort lowerTick = 0;
            bool hasUpper = false;
            bool hasLower = false;

            NetworkTransformState prevState = default;
            ushort prevTick = 0;
            bool hasPrev = false;

            for (int i = 0; i < _recvCount; i++)
            {
                int idx = (_recvHead - i + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;
                short diff = (short)(_recvTicks[idx] - targetTick);

                if (diff <= 0)
                {
                    lowerState = _recvStates[idx];
                    lowerTick = _recvTicks[idx];
                    hasLower = true;

                    if (i + 1 < _recvCount)
                    {
                        int prevIdx = (_recvHead - i - 1 + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;
                        prevState = _recvStates[prevIdx];
                        prevTick = _recvTicks[prevIdx];
                        hasPrev = true;
                    }

                    break;
                }

                upperState = _recvStates[idx];
                upperTick = _recvTicks[idx];
                hasUpper = true;
            }

            if (!hasLower)
                return hasUpper ? upperState : _anchorState;

            if (!hasUpper || lowerTick == upperTick)
                return lowerState;

            short span = (short)(upperTick - lowerTick);
            if (span <= 0)
                return upperState;

            short into = (short)(targetTick - lowerTick);

            if (span > _adaptiveSpacing)
            {
                if (!hasPrev || prevState.frame != lowerState.frame ||
                    !prevState.parentId.Equals(lowerState.parentId))
                    return lowerState;

                int prevGap = (short)(lowerTick - prevTick);
                if (prevGap < 1)
                    return lowerState;

                var restChord = NetworkTransformVelocity.Derive(prevState, lowerState, prevGap);
                int steps = into < _adaptiveSpacing ? into : _adaptiveSpacing;
                return NetworkTransformVelocity.Predict(lowerState, restChord, steps);
            }

            if (lowerState.frame != upperState.frame || !lowerState.parentId.Equals(upperState.parentId))
            {
                if (!hasPrev || prevState.frame != lowerState.frame ||
                    !prevState.parentId.Equals(lowerState.parentId))
                    return lowerState;

                int prevGap = (short)(lowerTick - prevTick);
                if (prevGap < 1)
                    return lowerState;

                var chord = NetworkTransformVelocity.Derive(prevState, lowerState, prevGap);
                return NetworkTransformVelocity.Predict(lowerState, chord, into);
            }

            float t = into / (float)span;

            if (hasPrev && _activeStrategy != null &&
                _activeStrategy.TryReconstructState(prevState, lowerState, upperState, t, out var shaped))
                return shaped;

            return NetworkTransformVelocity.Lerp(lowerState, upperState, t);
        }

        private bool TryStrategyExtrapolation(int back, ushort targetTick, out NetworkTransformState result)
        {
            result = default;

            if (_recvCount < back + 3)
                return false;

            int i0 = (_recvHead - back + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;
            int i1 = (i0 - 1 + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;
            int i2 = (i1 - 1 + RECV_HISTORY_SIZE) % RECV_HISTORY_SIZE;

            int span = (short)(_recvTicks[i0] - _recvTicks[i1]);
            if (span < 2)
                return false;

            int rel = (short)(targetTick - _recvTicks[i0]);
            if (rel <= 0)
                return false;

            float t = (span + rel) / (float)span;
            return _activeStrategy.TryReconstructState(_recvStates[i2], _recvStates[i1], _recvStates[i0], t,
                out result);
        }

        private void ClearAdaptiveAnchors()
        {
            _hasAdaptiveAnchor = false;
            _hasPrevAnchor = false;
            _recvCount = 0;
            _hasLastSample = false;
            _hasRenderTimeline = false;
            _hasCorrOffset = false;
            _corrPending = false;
            _hasCorrPrevTarget = false;
            _seamOffset = Vector3.zero;
        }

        private void SetAdaptiveAnchor(in NetworkTransformState state, in NetworkTransformVelocity velocity)
        {
            var nm = networkManager;
            uint localTick = nm && nm.tickModule != null ? nm.tickModule.localTick : 0u;

            if (_hasAdaptiveAnchor)
            {
                _prevAnchorState = _anchorState;
                _prevAnchorVelocity = _anchorVelocity;
                _prevAnchorSenderTick = _anchorSenderTick;
                _hasPrevAnchor = true;
                _corrPending = true;
            }

            _anchorState = state;
            _anchorVelocity = velocity;
            _anchorLocalTick = localTick;
            _anchorSenderTick = _lastAppliedSenderTick;
            _hasAdaptiveAnchor = true;
        }

        private System.IO.StreamWriter _debugWriter;

        internal bool adaptiveDebugDumpEnabled => _adaptiveDebugDump;

        /// <summary>
        /// Diagnostic: writes a per-tick adaptive sync log for this transform to
        /// <c>&lt;temp&gt;/purrnet_nt_debug/</c> until disabled. Enable at runtime on the
        /// sender's own transform and on the observer's remote transform while reproducing
        /// an issue, then send the files to support. Also available from the component's
        /// context menu.
        /// </summary>
        public void SetAdaptiveDebugDump(bool enabled)
        {
            if (_adaptiveDebugDump == enabled)
                return;

            _adaptiveDebugDump = enabled;

            if (enabled)
            {
                PurrLogger.Log($"[NT DEBUG] dump enabled for '{name}'", this);
            }
            else if (_debugWriter != null)
            {
                _debugWriter.Dispose();
                _debugWriter = null;
                PurrLogger.Log($"[NT DEBUG] dump disabled for '{name}', file closed", this);
            }
        }

        [ContextMenu("Adaptive Debug Dump/Enable")]
        private void EnableAdaptiveDebugDump() => SetAdaptiveDebugDump(true);

        [ContextMenu("Adaptive Debug Dump/Disable")]
        private void DisableAdaptiveDebugDump() => SetAdaptiveDebugDump(false);

        internal static string DebugPos(in NetworkTransformState state)
        {
            if (state.data.absolutePosition.HasValue)
            {
                var a = state.data.absolutePosition.Value;
                return $"abs:{a.x:F4}|{a.y:F4}|{a.z:F4}";
            }

            var p = state.data.position;
            return p.HasValue ? $"{p.Value.x.value:F4}|{p.Value.y.value:F4}|{p.Value.z.value:F4}" : "na";
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_debugWriter != null)
            {
                _debugWriter.Dispose();
                _debugWriter = null;
            }
        }

        internal void DebugDumpLine(string line)
        {
            if (!_adaptiveDebugDump)
                return;

            if (_debugWriter == null)
            {
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "purrnet_nt_debug");
                System.IO.Directory.CreateDirectory(dir);
                string role = isServer ? "server" : "client";
                int pid = System.Diagnostics.Process.GetCurrentProcess().Id;
                var path = System.IO.Path.Combine(dir, $"nt_{role}_{name}_{pid}.log");
                _debugWriter = new System.IO.StreamWriter(path, false) { AutoFlush = true };
                _debugWriter.WriteLine(
                    $"start,time={System.DateTime.Now:HH:mm:ss.fff},level={_adaptiveSynchronization}," +
                    $"tickRate={(networkManager && networkManager.tickModule != null ? networkManager.tickModule.tickRate : 0)}," +
                    $"spacing={_adaptiveSpacing},owner={owner},isServer={isServer},isController={_cachedIsController}");
                PurrLogger.Log($"[NT DEBUG] dumping to {path}");
            }

            _debugWriter.WriteLine(line);
        }

        internal bool TryTickAdaptiveRender(uint localTick, ushort vouchedTick, bool hasVouched,
            out NetworkTransformState state)
        {
            state = default;

            if (!_hasStrategy || _cachedIsController || !_hasAdaptiveAnchor)
                return false;

            if (_lastAdaptiveTick == localTick)
                return false;

            _lastAdaptiveTick = localTick;

            long maxAhead = _adaptiveSpacing;
            float targetRel;

            if (hasVouched)
            {
                targetRel = (short)(vouchedTick - _lastAppliedSenderTick) - ADAPTIVE_RENDER_BUFFER_TICKS;
            }
            else
            {
                long age = (long)localTick - _anchorLocalTick;
                if (age < 0)
                    age = 0;
                targetRel = age - _adaptiveSpacing;
            }

            if (targetRel > maxAhead)
                targetRel = maxAhead;

            if (!_hasRenderTimeline || Mathf.Abs(targetRel - _renderRel) > maxAhead + _adaptiveSpacing)
            {
                _renderRel = targetRel;
                _hasRenderTimeline = true;
            }
            else
            {
                float rate = 1f + Mathf.Clamp((targetRel - _renderRel) * RENDER_RATE_GAIN,
                    -RENDER_RATE_MAX_SLOWDOWN, RENDER_RATE_MAX_CATCHUP);
                _renderRel += rate;
                if (_renderRel > maxAhead)
                    _renderRel = maxAhead;
            }

            int relFloor = Mathf.FloorToInt(_renderRel);
            float frac = _renderRel - relFloor;
            ushort tickA = (ushort)(_lastAppliedSenderTick + relFloor);
            ushort tickB = (ushort)(tickA + 1);

            var target = SampleAdaptiveAt(relFloor, tickA);
            if (frac > 0f)
            {
                var next = SampleAdaptiveAt(relFloor + 1, tickB);
                if (next.frame == target.frame && next.parentId.Equals(target.parentId))
                    target = NetworkTransformVelocity.Lerp(target, next, frac);
            }

            if (_hasPrevAnchor &&
                (_prevAnchorState.frame != _anchorState.frame ||
                 !_prevAnchorState.parentId.Equals(_anchorState.parentId)))
            {
                _hasPrevAnchor = false;
                _corrPending = false;
            }

            if (_corrPending && _hasPrevAnchor &&
                target.frame == _anchorState.frame && target.parentId.Equals(_anchorState.parentId))
            {
                long prevAge = (short)(tickA - _prevAnchorSenderTick);

                if (prevAge > 0)
                {
                    if (prevAge > maxAhead + _adaptiveSpacing)
                        prevAge = maxAhead + _adaptiveSpacing;

                    var old = SampleOldAnchorAt(tickA, (int)prevAge);
                    if (frac > 0f)
                    {
                        var oldNext = SampleOldAnchorAt(tickB, (int)prevAge + 1);
                        if (oldNext.frame == old.frame && oldNext.parentId.Equals(old.parentId))
                            old = NetworkTransformVelocity.Lerp(old, oldNext, frac);
                    }

                    if (old.frame == target.frame && old.parentId.Equals(target.parentId))
                        CaptureCorrectionOffset(old, target);
                }
            }

            _corrPending = false;
            _hasPrevAnchor = false;

            if (_hasCorrOffset && (target.frame != _corrFrame || !target.parentId.Equals(_corrParentId)))
                _hasCorrOffset = false;

            if (_hasCorrOffset)
            {
                float posStep = 0f;
                double3 curTarget = default;
                bool hasCurTarget = true;

                if (target.data.position.HasValue)
                {
                    var tp = target.data.position.Value;
                    curTarget = new double3(tp.x.value, tp.y.value, tp.z.value);
                }
                else if (target.data.absolutePosition.HasValue)
                {
                    curTarget = target.data.absolutePosition.Value;
                }
                else
                {
                    hasCurTarget = false;
                }

                if (hasCurTarget)
                {
                    if (_hasCorrPrevTarget)
                        posStep = (float)math.length(curTarget - _corrPrevTarget);
                    _corrPrevTarget = curTarget;
                    _hasCorrPrevTarget = true;
                }

                // Cap the shrink at the target's own speed so a seam slows the render down
                // instead of playing the motion backwards.
                var nmc = networkManager;
                float corrTickDelta = nmc && nmc.tickModule != null ? nmc.tickModule.tickDelta : 1f / 30f;
                float posCap = Mathf.Max(posStep, corrTickDelta * 0.25f);
                float posMag = _corrPosOffset.magnitude;
                if (posMag > 0f)
                {
                    float shrink = Mathf.Min(posMag * (1f - CORRECTION_DECAY), posCap);
                    _corrPosOffset *= (posMag - shrink) / posMag;
                }

                _corrWeight *= CORRECTION_DECAY;
                if (_corrWeight < 0.02f && _corrPosOffset.sqrMagnitude < 1e-8f)
                    _hasCorrOffset = false;
                else
                    ApplyCorrectionOffset(ref target);
            }

            if (_adaptiveDebugDump)
            {
                string vouchRel = hasVouched ? ((short)(vouchedTick - _lastAppliedSenderTick)).ToString() : "na";
                string newestRel = _recvCount > 0
                    ? ((short)(_recvTicks[_recvHead] - _lastAppliedSenderTick)).ToString()
                    : "na";
                DebugDumpLine(
                    $"render,localTick={localTick},anchor={_lastAppliedSenderTick},rel={_renderRel:F2},target={targetRel:F2}," +
                    $"vouchRel={vouchRel},newestRel={newestRel},recvCount={_recvCount},extrap={relFloor >= 0}," +
                    $"corr={(_hasCorrOffset ? _corrWeight : 0f):F3},corrPosMag={_corrPosOffset.magnitude:F4}," +
                    $"pos={DebugPos(target)}");
            }

            state = target;
            return true;
        }

        private void CaptureCorrectionOffset(in NetworkTransformState old, in NetworkTransformState target)
        {
            var posDelta = Vector3.zero;
            if (old.data.position.HasValue && target.data.position.HasValue)
            {
                var op = old.data.position.Value;
                var tp = target.data.position.Value;
                posDelta = new Vector3(op.x.value - tp.x.value, op.y.value - tp.y.value, op.z.value - tp.z.value);
            }
            else if (old.data.absolutePosition.HasValue && target.data.absolutePosition.HasValue)
            {
                var d = old.data.absolutePosition.Value - target.data.absolutePosition.Value;
                posDelta = new Vector3((float)d.x, (float)d.y, (float)d.z);
            }

            var oRot = old.data.rotation;
            var tRot = target.data.rotation;
            var rotDelta = new Quaternion(oRot.x, oRot.y, oRot.z, oRot.w).normalized *
                           Quaternion.Inverse(new Quaternion(tRot.x, tRot.y, tRot.z, tRot.w).normalized);

            var oScale = old.data.scale;
            var tScale = target.data.scale;
            var scaleDelta = new Vector3(oScale.x.value - tScale.x.value, oScale.y.value - tScale.y.value,
                oScale.z.value - tScale.z.value);

            if (_hasCorrOffset)
            {
                posDelta += _corrPosOffset;
                rotDelta = Quaternion.Slerp(Quaternion.identity, _corrRotOffset, _corrWeight) * rotDelta;
                scaleDelta += _corrScaleOffset * _corrWeight;
            }

            _corrPosOffset = posDelta;
            _corrRotOffset = rotDelta;
            _corrScaleOffset = scaleDelta;
            _corrWeight = 1f;
            _corrFrame = target.frame;
            _corrParentId = target.parentId;
            _hasCorrOffset = true;
        }

        // Beyond this seam size the pose is treated as an intentional teleport and snaps instead.
        private const float MAX_SEAM_DISTANCE = 10f;

        private void SeedSeamCorrection(in NetworkTransformState state, Transform p)
        {
            if (!syncPosition)
                return;

            Vector3 posOffset;

            if (state.data.absolutePosition.HasValue)
            {
                if (!TryResolvePositionTransform(out var frame))
                    return;

                var d = frame.ToAbsolute(this, position) - state.data.absolutePosition.Value;
                posOffset = new Vector3((float)d.x, (float)d.y, (float)d.z);
            }
            else if (state.data.position.HasValue)
            {
                bool isLocal = _syncPosition == SyncMode.Local && p;
                var renderedPos = isLocal ? p.InverseTransformPoint(position) : position;
                var sp = state.data.position.Value;
                posOffset = renderedPos - new Vector3(sp.x.value, sp.y.value, sp.z.value);
            }
            else
            {
                return;
            }

            if (posOffset.sqrMagnitude > MAX_SEAM_DISTANCE * MAX_SEAM_DISTANCE)
                return;

            var renderedRot = _syncRotation == SyncMode.Local && p
                ? Quaternion.Inverse(p.rotation) * rotation
                : rotation;
            var tr = state.data.rotation;
            var rotOffset = renderedRot *
                            Quaternion.Inverse(new Quaternion(tr.x, tr.y, tr.z, tr.w).normalized);

            _corrPosOffset = posOffset;
            _corrRotOffset = rotOffset;
            _corrScaleOffset = Vector3.zero;
            _corrWeight = 1f;
            _corrFrame = state.frame;
            _corrParentId = state.parentId;
            _hasCorrOffset = true;
            _hasCorrPrevTarget = false;
        }

        private void ApplyCorrectionOffset(ref NetworkTransformState target)
        {
            if (target.data.position.HasValue && _corrPosOffset != Vector3.zero)
            {
                var p = target.data.position.Value;
                var pos = new Vector3(p.x.value, p.y.value, p.z.value) + _corrPosOffset;
                target.data.position = (CompressedVector3)pos;
            }
            else if (target.data.absolutePosition.HasValue && _corrPosOffset != Vector3.zero)
            {
                var ap = target.data.absolutePosition.Value;
                target.data.absolutePosition = new double3(
                    ap.x + _corrPosOffset.x, ap.y + _corrPosOffset.y, ap.z + _corrPosOffset.z);
            }

            if (Mathf.Abs(_corrRotOffset.w) < 0.9999999f)
            {
                var r = target.data.rotation;
                var rot = Quaternion.Slerp(Quaternion.identity, _corrRotOffset, _corrWeight) *
                          new Quaternion(r.x, r.y, r.z, r.w);
                r.x = new NormalizedFloat(rot.x);
                r.y = new NormalizedFloat(rot.y);
                r.z = new NormalizedFloat(rot.z);
                r.w = new NormalizedFloat(rot.w);
                target.data.rotation = r;
            }

            if (_corrScaleOffset != Vector3.zero)
            {
                var s = target.data.scale;
                var scale = new Vector3(s.x.value, s.y.value, s.z.value) + _corrScaleOffset * _corrWeight;
                target.data.scale = scale;
            }
        }

        private NetworkTransformState SampleAdaptiveAt(int rel, ushort tick)
        {
            if (rel < 0)
                return SampleReceivedHistory(tick);

            return TryStrategyExtrapolation(0, tick, out var shaped)
                ? shaped
                : NetworkTransformVelocity.Predict(_anchorState, _anchorVelocity, rel);
        }

        private NetworkTransformState SampleOldAnchorAt(ushort tick, int prevAge)
        {
            return TryStrategyExtrapolation(1, tick, out var shaped)
                ? shaped
                : NetworkTransformVelocity.Predict(_prevAnchorState, _prevAnchorVelocity, prevAge);
        }

        private const float SEAM_OFFSET_DECAY = 0.8f;

        private NetworkTransformFrame _lastSampleFrame;
        private NetworkID _lastSampleParentId;
        private Vector3 _lastSampleWorldPos;
        private Vector3 _seamOffset;
        private bool _hasLastSample;

        internal void ApplyAdaptiveSample(in NetworkTransformState state, NetworkIdentity frameParent)
        {
            if (_adaptiveDebugDump)
                DebugDumpLine($"sample,pos={DebugPos(state)},seam={_seamOffset.magnitude:F4}");

            var p = state.frame switch
            {
                NetworkTransformFrame.LocalIdentity => frameParent.transform,
                NetworkTransformFrame.LocalStatic => _trs.parent,
                _ => null
            };

            if (syncPosition && state.data.position.HasValue)
            {
                var quantized = state.data.position.Value;
                var localPos = new Vector3(quantized.x.value, quantized.y.value, quantized.z.value);
                bool isLocal = _syncPosition == SyncMode.Local && p;
                var world = isLocal ? p.TransformPoint(localPos) : localPos;

                if (_hasLastSample &&
                    (state.frame != _lastSampleFrame || !state.parentId.Equals(_lastSampleParentId)))
                    _seamOffset += _lastSampleWorldPos - world;

                _lastSampleFrame = state.frame;
                _lastSampleParentId = state.parentId;

                if (_seamOffset.sqrMagnitude > 0.000001f)
                {
                    _seamOffset *= SEAM_OFFSET_DECAY;
                    world += _seamOffset;

                    var adjusted = state;
                    adjusted.data.position = (CompressedVector3)(isLocal ? p.InverseTransformPoint(world) : world);
                    _lastSampleWorldPos = world;
                    _hasLastSample = true;
                    AddStateToBuffers(adjusted, p);
                    return;
                }

                _seamOffset = Vector3.zero;
                _lastSampleWorldPos = world;
                _hasLastSample = true;
            }

            AddStateToBuffers(state, p);
        }

        private void TeleportBuffers(in NetworkTransformState state, Transform p)
        {
            if (syncPosition)
                _position.Teleport(MakePositionSample(p, state.data));

            if (syncRotation)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, state.data.rotation));

            if (syncScale)
                _scale.Teleport(new ScaleWithParent(p, state.data.scale));
        }

        private void AddStateToBuffers(in NetworkTransformState state, Transform p)
        {
            if (syncPosition)
                _position.Add(MakePositionSample(p, state.data));

            if (syncRotation)
                _rotation.Add(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, state.data.rotation));

            if (syncScale)
                _scale.Add(new ScaleWithParent(p, state.data.scale));
        }

        internal void ResetUnreliableStream()
        {
            BumpSendGen();
            ResetUnreliableRecvState();
        }

        private bool ForceAdoptRecvGen(byte gen)
        {
            bool alreadyAhead = _hasRecvGen && _hasAppliedSeq && gen == _recvGen;

            _recvGen = gen;
            _hasRecvGen = true;
            _hasAuthoritativeRecvGen = true;

            if (!alreadyAhead)
                _hasAppliedSeq = false;

            return !alreadyAhead;
        }

        private Transform _cachedParentTrs;
        private NetworkIdentity _cachedParentIdentity;
        private NetworkTransformFrame _latestFrame;
        private NetworkID _latestParentId;
        private NetworkTransformFrame _currentFrame;
        private NetworkID _currentParentId;

        private void RefreshLatestFrame()
        {
            var p = _trs.parent;

            if (!ReferenceEquals(p, _cachedParentTrs))
            {
                _cachedParentTrs = p;
                _cachedParentIdentity = p && p.TryGetComponent<NetworkIdentity>(out var found) ? found : null;
            }

            var parentIdentity = _cachedParentIdentity;

            if (_syncParent && parentIdentity && parentIdentity.isSpawned && parentIdentity.id.HasValue)
            {
                _latestFrame = NetworkTransformFrame.LocalIdentity;
                _latestParentId = parentIdentity.id.Value;
            }
            else
            {
                _latestFrame = p ? NetworkTransformFrame.LocalStatic : NetworkTransformFrame.World;
                _latestParentId = default;
            }
        }

        private const int CAPTURE_HISTORY_SIZE = 32;

        private NetworkTransformState[] _historyStates;
        private ushort[] _historyTicks;
        private bool[] _historyUsed;

        internal bool TryGetCapturedAt(ushort tick, out NetworkTransformState state)
        {
            int slot = tick % CAPTURE_HISTORY_SIZE;
            if (_historyUsed != null && _historyUsed[slot] && _historyTicks[slot] == tick)
            {
                state = _historyStates[slot];
                return true;
            }

            state = default;
            return false;
        }

        internal bool IsChordInterpolable(in NetworkTransformState from, ushort fromTick, ushort currentTick,
            in NetworkTransformState current)
        {
            int gap = (short)(currentTick - fromTick);
            if (gap <= 1)
                return true;

            if (current.frame != from.frame || !current.parentId.Equals(from.parentId))
                return false;

            var chord = NetworkTransformVelocity.Derive(from, current, gap);
            var strategy = _activeStrategy;
            int shift = strategy?.toleranceVelocityShift ?? 2;
            long cap = strategy?.toleranceCapMultiplier ?? 64;

            for (int step = 1; step < gap; step++)
            {
                if (!TryGetCapturedAt((ushort)(fromTick + step), out var actual))
                    return false;

                if (actual.frame != from.frame || !actual.parentId.Equals(from.parentId))
                    return false;

                var expected = NetworkTransformVelocity.Predict(from, chord, step);
                if (!NTUnreliable.PredictionMatches(expected, actual, chord, shift, cap,
                        Mathf.Min(step, gap - step)))
                    return false;
            }

            return true;
        }

        internal void CaptureUnreliableState()
        {
            var nm = networkManager;
            ushort tick = nm && nm.tickModule != null ? (ushort)nm.tickModule.localTick : (ushort)0;
            CaptureUnreliableState(tick, false);
        }

        internal void CaptureUnreliableState(ushort tick)
        {
            CaptureUnreliableState(tick, true);
        }

        private void CaptureUnreliableState(ushort tick, bool recordHistory)
        {
            var state = currentState;

            if (!syncPosition)
            {
                state.data.position = default(CompressedVector3);
                state.data.absolutePosition = null;
            }

            if (!syncRotation)
                state.data.rotation = Quaternion.identity;

            if (!syncScale)
                state.data.scale = default;

            if (!usesNetworkFrame)
            {
                state.frame = NetworkTransformFrame.World;
                state.parentId = default;
            }

            if (!_hasCapturedState || !_capturedState.Equals(state))
            {
                _capturedState = state;
                _capturedRevision++;
                _hasCapturedState = true;
            }

            if (!recordHistory)
                return;

            if (_historyStates == null)
            {
                _historyStates = ArrayPool<NetworkTransformState>.Shared.Rent(CAPTURE_HISTORY_SIZE);
                _historyTicks = ArrayPool<ushort>.Shared.Rent(CAPTURE_HISTORY_SIZE);
                _historyUsed = ArrayPool<bool>.Shared.Rent(CAPTURE_HISTORY_SIZE);
                Array.Clear(_historyUsed, 0, _historyUsed.Length);
            }

            int slot = tick % CAPTURE_HISTORY_SIZE;
            _historyStates[slot] = _capturedState;
            _historyTicks[slot] = tick;
            _historyUsed[slot] = true;
        }

        private void ReleaseUnreliableHistory()
        {
            if (_recvStates != null)
            {
                ArrayPool<NetworkTransformState>.Shared.Return(_recvStates);
                ArrayPool<ushort>.Shared.Return(_recvTicks);
                _recvStates = null;
                _recvTicks = null;
            }

            _recvCount = 0;
            _recvHead = 0;

            if (_historyStates != null)
            {
                ArrayPool<NetworkTransformState>.Shared.Return(_historyStates);
                ArrayPool<ushort>.Shared.Return(_historyTicks);
                ArrayPool<bool>.Shared.Return(_historyUsed);
                _historyStates = null;
                _historyTicks = null;
                _historyUsed = null;
            }
        }

        private bool usesNetworkFrame => _syncPosition == SyncMode.Local ||
                                         _syncRotation == SyncMode.Local ||
                                         _syncScale;

        private NetworkTransformState currentState => new NetworkTransformState
        {
            data = _currentData,
            frame = _currentFrame,
            parentId = _currentParentId
        };

        private void RefreshCurrentState()
        {
            _currentData = GetCurrentTransformData();
            _latestData = _currentData;
            RefreshLatestFrame();
            _currentFrame = _latestFrame;
            _currentParentId = _latestParentId;
        }

        private void AdoptState(in NetworkTransformState state)
        {
            _hasLastAppliedState = false;
            ClearAdaptiveAnchors();
            _lastReadData = state.data;
            _currentData = state.data;
            _latestData = state.data;
            _latestFrame = state.frame;
            _latestParentId = state.parentId;
            _currentFrame = state.frame;
            _currentParentId = state.parentId;
        }

        private Transform ResolveFrameParent(in NetworkTransformState state)
        {
            switch (state.frame)
            {
                case NetworkTransformFrame.LocalIdentity:
                    if (networkManager.TryGetModule<HierarchyFactory>(isServer, out var factory) &&
                        factory.TryGetIdentity(sceneId, state.parentId, out var identity) && identity)
                        return identity.transform;
                    return _trs.parent;
                case NetworkTransformFrame.LocalStatic:
                    return _trs.parent;
                case NetworkTransformFrame.World:
                default:
                    return null;
            }
        }

        private void TeleportToState(in NetworkTransformState state)
        {
            var p = ResolveFrameParent(state);

            if (syncPosition)
                _position.Teleport(MakePositionSample(p, state.data));

            if (syncRotation)
                _rotation.Teleport(new QuaternionWithParent(p, _syncRotation == SyncMode.Local, state.data.rotation));

            if (syncScale)
                _scale.Teleport(new ScaleWithParent(p, state.data.scale));
        }

        internal bool CanDeltaAgainst(in NetworkTransformState baseline)
        {
            return baseline.data.absolutePosition.HasValue == _capturedState.data.absolutePosition.HasValue;
        }

        internal void WriteAbsoluteState(BitPacker packer)
        {
            var state = _capturedState;

            if (usesNetworkFrame)
            {
                packer.WriteBits((ulong)state.frame, 2);
                if (state.frame == NetworkTransformFrame.LocalIdentity)
                    Packer<NetworkID>.Write(packer, state.parentId);
            }

            if (syncPosition)
            {
                bool isAbsolute = state.data.absolutePosition.HasValue;
                packer.WriteBits(isAbsolute ? 1UL : 0UL, 1);

                if (isAbsolute)
                    Packer<double3>.Write(packer, state.data.absolutePosition.Value);
                else
                    Packer<CompressedVector3>.Write(packer, state.data.position.GetValueOrDefault());
            }

            if (syncRotation)
                Packer<PackedQuaternion>.Write(packer, state.data.rotation);

            if (syncScale)
                Packer<CompressedVector3>.Write(packer, state.data.scale);
        }

        internal NetworkTransformState ReadAbsoluteState(BitPacker packer)
        {
            var state = default(NetworkTransformState);

            if (usesNetworkFrame)
            {
                state.frame = (NetworkTransformFrame)packer.ReadBits(2);
                if (state.frame == NetworkTransformFrame.LocalIdentity)
                    Packer<NetworkID>.Read(packer, ref state.parentId);
            }
            else
            {
                state.frame = NetworkTransformFrame.World;
            }

            if (syncPosition)
            {
                bool isAbsolute = packer.ReadBits(1) == 1;
                if (isAbsolute)
                {
                    double3 pos = default;
                    Packer<double3>.Read(packer, ref pos);
                    state.data.absolutePosition = pos;
                }
                else
                {
                    CompressedVector3 pos = default;
                    Packer<CompressedVector3>.Read(packer, ref pos);
                    state.data.position = pos;
                }
            }
            else
            {
                state.data.position = default(CompressedVector3);
            }

            if (syncRotation)
                Packer<PackedQuaternion>.Read(packer, ref state.data.rotation);
            else
                state.data.rotation = Quaternion.identity;

            if (syncScale)
                Packer<CompressedVector3>.Read(packer, ref state.data.scale);

            return state;
        }

        internal void WriteDeltaState(BitPacker packer, in NetworkTransformState baseline, in NetworkTransformState predicted)
        {
            var state = _capturedState;

            if (usesNetworkFrame)
            {
                bool sameFrame = state.frame == baseline.frame && state.parentId.Equals(baseline.parentId);
                packer.WriteBits(sameFrame ? 1UL : 0UL, 1);
                if (!sameFrame)
                {
                    packer.WriteBits((ulong)state.frame, 2);
                    if (state.frame == NetworkTransformFrame.LocalIdentity)
                        DeltaPacker<NetworkID>.Write(packer, baseline.parentId, state.parentId);
                }
            }

            if (syncPosition)
            {
                if (state.data.absolutePosition.HasValue)
                {
                    var oldPos = baseline.data.absolutePosition.GetValueOrDefault();
                    var newPos = state.data.absolutePosition.GetValueOrDefault();
                    bool changed = !oldPos.Equals(newPos);
                    packer.WriteBits(changed ? 1UL : 0UL, 1);
                    if (changed)
                        DeltaPacker<double3>.Write(packer, oldPos, newPos);
                }
                else
                {
                    var newPos = state.data.position.GetValueOrDefault();
                    bool changed = !baseline.data.position.GetValueOrDefault().Equals(newPos);
                    packer.WriteBits(changed ? 1UL : 0UL, 1);
                    if (changed)
                        DeltaPacker<CompressedVector3>.Write(packer, predicted.data.position.GetValueOrDefault(), newPos);
                }
            }

            if (syncRotation)
            {
                bool changed = !state.data.rotation.Equals(baseline.data.rotation);
                packer.WriteBits(changed ? 1UL : 0UL, 1);
                if (changed)
                    DeltaPacker<PackedQuaternion>.Write(packer, predicted.data.rotation, state.data.rotation);
            }

            if (syncScale)
            {
                bool changed = !state.data.scale.Equals(baseline.data.scale);
                packer.WriteBits(changed ? 1UL : 0UL, 1);
                if (changed)
                    DeltaPacker<CompressedVector3>.Write(packer, predicted.data.scale, state.data.scale);
            }
        }

        internal NetworkTransformState ReadDeltaState(BitPacker packer, in NetworkTransformState baseline, in NetworkTransformState predicted)
        {
            var state = default(NetworkTransformState);

            if (usesNetworkFrame)
            {
                bool sameFrame = packer.ReadBits(1) == 1;
                if (sameFrame)
                {
                    state.frame = baseline.frame;
                    state.parentId = baseline.parentId;
                }
                else
                {
                    state.frame = (NetworkTransformFrame)packer.ReadBits(2);
                    if (state.frame == NetworkTransformFrame.LocalIdentity)
                    {
                        state.parentId = baseline.parentId;
                        DeltaPacker<NetworkID>.Read(packer, baseline.parentId, ref state.parentId);
                    }
                }
            }
            else
            {
                state.frame = NetworkTransformFrame.World;
            }

            state.data = baseline.data;

            if (syncPosition && packer.ReadBits(1) == 1)
            {
                if (baseline.data.absolutePosition.HasValue)
                {
                    var oldPos = baseline.data.absolutePosition.Value;
                    var newPos = oldPos;
                    DeltaPacker<double3>.Read(packer, oldPos, ref newPos);
                    state.data.absolutePosition = newPos;
                }
                else
                {
                    var refPos = predicted.data.position.GetValueOrDefault();
                    var newPos = refPos;
                    DeltaPacker<CompressedVector3>.Read(packer, refPos, ref newPos);
                    state.data.position = newPos;
                }
            }

            if (syncRotation && packer.ReadBits(1) == 1)
            {
                state.data.rotation = predicted.data.rotation;
                DeltaPacker<PackedQuaternion>.Read(packer, predicted.data.rotation, ref state.data.rotation);
            }

            if (syncScale && packer.ReadBits(1) == 1)
            {
                var refScale = predicted.data.scale;
                var newScale = refScale;
                DeltaPacker<CompressedVector3>.Read(packer, refScale, ref newScale);
                state.data.scale = newScale;
            }

            return state;
        }

        internal bool TryApplyUnreliableState(in NetworkTransformState state, byte gen, long packetOrder,
            ushort senderTick, NetworkIdentity frameParent, bool isAbsolute)
        {
            if (_cachedIsController)
                return true;

            if (_hasRecvGen)
            {
                var genDiff = (sbyte)(gen - _recvGen);

                switch (genDiff)
                {
                    case < 0 when _hasAuthoritativeRecvGen || !isAbsolute || genDiff >= -8:
                        return false;
                    case < 0:
                    case > 0:
                        _recvGen = gen;
                        _hasAppliedSeq = false;
                        break;
                }
            }
            else
            {
                _recvGen = gen;
                _hasRecvGen = true;
                _hasAuthoritativeRecvGen = false;
                _hasAppliedSeq = false;
            }

            if (!NTUnreliable.ShouldApplyOrder(_hasAppliedSeq, _lastAppliedOrder, packetOrder))
                return true;

            if (state.frame == NetworkTransformFrame.LocalIdentity && !frameParent)
                return false;

            var p = state.frame switch
            {
                NetworkTransformFrame.LocalIdentity => frameParent.transform,
                NetworkTransformFrame.LocalStatic => _trs.parent,
                _ => null
            };

            int gap = 0;

            if (_hasStrategy && !isAbsolute && _hasLastAppliedState &&
                state.frame == _lastAppliedState.frame && state.parentId.Equals(_lastAppliedState.parentId))
                gap = (short)(senderTick - _lastAppliedSenderTick);

            var previous = _lastAppliedState;

            if (_hasRenderTimeline && _hasLastAppliedState)
                _renderRel -= (short)(senderTick - _lastAppliedSenderTick);

            _lastAppliedOrder = packetOrder;
            _hasAppliedSeq = true;
            _lastReadData = state.data;
            _lastAppliedState = state;
            _lastAppliedSenderTick = senderTick;
            _hasLastAppliedState = true;
            bool hadLivePose = _hasLivePose;
            _hasLivePose = true;

            if (_hasStrategy)
            {
                if (isAbsolute)
                {
                    // Mid-stream absolutes (ownership change, stream reset, baseline recovery)
                    // blend instead of teleporting.
                    bool smooth = hadLivePose;

                    if (!smooth)
                    {
                        ClearAdaptiveAnchors();
                        SetAdaptiveAnchor(state, default);
                        TeleportBuffers(state, p);
                    }
                    else if (_hasAdaptiveAnchor)
                    {
                        SetAdaptiveAnchor(state, default);
                    }
                    else
                    {
                        SeedSeamCorrection(state, p);
                        SetAdaptiveAnchor(state, default);
                        _corrPending = false;
                        _hasPrevAnchor = false;
                        _hasRenderTimeline = false;
                    }

                    if (_adaptiveDebugDump)
                        DebugDumpLine($"apply,senderTick={senderTick},abs=True,order={packetOrder}," +
                                      $"smooth={smooth},pos={DebugPos(state)}");
                }
                else
                {
                    int velocityGap = gap >= 1 && gap <= NTUnreliable.ADAPTIVE_MAX_BACKFILL ? gap : 0;
                    var velocity = velocityGap >= 1
                        ? NetworkTransformVelocity.Derive(previous, state, velocityGap)
                        : default;
                    SetAdaptiveAnchor(state, velocity);

                    if (_adaptiveDebugDump)
                        DebugDumpLine($"apply,senderTick={senderTick},abs=False,order={packetOrder},gap={gap}," +
                                      $"velY={velocity.posY},pos={DebugPos(state)}");
                }

                PushReceivedSample(senderTick, state);
                return true;
            }

            AddStateToBuffers(state, p);

            return true;
        }

        private bool _cachedConnectedOwner;

        protected override void OnOwnerDisconnected(PlayerID ownerId)
        {
            _cachedConnectedOwner = false;
            BumpSendGen();
            if (isServer)
                ResetUnreliableRecvState();
            var wasController = _cachedIsController;
            _cachedIsController = IsController(_ownerAuth);
            if (wasController != _cachedIsController)
                OnIsControlledChanged(_cachedIsController);
        }

        public bool IsControlling(PlayerID player, bool asServer)
        {
            if (!_ownerAuth || !_cachedConnectedOwner)
                return asServer;

            if (player == owner)
                return true;

            return asServer;
        }
    }
}
