using System;
using System.IO;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [Serializable]
    public abstract class SyncFile<T> : SyncBigData
    {
        [SerializeField, PurrLock] private string _filePath;

        private T _content;

        public T content => _content;

        public event Action<T> onDataChanged;

        public override void OnPoolReset()
        {
            base.OnPoolReset();
            _content = default;
            onDataChanged = null;
        }

        public string filePath
        {
            get => _filePath;
            set
            {
                var trimmed = value?.Trim('"');
                // Pool reset keeps the configured path but clears its loaded payload.
                if (_filePath != trimmed || progress == 0f)
                {
                    _filePath = trimmed;
                    FilePathChanged();
                }
            }
        }

        protected SyncFile(bool ownerAuth = false, int maxKBPerSec = 15, bool ownerOnly = false) : base(ownerAuth, maxKBPerSec, ownerOnly) { }

        public abstract void FromBytes(ArraySegment<byte> bytes, ref T content);

        private void FilePathChanged()
        {
            if (!File.Exists(_filePath))
            {
                SetData(default);
                return;
            }

            SetData(File.ReadAllBytes(_filePath));
        }

        protected override void OnDataReady()
        {
            if (data.Count == 0)
            {
                _content = default;
                onDataChanged?.Invoke(_content);
                return;
            }

            FromBytes(data, ref _content);
            onDataChanged?.Invoke(_content);
        }
    }
}
