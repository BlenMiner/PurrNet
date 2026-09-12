#if UNITY_ANIMATION
using System.Collections.Generic;
using UnityEngine;

namespace PurrNet
{
    public sealed partial class NetworkAnimator
    {
        readonly Dictionary<int, int> _intValues = new Dictionary<int, int>();
        readonly Dictionary<int, float> _floatValues = new Dictionary<int, float>();
        readonly Dictionary<int, bool> _boolValues = new Dictionary<int, bool>();

        // keyed by layer index; layer 0 is never tracked since Unity pins its weight to 1
        readonly Dictionary<int, float> _layerWeights = new Dictionary<int, float>();

        private bool _wasController;
        private bool _hasPendingAnimatorParameterState;

        // triggers can't be represented in the value caches above, so buffer the
        // actions themselves until the animator can apply them
        readonly List<NetAnimatorRPC> _pendingTriggerActions = new List<NetAnimatorRPC>();

        private AnimatorControllerParameter[] _cachedParameters;
        private RuntimeAnimatorController _cachedController;
        private readonly Dictionary<int, int> _paramIndexByHash = new Dictionary<int, int>();

        private bool canApplyAnimatorParameters => _animator && _animator.isActiveAndEnabled;

        private AnimatorControllerParameter[] GetCachedParameters()
        {
            var controller = _animator.runtimeAnimatorController;
            if (_cachedParameters == null || _cachedController != controller ||
                _cachedParameters.Length != _animator.parameterCount)
            {
                _cachedController = controller;
                _cachedParameters = _animator.parameters;

                _paramIndexByHash.Clear();
                for (var i = 0; i < _cachedParameters.Length; i++)
                    _paramIndexByHash[_cachedParameters[i].nameHash] = i;
            }

            return _cachedParameters;
        }

        private int GetParamIndexPlusOne(int nameHash)
        {
            if (!_animator || !_animator.runtimeAnimatorController)
                return 0;

            GetCachedParameters();
            return _paramIndexByHash.TryGetValue(nameHash, out var index) ? index + 1 : 0;
        }

        protected override void OnSpawned()
        {
            _wasController = IsController(_ownerAuth);
        }

        protected override void OnOwnerChanged(
            PlayerID? oldOwner,
            PlayerID? newOwner,
            bool isSpawner,
            bool asServer)
        {
            bool isControlling = IsController(_ownerAuth);

            bool shouldReconcile = !isSpawner &&
                ((hasConnectedOwner && isOwner && !asServer) || (asServer && isControlling));

            if (shouldReconcile)
                Reconcile();

            if (_wasController != isControlling)
            {
                _wasController = isControlling;
                if (_autoSyncParameters)
                    UpdateParamerCache();
            }
        }

        private void UpdateParamerCache()
        {
            if (!canApplyAnimatorParameters || !_animator.runtimeAnimatorController)
                return;

            var animParams = GetCachedParameters();

            for (var i = 0; i < animParams.Length; i++)
            {
                var param = animParams[i];

                if (_dontSyncHashes.Contains(param.nameHash))
                    continue;

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        _boolValues[param.nameHash] = _animator.GetBool(param.nameHash);
                        break;
                    case AnimatorControllerParameterType.Float:
                        _floatValues[param.nameHash] = _animator.GetFloat(param.nameHash);
                        break;
                    case AnimatorControllerParameterType.Int:
                        _intValues[param.nameHash] = _animator.GetInteger(param.nameHash);
                        break;
                }
            }

            var layerCount = _animator.layerCount;
            for (var i = 1; i < layerCount; i++)
                _layerWeights[i] = _animator.GetLayerWeight(i);
        }

        private void CheckForParameterChanges()
        {
            if (!canApplyAnimatorParameters || !_animator.runtimeAnimatorController)
                return;

            var animParams = GetCachedParameters();

            for (var i = 0; i < animParams.Length; i++)
            {
                var param = animParams[i];

                if (_dontSyncHashes.Contains(param.nameHash))
                    continue;

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                    {
                        var current = _animator.GetBool(param.nameHash);

                        if (_boolValues.TryGetValue(param.nameHash, out var v) && v == current)
                            continue;

                        var setBool = new SetBool
                        {
                            value = current,
                            nameHash = param.nameHash,
                            paramIndexPlusOne = i + 1
                        };

                        _boolValues[param.nameHash] = setBool.value;

                        IfSameReplace(new NetAnimatorRPC(setBool),
                            (a, b) => a._bool.nameHash == b._bool.nameHash);
                        break;
                    }
                    case AnimatorControllerParameterType.Float:
                    {
                        var current = _animator.GetFloat(param.nameHash);

                        if (_floatValues.TryGetValue(param.nameHash, out var v) &&
                            Mathf.Approximately(v, current))
                            continue;

                        var setFloat = new SetFloat
                        {
                            value = current,
                            nameHash = param.nameHash,
                            paramIndexPlusOne = i + 1
                        };

                        _floatValues[param.nameHash] = setFloat.value;

                        IfSameReplace(new NetAnimatorRPC(setFloat),
                            (a, b) => a._float.nameHash == b._float.nameHash);
                        break;
                    }
                    case AnimatorControllerParameterType.Int:
                    {
                        var current = _animator.GetInteger(param.nameHash);

                        if (_intValues.TryGetValue(param.nameHash, out var v) && v == current)
                            continue;

                        var setInteger = new SetInteger
                        {
                            value = current,
                            nameHash = param.nameHash,
                            paramIndexPlusOne = i + 1
                        };

                        _intValues[param.nameHash] = setInteger.value;

                        IfSameReplace(new NetAnimatorRPC(setInteger),
                            (a, b) => a._integer.nameHash == b._integer.nameHash);
                        break;
                    }
                }
            }

