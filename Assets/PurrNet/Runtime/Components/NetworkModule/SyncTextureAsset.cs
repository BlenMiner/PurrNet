using System;
using UnityEngine;

namespace PurrNet
{
    [Serializable]
    public class SyncTextureAsset : SyncAsset<Texture2D>
    {
        public SyncTextureAsset(bool ownerAuth = false, int maxKBPerSec = 15, bool ownerOnly = false) : base(ownerAuth, maxKBPerSec, ownerOnly) { }

        public override void FromBytes(ArraySegment<byte> bytes, ref Texture2D content)
        {
            if (!content)
                content = new Texture2D(1, 1);

            content.LoadImage(bytes.Array);
        }

        public override ArraySegment<byte> GetBytes(Texture2D content)
        {
            return content.EncodeToJPG();
        }
    }
}
