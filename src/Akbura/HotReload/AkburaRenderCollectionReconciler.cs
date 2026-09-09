using Avalonia.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Akbura.HotReload;

internal static class AkburaRenderCollectionReconciler
{
    public static void Reconcile(
        System.Collections.IList target,
        IReadOnlyList<object> previousItems,
        IReadOnlyList<object> desiredItems)
    {
        ArgumentNullException.ThrowIfNull(target);

        _ = Reconcile<object>(
            new NonGenericListAdapter(target),
            previousItems,
            desiredItems,
            insertionAnchor: -1);
    }

    public static void Reconcile<T>(
        IList<T> target,
        IReadOnlyList<T> previousItems,
        IReadOnlyList<T> desiredItems)
    {
        _ = Reconcile(
            target,
            previousItems,
            desiredItems,
            insertionAnchor: -1);
    }

    internal static int Reconcile(
        System.Collections.IList target,
        IReadOnlyList<object> previousItems,
        IReadOnlyList<object> desiredItems,
        int insertionAnchor)
    {
        ArgumentNullException.ThrowIfNull(target);

        return Reconcile<object>(
            new NonGenericListAdapter(target),
            previousItems,
            desiredItems,
            insertionAnchor);
    }

    internal static int Reconcile<T>(
        IList<T> target,
        IReadOnlyList<T> previousItems,
        IReadOnlyList<T> desiredItems,
        int insertionAnchor)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(previousItems);
        ArgumentNullException.ThrowIfNull(desiredItems);

        if (target.IsReadOnly)
        {
            throw new InvalidOperationException(
                "The render collection is read-only and cannot be reconciled.");
        }

        var ownership = FindOwnedOccurrences(
            target,
            previousItems,
            insertionAnchor);
        var segmentStart = ownership.Anchor;

        for (var desiredIndex = 0;
            desiredIndex < desiredItems.Count;
            desiredIndex++)
        {
            var desiredItem = desiredItems[desiredIndex];
            var targetIndex = segmentStart + desiredIndex;
            var existingIndex = FindOwnedItem(
                target,
                ownership.Items,
                desiredItem,
                targetIndex);

            if (existingIndex >= 0)
            {
                if (existingIndex != targetIndex)
                {
                    Move(target, existingIndex, targetIndex);
                }

                ownership.Items.RemoveAt(existingIndex);
                ownership.Items.Insert(targetIndex, false);
                continue;
            }

            target.Insert(targetIndex, desiredItem);
            ownership.Items.Insert(targetIndex, false);
        }

        for (var targetIndex = ownership.Items.Count - 1;
            targetIndex >= 0;
            targetIndex--)
        {
            if (!ownership.Items[targetIndex])
            {
                continue;
            }

            target.RemoveAt(targetIndex);
        }

