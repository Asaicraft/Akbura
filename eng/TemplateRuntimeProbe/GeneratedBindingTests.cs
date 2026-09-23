using System.Windows.Input;
using System.Reflection;
using Akbura;
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

internal static class GeneratedAssemblies
{
    public static Assembly Ui { get; } = typeof(TemplateRuntimeProbeApp.App).Assembly;

    public static Assembly ViewModels { get; } = typeof(MainViewModel).Assembly;
}

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
        GeneratedAssemblies.Ui.GetType(
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
    [Fact]
    public void UiAndViewModelTypesAreOwnedByDifferentAssemblies()
    {
        Assert.NotSame(GeneratedAssemblies.Ui, GeneratedAssemblies.ViewModels);
        Assert.Equal(GeneratedAssemblies.ViewModels, typeof(MainViewModel).Assembly);
        Assert.Equal(GeneratedAssemblies.Ui, MainViewType.Assembly);
        Assert.Equal(GeneratedAssemblies.Ui, GreetingCardType.Assembly);
    }

    private static readonly Type MainViewType = GeneratedAssemblies.Ui.GetType(
        "TemplateRuntimeProbeApp.Views.MainView", throwOnError: true)!;

    private static readonly Type GreetingCardType = GeneratedAssemblies.Ui.GetType(
        "TemplateRuntimeProbeApp.Components.GreetingCard", throwOnError: true)!;

#if XPLAT_TEMPLATE
    private static readonly Type AppShellType = GeneratedAssemblies.Ui.GetType(
        "TemplateRuntimeProbeApp.Views.AppShell", throwOnError: true)!;

    private static readonly PropertyInfo AppShellVmProperty = AppShellType.GetProperty(
        "Vm", BindingFlags.Public | BindingFlags.Instance)!;
#endif

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
                Assert.Contains(textBlocks,
                    text => text.Text == viewModel.GeneratedStatus);
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
            var expectedPageType = GetExpectedPageType();
            var viewModel = NewViewModel();
            var shell = CreateAppShell(viewModel);
            var window = new Window { Content = shell };

            try
            {
                window.Show();
                var root = GetInitializedRoot(shell);

                Assert.Same(viewModel, AppShellVmProperty.GetValue(shell));
                Assert.Same(viewModel, shell.DataContext);
#if TEMPLATE_DI
                Assert.NotSame(GeneratedServices.ViewModel, viewModel);
#endif

                if (expectedPageType == "None")
                {
                    var userControl = Assert.IsType<UserControl>(root);
                    var mainView = Assert.IsAssignableFrom<Control>(
                        userControl.Content);
                    Assert.Equal(MainViewType, mainView.GetType());
                    Assert.Same(viewModel, mainView.DataContext);
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
                        var mainView = Assert.IsAssignableFrom<Control>(page.Content);
                        Assert.Equal(MainViewType, mainView.GetType());
                        Assert.Same(viewModel, mainView.DataContext);
                        break;
                    }

                    case "TabbedPage":
                    {
                        var tabs = Assert.IsType<TabbedPage>(host.Page);
                        var pages = Assert.IsType<
                            Avalonia.Collections.AvaloniaList<Page>>(tabs.Pages);
                        Assert.Equal(2, pages.Count);
                        var home = Assert.IsType<ContentPage>(pages[0]);
                        var settings = Assert.IsType<ContentPage>(pages[1]);
                        Assert.Equal("Home", home.Header);
                        Assert.Equal("Settings", settings.Header);
                        var mainView = Assert.IsAssignableFrom<Control>(home.Content);
                        Assert.Equal(MainViewType, mainView.GetType());
                        Assert.Same(viewModel, mainView.DataContext);
                        var settingsText = Assert.IsType<TextBlock>(settings.Content);
                        Assert.Equal(
                            "Settings for your Akbura application",
                            settingsText.Text);
                        Assert.Equal(new Thickness(24), settingsText.Margin);
                        break;
                    }

                    case "DrawerPage":
                    {
                        var drawer = Assert.IsType<DrawerPage>(host.Page);
                        Assert.Equal("Akbura", drawer.Header);
                        var menu = Assert.IsType<ListBox>(drawer.Drawer);
                        Assert.Equal(0, menu.SelectedIndex);
                        Assert.Equal(
                            ["Home", "Settings"],
                            Assert.IsAssignableFrom<IEnumerable<string>>(
                                menu.ItemsSource).ToArray());
                        var home = Assert.IsType<ContentPage>(drawer.Content);
                        Assert.Equal("Home", home.Header);
                        Assert.Same(
                            viewModel,
                            Assert.IsAssignableFrom<Control>(home.Content)
                                .DataContext);

                        menu.SelectedIndex = -1;
                        Drain();
                        Assert.Same(home, drawer.Content);

                        drawer.IsOpen = true;
                        menu.SelectedIndex = 1;
                        Drain();
                        var settings = Assert.IsType<ContentPage>(drawer.Content);
                        Assert.Equal("Settings", settings.Header);
                        Assert.False(drawer.IsOpen);
                        var settingsText = Assert.IsType<TextBlock>(
                            settings.Content);
                        Assert.Equal(new Thickness(24), settingsText.Margin);

                        menu.SelectedIndex = 0;
                        Drain();
                        var returnedHome = Assert.IsType<ContentPage>(
                            drawer.Content);
                        Assert.Equal("Home", returnedHome.Header);
                        Assert.Same(
                            viewModel,
                            Assert.IsAssignableFrom<Control>(returnedHome.Content)
                                .DataContext);
                        break;
                    }

                    case "NavigationPage":
                    {
                        Assert.True(host.Resources.ContainsKey(
                            "SettingsPageTemplate"));
                        Assert.IsType<Avalonia.Markup.Xaml.Templates.DataTemplate>(
                            host.Resources["SettingsPageTemplate"]);
                        var navigation = Assert.IsType<NavigationPage>(host.Page);
                        navigation.PageTransition = null;
                        var home = WaitForPage(navigation, "Home");
                        Assert.Equal(1, navigation.StackDepth);
                        var homeContent = Assert.IsType<StackPanel>(home.Content);
                        Assert.Contains(
                            homeContent.Children,
                            child => child.GetType() == MainViewType);
                        var openSettings = Assert.Single(
                            homeContent.Children.OfType<Button>(),
                            button => button.Content?.ToString() ==
                                "Open Settings");
                        Assert.Equal(new Thickness(16), openSettings.Margin);

                        openSettings.RaiseEvent(new RoutedEventArgs(
                            Button.ClickEvent));
                        var firstSettings = WaitForPage(
                            navigation,
                            "Settings");
                        Assert.Equal(2, navigation.StackDepth);
                        var settingsContent = Assert.IsType<StackPanel>(
                            firstSettings.Content);
                        Assert.Equal(new Thickness(24), settingsContent.Margin);
                        Assert.Equal(12, settingsContent.Spacing);
                        var back = Assert.Single(
                            settingsContent.Children.OfType<Button>(),
                            button => button.Content?.ToString() ==
                                "Back to Home");

                        back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.Same(home, WaitForPage(navigation, "Home"));
                        Assert.Equal(1, navigation.StackDepth);

                        openSettings.RaiseEvent(new RoutedEventArgs(
                            Button.ClickEvent));
                        var secondSettings = WaitForPage(
                            navigation,
                            "Settings");
                        Assert.NotSame(firstSettings, secondSettings);
                        Assert.Equal(2, navigation.StackDepth);
                        break;
                    }

                    default:
                        throw new InvalidOperationException(
                            $"Unexpected page type '{expectedPageType}'.");
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task SelectedPageTypePropagatesViewModelToAttachedMainView()
    {
        await Dispatch(() =>
        {
            var viewModel = NewViewModel();
            var shell = CreateAppShell(viewModel);
            var window = new Window { Content = shell };

            try
            {
                window.Show();
                var root = GetInitializedRoot(shell);
                var navigation = (root as PageNavigationHost)?.Page as
                    NavigationPage;
                if (navigation is not null)
                {
                    navigation.PageTransition = null;
                    WaitForPage(navigation, "Home");
                }

                if ((root as PageNavigationHost)?.Page is DrawerPage drawer)
                {
                    var menu = Assert.IsType<ListBox>(drawer.Drawer);
                    menu.SelectedIndex = 1;
                    Drain();
                    menu.SelectedIndex = 0;
                    Drain();
                }

                var mainView = FindMainView(shell);
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
    public async Task AppShellCreatesFreshInitializedTreesForOneViewModel()
    {
        await Dispatch(() =>
        {
            var viewModel = NewViewModel();
            var first = CreateAppShell(viewModel);
            var second = CreateAppShell(viewModel);
            var firstWindow = new Window { Content = first };
            var secondWindow = new Window { Content = second };

            try
            {
                firstWindow.Show();
                secondWindow.Show();
                var firstRoot = GetInitializedRoot(first);
                var secondRoot = GetInitializedRoot(second);
                var firstView = FindMainView(first);
                var secondView = FindMainView(second);

                Assert.NotSame(first, second);
                Assert.NotSame(firstRoot, secondRoot);
                Assert.NotSame(firstView, secondView);
                Assert.Same(viewModel, first.DataContext);
                Assert.Same(viewModel, second.DataContext);
                Assert.Same(viewModel, firstView.DataContext);
                Assert.Same(viewModel, secondView.DataContext);

                if (firstRoot is PageNavigationHost firstHost)
                {
                    var secondHost = Assert.IsType<PageNavigationHost>(secondRoot);
                    Assert.NotSame(firstHost.Page, secondHost.Page);

                    if (firstHost.Page is TabbedPage firstTabs)
                    {
                        var secondTabs = Assert.IsType<TabbedPage>(secondHost.Page);
                        Assert.NotSame(firstTabs.Pages, secondTabs.Pages);
                        var firstPages = firstTabs.Pages!.ToArray();
                        var secondPages = secondTabs.Pages!.ToArray();
                        Assert.Equal(2, firstPages.Length);
                        Assert.Equal(2, secondPages.Length);
                        Assert.NotSame(firstPages[0], secondPages[0]);
                        Assert.NotSame(firstPages[1], secondPages[1]);
                    }
                }
                else
                {
                    var firstUserControl = Assert.IsType<UserControl>(firstRoot);
                    var secondUserControl = Assert.IsType<UserControl>(secondRoot);
                    Assert.NotSame(firstUserControl.Content, secondUserControl.Content);
                }
            }
            finally
            {
                firstWindow.Close();
                secondWindow.Close();
            }
        });
    }

    [Fact]
    public async Task TabbedShellUpdateAndReattachmentPreserveStaticPagesAndSelection()
    {
        if (GetExpectedPageType() != "TabbedPage")
        {
            return;
        }

        await Dispatch(() =>
        {
            var previous = NewViewModel();
            var current = NewViewModel();
            var shell = CreateAppShell(previous);
            var window = new Window { Content = shell };

            try
            {
                window.Show();
                var host = Assert.IsType<PageNavigationHost>(
                    GetInitializedRoot(shell));
                var tabs = Assert.IsType<TabbedPage>(host.Page);
                var pages = Assert.IsType<
                    Avalonia.Collections.AvaloniaList<Page>>(tabs.Pages);
                var home = Assert.IsType<ContentPage>(pages[0]);
                var mainView = Assert.IsAssignableFrom<Control>(home.Content);
                tabs.SelectedIndex = 1;
                Drain();

                AppShellVmProperty.SetValue(shell, current);
                shell.DataContext = current;
                Drain();

                Assert.Same(host, shell.Child);
                Assert.Same(tabs, host.Page);
                Assert.Same(pages, tabs.Pages);
                Assert.Equal(2, pages.Count);
                Assert.Equal(1, tabs.SelectedIndex);
                Assert.Same(mainView, home.Content);
                Assert.Same(current, mainView.DataContext);

                window.Content = null;
                Drain();
                window.Content = shell;
                Drain();

                Assert.Same(host, shell.Child);
                Assert.Same(pages, tabs.Pages);
                Assert.Equal(2, pages.Count);
                Assert.Equal(1, tabs.SelectedIndex);
            }
            finally
            {
                window.Close();
            }
        });
    }

#if TEMPLATE_DI
    [Fact]
    public async Task ProviderOnlyAppShellUsesRegisteredViewModel()
    {
        await Dispatch(() =>
        {
            var shell = CreateAppShellWithoutViewModel();
            var window = new Window { Content = shell };

            try
            {
                window.Show();
                GetInitializedRoot(shell);
                Assert.Same(
                    GeneratedServices.ViewModel,
                    AppShellVmProperty.GetValue(shell));
            }
            finally
            {
                window.Close();
            }
        });
    }
#else
    [Fact]
    public async Task AppShellWithoutViewModelPreservesRequiredServiceError()
    {
        await Dispatch(() =>
        {
            var shell = CreateAppShellWithoutViewModel();
            var window = new Window { Content = shell };

            try
            {
                var exception = Assert.Throws<AkburaServiceNotFoundException>(
                    window.Show);
                Assert.Contains(
                    nameof(MainViewModel),
                    exception.Message,
                    StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }
#endif

    private static string GetExpectedPageType()
    {
        return typeof(GeneratedBindingTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute =>
                attribute.Key == "AkburaTemplatePageType")
            .Value!;
    }

    private static AkburaControl CreateAppShell(MainViewModel viewModel)
    {
        var shell = CreateAppShellWithoutViewModel();
        AppShellVmProperty.SetValue(shell, viewModel);
        shell.DataContext = viewModel;
        return shell;
    }

    private static AkburaControl CreateAppShellWithoutViewModel()
    {
        return Assert.IsAssignableFrom<AkburaControl>(
            Activator.CreateInstance(AppShellType, nonPublic: true));
    }

    private static Control GetInitializedRoot(AkburaControl shell)
    {
        Drain();
        return Assert.IsAssignableFrom<Control>(shell.Child);
    }

    private static Control FindMainView(AkburaControl shell)
    {
        return Assert.Single(
            shell.GetVisualDescendants().OfType<Control>(),
            control => control.GetType() == MainViewType);
    }

    private static ContentPage WaitForPage(
        NavigationPage navigation,
        string header)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (((navigation.CurrentPage as ContentPage)?.Header?.ToString() !=
                    header || navigation.IsNavigating) &&
               DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        var page = Assert.IsType<ContentPage>(navigation.CurrentPage);
        Assert.Equal(header, page.Header);
        Assert.False(
            navigation.IsNavigating,
            $"Navigation to {header} did not finish before the timeout.");
        return page;
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
