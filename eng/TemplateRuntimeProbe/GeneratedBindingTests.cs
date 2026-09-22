using System.Windows.Input;
using System.Reflection;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TemplateRuntimeProbeApp.Services;
using TemplateRuntimeProbeApp.ViewModels;
using Xunit;

#if TEMPLATE_REACTIVE_UI
using ReactiveUI.Avalonia;
#endif

#if XPLAT_TEMPLATE
using TemplateRuntimeProbeApp.Infrastructure;
#endif

[assembly: AvaloniaTestApplication(typeof(Akbura.TemplateRuntimeProbe.ProbeAppBuilder))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace Akbura.TemplateRuntimeProbe;

public static class ProbeAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
#if XPLAT_TEMPLATE || TEMPLATE_STARTUP
        var builder = AppBuilder.Configure<TemplateRuntimeProbeApp.App>()
#else
        var builder = AppBuilder.Configure<Application>()
#endif
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());

#if XPLAT_TEMPLATE
        return builder.UseAkburaApplication();
#else
#if TEMPLATE_REACTIVE_UI
        builder = builder.UseReactiveUI(_ => { });
#endif
#if TEMPLATE_DI
        return builder.UseAkbura(akbura =>
            akbura.WithServiceProvider(GeneratedServices.Provider));
#else
        return builder.UseAkbura();
#endif
#endif
    }
}

#if XPLAT_TEMPLATE || TEMPLATE_STARTUP
internal static class GeneratedServices
{
    private static readonly Type ServicesType =
        typeof(MainViewModel).Assembly.GetType(
            "TemplateRuntimeProbeApp.Infrastructure.AppServices",
            throwOnError: true)!;

#if XPLAT_TEMPLATE
    private const BindingFlags MemberFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Root => ServicesType.GetProperty(
        "Current", BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static)!.GetValue(null)!;
#else
    private const BindingFlags MemberFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static object? Root => null;
#endif

    public static MainViewModel ViewModel => (MainViewModel)
        ServicesType.GetProperty("MainViewModel", MemberFlags)!.GetValue(Root)!;

#if TEMPLATE_DI
    public static IServiceProvider Provider => (IServiceProvider)
        ServicesType.GetProperty("ServiceProvider", MemberFlags)!.GetValue(Root)!;
#endif
}
#endif

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeadlessCollection
{
    public const string Name = "Generated template headless";
}

[Collection(HeadlessCollection.Name)]
public sealed class GeneratedBindingTests
{
    private static readonly Type MainViewType = typeof(MainViewModel).Assembly.GetType(
        "TemplateRuntimeProbeApp.Views.MainView", throwOnError: true)!;

    private static readonly Type GreetingCardType = typeof(MainViewModel).Assembly.GetType(
        "TemplateRuntimeProbeApp.Components.GreetingCard", throwOnError: true)!;

#if TEMPLATE_STARTUP
    [Fact]
    public async Task GeneratedApplicationStartsWithRegisteredServices()
    {
        await Dispatch(() =>
        {
            var app = Assert.IsType<TemplateRuntimeProbeApp.App>(
                Application.Current);
            Assert.NotEmpty(app.Styles);

            var viewModel = GeneratedServices.ViewModel;
#if TEMPLATE_DI
            var provider = GeneratedServices.Provider;
            Assert.Same(viewModel, provider.GetService(typeof(MainViewModel)));
            Assert.IsAssignableFrom<IGreetingService>(
                provider.GetService(typeof(IGreetingService)));
#endif
            Assert.Equal("Hello, Akbura!", viewModel.Greeting);

            var view = NewMainView(viewModel);
            var window = new Window { Content = view };
            try
            {
                window.Show();
                Drain();
                AssertGreeting(view, Assert.Single(
                    view.GetVisualDescendants().OfType<Control>(),
                    control => control.GetType() == GreetingCardType),
                    "Hello, Akbura!");
            }
            finally
            {
                window.Close();
            }
        });
    }
#endif

