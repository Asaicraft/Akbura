using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Markup;

using static BreakpointsGroupKey;

file static class BreakpointsGroupKey
{
    public const string BreakpointsGroup = nameof(BreakpointsGroup);
}

public abstract class BreakpointMarkupExtension
{
    protected BreakpointPredicate IsActivatedPredicate
    {
        get; init;
    }


    public IObservable<bool>? ProvideValue(IServiceProvider? serviceProvider)
    {
        if (serviceProvider == null)
        {
            return null;
        }

        if (serviceProvider.GetService(typeof(IProvideValueTarget))
            is not IProvideValueTarget provideValueTarget)
        {
            return null;
        }

        if (provideValueTarget.TargetObject is not Visual target)
        {
            return null;
        }

        var topLevel = TopLevel.GetTopLevel(target);

        if (topLevel == null &&
            serviceProvider.GetService(typeof(IRootObjectProvider))
                is IRootObjectProvider rootObjectProvider)
        {
            topLevel = rootObjectProvider.IntermediateRootObject as TopLevel
                ?? rootObjectProvider.RootObject as TopLevel;
        }

        if (topLevel == null)
        {
            return null;
        }

        return new BreakpointObservable(
            topLevel.GetObservable(TopLevel.ClientSizeProperty),
            IsActivatedPredicate);
    }

    protected readonly unsafe struct BreakpointPredicate
    {
        private readonly delegate*<double, bool> _pointer;

        public BreakpointPredicate(delegate*<double, bool> pointer)
        {
            if (pointer == null)
            {
                throw new ArgumentNullException(nameof(pointer));
            }

            _pointer = pointer;
        }

        public bool Invoke(double width)
        {
            return _pointer(width);
        }
    }

    private sealed class BreakpointObservable : IObservable<bool>
    {
        private readonly IObservable<Size> _source;
        private readonly BreakpointPredicate _isActivated;

        public BreakpointObservable(
            IObservable<Size> source,
            BreakpointPredicate isActivated)
        {
            _source = source;
            _isActivated = isActivated;
        }

        public IDisposable Subscribe(IObserver<bool> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            return _source.Subscribe(
                new BreakpointObserver(observer, _isActivated));
        }
    }

    private sealed class BreakpointObserver : IObserver<Size>
    {
        private readonly IObserver<bool> _observer;
        private readonly BreakpointPredicate _isActivated;

        private bool _hasValue;
        private bool _lastValue;
        private bool _isStopped;

        public BreakpointObserver(
            IObserver<bool> observer,
            BreakpointPredicate isActivated)
        {
            _observer = observer;
            _isActivated = isActivated;
        }

        public void OnNext(Size size)
        {
            if (_isStopped)
            {
                return;
            }

            bool currentValue;

            try
            {
                currentValue = _isActivated.Invoke(size.Width);
            }
            catch (Exception exception)
            {
                _isStopped = true;
                _observer.OnError(exception);
                return;
            }

            if (_hasValue && currentValue == _lastValue)
            {
                return;
            }

            _hasValue = true;
            _lastValue = currentValue;

            _observer.OnNext(currentValue);
        }

        public void OnError(Exception error)
        {
            if (_isStopped)
            {
                return;
            }

            _isStopped = true;
            _observer.OnError(error);
        }

        public void OnCompleted()
        {
            if (_isStopped)
            {
                return;
            }

            _isStopped = true;
            _observer.OnCompleted();
        }
    }
}


#pragma warning disable IDE1006 // Naming Styles

[UtilityVariant(1, ConflictGroup = BreakpointsGroup, UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class smExtension : BreakpointMarkupExtension
{
    public smExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 640d;
    }
}

[UtilityVariant(10, ConflictGroup = BreakpointsGroup, UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class mdExtension : BreakpointMarkupExtension
{
    public mdExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 768d;
    }
}

[UtilityVariant(20, ConflictGroup = BreakpointsGroup, UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class lgExtension : BreakpointMarkupExtension
{
    public lgExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 1024d;
    }
}

[UtilityVariant(30, ConflictGroup = BreakpointsGroup, UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class xlExtension : BreakpointMarkupExtension
{
    public xlExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 1280d;
    }
}

[UtilityVariant(40, ConflictGroup = BreakpointsGroup, UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class xxlExtension : BreakpointMarkupExtension
{
    public xxlExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width)
    {
        return width >= 1536d;
    }
}


#pragma warning restore IDE1006 // Naming Styles