using System.Collections.Generic;
using UnityEngine.SceneManagement;
#if UNITY_6000_3_OR_NEWER
using SceneHandle = UnityEngine.SceneManagement.SceneHandle;
#else
using SceneHandle = System.Int32;
#endif

namespace PurrNet.Modules
{
    internal readonly struct SceneEqualityComparer : IEqualityComparer<Scene>
    {
        public static readonly SceneEqualityComparer instance = new SceneEqualityComparer();

        public bool Equals(Scene a, Scene b)
        {
            return a.handle == b.handle;
        }

        public int GetHashCode(Scene obj)
        {
            SceneHandle handle = obj.handle;
            return handle.GetHashCode();
        }
    }
}
