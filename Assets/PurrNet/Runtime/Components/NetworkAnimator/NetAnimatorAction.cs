#if UNITY_ANIMATION
using PurrNet.Packing;
using UnityEngine;

namespace PurrNet
{
    public enum NetAnimatorAction : byte
    {
        SetBool,
        SetFloat,
        SetInteger,
        SetTrigger,
        SetSpeed,
        SetAnimatePhysics,
        SetBodyPosition,
        SetBodyRotation,
        SetCullingMode,
        SetFireEvents,
        SetPlaybackTime,
        SetRootPosition,
        SetRootRotation,
        SetStabilizeFeet,
        SetUpdateMode,
        SetApplyRootMotion,
        SetFeetPivotActive,
        SetKeepAnimatorStateOnDisable,
        SetWriteDefaultValuesOnDisable,
        SetLogWarnings,
        SetLayersAffectMassCenter,
        ResetTrigger,
        Play_STATEHASH_LAYER_NORMALIZEDTIME,
        Play_STATEHASH_LAYER,
        PLAY_STATEHASH,
        Rebind,
        UpdateWithDelta,
        CrossFade_5,
        CrossFade_4,
        CrossFade_3,
        CrossFade_2,
        MatchTarget,
        InterruptMatchTarget,
        SetLayerWeight,
        WriteDefaultValues,
        ApplyBuiltinRootMotion,
        PlayInFixedTime,
        SetBoneLocalRotation,
        SetIKPosition,
        SetIKRotation,
        SetLookAtPosition,
        SetLookAtWeight,
        CrossFadeInFixedTime,
        SetIKHintPosition,
        SetIKPositionWeight,
        SetIKRotationWeight,
        SetIKHintPositionWeight,
        SetRuntimeAnimatorController,
        SetAvatar,
        SetTarget,
        StartPlayback,
        StopPlayback
    }

    internal static class AnimParamPacking
    {
        public static void Serialize(BitPacker packer, ref int nameHash, ref int paramIndexPlusOne)
        {
            bool hasIndex = paramIndexPlusOne is >= 1 and <= 256;
            Packer<bool>.Serialize(packer, ref hasIndex);

            if (hasIndex)
            {
                byte index = packer.isWriting ? (byte)(paramIndexPlusOne - 1) : default;
                Packer<byte>.Serialize(packer, ref index);

                if (packer.isReading)
                {
                    paramIndexPlusOne = index + 1;
                    nameHash = 0;
                }
            }
            else
            {
                Packer<int>.Serialize(packer, ref nameHash);

                if (packer.isReading)
                    paramIndexPlusOne = 0;
            }
        }
    }

    internal struct SetRuntimeAnimatorController : IPackedAuto
    {
        public RuntimeAnimatorController controller;

        public void Apply(Animator anim)
        {
            anim.runtimeAnimatorController = controller;
        }
    }

    internal struct SetAvatar : IPackedAuto
    {
        public Avatar avatar;

        public void Apply(Animator anim)
        {
            anim.avatar = avatar;
        }
    }

    internal struct SetIKHintPosition : IPackedAuto
    {
        public AvatarIKHint hint;
        public Vector3 position;

        public void Apply(Animator anim)
        {
            anim.SetIKHintPosition(hint, position);
        }
    }

    internal struct SetIKPositionWeight : IPackedAuto
    {
        public AvatarIKGoal goal;
        public float value;

        public void Apply(Animator anim)
        {
            anim.SetIKPositionWeight(goal, value);
        }
    }

    internal struct SetIKRotationWeight : IPackedAuto
    {
        public AvatarIKGoal goal;
        public float value;

        public void Apply(Animator anim)
        {
            anim.SetIKRotationWeight(goal, value);
        }
    }

    internal struct SetIKHintPositionWeight : IPackedAuto
    {
        public AvatarIKHint hint;
        public float value;

        public void Apply(Animator anim)
        {
            anim.SetIKHintPositionWeight(hint, value);
        }
    }

    internal struct CrossFadeInFixedTime : IPackedAuto
    {
        public int stateHash;
        public float fixedTime;
        public int layer;
        public float fixedDuration;
        public float normalizedTime;

        public void Apply(Animator anim)
        {
            anim.CrossFadeInFixedTime(stateHash, fixedTime, layer, fixedDuration, normalizedTime);
        }
    }

    internal struct SetLookAtPosition : IPackedAuto
    {
        public Vector3 position;

        public void Apply(Animator anim)
        {
            anim.SetLookAtPosition(position);
        }
    }

    internal struct SetLookAtWeight : IPackedAuto
    {
        public float weight, bodyWeight, headWeight, eyesWeight, clampWeight;

        public void Apply(Animator anim)
        {
            anim.SetLookAtWeight(weight, bodyWeight, headWeight, eyesWeight, clampWeight);
        }
    }

    internal struct SetIKPosition : IPackedAuto
    {
        public AvatarIKGoal goal;
        public Vector3 position;

        public void Apply(Animator anim)
        {
            anim.SetIKPosition(goal, position);
        }
    }

    internal struct SetIKRotation : IPackedAuto
    {
        public AvatarIKGoal goal;
        public Quaternion rotation;

