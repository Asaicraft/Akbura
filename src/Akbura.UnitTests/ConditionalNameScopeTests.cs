using Akbura.HotReload;
using Akbura.Markup;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System.Collections;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ConditionalNameScopeTests
{
    [Fact]
    public async Task SourceReplacement_UsesSeparateScopesAndRollbackRestoresNativeBindingSource()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture();
            fixture.Begin("initial");
            fixture.Select(0);
            var originalScope = fixture.Initialize();
            var originalSource = fixture.State.GetRequired<TextBox>(2);
            var target = fixture.State.GetRequired<TextBlock>(3);
            originalSource.Text = "original";
            fixture.State.CompleteRevision();
            Assert.Equal("original", target.Text);

            fixture.ReplaceSource = true;
            fixture.Begin("replacement");
            fixture.Select(0);
            var replacementScope = fixture.Initialize();
            var replacementSource = fixture.State.GetRequired<TextBlock>(2);
            replacementSource.Text = "replacement";
            Assert.NotSame(originalScope, replacementScope);
            Assert.Same(originalSource, originalScope.Find("source"));
            Assert.Same(replacementSource, replacementScope.Find("source"));
            Assert.Equal("replacement", target.Text);
            fixture.State.AbortRevision();

            Assert.Same(originalScope, fixture.State.GetConditionalNameScope(0, 0, null));
            Assert.Same(originalSource, fixture.State.GetRequired<TextBox>(2));
            Assert.Same(originalScope, NameScope.GetNameScope(fixture.Panel));
            Assert.Equal("original", target.Text);
            replacementSource.Text = "inactive replacement";
            Assert.Equal("original", target.Text);
            originalSource.Text = "restored binding";
            Assert.Equal("restored binding", target.Text);

            fixture.Begin("replacement");
            fixture.Select(0);
            var committedScope = fixture.Initialize();
            var committedSource = fixture.State.GetRequired<TextBlock>(2);
            committedSource.Text = "committed";
            fixture.State.CompleteRevision();
            Assert.Same(committedScope, NameScope.GetNameScope(fixture.Panel));
            Assert.Equal("committed", target.Text);
            originalSource.Text = "old source";
            Assert.Equal("committed", target.Text);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BranchExit_RemovesScopeAttachmentAndReentryUsesFreshNativeScope()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture();
            fixture.Begin("initial");
            fixture.Select(0);
            var firstScope = fixture.Initialize();
            var firstPanel = fixture.Panel;
            fixture.State.CompleteRevision();
            fixture.Select(1);
            fixture.Initialize();
            fixture.State.CompleteRevision();
            Assert.Null(NameScope.GetNameScope(firstPanel));
            fixture.Select(0);
            var nextScope = fixture.Initialize();
            fixture.State.CompleteRevision();
            Assert.NotSame(firstPanel, fixture.Panel);
            Assert.NotSame(firstScope, nextScope);
            Assert.Same(fixture.State.GetRequired<TextBox>(2), nextScope.Find("source"));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UnchangedScopeLookup_DoesNotStartTransactionOrAllocate()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture();
            fixture.Begin("initial");
            fixture.Select(0);
            var scope = fixture.State.GetConditionalNameScope(0, 0, null);
            scope.Register("source", fixture.State.GetRequired<TextBox>(2));
            scope.Complete();
            fixture.State.CompleteRevision();
            for (var i = 0; i < 1000; i++)
            {
                fixture.State.GetConditionalNameScope(0, 0, null);
            }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++)
            {
                fixture.State.GetConditionalNameScope(0, 0, null);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.Same(scope, fixture.State.GetConditionalNameScope(0, 0, null));
            Assert.False(fixture.State.HasPendingRevision);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SourceOmission_RemovesBaseScopeAndReleasesNamesWithoutPruningActivationOrAbort()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var fixture = new Fixture();
            fixture.Begin("initial");
            fixture.Select(0);
            fixture.Initialize();
            var root = fixture.State.GetRequired<Border>(0);
            var payload = InitializeBaseScope(fixture.State, root);
            fixture.State.CompleteRevision();

            fixture.Select(1);
            fixture.Initialize();
            fixture.State.CompleteRevision();
            AssertBaseScopePresent(fixture.State, root);

            fixture.Begin("without-base");
            fixture.Select(1);
            fixture.Initialize();
            fixture.State.AbortRevision();
            AssertBaseScopePresent(fixture.State, root);

            fixture.Begin("without-base");
            fixture.Select(1);
            fixture.Initialize();
            fixture.State.CompleteRevision();
            Assert.Null(fixture.State.GetNameScopeForNode(root));
            Assert.Null(NameScope.GetNameScope(root));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(payload.TryGetTarget(out _));
            GC.KeepAlive(fixture);
        }, CancellationToken.None);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<object> InitializeBaseScope(AkburaRenderState state, Border root)
    {
        var scope = state.GetLocalNameScope(null);
        var payload = new object();
        scope.Register("retired", payload);
        scope.Complete();
        state.ReconcileConditionalAvaloniaValue(0, "$base-names", root,
            NameScope.NameScopeProperty, "base-scope", scope);
        return new WeakReference<object>(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertBaseScopePresent(AkburaRenderState state, Border root)
    {
        var scope = state.GetNameScopeForNode(root);
        Assert.NotNull(scope);
        Assert.Same(scope, NameScope.GetNameScope(root));
        Assert.NotNull(scope.Find("retired"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Border _root = new();
        private int _branch;

        public AkburaRenderState State { get; } = new();

        public bool ReplaceSource { get; set; }

        public StackPanel Panel => State.GetRequired<StackPanel>(_branch == 0 ? 1 : 4);

        public void Begin(string revision)
        {
            State.BeginRevision(revision, builder =>
            {
                builder.Add(new(0, -1, "$root", typeof(Border), null, "root"));
                builder.Add(new(1, 0, "Child", typeof(StackPanel), null, "panel-a", 0, 0));
                builder.Add(new(2, 1, "Children", ReplaceSource ? typeof(TextBlock) : typeof(TextBox), null, "source-a", 0, 0));
                builder.Add(new(3, 1, "Children", typeof(TextBlock), null, "target-a", 0, 0));
                builder.Add(new(4, 0, "Child", typeof(StackPanel), null, "panel-b", 0, 1));
                builder.Add(new(5, 4, "Children", typeof(TextBox), null, "source-b", 0, 1));
                builder.Add(new(6, 4, "Children", typeof(TextBlock), null, "target-b", 0, 1));
                builder.AddConditional(new(0, 0, "Child", "same-if", [new("active", "a"), new("", "b")]));
            }, id => id switch
            {
                0 => _root,
                1 or 4 => new StackPanel(),
                2 when ReplaceSource => new TextBlock(),
                2 or 5 => new TextBox(),
                3 or 6 => new TextBlock(),
                _ => throw new ArgumentOutOfRangeException(nameof(id)),
            });
        }

        public void Select(int branch)
        {
            _branch = branch;
            State.SelectConditionalBranch(0, branch);
        }

        public INameScope Initialize()
        {
            var panelId = _branch == 0 ? 1 : 4;
            var sourceId = _branch == 0 ? 2 : 5;
            var targetId = _branch == 0 ? 3 : 6;
            var panel = Panel;
            var source = State.GetRequired<Control>(sourceId);
            var target = State.GetRequired<TextBlock>(targetId);
            var scope = State.GetConditionalNameScope(0, _branch, null);
            scope.Register("source", source);
            scope.Complete();
            State.ReconcileConditionalAvaloniaValue(panelId, "$names", panel,
                NameScope.NameScopeProperty, "scope", scope);
            State.ReconcileCollection(panelId, "Children", (IList)panel.Children, [source, target]);
            State.ReconcileConditionalClrValue(0, "Child", _root, typeof(Decorator), nameof(Border.Child), "child", panel);
            Assert.True(State.ShouldApplyOwnedOperation(targetId, "Text", "same-binding", true));
            var binding = new ReflectionBindingExtension("Text") { ElementName = "source" }
                .ProvideValue(new AkburaNameScopeServiceProvider(scope, EmptyServices.Instance));
            State.ApplyBindingOperation(targetId, "Text", target, TextBlock.TextProperty, binding);
            return scope;
        }

        public void Dispose() => State.Dispose();
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static EmptyServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
