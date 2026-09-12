using System;
using PurrNet.Contributors;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Packing;
using PurrNet.Pooling;
using PurrNet.Transports;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("PurrNet/Network Bones")]
    [CourtesyOf("Resolute Games", "https://resolutegames.com/")]
    public class NetworkBones : NetworkIdentity
    {
        [Header("Settings")]
        [SerializeField] private bool _ownerAuth = true;
        [SerializeField, Range(1, 128), PurrLock] private int _sendRatePerSecond = 10;
        [SerializeField, PurrLock] private Transform[] _extraBones;
        [Header("Accuracy")]
        [SerializeField, PurrLock] private float _positionAccuracy = 0.01f;
        [SerializeField, PurrLock] private float _angleAccuracy = 0.5f;
        [SerializeField, PurrLock] private float _scaleAccuracy = 0.05f;
        [Header("Interpolation")]
        [SerializeField, PurrLock, Min(1)] private int _minBufferSize = 2;
        [SerializeField, PurrLock, Min(1)] private int _maxBufferSize = 3;

        private DisposableList<Transform> _bones = DisposableList<Transform>.Create(512);
        private BoneInfo[] _bonesInfo;

        /// <summary>
        /// Extra bones to sync on top of the ones gathered from SkinnedMeshRenderers.
        /// Must be set before this identity is spawned.
        /// </summary>
        public Transform[] extraBones
        {
            get => _extraBones;
            set => _extraBones = value;
        }

        /// <summary>
        /// Number of bones gathered for syncing. Populated once spawned.
        /// </summary>
        public int boneCount => _bones.Count;

        private Interpolated<Vector3>[] _positions;
        private Interpolated<Quaternion>[] _rotations;
        private Interpolated<Vector3>[] _scales;

        private DeltaModule _clientDeltaModule;
        private DeltaModule _serverDeltaModule;

        private float _sendDelta;
        private bool _cachedIsController;
        private bool _cachedConnectedOwner;

        private Action _onLateLateUpdate;
        private int _lateLateUpdateHandle = PurrAction<Action>.InvalidHandle;

        private bool _loggedBoneMismatch;

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _bones.Dispose();
        }

        protected override void OnSpawned()
        {
            _sendDelta = 1f / _sendRatePerSecond;

            GatherBones();
            GatherBonesInfo(ref _bonesInfo);
        }

        protected override void OnSpawned(bool asServer)
        {
            _cachedConnectedOwner = hasConnectedOwner;
            _cachedIsController = IsController(_ownerAuth);

            if (asServer)
                 networkManager.TryGetModule<DeltaModule>(out _serverDeltaModule, true);
            else networkManager.TryGetModule<DeltaModule>(out _clientDeltaModule, false);
        }

        protected override void PromoteToServer()
        {
            networkManager.TryGetModule<DeltaModule>(out _serverDeltaModule, true);
            networkManager.TryGetModule<DeltaModule>(out _clientDeltaModule, false);
            _cachedIsController = IsController(_ownerAuth);
        }

        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            _cachedConnectedOwner = hasConnectedOwner;
            _cachedIsController = IsController(_ownerAuth);
        }

        protected override void OnOwnerDisconnected(PlayerID ownerId)
        {
            _cachedConnectedOwner = false;
            _cachedIsController = IsController(_ownerAuth);
        }

        protected override void OnOwnerReconnected(PlayerID ownerId)
        {
            _cachedConnectedOwner = hasConnectedOwner;
            _cachedIsController = IsController(_ownerAuth);
        }

        private void OnEnable()
        {
            _onLateLateUpdate ??= LateLateUpdate;
            _lateLateUpdateHandle = UnityLatestUpdate.latestUpdate.Add(_onLateLateUpdate);

            if (!isSpawned)
                return;

            for (var bIdx = 0; bIdx < _bones.Count; bIdx++)
            {
                _positions[bIdx].Teleport(_bones[bIdx].localPosition);
                _rotations[bIdx].Teleport(_bones[bIdx].localRotation);
                _scales[bIdx].Teleport(_bones[bIdx].localScale);
            }
        }

        private void OnDisable()
        {
            UnityLatestUpdate.latestUpdate.RemoveAt(_lateLateUpdateHandle, _onLateLateUpdate);
            _lateLateUpdateHandle = PurrAction<Action>.InvalidHandle;
        }

        private void GatherBones()
        {
            using var renderers = DisposableList<SkinnedMeshRenderer>.Create(32);

            gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true, renderers.list);
            _bones.Clear();

            for (var rIdx = 0; rIdx < renderers.list.Count; rIdx++)
            {
                var r = renderers.list[rIdx];
                var bones = r.bones;

                for (int bIdx = 0; bIdx < bones.Length; bIdx++)
                {
                    if (!_bones.Contains(bones[bIdx]))
                        _bones.Add(bones[bIdx]);
                }
            }

            if (_extraBones != null)
            {
                for (var bIdx = 0; bIdx < _extraBones.Length; bIdx++)
                {
                    var bone = _extraBones[bIdx];

                    if (!bone)
                        continue;

                    if (!_bones.Contains(bone))
                        _bones.Add(bone);
                }
            }

            _positions = new Interpolated<Vector3>[_bones.Count];
            _rotations = new Interpolated<Quaternion>[_bones.Count];
            _scales = new Interpolated<Vector3>[_bones.Count];
            _bonesInfo = new BoneInfo[_bones.Count];

            var nid = id!.Value;

            for (var bIdx = 0; bIdx < _bones.Count; bIdx++)
            {
                var posId = new NetworkBoneID(sceneId, nid, bIdx, BoneInfoType.Position);
                var rotId = new NetworkBoneID(sceneId, nid, bIdx, BoneInfoType.Rotation);
                var scaleId = new NetworkBoneID(sceneId, nid, bIdx, BoneInfoType.Scale);
                _bonesInfo[bIdx] = new BoneInfo
                {
                    posHash = posId,
                    rotHash = rotId,
                    scaleHash = scaleId,
                    posKeyHash = DeltaModule.GetKeyHash(posId),
                    rotKeyHash = DeltaModule.GetKeyHash(rotId),
                    scaleKeyHash = DeltaModule.GetKeyHash(scaleId),
                };
                _positions[bIdx] = new Interpolated<Vector3>(Vector3.Lerp, _sendDelta, _bones[bIdx].localPosition, _maxBufferSize, _minBufferSize);
                _rotations[bIdx] = new Interpolated<Quaternion>(Quaternion.Slerp, _sendDelta, _bones[bIdx].localRotation, _maxBufferSize, _minBufferSize);
                _scales[bIdx] = new Interpolated<Vector3>(Vector3.Lerp, _sendDelta, _bones[bIdx].localScale, _maxBufferSize, _minBufferSize);
            }
        }

        private void GatherBonesInfo(ref BoneInfo[] info)
        {
            for (var i = 0; i < _bones.Count; i++)
            {
                var bone = _bones[i];
                ref var boneInfo = ref info[i];
                boneInfo.localScale = bone.localScale;
                bone.GetLocalPositionAndRotation(out boneInfo.localPosition, out boneInfo.localRotation);
            }
        }

        private float _accumulateTime;

        private void LateLateUpdate()
        {
            if (!isSpawned)
                return;

            if (_bones.Count == 0)
                return;

            bool asServer = isServer;

            _accumulateTime += Time.unscaledDeltaTime;

            // if we dont control it, update from incoming data
            if (!_cachedIsController)
            {
                UpdateVisuals();

                // if we are server we still need to propagate it to the rest
                if (asServer && ConsumeTick() && _serverDeltaModule != null)
                    SendTransforms(_serverDeltaModule, true);
                return;
            }

            if (!ConsumeTick())
                return;

            var module = asServer ? _serverDeltaModule : _clientDeltaModule;

            if (module != null)
            {
                GatherBonesInfo(ref _bonesInfo);
                SendTransforms(module, asServer);
            }
        }

        private bool ConsumeTick()
        {
            if (_accumulateTime < _sendDelta)
                return false;

            int deltasNeeded = (int)(_accumulateTime / _sendDelta);
            _accumulateTime -= _sendDelta * deltasNeeded;
            return true;
        }

        private void UpdateVisuals()
        {
            float dt = Time.unscaledDeltaTime;
            for (var i = 0; i < _bones.Count; i++)
            {
                var bone = _bones[i];
                ref var boneInfo = ref _bonesInfo[i];

                var locPos = _positions[i].Advance(dt);
                var locRot = _rotations[i].Advance(dt);
                var locScale = _scales[i].Advance(dt);

                bone.SetLocalPositionAndRotation(locPos, locRot);
                bone.localScale = locScale;

                boneInfo.localPosition = locPos;
                boneInfo.localRotation = locRot;
                boneInfo.localScale = locScale;
            }
        }

        const int MAX_BONE_ENTRY_SIZE = 32; // worst-case single bone delta write ~26 bytes, rounded up
        const int RPC_BONE_PARAMS_SIZE = 10; // startingIdx + count + BitPacker size prefix per bone RPC

        void SendTransforms(DeltaModule module, bool asServer)
        {
            if (asServer)
            {
                bool checkOwner = _ownerAuth && _cachedConnectedOwner;
                var localPlayerCached = localPlayer;
                for (var i = 0; i < observers.Count; i++)
                {
                    var observer = observers[i];

                    if ((checkOwner && observer == owner) || observer == localPlayerCached)
                        continue;

                    int mtu = networkManager.GetMTU(observer, Channel.Unreliable, true) -
                              RPCBatch.MAX_HEADER_SIZE - RPC_BONE_PARAMS_SIZE;

                    SendPositions(observer, mtu, module);
                    SendRotations(observer, mtu, module);
                    SendScales(observer, mtu, module);
                }
            }
            else if (!isServer)
            {
                int mtu = networkManager.GetMTU(PlayerID.Server, Channel.Unreliable, false) -
                          RPCBatch.MAX_HEADER_SIZE - RPC_BONE_PARAMS_SIZE;

                SendPositions(default, mtu, module);
                SendRotations(default, mtu, module);
                SendScales(default, mtu, module);
            }
        }

        private Vector3Int CompressPosition(Vector3 position)
        {
            int x = Mathf.RoundToInt(position.x / _positionAccuracy);
            int y = Mathf.RoundToInt(position.y / _positionAccuracy);
            int z = Mathf.RoundToInt(position.z / _positionAccuracy);
            return new Vector3Int(x, y, z);
        }

        private Vector3 DecompressPosition(Vector3Int position)
        {
            return new Vector3(
                position.x * _positionAccuracy,
                position.y * _positionAccuracy,
                position.z * _positionAccuracy
            );
        }

        private Vector3Int CompressEuler(Vector3 euler)
        {
            int x = Mathf.RoundToInt(euler.x / _angleAccuracy);
            int y = Mathf.RoundToInt(euler.y / _angleAccuracy);
            int z = Mathf.RoundToInt(euler.z / _angleAccuracy);
            return new Vector3Int(x, y, z);
        }

        private Vector3 DecompressEuler(Vector3Int euler)
        {
            return new Vector3(
                euler.x * _angleAccuracy,
                euler.y * _angleAccuracy,
                euler.z * _angleAccuracy
            );
        }

        private Vector3Int CompressScale(Vector3 scale)
        {
            int x = Mathf.RoundToInt(scale.x / _scaleAccuracy);
            int y = Mathf.RoundToInt(scale.y / _scaleAccuracy);
            int z = Mathf.RoundToInt(scale.z / _scaleAccuracy);
            return new Vector3Int(x, y, z);
        }

        private Vector3 DecompressScale(Vector3Int scale)
        {
            return new Vector3(
                scale.x * _scaleAccuracy,
                scale.y * _scaleAccuracy,
                scale.z * _scaleAccuracy
            );
        }

        private bool ValidateBoneRange(PackedUInt startingIdx, PackedUInt count)
        {
            if (startingIdx.value + count.value <= (uint)_bones.Count)
                return true;

            if (!_loggedBoneMismatch)
            {
                _loggedBoneMismatch = true;
                PurrLogger.LogError(
                    $"NetworkBones '{name}': bone set mismatch, peer synced bones {startingIdx.value}..{startingIdx.value + count.value} but only {_bones.Count} were gathered locally. " +
                    "The rigs differ between peers (mesh import settings, stripped transforms or extraBones mismatch); ignoring their bone data.", this);
            }

            return false;
        }

        delegate void Forward(PlayerID observer, PackedUInt startingIdx, PackedUInt count, BitPacker data);
        delegate bool Write(BitPacker packer, DeltaModule module, PlayerID player, BoneInfo info, ref PackedUInt cachedKey);

        void Pack(PlayerID observer, int MTU, DeltaModule module, Forward forward, Write write)
        {
            using var packer = BitPackerPool.Get();
            PackedUInt cache = default;
            uint lastIndex = 0;
            bool writtenAny = false;

            for (uint b = 0; b < _bones.Count; b++)
            {
                if (writtenAny && packer.positionInBytes + MAX_BONE_ENTRY_SIZE > MTU)
                {
                    var count = b - lastIndex;
                    forward(observer, lastIndex, count, packer);

                    cache = default;
                    lastIndex = b;
                    packer.ResetPosition();
                    writtenAny = false;
                }

                writtenAny = write(packer, module, observer, _bonesInfo[b], ref cache) || writtenAny;
            }

            if (writtenAny && packer.positionInBits > 0)
            {
                var count = (uint)_bones.Count - lastIndex;
                forward(observer, lastIndex, count, packer);
            }
        }

        private void SendPositions(PlayerID observer, int MTU, DeltaModule module) =>
            Pack(observer, MTU, module, ForwardPositions, WritePosition);

        bool WritePosition(BitPacker packer, DeltaModule module, PlayerID player, BoneInfo info, ref PackedUInt cachedKey)
        {
            var newPos = CompressPosition(info.localPosition);
            return module.Write<Vector3Int>(packer, player, info.posKeyHash, newPos, ref cachedKey);
        }

        private void ForwardPositions(PlayerID observer, PackedUInt startingIdx, PackedUInt count, BitPacker data)
        {
            if (observer == default)
                 RpcPositionsToServer(startingIdx, count, data);
            else RpcPositions(observer, startingIdx, count, data);
        }

        [TargetRpc(channel: Channel.Unreliable)]
        private void RpcPositions(PlayerID target, PackedUInt startingIdx, PackedUInt count, BitPacker data,
            RPCInfo info = default)
        {
            using (data)
                ReadPositions(info.sender, startingIdx, count, data, _clientDeltaModule);
        }

        [ServerRpc(channel: Channel.Unreliable)]
        private void RpcPositionsToServer(PackedUInt startingIdx, PackedUInt count, BitPacker data, RPCInfo info = default)
        {
            using (data)
            {
                if (!_ownerAuth)
                    return;
                ReadPositions(info.sender, startingIdx, count, data, _serverDeltaModule);
            }
        }

        private void ReadPositions(PlayerID sender, PackedUInt startingIdx, PackedUInt count, BitPacker packer, DeltaModule module)
        {
            if (_bonesInfo == null)
                return;

            if (!ValidateBoneRange(startingIdx, count))
                return;

            uint lastIndex = startingIdx + count;
            PackedUInt cache = default;

            for (uint i = startingIdx; i < lastIndex; i++)
            {
                var boneInfo = _bonesInfo[(int)i];
                Vector3Int newPos = default;
                module.Read<Vector3Int>(packer, boneInfo.posKeyHash, sender, ref newPos, ref cache);
                _positions[i].Add(DecompressPosition(newPos));
            }
        }

        private void SendRotations(PlayerID observer, int MTU, DeltaModule module)
            => Pack(observer, MTU, module, ForwardRotations, WriteRotation);

        bool WriteRotation(BitPacker packer, DeltaModule module, PlayerID player, BoneInfo info, ref PackedUInt cachedKey)
        {
            var newRot = CompressEuler(info.localRotation.eulerAngles);
            return module.Write<Vector3Int>(packer, player, info.rotKeyHash, newRot, ref cachedKey);
        }

        private void ForwardRotations(PlayerID observer, PackedUInt startingIdx, PackedUInt count, BitPacker data)
        {
            if (observer == default)
                RpcRotationsToServer(startingIdx, count, data);
            else RpcRotations(observer, startingIdx, count, data);
        }

        [TargetRpc(channel: Channel.Unreliable)]
        private void RpcRotations(PlayerID target, PackedUInt startingIdx, PackedUInt count, BitPacker data,
            RPCInfo info = default)
        {
            using (data)
                ReadRotations(info.sender, startingIdx, count, data, _clientDeltaModule);
        }

        [ServerRpc(channel: Channel.Unreliable)]
        private void RpcRotationsToServer(PackedUInt startingIdx, PackedUInt count, BitPacker data, RPCInfo info = default)
        {
            using (data)
            {
                if (!_ownerAuth)
                    return;
                ReadRotations(info.sender, startingIdx, count, data, _serverDeltaModule);
            }
        }

        private void ReadRotations(PlayerID sender, PackedUInt startingIdx, PackedUInt count, BitPacker packer, DeltaModule module)
        {
            if (_bonesInfo == null)
                return;

            if (!ValidateBoneRange(startingIdx, count))
                return;

            uint lastIndex = startingIdx + count;
            PackedUInt cache = default;

            for (uint i = startingIdx; i < lastIndex; i++)
            {
                var boneInfo = _bonesInfo[(int)i];
                Vector3Int newRot = default;
                module.Read<Vector3Int>(packer, boneInfo.rotKeyHash, sender, ref newRot, ref cache);
                _rotations[i].Add(Quaternion.Euler(DecompressEuler(newRot)));
            }
        }

        private void SendScales(PlayerID observer, int MTU, DeltaModule module)
            => Pack(observer, MTU, module, ForwardScales, WriteScale);

        bool WriteScale(BitPacker packer, DeltaModule module, PlayerID player, BoneInfo info, ref PackedUInt cachedKey)
        {
            var newScale = CompressScale(info.localScale);
            return module.Write<Vector3Int>(packer, player, info.scaleKeyHash, newScale, ref cachedKey);
        }

        private void ForwardScales(PlayerID observer, PackedUInt startingIdx, PackedUInt count, BitPacker data)
        {
            if (observer == default)
                RpcScalesToServer(startingIdx, count, data);
            else RpcScales(observer, startingIdx, count, data);
        }

        [TargetRpc(channel: Channel.Unreliable)]
        private void RpcScales(PlayerID target, PackedUInt startingIdx, PackedUInt count, BitPacker data,
            RPCInfo info = default)
        {
            using (data)
                ReadScales(info.sender, startingIdx, count, data, _clientDeltaModule);
        }

        [ServerRpc(channel: Channel.Unreliable)]
        private void RpcScalesToServer(PackedUInt startingIdx, PackedUInt count, BitPacker data, RPCInfo info = default)
        {
            using (data)
            {
                if (!_ownerAuth)
                    return;
                ReadScales(info.sender, startingIdx, count, data, _serverDeltaModule);
            }
        }

        private void ReadScales(PlayerID sender, PackedUInt startingIdx, PackedUInt count, BitPacker packer, DeltaModule module)
        {
            if (_bonesInfo == null)
                return;

            if (!ValidateBoneRange(startingIdx, count))
                return;

            uint lastIndex = startingIdx + count;
            PackedUInt cache = default;

            for (uint i = startingIdx; i < lastIndex; i++)
            {
                var boneInfo = _bonesInfo[(int)i];
                Vector3Int newScale = default;
                module.Read<Vector3Int>(packer, boneInfo.scaleKeyHash, sender, ref newScale, ref cache);
                _scales[i].Add(DecompressScale(newScale));
            }
        }
    }
}