        public void Apply(Animator anim)
        {
            anim.SetIKRotation(goal, rotation);
        }
    }

    internal struct SetBoneLocalRotation : IPackedAuto
    {
        public HumanBodyBones bone;
        public Quaternion rotation;

        public void Apply(Animator anim)
        {
            anim.SetBoneLocalRotation(bone, rotation);
        }
    }

    internal struct SetBool : IPackedSimple
    {
        public int nameHash;
        public int paramIndexPlusOne;
        public bool value;

        public void Serialize(BitPacker packer)
        {
            AnimParamPacking.Serialize(packer, ref nameHash, ref paramIndexPlusOne);
            Packer<bool>.Serialize(packer, ref value);
        }

        public void Apply(Animator anim)
        {
            anim.SetBool(nameHash, value);
        }
    }

    internal struct SetFloat : IPackedSimple
    {
        public int nameHash;
        public int paramIndexPlusOne;
        public float value;

        public void Serialize(BitPacker packer)
        {
            AnimParamPacking.Serialize(packer, ref nameHash, ref paramIndexPlusOne);
            Packer<float>.Serialize(packer, ref value);
        }

        public void Apply(Animator anim)
        {
            anim.SetFloat(nameHash, value);
        }
    }

    internal struct SetInteger : IPackedSimple
    {
        public int nameHash;
        public int paramIndexPlusOne;
        public int value;

        public void Serialize(BitPacker packer)
        {
            AnimParamPacking.Serialize(packer, ref nameHash, ref paramIndexPlusOne);
            Packer<int>.Serialize(packer, ref value);
        }

        public void Apply(Animator anim)
        {
            anim.SetInteger(nameHash, value);
        }
    }

    internal struct SetTrigger : IPackedSimple
    {
        public int nameHash;
        public int paramIndexPlusOne;

        public void Serialize(BitPacker packer)
        {
            AnimParamPacking.Serialize(packer, ref nameHash, ref paramIndexPlusOne);
        }

        public void Apply(Animator anim)
        {
            anim.SetTrigger(nameHash);
        }
    }

    internal struct ResetTrigger : IPackedSimple
    {
        public int nameHash;
        public int paramIndexPlusOne;

        public void Serialize(BitPacker packer)
        {
            AnimParamPacking.Serialize(packer, ref nameHash, ref paramIndexPlusOne);
        }

        public void Apply(Animator anim)
        {
            anim.ResetTrigger(nameHash);
        }
    }

    internal struct SetSpeed : IPackedAuto
    {
        public float speed;

        public void Apply(Animator anim)
        {
            anim.speed = speed;
        }
    }

    internal struct SetAnimatePhysics
    {
        public bool value;

        public void Apply(Animator anim)
        {
            anim.animatePhysics = value;
        }
    }

    internal struct SetBodyPosition : IPackedAuto
    {
        public Vector3 value;

        public void Apply(Animator anim)
        {
            anim.bodyPosition = value;
        }
    }

    internal struct SetBodyRotation : IPackedAuto
    {
        public Quaternion value;

        public void Apply(Animator anim)
        {
            anim.bodyRotation = value;
        }
    }

    internal struct SetCullingMode : IPackedAuto
    {
        public AnimatorCullingMode value;

        public void Apply(Animator anim)
        {
            anim.cullingMode = value;
        }
    }

    internal struct SetFireEvents : IPackedAuto
    {
        public bool value;

        public void Apply(Animator anim)
        {
            anim.fireEvents = value;
        }
    }

    internal struct SetPlaybackTime : IPackedAuto
    {
        public float value;

        public void Apply(Animator anim)
        {
            anim.playbackTime = value;
        }
    }

    internal struct SetRootPosition : IPackedAuto
    {
        public Vector3 value;

        public void Apply(Animator anim)
        {
            anim.rootPosition = value;
        }
    }

    internal struct SetRootRotation : IPackedAuto
    {
        public Quaternion value;

        public void Apply(Animator anim)
        {
            anim.rootRotation = value;
        }
    }

    internal struct SetStabilizeFeet : IPackedAuto
    {
        public bool value;

        public void Apply(Animator anim)
        {
            anim.stabilizeFeet = value;
        }
    }

    internal struct SetUpdateMode : IPackedAuto
    {
        public AnimatorUpdateMode value;

        public void Apply(Animator anim)
        {
            anim.updateMode = value;
        }
    }

    internal struct SetApplyRootMotion : IPackedAuto
    {
        public bool value;

        public void Apply(Animator anim)
        {
            anim.applyRootMotion = value;
        }
    }

    internal struct SetFeetPivotActive : IPackedAuto
    {
        public float value;

        public void Apply(Animator anim)
        {
            anim.feetPivotActive = value;
        }
    }

    internal struct SetKeepAnimatorStateOnDisable : IPackedAuto
    {
        public bool value;

        public void Apply(Animator anim)
        {
            anim.keepAnimatorStateOnDisable = value;
        }
    }

    internal struct SetWriteDefaultValuesOnDisable : IPackedAuto
    {
        public bool value;

