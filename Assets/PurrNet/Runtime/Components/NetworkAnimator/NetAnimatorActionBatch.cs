#if UNITY_ANIMATION
using System;
using System.Collections.Generic;
using PurrNet.Packing;
using PurrNet.Pooling;
using UnityEngine;

namespace PurrNet
{
    internal struct NetAnimatorActionBatch : IPackedAuto, IDisposable
    {
        public DisposableList<NetAnimatorRPC> actions;

        public void Dispose()
        {
            actions.Dispose();
        }

        public static NetAnimatorActionBatch CreateTimeReconcile(
            HashSet<int> ignoreHashes,
            Animator animator,
            IReadOnlyDictionary<int, bool> boolValues,
            IReadOnlyDictionary<int, float> floatValues,
            IReadOnlyDictionary<int, int> intValues)
        {
            var actions = DisposableList<NetAnimatorRPC>.Create();
            SyncParameters(ignoreHashes, animator, actions, boolValues, floatValues, intValues);
            return new NetAnimatorActionBatch
            {
                actions = actions
            };
        }

        public static NetAnimatorActionBatch CreateReconcile(
            HashSet<int> ignoreHashes,
            Animator animator,
            bool ik,
            IReadOnlyDictionary<int, bool> boolValues,
            IReadOnlyDictionary<int, float> floatValues,
            IReadOnlyDictionary<int, int> intValues)
        {
            var actions = DisposableList<NetAnimatorRPC>.Create();

            if (ik)
            {
                SyncIK(AvatarIKGoal.LeftFoot, animator, actions);
                SyncIK(AvatarIKGoal.RightFoot, animator, actions);
                SyncIK(AvatarIKGoal.LeftHand, animator, actions);
                SyncIK(AvatarIKGoal.RightHand, animator, actions);

                SyncIKHint(AvatarIKHint.LeftKnee, animator, actions);
                SyncIKHint(AvatarIKHint.RightKnee, animator, actions);
                SyncIKHint(AvatarIKHint.LeftElbow, animator, actions);
                SyncIKHint(AvatarIKHint.RightElbow, animator, actions);
            }
            else
            {
                SyncAssetReferences(animator, actions);
                SyncParameters(ignoreHashes, animator, actions, boolValues, floatValues, intValues);
                if (animator.isActiveAndEnabled)
                    SyncAnimationState(animator, actions);

                actions.Add(new NetAnimatorRPC(new SetApplyRootMotion
                {
                    value = animator.applyRootMotion
                }));

                actions.Add(new NetAnimatorRPC(new SetAnimatePhysics
                {
                    value = animator.animatePhysics
                }));

                actions.Add(new NetAnimatorRPC(new SetUpdateMode
                {
                    value = animator.updateMode
                }));

                actions.Add(new NetAnimatorRPC(new SetCullingMode
                {
                    value = animator.cullingMode
                }));
            }

            return new NetAnimatorActionBatch
            {
                actions = actions
            };
        }

        private static void SyncIK(AvatarIKGoal goal, Animator animator, DisposableList<NetAnimatorRPC> actions)
        {
            if (animator.GetIKPositionWeight(goal) > 0)
            {
                actions.Add(new NetAnimatorRPC(new SetIKPositionWeight
                {
                    goal = goal,
                    value = animator.GetIKPositionWeight(goal)
                }));
            }

            if (animator.GetIKRotationWeight(goal) > 0)
            {
                actions.Add(new NetAnimatorRPC(new SetIKRotationWeight
                {
                    goal = goal,
                    value = animator.GetIKRotationWeight(goal)
                }));
            }

            if (animator.GetIKPosition(goal) != default)
            {
                actions.Add(new NetAnimatorRPC(new SetIKPosition
                {
                    goal = goal,
                    position = animator.GetIKPosition(goal)
                }));
            }

            if (animator.GetIKRotation(goal) != Quaternion.identity)
            {
                actions.Add(new NetAnimatorRPC(new SetIKRotation
                {
                    goal = goal,
                    rotation = animator.GetIKRotation(goal)
                }));
            }
        }

