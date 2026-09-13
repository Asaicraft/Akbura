using Akbura.Commands;
using Akbura.HotReload;
using System.Windows.Input;

namespace Akbura.UnitTests;

public sealed class AkburaCommandFactoryTests
{
    [Fact]
    public async Task ParameterlessAndTypedHandlers_PreserveVoidAndResultContracts()
    {
        var calls = 0;
        var action = AkburaCommandFactory.CreateAction<object>(() => calls++);
        Assert.Null(await action.Execute());
        Assert.Equal(1, calls);

        var function = AkburaCommandFactory.CreateFunction<int, int, long>((left, right) => left + right);
        Assert.Equal(7L, await function.Execute(3, 4));
        Assert.Equal(11L, await ((IAkburaCommand)function).Execute(5, 6));
        Assert.IsType<long>(await ((IAkburaCommand)function).Execute(1, 2));
        var ignored = AkburaCommandFactory.CreateAction<int, int>(_ => calls++);
        Assert.Equal(0, await ignored.Execute(123));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task MaximumTypedArity_UsesAllSixteenArgumentsThroughBothInterfaces()
    {
        var command = AkburaCommandFactory.CreateFunction<
            int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int, int>(
            (a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p) =>
                a + b + c + d + e + f + g + h + i + j + k + l + m + n + o + p);

        Assert.Equal(136, await command.Execute(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16));
        Assert.Equal(136, await ((IAkburaCommand)command).Execute(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16));
    }

    [Fact]
    public async Task AsyncExecution_ReportsBusyUntilAllOutstandingExecutionsFinish()
    {
        var firstResult = new TaskCompletionSource<int>();
        var secondResult = new TaskCompletionSource<int>();
        var command = AkburaCommandFactory.CreateAsyncFunction<int, int>(async value =>
            await (value == 1 ? firstResult.Task : secondResult.Task));
        var executing = new Observer();
        var available = new Observer();
        using var executingSubscription = command.IsExecuting.Subscribe(executing);
        using var availableSubscription = command.CanExecute.Subscribe(available);
        var changed = 0;
        ((ICommand)command).CanExecuteChanged += (_, _) => changed++;

        var first = command.Execute(1);
        var second = command.Execute(2);
        Assert.False(((ICommand)command).CanExecute(null));
        firstResult.SetResult(11);
        Assert.Equal(11, await first);
        Assert.False(((ICommand)command).CanExecute(null));
        secondResult.SetResult(22);
        Assert.Equal(22, await second);

        Assert.True(((ICommand)command).CanExecute(null));
        Assert.Equal([false, true, false], executing.Values);
        Assert.Equal([true, false, true], available.Values);
        Assert.Equal(2, changed);
    }

    [Fact]
    public async Task ThrowingHandler_RestoresExecutingStateAndCanRunAgain()
    {
        var fail = true;
        var command = AkburaCommandFactory.CreateAsyncAction<int, object>(async value =>
        {
            await Task.Yield();
            if (fail)
            {
                throw new InvalidOperationException("handler failure");
            }
        });
        var executing = new Observer();
        using var subscription = command.IsExecuting.Subscribe(executing);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () => await command.Execute(1));
        Assert.Equal("handler failure", failure.Message);
        Assert.True(((ICommand)command).CanExecute(null));
        fail = false;
        Assert.Null(await command.Execute(2));
        Assert.Equal([false, true, false, true, false], executing.Values);
    }

    [Fact]
    public void OwnedCommandRefresh_RollsBackPendingReplacementAndRestoresForeignBaselineOnDispose()
    {
        var foreign = AkburaCommandFactory.CreateAction<object>(() => { });
        var host = new CommandHost { Notify = foreign };
        var state = new AkburaRenderState();
        void Begin(string revision) => state.BeginRevision(revision,
            builder => builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(CommandHost), null, "host")),
            _ => host);
        void Set(IAkburaCommand command) => state.ReconcileConditionalClrValue(0, "Notify", host,
            typeof(CommandHost), nameof(CommandHost.Notify), "handler", command);
        Begin("initial");
        var initial = AkburaCommandFactory.CreateAction<object>(() => { });
        Set(initial);
        state.CompleteRevision();
        var current = AkburaCommandFactory.CreateAction<object>(() => { });
        Set(current);
        Assert.False(state.HasPendingRevision);
        Assert.Same(current, host.Notify);

        Begin("changed-source");
        var abandoned = AkburaCommandFactory.CreateAction<object>(() => { });
        Set(abandoned);
        Assert.Same(abandoned, host.Notify);
        state.PrepareRevisionCompletion();
        state.AbortRevision();

        Assert.Same(current, host.Notify);
        state.Dispose();
        Assert.Same(foreign, host.Notify);
    }

    private sealed class Observer : IObserver<bool>
    {
        public List<bool> Values { get; } = [];

        public void OnNext(bool value) => Values.Add(value);

        public void OnError(Exception error) => throw error;

        public void OnCompleted()
        {
        }
    }

    private sealed class CommandHost
    {
        public IAkburaCommand? Notify { get; set; }
    }
}