        public void Apply(Animator anim)
        {
            anim.writeDefaultValuesOnDisable = value;
        }
    }

    internal struct SetLogWarnings : IPackedAuto
    {
        public bool value;

        public void Apply(Animator anim)
        {
            anim.logWarnings = value;
        }
    }

    internal struct SetLayersAffectMassCenter : IPackedAuto
    {
        public bool value;

        public void Apply(Animator anim)
        {
            anim.layersAffectMassCenter = value;
        }
    }

    internal struct Play_STATEHASH_LAYER_NORMALIZEDTIME : IPackedAuto
    {
        public int stateHash;
        public int layer;
        public float normalizedTime;

        public void Apply(Animator anim)
        {
            anim.Play(stateHash, layer, normalizedTime);
        }
    }

    internal struct Play_STATEHASH_LAYER : IPackedAuto
    {
        public int stateHash;
        public int layer;

        public void Apply(Animator anim)
        {
            anim.Play(stateHash, layer);
        }
    }

    internal struct PLAY_STATEHASH : IPackedAuto
    {
        public int stateHash;

        public void Apply(Animator anim)
        {
            anim.Play(stateHash);
        }
    }

    internal struct Rebind : IPackedAuto
    {
        public void Apply(Animator anim)
        {
            anim.Rebind();
        }
    }

    internal struct UpdateWithDelta : IPackedAuto
    {
        public float delta;

        public void Apply(Animator anim)
        {
            anim.Update(delta);
        }
    }

    internal struct CrossFade_5 : IPackedAuto
    {
        public int stateHash;
        public float normalizedTime;
        public int layer;
        public float fixedTime;
        public float fixedDuration;

        public void Apply(Animator anim)
        {
            anim.CrossFade(stateHash, normalizedTime, layer, fixedTime, fixedDuration);
        }
    }

    internal struct CrossFade_4 : IPackedAuto
    {
        public int stateHash;
        public float normalizedTime;
        public int layer;
        public float fixedDuration;

        public void Apply(Animator anim)
        {
            anim.CrossFade(stateHash, normalizedTime, layer, fixedDuration);
        }
    }

    internal struct CrossFade_3 : IPackedAuto
    {
        public int stateHash;
        public float normalizedTime;
        public int layer;

        public void Apply(Animator anim)
        {
            anim.CrossFade(stateHash, normalizedTime, layer);
        }
    }

    internal struct CrossFade_2 : IPackedAuto
    {
        public int stateHash;
        public float normalizedTime;

        public void Apply(Animator anim)
        {
            anim.CrossFade(stateHash, normalizedTime);
        }
    }

    internal struct MatchTarget : IPackedAuto
    {
        public Vector3 matchPosition;
        public Quaternion matchRotation;
        public AvatarTarget targetBodyPart;
        public MatchTargetWeightMask weightMask;
        public float startNormalizedTime;
        public float targetNormalizedTime;
        public bool completeMatch;

        public void Apply(Animator anim)
        {
            anim.MatchTarget(matchPosition, matchRotation, targetBodyPart, weightMask, startNormalizedTime,
                targetNormalizedTime, completeMatch);
        }
    }

    internal struct InterruptMatchTarget : IPackedAuto
    {
        public bool completeMatch;

        public void Apply(Animator anim)
        {
            anim.InterruptMatchTarget(completeMatch);
        }
    }

    internal struct SetLayerWeight : IPackedAuto
    {
        public PackedInt layerIndex;
        public float weight;

        public void Apply(Animator anim)
        {
            anim.SetLayerWeight(layerIndex, weight);
        }
    }

    internal struct WriteDefaultValues : IPackedAuto
    {
        public void Apply(Animator anim)
        {
            anim.WriteDefaultValues();
        }
    }

    internal struct ApplyBuiltinRootMotion : IPackedAuto
    {
        public void Apply(Animator anim)
        {
            anim.ApplyBuiltinRootMotion();
        }
    }

    internal struct PlayInFixedTime : IPackedAuto
    {
        public int stateHash;
        public int layer;
        public float fixedTime;

        public void Apply(Animator anim)
        {
            anim.PlayInFixedTime(stateHash, layer, fixedTime);
        }
    }

    internal struct SetTarget : IPackedAuto
    {
        public AvatarTarget targetIndex;
        public float targetNormalizedTime;

        public void Apply(Animator anim)
        {
            anim.SetTarget(targetIndex, targetNormalizedTime);
        }
    }

    internal struct StartPlayback : IPackedAuto
    {
        public void Apply(Animator anim)
        {
            anim.StartPlayback();
        }
    }

    internal struct StopPlayback : IPackedAuto
    {
        public void Apply(Animator anim)
        {
            anim.StopPlayback();
        }
    }

    internal struct NetAnimatorRPC : IPackedSimple
    {
        internal NetAnimatorAction type;