        private static void SyncIKHint(AvatarIKHint hint, Animator animator, DisposableList<NetAnimatorRPC> actions)
        {
            if (animator.GetIKHintPositionWeight(hint) > 0)
            {
                actions.Add(new NetAnimatorRPC(new SetIKHintPositionWeight
                {
                    hint = hint,
                    value = animator.GetIKHintPositionWeight(hint)
                }));
            }

            if (animator.GetIKHintPosition(hint) != default)
            {
                actions.Add(new NetAnimatorRPC(new SetIKHintPosition
                {
                    hint = hint,
                    position = animator.GetIKHintPosition(hint)
                }));
            }
        }

        private static void SyncAssetReferences(Animator animator, DisposableList<NetAnimatorRPC> actions)
        {
            var nm = NetworkManager.main;
            if (!nm)
                return;

            var assets = nm.networkAssetResolver;

            var controller = animator.runtimeAnimatorController;
            if (controller && assets.TryGetId(controller, out _))
            {
                actions.Add(new NetAnimatorRPC(new SetRuntimeAnimatorController
                {
                    controller = controller
                }));
            }

            var avatar = animator.avatar;
            if (avatar && assets.TryGetId(avatar, out _))
            {
                actions.Add(new NetAnimatorRPC(new SetAvatar
                {
                    avatar = avatar
                }));
            }
        }

        private static void SyncAnimationState(Animator animator, DisposableList<NetAnimatorRPC> actions)
        {
            if (!animator.runtimeAnimatorController)
                return;

            for (var i = 0; i < animator.layerCount; i++)
            {
                var info = animator.GetCurrentAnimatorStateInfo(i);
                actions.Add(new NetAnimatorRPC(new Play_STATEHASH_LAYER_NORMALIZEDTIME
                {
                    stateHash = info.fullPathHash,
                    layer = i,
                    normalizedTime = info.normalizedTime
                }));

                if (i == 0)
                    continue;

                actions.Add(new NetAnimatorRPC(new SetLayerWeight
                {
                    layerIndex = i,
                    weight = animator.GetLayerWeight(i)
                }));
            }
        }

        private static void SyncParameters(
            HashSet<int> ignoreHashes,
            Animator animator,
            DisposableList<NetAnimatorRPC> actions,
            IReadOnlyDictionary<int, bool> boolValues,
            IReadOnlyDictionary<int, float> floatValues,
            IReadOnlyDictionary<int, int> intValues)
        {
            if (!animator.runtimeAnimatorController)
                return;

            var parameters = animator.parameters;
            bool useCachedValues = !animator.isActiveAndEnabled;

            for (var i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];

                if (ignoreHashes.Contains(param.nameHash))
                    continue;

                switch (param.type)
                {
                    case AnimatorControllerParameterType.Bool:
                    {
                        var setBool = new SetBool
                        {
                            value = useCachedValues && boolValues.TryGetValue(param.nameHash, out var value)
                                ? value
                                : animator.GetBool(param.nameHash),
                            nameHash = param.nameHash,
                            paramIndexPlusOne = i + 1
                        };

                        actions.Add(new NetAnimatorRPC(setBool));
                        break;
                    }
                    case AnimatorControllerParameterType.Float:
                    {
                        var setFloat = new SetFloat
                        {
                            value = useCachedValues && floatValues.TryGetValue(param.nameHash, out var value)
                                ? value
                                : animator.GetFloat(param.nameHash),
                            nameHash = param.nameHash,
                            paramIndexPlusOne = i + 1
                        };

                        actions.Add(new NetAnimatorRPC(setFloat));
                        break;
                    }
                    case AnimatorControllerParameterType.Int:
                    {
                        var setInteger = new SetInteger
                        {
                            value = useCachedValues && intValues.TryGetValue(param.nameHash, out var value)
                                ? value
                                : animator.GetInteger(param.nameHash),
                            nameHash = param.nameHash,
                            paramIndexPlusOne = i + 1
                        };

                        actions.Add(new NetAnimatorRPC(setInteger));
                        break;
                    }
                }
            }
        }
    }
}
#endif
