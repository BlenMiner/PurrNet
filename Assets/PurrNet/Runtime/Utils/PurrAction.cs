using System;
using System.Runtime.CompilerServices;

namespace PurrNet.Utils
{
    public sealed class PurrAction<T> where T : class
    {
        private const int MinNullsBeforeCompact = 8;

        private static readonly bool UseValueEquality = typeof(Delegate).IsAssignableFrom(typeof(T));

        public const int InvalidHandle = -1;

        private T[] _listeners;
        private int[] _slotToHandle;
        private int[] _handleToSlot;
        private int[] _freeHandles;

        private int _count;
        private int _nullCount;
        private int _handleCount;
        private int _freeHandleCount;
        private int _invokeDepth;
        private readonly Action<T> _invoke;

        public int count => _count - _nullCount;

        public PurrAction(Action<T> invoke, int capacity = 0)
        {
            _invoke = invoke;

            if (capacity > 0)
            {
                _listeners = new T[capacity];
                _slotToHandle = new int[capacity];
                _handleToSlot = new int[capacity];
                _freeHandles = new int[capacity];
            }
            else
            {
                _listeners = Array.Empty<T>();
                _slotToHandle = Array.Empty<int>();
                _handleToSlot = Array.Empty<int>();
                _freeHandles = Array.Empty<int>();
            }
        }

        public int Add(T listener)
        {
            if (listener == null)
                return InvalidHandle;

            if (_invokeDepth == 0 && ShouldCompact())
                Compact();

            if (_count == _listeners.Length)
                EnsureSlotCapacity();

            var handle = RentHandle();

            _listeners[_count] = listener;
            _slotToHandle[_count] = handle;
            _handleToSlot[handle] = _count;
            _count++;

            return handle;
        }

        /// <summary>
        /// Removes a listener by the handle <see cref="Add"/> returned. O(1) when the handle
        /// still points at <paramref name="listener"/>, otherwise falls back to <see cref="Remove"/>.
        /// </summary>
        public void RemoveAt(int handle, T listener)
        {
            if (listener == null)
                return;

            if ((uint)handle < (uint)_handleCount)
            {
                var slot = _handleToSlot[handle];

                if ((uint)slot < (uint)_count && Matches(_listeners[slot], listener))
                {
                    FreeSlot(slot);
                    return;
                }
            }

            Remove(listener);
        }

        public void Remove(T listener)
        {
            if (listener == null)
                return;

            var listeners = _listeners;

            for (var i = _count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(listeners[i], listener))
                    continue;

                FreeSlot(i);
                return;
            }

            if (!UseValueEquality)
                return;

            for (var i = _count - 1; i >= 0; i--)
            {
                var current = listeners[i];
                if (current == null || !current.Equals(listener))
                    continue;

                FreeSlot(i);
                return;
            }
        }

        public void Invoke()
        {
            var count = _count;
            _invokeDepth++;

            try
            {
                for (var i = 0; i < count; i++)
                {
                    var listener = _listeners[i];
                    if (listener != null)
                        _invoke(listener);
                }
            }
            finally
            {
                if (--_invokeDepth == 0 && ShouldCompact())
                    Compact();
            }
        }

        /// <summary>Force reclamation of removed slots. No-op during dispatch.</summary>
        public void CompactNow()
        {
            if (_invokeDepth == 0 && _nullCount > 0)
                Compact();
        }

        public void Clear()
        {
            if (_invokeDepth > 0)
            {
                for (var i = 0; i < _count; i++)
                {
                    if (_listeners[i] != null)
                        FreeSlot(i);
                }
                return;
            }

            for (var i = 0; i < _count; i++)
            {
                var handle = _slotToHandle[i];
                if (handle != InvalidHandle)
                    _handleToSlot[handle] = InvalidHandle;
            }

            Array.Clear(_listeners, 0, _count);
            _count = 0;
            _nullCount = 0;
            _handleCount = 0;
            _freeHandleCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Matches(T listener, T other)
        {
            if (ReferenceEquals(listener, other))
                return true;

            return UseValueEquality && listener != null && listener.Equals(other);
        }

        private void FreeSlot(int slot)
        {
            var handle = _slotToHandle[slot];

            if (handle != InvalidHandle)
            {
                _handleToSlot[handle] = InvalidHandle;
                ReturnHandle(handle);
            }

            _listeners[slot] = null;
            _slotToHandle[slot] = InvalidHandle;
            _nullCount++;
        }

        private int RentHandle()
        {
            if (_freeHandleCount > 0)
                return _freeHandles[--_freeHandleCount];

            if (_handleCount == _handleToSlot.Length)
            {
                var size = _handleToSlot.Length == 0 ? 4 : _handleToSlot.Length * 2;
                Array.Resize(ref _handleToSlot, size);
                Array.Resize(ref _freeHandles, size);
            }

            return _handleCount++;
        }

        private void ReturnHandle(int handle)
        {
            _freeHandles[_freeHandleCount++] = handle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldCompact()
        {
            return _nullCount >= MinNullsBeforeCompact && _nullCount * 2 >= _count;
        }

        private void EnsureSlotCapacity()
        {
            if (_invokeDepth == 0 && _nullCount > 0)
            {
                Compact();

                if (_count < _listeners.Length)
                    return;
            }

            GrowSlots();
        }

        private void GrowSlots()
        {
            var size = _listeners.Length == 0 ? 4 : _listeners.Length * 2;
            Array.Resize(ref _listeners, size);
            Array.Resize(ref _slotToHandle, size);
        }

        private void Compact()
        {
            var listeners = _listeners;
            var slotToHandle = _slotToHandle;
            var count = _count;
            var write = 0;

            for (var read = 0; read < count; read++)
            {
                var listener = listeners[read];
                if (listener == null)
                    continue;

                var handle = slotToHandle[read];

                listeners[write] = listener;
                slotToHandle[write] = handle;
                _handleToSlot[handle] = write;
                write++;
            }

            Array.Clear(listeners, write, count - write);
            _count = write;
            _nullCount = 0;
        }
    }
}
