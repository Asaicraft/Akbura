using Akbura.Markup;
using Akbura.HotReload;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.VisualTree;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ConditionalTemplateHostTests
{
    [Fact]
    public void DisposedInstanceHeldByCaller_ReleasesRendererAndCleanupCaptures()
    {
        var (instance, capture) = CreateDisposedInstanceWithCapture();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(capture.TryGetTarget(out _));
        instance.Update();
        GC.KeepAlive(instance);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (AkburaConditionalTemplateInstance Instance, WeakReference<object> Capture) CreateDisposedInstanceWithCapture()
    {
        var capture = new object();
        var reference = new WeakReference<object>(capture);
        var instance = new AkburaConditionalTemplateInstance(() => GC.KeepAlive(capture), () => GC.KeepAlive(capture));
        instance.Dispose();
        return (instance, reference);
    }

    [Fact]
    public async Task InitiallyEmptyRoot_AdoptsNativePresenterAndTransitionsWithoutVisualWrapper()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var fixture = new Fixture();
            var host = new ContentPresenter { Content = "first", ContentTemplate = fixture.Template };
            var window = new Window { Content = host };
            try
            {
                window.Show();
                host.UpdateChild();
                var instance = Assert.Single(fixture.Instances);
                Assert.Null(host.Child);
                Assert.Equal(0, fixture.Constructed);

                fixture.Selected = 0;
                instance.Update();
                var first = Assert.IsType<TextBlock>(host.Child);
                Assert.Same(host, first.GetVisualParent());
                Assert.Same(host, window.Content);
                Assert.Equal(1, fixture.Constructed);

                instance.Update();
                host.UpdateChild();
                Assert.Same(first, host.Child);
                Assert.Equal(1, fixture.Constructed);

                fixture.Selected = 1;
                instance.Update();
                var second = Assert.IsType<Button>(host.Child);
                Assert.NotSame(first, second);
                Assert.Null(first.GetVisualParent());
                Assert.Same(host, second.GetVisualParent());
                fixture.Selected = -1;
                instance.Update();
                Assert.Null(host.Child);
                Assert.Null(second.GetVisualParent());
                fixture.Selected = 0;
                instance.Update();
                Assert.NotSame(first, Assert.IsType<TextBlock>(host.Child));
                Assert.Equal(3, fixture.Constructed);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InitialActiveBranch_ConstructsExactlyOnceAcrossHostAdoptionAndNativeRecycling()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var fixture = new Fixture { Selected = 0 };
            var host = new ContentPresenter { Content = "first", ContentTemplate = fixture.Template };
            var window = new Window { Content = host };
            try
            {
                window.Show();
                host.UpdateChild();
                var root = Assert.IsType<TextBlock>(host.Child);
                Assert.Single(fixture.Instances);
                Assert.Equal(1, fixture.Constructed);
                host.UpdateChild();
                host.UpdateChild();
                Assert.Same(root, host.Child);
                Assert.Equal(1, fixture.Constructed);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SharedTemplate_CreatesIndependentInstancesEvenWhenDataReferenceIsIdentical()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var fixture = new Fixture { Selected = 0 };
            var first = new ContentPresenter { Content = "shared", ContentTemplate = fixture.Template };
            var second = new ContentPresenter { Content = "shared", ContentTemplate = fixture.Template };
            var foreign = new Button();
            var panel = new StackPanel { Children = { first, foreign, second } };
            var window = new Window { Content = panel };
            try
            {
                window.Show();
                first.UpdateChild();
                second.UpdateChild();
                Assert.Equal(2, fixture.Instances.Count);
                var firstRoot = Assert.IsType<TextBlock>(first.Child);
                var secondRoot = Assert.IsType<TextBlock>(second.Child);
                Assert.NotSame(firstRoot, secondRoot);
                fixture.Selected = 1;
                fixture.Instances[0].Update();
                Assert.IsType<Button>(first.Child);
                Assert.Same(secondRoot, second.Child);
                Assert.Equal(new Control[] { first, foreign, second }, panel.Children);
                Assert.Same(first, first.Child!.GetVisualParent());
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HostAdoption_PreservesBindingAndDoesNotInterceptForeignTemplate()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var fixture = new Fixture { Selected = 0 };
            var source = new TemplateSource { Template = fixture.Template };
            var host = new ContentPresenter { Content = "first" };
            using var binding = host.Bind(ContentPresenter.ContentTemplateProperty,
                new Binding(nameof(TemplateSource.Template)) { Source = source });
            var window = new Window { Content = host };
            try
            {
                window.Show();
                host.UpdateChild();
                var oldInstance = Assert.Single(fixture.Instances);
                var root = Assert.IsType<TextBlock>(host.Child);
                var foreignRoot = new Button { Content = "foreign" };
                var foreign = new FuncDataTemplate<string>((_, _) => foreignRoot);
                source.Template = foreign;
                host.UpdateChild();
                Assert.Same(foreign, host.ContentTemplate);
                Assert.Same(foreignRoot, host.Child);
                Assert.Equal(1, fixture.Cleaned);
                fixture.Selected = 1;
                oldInstance.Update();
                Assert.Same(foreignRoot, host.Child);
                Assert.Null(root.GetVisualParent());

                source.Template = fixture.Template;
                host.UpdateChild();
                Assert.Equal(2, fixture.Instances.Count);
                Assert.NotSame(oldInstance, fixture.Instances[1]);
                Assert.IsType<Button>(host.Child);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FailedUpdate_RestoresPreviouslyPublishedNativeRoot()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var fixture = new Fixture { Selected = 0 };
            var host = new ContentPresenter { Content = "first", ContentTemplate = fixture.Template };
            var window = new Window { Content = host };
            try
            {
                window.Show();
                host.UpdateChild();
                var first = Assert.IsType<TextBlock>(host.Child);
                fixture.Selected = 1;
                fixture.FailAfterPublish = true;
                Assert.Throws<InvalidOperationException>(() => Assert.Single(fixture.Instances).Update());
                Assert.Same(first, host.Child);
                Assert.Same(host, first.GetVisualParent());
                fixture.FailAfterPublish = false;
                Assert.Single(fixture.Instances).Update();
                Assert.IsType<Button>(host.Child);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UnchangedRoot_DoesNotAllocateOrInvokeNativeReplacement()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var fixture = new Fixture { Selected = 0 };
            var host = new ContentPresenter { Content = "first", ContentTemplate = fixture.Template };
            var instance = Assert.Single(fixture.Instances);
            var root = Assert.IsType<TextBlock>(host.Child);
            for (var i = 0; i < 1000; i++)
            {
                instance.Update();
            }

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++)
            {
                instance.Update();
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(0, allocated);
            Assert.Same(root, host.Child);
            Assert.Equal(1, fixture.Constructed);
            instance.Dispose();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeDataTemplate_DeferredMarkerPreservesMatchingAndSupportsEmptyRoot()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var fixture = new Fixture();
            var template = new Avalonia.Markup.Xaml.Templates.DataTemplate
                { DataType = typeof(string), Content = fixture.CreateMarker() };
            var host = new ContentPresenter { Content = "first", ContentTemplate = template };
            var window = new Window { Content = host };
            try
            {
                window.Show();
                host.UpdateChild();
                Assert.Null(host.Child);
                Assert.Single(fixture.Instances);
                Assert.True(host.ContentTemplate!.Match("other"));
                Assert.False(host.ContentTemplate.Match(123));
                fixture.Selected = 0;
                fixture.Instances[0].Update();
                Assert.IsType<TextBlock>(host.Child);
                Assert.Equal(1, fixture.Constructed);
                Assert.Same(host, host.Child!.GetVisualParent());
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NativeControlTemplate_DeferredMarkerUsesNativeLifecycleAcrossNullableRootTransitions()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var fixture = new Fixture();
            var host = new Avalonia.Controls.Primitives.TemplatedControl
            {
                Template = new Avalonia.Markup.Xaml.Templates.ControlTemplate { Content = fixture.CreateMarker() },
            };
            var window = new Window { Content = host };
            try
            {
                window.Show();
                host.ApplyTemplate();
                Assert.Empty(host.GetVisualChildren());
                var instance = Assert.Single(fixture.Instances);
                Assert.Equal(0, fixture.Constructed);
                fixture.Selected = 0;
                instance.Update();
                var first = Assert.IsType<TextBlock>(Assert.Single(host.GetVisualChildren()));
                Assert.Same(host, first.GetVisualParent());
                Assert.Same(host, first.TemplatedParent);
                instance.Update();
                host.ApplyTemplate();
                Assert.Same(first, Assert.Single(host.GetVisualChildren()));
                Assert.Equal(1, fixture.Constructed);
                fixture.Selected = 1;
                instance.Update();
                var second = Assert.IsType<Button>(Assert.Single(host.GetVisualChildren()));
                Assert.Null(first.GetVisualParent());
                Assert.Null(first.TemplatedParent);
                Assert.Same(host, second.TemplatedParent);
                fixture.Selected = -1;
                instance.Update();
                Assert.Empty(host.GetVisualChildren());
                fixture.Selected = 0;
                instance.Update();
                Assert.NotSame(first, Assert.IsType<TextBlock>(Assert.Single(host.GetVisualChildren())));
                Assert.Equal(3, fixture.Constructed);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PendingTemplateReplacement_AbortsWithoutDestroyingAppliedInstanceAndRetiresOnlyOnCommit()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var state = new AkburaRenderState();
            var fixture = new Fixture { Selected = 0, ParentState = state };
            var host = new ContentPresenter { Content = "first", ContentTemplate = fixture.Template };
            Begin(state, host, "initial");
            state.ReconcileClrValue(0, "Template", host, typeof(ContentPresenter), nameof(ContentPresenter.ContentTemplate),
                "initial", host.ContentTemplate);
            state.CompleteRevision();
            var original = Assert.IsType<TextBlock>(host.Child);
            var originalInstance = Assert.Single(fixture.Instances);
            var foreign = new FuncDataTemplate<string>((_, _) => new Button());
            Begin(state, host, "failed-source");
            state.ReconcileClrValue(0, "Template", host, typeof(ContentPresenter), nameof(ContentPresenter.ContentTemplate),
                "changed", foreign);
            host.UpdateChild();
            Assert.IsType<Button>(host.Child);
            Assert.Equal(0, fixture.Cleaned);
            state.PrepareRevisionCompletion();
            state.AbortRevision();
            host.UpdateChild();
            Assert.Same(original, host.Child);
            Assert.Single(fixture.Instances);
            Assert.Equal(0, fixture.Cleaned);
            originalInstance.Update();
            Assert.Same(original, host.Child);

            Begin(state, host, "committed-source");
            state.ReconcileClrValue(0, "Template", host, typeof(ContentPresenter), nameof(ContentPresenter.ContentTemplate),
                "changed", foreign);
            host.UpdateChild();
            state.PrepareRevisionCompletion();
            Assert.Equal(0, fixture.Cleaned);
            state.CompleteRevision();
            Assert.Equal(1, fixture.Cleaned);
            originalInstance.Update();
            Assert.IsType<Button>(host.Child);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NonmatchingDataReplacement_PreservesTheAppliedInstanceOnSourceAbortAndUnmountsOnCommit()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            using var state = new AkburaRenderState();
            var fixture = new Fixture { Selected = 0, ParentState = state };
            var host = new ContentPresenter { Content = "first", ContentTemplate = fixture.Template };
            Begin(state, host, "initial");
            state.ReconcileClrValue(0, "Content", host, typeof(ContentPresenter), nameof(ContentPresenter.Content),
                "initial", "first");
            state.CompleteRevision();
            var descriptor = host.ContentTemplate;
            var original = Assert.IsType<TextBlock>(host.Child);
            var originalInstance = Assert.Single(fixture.Instances);

            Begin(state, host, "failed-source");
            state.ReconcileClrValue(0, "Content", host, typeof(ContentPresenter), nameof(ContentPresenter.Content),
                "changed", 123);
            host.UpdateChild();
            var fallback = Assert.IsType<TextBlock>(host.Child);
            Assert.Equal("123", fallback.Text);
            Assert.NotSame(original, fallback);
            fixture.Selected = 1;
            originalInstance.Update();
            Assert.Same(fallback, host.Child);
            Assert.Equal(1, fixture.Constructed);
            Assert.Equal(0, fixture.Cleaned);
            fixture.Selected = 0;
            state.PrepareRevisionCompletion();
            state.AbortRevision();
            host.UpdateChild();
            Assert.Same(descriptor, host.ContentTemplate);
            Assert.Same(original, host.Child);
            Assert.Single(fixture.Instances);
            Assert.Equal(1, fixture.Constructed);
            Assert.Equal(0, fixture.Cleaned);

            Begin(state, host, "committed-source");
            state.ReconcileClrValue(0, "Content", host, typeof(ContentPresenter), nameof(ContentPresenter.Content),
                "changed", 456);
            host.UpdateChild();
            var committedFallback = Assert.IsType<TextBlock>(host.Child);
            Assert.Equal("456", committedFallback.Text);
            state.CompleteRevision();
            Assert.Same(committedFallback, host.Child);
            Assert.Equal(1, fixture.Cleaned);
            Assert.Null(originalInstance.Root);
            originalInstance.Update();
            Assert.Equal(1, fixture.Constructed);
            host.Content = "first";
            host.UpdateChild();
            Assert.Same(descriptor, host.ContentTemplate);
            Assert.NotSame(original, Assert.IsType<TextBlock>(host.Child));
            Assert.Equal(2, fixture.Instances.Count);
            Assert.Equal(2, fixture.Constructed);
            Assert.Equal(1, fixture.Cleaned);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PendingDataReplacement_ReusesOriginalDataInstanceOnAbortAndRetiresAfterCommit()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var state = new AkburaRenderState();
            var fixture = new Fixture { Selected = 0, ParentState = state };
            var host = new ContentPresenter { Content = "first", ContentTemplate = fixture.Template };
            Begin(state, host, "initial");
            state.ReconcileClrValue(0, "Content", host, typeof(ContentPresenter), nameof(ContentPresenter.Content),
                "initial", "first");
            state.CompleteRevision();
            var original = Assert.IsType<TextBlock>(host.Child);
            Begin(state, host, "failed-source");
            state.ReconcileClrValue(0, "Content", host, typeof(ContentPresenter), nameof(ContentPresenter.Content),
                "changed", "second");
            host.UpdateChild();
            Assert.NotSame(original, host.Child);
            Assert.Equal(0, fixture.Cleaned);
            state.PrepareRevisionCompletion();
            state.AbortRevision();
            host.UpdateChild();
            Assert.Same(original, host.Child);
            Assert.Equal(2, fixture.Instances.Count);
            Assert.Equal(1, fixture.Cleaned);
            Assert.Equal(2, fixture.Constructed);

            Begin(state, host, "committed-source");
            state.ReconcileClrValue(0, "Content", host, typeof(ContentPresenter), nameof(ContentPresenter.Content),
                "changed", "third");
            host.UpdateChild();
            var committed = Assert.IsType<TextBlock>(host.Child);
            Assert.NotSame(original, committed);
            Assert.Equal(1, fixture.Cleaned);
            state.CompleteRevision();
            Assert.Equal(2, fixture.Cleaned);
            Assert.Same(committed, host.Child);
            Assert.Equal(3, fixture.Instances.Count);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OmittedOwnedTemplate_RestoresAvaloniaBaselineAfterAdapterAdoption()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var state = new AkburaRenderState();
            var fixture = new Fixture { Selected = 0, ParentState = state };
            var foreignRoot = new Button();
            var baseline = new FuncDataTemplate<string>((_, _) => foreignRoot);
            var host = new ContentPresenter { Content = "first", ContentTemplate = baseline };
            host.UpdateChild();
            Assert.Same(foreignRoot, host.Child);
            Begin(state, host, "initial");
            state.ReconcileAvaloniaValue(0, "Template", host, ContentPresenter.ContentTemplateProperty,
                "owned", fixture.Template);
            state.CompleteRevision();
            var owned = Assert.IsType<TextBlock>(host.Child);
            Assert.NotSame(fixture.Template, host.ContentTemplate);

            Begin(state, host, "omitted");
            state.PrepareRevisionCompletion();
            host.UpdateChild();
            Assert.Same(baseline, host.ContentTemplate);
            Assert.Same(foreignRoot, host.Child);
            Assert.Null(owned.GetVisualParent());
            Assert.Equal(0, fixture.Cleaned);
            state.CompleteRevision();
            Assert.Equal(1, fixture.Cleaned);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RepeatedFailedDataEdits_RetainAppliedRootAndDisposeEveryAbandonedInstance()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var state = new AkburaRenderState();
            var fixture = new Fixture { Selected = 0, ParentState = state };
            var host = new ContentPresenter { Content = "first", ContentTemplate = fixture.Template };
            Begin(state, host, "initial");
            state.ReconcileClrValue(0, "Content", host, typeof(ContentPresenter), nameof(ContentPresenter.Content),
                "initial", "first");
            state.CompleteRevision();
            var original = Assert.IsType<TextBlock>(host.Child);
            for (var i = 0; i < 20; i++)
            {
                Begin(state, host, "failed-source-" + i);
                state.ReconcileClrValue(0, "Content", host, typeof(ContentPresenter), nameof(ContentPresenter.Content),
                    "changed", "abandoned-" + i);
                host.UpdateChild();
                state.AbortRevision();
                host.UpdateChild();
                Assert.Same(original, host.Child);
                Assert.Equal(i + 1, fixture.Cleaned);
            }

            Assert.Equal(21, fixture.Instances.Count);
            Assert.Equal(21, fixture.Constructed);
            Assert.False(state.HasPendingRevision);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeDeferredContentEdit_RebindsNewMarkerAndRestoresCachedAppliedInstanceOnAbort(bool controlTemplate)
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var state = new AkburaRenderState();
            var originalFixture = new Fixture { Selected = 0, ParentState = state };
            var updatedFixture = new Fixture { Selected = 1, ParentState = state };
            var originalMarker = originalFixture.CreateMarker();
            var updatedMarker = updatedFixture.CreateMarker();
            object native;
            Control host;
            if (controlTemplate)
            {
                var template = new Avalonia.Markup.Xaml.Templates.ControlTemplate { Content = originalMarker };
                native = template;
                host = new Avalonia.Controls.Primitives.TemplatedControl { Template = template };
            }
            else
            {
                var template = new Avalonia.Markup.Xaml.Templates.DataTemplate { DataType = typeof(string), Content = originalMarker };
                native = template;
                host = new ContentPresenter { Content = "first", ContentTemplate = template };
            }

            var window = new Window { Content = host };
            try
            {
                window.Show();
                BeginNative(state, native, "initial");
                state.ReconcileClrValue(0, "Content", native, native.GetType(), "Content", "original", originalMarker);
                state.CompleteRevision();
                var original = Assert.IsType<TextBlock>(GetRoot(host));
                BeginNative(state, native, "failed-source");
                state.ReconcileClrValue(0, "Content", native, native.GetType(), "Content", "updated", updatedMarker);
                Assert.IsType<Button>(GetRoot(host));
                Assert.Equal(0, originalFixture.Cleaned);
                state.PrepareRevisionCompletion();
                state.AbortRevision();
                Assert.Same(original, GetRoot(host));
                Assert.Single(originalFixture.Instances);
                Assert.Equal(0, originalFixture.Cleaned);
                Assert.Equal(1, updatedFixture.Cleaned);

                BeginNative(state, native, "committed-source");
                state.ReconcileClrValue(0, "Content", native, native.GetType(), "Content", "updated", updatedMarker);
                var committed = Assert.IsType<Button>(GetRoot(host));
                Assert.Equal(0, originalFixture.Cleaned);
                state.CompleteRevision();
                Assert.Equal(1, originalFixture.Cleaned);
                Assert.Same(committed, GetRoot(host));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Control? GetRoot(Control host) => host is ContentPresenter presenter
        ? presenter.Child : host.GetVisualChildren().OfType<Control>().SingleOrDefault();

    private static void BeginNative(AkburaRenderState state, object native, string revision)
    {
        Assert.True(state.BeginRevision(revision,
            builder => builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", native.GetType(), null, "root")),
            _ => native));
    }

    private static void Begin(AkburaRenderState state, ContentPresenter host, string revision)
    {
        Assert.True(state.BeginRevision(revision,
            static builder => builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(ContentPresenter), null, "root")),
            _ => host));
    }

    private sealed class Fixture
    {
        public Fixture() => Template = new AkburaConditionalDataTemplate<string>(CreateInstance);

        public AkburaConditionalDataTemplate<string> Template { get; }

        public List<AkburaConditionalTemplateInstance> Instances { get; } = [];

        public int Selected { get; set; } = -1;

        public int Constructed { get; private set; }

        public int Cleaned { get; private set; }

        public bool FailAfterPublish { get; set; }

        public AkburaRenderState? ParentState { get; set; }

        public AkburaConditionalDeferredTemplate CreateMarker() => new((host, scope) => CreateCoreInstance("native", scope, host));

        private AkburaConditionalTemplateInstance CreateInstance(string data, INameScope nameScope, ContentPresenter host)
            => CreateCoreInstance(data, nameScope, host);

        private AkburaConditionalTemplateInstance CreateCoreInstance(string data, INameScope nameScope, Control host)
        {
            var selected = -1;
            Control? root = null;
            AkburaConditionalTemplateInstance instance = null!;
            instance = new(() =>
            {
                if (selected != Selected)
                {
                    selected = Selected;
                    root = selected switch
                    {
                        0 => new TextBlock { Text = data },
                        1 => new Button { Content = data },
                        _ => null,
                    };
                    if (root != null)
                    {
                        Constructed++;
                    }
                }

                instance.Root = root;
                if (FailAfterPublish)
                {
                    throw new InvalidOperationException("later render failure");
                }
            }, () => Cleaned++);
            if (ParentState != null)
            {
                instance.SetLifetime(new Lifetime(), ParentState);
            }
            Instances.Add(instance);
            return instance;
        }
    }

    private sealed class Lifetime : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class TemplateSource : INotifyPropertyChanged
    {
        private IDataTemplate? _template;

        public IDataTemplate? Template
        {
            get => _template;
            set
            {
                _template = value;
                PropertyChanged?.Invoke(this, new(nameof(Template)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
