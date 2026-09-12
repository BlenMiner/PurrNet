using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class FloatPacking
    {
        [UsedByIL]
        public static unsafe void Write(this BitPacker packer, float data)
        {
            ulong bits = *(uint*)&data;
            packer.WriteBits(bits, 32);
        }

        [UsedByIL]
        public static unsafe void Read(this BitPacker packer, ref float data)
        {
            ulong bits = packer.ReadBits(32);
            data = *(float*)&bits;
        }

        [UsedByIL]
        private static unsafe bool WriteSingle(BitPacker packer, float oldvalue, float newvalue)
        {
            uint newbits = *(uint*)&newvalue;
            uint oldbits = *(uint*)&oldvalue;

            if (newbits == oldbits)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            uint zigzag = PackingIntegers.ZigzagEncode((int)(newbits - oldbits));
            int bitCount = 64 - PackingIntegers.CountLeadingZeroBits(zigzag);
            packer.WriteBits((ulong)(bitCount - 1), 5);
            packer.WriteBits(zigzag, (byte)bitCount);
            return true;
        }

        [UsedByIL]
        private static unsafe void ReadSingle(BitPacker packer, float oldvalue, ref float value)
        {
            bool hasChanged = packer.ReadBit();

            if (!hasChanged)
            {
                value = oldvalue;
                return;
            }

            int bitCount = (int)packer.ReadBits(5) + 1;
            uint zigzag = (uint)packer.ReadBits((byte)bitCount);
            uint diff = (uint)PackingIntegers.ZigzagDecode(zigzag);
            uint newbits = *(uint*)&oldvalue + diff;
            value = *(float*)&newbits;
        }
    }
}
