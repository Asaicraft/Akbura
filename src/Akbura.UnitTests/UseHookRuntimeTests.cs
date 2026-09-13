using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class UseHookRuntimeTests
{
    [Fact]
    public void UseEffect_WithoutDependenciesRunsAfterEveryRender_EmptyDependenciesRunsOnce()
    {
        var component = new HookComponent
        {
            RenderFrame = control =>
            {
                control.useEffect((Action)(() => control.EveryRenderCount++));
                EffectHooks.useEffect(control, (Action)(() => control.FirstRenderCount++), []);
            },
        };

        component.InitializeForTest();
        component.RenderAgain();
        component.RenderAgain();

        Assert.Equal(3, component.UpdateCount);
        Assert.Equal(3, component.EveryRenderCount);
        Assert.Equal(1, component.FirstRenderCount);
    }

    [Fact]
    public void UseEffect_RestartsOnlyAfterDependenciesChange()
    {
        var component = new HookComponent
        {
            Dependency = 1,
            RenderFrame = control => control.useEffect(
                (Action)(() => control.EffectRuns++),
                [control.Dependency]),
        };

        component.InitializeForTest();
        component.RenderAgain();
        component.Dependency = 2;
        component.RenderAgain();

        Assert.Equal(3, component.UpdateCount);
        Assert.Equal(2, component.EffectRuns);
    }

    [Fact]
    public void UseEffect_UsesCustomDependencyComparerForTheWholeList()
    {
        var comparer = new ParityDependenciesComparer();
        var component = new HookComponent
        {
            Dependency = 1,
            RenderFrame = control => control.useEffect(
                (Action)(() => control.EffectRuns++),
                [control.Dependency],
                comparer),
        };

        component.InitializeForTest();
        component.Dependency = 3;
        component.RenderAgain();
        component.Dependency = 4;
        component.RenderAgain();

        Assert.Equal(2, component.EffectRuns);
        Assert.Equal(2, comparer.CallCount);
    }

    [Fact]
    public void UseEffect_CancelsBeforeRunningPreviousCleanup()
    {
        var component = new HookComponent
        {
            Dependency = 1,
            RenderFrame = control => control.useEffect(
                (Func<CancellationToken, Action?>)(cancellationToken =>
                {
                    var dependency = control.Dependency;
                    control.Events.Add($"run:{dependency}");
                    return () => control.Events.Add(
                        $"cleanup:{dependency}:{cancellationToken.IsCancellationRequested}");
                }),
                [control.Dependency]),
        };

        component.InitializeForTest();
        component.Dependency = 2;
        component.RenderAgain();

        Assert.Equal(
            ["run:1", "cleanup:1:True", "run:2"],
            component.Events);
    }

    [Fact]
    public void ChangedHookCount_FailsFastAndKeepsPreviousFrame()
    {
        var component = new HookComponent
        {
            RenderFrame = control =>
            {
                EffectHooks.useEffect(control, (Action)(() => control.EffectRuns++), []);
                if (control.IncludeSecondHook)
                {
                    EffectHooks.useEffect(control, (Action)(() => control.EffectRuns++), []);
                }
            },
        };
        component.InitializeForTest();
        component.IncludeSecondHook = true;

        var exception = Assert.Throws<AkburaUseHooksFrameChangedException>(
            component.RenderAgain);

        Assert.Same(component, exception.AkburaControl);
        Assert.Equal(1, exception.ExpectedCount);
        Assert.Equal(2, exception.ActualCount);

        component.IncludeSecondHook = false;
        component.RenderAgain();
        Assert.Equal(1, component.EffectRuns);
    }

    [Fact]
    public void ChangedHookOrder_FailsFast()
    {
        var component = new HookComponent
        {
            RenderFrame = control =>
            {
                if (control.ReverseHooks)
                {
                    RegisterTokenEffect(control);
                    RegisterActionEffect(control);
                }
                else
                {
                    RegisterActionEffect(control);
                    RegisterTokenEffect(control);
                }
            },
        };
        component.InitializeForTest();
        component.ReverseHooks = true;

        var exception = Assert.Throws<AkburaUseHooksFrameChangedException>(
            component.RenderAgain);

        Assert.Equal(0, exception.MismatchIndex);
        Assert.Equal(2, exception.ExpectedCount);
        Assert.Equal(2, exception.ActualCount);
    }

    [Fact]
    public void UseEffect_OutsideUpdateFrameFailsFast()
    {
        var component = new HookComponent();

        var exception = Assert.Throws<AkburaUseHookOutsideRenderException>(() =>
            EffectHooks.useEffect(component, (Action)(() => { })));

        Assert.Same(component, exception.AkburaControl);
    }

    [Fact]
    public void UseEffect_CalledFromAnotherEffectFailsFast()
    {
        var component = new HookComponent
        {
            RenderFrame = control => EffectHooks.useEffect(
                control,
                (Action)(() => EffectHooks.useEffect(
                    control,
                    (Action)(() => { }))),
                []),
        };

        var exception = Assert.Throws<AkburaUseHookOutsideRenderException>(
            component.InitializeForTest);

        Assert.Same(component, exception.AkburaControl);
    }

    [Fact]
    public void StateChangeInsideEffect_QueuesAnotherNonRecursiveRender()
    {
        var component = new HookComponent
        {
            RenderFrame = control => EffectHooks.useEffect(
                control,
                (Action)(() => control.Trigger.Value++),
                []),
        };

        component.InitializeForTest();

        Assert.Equal(2, component.UpdateCount);
        Assert.Equal(1, component.Trigger.Value);
        Assert.Equal(1, component.MaxUpdateDepth);
    }

    [Fact]
    public void FailedRender_DoesNotReplaceTheLastCompletedHookFrame()
    {
        var component = new HookComponent
        {
            RenderFrame = control =>
            {
                EffectHooks.useEffect(control, (Action)(() => control.EffectRuns++), []);
                if (control.ThrowDuringRender)
                {
                    throw new ExpectedRenderException();
                }
            },
        };
        component.InitializeForTest();
        component.ThrowDuringRender = true;

        Assert.Throws<ExpectedRenderException>(component.RenderAgain);

        component.ThrowDuringRender = false;
        component.RenderAgain();
        Assert.Equal(1, component.EffectRuns);
    }

    [Fact]
    public void UseAvaloniaProperty_TracksInitialAndSubsequentValues()
    {
        var component = new AvaloniaPropertyHookComponent
        {
            ObservedValue = 4,
        };

        component.InitializeForTest();

        Assert.Equal(4, component.ObservedState.InitialValue);
        Assert.Equal(4, component.ObservedState.Value);
        Assert.Equal(1, component.UpdateCount);

        component.ObservedValue = 7;

        Assert.Equal(7, component.ObservedState.Value);
        Assert.Equal(2, component.UpdateCount);

        component.ObservedValue = 7;
        Assert.Equal(2, component.UpdateCount);

        component.ObservedState.Value = 9;

        Assert.Equal(9, component.ObservedValue);
        Assert.Equal(9, component.ObservedState.Value);
        Assert.Equal(3, component.UpdateCount);

        component.ObservedState.Value = 9;
        Assert.Equal(3, component.UpdateCount);
    }

    [Fact]
    public void UseAvaloniaPropertyEffect_RunsOnChangesWithoutRenderAndCleansUpInOrder()
    {
        var run = 0;
        var component = new HookComponent
        {
            RenderFrame = control => AvaloniaPropertyHooks.useAvaloniaProperty(
                control,
                (Func<CancellationToken, Action?>)(cancellationToken =>
                {
                    var currentRun = ++run;
                    control.Events.Add($"run:{currentRun}");
                    return () => control.Events.Add(
                        $"cleanup:{currentRun}:{cancellationToken.IsCancellationRequested}");
                }),
                [Control.WidthProperty, Control.HeightProperty]),
        };

        component.InitializeForTest();

        Assert.Equal(1, component.UpdateCount);
        Assert.Equal(1, run);

        component.Width = 120;

        Assert.Equal(1, component.UpdateCount);
        Assert.Equal(2, run);
        Assert.Equal(
            ["run:1", "cleanup:1:True", "run:2"],
            component.Events);

        component.Width = 120;
        Assert.Equal(2, run);

        component.Height = 80;

        Assert.Equal(1, component.UpdateCount);
        Assert.Equal(3, run);
        Assert.Equal(
            [
                "run:1",
                "cleanup:1:True",
                "run:2",
                "cleanup:2:True",
                "run:3",
            ],
            component.Events);
    }

    [Fact]
    public void UseAvaloniaPropertyEffect_UsesCallbackFromLatestRender()
    {
        var component = new HookComponent
        {
            Dependency = 1,
            RenderFrame = control =>
            {
                var version = control.Dependency;
                AvaloniaPropertyHooks.useAvaloniaProperty(
                    control,
                    (Action)(() => control.Events.Add($"run:{version}")),
                    [Control.WidthProperty]);
            },
        };

        component.InitializeForTest();
        component.Dependency = 2;
        component.RenderAgain();

        Assert.Equal(["run:1"], component.Events);

        component.Width = 64;

        Assert.Equal(["run:1", "run:2"], component.Events);
        Assert.Equal(2, component.UpdateCount);
    }

    [Fact]
    public void UseAvaloniaPropertyEffect_ParticipatesInHookFrameCount()
    {
        var component = new HookComponent
        {
            RenderFrame = control =>
            {
                AvaloniaPropertyHooks.useAvaloniaProperty(
                    control,
                    (Action)(() => { }),
                    [Control.WidthProperty]);
                if (control.IncludeSecondHook)
                {
                    AvaloniaPropertyHooks.useAvaloniaProperty(
                        control,
                        (Action)(() => { }),
                        [Control.HeightProperty]);
                }
            },
        };
        component.InitializeForTest();
        component.IncludeSecondHook = true;

        var exception = Assert.Throws<AkburaUseHooksFrameChangedException>(
            component.RenderAgain);

        Assert.Equal(1, exception.ExpectedCount);
        Assert.Equal(2, exception.ActualCount);
    }

    [Fact]
    public void CustomUseHook_OwnsPersistentStateAndParticipatesInFrameCount()
    {
        var component = new HookComponent
        {
            RenderFrame = control =>
            {
                CustomHooks.useCounter(control);
                if (control.IncludeSecondHook)
                {
                    CustomHooks.useCounter(control);
                }
            },
        };

        component.InitializeForTest();
        component.RenderAgain();

        Assert.Equal(1, component.CustomHookStateCreations);
        Assert.Equal(2, component.CustomHookApplications);

        component.IncludeSecondHook = true;
        var exception = Assert.Throws<AkburaUseHooksFrameChangedException>(
            component.RenderAgain);

        Assert.Equal(1, exception.ExpectedCount);
        Assert.Equal(2, exception.ActualCount);
        Assert.Equal(1, component.CustomHookStateCreations);
        Assert.Equal(2, component.CustomHookApplications);
    }

    [Fact]
    public void StopForDetach_CancelsAndCleansUpBeforeResumeRestartsTheEffect()
    {
        var component = new HookComponent();
        var runtime = new UseHookRuntime(component);
        var events = new List<string>();
        var run = 0;
        var registration = new UseEffectRegistration(
            new UseHookKey(),
            cancellationToken =>
            {
                var currentRun = ++run;
                events.Add($"run:{currentRun}");
                return ValueTask.FromResult<IDisposable?>(new CallbackDisposable(
                    () => events.Add($"cleanup:{currentRun}:{cancellationToken.IsCancellationRequested}")));
            },
            hasDependencies: true,
            dependencies: [],
            comparer: null);

        CompleteFrame(runtime, registration);
        runtime.StopForDetach();

        Assert.True(runtime.NeedsRestart);
        Assert.Equal(["run:1", "cleanup:1:True"], events);

        runtime.Resume();
        CompleteFrame(runtime, registration);

        Assert.False(runtime.NeedsRestart);
        Assert.Equal(["run:1", "cleanup:1:True", "run:2"], events);
    }

    [Fact]
    public void InitialFrame_FactoryFailureCleansUpCreatedSlotsWithoutApplyingEffectsAndCanRetry()
    {
        var runtime = new UseHookRuntime(new HookComponent());
        var firstKey = new UseHookKey();
        var secondKey = new UseHookKey();
        var creations = 0;
        var applications = 0;
        var detachments = 0;

        runtime.BeginFrame();
        runtime.Register(CreateTestRegistration(
            firstKey,
            () => creations++,
            () => applications++,
            () => detachments++));
        runtime.Register(new DelegateUseHookRegistration<TestHookState, int>(
            secondKey,
            0,
            static _ => throw new ExpectedFactoryException(),
            static (_, _) => { },
            static _ => { }));

        Assert.Throws<ExpectedFactoryException>(runtime.CompleteFrame);
        Assert.False(runtime.HasSlots);
        Assert.Equal(1, creations);
        Assert.Equal(0, applications);
        Assert.Equal(1, detachments);

        runtime.BeginFrame();
        runtime.Register(CreateTestRegistration(
            firstKey,
            () => creations++,
            () => applications++,
            () => detachments++));
        runtime.CompleteFrame();

        Assert.True(runtime.HasSlots);
        Assert.Equal(2, creations);
        Assert.Equal(1, applications);
        Assert.Equal(1, detachments);
    }

    [Fact]
    public void InitialFrame_ApplyFailureStopsCommittedSlotsAndCanRetry()
    {
        var runtime = new UseHookRuntime(new HookComponent());
        var key = new UseHookKey();
        var creations = 0;
        var applications = 0;
        var detachments = 0;
        var failApply = true;

        runtime.BeginFrame();
        runtime.Register(CreateTestRegistration(
            key,
            () => creations++,
            () =>
            {
                applications++;
                if (failApply)
                {
                    throw new ExpectedApplyException();
                }
            },
            () => detachments++));

        Assert.Throws<ExpectedApplyException>(runtime.CompleteFrame);
        Assert.True(runtime.HasSlots);
        Assert.True(runtime.IsFrameCommitted);
        Assert.True(runtime.NeedsRestart);
        Assert.Equal(1, creations);
        Assert.Equal(1, applications);
        Assert.Equal(1, detachments);

        failApply = false;
        runtime.BeginFrame();
        runtime.Register(CreateTestRegistration(
            key,
            () => creations++,
            () => applications++,
            () => detachments++));
        runtime.CompleteFrame();

        Assert.True(runtime.HasSlots);
        Assert.False(runtime.NeedsRestart);
        Assert.Equal(1, creations);
        Assert.Equal(2, applications);
        Assert.Equal(1, detachments);
    }

    [Fact]
    public void ExistingFrame_ApplyFailureStopsAllSlotsAndCanRetry()
    {
        var runtime = new UseHookRuntime(new HookComponent());
        var firstKey = new UseHookKey();
        var secondKey = new UseHookKey();
        var firstApplications = 0;
        var secondApplications = 0;
        var firstDetachments = 0;
        var secondDetachments = 0;
        var failSecondApply = false;

        void CompleteCurrentFrame()
        {
            runtime.BeginFrame();
            runtime.Register(CreateTestRegistration(
                firstKey,
                static () => { },
                () => firstApplications++,
                () => firstDetachments++));
            runtime.Register(CreateTestRegistration(
                secondKey,
                static () => { },
                () =>
                {
                    secondApplications++;
                    if (failSecondApply)
                    {
                        throw new ExpectedApplyException();
                    }
                },
                () => secondDetachments++));
            runtime.CompleteFrame();
        }

        CompleteCurrentFrame();
        failSecondApply = true;

        Assert.Throws<ExpectedApplyException>(CompleteCurrentFrame);
        Assert.True(runtime.HasSlots);
        Assert.True(runtime.NeedsRestart);
        Assert.Equal(1, firstDetachments);
        Assert.Equal(1, secondDetachments);

        failSecondApply = false;
        CompleteCurrentFrame();

        Assert.False(runtime.NeedsRestart);
        Assert.Equal(3, firstApplications);
        Assert.Equal(3, secondApplications);
        Assert.Equal(1, firstDetachments);
        Assert.Equal(1, secondDetachments);
    }

    [Fact]
    public void StopForDetach_StopsEverySlotAndAggregatesFailures()
    {
        var runtime = new UseHookRuntime(new HookComponent());
        var firstDetachments = 0;
        var secondDetachments = 0;

        runtime.BeginFrame();
        runtime.Register(CreateTestRegistration(
            new UseHookKey(),
            static () => { },
            static () => { },
            () =>
            {
                firstDetachments++;
                throw new FirstDetachException();
            }));
        runtime.Register(CreateTestRegistration(
            new UseHookKey(),
            static () => { },
            static () => { },
            () =>
            {
                secondDetachments++;
                throw new SecondDetachException();
            }));
        runtime.CompleteFrame();

        var exception = Assert.Throws<AggregateException>(runtime.StopForDetach);

        Assert.True(runtime.NeedsRestart);
        Assert.Equal(1, firstDetachments);
        Assert.Equal(1, secondDetachments);
        Assert.Collection(
            exception.InnerExceptions,
            failure => Assert.IsType<FirstDetachException>(failure),
            failure => Assert.IsType<SecondDetachException>(failure));
    }

    [Fact]
    public void DetachedRuntime_DoesNotApplyFramesUntilItIsResumed()
    {
        var runtime = new UseHookRuntime(new HookComponent());
        var key = new UseHookKey();
        var applications = 0;
        var detachments = 0;
        var registration = CreateTestRegistration(
            key,
            static () => { },
            () => applications++,
            () => detachments++);

        CompleteFrame(runtime, registration);
        runtime.StopForDetach();
        CompleteFrame(runtime, registration);

        Assert.True(runtime.NeedsRestart);
        Assert.Equal(1, applications);
        Assert.Equal(1, detachments);

        runtime.Resume();
        CompleteFrame(runtime, registration);

        Assert.False(runtime.NeedsRestart);
        Assert.Equal(2, applications);
        Assert.Equal(1, detachments);
    }

    [Fact]
    public void ResetForHotReload_RecreatesEffectsWithUnchangedDependencies()
    {
        var runtime = new UseHookRuntime(new HookComponent());
        var key = new UseHookKey();
        var events = new List<string>();

        CompleteFrame(runtime, CreateEffectRegistration(
            key,
            () =>
            {
                events.Add("run:before");
                return new CallbackDisposable(
                    () => events.Add("cleanup:before"));
            }));

        runtime.ResetForHotReload();

        CompleteFrame(runtime, CreateEffectRegistration(
            key,
            () =>
            {
                events.Add("run:after");
                return new CallbackDisposable(
                    () => events.Add("cleanup:after"));
            }));

        Assert.Equal(
            ["run:before", "cleanup:before", "run:after"],
            events);
    }

    [Fact]
    public void AbortedFrame_CompletesPendingHotReloadReset()
    {
        var runtime = new UseHookRuntime(new HookComponent());
        var key = new UseHookKey();
        var events = new List<string>();

        CompleteFrame(runtime, CreateEffectRegistration(
            key,
            () =>
            {
                events.Add("run:before");
                return new CallbackDisposable(
                    () => events.Add("cleanup:before"));
            }));

        runtime.BeginFrame();
        runtime.ResetForHotReload();
        runtime.AbortFrame();

        Assert.False(runtime.HasSlots);
        Assert.Equal(["run:before", "cleanup:before"], events);

        CompleteFrame(runtime, CreateEffectRegistration(
            key,
            () =>
            {
                events.Add("run:after");
                return null;
            }));

        Assert.Equal(
            ["run:before", "cleanup:before", "run:after"],
            events);
    }

    [Fact]
    public void ApplyHotReload_ReplaysAnEffectWithUnchangedDependencies()
    {
        var component = new HookComponent
        {
            Dependency = 1,
            RenderFrame = control =>
            {
                var version = control.Dependency;
                EffectHooks.useEffect(
                    control,
                    (Func<Action?>)(() =>
                    {
                        control.Events.Add($"run:{version}");
                        return () => control.Events.Add(
                            $"cleanup:{version}");
                    }),
                    []);
            },
        };
        component.InitializeForTest();
        component.Dependency = 2;

        component.ApplyHotReload(static _ => { });

        Assert.Equal(
            ["run:1", "cleanup:1", "run:2"],
            component.Events);
    }

    [Fact]
    public void ApplyHotReload_RecreatesReorderedCompatibleEffects()
    {
        var component = new HookComponent
        {
            RenderFrame = control =>
            {
                if (control.ReverseHooks)
                {
                    RegisterTrackedEffect(control, "second");
                    RegisterTrackedEffect(control, "first");
                }
                else
                {
                    RegisterTrackedEffect(control, "first");
                    RegisterTrackedEffect(control, "second");
                }
            },
        };
        component.InitializeForTest();
        component.ReverseHooks = true;

        component.ApplyHotReload(static _ => { });

        Assert.Equal(
            [
                "run:first",
                "run:second",
                "cleanup:first",
                "cleanup:second",
                "run:second",
                "run:first",
            ],
            component.Events);
    }

    [Fact]
    public void EffectStop_AttemptsCleanupAfterCancellationFailure()
    {
        var key = new UseHookKey();
        var slot = new UseEffectSlot(key);
        var events = new List<string>();

        slot.Apply(CreateEffectRegistration(
            key,
            cancellationToken =>
            {
                cancellationToken.Register(() =>
                {
                    events.Add("cancel");
                    throw new ExpectedCancellationException();
                });
                return new CallbackDisposable(() =>
                {
                    events.Add("cleanup");
                    throw new ExpectedCleanupException();
                });
            }));

        var exception = Assert.Throws<AggregateException>(slot.StopForDetach);

        Assert.Equal(["cancel", "cleanup"], events);
        Assert.Contains(
            exception.InnerExceptions,
            failure => failure is ExpectedCancellationException);
        Assert.Contains(
            exception.InnerExceptions,
            failure => failure is ExpectedCleanupException);

        slot.Apply(CreateEffectRegistration(
            key,
            () =>
            {
                events.Add("restart");
                return null;
            }));

        Assert.Equal(["cancel", "cleanup", "restart"], events);
    }

    [Fact]
    public async Task CleanupFromStaleAsyncRun_IsDisposedOnUiThread()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(async () =>
        {
            var key = new UseHookKey();
            var slot = new UseEffectSlot(key);
            var firstCompletion = new TaskCompletionSource<IDisposable?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupUsedUiThread = false;
            var staleCleanup = new TrackingDisposable(
                () => cleanupUsedUiThread =
                    Dispatcher.UIThread.CheckAccess());
            var run = 0;
            UseEffectCallback callback = _ =>
            {
                run++;
                return run == 1
                    ? new ValueTask<IDisposable?>(firstCompletion.Task)
                    : ValueTask.FromResult<IDisposable?>(null);
            };

            slot.Apply(new UseEffectRegistration(
                key,
                callback,
                hasDependencies: true,
                dependencies: [1],
                comparer: null));
            slot.Apply(new UseEffectRegistration(
                key,
                callback,
                hasDependencies: true,
                dependencies: [2],
                comparer: null));

            firstCompletion.SetResult(staleCleanup);
            for (var attempt = 0;
                attempt < 50 && !staleCleanup.IsDisposed;
                attempt++)
            {
                await Task.Delay(10);
            }

            Assert.True(staleCleanup.IsDisposed);
            Assert.True(cleanupUsedUiThread);
            Assert.Equal(2, run);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailureFromStaleAsyncRun_IsIgnoredAfterGenerationChanges()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(async () =>
        {
            var key = new UseHookKey();
            var slot = new UseEffectSlot(key);
            var firstCompletion = new TaskCompletionSource<IDisposable?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var run = 0;
            UseEffectCallback callback = _ => ++run == 1
                ? new ValueTask<IDisposable?>(firstCompletion.Task)
                : ValueTask.FromResult<IDisposable?>(null);

            slot.Apply(new UseEffectRegistration(
                key,
                callback,
                hasDependencies: true,
                dependencies: [1],
                comparer: null));
            slot.Apply(new UseEffectRegistration(
                key,
                callback,
                hasDependencies: true,
                dependencies: [2],
                comparer: null));

            firstCompletion.SetException(
                new ExpectedAsyncEffectException());

            await Task.Delay(100);

            slot.Trigger();
            Assert.Equal(3, run);
            return true;
        }, CancellationToken.None);

    }

    private static void CompleteFrame(
        UseHookRuntime runtime,
        UseEffectRegistration registration)
    {
        CompleteFrame(
            runtime,
            new DelegateUseHookRegistration<UseEffectSlot, UseEffectRegistration>(
                registration.Key,
                registration,
                static current => new UseEffectSlot(current.Key),
                static (slot, current) => slot.Apply(current),
                static slot => slot.StopForDetach()));
    }

    private static void CompleteFrame(
        UseHookRuntime runtime,
        IUseHookRegistration registration)
    {
        runtime.BeginFrame();
        runtime.Register(registration);
        runtime.CompleteFrame();
    }

    private static IUseHookRegistration CreateTestRegistration(
        UseHookKey key,
        Action create,
        Action apply,
        Action detach)
    {
        return new DelegateUseHookRegistration<TestHookState, Action>(
            key,
            apply,
            _ =>
            {
                create();
                return new TestHookState(detach);
            },
            static (_, currentApply) => currentApply(),
            static state => state.Detach());
    }

    private static UseEffectRegistration CreateEffectRegistration(
        UseHookKey key,
        Func<IDisposable?> effect)
    {
        return CreateEffectRegistration(key, _ => effect());
    }

    private static UseEffectRegistration CreateEffectRegistration(
        UseHookKey key,
        Func<CancellationToken, IDisposable?> effect)
    {
        return new UseEffectRegistration(
            key,
            cancellationToken => ValueTask.FromResult(
                effect(cancellationToken)),
            hasDependencies: true,
            dependencies: [],
            comparer: null);
    }

    private static void RegisterActionEffect(HookComponent control)
    {
        EffectHooks.useEffect(control, (Action)(() => { }), []);
    }

    private static void RegisterTokenEffect(HookComponent control)
    {
        EffectHooks.useEffect(control, (Action<CancellationToken>)(_ => { }), []);
    }

    private static void RegisterTrackedEffect(
        HookComponent control,
        string name)
    {
        EffectHooks.useEffect(
            control,
            (Func<Action?>)(() =>
            {
                control.Events.Add($"run:{name}");
                return () => control.Events.Add($"cleanup:{name}");
            }),
            []);
    }

    private sealed class HookComponent : AkburaControl
    {
        private static readonly StateInfo<int> s_triggerInfo =
            new("trigger", static _ => 0);
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];

        private ImmutableArray<State> _states;
        private State<int> _trigger = null!;
        private int _updateDepth;

        public HookComponent()
            : base(AkburaEngine.Empty)
        {
        }

        public Action<HookComponent>? RenderFrame { get; init; }

        public State<int> Trigger => _trigger;

        public int Dependency { get; set; }

        public bool IncludeSecondHook { get; set; }

        public bool ReverseHooks { get; set; }

        public bool ThrowDuringRender { get; set; }

        public int UpdateCount { get; private set; }

        public int MaxUpdateDepth { get; private set; }

        public int EveryRenderCount { get; set; }

        public int FirstRenderCount { get; set; }

        public int EffectRuns { get; set; }

        public int CustomHookStateCreations { get; set; }

        public int CustomHookApplications { get; set; }

        public List<string> Events { get; } = [];

        public void InitializeForTest()
        {
            base.OnInitialized();
        }

        public void RenderAgain()
        {
            InvalidState();
        }

        protected override Control Update()
        {
            _updateDepth++;
            MaxUpdateDepth = Math.Max(MaxUpdateDepth, _updateDepth);
            try
            {
                UpdateCount++;
                RenderFrame?.Invoke(this);
                return new Border();
            }
            finally
            {
                _updateDepth--;
            }
        }

        protected override Control FirstUpdate()
        {
            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters() => s_parameters;

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => s_commands;

        protected override ImmutableArray<InjectService> GetServices() => s_services;

        protected override ImmutableArray<State> GetStates()
        {
            if (_states.IsDefault)
            {
                _trigger = CreateState(s_triggerInfo);
                _states = [_trigger];
            }

            return _states;
        }
    }

    private sealed class AvaloniaPropertyHookComponent : AkburaControl
    {
        public static readonly StyledProperty<int> ObservedValueProperty =
            AvaloniaProperty.Register<AvaloniaPropertyHookComponent, int>(nameof(ObservedValue));

        private static readonly StateInfo<int> s_observedStateInfo =
            StateInfo<int>.FromState(
                "observedValue",
                static control => AvaloniaPropertyHooks.useAvaloniaProperty(
                    (AvaloniaPropertyHookComponent)control,
                    ObservedValueProperty));
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];

        private ImmutableArray<State> _states;
        private State<int> _observedState = null!;

        public AvaloniaPropertyHookComponent()
            : base(AkburaEngine.Empty)
        {
        }

        public int ObservedValue
        {
            get => GetValue(ObservedValueProperty);
            set => SetValue(ObservedValueProperty, value);
        }

        public State<int> ObservedState => _observedState;

        public int UpdateCount { get; private set; }

        public void InitializeForTest()
        {
            base.OnInitialized();
        }

        protected override Control Update()
        {
            UpdateCount++;
            return new Border();
        }

        protected override Control FirstUpdate()
        {
            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters() => s_parameters;

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => s_commands;

        protected override ImmutableArray<InjectService> GetServices() => s_services;

        protected override ImmutableArray<State> GetStates()
        {
            if (_states.IsDefault)
            {
                _observedState = CreateState(s_observedStateInfo);
                _states = [_observedState];
            }

            return _states;
        }
    }

    private sealed class CustomHooks
    {
        private static readonly UseHookKey s_counterKey = new();

        [UseHook]
        public static void useCounter([Self] AkburaControl control)
        {
            var component = (HookComponent)control;
            control.UseHook(
                s_counterKey,
                component,
                static current =>
                {
                    current.CustomHookStateCreations++;
                    return new CustomCounterState();
                },
                static (state, current) =>
                {
                    state.ApplicationCount++;
                    current.CustomHookApplications = state.ApplicationCount;
                });
        }
    }

    private sealed class CustomCounterState
    {
        public int ApplicationCount { get; set; }
    }

    private sealed class TestHookState
    {
        private readonly Action _detach;

        public TestHookState(Action detach)
        {
            _detach = detach;
        }

        public void Detach()
        {
            _detach();
        }
    }

    private sealed class ParityDependenciesComparer : IUseHookDependenciesComparer
    {
        public int CallCount { get; private set; }

        public bool Equals(
            ReadOnlySpan<object?> previousDependencies,
            ReadOnlySpan<object?> currentDependencies)
        {
            CallCount++;
            return (int)previousDependencies[0]! % 2 ==
                (int)currentDependencies[0]! % 2;
        }
    }

    private sealed class CallbackDisposable : IDisposable
    {
        private Action? _callback;

        public CallbackDisposable(Action callback)
        {
            _callback = callback;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _callback, null)?.Invoke();
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        private readonly Action? _dispose;
        private int _isDisposed;

        public TrackingDisposable(Action? dispose = null)
        {
            _dispose = dispose;
        }

        public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _dispose?.Invoke();
            }
        }
    }

    private sealed class ExpectedFactoryException : Exception
    {
    }

    private sealed class ExpectedApplyException : Exception
    {
    }

    private sealed class FirstDetachException : Exception
    {
    }

    private sealed class SecondDetachException : Exception
    {
    }

    private sealed class ExpectedCancellationException : Exception
    {
    }

    private sealed class ExpectedCleanupException : Exception
    {
    }

    private sealed class ExpectedAsyncEffectException : Exception
    {
    }

    private sealed class ExpectedRenderException : Exception
    {
    }
}
