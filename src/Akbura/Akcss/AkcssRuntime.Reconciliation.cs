using Avalonia.Controls;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Akbura.Akcss;

internal static partial class AkcssRuntime
{
    internal static bool QueueContributionUpdate(object target, AkcssPropertyContribution contribution)
    {
        return s_targets.TryGetValue(target, out var runtime) && runtime.QueueContribution(contribution);
    }

    internal abstract class Reconciliation
    {
        public abstract ImmutableArray<AkcssStyleActivator> Styles { get; }
        public abstract ImmutableArray<AkcssActivatorDescriptor> Descriptors { get; }
        public abstract void Commit();
        public virtual void PrepareCommit() => Commit();
        public abstract void Rollback();
        public virtual void Complete()
        {
        }
    }

    internal static Reconciliation Reconcile(object target,
        ImmutableArray<AkcssActivatorDescriptor> descriptors, Func<int, AkcssStyleActivator> factory, bool propertySet = true)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(factory);
        return s_targets.GetValue(target, static value => new TargetRuntime(value)).Reconcile(descriptors, factory, propertySet);
    }

    internal static void SetStylesFromProperty(object target, ImmutableArray<AkcssStyleActivator> styles)
    {
        var runtime = s_targets.GetValue(target, static value => new TargetRuntime(value));
        styles = styles.IsDefault ? [] : styles;
        if (runtime.IsPublishingStyles)
        {
            if (runtime.Styles != styles)
            {
                throw new InvalidOperationException("An AKCSS cascade cannot be replaced while its reconciliation is being published.");
            }
            return;
        }
        if (runtime.IsReconcilingStyles)
        {
            throw new InvalidOperationException("An AKCSS cascade cannot be replaced during reconciliation.");
        }
        runtime.SetStyles(styles);
    }

    internal static void RestoreDescriptors(object target, ImmutableArray<AkcssStyleActivator> styles,
        ImmutableArray<AkcssActivatorDescriptor> descriptors)
    {
        var runtime = s_targets.GetValue(target, static value => new TargetRuntime(value));
        if (runtime.Styles != styles)
        {
            throw new InvalidOperationException("The installed AKCSS cascade no longer belongs to this operation.");
        }
        runtime.SetDescriptors(descriptors);
    }

    private sealed partial class TargetRuntime
    {
        private ImmutableArray<AkcssActivatorDescriptor> _descriptors;
        private bool _isPublishing;
        private bool _isReconciling;
        private CascadeTransition? _activeTransition;
        private HashSet<AkcssPropertyContribution>? _pendingContributions;

        public bool QueueContribution(AkcssPropertyContribution contribution)
        {
            if (!CannotFlush)
            {
                return false;
            }
            (_pendingContributions ??= []).Add(contribution);
            return true;
        }

        private void FlushContributions()
        {
            if (_pendingContributions == null || _updateDepth != 0)
            {
                return;
            }
            var pending = _pendingContributions;
            _pendingContributions = null;
            List<Exception>? failures = null;
            foreach (var contribution in pending)
            {
                try
                {
                    contribution.PublishLatest();
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures != null)
            {
                throw new AggregateException("AKCSS queued resource updates failed.", failures);
            }
        }

        public bool IsPublishingStyles => _isPublishing;
        public bool IsReconcilingStyles => _isReconciling || _activeTransition != null;

        private readonly struct ReconciliationScope : IDisposable
        {
            private readonly TargetRuntime _runtime;
            private readonly bool _previous;
            public ReconciliationScope(TargetRuntime runtime)
            {
                _runtime = runtime;
                _previous = runtime._isReconciling;
                runtime._isReconciling = true;
            }
            public void Dispose() => _runtime._isReconciling = _previous;
        }

        public void SetDescriptors(ImmutableArray<AkcssActivatorDescriptor> descriptors)
        {
            _descriptors = descriptors;
            _conflictIndex = new(StringComparer.Ordinal);
        }

        public Reconciliation Reconcile(ImmutableArray<AkcssActivatorDescriptor> descriptors, Func<int, AkcssStyleActivator> factory, bool propertySet)
        {
            if (_isApplying || _isChanging || IsReconcilingStyles || descriptors.IsDefault)
            {
                throw new InvalidOperationException("An AKCSS reconciliation cannot start during a cascade update.");
            }
            using var scope = new ReconciliationScope(this);
            var transition = new CascadeTransition(this, descriptors, factory, propertySet);
            _activeTransition = transition;
            try
            {
                transition.Apply();
                return transition;
            }
            catch (Exception failure)
            {
                try
                {
                    transition.Rollback();
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException("AKCSS reconciliation and restoration failed.", failure, rollbackFailure);
                }
                throw;
            }
        }

        private void PublishStyles(bool isSet = true)
        {
            if (_target is not Control control)
            {
                return;
            }
            var expected = _styles;
            _isPublishing = true;
            try
            {
                if (isSet)
                {
                    control.SetValue(AkburaControl.AkcssStylesProperty, expected);
                }
                else
                {
                    control.ClearValue(AkburaControl.AkcssStylesProperty);
                }
                if (AkburaControl.GetAkcssStyles(control) != expected || _styles != expected)
                {
                    throw new InvalidOperationException("AKCSS styles changed while publishing a reconciliation.");
                }
            }
            finally
            {
                _isPublishing = false;
            }
        }

        private sealed class CascadeTransition : Reconciliation
        {
            private readonly TargetRuntime _runtime;
            private readonly ImmutableArray<AkcssStyleActivator> _oldStyles;
            private readonly ImmutableArray<AkcssActivatorDescriptor> _oldDescriptors;
            private readonly ImmutableArray<AkcssStyleActivator> _newStyles;
            private readonly ImmutableArray<AkcssActivatorDescriptor> _newDescriptors;
            private readonly Dictionary<string, ConflictIndex> _oldIndex;
            private readonly List<AkcssUtilityCandidateActivator> _added = [];
            private readonly List<AkcssUtilityCandidateActivator> _removed = [];
            private readonly List<AkcssUtilityCandidateActivator> _retired = [];
            private readonly List<AkcssUtilityCandidateActivator.ContributionUpdate> _contributions = [];
            private readonly HashSet<string> _affected = new(StringComparer.Ordinal);
            private readonly bool _partial;
            private readonly bool _oldPropertySet;
            private readonly bool _propertySet;
            private bool _started;
            private bool _committed;
            private bool _completed;
            private bool _rolledBack;

            public CascadeTransition(TargetRuntime runtime, ImmutableArray<AkcssActivatorDescriptor> desired,
                Func<int, AkcssStyleActivator> factory, bool propertySet)
            {
                _runtime = runtime;
                _propertySet = propertySet;
                _oldStyles = runtime._styles;
                _oldDescriptors = runtime._descriptors;
                _oldIndex = runtime._conflictIndex;
                _oldPropertySet = runtime._target is not Control control ||
                    control.GetBaseValue(AkburaControl.AkcssStylesProperty).HasValue;
                foreach (var descriptor in desired)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Identity);
                    ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Family);
                    ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.SyntaxIdentity);
                }

                var matches = Match(_oldDescriptors, desired);
                var canRetain = (_oldStyles.IsEmpty || !_oldDescriptors.IsDefault) &&
                    _oldStyles.All(IsSupported) && desired.All(static value => value.Revision != null);
                var styles = new AkcssStyleActivator[desired.Length];
                var instances = new HashSet<AkcssStyleActivator>(desired.Length, ReferenceEqualityComparer.Instance);
                var metadata = desired.ToArray();
                var retained = new HashSet<AkcssStyleActivator>(Math.Min(_oldStyles.Length, desired.Length));
                for (var index = 0; index < styles.Length; index++)
                {
                    var previous = matches[index];
                    if (canRetain && previous >= 0 && _oldDescriptors[previous].Revision == desired[index].Revision)
                    {
                        styles[index] = _oldStyles[previous];
                        metadata[index] = desired[index] with { Identity = _oldDescriptors[previous].Identity };
                        retained.Add(styles[index]);
                    }
                    else
                    {
                        styles[index] = factory(index) ?? throw new InvalidOperationException("An AKCSS factory returned null.");
                        if (previous >= 0)
                        {
                            metadata[index] = desired[index] with { Identity = _oldDescriptors[previous].Identity };
                        }
                    }
                }
                _partial = canRetain && styles.All(IsSupported);
                if (!_partial)
                {
                    for (var index = 0; index < styles.Length; index++)
                    {
                        if (retained.Contains(styles[index]))
                        {
                            styles[index] = factory(index);
                        }
                    }
                    retained.Clear();
                }
                var identities = new HashSet<string>(metadata.Length, StringComparer.Ordinal);
                for (var index = 0; index < metadata.Length; index++)
                {
                    if (matches[index] >= 0)
                    {
                        identities.Add(metadata[index].Identity);
                    }
                }
                for (var index = 0; index < metadata.Length; index++)
                {
                    if (matches[index] >= 0)
                    {
                        continue;
                    }
                    var identity = metadata[index].Identity;
                    var suffix = 0;
                    while (!identities.Add(identity))
                    {
                        identity = metadata[index].Identity + "/new-" + (++suffix).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    metadata[index] = metadata[index] with { Identity = identity };
                }
                _newStyles = styles.Length == 0 ? [] : ImmutableCollectionsMarshal.AsImmutableArray(styles);
                foreach (var style in styles)
                {
                    if (style == null || !instances.Add(style))
                    {
                        throw new InvalidOperationException("An AKCSS declaration must own a distinct activator instance.");
                    }
                }
                _newDescriptors = ImmutableCollectionsMarshal.AsImmutableArray(metadata);
                if (!_partial)
                {
                    return;
                }
                foreach (var old in _oldStyles.Cast<AkcssUtilityCandidateActivator>())
                {
                    if (!retained.Contains(old))
                    {
                        _removed.Add(old);
                        _affected.UnionWith(old.ConflictKeys);
                    }
                }
                var lastOrder = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var index = 0; index < styles.Length; index++)
                {
                    var candidate = (AkcssUtilityCandidateActivator)styles[index];
                    if (!retained.Contains(candidate))
                    {
                        _added.Add(candidate);
                        _affected.UnionWith(candidate.ConflictKeys);
                    }
                    else
                    {
                        foreach (var key in candidate.ConflictKeys)
                        {
                            if (lastOrder.TryGetValue(key, out var previousOrder) && previousOrder > matches[index])
                            {
                                _affected.Add(key);
                            }
                            lastOrder[key] = matches[index];
                        }
                    }
                }
            }

            public override ImmutableArray<AkcssStyleActivator> Styles => _newStyles;
            public override ImmutableArray<AkcssActivatorDescriptor> Descriptors => _newDescriptors;

            private static bool IsSupported(AkcssStyleActivator style) =>
                style is AkcssUtilityCandidateActivator { SupportsHotReloadReconciliation: true };

            public void Apply()
            {
                using var scope = new ReconciliationScope(_runtime);
                _started = true;
                if (!_partial)
                {
                    _runtime._activeTransition = null;
                    try
                    {
                        _runtime.SetStyles(_newStyles);
                    }
                    finally
                    {
                        _runtime._activeTransition = this;
                    }
                    _runtime._descriptors = _newDescriptors;
                    _runtime.PublishStyles(_propertySet);
                    return;
                }
                _runtime._isChanging = true;
                try
                {
                    foreach (var candidate in _oldStyles.Cast<AkcssUtilityCandidateActivator>())
                    {
                        if (candidate.BeginContributionUpdate(_affected) is { } update)
                        {
                            _contributions.Add(update);
                        }
                    }
                    foreach (var update in _contributions)
                    {
                        update.Suspend();
                    }
                    if (!_runtime._isDetached)
                    {
                        foreach (var candidate in _added)
                        {
                            candidate.Attach(_runtime._target, _runtime.RequestApplyCandidate);
                        }
                    }
                    Reset(_oldStyles);
                    _runtime._styles = _newStyles;
                    _runtime._descriptors = _newDescriptors;
                    _runtime._conflictIndex = new(_oldIndex, StringComparer.Ordinal);
                    foreach (var key in _affected)
                    {
                        _runtime._conflictIndex.Remove(key);
                    }
                    foreach (var key in _affected)
                    {
                        if (!_runtime._isDetached)
                        {
                            _runtime.ExecuteConflictKey(key);
                        }
                    }
                    _runtime.PublishStyles(_propertySet);
                }
                finally
                {
                    _runtime._isChanging = false;
                }
            }

            public override void Commit()
            {
                PrepareCommit();
                Complete();
            }

            public override void PrepareCommit()
            {
                if (_rolledBack || _committed)
                {
                    return;
                }
                using var scope = new ReconciliationScope(_runtime);
                foreach (var candidate in _removed)
                {
                    if (_retired.Contains(candidate))
                    {
                        continue;
                    }
                    _retired.Add(candidate);
                    candidate.Detach(_runtime._target);
                }
                _committed = true;
            }

            public override void Complete()
            {
                if (!_committed || _rolledBack || _completed)
                {
                    return;
                }
                using var scope = new ReconciliationScope(_runtime);
                _completed = true;
                // All enclosing render mutations have committed. Only now retire
                // rollback contributions and allow queued reactive invalidations.
                List<Exception>? failures = null;
                foreach (var update in _contributions)
                {
                    Attempt(update.Complete, ref failures);
                }
                _runtime._activeTransition = null;
                ThrowFailures(failures);
                _runtime.ApplyPendingCandidates();
            }

            public override void Rollback()
            {
                if (!_started || _rolledBack)
                {
                    return;
                }
                using var scope = new ReconciliationScope(_runtime);
                _rolledBack = true;
                if (!_partial)
                {
                    _runtime._activeTransition = null;
                    _runtime._pendingContributions = null;
                    _runtime.SetStyles(_oldStyles);
                    _runtime._descriptors = _oldDescriptors;
                    _runtime.PublishStyles(_oldPropertySet);
                    return;
                }
                _runtime._isChanging = true;
                List<Exception>? failures = null;
                try
                {
                    foreach (var candidate in _added)
                    {
                        Attempt(() => candidate.Detach(_runtime._target), ref failures);
                    }
                    Attempt(() => Reset(_newStyles), ref failures);
                    _runtime._styles = _oldStyles;
                    _runtime._descriptors = _oldDescriptors;
                    _runtime._conflictIndex = _oldIndex;
                    if (!_runtime._isDetached)
                    {
                        foreach (var candidate in _retired)
                        {
                            _runtime._isRestoringSources = true;
                            try
                            {
                                Attempt(() => candidate.Attach(_runtime._target, _runtime.RequestApplyCandidate), ref failures);
                            }
                            finally
                            {
                                _runtime._isRestoringSources = false;
                            }
                            if (candidate.HaveArgumentValuesChanged())
                            {
                                _runtime.RequestApplyCandidate(candidate);
                            }
                        }
                    }
                    foreach (var update in _contributions)
                    {
                        Attempt(() => update.Rollback(!_runtime._isDetached), ref failures);
                    }
                    if (!_runtime._isDetached)
                    {
                        foreach (var key in _affected)
                        {
                            Attempt(() => _runtime.ExecuteConflictKey(key, preserveContribution: true), ref failures);
                        }
                    }
                    Attempt(() => _runtime.PublishStyles(_oldPropertySet), ref failures);
                }
                finally
                {
                    _runtime._isChanging = false;
                    _runtime._activeTransition = null;
                }
                Attempt(_runtime.ApplyPendingCandidates, ref failures);
                Attempt(_runtime.FlushContributions, ref failures);
                ThrowFailures(failures);
            }

            public bool ContainsPrevious(AkcssUtilityCandidateActivator candidate) => _oldStyles.Contains(candidate);

            public void DetachPrevious()
            {
                List<Exception>? failures = null;
                foreach (var candidate in _removed)
                {
                    if (!_retired.Contains(candidate))
                    {
                        _retired.Add(candidate);
                        Attempt(() => candidate.Detach(_runtime._target), ref failures);
                    }
                }
                foreach (var update in _contributions)
                {
                    Attempt(update.Detach, ref failures);
                }
                ThrowFailures(failures);
            }

            private static void Attempt(Action action, ref List<Exception>? failures)
            {
                try
                {
                    action();
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }

            private static void ThrowFailures(List<Exception>? failures)
            {
                if (failures?.Count == 1)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
                }
                if (failures != null)
                {
                    throw new AggregateException("AKCSS transition recovery or cleanup failed.", failures);
                }
            }

            private void Reset(ImmutableArray<AkcssStyleActivator> styles)
            {
                List<Exception>? failures = null;
                for (var index = styles.Length - 1; index >= 0; index--)
                {
                    var candidate = (AkcssUtilityCandidateActivator)styles[index];
                    for (var operation = candidate.OperationCount - 1; operation >= 0; operation--)
                    {
                        if (_affected.Contains(candidate.GetOperationConflictKey(operation)))
                        {
                            try
                            {
                                candidate.ResetOperation(operation, _runtime._target);
                            }
                            catch (Exception failure)
                            {
                                (failures ??= []).Add(failure);
                            }
                        }
                    }
                }
                ThrowFailures(failures);
            }

            private static int[] Match(ImmutableArray<AkcssActivatorDescriptor> old, ImmutableArray<AkcssActivatorDescriptor> desired)
            {
                var matches = new int[desired.Length];
                Array.Fill(matches, -1);
                if (old.IsDefaultOrEmpty)
                {
                    return matches;
                }
                var anchors = new Dictionary<(string Family, string Syntax), (int Old, int New)>(Math.Max(old.Length, desired.Length));
                for (var index = 0; index < old.Length; index++)
                {
                    var key = (old[index].Family, old[index].SyntaxIdentity);
                    if (anchors.TryGetValue(key, out var anchor))
                    {
                        anchors[key] = (-2, anchor.New);
                    }
                    else
                    {
                        anchors.Add(key, (index, -1));
                    }
                }
                for (var index = 0; index < desired.Length; index++)
                {
                    var key = (desired[index].Family, desired[index].SyntaxIdentity);
                    if (anchors.TryGetValue(key, out var anchor))
                    {
                        anchors[key] = (anchor.Old, anchor.New == -1 ? index : -2);
                    }
                    else
                    {
                        anchors.Add(key, (-1, index));
                    }
                }
                var used = new bool[old.Length];
                foreach (var anchor in anchors.Values)
                {
                    if (anchor.Old >= 0 && anchor.New >= 0)
                    {
                        matches[anchor.New] = anchor.Old;
                        used[anchor.Old] = true;
                    }
                }
                var families = new Dictionary<string, (int Old, int New)>(StringComparer.Ordinal);
                for (var index = 0; index < old.Length; index++)
                {
                    if (!used[index])
                    {
                        var family = old[index].Family;
                        var group = families.GetValueOrDefault(family, (Old: -1, New: -1));
                        families[family] = (group.Old == -1 ? index : -2, group.New);
                    }
                }
                for (var index = 0; index < desired.Length; index++)
                {
                    if (matches[index] < 0)
                    {
                        var family = desired[index].Family;
                        var group = families.GetValueOrDefault(family, (Old: -1, New: -1));
                        families[family] = (group.Old, group.New == -1 ? index : -2);
                    }
                }
                foreach (var group in families.Values)
                {
                    if (group.Old >= 0 && group.New >= 0)
                    {
                        matches[group.New] = group.Old;
                    }
                }
                return matches;
            }
        }
    }
}
