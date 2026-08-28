using Avalonia;
using Avalonia.Data;
using Avalonia.LogicalTree;

using System;
using System.Collections.Specialized;

namespace Akbura.Markup;

internal enum TailwindStructuralCondition
{
    First,
    Last,
    Only,
    Odd,
    Even,
    FirstOfType,
    LastOfType,
    OnlyOfType,
    Empty,
    Nth,
    NthLast,
    NthOfType,
    NthLastOfType,
}

/// <summary>
/// Provides a reactive condition based on the target's position in the
/// Avalonia logical tree.
/// </summary>
[UtilityBindingPriority(Priority = BindingPriority.StyleTrigger)]
public abstract class StructuralMarkupExtension
    : StyledElementStateMarkupExtension
{
    private readonly TailwindStructuralCondition _condition;

    internal StructuralMarkupExtension(
        TailwindStructuralCondition condition)
    {
        _condition = condition;
    }

    /// <inheritdoc />
    protected override IObservable<bool> Observe(StyledElement target)
    {
        return new StructuralConditionObservable(
            target,
            _condition,
            step: 0,
            offset: 0);
    }

    internal static IObservable<bool> ObserveNth(
        StyledElement target,
        TailwindStructuralCondition condition,
        int step,
        int offset)
    {
        return new StructuralConditionObservable(
            target,
            condition,
            step,
            offset);
    }

    private sealed class StructuralConditionObservable : IObservable<bool>
    {
        private readonly StyledElement _target;
        private readonly TailwindStructuralCondition _condition;
        private readonly int _step;
        private readonly int _offset;

        public StructuralConditionObservable(
            StyledElement target,
            TailwindStructuralCondition condition,
            int step,
            int offset)
        {
            _target = target;
            _condition = condition;
            _step = step;
            _offset = offset;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            return new Subscription(
                _target,
                _condition,
                _step,
                _offset,
                observer);
        }

        private sealed class Subscription : IDisposable
        {
            private StyledElement? _target;
            private IObserver<bool>? _observer;
            private INotifyCollectionChanged? _observedChildren;
            private readonly TailwindStructuralCondition _condition;
            private readonly int _step;
            private readonly int _offset;

            private bool _hasValue;
            private bool _lastValue;

            public Subscription(
                StyledElement target,
                TailwindStructuralCondition condition,
                int step,
                int offset,
                IObserver<bool> observer)
            {
                _target = target;
                _condition = condition;
                _step = step;
                _offset = offset;
                _observer = observer;

                target.PropertyChanged += OnTargetPropertyChanged;

                try
                {
                    RewireChildren();
                    PublishCurrentValue();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                var target = _target;
                if (target is null)
                {
                    return;
                }

                target.PropertyChanged -= OnTargetPropertyChanged;
                UnsubscribeChildren();
                _target = null;
                _observer = null;
            }

            private void OnTargetPropertyChanged(
                object? sender,
                AvaloniaPropertyChangedEventArgs eventArgs)
            {
                if (_condition != TailwindStructuralCondition.Empty &&
                    eventArgs.Property == StyledElement.ParentProperty)
                {
                    RewireChildren();
                    PublishCurrentValue();
                }
            }

            private void OnChildrenChanged(
                object? sender,
                NotifyCollectionChangedEventArgs eventArgs)
            {
                PublishCurrentValue();
            }

            private void RewireChildren()
            {
                UnsubscribeChildren();

                var target = _target;
                if (target is null)
                {
                    return;
                }

                var logical = _condition == TailwindStructuralCondition.Empty
                    ? (ILogical)target
                    : target.Parent as ILogical;

                if (logical?.LogicalChildren is INotifyCollectionChanged
                    children)
                {
                    _observedChildren = children;
                    children.CollectionChanged += OnChildrenChanged;
                }
            }

            private void UnsubscribeChildren()
            {
                var children = _observedChildren;
                if (children is null)
                {
                    return;
                }

                _observedChildren = null;
                children.CollectionChanged -= OnChildrenChanged;
            }

            private void PublishCurrentValue()
            {
                var target = _target;
                var observer = _observer;
                if (target is null || observer is null)
                {
                    return;
                }

                var currentValue = Evaluate(target);
                if (_hasValue && currentValue == _lastValue)
                {
                    return;
                }

                _hasValue = true;
                _lastValue = currentValue;
                observer.OnNext(currentValue);
            }

            private bool Evaluate(StyledElement target)
            {
                if (_condition == TailwindStructuralCondition.Empty)
                {
                    return ((ILogical)target).LogicalChildren.Count == 0;
                }

                if (target.Parent is not ILogical parent)
                {
                    return false;
                }

                var children = parent.LogicalChildren;
                var index = IndexOf(children, target);
                if (index < 0)
                {
                    return false;
                }

                return _condition switch
                {
                    TailwindStructuralCondition.First => index == 0,
                    TailwindStructuralCondition.Last =>
                        index == children.Count - 1,
                    TailwindStructuralCondition.Only => children.Count == 1,
                    TailwindStructuralCondition.Odd => index % 2 == 0,
                    TailwindStructuralCondition.Even => index % 2 != 0,
                    TailwindStructuralCondition.FirstOfType =>
                        IsFirstOfType(children, target, index),
                    TailwindStructuralCondition.LastOfType =>
                        IsLastOfType(children, target, index),
                    TailwindStructuralCondition.OnlyOfType =>
                        IsFirstOfType(children, target, index) &&
                        IsLastOfType(children, target, index),
                    TailwindStructuralCondition.Nth =>
                        MatchesNth(index + 1, _step, _offset),
                    TailwindStructuralCondition.NthLast =>
                        MatchesNth(
                            children.Count - index,
                            _step,
                            _offset),
                    TailwindStructuralCondition.NthOfType =>
                        MatchesNth(
                            GetTypePosition(children, target, index, false),
                            _step,
                            _offset),
                    TailwindStructuralCondition.NthLastOfType =>
                        MatchesNth(
                            GetTypePosition(children, target, index, true),
                            _step,
                            _offset),
                    _ => false,
                };
            }

            private static int IndexOf(
                IReadOnlyList<ILogical> children,
                ILogical target)
            {
                for (var index = 0; index < children.Count; index++)
                {
                    if (ReferenceEquals(children[index], target))
                    {
                        return index;
                    }
                }

                return -1;
            }

            private static bool IsFirstOfType(
                IReadOnlyList<ILogical> children,
                StyledElement target,
                int index)
            {
                var styleKey = target.StyleKey;
                for (var siblingIndex = 0;
                     siblingIndex < index;
                     siblingIndex++)
                {
                    if (children[siblingIndex] is StyledElement sibling &&
                        sibling.StyleKey == styleKey)
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool IsLastOfType(
                IReadOnlyList<ILogical> children,
                StyledElement target,
                int index)
            {
                var styleKey = target.StyleKey;
                for (var siblingIndex = index + 1;
                     siblingIndex < children.Count;
                     siblingIndex++)
                {
                    if (children[siblingIndex] is StyledElement sibling &&
                        sibling.StyleKey == styleKey)
                    {
                        return false;
                    }
                }

                return true;
            }

            private static int GetTypePosition(
                IReadOnlyList<ILogical> children,
                StyledElement target,
                int targetIndex,
                bool fromEnd)
            {
                var styleKey = target.StyleKey;
                var position = 0;

                if (!fromEnd)
                {
                    for (var index = 0; index <= targetIndex; index++)
                    {
                        if (children[index] is StyledElement sibling &&
                            sibling.StyleKey == styleKey)
                        {
                            position++;
                        }
                    }

                    return position;
                }

                for (var index = children.Count - 1;
                     index >= targetIndex;
                     index--)
                {
                    if (children[index] is StyledElement sibling &&
                        sibling.StyleKey == styleKey)
                    {
                        position++;
                    }
                }

                return position;
            }

            private static bool MatchesNth(
                int oneBasedPosition,
                int step,
                int offset)
            {
                if (oneBasedPosition <= 0)
                {
                    return false;
                }

                if (step == 0)
                {
                    return oneBasedPosition == offset;
                }

                var difference = (long)oneBasedPosition - offset;
                return difference % step == 0 && difference / step >= 0;
            }
        }
    }
}

/// <summary>
/// Base for parameterized <c>nth-*</c> logical-tree variants.
/// </summary>
[UtilityBindingPriority(Priority = BindingPriority.StyleTrigger)]
public abstract class NthStructuralMarkupExtension
    : StyledElementStateMarkupExtension
{
    private readonly TailwindStructuralCondition _condition;

    internal NthStructuralMarkupExtension(
        TailwindStructuralCondition condition)
    {
        _condition = condition;
    }

    /// <summary>
    /// Gets or sets the coefficient in the <c>an+b</c> expression.
    /// Use zero to match the single position specified by <see cref="Offset"/>.
    /// </summary>
    public int Step { get; set; }

    /// <summary>
    /// Gets or sets the one-based offset in the <c>an+b</c> expression.
    /// </summary>
    public int Offset { get; set; } = 1;

    /// <inheritdoc />
    protected override IObservable<bool> Observe(StyledElement target)
    {
        return StructuralMarkupExtension.ObserveNth(
            target,
            _condition,
            Step,
            Offset);
    }
}

#pragma warning disable IDE1006 // Names intentionally match AKCSS prefixes.

[UtilityVariant(10d, ConflictGroup = "Akbura.Tailwind.Structure", UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class firstExtension : StructuralMarkupExtension
{
    public firstExtension() : base(TailwindStructuralCondition.First) { }
}

[UtilityVariant(20d, ConflictGroup = "Akbura.Tailwind.Structure", UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class lastExtension : StructuralMarkupExtension
{
    public lastExtension() : base(TailwindStructuralCondition.Last) { }
}

[UtilityVariant(30d, ConflictGroup = "Akbura.Tailwind.Structure", UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class onlyExtension : StructuralMarkupExtension
{
    public onlyExtension() : base(TailwindStructuralCondition.Only) { }
}

[UtilityVariant(40d, ConflictGroup = "Akbura.Tailwind.Structure", UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class oddExtension : StructuralMarkupExtension
{
    public oddExtension() : base(TailwindStructuralCondition.Odd) { }
}

[UtilityVariant(50d, ConflictGroup = "Akbura.Tailwind.Structure", UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class evenExtension : StructuralMarkupExtension
{
    public evenExtension() : base(TailwindStructuralCondition.Even) { }
}

[UtilityVariant(60d, ConflictGroup = "Akbura.Tailwind.Structure", UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class firstOfTypeExtension : StructuralMarkupExtension
{
    public firstOfTypeExtension() : base(TailwindStructuralCondition.FirstOfType) { }
}

[UtilityVariant(70d, ConflictGroup = "Akbura.Tailwind.Structure", UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class lastOfTypeExtension : StructuralMarkupExtension
{
    public lastOfTypeExtension() : base(TailwindStructuralCondition.LastOfType) { }
}

[UtilityVariant(80d, ConflictGroup = "Akbura.Tailwind.Structure", UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class onlyOfTypeExtension : StructuralMarkupExtension
{
    public onlyOfTypeExtension() : base(TailwindStructuralCondition.OnlyOfType) { }
}

[UtilityVariant(130d, ConflictGroup = "Akbura.Tailwind.Structure", UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class emptyExtension : StructuralMarkupExtension
{
    public emptyExtension() : base(TailwindStructuralCondition.Empty) { }
}

[UtilityVariant(0d, UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class nthExtension : NthStructuralMarkupExtension
{
    public nthExtension() : base(TailwindStructuralCondition.Nth) { }
}

[UtilityVariant(0d, UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class nthLastExtension : NthStructuralMarkupExtension
{
    public nthLastExtension() : base(TailwindStructuralCondition.NthLast) { }
}

[UtilityVariant(0d, UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class nthOfTypeExtension : NthStructuralMarkupExtension
{
    public nthOfTypeExtension() : base(TailwindStructuralCondition.NthOfType) { }
}

[UtilityVariant(0d, UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class nthLastOfTypeExtension : NthStructuralMarkupExtension
{
    public nthLastOfTypeExtension()
        : base(TailwindStructuralCondition.NthLastOfType)
    {
    }
}

#pragma warning restore IDE1006 // Names intentionally match AKCSS prefixes.