        internal SetBool _bool;
        internal SetFloat _float;
        internal SetInteger _integer;
        internal SetTrigger _trigger;
        private SetSpeed _speed;
        private SetAnimatePhysics _animatePhysics;
        private SetBodyPosition _bodyPosition;
        private SetBodyRotation _bodyRotation;
        private SetCullingMode _cullingMode;
        private SetFireEvents _fireEvents;
        private SetPlaybackTime _playbackTime;
        private SetRootPosition _rootPosition;
        private SetRootRotation _rootRotation;
        private SetStabilizeFeet _stabilizeFeet;
        private SetUpdateMode _updateMode;
        private SetApplyRootMotion _applyRootMotion;
        private SetFeetPivotActive _feetPivotActive;
        private SetKeepAnimatorStateOnDisable _keepAnimatorStateOnDisable;
        private SetWriteDefaultValuesOnDisable _writeDefaultValuesOnDisable;
        private SetLogWarnings _logWarnings;
        private SetLayersAffectMassCenter _layersAffectMassCenter;
        internal ResetTrigger _resetTrigger;
        private Play_STATEHASH_LAYER_NORMALIZEDTIME _play_STATEHASH_LAYER_NORMALIZEDTIME;
        private Play_STATEHASH_LAYER _play_STATEHASH_LAYER;
        private PLAY_STATEHASH _PLAY_STATEHASH;
        private Rebind _rebind;
        private UpdateWithDelta _updateWithDelta;
        private CrossFade_5 _crossFade_5;
        private CrossFade_4 _crossFade_4;
        private CrossFade_3 _crossFade_3;
        private CrossFade_2 _crossFade_2;
        private MatchTarget _matchTarget;
        private InterruptMatchTarget _interruptMatchTarget;
        internal SetLayerWeight _setLayerWeight;
        private WriteDefaultValues _writeDefaultValues;
        private ApplyBuiltinRootMotion _applyBuiltinRootMotion;
        private PlayInFixedTime _playInFixedTime;
        internal SetBoneLocalRotation _setBoneLocalRotation;
        internal SetIKPosition _setIKPosition;
        internal SetIKRotation _setIKRotation;
        private SetLookAtPosition _setLookAtPosition;
        private SetLookAtWeight _setLookAtWeight;
        private CrossFadeInFixedTime _crossFadeInFixedTime;
        internal SetIKHintPosition _setIKHintPosition;
        internal SetIKPositionWeight _setIKPositionWeight;
        internal SetIKRotationWeight _setIKRotationWeight;
        internal SetIKHintPositionWeight _setIKHintPositionWeight;
        private SetRuntimeAnimatorController _setRuntimeAnimatorController;
        private SetAvatar _setAvatar;
        private SetTarget _setTarget;
        private StartPlayback _startPlayback;
        private StopPlayback _stopPlayback;

        public NetAnimatorRPC(SetRuntimeAnimatorController action) : this()
        {
            type = NetAnimatorAction.SetRuntimeAnimatorController;
            _setRuntimeAnimatorController = action;
        }

        public NetAnimatorRPC(SetAvatar action) : this()
        {
            type = NetAnimatorAction.SetAvatar;
            _setAvatar = action;
        }

        public NetAnimatorRPC(SetBoneLocalRotation action) : this()
        {
            type = NetAnimatorAction.SetBoneLocalRotation;
            _setBoneLocalRotation = action;
        }

        public NetAnimatorRPC(SetIKHintPosition action) : this()
        {
            type = NetAnimatorAction.SetIKHintPosition;
            _setIKHintPosition = action;
        }

        public NetAnimatorRPC(SetIKPositionWeight action) : this()
        {
            type = NetAnimatorAction.SetIKPositionWeight;
            _setIKPositionWeight = action;
        }

        public NetAnimatorRPC(SetIKRotationWeight action) : this()
        {
            type = NetAnimatorAction.SetIKRotationWeight;
            _setIKRotationWeight = action;
        }

        public NetAnimatorRPC(SetIKHintPositionWeight action) : this()
        {
            type = NetAnimatorAction.SetIKHintPositionWeight;
            _setIKHintPositionWeight = action;
        }

        public NetAnimatorRPC(CrossFadeInFixedTime action) : this()
        {
            type = NetAnimatorAction.CrossFadeInFixedTime;
            _crossFadeInFixedTime = action;
        }

        public NetAnimatorRPC(SetIKPosition action) : this()
        {
            type = NetAnimatorAction.SetIKPosition;
            _setIKPosition = action;
        }

        public NetAnimatorRPC(SetIKRotation action) : this()
        {
            type = NetAnimatorAction.SetIKRotation;
            _setIKRotation = action;
        }

        public NetAnimatorRPC(SetBool action) : this()
        {
            type = NetAnimatorAction.SetBool;
            _bool = action;
        }

        public NetAnimatorRPC(SetFloat action) : this()
        {
            type = NetAnimatorAction.SetFloat;
            _float = action;
        }

        public NetAnimatorRPC(SetInteger action) : this()
        {
            type = NetAnimatorAction.SetInteger;
            _integer = action;
        }

        public NetAnimatorRPC(SetTrigger action) : this()
        {
            type = NetAnimatorAction.SetTrigger;
            _trigger = action;
        }

        public NetAnimatorRPC(ResetTrigger action) : this()
        {
            type = NetAnimatorAction.ResetTrigger;
            _resetTrigger = action;
        }

