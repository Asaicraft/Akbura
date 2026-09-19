using Akbura.Collections;
using Akbura.HotReload;
using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Xunit;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CollectionParameterBindingTests
{
    [Fact]
    public void Reset_RelaysInvalidationEvenForUnchangedReferences()
    {
        var item = new object();
        var source = new TestList<object>([item]);
        var owned = new ObservableCollection<object>();
        using var binding = new CollectionParameterBinding<object>(owned, () => { });
        binding.SetSource(source);
        var actions = new List<NotifyCollectionChangedAction>();
        owned.CollectionChanged += (_, args) => actions.Add(args.Action);
        source.Reset([item]);
        Assert.Same(item, Assert.Single(owned));
        Assert.Equal(new[] { NotifyCollectionChangedAction.Replace }, actions);
    }

    [Fact]
    public void UntypedParameter_ObservingValueTypedList_PreservesMoveEvents()
    {
        var source = new ObservableCollection<int>([1, 2, 3]);
        var owned = new ObservableCollection<object>();
        using var binding = new CollectionParameterBinding<object>(owned, () => { });
        binding.SetSource(source);
        var actions = new List<NotifyCollectionChangedAction>();
        owned.CollectionChanged += (_, args) => actions.Add(args.Action);
        source.Move(0, 2);
        Assert.Equal(new object[] { 2, 3, 1 }, owned);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Move }, actions);
    }

    [Fact]
    public void NullSource_CanClearLaterLocalEditsAgain()
    {
        var owned = new ObservableCollection<int>();
        using var binding = new CollectionParameterBinding<int>(owned, () => { });
        binding.SetSource(null);
        owned.Add(1);
        binding.SetSource(null);
        Assert.Empty(owned);
    }

    [Fact]
    public void Snapshot_SelfAssignment_Null_AndInvalidInput()
    {
        var items = new ObservableCollection<int>();
        using var binding = new CollectionParameterBinding<int>(items, () => { });
        var source = new TestList<int>([1, 2]);
        binding.SetSource(source);
        Assert.Same(items, binding.Items);
        Assert.Equal(new[] { 1, 2 }, items);
        var notifications = 0;
        items.CollectionChanged += (_, _) => notifications++;
        binding.SetSource(source);
        binding.SetSource(items);
        Assert.Equal(0, notifications);
        Assert.Equal(1, source.Enumerations);
        Assert.Equal(1, source.Subscribers);
        Assert.Throws<ArgumentException>(() => binding.SetSource(new ArrayList { 3, "bad" }));
        Assert.Same(source, binding.Source);
        Assert.Equal(new[] { 1, 2 }, items);
        Assert.Equal(1, source.Subscribers);
        binding.SetSource(null);
        Assert.Empty(items);
        Assert.Equal(0, source.Subscribers);
    }

    [Fact]
    public void GenericOnlyNotifier_AppliesRangeDeltasWithoutEnumeratingAgain()
    {
        var source = new TestList<int>([1, 2, 3, 4]);
        Assert.False((object)source is IList);
        using var binding = Bind(source);
        source.AddRange(1, [8, 9]);
        Assert.Equal(new[] { 1, 8, 9, 2, 3, 4 }, binding.Items);
        source.MoveRange(1, 2, 3);
        Assert.Equal(new[] { 1, 2, 3, 8, 9, 4 }, binding.Items);
        source.MoveRange(3, 2, 0);
        Assert.Equal(new[] { 8, 9, 1, 2, 3, 4 }, binding.Items);
        source[2] = 7;
        source.RemoveAt(3);
        Assert.Equal(new[] { 8, 9, 7, 3, 4 }, binding.Items);
        Assert.Equal(1, source.Enumerations);
        source.Reset([5, 6]);
        Assert.Equal(new[] { 5, 6 }, binding.Items);
        Assert.Equal(2, source.Enumerations);
    }

    [Fact]
    public void WritableNotifier_SynchronizesBackByIndexWithoutFeedback()
    {
        var source = new TestList<int>([7, 7, 9]);
        using var binding = Bind(source);
        Assert.True(binding.IsTwoWay);
        binding.Items.RemoveAt(1);
        Assert.Equal(new[] { 7, 9 }, source);
        binding.Items.Insert(1, 5);
        binding.Items[0] = 4;
        binding.Items.Move(2, 0);
        Assert.Equal(new[] { 9, 4, 5 }, source);
        binding.Items.Clear();
        Assert.Empty(source);
        Assert.Empty(binding.Items);
        Assert.Equal(1, source.Subscribers);
    }

    [Fact]
    public void ObservableCollection_Move_IsNotConvertedToResetOrRemoveAdd()
    {
        var source = new ObservableCollection<int>([1, 2, 3]);
        using var binding = Bind(source);
        var sourceEvents = new List<NotifyCollectionChangedAction>();
        var ownedEvents = new List<NotifyCollectionChangedAction>();
        source.CollectionChanged += (_, e) => sourceEvents.Add(e.Action);
        binding.Items.CollectionChanged += (_, e) => ownedEvents.Add(e.Action);
        source.Move(0, 2);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Move }, ownedEvents);
        sourceEvents.Clear(); ownedEvents.Clear();
        binding.Items.Move(2, 0);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Move }, sourceEvents);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Move }, ownedEvents);
        Assert.Equal(new[] { 1, 2, 3 }, source);
    }

    [Fact]
    public void ReadonlyNotifier_IsOneWay_AndResynchronizesAfterLocalEdits()
    {
        var writable = new ObservableCollection<int>([1, 2]);
        var source = new ReadOnlyObservableCollection<int>(writable);
        using var binding = Bind(source);
        Assert.False(binding.IsTwoWay);
        binding.Items.Add(99);
        Assert.Equal(new[] { 1, 2 }, source);
        writable.Insert(0, 5);
        Assert.Equal(new[] { 5, 1, 2 }, binding.Items);
        writable.Clear();
        Assert.Empty(binding.Items);
    }

    [Fact]
    public void FixedSizeNotifier_IsOneWayEvenWhenIsReadOnlyIsFalse()
    {
        var source = new FixedNotifier([1, 2]);
        using var binding = Bind(source);
        Assert.False(source.IsReadOnly);
        Assert.True(source.IsFixedSize);
        Assert.False(binding.IsTwoWay);
        binding.Items.Add(3);
        Assert.Equal(2, source.Count);
        source[0] = 9;
        Assert.Equal(new[] { 9, 2 }, binding.Items);
    }

    [Fact]
    public void PlainList_IsSnapshotOnly_AndCanBeRefreshedExplicitly()
    {
        var source = new List<int> { 1 };
        using var binding = Bind(source);
        Assert.False(binding.IsTwoWay);
        source.Add(2);
        binding.SetSource(source); // render replay must not copy unchanged sources
        Assert.Equal(new[] { 1 }, binding.Items);
        binding.Refresh();
        Assert.Equal(new[] { 1, 2 }, binding.Items);
        binding.Items.Add(3);
        Assert.Equal(new[] { 1, 2 }, source);
    }

    [Fact]
    public void ReplacingSource_UnsubscribesAndPreservesTheOwnedIdentity()
    {
        var first = new TestList<int>([1]);
        var second = new TestList<int>([2]);
        using var binding = Bind(first);
        var owned = binding.Items;
        binding.SetSource(second);
        Assert.Same(owned, binding.Items);
        Assert.Equal(0, first.Subscribers);
        Assert.Equal(1, second.Subscribers);
        first.Add(99);
        Assert.Equal(new[] { 2 }, owned);
        second.Add(3);
        Assert.Equal(new[] { 2, 3 }, owned);
        binding.Dispose();
        Assert.Equal(0, second.Subscribers);
    }

    [Fact]
    public void Suspension_Unsubscribes_AndResumeReadsTheLatestSource()
    {
        var source = new TestList<int>([1]);
        using var binding = Bind(source);
        binding.Suspend();
        Assert.Equal(0, source.Subscribers);
        source.Add(2);
        binding.Items.Add(99);
        Assert.Equal(new[] { 1, 99 }, binding.Items);
        Assert.Equal(new[] { 1, 2 }, source);
        binding.Resume();
        Assert.Equal(1, source.Subscribers);
        Assert.Equal(new[] { 1, 2 }, binding.Items);
        binding.Resume();
        Assert.Equal(1, source.Subscribers);
    }

    [Fact]
    public void EqualButDistinctReferenceItems_AreNotCollapsed()
    {
        var first = new EqualItem(1);
        var second = new EqualItem(1);
        using var binding = new CollectionParameterBinding<EqualItem>(new(), () => { });
        binding.SetSource(new List<EqualItem> { first });
        binding.SetSource(new List<EqualItem> { second, second });
        Assert.Same(second, binding.Items[0]);
        Assert.Same(second, binding.Items[1]);
        Assert.NotSame(first, binding.Items[0]);
    }

    [Fact]
    public void NonGenericParameter_PreservesMixedObjectsAndObservesUntypedSource()
    {
        var source = new FixedNotifier([1, "text"]);
        using var binding = new CollectionParameterBinding<object>(new(), () => { });
        binding.SetSource(source);
        Assert.Equal(new object[] { 1, "text" }, binding.Items);
        source[1] = 42;
        Assert.Equal(new object[] { 1, 42 }, binding.Items);
    }

    [Fact]
    public async Task Foreach_ConsumesBridgeDeltas_AndRetainsMovedUnkeyedRows()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var source = new TestList<int>([1, 2, 3]);
            using var binding = Bind(source);
            using var region = new AkburaForeachRegion<int, object>(() => { });
            var evaluations = 0;
            LoopFlow Emit(AkburaForeachFrame<int, object> frame)
            {
                evaluations++;
                frame.Emit(frame.GetOrCreate(0, () => new object()));
                return LoopFlow.Next;
            }
            object[] Render()
            {
                var result = region.Render(binding.Items, "v1", AkburaForeachDependencies.None, Emit).ToArray();
                region.Commit();
                return result;
            }
            var first = Render();
            source.MoveRange(0, 1, 2);
            var moved = Render();
            Assert.Same(first[1], moved[0]);
            Assert.Same(first[2], moved[1]);
            Assert.Same(first[0], moved[2]);
            Assert.Equal(3, evaluations);
            source.Add(4);
            var added = Render();
            Assert.Equal(4, evaluations);
            Assert.Same(moved[0], added[0]);
            Assert.Equal(1, source.Enumerations);
            return true;
        }, CancellationToken.None);
    }

    private static CollectionParameterBinding<int> Bind(object source)
    {
        var result = new CollectionParameterBinding<int>(new(), () => { });
        result.SetSource(source);
        return result;
    }
    private sealed record EqualItem(int Value);

    // Deliberately implements only generic IList<T>, not nongeneric IList and
    // not ObservableCollection<T>. This catches concrete-type subscription bugs.
    internal sealed class TestList<T>(IEnumerable<T> values) : IList<T>, INotifyCollectionChanged
    {
        private readonly List<T> _items = new(values);
        private NotifyCollectionChangedEventHandler? _changed;
        public int Subscribers { get; private set; }
        public int Enumerations { get; private set; }
        public event NotifyCollectionChangedEventHandler? CollectionChanged
        {
            add { _changed += value; Subscribers++; }
            remove { _changed -= value; Subscribers--; }
        }
        public T this[int index]
        {
            get => _items[index];
            set
            {
                var old = _items[index]; _items[index] = value;
                _changed?.Invoke(this, new(NotifyCollectionChangedAction.Replace, value, old, index));
            }
        }
        public int Count => _items.Count;
        public bool IsReadOnly => false;
        public void Add(T item) => Insert(Count, item);
        public void Insert(int index, T item)
        {
            _items.Insert(index, item);
            _changed?.Invoke(this, new(NotifyCollectionChangedAction.Add, item, index));
        }
        public void RemoveAt(int index)
        {
            var old = _items[index]; _items.RemoveAt(index);
            _changed?.Invoke(this, new(NotifyCollectionChangedAction.Remove, old, index));
        }
        public bool Remove(T item)
        {
            var index = IndexOf(item); if (index < 0) return false;
            RemoveAt(index); return true;
        }
        public void Clear() => Reset([]);
        public void AddRange(int index, T[] items)
        {
            _items.InsertRange(index, items);
            _changed?.Invoke(this, new(NotifyCollectionChangedAction.Add, items, index));
        }
        public void MoveRange(int from, int count, int to)
        {
            var moved = _items.GetRange(from, count).ToArray();
            _items.RemoveRange(from, count); _items.InsertRange(to, moved);
            _changed?.Invoke(this, new(NotifyCollectionChangedAction.Move, moved, to, from));
        }
        public void Reset(T[] items)
        {
            _items.Clear(); _items.AddRange(items);
            _changed?.Invoke(this, new(NotifyCollectionChangedAction.Reset));
        }
        public bool Contains(T item) => _items.Contains(item);
        public int IndexOf(T item) => _items.IndexOf(item);
        public void CopyTo(T[] array, int index) => _items.CopyTo(array, index);
        public IEnumerator<T> GetEnumerator() { Enumerations++; return _items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FixedNotifier(object[] values) : IList, INotifyCollectionChanged
    {
        private readonly object[] _items = values;
        public object? this[int index]
        {
            get => _items[index];
            set
            {
                var old = _items[index]; _items[index] = value!;
                CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Replace, value, old, index));
            }
        }
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public bool IsFixedSize => true;
        public bool IsReadOnly => false;
        public int Count => _items.Length;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public int Add(object? value) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, object? value) => throw new NotSupportedException();
        public void Remove(object? value) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
        public bool Contains(object? value) => IndexOf(value) >= 0;
        public int IndexOf(object? value) => Array.IndexOf(_items, value);
        public void CopyTo(Array array, int index) => _items.CopyTo(array, index);
        public IEnumerator GetEnumerator() => _items.GetEnumerator();
    }
}
