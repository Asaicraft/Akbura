using System.Collections.Generic;

namespace Akbura.ComponentTree;

public sealed class State<T> : State
{
    private Action<T>? _subscribers;
    private T _value;
    private List<IDisposable>? _retainedSubscriptions;

    public State(T initialValue)
    {
        InitialValue = initialValue;
        _value = initialValue;
    }

    public new StateInfo<T>? Info => (StateInfo<T>?)base.Info;

    public T InitialValue
    {
        get;
    }

    public T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value))
            {
                return;
            }

            _value = value;
            NotifyChanged(_subscribers, value);
        }
    }

    public override Type ValueType => typeof(T);

    internal override object? BoxedInitialValue => InitialValue;

    internal override object? BoxedValue
    {
        get => Value;
        set
        {
            if (value == null && default(T) is not null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            Value = (T)value!;
        }
    }

    public IDisposable Subscribe(Action<T> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        _subscribers += subscriber;
        return new Subscription(this, subscriber);
    }

    internal void RetainSubscription(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        (_retainedSubscriptions ??= []).Add(subscription);
    }

    private void Unsubscribe(Action<T> subscriber)
    {
        _subscribers -= subscriber;
    }

    private sealed class Subscription : IDisposable
    {
        private State<T>? _state;
        private Action<T>? _subscriber;

        public Subscription(State<T> state, Action<T> subscriber)
        {
            _state = state;
            _subscriber = subscriber;
        }

        public void Dispose()
        {
            var state = _state;
            var subscriber = _subscriber;
            if (state == null || subscriber == null)
            {
                return;
            }

            _state = null;
            _subscriber = null;
            state.Unsubscribe(subscriber);
        }
    }
}
