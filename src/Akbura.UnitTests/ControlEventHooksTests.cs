using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ControlEventHooksTests
{
    [Fact]
    public Task NonGenericEvent_UsesLatestCommittedCallbackWithoutDuplicateSubscriptions() =>
        OnDispatcher(() =>
        {
            var component = new EventHookComponent();
            var version = 1;
            var received = new List<int>();
            component.RenderFrame = control =>
            {
                var frameVersion = version;
                control.useDataContextChanged(() => received.Add(frameVersion));
            };
            component.InitializeForTest();
            received.Clear();

            for (var index = 0; index < 50; index++)
            {
                version++;
                component.RenderAgain();
            }

            component.DataContext = new object();
            Assert.Equal([51], received);

            version = 52;
            component.ApplyHotReload(static _ => { });
            component.DataContext = new object();
            Assert.Equal([51, 52], received);

            version = 53;
            component.FailRender = true;
            Assert.Throws<ExpectedRenderException>(component.RenderAgain);
            component.FailRender = false;
            component.DataContext = new object();
            Assert.Equal([51, 52, 52], received);
        });

    [Fact]
    public Task NonGenericEvent_FullHandlerUnsubscribesOnDetachAndRestoresOnReattach() =>
        OnDispatcher(() =>
        {
            var calls = 0;
            object? sender = null;
            EventArgs? receivedArgs = null;
            var component = new EventHookComponent();
            component.RenderFrame = control =>
                control.useDataContextChanged((currentSender, args) =>
                {
                    calls++;
                    sender = currentSender;
                    receivedArgs = args;
                });
            component.InitializeForTest();
            var window = new Window { Content = component };

            try
            {
                window.Show();
                calls = 0;
                component.DataContext = new object();

                Assert.Equal(1, calls);
                Assert.Same(component, sender);
                Assert.Same(EventArgs.Empty, receivedArgs);

                window.Content = null;
                component.DataContext = new object();
                Assert.Equal(1, calls);

                window.Content = component;
                Dispatcher.UIThread.RunJobs();
                component.DataContext = new object();
                Assert.Equal(2, calls);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task RoutedEvent_PreservesArgsAndHandledEventsTooSemanticsSynchronously() =>
        OnDispatcher(() =>
        {
            var component = new EventHookComponent(markContextCanceledHandled: true);
            var defaultCalls = 0;
            var handledCalls = 0;
            object? sender = null;
            RoutedEventArgs? receivedArgs = null;
            component.RenderFrame = control =>
            {
                control.useContextCanceled(_ => defaultCalls++);
                control.useContextCanceled(
                    (currentSender, args) =>
                    {
                        handledCalls++;
                        sender = currentSender;
                        receivedArgs = args;
                        args.Handled = true;
                    },
                    handledEventsToo: true);
            };
            component.InitializeForTest();

            var args = new RoutedEventArgs(InputElement.ContextCanceledEvent);
            component.RaiseEvent(args);

            Assert.Equal(0, defaultCalls);
            Assert.Equal(1, handledCalls);
            Assert.Same(component, sender);
            Assert.Same(args, receivedArgs);
            Assert.True(args.Handled);
        });

    [Fact]
    public Task RequestBringIntoView_SupportsAllCallbackFormsAndPreservesRoutedSource() =>
        OnDispatcher(() =>
        {
            var noArgsCalls = 0;
            var argsCalls = 0;
            var fullCalls = 0;
            object? sender = null;
            RequestBringIntoViewEventArgs? receivedArgs = null;
            var routes = RoutingStrategies.Direct | RoutingStrategies.Bubble;
            var component = new EventHookComponent();
            component.RenderFrame = control =>
            {
                control.useRequestBringIntoView(() => noArgsCalls++, routes);
                control.useRequestBringIntoView(_ => argsCalls++, routes);
                control.useRequestBringIntoView(
                    (currentSender, args) =>
                    {
                        fullCalls++;
                        sender = currentSender;
                        receivedArgs = args;
                    },
                    routes);
            };
            var window = new Window { Content = component };

            try
            {
                window.Show();
                var firstArgs = new RequestBringIntoViewEventArgs
                {
                    RoutedEvent = Control.RequestBringIntoViewEvent,
                };
                component.Root.RaiseEvent(firstArgs);

                Assert.Equal(1, noArgsCalls);
                Assert.Equal(1, argsCalls);
                Assert.Equal(1, fullCalls);
                Assert.Same(component, sender);
                Assert.Same(component.Root, firstArgs.Source);
                Assert.Same(firstArgs, receivedArgs);

                routes = RoutingStrategies.Bubble;
                component.RenderAgain();
                var secondArgs = new RequestBringIntoViewEventArgs
                {
                    RoutedEvent = Control.RequestBringIntoViewEvent,
                };
                component.Root.RaiseEvent(secondArgs);

                Assert.Equal(2, noArgsCalls);
                Assert.Equal(2, argsCalls);
                Assert.Equal(2, fullCalls);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task LogicalAndVisualAttachmentHooksRemainDistinct() =>
        OnDispatcher(() =>
        {
            var logicalCalls = 0;
            var visualCalls = 0;
            Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs? logicalArgs = null;
            VisualTreeAttachmentEventArgs? visualArgs = null;
            var component = new EventHookComponent();
            component.RenderFrame = control =>
            {
                control.useAttachedToLogicalTree(args =>
                {
                    logicalCalls++;
                    logicalArgs = args;
                });
                control.useAttachedToVisualTree(args =>
                {
                    visualCalls++;
                    visualArgs = args;
                });
            };
            component.InitializeForTest();
            var window = new Window { Content = component };

            try
            {
                window.Show();

                Assert.Equal(1, logicalCalls);
                Assert.Equal(1, visualCalls);
                Assert.NotNull(logicalArgs);
                Assert.NotNull(visualArgs);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task DetachAndUnload_AreDeliveredOnceAfterCleanupAndRestartAfterReattach() =>
        OnDispatcher(() =>
        {
            var trace = new List<string>();
            var component = new EventHookComponent();
            VisualTreeAttachmentEventArgs? hookDetachedArgs = null;
            VisualTreeAttachmentEventArgs? publicDetachedArgs = null;
            RoutedEventArgs? hookUnloadedArgs = null;
            RoutedEventArgs? publicUnloadedArgs = null;
            component.DetachedFromVisualTree += (_, args) => publicDetachedArgs = args;
            component.Unloaded += (_, args) => publicUnloadedArgs = args;
            component.RenderFrame = control =>
            {
                control.useEffect(() =>
                {
                    trace.Add("effect");
                    return () => trace.Add("cleanup");
                }, []);
                control.useDetachedFromVisualTree(args =>
                {
                    hookDetachedArgs = args;
                    trace.Add("detached");
                });
                control.useUnloaded(args =>
                {
                    hookUnloadedArgs = args;
                    trace.Add("unloaded");
                });
            };
            var window = new Window { Content = component };

            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                trace.Clear();
                window.Content = null;

                Assert.Equal(["cleanup", "detached", "unloaded"], trace);
                Assert.Same(publicDetachedArgs, hookDetachedArgs);
                Assert.Same(publicUnloadedArgs, hookUnloadedArgs);

                window.Content = component;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("effect", Assert.Single(trace.Skip(3)));
                trace.Clear();
                window.Content = null;

                Assert.Equal(["cleanup", "detached", "unloaded"], trace);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Initialized_IsDeliveredOnceAndHotReloadDoesNotSynthesizeLifecycleEvents() =>
        OnDispatcher(() =>
        {
            var initialized = 0;
            var detached = 0;
            var unloaded = 0;
            var component = new EventHookComponent();
            component.RenderFrame = control =>
            {
                control.useInitialized(() => initialized++);
                control.useDetachedFromVisualTree(() => detached++);
                control.useUnloaded(() => unloaded++);
            };
            var window = new Window { Content = component };

            try
            {
                window.Show();
                Assert.Equal(1, initialized);

                component.ApplyHotReload(static _ => { });

                Assert.Equal(1, initialized);
                Assert.Equal(0, detached);
                Assert.Equal(0, unloaded);
                component.RenderAgain();
                Assert.Equal(1, initialized);

                window.Content = null;
                window.Content = component;
                Assert.Equal(1, initialized);
            }
            finally
            {
                window.Close();
            }
        });

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public Task Initialized_LateRegistrationHonorsCatchUpOption(bool invokeIfAlreadyInitialized, int expectedCalls) =>
        OnDispatcher(() =>
        {
            var calls = 0;
            var component = new EventHookComponent();
            var window = new Window { Content = component };

            try
            {
                window.Show();
                component.RenderFrame = control =>
                    control.useInitialized(() => calls++, invokeIfAlreadyInitialized);
                component.ApplyHotReload(static _ => { });

                Assert.Equal(expectedCalls, calls);
                component.RenderAgain();
                Assert.Equal(expectedCalls, calls);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public Task Initialized_RemovedAndReintroducedHookGetsANewLogicalIdentity() =>
        OnDispatcher(() =>
        {
            var calls = 0;
            var includeHook = true;
            var component = new EventHookComponent();
            component.RenderFrame = control =>
            {
                if (includeHook)
                {
                    control.useInitialized(() => calls++);
                }
            };
            var window = new Window { Content = component };

            try
            {
                window.Show();
                Assert.Equal(1, calls);

                includeHook = false;
                component.ApplyHotReload(static _ => { });
                Assert.Equal(1, calls);

                includeHook = true;
                component.ApplyHotReload(static _ => { });
                Assert.Equal(2, calls);
            }
            finally
            {
                window.Close();
            }
        });

    private static async Task OnDispatcher(Action test)
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(
            () =>
            {
                test();
                return true;
            },
            CancellationToken.None);
    }

    private sealed class EventHookComponent : AkburaControl
    {
        private readonly Border _root = new();

        public EventHookComponent(bool markContextCanceledHandled = false)
            : base(AkburaEngine.Empty)
        {
            if (markContextCanceledHandled)
            {
                AddHandler(
                    InputElement.ContextCanceledEvent,
                    static (_, args) => args.Handled = true,
                    RoutingStrategies.Direct | RoutingStrategies.Bubble,
                    handledEventsToo: true);
            }
        }

        public Action<EventHookComponent>? RenderFrame { get; set; }

        public bool FailRender { get; set; }

        public Border Root => _root;

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
}
