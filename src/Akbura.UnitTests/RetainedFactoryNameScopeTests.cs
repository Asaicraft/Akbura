using Akbura.HotReload;
using Akbura.Markup;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class RetainedFactoryNameScopeTests
{
    [Fact]
    public void SourceReplacementAndRegionOrdinalShift_UseCurrentScopeAndReleaseOldNames()
    {
        using var state = new AkburaRenderState();
        var fallback = new DeclaringServices();
        Begin(state, "initial");
        state.SelectConditionalBranch(0, 0);
        var (provider, retired) = InitializeBranch(state, 0, fallback, "retired");
        state.CompleteRevision();
        AssertCurrentName(provider, "retired");

        Begin(state, "inserted-earlier-region", shifted: true);
        state.SelectConditionalBranch(0, -1);
        state.SelectConditionalBranch(1, 0);
        var current = state.GetConditionalNameScope(1, 0, null);
        current.Register("current", new object());
        current.Complete();
        Assert.Same(current, provider.CurrentNameScope);
        state.CompleteRevision();

        Assert.Same(current, provider.GetService(typeof(INameScope)));
        Assert.Same(provider, provider.GetService(typeof(AkburaRetainedFactoryServiceProvider)));
        Assert.Same(fallback.Token, provider.GetService(typeof(ServiceToken)));
        AssertCurrentName(provider, "current");
        Assert.Null(provider.CurrentNameScope!.Find("retired"));
        Collect();
        Assert.False(retired.TryGetTarget(out _));
        GC.KeepAlive(provider);
        GC.KeepAlive(state);
    }

    [Fact]
    public void SourceAbort_RestoresCurrentLookupAndReleasesAbandonedNames()
    {
        using var state = new AkburaRenderState();
        Begin(state, "initial");
        state.SelectConditionalBranch(0, 0);
        var (provider, original) = InitializeBranch(state, 0, new DeclaringServices(), "original");
        state.CompleteRevision();

        Begin(state, "failed-source");
        state.SelectConditionalBranch(0, 0);
        var abandoned = InitializeReplacement(state, 0, "abandoned");
        AssertCurrentName(provider, "abandoned");
        state.PrepareRevisionCompletion();
        state.AbortRevision(new InvalidOperationException("a later initializer failed"));

        AssertCurrentName(provider, "original");
        Assert.Null(provider.CurrentNameScope!.Find("abandoned"));
        Collect();
        Assert.True(original.TryGetTarget(out _));
        Assert.False(abandoned.TryGetTarget(out _));
        GC.KeepAlive(provider);
        GC.KeepAlive(state);
    }

    [Fact]
    public void SourceOmission_DoesNotResurrectFallbackScopeOrRetainRemovedBaseNames()
    {
        using var state = new AkburaRenderState();
        var fallback = new DeclaringServices();
        Begin(state, "initial");
        var (provider, retired) = InitializeBase(state, fallback);
        state.CompleteRevision();
        AssertCurrentName(provider, "retired");

        Begin(state, "without-base-scope");
        state.CompleteRevision();

        Assert.Null(provider.CurrentNameScope);
        Assert.Null(provider.GetService(typeof(INameScope)));
        Assert.NotNull(fallback.GetService(typeof(INameScope)));
        Collect();
        Assert.False(retired.TryGetTarget(out _));
        GC.KeepAlive(provider);
        GC.KeepAlive(state);
    }

    [Fact]
    public async Task ConditionalDeferred_UsesCurrentMarkerAndNativeAttachedScopeWithoutSdkParentSnapshot()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            using var state = new AkburaRenderState();
            var fallback = new DeclaringServices();
            Begin(state, "initial");
            state.SelectConditionalBranch(0, 0);
            var (provider, retired) = InitializeBranch(state, 0, fallback, "retired");
            state.CompleteRevision();

            IServiceProvider? capturedSdkServices = null;
            var constructed = 0;
            var expectedToken = fallback.Token;
            var content = new AkburaConditionalDeferredContent<Border>(services =>
            {
                capturedSdkServices = services;
                var marker = Assert.IsType<AkburaRetainedFactoryServiceProvider>(
                    services.GetService(typeof(AkburaRetainedFactoryServiceProvider)));
                Assert.Same(provider, marker);
                Assert.Same(expectedToken, services.GetService(typeof(ServiceToken)));
                Assert.Same(fallback.RootObject, Assert.IsAssignableFrom<IRootObjectProvider>(
                    services.GetService(typeof(IRootObjectProvider))).RootObject);
                var sdkScope = Assert.IsAssignableFrom<INameScope>(services.GetService(typeof(INameScope)));
                Assert.NotSame(marker.CurrentNameScope, sdkScope);

                var root = new Border();
                constructed++;
                var rootScope = new NameScope();
                rootScope.Register("PART_Root", root);
                rootScope.Complete();
                NameScope.SetNameScope(root, rootScope);
                return root;
            }, provider);

            Assert.Equal(0, constructed);
            var first = Assert.IsType<TemplateResult<Border>>(content.Build(null));
            Assert.Same(first.Result, first.NameScope.Find("PART_Root"));
            Assert.Same(NameScope.GetNameScope(first.Result), first.NameScope);
            Assert.Equal(1, constructed);

            Begin(state, "changed-source");
            state.SelectConditionalBranch(0, 0);
            InitializeReplacement(state, 0, "current");
            AssertSdkMarkerName(capturedSdkServices!, "current");
            state.AbortRevision();
            AssertSdkMarkerName(capturedSdkServices!, "retired");

            Begin(state, "committed-source");
            state.SelectConditionalBranch(0, 0);
            InitializeReplacement(state, 0, "current");
            state.CompleteRevision();
            AssertSdkMarkerName(capturedSdkServices!, "current");
            Assert.Null(Assert.IsAssignableFrom<INameScope>(
                capturedSdkServices!.GetService(typeof(INameScope))).Find("retired"));
            Collect();
            Assert.False(retired.TryGetTarget(out _));

            var buildServices = new DeclaringServices();
            expectedToken = buildServices.Token;
            var second = Assert.IsType<TemplateResult<Border>>(content.Build(buildServices));
            Assert.NotSame(first.Result, second.Result);
            Assert.Same(second.Result, second.NameScope.Find("PART_Root"));
            Assert.Same(NameScope.GetNameScope(second.Result), second.NameScope);
            Assert.Equal(2, constructed);
            GC.KeepAlive(content);
            GC.KeepAlive(capturedSdkServices);
            GC.KeepAlive(provider);
            GC.KeepAlive(state);
        }, CancellationToken.None);
    }

    private static void Begin(AkburaRenderState state, string revision, bool shifted = false)
    {
        state.BeginRevision(revision, builder =>
        {
            builder.Add(new(0, -1, "$root", typeof(object), null, "root"));
            if (shifted)
            {
                builder.AddConditional(new(0, 0, "Other", "inserted-if", [new("OtherEnabled", "other")]));
            }
            builder.AddConditional(new(shifted ? 1 : 0, 0, "Template", "stable-if", [new("Enabled", "branch")]));
        }, _ => new object());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (AkburaRetainedFactoryServiceProvider Provider, WeakReference<object> Payload) InitializeBranch(
        AkburaRenderState state, int regionId, IServiceProvider fallback, string name)
    {
        var scope = state.GetConditionalNameScope(regionId, 0, null);
        var payload = new object();
        scope.Register(name, payload);
        scope.Complete();
        return (state.CreateRetainedFactoryServiceProvider(scope, fallback), new(payload));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<object> InitializeReplacement(AkburaRenderState state, int regionId, string name)
    {
        var scope = state.GetConditionalNameScope(regionId, 0, null);
        var payload = new object();
        scope.Register(name, payload);
        scope.Complete();
        return new(payload);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (AkburaRetainedFactoryServiceProvider Provider, WeakReference<object> Payload) InitializeBase(
        AkburaRenderState state, IServiceProvider fallback)
    {
        var scope = state.GetLocalNameScope(null);
        var payload = new object();
        scope.Register("retired", payload);
        scope.Complete();
        return (state.CreateRetainedFactoryServiceProvider(scope, fallback), new(payload));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertCurrentName(AkburaRetainedFactoryServiceProvider provider, string name)
    {
        var scope = provider.CurrentNameScope;
        Assert.NotNull(scope);
        Assert.NotNull(scope.Find(name));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertSdkMarkerName(IServiceProvider services, string name)
    {
        var marker = Assert.IsType<AkburaRetainedFactoryServiceProvider>(
            services.GetService(typeof(AkburaRetainedFactoryServiceProvider)));
        AssertCurrentName(marker, name);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class ServiceToken;

    private sealed class DeclaringServices : IServiceProvider, IRootObjectProvider, IAvaloniaXamlIlEagerParentStackProvider
    {
        private readonly object _root = new();
        private readonly NameScope _fallbackScope = new();

        public ServiceToken Token { get; } = new();

        public object RootObject => _root;

        public object IntermediateRootObject => _root;

        public IReadOnlyList<object> DirectParentsStack => [];

        public IAvaloniaXamlIlEagerParentStackProvider? ParentProvider => null;

        public IEnumerable<object> Parents => [];

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IRootObjectProvider) || serviceType == typeof(IAvaloniaXamlIlParentStackProvider))
            {
                return this;
            }
            if (serviceType == typeof(INameScope))
            {
                return _fallbackScope;
            }
            return serviceType == typeof(ServiceToken) ? Token : null;
        }
    }
}
