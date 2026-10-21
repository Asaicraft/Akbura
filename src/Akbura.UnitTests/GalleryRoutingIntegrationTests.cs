using Akbura.Engine;
using Akbura.FeatureGallery;
using Akbura.FeatureGallery.Routing;
using Akbura.FeatureGallery.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class GalleryRoutingIntegrationTests
{
    [Fact]
    public async Task RealGalleryObservesServiceLinksAndNotFoundWithoutManualUpdates()
    {
        // The gallery has wrapped prose and code editors. Exercise their real
        // text shaping: the headless dummy font can loop while wrapping glyphs.
        // Keep this isolated from the existing lightweight unit-test app.
        using var session = HeadlessUnitTestSession.StartNew(typeof(GalleryRoutingTestAppBuilder));
        await session.Dispatch(() =>
        {
            Trace("Configure services and styles");
            var service = new MockRouter { Url = "/commands" };
            var builder = new AkburaEngineExtensions.AkburaEngineBuilder();
            builder.WithGalleryOptions();
            builder.WithGalleryRouter(service);
            // This is the actual gallery startup contract, not a replacement
            // component or a reflection-based generated-field fixture.
            AkburaEngine.Singletone = builder.Build();
            var app = Application.Current!;
            app.Styles.Add(new FluentTheme());
            app.Styles.Add(new StyleInclude(new Uri("avares://Akbura.FeatureGallery/"))
            {
                Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml"),
            });
            app.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Akbura.FeatureGallery/"))
            {
                Source = new Uri("avares://Akbura/Styles.axaml"),
            });
            Trace("Create actual MainView");
            var view = new MainView();
            Trace("MainView created");
            var window = new Window { Width = 1280, Height = 900, Content = view };
            try
            {
                Trace("Show window");
                window.Show();
                Trace("Pump initial UI");
                Pump();
                Trace("Assert initial Commands page");
                var commands = Page("CommandsPage");
                AssertRouteIndicator("/commands");
                foreach (var component in view.GetVisualDescendants().OfType<AkburaControl>())
                    Assert.Same(service, AkburaEngine.Singletone.GetService(component, typeof(IRouter)));
                var commandLink = Link("Commands");
                var overviewLink = Link("Overview");
                Assert.Equal(1d, commandLink.Opacity);
                Assert.Equal(0.75d, overviewLink.Opacity);
                Assert.NotEqual(Colors.Transparent, ((ISolidColorBrush)commandLink.Background!).Color);

                service.Url = "COMMANDS/";
                Pump();
                Assert.Same(commands, Page("CommandsPage"));

                overviewLink.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Trace("Overview clicked");
                Pump();
                Assert.Equal("/overview", service.Url);
                Page("OverviewPage");
                AssertRouteIndicator("/overview");
                Assert.Equal(1d, overviewLink.Opacity);
                Assert.Equal(0.75d, commandLink.Opacity);
#if DEBUG
                // Exercise the real diagnostics picker against Router's actual
                // state metadata, then the same write path used by diagnostics.
                var routerComponent = Page("Router");
                var routeState = Assert.Single(routerComponent.GetDiagnosticStates(), state => state.Info?.Name == "url");
                var inputBuilder = new Akbura.FeatureGallery.Diagnostics.RouteInputBuilder();
                var request = new Akbura.Diagnostics.InputRequest
                {
                    RequestedType = typeof(string), ComponentType = routerComponent.GetType(),
                    ComponentInstance = routerComponent, Variation = Akbura.Diagnostics.DataVariation.State,
                    MemberName = "url", ExistingValue = service.Url,
                };
                Assert.True(inputBuilder.CanProvide(request));
                var picker = Assert.IsType<ComboBox>(inputBuilder.Build(request, service.Url));
                Assert.Contains("/commands", picker.Items.Cast<string>());
                Assert.Contains("/overview", picker.Items.Cast<string>());
                picker.PropertyChanged += (_, args) =>
                {
                    if (args.Property == Akbura.Diagnostics.InputBuilder.InputValueProperty)
                        routeState.BoxedValue = args.NewValue;
                };
                picker.SelectedItem = "/commands";
                Trace("Diagnostics selected Commands");
                Pump();
                Assert.Equal("/commands", service.Url);
                Page("CommandsPage");
#endif
                service.Url = "/commands"; // e.g. a browser popstate notification
                Pump();
                Page("CommandsPage");
                AssertRouteIndicator("/commands");

                service.Url = "/missing-page";
                Trace("Navigate to NotFound");
                Pump();
                Page("NotFoundPage");
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Page not found");
                Link("Go to Overview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
                Assert.Equal("/overview", service.Url);
                Page("OverviewPage");
                AssertRouteIndicator("/overview");
            }
            finally { Trace("Close window"); window.Close(); Dispatcher.UIThread.RunJobs(); Trace("Window closed"); }

            AkburaControl Page(string name) => Assert.Single(view.GetVisualDescendants().OfType<AkburaControl>(), control => control.GetType().Name == name);
            Button Link(string content) => Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, content));
            void AssertRouteIndicator(string route) => Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == route);
            void Pump()
            {
                for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            }
        }, CancellationToken.None);
    }

    private static void Trace(string stage)
    {
        var file = Environment.GetEnvironmentVariable("AKBURA_GALLERY_ROUTING_TRACE");
        if (file is not null) File.AppendAllText(file, $"{DateTimeOffset.Now:o} {stage}{Environment.NewLine}");
    }
}

public static class GalleryRoutingTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<Application>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
