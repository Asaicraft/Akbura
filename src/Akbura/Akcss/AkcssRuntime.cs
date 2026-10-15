using Akbura.Markup;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Akbura.Akcss;

internal static partial class AkcssRuntime
{
    private static readonly ConditionalWeakTable<object, TargetRuntime> s_targets = new();

    public static void SetStyles(
        object target,
        ImmutableArray<AkcssStyleActivator> styles)
    {
        ArgumentNullException.ThrowIfNull(target);

        var runtime = s_targets.GetValue(target, static value => new TargetRuntime(value));
        if (runtime.IsReconcilingStyles)
        {
            throw new InvalidOperationException("An AKCSS cascade cannot be replaced during reconciliation.");
        }
        runtime.SetStyles(styles.IsDefault ? [] : styles);
    }

    public static ImmutableArray<AkcssStyleActivator> GetStyles(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return s_targets.TryGetValue(target, out var runtime)
            ? runtime.Styles
            : [];
    }

    public static void Refresh(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        s_targets.GetValue(target, static value => new TargetRuntime(value))
            .Refresh();
    }

    public static void RefreshForHotReload(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        s_targets.GetValue(target, static value => new TargetRuntime(value))
            .RefreshForHotReload();
    }

    private sealed partial class TargetRuntime : IObserver<object?>
    {
        private readonly object _target;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly HashSet<string> _pendingConflictKeys =
            new(StringComparer.Ordinal);
        private ImmutableArray<AkcssStyleActivator> _styles = [];
        private bool _isApplying;
        private bool _isChanging;
        private bool _isDetached;
        private bool _applyPending;
        private bool _isRestoringSources;

        public TargetRuntime(object target)
        {
            _target = target;
            if (target is Control control)
            {
                control.AttachedToVisualTree += OnAttachedToVisualTree;
                control.DetachedFromVisualTree += OnDetachedFromVisualTree;
            }
        }

        public ImmutableArray<AkcssStyleActivator> Styles => _styles;

        public void SetStyles(ImmutableArray<AkcssStyleActivator> styles)
        {
            _descriptors = default;
            _isChanging = true;
            _applyPending = false;
            try
            {
                DisposeSubscriptions();
                ResetStyles();
                _styles = styles;
                _conflictIndex = new(StringComparer.Ordinal);

                if (!_isDetached)
                {
                    Subscribe();
                }
            }
            catch (Exception failure)
            {
                try
                {
                    DisposeSubscriptions();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException("AKCSS application and cleanup failed.", failure, cleanup);
                }
                throw;
            }
            finally
            {
                _isChanging = false;
            }

            if (!_isDetached)
            {
                RequestApply();
            }
        }

        public void RequestApply()
        {
            _applyPending = true;
            ApplyPendingCandidates();
        }

        public void Refresh()
        {
            _isChanging = true;
            try
            {
                foreach (var style in _styles)
                {
                    if (style is AkcssUtilityCandidateActivator candidate)
                    {
                        candidate.Refresh(_target);
                    }
                }
            }
            finally
            {
                _isChanging = false;
            }

            RequestApply();
        }

        public void RefreshForHotReload()
        {
            if (_isDetached)
            {
                return;
            }

            _isChanging = true;
            try
            {
                foreach (var style in _styles)
                {
                    if (style is AkcssUtilityCandidateActivator candidate)
                    {
                        candidate.Refresh(_target);
                        if (candidate.RefreshFrameCondition())
                        {
                            RequestApplyCandidate(candidate);
                        }
                    }
                }
            }
            finally
            {
                _isChanging = false;
            }

            ApplyPendingCandidates();
        }

        public void OnNext(object? value)
        {
            RequestApply();
        }

        public void OnError(Exception error)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        public void OnCompleted()
        {
        }

        private void Subscribe()
        {
            foreach (var style in _styles)
            {
                if (style is AkcssUtilityCandidateActivator candidate)
                {
                    candidate.Attach(
                        _target,
                        RequestApplyCandidate);
                    continue;
                }

                var signal = style.Watch(_target) ?? throw new InvalidOperationException(
                    $"AKCSS style '{style.Style.Name}' returned a null Watch signal.");
                _subscriptions.Add(signal.Subscribe(this));
            }
        }

        private void ResetStyles()
        {
            for (var index = _styles.Length - 1; index >= 0; index--)
            {
                _styles[index].Reset(_target);
            }
        }

        private void ExecuteStyles()
        {
            var utilityWinners = ResolveUtilityWinners();
            foreach (var style in _styles)
            {
                if (style is AkcssUtilityCandidateActivator candidate)
                {
                    for (var operationIndex = 0;
                         operationIndex < candidate.OperationCount;
                         operationIndex++)
                    {
                        var key = candidate.GetOperationConflictKey(
                            operationIndex);
                        if (utilityWinners.TryGetValue(
                                key,
                                out var winner) &&
                            winner.Is(candidate, operationIndex))
                        {
                            candidate.ExecuteOperation(
                                operationIndex,
                                _target);
                        }
                    }

                    continue;
                }

                if (style.Condition)
                {
                    style.Execute(_target);
                }
            }
        }

