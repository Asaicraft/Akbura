using Akbura.Commands;
using System.Windows.Input;

namespace Akbura.UnitTests;

public sealed class AkburaICommandAdapterFactoryTests
{
    [Fact]
    public void Execute_CapturesHandlerBeforeBusyNotificationCanRefreshIt()
    {
        var firstCalls = 0;
        var secondCalls = 0;
        ICommand command = AkburaICommandAdapterFactory.CreateOrUpdateAction<object>(
            "capture",
            current: null,
            () => firstCalls++);
        command.CanExecuteChanged += (_, _) =>
        {
            if (!command.CanExecute(parameter: null))
            {
                command = AkburaICommandAdapterFactory.CreateOrUpdateAction<object>(
                    "capture",
                    command,
                    () => secondCalls++);
            }
        };

        command.Execute(parameter: null);

        Assert.Equal(1, firstCalls);
        Assert.Equal(0, secondCalls);
        command.Execute(parameter: null);
        Assert.Equal(1, firstCalls);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public void OwnerStorage_PreservesAdapterForWriteOnlyDestination()
    {
        var owner = new object();
        var first = AkburaICommandAdapterFactory.CreateOrUpdateAction<object>(
            "write-only",
            owner,
            () => { });
        var second = AkburaICommandAdapterFactory.CreateOrUpdateAction<object>(
            "write-only",
            owner,
            () => { });

        Assert.Same(first, second);
    }

    [Fact]
    public async Task CreateOrUpdate_PreservesBusyAdapterAndRefreshesOnlyFutureExecutions()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var becameAvailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCalls = 0;
        var secondCalls = 0;
        ICommand command = AkburaICommandAdapterFactory.CreateOrUpdateTaskAction<object>(
            "slot",
            current: null,
            async () =>
            {
                firstCalls++;
                started.SetResult();
                await release.Task;
            });
        command.CanExecuteChanged += (_, _) =>
        {
            if (command.CanExecute(parameter: null))
            {
                becameAvailable.TrySetResult();
            }
        };

        command.Execute(parameter: null);
        await started.Task;
        Assert.False(command.CanExecute(parameter: null));

        var updated = AkburaICommandAdapterFactory.CreateOrUpdateTaskAction<object>(
            "slot",
            command,
            () =>
            {
                secondCalls++;
                secondCalled.TrySetResult();
                return Task.CompletedTask;
            });
        Assert.Same(command, updated);
        Assert.False(updated.CanExecute(parameter: null));
        Assert.Equal(1, firstCalls);
        Assert.Equal(0, secondCalls);

        release.SetResult();
        await becameAvailable.Task;
        Assert.True(updated.CanExecute(parameter: null));

        updated.Execute(parameter: null);
        await secondCalled.Task;
        Assert.Equal(1, firstCalls);
        Assert.Equal(1, secondCalls);
    }

    [Fact]
    public async Task ValueTaskHandler_PreservesBusyStateUntilCompletion()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var becameAvailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = AkburaICommandAdapterFactory.CreateOrUpdateAsyncAction<object>(
            "value-task",
            current: null,
            ExecuteAsync);
        command.CanExecuteChanged += (_, _) =>
        {
            if (command.CanExecute(parameter: null))
            {
                becameAvailable.TrySetResult();
            }
        };

        command.Execute(parameter: null);
        await started.Task;
        Assert.False(command.CanExecute(parameter: null));

        release.SetResult();
        await becameAvailable.Task;
        Assert.True(command.CanExecute(parameter: null));

        async ValueTask ExecuteAsync()
        {
            started.SetResult();
            await release.Task;
        }
    }

    [Fact]
    public void FaultedHandler_RestoresAvailabilityAndPostsTheException()
    {
        var failure = new InvalidOperationException("Expected command failure.");
        var context = new CapturingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var command = AkburaICommandAdapterFactory.CreateOrUpdateTaskAction<object>(
                "failure",
                current: null,
                () => Task.FromException(failure));

            command.Execute(parameter: null);

            Assert.True(command.CanExecute(parameter: null));
            Assert.Same(failure, context.RunPostedCallback());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void TypedAdapter_ValidatesParameterBeforeInvokingHandler()
    {
        var calls = 0;
        var command = AkburaICommandAdapterFactory.CreateOrUpdateAction<string, object>(
            "typed",
            current: null,
            value => calls += value?.Length ?? 1);

        Assert.True(command.CanExecute("value"));
        Assert.True(command.CanExecute(parameter: null));
        Assert.False(command.CanExecute(42));
        Assert.Throws<ArgumentException>(() => command.Execute(42));
        Assert.Equal(0, calls);

        command.Execute("value");
        Assert.Equal(5, calls);
    }

    [Fact]
    public void ValueTypeAdapter_RejectsNull()
    {
        var calls = 0;
        var command = AkburaICommandAdapterFactory.CreateOrUpdateAction<int, object>(
            "value-type",
            current: null,
            value => calls += value);

        Assert.False(command.CanExecute(parameter: null));
        Assert.Throws<ArgumentException>(() => command.Execute(parameter: null));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ObjectArrayParameter_IsPassedAsOneValue()
    {
        object[]? received = null;
        var command = AkburaICommandAdapterFactory.CreateOrUpdateAction<object[], object>(
            "array",
            current: null,
            value => received = value);
        object[] parameter = [1, "two"];

        command.Execute(parameter);

        Assert.Same(parameter, received);
    }

    private sealed class CapturingSynchronizationContext : SynchronizationContext
    {
        private SendOrPostCallback? _callback;
        private object? _state;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Assert.Null(_callback);
            _callback = callback;
            _state = state;
        }

        public Exception RunPostedCallback()
        {
            var callback = Assert.IsType<SendOrPostCallback>(_callback);
            return Assert.ThrowsAny<Exception>(() => callback(_state));
        }
    }
}
