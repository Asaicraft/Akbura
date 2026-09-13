using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class HookStateRuntimeTests
{
    [Fact]
    public void HookState_ReusesItsInstanceWithoutOverwritingTheValue()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => -1);

        runtime.BeginFrame();
        var first = runtime.GetState(info, 10);
        Assert.Equal(10, first.Value);
        Assert.False(first.IsAttached);
        runtime.CompleteFrame();
        first.Value = 20;

        runtime.BeginFrame();
        var second = runtime.GetState(info, 999);
        runtime.CompleteFrame();

        Assert.Same(first, second);
        Assert.True(second.IsAttached);
        Assert.Same(info, second.Info);
        Assert.Equal(20, second.Value);
        Assert.Equal(10, second.InitialValue);
    }

    [Fact]
    public void HookState_LazyAndDescriptorFactoriesRunOnlyForNewSlots()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var lazyCalls = 0;
        var descriptorCalls = 0;
        var lazyInfo = new StateInfo<int>("lazy", static _ => -1);
        var descriptorInfo = new StateInfo<int>("descriptor", _ => ++descriptorCalls);

        runtime.BeginFrame();
        var lazy = runtime.GetState(lazyInfo, () => ++lazyCalls);
        var descriptor = runtime.GetState(descriptorInfo);
        runtime.CompleteFrame();

        runtime.BeginFrame();
        Assert.Same(lazy, runtime.GetState(lazyInfo, () => ++lazyCalls));
        Assert.Same(descriptor, runtime.GetState(descriptorInfo));
        runtime.CompleteFrame();

        Assert.Equal(1, lazyCalls);
        Assert.Equal(1, descriptorCalls);
        Assert.Equal(1, lazy.Value);
        Assert.Equal(1, descriptor.Value);
    }

    [Fact]
    public void HookState_SharedDescriptorUsesIndependentPositionsAndOwners()
    {
        var info = new StateInfo<int>("shared", static _ => 0);
        var firstRuntime = new UseHookRuntime(new StateComponent());
        var secondRuntime = new UseHookRuntime(new StateComponent());

        firstRuntime.BeginFrame();
        var first = firstRuntime.GetState(info, 1);
        var second = firstRuntime.GetState(info, 2);
        firstRuntime.CompleteFrame();
        secondRuntime.BeginFrame();
        var otherOwner = secondRuntime.GetState(info, 3);
        secondRuntime.CompleteFrame();
        first.Value = 4;

        Assert.NotSame(first, second);
        Assert.NotSame(first, otherOwner);
        Assert.Equal(2, second.Value);
        Assert.Equal(3, otherOwner.Value);
    }

    [Fact]
    public void HookState_AllStatesAreAttachedAndAliasesCommittedBeforeTheFirstEffect()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var events = new List<string>();
        State<int>? first = null;
        State<int>? last = null;
        State<int>? alias = null;

        runtime.BeginFrame();
        runtime.Register(CreateEffect(new UseHookKey(), () =>
        {
            Assert.True(first!.IsAttached);
            Assert.True(last!.IsAttached);
            Assert.Same(last, alias);
            events.Add("effect");
        }));
        first = runtime.GetState(info, 1);
        last = runtime.GetState(info, 2);
        Assert.Empty(events);

        runtime.CompleteFrame(() =>
        {
            Assert.True(first.IsAttached);
            Assert.True(last.IsAttached);
            alias = last;
            events.Add("commit");
        });

        Assert.Equal(["commit", "effect"], events);
    }

    [Fact]
    public void HookState_AbortedInitialFrameDiscardsProvisionalStateAndRetriesItsFactory()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var factoryCalls = 0;
        var effectCalls = 0;

        runtime.BeginFrame();
        var provisional = runtime.GetState(info, () => ++factoryCalls);
        runtime.Register(CreateEffect(new UseHookKey(), () => effectCalls++));
        runtime.AbortFrame();

        Assert.False(runtime.HasSlots);
        Assert.False(provisional.IsAttached);
        Assert.Equal(0, effectCalls);

        runtime.BeginFrame();
        var committed = runtime.GetState(info, () => ++factoryCalls);
        runtime.CompleteFrame();

        Assert.NotSame(provisional, committed);
        Assert.Equal(2, factoryCalls);
        Assert.Equal(2, committed.Value);
        Assert.True(committed.IsAttached);
    }

    [Fact]
    public void HookState_AbortedLaterFramePreservesCommittedStateAndActiveEffect()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var key = new UseHookKey();
        var runs = 0;
        var stops = 0;

        runtime.BeginFrame();
        var state = runtime.GetState(info, 10);
        runtime.Register(CreateEffect(key, () => runs++, () => stops++));
        runtime.CompleteFrame();

        runtime.BeginFrame();
        Assert.Same(state, runtime.GetState(info, 20));
        runtime.Register(CreateEffect(key, () => runs++, () => stops++));
        runtime.AbortFrame();

        Assert.Equal(10, state.Value);
        Assert.True(state.IsAttached);
        Assert.Equal(1, runs);
        Assert.Equal(0, stops);
    }

    [Fact]
    public void HookState_MissingPrimitiveRejectsTheFrameBeforeApplyingAnyEffects()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var key = new UseHookKey();
        var runs = 0;
        var stops = 0;

        runtime.BeginFrame();
        runtime.Register(CreateEffect(key, () => runs++, () => stops++));
        var state = runtime.GetState(info, 1);
        runtime.CompleteFrame();

        runtime.BeginFrame();
        runtime.Register(CreateEffect(key, () => runs++, () => stops++));
        Assert.Throws<AkburaUseHooksFrameChangedException>(runtime.CompleteFrame);

        Assert.True(state.IsAttached);
        Assert.Equal(1, runs);
        Assert.Equal(0, stops);
        Assert.False(runtime.IsFrameCommitted);
    }

    [Fact]
    public void HookState_EffectAndStateShareTheSamePositionSequence()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var key = new UseHookKey();
        var runs = 0;

        runtime.BeginFrame();
        runtime.GetState(info, 1);
        runtime.Register(CreateEffect(key, () => runs++));
        runtime.CompleteFrame();

        runtime.BeginFrame();
        runtime.Register(CreateEffect(key, () => runs++));
        Assert.Throws<AkburaUseHooksFrameChangedException>(() => runtime.GetState(info, 2));
        runtime.AbortFrame();

        Assert.Equal(1, runs);
    }

    [Fact]
    public void HookState_RejectsChangedTypeAndDescriptorBeforeRunningTheirFactories()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 1);
        var changedDescriptor = new StateInfo<int>("value", static _ => throw new Exception());
        var changedType = new StateInfo<string>("value", static _ => throw new Exception());

        runtime.BeginFrame();
        runtime.GetState(info);
        runtime.CompleteFrame();

        runtime.BeginFrame();
        Assert.Throws<AkburaUseHooksFrameChangedException>(() => runtime.GetState(changedDescriptor));
        runtime.AbortFrame();
        runtime.BeginFrame();
        Assert.Throws<AkburaUseHooksFrameChangedException>(() => runtime.GetState(changedType));
        runtime.AbortFrame();
    }

    [Fact]
    public void HookState_LazyInitializerCannotRegisterEffectsOrOtherHookStates()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var effects = 0;

        runtime.BeginFrame();
        Assert.Throws<AkburaUseHookOutsideRenderException>(() => runtime.GetState(info, () =>
        {
            runtime.Register(CreateEffect(new UseHookKey(), () => effects++));
            return 1;
        }));
        Assert.Throws<AkburaUseHookOutsideRenderException>(() => runtime.GetState(info, () =>
            runtime.GetState(info, 2).Value));
        var state = runtime.GetState(info, 3);
        runtime.CompleteFrame();

        Assert.Equal(3, state.Value);
        Assert.Equal(0, effects);
    }

    [Fact]
    public void HookState_OutsideFrameDoesNotExecuteInitializers()
    {
        var control = new StateComponent();
        var calls = 0;
        var info = new StateInfo<int>("value", _ => ++calls);

        Assert.Throws<AkburaUseHookOutsideRenderException>(() => control.useHookState(0));
        Assert.Throws<AkburaUseHookOutsideRenderException>(() => control.useHookState(() => ++calls));
        Assert.Throws<AkburaUseHookOutsideRenderException>(() => control.useHookState(info));
        Assert.Throws<AkburaUseHookOutsideRenderException>(() => control.useHookState(info, 0));

        Assert.Equal(0, calls);
    }

    [Fact]
    public void HookState_DetachPreservesStateAndSuppressesEffectsUntilResume()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var key = new UseHookKey();
        var runs = 0;
        var stops = 0;

        runtime.BeginFrame();
        var state = runtime.GetState(info, 1);
        runtime.Register(CreateEffect(key, () => runs++, () => stops++));
        runtime.CompleteFrame();
        runtime.StopForDetach();
        state.Value = 5;

        runtime.BeginFrame();
        Assert.Same(state, runtime.GetState(info, 2));
        runtime.Register(CreateEffect(key, () => runs++, () => stops++));
        runtime.CompleteFrame();
        Assert.Equal(1, runs);
        Assert.Equal(1, stops);

        runtime.Resume();
        runtime.BeginFrame();
        Assert.Same(state, runtime.GetState(info, 3));
        runtime.Register(CreateEffect(key, () => runs++, () => stops++));
        runtime.CompleteFrame();

        Assert.Equal(5, state.Value);
        Assert.Equal(2, runs);
        Assert.Equal(1, stops);
    }

    [Fact]
    public void HookState_SuspendedRuntimeDoesNotCreateAProvisionalLayout()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var calls = 0;
        var info = new StateInfo<int>("value", _ => ++calls);
        runtime.StopForDetach();
        runtime.BeginFrame();

        Assert.Throws<InvalidOperationException>(() => runtime.GetState(info));
        runtime.AbortFrame();

        Assert.Equal(0, calls);
        Assert.False(runtime.HasSlots);
    }

    [Fact]
    public void HookState_DetachDuringInitialPreparationDiscardsSlotsAndRequestsAFrameOnResume()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var aliasCommitted = false;

        runtime.BeginFrame();
        var provisional = runtime.GetState(info, 1);
        runtime.StopForDetach();
        runtime.CompleteFrame(() => aliasCommitted = true);

        Assert.False(runtime.IsFrameCommitted);
        Assert.False(aliasCommitted);
        Assert.False(provisional.IsAttached);
        Assert.False(runtime.HasSlots);
        Assert.True(runtime.NeedsRestart);

        runtime.Resume();
        Assert.True(runtime.NeedsRestart);
        runtime.BeginFrame();
        var committed = runtime.GetState(info, 2);
        runtime.CompleteFrame();

        Assert.NotSame(provisional, committed);
        Assert.True(committed.IsAttached);
        Assert.False(runtime.NeedsRestart);
    }

    [Fact]
    public void HookState_HotReloadReleasesOldStateAndCreatesANewInstance()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);

        runtime.BeginFrame();
        var previous = runtime.GetState(info, 1);
        runtime.CompleteFrame();
        previous.Value = 5;
        runtime.ResetForHotReload();

        Assert.False(previous.IsAttached);
        runtime.BeginFrame();
        var current = runtime.GetState(info, 2);
        runtime.CompleteFrame();

        Assert.NotSame(previous, current);
        Assert.True(current.IsAttached);
        Assert.Equal(2, current.Value);
    }

    [Fact]
    public void HookState_ChangesInvalidateItsOwnerButReleasedStatesDoNot()
    {
        State<int>? result = null;
        var component = new StateComponent
        {
            RenderFrame = control => result = control.useHookState(1),
        };
        component.InitializeForTest();
        var first = result!;
        var initialRenders = component.RenderCount;

        first.Value = 2;

        Assert.Equal(initialRenders + 1, component.RenderCount);
        Assert.Same(first, result);
        component.ApplyHotReload(static _ => { });
        Assert.NotSame(first, result);
        var afterHotReload = component.RenderCount;
        first.Value = 3;

        Assert.Equal(afterHotReload, component.RenderCount);
        Assert.Equal(1, result!.Value);
    }

    [Fact]
    public void HookState_EffectFailureRetainsTheCommittedStateForRecovery()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var key = new UseHookKey();

        runtime.BeginFrame();
        var state = runtime.GetState(info, 1);
        runtime.Register(CreateEffect(key, static () => throw new InvalidOperationException("effect")));
        Assert.Throws<InvalidOperationException>(runtime.CompleteFrame);

        Assert.True(runtime.IsFrameCommitted);
        Assert.True(state.IsAttached);
        runtime.BeginFrame();
        Assert.Same(state, runtime.GetState(info, 2));
        runtime.Register(CreateEffect(key, static () => { }));
        runtime.CompleteFrame();

        Assert.Equal(1, state.Value);
        Assert.False(runtime.NeedsRestart);
    }

    [Fact]
    public void HookState_DuplicateFactoryStateDoesNotLeavePartiallyAttachedSlots()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var shared = new State<int>(1);
        var info = StateInfo<int>.FromState("duplicate", _ => shared);

        runtime.BeginFrame();
        runtime.GetState(info);
        runtime.GetState(info);

        Assert.Throws<InvalidOperationException>(runtime.CompleteFrame);
        Assert.False(shared.IsAttached);
        Assert.False(runtime.HasSlots);
        Assert.False(runtime.IsFrameCommitted);
    }

    [Fact]
    public void HookState_AliasMustBelongToTheCurrentOwnerOrItsPendingSlots()
    {
        var owner = new StateComponent();
        var runtime = new UseHookRuntime(owner);
        var info = new StateInfo<int>("value", static _ => 0);
        var ordinary = info.CreateTypedState(owner);
        var foreign = info.CreateTypedState(new StateComponent());

        runtime.BeginFrame();
        var provisional = runtime.GetState(info, 1);
        runtime.ValidateStateAlias(ordinary);
        runtime.ValidateStateAlias(provisional);

        Assert.Throws<InvalidOperationException>(() => runtime.ValidateStateAlias(new State<int>(2)));
        Assert.Throws<InvalidOperationException>(() => runtime.ValidateStateAlias(foreign));
        runtime.CompleteFrame();
    }

    [Fact]
    public void HookState_EffectCallbacksCannotRegisterMorePrimitives()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var initializerCalls = 0;

        runtime.BeginFrame();
        runtime.Register(CreateEffect(new UseHookKey(), () =>
            runtime.GetState(info, () => ++initializerCalls)));

        Assert.Throws<AkburaUseHookOutsideRenderException>(runtime.CompleteFrame);
        Assert.Equal(0, initializerCalls);
    }

    [Fact]
    public void HookState_WorkerThreadCannotRegisterPrimitivesDuringAnOpenFrame()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var initializerCalls = 0;
        Exception? stateFailure = null;
        Exception? effectFailure = null;

        runtime.BeginFrame();
        var worker = new Thread(() =>
        {
            stateFailure = Record.Exception(() => runtime.GetState(info, () => ++initializerCalls));
            effectFailure = Record.Exception(() => runtime.Register(
                CreateEffect(new UseHookKey(), static () => { })));
        });
        worker.Start();
        worker.Join();

        Assert.IsType<AkburaUseHookOutsideRenderException>(stateFailure);
        Assert.IsType<AkburaUseHookOutsideRenderException>(effectFailure);
        Assert.Equal(0, initializerCalls);
        var state = runtime.GetState(info, 1);
        runtime.CompleteFrame();
        Assert.True(state.IsAttached);
    }

    [Fact]
    public void HookState_DetachCleanupCannotRegisterPrimitivesDuringAnOpenFrame()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var info = new StateInfo<int>("value", static _ => 0);
        var key = new UseHookKey();
        Exception? stateFailure = null;
        Exception? effectFailure = null;

        runtime.BeginFrame();
        var first = runtime.GetState(info, 1);
        runtime.Register(CreateEffect(key, static () => { }, () =>
        {
            stateFailure = Record.Exception(() => runtime.GetState(info, 2));
            effectFailure = Record.Exception(() => runtime.Register(
                CreateEffect(new UseHookKey(), static () => { })));
        }));
        runtime.CompleteFrame();

        runtime.BeginFrame();
        runtime.StopForDetach();

        Assert.IsType<AkburaUseHookOutsideRenderException>(stateFailure);
        Assert.IsType<AkburaUseHookOutsideRenderException>(effectFailure);
        Assert.Same(first, runtime.GetState(info, 3));
        runtime.Register(CreateEffect(key, static () => { }));
        runtime.CompleteFrame();
    }

    [Fact]
    public void HookState_DetachingFromAnEffectStopsLaterEffectsInTheSameCommit()
    {
        var runtime = new UseHookRuntime(new StateComponent());
        var firstKey = new UseHookKey();
        var secondKey = new UseHookKey();
        var secondRuns = 0;
        var stops = 0;

        runtime.BeginFrame();
        runtime.Register(CreateEffect(firstKey, runtime.StopForDetach, () => stops++));
        runtime.Register(CreateEffect(secondKey, () => secondRuns++));
        runtime.CompleteFrame();

        Assert.Equal(0, secondRuns);
        Assert.Equal(1, stops);
        Assert.True(runtime.NeedsRestart);
    }

    private static IUseHookRegistration CreateEffect(UseHookKey key, Action run, Action? stop = null)
    {
        var registration = new UseEffectRegistration(
            key,
            _ =>
            {
                run();
                return ValueTask.FromResult<IDisposable?>(stop == null ? null : new Cleanup(stop));
            },
            hasDependencies: false,
            dependencies: [],
            comparer: null);

        return new DelegateUseHookRegistration<UseEffectSlot, UseEffectRegistration>(
            key,
            registration,
            static value => new UseEffectSlot(value.Key),
            static (slot, value) => slot.Apply(value),
            static slot => slot.StopForDetach());
    }

    private sealed class Cleanup(Action cleanup) : IDisposable
    {
        public void Dispose() => cleanup();
    }

    private sealed class StateComponent : AkburaControl
    {
        public StateComponent() : base(AkburaEngine.Empty)
        {
        }

        public Action<StateComponent>? RenderFrame { get; init; }

        public int RenderCount { get; private set; }

        public void InitializeForTest() => base.OnInitialized();

        protected override Control FirstUpdate() => new Border();

        protected override Control Update()
        {
            RenderCount++;
            RenderFrame?.Invoke(this);
            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];

        protected override ImmutableArray<InjectService> GetServices() => [];

        protected override ImmutableArray<State> GetStates() => [];
    }
}
