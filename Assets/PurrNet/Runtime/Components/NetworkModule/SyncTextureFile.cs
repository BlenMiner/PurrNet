using System;
using UnityEngine;

namespace PurrNet
{
    [Serializable]
    public class SyncTextureFile : SyncFile<Texture2D>
    {
        public SyncTextureFile(bool ownerAuth = false, int maxKBPerSec = 15, bool ownerOnly = false) : base(ownerAuth, maxKBPerSec, ownerOnly) { }

        public override void FromBytes(ArraySegment<byte> bytes, ref Texture2D content)
        {
            if (!content)
                content = new Texture2D(1, 1);

            content.LoadImage(bytes.Array);
        }
    }
}
