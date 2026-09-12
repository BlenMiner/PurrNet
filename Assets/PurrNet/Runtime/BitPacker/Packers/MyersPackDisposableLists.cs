using System.Runtime.CompilerServices;
using PurrNet.Modules;
using PurrNet.Pooling;

namespace PurrNet.Packing
{
    public static class MyersPackDisposableLists
    {
        [UsedByIL]
        public static bool WriteDisposableDeltaList<T>(BitPacker packer, DisposableList<T> old, DisposableList<T> value)
        {
            var scope = new DeltaWritingScope(packer);

            if (old.Equals(value))
                return scope.Complete();

            if (value.isDisposed)
            {
                scope.Write<bool>(false);
                return scope.Complete();
            }

            scope.Write<bool>(true);

            DisposableList<DiffOp<T>> changes;

            if (old.isDisposed)
            {
                using var tmp = DisposableList<T>.Create();
                changes = MyersDiff.Diff(tmp.rawList, value.rawList);
            }
            else changes = MyersDiff.Diff(old.rawList, value.rawList);

            if (changes.Count > 0)
            {
                int count = changes.Count;
                for (int i = 0; i < count; i++)
                    scope.Write<DiffOp<T>>(changes[i]);
            }

            scope.Write(DiffOp<T>.FinalOperation());

            var result = scope.Complete();

            for (int i = 0; i < changes.Count; i++)
                changes[i].values.Dispose();
            changes.Dispose();

            return result;
        }

        [UsedByIL]
        public static void ReadDisposableDeltaList<T>(BitPacker packer, DisposableList<T> old, ref DisposableList<T> value)
        {
            if (!DeltaReadingScope.Continue(packer, old, ref value))
                return;

            if (!packer.ReadBit())
            {
                value.Dispose();
                return;
            }

            if (value.isDisposed)
            {
                value = DisposableList<T>.Create();
            }
            else if (!old.isDisposed && old.rawList == value.rawList)
            {
                value = DisposableList<T>.Create();
            }

            if (!old.isDisposed)
            {
                value.Clear();
                if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                {
                    for (int i = 0; i < old.Count; i++)
                        value.Add(PurrCopy<T>.Copy(old[i]));
                }
                else value.AddRange(old);
            }

            var changes = DisposableList<DiffOp<T>>.Create();
            while (true)
            {
                var operation = Packer<DiffOp<T>>.Read(packer);
                if (operation.type == OperationType.End)
                {
                    operation.Dispose();
                    break;
                }
                changes.Add(operation);
            }

            if (changes.Count > 0)
            {
                MyersDiff.Apply(value, changes);
                for (var i = 0; i < changes.Count; i++)
                    changes[i].Dispose();
            }

            changes.Dispose();
        }
    }
}
