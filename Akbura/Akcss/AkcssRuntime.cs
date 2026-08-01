using Akbura.CompilerAnotations;
using Akbura.Markup;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Akbura.Akcss;

internal static class AkcssRuntime
{
    private static readonly ConditionalWeakTable<Control, TargetRuntime> s_targets = new();

    public static void SetStyles(
        Control target,
        ImmutableArray<AkcssStyleActivator> styles)
    {
        ArgumentNullException.ThrowIfNull(target);

        s_targets.GetValue(target, static control => new TargetRuntime(control))
            .SetStyles(styles.IsDefault ? [] : styles);
    }

    public static void Refresh(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);

        s_targets.GetValue(target, static control => new TargetRuntime(control))
            .Refresh();
    }

    private sealed class TargetRuntime : IObserver<object?>
    {
        private readonly Control _target;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly HashSet<string> _pendingConflictKeys =
            new(StringComparer.Ordinal);
        private ImmutableArray<AkcssStyleActivator> _styles = [];
        private bool _isApplying;
        private bool _isChanging;
        private bool _isDetached;
        private bool _applyPending;

        public TargetRuntime(Control target)
        {
            _target = target;
            target.AttachedToVisualTree += OnAttachedToVisualTree;
            target.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        public void SetStyles(ImmutableArray<AkcssStyleActivator> styles)
        {
            _isChanging = true;
            _applyPending = false;
            try
            {
                DisposeSubscriptions();
                ResetStyles();
                _styles = styles;

                if (!_isDetached)
                {
                    Subscribe();
                }
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

            if (!_isDetached)
            {
                RequestApply();
            }
        }

        public void RequestApply()
        {
            _applyPending = true;
            if (_isApplying || _isChanging || _isDetached)
            {
                return;
            }

            while (_applyPending && !_isDetached)
            {
                _applyPending = false;
                _pendingConflictKeys.Clear();
                _isApplying = true;
                try
                {
                    ResetStyles();
                    ExecuteStyles();
                }
                finally
                {
                    _isApplying = false;
                }
            }
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
            foreach (var style in _styles)
            {
                if (style is AkcssUtilityCandidateActivator candidate)
                {
                    candidate.Detach(_target);
                }
            }

            foreach (var subscription in _subscriptions)
            {
                subscription.Dispose();
            }

            _subscriptions.Clear();
        }

        private void RequestApplyCandidate(
            AkcssUtilityCandidateActivator candidate)
        {
            if (_applyPending)
            {
                return;
            }

            foreach (var conflictKey in candidate.ConflictKeys)
            {
                _pendingConflictKeys.Add(conflictKey);
            }

            if (_isApplying || _isChanging || _isDetached)
            {
                return;
            }

            while (_pendingConflictKeys.Count > 0 &&
                   !_applyPending &&
                   !_isDetached)
            {
                var key = _pendingConflictKeys.First();
                _pendingConflictKeys.Remove(key);
                _isApplying = true;
                try
                {
                    ResetConflictKey(key);
                    ExecuteConflictKey(key);
                }
                finally
                {
                    _isApplying = false;
                }
            }

            if (_applyPending)
            {
                RequestApply();
            }
        }

        private void ResetConflictKey(string conflictKey)
        {
            for (var styleIndex = _styles.Length - 1;
                 styleIndex >= 0;
                 styleIndex--)
            {
                if (_styles[styleIndex] is not
                    AkcssUtilityCandidateActivator candidate)
                {
                    continue;
                }

                for (var operationIndex = candidate.OperationCount - 1;
                     operationIndex >= 0;
                     operationIndex--)
                {
                    if (string.Equals(
                            candidate.GetOperationConflictKey(
                                operationIndex),
                            conflictKey,
                            StringComparison.Ordinal))
                    {
                        candidate.ResetOperation(
                            operationIndex,
                            _target);
                    }
                }
            }
        }

        private void ExecuteConflictKey(string conflictKey)
        {
            var winner = ResolveUtilityWinner(conflictKey);
            if (winner.HasValue)
            {
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

        private UtilityOperationWinner? ResolveUtilityWinner(
            string conflictKey)
        {
            var contenders = new List<UtilityOperationWinner>();
            AkcssOperationPriority? highestPriority = null;

            foreach (var style in _styles)
            {
                if (style is not
                    AkcssUtilityCandidateActivator candidate)
                {
                    continue;
                }

                for (var operationIndex = 0;
                     operationIndex < candidate.OperationCount;
                     operationIndex++)
                {
                    if (!string.Equals(
                            candidate.GetOperationConflictKey(
                                operationIndex),
                            conflictKey,
                            StringComparison.Ordinal) ||
                        !candidate.IsOperationActive(
                            operationIndex,
                            _target))
                    {
                        continue;
                    }

                    var priority = candidate.GetOperationPriority(
                        operationIndex);
                    if (!highestPriority.HasValue ||
                        priority > highestPriority.Value)
                    {
                        contenders.Clear();
                        highestPriority = priority;
                    }

                    if (priority == highestPriority.Value)
                    {
                        contenders.Add(
                            new UtilityOperationWinner(
                                candidate,
                                operationIndex));
                    }
                }
            }

            if (contenders.Count == 0)
            {
                return null;
            }

            UtilityOperationWinner? unprefixedWinner = null;
            var groupedWinners =
                new Dictionary<string, UtilityOperationWinner>(
                    StringComparer.Ordinal);
            var ungroupedPrefixed =
                new List<UtilityOperationWinner>();

            foreach (var contender in contenders)
            {
                var candidate = contender.Candidate;
                if (!candidate.IsPrefixed)
                {
                    if (!unprefixedWinner.HasValue ||
                        IsLater(
                            contender,
                            unprefixedWinner.Value))
                    {
                        unprefixedWinner = contender;
                    }

                    continue;
                }

                if (candidate.ConflictGroup == null)
                {
                    ungroupedPrefixed.Add(contender);
                    continue;
                }

                if (!groupedWinners.TryGetValue(
                        candidate.ConflictGroup,
                        out var groupWinner) ||
                    candidate.Order >
                        groupWinner.Candidate.Order ||
                    candidate.Order ==
                        groupWinner.Candidate.Order &&
                    IsLater(contender, groupWinner))
                {
                    groupedWinners[candidate.ConflictGroup] =
                        contender;
                }
            }

            UtilityOperationWinner? prefixedWinner = null;
            foreach (var contender in groupedWinners.Values)
            {
                if (!prefixedWinner.HasValue ||
                    IsLater(
                        contender,
                        prefixedWinner.Value))
                {
                    prefixedWinner = contender;
                }
            }

            foreach (var contender in ungroupedPrefixed)
            {
                if (!prefixedWinner.HasValue ||
                    IsLater(
                        contender,
                        prefixedWinner.Value))
                {
                    prefixedWinner = contender;
                }
            }

            if (!prefixedWinner.HasValue)
            {
                return unprefixedWinner;
            }

            if (!unprefixedWinner.HasValue)
            {
                return prefixedWinner;
            }

            return prefixedWinner.Value.Candidate
                .UnprefixedPrecedence switch
            {
                UnprefixedUtilityPrecedence.Below =>
                    unprefixedWinner,
                UnprefixedUtilityPrecedence.Above =>
                    prefixedWinner,
                _ => IsLater(
                        prefixedWinner.Value,
                        unprefixedWinner.Value)
                    ? prefixedWinner
                    : unprefixedWinner,
            };
        }

        private static bool IsLater(
            UtilityOperationWinner left,
            UtilityOperationWinner right)
        {
            if (left.Candidate.SourceOrder !=
                right.Candidate.SourceOrder)
            {
                return left.Candidate.SourceOrder >
                    right.Candidate.SourceOrder;
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
            DisposeSubscriptions();
        }

        private readonly struct UtilityOperationWinner
        {
            public UtilityOperationWinner(
                AkcssUtilityCandidateActivator candidate,
                int operationIndex)
            {
                Candidate = candidate;
                OperationIndex = operationIndex;
            }

            public AkcssUtilityCandidateActivator Candidate { get; }

            public int OperationIndex { get; }

            public bool Is(
                AkcssUtilityCandidateActivator candidate,
                int operationIndex)
            {
                return ReferenceEquals(Candidate, candidate) &&
                    OperationIndex == operationIndex;
            }
        }
    }
}
