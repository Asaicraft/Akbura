using Akbura.Akcss;
using Akbura.Language.CodeGeneration;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.CodeAnalysis;
using System.Runtime.Loader;

namespace Akbura.UnitTests;

public sealed partial class AkcssNativeGenerationTests
{
    private const string FragmentUtilities = Controls + """
        @mixin Button PresenterBackground(object resourceKey)
        {
            ^ /template/ ContentPresenter#PART_ContentPresenter
            {
                Background: Amx.DynamicResource(resourceKey);
            }
        }

        @utilities
        {
            Button.bg-red-300 { @include PresenterBackground("--color-red-300"); }
            Button.bg-red-700 { @include PresenterBackground("--color-red-700"); }
            Button.bg-focus { @include PresenterBackground("--color-focus"); }
            Button.bg-(object key) { @include PresenterBackground(key); }
        }

        Button.interactive
        {
            Opacity: 0.8d;
            ^:pointerover { Opacity: 1d; }
        }
        """;

    private const string FragmentViewMarkup = """
        using Avalonia.Controls;
        using Akbura.Markup;
        using HelloWorld.Fragments.akcss;
        <StackPanel>
            <Button Content="First" Width={120d} Height={35d} class="interactive"
                bg-red-300 ${hover}:bg-red-700 ${focus}:bg-focus />
            <Button Content="Other" Width={120d} Height={35d} />
        </StackPanel>
        """;

