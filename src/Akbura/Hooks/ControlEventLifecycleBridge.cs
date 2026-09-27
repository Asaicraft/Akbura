using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Akbura.Hooks;

internal enum TerminalControlEvent
{
    DetachedFromVisualTree,
    Unloaded,
}

internal interface ITerminalControlEventRegistration
{
    TerminalControlEvent Event { get; }

    Action<object?, EventArgs> Snapshot();

    void InvokeCurrent(object? sender, EventArgs args);
}

internal sealed class TerminalControlEventHookState<TArgs> : ITerminalControlEventRegistration
    where TArgs : EventArgs
{
    private readonly ControlEventLifecycleBridge _bridge;
    private EventHandler<TArgs> _listener;
    private bool _registered;

    public TerminalControlEventHookState(ControlEventLifecycleBridge bridge, TerminalControlEvent @event, EventHandler<TArgs> listener)
    {
        _bridge = bridge;
        Event = @event;
        _listener = listener;
    }

    public TerminalControlEvent Event { get; }

    public void Apply(EventHandler<TArgs> listener)
    {
        _listener = listener;
        if (_registered)
        {
            return;
        }

        _bridge.RegisterTerminal(this);
        _registered = true;
    }

    public void Stop()
    {
        if (!_registered)
        {
            return;
        }

        _bridge.UnregisterTerminal(this);
        _registered = false;
    }

    public Action<object?, EventArgs> Snapshot()
    {
        var listener = _listener;
        return (sender, args) => listener(sender, (TArgs)args);
    }

    public void InvokeCurrent(object? sender, EventArgs args) => _listener(sender, (TArgs)args);
}

internal sealed class InitializedControlEventHookState
{
    private readonly ControlEventLifecycleBridge _bridge;
    private readonly int _identity;
    private EventHandler _listener;
    private bool _invokeIfAlreadyInitialized;
    private bool _registered;

    public InitializedControlEventHookState(ControlEventLifecycleBridge bridge, int identity, EventHandler listener, bool invokeIfAlreadyInitialized)
    {
        _bridge = bridge;
        _identity = identity;
        _listener = listener;
        _invokeIfAlreadyInitialized = invokeIfAlreadyInitialized;
    }

    public int Identity => _identity;

    public void Apply(EventHandler listener, bool invokeIfAlreadyInitialized)
    {
        _listener = listener;
        _invokeIfAlreadyInitialized = invokeIfAlreadyInitialized;
        if (!_registered)
        {
            _bridge.RegisterInitialized(this);
            _registered = true;
        }

        _bridge.DeliverInitializedCatchUp(this);
    }

    public void Stop()
    {
        if (!_registered)
        {
            return;
        }

        _bridge.UnregisterInitialized(this);
        _registered = false;
    }

    public void Deliver(object? sender, EventArgs args)
    {
        if (!_bridge.TryMarkInitializedDelivered(_identity))
        {
            return;
        }

        _listener(sender, args);
    }

    public bool InvokeIfAlreadyInitialized => _invokeIfAlreadyInitialized;
}

internal sealed class ControlEventLifecycleBridge
{
    private readonly AkburaControl _owner;
    private readonly List<ITerminalControlEventRegistration> _terminalRegistrations = [];
    private readonly List<InitializedControlEventHookState> _initializedRegistrations = [];
    private readonly HashSet<int> _deliveredInitializedIdentities = [];
    private readonly HashSet<int> _currentInitializedIdentities = [];
    private DetachTransition? _detachTransition;
    private bool _initializedRaised;