        public NetAnimatorRPC(SetSpeed action) : this()
        {
            type = NetAnimatorAction.SetSpeed;
            _speed = action;
        }

        public NetAnimatorRPC(SetAnimatePhysics action) : this()
        {
            type = NetAnimatorAction.SetAnimatePhysics;
            _animatePhysics = action;
        }

        public NetAnimatorRPC(SetBodyPosition action) : this()
        {
            type = NetAnimatorAction.SetBodyPosition;
            _bodyPosition = action;
        }

        public NetAnimatorRPC(SetBodyRotation action) : this()
        {
            type = NetAnimatorAction.SetBodyRotation;
            _bodyRotation = action;
        }

        public NetAnimatorRPC(SetCullingMode action) : this()
        {
            type = NetAnimatorAction.SetCullingMode;
            _cullingMode = action;
        }

        public NetAnimatorRPC(SetFireEvents action) : this()
        {
            type = NetAnimatorAction.SetFireEvents;
            _fireEvents = action;
        }

        public NetAnimatorRPC(SetPlaybackTime action) : this()
        {
            type = NetAnimatorAction.SetPlaybackTime;
            _playbackTime = action;
        }

        public NetAnimatorRPC(SetRootPosition action) : this()
        {
            type = NetAnimatorAction.SetRootPosition;
            _rootPosition = action;
        }

        public NetAnimatorRPC(SetRootRotation action) : this()
        {
            type = NetAnimatorAction.SetRootRotation;
            _rootRotation = action;
        }

        public NetAnimatorRPC(SetStabilizeFeet action) : this()
        {
            type = NetAnimatorAction.SetStabilizeFeet;
            _stabilizeFeet = action;
        }

        public NetAnimatorRPC(SetUpdateMode action) : this()
        {
            type = NetAnimatorAction.SetUpdateMode;
            _updateMode = action;
        }

        public NetAnimatorRPC(SetApplyRootMotion action) : this()
        {
            type = NetAnimatorAction.SetApplyRootMotion;
            _applyRootMotion = action;
        }

        public NetAnimatorRPC(SetFeetPivotActive action) : this()
        {
            type = NetAnimatorAction.SetFeetPivotActive;
            _feetPivotActive = action;
        }

        public NetAnimatorRPC(SetKeepAnimatorStateOnDisable action) : this()
        {
            type = NetAnimatorAction.SetKeepAnimatorStateOnDisable;
            _keepAnimatorStateOnDisable = action;
        }

        public NetAnimatorRPC(SetWriteDefaultValuesOnDisable action) : this()
        {
            type = NetAnimatorAction.SetWriteDefaultValuesOnDisable;
            _writeDefaultValuesOnDisable = action;
        }

        public NetAnimatorRPC(SetLogWarnings action) : this()
        {
            type = NetAnimatorAction.SetLogWarnings;
            _logWarnings = action;
        }

        public NetAnimatorRPC(SetLayersAffectMassCenter action) : this()
        {
            type = NetAnimatorAction.SetLayersAffectMassCenter;
            _layersAffectMassCenter = action;
        }

        public NetAnimatorRPC(Play_STATEHASH_LAYER_NORMALIZEDTIME action) : this()
        {
            type = NetAnimatorAction.Play_STATEHASH_LAYER_NORMALIZEDTIME;
            _play_STATEHASH_LAYER_NORMALIZEDTIME = action;
        }

        public NetAnimatorRPC(Play_STATEHASH_LAYER action) : this()
        {
            type = NetAnimatorAction.Play_STATEHASH_LAYER;
            _play_STATEHASH_LAYER = action;
        }

        public NetAnimatorRPC(PLAY_STATEHASH action) : this()
        {
            type = NetAnimatorAction.PLAY_STATEHASH;
            _PLAY_STATEHASH = action;
        }

        public NetAnimatorRPC(Rebind action) : this()
        {
            type = NetAnimatorAction.Rebind;
            _rebind = action;
        }

        public NetAnimatorRPC(UpdateWithDelta action) : this()
        {
            type = NetAnimatorAction.UpdateWithDelta;
            _updateWithDelta = action;
        }

        public NetAnimatorRPC(CrossFade_5 action) : this()
        {
            type = NetAnimatorAction.CrossFade_5;
            _crossFade_5 = action;
        }

        public NetAnimatorRPC(CrossFade_4 action) : this()
        {
            type = NetAnimatorAction.CrossFade_4;
            _crossFade_4 = action;
        }

        public NetAnimatorRPC(CrossFade_3 action) : this()
        {
            type = NetAnimatorAction.CrossFade_3;
            _crossFade_3 = action;
        }

        public NetAnimatorRPC(CrossFade_2 action) : this()
        {
            type = NetAnimatorAction.CrossFade_2;
            _crossFade_2 = action;
        }

        public NetAnimatorRPC(MatchTarget action) : this()
        {
            type = NetAnimatorAction.MatchTarget;
            _matchTarget = action;
        }

        public NetAnimatorRPC(InterruptMatchTarget action) : this()
        {
            type = NetAnimatorAction.InterruptMatchTarget;
            _interruptMatchTarget = action;
        }

