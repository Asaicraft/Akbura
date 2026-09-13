using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class LocalConditionalRenderScopeTests
{
    [Fact]
    public async Task Instances_RetainTheirOwnBranchesAndUpdateWithoutSharingLocalIds()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = new ScopeOwner();
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var first = owner.CreateScope(true, "first");
                var second = owner.CreateScope(false, "second");
                var retained = Assert.IsType<TextBlock>(Assert.Single(first.Root.Children));
                Assert.IsType<Button>(Assert.Single(second.Root.Children));
                first.Text = "updated";
                owner.InvalidState();
                Assert.Same(retained, Assert.Single(first.Root.Children));
                Assert.Equal("updated", retained.Text);
                Assert.NotSame(first.Root, second.Root);
                Assert.Equal(1, first.ConstructedChildren);
                Assert.Equal(1, second.ConstructedChildren);

                first.Selected = false;
                owner.InvalidState();
                Assert.IsType<Button>(Assert.Single(first.Root.Children));
                Assert.Equal(2, first.ConstructedChildren);
                Assert.Equal(1, second.ConstructedChildren);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InstanceCreatedInsideParentUpdate_RendersOnlyOnceInThatFrame()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = new ScopeOwner { CreateDuringUpdate = true };
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var scope = Assert.Single(owner.Created);
                Assert.Equal(1, scope.RenderCount);
                owner.InvalidState();
                Assert.Equal(2, scope.RenderCount);
                Assert.Equal(1, scope.ConstructedChildren);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NestedInstanceCreatedByRenderCallback_DoesNotRenderAgainInTheSameFrame()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = new ScopeOwner();
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var outer = owner.CreateScope(true, "outer");
                ScopeInstance? inner = null;
                outer.AfterRender = () => inner ??= owner.CreateScope(true, "inner");
                owner.InvalidState();
                Assert.NotNull(inner);
                Assert.Equal(1, inner.RenderCount);
                Assert.Equal(2, outer.RenderCount);
                owner.InvalidState();
                Assert.Equal(2, inner.RenderCount);
                Assert.Equal(3, outer.RenderCount);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DetachedInstance_CleansItsStateAndReattachesWithFreshChildren()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = new ScopeOwner();
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var first = owner.CreateScope(true, "first");
                var second = owner.CreateScope(true, "second");
                var oldChild = Assert.Single(first.Root.Children);
                var retainedSecond = Assert.Single(second.Root.Children);
                owner.Root.Children.Remove(first.Root);
                Assert.Equal(1, first.CleanupCount);
                var firstRenderCount = first.RenderCount;
                owner.InvalidState();
                Assert.Equal(firstRenderCount, first.RenderCount);
                Assert.Same(retainedSecond, Assert.Single(second.Root.Children));

                owner.Root.Children.Add(first.Root);
                Assert.NotSame(oldChild, Assert.Single(first.Root.Children));
                Assert.Equal(firstRenderCount + 1, first.RenderCount);
                Assert.Equal(2, first.ConstructedChildren);
                first.Text = "reattached";
                owner.InvalidState();
                Assert.Equal("reattached", Assert.IsType<TextBlock>(Assert.Single(first.Root.Children)).Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PermanentlyDisposedInstance_CleansOnceAndDoesNotReactivate()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = new ScopeOwner();
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var scope = owner.CreateScope(true, "first");
                scope.Lease!.Dispose();
                scope.Lease.Dispose();
                Assert.Equal(1, scope.CleanupCount);
                var renders = scope.RenderCount;
                owner.Root.Children.Remove(scope.Root);
                owner.Root.Children.Add(scope.Root);
                owner.InvalidState();
                Assert.Equal(renders, scope.RenderCount);
                Assert.Equal(1, scope.CleanupCount);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedParentRevision_ReattachesTheExistingInstanceWithoutDisposingItsBranch()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = new ScopeOwner();
            var parentState = new AkburaRenderState();
            owner.LifetimeOwner = parentState;
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var scope = new ScopeInstance { Selected = true, Text = "retained" };
                scope.Render();
                owner.RegisterUnattachedScope(scope);
                var retained = Assert.Single(scope.Root.Children);
                parentState.BeginRevision("initial", builder =>
                    builder.Add(new(0, -1, "$root", typeof(StackPanel), null, "root")), _ => owner.Root);
                parentState.RegisterLocalRenderScopeLifetime(owner.Root, scope.Lease!);
                parentState.ReconcileCollection<Control>(0, "instances", owner.Root.Children, [scope.Root]);
                parentState.CompleteRevision();

                parentState.BeginRevision("failed", builder =>
                    builder.Add(new(0, -1, "$root", typeof(StackPanel), null, "root")), _ => owner.Root);
                parentState.ReconcileCollection<Control>(0, "instances", owner.Root.Children, []);
                Assert.False(scope.Root.IsAttachedToVisualTree());
                Assert.Same(retained, Assert.Single(scope.Root.Children));
                Assert.Equal(0, scope.CleanupCount);
                parentState.AbortRevision();

                Assert.True(scope.Root.IsAttachedToVisualTree());
                Assert.Same(scope.Root, Assert.Single(owner.Root.Children));
                Assert.Same(retained, Assert.Single(scope.Root.Children));
                Assert.Equal(0, scope.CleanupCount);
                var renders = scope.RenderCount;
                owner.InvalidState();
                Assert.Equal(renders + 1, scope.RenderCount);
                Assert.Same(retained, Assert.Single(scope.Root.Children));
            }
            finally
            {
                window.Close();
                parentState.Dispose();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DiscardedNeverAttachedInstance_IsNotRetainedByTheParentScheduler()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = new ScopeOwner();
            var discarded = CreateDiscardedInstance(owner);
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Assert.False(discarded.TryGetTarget(out _));
            GC.KeepAlive(owner);
        }, CancellationToken.None);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Control> CreateDiscardedInstance(ScopeOwner owner)
    {
        var scope = new ScopeInstance { Selected = true, Text = "discarded" };
        scope.Render();
        owner.RegisterUnattachedScope(scope);
        return new(scope.Root);
    }

    private sealed class ScopeOwner : AkburaControl
    {
        public ScopeOwner() : base(AkburaEngine.Empty) { }
        public StackPanel Root { get; } = new();
        public bool CreateDuringUpdate { get; set; }
        public AkburaRenderState? LifetimeOwner { get; set; }
        public List<ScopeInstance> Created { get; } = [];

        public ScopeInstance CreateScope(bool selected, string text)
        {
            var scope = new ScopeInstance { Selected = selected, Text = text };
            scope.Render();
            RegisterUnattachedScope(scope);
            Created.Add(scope);
            Root.Children.Add(scope.Root);
            return scope;
        }

        public void RegisterUnattachedScope(ScopeInstance scope) =>
            scope.Lease = __AkburaRegisterLocalRenderScope(scope.Root, scope.Render, scope.Cleanup, LifetimeOwner);

        protected override Control FirstUpdate() => Root;
        protected override Control Update()
        {
            if (CreateDuringUpdate)
            {
                CreateDuringUpdate = false;
                CreateScope(true, "created in parent update");
            }
            return Root;
        }
        protected override ImmutableArray<Parameter> GetParameters() => [];
        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];
        protected override ImmutableArray<InjectService> GetServices() => [];
        protected override ImmutableArray<State> GetStates() => [];
    }

    private sealed class ScopeInstance
    {
        public StackPanel Root { get; } = new();
        public AkburaRenderState State { get; } = new();
        public bool Selected { get; set; }
        public string Text { get; set; } = string.Empty;
        public int RenderCount { get; private set; }
        public int CleanupCount { get; private set; }
        public int ConstructedChildren { get; private set; }
        public Action? AfterRender { get; set; }
        public IDisposable? Lease { get; set; }

        public void Render()
        {
            RenderCount++;
            State.BeginRevision("scope", builder =>
            {
                builder.Add(new(0, -1, "$root", typeof(StackPanel), null, "root"));
                builder.Add(new(1, 0, "children", typeof(TextBlock), null, "text", 0, 0));
                builder.Add(new(2, 0, "children", typeof(Button), null, "button", 0, 1));
                builder.AddConditional(new(0, 0, "children", "if", [new("selected", "text"), new("", "button")]));
            }, id =>
            {
                if (id == 0) return Root;
                ConstructedChildren++;
                return id == 1 ? new TextBlock() : new Button();
            });
            var changed = State.IsApplyingSourceRevision;
            changed |= State.SelectConditionalBranch(0, Selected ? 0 : 1);
            var child = State.GetRequired<Control>(Selected ? 1 : 2);
            if (child is TextBlock textBlock) textBlock.Text = Text;
            if (changed) State.ReconcileCollection<Control>(0, "children", Root.Children, [child]);
            if (State.HasPendingRevision) State.CompleteRevision();
            AfterRender?.Invoke();
        }

        public void Cleanup()
        {
            CleanupCount++;
            State.Dispose();
        }
    }
}
