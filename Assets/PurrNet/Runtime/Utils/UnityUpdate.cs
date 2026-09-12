using System;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-1000)]
    public class UnityUpdate : MonoBehaviour
    {
        private static UnityUpdate _instance;

        private static readonly PurrAction<Action> _update = new(static action => action(), 64);
        private static readonly PurrAction<Action> _lateUpdate = new(static action => action(), 64);

        internal static PurrAction<Action> update => _update;

        internal static PurrAction<Action> lateUpdate => _lateUpdate;

        public static event Action onUpdate
        {
            add => _update.Add(value);
            remove => _update.Remove(value);
        }

        public static event Action onLateUpdate
        {
            add => _lateUpdate.Add(value);
            remove => _lateUpdate.Remove(value);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            _update.Clear();
            _lateUpdate.Clear();

            if (_instance)
                return;

            var go = new GameObject("PurrNet_UnityUpdate")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            DontDestroyOnLoad(go);

            _instance = go.AddComponent<UnityUpdate>();
        }

        private void Update()
        {
            _update.Invoke();
        }

        private void LateUpdate()
        {
            _lateUpdate.Invoke();
        }
    }
}
