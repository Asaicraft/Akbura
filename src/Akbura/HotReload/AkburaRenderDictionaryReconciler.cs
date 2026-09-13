using System.Collections;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;

namespace Akbura.HotReload;

/// <summary>Reconciles the entries owned by one markup declaration without clearing external entries.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaRenderDictionaryReconciler<TKey, TValue> : IDisposable
{
    private IDictionary<TKey, TValue>? _target;
    private KeyValuePair<TKey, TValue>[] _entries = [];

    public static IDictionary<TKey, TValue> CreateDictionary()
    {
        // IDictionary does not constrain nullable key annotations. Dictionary checks
        // actual null keys at runtime; keep its implementation-only annotation local.
#pragma warning disable CS8714
        return new Dictionary<TKey, TValue>();
#pragma warning restore CS8714
    }

    public void Reconcile(IDictionary<TKey, TValue> target,
        ReadOnlySpan<KeyValuePair<TKey, TValue>> desiredEntries)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (ReferenceEquals(target, _target) && Matches(target, desiredEntries))
        {
            return;
        }

        var desired = desiredEntries.ToArray();
        if (_target != null && !ReferenceEquals(target, _target))
        {
            Apply(target, [], desired);
            try
            {
                Apply(_target, _entries, []);
            }
            catch (Exception failure)
            {
                try
                {
                    Apply(target, desired, []);
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("Dictionary ownership migration and rollback failed.",
                        failure, rollbackFailure);
                }

                ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
        }
        else
        {
            Apply(target, _entries, desired);
        }

        _target = target;
        _entries = desired;
    }

    public void Clear()
    {
        if (_target == null)
        {
            return;
        }

        Apply(_target, _entries, []);
        _target = null;
        _entries = [];
    }

    public void Dispose() => Clear();

    private bool Matches(IDictionary<TKey, TValue> target,
        ReadOnlySpan<KeyValuePair<TKey, TValue>> desired)
    {
        if (desired.Length != _entries.Length)
        {
            return false;
        }

        for (var i = 0; i < desired.Length; i++)
        {
            if (!SameKeyIdentity(desired[i].Key, _entries[i].Key) ||
                !SameValue(desired[i].Value, _entries[i].Value) ||
                !IsOwned(target, _entries[i]) || !IsOwned(target, desired[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameKeyIdentity(TKey first, TKey second)
    {
        if (!typeof(TKey).IsValueType)
        {
            return ReferenceEquals(first, second);
        }

        if (typeof(TKey) == typeof(float))
        {
            return BitConverter.SingleToInt32Bits(Unsafe.As<TKey, float>(ref first)) ==
                BitConverter.SingleToInt32Bits(Unsafe.As<TKey, float>(ref second));
        }

        if (typeof(TKey) == typeof(double))
        {
            return BitConverter.DoubleToInt64Bits(Unsafe.As<TKey, double>(ref first)) ==
                BitConverter.DoubleToInt64Bits(Unsafe.As<TKey, double>(ref second));
        }

        if (typeof(TKey) == typeof(decimal))
        {
            Span<int> firstBits = stackalloc int[4];
            Span<int> secondBits = stackalloc int[4];
            decimal.GetBits(Unsafe.As<TKey, decimal>(ref first), firstBits);
            decimal.GetBits(Unsafe.As<TKey, decimal>(ref second), secondBits);
            return firstBits.SequenceEqual(secondBits);
        }

        // Unknown value types may override equality or expose representation-sensitive
        // native comparers. Reconcile them rather than infer the dictionary's comparer.
        return (typeof(TKey).IsPrimitive || typeof(TKey).IsEnum) &&
            EqualityComparer<TKey>.Default.Equals(first, second);
    }

    private static void Apply(IDictionary<TKey, TValue> target,
        ReadOnlySpan<KeyValuePair<TKey, TValue>> previous,
        ReadOnlySpan<KeyValuePair<TKey, TValue>> desired)
    {
        if (target.IsReadOnly)
        {
            throw new InvalidOperationException("The markup dictionary is read-only.");
        }

        var owned = new List<KeyValuePair<TKey, TValue>>(previous.Length);
        foreach (var entry in previous)
        {
            if (IsOwned(target, entry))
            {
                owned.Add(entry);
            }
        }

        foreach (var entry in desired)
        {
            if (!target.TryGetValue(entry.Key, out var currentValue))
            {
                continue;
            }

            var mightBeOwned = false;
            foreach (var oldEntry in owned)
            {
                if (SameValue(currentValue, oldEntry.Value))
                {
                    mightBeOwned = true;
                    break;
                }
            }

            if (!mightBeOwned)
            {
                throw new InvalidOperationException(
                    "A markup dictionary key conflicts with an external entry.");
            }
        }

        // A universal IDictionary contract does not expose its comparer. Native lookup and
        // insertion decide collisions. Rollback is best-effort, not notification-atomic.
        var attempted = new List<KeyValuePair<TKey, TValue>>(desired.Length);
        try
        {
            foreach (var entry in owned)
            {
                target.Remove(entry.Key);
            }

            foreach (var entry in desired)
            {
                if (target.ContainsKey(entry.Key))
                {
                    throw new InvalidOperationException(
                        "A markup dictionary key conflicts with another owned or external entry.");
                }

                attempted.Add(entry);
                target.Add(entry.Key, entry.Value);
            }
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            foreach (var entry in attempted)
            {
                try
                {
                    if (IsOwned(target, entry))
                    {
                        target.Remove(entry.Key);
                    }
                }
                catch (Exception rollbackFailure)
                {
                    rollbackFailures.Add(rollbackFailure);
                }
            }

            foreach (var entry in owned)
            {
                try
                {
                    if (!target.ContainsKey(entry.Key))
                    {
                        target.Add(entry.Key, entry.Value);
                    }
                    else if (!IsOwned(target, entry))
                    {
                        throw new InvalidOperationException(
                            "An external entry prevents restoration of the markup dictionary.");
                    }
                }
                catch (Exception rollbackFailure)
                {
                    rollbackFailures.Add(rollbackFailure);
                }
            }

            if (rollbackFailures.Count != 0)
            {
                rollbackFailures.Insert(0, failure);
                throw new AggregateException("Markup dictionary reconciliation and rollback failed.",
                    rollbackFailures);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private static bool IsOwned(IDictionary<TKey, TValue> target, KeyValuePair<TKey, TValue> entry) =>
        target.TryGetValue(entry.Key, out var value) && SameValue(value, entry.Value);

    private static bool SameValue(TValue first, TValue second) => typeof(TValue).IsValueType
        ? EqualityComparer<TValue>.Default.Equals(first, second)
        : ReferenceEquals(first, second);
}

[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaRenderDictionaryReconciler : IDisposable
{
    private readonly AkburaRenderDictionaryReconciler<object, object?> _inner = new();
    private IDictionary? _target;
    private NonGenericAdapter? _adapter;

    public void Reconcile(IDictionary target, ReadOnlySpan<DictionaryEntry> desiredEntries)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsFixedSize)
        {
            throw new InvalidOperationException("The markup dictionary has a fixed size.");
        }

        var adapter = ReferenceEquals(_target, target) ? _adapter! : new NonGenericAdapter(target);
        var desired = new KeyValuePair<object, object?>[desiredEntries.Length];
        for (var i = 0; i < desired.Length; i++)
        {
            desired[i] = new(desiredEntries[i].Key, desiredEntries[i].Value);
        }

        _inner.Reconcile(adapter, desired);
        _target = target;
        _adapter = adapter;
    }

    public void Clear()
    {
        _inner.Clear();
        _target = null;
        _adapter = null;
    }

    public void Dispose() => Clear();

    private sealed class NonGenericAdapter(IDictionary target) : IDictionary<object, object?>
    {
        public object? this[object key] { get => target[key]; set => target[key] = value; }
        public ICollection<object> Keys => target.Keys.Cast<object>().ToArray();
        public ICollection<object?> Values => target.Values.Cast<object?>().ToArray();
        public int Count => target.Count;
        public bool IsReadOnly => target.IsReadOnly || target.IsFixedSize;
        public void Add(object key, object? value) => target.Add(key, value);
        public bool ContainsKey(object key) => target.Contains(key);
        public bool Remove(object key)
        {
            var contains = target.Contains(key);
            if (contains)
            {
                target.Remove(key);
            }

            return contains;
        }

        public bool TryGetValue(object key, out object? value)
        {
            var contains = target.Contains(key);
            value = contains ? target[key] : null;
            return contains;
        }

        public void Add(KeyValuePair<object, object?> item) => Add(item.Key, item.Value);
        public void Clear() => target.Clear();
        public bool Contains(KeyValuePair<object, object?> item) =>
            TryGetValue(item.Key, out var value) && EqualityComparer<object?>.Default.Equals(value, item.Value);
        public void CopyTo(KeyValuePair<object, object?>[] array, int arrayIndex)
        {
            foreach (var entry in this)
            {
                array[arrayIndex++] = entry;
            }
        }

        public bool Remove(KeyValuePair<object, object?> item) => Contains(item) && Remove(item.Key);
        public IEnumerator<KeyValuePair<object, object?>> GetEnumerator()
        {
            foreach (DictionaryEntry entry in target)
            {
                yield return new(entry.Key, entry.Value);
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
