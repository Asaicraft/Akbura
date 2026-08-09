using Akbura.CompilerAnotations;
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
    private readonly ImmutableArray<CandidateOperation> _operations;
    private readonly ImmutableArray<string> _conflictKeys;
    private readonly ImmutableArray<AkcssUtilityValueSource> _arguments;
    private readonly Func<bool>? _condition;
    private readonly AkcssUtilityValueSource? _variant;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly object?[] _values;
    private Action<AkcssUtilityCandidateActivator>? _changed;

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
        _operations = CreateOperations(
            conflictKey,
            applications,
            _arguments.Length);
        _conflictKeys = GetConflictKeys(_operations);
    }

    /// <summary>
    /// Gets the legacy utility-name conflict key.
    /// </summary>
    /// <remarks>
    /// Newly generated utilities resolve their individual operations by property key.
    /// This value remains available for hand-written and previously compiled utilities.
    /// </remarks>
    public string ConflictKey { get; }

    public int SourceOrder { get; }

    public bool IsPrefixed => _condition != null || _variant != null;

    public double Order { get; }

    public string? ConflictGroup { get; }

    public UnprefixedUtilityPrecedence UnprefixedPrecedence { get; }

    public override bool IsConditional => IsPrefixed;

    public override bool Condition => IsReady && IsActive;

    internal ImmutableArray<string> ConflictKeys => _conflictKeys;

    internal int OperationCount => _operations.Length;

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
        if (!IsReady || !IsActive)
        {
            return;
        }

        CopyArgumentValues();
        for (var index = 0; index < _operations.Length; index++)
        {
            if (_operations[index].IsActive(target, _values))
            {
                _operations[index].Execute(target, _values);
            }
        }
    }

    public override void Reset(object target)
    {
        for (var index = _operations.Length - 1;
             index >= 0;
             index--)
        {
            _operations[index].Reset(target);
        }
    }

    internal string GetOperationConflictKey(int index)
    {
        return _operations[index].ConflictKey;
    }

    internal AkcssOperationPriority GetOperationPriority(int index)
    {
        return _operations[index].Priority;
    }

    internal int GetOperationApplicationOrder(int index)
    {
        return _operations[index].ApplicationOrder;
    }

    internal int GetOperationOrder(int index)
    {
        return _operations[index].Order;
    }

    internal bool IsOperationActive(
        int index,
        object target)
    {
        if (!IsReady || !IsActive)
        {
            return false;
        }

        CopyArgumentValues();
        return _operations[index].IsActive(target, _values);
    }

    internal void ExecuteOperation(
        int index,
        object target)
    {
        CopyArgumentValues();
        _operations[index].Execute(target, _values);
    }

    internal void ResetOperation(
        int index,
        object target)
    {
        _operations[index].Reset(target);
    }

    internal void Attach(
        object target,
        Action<AkcssUtilityCandidateActivator> changed)
    {
        _changed = changed ??
            throw new ArgumentNullException(nameof(changed));

        var watchedUtilities = new List<AkcssUtility>();
        foreach (var application in _applications)
        {
            if (ContainsReference(
                    watchedUtilities,
                    application.Utility))
            {
                continue;
            }

            watchedUtilities.Add(application.Utility);
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

    internal void Refresh(object target)
    {
        foreach (var argument in _arguments)
        {
            argument.Refresh(target);
        }

        _variant?.Refresh(target);
    }

    internal void Detach(object target)
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

    private void CopyArgumentValues()
    {
        for (var index = 0;
             index < _arguments.Length;
             index++)
        {
            _values[index] = _arguments[index].Value;
        }
    }

    private void OnChanged()
    {
        _changed?.Invoke(this);
    }

    private static ImmutableArray<CandidateOperation> CreateOperations(
        string legacyConflictKey,
        ImmutableArray<AkcssUtilityApplication> applications,
        int argumentCount)
    {
        var builder = ImmutableArray.CreateBuilder<CandidateOperation>();
        for (var applicationIndex = 0;
             applicationIndex < applications.Length;
             applicationIndex++)
        {
            var application = applications[applicationIndex];
            var utility = application.Utility;
            var operations = utility.Operations;
            if (!operations.IsDefaultOrEmpty &&
                utility.Parameters.Length == argumentCount)
            {
                foreach (var operation in operations)
                {
                    builder.Add(
                        new CandidateOperation(
                            applicationIndex,
                            operation));
                }

                continue;
            }

            builder.Add(
                new CandidateOperation(
                    applicationIndex,
                    legacyConflictKey,
                    application));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> GetConflictKeys(
        ImmutableArray<CandidateOperation> operations)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var operation in operations)
        {
            if (keys.Add(operation.ConflictKey))
            {
                builder.Add(operation.ConflictKey);
            }
        }

        return builder.ToImmutable();
    }

    private static bool ContainsReference(
        List<AkcssUtility> utilities,
        AkcssUtility utility)
    {
        foreach (var candidate in utilities)
        {
            if (ReferenceEquals(candidate, utility))
            {
                return true;
            }
        }

        return false;
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

    private readonly struct CandidateOperation
    {
        private readonly AkcssUtilityOperation? _operation;
        private readonly AkcssUtilityApplication? _legacyApplication;

        public CandidateOperation(
            int applicationOrder,
            AkcssUtilityOperation operation)
        {
            ApplicationOrder = applicationOrder;
            _operation = operation ??
                throw new ArgumentNullException(nameof(operation));
            _legacyApplication = null;
            ConflictKey = operation.ConflictKey;
            Priority = operation.Priority;
            Order = operation.Order;
        }

        public CandidateOperation(
            int applicationOrder,
            string conflictKey,
            AkcssUtilityApplication legacyApplication)
        {
            ApplicationOrder = applicationOrder;
            _operation = null;
            _legacyApplication = legacyApplication ??
                throw new ArgumentNullException(nameof(legacyApplication));
            ConflictKey = conflictKey;
            Priority = AkcssOperationPriority.Style;
            Order = 0;
        }

        public string ConflictKey { get; }

        public AkcssOperationPriority Priority { get; }

        public int ApplicationOrder { get; }

        public int Order { get; }

        public bool IsActive(
            object target,
            IReadOnlyList<object?> arguments)
        {
            return _operation?.IsActive(target, arguments) ?? true;
        }

        public void Execute(
            object target,
            IReadOnlyList<object?> arguments)
        {
            if (_operation != null)
            {
                _operation.Update(target, arguments);
                return;
            }

            _legacyApplication!.Execute(target, arguments);
        }

        public void Reset(object target)
        {
            if (_operation != null)
            {
                _operation.Reset(target);
                return;
            }

            _legacyApplication!.Utility.Reset(target);
        }
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