        return Math.Min(segmentStart, target.Count);
    }

    internal static void Restore(
        System.Collections.IList target,
        IReadOnlyList<object> snapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);

        Restore<object>(new NonGenericListAdapter(target), snapshot);
    }

    internal static void Restore<T>(
        IList<T> target,
        IReadOnlyList<T> snapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);

        if (target.IsReadOnly)
        {
            throw new InvalidOperationException(
                "The render collection is read-only and cannot be restored.");
        }

        for (var snapshotIndex = 0;
            snapshotIndex < snapshot.Count;
            snapshotIndex++)
        {
            var item = snapshot[snapshotIndex];
            if (snapshotIndex < target.Count &&
                ItemsMatch(target[snapshotIndex], item))
            {
                continue;
            }

            var currentIndex = FindItem(
                target,
                item,
                snapshotIndex + 1,
                target.Count);
            if (currentIndex >= 0)
            {
                Move(target, currentIndex, snapshotIndex);
                continue;
            }

            target.Insert(snapshotIndex, item);
        }

        while (target.Count > snapshot.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static CollectionOwnership FindOwnedOccurrences<T>(
        IList<T> target,
        IReadOnlyList<T> previousItems,
        int insertionAnchor)
    {
        var ownership = new List<bool>(target.Count);
        for (var index = 0; index < target.Count; index++)
        {
            ownership.Add(false);
        }

        if (previousItems.Count == 0)
        {
            var emptyAnchor = insertionAnchor < 0
                ? target.Count
                : Math.Min(insertionAnchor, target.Count);

            return new CollectionOwnership(ownership, emptyAnchor);
        }

        var searchStart = insertionAnchor < 0
            ? 0
            : Math.Min(insertionAnchor, target.Count);
        if (!TryMarkOwnedSubsequence(
                target,
                previousItems,
                ownership,
                searchStart))
        {
            for (var index = 0; index < ownership.Count; index++)
            {
                ownership[index] = false;
            }

            for (var previousIndex = 0;
                previousIndex < previousItems.Count;
                previousIndex++)
            {
                var targetIndex = FindUnownedItem(
                    target,
                    ownership,
                    previousItems[previousIndex],
                    0);
                if (targetIndex < 0)
                {
                    throw new InvalidOperationException(
                        "The generated render collection was changed outside " +
                        "the render reconciler because an owned item was removed.");
                }

                ownership[targetIndex] = true;
            }
        }

        var anchor = ownership.IndexOf(true);
        return new CollectionOwnership(ownership, anchor);
    }

    private static bool TryMarkOwnedSubsequence<T>(
        IList<T> target,
        IReadOnlyList<T> previousItems,
        List<bool> ownership,
        int searchStart)
    {
        for (var previousIndex = 0;
            previousIndex < previousItems.Count;
            previousIndex++)
        {
            var targetIndex = FindUnownedItem(
                target,
                ownership,
                previousItems[previousIndex],
                searchStart);
            if (targetIndex < 0)
            {
                return false;
            }

            ownership[targetIndex] = true;
            searchStart = targetIndex + 1;
        }

        return true;
    }

    private static int FindUnownedItem<T>(
        IList<T> target,
        IReadOnlyList<bool> ownership,
        T item,
        int start)
    {
        for (var index = start; index < target.Count; index++)
        {
            if (!ownership[index] &&
                ItemsMatch(target[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindOwnedItem<T>(
        IList<T> target,
        IReadOnlyList<bool> ownership,
        T item,
        int preferredStart)
    {
        for (var index = preferredStart; index < target.Count; index++)
        {
            if (ownership[index] &&
                ItemsMatch(target[index], item))
            {
                return index;
            }
        }

        for (var index = 0; index < preferredStart; index++)
        {
            if (ownership[index] &&
                ItemsMatch(target[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private readonly record struct CollectionOwnership(
        List<bool> Items,
        int Anchor);

    private static int FindItem<T>(
        IList<T> target,
        T item,
        int start,
        int end)
    {
        for (var index = start; index < end; index++)
        {
            if (ItemsMatch(target[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    private static void Move<T>(
        IList<T> target,
        int oldIndex,
        int newIndex)
    {
        if (target is IMoveableList moveableList &&
            moveableList.TryMove(oldIndex, newIndex))
        {
            return;
        }

        if (target is IAvaloniaList<T> avaloniaList)
        {
            avaloniaList.Move(oldIndex, newIndex);
            return;
        }

        var item = target[oldIndex];
        target.RemoveAt(oldIndex);
        target.Insert(newIndex, item);
    }

    private static bool ItemsMatch<T>(T left, T right)
    {
        return typeof(T).IsValueType
            ? EqualityComparer<T>.Default.Equals(left, right)
            : ReferenceEquals(left, right);
    }

    private interface IMoveableList
    {
        bool TryMove(int oldIndex, int newIndex);
    }

    private sealed class NonGenericListAdapter : IList<object>, IMoveableList
    {
        private readonly System.Collections.IList _list;
        private readonly MethodInfo? _moveMethod;

        public NonGenericListAdapter(System.Collections.IList list)
        {
            _list = list;
            _moveMethod = FindMoveMethod(list.GetType());
        }

        public object this[int index]
        {
            get => _list[index]!;
            set => _list[index] = value;
        }

        public int Count => _list.Count;

        public bool IsReadOnly => _list.IsReadOnly || _list.IsFixedSize;

        public void Add(object item)
        {
            _list.Add(item);
        }

        public void Clear()
        {
            _list.Clear();
        }

        public bool Contains(object item)
        {
            return _list.Contains(item);
        }

        public void CopyTo(object[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        public IEnumerator<object> GetEnumerator()
        {
            for (var index = 0; index < _list.Count; index++)
            {
                yield return _list[index]!;
            }
        }

        public int IndexOf(object item)
        {
            return _list.IndexOf(item);
        }

        public void Insert(int index, object item)
        {
            _list.Insert(index, item);
        }

        public bool Remove(object item)
        {
            var index = _list.IndexOf(item);
            if (index < 0)
            {
                return false;
            }

            _list.RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            _list.RemoveAt(index);
        }

        public bool TryMove(int oldIndex, int newIndex)
        {
            if (_moveMethod == null)
            {
                return false;
            }

            try
            {
                _moveMethod.Invoke(
                    _list,
                    new object[] { oldIndex, newIndex });
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }

            return true;
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        private static MethodInfo? FindMoveMethod(Type type)
        {
            var interfaces = type.GetInterfaces();
            for (var index = 0; index < interfaces.Length; index++)
            {
                var interfaceType = interfaces[index];
                if (!interfaceType.IsGenericType ||
                    interfaceType.GetGenericTypeDefinition() !=
                    typeof(IAvaloniaList<>))
                {
                    continue;
                }

                return interfaceType.GetMethod(
                    nameof(IAvaloniaList<object>.Move),
                    new[] { typeof(int), typeof(int) });
            }

            return null;
        }
    }
}