        private void DisposeSubscriptions()
        {
            List<Exception>? failures = null;
            foreach (var style in _styles)
            {
                if (style is AkcssUtilityCandidateActivator candidate)
                {
                    try
                    {
                        candidate.Detach(_target);
                    }
                    catch (Exception failure)
                    {
                        (failures ??= []).Add(failure);
                    }
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
            if (failures?.Count == 1)
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }
            if (failures != null)
            {
                throw new AggregateException("AKCSS subscription cleanup failed.", failures);
            }
        }

        private void RequestApplyCandidate(
            AkcssUtilityCandidateActivator candidate)
        {
            if (_isRestoringSources)
            {
                return;
            }
            if (!_styles.Contains(candidate) && _activeTransition?.ContainsPrevious(candidate) != true)
            {
                return;
            }
            if (_applyPending)
            {
                return;
            }

            foreach (var conflictKey in candidate.ConflictKeys)
            {
                _pendingConflictKeys.Add(conflictKey);
            }

            if (CannotFlush)
            {
                return;
            }

            ApplyPendingCandidates();
        }

        private void ApplyPendingCandidates()
        {
            if (CannotFlush)
            {
                return;
            }
            _isApplying = true;
            try
            {
                while (!_isDetached && _activeTransition == null && _updateDepth == 0)
                {
                    if (_applyPending)
                    {
                        _applyPending = false;
                        _pendingConflictKeys.Clear();
                        ResetStyles();
                        ExecuteStyles();
                    }
                    else if (_pendingConflictKeys.Count > 0)
                    {
                        var enumerator = _pendingConflictKeys.GetEnumerator();
                        enumerator.MoveNext();
                        var key = enumerator.Current;
                        enumerator.Dispose();
                        _pendingConflictKeys.Remove(key);
                        var index = GetConflictIndex(key);
                        if (!index.SupportsPublication)
                        {
                            index.Reset(_target);
                            ExecuteConflictKey(key);
                            continue;
                        }
                        var winner = index.Resolve(_target);
                        if (winner.HasValue && winner.Value.Candidate.TryUpdateOperation(winner.Value.OperationIndex, _target))
                        {
                            index.Reset(_target, winner);
                        }
                        else
                        {
                            index.Reset(_target);
                            if (winner.HasValue)
                            {
                                winner.Value.Candidate.ExecuteOperation(winner.Value.OperationIndex, _target);
                            }
                        }
                    }
                    else if (_pendingContributions?.Count > 0)
                    {
                        FlushContributions();
                    }
                    else
                    {
                        break;
                    }
                }
            }
            finally
            {
                _isApplying = false;
            }
        }

        private void ExecuteConflictKey(string conflictKey, bool preserveContribution = false)
        {
            var winner = ResolveUtilityWinner(conflictKey);
            if (winner.HasValue)
            {
                if (preserveContribution && winner.Value.Candidate.HasOwnedContribution(winner.Value.OperationIndex))
                {
                    return;
                }
                winner.Value.Candidate.ExecuteOperation(
                    winner.Value.OperationIndex,
                    _target);
            }
        }

        private Dictionary<string, UtilityOperationWinner>
            ResolveUtilityWinners()
        {
            var result =
                new Dictionary<string, UtilityOperationWinner>(
                    StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var style in _styles)
            {
                if (style is AkcssUtilityCandidateActivator candidate)
                {
                    keys.UnionWith(candidate.ConflictKeys);
                }
            }

            foreach (var key in keys)
            {
                var winner = ResolveUtilityWinner(key);
                if (winner.HasValue)
                {
                    result.Add(key, winner.Value);
                }
            }

            return result;
        }

        private UtilityOperationWinner? ResolveUtilityWinner(string conflictKey)
        {
            return GetConflictIndex(conflictKey).Resolve(_target);
        }

        private static bool IsLater(
            UtilityOperationWinner left,
            UtilityOperationWinner right)
        {
            if (left.SourceOrder != right.SourceOrder)
            {
                return left.SourceOrder > right.SourceOrder;
            }

            var leftApplication =
                left.Candidate.GetOperationApplicationOrder(
                    left.OperationIndex);
            var rightApplication =
                right.Candidate.GetOperationApplicationOrder(
                    right.OperationIndex);
            if (leftApplication != rightApplication)
            {
                return leftApplication > rightApplication;
            }

            return left.Candidate.GetOperationOrder(
                       left.OperationIndex) >=
                   right.Candidate.GetOperationOrder(
                       right.OperationIndex);
        }

        private void OnAttachedToVisualTree(
            object? sender,
            VisualTreeAttachmentEventArgs args)
        {
            _isDetached = false;
            _isChanging = true;
            try
            {
                DisposeSubscriptions();
                Subscribe();
            }
            catch
            {
                DisposeSubscriptions();
                throw;
            }
            finally
            {
                _isChanging = false;
            }

            RequestApply();
        }

        private void OnDetachedFromVisualTree(
            object? sender,
            VisualTreeAttachmentEventArgs args)
        {
            _isDetached = true;
            _applyPending = false;
            _pendingConflictKeys.Clear();
            List<Exception>? failures = null;
            try
            {
                DisposeSubscriptions();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            try
            {
                _activeTransition?.DetachPrevious();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
            if (failures != null)
            {
                throw new AggregateException("AKCSS detach cleanup failed.", failures);
            }
        }

        private readonly struct UtilityOperationWinner
        {
            public UtilityOperationWinner(
                AkcssUtilityCandidateActivator candidate,
                int operationIndex,
                int sourceOrder = 0)
            {
                Candidate = candidate;
                OperationIndex = operationIndex;
                SourceOrder = sourceOrder;
            }

            public AkcssUtilityCandidateActivator Candidate { get; }

            public int OperationIndex { get; }

            public int SourceOrder { get; }

            public bool Is(AkcssUtilityCandidateActivator candidate, int operationIndex)
            {
                return ReferenceEquals(Candidate, candidate) &&
                    OperationIndex == operationIndex;
            }
        }
    }
}