    public ControlEventLifecycleBridge(AkburaControl owner)
    {
        _owner = owner;
        owner.Initialized += OnInitialized;
        owner.DetachedFromVisualTree += OnDetachedFromVisualTree;
        owner.AddHandler(
            Control.UnloadedEvent,
            OnUnloaded,
            RoutingStrategies.Tunnel | RoutingStrategies.Direct | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    public void BeginVisualDetach()
    {
        var transition = new DetachTransition();
        for (var index = 0; index < _terminalRegistrations.Count; index++)
        {
            var registration = _terminalRegistrations[index];
            var snapshot = registration.Snapshot();
            if (registration.Event == TerminalControlEvent.DetachedFromVisualTree)
            {
                transition.DetachedCallbacks.Add(snapshot);
            }
            else
            {
                transition.UnloadedCallbacks.Add(snapshot);
            }
        }

        _detachTransition = transition;
    }

    public void CancelVisualDetach() => _detachTransition = null;

    public void BeginHookFrame() => _currentInitializedIdentities.Clear();

    public void MarkInitializedIdentity(int identity) => _currentInitializedIdentities.Add(identity);

    public void CompleteHookFrame() => _deliveredInitializedIdentities.IntersectWith(
        _currentInitializedIdentities);

    public bool TryMarkInitializedDelivered(int identity) =>
        _deliveredInitializedIdentities.Add(identity);

    public void RegisterTerminal(ITerminalControlEventRegistration registration)
    {
        _terminalRegistrations.Add(registration);
    }

    public void UnregisterTerminal(ITerminalControlEventRegistration registration)
    {
        _terminalRegistrations.Remove(registration);
    }

    public void RegisterInitialized(InitializedControlEventHookState registration)
    {
        _initializedRegistrations.Add(registration);
    }

    public void UnregisterInitialized(InitializedControlEventHookState registration)
    {
        _initializedRegistrations.Remove(registration);
    }

    public void DeliverInitializedCatchUp(InitializedControlEventHookState registration)
    {
        if (!_initializedRaised ||
            !registration.InvokeIfAlreadyInitialized)
        {
            return;
        }

        registration.Deliver(_owner, EventArgs.Empty);
    }

    private void OnInitialized(object? sender, EventArgs args)
    {
        _initializedRaised = true;
        var registrations = _initializedRegistrations.ToArray();
        for (var index = 0; index < registrations.Length; index++)
        {
            registrations[index].Deliver(sender, args);
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
    {
        var transition = _detachTransition;
        if (transition == null || transition.DetachedDelivered)
        {
            return;
        }

        transition.DetachedDelivered = true;
        InvokeCallbacks(transition.DetachedCallbacks, sender, args);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs args)
    {
        var transition = _detachTransition;
        if (transition == null || !ReferenceEquals(args.Source, _owner))
        {
            InvokeLiveUnloaded(sender, args);
            return;
        }

        if (transition.UnloadedDelivered)
        {
            return;
        }

        transition.UnloadedDelivered = true;
        try
        {
            InvokeCallbacks(transition.UnloadedCallbacks, sender, args);
        }
        finally
        {
            _detachTransition = null;
        }
    }

    private void InvokeLiveUnloaded(object? sender, RoutedEventArgs args)
    {
        var registrations = _terminalRegistrations.ToArray();
        List<Exception>? failures = null;
        for (var index = 0; index < registrations.Length; index++)
        {
            if (registrations[index].Event != TerminalControlEvent.Unloaded)
            {
                continue;
            }

            try
            {
                registrations[index].InvokeCurrent(sender, args);
            }
            catch (Exception exception)
            {
                UseHookFailures.Capture(ref failures, exception);
            }
        }

        UseHookFailures.ThrowIfAny(failures, "One or more control event hooks failed.");
    }

    private static void InvokeCallbacks(List<Action<object?, EventArgs>> callbacks, object? sender, EventArgs args)
    {
        List<Exception>? failures = null;
        for (var index = 0; index < callbacks.Count; index++)
        {
            try
            {
                callbacks[index](sender, args);
            }
            catch (Exception exception)
            {
                UseHookFailures.Capture(ref failures, exception);
            }
        }

        UseHookFailures.ThrowIfAny(failures, "One or more control event hooks failed.");
    }

    private sealed class DetachTransition
    {
        public List<Action<object?, EventArgs>> DetachedCallbacks { get; } = [];

        public List<Action<object?, EventArgs>> UnloadedCallbacks { get; } = [];

        public bool DetachedDelivered { get; set; }

        public bool UnloadedDelivered { get; set; }
    }
}
