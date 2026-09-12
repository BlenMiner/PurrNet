using System;
using UnityEngine;

namespace PurrNet
{
    [Serializable]
    public class SyncSpriteFile : SyncFile<Sprite>
    {
        private Texture2D _cache;

        public SyncSpriteFile(bool ownerAuth = false, int maxKBPerSec = 15, bool ownerOnly = false) : base(ownerAuth, maxKBPerSec, ownerOnly) { }

        public override void FromBytes(ArraySegment<byte> bytes, ref Sprite content)
        {
            if (!_cache)
                _cache = new Texture2D(1, 1);
            _cache.LoadImage(bytes.Array);
            content = Sprite.Create(_cache, new Rect(0, 0, _cache.width, _cache.height), new Vector2(0.5f, 0.5f));
        }
    }
}
