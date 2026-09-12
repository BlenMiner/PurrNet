using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    public class NetworkScenePrefabs : SceneRegistry<NetworkPrefabs>
    {
        [SerializeField, PurrLock] private NetworkPrefabs _networkPrefabs;

        public override NetworkPrefabs Me() => _networkPrefabs;
    }
}
