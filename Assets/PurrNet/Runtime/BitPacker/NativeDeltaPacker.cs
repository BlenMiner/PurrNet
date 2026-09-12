using System;
using System.Runtime.CompilerServices;
using PurrNet.Modules;
#if PURR_DELTA_CHECK
using PurrNet.Logging;
#endif

namespace PurrNet.Packing
{
    public static class NativeDeltaPacker<T>
    {
        public static unsafe delegate*<BitPacker, T, T, bool> WriteFunc;
        public static unsafe delegate*<BitPacker, T, ref T, void> ReadFunc;

        static bool _hasWriter, _hasReader;

        static DeltaWriteFunc<T> _writeDelegate;
        static DeltaReadFunc<T> _readDelegate;

        static unsafe NativeDeltaPacker()
        {
            WriteFunc = &DeltaPacker.FallbackWriter;
            ReadFunc = &DeltaPacker.FallbackReader;
        }

        public static void Register(DeltaWriteFunc<T> write, DeltaReadFunc<T> read)
        {
            RegisterWriter(write);
            RegisterReader(read);
        }

        public static bool HasPacker()
        {
            return _hasWriter && _hasReader;
        }

        static bool WriteDelegateFallback(BitPacker packer, T oldValue, T newValue)
        {
            return _writeDelegate(packer, oldValue, newValue);
        }

        static void ReadDelegateFallback(BitPacker packer, T oldValue, ref T value)
        {
            _readDelegate(packer, oldValue, ref value);
        }

        public static unsafe void RegisterWriter(DeltaWriteFunc<T> write)
        {
            if (_hasWriter || RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return;

#if ENABLE_IL2CPP && !UNITY_EDITOR
            // IL2CPP does not support GetFunctionPointer; WebGL may have exceptions disabled.
            _writeDelegate = write;
            RegisterWriterWithPointer(write, &WriteDelegateFallback);
#else
            try
            {
                var ptr = (delegate*<BitPacker, T, T, bool>)write.Method.MethodHandle.GetFunctionPointer();
                RegisterWriterWithPointer(write, ptr);
            }
            catch (NotSupportedException)
            {
                _writeDelegate = write;
                RegisterWriterWithPointer(write, &WriteDelegateFallback);
            }
#endif
        }

        static unsafe void RegisterWriterWithPointer(DeltaWriteFunc<T> write, delegate*<BitPacker, T, T, bool> ptr)
        {
            if (_hasWriter)
                return;

            _hasWriter = true;
            DeltaPacker.RegisterWriter(typeof(T), write.Method);
            WriteFunc = ptr;
        }

        public static unsafe void RegisterReader(DeltaReadFunc<T> read)
        {
            if (_hasReader || RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return;

#if ENABLE_IL2CPP && !UNITY_EDITOR
            _readDelegate = read;
            RegisterReaderWithPointer(read, &ReadDelegateFallback);
#else
            try
            {
                var ptr = (delegate*<BitPacker, T, ref T, void>)read.Method.MethodHandle.GetFunctionPointer();
                RegisterReaderWithPointer(read, ptr);
            }
            catch (NotSupportedException)
            {
                _readDelegate = read;
                RegisterReaderWithPointer(read, &ReadDelegateFallback);
            }
#endif
        }

        public static unsafe void RegisterReaderWithPointer(DeltaReadFunc<T> b, delegate*<BitPacker, T, ref T, void> ptr)
        {
            if (_hasReader)
                return;

            _hasReader = true;
            DeltaPacker.RegisterReader(typeof(T), b.Method);
            ReadFunc = ptr;
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool WriteUnpacked(BitPacker packer, T oldValue, T newValue)
        {
            if (Packer.AreEqual(oldValue, newValue))
            {
                packer.WriteBit(false);
                return false;
            }

            packer.WriteBit(true);
            Packer<T>.WriteFunc(packer, newValue);
            return true;
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadUnpacked(BitPacker packer, T oldValue, ref T value)
        {
            if (!packer.ReadBit())
            {
                value = oldValue;
                return;
            }

            Packer<T>.ReadFunc(packer, ref value);
        }

#if !PURR_DELTA_CHECK
        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static unsafe bool Write(BitPacker packer, T oldValue, T newValue)
        {
#if PURR_DELTA_CHECK
            Packer<T>.Write(packer, oldValue);
            Packer<T>.Write(packer, newValue);
            int sizePos = packer.AdvanceBits(32);

            int bits = packer.positionInBits;
            var changed = WriteFunc(packer, oldValue, newValue);
            int endPos = packer.positionInBits;

            packer.SetBitPosition(sizePos);
            Packer<int>.Write(packer, endPos - bits);
            packer.SetBitPosition(endPos);
            return changed;
#else
            return WriteFunc(packer, oldValue, newValue);
#endif
        }

#if !PURR_DELTA_CHECK
        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static unsafe void Read(BitPacker packer, T oldValue, ref T value)
        {
#if PURR_DELTA_CHECK
            var shouldBeOld = Packer<T>.Read(packer);
            var shouldBeNew = Packer<T>.Read(packer);
            var shouldReadCount = Packer<int>.Read(packer);

            int startPos = packer.positionInBits;

            ReadFunc(packer, oldValue, ref value);

            if (!Packer.AreEqual(shouldBeOld, oldValue))
                PurrLogger.LogError($"<{typeof(T)}> old value `{oldValue}` is not equal to the one that was used to write the delta `{shouldBeOld}`.");

            if (!Packer.AreEqual(shouldBeNew, value))
                PurrLogger.LogError($"<{typeof(T)}> New value `{value}` is not equal to the one that was used to write the delta `{shouldBeNew}`.");

            int readCount = packer.positionInBits - startPos;
            if (shouldReadCount != readCount)
            {
                PurrLogger.LogError($"<{typeof(T)}> Delta read count `{readCount}` is not equal to the actual read count `{shouldReadCount}`.");
                packer.SetBitPosition(startPos + shouldReadCount);
            }
#else
            ReadFunc(packer, oldValue, ref value);
#endif
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Serialize(BitPacker packer, T oldValue, ref T value)
        {
            if (packer.isWriting)
                WriteFunc(packer, oldValue, value);
            else ReadFunc(packer, oldValue, ref value);
        }
    }
}
