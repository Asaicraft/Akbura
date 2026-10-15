using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.Akcss;

internal static partial class AkcssRuntime
{
    internal abstract class Reconciliation
    {
        public abstract ImmutableArray<AkcssStyleActivator> Styles { get; }
        public abstract ImmutableArray<AkcssActivatorDescriptor> Descriptors { get; }
        public abstract void Commit();
        public abstract void Rollback();
    }

    internal static Reconciliation Reconcile(object target,
        ImmutableArray<AkcssActivatorDescriptor> descriptors, Func<int, AkcssStyleActivator> factory)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(factory);
        return s_targets.GetValue(target, static value => new TargetRuntime(value)).Reconcile(descriptors, factory);
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

        public bool IsPublishingStyles => _isPublishing;
        public bool IsReconcilingStyles => _isReconciling;

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
        }

        public Reconciliation Reconcile(ImmutableArray<AkcssActivatorDescriptor> descriptors, Func<int, AkcssStyleActivator> factory)
        {
            if (_isApplying || _isChanging || _isReconciling || descriptors.IsDefault)
            {
                throw new InvalidOperationException("An AKCSS reconciliation cannot start during a cascade update.");
            }
            var transition = new CascadeTransition(this, descriptors, factory);
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
            private readonly List<AkcssUtilityCandidateActivator> _added = [];
            private readonly List<AkcssUtilityCandidateActivator> _removed = [];
            private readonly List<AkcssUtilityCandidateActivator> _retired = [];
            private readonly HashSet<string> _affected = new(StringComparer.Ordinal);
            private readonly bool _partial;
            private readonly bool _oldPropertySet;
            private bool _started;
            private bool _committed;
            private bool _rolledBack;

            public CascadeTransition(TargetRuntime runtime, ImmutableArray<AkcssActivatorDescriptor> desired,
                Func<int, AkcssStyleActivator> factory)
            {
                _runtime = runtime;
                _oldStyles = runtime._styles;
                _oldDescriptors = runtime._descriptors;
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
                var metadata = desired.ToArray();
                var retained = new HashSet<AkcssStyleActivator>();
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
                var identities = new HashSet<string>(StringComparer.Ordinal);
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
                _newStyles = ImmutableArray.Create(styles);
                _newDescriptors = ImmutableArray.Create(metadata);
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
                    _runtime.SetStyles(_newStyles);
                    _runtime._descriptors = _newDescriptors;
                    _runtime.PublishStyles();
                    return;
                }
                _runtime._isChanging = true;
                try
                {
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
                    foreach (var key in _affected)
                    {
                        if (!_runtime._isDetached)
                        {
                            _runtime.ExecuteConflictKey(key);
                        }
                    }
                    _runtime.PublishStyles();
                }
                finally
                {
                    _runtime._isChanging = false;
                }
                _runtime.ApplyPendingCandidates();
            }

            public override void Commit()
            {
                if (_rolledBack || _committed)
                {
                    return;
                }
                using var scope = new ReconciliationScope(_runtime);
                _committed = true;
                foreach (var candidate in _removed)
                {
                    _retired.Add(candidate);
                    candidate.Detach(_runtime._target);
                }
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
                    _runtime.SetStyles(_oldStyles);
                    _runtime._descriptors = _oldDescriptors;
                    _runtime.PublishStyles(_oldPropertySet);
                    return;
                }
                _runtime._isChanging = true;
                try
                {
                    foreach (var candidate in _added)
                    {
                        candidate.Detach(_runtime._target);
                    }
                    Reset(_newStyles);
                    _runtime._styles = _oldStyles;
                    _runtime._descriptors = _oldDescriptors;
                    if (!_runtime._isDetached)
                    {
                        if (_committed)
                        {
                            foreach (var candidate in _retired)
                            {
                                candidate.Attach(_runtime._target, _runtime.RequestApplyCandidate);
                            }
                        }
                        foreach (var key in _affected)
                        {
                            _runtime.ExecuteConflictKey(key);
                        }
                    }
                    _runtime.PublishStyles(_oldPropertySet);
                }
                finally
                {
                    _runtime._isChanging = false;
                }
                _runtime.ApplyPendingCandidates();
            }

            private void Reset(ImmutableArray<AkcssStyleActivator> styles)
            {
                for (var index = styles.Length - 1; index >= 0; index--)
                {
                    var candidate = (AkcssUtilityCandidateActivator)styles[index];
                    for (var operation = candidate.OperationCount - 1; operation >= 0; operation--)
                    {
                        if (_affected.Contains(candidate.GetOperationConflictKey(operation)))
                        {
                            candidate.ResetOperation(operation, _runtime._target);
                        }
                    }
                }
            }

            private static int[] Match(ImmutableArray<AkcssActivatorDescriptor> old, ImmutableArray<AkcssActivatorDescriptor> desired)
            {
                var matches = Enumerable.Repeat(-1, desired.Length).ToArray();
                if (old.IsDefaultOrEmpty)
                {
                    return matches;
                }
                var families = new Dictionary<string, (List<int> Old, List<int> New)>(StringComparer.Ordinal);
                for (var index = 0; index < old.Length; index++)
                {
                    GetGroup(old[index].Family).Old.Add(index);
                }
                for (var index = 0; index < desired.Length; index++)
                {
                    GetGroup(desired[index].Family).New.Add(index);
                }
                foreach (var group in families.Values)
                {
                    var oldAnchors = group.Old.GroupBy(index => old[index].SyntaxIdentity)
                        .ToDictionary(values => values.Key, values => values.ToArray(), StringComparer.Ordinal);
                    var used = new HashSet<int>();
                    foreach (var anchor in group.New.GroupBy(index => desired[index].SyntaxIdentity))
                    {
                        if (anchor.Count() == 1 && oldAnchors.TryGetValue(anchor.Key, out var previous) && previous.Length == 1)
                        {
                            matches[anchor.Single()] = previous[0];
                            used.Add(previous[0]);
                        }
                    }
                    var remainingOld = group.Old.Where(index => !used.Contains(index)).ToArray();
                    var remainingNew = group.New.Where(index => matches[index] < 0).ToArray();
                    if (remainingOld.Length == 1 && remainingNew.Length == 1)
                    {
                        matches[remainingNew[0]] = remainingOld[0];
                    }
                }
                return matches;

                (List<int> Old, List<int> New) GetGroup(string family)
                {
                    if (!families.TryGetValue(family, out var group))
                    {
                        group = ([], []);
                        families.Add(family, group);
                    }
                    return group;
                }
            }
        }
    }
}
