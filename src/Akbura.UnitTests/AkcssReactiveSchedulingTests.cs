using Akbura.Akcss;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Xunit.Abstractions;
using static Akbura.UnitTests.AkcssOptimizationPerformanceTests;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkcssReactiveSchedulingTests(ITestOutputHelper output)
{
    [Fact]
    public Task EqualSourceOrders_PreserveDynamicGroupInsertionAndUngroupedTieBreaks() => OnDispatcher(() =>
    {
        var target = new Border();
        var counts = new Counters();
        var active = new[] { true, true, true, true };
        var styles = Enumerable.Range(0, 4).Select(index =>
        {
            var utility = new Utility(0, counts);
            return (AkcssStyleActivator)new AkcssUtilityCandidateActivator("0", 0,
                [new AkcssUtilityApplication(utility, static (_, _) => { })],
                [AkcssUtilityValueSource.Create<double>(_ => index + 10)],
                variant: AkcssUtilityValueSource.Create<bool>(_ => active[index], recreateOnRefresh: true),
                conflictGroup: index == 3 ? null : index == 1 ? "b" : "a", bindingPriority: BindingPriority.Animation);
        }).ToImmutableArray();
        AkcssRuntime.SetStyles(target, styles);
        Assert.Equal(13, target.GetValue(Operation.Properties[0]));
        active[3] = false;
        AkcssRuntime.RefreshForHotReload(target);
        Assert.Equal(11, target.GetValue(Operation.Properties[0]));
        active[0] = false;
        AkcssRuntime.RefreshForHotReload(target);
        Assert.Equal(12, target.GetValue(Operation.Properties[0]));
        active[3] = true;
        AkcssRuntime.RefreshForHotReload(target);
        Assert.Equal(13, target.GetValue(Operation.Properties[0]));
        AkcssRuntime.SetStyles(target, []);
    });

    [Fact]
    public Task RemovedConflictKeys_DoNotAccumulateEmptyIndexEntries() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(1);
        for (var index = 0; index < 100; index++)
        {
            var utility = new Utility(index + 2000, fixture.Counts);
            var candidate = new AkcssUtilityCandidateActivator("changing", 0,
                [new AkcssUtilityApplication(utility, static (_, _) => { })],
                [AkcssUtilityValueSource.Create<double>(_ => 12)], bindingPriority: BindingPriority.Animation);
            AkcssRuntime.Reconcile(fixture.Target, [new("0", "0", "0", index.ToString())], _ => candidate).Commit();
            var storage = AkcssRuntime.GetStorageInfo(fixture.Target);
            Assert.Equal(1, storage.Keys);
            Assert.Equal(1, storage.Operations);
            AssertEmptyQueue(fixture.Target);
        }
        fixture.Sources[0].Stale?.OnNext(999);
        Assert.Equal(0, fixture.Sources[0].Subscribers);
    });

    [Fact]
    public Task SameKeyBatch_ResolvesOnceWithLatestValueAndNestedScopesAreIdempotent() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(10);
        fixture.Counts.Clear();
        var outer = AkburaControl.BeginAkcssUpdate(fixture.Target);
        using (AkburaControl.BeginAkcssUpdate(fixture.Target))
        {
            fixture.Sources[0].Publish(42);
            fixture.Sources[0].Publish(43);
            fixture.Sources[0].Publish(44);
        }
        Assert.Equal(12d, Value(fixture, 0));
        Assert.Equal(0, fixture.Counts.Eligibility);
        outer.Dispose();
        outer.Dispose();
        Assert.Equal(44d, Value(fixture, 0));
        Assert.Equal(1, fixture.Counts.Eligibility);
        Assert.Equal(1, fixture.Counts.Installed);
        AssertEmptyQueue(fixture.Target);
    });

    [Fact]
    public Task UnbatchedNotifications_AreImmediatelyVisibleBeforePublishReturns() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(10);
        fixture.Counts.Clear();
        for (var index = 42; index <= 44; index++)
        {
            fixture.Sources[0].Publish(index);
            Assert.Equal((double)index, Value(fixture, 0));
        }
        Assert.Equal(3, fixture.Counts.Eligibility);
        Assert.Equal(3, fixture.Counts.Installed);
    });

    [Fact]
    public Task IndependentKeys_AreEachResolvedOnceAndUnrelatedEntriesAreUntouched() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(50);
        fixture.Counts.Clear();
        using (AkburaControl.BeginAkcssUpdate(fixture.Target))
        {
            fixture.Sources[0].Publish(42);
            fixture.Sources[1].Publish(43);
            fixture.Sources[0].Publish(44);
        }
        Assert.Equal(44d, Value(fixture, 0));
        Assert.Equal(43d, Value(fixture, 1));
        Assert.Equal(12d, Value(fixture, 49));
        Assert.Equal(2, fixture.Counts.Eligibility);
        Assert.Equal(2, fixture.Counts.Installed);
        Assert.Equal(0, fixture.Counts.Subscriptions);
    });

    [Fact]
    public Task ReentrantNotifications_UseIterativeDrainAndNeverLoseNewInvalidations() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(10);
        var operation = OperationAt(fixture, 0);
        var remaining = 1000;
        var depth = 0;
        var maxDepth = 0;
        operation.Executing = () =>
        {
            depth++;
            maxDepth = Math.Max(depth, maxDepth);
            if (remaining > 0)
            {
                fixture.Sources[0].Publish(--remaining);
                fixture.Sources[1].Publish(remaining + 50);
            }
            depth--;
        };
        fixture.Sources[0].Publish(1001);
        Assert.Equal(0, remaining);
        Assert.Equal(1, maxDepth);
        Assert.Equal(0d, Value(fixture, 0));
        Assert.Equal(50d, Value(fixture, 1));
        AssertEmptyQueue(fixture.Target);
    });

    [Fact]
    public Task EligibilityChanges_AreFreshEvenWhenIndexedCandidatesDoNotChange() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(10, competing: true);
        var active = true;
        OperationAt(fixture, 9).Active = () => active;
        fixture.Sources[9].Publish(99);
        Assert.Equal(99d, fixture.Target.GetValue(Operation.Properties[0]));
        active = false;
        fixture.Sources[9].Publish(100);
        Assert.Equal(12d, fixture.Target.GetValue(Operation.Properties[0]));
        fixture.Sources[8].Publish(88);
        Assert.Equal(88d, fixture.Target.GetValue(Operation.Properties[0]));
        active = true;
        fixture.Sources[9].Publish(101);
        Assert.Equal(101d, fixture.Target.GetValue(Operation.Properties[0]));
    });

    [Fact]
    public Task ScalarPublication_ReusesOneHandleAndOneSourceSubscription() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(1, scalar: true);
        fixture.Sources[0].Publish(20);
        fixture.Counts.Clear();
        for (var index = 21; index < 121; index++)
        {
            fixture.Sources[0].Publish(index);
            Assert.Equal((double)index, Value(fixture, 0));
        }
        Assert.Equal(0, fixture.Counts.Installed);
        Assert.Equal(0, fixture.Counts.Released);
        Assert.Equal(0, fixture.Counts.Subscriptions);
        Assert.Equal(100, fixture.Counts.Writes);
        Assert.Equal(1, fixture.Sources[0].Subscribers);
    });

    [Theory]
    [InlineData(AkcssValueEqualityPolicy.AlwaysNotify, 4)]
    [InlineData(AkcssValueEqualityPolicy.ImmutableScalar, 2)]
    public Task ScalarEquality_IsExplicitAndSuppressesOnlyEquivalentValues(AkcssValueEqualityPolicy policy, int expected) => OnDispatcher(() =>
    {
        var counts = new Counters();
        var signal = new Signal(counts);
        var source = AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity)
            .WithEqualityPolicy(policy);
        var changes = 0;
        var target = new Border();
        source.Attach(target, () => changes++);
        changes = 0;
        signal.Publish(42);
        signal.Publish(42);
        signal.Publish(42);
        signal.Publish(43);
        Assert.Equal(expected, changes);
        Assert.Throws<InvalidOperationException>(() => source.WithEqualityPolicy(policy));
        source.Detach(target);
        signal.Stale!.OnNext(99);
        Assert.Equal(expected, changes);
    });

    [Fact]
    public Task EqualityPolicy_DoesNotCallCustomEqualityOrSuppressMutableValuesOrRefresh() => OnDispatcher(() =>
    {
        var target = new Border();
        var mutable = new DangerousEquality();
        var source = AkcssUtilityValueSource.Create(_ => mutable, recreateOnRefresh: true)
            .WithEqualityPolicy(AkcssValueEqualityPolicy.ImmutableScalar);
        var changes = 0;
        source.Attach(target, () => changes++);
        source.Refresh(target);
        source.Refresh(target);
        Assert.Equal(3, changes);
        source.Detach(target);
        Assert.True(AkcssValueEquality.IsEquivalent((int?)42, 42, AkcssValueEqualityPolicy.ImmutableScalar));
        Assert.True(AkcssValueEquality.IsEquivalent(null, null, AkcssValueEqualityPolicy.ImmutableScalar));
        Assert.True(AkcssValueEquality.IsEquivalent(BindingPriority.Style, BindingPriority.Style, AkcssValueEqualityPolicy.ImmutableScalar));
        Assert.False(AkcssValueEquality.IsEquivalent(mutable, mutable, AkcssValueEqualityPolicy.ImmutableScalar));
        Assert.False(AkcssValueEquality.IsEquivalent(new Avalonia.Media.SolidColorBrush(), new Avalonia.Media.SolidColorBrush(), AkcssValueEqualityPolicy.ImmutableScalar));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PendingTransaction_DrainsAgainstCommittedOrRestoredSnapshot(bool rollback) => OnDispatcher(() =>
    {
        using var fixture = new Fixture(10);
        fixture.Counts.Clear();
        var desired = fixture.Original.SetItem(0, fixture.Original[0] with { Revision = "next" });
        var transition = AkcssRuntime.Reconcile(fixture.Target, desired, index => fixture.Create(index));
        fixture.Sources[1].Publish(42);
        fixture.Sources[1].Publish(43);
        Assert.Equal(12d, Value(fixture, 1));
        if (rollback)
        {
            transition.Rollback();
        }
        else
        {
            transition.Commit();
        }
        Assert.Equal(43d, Value(fixture, 1));
        Assert.Equal(2, fixture.Counts.Installed);
        AssertEmptyQueue(fixture.Target);
    });

    [Fact]
    public Task StructuralRenderBoundary_CoalescesRetainedTargetNotificationsOnCommitAndAbort() => OnDispatcher(() =>
    {
        using var state = new AkburaRenderState();
        AkcssReconciliationTests.Begin(state, "first");
        var target = state.GetRequired<Border>(0);
        var counts = new Counters();
        var signal = new Signal(counts);
        var utility = new Utility(0, counts);
        var candidate = new AkcssUtilityCandidateActivator("0", 0,
            [new AkcssUtilityApplication(utility, static (_, _) => { })],
            [AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity)],
            bindingPriority: BindingPriority.Animation);
        state.ReconcileAkcssStylesOperation(0, target, [new("0", "0", "0", "initial")], _ => candidate, true);
        state.CompleteRevision();
        foreach (var abort in new[] { false, true })
        {
            AkcssReconciliationTests.Begin(state, abort ? "abort" : "commit");
            counts.Clear();
            signal.Publish(42);
            signal.Publish(43);
            Assert.Equal(0, counts.Eligibility);
            if (abort)
            {
                state.AbortRevision();
            }
            else
            {
                state.ReconcileAkcssStylesOperation(0, target, [new("0", "0", "0", "initial")],
                    _ => throw new InvalidOperationException("Retained factory must remain lazy"), true);
                state.CompleteRevision();
            }
            Assert.Equal(43d, target.GetValue(Operation.Properties[0]));
            Assert.Equal(1, counts.Eligibility);
            AssertEmptyQueue(target);
        }
    });

    [Fact]
    public Task ProvisionalNotifications_SurviveRepeatedFailureOnlyAgainstRestoredCandidates() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(10);
        var desired = fixture.Original.SetItem(0, fixture.Original[0] with { Revision = "failed" });
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var utility = new Utility(0, fixture.Counts, multiProperty: true, fail: true);
            ((Operation)utility.Operations[0]).Executing = () =>
            {
                fixture.Sources[1].Publish(42);
                fixture.Sources[1].Publish(43);
            };
            var candidate = new AkcssUtilityCandidateActivator("0", 0,
                [new AkcssUtilityApplication(utility, static (_, _) => { })],
                [AkcssUtilityValueSource.Create<double>(_ => 20)], bindingPriority: BindingPriority.Animation);
            Assert.Throws<InvalidOperationException>(() => AkcssRuntime.Reconcile(fixture.Target, desired, _ => candidate));
            Assert.Equal(12d, Value(fixture, 0));
            Assert.Equal(43d, Value(fixture, 1));
            AssertEmptyQueue(fixture.Target);
            Assert.Equal(10, AkcssRuntime.GetStorageInfo(fixture.Target).Operations);
        }
        fixture.Change(1001);
        AssertEmptyQueue(fixture.Target);
    });

    [Fact]
    public Task DetachedBatch_DoesNotActivateSourcesAndReattachmentReadsLatestValues() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(10);
        var window = new Window { Content = fixture.Target };
        try
        {
            window.Show();
            window.Content = null;
            Assert.Equal(0, fixture.Sources.Sum(source => source.Subscribers));
            using (AkburaControl.BeginAkcssUpdate(fixture.Target))
            {
                fixture.Sources[0].Publish(42);
                fixture.Sources[0].Publish(43);
                fixture.Change(1001);
            }
            fixture.Sources[0].Stale?.OnNext(999);
            Assert.Equal(0, fixture.Sources.Sum(source => source.Subscribers));
            window.Content = fixture.Target;
            Assert.Equal(43d, Value(fixture, 0));
            Assert.Equal(10, fixture.Sources.Sum(source => source.Subscribers));
            AssertEmptyQueue(fixture.Target);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task DynamicResources_BatchLatestPublicationWithoutRebindingOrLosingRollback() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(1);
        fixture.Target.Resources["size"] = 12d;
        var source = fixture.Target.GetResourceObservable("size");
        var contribution = AkcssPropertyContribution.Bind(fixture.Target, Control.WidthProperty, source, BindingPriority.Animation);
        using (AkburaControl.BeginAkcssUpdate(fixture.Target))
        {
            fixture.Target.Resources["size"] = 42d;
            fixture.Target.Resources["size"] = 43d;
            Assert.Equal(12, fixture.Target.Width);
        }
        Assert.Equal(43, fixture.Target.Width);
        contribution.Dispose();
        fixture.Target.Resources["size"] = 99d;
        Assert.True(double.IsNaN(fixture.Target.Width));
        AssertEmptyQueue(fixture.Target);
    });

    [Fact]
    public Task RetiredSnapshots_AreCollectibleWhileCurrentTargetRemainsAlive() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(10);
        var references = RetireSnapshots(fixture);
        for (var attempt = 0; attempt < 5 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        Assert.Equal(10, AkcssRuntime.GetStorageInfo(fixture.Target).Operations);
        GC.KeepAlive(fixture);
    });

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] RetireSnapshots(Fixture fixture)
    {
        var references = new List<WeakReference>();
        for (var revision = 0; revision < 100; revision++)
        {
            var desired = fixture.Original.SetItem(0, fixture.Original[0] with { Revision = revision.ToString() });
            var old = AkcssRuntime.GetStyles(fixture.Target)[0];
            references.Add(new(old));
            var transition = AkcssRuntime.Reconcile(fixture.Target, desired, index => fixture.Create(index));
            transition.Commit();
            references.Add(new(transition));
        }
        return references.ToArray();
    }

    [Fact]
    public Task RepeatedFailuresAndRetry_LeaveQueuesAndIndexesBoundedAndRejectStaleCallbacks() => OnDispatcher(() =>
    {
        using var fixture = new Fixture(50);
        for (var index = 0; index < 100; index++)
        {
            fixture.Change(index, fail: true);
            fixture.Sources[0].Publish(index + 20);
            fixture.Sources[0].Stale?.OnNext(999);
            Assert.Equal(index + 20d, Value(fixture, 0));
            Assert.Equal(1, fixture.Sources[0].Subscribers);
            AssertEmptyQueue(fixture.Target);
            Assert.Equal(50, AkcssRuntime.GetStorageInfo(fixture.Target).Operations);
        }
        fixture.Change(1001);
        AssertEmptyQueue(fixture.Target);
        Assert.Equal(1, fixture.Sources[0].Subscribers);
    });

    [Fact]
    public Task LargeTree_ThousandsOfUtilitiesRemainBoundedAcrossReactiveAndStructuralUpdates() => OnDispatcher(() =>
    {
        var panel = new StackPanel();
        var fixtures = Enumerable.Range(0, 200).Select(_ => new Fixture(10)).ToArray();
        try
        {
            foreach (var fixture in fixtures)
            {
                panel.Children.Add(fixture.Target);
            }
            for (var revision = 0; revision < 20; revision++)
            {
                foreach (var fixture in fixtures)
                {
                    using (AkburaControl.BeginAkcssUpdate(fixture.Target))
                    {
                        fixture.Sources[0].Publish(revision + 20);
                        fixture.Sources[0].Publish(revision + 21);
                        fixture.Sources[1].Publish(revision + 22);
                    }
                    fixture.Change(revision);
                    AssertEmptyQueue(fixture.Target);
                    Assert.Equal(10, AkcssRuntime.GetStorageInfo(fixture.Target).Operations);
                    Assert.Equal(10, fixture.Sources.Sum(source => source.Subscribers));
                }
            }
            output.WriteLine("200 controls, 2000 utility applications, 20 cycles: 4000 reconciliations and 12000 source emissions; bounded 10 indexed operations/control.");
        }
        finally
        {
            panel.Children.Clear();
            foreach (var fixture in fixtures)
            {
                fixture.Dispose();
                Assert.Equal(0, fixture.Sources.Sum(source => source.Subscribers));
            }
        }
    });

    [Theory]
    [InlineData("removed")]
    [InlineData("committed")]
    [InlineData("aborted")]
    [InlineData("detached")]
    [InlineData("contribution")]
    public async Task Lifetime_NoRuntimeRootRetainsDiscardedObjects(string scenario)
    {
        WeakReference[] references = [];
        await OnDispatcher(() => references = CreateRetiredReferences(scenario));
        for (var attempt = 0; attempt < 5 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await OnDispatcher(() => { });
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive, scenario));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateRetiredReferences(string scenario)
    {
        var fixture = new Fixture(10);
        var old = AkcssRuntime.GetStyles(fixture.Target)[0];
        object? transition = null;
        var references = new List<WeakReference> { new(fixture.Target), new(old), new(fixture.Sources[0]) };
        if (scenario is "committed" or "aborted")
        {
            var desired = fixture.Original.SetItem(0, fixture.Original[0] with { Revision = "next" });
            var pending = AkcssRuntime.Reconcile(fixture.Target, desired, index => fixture.Create(index));
            transition = pending;
            if (scenario == "committed")
            {
                pending.Commit();
            }
            else
            {
                pending.Rollback();
            }
        }
        if (scenario == "detached")
        {
            var window = new Window { Content = fixture.Target };
            window.Show();
            window.Content = null;
            window.Close();
        }
        if (scenario == "contribution")
        {
            var contribution = AkcssPropertyContribution.SetValue(fixture.Target, Control.WidthProperty, 12d, BindingPriority.Animation);
            contribution.Dispose();
            references.Add(new(contribution));
        }
        if (transition != null)
        {
            references.Add(new(transition));
        }
        fixture.Dispose();
        return references.ToArray();
    }

    private static double Value(Fixture fixture, int key) => fixture.Target.GetValue(Operation.Properties[key]);
    private static Operation OperationAt(Fixture fixture, int index) => (Operation)((AkcssUtility)AkcssRuntime.GetStyles(fixture.Target)[index].Style).Operations[0];
    private static void AssertEmptyQueue(object target)
    {
        var storage = AkcssRuntime.GetStorageInfo(target);
        Assert.Equal(0, storage.PendingKeys);
        Assert.Equal(0, storage.PendingContributions);
        Assert.False(storage.Transition);
    }

    private sealed class DangerousEquality
    {
        public override bool Equals(object? obj) => throw new InvalidOperationException("Custom equality must not run");
        public override int GetHashCode() => 0;
    }
}
