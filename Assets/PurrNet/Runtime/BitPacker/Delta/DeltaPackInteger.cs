using PurrNet.Modules;

namespace PurrNet.Packing
{
    public static class DeltaPackInteger
    {
        static readonly byte[] buckets8 = { 2, 4, 6, 8 };
        static readonly byte[] buckets16 = { 4, 8, 12, 16 };
        static readonly byte[] buckets32 = { 4, 8, 16, 32 };
        static readonly byte[] buckets64 = { 4, 8, 24, 64 };

        static void WriteDiff(BitPacker packer, ulong zigzag, byte[] buckets)
        {
            int bits = 64 - PackingIntegers.CountLeadingZeroBits(zigzag);
            int selector = 0;
            while (bits > buckets[selector]) selector++;
            packer.WriteBits((ulong)selector, 2);
            packer.WriteBits(zigzag, buckets[selector]);
        }

        static ulong ReadDiff(BitPacker packer, byte[] buckets)
        {
            int selector = (int)packer.ReadBits(2);
            return packer.ReadBits(buckets[selector]);
        }

        [UsedByIL]
        public static bool WriteBool(BitPacker packer, bool oldvalue, bool newvalue)
        {
            bool hasChanged = oldvalue != newvalue;
            packer.WriteBit(hasChanged);
            return hasChanged;
        }

        [UsedByIL]
        public static void ReadBool(BitPacker packer, bool oldvalue, ref bool value)
        {
            bool hasChanged = packer.ReadBit();
            value = hasChanged ? !oldvalue : oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt8(BitPacker packer, sbyte oldvalue, sbyte newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            WriteDiff(packer, PackingIntegers.ZigzagEncode((sbyte)(newvalue - oldvalue)), buckets8);
            return true;
        }

        [UsedByIL]
        public static void ReadInt8(BitPacker packer, sbyte oldvalue, ref sbyte value)
        {
            if (packer.ReadBit())
            {
                sbyte diff = PackingIntegers.ZigzagDecode((byte)ReadDiff(packer, buckets8));
                value = (sbyte)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt8(BitPacker packer, byte oldvalue, byte newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            WriteDiff(packer, PackingIntegers.ZigzagEncode((sbyte)(newvalue - oldvalue)), buckets8);
            return true;
        }

        [UsedByIL]
        public static void ReadUInt8(BitPacker packer, byte oldvalue, ref byte value)
        {
            if (packer.ReadBit())
            {
                sbyte diff = PackingIntegers.ZigzagDecode((byte)ReadDiff(packer, buckets8));
                value = (byte)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt16(BitPacker packer, short oldvalue, short newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            WriteDiff(packer, PackingIntegers.ZigzagEncode((short)(newvalue - oldvalue)), buckets16);
            return true;
        }

        [UsedByIL]
        public static void ReadInt16(BitPacker packer, short oldvalue, ref short value)
        {
            if (packer.ReadBit())
            {
                short diff = PackingIntegers.ZigzagDecode((ushort)ReadDiff(packer, buckets16));
                value = (short)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt16(BitPacker packer, ushort oldvalue, ushort newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            WriteDiff(packer, PackingIntegers.ZigzagEncode((short)(newvalue - oldvalue)), buckets16);
            return true;
        }

        [UsedByIL]
        public static void ReadUInt16(BitPacker packer, ushort oldvalue, ref ushort value)
        {
            if (packer.ReadBit())
            {
                short diff = PackingIntegers.ZigzagDecode((ushort)ReadDiff(packer, buckets16));
                value = (ushort)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt32(BitPacker packer, int oldvalue, int newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            WriteDiff(packer, PackingIntegers.ZigzagEncode(newvalue - oldvalue), buckets32);
            return true;
        }

        [UsedByIL]
        public static void ReadInt32(BitPacker packer, int oldvalue, ref int value)
        {
            if (packer.ReadBit())
            {
                int diff = PackingIntegers.ZigzagDecode((uint)ReadDiff(packer, buckets32));
                value = oldvalue + diff;
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt32(BitPacker packer, uint oldvalue, uint newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            WriteDiff(packer, PackingIntegers.ZigzagEncode((int)(newvalue - oldvalue)), buckets32);
            return true;
        }

        [UsedByIL]
        public static void ReadUInt32(BitPacker packer, uint oldvalue, ref uint value)
        {
            if (packer.ReadBit())
            {
                int diff = PackingIntegers.ZigzagDecode((uint)ReadDiff(packer, buckets32));
                value = (uint)(oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteInt64(BitPacker packer, long oldvalue, long newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            WriteDiff(packer, PackingIntegers.ZigzagEncode(newvalue - oldvalue), buckets64);
            return true;
        }

        [UsedByIL]
        public static void ReadInt64(BitPacker packer, long oldvalue, ref long value)
        {
            if (packer.ReadBit())
            {
                long diff = PackingIntegers.ZigzagDecode(ReadDiff(packer, buckets64));
                value = oldvalue + diff;
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt64(BitPacker packer, ulong oldvalue, ulong newvalue)
        {
            if (oldvalue == newvalue)
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            WriteDiff(packer, PackingIntegers.ZigzagEncode((long)(newvalue - oldvalue)), buckets64);
            return true;
        }

        [UsedByIL]
        public static void ReadUInt64(BitPacker packer, ulong oldvalue, ref ulong value)
        {
            if (packer.ReadBit())
            {
                long diff = PackingIntegers.ZigzagDecode(ReadDiff(packer, buckets64));
                value = (ulong)((long)oldvalue + diff);
            }
            else value = oldvalue;
        }

        [UsedByIL]
        public static bool WriteUInt32(BitPacker packer, PackedUInt oldvalue, PackedUInt newvalue)
        {
            return WriteUInt32(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadUInt32(BitPacker packer, PackedUInt oldvalue, ref PackedUInt value)
        {
            ReadUInt32(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static bool WriteInt64(BitPacker packer, PackedLong oldvalue, PackedLong newvalue)
        {
            return WriteInt64(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadInt64(BitPacker packer, PackedLong oldvalue, ref PackedLong value)
        {
            ReadInt64(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static bool WriteIndex(BitPacker packer, Size oldvalue, Size newvalue)
        {
            return WriteUInt32(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadIndex(BitPacker packer, Size oldvalue, ref Size value)
        {
            ReadUInt32(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static bool WriteUInt64(BitPacker packer, PackedULong oldvalue, PackedULong newvalue)
        {
            return WriteUInt64(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadUInt64(BitPacker packer, PackedULong oldvalue, ref PackedULong value)
        {
            ReadUInt64(packer, oldvalue.value, ref value.value);
        }

        [UsedByIL]
        public static bool WriteUInt16(BitPacker packer, PackedUShort oldvalue, PackedUShort newvalue)
        {
            return WriteUInt16(packer, oldvalue.value, newvalue.value);
        }

        [UsedByIL]
        public static void ReadUInt16(BitPacker packer, PackedUShort oldvalue, ref PackedUShort value)
        {
            ReadUInt16(packer, oldvalue.value, ref value.value);
        }
    }
}
