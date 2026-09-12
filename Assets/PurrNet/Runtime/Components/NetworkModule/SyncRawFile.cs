using System;

namespace PurrNet
{
    [Serializable]
    public class SyncRawFile : SyncFile<byte[]>
    {
        public SyncRawFile(bool ownerAuth = false, int maxKBPerSec = 15, bool ownerOnly = false) : base(ownerAuth, maxKBPerSec, ownerOnly) { }

        public override void FromBytes(ArraySegment<byte> bytes, ref byte[] content)
        {
            if (content == null)
            {
                content = bytes.ToArray();
                return;
            }

            if (content.Length != bytes.Count)
                Array.Resize(ref content, bytes.Count);

            bytes.CopyTo(content);
        }
    }
}