            CheckForLayerWeightChanges();
        }

        private void CheckForLayerWeightChanges()
        {
            var layerCount = _animator.layerCount;

            for (var i = 1; i < layerCount; i++)
            {
                var current = _animator.GetLayerWeight(i);

                if (_layerWeights.TryGetValue(i, out var v) && Mathf.Approximately(v, current))
                    continue;

                _layerWeights[i] = current;

                IfSameReplace(new NetAnimatorRPC(new SetLayerWeight { layerIndex = i, weight = current }),
                    (a, b) => a._setLayerWeight.layerIndex.value == b._setLayerWeight.layerIndex.value);
            }
        }

        private void CacheLayerWeight(int layerIndex, float weight)
        {
            if (layerIndex > 0)
                _layerWeights[layerIndex] = weight;
        }

        private bool CacheActionState(NetAnimatorRPC action)
        {
            switch (action.type)
            {
                case NetAnimatorAction.SetBool:
                    _boolValues[action._bool.nameHash] = action._bool.value;
                    return true;
                case NetAnimatorAction.SetFloat:
                    _floatValues[action._float.nameHash] = action._float.value;
                    return true;
                case NetAnimatorAction.SetInteger:
                    _intValues[action._integer.nameHash] = action._integer.value;
                    return true;
                case NetAnimatorAction.SetLayerWeight:
                    CacheLayerWeight(action._setLayerWeight.layerIndex, action._setLayerWeight.weight);
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsCachedStateAction(NetAnimatorAction action)
        {
            return action is NetAnimatorAction.SetBool or NetAnimatorAction.SetFloat or
                NetAnimatorAction.SetInteger or NetAnimatorAction.SetTrigger or
                NetAnimatorAction.ResetTrigger or NetAnimatorAction.SetLayerWeight;
        }

        private static int GetTriggerNameHash(NetAnimatorRPC action)
        {
            return action.type == NetAnimatorAction.SetTrigger
                ? action._trigger.nameHash
                : action._resetTrigger.nameHash;
        }

        private void QueuePendingTrigger(NetAnimatorRPC action)
        {
            int nameHash = GetTriggerNameHash(action);

            for (int i = _pendingTriggerActions.Count - 1; i >= 0; i--)
            {
                if (GetTriggerNameHash(_pendingTriggerActions[i]) == nameHash)
                    _pendingTriggerActions.RemoveAt(i);
            }

            _pendingTriggerActions.Add(action);
            _hasPendingAnimatorParameterState = true;
        }

        private void ApplyPendingAnimatorParameterState()
        {
            if (!_hasPendingAnimatorParameterState || !canApplyAnimatorParameters ||
                !_animator.runtimeAnimatorController)
                return;

            var animParams = GetCachedParameters();
            for (var i = 0; i < animParams.Length; i++)
            {
                var param = animParams[i];
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                        if (_boolValues.TryGetValue(param.nameHash, out var boolValue))
                            _animator.SetBool(param.nameHash, boolValue);
                        break;
                    case AnimatorControllerParameterType.Float:
                        if (_floatValues.TryGetValue(param.nameHash, out var floatValue))
                            _animator.SetFloat(param.nameHash, floatValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        if (_intValues.TryGetValue(param.nameHash, out var intValue))
                            _animator.SetInteger(param.nameHash, intValue);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                    default:
                        break;
                }
            }

            var layerCount = _animator.layerCount;
            foreach (var (layerIndex, weight) in _layerWeights)
            {
                if (layerIndex < layerCount)
                    _animator.SetLayerWeight(layerIndex, weight);
            }

            for (var i = 0; i < _pendingTriggerActions.Count; i++)
                _pendingTriggerActions[i].Apply(_animator);
            _pendingTriggerActions.Clear();

            _hasPendingAnimatorParameterState = false;
        }
    }
}
#endif
