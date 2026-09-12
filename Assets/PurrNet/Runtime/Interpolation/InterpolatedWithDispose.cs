using System;
using System.Collections.Generic;

namespace PurrNet
{
    public class InterpolatedWithDispose<T> where T : IDisposable
    {
        private readonly LerpFunction<T> _lerp;
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

        public InterpolatedWithDispose(
            LerpFunction<T> lerp,
            float tickDelta,
            T initialValue = default,
            int maxBufferSize = 2,
            int minBufferSize = 1
        )
        {
            _lerp = lerp ?? throw new ArgumentNullException(nameof(lerp));

            if (tickDelta <= 0f)
                throw new ArgumentException("tickDelta must be greater than 0", nameof(tickDelta));

            _buffer = new List<T>(Math.Max(1, maxBufferSize));

            this.maxBufferSize = Math.Max(1, maxBufferSize);
            this.minBufferSize = Math.Max(0, Math.Min(minBufferSize, this.maxBufferSize - 1));

            _tickDelta = tickDelta;
            _lastValue = initialValue;
            _waitForMinBufferSize = true;
        }

        public void Add(T value)
        {
            _idleTime = 0f;

            if (_buffer.Count >= maxBufferSize)
            {
                var removeCount = _buffer.Count - minBufferSize;
                if (removeCount > 0)
                {
                    // Commit the dropped span so the restarted lerp cannot snap backwards.
                    // Lerp results are caller-owned, so rebasing on one is not an option here.
                    _lastValue?.Dispose();
                    for (int i = 0; i < removeCount - 1; i++)
                        _buffer[i].Dispose();
                    _lastValue = _buffer[removeCount - 1];

                    _buffer.RemoveRange(0, removeCount);
                    _timer = 0f;
                }
            }

            _buffer.Add(value);
        }

        public void Teleport(T value)
        {
            _lastValue?.Dispose();
            _lastValue = value;

            for (int i = 0; i < _buffer.Count; i++)
                _buffer[i].Dispose();

            _buffer.Clear();
            _timer = 0f;
            _waitForMinBufferSize = true;
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

            while (_timer >= _tickDelta && _buffer.Count > 0)
            {
                // We are moving to the next committed value.
                // Dispose previous _lastValue (it’s no longer needed).
                _lastValue?.Dispose();

                // Pop the next state from buffer and set as new _lastValue
                var next = _buffer[0];
                _buffer.RemoveAt(0);
                _lastValue = next;

                _timer -= _tickDelta;

                if (_buffer.Count <= 0)
                {
                    _timer = 0f;
                    _waitForMinBufferSize = true;
                    // No "to" state; hold on the last committed value.
                    return _lerp(_lastValue, _lastValue, 1f);
                }
            }

            return _lerp(_lastValue, _buffer[0], _timer / _tickDelta);
        }
    }
}
