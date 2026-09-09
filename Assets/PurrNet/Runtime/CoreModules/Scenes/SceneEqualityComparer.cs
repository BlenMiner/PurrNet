using System.Collections.Generic;
using UnityEngine.SceneManagement;

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
            return obj.handle;
        }
    }
}
