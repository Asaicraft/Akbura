using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using System.Collections.Immutable;
using System.ComponentModel;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReactiveUtilityCandidateRuntimeTests
{
    [Fact]
    public async Task StateChanges_UpdateTheCascadeWithoutUpdateOrRefresh()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var width = new State<double>(800);
            var target = new TextBlock();
            var evaluations = 0;
            var candidate = Candidate(Visual.IsVisibleProperty, false, 0, tracker =>
            {
                evaluations++;
                return tracker.State(width, 0) > 1000;
            });
            AkburaControl.SetAkcssStyles(target, [candidate]);
            Assert.True(target.IsVisible);
            width.Value = 1100;
            Assert.False(target.IsVisible);
            width.Value = 1400;
            Assert.False(target.IsVisible);
            width.Value = 800;
            Assert.True(target.IsVisible);
            width.Value = 1100;
            Assert.False(target.IsVisible);
            var reads = evaluations;
            for (var index = 0; index < 10; index++) Assert.True(candidate.Condition);
            Assert.Equal(reads, evaluations); // IsActive never polls the evaluator.
            target.ClearValue(AkburaControl.AkcssStylesProperty);
            width.Value = 800;
            Assert.Equal(reads, evaluations);
            Assert.True(target.IsVisible);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShortCircuitDependencies_AreRetiredAndNotDuplicated()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var source = new Source();
            var target = new TextBlock();
            var candidate = Candidate(Visual.IsVisibleProperty, false, 0, tracker =>
                tracker.Property(source, 0, nameof(Source.Enabled)).Enabled &&
                tracker.Property(source, 1, nameof(Source.Other)).Other);
            AkburaControl.SetAkcssStyles(target, [candidate]);
            Assert.Equal(1, source.Subscribers);
            source.Enabled = true;
            Assert.Equal(2, source.Subscribers);
            Assert.True(target.IsVisible);
            source.Other = true;
            Assert.False(target.IsVisible);
            for (var index = 0; index < 3; index++) source.Other = true;
            Assert.Equal(2, source.Subscribers);
            source.Enabled = false;
            Assert.Equal(1, source.Subscribers);
            Assert.True(target.IsVisible);
            target.ClearValue(AkburaControl.AkcssStylesProperty);
            Assert.Equal(0, source.Subscribers);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CompetingConditions_RestoreTheRemainingWinnerAndBaseContribution()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var source = new Source();
            var target = new Border();
            var baseline = Candidate(Control.WidthProperty, 20d, 0);
            var selected = Candidate(Control.WidthProperty, 30d, 1,
                tracker => tracker.Property(source, 0, nameof(Source.Enabled)).Enabled);
            var error = Candidate(Control.WidthProperty, 40d, 2,
                tracker => tracker.Property(source, 0, nameof(Source.Other)).Other);
            AkburaControl.SetAkcssStyles(target, [baseline, selected, error]);
            Assert.Equal(20, target.Width);
            source.Enabled = true;
            Assert.Equal(30, target.Width);
            source.Other = true;
            Assert.Equal(40, target.Width);
            source.Enabled = false;
            Assert.Equal(40, target.Width);
            source.Other = false;
            Assert.Equal(20, target.Width);
            target.ClearValue(AkburaControl.AkcssStylesProperty);
            Assert.True(double.IsNaN(target.Width));
            Assert.Equal(0, source.Subscribers);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task VisualDetachAndReattach_ReleaseAndRestoreObservations()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var source = new Source();
            var target = new TextBlock();
            var window = new Window { Content = target };
            try
            {
                AkburaControl.SetAkcssStyles(target, [Candidate(Visual.IsVisibleProperty, false, 0,
                    tracker => tracker.Property(source, 0, nameof(Source.Enabled)).Enabled)]);
                window.Show();
                Assert.Equal(1, source.Subscribers);
                source.Enabled = true;
                Assert.False(target.IsVisible);
                window.Content = null;
                Assert.Equal(0, source.Subscribers);
                source.Enabled = false;
                window.Content = target;
                Assert.Equal(1, source.Subscribers);
                Assert.True(target.IsVisible);
                window.Content = null;
                Assert.Equal(0, source.Subscribers);
            }
            finally
            {
                window.Close();
                target.ClearValue(AkburaControl.AkcssStylesProperty);
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReplacingCandidate_ReleasesOldSourceAndDoesNotKeepAStaleContribution()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var oldSource = new Source { Enabled = true };
            var newSource = new Source();
            var target = new TextBlock();
            AkburaControl.SetAkcssStyles(target, [Candidate(Visual.IsVisibleProperty, false, 0,
                tracker => tracker.Property(oldSource, 0, nameof(Source.Enabled)).Enabled)]);
            Assert.False(target.IsVisible);
            AkburaControl.ReplaceAkcssStylesForHotReload(target, [Candidate(Visual.IsVisibleProperty, false, 0,
                tracker => tracker.Property(newSource, 0, nameof(Source.Enabled)).Enabled)]);
            Assert.Equal(0, oldSource.Subscribers);
            Assert.Equal(1, newSource.Subscribers);
            Assert.True(target.IsVisible);
            oldSource.Enabled = false;
            oldSource.Enabled = true;
            Assert.True(target.IsVisible);
            newSource.Enabled = true;
            Assert.False(target.IsVisible);
            target.ClearValue(AkburaControl.AkcssStylesProperty);
            Assert.Equal(0, newSource.Subscribers);
            Assert.True(target.IsVisible);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedInitialEvaluation_ReleasesSubscriptions()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var source = new Source();
            var target = new TextBlock();
            var candidate = Candidate(Visual.IsVisibleProperty, false, 0, tracker =>
            {
                tracker.Property(source, 0, nameof(Source.Enabled));
                throw new InvalidOperationException("condition failed");
            });
            Assert.ThrowsAny<Exception>(() => AkburaControl.SetAkcssStyles(target, [candidate]));
            Assert.Equal(0, source.Subscribers);
            target.ClearValue(AkburaControl.AkcssStylesProperty);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RefreshedCapture_ReusesCandidateAndReleasesTheOldSource()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var first = new Source { Enabled = true };
            var next = new Source();
            var target = new TextBlock();
            var candidate = Candidate(Visual.IsVisibleProperty, false, 0,
                tracker => tracker.Property(first, 0, nameof(Source.Enabled)).Enabled);
            AkburaControl.SetAkcssStyles(target, [candidate]);
            Assert.False(target.IsVisible);
            AkcssUtilityCandidateActivator.RefreshReactiveCondition(target, 0,
                tracker => tracker.Property(next, 0, nameof(Source.Enabled)).Enabled);
            Assert.Same(candidate, Assert.Single(AkburaControl.GetAkcssStyles(target)));
            Assert.Equal(0, first.Subscribers);
            Assert.Equal(1, next.Subscribers);
            Assert.True(target.IsVisible);
            first.Enabled = false;
            first.Enabled = true;
            Assert.True(target.IsVisible);
            next.Enabled = true;
            Assert.False(target.IsVisible);
            target.ClearValue(AkburaControl.AkcssStylesProperty);
            Assert.Equal(0, next.Subscribers);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RefreshedCapture_WhileDetachedDoesNotResumeTheBinding()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var first = new Source();
            var next = new Source { Enabled = true };
            var target = new TextBlock();
            var window = new Window { Content = target };
            try
            {
                AkburaControl.SetAkcssStyles(target, [Candidate(Visual.IsVisibleProperty, false, 0,
                    tracker => tracker.Property(first, 0, nameof(Source.Enabled)).Enabled)]);
                window.Show();
                window.Content = null;
                var detachedValue = target.IsVisible;
                AkcssUtilityCandidateActivator.RefreshReactiveCondition(target, 0,
                    tracker => tracker.Property(next, 0, nameof(Source.Enabled)).Enabled);
                Assert.Equal(0, first.Subscribers);
                Assert.Equal(0, next.Subscribers);
                Assert.Equal(detachedValue, target.IsVisible);
                window.Content = target;
                Assert.False(target.IsVisible);
                Assert.Equal(0, first.Subscribers);
                Assert.Equal(1, next.Subscribers);
            }
            finally
            {
                window.Close();
                target.ClearValue(AkburaControl.AkcssStylesProperty);
            }
            Assert.Equal(0, next.Subscribers);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RefreshedCapture_RejectsUnrelatedActivatorSlots()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var target = new TextBlock();
            AkburaControl.SetAkcssStyles(target, [Candidate(Visual.IsVisibleProperty, false, 0)]);
            Assert.Throws<InvalidOperationException>(() =>
                AkcssUtilityCandidateActivator.RefreshReactiveCondition(target, 0, _ => true));
            Assert.Throws<InvalidOperationException>(() =>
                AkcssUtilityCandidateActivator.RefreshReactiveCondition(target, 1, _ => true));
            target.ClearValue(AkburaControl.AkcssStylesProperty);
            return true;
        }, CancellationToken.None);
    }

    private static AkcssUtilityCandidateActivator Candidate<T>(StyledProperty<T> property, T value, int order,
        Func<ExpressionBindingContext, bool>? condition = null)
    {
        var utility = new PropertyUtility<T>(property, value);
        ImmutableArray<AkcssUtilityApplication> applications = [new(utility, (_, _) => { })];
        return condition == null
            ? new(property.Name, order, applications, bindingPriority: BindingPriority.Style)
            : new(property.Name, order, applications, reactiveCondition: condition, bindingPriority: BindingPriority.Style);
    }

    private sealed class PropertyUtility<T> : ZeroAkcssUtility
    {
        private readonly ImmutableArray<AkcssUtilityOperation> _operations;
        public PropertyUtility(StyledProperty<T> property, T value) => _operations = [new PropertyOperation(this, property, value)];
        public override ImmutableArray<AkcssUtilityOperation> Operations => _operations;
        public override void Update(object target) { }

        private sealed class PropertyOperation(AkcssUtility utility, StyledProperty<T> property, T value)
            : AkcssUtilityOperation(utility, "property:" + property.Name, AkcssOperationPriority.Style, 0)
        {
            public override bool IsActive(object target, IReadOnlyList<object?> arguments) => true;
            public override void Update(object target, IReadOnlyList<object?> arguments) =>
                throw new InvalidOperationException("Expected the existing priority contribution path.");
            public override IDisposable Apply(object target, IReadOnlyList<object?> arguments, BindingPriority priority) =>
                ((AvaloniaObject)target).SetValue(property, value, priority)!;
            public override void Reset(object target) => ((AvaloniaObject)target).ClearValue(property);
        }
    }

    private sealed class Source : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _handlers;
        private bool _enabled, _other;
        public int Subscribers { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { _handlers += value; Subscribers++; }
            remove { _handlers -= value; Subscribers--; }
        }
        public bool Enabled { get => _enabled; set { _enabled = value; _handlers?.Invoke(this, new(nameof(Enabled))); } }
        public bool Other { get => _other; set { _other = value; _handlers?.Invoke(this, new(nameof(Other))); } }
    }
}
