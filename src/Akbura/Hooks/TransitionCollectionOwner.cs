using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Data;

namespace Akbura.Hooks;

/// <summary>
/// Shares one reversible, target-local collection overlay between hook slots.
/// The Animation priority is used only for the Transitions configuration, so
/// original local values, bindings and styles are never overwritten.
/// </summary>
internal sealed class TransitionCollectionOwner(Animatable target)
{
    private static readonly ConditionalWeakTable<Animatable, TransitionCollectionOwner> s_owners = new();
    private readonly List<TransitionLease> _leases = [];
    private Transitions? _overlay;
    private ITransition[] _baseline = [];
    private IDisposable? _valueLease;
    private bool _isDetached;

    public static void Validate(Animatable target, ITransition[] rules)
    {
        var owner = s_owners.GetValue(target, static target => new(target));
        var properties = new HashSet<AvaloniaProperty>();
        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            if (rule is not TransitionBase transition)
            {
                throw new NotSupportedException("Transition hooks require Avalonia TransitionBase rules.");
            }

            var property = transition.Property ?? throw new InvalidOperationException(
                "A transition rule must specify a property.");
            if (property.IsDirect)
            {
                throw new ArgumentException("Direct properties cannot be animated.", nameof(rules));
            }

            if (!properties.Add(property))
            {
                throw new InvalidOperationException("A hook cannot own two transitions for the same property.");
            }

            if (transition.Duration < TimeSpan.Zero || transition.Delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(rules),
                    "Transition durations and delays must be non-negative.");
            }

            foreach (var lease in owner._leases)
            {
                if (lease.Rules.Any(existing => ((TransitionBase)existing).Property == property))
                {
                    throw new InvalidOperationException(
                        $"Another hook already owns a transition for {property.Name}.");
                }
            }

            var alreadyInstalled = target.Transitions?.Any(existing => ReferenceEquals(existing, rule)) == true;
            if (alreadyInstalled)
            {
                throw new InvalidOperationException("A transition rule must be owned by only one registration.");
            }
        }
    }

    public static IDisposable Add(Animatable target, ITransition[] rules)
    {
        var owner = s_owners.GetValue(target, static target => new(target));
        var lease = new TransitionLease(owner, rules);
        owner._leases.Add(lease);
        if (owner._leases.Count == 1 && target is Visual visual)
        {
            visual.DetachedFromVisualTree += owner.OnDetached;
            visual.AttachedToVisualTree += owner.OnAttached;
        }

        try
        {
            if (owner._overlay is not null)
            {
                foreach (var rule in rules)
                {
                    owner._overlay.Add(rule);
                }
            }
            else if (!owner._isDetached)
            {
                owner.InstallOverlay();
            }

            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private void InstallOverlay()
    {
        _baseline = target.Transitions?.ToArray() ?? [];
        var overlay = new Transitions();
        foreach (var rule in _baseline)
        {
            overlay.Add(rule);
        }

        foreach (var lease in _leases)
        {
            foreach (var rule in lease.Rules)
            {
                overlay.Add(rule);
            }
        }

        _overlay = overlay;
        _valueLease = target.SetValue(Animatable.TransitionsProperty, overlay, BindingPriority.Animation);
    }

    private void Remove(TransitionLease lease)
    {
        if (!_leases.Contains(lease))
        {
            return;
        }

        if (_overlay is not null)
        {
            foreach (var rule in lease.Rules)
            {
                _overlay.Remove(rule);
            }
        }

        _leases.Remove(lease);
        if (_leases.Count == 0)
        {
            RemoveOverlay();
            if (target is Visual visual)
            {
                visual.DetachedFromVisualTree -= OnDetached;
                visual.AttachedToVisualTree -= OnAttached;
            }

            _isDetached = false;
            s_owners.Remove(target);
        }
    }

    private void RemoveOverlay()
    {
        var overlay = _overlay;
        if (overlay is null)
        {
            return;
        }

        var foreignRules = overlay.Where(rule =>
            !_leases.Any(lease => lease.Rules.Any(owned => ReferenceEquals(owned, rule)))).ToArray();
        var wasEffective = ReferenceEquals(target.Transitions, overlay);
        _overlay = null;
        _valueLease?.Dispose();
        _valueLease = null;

        // A foreign owner may edit our local copy while it is visible. Preserve
        // those edits without mutating the shared original or replacing a data
        // binding. A newer effective overlay belongs to somebody else entirely.
        if (wasEffective && !SameRules(_baseline, foreignRules))
        {
            var merged = new Transitions();
            foreach (var rule in target.Transitions ?? [])
            {
                if (!_baseline.Any(original => ReferenceEquals(original, rule)) ||
                    foreignRules.Any(current => ReferenceEquals(current, rule)))
                {
                    merged.Add(rule);
                }
            }

            foreach (var rule in foreignRules)
            {
                if (!_baseline.Any(original => ReferenceEquals(original, rule)) &&
                    !merged.Any(current => ReferenceEquals(current, rule)))
                {
                    merged.Add(rule);
                }
            }

            target.SetCurrentValue(Animatable.TransitionsProperty, merged);
        }

        _baseline = [];
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _isDetached = true;
        RemoveOverlay();
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        _isDetached = false;
        if (_leases.Count > 0 && _overlay is null)
        {
            InstallOverlay();
        }
    }

    private static bool SameRules(ITransition[] previous, ITransition[] current)
    {
        if (previous.Length != current.Length)
        {
            return false;
        }

        for (var i = 0; i < previous.Length; i++)
        {
            if (!ReferenceEquals(previous[i], current[i]))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class TransitionLease(TransitionCollectionOwner owner, ITransition[] rules) : IDisposable
    {
        private bool _isDisposed;
        public ITransition[] Rules { get; } = rules;

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                owner.Remove(this);
            }
        }
    }
}
