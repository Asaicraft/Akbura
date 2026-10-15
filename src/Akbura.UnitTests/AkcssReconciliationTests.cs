using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class AkcssReconciliationTests
{
    [Fact]
    public void OneChangedUtility_RetainsOtherInstancesAndOnlyExecutesItsConflictKey()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        Apply(state, "first", [Descriptor("width", "12"), Descriptor("height", "24")],
            index => Create(probe, index == 0 ? "width" : "height", index == 0 ? 12 : 24));
        var target = state.GetRequired<Border>(0);
        var original = AkburaControl.GetAkcssStyles(target);
        probe.Clear();
        Apply(state, "second", [Descriptor("width", "13"), Descriptor("height", "24")],
            index => Create(probe, index == 0 ? "width" : "height", index == 0 ? 13 : 24));
        var current = AkburaControl.GetAkcssStyles(target);
        Assert.NotSame(original[0], current[0]);
        Assert.Same(original[1], current[1]);
        Assert.Equal(1, probe.Constructions);
        Assert.DoesNotContain("height:reset", probe.Events);
        Assert.DoesNotContain("height:execute", probe.Events);
        Assert.Equal(13, target.Width);
        Assert.Equal(24, target.Height);
    }

    internal static AkcssActivatorDescriptor Descriptor(string family, string value, string? identity = null) =>
        new(identity ?? family, family, family + ":" + value, value);

    [Fact]
    public void InsertBeforeExistingDeclarations_ConstructsOnlyTheAddedCandidate()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        Apply(state, "one", [Descriptor("width", "12")], _ => Create(probe, "width", 12));
        var target = state.GetRequired<Border>(0);
        var width = Assert.Single(AkburaControl.GetAkcssStyles(target));
        probe.Clear();
        Apply(state, "two", [Descriptor("height", "24"), Descriptor("width", "12")],
            index => Create(probe, index == 0 ? "height" : "width", index == 0 ? 24 : 12));
        Assert.Same(width, AkburaControl.GetAkcssStyles(target)[1]);
        Assert.Equal(1, probe.Constructions);
        Assert.DoesNotContain("width:reset", probe.Events);
        Assert.DoesNotContain("width:execute", probe.Events);
        Assert.Equal(24, target.Height);
    }

    [Fact]
    public void RemovingWinner_ExposesTheRetainedShadowedCandidate()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        Apply(state, "one", [Descriptor("width", "12", "w0"), Descriptor("width", "13", "w1"), Descriptor("height", "24")],
            index => Create(probe, index == 2 ? "height" : "width", index == 2 ? 24 : 12 + index));
        var target = state.GetRequired<Border>(0);
        var original = AkburaControl.GetAkcssStyles(target);
        Assert.Equal(13, target.Width);
        probe.Clear();
        Apply(state, "two", [Descriptor("width", "12", "w0"), Descriptor("height", "24")],
            _ => throw new InvalidOperationException("Retained declarations must not invoke factories."));
        Assert.Same(original[0], AkburaControl.GetAkcssStyles(target)[0]);
        Assert.Same(original[2], AkburaControl.GetAkcssStyles(target)[1]);
        Assert.Equal(12, target.Width);
        Assert.DoesNotContain("height:reset", probe.Events);
    }

    [Fact]
    public void ReorderingConflictingCandidates_UsesSidecarPrecedenceWithoutMutatingSourceOrder()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        Apply(state, "one", [Descriptor("width", "12", "w0"), Descriptor("width", "13", "w1"), Descriptor("height", "24")],
            index => Create(probe, index == 2 ? "height" : "width", index == 2 ? 24 : 12 + index));
        var target = state.GetRequired<Border>(0);
        var old = AkburaControl.GetAkcssStyles(target);
        probe.Clear();
        Apply(state, "two", [Descriptor("width", "13", "w1"), Descriptor("height", "24"), Descriptor("width", "12", "w0")],
            _ => throw new InvalidOperationException("A reorder does not require new activators."));
        Assert.Same(old[1], AkburaControl.GetAkcssStyles(target)[0]);
        Assert.Same(old[0], AkburaControl.GetAkcssStyles(target)[2]);
        Assert.Equal(0, ((AkcssUtilityCandidateActivator)old[0]).SourceOrder);
        Assert.Equal(12, target.Width);
        Assert.DoesNotContain("height:reset", probe.Events);
    }

    [Fact]
    public void AmbiguousDuplicates_AreReplacedWithoutLosingAnUnrelatedUniqueCandidate()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        var descriptors = ImmutableArray.Create(Descriptor("width", "12", "w0"), Descriptor("width", "12", "w1"), Descriptor("height", "24"));
        Apply(state, "one", descriptors, index => Create(probe, index == 2 ? "height" : "width", index == 2 ? 24 : 12));
        var target = state.GetRequired<Border>(0);
        var old = AkburaControl.GetAkcssStyles(target);
        probe.Clear();
        Apply(state, "two", descriptors, index => Create(probe, index == 2 ? "height" : "width", index == 2 ? 24 : 12));
        Assert.NotSame(old[0], AkburaControl.GetAkcssStyles(target)[0]);
        Assert.NotSame(old[1], AkburaControl.GetAkcssStyles(target)[1]);
        Assert.Same(old[2], AkburaControl.GetAkcssStyles(target)[2]);
        Assert.Equal(2, probe.Constructions);
    }

    [Fact]
    public void AttachmentFailure_RestoresOldValuesInstancesAndSubscriptionsWithoutTouchingOtherKeys()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        var signal = new Signal<double>();
        Apply(state, "one", [Descriptor("width", "12"), Descriptor("height", "24")], index =>
            Create(probe, index == 0 ? "width" : "height", index == 0 ? 12 : 24,
                index == 0 ? AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity) : null));
        var target = state.GetRequired<Border>(0);
        var old = AkburaControl.GetAkcssStyles(target);
        signal.Publish(17);
        probe.Clear();
        Begin(state, "failed");
        var error = Assert.Throws<InvalidOperationException>(() => state.ReconcileAkcssStylesOperation(0, target,
            [Descriptor("width", "failed"), Descriptor("height", "24")], index =>
                Create(probe, "width", 22, AkcssUtilityValueSource.CreateObservable<double, double>(
                    _ => throw new InvalidOperationException("attach failed"), AkcssUtilityValueSource.Identity)), true));
        Assert.Equal("attach failed", error.Message);
        Assert.True(old == AkburaControl.GetAkcssStyles(target));
        Assert.Equal(1, signal.Subscribers);
        Assert.Equal(17, target.Width);
        Assert.DoesNotContain("height:reset", probe.Events);
        signal.Publish(18);
        Assert.Equal(18, target.Width);
        Apply(state, "retry", [Descriptor("width", "25"), Descriptor("height", "24")], _ => Create(probe, "width", 25));
        Assert.Equal(25, target.Width);
        Assert.Equal(0, signal.Subscribers);
        signal.PublishRetired(500);
        Assert.Equal(25, target.Width);
    }

    [Fact]
    public void AbortingAStagedRevision_RestoresOrderAndLeavesRetainedSubscriptionsUntouched()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        var signal = new Signal<double>();
        Apply(state, "one", [Descriptor("width", "12"), Descriptor("height", "24")], index =>
            Create(probe, index == 0 ? "width" : "height", index == 0 ? 12 : 24,
                index == 1 ? AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity) : null));
        signal.Publish(24);
        var target = state.GetRequired<Border>(0);
        var old = AkburaControl.GetAkcssStyles(target);
        probe.Clear();
        Begin(state, "two");
        state.ReconcileAkcssStylesOperation(0, target, [Descriptor("height", "24"), Descriptor("width", "13")],
            _ => Create(probe, "width", 13), true);
        Assert.Equal(13, target.Width);
        state.AbortRevision();
        Assert.True(old == AkburaControl.GetAkcssStyles(target));
        Assert.Equal(12, target.Width);
        Assert.Equal(1, signal.Subscriptions);
        Assert.DoesNotContain("height:reset", probe.Events);
        signal.Publish(31);
        Assert.Equal(31, target.Height);
    }

    [Fact]
    public void RetainedPriorityContribution_RemainsInstalledUnderAnExternalLocalValue()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        Apply(state, "one", [Descriptor("width", "12"), Descriptor("height", "24")], index =>
            Create(probe, index == 0 ? "width" : "height", index == 0 ? 12 : 24, priority: index == 1 ? BindingPriority.Style : null));
        var target = state.GetRequired<Border>(0);
        target.Height = 777;
        var height = AkburaControl.GetAkcssStyles(target)[1];
        probe.Clear();
        Apply(state, "two", [Descriptor("width", "13"), Descriptor("height", "24")], _ => Create(probe, "width", 13));
        Assert.Same(height, AkburaControl.GetAkcssStyles(target)[1]);
        Assert.Equal(777, target.Height);
        target.ClearValue(Control.HeightProperty);
        Assert.Equal(24, target.Height);
        Assert.DoesNotContain("height:reset", probe.Events);
        Assert.DoesNotContain("height:apply", probe.Events);
    }

    internal sealed class Signal<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _current = [];
        private readonly List<IObserver<T>> _retired = [];
        public int Subscribers => _current.Count;
        public int Subscriptions { get; private set; }
        public bool FailNextDispose { get; set; }
        public IDisposable Subscribe(IObserver<T> observer)
        {
            Subscriptions++;
            _current.Add(observer);
            return new Subscription(() =>
            {
                _current.Remove(observer);
                _retired.Add(observer);
                if (FailNextDispose)
                {
                    FailNextDispose = false;
                    throw new InvalidOperationException("dispose failed");
                }
            });
        }
        public void Publish(T value)
        {
            foreach (var observer in _current.ToArray())
            {
                observer.OnNext(value);
            }
        }
        public void PublishRetired(T value)
        {
            foreach (var observer in _retired.ToArray())
            {
                observer.OnNext(value);
            }
        }
    }

    [Fact]
    public void OmissionAndAbort_RestoreTheOwnedSnapshotAndEventuallyRestoreThePublicBaseline()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        Begin(state, "one");
        var target = state.GetRequired<Border>(0);
        var baseline = ImmutableArray.Create<AkcssStyleActivator>(Create(probe, "width", 8));
        AkburaControl.SetAkcssStyles(target, baseline);
        state.ReconcileAkcssStylesOperation(0, target, [Descriptor("width", "12"), Descriptor("height", "24")],
            index => Create(probe, index == 0 ? "width" : "height", index == 0 ? 12 : 24), true);
        state.CompleteRevision();
        var owned = AkburaControl.GetAkcssStyles(target);
        BeginWithoutOperation(state, "omit-abort");
        state.PrepareRevisionCompletion();
        Assert.True(baseline == AkburaControl.GetAkcssStyles(target));
        state.AbortRevision();
        Assert.True(owned == AkburaControl.GetAkcssStyles(target));
        Apply(state, "two", [Descriptor("width", "13"), Descriptor("height", "24")], _ => Create(probe, "width", 13));
        Assert.Same(owned[1], AkburaControl.GetAkcssStyles(target)[1]);
        BeginWithoutOperation(state, "omit-commit");
        state.CompleteRevision();
        Assert.True(baseline == AkburaControl.GetAkcssStyles(target));
        Assert.Equal(8, target.Width);
        Assert.True(double.IsNaN(target.Height));
        Assert.Throws<InvalidOperationException>(() => AkburaControl.SetAkcssStyles(target, baseline));
    }

    [Fact]
    public void RefreshingAnUnchangedFrameCondition_DoesNotResetAnUnrelatedConflictKey()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        var active = true;
        Apply(state, "one", [Descriptor("width", "12"), Descriptor("height", "24")], index =>
            Create(probe, index == 0 ? "width" : "height", index == 0 ? 12 : 24,
                condition: index == 1 ? () => active : null));
        var target = state.GetRequired<Border>(0);
        probe.Clear();
        Apply(state, "two", [Descriptor("width", "13"), Descriptor("height", "24")], _ => Create(probe, "width", 13));
        AkburaControl.RefreshAkcssStylesForHotReload(target);
        Assert.DoesNotContain("height:reset", probe.Events);
        Assert.DoesNotContain("height:execute", probe.Events);
        active = false;
        probe.Clear();
        AkburaControl.RefreshAkcssStylesForHotReload(target);
        Assert.True(double.IsNaN(target.Height));
        Assert.DoesNotContain("width:reset", probe.Events);
    }

    [Fact]
    public void RetiredUtilityWatchObservers_AreInactiveEvenAfterTheSameCandidateReattaches()
    {
        var probe = new Probe();
        var signal = new Signal<object?>();
        var utility = new Utility(probe, "width", signal);
        var candidate = new AkcssUtilityCandidateActivator("width", 0,
            [new AkcssUtilityApplication(utility, static (_, _) => { })],
            [AkcssUtilityValueSource.Create<double>(_ => 12)]);
        var target = new Border();
        var changes = 0;
        candidate.Attach(target, _ => changes++);
        candidate.Detach(target);
        candidate.Attach(target, _ => changes++);
        changes = 0;
        signal.PublishRetired(null);
        Assert.Equal(0, changes);
        signal.Publish(null);
        Assert.Equal(1, changes);
        candidate.Detach(target);
        Assert.Equal(0, signal.Subscribers);
    }

    [Fact]
    public void NonControlTarget_ReconcilesAndRestoresItsOriginalRuntimeCascade()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        var target = new AvaloniaObject();
        var baseline = ImmutableArray.Create<AkcssStyleActivator>(CreateForObject("width", 8));
        AkburaControl.SetAkcssStyles((object)target, baseline);
        ApplyObject("one", "12");
        var owned = AkcssRuntime.GetStyles(target);
        ApplyObject("two", "13");
        Assert.Same(owned[1], AkcssRuntime.GetStyles(target)[1]);
        Assert.Equal(13, target.GetValue(Control.WidthProperty));
        state.Dispose();
        Assert.True(baseline == AkcssRuntime.GetStyles(target));
        Assert.Equal(8, target.GetValue(Control.WidthProperty));

        void ApplyObject(string revision, string width)
        {
            Assert.True(state.BeginRevision(revision, builder => builder.Add(new AkburaRenderNodeDefinition(
                0, -1, "$root", typeof(AvaloniaObject), null, "object")), _ => target));
            Assert.True(state.ShouldApplyOwnedOperation(0, "$akcss", revision));
            state.ReconcileAkcssStylesOperation(0, target, [Descriptor("width", width), Descriptor("height", "24")],
                index => CreateForObject(index == 0 ? "width" : "height", index == 0 ? double.Parse(width) : 24), true);
            state.CompleteRevision();
        }
        AkcssStyleActivator CreateForObject(string key, double value) =>
            Create(probe, key, value, AkcssUtilityValueSource.CreateForObject<double>(_ => value));
    }

    private static void BeginWithoutOperation(AkburaRenderState state, string revision) =>
        Assert.True(state.BeginRevision(revision, builder => builder.Add(new AkburaRenderNodeDefinition(
            0, -1, "$root", typeof(Border), null, "border")), static _ => new Border()));

    [Fact]
    public void FailureWhileCommittingRetiredSubscriptions_RestoresWithoutDuplicatingTheOtherSources()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        var width = new Signal<double>();
        var height = new Signal<double>();
        Apply(state, "one", [Descriptor("width", "old"), Descriptor("height", "old")], index =>
            Create(probe, index == 0 ? "width" : "height", 12,
                AkcssUtilityValueSource.CreateObservable<double, double>(_ => index == 0 ? width : height, AkcssUtilityValueSource.Identity)));
        width.Publish(12);
        height.Publish(24);
        var target = state.GetRequired<Border>(0);
        var old = AkburaControl.GetAkcssStyles(target);
        width.FailNextDispose = true;
        Begin(state, "failed");
        state.ReconcileAkcssStylesOperation(0, target, [Descriptor("width", "13"), Descriptor("height", "25")],
            index => Create(probe, index == 0 ? "width" : "height", index == 0 ? 13 : 25), true);
        Assert.Equal("dispose failed", Assert.Throws<InvalidOperationException>(state.CompleteRevision).Message);
        Assert.True(old == AkburaControl.GetAkcssStyles(target));
        Assert.Equal(1, width.Subscribers);
        Assert.Equal(1, height.Subscribers);
        Assert.Equal(1, height.Subscriptions);
        width.Publish(14);
        height.Publish(26);
        width.PublishRetired(900);
        Assert.Equal(14, target.Width);
        Assert.Equal(26, target.Height);
        Apply(state, "retry", [Descriptor("width", "15"), Descriptor("height", "27")],
            index => Create(probe, index == 0 ? "width" : "height", index == 0 ? 15 : 27));
        Assert.Equal(0, width.Subscribers);
        Assert.Equal(0, height.Subscribers);
    }

    [Fact]
    public void ReentrantPropertyReplacement_AbortsWithoutDetachingTheRetainedCascade()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        var signal = new Signal<double>();
        Apply(state, "one", [Descriptor("width", "12"), Descriptor("height", "24")], index =>
            Create(probe, index == 0 ? "width" : "height", index == 0 ? 12 : 24,
                index == 1 ? AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity) : null));
        signal.Publish(24);
        var target = state.GetRequired<Border>(0);
        var old = AkburaControl.GetAkcssStyles(target);
        var reenter = true;
        target.PropertyChanged += (_, args) =>
        {
            if (reenter && args.Property == AkburaControl.AkcssStylesProperty)
            {
                reenter = false;
                AkburaControl.ReplaceAkcssStylesForHotReload(target, [Create(probe, "width", 500)]);
            }
        };
        Begin(state, "failed");
        Assert.Throws<InvalidOperationException>(() => state.ReconcileAkcssStylesOperation(0, target,
            [Descriptor("width", "13"), Descriptor("height", "24")], _ => Create(probe, "width", 13), true));
        Assert.True(old == AkburaControl.GetAkcssStyles(target));
        Assert.Equal(1, signal.Subscriptions);
        Assert.Equal(1, signal.Subscribers);
        Assert.Equal(12, target.Width);
        signal.Publish(26);
        Assert.Equal(26, target.Height);
    }

    [Fact]
    public void ModifiedDeclaration_KeepsItsInstalledIdentityAndInsertionDoesNotStealThatSlot()
    {
        var probe = new Probe();
        var target = new Border();
        try
        {
            var first = AkcssRuntime.Reconcile(target,
                [Descriptor("width", "12", "width/0"), Descriptor("width", "13", "width/1")],
                index => Create(probe, "width", index == 0 ? 12 : 13));
            first.Commit();
            var modified = AkcssRuntime.Reconcile(target,
                [Descriptor("width", "14", "width/0"), Descriptor("width", "13", "width/1")],
                _ => Create(probe, "width", 14));
            modified.Commit();
            Assert.Equal(first.Descriptors[0].Identity, modified.Descriptors[0].Identity);
            Assert.NotEqual(first.Descriptors[0].Revision, modified.Descriptors[0].Revision);
            Assert.Same(first.Styles[1], modified.Styles[1]);
            var inserted = AkcssRuntime.Reconcile(target,
                [Descriptor("width", "15", "width/0"), Descriptor("width", "14", "width/1"), Descriptor("width", "13", "width/2")],
                _ => Create(probe, "width", 15));
            inserted.Commit();
            Assert.Equal(modified.Descriptors[0].Identity, inserted.Descriptors[1].Identity);
            Assert.Equal(modified.Descriptors[1].Identity, inserted.Descriptors[2].Identity);
            Assert.NotEqual(inserted.Descriptors[0].Identity, inserted.Descriptors[1].Identity);
            Assert.Equal(3, inserted.Descriptors.Select(value => value.Identity).Distinct(StringComparer.Ordinal).Count());
            Assert.Same(modified.Styles[0], inserted.Styles[1]);
            Assert.Same(modified.Styles[1], inserted.Styles[2]);
            Assert.Equal(13, target.Width);
        }
        finally
        {
            AkcssRuntime.SetStyles(target, []);
        }
    }

    [Fact]
    public void ReentrantReplacementWhileResettingAProperty_PreservesTheWorkingSubscriptions()
    {
        using var state = new AkburaRenderState();
        var probe = new Probe();
        var signal = new Signal<double>();
        Apply(state, "one", [Descriptor("width", "12"), Descriptor("height", "24")], index =>
            Create(probe, index == 0 ? "width" : "height", index == 0 ? 12 : 24,
                index == 1 ? AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity) : null));
        signal.Publish(24);
        var target = state.GetRequired<Border>(0);
        var old = AkburaControl.GetAkcssStyles(target);
        var reenter = true;
        target.PropertyChanged += (_, args) =>
        {
            if (reenter && args.Property == Control.WidthProperty)
            {
                reenter = false;
                AkburaControl.ReplaceAkcssStylesForHotReload(target, [Create(probe, "width", 500)]);
            }
        };
        Begin(state, "failed");
        Assert.Throws<InvalidOperationException>(() => state.ReconcileAkcssStylesOperation(0, target,
            [Descriptor("width", "13"), Descriptor("height", "24")], _ => Create(probe, "width", 13), true));
        Assert.True(old == AkburaControl.GetAkcssStyles(target));
        Assert.Equal(1, signal.Subscriptions);
        Assert.Equal(1, signal.Subscribers);
        Assert.Equal(12, target.Width);
        signal.Publish(26);
        Assert.Equal(26, target.Height);
    }

    private sealed class Subscription(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    internal static void Begin(AkburaRenderState state, string revision)
    {
        Assert.True(state.BeginRevision(revision, builder => builder.Add(new AkburaRenderNodeDefinition(
            0, -1, "$root", typeof(Border), null, "border")), static _ => new Border()));
        Assert.True(state.ShouldApplyOwnedOperation(0, "$akcss", revision));
    }

    internal static void Apply(AkburaRenderState state, string revision,
        ImmutableArray<AkcssActivatorDescriptor> descriptors, Func<int, AkcssStyleActivator> factory)
    {
        Begin(state, revision);
        state.ReconcileAkcssStylesOperation(0, state.GetRequired<Border>(0), descriptors, factory, true);
        state.CompleteRevision();
    }

    internal static AkcssUtilityCandidateActivator Create(Probe probe, string key, double value,
        AkcssUtilityValueSource? argument = null, AkcssUtilityValueSource? variant = null,
        BindingPriority? priority = null, Func<bool>? condition = null)
    {
        probe.Constructions++;
        var utility = new Utility(probe, key);
        return new AkcssUtilityCandidateActivator(key, 0,
            [new AkcssUtilityApplication(utility, (target, values) => utility.Update(target, (double)values[0]!))],
            [argument ?? AkcssUtilityValueSource.Create<double>(_ => value)], condition: condition, variant: variant, bindingPriority: priority);
    }

    internal sealed class Probe
    {
        public int Constructions { get; set; }
        public List<string> Events { get; } = [];
        public void Clear()
        {
            Constructions = 0;
            Events.Clear();
        }
    }

    private sealed class Utility : AkcssUtility<double>
    {
        private readonly ImmutableArray<AkcssUtilityOperation> _operations;
        private readonly IObservable<object?>? _watch;
        public Utility(Probe probe, string key, IObservable<object?>? watch = null)
        {
            _operations = [new Operation(this, probe, key)];
            _watch = watch;
        }
        public override IObservable<object?> Watch(object target) => _watch ?? base.Watch(target);
        public override ImmutableArray<AkcssUtilityOperation> Operations => _operations;
        public override void Update(object target, double value) => _operations[0].Update(target, [value]);
    }

    private sealed class Operation : AkcssUtilityOperation
    {
        private readonly Probe _probe;
        private readonly string _key;
        private AvaloniaProperty<double> Property => _key == "width" ? Control.WidthProperty : Control.HeightProperty;
        public Operation(AkcssUtility utility, Probe probe, string key) : base(utility, key, AkcssOperationPriority.Style, 0)
        {
            _probe = probe;
            _key = key;
        }
        public override bool IsActive(object target, IReadOnlyList<object?> arguments) => true;
        public override bool SupportsHotReloadReconciliation => true;
        public override AvaloniaProperty HotReloadProperty => Property;
        public override void Update(object target, IReadOnlyList<object?> arguments)
        {
            _probe.Events.Add(_key + ":execute");
            ((AvaloniaObject)target).SetValue(Property, (double)arguments[0]!);
        }
        public override void Reset(object target)
        {
            _probe.Events.Add(_key + ":reset");
            ((AvaloniaObject)target).ClearValue(Property);
        }
        public override void Reset(object target, AvaloniaProperty property)
        {
            _probe.Events.Add(_key + ":reset");
            base.Reset(target, property);
        }
        public override IDisposable Apply(object target, IReadOnlyList<object?> arguments, BindingPriority priority)
        {
            _probe.Events.Add(_key + ":apply");
            return ((AvaloniaObject)target).SetValue(Property, (double)arguments[0]!, priority) ?? throw new InvalidOperationException("No contribution.");
        }
    }
}