    [Fact]
    public async Task GeneratedViewBindsCommandsNameAndGreetingComponent()
    {
        await Dispatch(() =>
        {
            var viewModel = NewViewModel();
            var view = NewMainView(viewModel);
            var window = new Window { Content = view };

            try
            {
                window.Show();
                Drain();

                var textBlocks = view.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .ToArray();
                Assert.True(
                    textBlocks.Any(text => text.Text == "0"),
                    "Initial TextBlocks: " + string.Join(" | ",
                        textBlocks.Select(text => text.Text)));
                var count = Assert.Single(textBlocks,
                    text => text.Text == "0");
                var name = Assert.Single(view.GetVisualDescendants()
                    .OfType<TextBox>());
                var card = Assert.Single(
                    view.GetVisualDescendants().OfType<Control>(),
                    control => control.GetType() == GreetingCardType);
                var buttons = view.GetVisualDescendants()
                    .OfType<Button>()
                    .ToArray();
                var reset = Assert.Single(buttons, button =>
                    ReferenceEquals(button.Command,
                        viewModel.ResetCounterCommand));

                Assert.Equal("0", count.Text);
                Assert.Equal("Akbura", name.Text);
                Assert.False(reset.IsEffectivelyEnabled);
                AssertGreeting(view, card, "Hello, Akbura!");

                var resetCommandEvents = 0;
                EventHandler changed = (_, _) => resetCommandEvents++;
                ((ICommand)viewModel.ResetCounterCommand).CanExecuteChanged +=
                    changed;
                Execute(viewModel.IncrementCommand);
                Drain();
                ((ICommand)viewModel.ResetCounterCommand).CanExecuteChanged -=
                    changed;
                Assert.Equal(1, viewModel.Count);
                Assert.Equal("1", count.Text);
                Assert.True(
                    reset.IsEffectivelyEnabled,
                    $"Reset button IsEnabled={reset.IsEnabled}, logical attached={((ILogical)reset).IsAttachedToLogicalTree}, " +
                    $"observed command events={resetCommandEvents}, " +
                    $"command CanExecute={((ICommand)viewModel.ResetCounterCommand).CanExecute(null)}; " +
                    "ancestors=" + string.Join(" -> ",
                        reset.GetVisualAncestors()
                            .OfType<Control>()
                            .Select(control =>
                                $"{control.GetType().Name}:{control.IsEnabled}/{control.IsEffectivelyEnabled}")));

                Execute(viewModel.ResetCounterCommand);
                Drain();
                Assert.Equal(0, viewModel.Count);
                Assert.Equal("0", count.Text);
                Assert.False(reset.IsEffectivelyEnabled);

                name.Text = "Ada";
                Drain();
                Assert.Equal("Ada", viewModel.UserName);
                AssertGreeting(view, card, "Hello, Ada!");

                Execute(viewModel.UseExampleNameCommand);
                Drain();
                Assert.Equal("Developer", viewModel.UserName);
                Assert.Equal("Developer", name.Text);
                AssertGreeting(view, card, "Hello, Developer!");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task ReplacingDataContextDetachesOldBindingsAndReattachmentWorks()
    {
        await Dispatch(() =>
        {
            var previous = NewViewModel();
            previous.UserName = "Previous";
            var current = NewViewModel();
            current.UserName = "Current";

            var view = NewMainView(previous);
            var window = new Window { Content = view };

            try
            {
                window.Show();
                Drain();

                var card = Assert.Single(
                    view.GetVisualDescendants().OfType<Control>(),
                    control => control.GetType() == GreetingCardType);
                AssertGreeting(view, card, "Hello, Previous!");

                view.DataContext = current;
                Drain();
                AssertGreeting(view, card, "Hello, Current!");

                previous.UserName = "Stale";
                Drain();
                AssertGreeting(view, card, "Hello, Current!");

                window.Content = null;
                Drain();
                window.Content = view;
                Drain();

                current.UserName = "Reattached";
                Drain();
                Assert.Equal("Reattached", Assert.Single(view
                    .GetVisualDescendants().OfType<TextBox>()).Text);
                AssertGreeting(view, card, "Hello, Reattached!");
                Assert.Single(
                    view.GetVisualDescendants().OfType<Control>(),
                    control => control.GetType() == GreetingCardType);
            }
            finally
            {
                window.Close();
            }
        });
    }

#if XPLAT_TEMPLATE
    [Fact]
    public async Task SelectedPageTypeBuildsRealPageShells()
    {
        await Dispatch(() =>
        {
            var expectedPageType = typeof(GeneratedBindingTests).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Single(attribute =>
                    attribute.Key == "AkburaTemplatePageType")
                .Value;
            var viewModel = NewViewModel();
            var root = CreateHostWithDataContext(viewModel);

            if (expectedPageType == "None")
            {
                var userControl = Assert.IsType<UserControl>(root);
                var mainView = Assert.IsAssignableFrom<Control>(
                    userControl.Content);
                Assert.Equal(MainViewType, mainView.GetType());
                Assert.Same(root.DataContext, mainView.DataContext);
                return;
            }

            var host = Assert.IsType<PageNavigationHost>(root);
            Assert.NotNull(host.Page);

            switch (expectedPageType)
            {
                case "ContentPage":
                {
                    var page = Assert.IsType<ContentPage>(host.Page);
                    Assert.Equal("Home", page.Header);
                    Assert.Equal(MainViewType,
                        Assert.IsAssignableFrom<Control>(page.Content)
                            .GetType());
                    break;
                }

                case "TabbedPage":
                {
                    var tabs = Assert.IsType<TabbedPage>(host.Page);
                    Assert.NotNull(tabs.Pages);
                    var pages = tabs.Pages.ToArray();
                    Assert.Equal(2, pages.Length);
                    var home = Assert.IsType<ContentPage>(pages[0]);
                    var settings = Assert.IsType<ContentPage>(pages[1]);
                    Assert.Equal("Home", home.Header);
                    Assert.Equal("Settings", settings.Header);
                    Assert.Equal(MainViewType,
                        Assert.IsAssignableFrom<Control>(home.Content)
                            .GetType());
                    Assert.IsType<TextBlock>(settings.Content);
                    break;
                }

                case "DrawerPage":
                {
                    var drawer = Assert.IsType<DrawerPage>(host.Page);
                    var menu = Assert.IsType<ListBox>(drawer.Drawer);
                    var home = Assert.IsType<ContentPage>(drawer.Content);
                    Assert.Equal("Home", home.Header);
                    Assert.Equal(MainViewType,
                        Assert.IsAssignableFrom<Control>(home.Content)
                            .GetType());

                    menu.SelectedIndex = 1;
                    Drain();
                    var settings = Assert.IsType<ContentPage>(drawer.Content);
                    Assert.Equal("Settings", settings.Header);
                    Assert.IsType<TextBlock>(settings.Content);

                    menu.SelectedIndex = 0;
                    Drain();
                    var recreatedHome = Assert.IsType<ContentPage>(
                        drawer.Content);
                    Assert.Equal("Home", recreatedHome.Header);
                    Assert.Same(
                        viewModel,
                        Assert.IsAssignableFrom<Control>(recreatedHome.Content)
                            .DataContext);
                    break;
                }

                case "NavigationPage":
                {
                    var navigation = Assert.IsAssignableFrom<NavigationPage>(
                        host.Page);
                    // Headless Dispatch runs this assertion on the UI thread, so
                    // animated transitions cannot advance while it waits.
                    // Keep real PushAsync/PopAsync and stack behavior under test.
                    navigation.PageTransition = null;
                    var assembly = typeof(MainViewModel).Assembly;
                    var homeType = assembly.GetType(
                        "TemplateRuntimeProbeApp.Views.HomePage",
                        throwOnError: true)!;
                    var settingsType = assembly.GetType(
                        "TemplateRuntimeProbeApp.Views.SettingsPage",
                        throwOnError: true)!;
                    var home = Assert.IsAssignableFrom<ContentPage>(
                        Activator.CreateInstance(homeType, nonPublic: true));
                    var settings = Assert.IsAssignableFrom<ContentPage>(
                        Activator.CreateInstance(settingsType,
                            nonPublic: true));
                    Assert.Equal("Home", home.Header);
                    Assert.Equal("Settings", settings.Header);
                    var homeContent = Assert.IsType<StackPanel>(home.Content);
                    Assert.Contains(homeContent.Children,
                        child => child.GetType() == MainViewType);
                    Assert.Contains(homeContent.Children.OfType<Button>(),
                        button => button.Content?.ToString() ==
                            "Open Settings");
                    var settingsContent = Assert.IsType<StackPanel>(
                        settings.Content);
                    Assert.Contains(settingsContent.Children.OfType<Button>(),
                        button => button.Content?.ToString() ==
                            "Back to Home");

                    var window = new Window { Content = root };
                    try
                    {
                        window.Show();
                        WaitForPage(navigation, homeType);

                        var openSettings = Assert.Single(
                            Assert.IsType<StackPanel>(
                                Assert.IsAssignableFrom<ContentPage>(
                                    navigation.CurrentPage).Content)
                                .Children.OfType<Button>(),
                            button => button.Content?.ToString() ==
                                "Open Settings");
                        openSettings.RaiseEvent(new RoutedEventArgs(
                            Button.ClickEvent));
                        WaitForPage(navigation, settingsType);

                        var back = Assert.Single(
                            Assert.IsType<StackPanel>(
                                Assert.IsAssignableFrom<ContentPage>(
                                    navigation.CurrentPage).Content)
                                .Children.OfType<Button>(),
                            button => button.Content?.ToString() ==
                                "Back to Home");
                        back.RaiseEvent(new RoutedEventArgs(
                            Button.ClickEvent));
                        WaitForPage(navigation, homeType);
                    }
                    finally
                    {
                        window.Close();
                    }
                    break;
                }

                default:
                    throw new InvalidOperationException(
                        $"Unexpected page type '{expectedPageType}'.");
            }
        });
    }

    [Fact]
    public async Task SelectedPageTypePropagatesViewModelToAttachedMainView()
    {
        await Dispatch(() =>
        {
            var viewModel = NewViewModel();
            var root = CreateHostWithDataContext(viewModel);
            var window = new Window { Content = root };
            var navigation = (root as PageNavigationHost)?.Page as
                NavigationPage;
            if (navigation is not null)
            {
                navigation.PageTransition = null;
            }

            try
            {
                window.Show();

                if (navigation is not null)
                {
                    var homeType = typeof(MainViewModel).Assembly.GetType(
                        "TemplateRuntimeProbeApp.Views.HomePage",
                        throwOnError: true)!;
                    WaitForPage(navigation, homeType);
                }

                if ((root as PageNavigationHost)?.Page is DrawerPage drawer)
                {
                    var menu = Assert.IsType<ListBox>(drawer.Drawer);
                    menu.SelectedIndex = 1;
                    Drain();
                    menu.SelectedIndex = 0;
                }

                Drain();

                var mainView = Assert.Single(
                    root.GetVisualDescendants().OfType<Control>(),
                    control => control.GetType() == MainViewType);
                Assert.Same(viewModel, mainView.DataContext);

                var count = Assert.Single(mainView.GetVisualDescendants()
                    .OfType<TextBlock>(), text => text.Text == "0");
                var increment = Assert.Single(mainView
                    .GetVisualDescendants().OfType<Button>(), button =>
                        ReferenceEquals(
                            button.Command,
                            viewModel.IncrementCommand));

                Execute(Assert.IsAssignableFrom<ICommand>(increment.Command));
                Drain();

                Assert.Equal(1, viewModel.Count);
                Assert.Equal("1", count.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task MainViewHostCreatesFreshVisualTreesForOneViewModel()
    {
        await Dispatch(() =>
        {
            var viewModel = NewViewModel();
            var first = CreateHostWithDataContext(viewModel);
            var second = CreateHostWithDataContext(viewModel);

            Assert.NotSame(first, second);
            Assert.Same(viewModel, first.DataContext);
            Assert.Same(viewModel, second.DataContext);

            if (first is UserControl firstHost)
            {
                var secondHost = Assert.IsType<UserControl>(second);
                var firstView = Assert.IsAssignableFrom<Control>(
                    firstHost.Content);
                var secondView = Assert.IsAssignableFrom<Control>(
                    secondHost.Content);

                Assert.Equal(MainViewType, firstView.GetType());
                Assert.Equal(MainViewType, secondView.GetType());
                Assert.NotSame(firstView, secondView);
                Assert.Same(viewModel, firstView.DataContext);
                Assert.Same(viewModel, secondView.DataContext);
            }
        });
    }

    private static Control CreateHostWithDataContext(
        MainViewModel viewModel)
    {
        var hostType = typeof(MainViewModel).Assembly.GetType(
            "TemplateRuntimeProbeApp.Views.MainViewHost",
            throwOnError: true)!;
        var factory = hostType.GetMethod(
            "CreateWithDataContext",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(MainViewModel)],
            modifiers: null);
        Assert.NotNull(factory);

        return Assert.IsAssignableFrom<Control>(
            factory.Invoke(null, [viewModel]));
    }

    private static void WaitForPage(
        NavigationPage navigation,
        Type expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while ((navigation.CurrentPage?.GetType() != expected ||
                navigation.IsNavigating) &&
               DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Assert.Equal(expected, navigation.CurrentPage?.GetType());
        Assert.False(navigation.IsNavigating,
            $"Navigation to {expected.Name} did not finish before the timeout.");
    }
#endif

    private static MainViewModel NewViewModel() =>
        new(new GreetingService());

    private static Control NewMainView(MainViewModel viewModel)
    {
        var view = Assert.IsAssignableFrom<Control>(
            Activator.CreateInstance(MainViewType, nonPublic: true));
        view.DataContext = viewModel;
        return view;
    }

    private static void Execute(ICommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
    }

    private static void AssertGreeting(
        Control view,
        Control card,
        string expected)
    {
        Assert.Contains(view.GetVisualDescendants()
            .OfType<TextBlock>(), text => text.Text == expected);
        Assert.Contains(card.GetVisualDescendants()
            .OfType<TextBlock>(), text => text.Text == expected);
    }

    private static void Drain()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static Task Dispatch(Action action)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(
            typeof(GeneratedBindingTests).Assembly);
        return session.Dispatch(action, CancellationToken.None);
    }
}
