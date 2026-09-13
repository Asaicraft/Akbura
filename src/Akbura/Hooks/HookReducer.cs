using Akbura.ComponentTree;
using Avalonia.Threading;

namespace Akbura.Hooks;

/// <summary>
/// A stable reducer model backed by a private reactive state.
/// </summary>
public sealed class HookReducer<TState, TAction>
{
    private readonly AkburaControl _owner;
    private readonly State<TState> _state;
    private Func<TState, TAction, TState> _reducer;
    private bool _isReducing;

    internal HookReducer(
        AkburaControl owner,
        State<TState> state,
        Func<TState, TAction, TState> reducer)
    {
        _owner = owner;
        _state = state;
        _reducer = reducer;
    }

    public TState Value => _state.Value;

    public void Dispatch(TAction action)
    {
        VerifyMutationAllowed();
        TState nextValue;
        _isReducing = true;
        try
        {
            nextValue = _reducer(_state.Value, action);
        }
        finally
        {
            _isReducing = false;
        }

        _state.Value = nextValue;
    }

    public void Reset()
    {
        VerifyMutationAllowed();
        _state.Value = _state.InitialValue;
    }

    internal void SetReducer(Func<TState, TAction, TState> reducer)
    {
        _reducer = reducer;
    }

    private void VerifyMutationAllowed()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_state.IsOwnedBy(_owner))
        {
            throw new InvalidOperationException("This reducer no longer belongs to an active hook slot.");
        }

        if (_isReducing)
        {
            throw new InvalidOperationException("A reducer cannot dispatch or reset recursively.");
        }
    }
}
