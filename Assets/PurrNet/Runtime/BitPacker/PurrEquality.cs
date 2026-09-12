using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PurrNet.Modules;
using Unity.Collections.LowLevel.Unsafe;

namespace PurrNet.Packing
{
    [UsedByIL]
    public static class PurrEquality
    {
        [UsedByIL]
        public static void Override<D>() where D : IPurrEquatable<D>
        {
            PurrEquality<D>.OverrideDefault(new PurrEqualityComparer<D>());
        }

        private sealed class PurrEqualityComparer<D> : IEqualityComparer<D> where D : IPurrEquatable<D>
        {
            public bool Equals(D x, D y)
            {
                if (!typeof(D).IsValueType)
                {
                    if (x is null) return y is null;
                    if (y is null) return false;
                }
                return x.PurrEquals(y);
            }

            public int GetHashCode(D obj) => EqualityComparer<D>.Default.GetHashCode(obj);
        }
    }

    public static class PurrEquality<T>
    {
        public static IEqualityComparer<T> Default;

        static PurrEquality()
        {
            Default = EqualityComparer<T>.Default;
        }

        public static void OverrideDefault(IEqualityComparer<T> comparer)
        {
            Default = comparer;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static unsafe bool MemEquals(ref T a, ref T b)
        {
            return UnsafeUtility.MemCmp(
                Unsafe.AsPointer(ref a),
                Unsafe.AsPointer(ref b),
                Unsafe.SizeOf<T>()
            ) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), UsedByIL]
        public static bool Equals(T a, T b)
        {
            if (!RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                return MemEquals(ref a, ref b);
            return Default.Equals(a, b);
        }
    }
}
