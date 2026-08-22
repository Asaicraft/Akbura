using Akbura.Markup;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Styling;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class BuiltInUtilityVariantTests
{
    [Theory]
    [InlineData(typeof(smExtension))]
    [InlineData(typeof(mdExtension))]
    [InlineData(typeof(lgExtension))]
    [InlineData(typeof(xlExtension))]
    [InlineData(typeof(xxlExtension))]
    public void BreakpointVariants_InheritStyleTriggerBindingPriority(
        Type extensionType)
    {
        var bindingPriority = Assert.Single(
            extensionType.GetCustomAttributes<UtilityBindingPriorityAttribute>(
                inherit: true));

        Assert.Equal(BindingPriority.StyleTrigger, bindingPriority.Priority);
        Assert.Null(bindingPriority.PriorityMember);
    }

    [Theory]
    [InlineData(
        typeof(hoverExtension),
        10d,
        "Akbura.Tailwind.Interaction")]
    [InlineData(
        typeof(focusExtension),
        20d,
        "Akbura.Tailwind.Interaction")]
    [InlineData(
        typeof(lightExtension),
        10d,
        "Akbura.Tailwind.ColorScheme")]
    [InlineData(
        typeof(darkExtension),
        20d,
        "Akbura.Tailwind.ColorScheme")]
    public void BuiltInUtilityVariants_ExposeExpectedMetadata(
        Type extensionType,
        double order,
        string conflictGroup)
    {
        var variant = Assert.Single(
            extensionType.GetCustomAttributes<UtilityVariantAttribute>(
                inherit: false));

        Assert.Equal(order, variant.Order);
        Assert.Equal(conflictGroup, variant.ConflictGroup);
        Assert.Equal(
            UnprefixedUtilityPrecedence.Above,
            variant.UnprefixedPrecedence);

        var bindingPriority = Assert.Single(
            extensionType.GetCustomAttributes<UtilityBindingPriorityAttribute>(
                inherit: true));

        Assert.Equal(BindingPriority.StyleTrigger, bindingPriority.Priority);
        Assert.Null(bindingPriority.PriorityMember);
    }

    [Fact]
    public async Task InteractionVariants_ResolveHoverAndObserveFocusState()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var first = new Button();
                var second = new Button();
                var window = new Window
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            first,
                            second,
                        },
                    },
                };

                window.Show();

                try
                {
                    var serviceProvider = CreateServiceProvider(first);
                    var hover = Assert.IsAssignableFrom<IObservable<bool>>(
                        new hoverExtension().ProvideValue(serviceProvider));
                    var focus = Assert.IsAssignableFrom<IObservable<bool>>(
                        new focusExtension().ProvideValue(serviceProvider));
                    var hoverObserver = new RecordingObserver<bool>();
                    var focusObserver = new RecordingObserver<bool>();

                    using var hoverSubscription = hover.Subscribe(hoverObserver);
                    using var focusSubscription = focus.Subscribe(focusObserver);

                    Assert.Equal(new[] { false }, hoverObserver.Values);
                    Assert.Equal(new[] { false }, focusObserver.Values);

                    Assert.True(first.Focus());
                    Assert.Equal(
                        new[] { false, true },
                        focusObserver.Values);

                    Assert.True(second.Focus());
                    Assert.Equal(
                        new[] { false, true, false },
                        focusObserver.Values);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task ColorSchemeVariants_ObserveEffectiveThemeChanges()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var target = new Border();
                var themeScope = new ThemeVariantScope
                {
                    RequestedThemeVariant = ThemeVariant.Dark,
                    Child = target,
                };
                var window = new Window { Content = themeScope };

                window.Show();

                try
                {
                    var serviceProvider = CreateServiceProvider(target);
                    var dark = Assert.IsAssignableFrom<IObservable<bool>>(
                        new darkExtension().ProvideValue(serviceProvider));
                    var light = Assert.IsAssignableFrom<IObservable<bool>>(
                        new lightExtension().ProvideValue(serviceProvider));
                    var darkObserver = new RecordingObserver<bool>();
                    var lightObserver = new RecordingObserver<bool>();

                    using var darkSubscription = dark.Subscribe(darkObserver);
                    using var lightSubscription = light.Subscribe(lightObserver);

                    Assert.Equal(new[] { true }, darkObserver.Values);
                    Assert.Equal(new[] { false }, lightObserver.Values);

                    themeScope.RequestedThemeVariant = ThemeVariant.Light;

                    Assert.Equal(
                        new[] { true, false },
                        darkObserver.Values);
                    Assert.Equal(
                        new[] { false, true },
                        lightObserver.Values);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static AkburaMarkupServiceProvider CreateServiceProvider(
        StyledElement target)
    {
        return new AkburaMarkupServiceProvider(
            target,
            StyledElement.DataContextProperty,
            target,
            target,
            new Uri("avares://Akbura.UnitTests/"),
            [target]);
    }

    private sealed class RecordingObserver<T> : IObserver<T>
    {
        public List<T> Values { get; } = [];

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            throw error;
        }

        public void OnNext(T value)
        {
            Values.Add(value);
        }
    }
}
