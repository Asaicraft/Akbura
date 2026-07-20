using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Avalonia;

namespace Akbura.Hooks;

public static class AvaloniaPropertyHooks
{
    [UseHook]
    public static State<TValue> useAvaloniaProperty<TObject, TValue>(
        [Self] TObject control,
        AvaloniaProperty<TValue> property)
        where TObject : AvaloniaObject
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(property);

        var state = new State<TValue>((TValue)control.GetValue(property)!);
        var subscription = control
            .GetObservable(property)
            .Subscribe(new StateObserver<TValue>(state));
        state.RetainSubscription(subscription);
        return state;
    }

    private sealed class StateObserver<T> : IObserver<T>
    {
        private readonly State<T> _state;

        public StateObserver(State<T> state)
        {
            _state = state;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            throw error;
        }

        public void OnNext(T value)
        {
            _state.Value = value;
        }
    }
}
