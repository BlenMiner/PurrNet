using System;

namespace PurrNet.Packing
{
    public struct BitData : IDisposable, IDuplicate<BitData>, IEquatable<BitData>
    {
        public readonly BitPacker packer;
        public Size bitOrigin;
        public Size bitLength;

        public int byteLength => ((int)bitLength.value + 7) >> 3;

        public BitData(BitPacker packer)
        {
            this.packer = packer;
            this.bitOrigin = 0;
            this.bitLength = packer.positionInBits;
        }

        public BitData(BitPacker packer, int bitOrigin, int bitLength)
        {
            this.packer = packer;
            this.bitOrigin = bitOrigin;
            this.bitLength = bitLength;
        }

        public void Dispose()
        {
            packer?.Dispose();
        }

        public BitDataScope AutoScope()
        {
            return new BitDataScope(this);
        }

        public BitData Duplicate()
        {
            if (bitLength.value == 0 || packer == null)
                return default;
            var newpacker = BitPackerPool.Get();
            newpacker.WriteBitDataWithoutConsumingIt(this);
            return new BitData(newpacker);
        }

        public bool Equals(BitData other)
        {
            int count = (int)bitLength.value;
            int otherCount = (int)other.bitLength.value;

            if (packer == null) count = 0;
            if (other.packer == null) otherCount = 0;

            if (count != otherCount)
                return false;

            if (count == 0)
                return true;

            if (ReferenceEquals(packer, other.packer) && bitOrigin.value == other.bitOrigin.value)
                return true;

            return BitsEqual(packer.buffer, (int)bitOrigin.value, other.packer.buffer, (int)other.bitOrigin.value, count);
        }

        static bool BitsEqual(byte[] a, int aPos, byte[] b, int bPos, int count)
        {
            while (count > 0)
            {
                int chunk = count > 56 ? 56 : count;
                if (PeekBits(a, aPos, chunk) != PeekBits(b, bPos, chunk))
                    return false;
                aPos += chunk;
                bPos += chunk;
                count -= chunk;
            }

            return true;
        }

        static ulong PeekBits(byte[] buffer, int bitPos, int count)
        {
            int bytePos = bitPos >> 3;
            int bitOffset = bitPos & 7;
            int end = bytePos + 8 < buffer.Length ? bytePos + 8 : buffer.Length;

            ulong raw = 0;
            for (int i = bytePos; i < end; i++)
                raw |= (ulong)buffer[i] << ((i - bytePos) * 8);

            raw >>= bitOffset;
            return raw & ((1UL << count) - 1);
        }
    }
}
