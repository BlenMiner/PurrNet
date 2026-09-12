using PurrNet.Packing;

namespace PurrNet.Modules
{
    internal sealed class NTEncodeCache
    {
        private struct Slot
        {
            public ushort baselineTick;
            public NetworkTransformVelocity baselineVelocity;
            public NetworkTransformVelocity derivedVelocity;
            public BitPacker bits;
        }

        private readonly Slot[] _slots = new Slot[3];
        private int _slotCount;
        private int _lastClaimed;
        private BitPacker _absolute;
        private bool _absoluteValid;
        private int _cachedTick = -1;

        public void BeginTick(ushort tick)
        {
            if (_cachedTick == tick)
                return;

            _cachedTick = tick;
            _slotCount = 0;
            _absoluteValid = false;
        }

        public bool TryGetDelta(ushort baselineTick, in NetworkTransformVelocity baselineVelocity,
            out BitPacker bits, out NetworkTransformVelocity derivedVelocity)
        {
            for (int i = 0; i < _slotCount; i++)
            {
                ref var slot = ref _slots[i];
                if (slot.baselineTick != baselineTick || !VelocityEquals(slot.baselineVelocity, baselineVelocity))
                    continue;

                bits = slot.bits;
                derivedVelocity = slot.derivedVelocity;
                return true;
            }

            bits = null;
            derivedVelocity = default;
            return false;
        }

        public BitPacker ClaimDeltaSlot(ushort baselineTick, in NetworkTransformVelocity baselineVelocity)
        {
            _lastClaimed = _slotCount < _slots.Length ? _slotCount++ : _slots.Length - 1;
            ref var slot = ref _slots[_lastClaimed];
            slot.baselineTick = baselineTick;
            slot.baselineVelocity = baselineVelocity;
            slot.bits ??= BitPackerPool.Get();
            slot.bits.ResetPositionAndMode(false);
            return slot.bits;
        }

        public void CompleteDeltaSlot(in NetworkTransformVelocity derivedVelocity)
        {
            _slots[_lastClaimed].derivedVelocity = derivedVelocity;
        }

        public BitPacker GetAbsolute(NetworkTransform nt)
        {
            if (!_absoluteValid)
            {
                _absolute ??= BitPackerPool.Get();
                _absolute.ResetPositionAndMode(false);
                nt.WriteAbsoluteState(_absolute);
                _absoluteValid = true;
            }

            return _absolute;
        }

        public void Dispose()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].bits?.Dispose();
                _slots[i].bits = null;
            }

            _absolute?.Dispose();
            _absolute = null;
            _slotCount = 0;
            _absoluteValid = false;
            _cachedTick = -1;
        }

        private static bool VelocityEquals(in NetworkTransformVelocity a, in NetworkTransformVelocity b)
        {
            return a.posX == b.posX && a.posY == b.posY && a.posZ == b.posZ &&
                   a.rotX == b.rotX && a.rotY == b.rotY && a.rotZ == b.rotZ && a.rotW == b.rotW &&
                   a.scaleX == b.scaleX && a.scaleY == b.scaleY && a.scaleZ == b.scaleZ;
        }
    }
}
