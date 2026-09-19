using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ResourceHooksTests
{
    [Fact]
    public Task StaticResource_ReusesResolvedValueAcrossUpdates() => OnDispatcher(() =>
    {
        var owner = new ResourceComponent();
        var original = new SolidColorBrush(Colors.Red);
        owner.Resources["color"] = original;
        State<IBrush?>? value = null;
        owner.RenderFrame = self => value = self.useStaticResource<IBrush?>("color");
        owner.InitializeForTest();
        var state = value;
        Assert.Same(original, value!.Value);
        owner.Resources["color"] = new SolidColorBrush(Colors.Blue);
        for (var i = 0; i < 10; i++) owner.RenderAgain();
        Assert.Same(state, value);
        Assert.Same(original, value!.Value);
    });

    [Fact]
    public Task StaticResource_ChangingKeyRefreshesTheValue() => OnDispatcher(() =>
    {
        var owner = new ResourceComponent();
        owner.Resources["a"] = 10;
        owner.Resources["b"] = 20;
        var key = "a";
        State<int>? value = null;
        owner.RenderFrame = self => value = self.useStaticResource(key, -1);
        owner.InitializeForTest();
        Assert.Equal(10, value!.Value);
        key = "b";
        owner.RenderAgain();
        Assert.Equal(20, value.Value);
    });

    [Fact]
    public Task StaticResource_FirstUnrootedMissResolvesOnAttachment() => OnDispatcher(() =>
    {
        var host = new Border();
        var owner = new ResourceComponent();
        State<int>? value = null;
        owner.RenderFrame = self => value = self.useStaticResource(host, "answer", -1);
        owner.InitializeForTest();
        Assert.Equal(-1, value!.Value);
        var window = new Window();
        window.Resources["answer"] = 42;
        try
        {
            window.Content = host;
            window.Show();
            Assert.Equal(42, value.Value);
            window.Resources["answer"] = 99;
            owner.RenderAgain();
            Assert.Equal(42, value.Value);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DynamicResource_UpdatesAndFallsBackOnRemovalOrWrongType() => OnDispatcher(() =>
    {
        var owner = new ResourceComponent();
        owner.Resources["value"] = 1;
        State<int>? value = null;
        owner.RenderFrame = self => value = self.useDynamicResource("value", -1);
        owner.InitializeForTest();
        var state = value;
        Assert.Equal(1, value!.Value);
        owner.Resources["value"] = 2;
        Assert.Equal(2, value.Value);
        owner.Resources["value"] = "not an integer";
        Assert.Equal(-1, value.Value);
        owner.Resources.Remove("value");
        Assert.Equal(-1, value.Value);
        owner.Resources["value"] = 3;
        Assert.Equal(3, value.Value);
        Assert.Same(state, value);
    });

    [Fact]
    public Task DynamicResource_ChangesHostWithoutKeepingOldSubscription() => OnDispatcher(() =>
    {
        var a = new Border();
        var b = new Border();
        a.Resources["x"] = 1;
        b.Resources["x"] = 2;
        var host = a;
        var owner = new ResourceComponent();
        State<int>? value = null;
        owner.RenderFrame = self => value = self.useDynamicResource(host, "x", -1);
        owner.InitializeForTest();
        Assert.Equal(1, value!.Value);
        host = b;
        owner.RenderAgain();
        Assert.Equal(2, value.Value);
        a.Resources["x"] = 99;
        Assert.Equal(2, value.Value);
        b.Resources["x"] = 3;
        Assert.Equal(3, value.Value);
    });

    [Fact]
    public Task DynamicResource_AbortedRenderDoesNotSwitchHost() => OnDispatcher(() =>
    {
        var a = new Border();
        var b = new Border();
        a.Resources["x"] = 1;
        b.Resources["x"] = 2;
        var host = a;
        var owner = new ResourceComponent();
        State<int>? value = null;
        owner.RenderFrame = self => value = self.useDynamicResource(host, "x", -1);
        owner.InitializeForTest();
        host = b;
        owner.FailRender = true;
        Assert.Throws<ExpectedRenderException>(owner.RenderAgain);
        owner.FailRender = false;
        host = a;
        b.Resources["x"] = 55;
        Assert.Equal(1, value!.Value);
        a.Resources["x"] = 7;
        Assert.Equal(7, value.Value);
    });

    [Fact]
    public Task DynamicResource_FollowsThemeAndStaticResourceDoesNot() => OnDispatcher(() =>
    {
        var owner = new ResourceComponent();
        State<int>? dynamicValue = null;
        State<int>? staticValue = null;
        owner.RenderFrame = self =>
        {
            dynamicValue = self.useDynamicResource("themed", -1);
            staticValue = self.useStaticResource("themed", -1);
        };
        var window = new Window { RequestedThemeVariant = ThemeVariant.Light };
        window.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary { ["themed"] = 1 };
        window.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary { ["themed"] = 2 };
        try
        {
            window.Content = owner;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, dynamicValue!.Value);
            Assert.Equal(1, staticValue!.Value);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, dynamicValue.Value);
            Assert.Equal(1, staticValue.Value);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task DynamicResource_DetachStopsDeliveryAndReattachRefreshes() => OnDispatcher(() =>
    {
        var host = new Border();
        host.Resources["x"] = 1;
        var owner = new ResourceComponent();
        State<int>? value = null;
        owner.RenderFrame = self => value = self.useDynamicResource(host, "x", -1);
        var window = new Window { Content = owner };
        try
        {
            window.Show();
            Assert.Equal(1, value!.Value);
            var original = value;
            window.Content = null;
            host.Resources["x"] = 9;
            Assert.Equal(1, value.Value);
            window.Content = owner;
            Assert.Same(original, value);
            Assert.Equal(9, value.Value);
        }
        finally { window.Close(); }
    });

    private static async Task OnDispatcher(Action test)
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            test();
            return true;
        }, CancellationToken.None);
    }

    private sealed class ExpectedRenderException : Exception;

    private sealed class ResourceComponent() : AkburaControl(AkburaEngine.Empty)
    {
        private readonly Border _root = new();
        public Action<ResourceComponent>? RenderFrame { get; set; }
        public bool FailRender { get; set; }
        public void InitializeForTest() => base.OnInitialized();
        public void RenderAgain() => InvalidState();
        protected override Control FirstUpdate() => _root;
        protected override Control Update()
        {
            RenderFrame?.Invoke(this);
            if (FailRender) throw new ExpectedRenderException();
            return _root;
        }
        protected override ImmutableArray<Parameter> GetParameters() => [];
        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];
        protected override ImmutableArray<InjectService> GetServices() => [];
        protected override ImmutableArray<State> GetStates() => [];
    }
}
