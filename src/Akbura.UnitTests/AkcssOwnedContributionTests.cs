using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using System.ComponentModel;
using System.Collections.Immutable;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkcssOwnedContributionTests(ITestOutputHelper output)
{
    private static Task OnDispatcher(Action action) => AvaloniaHeadlessTestSession.GetSession().Dispatch(action, CancellationToken.None);

    [Theory]
    [InlineData(BindingPriority.Animation)]
    [InlineData(BindingPriority.StyleTrigger)]
    [InlineData(BindingPriority.Template)]
    [InlineData(BindingPriority.Style)]
    public Task OwnedLayer_PreservesExternalLocalValueAndUnsetState(BindingPriority priority) => OnDispatcher(() =>
    {
        var target = new Border { Width = 73 };
        var contribution = AkcssPropertyContribution.SetValue(target, Control.WidthProperty, 12d, priority);
        Assert.Equal(priority == BindingPriority.Animation ? 12 : 73, target.Width);
        contribution.Dispose();
        contribution.Dispose();
        Assert.Equal(73, target.Width);
        Assert.True(target.IsSet(Control.WidthProperty));
        target.ClearValue(Control.WidthProperty);
        using (AkcssPropertyContribution.SetValue(target, Control.WidthProperty, 24d, priority))
        {
            Assert.Equal(24, target.Width);
        }
        Assert.False(target.IsSet(Control.WidthProperty));
        Assert.True(double.IsNaN(target.Width));
    });

    [Fact]
    public Task RealExternalBinding_RemainsConnectedAndDeliversValuesAfterRelease() => OnDispatcher(() =>
    {
        var target = new Border();
        var source = new BindingSource { Value = 73 };
        using var external = target.Bind(Control.WidthProperty, new Binding(nameof(BindingSource.Value)) { Source = source });
        using var style = target.SetValue(Control.WidthProperty, 6d, BindingPriority.Style);
        var contribution = AkcssPropertyContribution.SetValue(target, Control.WidthProperty, 12d, BindingPriority.Animation);
        Assert.Equal(12, target.Width);
        source.Value = 81;
        Assert.Equal(12, target.Width);
        contribution.Dispose();
        Assert.Equal(81, target.Width);
        source.Value = 92;
        Assert.Equal(92, target.Width);
        external.Dispose();
        Assert.Equal(6, target.Width);
    });

    [Fact]
    public Task CompetingContributions_ReleaseOnlyTheirOwnEntry() => OnDispatcher(() =>
    {
        var target = new Border();
        using var baseline = target.SetValue(Control.WidthProperty, 7d, BindingPriority.Style);
        var first = AkcssPropertyContribution.SetValue(target, Control.WidthProperty, 12d, BindingPriority.StyleTrigger);
        var second = AkcssPropertyContribution.SetValue(target, Control.WidthProperty, 24d, BindingPriority.StyleTrigger);
        Assert.Equal(24, target.Width);
        second.Dispose();
        Assert.Equal(12, target.Width);
        first.Dispose();
        Assert.Equal(7, target.Width);
    });

    [Fact]
    public Task ReorderedCandidates_KeepCascadeWinnerIndependentOfAvaloniaPriority() => OnDispatcher(() =>
    {
        var target = new Border { Width = 73 };
        var first = Candidate(new Utility(Control.WidthProperty, 12));
        var second = new AkcssUtilityCandidateActivator("width", 1,
            [new AkcssUtilityApplication(new Utility(Control.WidthProperty, 25), static (_, _) => { })],
            [AkcssUtilityValueSource.Create<double>(_ => 25)], bindingPriority: BindingPriority.Animation);
        AkcssRuntime.Reconcile(target, [new("a", "w", "a", "12"), new("b", "w", "b", "25")], index => index == 0 ? first : second).Commit();
        Assert.Equal(25, target.Width);
        AkcssRuntime.Reconcile(target, [new("b", "w", "b", "25"), new("a", "w", "a", "12")], _ => throw new InvalidOperationException()).Commit();
        Assert.Equal(73, target.Width);
        target.ClearValue(Control.WidthProperty);
        Assert.Equal(12, target.Width);
        AkcssRuntime.Reconcile(target, [new("b", "w", "b", "25")], _ => throw new InvalidOperationException()).Commit();
        Assert.Equal(25, target.Width);
        AkcssRuntime.SetStyles(target, []);
        Assert.False(target.IsSet(Control.WidthProperty));
    });

    [Fact]
    public Task PreparationFailure_LeavesWorkingSourcesAndValuesUntouched() => OnDispatcher(() =>
    {
        var target = new Border();
        Apply(target, new Utility(Control.WidthProperty, 12), "old").Commit();
        var installed = AkcssRuntime.GetStyles(target);
        Assert.Throws<InvalidOperationException>(() => AkcssRuntime.Reconcile(target,
            [new("a", "a", "a", "new"), new("b", "b", "b", "new")], index => index == 0
                ? Candidate(new Utility(Control.WidthProperty, 25)) : throw new InvalidOperationException("factory failed")));
        Assert.True(installed == AkcssRuntime.GetStyles(target));
        Assert.Equal(12, target.Width);
        Apply(target, new Utility(Control.WidthProperty, 31), "retry").Commit();
        Assert.Equal(31, target.Width);
        AkcssRuntime.SetStyles(target, []);
    });

    [Fact]
    public Task RepeatedCommit_DoesNotUnlockAnotherPendingTransition() => OnDispatcher(() =>
    {
        var target = new Border();
        var first = Apply(target, new Utility(Control.WidthProperty, 12), "old");
        first.Commit();
        var next = Apply(target, new Utility(Control.WidthProperty, 25), "new");
        first.Commit();
        Assert.Throws<InvalidOperationException>(() => AkcssRuntime.SetStyles(target, []));
        next.Rollback();
        next.Rollback();
        Assert.Equal(12, target.Width);
        AkcssRuntime.SetStyles(target, []);
    });

    [Fact]
    public Task VariantAndPriorityChanges_ReleaseOnlyTheirCurrentOwnedLayer() => OnDispatcher(() =>
    {
        var target = new Border { Width = 73 };
        var active = true;
        var priority = BindingPriority.Style;
        var utility = new Utility(Control.WidthProperty, 12);
        var variant = AkcssUtilityValueSource.CreateWithPriority<bool>(
            _ => new AkcssUtilityPrefixInvocation<bool>(active, priority), recreateOnRefresh: true);
        var candidate = new AkcssUtilityCandidateActivator("width", 0,
            [new AkcssUtilityApplication(utility, static (_, _) => { })],
            [AkcssUtilityValueSource.Create<double>(_ => 12)], variant: variant);
        AkcssRuntime.SetStyles(target, [candidate]);
        Assert.Equal(73, target.Width);
        priority = BindingPriority.Animation;
        AkcssRuntime.RefreshForHotReload(target);
        Assert.Equal(12, target.Width);
        active = false;
        AkcssRuntime.RefreshForHotReload(target);
        Assert.Equal(73, target.Width);
        active = true;
        AkcssRuntime.RefreshForHotReload(target);
        Assert.Equal(12, target.Width);
        AkcssRuntime.SetStyles(target, []);
        Assert.Equal(73, target.Width);
    });

    [Fact]
    public Task InvalidInstallation_DoesNotAcquireAPropertySource() => OnDispatcher(() =>
    {
        var target = new Border();
        Assert.Throws<ArgumentException>(() => AkcssPropertyContribution.SetValue(target, Control.WidthProperty, "invalid", BindingPriority.Style));
        Assert.False(target.IsSet(Control.WidthProperty));
    });

    [Fact]
    public Task DynamicSource_SuspendAndRollbackRetainTheOriginalSubscriptionAndLatestValue() => OnDispatcher(() =>
    {
        var target = new Border();
        var source = new AkcssReconciliationTests.Signal<object?>();
        var contribution = Assert.IsType<AkcssPropertyContribution>(AkcssPropertyContribution.Bind(target, Control.WidthProperty, source, BindingPriority.Style));
        source.Publish(12d);
        Assert.Equal(12, target.Width);
        contribution.Suspend();
        source.Publish(25d);
        Assert.True(double.IsNaN(target.Width));
        contribution.Resume();
        Assert.Equal(25, target.Width);
        Assert.Equal(1, source.Subscriptions);
        contribution.Dispose();
        source.PublishRetired(500d);
        Assert.False(target.IsSet(Control.WidthProperty));
        Assert.Equal(0, source.Subscribers);
    });

    [Fact]
    public Task LocalAndDirectWrites_AreNotAdvertisedAsReversibleContributions() => OnDispatcher(() =>
    {
        var target = new Border { Width = 73 };
        Assert.Throws<InvalidOperationException>(() => AkcssPropertyContribution.SetValue(target, Control.WidthProperty, 12d, BindingPriority.LocalValue));
        Assert.Throws<NotSupportedException>(() => AkcssPropertyContribution.SetValue(target, DirectTarget.ValueProperty, 12d, BindingPriority.Style));
        Assert.Equal(73, target.Width);
        var utility = new Utility(Control.WidthProperty, 12);
        AkcssRuntime.SetStyles(target, [new AkcssUtilityCandidateActivator("width", 0,
            [new AkcssUtilityApplication(utility, static (_, _) => { })], [AkcssUtilityValueSource.Create<double>(_ => 12)])]);
        Assert.Equal(12, target.Width);
        AkcssRuntime.SetStyles(target, []);
        // This is deliberately the established destructive LocalValue fallback,
        // not an invented snapshot restoration of the external source.
        Assert.True(double.IsNaN(target.Width));
    });

    [Fact]
    public Task StructuralAbort_RestoresOldOwnedResourcesAndBaseline() => OnDispatcher(() =>
    {
        using var state = new AkburaRenderState();
        var old = new Utility(Control.WidthProperty, 12);
        var target = ApplyState(state, old, "old");
        var installed = AkburaControl.GetAkcssStyles(target);
        ApplyState(state, new Utility(Control.WidthProperty, 25), "new", complete: false);
        old.Value = 999;
        state.AbortRevision();
        Assert.True(installed == AkburaControl.GetAkcssStyles(target));
        Assert.Equal(12, target.Width);
        state.Dispose();
        Assert.False(target.IsSet(Control.WidthProperty));
        Assert.False(target.GetBaseValue(AkburaControl.AkcssStylesProperty).HasValue);
    });

    [Fact]
    public Task OmissionAbort_RestoresContributionsWithoutReexecutingGeneratedOperations() => OnDispatcher(() =>
    {
        using var state = new AkburaRenderState();
        var old = new Utility(Control.WidthProperty, 12);
        var target = ApplyState(state, old, "old");
        var calls = old.ApplicationCount;
        BeginOmission("aborted");
        state.PrepareRevisionCompletion();
        // A suspended binding remains owned until commit, but supplies UnsetValue.
        Assert.True(double.IsNaN(target.Width));
        state.AbortRevision();
        Assert.Equal(12, target.Width);
        Assert.Equal(calls, old.ApplicationCount);
        BeginOmission("removed");
        state.CompleteRevision();
        Assert.False(target.IsSet(Control.WidthProperty));
        Assert.False(target.GetBaseValue(AkburaControl.AkcssStylesProperty).HasValue);

        void BeginOmission(string revision)
        {
            Assert.True(state.BeginRevision(revision, builder => builder.Add(new AkburaRenderNodeDefinition(
                0, -1, "$root", typeof(Border), null, "border")), static _ => new Border()));
        }
    });

    [Fact]
    public Task CommitDisposeFailure_RestoresOwnedValueAndDoesNotDuplicateOtherSubscriptions() => OnDispatcher(() =>
    {
        using var state = new AkburaRenderState();
        var watch = new AkcssReconciliationTests.Signal<object?>();
        var old = new Utility(Control.WidthProperty, 12) { WatchSource = watch };
        var target = ApplyState(state, old, "old");
        var installed = AkburaControl.GetAkcssStyles(target);
        watch.FailNextDispose = true;
        ApplyState(state, new Utility(Control.WidthProperty, 25), "failed", complete: false);
        old.MigratedProperty = Control.MinWidthProperty;
        Assert.Throws<InvalidOperationException>(state.CompleteRevision);
        Assert.True(installed == AkburaControl.GetAkcssStyles(target));
        Assert.Equal(12, target.Width);
        Assert.Equal(0, target.MinWidth);
        Assert.Equal(1, watch.Subscribers);
        ApplyState(state, new Utility(Control.WidthProperty, 31), "retry");
        Assert.Equal(31, target.Width);
        Assert.Equal(0, watch.Subscribers);
    });

    [Fact]
    public Task ApplicationAndRollbackFailures_AggregateBothAndCleanIndependentResources() => OnDispatcher(() =>
    {
        var target = new Border();
        Apply(target, new Utility(Control.WidthProperty, 12), "old").Commit();
        var source = new AkcssReconciliationTests.Signal<object?> { FailNextDispose = true };
        var failing = new Utility(Control.WidthProperty, 25, Control.HeightProperty) { Fail = true, ResourceSource = source };
        var error = Assert.Throws<AggregateException>(() => Apply(target, failing, "new"));
        var errors = error.Flatten().InnerExceptions;
        Assert.Contains(errors, failure => failure.Message == "second property failed");
        Assert.Contains(errors, failure => failure.Message == "dispose failed");
        Assert.Equal(0, source.Subscribers);
        Assert.Equal(12, target.Width);
        Assert.False(target.IsSet(Control.HeightProperty));
        Apply(target, new Utility(Control.WidthProperty, 31), "retry").Commit();
        Assert.Equal(31, target.Width);
        AkcssRuntime.SetStyles(target, []);
    });

    [Fact]
    public Task RetainedReactiveNeighbor_IsNotAppliedUntilCommit() => OnDispatcher(() =>
    {
        var target = new Border();
        var signal = new AkcssReconciliationTests.Signal<double>();
        var height = Candidate(new Utility(Control.HeightProperty, 24),
            AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity));
        Reconcile("old", 12).Commit();
        signal.Publish(24);
        var transition = Reconcile("new", 25);
        signal.Publish(42);
        Assert.Equal(24, target.Height);
        transition.Commit();
        Assert.Equal(42, target.Height);
        Assert.Same(height, AkcssRuntime.GetStyles(target)[1]);
        Assert.Equal(1, signal.Subscriptions);
        AkcssRuntime.SetStyles(target, []);

        AkcssRuntime.Reconciliation Reconcile(string revision, double value) => AkcssRuntime.Reconcile(target,
            [new("w", "w", "w", revision), new("h", "h", "h", "24")],
            index => index == 0 ? Candidate(new Utility(Control.WidthProperty, value)) : height);
    });

    [Fact]
    public async Task DynamicResource_RollbackRestoresTheLiveOriginalResourceSource()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var target = new Border();
            target.Resources["size"] = 12d;
            var old = new Utility(Control.WidthProperty, 0) { ResourceSource = target.GetResourceObservable("size") };
            Apply(target, old, "old").Commit();
            Assert.Equal(12, target.Width);
            var transition = Apply(target, new Utility(Control.WidthProperty, 25), "new");
            target.Resources["size"] = 31d;
            Assert.Equal(25, target.Width);
            transition.Rollback();
            Assert.Equal(31, target.Width);
            target.Resources["size"] = 42d;
            Assert.Equal(42, target.Width);
            AkcssRuntime.SetStyles(target, []);
            Assert.False(target.IsSet(Control.WidthProperty));
            target.Resources["size"] = 99d;
            Assert.False(target.IsSet(Control.WidthProperty));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DetachDuringPendingRevision_ReleasesBothSnapshotsAndDefersRollbackActivation()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var state = new AkburaRenderState();
            var watch = new AkcssReconciliationTests.Signal<object?>();
            var old = new Utility(Control.WidthProperty, 12) { WatchSource = watch };
            var target = ApplyState(state, old, "old");
            var window = new Window { Content = target };
            try
            {
                window.Show();
                ApplyState(state, new Utility(Control.WidthProperty, 25), "new", complete: false);
                window.Content = null;
                Assert.Equal(0, watch.Subscribers);
                Assert.False(target.IsSet(Control.WidthProperty));
                state.AbortRevision();
                Assert.False(target.IsSet(Control.WidthProperty));
                window.Content = target;
                Assert.Equal(12, target.Width);
                Assert.Equal(1, watch.Subscribers);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("conflicts")]
    [InlineData("multi-property")]
    [InlineData("reactive")]
    [InlineData("rollback")]
    public Task OwnedWorkloads_RecordAllocationAndExecutionWithoutTimingAssertions(string workload) => OnDispatcher(() =>
    {
        foreach (var legacy in new[] { true, false })
        {
            var target = new Border();
            var signal = new AkcssReconciliationTests.Signal<double>();
            var source = AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity);
            var original = new Utility(Control.WidthProperty, 12) { Legacy = legacy };
            Apply(target, original, "initial", workload == "reactive" ? source : null).Commit();
            signal.Publish(12);
            const int iterations = 250;
            for (var index = 0; index < 25; index++)
            {
                Update(index);
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            for (var index = 25; index < iterations + 25; index++)
            {
                Update(index);
            }
            timer.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            output.WriteLine($"{workload}, {(legacy ? "disposable handles" : "owned sources")}: {allocated / iterations} B/update; {timer.Elapsed.TotalMicroseconds / iterations:F2} us/update");
            Assert.True(target.Width >= 12);
            AkcssRuntime.SetStyles(target, []);
            Assert.False(target.IsSet(Control.WidthProperty));

            void Update(int index)
            {
                if (workload == "reactive")
                {
                    signal.Publish(index + 12);
                    return;
                }
                var value = index + 12;
                var revision = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (workload == "rollback")
                {
                    var failed = new Utility(Control.WidthProperty, value, Control.HeightProperty) { Legacy = legacy, Fail = true };
                    Assert.Throws<InvalidOperationException>(() => Apply(target, failed, revision));
                    return;
                }
                if (workload == "conflicts")
                {
                    AkcssRuntime.Reconcile(target,
                        [new("a", "w", "a", revision), new("b", "w", "b", revision)],
                        candidate => Candidate(new Utility(Control.WidthProperty, value + candidate) { Legacy = legacy })).Commit();
                    return;
                }
                Apply(target, new Utility(Control.WidthProperty, value, Control.HeightProperty) { Legacy = legacy }, revision).Commit();
            }
        }
    });

    private static Border ApplyState(AkburaRenderState state, Utility utility, string revision, bool complete = true)
    {
        AkcssReconciliationTests.Begin(state, revision);
        var target = state.GetRequired<Border>(0);
        state.ReconcileAkcssStylesOperation(0, target, [new("slot", "width", "anchor", revision)], _ => Candidate(utility), true);
        if (complete)
        {
            state.CompleteRevision();
        }
        return target;
    }

    [Fact]
    public Task Abort_RestoresCapturedContributionWithoutExecutingReplacedOperationCode() => OnDispatcher(() =>
    {
        var target = new Border();
        var old = new Utility(Control.WidthProperty, 12);
        var first = Apply(target, old, "old");
        first.Commit();
        var replacement = Apply(target, new Utility(Control.WidthProperty, 25), "new");
        old.Value = 999;
        replacement.Rollback();
        Assert.Equal(12, target.Width);
        Assert.Equal(1, old.ApplicationCount);
        Assert.Same(first.Styles[0], AkcssRuntime.GetStyles(target)[0]);
        AkcssRuntime.SetStyles(target, []);
    });

    [Fact]
    public Task FailureAfterFirstProperty_RestoresCapturedValuesWithoutReexecutingOldOperations() => OnDispatcher(() =>
    {
        var target = new Border();
        var old = new Utility(Control.WidthProperty, 12);
        var first = Apply(target, old, "old");
        first.Commit();
        old.Value = 999;
        var failing = new Utility(Control.WidthProperty, 25, Control.HeightProperty) { Fail = true };
        Assert.Throws<InvalidOperationException>(() => Apply(target, failing, "new"));
        Assert.Equal(12, target.Width);
        Assert.Equal(1, old.ApplicationCount);
        Assert.True(double.IsNaN(target.Height));
        AkcssRuntime.SetStyles(target, []);
    });

    [Fact]
    public Task ReactiveNotification_WaitsUntilPendingCascadeCommits() => OnDispatcher(() =>
    {
        var target = new Border();
        var signal = new AkcssReconciliationTests.Signal<double>();
        var old = new Utility(Control.WidthProperty, 12);
        var source = AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity);
        var first = Apply(target, old, "old", source);
        first.Commit();
        signal.Publish(12);
        var changed = Apply(target, new Utility(Control.WidthProperty, 25), "new", source: null);
        signal.Publish(999);
        Assert.Equal(25, target.Width);
        changed.Rollback();
        Assert.Equal(999, target.Width);
        AkcssRuntime.SetStyles(target, []);
    });

    private static AkcssRuntime.Reconciliation Apply(Border target, Utility utility, string revision, AkcssUtilityValueSource? source = null)
    {
        return AkcssRuntime.Reconcile(target, [new("slot", "width", "anchor", revision)], _ => Candidate(utility, source));
    }

    private static AkcssUtilityCandidateActivator Candidate(Utility utility, AkcssUtilityValueSource? source = null)
    {
        return new AkcssUtilityCandidateActivator("width", 0,
            [new AkcssUtilityApplication(utility, static (_, _) => { })],
            [source ?? AkcssUtilityValueSource.Create<double>(_ => utility.Value)], bindingPriority: BindingPriority.Style);
    }

    private sealed class Utility : AkcssUtility<double>
    {
        public double Value { get; set; }
        public bool Fail { get; set; }
        public int ApplicationCount { get; set; }
        public bool Legacy { get; init; }
        public AvaloniaProperty? MigratedProperty { get; set; }
        public IObservable<object?>? WatchSource { get; init; }
        public IObservable<object?>? ResourceSource { get; init; }
        public override IObservable<object?> Watch(object target) => WatchSource ?? base.Watch(target);
        public Utility(AvaloniaProperty property, double value, AvaloniaProperty? second = null)
        {
            Value = value;
            Operations = second == null ? [new Operation(this, property)] : [new Operation(this, property), new Operation(this, second, true)];
        }
        public override ImmutableArray<AkcssUtilityOperation> Operations { get; }
        public override void Update(object target, double value) => throw new NotSupportedException();
    }

    private sealed class Operation(Utility utility, AvaloniaProperty property, bool second = false)
        : AkcssUtilityOperation(utility, "property:" + property.Name, AkcssOperationPriority.Style, second ? 1 : 0)
    {
        public override bool SupportsHotReloadReconciliation => true;
        public override bool SupportsOwnedContributions => true;
        public override AvaloniaProperty HotReloadProperty => property;
        public override bool IsActive(object target, IReadOnlyList<object?> arguments) => true;
        public override void Update(object target, IReadOnlyList<object?> arguments) => ((AvaloniaObject)target).SetValue(property, (double)arguments[0]!);
        public override IDisposable Apply(object target, IReadOnlyList<object?> arguments, BindingPriority priority)
        {
            utility.ApplicationCount++;
            if (second && utility.Fail)
            {
                throw new InvalidOperationException("second property failed");
            }
            if (!second && utility.ResourceSource != null)
            {
                return AkcssPropertyContribution.Bind((AvaloniaObject)target, property, utility.ResourceSource, priority);
            }
            if (utility.Legacy)
            {
                return ((AvaloniaObject)target).SetValue(property, (double)arguments[0]!, priority)!;
            }
            return AkcssPropertyContribution.SetValue((AvaloniaObject)target, utility.MigratedProperty ?? property, (double)arguments[0]!, priority);
        }
    }

    private sealed class BindingSource : INotifyPropertyChanged
    {
        private double _value;
        public double Value
        {
            get => _value;
            set
            {
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class DirectTarget : AvaloniaObject
    {
        public static readonly DirectProperty<DirectTarget, double> ValueProperty =
            AvaloniaProperty.RegisterDirect<DirectTarget, double>(nameof(Value), target => target.Value, (target, value) => target.Value = value);
        public double Value { get; set; }
    }
}
