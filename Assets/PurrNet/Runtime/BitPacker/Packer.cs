using System;
using System.Collections.Generic;
using System.Reflection;
using PurrNet.Logging;
using PurrNet.Utils;

namespace PurrNet.Packing
{
    public delegate void DeltaWriteFunc<in T>(BitPacker packer, T oldValue, T newValue);

    public delegate void DeltaReadFunc<T>(BitPacker packer, T oldValue, ref T value);
    
    public delegate void WriteFunc<in T>(BitPacker packer, T value);

    public delegate void ReadFunc<T>(BitPacker packer, ref T value);

    public static class DeltaPacker<T>
    {
        static DeltaWriteFunc<T> _write;
        static DeltaReadFunc<T> _read;
        
        public static void RegisterWriter(DeltaWriteFunc<T> a)
        {
            if (_write != null)
                return;
            
            Packer.RegisterWriter(typeof(T), a.Method);
            _write = a;
        }
        
        public static void RegisterReader(DeltaReadFunc<T> b)
        {
            if (_read != null)
                return;

            Packer.RegisterReader(typeof(T), b.Method);
            _read = b;
        }
        
        public static void Write(BitPacker packer, T oldValue, T newValue)
        {
            try
            {
                if (_write == null)
                {
                    PurrLogger.LogError($"No writer for type '{typeof(T)}' is registered.");
                    return;
                }
                
                _write(packer, oldValue, newValue);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }
        
        public static void Read(BitPacker packer, T oldValue, ref T value)
        {
            try
            {
                if (_read == null)
                {
                    PurrLogger.LogError($"No reader for type '{typeof(T)}' is registered.");
                    return;
                }
                
                _read(packer, oldValue, ref value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }
        
        public static void Serialize(BitPacker packer, T oldValue, ref T value)
        {
            if (packer.isWriting)
                 Write(packer, oldValue, value);
            else Read(packer, oldValue, ref value);
        }
    }
    
    public static class Packer<T>
    {
        static WriteFunc<T> _write;
        static ReadFunc<T> _read;

        public static void RegisterWriter(WriteFunc<T> a)
        {
            if (_write != null)
                return;
            
            Packer.RegisterWriter(typeof(T), a.Method);
            _write = a;
        }
        
        public static void RegisterReader(ReadFunc<T> b)
        {
            if (_read != null)
                return;

            Packer.RegisterReader(typeof(T), b.Method);
            _read = b;
        }
        
        public static void Write(BitPacker packer, T value)
        {
            try
            {
                if (_write == null)
                {
                    PurrLogger.LogError($"No writer for type '{typeof(T)}' is registered.");
                    return;
                }
                
                _write(packer, value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }
        
        public static void Read(BitPacker packer, ref T value)
        {
            try
            {
                if (_read == null)
                {
                    PurrLogger.LogError($"No reader for type '{typeof(T)}' is registered.");
                    return;
                }
                
                _read(packer, ref value);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to read value of type '{typeof(T)}'.\n{e.Message}\n{e.StackTrace}");
            }
        }
        
        public static void Serialize(BitPacker packer, ref T value)
        {
            if (packer.isWriting)
                 Write(packer, value);
            else Read(packer, ref value);
        }
    }

    public static class Packer
    {
        static readonly Dictionary<Type, MethodInfo> _writeMethods = new Dictionary<Type, MethodInfo>();
        static readonly Dictionary<Type, MethodInfo> _readMethods = new Dictionary<Type, MethodInfo>();
        
        public static void RegisterWriter(Type type, MethodInfo method)
        {
            if (!_writeMethods.TryAdd(type, method))
                PurrLogger.LogError($"Writer for type '{type}' is already registered.");
        }
        
        public static void RegisterReader(Type type, MethodInfo method)
        {
            if (!_readMethods.TryAdd(type, method))
            {
                PurrLogger.LogError($"Reader for type '{type}' is already registered.");
            }
            else
            {
                Hasher.PrepareType(type);
            }
        }
        
        static readonly object[] _args = new object[2];
        
        public static void Write(BitPacker packer, object value)
        {
            var type = value.GetType();
            
            if (!_writeMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No writer for type '{type}' is registered.");
                return;
            }
            
            try
            {
                _args[0] = packer;
                _args[1] = value;
                method.Invoke(null, _args);
            }
            catch (Exception e)
            {
                PurrLogger.LogError($"Failed to write value of type '{type}'.\n{e.Message}\n{e.StackTrace}");
            }
        }
        
        public static void Read(BitPacker packer, Type type, ref object value)
        {
            if (!_readMethods.TryGetValue(type, out var method))
            {
                PurrLogger.LogError($"No reader for type '{type}' is registered.");
                return;
            }
            
            try
            {
                _args[0] = packer;
                _args[1] = value;
                method.Invoke(null, _args);
                value = _args[1];
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
