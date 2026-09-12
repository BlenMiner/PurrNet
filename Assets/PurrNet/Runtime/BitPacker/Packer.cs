using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PurrNet.Packing
{
    public delegate bool DeltaWriteFunc<in T>(BitPacker packer, T oldValue, T newValue);

    public delegate void DeltaReadFunc<T>(BitPacker packer, T oldValue, ref T value);

    public delegate void WriteFunc<in T>(BitPacker packer, T value);

    public delegate void ReadFunc<T>(BitPacker packer, ref T value);

    public static class Packer<T>
    {
        /// <summary>
        /// The function that writes type T.
        /// It's direct because it doesn't care for inheritance.
        /// </summary>
        public static WriteFunc<T> DirectWrite;

        /// <summary>
        /// The function that reads type T.
        /// It's direct because it doesn't care for inheritance.
        /// </summary>
        public static ReadFunc<T> DirectRead;

        /// <summary>
        /// The function that writes type T.
        /// It cares for inheritance, if T isn't the top level type it will use the top level type's writer.
        /// </summary>
        public static WriteFunc<T> WriteFunc;

        /// <summary>
        /// The function that reads type T.
        /// It cares for inheritance, if T isn't the top level type it will use the top level type's reader.
        /// </summary>
        public static ReadFunc<T> ReadFunc;

        static bool _hasWriter, _hasReader;

        static Packer()
        {
            WriteFunc = Packer.FallbackWriter;
            ReadFunc = Packer.FallbackReader;
            DirectWrite = Packer.FallbackWriter;
            DirectRead = Packer.FallbackReader;
        }

        public static void RegisterWriter(WriteFunc<T> a)
        {
            if (_hasWriter)
                return;

            _hasWriter = true;
            DirectWrite = a;

            bool isStructOrSealed = typeof(T).IsValueType || typeof(T).IsSealed;
            WriteFunc = !isStructOrSealed ? WriteClass : DirectWrite;

            Packer.RegisterWriter(typeof(T), DirectWrite.Method, WriteFunc.Method);
            NativePacker<T>.RegisterWriter(a);
        }

        public static bool HasPacker() => _hasWriter && _hasReader;

        public static void RegisterReader(ReadFunc<T> b)
        {
            Hasher.PrepareType(typeof(T));

            if (_hasReader)
                return;

            _hasReader = true;
            DirectRead = b;

            bool isStructOrSealed = typeof(T).IsValueType || typeof(T).IsSealed;
            ReadFunc = !isStructOrSealed ? ReadClass : DirectRead;

            Packer.RegisterReader(typeof(T), DirectRead.Method, ReadFunc.Method);
            NativePacker<T>.RegisterReader(b);
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteAsExactType(BitPacker packer, T value) => DirectWrite(packer, value);

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ReadAsExactType(BitPacker packer, ref T value) => DirectRead(packer, ref value);

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Write(BitPacker packer, T value) => WriteFunc(packer, value);

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Read(BitPacker packer, ref T value) => ReadFunc(packer, ref value);

        static void WriteClass(BitPacker packer, T value)
        {
            Type type;

            if (value == null)
            {
                type = typeof(T);
            }
            else
            {
                var vtype = value.GetType();
                type = Hasher.IsRegistered(vtype) ? vtype : typeof(T);
            }

            bool isTypeSameAsGeneric = type == typeof(T);

            packer.WriteBit(isTypeSameAsGeneric);

            if (isTypeSameAsGeneric)
            {
                DirectWrite(packer, value);
                return;
            }

            packer.Write(Hasher.GetStableHashU32(type));
            Packer.WriteAsExactType(packer, type, value);
        }

        static void ReadClass(BitPacker packer, ref T value)
        {
            bool isTypeSameAsGeneric = Packer<bool>.Read(packer);

            if (isTypeSameAsGeneric)
            {
                DirectRead(packer, ref value);
                return;
            }

            uint hash = default;
            packer.Read(ref hash);

            if (!Hasher.TryGetType(hash, out var type))
            {
                Hasher.PrintHashError(hash);
                value = default;
                return;
            }

            object result = value;
            Packer.ReadAsExactType(packer, type, ref result);

            switch (result)
            {
                case null:
                    value = default;
                    break;
                case T cast:
                    value = cast;
                    break;
                default:
                    PurrLogger.LogError($"While reading `{type}`, we got `{result.GetType()}` which does not match expected type `{typeof(T)}`.");
                    value = default;
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Read(BitPacker packer)
        {
            var value = default(T);
            ReadFunc(packer, ref value);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Serialize(BitPacker packer, ref T value)
        {
            if (packer.isWriting)
            {
                WriteFunc(packer, value);
            }
            else
            {
                ReadFunc(packer, ref value);
            }
        }
    }

    public static class Packer
    {
        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T Copy<T>(in T value)
        {
            return PurrCopy<T>.Copy(value);
        }

        /// <summary>
        /// Modifies `target` to become `whatToCopy` without re-creating it.
        /// </summary>
        /// <returns>
        /// If a modification happened
        /// </returns>
        public static bool Transform<T>(ref T target, T whatToCopy)
        {
            if (PurrEquality<T>.Equals(target, whatToCopy))
                return false;

            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                target = whatToCopy;
                return true;
            }

            using var packerB = BitPackerPool.Get();
            Packer<T>.Write(packerB, whatToCopy);
            packerB.ResetPositionAndMode(true);

            if (target?.GetType() != whatToCopy?.GetType())
                target = default;

            Packer<T>.Read(packerB, ref target);
            return true;
        }

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreEqual<T>(T a, T b) => PurrEquality<T>.Equals(a, b);

        [UsedByIL, MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AreEqualRef<T>(ref T a, ref T b)
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return PurrEquality<T>.MemEquals(ref a, ref b);
            return PurrEquality<T>.Default.Equals(a, b);
        }

        static readonly Dictionary<Type, MethodInfo> _writeExactMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _writeWrappedMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _readExactMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _readWrappedMethods = new Dictionary<Type, MethodInfo>();

        public static void RegisterWriter(Type type, MethodInfo exact, MethodInfo wrapper)
        {
            _writeWrappedMethods.TryAdd(type, wrapper);
            _writeExactMethods.TryAdd(type, exact);
        }

        public static void RegisterReader(Type type, MethodInfo exact, MethodInfo wrapper)
        {
            _readWrappedMethods.TryAdd(type, wrapper);
            _readExactMethods.TryAdd(type, exact);
        }

        public static void FallbackWriter<T>(BitPacker packer, T value)
        {
            bool hasValue = value != null;
            packer.WriteBit(hasValue);

            if (!hasValue) return;

            object obj = value;

            if (obj is Object unityObj)
            {
                if (!unityObj)
                {
                    packer.SetBitPosition(packer.positionInBits - 1);
                    packer.WriteBit(false);
                    return;
                }

                if (WriteAsNetworkAsset(packer, unityObj))
                    return;
            }
            else packer.WriteBit(false);

            uint typeHash = Hasher.GetStableHashU32(obj.GetType());
            PackUIntegers.Write(packer, typeHash);
            WriteRawObject(obj, packer);
        }

        public static bool WriteAsNetworkAsset(BitPacker packer, Object unityObj)
        {
            var manager = NetworkManager.main;
            var id = NetworkAssetID.invalid;
            bool isNetworkAsset = manager && manager.networkAssetResolver.TryGetId(
                unityObj, NetworkAssetResolver.serializationSceneHint, out id, true);
            packer.WriteBit(isNetworkAsset);

            if (isNetworkAsset)
            {
                Packer<NetworkAssetID>.Write(packer, id);
                return true;
            }

            return false;
        }

        public static void FallbackReader<T>(BitPacker packer, ref T value)
        {
            try
            {
                bool hasValue = default;
                Packer<bool>.Read(packer, ref hasValue);

                if (!hasValue)
                {
                    value = default;
                    return;
                }

                if (ReadAsNetworkAsset(packer, ref value))
                    return;

                uint typeHash = default;
                NativePacker<uint>.Read(packer, ref typeHash);
                var type = Hasher.ResolveType(typeHash);

                object obj = null;
                ReadRawObject(type, packer, ref obj);

                if (obj is T entity)
                    value = entity;
                else value = default;
            }
            catch (Exception e)
            {
                PurrLogger.LogError(
                    $"Failed to read value of type '{typeof(T)}' when using fallback reader.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static bool ReadAsNetworkAsset<T>(BitPacker packer, ref T value)
        {
            bool isNetworkAsset = Packer<bool>.Read(packer);

            var manager = NetworkManager.main;
            if (isNetworkAsset && manager)
            {
                var id = Packer<NetworkAssetID>.Read(packer);
                value = manager.networkAssetResolver.TryGetAsset(id, out var asset) && asset is T cast ? cast : default;
                return true;
            }

            return false;
        }

        public static void WriteAsExactType<T>(BitPacker packer, Type type, T value)
        {
            if (!_writeExactMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No writer for type '{type}' is registered.");
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                if (e.InnerException != null)
                    Debug.LogException(e);
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Write(BitPacker packer, Type type, object value)
        {
            if (!_writeWrappedMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No writer for type '{type}' is registered.");
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Write(BitPacker packer, object value)
        {
            var type = value.GetType();

            if (!_writeWrappedMethods.TryGetValue(type, out var method))
            {
                FallbackWriter(packer, value);
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        static void WriteRawObject(object value, BitPacker packer)
        {
            var type = value.GetType();

            if (!_writeExactMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No writer for type '{type}' is registered.");
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Read(BitPacker packer, Type type, ref object value)
        {
            if (!_readWrappedMethods.TryGetValue(type, out var method))
            {
                FallbackReader(packer, ref value);
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                value = args[1];
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{type}'.\n{e.InnerException ?? e}");
            }
        }

        public static void ReadAsExactType(BitPacker packer, Type type, ref object value)
        {
            if (!_readExactMethods.TryGetValue(type, out var method))
            {
                FallbackReader(packer, ref value);
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                value = args[1];
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{type}'.\n{e.InnerException ?? e}");
            }
        }

        public static void ReadRawObject(Type type, BitPacker packer, ref object value)
        {
            if (!_readExactMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No reader for type '{type}' is registered.");
                return;
            }

            try
            {
                var args = PreciseArrayPool<object>.Rent(2);
                args[0] = packer;
                args[1] = value;
                method.Invoke(null, args);
                value = args[1];
                PreciseArrayPool<object>.Return(args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }

        public static void Serialize(BitPacker packer, Type type, ref object value)
        {
            if (packer.isWriting)
                Write(packer, value);
            else Read(packer, type, ref value);
        }
    }
}
