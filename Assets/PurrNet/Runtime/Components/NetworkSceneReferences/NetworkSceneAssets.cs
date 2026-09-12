using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    public class NetworkSceneAssets : SceneRegistry<NetworkAssets>
    {
        [SerializeField, PurrLock] private NetworkAssets _networkAssets;

        public override NetworkAssets Me() => _networkAssets;
    }
}
