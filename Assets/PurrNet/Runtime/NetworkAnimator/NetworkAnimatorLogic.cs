﻿using System.Collections.Generic;
using JetBrains.Annotations;
using PurrNet.Logging;

namespace PurrNet
{
    public partial class NetworkAnimator
    {
        readonly List<NetAnimatorRPC> _dirty = new ();
        readonly List<NetAnimatorRPC> _ikActions = new ();
        
        readonly List<PlayerID> _reconcilePlayers = new ();

        protected override void OnObserverAdded(PlayerID player)
        {
            Reconcile(player);
            _reconcilePlayers.Add(player);
        }

        protected override void OnTick(float delta)
        {
            if (!IsController(isController))
            {
                if (_dirty.Count > 0)
                    _dirty.Clear();
                return;
            }
            
            SendDirtyActions();
        }
        
        /// <summary>
        /// Sends the current state of the animator to the observers.
        /// This is useful when you need to ensure that the observers are in sync with the controller.
        /// </summary>
        public void Reconcile(bool isIk = false)
        {
            if (!IsController(isController))
                return;
            
            var data = NetAnimatorActionBatch.CreateReconcile(_animator, isIk);
            
            if (isServer)
            {
                ApplyActionsOnObservers(data);
            }
            else
            {
                ForwardThroughServer(data);
            }
        }
        
        /// <summary>
        /// Sends the current state of the animator to the target player.
        /// This is useful when a new player joins the scene.
        /// Or when you need to ensure that the player is in sync with the controller.
        /// </summary>
        /// <param name="target">The target player to reconcile the state with.</param>
        /// <param name="isIk">Whether to reconcile the IK state or the regular state.</param>
        public void Reconcile(PlayerID target, bool isIk = false)
        {
            if (!IsController(isController))
                return;
            
            var data = NetAnimatorActionBatch.CreateReconcile(_animator, isIk);
            
            if (isServer)
            {
                ReconcileState(target, data);
            }
            else
            {
                ForwardThroughServerToTarget(target, data);
            }
        }
        
        private void SendDirtyActions()
        {
            if (_dirty.Count <= 0)
                return;
            
            var batch = new NetAnimatorActionBatch
            {
                actions = _dirty
            };
            
            if (isServer)
            {
                ApplyActionsOnObservers(batch);
            }
            else
            {
                ForwardThroughServer(batch);
            }
            
            _dirty.Clear();
        }

        protected virtual void OnAnimatorIK(int layerIndex)
        {
            if (IsController(isController))
            {
                _ikActions.Clear();
                
                for (var i = 0; i < _reconcilePlayers.Count; i++)
                    Reconcile(_reconcilePlayers[i], true);
                _reconcilePlayers.Clear();
                return;
            }
            else _reconcilePlayers.Clear();
            
            for (var i = 0; i < _ikActions.Count; i++)
                _ikActions[i].Apply(_animator);
        }

        [TargetRPC]
        private void ReconcileState([UsedImplicitly] PlayerID player, NetAnimatorActionBatch actions)
        {
            if (IsController(_ownerAuth))
                return;
            
            ExecuteBatch(actions);
        }
        
        [ServerRPC]
        private void ForwardThroughServerToTarget(PlayerID target, NetAnimatorActionBatch actions)
        {
            if (_ownerAuth)
                ReconcileState(target, actions);
        }
        
        [ServerRPC]
        private void ForwardThroughServer(NetAnimatorActionBatch actions)
        {
            if (_ownerAuth)
                ApplyActionsOnObservers(actions);
        }
        
        [ObserversRPC]
        private void ApplyActionsOnObservers(NetAnimatorActionBatch actions)
        {
            if (IsController(_ownerAuth))
                return;
            
            ExecuteBatch(actions);
        }

        private void ExecuteBatch(NetAnimatorActionBatch actions)
        {
            if (!_animator)
            {
                PurrLogger.LogError($"Animator is null, can't apply actions, dismissing {actions.actions.Count} actions.");
                return;
            }
            
            if (actions.actions == null)
                return;

            for (var i = 0; i < actions.actions.Count; i++)
            {
                bool isIk = actions.actions[i].type is
                    NetAnimatorAction.SetIKPosition or NetAnimatorAction.SetIKRotation or
                    NetAnimatorAction.SetIKHintPosition or NetAnimatorAction.SetLookAtPosition or
                    NetAnimatorAction.SetBoneLocalRotation;

                if (!isIk)
                     actions.actions[i].Apply(_animator);
                else _ikActions.Add(actions.actions[i]);
            }
        }
    }
}