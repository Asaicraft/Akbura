using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class StateCollectionTests
{
    [Fact]
    public void GetOrCreate_PreservesTheInstanceAndValueWithoutCallingTheFactoryAgain()
    {
        var states = new StateCollection();
        var calls = 0;
        var first = states.GetOrCreate("count", () =>
        {
            calls++;
            return new State<int>(1);
        });
        first.Value = 42;

        var second = states.GetOrCreate<int>("count", () => throw new Exception("Unexpected factory call."));

        Assert.Same(first, second);
        Assert.Equal(42, second.Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetOrCreate_UsesOrdinalKeysAndIndependentCollections()
    {
        var firstCollection = new StateCollection();
        var secondCollection = new StateCollection();
        var first = firstCollection.GetOrCreate("count", () => new State<int>(1));
        var otherKey = firstCollection.GetOrCreate("Count", () => new State<int>(2));
        var otherOwner = secondCollection.GetOrCreate("count", () => new State<int>(3));

        Assert.NotSame(first, otherKey);
        Assert.NotSame(first, otherOwner);
        Assert.Equal(2, otherKey.Value);
        Assert.Equal(3, otherOwner.Value);
    }

    [Fact]
    public void GetOrCreate_RejectsAnIncompatibleTypeWithoutReplacingTheState()
    {
        var states = new StateCollection();
        var count = states.GetOrCreate("count", () => new State<int>(1));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            states.GetOrCreate("count", () => new State<string>("new")));

        Assert.Contains("count", exception.Message);
        Assert.Contains(typeof(int).ToString(), exception.Message);
        Assert.Contains(typeof(string).ToString(), exception.Message);
        Assert.Same(count, states.GetOrCreate("count", () => new State<int>(2)));
    }

    [Fact]
    public void GetOrCreate_AllowsRetryAfterAThrowingOrNullFactory()
    {
        var states = new StateCollection();

        Assert.Throws<InvalidOperationException>(() =>
            states.GetOrCreate<int>("count", () => throw new InvalidOperationException("Creation failed.")));
        Assert.Throws<InvalidOperationException>(() => states.GetOrCreate<int>("count", () => null!));

        Assert.Equal(42, states.GetOrCreate("count", () => new State<int>(42)).Value);
    }

    [Fact]
    public void GetOrCreate_RejectsNullArguments()
    {
        var states = new StateCollection();

        Assert.Throws<ArgumentNullException>(() => states.GetOrCreate(null!, () => new State<int>(0)));
        Assert.Throws<ArgumentNullException>(() => states.GetOrCreate<int>("count", null!));
    }

    [Fact]
    public void AbortFrame_DiscardsNewAliasesAndSnapshotsButRetainsOrdinaryState()
    {
        var states = new StateCollection();
        states.BeginFrame();
        var count = states.GetOrCreate("count", () => new State<int>(1));
        count.Value = 42;
        var hook = new State<int>(7);
        states.StageHook("hook", hook);
        var provisional = states.GetSnapshot(() => [count, states.GetRequiredHook<int>("hook")]);
        Assert.Same(hook, provisional[1]);

        states.AbortFrame();
        states.AbortFrame();

        Assert.Null(states.GetBoundOrNull<int>("hook"));
        Assert.Throws<InvalidOperationException>(() => states.GetRequiredHook<int>("hook"));
        Assert.Same(count, states.GetOrCreate("count", () => new State<int>(0)));
        Assert.Equal(42, count.Value);
        states.BeginFrame();
        var replacement = new State<int>(8);
        states.StageHook("hook", replacement);
        var retried = states.GetSnapshot(() => [count, states.GetRequiredHook<int>("hook")]);
        states.CommitFrame();
        Assert.Same(replacement, retried[1]);
        Assert.True(provisional != retried);
    }

    [Fact]
    public void AbortFrame_RestoresCommittedAliasesAndTheExactSnapshot()
    {
        var states = new StateCollection();
        var original = new State<int>(1);
        states.BeginFrame();
        states.StageHook("hook", original);
        var committed = states.GetSnapshot(() => [states.GetRequiredHook<int>("hook")]);
        states.CommitFrame();

        states.BeginFrame();
        var replacement = new State<int>(2);
        states.StageHook("hook", replacement);
        Assert.Same(replacement, states.GetRequiredHook<int>("hook"));
        var provisional = states.GetSnapshot(() => [states.GetRequiredHook<int>("hook")]);
        Assert.True(committed != provisional);
        states.AbortFrame();

        Assert.Same(original, states.GetRequiredHook<int>("hook"));
        Assert.True(committed == states.GetSnapshot(() => throw new Exception("Snapshot should be cached.")));
    }

    [Fact]
    public void CommitFrame_PreservesSnapshotIdentityForUnchangedAliases()
    {
        var states = new StateCollection();
        var hook = new State<int>(1);
        states.BeginFrame();
        states.StageHook("hook", hook);
        var snapshot = states.GetSnapshot(() => [hook]);
        states.CommitFrame();

        states.BeginFrame();
        states.StageHook("hook", hook);
        states.CommitFrame();

        Assert.Same(hook, states.GetRequiredHook<int>("hook"));
        Assert.True(snapshot == states.GetSnapshot(() => throw new Exception("Snapshot should be cached.")));
        states.AbortFrame();
        Assert.Same(hook, states.GetRequiredHook<int>("hook"));
    }

    [Fact]
    public void CommitFrame_RemovesAliasesNotStagedInTheCurrentFrame()
    {
        var states = new StateCollection();
        var retained = new State<int>(1);
        states.BeginFrame();
        states.StageHook("retained", retained);
        states.StageHook("removed", new State<int>(2));
        var previous = states.GetSnapshot(() => [retained, states.GetRequiredHook<int>("removed")]);
        states.CommitFrame();

        states.BeginFrame();
        states.StageHook("retained", retained);
        states.CommitFrame();

        Assert.Null(states.GetBoundOrNull<int>("removed"));
        Assert.Same(retained, states.GetRequiredHook<int>("retained"));
        var current = states.GetSnapshot(() => [retained]);
        Assert.True(previous != current);
        Assert.Same(retained, Assert.Single(current));
    }

    [Fact]
    public void HotReload_ResetPreservesOrdinaryStateAndThePendingReconciliationAcrossFailedFrames()
    {
        var states = new StateCollection();
        var count = states.GetOrCreate("count", () => new State<int>(42));
        states.BeginFrame();
        states.StageHook("hook", new State<int>(1));
        var previous = states.GetSnapshot(() => [count, states.GetRequiredHook<int>("hook")]);
        states.CommitFrame();

        states.InvalidateSnapshotForHotReload();
        states.ResetHookBindingsForHotReload();
        Assert.True(states.HasPendingReconciliation);
        Assert.True(previous == states.PreviousSnapshotForHotReload);
        Assert.Null(states.GetBoundOrNull<int>("hook"));
        Assert.Same(count, states.GetOrCreate("count", () => new State<int>(0)));
        states.BeginFrame();
        states.StageHook("hook", new State<int>(2));
        _ = states.GetSnapshot(() => [count, states.GetRequiredHook<int>("hook")]);
        states.AbortFrame();

        states.ResetHookBindingsForHotReload();
        Assert.True(previous == states.PreviousSnapshotForHotReload);
        states.BeginFrame();
        var replacement = new State<int>(3);
        states.StageHook("hook", replacement);
        var current = states.GetSnapshot(() => [count, replacement]);
        states.CommitFrame();
        Assert.Same(count, current[0]);
        states.CompleteHotReloadReconciliation();
        Assert.False(states.HasPendingReconciliation);
        Assert.True(states.PreviousSnapshotForHotReload.IsDefault);
    }

    [Fact]
    public void Frames_RejectInvalidLifecycleAndHookTypes()
    {
        var states = new StateCollection();
        Assert.Throws<InvalidOperationException>(states.CommitFrame);
        Assert.Throws<InvalidOperationException>(() => states.StageHook("hook", new State<int>(0)));
        states.BeginFrame();
        Assert.Throws<InvalidOperationException>(states.BeginFrame);
        Assert.Throws<InvalidOperationException>(states.ResetHookBindingsForHotReload);
        states.StageHook("hook", new State<int>(1));
        Assert.Throws<InvalidOperationException>(() => states.GetBoundOrNull<string>("hook"));
        states.CommitFrame();
        Assert.Throws<InvalidOperationException>(() => states.GetBoundOrNull<string>("hook"));
        Assert.Throws<InvalidOperationException>(() => states.GetSnapshot(() => default));
        Assert.Throws<ArgumentNullException>(() => states.GetBoundOrNull<int>(null!));
        Assert.Throws<ArgumentNullException>(() => states.GetSnapshot(null!));
    }

    [Fact]
    public void HotReload_PrunesOrdinaryStatesCreatedInAbortedFrames()
    {
        var states = new StateCollection();
        var count = states.GetOrCreate("count", () => new State<int>(42));
        states.BeginFrame();
        _ = states.GetSnapshot(() => [count]);
        states.CommitFrame();
        states.InvalidateSnapshotForHotReload();
        states.BeginFrame();
        var removed = states.GetOrCreate("temporary", () => new State<string>("Removed after failure"));
        _ = states.GetSnapshot(() => [count, removed]);
        states.AbortFrame();

        states.InvalidateSnapshotForHotReload();
        states.BeginFrame();
        var current = states.GetSnapshot(() => [count]);
        states.CommitFrame();
        states.PruneOrdinaryStates(current, new StorageOwner());
        states.CompleteHotReloadReconciliation();

        Assert.Same(count, states.GetOrCreate("count", () => new State<int>(0)));
        Assert.NotSame(removed, states.GetOrCreate("temporary", () => new State<string>("New declaration")));
        Assert.True(current == states.GetSnapshot(() => throw new Exception("Snapshot should be cached.")));
    }

    [Fact]
    public void HotReload_PruneDetachesRemovedStatesButPreservesRetainedAndForeignOwnership()
    {
        var owner = new StorageOwner();
        var otherOwner = new StorageOwner();
        var states = new StateCollection();
        var count = states.GetOrCreate("count", () =>
            new StateInfo<int>("count", static _ => 42).CreateTypedState(owner));
        var removed = states.GetOrCreate("removed", () =>
            new StateInfo<int>("removed", static _ => 1).CreateTypedState(owner));
        var foreign = states.GetOrCreate("foreign", () =>
            new StateInfo<int>("foreign", static _ => 2).CreateTypedState(otherOwner));

        states.PruneOrdinaryStates([count], owner);

        Assert.True(count.IsOwnedBy(owner));
        Assert.False(removed.IsAttached);
        Assert.True(foreign.IsOwnedBy(otherOwner));
        Assert.Same(count, states.GetOrCreate<int>("count", () => throw new Exception("Retained state must not be recreated.")));
        Assert.NotSame(removed, states.GetOrCreate("removed", () => new State<int>(0)));
        Assert.NotSame(foreign, states.GetOrCreate("foreign", () => new State<int>(0)));
    }

    [Fact]
    public void HotReload_PruneRejectsNullOwnerWithoutRemovingStates()
    {
        var states = new StateCollection();
        var state = states.GetOrCreate("count", () => new State<int>(42));

        Assert.Throws<ArgumentNullException>(() => states.PruneOrdinaryStates([], null!));
        Assert.Same(state, states.GetOrCreate<int>("count", () => throw new Exception("State must remain registered.")));
    }

    private sealed class StorageOwner : AkburaControl
    {
        public StorageOwner() : base(AkburaEngine.Empty)
        {
        }

        protected override Control FirstUpdate() => new Border();
        protected override Control Update() => new Border();
        protected override ImmutableArray<Parameter> GetParameters() => [];
        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];
        protected override ImmutableArray<InjectService> GetServices() => [];
        protected override ImmutableArray<State> GetStates() => [];
    }
}
