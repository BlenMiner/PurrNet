using System;
using PurrNet.Utils;
using UnityEngine;

namespace PurrNet
{
    [Serializable]
    public abstract class SyncAsset<T> : SyncBigData where T : UnityEngine.Object
    {
        [SerializeField, PurrLock] private T _assetToSync;

        private T _content;

        public T asset => _content;

        public event Action<T> onDataChanged;

        public T assetToSync
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    AssetChanged();
                }
            }
        }

        protected SyncAsset(bool ownerAuth = false, int maxKBPerSec = 15, bool ownerOnly = false) : base(ownerAuth, maxKBPerSec, ownerOnly) { }

        public abstract void FromBytes(ArraySegment<byte> bytes, ref T content);

        public abstract ArraySegment<byte> GetBytes(T content);

        private void AssetChanged()
        {
            if (!_content)
            {
                SetData(default);
                return;
            }

            SetData(GetBytes(_content));
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
