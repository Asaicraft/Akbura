using Avalonia.Threading;
using System.Collections;
using System.Collections.Immutable;
using System.Collections.Specialized;

namespace Akbura.HotReload;

public sealed partial class AkburaForeachRegion<TItem, TChild>
{
    private SourceAccess GetSource<TSource>(IEnumerable<TSource>? source, Func<TSource, TItem> convert)
    {
        if (_source is TypedSourceAccess<TSource> previous && previous.Matches(source))
        {
            previous.UpdateConversion(convert);
            return previous;
        }

        return new TypedSourceAccess<TSource>(source, convert);
    }

    private abstract class SourceAccess
    {
        public abstract object? Identity { get; }
        public abstract bool IsImmutable { get; }
        public abstract IEnumerable<TItem> Enumerate();
        public abstract TItem ConvertPayload(object? value);
    }

    private SourceAccess GetNonGenericSource(IEnumerable? source, Func<object?, TItem> convert)
    {
        if (_source is NonGenericSourceAccess previous && previous.Matches(source))
        {
            previous.UpdateConversion(convert);
            return previous;
        }

        return new NonGenericSourceAccess(source, convert);
    }

    private sealed class NonGenericSourceAccess(IEnumerable? source,
        Func<object?, TItem> convert) : SourceAccess
    {
        public override object? Identity => source;
        public override bool IsImmutable => source == null;
        public bool Matches(IEnumerable? other) => ReferenceEquals(source, other);

        public void UpdateConversion(Func<object?, TItem> conversion) => convert = conversion;

        public override IEnumerable<TItem> Enumerate()
        {
            if (source == null)
            {
                yield break;
            }

            var enumerator = source.GetEnumerator();
            try
            {
                while (enumerator.MoveNext())
                {
                    yield return convert(enumerator.Current);
                }
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        public override TItem ConvertPayload(object? value) => convert(value);
    }

    private sealed class TypedSourceAccess<TSource>(IEnumerable<TSource>? source,
        Func<TSource, TItem> convert) : SourceAccess
    {
        public override object? Identity => source;
        public override bool IsImmutable => source is ImmutableArray<TSource> || source == null;

        public bool Matches(IEnumerable<TSource>? other) =>
            source is ImmutableArray<TSource> array &&
                other is ImmutableArray<TSource> otherArray
                ? array.Equals(otherArray)
                : ReferenceEquals(source, other);

        public void UpdateConversion(Func<TSource, TItem> conversion) => convert = conversion;

        public override IEnumerable<TItem> Enumerate()
        {
            if (source == null || source is ImmutableArray<TSource> { IsDefault: true })
            {
                yield break;
            }

            foreach (var value in source)
            {
                yield return convert(value);
            }
        }

        public override TItem ConvertPayload(object? value) => convert((TSource)value!);
    }

    private KeyContract GetKeyContract<TKey>(Func<TItem, TKey> selector, object revision,
        IEqualityComparer<TKey>? comparer)
    {
        var equality = comparer ?? EqualityComparer<TKey>.Default;
        if (_keys is TypedKeyContract<TKey> previous && previous.Matches(selector, revision, equality))
        {
            return previous;
        }

        return new TypedKeyContract<TKey>(selector, revision, equality);
    }

    private abstract class KeyContract
    {
        public abstract object Select(TItem value);
        public abstract IEqualityComparer<object> Comparer { get; }
        public abstract bool HasSameIdentity(KeyContract? other);
    }

    private sealed class TypedKeyContract<TKey>(Func<TItem, TKey> selector, object revision,
        IEqualityComparer<TKey> comparer) : KeyContract
    {
        private readonly IEqualityComparer<object> _boxedComparer = new KeyEquality<TKey>(comparer);
        public override IEqualityComparer<object> Comparer => _boxedComparer;

        public override bool HasSameIdentity(KeyContract? other) =>
            other is TypedKeyContract<TKey> typed && Equals(revision, typed.Revision) &&
            ReferenceEquals(comparer, typed.Equality);

        private object Revision => revision;
        private IEqualityComparer<TKey> Equality => comparer;

        public bool Matches(Func<TItem, TKey> other, object otherRevision, IEqualityComparer<TKey> equality) =>
            selector.Equals(other) && Equals(revision, otherRevision) && ReferenceEquals(comparer, equality);

        public override object Select(TItem value) => selector(value) is { } key ? key :
            throw new InvalidOperationException("A foreach iteration key cannot be null.");
    }

    private sealed class KeyEquality<TKey>(IEqualityComparer<TKey> comparer) : IEqualityComparer<object>
    {
        public new bool Equals(object? left, object? right) =>
            left is TKey first && right is TKey second && comparer.Equals(first, second);

        public int GetHashCode(object value) => comparer.GetHashCode(((TKey)value)!);
    }

    private sealed class SourceLease : IDisposable
    {
        private readonly WeakReference<AkburaForeachRegion<TItem, TChild>> _owner;
        private INotifyCollectionChanged? _source;
        private long _sequence;

        public SourceLease(AkburaForeachRegion<TItem, TChild> owner, INotifyCollectionChanged source, long epoch)
        {
            _owner = new(owner);
            _source = source;
            Epoch = epoch;
            source.CollectionChanged += OnChanged;
        }

        public long Epoch { get; }
        public long Sequence => _sequence;
        public List<SourcePacket> Packets { get; } = [];
        public bool NeedsSnapshot { get; set; }

        public void Acknowledge(long sequence)
        {
            var count = 0;
            while (count < Packets.Count && Packets[count].Sequence <= sequence)
            {
                count++;
            }

            if (count != 0)
            {
                Packets.RemoveRange(0, count);
            }

            if (_sequence == sequence)
            {
                NeedsSnapshot = false;
            }
        }

        public void Dispose()
        {
            if (_source is { } source)
            {
                _source = null;
                source.CollectionChanged -= OnChanged;
            }

            Packets.Clear();
        }

        private void OnChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (!_owner.TryGetTarget(out var owner))
            {
                Dispose();
                return;
            }

            if (!Dispatcher.UIThread.CheckAccess())
            {
                NeedsSnapshot = true;
                throw new InvalidOperationException("Observed foreach sources must be mutated on the Avalonia UI thread.");
            }

            // Payloads are copied now. Conversion and user code run only in the render phase.
            Packets.Add(new SourcePacket(Epoch, ++_sequence, args.Action,
                args.OldStartingIndex, args.NewStartingIndex, Copy(args.OldItems), Copy(args.NewItems)));
            owner.ScheduleInvalidation();
        }

        private static object?[] Copy(IList? values)
        {
            if (values == null)
            {
                return [];
            }

            var result = new object?[values.Count];
            values.CopyTo(result, 0);
            return result;
        }
    }

    private readonly record struct SourcePacket(long Epoch, long Sequence,
        NotifyCollectionChangedAction Action, int OldIndex, int NewIndex,
        object?[] OldItems, object?[] NewItems);

    private bool ApplyPackets(RegionUpdate update)
    {
        var records = update.Records;
        foreach (var packet in update.Lease!.Packets)
        {
            if (packet.Sequence > update.Sequence)
            {
                break;
            }

            if (packet.Epoch != update.Lease.Epoch)
            {
                continue;
            }

            switch (packet.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (packet.NewIndex < 0 || packet.NewIndex > records.Count || packet.NewItems.Length == 0)
                    {
                        return false;
                    }

                    for (var i = 0; i < packet.NewItems.Length; i++)
                    {
                        records.Insert(packet.NewIndex + i,
                            NewRecord(update.Source.ConvertPayload(packet.NewItems[i]), update.Keys));
                    }

                    break;
                case NotifyCollectionChangedAction.Remove:
                    if (!MatchesOldPacket(update, packet))
                    {
                        return false;
                    }

                    records.RemoveRange(packet.OldIndex, packet.OldItems.Length);
                    break;
                case NotifyCollectionChangedAction.Move:
                    if (!MatchesOldPacket(update, packet) || packet.NewIndex < 0 ||
                        packet.NewIndex > records.Count - packet.OldItems.Length)
                    {
                        return false;
                    }

                    var moved = records.GetRange(packet.OldIndex, packet.OldItems.Length);
                    records.RemoveRange(packet.OldIndex, moved.Count);
                    records.InsertRange(packet.NewIndex, moved);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    if (!MatchesOldPacket(update, packet) || packet.OldItems.Length != packet.NewItems.Length ||
                        packet.NewIndex != packet.OldIndex)
                    {
                        return false;
                    }

                    for (var i = 0; i < packet.NewItems.Length; i++)
                    {
                        var index = packet.NewIndex + i;
                        var value = update.Source.ConvertPayload(packet.NewItems[i]);
                        var previous = records[index];
                        var key = update.Keys?.Select(value);
                        if (update.Keys != null && !update.Keys.Comparer.Equals(previous.Key, key))
                        {
                            records[index] = NewRecord(value, key);
                        }
                        else
                        {
                            previous.Item = value;
                            previous.Key = key;
                            previous.Dirty = true;
                        }
                    }

                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool MatchesOldPacket(RegionUpdate update, SourcePacket packet)
    {
        if (packet.OldItems.Length == 0 || packet.OldIndex < 0 ||
            packet.OldIndex > update.Records.Count - packet.OldItems.Length)
        {
            return false;
        }

        for (var i = 0; i < packet.OldItems.Length; i++)
        {
            var value = update.Source.ConvertPayload(packet.OldItems[i]);
            var recorded = update.Records[packet.OldIndex + i].Item;
            if (!SameItem(value, recorded))
            {
                return false;
            }
        }

        return true;
    }
}
