using Akbura.HotReload;
using System.Collections;
using System.Collections.ObjectModel;

namespace Akbura.UnitTests;

public sealed class AkburaRenderDictionaryReconcilerTests
{
    [Fact]
    public void KeyChangesAndSwaps_PreserveChildrenAndExternalEntries()
    {
        var first = new object();
        var second = new object();
        var external = new object();
        var dictionary = new Dictionary<int, object> { [99] = external };
        using var owner = new AkburaRenderDictionaryReconciler<int, object>();
        owner.Reconcile(dictionary, [new(1, first), new(2, second)]);
        owner.Reconcile(dictionary, [new(2, first), new(1, second)]);
        Assert.Same(first, dictionary[2]);
        Assert.Same(second, dictionary[1]);
        Assert.Same(external, dictionary[99]);
        owner.Reconcile(dictionary, [new(3, first), new(4, second)]);
        Assert.False(dictionary.ContainsKey(1));
        Assert.False(dictionary.ContainsKey(2));
        owner.Clear();
        Assert.Single(dictionary);
        Assert.Same(external, dictionary[99]);
    }

    [Fact]
    public void UnchangedEntries_DoNotMutateLiveDictionary()
    {
        var child = new object();
        var dictionary = new CountingDictionary();
        using var owner = new AkburaRenderDictionaryReconciler<int, object>();
        owner.Reconcile(dictionary, [new(1, child)]);
        var mutations = dictionary.Mutations;
        owner.Reconcile(dictionary, [new(1, child)]);
        Assert.Equal(mutations, dictionary.Mutations);
    }

    [Fact]
    public void NativeComparer_RejectsCollisionsAndRestoresPreviousEntries()
    {
        var child = new object();
        var dictionary = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        using var owner = new AkburaRenderDictionaryReconciler<string, object>();
        owner.Reconcile(dictionary, [new("old", child)]);
        Assert.Throws<InvalidOperationException>(() =>
            owner.Reconcile(dictionary, [new("A", new()), new("a", new())]));
        Assert.Single(dictionary);
        Assert.Same(child, dictionary["old"]);
        owner.Reconcile(dictionary, [new("next", child)]);
        Assert.Single(dictionary);
        Assert.Same(child, dictionary["next"]);
    }

    [Fact]
    public void NativeBitwiseComparer_MovesSignedZeroWithoutReplacingTheChild()
    {
        var child = new object();
        var dictionary = new Dictionary<double, object>(new BitwiseDoubleComparer());
        using var owner = new AkburaRenderDictionaryReconciler<double, object>();
        owner.Reconcile(dictionary, [new(0d, child)]);
        owner.Reconcile(dictionary, [new(-0d, child)]);
        Assert.False(dictionary.ContainsKey(0d));
        Assert.Same(child, dictionary[-0d]);
        Assert.Single(dictionary);
    }

    [Fact]
    public void NativeBitwiseComparer_EqualValuedExternalKeyIsNotMistakenForAnUnchangedOwnedKey()
    {
        var child = new object();
        var dictionary = new Dictionary<double, object>(new BitwiseDoubleComparer()) { [-0d] = child };
        using var owner = new AkburaRenderDictionaryReconciler<double, object>();
        owner.Reconcile(dictionary, [new(0d, child)]);
        Assert.Throws<InvalidOperationException>(() => owner.Reconcile(dictionary, [new(-0d, child)]));
        Assert.Same(child, dictionary[0d]);
        Assert.Same(child, dictionary[-0d]);
        owner.Clear();
        Assert.Single(dictionary);
        Assert.Same(child, dictionary[-0d]);
    }

    [Fact]
    public void ExternalCollision_IsNeverOverwritten()
    {
        var child = new object();
        var external = new object();
        var dictionary = new Dictionary<int, object> { [99] = external };
        using var owner = new AkburaRenderDictionaryReconciler<int, object>();
        owner.Reconcile(dictionary, [new(1, child)]);
        Assert.Throws<InvalidOperationException>(() => owner.Reconcile(dictionary, [new(99, child)]));
        Assert.Same(external, dictionary[99]);
        Assert.Same(child, dictionary[1]);
    }

    [Fact]
    public void ExternalReplacementOfOwnedKey_IsNotDeletedDuringCleanup()
    {
        var dictionary = new Dictionary<int, object>();
        var external = new object();
        using var owner = new AkburaRenderDictionaryReconciler<int, object>();
        owner.Reconcile(dictionary, [new(1, new())]);
        dictionary[1] = external;
        owner.Clear();
        Assert.Same(external, dictionary[1]);
    }

    [Fact]
    public void ProvenExternalConflict_IsRejectedBeforeOwnedEntriesAreMutated()
    {
        var dictionary = new CountingDictionary();
        var child = new object();
        dictionary.Add(99, new());
        using var owner = new AkburaRenderDictionaryReconciler<int, object>();
        owner.Reconcile(dictionary, [new(1, child)]);
        var mutations = dictionary.Mutations;
        Assert.Throws<InvalidOperationException>(() => owner.Reconcile(dictionary, [new(99, child)]));
        Assert.Equal(mutations, dictionary.Mutations);
    }

    [Fact]
    public void ChangingReceiver_MigratesOnlyOwnedEntries()
    {
        var child = new object();
        var first = new Dictionary<int, object> { [99] = new() };
        var second = new Dictionary<int, object> { [98] = new() };
        using var owner = new AkburaRenderDictionaryReconciler<int, object>();
        owner.Reconcile(first, [new(1, child)]);
        owner.Reconcile(second, [new(2, child)]);
        Assert.Single(first);
        Assert.True(first.ContainsKey(99));
        Assert.Same(child, second[2]);
        owner.Clear();
        Assert.Single(second);
        Assert.True(second.ContainsKey(98));
    }

