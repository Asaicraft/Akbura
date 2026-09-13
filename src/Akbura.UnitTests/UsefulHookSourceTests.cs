using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class UsefulHookSourceTests
{
    [Fact]
    public Task Async_StartsAfterCommitAndRecognizesSuccessfulDefaultValues() =>
        OnDispatcher(async () =>
        {
            var component = new SourceComponent();
            var load = new TaskCompletionSource<int>();
            State<AsyncSnapshot<int>>? result = null;
            AsyncSnapshot<int> firstRender = default;
            var rendered = false;
            var loaderCalls = 0;
            component.RenderFrame = control =>
            {
                result = control.useAsync(_ =>
                {
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    loaderCalls++;
                    return load.Task;
                }, []);
                if (!rendered)
                {
                    rendered = true;
                    firstRender = result.Value;
                    Assert.Equal(0, loaderCalls);
                }
            };

            component.InitializeForTest();
            var original = result!;
            Assert.False(firstRender.IsLoading);
            Assert.False(firstRender.HasValue);
            Assert.Null(firstRender.Error);
            Assert.True(result!.Value.IsLoading);
            Assert.Equal(1, loaderCalls);
            var published = Observe(result, value => value.HasValue && !value.IsLoading);
            load.SetResult(0);
            await published;

            Assert.Same(original, result);
            Assert.Equal(0, result.Value.Value);
            Assert.Null(result.Value.Error);
            component.RenderAgain();
            Assert.Equal(1, loaderCalls);
        });

    [Fact]
    public Task Async_SuccessfulNullStillHasAValue() =>
        OnDispatcher(async () =>
        {
            var component = new SourceComponent();
            State<AsyncSnapshot<string?>>? result = null;
            component.RenderFrame = control => result = control.useAsync<string?>(
                static _ => Task.FromResult<string?>(null), []);
            component.InitializeForTest();
            await Observe(result!, value => value.HasValue);

            Assert.Null(result!.Value.Value);
            Assert.False(result.Value.IsLoading);
            Assert.Null(result.Value.Error);
        });

    [Fact]
    public Task Async_CanceledQueuedResultCannotOverwriteTheNextRun() =>
        OnDispatcher(async () =>
        {
            var component = new SourceComponent();
            State<AsyncSnapshot<int>>? result = null;
            var dependency = 1;
            var tokens = new List<CancellationToken>();
            component.RenderFrame = control =>
            {
                var value = dependency;
                result = control.useAsync(token =>
                {
                    tokens.Add(token);
                    return Task.FromResult(value);
                }, [dependency]);
            };
            component.InitializeForTest();
            var publishedValues = new List<int>();
            using var subscription = result!.Subscribe(value =>
            {
                if (value.HasValue)
                {
                    publishedValues.Add(value.Value);
                }
            });

            dependency = 2;
            component.RenderAgain();
            await Observe(result, value => value.HasValue && value.Value == 2);

            Assert.True(tokens[0].IsCancellationRequested);
            Assert.False(tokens[1].IsCancellationRequested);
            Assert.DoesNotContain(1, publishedValues);
            Assert.Equal(2, result.Value.Value);
        });

    [Fact]
    public Task Async_LateResultFromARequestIgnoringCancellationIsDiscarded() =>
        OnDispatcher(async () =>
        {
            var component = new SourceComponent();
            var first = new TaskCompletionSource<int>();
            var second = new TaskCompletionSource<int>();
            var dependency = 1;
            var tokens = new List<CancellationToken>();
            State<AsyncSnapshot<int>>? result = null;
            component.RenderFrame = control =>
            {
                var completion = dependency == 1 ? first : second;
                result = control.useAsync(token =>
                {
                    tokens.Add(token);
                    return completion.Task;
                }, [dependency]);
            };
            component.InitializeForTest();
            dependency = 2;
            component.RenderAgain();
            second.SetResult(20);
            await Observe(result!, value => value.HasValue && value.Value == 20);

            first.SetResult(10);
            await DrainDispatcher();
            Assert.True(tokens[0].IsCancellationRequested);
            Assert.Equal(20, result!.Value.Value);
            Assert.Null(result.Value.Error);
        });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Async_RestartCanKeepOrClearThePreviousValue(bool keepPreviousValue) =>
        OnDispatcher(async () =>
        {
            var component = new SourceComponent();
            var dependency = 1;
            var second = new TaskCompletionSource<int>();
            State<AsyncSnapshot<int>>? result = null;
            component.RenderFrame = control => result = control.useAsync(
                _ => dependency == 1 ? Task.FromResult(7) : second.Task,
                [dependency], keepPreviousValue);
            component.InitializeForTest();
            await Observe(result!, value => value.HasValue);

            dependency = 2;
            component.RenderAgain();
            Assert.True(result!.Value.IsLoading);
            Assert.Equal(keepPreviousValue, result.Value.HasValue);
            Assert.Equal(keepPreviousValue ? 7 : 0, result.Value.Value);
            second.SetResult(9);
            await Observe(result, value => value.HasValue && value.Value == 9);
        });

    [Theory]
    [InlineData("throw")]
    [InlineData("null")]
    [InlineData("cancellation")]
    public Task Async_LoaderFailuresBecomeSnapshotErrors(string failure) =>
        OnDispatcher(async () =>
        {
            var component = new SourceComponent();
            var expected = new InvalidOperationException("loader failed");
            var cancellation = new OperationCanceledException();
            State<AsyncSnapshot<int>>? result = null;
            component.RenderFrame = control => result = control.useAsync<int>(
                _ => failure switch
                {
                    "throw" => throw expected,
                    "null" => null!,
                    _ => Task.FromException<int>(cancellation)
                }, []);
            component.InitializeForTest();
            await Observe(result!, value => value.Error != null);

            Assert.False(result!.Value.IsLoading);
            Assert.False(result.Value.HasValue);
            if (failure == "null")
            {
                Assert.IsType<InvalidOperationException>(result.Value.Error);
            }
            else
            {
                Assert.Same(failure == "throw" ? expected : cancellation, result.Value.Error);
            }
        });

    [Fact]
    public Task Async_DetachPreservesSnapshotAndReattachLoadsAgain() =>
        OnDispatcher(async () =>
        {
            var component = new SourceComponent();
            var runs = new List<(CancellationToken Token, TaskCompletionSource<int> Completion)>();
            State<AsyncSnapshot<int>>? result = null;
            component.RenderFrame = control => result = control.useAsync(token =>
            {
                var completion = new TaskCompletionSource<int>();
                runs.Add((token, completion));
                return completion.Task;
            }, []);
            var window = new Window { Content = component };
            window.Show();
            runs[0].Completion.SetResult(4);
            await Observe(result!, value => value.HasValue && value.Value == 4);
            var original = result;

            window.Content = null;
            Assert.True(runs[0].Token.IsCancellationRequested);
            Assert.Equal(4, result!.Value.Value);
            window.Content = component;
            Assert.Equal(2, runs.Count);
            Assert.Same(original, result);
            Assert.True(result.Value.IsLoading);
            Assert.Equal(4, result.Value.Value);
            runs[1].Completion.SetResult(8);
            await Observe(result, value => value.HasValue && value.Value == 8);
            window.Close();
        });

    [Fact]
    public Task Observable_StreamSwitchRetainsValueAndRejectsQueuedOldNotifications() =>
        OnDispatcher(async () =>
        {
            var first = new TestObservable<int>();
            var second = new TestObservable<int>();
            var stream = first;
            var component = new SourceComponent();
            State<int>? result = null;
            component.RenderFrame = control => result = control.useObservable(stream, -1);
            component.InitializeForTest();
            Assert.Equal(1, first.SubscribeCount);
            first.Next(3);
            Assert.Equal(3, result!.Value);

            RunOnWorker(() => first.Next(99));
            stream = second;
            component.RenderAgain();
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.SubscribeCount);
            Assert.Equal(3, result.Value);
            await DrainDispatcher();
            Assert.Equal(3, result.Value);
            RunOnWorker(() => second.Next(7));
            await DrainDispatcher();
            Assert.Equal(7, result.Value);
            second.Complete();
            Assert.Equal(7, result.Value);
        });

    [Fact]
    public Task Observable_DifferentStreamsWithEqualValuesStillRestartTheSubscription() =>
        OnDispatcher(() =>
        {
            var first = new TestObservable<int>();
            var second = new TestObservable<int>();
            Assert.Equal(first, second);
            var stream = first;
            var component = new SourceComponent();
            component.RenderFrame = control => control.useObservable(stream, 0);
            component.InitializeForTest();

            stream = second;
            component.RenderAgain();
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.SubscribeCount);
            return Task.CompletedTask;
        });

    [Fact]
    public Task Observable_ErrorHandlerUsesCommittedCallbackWithoutResubscribing() =>
        OnDispatcher(async () =>
        {
            var stream = new TestObservable<int>();
            var component = new SourceComponent();
            var version = 1;
            var received = new List<int>();
            component.RenderFrame = control =>
            {
                var frameVersion = version;
                control.useObservable(stream, 0, _ => received.Add(frameVersion));
            };
            component.InitializeForTest();
            version = 2;
            component.RenderAgain();
            version = 3;
            component.FailRender = true;
            Assert.Throws<ExpectedRenderException>(component.RenderAgain);
            component.FailRender = false;
            RunOnWorker(() => stream.Error(new InvalidOperationException()));
            await DrainDispatcher();

            Assert.Equal([2], received);
            Assert.Equal(1, stream.SubscribeCount);
            Assert.Equal(0, stream.DisposeCount);
        });

    [Fact]
    public Task Observable_UnhandledErrorIsRaisedOnTheUiThread() =>
        OnDispatcher(() =>
        {
            var stream = new TestObservable<int>();
            var component = new SourceComponent();
            component.RenderFrame = control => control.useObservable(stream, 0);
            component.InitializeForTest();
            var expected = new InvalidOperationException("observable failed");

            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => stream.Error(expected)));
            return Task.CompletedTask;
        });

    [Fact]
    public Task ExternalStore_SubscribesBeforeRereadingAndRetainsItsLaunchingGetter() =>
        OnDispatcher(async () =>
        {
            var store = new TestStore { Value = 1 };
            var component = new SourceComponent();
            var multiplier = 10;
            var restarts = 0;
            State<int>? result = null;
            component.RenderFrame = control =>
            {
                var frameMultiplier = multiplier;
                result = control.useExternalStore(
                    changed =>
                    {
                        store.Value = 2;
                        return store.Subscribe(changed);
                    },
                    () => store.Value * frameMultiplier,
                    [restarts]);
            };
            component.InitializeForTest();
            Assert.Equal(20, result!.Value);
            Assert.Equal(1, store.SubscribeCount);

            multiplier = 100;
            component.RenderAgain();
            store.Value = 3;
            RunOnWorker(store.Notify);
            await DrainDispatcher();
            Assert.Equal(30, result.Value);
            Assert.Equal(1, store.SubscribeCount);

            restarts++;
            component.RenderAgain();
            Assert.Equal(200, result.Value);
            Assert.Equal(2, store.SubscribeCount);
            Assert.Equal(1, store.DisposeCount);
        });

    [Fact]
    public Task ExternalStore_FailedPostSubscribeReadDisposesTheSubscription() =>
        OnDispatcher(() =>
        {
            var store = new TestStore();
            var component = new SourceComponent();
            var reads = 0;
            var expected = new InvalidOperationException("getter failed");
            component.RenderFrame = control => control.useExternalStore(
                store.Subscribe,
                () => ++reads == 1 ? 1 : throw expected,
                []);

            Assert.Same(expected, Assert.Throws<InvalidOperationException>(component.InitializeForTest));
            Assert.Equal(1, store.SubscribeCount);
            Assert.Equal(1, store.DisposeCount);
            return Task.CompletedTask;
        });

    [Fact]
    public Task ExternalStore_CanceledQueuedNotificationDoesNotReadTheOldStore() =>
        OnDispatcher(async () =>
        {
            var first = new TestStore { Value = 1 };
            var second = new TestStore { Value = 8 };
            var store = first;
            var component = new SourceComponent();
            var oldReads = 0;
            State<int>? result = null;
            component.RenderFrame = control =>
            {
                var frameStore = store;
                result = control.useExternalStore(frameStore.Subscribe, () =>
                {
                    if (ReferenceEquals(frameStore, first))
                    {
                        oldReads++;
                    }

                    return frameStore.Value;
                }, [frameStore]);
            };
            component.InitializeForTest();
            Assert.Equal(2, oldReads);
            RunOnWorker(first.Notify);
            store = second;
            component.RenderAgain();
            await DrainDispatcher();

            Assert.Equal(2, oldReads);
            Assert.Equal(8, result!.Value);
            Assert.Equal(1, first.DisposeCount);
            Assert.Equal(1, second.SubscribeCount);
        });

    [Fact]
    public Task EventListener_UpdatesOnlyCommittedCallbacksAndRemovesTheExactDelegate() =>
        OnDispatcher(async () =>
        {
            var publisher = new TestPublisher();
            var component = new SourceComponent();
            var version = 1;
            var received = new List<(int Version, object? Sender, EventArgs Args)>();
            var dependency = 0;
            component.RenderFrame = control =>
            {
                var frameVersion = version;
                control.useEventListener<EventArgs>(
                    publisher.Add,
                    publisher.Remove,
                    (sender, args) => received.Add((frameVersion, sender, args)),
                    [dependency]);
            };
            component.InitializeForTest();
            var firstHandler = publisher.Handler;
            version = 2;
            component.RenderAgain();
            Assert.Same(firstHandler, publisher.Handler);
            version = 3;
            component.FailRender = true;
            Assert.Throws<ExpectedRenderException>(component.RenderAgain);
            component.FailRender = false;
            var args = new EventArgs();
            RunOnWorker(() => publisher.Raise(args));
            await DrainDispatcher();
            Assert.Equal(2, Assert.Single(received).Version);
            Assert.Same(publisher, received[0].Sender);
            Assert.Same(args, received[0].Args);

            RunOnWorker(() => publisher.Raise(args));
            dependency++;
            component.RenderAgain();
            Assert.Same(firstHandler, Assert.Single(publisher.Removed));
            Assert.NotSame(firstHandler, publisher.Handler);
            await DrainDispatcher();
            Assert.Single(received);
            publisher.Raise(args);
            Assert.Equal(3, received[1].Version);
        });

    [Fact]
    public Task ExternalSources_DetachStopsSubscriptionsAndReattachRestartsThem() =>
        OnDispatcher(async () =>
        {
            var stream = new TestObservable<int>();
            var store = new TestStore { Value = 2 };
            var publisher = new TestPublisher();
            var component = new SourceComponent();
            State<int>? observed = null;
            State<int>? snapshot = null;
            var events = 0;
            component.RenderFrame = control =>
            {
                observed = control.useObservable(stream, 0);
                snapshot = control.useExternalStore(store.Subscribe, () => store.Value, [store]);
                control.useEventListener<EventArgs>(publisher.Add, publisher.Remove,
                    (_, _) => events++, [publisher]);
            };
            var window = new Window { Content = component };
            window.Show();
            stream.Next(3);
            var originalObserved = observed;
            var originalSnapshot = snapshot;
            RunOnWorker(() =>
            {
                stream.Next(99);
                store.Notify();
                publisher.Raise(EventArgs.Empty);
            });

            window.Content = null;
            store.Value = 7;
            await DrainDispatcher();
            Assert.Equal(3, observed!.Value);
            Assert.Equal(2, snapshot!.Value);
            Assert.Equal(0, events);
            Assert.Equal(1, stream.DisposeCount);
            Assert.Equal(1, store.DisposeCount);
            Assert.Null(publisher.Handler);

            window.Content = component;
            Assert.Same(originalObserved, observed);
            Assert.Same(originalSnapshot, snapshot);
            Assert.Equal(3, observed.Value);
            Assert.Equal(7, snapshot.Value);
            Assert.Equal(2, stream.SubscribeCount);
            Assert.Equal(2, store.SubscribeCount);
            publisher.Raise(EventArgs.Empty);
            Assert.Equal(1, events);
            window.Close();
            Assert.Equal(2, stream.DisposeCount);
            Assert.Equal(2, store.DisposeCount);
            Assert.Equal(2, publisher.Removed.Count);
        });

    private static async Task OnDispatcher(Func<Task> test)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(async () =>
        {
            await test();
            return true;
        }, CancellationToken.None);
    }

    private static async Task DrainDispatcher() =>
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

    private static async Task Observe<T>(State<T> state, Func<T, bool> predicate)
    {
        var completion = new TaskCompletionSource();
        using var subscription = state.Subscribe(value =>
        {
            if (predicate(value))
            {
                completion.TrySetResult();
            }
        });
        if (predicate(state.Value))
        {
            completion.TrySetResult();
        }

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void RunOnWorker(Action callback)
    {
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        worker.Start();
        worker.Join();
        Assert.Null(failure);
    }

    private sealed class SourceComponent() : AkburaControl(AkburaEngine.Empty)
    {
        private readonly Border _root = new();

        public Action<SourceComponent>? RenderFrame { get; set; }

        public bool FailRender { get; set; }

        public void InitializeForTest() => base.OnInitialized();

        public void RenderAgain() => InvalidState();

        protected override Control FirstUpdate() => _root;

        protected override Control Update()
        {
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

        protected override ImmutableArray<State> GetStates() => [];
    }

    private sealed class ExpectedRenderException : Exception;

    private sealed class TestObservable<T> : IObservable<T>
    {
        private IObserver<T>? _observer;

        public int SubscribeCount { get; private set; }

        public int DisposeCount { get; private set; }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            SubscribeCount++;
            _observer = observer;
            return new TestSubscription(() => DisposeCount++);
        }

        public void Next(T value) => _observer!.OnNext(value);

        public void Error(Exception exception) => _observer!.OnError(exception);

        public void Complete() => _observer!.OnCompleted();

        public override bool Equals(object? obj) => obj is TestObservable<T>;

        public override int GetHashCode() => 0;
    }

    private sealed class TestStore
    {
        private Action? _changed;

        public int Value { get; set; }

        public int SubscribeCount { get; private set; }

        public int DisposeCount { get; private set; }

        public IDisposable Subscribe(Action changed)
        {
            SubscribeCount++;
            _changed = changed;
            return new TestSubscription(() => DisposeCount++);
        }

        public void Notify() => _changed!();
    }

    private sealed class TestPublisher
    {
        public EventHandler<EventArgs>? Handler { get; private set; }

        public List<EventHandler<EventArgs>> Removed { get; } = [];

        public void Add(EventHandler<EventArgs> handler) => Handler = handler;

        public void Remove(EventHandler<EventArgs> handler)
        {
            Assert.Same(Handler, handler);
            Removed.Add(handler);
            Handler = null;
        }

        public void Raise(EventArgs args) => Handler!(this, args);
    }

    private sealed class TestSubscription(Action dispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                dispose();
            }
        }
    }
}