    private const string FragmentViewHost = """
        namespace HelloWorld;
        public partial class FragmentView : Akbura.AkburaControl
        {
            public FragmentView() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public void InitializeForTest() => InitializeIfNeeded();
            public void UpdateForTest() => Update();
        }
        """;

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "AkburaBlackSilenceGenerator")]
    [InlineData(0, "AkburaCsGenerator")]
    public async Task NativeFragmentsStyleTheRealFluentTemplateAndFollowHover(int mode, string? generator)
    {
        var fixture = Generate([("Fragments.akcss", FragmentUtilities), ("FragmentView.akbura", FragmentViewMarkup)],
            FragmentViewHost, mode: (ComponentGenerationMode)mode, generatorName: generator);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentTheme());
            var viewType = assembly.GetType("HelloWorld.FragmentView")!;
            var view = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(viewType));
            viewType.GetMethod("InitializeForTest")!.Invoke(view, null);
            var panel = Assert.IsType<StackPanel>(view.Child);
            var first = Assert.IsType<Button>(panel.Children[0]);
            var second = Assert.IsType<Button>(panel.Children[1]);
            var userStyles = new Style();
            first.Styles.Add(userStyles);
            first.Classes.Add("user");
            var window = new Window { Width = 280d, Height = 180d, Content = view };
            window.Resources["--color-red-300"] = Brushes.DarkRed;
            window.Resources["--color-red-700"] = Brushes.Red;
            window.Resources["--color-focus"] = Brushes.Green;
            try
            {
                window.Show();
                first.ApplyTemplate();
                second.ApplyTemplate();
                PumpFragmentWindow(window);
                var presenter = GetFragmentPresenter(first);
                var sibling = GetFragmentPresenter(second);
                var nativeBackground = sibling.Background;
                var group = Assert.Single(first.Styles.OfType<Styles>());
                var initialRoots = group.ToArray();
                Assert.Equal(4, initialRoots.Length);
                Assert.Equal(3, AkburaControl.GetAkcssStyles(first).OfType<AkcssUtilityCandidateActivator>().Count());
                Assert.All(AkburaControl.GetAkcssStyles(first).OfType<AkcssUtilityCandidateActivator>(),
                    candidate => Assert.Equal(0, candidate.OperationCount));
                Assert.Equal(Brushes.DarkRed, presenter.Background);
                Assert.Equal(0.8d, first.Opacity);
                var inside = first.TranslatePoint(new Point(10d, 10d), window)!.Value;
                var outside = new Point(250d, 150d);
                window.MouseMove(inside, RawInputModifiers.None);
                PumpFragmentWindow(window);
                Assert.True(first.IsPointerOver);
                Assert.Equal(Brushes.Red, presenter.Background);
                Assert.Equal(nativeBackground, sibling.Background);
                Assert.Equal(1d, first.Opacity);
                window.MouseMove(outside, RawInputModifiers.None);
                PumpFragmentWindow(window);
                Assert.Equal(Brushes.DarkRed, presenter.Background);
                window.MouseMove(inside, RawInputModifiers.None);
                PumpFragmentWindow(window);
                window.Resources["--color-red-700"] = Brushes.Cyan;
                Assert.Equal(Brushes.Cyan, presenter.Background);
                Assert.True(first.Focus());
                PumpFragmentWindow(window);
                Assert.Equal(Brushes.Green, presenter.Background);
                window.MouseDown(inside, MouseButton.Left, RawInputModifiers.None);
                PumpFragmentWindow(window);
                Assert.Contains(":pressed", first.Classes);
                Assert.Equal(Brushes.Green, presenter.Background);
                window.MouseUp(inside, MouseButton.Left, RawInputModifiers.None);
                PumpFragmentWindow(window);
                window.MouseMove(outside, RawInputModifiers.None);
                PumpFragmentWindow(window);
                Assert.False(first.IsPointerOver);
                Assert.Equal(Brushes.Green, presenter.Background);
                Assert.Equal(0.8d, first.Opacity);
                window.MouseMove(inside, RawInputModifiers.None);
                PumpFragmentWindow(window);
                Assert.Equal(Brushes.Green, presenter.Background);
                second.Focus();
                PumpFragmentWindow(window);
                Assert.Equal(Brushes.Cyan, presenter.Background);
                Assert.Equal(initialRoots, group.ToArray());

                presenter.Background = Brushes.Gold;
                Assert.Equal(Brushes.Gold, presenter.Background);
                presenter.ClearValue(ContentPresenter.BackgroundProperty);
                Assert.Equal(Brushes.Cyan, presenter.Background);
                using (presenter.SetValue(ContentPresenter.BackgroundProperty, Brushes.Black, BindingPriority.Animation))
                {
                    Assert.Equal(Brushes.Black, presenter.Background);
                }
                Assert.Equal(Brushes.Cyan, presenter.Background);

                first.Template = new FuncControlTemplate<Button>((_, scope) =>
                {
                    var replacement = new ContentPresenter { Name = "PART_ContentPresenter" };
                    scope.Register(replacement.Name, replacement);
                    scope.Complete();
                    return replacement;
                });
                first.ApplyTemplate();
                PumpFragmentWindow(window);
                var next = GetFragmentPresenter(first);
                Assert.NotSame(presenter, next);
                Assert.Equal(first.IsPointerOver ? Brushes.Cyan : Brushes.DarkRed, next.Background);
                Assert.Null(presenter.TemplatedParent);
                var retiredBackground = presenter.Background;
                window.MouseMove(outside, RawInputModifiers.None);
                window.MouseMove(inside, RawInputModifiers.None);
                PumpFragmentWindow(window);
                Assert.Equal(Brushes.Cyan, next.Background);

                AkburaControl.ReplaceAkcssStylesForHotReload(first, []);
                PumpFragmentWindow(window);
                Assert.True(first.IsPointerOver);
                Assert.NotEqual(Brushes.Cyan, next.Background);
                var restored = next.Background;
                window.Resources["--color-red-700"] = Brushes.Purple;
                Assert.Equal(restored, next.Background);
                Assert.Equal(retiredBackground, presenter.Background);
                Assert.Equal(nativeBackground, sibling.Background);
                Assert.Same(userStyles, Assert.Single(first.Styles));
                Assert.Contains("user", first.Classes);
                Assert.DoesNotContain(first.Classes, token => token.StartsWith(":akbura-native-", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeApplicationsInsideControlTemplatesHaveIndependentScopes(bool compiled)
    {
        var themed = ("Themed.akcss", Controls + """
                @using Akbura.Markup;
                @using HelloWorld.Fragments.akcss;
                @theme Button ThemedButton
                {
                    @template {
                        <StackPanel>
                            $if (true) {
                                <Button x.Name="child" Content="Child" Width={95d} Height={25d}
                                    class="interactive" bg-{"--color-red-300"} ${hover}:bg-red-700 ${focus}:bg-focus />
                            }
                        </StackPanel>
                    }
                }
                """);
        System.Reflection.Assembly assembly;
        if (compiled)
        {
            var image = Generate([("Fragments.akcss", FragmentUtilities)]).Emit();
            using var dependency = new MemoryStream(image);
            AssemblyLoadContext.Default.LoadFromStream(dependency);
            var fixture = Generate([themed], references: [MetadataReference.CreateFromImage(image)]);
            using var consumer = new MemoryStream(fixture.Emit());
            assembly = AssemblyLoadContext.Default.LoadFromStream(consumer);
        }
        else
        {
            assembly = Generate([("Fragments.akcss", FragmentUtilities), themed]).Compile();
        }
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentTheme());
            var theme = Assert.IsType<ControlTheme>(assembly.GetType("HelloWorld.Themed")!
                .GetProperty("ThemedButton")!.GetValue(null));
            var first = new Button { Theme = theme };
            var second = new Button { Theme = theme };
            var window = new Window { Width = 250d, Height = 140d, Content = new StackPanel { Children = { first, second } } };
            window.Resources["--color-red-300"] = Brushes.DarkRed;
            window.Resources["--color-red-700"] = Brushes.Red;
            window.Resources["--color-focus"] = Brushes.Green;
            try
            {
                window.Show();
                first.ApplyTemplate();
                second.ApplyTemplate();
                PumpFragmentWindow(window);
                var firstRoot = Assert.IsType<StackPanel>(Assert.Single(first.GetVisualChildren()));
                var secondRoot = Assert.IsType<StackPanel>(Assert.Single(second.GetVisualChildren()));
                var child = Assert.IsType<Button>(Assert.Single(firstRoot.Children));
                var other = Assert.IsType<Button>(Assert.Single(secondRoot.Children));
                Assert.Equal(0.8d, child.Opacity);
                Assert.Equal(Brushes.DarkRed, GetFragmentPresenter(child).Background);
                Assert.Equal(Brushes.DarkRed, GetFragmentPresenter(other).Background);
                Assert.NotSame(Assert.Single(child.Styles.OfType<Styles>()), Assert.Single(other.Styles.OfType<Styles>()));
                var inside = child.TranslatePoint(new Point(8d, 8d), window)!.Value;
                window.MouseMove(inside, RawInputModifiers.None);
                PumpFragmentWindow(window);
                Assert.True(child.IsPointerOver);
                Assert.Equal(Brushes.Red, GetFragmentPresenter(child).Background);
                Assert.Equal(Brushes.DarkRed, GetFragmentPresenter(other).Background);
                first.Template = new FuncControlTemplate<Button>((_, _) => new Border());
                first.ApplyTemplate();
                PumpFragmentWindow(window);
                Assert.Empty(child.Styles.OfType<Styles>());
                Assert.Single(other.Styles.OfType<Styles>());
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }


    [Fact]
    public async Task GeneratedUtilityArgumentsUseDefinitionScopeAndResolveResourcesInEachHost()
    {
        var fixture = Generate([
            ("Definitions.akcss", Controls + """
                @using Helpers = Definition.Keys;
                @mixin Button Inner(object key)
                {
                    ^ /template/ ContentPresenter#PART_ContentPresenter
                    {
                        Background: Amx.DynamicResource(key);
                    }
                }
                @mixin Button Outer(object key)
                {
                    @include Inner(Helpers.Record(key));
                }
                """),
            ("Arguments.akcss", Controls + """
                @using HelloWorld.Definitions.akcss;
                @using Helpers = Caller.Keys;
                @utilities
                {
                    Button.resource-(object key) { @include Outer(Helpers.Record(key)); }
                }
                """),
            ("ArgumentView.akbura", """
                using Avalonia.Controls;
                using HelloWorld.Arguments.akcss;
                <SpecializedButton Content="Argument" resource-${FragmentKey} />
                """),
        ], """
            using System;
            namespace Definition
            {
                public static class Keys
                {
                    public static int Calls { get; private set; }
                    public static object Record(object key) { Calls++; return key; }
                }
            }
            namespace Caller
            {
                public static class Keys
                {
                    public static int Calls { get; private set; }
                    public static object Record(object key) { Calls++; return key; }
                }
            }
            namespace HelloWorld
            {
                public sealed class SpecializedButton : Avalonia.Controls.Button { }
                public sealed class FragmentKeyExtension
                {
                    public static IObservable<object>? Source { get; set; }
                    public IObservable<object>? ProvideValue() => Source;
                }
                public partial class ArgumentView : Akbura.AkburaControl
                {
                    public ArgumentView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                    public void InitializeForTest() => InitializeIfNeeded();
                    public void UpdateForTest() => Update();
                }
            }
            """);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var keys = new AkcssNativeFragmentRuntimeTests.CountedSignal<object>();
            assembly.GetType("HelloWorld.FragmentKeyExtension")!.GetProperty("Source")!.SetValue(null, keys);
            var type = assembly.GetType("HelloWorld.ArgumentView")!;
            var first = CreateView();
            var second = CreateView();
            var firstButton = Assert.IsAssignableFrom<Button>(first.Child);
            Assert.Equal(firstButton.GetType(), firstButton.StyleKey);
            var secondButton = Assert.IsAssignableFrom<Button>(second.Child);
            var firstWindow = new Window { Width = 180d, Height = 100d, Content = first };
            var secondWindow = new Window { Width = 180d, Height = 100d, Content = second };
            var initial = new object();
            var replacement = new object();
            firstWindow.Resources[initial] = Brushes.Red;
            secondWindow.Resources[initial] = Brushes.Green;
            firstWindow.Resources[replacement] = Brushes.Blue;
            secondWindow.Resources[replacement] = Brushes.Purple;
            try
            {
                firstWindow.Show();
                secondWindow.Show();
                firstButton.ApplyTemplate();
                secondButton.ApplyTemplate();
                PumpFragmentWindow(firstWindow);
                PumpFragmentWindow(secondWindow);
                Assert.Equal(0, ReadCalls("Caller.Keys"));
                Assert.Empty(firstButton.Styles);
                Assert.Equal(2, keys.Subscriptions);
                keys.Send(initial);
                PumpFragmentWindow(firstWindow);
                PumpFragmentWindow(secondWindow);
                Assert.Equal(Brushes.Red, GetFragmentPresenter(firstButton).Background);
                Assert.Equal(Brushes.Green, GetFragmentPresenter(secondButton).Background);
                Assert.Equal(2, ReadCalls("Caller.Keys"));
                Assert.Equal(2, ReadCalls("Definition.Keys"));
                var firstStyles = Assert.IsType<Styles>(Assert.Single(firstButton.Styles));
                var secondStyles = Assert.IsType<Styles>(Assert.Single(secondButton.Styles));
                Assert.NotSame(Assert.Single(firstStyles), Assert.Single(secondStyles));
                type.GetMethod("UpdateForTest")!.Invoke(first, null);
                Assert.Equal(2, ReadCalls("Caller.Keys"));
                keys.Send(replacement);
                PumpFragmentWindow(firstWindow);
                PumpFragmentWindow(secondWindow);
                Assert.Equal(Brushes.Blue, GetFragmentPresenter(firstButton).Background);
                Assert.Equal(Brushes.Purple, GetFragmentPresenter(secondButton).Background);
                Assert.Equal(4, ReadCalls("Caller.Keys"));
                Assert.Equal(4, ReadCalls("Definition.Keys"));
            }
            finally
            {
                firstWindow.Close();
                secondWindow.Close();
            }

            AkburaControl CreateView()
            {
                var view = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
                type.GetMethod("InitializeForTest")!.Invoke(view, null);
                var button = Assert.IsAssignableFrom<Button>(view.Child);
                button.Template = new FuncControlTemplate<Button>((_, scope) =>
                {
                    var part = new ContentPresenter { Name = "PART_ContentPresenter" };
                    scope.Register(part.Name, part);
                    scope.Complete();
                    return part;
                });
                return view;
            }

            int ReadCalls(string name) => (int)assembly.GetType(name)!.GetProperty("Calls")!.GetValue(null)!;
        }, CancellationToken.None);
    }

    private static ContentPresenter GetFragmentPresenter(Button button) => Assert.Single(
        button.GetVisualDescendants().OfType<ContentPresenter>(), part => part.Name == "PART_ContentPresenter");

    private static void PumpFragmentWindow(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