        public NetAnimatorRPC(SetLayerWeight action) : this()
        {
            type = NetAnimatorAction.SetLayerWeight;
            _setLayerWeight = action;
        }

        public NetAnimatorRPC(WriteDefaultValues action) : this()
        {
            type = NetAnimatorAction.WriteDefaultValues;
            _writeDefaultValues = action;
        }

        public NetAnimatorRPC(ApplyBuiltinRootMotion action) : this()
        {
            type = NetAnimatorAction.ApplyBuiltinRootMotion;
            _applyBuiltinRootMotion = action;
        }

        public NetAnimatorRPC(PlayInFixedTime action) : this()
        {
            type = NetAnimatorAction.PlayInFixedTime;
            _playInFixedTime = action;
        }

        public NetAnimatorRPC(SetLookAtPosition action) : this()
        {
            type = NetAnimatorAction.SetLookAtPosition;
            _setLookAtPosition = action;
        }

        public NetAnimatorRPC(SetLookAtWeight action) : this()
        {
            type = NetAnimatorAction.SetLookAtWeight;
            _setLookAtWeight = action;
        }

        public NetAnimatorRPC(SetTarget action) : this()
        {
            type = NetAnimatorAction.SetTarget;
            _setTarget = action;
        }

        public NetAnimatorRPC(StartPlayback action) : this()
        {
            type = NetAnimatorAction.StartPlayback;
            _startPlayback = action;
        }

        public NetAnimatorRPC(StopPlayback action) : this()
        {
            type = NetAnimatorAction.StopPlayback;
            _stopPlayback = action;
        }

        public void Apply(Animator anim)
        {
            switch (type)
            {
                case NetAnimatorAction.SetBool: _bool.Apply(anim); break;
                case NetAnimatorAction.SetFloat: _float.Apply(anim); break;
                case NetAnimatorAction.SetInteger: _integer.Apply(anim); break;
                case NetAnimatorAction.SetTrigger: _trigger.Apply(anim); break;
                case NetAnimatorAction.SetSpeed: _speed.Apply(anim); break;
                case NetAnimatorAction.SetAnimatePhysics: _animatePhysics.Apply(anim); break;
                case NetAnimatorAction.SetBodyPosition: _bodyPosition.Apply(anim); break;
                case NetAnimatorAction.SetBodyRotation: _bodyRotation.Apply(anim); break;
                case NetAnimatorAction.SetCullingMode: _cullingMode.Apply(anim); break;
                case NetAnimatorAction.SetFireEvents: _fireEvents.Apply(anim); break;
                case NetAnimatorAction.SetPlaybackTime: _playbackTime.Apply(anim); break;
                case NetAnimatorAction.SetRootPosition: _rootPosition.Apply(anim); break;
                case NetAnimatorAction.SetRootRotation: _rootRotation.Apply(anim); break;
                case NetAnimatorAction.SetStabilizeFeet: _stabilizeFeet.Apply(anim); break;
                case NetAnimatorAction.SetUpdateMode: _updateMode.Apply(anim); break;
                case NetAnimatorAction.SetApplyRootMotion: _applyRootMotion.Apply(anim); break;
                case NetAnimatorAction.SetFeetPivotActive: _feetPivotActive.Apply(anim); break;
                case NetAnimatorAction.SetKeepAnimatorStateOnDisable: _keepAnimatorStateOnDisable.Apply(anim); break;
                case NetAnimatorAction.SetWriteDefaultValuesOnDisable: _writeDefaultValuesOnDisable.Apply(anim); break;
                case NetAnimatorAction.SetLogWarnings: _logWarnings.Apply(anim); break;
                case NetAnimatorAction.SetLayersAffectMassCenter: _layersAffectMassCenter.Apply(anim); break;
                case NetAnimatorAction.ResetTrigger: _resetTrigger.Apply(anim); break;
                case NetAnimatorAction.Play_STATEHASH_LAYER_NORMALIZEDTIME:
                    _play_STATEHASH_LAYER_NORMALIZEDTIME.Apply(anim); break;
                case NetAnimatorAction.Play_STATEHASH_LAYER: _play_STATEHASH_LAYER.Apply(anim); break;
                case NetAnimatorAction.PLAY_STATEHASH: _PLAY_STATEHASH.Apply(anim); break;
                case NetAnimatorAction.Rebind: _rebind.Apply(anim); break;
                case NetAnimatorAction.UpdateWithDelta: _updateWithDelta.Apply(anim); break;
                case NetAnimatorAction.CrossFade_5: _crossFade_5.Apply(anim); break;
                case NetAnimatorAction.CrossFade_4: _crossFade_4.Apply(anim); break;
                case NetAnimatorAction.CrossFade_3: _crossFade_3.Apply(anim); break;
                case NetAnimatorAction.CrossFade_2: _crossFade_2.Apply(anim); break;
                case NetAnimatorAction.MatchTarget: _matchTarget.Apply(anim); break;
                case NetAnimatorAction.InterruptMatchTarget: _interruptMatchTarget.Apply(anim); break;
                case NetAnimatorAction.SetLayerWeight: _setLayerWeight.Apply(anim); break;
                case NetAnimatorAction.WriteDefaultValues: _writeDefaultValues.Apply(anim); break;
                case NetAnimatorAction.ApplyBuiltinRootMotion: _applyBuiltinRootMotion.Apply(anim); break;
                case NetAnimatorAction.PlayInFixedTime: _playInFixedTime.Apply(anim); break;
                case NetAnimatorAction.SetBoneLocalRotation: _setBoneLocalRotation.Apply(anim); break;
                case NetAnimatorAction.SetIKPosition: _setIKPosition.Apply(anim); break;
                case NetAnimatorAction.SetIKRotation: _setIKRotation.Apply(anim); break;
                case NetAnimatorAction.SetLookAtPosition: _setLookAtPosition.Apply(anim); break;
                case NetAnimatorAction.SetLookAtWeight: _setLookAtWeight.Apply(anim); break;
                case NetAnimatorAction.CrossFadeInFixedTime: _crossFadeInFixedTime.Apply(anim); break;
                case NetAnimatorAction.SetIKHintPosition: _setIKHintPosition.Apply(anim); break;
                case NetAnimatorAction.SetIKPositionWeight: _setIKPositionWeight.Apply(anim); break;
                case NetAnimatorAction.SetIKRotationWeight: _setIKRotationWeight.Apply(anim); break;
                case NetAnimatorAction.SetIKHintPositionWeight: _setIKHintPositionWeight.Apply(anim); break;
                case NetAnimatorAction.SetRuntimeAnimatorController:
                    _setRuntimeAnimatorController.Apply(anim); break;
                case NetAnimatorAction.SetAvatar: _setAvatar.Apply(anim); break;
                case NetAnimatorAction.SetTarget: _setTarget.Apply(anim); break;
                case NetAnimatorAction.StartPlayback: _startPlayback.Apply(anim); break;
                case NetAnimatorAction.StopPlayback: _stopPlayback.Apply(anim); break;
                default:
                    throw new System.NotImplementedException(type.ToString());
            }
        }

