using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Controls;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class UsefulHookPrimitiveTests
{
    [Fact]
    public Task UseState_IsAnAliasWithoutAnExtraSlot() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        State<int>? result = null;
        var alias = true;
        component.RenderFrame = control => result = alias
            ? control.useState(10)
            : control.useHookState(999);
        component.InitializeForTest();
        var initial = Assert.IsType<State<int>>(result);
        initial.Value = 20;
        alias = false;
        component.RenderAgain();

        Assert.Same(initial, result);
        Assert.Equal(20, result.Value);
        Assert.Equal(10, result.InitialValue);
    });

    [Fact]
    public Task UseState_DescriptorAndLazyInitializersAreStableAndIndependent() => OnDispatcher(() =>
    {
        var descriptorCalls = 0;
        var lazyCalls = 0;
        var info = new StateInfo<int>("shared", _ => ++descriptorCalls);
        var component = new HookComponent();
        State<int>? first = null;
        State<int>? second = null;
        State<int>? explicitValue = null;
        State<int>? lazy = null;
        component.RenderFrame = control =>
        {
            first = control.useState(info);
            second = control.useState(info);
            explicitValue = control.useState(info, 42);
            lazy = control.useState(() => ++lazyCalls);
        };
        component.InitializeForTest();
        var initialFirst = first;
        var initialSecond = second;
        component.RenderAgain();

        Assert.Same(initialFirst, first);
        Assert.Same(initialSecond, second);
        Assert.NotSame(first, second);
        Assert.Equal(2, descriptorCalls);
        Assert.Equal(1, lazyCalls);
        Assert.Equal(1, first!.Value);
        Assert.Equal(2, second!.Value);
        Assert.Equal(42, explicitValue!.Value);
        Assert.Equal(1, lazy!.Value);
    });

    [Fact]
    public Task UseState_AbortedFirstFrameRetriesTheInitializer() => OnDispatcher(() =>
    {
        var component = new HookComponent { FailRender = true };
        var factoryCalls = 0;
        State<int>? result = null;
        component.RenderFrame = control => result = control.useState(() => ++factoryCalls);
        Assert.Throws<ExpectedRenderException>(component.InitializeForTest);
        var provisional = result;
        Assert.False(provisional!.IsAttached);

        component.FailRender = false;
        component.InitializeForTest();

        Assert.NotSame(provisional, result);
        Assert.True(result!.IsAttached);
        Assert.Equal(2, factoryCalls);
        Assert.Equal(2, result.Value);
    });

    [Fact]
    public Task UseRef_MutationDoesNotRenderAndTheCellIsNotReinitialized() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        State<HookRef<int>>? result = null;
        var initialValue = 10;
        component.RenderFrame = control => result = control.useRef(initialValue);
        component.InitializeForTest();
        var initialState = result;
        var initialCell = result!.Value;
        var renderCount = component.RenderCount;
        initialCell.Current = 123;
        Assert.Equal(renderCount, component.RenderCount);

        initialValue = 999;
        component.RenderAgain();

        Assert.Same(initialState, result);
        Assert.Same(initialCell, result.Value);
        Assert.Equal(123, result.Value.Current);
    });

    [Fact]
    public Task UseRef_LazyCellsAreIndependentAcrossCallsAndOwners() => OnDispatcher(() =>
    {
        var calls = 0;
        var first = new HookComponent();
        var second = new HookComponent();
        State<HookRef<int>>? a = null;
        State<HookRef<int>>? b = null;
        State<HookRef<int>>? c = null;
        first.RenderFrame = control =>
        {
            a = control.useRef(() => ++calls);
            b = control.useRef(() => ++calls);
        };
        second.RenderFrame = control => c = control.useRef(() => ++calls);
        first.InitializeForTest();
        second.InitializeForTest();
        first.RenderAgain();
        second.RenderAgain();
        a!.Value.Current = 20;

        Assert.Equal(3, calls);
        Assert.NotSame(a.Value, b!.Value);
        Assert.NotSame(a.Value, c!.Value);
        Assert.Equal(2, b.Value.Current);
        Assert.Equal(3, c.Value.Current);
    });

    [Fact]
    public Task UseLatest_ChangesOnlyAfterACommittedFrame() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var input = 1;
        State<HookRef<int>>? result = null;
        var observedDuringRender = new List<int>();
        component.RenderFrame = control =>
        {
            result = control.useLatest(input);
            observedDuringRender.Add(result.Value.Current);
        };
        component.InitializeForTest();
        var initial = result;
        input = 2;
        component.FailRender = true;
        Assert.Throws<ExpectedRenderException>(component.RenderAgain);
        Assert.Equal(1, result!.Value.Current);

        component.FailRender = false;
        component.RenderAgain();

        Assert.Same(initial, result);
        Assert.Equal([1, 1, 1], observedDuringRender);
        Assert.Equal(2, result.Value.Current);
    });

    [Fact]
    public Task UseId_IsStableUntilHotReloadAndIndependentAcrossSlots() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var prefix = "search";
        State<string>? first = null;
        State<string>? second = null;
        component.RenderFrame = control =>
        {
            first = control.useId(prefix);
            second = control.useId(prefix);
        };
        component.InitializeForTest();
        var original = Assert.IsType<State<string>>(first);
        var originalValue = original.Value;
        Assert.StartsWith("search-", originalValue);
        Assert.NotEqual(originalValue, second!.Value);
        prefix = "other";
        component.RenderAgain();
        Assert.Same(original, first);
        Assert.Equal(originalValue, first.Value);

        component.ApplyHotReload(static _ => { });

        Assert.NotSame(original, first);
        Assert.False(original.IsAttached);
        Assert.StartsWith("other-", first.Value);
        Assert.NotEqual(originalValue, first.Value);
    });

    [Fact]
    public Task UseReducer_DispatchRendersWhileKeepingTheWrapperStableAndResetUsesInitialValue() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var initialValue = 3;
        State<HookReducer<int, int>>? result = null;
        component.RenderFrame = control => result = control.useReducer<int, int>(
            static (value, delta) => value + delta, initialValue);
        component.InitializeForTest();
        var initialState = result;
        var initialModel = result!.Value;
        var renderCount = component.RenderCount;
        initialModel.Dispatch(2);

        Assert.Equal(renderCount + 1, component.RenderCount);
        Assert.Same(initialState, result);
        Assert.Same(initialModel, result.Value);
        Assert.Equal(5, initialModel.Value);
        initialValue = 99;
        component.RenderAgain();
        initialModel.Reset();
        Assert.Equal(3, initialModel.Value);
    });

    [Fact]
    public Task UseReducer_AbortedFrameKeepsThePreviouslyCommittedReducer() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var multiplier = 1;
        State<HookReducer<int, int>>? result = null;
        component.RenderFrame = control =>
        {
            var frameMultiplier = multiplier;
            result = control.useReducer<int, int>((value, delta) => value + delta * frameMultiplier, 0);
        };
        component.InitializeForTest();
        var model = result!.Value;
        multiplier = 10;
        component.FailRender = true;
        Assert.Throws<ExpectedRenderException>(component.RenderAgain);
        component.FailRender = false;
        model.Dispatch(1);
        Assert.Equal(1, model.Value);

        model.Dispatch(1);
        Assert.Equal(11, model.Value);
    });

    [Fact]
    public Task UseReducer_HasIndependentValuesAndInitializesLazyStateOnce() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var factoryCalls = 0;
        State<HookReducer<int, int>>? first = null;
        State<HookReducer<int, int>>? second = null;
        component.RenderFrame = control =>
        {
            first = control.useReducer<int, int>(static (value, delta) => value + delta, () => ++factoryCalls);
            second = control.useReducer<int, int>(static (value, delta) => value + delta, () => ++factoryCalls);
        };
        component.InitializeForTest();
        first!.Value.Dispatch(5);

        Assert.Equal(2, factoryCalls);
        Assert.Equal(6, first.Value.Value);
        Assert.Equal(2, second!.Value.Value);
        Assert.NotSame(first.Value, second.Value);
    });

    [Fact]
    public Task UseReducer_AllowsAnEffectToDispatchAFollowupActionAfterTheReducerReturns() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        State<HookReducer<int, int>>? result = null;
        component.RenderFrame = control =>
        {
            result = control.useReducer<int, int>(static (value, delta) => value + delta, 0);
            var value = result.Value.Value;
            control.useEffect((Action)(() =>
            {
                if (value == 1)
                {
                    result.Value.Dispatch(1);
                }
            }), [value]);
        };
        component.InitializeForTest();
        var initialModel = result!.Value;
        var renderCount = component.RenderCount;

        initialModel.Dispatch(1);

        Assert.Same(initialModel, result.Value);
        Assert.Equal(2, initialModel.Value);
        Assert.Equal(renderCount + 2, component.RenderCount);
    });

    [Fact]
    public Task UseReducer_RejectsRecursiveDispatchAndResetAndRecoversAfterFailure() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        State<HookReducer<int, int>>? result = null;
        var recurse = true;
        component.RenderFrame = control => result = control.useReducer<int, int>((value, action) =>
        {
            if (recurse)
            {
                if (action == 1)
                {
                    result!.Value.Dispatch(0);
                }
                else
                {
                    result!.Value.Reset();
                }
            }

            return value + action;
        }, 0);
        component.InitializeForTest();
        Assert.Throws<InvalidOperationException>(() => result!.Value.Dispatch(1));
        Assert.Throws<InvalidOperationException>(() => result!.Value.Dispatch(2));
        Assert.Equal(0, result!.Value.Value);
        recurse = false;
        result.Value.Dispatch(3);
        Assert.Equal(3, result.Value.Value);
    });

    [Fact]
    public Task UseReducer_RejectsWorkerThreadDispatchAndStaleModelsAfterHotReload() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        State<HookReducer<int, int>>? result = null;
        component.RenderFrame = control => result = control.useReducer<int, int>(
            static (value, delta) => value + delta, 0);
        component.InitializeForTest();
        var original = result!.Value;
        Exception? workerFailure = null;
        var worker = new Thread(() => workerFailure = Record.Exception(() => original.Dispatch(1)));
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        Assert.IsType<InvalidOperationException>(workerFailure);
        Assert.Equal(0, original.Value);

        component.ApplyHotReload(static _ => { });
        Assert.NotSame(original, result.Value);
        Assert.Throws<InvalidOperationException>(() => original.Dispatch(1));
        Assert.Throws<InvalidOperationException>(original.Reset);
        result.Value.Dispatch(2);
        Assert.Equal(2, result.Value.Value);
    });

    [Fact]
    public Task UseUpdateEffect_SkipsFirstFrameButRunsOnSubsequentChanges() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var calls = new List<int>();
        component.RenderFrame = control =>
        {
            var input = control.Trigger.Value;
            control.useUpdateEffect(() => calls.Add(input), [input]);
        };
        component.InitializeForTest();
        component.RenderAgain();
        Assert.Empty(calls);
        component.Trigger.Value = 1;
        component.RenderAgain();
        component.Trigger.Value = 2;

        Assert.Equal([1, 2], calls);
    });

    [Fact]
    public Task UseUpdateEffect_AbortedFrameDoesNotRunOrReplaceCommittedCleanup() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var input = 0;
        var events = new List<string>();
        component.RenderFrame = control =>
        {
            var frameInput = input;
            control.useUpdateEffect((Func<Action?>)(() =>
            {
                events.Add($"run:{frameInput}");
                return () => events.Add($"cleanup:{frameInput}");
            }), [frameInput]);
        };
        component.InitializeForTest();
        input = 1;
        component.RenderAgain();
        input = 2;
        component.FailRender = true;
        Assert.Throws<ExpectedRenderException>(component.RenderAgain);
        Assert.Equal(["run:1"], events);
        component.FailRender = false;
        component.RenderAgain();

        Assert.Equal(["run:1", "cleanup:1", "run:2"], events);
    });

    [Fact]
    public Task UseUpdateEffect_SkipsTheFirstFrameOfEachAttachedLifetime() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var events = new List<string>();
        component.RenderFrame = control =>
        {
            var input = control.Trigger.Value;
            control.useUpdateEffect((Func<Action?>)(() =>
            {
                events.Add($"run:{input}");
                return () => events.Add($"cleanup:{input}");
            }), [input]);
        };
        var window = new Window { Content = component };
        window.Show();
        try
        {
            Assert.Empty(events);
            component.Trigger.Value = 1;
            window.Content = null;
            Assert.Equal(["run:1", "cleanup:1"], events);
            component.Trigger.Value = 2;
            window.Content = component;
            Assert.Equal(["run:1", "cleanup:1"], events);
            component.Trigger.Value = 3;
            Assert.Equal(["run:1", "cleanup:1", "run:3"], events);
        }
        finally
        {
            window.Close();
        }

        Assert.Equal(["run:1", "cleanup:1", "run:3", "cleanup:3"], events);
    });

    [Fact]
    public Task UseUpdateEffect_AsyncRunsReceiveTheOriginalEffectCancellation() => OnDispatcher(() =>
    {
        var component = new HookComponent();
        var tokens = new List<CancellationToken>();
        var completions = new List<TaskCompletionSource>();
        component.RenderFrame = control => control.useUpdateEffect(token =>
        {
            tokens.Add(token);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            completions.Add(completion);
            return completion.Task;
        }, [control.Trigger.Value]);
        component.InitializeForTest();
        Assert.Empty(tokens);
        component.Trigger.Value = 1;
        Assert.Single(tokens);
        Assert.False(tokens[0].IsCancellationRequested);
        component.Trigger.Value = 2;
        Assert.Equal(2, tokens.Count);
        Assert.True(tokens[0].IsCancellationRequested);
        Assert.False(tokens[1].IsCancellationRequested);
        foreach (var completion in completions)
        {
            completion.SetResult();
        }
    });

    private static async Task OnDispatcher(Action test)
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            test();
            return true;
        }, CancellationToken.None);
    }

    private sealed class HookComponent : AkburaControl
    {
        private static readonly StateInfo<int> s_trigger = new("trigger", static _ => 0);
        private readonly ImmutableArray<State> _states;
        private readonly Border _root = new();

        public HookComponent() : base(AkburaEngine.Empty)
        {
            Trigger = CreateState(s_trigger);
            _states = [Trigger];
        }

        public State<int> Trigger { get; }

        public Action<HookComponent>? RenderFrame { get; set; }

        public bool FailRender { get; set; }

        public int RenderCount { get; private set; }

        public void InitializeForTest() => base.OnInitialized();

        public void RenderAgain() => InvalidState();

        protected override Control FirstUpdate() => _root;

        protected override Control Update()
        {
            RenderCount++;
            RenderFrame?.Invoke(this);
            if (FailRender)
            {
                throw new ExpectedRenderException();
            }

            return _root;
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];

        protected override ImmutableArray<InjectService> GetServices() => [];

        protected override ImmutableArray<State> GetStates() => _states;
    }

    private sealed class ExpectedRenderException : Exception;
}
