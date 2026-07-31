using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Akbura.Markup;
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
        private ImmutableArray<AkcssStyleActivator> _styles = [];
        private bool _isApplying;
        private bool _isChanging;
        private bool _isDetached;
        private bool _applyPending;
        private readonly HashSet<string> _pendingConflictKeys =
            new(StringComparer.Ordinal);

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
                        RequestApplyConflictKey);
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
                    if (utilityWinners.TryGetValue(
                            candidate.ConflictKey,
                            out var winner) &&
                        ReferenceEquals(candidate, winner))
                    {
                        candidate.Execute(_target);
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

        private void RequestApplyConflictKey(string conflictKey)
        {
            if (string.IsNullOrWhiteSpace(conflictKey) ||
                _applyPending)
            {
                return;
            }

            _pendingConflictKeys.Add(conflictKey);
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
            for (var index = _styles.Length - 1;
                 index >= 0;
                 index--)
            {
                if (_styles[index] is
                        AkcssUtilityCandidateActivator candidate &&
                    string.Equals(
                        candidate.ConflictKey,
                        conflictKey,
                        StringComparison.Ordinal))
                {
                    candidate.Reset(_target);
                }
            }
        }

        private void ExecuteConflictKey(string conflictKey)
        {
            var winner = ResolveUtilityWinner(conflictKey);
            winner?.Execute(_target);
        }

        private Dictionary<string, AkcssUtilityCandidateActivator>
            ResolveUtilityWinners()
        {
            var result =
                new Dictionary<string, AkcssUtilityCandidateActivator>(
                    StringComparer.Ordinal);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var style in _styles)
            {
                if (style is AkcssUtilityCandidateActivator candidate)
                {
                    keys.Add(candidate.ConflictKey);
                }
            }

            foreach (var key in keys)
            {
                var winner = ResolveUtilityWinner(key);
                if (winner != null)
                {
                    result.Add(key, winner);
                }
            }

            return result;
        }

        private AkcssUtilityCandidateActivator? ResolveUtilityWinner(
            string conflictKey)
        {
            AkcssUtilityCandidateActivator? unprefixedWinner = null;
            var groupedWinners =
                new Dictionary<string, AkcssUtilityCandidateActivator>(
                    StringComparer.Ordinal);
            var ungroupedPrefixed =
                new List<AkcssUtilityCandidateActivator>();

            foreach (var style in _styles)
            {
                if (style is not
                        AkcssUtilityCandidateActivator candidate ||
                    !string.Equals(
                        candidate.ConflictKey,
                        conflictKey,
                        StringComparison.Ordinal) ||
                    !candidate.IsReady ||
                    !candidate.IsActive)
                {
                    continue;
                }

                if (!candidate.IsPrefixed)
                {
                    if (unprefixedWinner == null ||
                        candidate.SourceOrder >=
                            unprefixedWinner.SourceOrder)
                    {
                        unprefixedWinner = candidate;
                    }

                    continue;
                }

                if (candidate.ConflictGroup == null)
                {
                    ungroupedPrefixed.Add(candidate);
                    continue;
                }

                if (!groupedWinners.TryGetValue(
                        candidate.ConflictGroup,
                        out var groupWinner) ||
                    candidate.Order > groupWinner.Order ||
                    candidate.Order == groupWinner.Order &&
                    candidate.SourceOrder >
                        groupWinner.SourceOrder)
                {
                    groupedWinners[candidate.ConflictGroup] =
                        candidate;
                }
            }

            AkcssUtilityCandidateActivator? prefixedWinner = null;
            foreach (var candidate in groupedWinners.Values)
            {
                if (prefixedWinner == null ||
                    candidate.SourceOrder >
                        prefixedWinner.SourceOrder)
                {
                    prefixedWinner = candidate;
                }
            }

            foreach (var candidate in ungroupedPrefixed)
            {
                if (prefixedWinner == null ||
                    candidate.SourceOrder >
                        prefixedWinner.SourceOrder)
                {
                    prefixedWinner = candidate;
                }
            }

            if (prefixedWinner == null)
            {
                return unprefixedWinner;
            }

            if (unprefixedWinner == null)
            {
                return prefixedWinner;
            }

            return prefixedWinner.UnprefixedPrecedence switch
            {
                UnprefixedUtilityPrecedence.Below =>
                    unprefixedWinner,
                UnprefixedUtilityPrecedence.Above =>
                    prefixedWinner,
                _ => prefixedWinner.SourceOrder >
                        unprefixedWinner.SourceOrder
                    ? prefixedWinner
                    : unprefixedWinner,
            };
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
    }
}