        public void Serialize(BitPacker packer)
        {
            Packer<NetAnimatorAction>.Serialize(packer, ref type);

            switch (type)
            {
                case NetAnimatorAction.SetBool: Packer<SetBool>.Serialize(packer, ref _bool); break;
                case NetAnimatorAction.SetFloat: Packer<SetFloat>.Serialize(packer, ref _float); break;
                case NetAnimatorAction.SetInteger: Packer<SetInteger>.Serialize(packer, ref _integer); break;
                case NetAnimatorAction.SetTrigger: Packer<SetTrigger>.Serialize(packer, ref _trigger); break;
                case NetAnimatorAction.SetSpeed: Packer<SetSpeed>.Serialize(packer, ref _speed); break;
                case NetAnimatorAction.SetAnimatePhysics:
                    Packer<SetAnimatePhysics>.Serialize(packer, ref _animatePhysics); break;
                case NetAnimatorAction.SetBodyPosition:
                    Packer<SetBodyPosition>.Serialize(packer, ref _bodyPosition); break;
                case NetAnimatorAction.SetBodyRotation:
                    Packer<SetBodyRotation>.Serialize(packer, ref _bodyRotation); break;
                case NetAnimatorAction.SetCullingMode:
                    Packer<SetCullingMode>.Serialize(packer, ref _cullingMode); break;
                case NetAnimatorAction.SetFireEvents: Packer<SetFireEvents>.Serialize(packer, ref _fireEvents); break;
                case NetAnimatorAction.SetPlaybackTime:
                    Packer<SetPlaybackTime>.Serialize(packer, ref _playbackTime); break;
                case NetAnimatorAction.SetRootPosition:
                    Packer<SetRootPosition>.Serialize(packer, ref _rootPosition); break;
                case NetAnimatorAction.SetRootRotation:
                    Packer<SetRootRotation>.Serialize(packer, ref _rootRotation); break;
                case NetAnimatorAction.SetStabilizeFeet:
                    Packer<SetStabilizeFeet>.Serialize(packer, ref _stabilizeFeet); break;
                case NetAnimatorAction.SetUpdateMode: Packer<SetUpdateMode>.Serialize(packer, ref _updateMode); break;
                case NetAnimatorAction.SetApplyRootMotion:
                    Packer<SetApplyRootMotion>.Serialize(packer, ref _applyRootMotion); break;
                case NetAnimatorAction.SetFeetPivotActive:
                    Packer<SetFeetPivotActive>.Serialize(packer, ref _feetPivotActive); break;
                case NetAnimatorAction.SetKeepAnimatorStateOnDisable:
                    Packer<SetKeepAnimatorStateOnDisable>.Serialize(packer, ref _keepAnimatorStateOnDisable); break;
                case NetAnimatorAction.SetWriteDefaultValuesOnDisable:
                    Packer<SetWriteDefaultValuesOnDisable>.Serialize(packer, ref _writeDefaultValuesOnDisable); break;
                case NetAnimatorAction.SetLogWarnings:
                    Packer<SetLogWarnings>.Serialize(packer, ref _logWarnings); break;
                case NetAnimatorAction.SetLayersAffectMassCenter:
                    Packer<SetLayersAffectMassCenter>.Serialize(packer, ref _layersAffectMassCenter); break;
                case NetAnimatorAction.ResetTrigger: Packer<ResetTrigger>.Serialize(packer, ref _resetTrigger); break;
                case NetAnimatorAction.Play_STATEHASH_LAYER_NORMALIZEDTIME:
                    Packer<Play_STATEHASH_LAYER_NORMALIZEDTIME>.Serialize(packer,
                        ref _play_STATEHASH_LAYER_NORMALIZEDTIME); break;
                case NetAnimatorAction.Play_STATEHASH_LAYER:
                    Packer<Play_STATEHASH_LAYER>.Serialize(packer, ref _play_STATEHASH_LAYER); break;
                case NetAnimatorAction.PLAY_STATEHASH:
                    Packer<PLAY_STATEHASH>.Serialize(packer, ref _PLAY_STATEHASH); break;
                case NetAnimatorAction.Rebind: Packer<Rebind>.Serialize(packer, ref _rebind); break;
                case NetAnimatorAction.UpdateWithDelta:
                    Packer<UpdateWithDelta>.Serialize(packer, ref _updateWithDelta); break;
                case NetAnimatorAction.CrossFade_5: Packer<CrossFade_5>.Serialize(packer, ref _crossFade_5); break;
                case NetAnimatorAction.CrossFade_4: Packer<CrossFade_4>.Serialize(packer, ref _crossFade_4); break;
                case NetAnimatorAction.CrossFade_3: Packer<CrossFade_3>.Serialize(packer, ref _crossFade_3); break;
                case NetAnimatorAction.CrossFade_2: Packer<CrossFade_2>.Serialize(packer, ref _crossFade_2); break;
                case NetAnimatorAction.MatchTarget: Packer<MatchTarget>.Serialize(packer, ref _matchTarget); break;
                case NetAnimatorAction.InterruptMatchTarget:
                    Packer<InterruptMatchTarget>.Serialize(packer, ref _interruptMatchTarget); break;
                case NetAnimatorAction.SetLayerWeight:
                    Packer<SetLayerWeight>.Serialize(packer, ref _setLayerWeight); break;
                case NetAnimatorAction.WriteDefaultValues:
                    Packer<WriteDefaultValues>.Serialize(packer, ref _writeDefaultValues); break;
                case NetAnimatorAction.ApplyBuiltinRootMotion:
                    Packer<ApplyBuiltinRootMotion>.Serialize(packer, ref _applyBuiltinRootMotion); break;
                case NetAnimatorAction.PlayInFixedTime:
                    Packer<PlayInFixedTime>.Serialize(packer, ref _playInFixedTime); break;
                case NetAnimatorAction.SetBoneLocalRotation:
                    Packer<SetBoneLocalRotation>.Serialize(packer, ref _setBoneLocalRotation); break;
                case NetAnimatorAction.SetIKPosition:
                    Packer<SetIKPosition>.Serialize(packer, ref _setIKPosition); break;
                case NetAnimatorAction.SetIKRotation:
                    Packer<SetIKRotation>.Serialize(packer, ref _setIKRotation); break;
                case NetAnimatorAction.SetLookAtPosition:
                    Packer<SetLookAtPosition>.Serialize(packer, ref _setLookAtPosition); break;
                case NetAnimatorAction.SetLookAtWeight:
                    Packer<SetLookAtWeight>.Serialize(packer, ref _setLookAtWeight); break;
                case NetAnimatorAction.CrossFadeInFixedTime:
                    Packer<CrossFadeInFixedTime>.Serialize(packer, ref _crossFadeInFixedTime); break;
                case NetAnimatorAction.SetIKHintPosition:
                    Packer<SetIKHintPosition>.Serialize(packer, ref _setIKHintPosition); break;
                case NetAnimatorAction.SetIKPositionWeight:
                    Packer<SetIKPositionWeight>.Serialize(packer, ref _setIKPositionWeight); break;
                case NetAnimatorAction.SetIKRotationWeight:
                    Packer<SetIKRotationWeight>.Serialize(packer, ref _setIKRotationWeight); break;
                case NetAnimatorAction.SetIKHintPositionWeight:
                    Packer<SetIKHintPositionWeight>.Serialize(packer, ref _setIKHintPositionWeight); break;
                case NetAnimatorAction.SetRuntimeAnimatorController:
                    Packer<SetRuntimeAnimatorController>.Serialize(packer, ref _setRuntimeAnimatorController); break;
                case NetAnimatorAction.SetAvatar:
                    Packer<SetAvatar>.Serialize(packer, ref _setAvatar); break;
                case NetAnimatorAction.SetTarget:
                    Packer<SetTarget>.Serialize(packer, ref _setTarget); break;
                case NetAnimatorAction.StartPlayback:
                    Packer<StartPlayback>.Serialize(packer, ref _startPlayback); break;
                case NetAnimatorAction.StopPlayback:
                    Packer<StopPlayback>.Serialize(packer, ref _stopPlayback); break;
                default:
                    throw new System.NotImplementedException(type.ToString());
            }
        }
    }
}
#endif
