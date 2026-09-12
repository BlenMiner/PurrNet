using System;
using System.Collections.Generic;

namespace PurrNet
{
    public delegate T LerpFunction<T>(T from, T to, float t);

    [DontPack]
    public class Interpolated<T>
    {
        private LerpFunction<T> _lerp;
        private readonly List<T> _buffer;
        private T _lastValue;
        private float _timer;
        private float _tickDelta;
        private float _idleTime;
        protected bool _waitForMinBufferSize;

        public int bufferSize => _buffer.Count;

        public float tickDelta
        {
            get => _tickDelta;
            set
            {
                if (value <= 0f)
                    throw new ArgumentException("TickDelta must be greater than 0", nameof(value));
                _tickDelta = value;
            }
        }

        public int maxBufferSize { get; set; }

        public int minBufferSize { get; set; }

        public Interpolated(LerpFunction<T> lerp, float tickDelta, T initialValue = default, int maxBufferSize = 2, int minBufferSize = 1)
        {
            _lerp = lerp ?? throw new ArgumentNullException(nameof(lerp));

            if (tickDelta <= 0f)
                throw new ArgumentException("tickDelta must be greater than 0", nameof(tickDelta));

            _buffer = new List<T>(maxBufferSize);

            this.maxBufferSize = maxBufferSize;
            this.minBufferSize = minBufferSize;

            _tickDelta = tickDelta;
            _lastValue = initialValue;
            _waitForMinBufferSize = true;
        }

        public void Reset(LerpFunction<T> lerp, float tickDelta, T initialValue = default, int maxBufferSize = 2, int minBufferSize = 1)
        {
            _lerp = lerp ?? throw new ArgumentNullException(nameof(lerp));

            if (tickDelta <= 0f)
                throw new ArgumentException("tickDelta must be greater than 0", nameof(tickDelta));

            if (_buffer.Capacity < maxBufferSize)
                _buffer.Capacity = maxBufferSize;
            _buffer.Clear();

            this.maxBufferSize = maxBufferSize;
            this.minBufferSize = minBufferSize;

            _tickDelta = tickDelta;
            _lastValue = initialValue;
            _timer = 0f;
            _idleTime = 0f;
            _waitForMinBufferSize = true;
        }

        public void Add(T value)
        {
            _idleTime = 0f;

            if (_buffer.Count >= maxBufferSize)
            {
                // Rebase on the rendered value, else the restarted lerp snaps back mid-segment.
                _lastValue = _lerp(_lastValue, _buffer[0], _timer / _tickDelta);
                var removeCount = _buffer.Count - minBufferSize;
                _buffer.RemoveRange(0, removeCount);
                _timer = 0f;
            }
            _buffer.Add(value);
        }

        public void Teleport(T value)
        {
            _lastValue = value;
            _buffer.Clear();
            _timer = 0f;
        }

        public T Advance(float deltaTime)
        {
            if (_waitForMinBufferSize)
            {
                if (_buffer.Count < minBufferSize)
                {
                    _idleTime += deltaTime;

                    bool starved = _buffer.Count > 0 && _idleTime >= _tickDelta * 2f;

                    if (!starved)
                    {
                        _timer = 0f;
                        return _lerp(_lastValue, _lastValue, 1f);
                    }
                }

                _waitForMinBufferSize = false;
            }

            if (_buffer.Count <= 0)
            {
                _timer = 0f;
                _waitForMinBufferSize = true;
                return _lerp(_lastValue, _lastValue, 1f);
            }

            _timer += deltaTime;

            while (_timer >= _tickDelta)
            {
                _lastValue = _buffer[0];
                _buffer.RemoveAt(0);
                _timer -= _tickDelta;

                if (_buffer.Count <= 0)
                {
                    _timer = 0f;
                    _waitForMinBufferSize = true;
                    return _lerp(_lastValue, _lastValue, 1f);
                }
            }

            return _lerp(_lastValue, _buffer[0], _timer / _tickDelta);
        }
    }
}
