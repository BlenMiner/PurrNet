using System.Collections.Generic;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using SceneHandle = UnityEngine.SceneManagement.SceneHandle;
#else
using SceneHandle = System.Int32;
#endif

namespace PurrNet
{
    public abstract class SceneRegistry<T> : MonoBehaviour
    {
        private static readonly Dictionary<SceneHandle, List<T>> _entries = new();

        public abstract T Me();

        public static bool TryGetEntries(SceneHandle sceneHandle, out List<T> entries)
        {
            return _entries.TryGetValue(sceneHandle, out entries);
        }

        private T _cached;

        private void OnEnable()
        {
            _cached = Me();

            if (_cached == null)
                return;

            var handle = gameObject.scene.handle;
            if (_entries.TryGetValue(handle, out var entries))
                entries.Add(_cached);
            else _entries.Add(handle, new List<T> { _cached });
        }

        private void OnDisable()
        {
            if (_cached == null)
                return;

            var handle = gameObject.scene.handle;
            if (_entries.TryGetValue(handle, out var entries))
            {
                if (entries.Remove(_cached) && entries.Count == 0)
                    _entries.Remove(handle);
            }

            _cached = default;
        }
    }
}
