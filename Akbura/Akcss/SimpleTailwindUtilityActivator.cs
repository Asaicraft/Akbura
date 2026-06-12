using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Immutable;

namespace Akbura.Akcss;

public abstract class SimpleTailwindUtilityActivator : TailwindUtilityActivator
{
    private readonly IObservable<bool>? _condition;
    private bool _currentCondition;

    protected SimpleTailwindUtilityActivator(
        AkcssUtility utility,
        ImmutableArray<object?> arguments,
        IObservable<bool>? condition = null)
        : base(utility, isConditional: condition != null, arguments)
    {
        _condition = condition;
        _currentCondition = condition == null;
    }

    public override bool Condition => _currentCondition;

    public override IObservable<object?> Watch(Control control)
    {
        var utilitySignal = base.Watch(control);
        if (_condition == null)
        {
            return utilitySignal;
        }

        return new ConditionAndUtilitySignal(this, _condition, utilitySignal);
    }

    private sealed class ConditionAndUtilitySignal : IObservable<object?>
    {
        private readonly SimpleTailwindUtilityActivator _activator;
        private readonly IObservable<bool> _condition;
        private readonly IObservable<object?> _utilitySignal;

        public ConditionAndUtilitySignal(
            SimpleTailwindUtilityActivator activator,
            IObservable<bool> condition,
            IObservable<object?> utilitySignal)
        {
            _activator = activator;
            _condition = condition;
            _utilitySignal = utilitySignal;
        }

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);

            var conditionSubscription = _condition.Subscribe(new ConditionObserver(_activator, observer));
            var utilitySubscription = _utilitySignal.Subscribe(observer);
            return new CompositeDisposable(conditionSubscription, utilitySubscription);
        }
    }

    private sealed class ConditionObserver : IObserver<bool>
    {
        private readonly SimpleTailwindUtilityActivator _activator;
        private readonly IObserver<object?> _observer;

        public ConditionObserver(SimpleTailwindUtilityActivator activator, IObserver<object?> observer)
        {
            _activator = activator;
            _observer = observer;
        }

        public void OnNext(bool value)
        {
            _activator._currentCondition = value;
            _observer.OnNext(AvaloniaProperty.UnsetValue);
        }

        public void OnError(Exception error)
        {
            _observer.OnError(error);
        }

        public void OnCompleted()
        {
        }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable _first;
        private readonly IDisposable _second;

        public CompositeDisposable(IDisposable first, IDisposable second)
        {
            _first = first;
            _second = second;
        }

        public void Dispose()
        {
            _first.Dispose();
            _second.Dispose();
        }
    }
}