    [Fact]
    public void MutableInterfaceWithReadOnlyInstance_IsRejectedBeforeMutation()
    {
        var inner = new Dictionary<int, object> { [1] = new() };
        using var owner = new AkburaRenderDictionaryReconciler<int, object>();
        Assert.Throws<InvalidOperationException>(() =>
            owner.Reconcile(new ReadOnlyDictionary<int, object>(inner), [new(2, new())]));
        Assert.Single(inner);
        Assert.True(inner.ContainsKey(1));
    }

    [Fact]
    public void ThrowingMutator_RollsBackAndDoesNotCommitFailedSnapshot()
    {
        var child = new object();
        var dictionary = new CountingDictionary();
        using var owner = new AkburaRenderDictionaryReconciler<int, object>();
        owner.Reconcile(dictionary, [new(1, child)]);
        dictionary.ThrowOnceOnAdd = 3;
        Assert.Throws<NotSupportedException>(() => owner.Reconcile(dictionary, [new(2, child), new(3, new())]));
        Assert.Single(dictionary);
        Assert.Same(child, dictionary[1]);
        owner.Reconcile(dictionary, [new(4, child)]);
        Assert.Single(dictionary);
        Assert.Same(child, dictionary[4]);
    }

    [Fact]
    public void ThrowingRollback_ReportsOriginalAndRollbackFailures()
    {
        var dictionary = new CountingDictionary();
        var owner = new AkburaRenderDictionaryReconciler<int, object>();
        owner.Reconcile(dictionary, [new(1, new())]);
        dictionary.ThrowAllAdds = true;
        var error = Assert.Throws<AggregateException>(() => owner.Reconcile(dictionary, [new(2, new())]));
        Assert.Equal(2, error.InnerExceptions.Count);
        dictionary.ThrowAllAdds = false;
        owner.Clear();
    }

    [Fact]
    public void MutatorThrowingAfterRemoval_RestoresRemovedOwnedEntry()
    {
        var child = new object();
        var dictionary = new CountingDictionary();
        using var owner = new AkburaRenderDictionaryReconciler<int, object>();
        owner.Reconcile(dictionary, [new(1, child)]);
        dictionary.ThrowOnceAfterRemove = 1;
        Assert.Throws<NotSupportedException>(() => owner.Reconcile(dictionary, [new(2, child)]));
        Assert.Single(dictionary);
        Assert.Same(child, dictionary[1]);
    }

    [Fact]
    public void NonGenericDictionary_PreservesKeyTypesAndUsesNativeComparer()
    {
        var child = new object();
        var dictionary = new Hashtable(StringComparer.OrdinalIgnoreCase) { ["external"] = new object() };
        using var owner = new AkburaRenderDictionaryReconciler();
        owner.Reconcile(dictionary, [new(42, child), new("42", new())]);
        Assert.Same(child, dictionary[42]);
        Assert.NotSame(dictionary[42], dictionary["42"]);
        Assert.Throws<InvalidOperationException>(() =>
            owner.Reconcile(dictionary, [new("A", child), new("a", new())]));
        Assert.Equal(3, dictionary.Count);
        owner.Clear();
        Assert.Single(dictionary);
        Assert.True(dictionary.Contains("external"));
    }

    private sealed class BitwiseDoubleComparer : IEqualityComparer<double>
    {
        public bool Equals(double first, double second) =>
            BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second);
        public int GetHashCode(double value) => BitConverter.DoubleToInt64Bits(value).GetHashCode();
    }

    private sealed class CountingDictionary : IDictionary<int, object>
    {
        private readonly Dictionary<int, object> _values = new();
        public int Mutations { get; private set; }
        public int? ThrowOnceOnAdd { get; set; }
        public int? ThrowOnceAfterRemove { get; set; }
        public bool ThrowAllAdds { get; set; }
        public object this[int key] { get => _values[key]; set => _values[key] = value; }
        public ICollection<int> Keys => _values.Keys;
        public ICollection<object> Values => _values.Values;
        public int Count => _values.Count;
        public bool IsReadOnly => false;
        public void Add(int key, object value)
        {
            if (ThrowAllAdds || ThrowOnceOnAdd == key)
            {
                ThrowOnceOnAdd = null;
                throw new NotSupportedException("Injected dictionary add failure.");
            }

            Mutations++;
            _values.Add(key, value);
        }

        public bool Remove(int key)
        {
            Mutations++;
            var removed = _values.Remove(key);
            if (ThrowOnceAfterRemove == key)
            {
                ThrowOnceAfterRemove = null;
                throw new NotSupportedException("Injected failure after native removal.");
            }

            return removed;
        }
        public bool ContainsKey(int key) => _values.ContainsKey(key);
        public bool TryGetValue(int key, out object value) => _values.TryGetValue(key, out value!);
        public void Add(KeyValuePair<int, object> item) => Add(item.Key, item.Value);
        public void Clear() { Mutations++; _values.Clear(); }
        public bool Contains(KeyValuePair<int, object> item) => ((ICollection<KeyValuePair<int, object>>)_values).Contains(item);
        public void CopyTo(KeyValuePair<int, object>[] array, int arrayIndex) =>
            ((ICollection<KeyValuePair<int, object>>)_values).CopyTo(array, arrayIndex);
        public bool Remove(KeyValuePair<int, object> item) => Contains(item) && Remove(item.Key);
        public IEnumerator<KeyValuePair<int, object>> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
