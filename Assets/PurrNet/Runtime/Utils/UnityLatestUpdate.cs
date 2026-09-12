using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrNet.Pooling;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(32000)]
    public class UnityLatestUpdate : MonoBehaviour
    {
        static UnityLatestUpdate _instance;

        private static readonly PurrAction<Action> _update = new(static action => action(), 32);
        private static readonly PurrAction<Action> _fixedUpdate = new(static action => action(), 64);
        private static readonly PurrAction<Action> _latestUpdate = new(static action => action(), 64);
        private static readonly PurrAction<Action> _postLatestUpdate = new(static action => action(), 8);

        internal static PurrAction<Action> update => _update;

        internal static PurrAction<Action> fixedUpdate => _fixedUpdate;

        internal static PurrAction<Action> latestUpdate => _latestUpdate;

        public static event Action onUpdate
        {
            add => _update.Add(value);
            remove => _update.Remove(value);
        }

        public static event Action onFixedUpdate
        {
            add => _fixedUpdate.Add(value);
            remove => _fixedUpdate.Remove(value);
        }

        public static event Action onLatestUpdate
        {
            add => _latestUpdate.Add(value);
            remove => _latestUpdate.Remove(value);
        }

        /// <summary>
        /// Runs after every <see cref="onLatestUpdate"/> subscriber; for work that must
        /// observe everything the latest-update callbacks produced this frame.
        /// </summary>
        public static event Action onPostLatestUpdate
        {
            add => _postLatestUpdate.Add(value);
            remove => _postLatestUpdate.Remove(value);
        }

        private static readonly List<PriorityAction> _executeASAP = new();

        struct PriorityAction
        {
            public int priority;
            public int subPriority;
            public Action action;
        }

        private void Awake()
        {
            TriggerPendingAsaps();
        }

        /// <summary>
        /// Execute body as soon as possible, be it Update/LateUpdate/Start/Awake whatever
        /// Higher priority value means it will be executed later
        /// </summary>
        /// <param name="action"></param>
        /// <param name="priority"></param>
        /// <param name="subPriority"></param>
        public static void ExecuteAsap(Action action, int priority = 0, int subPriority = 0)
        {
            var item = new PriorityAction
            {
                priority = priority,
                subPriority = subPriority,
                action = action,
            };

            lock (_executeASAP)
            {
                int insertIdx = _executeASAP.Count;

                for (int i = 0; i < _executeASAP.Count; i++)
                {
                    var cur = _executeASAP[i];
                    if (cur.priority > priority ||
                        (cur.priority == priority && cur.subPriority > subPriority))
                    {
                        insertIdx = i;
                        break;
                    }
                }

                _executeASAP.Insert(insertIdx, item);
            }
        }

        public static void TriggerPendingAsaps()
        {
            List<PriorityAction> toRun;
            lock (_executeASAP)
            {
                if (_executeASAP.Count == 0)
                    return;
                toRun = ListPool<PriorityAction>.Instantiate();
                toRun.AddRange(_executeASAP);
                _executeASAP.Clear();
            }
            try
            {
                for (var i = 0; i < toRun.Count; i++)
                {
                    try
                    {
                        toRun[i].action?.Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                ListPool<PriorityAction>.Destroy(toRun);
            }
        }

        /// <summary>Completes during the next Unity update.</summary>
        /// <remarks>Call from the Unity main thread.</remarks>
        public static Task Yield()
        {
            var promise = new TaskCompletionSource<bool>();

            onUpdate += OnUpdate;

            return promise.Task;

            void OnUpdate()
            {
                if (promise.TrySetResult(true))
                    onUpdate -= OnUpdate;
            }
        }

        /// <summary>Completes after at least <paramref name="seconds"/> of Unity update time.</summary>
        /// <remarks>Call from the Unity main thread.</remarks>
        public static Task WaitSeconds(float seconds)
        {
            var promise = new TaskCompletionSource<bool>();
            float timer = 0f;

            onUpdate += OnUpdate;

            return promise.Task;

            void OnUpdate()
            {
                timer += Time.deltaTime;
                if (timer >= seconds && promise.TrySetResult(true))
                    onUpdate -= OnUpdate;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            _update.Clear();
            _fixedUpdate.Clear();
            _latestUpdate.Clear();
            _postLatestUpdate.Clear();
            lock (_executeASAP)
                _executeASAP.Clear();

            if (_instance)
                return;

            var go = new GameObject("PurrNet_UnityLatestUpdate")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);

            _instance = go.AddComponent<UnityLatestUpdate>();
        }

#if UNITY_EDITOR && PURR_LEAKS_CHECK
        private float _sweep;
#endif

        private void OnEnable()
        {
            TriggerPendingAsaps();
        }

        private void OnDisable()
        {
            TriggerPendingAsaps();
        }

        private void OnDestroy()
        {
            TriggerPendingAsaps();
        }

        private void Update()
        {
            TriggerPendingAsaps();
            _update.Invoke();
#if UNITY_EDITOR && PURR_LEAKS_CHECK
            _sweep += Time.deltaTime;

            if (_sweep >= 1f)
            {
                _sweep = 0f;
                AllocationTracker.CheckForLeaks();
            }
#endif
        }

        private void FixedUpdate()
        {
            TriggerPendingAsaps();
            _fixedUpdate.Invoke();
        }

        private void LateUpdate()
        {
            TriggerPendingAsaps();
            _latestUpdate.Invoke();
            _postLatestUpdate.Invoke();
        }
    }
}
