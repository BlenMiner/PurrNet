using System;
using JetBrains.Annotations;
using PurrNet.Transports;
using UnityEngine;

namespace PurrNet
{
    public enum TimerEvent
    {
        Start,
        Stop,
        Pause,
        Resume
    }

    public enum TimerState
    {
        Stopped,
        Running,
        Paused
    }

    [Serializable]
    public class SyncTimer : NetworkModule, ITick
    {
        private readonly bool _ownerAuth;
        private readonly bool _ownerOnly;

        public override bool ownerOnly => _ownerOnly;
        private readonly float _reconcileInterval;

        [SerializeField, HideInInspector] private TimerState _state;
        [SerializeField, HideInInspector] private float _remaining;
        [SerializeField, HideInInspector] private float _lastReconcile;
        [SerializeField, HideInInspector] private bool _manualUpdate;

        public float remaining => _remaining;
        public bool isRunning => _state == TimerState.Running;
        public bool isPaused => _state == TimerState.Paused;

        public int remainingInt => Mathf.CeilToInt(_remaining);

        public event Action onTimerEnd;
        public event Action onTimerStart;
        public event Action onTimerSecondTick;
        public event Action onTimerPaused;
        public event Action onTimerResumed;

        public SyncTimer(bool ownerAuth = false, float reconcileInterval = 3, bool manualUpdate = false, bool ownerOnly = false)
        {
            _ownerAuth = ownerAuth;
            _ownerOnly = ownerOnly;
            _reconcileInterval = reconcileInterval;
            _state = TimerState.Stopped;
            _manualUpdate = manualUpdate;
        }

        public void OnTick(float delta)
        {
            if (_manualUpdate) 
                return;

            Advance(delta);
        }

        /// <summary>
        /// Used to manually advance the sync timer if the manual update has been set to true when initializing the timer
        /// </summary>
        /// <param name="delta">The delta to advance the timer by</param>
        /// <param name="useRunningCheck">Only for special use cases, where you'd want to skip the synchronized check to see whether the timer is running or not</param>
        public void Advance(float delta, bool useRunningCheck = true)
        {
            if (useRunningCheck && _state != TimerState.Running)
                return;
            
            int lastSecond = remainingInt;
            _remaining -= delta;
            if (lastSecond != remainingInt)
                onTimerSecondTick?.Invoke();

            if (IsController(_ownerAuth))
            {
                if (_remaining <= 0)
                {
                    HandleTimerEvent(TimerEvent.Stop);
                    return;
                }

                if (_lastReconcile + _reconcileInterval < Time.unscaledTime)
                {
                    if (isServer)
                        SendTimerEventToAll(TimerEvent.Start, _remaining, true, localPlayer);
                    else
                        SendTimerEventToServer(TimerEvent.Start, _remaining, true);
                    _lastReconcile = Time.unscaledTime;
                }
            }
        }

        public override void OnObserverAdded(PlayerID player)
        {
            BufferPlayer(player, _remaining, _state);
        }

        [TargetRpc]
        private void BufferPlayer([UsedImplicitly] PlayerID player, float timeRemaining, TimerState state)
        {
            _remaining = timeRemaining;
            _state = state;
        }

        private void HandleTimerEvent(TimerEvent timerEvent, float duration = 0, bool syncRemaining = false)
        {
            if (!IsController(_ownerAuth)) return;

            float remainingTime = syncRemaining ? _remaining : duration;

            switch (timerEvent)
            {
                case TimerEvent.Start:
                    _remaining = duration;
                    _state = TimerState.Running;
                    _lastReconcile = Time.unscaledTime;
                    onTimerStart?.Invoke();
                    break;

                case TimerEvent.Stop:
                    _state = TimerState.Stopped;
                    _remaining = 0;
                    onTimerEnd?.Invoke();
                    break;

                case TimerEvent.Pause:
                    _state = TimerState.Paused;
                    onTimerPaused?.Invoke();
                    break;

                case TimerEvent.Resume:
                    _state = TimerState.Running;
                    _lastReconcile = Time.unscaledTime;
                    onTimerResumed?.Invoke();
                    break;
            }

            if (isServer)
                SendTimerEventToAll(timerEvent, remainingTime, syncRemaining, localPlayer);
            else
                SendTimerEventToServer(timerEvent, remainingTime, syncRemaining);
        }

        [ServerRpc(Channel.ReliableOrdered, requireOwnership: true)]
        private void SendTimerEventToServer(TimerEvent timerEvent, float remainingTime, bool syncRemaining,
            RPCInfo info = default)
        {
            ProcessTimerEvent(timerEvent, remainingTime, syncRemaining);
            SendTimerEventToAll(timerEvent, remainingTime, syncRemaining, info.sender);
        }

        [ObserversRpc(Channel.ReliableOrdered)]
        private void SendTimerEventToAll(TimerEvent timerEvent, float remainingTime, bool syncRemaining,
            PlayerID? toIgnore = null)
        {
            if (toIgnore.HasValue && localPlayer.HasValue && toIgnore.Value.id == localPlayer.Value.id)
                return;

            ProcessTimerEvent(timerEvent, remainingTime, syncRemaining);
        }

        private void ProcessTimerEvent(TimerEvent timerEvent, float remainingTime, bool syncRemaining)
        {
            if (syncRemaining)
                _remaining = remainingTime;

            var previousState = _state;

            switch (timerEvent)
            {
                case TimerEvent.Start:
                    if (!syncRemaining)
                        _remaining = remainingTime;

                    if (previousState != TimerState.Running)
                    {
                        _state = TimerState.Running;
                        onTimerStart?.Invoke();
                    }

                    break;

                case TimerEvent.Stop:
                    if (previousState != TimerState.Stopped)
                    {
                        _state = TimerState.Stopped;
                        if (!syncRemaining)
                            _remaining = 0;
                        onTimerEnd?.Invoke();
                    }

                    break;

                case TimerEvent.Pause:
                    if (previousState != TimerState.Paused)
                    {
                        _state = TimerState.Paused;
                        onTimerPaused?.Invoke();
                    }

                    break;

                case TimerEvent.Resume:
                    if (previousState != TimerState.Running)
                    {
                        _state = TimerState.Running;
                        onTimerResumed?.Invoke();
                    }

                    break;
            }
        }

        public void StartTimer(float duration) => HandleTimerEvent(TimerEvent.Start, duration);

        public void StopTimer(bool syncRemaining = false) =>
            HandleTimerEvent(TimerEvent.Stop, syncRemaining: syncRemaining);

        public void PauseTimer(bool syncRemaining = false) =>
            HandleTimerEvent(TimerEvent.Pause, syncRemaining: syncRemaining);

        public void ResumeTimer() => HandleTimerEvent(TimerEvent.Resume);
    }
}
