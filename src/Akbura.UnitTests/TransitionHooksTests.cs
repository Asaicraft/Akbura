using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Layout;
using Avalonia.Media;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TransitionHooksTests
{
    [Fact]
    public Task ShortTransition_KeepsItsRuleWhenOnlyTheTargetValueChanges() =>
        OnDispatcher(() =>
        {
            var target = new Border();
            var enabled = true;
            var component = new HookComponent(control =>
                control.useTransition(target, Visual.OpacityProperty, 250, enabled: enabled));
            component.InitializeForTest();
            var collection = target.Transitions;
            var transition = Assert.IsType<DoubleTransition>(Assert.Single(collection!));
            Assert.Equal(Visual.OpacityProperty, transition.Property);
            Assert.Equal(TimeSpan.FromMilliseconds(250), transition.Duration);
            Assert.IsType<CubicEaseOut>(transition.Easing);

            target.Opacity = 0.25;
            component.InvalidState();
            Assert.Same(collection, target.Transitions);
            Assert.Same(transition, Assert.Single(target.Transitions!));
            Assert.Equal(0.25, target.GetBaseValue(Visual.OpacityProperty).Value);

            enabled = false;
            component.InvalidState();
            Assert.Null(target.Transitions);
            Assert.Equal(0.25, target.Opacity);
            Assert.False(target.IsSet(Animatable.TransitionsProperty));
        });

    [Fact]
    public Task ShortTransition_ReplacesOnlyItsRuleForDurationEasingAndTargetChanges() =>
        OnDispatcher(() =>
        {
            var firstTarget = new Border();
            var secondTarget = new Border();
            var target = firstTarget;
            var duration = TimeSpan.FromMilliseconds(100);
            Easing easing = new LinearEasing();
            var enabled = true;
            var component = new HookComponent(control =>
                control.useTransition(target, Visual.OpacityProperty, duration, easing, enabled));
            component.InitializeForTest();
            var first = Assert.IsType<DoubleTransition>(Assert.Single(target.Transitions!));

            duration = TimeSpan.FromMilliseconds(300);
            component.InvalidState();
            var second = Assert.IsType<DoubleTransition>(Assert.Single(target.Transitions!));
            Assert.NotSame(first, second);
            Assert.Equal(duration, second.Duration);
            Assert.Same(easing, second.Easing);

            easing = new CubicEaseIn();
            component.InvalidState();
            Assert.Same(easing, Assert.IsType<DoubleTransition>(Assert.Single(target.Transitions!)).Easing);

            target = secondTarget;
            component.InvalidState();
            Assert.Null(firstTarget.Transitions);
            Assert.Single(secondTarget.Transitions!);
            enabled = false;
            component.InvalidState();
            Assert.Null(secondTarget.Transitions);
        });

    [Fact]
    public Task Factory_RunsOnlyForCommittedDependencyChangesAndCopiesTheDependencySpan() =>
        OnDispatcher(() =>
        {
            var target = new Border();
            object?[] dependencies = [1];
            var factories = 0;
            var enabled = true;
            var expectedCallsDuringRender = 0;
            var component = new HookComponent(control =>
            {
                control.useTransitions(target, () =>
                {
                    factories++;
                    return [OpacityTransition(100)];
                }, dependencies, enabled);
                Assert.Equal(expectedCallsDuringRender, factories);
            });
            component.InitializeForTest();
            expectedCallsDuringRender = 1;
            var firstRule = Assert.Single(target.Transitions!);
            component.InvalidState();
            Assert.Equal(1, factories);
            Assert.Same(firstRule, Assert.Single(target.Transitions!));

            dependencies[0] = 2;
            component.FailRender = true;
            Assert.Throws<RenderException>(component.InvalidState);
            Assert.Equal(1, factories);
            Assert.Same(firstRule, Assert.Single(target.Transitions!));
            component.FailRender = false;
            component.InvalidState();
            expectedCallsDuringRender = 2;
            Assert.Equal(2, factories);
            Assert.NotSame(firstRule, Assert.Single(target.Transitions!));

            enabled = false;
            component.InvalidState();
            Assert.Equal(2, factories);
            Assert.Null(target.Transitions);
            enabled = true;
            component.InvalidState();
            Assert.Equal(3, factories);
        });

    [Fact]
    public Task Resolver_ObservesPostRenderTargetsAndCachesPreviousFrameIdentity() =>
        OnDispatcher(() =>
        {
            Border? target = null;
            var nextTarget = new Border();
            var calls = 0;
            var enabled = true;
            var component = new HookComponent(control =>
            {
                control.useTransitions(() => target, () =>
                {
                    calls++;
                    return [OpacityTransition(100)];
                }, [], enabled);
                target = nextTarget;
            });
            component.InitializeForTest();
            var firstTarget = target;
            var firstRule = Assert.Single(firstTarget!.Transitions!);
            Assert.Equal(1, calls);
            component.InvalidState();
            Assert.Same(firstRule, Assert.Single(target!.Transitions!));
            Assert.Equal(1, calls);

            nextTarget = new Border();
            component.InvalidState();
            Assert.Null(firstTarget.Transitions);
            Assert.Single(nextTarget.Transitions!);
            Assert.Equal(2, calls);
            enabled = false;
            component.InvalidState();
            Assert.Null(nextTarget.Transitions);
        });

    [Fact]
    public Task NullTargetAndDisabledHook_DoNotCreateRulesButCanBecomeEnabledLater() =>
        OnDispatcher(() =>
        {
            Animatable? target = null;
            var enabled = true;
            var calls = 0;
            var component = new HookComponent(control => control.useTransitions(target, () =>
            {
                calls++;
                return [OpacityTransition(100)];
            }, [], enabled));
            component.InitializeForTest();
            Assert.Equal(0, calls);
            enabled = false;
            target = new Border();
            component.InvalidState();
            Assert.Equal(0, calls);
            enabled = true;
            component.InvalidState();
            Assert.Equal(1, calls);
            Assert.Single(target.Transitions!);
            target = null;
            component.InvalidState();
            Assert.Equal(1, calls);
        });

    [Fact]
    public Task SharedStyleCollection_IsCopiedAndOriginalPriorityIsRestored() =>
        OnDispatcher(() =>
        {
            var foreign = new DoubleTransition { Property = Layoutable.WidthProperty, Duration = TimeSpan.FromSeconds(1) };
            var shared = new Transitions { foreign };
            var firstTarget = new Border();
            var otherTarget = new Border();
            using var firstStyle = firstTarget.SetValue(Animatable.TransitionsProperty, shared, BindingPriority.Style);
            using var otherStyle = otherTarget.SetValue(Animatable.TransitionsProperty, shared, BindingPriority.Style);
            var enabled = true;
            var component = new HookComponent(control =>
                control.useTransition(firstTarget, Visual.OpacityProperty, 100, enabled: enabled));
            component.InitializeForTest();

            Assert.NotSame(shared, firstTarget.Transitions);
            Assert.Equal(2, firstTarget.Transitions!.Count);
            Assert.Same(foreign, firstTarget.Transitions[0]);
            Assert.Same(shared, otherTarget.Transitions);
            Assert.Same(foreign, Assert.Single(shared));

            enabled = false;
            component.InvalidState();
            Assert.Same(shared, firstTarget.Transitions);
            Assert.Equal(BindingPriority.Style, firstTarget.GetDiagnostic(Animatable.TransitionsProperty).Priority);
            Assert.Same(shared, otherTarget.Transitions);
        });

    [Fact]
    public Task OriginalLocalBinding_RemainsSubscribedAndItsNewValueAppearsAfterCleanup() =>
        OnDispatcher(() =>
        {
            var original = new Transitions { WidthTransition() };
            var updated = new Transitions { new ThicknessTransition { Property = Layoutable.MarginProperty } };
            var source = new TransitionSource(original);
            var target = new Border();
            using var binding = target.Bind(Animatable.TransitionsProperty, source);
            var enabled = true;
            var component = new HookComponent(control =>
                control.useTransition(target, Visual.OpacityProperty, 100, enabled: enabled));
            component.InitializeForTest();
            Assert.Equal(1, source.Subscribers);
            source.Publish(updated);
            Assert.Equal(1, source.Subscribers);
            Assert.NotSame(updated, target.Transitions);

            enabled = false;
            component.InvalidState();
            Assert.Same(updated, target.Transitions);
            Assert.Equal(1, source.Subscribers);
            Assert.Equal(BindingPriority.LocalValue, target.GetDiagnostic(Animatable.TransitionsProperty).Priority);
            source.Publish(original);
            Assert.Same(original, target.Transitions);
        });

    [Fact]
    public Task TwoHooks_ShareAnOverlayAndCleanupOnlyTheirOwnRules() =>
        OnDispatcher(() =>
        {
            var target = new Border();
            var firstEnabled = true;
            var secondEnabled = true;
            var component = new HookComponent(control =>
            {
                control.useTransition(target, Visual.OpacityProperty, 100, enabled: firstEnabled);
                control.useTransition(target, Layoutable.MarginProperty, 200, enabled: secondEnabled);
            });
            component.InitializeForTest();
            var collection = target.Transitions;
            var secondRule = collection![1];
            Assert.Equal(2, collection.Count);

            firstEnabled = false;
            component.InvalidState();
            Assert.Same(collection, target.Transitions);
            Assert.Same(secondRule, Assert.Single(target.Transitions!));
            secondEnabled = false;
            component.InvalidState();
            Assert.Null(target.Transitions);
            Assert.False(target.IsSet(Animatable.TransitionsProperty));
        });

    [Fact]
    public Task ForeignEdits_ToTheOverlaySurviveWithoutMutatingTheSharedStyleCollection() =>
        OnDispatcher(() =>
        {
            var originalRule = WidthTransition();
            var shared = new Transitions { originalRule };
            var target = new Border();
            using var style = target.SetValue(Animatable.TransitionsProperty, shared, BindingPriority.Style);
            var enabled = true;
            var component = new HookComponent(control =>
                control.useTransition(target, Visual.OpacityProperty, 100, enabled: enabled));
            component.InitializeForTest();
            var added = new ThicknessTransition { Property = Layoutable.MarginProperty };
            target.Transitions!.Remove(originalRule);
            target.Transitions.Add(added);

            enabled = false;
            component.InvalidState();
            Assert.Same(added, Assert.Single(target.Transitions!));
            Assert.Same(originalRule, Assert.Single(shared));
            Assert.Equal(BindingPriority.Style, target.GetDiagnostic(Animatable.TransitionsProperty).Priority);
        });

    [Fact]
    public Task NewerExternalOverride_IsNotReplacedByHookCleanup() =>
        OnDispatcher(() =>
        {
            var target = new Border();
            var enabled = true;
            var component = new HookComponent(control =>
                control.useTransition(target, Visual.OpacityProperty, 100, enabled: enabled));
            component.InitializeForTest();
            var external = new Transitions { WidthTransition() };
            using var externalLease = target.SetValue(Animatable.TransitionsProperty, external, BindingPriority.Animation);
            enabled = false;
            component.InvalidState();
            Assert.Same(external, target.Transitions);
            Assert.Single(external);
        });

    [Fact]
    public Task TargetDetach_SuspendsRulesAndReattachRestoresTheSameRulesWithoutCallingFactory() =>
        OnDispatcher(() =>
        {
            var target = new Border();
            var calls = 0;
            var component = new HookComponent(control => control.useTransitions(target, () =>
            {
                calls++;
                return [OpacityTransition(100)];
            }, []));
            component.Root.Children.Add(target);
            var window = new Window { Content = component };
            window.Show();
            var rule = Assert.Single(target.Transitions!);
            Assert.Equal(1, calls);

            component.Root.Children.Remove(target);
            Assert.Null(target.Transitions);
            component.InvalidState();
            Assert.Null(target.Transitions);
            Assert.Equal(1, calls);
            component.Root.Children.Add(target);
            Assert.Same(rule, Assert.Single(target.Transitions!));
            Assert.Equal(1, calls);

            window.Content = null;
            Assert.Null(target.Transitions);
            window.Content = component;
            Assert.Single(target.Transitions!);
            Assert.Equal(2, calls);
            window.Close();
        });

    [Fact]
    public Task ConflictingHooks_RejectDoubleOwnershipAndKeepTheFirstRule() =>
        OnDispatcher(() =>
        {
            var target = new Border();
            var secondEnabled = false;
            var firstOwner = new HookComponent(control =>
                control.useTransition(target, Visual.OpacityProperty, 100));
            var secondOwner = new HookComponent(control =>
                control.useTransition(target, Visual.OpacityProperty, 200, enabled: secondEnabled));
            firstOwner.InitializeForTest();
            secondOwner.InitializeForTest();
            var first = Assert.Single(target.Transitions!);
            secondEnabled = true;
            var error = Assert.Throws<InvalidOperationException>(secondOwner.InvalidState);
            Assert.Contains("already owns", error.Message);
            Assert.Same(first, Assert.Single(target.Transitions!));
        });

    [Fact]
    public Task ConflictingHooks_InTheSameOwnerRollBackAllItsRulesAndRestoreTheBaseline() =>
        OnDispatcher(() =>
        {
            var baseline = new Transitions { WidthTransition() };
            var target = new Border { Transitions = baseline };
            var secondEnabled = false;
            var component = new HookComponent(control =>
            {
                control.useTransition(target, Visual.OpacityProperty, 100);
                control.useTransition(target, Visual.OpacityProperty, 200, enabled: secondEnabled);
            });
            component.InitializeForTest();
            Assert.Equal(2, target.Transitions!.Count);
            secondEnabled = true;

            Assert.Throws<InvalidOperationException>(component.InvalidState);
            Assert.Same(baseline, target.Transitions);
            Assert.Single(target.Transitions!);

            secondEnabled = false;
            component.InvalidState();
            Assert.Equal(2, target.Transitions!.Count);
            Assert.Same(baseline[0], target.Transitions[0]);
        });

    [Fact]
    public Task InvalidFactories_DoNotOverwriteForeignRulesAndCanBeRetried() =>
        OnDispatcher(() =>
        {
            var original = new Transitions { WidthTransition() };
            var target = new Border { Transitions = original };
            var fail = true;
            var calls = 0;
            var component = new HookComponent(control => control.useTransitions(target, () =>
            {
                calls++;
                return fail ? null! : new Transitions { OpacityTransition(100) };
            }, []));
            Assert.Throws<InvalidOperationException>(component.InitializeForTest);
            Assert.Same(original, target.Transitions);
            fail = false;
            component.InvalidState();
            Assert.Equal(2, calls);
            Assert.Equal(2, target.Transitions!.Count);
            Assert.Same(original[0], target.Transitions[0]);
        });

    [Fact]
    public Task ShortForms_ChooseBuiltInTransitionsForSupportedPropertyTypes() =>
        OnDispatcher(() =>
        {
            var target = new Border();
            var component = new HookComponent(control =>
            {
                control.useTransition(() => target, Border.PaddingProperty, 100);
                control.useTransition(target, Border.CornerRadiusProperty, TimeSpan.FromMilliseconds(100));
                control.useTransition(target, Border.BackgroundProperty, 100);
                control.useTransition(target, Visual.RenderTransformProperty, 100);
            });
            component.InitializeForTest();
            Assert.Collection(target.Transitions!,
                rule => Assert.IsType<ThicknessTransition>(rule),
                rule => Assert.IsType<CornerRadiusTransition>(rule),
                rule => Assert.IsType<BrushTransition>(rule),
                rule => Assert.IsType<TransformOperationsTransition>(rule));
        });

    [Fact]
    public Task UnsupportedAndDirectPropertiesAndNegativeDurationsAreRejected() =>
        OnDispatcher(() =>
        {
            var target = new Border();
            var component = new HookComponent(_ => { });
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                component.useTransition(target, Visual.OpacityProperty, -1));
            Assert.Throws<ArgumentException>(() =>
                component.useTransition(target, Visual.BoundsProperty, 100));
            var unsupported = new HookComponent(control =>
                control.useTransition(target, TextBlock.TextProperty, 100));
            Assert.Throws<NotSupportedException>(unsupported.InitializeForTest);
            Assert.Null(target.Transitions);
        });

    private static DoubleTransition OpacityTransition(int milliseconds) => new()
    {
        Property = Visual.OpacityProperty,
        Duration = TimeSpan.FromMilliseconds(milliseconds)
    };

    private static DoubleTransition WidthTransition() => new()
    {
        Property = Layoutable.WidthProperty,
        Duration = TimeSpan.FromMilliseconds(100)
    };

    private static async Task OnDispatcher(Action test)
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            test();
            return true;
        }, CancellationToken.None);
    }

    private sealed class HookComponent(Action<HookComponent> renderFrame) : AkburaControl(AkburaEngine.Empty)
    {
        public StackPanel Root { get; } = new();
        public bool FailRender { get; set; }
        public void InitializeForTest() => base.OnInitialized();
        protected override Control FirstUpdate() => Root;

        protected override Control Update()
        {
            renderFrame(this);
            if (FailRender)
            {
                throw new RenderException();
            }

            return Root;
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];
        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];
        protected override ImmutableArray<InjectService> GetServices() => [];
        protected override ImmutableArray<State> GetStates() => [];
    }

    private sealed class RenderException : Exception;

    private sealed class TransitionSource(Transitions initial) : IObservable<Transitions?>
    {
        private IObserver<Transitions?>? _observer;
        public int Subscribers { get; private set; }

        public IDisposable Subscribe(IObserver<Transitions?> observer)
        {
            _observer = observer;
            Subscribers++;
            observer.OnNext(initial);
            return new Subscription(() =>
            {
                _observer = null;
                Subscribers--;
            });
        }

        public void Publish(Transitions value) => _observer!.OnNext(value);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
