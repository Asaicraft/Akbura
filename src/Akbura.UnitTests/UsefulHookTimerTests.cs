using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class UsefulHookTimerTests
{
    [Fact]
    public Task Timeout_UsesTheLatestCommittedCallbackWithoutMovingTheDeadline() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var completed = new TaskCompletionSource<int>();
            var value = 1;
            component.RenderFrame = control =>
            {
                var snapshot = value;
                TimerHooks.useTimeout(
                    control,
                    _ =>
                    {
                        completed.SetResult(snapshot);
                        return Task.CompletedTask;
                    },
                    TimeSpan.FromMilliseconds(300),
                    [],
                    clock);
            };
            component.InitializeForTest();
            clock.Advance(200);
            value = 2;
            component.RenderAgain();
            value = 3;
            component.FailFrame = true;
            Assert.Throws<ExpectedFrameException>(component.RenderAgain);
            component.FailFrame = false;
            Assert.Equal(1, clock.CreatedTimerCount);

            clock.Advance(100);
            Assert.Equal(2, await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            component.RenderAgain();
            clock.Advance(1000);
            await DrainDispatcher();
            Assert.Equal(1, clock.CreatedTimerCount);
        });

    [Fact]
    public Task Timeout_ExplicitDependenciesAreCopiedAndRestartTheFullDelay() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var calls = new List<int>();
            var completed = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                var value = control.Source.Value;
                object?[] dependencies = [value];
                TimerHooks.useTimeout(
                    control,
                    _ =>
                    {
                        calls.Add(value);
                        completed.TrySetResult();
                        return Task.CompletedTask;
                    },
                    TimeSpan.FromMilliseconds(300),
                    dependencies,
                    clock);
                dependencies[0] = 999;
            };
            component.InitializeForTest();
            clock.Advance(200);
            component.Source.Value = 2;
            component.RenderAgain();
            Assert.Equal(2, clock.CreatedTimerCount);
            Assert.Equal(1, clock.ActiveTimerCount);
            clock.Advance(299);
            await DrainDispatcher();
            Assert.Empty(calls);

            clock.Advance(1);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal([2], calls);
        });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public Task Timeout_ZeroDelayPublicOverloadsQueueAndCancelOldQueuedCallbacks(int overload) =>
        OnDispatcher(async () =>
        {
            var component = new TimerComponent();
            var calls = new List<int>();
            var completed = new TaskCompletionSource<bool>();
            component.RenderFrame = control =>
            {
                var value = control.Source.Value;
                Action callback = () =>
                {
                    calls.Add(value);
                    completed.TrySetResult(Dispatcher.UIThread.CheckAccess());
                };
                Func<CancellationToken, Task> asyncCallback = _ =>
                {
                    callback();
                    return Task.CompletedTask;
                };
                switch (overload)
                {
                    case 0:
                        control.useTimeout(callback, 0, [value]);
                        break;
                    case 1:
                        control.useTimeout(callback, TimeSpan.Zero, [value]);
                        break;
                    case 2:
                        control.useTimeout(asyncCallback, 0, [value]);
                        break;
                    default:
                        control.useTimeout(asyncCallback, TimeSpan.Zero, [value]);
                        break;
                }

                Assert.Empty(calls);
            };
            component.InitializeForTest();
            component.Source.Value = 2;
            Assert.Empty(calls);
            Assert.True(await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            await DrainDispatcher();
            Assert.Equal([2], calls);
        });

    [Fact]
    public Task NullableDelay_PausesBothTimersWithoutChangingHookOrder() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            TimeSpan? delay = null;
            var timeout = new TaskCompletionSource();
            var interval = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                TimerHooks.useTimeout(
                    control,
                    _ =>
                    {
                        timeout.TrySetResult();
                        return Task.CompletedTask;
                    },
                    delay,
                    [],
                    clock);
                TimerHooks.useInterval(
                    control,
                    _ =>
                    {
                        interval.TrySetResult();
                        return Task.CompletedTask;
                    },
                    delay,
                    [],
                    clock);
            };
            component.InitializeForTest();
            Assert.Equal(0, clock.CreatedTimerCount);
            delay = TimeSpan.FromMilliseconds(100);
            component.RenderAgain();
            Assert.Equal(2, clock.ActiveTimerCount);
            delay = null;
            component.RenderAgain();
            Assert.Equal(0, clock.ActiveTimerCount);
            clock.Advance(1000);
            await DrainDispatcher();
            Assert.False(timeout.Task.IsCompleted);
            Assert.False(interval.Task.IsCompleted);

            delay = TimeSpan.FromMilliseconds(100);
            component.RenderAgain();
            clock.Advance(100);
            await Task.WhenAll(timeout.Task, interval.Task).WaitAsync(TimeSpan.FromSeconds(10));
            await clock.WaitForTimerCount(5);
            delay = null;
            component.RenderAgain();
            Assert.Equal(0, clock.ActiveTimerCount);
        });

    [Fact]
    public Task Interval_AwaitsAsyncCallbacksAndStartsTheNextDelayAfterCompletion() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var started = new TaskCompletionSource<CancellationToken>();
            var release = new TaskCompletionSource();
            var second = new TaskCompletionSource();
            var calls = 0;
            component.RenderFrame = control => TimerHooks.useInterval(
                control,
                async cancellationToken =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        started.SetResult(cancellationToken);
                        await release.Task;
                    }
                    else
                    {
                        second.TrySetResult();
                    }
                },
                TimeSpan.FromMilliseconds(100),
                [],
                clock);
            component.InitializeForTest();
            clock.Advance(99);
            Assert.Equal(0, calls);
            clock.Advance(1);
            var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(token.IsCancellationRequested);
            Assert.Equal(0, clock.ActiveTimerCount);
            clock.Advance(1000);
            await DrainDispatcher();
            Assert.Equal(1, calls);

            release.SetResult();
            await clock.WaitForTimerCount(2);
            clock.Advance(99);
            await DrainDispatcher();
            Assert.Equal(1, calls);
            clock.Advance(1);
            await second.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, calls);
        });

    [Fact]
    public Task Interval_UsesCommittedCallbacksAndDelayChangesCancelTheRunningCallback() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var delay = TimeSpan.FromMilliseconds(100);
            var label = 1;
            var started = new TaskCompletionSource<CancellationToken>();
            var stopped = new TaskCompletionSource();
            var restarted = new TaskCompletionSource<int>();
            component.RenderFrame = control =>
            {
                var snapshot = label;
                TimerHooks.useInterval(
                    control,
                    async cancellationToken =>
                    {
                        if (snapshot == 2)
                        {
                            started.SetResult(cancellationToken);
                            try
                            {
                                await Task.Delay(Timeout.Infinite, cancellationToken);
                            }
                            finally
                            {
                                stopped.TrySetResult();
                            }
                        }
                        else
                        {
                            restarted.TrySetResult(snapshot);
                        }
                    },
                    delay,
                    [],
                    clock);
            };
            component.InitializeForTest();
            clock.Advance(80);
            label = 2;
            component.RenderAgain();
            Assert.Equal(1, clock.CreatedTimerCount);
            clock.Advance(20);
            var oldToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            label = 3;
            delay = TimeSpan.FromMilliseconds(300);
            component.RenderAgain();
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(oldToken.IsCancellationRequested);
            Assert.Equal(1, clock.ActiveTimerCount);
            clock.Advance(299);
            Assert.False(restarted.Task.IsCompleted);
            clock.Advance(1);
            Assert.Equal(3, await restarted.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Timers_DetachCancelsAndReattachRestartsTheFullDelay(bool repeat) =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var completed = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                Func<CancellationToken, Task> callback = _ =>
                {
                    completed.TrySetResult();
                    return Task.CompletedTask;
                };
                if (repeat)
                {
                    TimerHooks.useInterval(control, callback, TimeSpan.FromMilliseconds(300), [], clock);
                }
                else
                {
                    TimerHooks.useTimeout(control, callback, TimeSpan.FromMilliseconds(300), [], clock);
                }
            };
            var window = new Window { Content = component };
            window.Show();
            try
            {
                clock.Advance(200);
                window.Content = null;
                Assert.Equal(0, clock.ActiveTimerCount);
                clock.Advance(1000);
                await DrainDispatcher();
                Assert.False(completed.Task.IsCompleted);
                window.Content = component;
                Assert.Equal(1, clock.ActiveTimerCount);
                clock.Advance(299);
                Assert.False(completed.Task.IsCompleted);
                clock.Advance(1);
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                window.Close();
            }
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Timers_AsyncFailuresAndNullTasksAreObserved(bool repeat, bool returnNull) =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var started = new TaskCompletionSource();
            var callbackCompletion = new TaskCompletionSource();
            var observed = new TaskCompletionSource<Exception>();
            void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
            {
                args.Handled = true;
                observed.TrySetResult(args.Exception);
            }

            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
            try
            {
                component.RenderFrame = control =>
                {
                    Func<CancellationToken, Task> callback = _ =>
                    {
                        started.TrySetResult();
                        return returnNull ? null! : callbackCompletion.Task;
                    };
                    if (repeat)
                    {
                        TimerHooks.useInterval(control, callback, TimeSpan.FromMilliseconds(1), [], clock);
                    }
                    else
                    {
                        TimerHooks.useTimeout(control, callback, TimeSpan.Zero, [], clock);
                    }
                };
                component.InitializeForTest();
                if (repeat)
                {
                    clock.Advance(1);
                }

                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (!returnNull)
                {
                    callbackCompletion.SetException(new ExpectedCallbackException());
                }

                var exception = await observed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                if (returnNull)
                {
                    Assert.IsType<InvalidOperationException>(exception);
                    Assert.Contains("null Task", exception.Message);
                }
                else
                {
                    Assert.IsType<ExpectedCallbackException>(exception);
                }

                Assert.Equal(0, clock.ActiveTimerCount);
            }
            finally
            {
                Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
            }
        });

    [Fact]
    public Task Throttle_QueuesTheLatestLeadingInputAndDoesNotRepeatWithoutNewInputs() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var values = new List<int>();
            var leading = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                var value = control.Source.Value;
                ThrottleHooks.useThrottle(
                    control,
                    () =>
                    {
                        values.Add(value);
                        leading.TrySetResult();
                    },
                    TimeSpan.FromMilliseconds(300),
                    [value],
                    clock);
                Assert.Empty(values);
            };
            component.InitializeForTest();
            component.Source.Value = 2;
            component.Source.Value = 3;
            Assert.Empty(values);
            await leading.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal([3], values);
            await clock.WaitForTimerCount(1);
            clock.Advance(1000);
            await DrainDispatcher();
            Assert.Equal([3], values);
            Assert.Equal(1, clock.CreatedTimerCount);
            Assert.Equal(0, clock.ActiveTimerCount);
        });

    [Fact]
    public Task Throttle_UsesFixedWindowsAndTheLastExplicitlyAcceptedCallback() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var values = new List<int>();
            var multiplier = 1;
            var leading = new TaskCompletionSource();
            var trailing = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                var value = control.Source.Value;
                var frameMultiplier = multiplier;
                object?[] dependencies = [value];
                ThrottleHooks.useThrottle(
                    control,
                    () =>
                    {
                        values.Add(value * frameMultiplier);
                        if (values.Count == 1)
                        {
                            leading.TrySetResult();
                        }
                        else
                        {
                            trailing.TrySetResult();
                        }
                    },
                    TimeSpan.FromMilliseconds(300),
                    dependencies,
                    clock);
                dependencies[0] = 999;
            };
            component.InitializeForTest();
            await leading.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await clock.WaitForTimerCount(1);
            clock.Advance(80);
            component.Source.Value = 2;
            clock.Advance(80);
            component.Source.Value = 3;
            clock.Advance(80);
            component.Source.Value = 4;
            multiplier = 10;
            component.RenderAgain();
            Assert.Equal([1], values);
            Assert.Equal(1, clock.CreatedTimerCount);
            clock.Advance(59);
            await DrainDispatcher();
            Assert.Equal([1], values);
            clock.Advance(1);
            await trailing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal([1, 4], values);
            await clock.WaitForTimerCount(2);
            clock.Advance(1000);
            await DrainDispatcher();
            Assert.Equal([1, 4], values);
            Assert.Equal(2, clock.CreatedTimerCount);
        });

    [Fact]
    public Task Throttle_IntervalChangesCancelOldPendingInputsAndStartANewLeadingSeries() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var interval = TimeSpan.FromMilliseconds(300);
            var values = new List<int>();
            var leading = new TaskCompletionSource();
            var restarted = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                var value = control.Source.Value;
                ThrottleHooks.useThrottle(
                    control,
                    () =>
                    {
                        values.Add(value);
                        if (values.Count == 1)
                        {
                            leading.TrySetResult();
                        }
                        else
                        {
                            restarted.TrySetResult();
                        }
                    },
                    interval,
                    [value],
                    clock);
            };
            component.InitializeForTest();
            await leading.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await clock.WaitForTimerCount(1);
            clock.Advance(200);
            component.Source.Value = 2;
            interval = TimeSpan.FromMilliseconds(1000);
            component.RenderAgain();
            await restarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal([1, 2], values);
            await clock.WaitForTimerCount(2);
            Assert.Equal(1, clock.ActiveTimerCount);
            clock.Advance(100);
            await DrainDispatcher();
            Assert.Equal([1, 2], values);
        });

    [Fact]
    public Task Throttle_SelectorStateIsStableAndRetainsTheAcceptedFramesSelector() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            State<int>? result = null;
            State<int>? independent = null;
            var multiplier = 10;
            var selectorCalls = 0;
            var initialLeading = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                var frameMultiplier = multiplier;
                result = ThrottleHooks.useThrottle(
                    control,
                    control.Source,
                    value =>
                    {
                        selectorCalls++;
                        if (selectorCalls == 2)
                        {
                            initialLeading.TrySetResult();
                        }
                        return value * frameMultiplier;
                    },
                    TimeSpan.FromMilliseconds(300),
                    clock);
                independent = ThrottleHooks.useThrottle(
                    control, control.Source, TimeSpan.FromMilliseconds(1000), clock);
            };
            component.InitializeForTest();
            var originalResult = Assert.IsType<State<int>>(result);
            Assert.Equal(10, originalResult.Value);
            Assert.Equal(1, selectorCalls);
            Assert.NotSame(result, independent);
            await initialLeading.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await clock.WaitForTimerCount(2);

            component.Source.Value = 2;
            multiplier = 100;
            component.RenderAgain();
            Assert.Same(originalResult, result);
            Assert.Equal(10, result.Value);
            var publication = ObserveValue(result, 20);
            clock.Advance(300);
            await publication;
            Assert.Equal(20, result.Value);
            Assert.Equal(1, independent!.Value);
            Assert.Equal(3, selectorCalls);
        });

    [Fact]
    public Task Throttle_DetachCancelsItsWindowAndReattachQueuesTheLatestLeadingInput() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var values = new List<int>();
            var first = new TaskCompletionSource();
            var second = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                var value = control.Source.Value;
                ThrottleHooks.useThrottle(
                    control,
                    () =>
                    {
                        values.Add(value);
                        if (values.Count == 1)
                        {
                            first.TrySetResult();
                        }
                        else
                        {
                            second.TrySetResult();
                        }
                    },
                    TimeSpan.FromMilliseconds(300),
                    [value],
                    clock);
            };
            var window = new Window { Content = component };
            window.Show();
            try
            {
                await first.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await clock.WaitForTimerCount(1);
                component.Source.Value = 2;
                clock.Advance(200);
                window.Content = null;
                Assert.Equal(0, clock.ActiveTimerCount);
                component.Source.Value = 3;
                clock.Advance(1000);
                await DrainDispatcher();
                Assert.Equal([1], values);
                window.Content = component;
                await second.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal([1, 3], values);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Throttle_AbortedFramesDoNotReplaceThePendingAcceptedInput() =>
        OnDispatcher(async () =>
        {
            var clock = new TimerClock();
            var component = new TimerComponent();
            var value = 1;
            var calls = new List<int>();
            var leading = new TaskCompletionSource();
            var trailing = new TaskCompletionSource();
            component.RenderFrame = control =>
            {
                var snapshot = value;
                ThrottleHooks.useThrottle(
                    control,
                    () =>
                    {
                        calls.Add(snapshot);
                        if (calls.Count == 1)
                        {
                            leading.TrySetResult();
                        }
                        else
                        {
                            trailing.TrySetResult();
                        }
                    },
                    TimeSpan.FromMilliseconds(300),
                    [snapshot],
                    clock);
            };
            component.InitializeForTest();
            await leading.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await clock.WaitForTimerCount(1);
            value = 2;
            component.RenderAgain();
            value = 3;
            component.FailFrame = true;
            Assert.Throws<ExpectedFrameException>(component.RenderAgain);
            component.FailFrame = false;
            clock.Advance(300);
            await trailing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal([1, 2], calls);
        });

    [Fact]
    public Task Throttle_DetachCancelsTheLeadingCallbackAlreadyQueuedOnTheDispatcher() =>
        OnDispatcher(async () =>
        {
            var component = new TimerComponent();
            var calls = new List<int>();
            component.RenderFrame = control => control.useThrottle(
                () => calls.Add(control.Source.Value), 100, [control.Source.Value]);
            var window = new Window { Content = component };
            window.Show();
            try
            {
                window.Content = null;
                await DrainDispatcher();
                Assert.Empty(calls);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Throttle_CallbackFailuresAreObservedByTheEffectRuntime() =>
        OnDispatcher(async () =>
        {
            var component = new TimerComponent();
            var observed = new TaskCompletionSource<Exception>();
            void OnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs args)
            {
                args.Handled = true;
                observed.TrySetResult(args.Exception);
            }

            Dispatcher.UIThread.UnhandledException += OnUnhandledException;
            try
            {
                component.RenderFrame = control => control.useThrottle(
                    static () => throw new ExpectedCallbackException(), 100, []);
                component.InitializeForTest();
                Assert.IsType<ExpectedCallbackException>(
                    await observed.Task.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            finally
            {
                Dispatcher.UIThread.UnhandledException -= OnUnhandledException;
            }
        });

    [Fact]
    public void InvalidTimerIntervalsAreRejectedBeforeRegisteringHooks()
    {
        var component = new TimerComponent();
        var callback = static () => { };
        Func<CancellationToken, Task> asyncCallback = static _ => Task.CompletedTask;
        foreach (var delay in new[] { -1, int.MinValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => component.useTimeout(callback, delay));
            Assert.Throws<ArgumentOutOfRangeException>(() => component.useTimeout(asyncCallback, delay));
            Assert.Throws<ArgumentOutOfRangeException>(() => component.useInterval(callback, delay));
            Assert.Throws<ArgumentOutOfRangeException>(() => component.useThrottle(callback, delay, []));
        }

        foreach (var interval in new[] { TimeSpan.Zero, TimeSpan.FromTicks(9999) })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => component.useInterval(callback, interval));
            Assert.Throws<ArgumentOutOfRangeException>(() => component.useInterval(asyncCallback, interval));
            Assert.Throws<ArgumentOutOfRangeException>(() => component.useThrottle(callback, interval, []));
            Assert.Throws<ArgumentOutOfRangeException>(() => component.useThrottle(component.Source, interval));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                component.useThrottle(component.Source, static value => value, interval));
        }

        var tooLong = TimeSpan.FromMilliseconds(int.MaxValue) + TimeSpan.FromTicks(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => component.useTimeout(callback, tooLong));
        Assert.Throws<ArgumentOutOfRangeException>(() => component.useInterval(callback, tooLong));
        Assert.Throws<ArgumentOutOfRangeException>(() => component.useThrottle(callback, tooLong, []));
    }

    private static async Task OnDispatcher(Func<Task> test)
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(async () =>
        {
            await test();
            return true;
        }, CancellationToken.None);
    }

    private static async Task DrainDispatcher() =>
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

    private static async Task ObserveValue(State<int> state, int expected)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = state.Subscribe(value =>
        {
            if (value == expected)
            {
                completion.TrySetResult();
            }
        });
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class ExpectedFrameException : Exception;

    private sealed class ExpectedCallbackException : Exception;

    private sealed class TimerComponent : AkburaControl
    {
        private static readonly StateInfo<int> s_sourceInfo = new("source", static _ => 1);
        private readonly ImmutableArray<State> _states;
        private readonly Border _root = new();

        public TimerComponent()
            : base(AkburaEngine.Empty)
        {
            Source = CreateState(s_sourceInfo);
            _states = [Source];
        }

        public State<int> Source { get; }

        public Action<TimerComponent>? RenderFrame { get; set; }

        public bool FailFrame { get; set; }

        public void InitializeForTest() => base.OnInitialized();

        public void RenderAgain() => InvalidState();

        protected override Control FirstUpdate() => _root;

        protected override Control Update()
        {
            RenderFrame?.Invoke(this);
            if (FailFrame)
            {
                throw new ExpectedFrameException();
            }

            return _root;
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];

        protected override ImmutableArray<InjectService> GetServices() => [];

        protected override ImmutableArray<State> GetStates() => _states;
    }

    private sealed class TimerClock : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private readonly List<(int Count, TaskCompletionSource Completion)> _waiters = [];
        private long _ticks;

        public int CreatedTimerCount
        {
            get
            {
                lock (_gate)
                {
                    return _timers.Count;
                }
            }
        }

        public int ActiveTimerCount
        {
            get
            {
                lock (_gate)
                {
                    return _timers.Count(timer => timer.DueAt != long.MaxValue);
                }
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            lock (_gate)
            {
                var timer = new ManualTimer(this, callback, state);
                timer.Change(dueTime, period);
                _timers.Add(timer);
                for (var index = _waiters.Count - 1; index >= 0; index--)
                {
                    if (_timers.Count >= _waiters[index].Count)
                    {
                        _waiters[index].Completion.TrySetResult();
                        _waiters.RemoveAt(index);
                    }
                }

                return timer;
            }
        }

        public Task WaitForTimerCount(int count)
        {
            lock (_gate)
            {
                if (_timers.Count >= count)
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, completion));
                return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        public void Advance(int milliseconds)
        {
            List<ManualTimer> due;
            lock (_gate)
            {
                _ticks += TimeSpan.FromMilliseconds(milliseconds).Ticks;
                due = _timers.Where(timer => timer.DueAt <= _ticks).ToList();
                foreach (var timer in due)
                {
                    timer.DueAt = long.MaxValue;
                }
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer(TimerClock clock, TimerCallback callback, object? state) : ITimer
        {
            public long DueAt { get; set; } = long.MaxValue;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                lock (clock._gate)
                {
                    DueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? long.MaxValue
                        : clock._ticks + dueTime.Ticks;
                    return true;
                }
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (clock._gate)
                {
                    DueAt = long.MaxValue;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
