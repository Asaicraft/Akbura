using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Akbura.Markup;
using Akbura.RuntimePools;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace Akbura.Akcss;

/// <summary>
/// Represents one generated utility attribute in the runtime cascade.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkcssUtilityCandidateActivator : AkcssStyleActivator
{
    private readonly ImmutableArray<AkcssUtilityApplication> _applications;
    private readonly ImmutableArray<CandidateOperation> _operations;
    private readonly ImmutableArray<string> _conflictKeys;
    private readonly ImmutableArray<AkcssUtilityValueSource> _arguments;
    private readonly Func<bool>? _condition;
    private Func<ExpressionBindingContext, bool>? _reactiveCondition;
    private ExpressionBinding<bool>? _reactiveBinding;
    private bool _reactiveConditionValue;
    private bool _hasReactiveConditionValue;
    private bool _initializingReactiveCondition;
    private readonly AkcssUtilityValueSource? _variant;
    private readonly BindingPriority? _bindingPriority;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly List<CandidateObserver> _observers = [];
    private readonly object?[] _values;
    private readonly IDisposable?[] _operationContributions;
    private Action<AkcssUtilityCandidateActivator>? _changed;
    private bool? _lastFrameCondition;

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
            UnprefixedUtilityPrecedence.SourceOrder,
        BindingPriority? bindingPriority = null)
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
        _bindingPriority = bindingPriority.HasValue
            ? AkcssBindingPriority.Validate(bindingPriority.Value)
            : null;
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
        _operationContributions = new IDisposable?[_operations.Length];
        _conflictKeys = GetConflictKeys(_operations);
    }

    /// <summary>
    /// Creates a tracked condition while preserving the existing constructor
    /// and frame-evaluated condition contract for previously compiled callers.
    /// </summary>
    public AkcssUtilityCandidateActivator(
        string conflictKey,
        int sourceOrder,
        ImmutableArray<AkcssUtilityApplication> applications,
        Func<ExpressionBindingContext, bool> reactiveCondition,
        ImmutableArray<AkcssUtilityValueSource> arguments = default,
        AkcssUtilityValueSource? variant = null,
        double order = 0d,
        string? conflictGroup = null,
        UnprefixedUtilityPrecedence unprefixedPrecedence = UnprefixedUtilityPrecedence.SourceOrder,
        BindingPriority? bindingPriority = null)
        : this(conflictKey, sourceOrder, applications, arguments, condition: null,
            variant, order, conflictGroup, unprefixedPrecedence, bindingPriority)
    {
        _reactiveCondition = reactiveCondition ?? throw new ArgumentNullException(nameof(reactiveCondition));
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

    public bool IsPrefixed => _condition != null || _reactiveCondition != null || _variant != null;

    /// <summary>
    /// Refreshes generated render-local captures without replacing the candidate,
    /// its contributions, or its existing dependency subscriptions.
    /// </summary>
    public static void RefreshReactiveCondition(object target, int activatorIndex,
        Func<ExpressionBindingContext, bool> evaluate)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(evaluate);
        Avalonia.Threading.Dispatcher.UIThread.VerifyAccess();
        var styles = AkcssRuntime.GetStyles(target);
        if ((uint)activatorIndex >= (uint)styles.Length ||
            styles[activatorIndex] is not AkcssUtilityCandidateActivator candidate ||
            candidate._reactiveCondition == null)
        {
            throw new InvalidOperationException("The generated reactive AKCSS condition does not match its owning activator slot.");
        }
        candidate._reactiveCondition = evaluate;
        candidate._reactiveBinding?.Update(evaluate, candidate.SetReactiveConditionValue);
    }

    public double Order { get; }

    public string? ConflictGroup { get; }

    public UnprefixedUtilityPrecedence UnprefixedPrecedence { get; }

    public override bool IsConditional => IsPrefixed;
    internal bool HasFrameCondition => _condition != null;

    public override bool Condition => IsReady && IsActive;

    internal ImmutableArray<string> ConflictKeys => _conflictKeys;

    internal int OperationCount => _operations.Length;

    internal bool SupportsHotReloadReconciliation => _operations.Length != 0 &&
        _operations.All(static operation => operation.SupportsHotReloadReconciliation);

    internal bool RefreshFrameCondition()
    {
        if (_condition == null)
        {
            return false;
        }
        var previous = _lastFrameCondition;
        _lastFrameCondition = _condition();
        return previous != _lastFrameCondition;
    }

    internal bool UsesBindingPriority =>
        _bindingPriority.HasValue ||
        _variant?.HasBindingPriorityMetadata == true;

    internal bool IsReady
    {
        get
        {
            if (_reactiveCondition != null && !_hasReactiveConditionValue)
            {
                return false;
            }

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
            // Reactive conditions are evaluated exclusively by ExpressionBinding.
            // Cascade winner selection only reads the cached result.
            if (_reactiveCondition != null && !_reactiveConditionValue)
            {
                return false;
            }

            if (_condition != null)
            {
                _lastFrameCondition = _condition();
                if (!_lastFrameCondition.Value)
                {
                    return false;
                }
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
                ExecuteOperationCore(index, target);
            }
            else if (UsesBindingPriority)
            {
                DisposeContribution(index);
            }
        }
    }

    public override void Reset(object target)
    {
        for (var index = _operations.Length - 1;
             index >= 0;
             index--)
        {
            ResetOperationCore(index, target);
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
        ExecuteOperationCore(index, target);
    }

    internal void ResetOperation(
        int index,
        object target)
    {
        ResetOperationCore(index, target);
    }

    internal bool CanUpdateContribution(int index) => UsesBindingPriority && _condition == null && _reactiveCondition == null &&
        _operations[index].SupportsContributionUpdates;

    internal bool TryUpdateOperation(int index, object target)
    {
        if (!CanUpdateContribution(index) || _operationContributions[index] is not AkcssPropertyContribution contribution)
        {
            return false;
        }
        CopyArgumentValues();
        return _operations[index].TryUpdateContribution(target, _values, GetBindingPriority(), contribution);
    }

    internal void Attach(
        object target,
        Action<AkcssUtilityCandidateActivator> changed)
    {
        _changed = changed ??
            throw new ArgumentNullException(nameof(changed));

        AttachReactiveCondition();

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
            var observer = new CandidateObserver(this);
            _observers.Add(observer);
            _subscriptions.Add(signal.Subscribe(observer));
        }

        foreach (var argument in _arguments)
        {
            argument.Attach(target, OnChanged);
        }

        _variant?.Attach(target, OnChanged);
    }

    internal void Refresh(object target)
    {
        if (_variant?.HasBindingPriorityMetadata == true &&
            _variant.RecreateOnRefresh)
        {
            DisposeContributions();
        }

        foreach (var argument in _arguments)
        {
            argument.Refresh(target);
        }

        _variant?.Refresh(target);
    }

    internal void Detach(object target)
    {
        _changed = null;
        foreach (var observer in _observers)
        {
            observer.Deactivate();
        }
        _observers.Clear();
        List<Exception>? failures = null;
        var reactiveBinding = _reactiveBinding;
        _reactiveBinding = null;
        _hasReactiveConditionValue = false;
        _reactiveConditionValue = false;
        try
        {
            reactiveBinding?.Dispose();
        }
        catch (Exception failure)
        {
            (failures ??= []).Add(failure);
        }
        for (var index = _operationContributions.Length - 1; index >= 0; index--)
        {
            try
            {
                DisposeContribution(index);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }

        foreach (var subscription in _subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }

        _subscriptions.Clear();

        foreach (var argument in _arguments)
        {
            DetachSource(argument);
        }

        if (_variant != null)
        {
            DetachSource(_variant);
        }
        if (failures?.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures != null)
        {
            throw new AggregateException("AKCSS candidate cleanup failed.", failures);
        }

        void DetachSource(AkcssUtilityValueSource source)
        {
            try
            {
                source.Detach(target);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }
    }

    private void ExecuteOperationCore(int index, object target)
    {
        if (!UsesBindingPriority)
        {
            _operations[index].Execute(target, _values);
            return;
        }

        DisposeContribution(index);
        _operationContributions[index] = _operations[index].Apply(
            target,
            _values,
            GetBindingPriority());
    }

    internal ContributionUpdate? BeginContributionUpdate(HashSet<string> affected)
    {
        ContributionUpdate? update = null;
        for (var index = 0; index < _operations.Length; index++)
        {
            if (affected.Contains(_operations[index].ConflictKey) &&
                _operationContributions[index] is AkcssPropertyContribution contribution)
            {
                (update ??= new ContributionUpdate(this)).Capture(index, contribution);
                _operationContributions[index] = null;
            }
        }
        return update;
    }

    internal sealed class ContributionUpdate(AkcssUtilityCandidateActivator owner)
    {
        private readonly List<(int Index, AkcssPropertyContribution Resource)> _previous = [];
        private bool _completed;
        private bool _rolledBack;
        public void Capture(int index, AkcssPropertyContribution contribution) => _previous.Add((index, contribution));
        public void Suspend()
        {
            foreach (var (_, resource) in _previous)
            {
                resource.Suspend();
            }
        }
        public void Complete()
        {
            Finish(rollback: false);
        }
        public void Rollback(bool activate = true)
        {
            Finish(rollback: true, activate);
        }
        public void Detach()
        {
            List<Exception>? failures = null;
            foreach (var (_, resource) in _previous)
            {
                try
                {
                    resource.Dispose();
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures != null)
            {
                throw new AggregateException("AKCSS detached contribution cleanup failed.", failures);
            }
        }
        private void Finish(bool rollback, bool activate = true)
        {
            if (_rolledBack || (!rollback && _completed))
            {
                return;
            }
            _rolledBack = rollback;
            _completed = !rollback;
            List<Exception>? failures = null;
            foreach (var (index, resource) in _previous)
            {
                try
                {
                    if (rollback)
                    {
                        owner.DisposeContribution(index);
                        owner._operationContributions[index] = resource;
                        if (activate)
                        {
                            resource.Resume();
                        }
                        else
                        {
                            resource.Dispose();
                        }
                    }
                    else
                    {
                        resource.Dispose();
                    }
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures != null)
            {
                throw new AggregateException("AKCSS contribution transition cleanup failed.", failures);
            }
        }
    }

    private void ResetOperationCore(int index, object target)
    {
        if (UsesBindingPriority)
        {
            DisposeContribution(index);
            return;
        }

        _operations[index].Reset(target);
    }

    internal bool HasOwnedContribution(int index) => _operationContributions[index] is AkcssPropertyContribution;

    internal bool HaveArgumentValuesChanged()
    {
        for (var index = 0; index < _arguments.Length; index++)
        {
            if (_arguments[index].HasValue && !Equals(_arguments[index].Value, _values[index]))
            {
                return true;
            }
        }
        return false;
    }

    private BindingPriority GetBindingPriority()
    {
        if (_variant?.HasBindingPriorityMetadata == true)
        {
            return AkcssBindingPriority.Validate(_variant.BindingPriority);
        }

        if (_bindingPriority.HasValue)
        {
            return _bindingPriority.Value;
        }

        throw new InvalidOperationException(
            "The AKCSS candidate does not define a binding priority.");
    }

    private void DisposeContributions()
    {
        for (var index = _operationContributions.Length - 1;
             index >= 0;
             index--)
        {
            DisposeContribution(index);
        }
    }

    private void DisposeContribution(int index)
    {
        var contribution = _operationContributions[index];
        _operationContributions[index] = null;
        contribution?.Dispose();
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

    private void AttachReactiveCondition()
    {
        if (_reactiveCondition == null)
        {
            return;
        }

        _reactiveBinding?.Dispose();
        var binding = new ExpressionBinding<bool>();
        _reactiveBinding = binding;
        _hasReactiveConditionValue = false;
        _initializingReactiveCondition = true;
        try
        {
            binding.Update(_reactiveCondition, SetReactiveConditionValue);
        }
        catch (Exception failure)
        {
            _reactiveBinding = null;
            _hasReactiveConditionValue = false;
            try { binding.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
        finally
        {
            _initializingReactiveCondition = false;
        }
    }

    private void SetReactiveConditionValue(bool value)
    {
        var changed = !_hasReactiveConditionValue || _reactiveConditionValue != value;
        _reactiveConditionValue = value;
        _hasReactiveConditionValue = true;
        if (changed && !_initializingReactiveCondition)
        {
            OnChanged();
        }
    }

    private static ImmutableArray<CandidateOperation> CreateOperations(
        string legacyConflictKey,
        ImmutableArray<AkcssUtilityApplication> applications,
        int argumentCount)
    {
        using var builder =
            ImmutableArrayBuilder<CandidateOperation>.Rent();
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
        using var builder = ImmutableArrayBuilder<string>.Rent();
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
        private readonly AvaloniaProperty? _resetProperty;
        private readonly Type? _resetTargetType;

        public CandidateOperation(
            int applicationOrder,
            AkcssUtilityOperation operation)
        {
            ApplicationOrder = applicationOrder;
            _operation = operation ??
                throw new ArgumentNullException(nameof(operation));
            _legacyApplication = null;
            _resetProperty = operation.SupportsHotReloadReconciliation ? operation.HotReloadProperty : null;
            _resetTargetType = _resetProperty != null ? operation.HotReloadResetTargetType : null;
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
            _resetProperty = null;
            _resetTargetType = null;
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

        public bool SupportsHotReloadReconciliation => _resetProperty != null;

        public bool SupportsContributionUpdates => _operation?.SupportsContributionUpdates == true;

        public bool TryUpdateContribution(object target, IReadOnlyList<object?> arguments,
            BindingPriority priority, IDisposable contribution) =>
            _operation!.TryUpdateContribution(target, arguments, priority, contribution);

        public bool IsActive(object target, IReadOnlyList<object?> arguments)
        {
            return _operation?.IsActive(target, arguments) ?? true;
        }

        public void Execute(object target, IReadOnlyList<object?> arguments)
        {
            if (_operation != null)
            {
                _operation.Update(target, arguments);
                return;
            }

            _legacyApplication!.Execute(target, arguments);
        }

        public IDisposable Apply(
            object target,
            IReadOnlyList<object?> arguments,
            BindingPriority priority)
        {
            if (_operation == null)
            {
                throw new NotSupportedException(
                    "Legacy AKCSS utility applications cannot use UtilityBindingPriorityAttribute.");
            }

            return _operation.Apply(target, arguments, priority);
        }

        public void Reset(object target)
        {
            if (_operation != null)
            {
                if (_resetProperty != null)
                {
                    if (_resetTargetType!.IsInstanceOfType(target))
                    {
                        _operation.Reset(target, _resetProperty);
                    }
                }
                else
                {
                    _operation.Reset(target);
                }
                return;
            }

            _legacyApplication!.Utility.Reset(target);
        }
    }

    private sealed class CandidateObserver
        : IObserver<object?>
    {
        private AkcssUtilityCandidateActivator? _owner;

        public CandidateObserver(
            AkcssUtilityCandidateActivator owner)
        {
            _owner = owner;
        }

        public void OnNext(object? value)
        {
            _owner?.OnChanged();
        }

        public void OnError(Exception error)
        {
            if (_owner != null)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }
        }

        public void OnCompleted()
        {
        }

        public void Deactivate() => _owner = null;
    }
}
