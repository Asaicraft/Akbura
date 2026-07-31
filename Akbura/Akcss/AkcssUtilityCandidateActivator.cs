using Akbura.Markup;
using Avalonia.Controls;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace Akbura.Akcss;

/// <summary>
/// Represents one generated utility attribute in the runtime cascade.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkcssUtilityCandidateActivator
    : AkcssStyleActivator
{
    private readonly ImmutableArray<AkcssUtilityApplication> _applications;
    private readonly ImmutableArray<AkcssUtilityValueSource> _arguments;
    private readonly Func<bool>? _condition;
    private readonly AkcssUtilityValueSource? _variant;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly object?[] _values;
    private Action<string>? _changed;

    public AkcssUtilityCandidateActivator(
        string conflictKey,
        int sourceOrder,
        ImmutableArray<AkcssUtilityApplication> applications,
        ImmutableArray<AkcssUtilityValueSource> arguments = default,
        Func<bool>? condition = null,
        AkcssUtilityValueSource? variant = null,
        double order = 0d,
        string? conflictGroup = null,
        UnprefixedUtilityPrecedence unprefixedPrecedence =
            UnprefixedUtilityPrecedence.SourceOrder)
        : base(GetFirstUtility(applications))
    {
        if (string.IsNullOrWhiteSpace(conflictKey))
        {
            throw new ArgumentException(
                "A utility conflict key is required.",
                nameof(conflictKey));
        }

        ConflictKey = conflictKey;
        SourceOrder = sourceOrder;
        _applications = applications;
        _arguments = arguments.IsDefault ? [] : arguments;
        _condition = condition;
        _variant = variant;
        Order = order;
        ConflictGroup = string.IsNullOrWhiteSpace(conflictGroup)
            ? null
            : conflictGroup;
        UnprefixedPrecedence = unprefixedPrecedence;
        _values = new object?[_arguments.Length];
    }

    public string ConflictKey { get; }

    public int SourceOrder { get; }

    public bool IsPrefixed => _condition != null || _variant != null;

    public double Order { get; }

    public string? ConflictGroup { get; }

    public UnprefixedUtilityPrecedence UnprefixedPrecedence { get; }

    public override bool IsConditional => IsPrefixed;

    public override bool Condition => IsReady && IsActive;

    internal bool IsReady
    {
        get
        {
            if (_variant != null && !_variant.HasValue)
            {
                return false;
            }

            foreach (var argument in _arguments)
            {
                if (!argument.HasValue)
                {
                    return false;
                }
            }

            return true;
        }
    }

    internal bool IsActive
    {
        get
        {
            if (_condition != null && !_condition())
            {
                return false;
            }

            return _variant == null ||
                _variant.Value is true;
        }
    }

    public override void Execute(object target)
    {
        var control = GetControl(target);
        for (var index = 0;
             index < _arguments.Length;
             index++)
        {
            _values[index] = _arguments[index].Value;
        }

        foreach (var application in _applications)
        {
            application.Execute(control, _values);
        }
    }

    public override void Reset(object target)
    {
        var control = GetControl(target);
        for (var index = _applications.Length - 1;
             index >= 0;
             index--)
        {
            _applications[index].Utility.Reset(control);
        }
    }

    internal void Attach(
        Control target,
        Action<string> changed)
    {
        _changed = changed ??
            throw new ArgumentNullException(nameof(changed));

        foreach (var application in _applications)
        {
            var signal = application.Utility.Watch(target) ??
                throw new InvalidOperationException(
                    $"AKCSS utility '{application.Utility.Name}' returned a null Watch signal.");
            _subscriptions.Add(
                signal.Subscribe(
                    new CandidateObserver(this)));
        }

        foreach (var argument in _arguments)
        {
            argument.Attach(target, OnChanged);
        }

        _variant?.Attach(target, OnChanged);
    }

    internal void Refresh(Control target)
    {
        foreach (var argument in _arguments)
        {
            argument.Refresh(target);
        }

        _variant?.Refresh(target);
    }

    internal void Detach(Control target)
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();

        foreach (var argument in _arguments)
        {
            argument.Detach(target);
        }

        _variant?.Detach(target);
        _changed = null;
    }

    private void OnChanged()
    {
        _changed?.Invoke(ConflictKey);
    }

    private static AkcssUtility GetFirstUtility(
        ImmutableArray<AkcssUtilityApplication> applications)
    {
        if (applications.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A utility candidate requires at least one application.",
                nameof(applications));
        }

        return applications[0].Utility;
    }

    private static Control GetControl(object target)
    {
        return target as Control ??
            throw new ArgumentException(
                $"An AKCSS utility target must derive from '{typeof(Control)}'.",
                nameof(target));
    }

    private sealed class CandidateObserver
        : IObserver<object?>
    {
        private readonly AkcssUtilityCandidateActivator _owner;

        public CandidateObserver(
            AkcssUtilityCandidateActivator owner)
        {
            _owner = owner;
        }

        public void OnNext(object? value)
        {
            _owner.OnChanged();
        }

        public void OnError(Exception error)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        public void OnCompleted()
        {
        }
    }
}
