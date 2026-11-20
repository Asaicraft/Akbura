using Akbura.BlackSilence;
using Akbura.Furioso;
using Akbura.Language.CodeGeneration;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Akbura.UnitTests;

public sealed partial class AkcssNativeGenerationTests
{
    [Fact]
    public async Task ImportedMixinsEvaluateArgumentsOnceAndUseNativePropertyIdentity()
    {
        var fixture = Generate([
            ("Definitions.akcss", Controls + """
                @using Scope = Definition.Helpers;
                @mixin HelloWorld.BaseButton Inner(double amount)
                {
                    Shared: Scope.Mark("inner", amount);
                }
                @mixin HelloWorld.BaseButton Outer(double amount, double second, double extra = Scope.Default)
                {
                    @include Inner(amount);
                    Opacity: Scope.Eval(__akcssArgument0 => __akcssArgument0 + (second / 100d), amount / 100d);
                    FontSize: Scope.Mark("font", amount + second + extra);
                }
                """),
            ("Overrides.akcss", Controls + """
                @using HelloWorld.Definitions.akcss;
                @using Scope = Caller.Helpers;
                @theme HelloWorld.DerivedButton Combined
                {
                    @include Outer(Scope.Mark("first", 10d), Scope.Mark("second", 20d));
                    Alias: Scope.Mark("override", 42d);
                }
                """),
        ], """
            using System;
            using System.Collections.Generic;
            using Avalonia;
            using Avalonia.Controls;
            namespace HelloWorld
            {
                public class BaseButton : Button
                {
                    public static readonly StyledProperty<double> SharedProperty =
                        AvaloniaProperty.Register<BaseButton, double>("Shared");
                }
                public class DerivedButton : BaseButton
                {
                    public static readonly StyledProperty<double> AliasProperty = SharedProperty.AddOwner<DerivedButton>();
                }
                public static class Trace
                {
                    public static List<string> Calls { get; } = new();
                    public static double Record(string label, double value) { Calls.Add(label); return value; }
                }
            }
            namespace Definition
            {
                public static class Helpers
                {
                    public const double Default = 5d;
                    public static double Mark(string label, double value) => HelloWorld.Trace.Record("definition:" + label, value);
                    public static double Eval(Func<double, double> function, double value) => function(value);
                }
            }
            namespace Caller
            {
                public static class Helpers
                {
                    public static double Mark(string label, double value) => HelloWorld.Trace.Record("caller:" + label, value);
                }
            }
            """);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var facade = assembly.GetType("HelloWorld.Overrides")!;
            var theme = Assert.IsType<ControlTheme>(facade.GetProperty("Combined")!.GetValue(null));
            var setters = theme.Setters.Cast<Setter>().ToArray();
            Assert.Equal(new[] { "Opacity", "FontSize", "Shared" }, setters.Select(static setter => setter.Property!.Name));
            Assert.Equal(0.3d, Assert.IsType<double>(setters[0].Value), precision: 10);
            Assert.Equal(35d, setters[1].Value);
            Assert.Equal(42d, setters[2].Value);
            var alias = assembly.GetType("HelloWorld.DerivedButton")!.GetField("AliasProperty")!.GetValue(null);
            Assert.Same(alias, setters[2].Property);
            var calls = Assert.IsType<List<string>>(assembly.GetType("HelloWorld.Trace")!.GetProperty("Calls")!.GetValue(null));
            Assert.Equal(new[] { "caller:first", "caller:second", "definition:inner", "definition:font", "caller:override" }, calls);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LazyThemesLinkForwardCrossModuleAndExternalBases()
    {
        var fixture = Generate([
            ("Library.akcss", Controls + """
                @theme Button Original
                {
                    @template { <TextBlock x.Name="original" Text="original" /> }
                }
                """),
            ("Inheritance.akcss", Controls + """
                @theme Button Forward { @based-on Local; }
                @theme Button Local { @based-on global::HelloWorld.Library.Original; }
                @theme Button Replacement
                {
                    @based-on Forward;
                    @template { <TextBlock x.Name="replacement" Text="replacement" /> }
                }
                @theme Button External { @based-on HelloWorld.Host.GetTheme(); }
                """),
        ], """
            namespace HelloWorld;
            public static class Host
            {
                public static int Calls { get; private set; }
                public static readonly Avalonia.Styling.ControlTheme Existing =
                    new Avalonia.Styling.ControlTheme { TargetType = typeof(Avalonia.Controls.Button) };
                public static Avalonia.Styling.ControlTheme GetTheme() { Calls++; return Existing; }
            }
            """);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var facade = assembly.GetType("HelloWorld.Inheritance")!;
            ControlTheme Read(string name) => Assert.IsType<ControlTheme>(facade.GetProperty(name)!.GetValue(null));
            var forward = Read("Forward");
            var local = Read("Local");
            var original = Assert.IsType<ControlTheme>(assembly.GetType("HelloWorld.Library")!.GetProperty("Original")!.GetValue(null));
            Assert.Same(local, forward.BasedOn);
            Assert.Same(original, local.BasedOn);
            Assert.Empty(forward.Setters);
            Assert.Empty(local.Setters);
            var replacement = Read("Replacement");
            Assert.Same(forward, replacement.BasedOn);
            var external = Read("External");
            var host = assembly.GetType("HelloWorld.Host")!;
            Assert.Same(host.GetField("Existing")!.GetValue(null), external.BasedOn);
            Assert.Same(external, Read("External"));
            Assert.Equal(1, host.GetProperty("Calls")!.GetValue(null));

            var button = new Button { Theme = forward };
            var window = new Window { Content = button };
            try
            {
                window.Show();
                button.ApplyTemplate();
                var first = Assert.IsType<TextBlock>(Assert.Single(button.GetVisualChildren()));
                Assert.Equal("original", first.Name);
                button.Theme = replacement;
                button.ApplyTemplate();
                Dispatcher.UIThread.RunJobs();
                var next = Assert.IsType<TextBlock>(Assert.Single(button.GetVisualChildren()));
                Assert.NotSame(first, next);
                Assert.Equal("replacement", next.Name);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ResourceLoweringUsesTypedObjectKeysAndLeavesLookalikesExecutable()
    {
        var fixture = Generate([("Resources.akcss", Controls + """
                @using ResourceApi = Akbura.Amx;
                @using Shadow = HelloWorld.Lookalike;
                @theme Button TypedResources
                {
                    Background: ResourceApi.DynamicResource(typeof(Button));
                    FontSize: global::Akbura.Amx.DynamicResource<double>("size");
                    CornerRadius: new CornerRadius(Shadow.DynamicResource("ordinary"));
                }
                """)], """
            namespace HelloWorld;
            public static class Lookalike { public static double DynamicResource(object key) => 2d; }
            """);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var theme = Assert.IsType<ControlTheme>(
                assembly.GetType("HelloWorld.Resources")!.GetProperty("TypedResources")!.GetValue(null));
            var setters = theme.Setters.Cast<Setter>().ToArray();
            Assert.Same(typeof(Button), Assert.IsType<DynamicResourceExtension>(setters[0].Value).ResourceKey);
            Assert.Equal("size", Assert.IsType<DynamicResourceExtension>(setters[1].Value).ResourceKey);
            Assert.Equal(new CornerRadius(2d), Assert.IsType<CornerRadius>(setters[2].Value));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TemplateMarkupResourcesAndAssignmentsUseTemplatePriority()
    {
        var fixture = Generate([("TemplateValues.akcss", Controls + """
            @using ResourceApi = Akbura.Amx;
            @mixin Border Fill() { Background: Amx.DynamicResource("inside"); }
            @theme Button TemplateValuesTheme
            {
                @template {
                    <StackPanel>
                        <Border x.Name="literal" Background="Red" />
                        <Border x.Name="bound" Background={ResourceApi.DynamicResource("inside")} />
                        <Border x.Name="extension" Background=${DynamicResource inside} />
                        <Border x.Name="fromInclude">@include Fill();</Border>
                        <Border x.Name="fromStatic" Background={ResourceApi.StaticResource("inside")} />
                    </StackPanel>
                }
                ^:pointerover /template/ #literal:highlight { Background: Blue; }
                ^:pointerover /template/ #bound.highlight { Background: Purple; }
            }
            """)]);
        Assert.DoesNotContain("ResourceApi.DynamicResource(", fixture.Text);
        Assert.DoesNotContain("ResourceApi.StaticResource(", fixture.Text);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var theme = Assert.IsType<ControlTheme>(assembly.GetType("HelloWorld.TemplateValues")!
                .GetProperty("TemplateValuesTheme")!.GetValue(null));
            var button = new Button { Theme = theme };
            button.Resources["inside"] = Avalonia.Media.Brushes.Green;
            var window = new Window { Content = button };
            try
            {
                window.Show();
                button.ApplyTemplate();
                var parts = button.GetVisualDescendants().OfType<Border>().ToDictionary(border => border.Name!);
                Assert.All(new[] { "bound", "extension", "fromInclude", "fromStatic" },
                    name => Assert.Equal(Avalonia.Media.Brushes.Green, parts[name].Background));
                ((IPseudoClasses)parts["literal"].Classes).Add(":highlight");
                parts["bound"].Classes.Add("highlight");
                ((IPseudoClasses)button.Classes).Add(":pointerover");
                Assert.Equal(Avalonia.Media.Colors.Blue, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(parts["literal"].Background).Color);
                Assert.Equal(Avalonia.Media.Colors.Purple, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(parts["bound"].Background).Color);
                button.Resources["inside"] = Avalonia.Media.Brushes.Cyan;
                Assert.Equal(Avalonia.Media.Brushes.Cyan, parts["extension"].Background);
                Assert.Equal(Avalonia.Media.Brushes.Cyan, parts["fromInclude"].Background);
                Assert.Equal(Avalonia.Media.Brushes.Green, parts["fromStatic"].Background);
                ((IPseudoClasses)button.Classes).Remove(":pointerover");
                Assert.Equal(Avalonia.Media.Brushes.Cyan, parts["bound"].Background);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NestedRulesPreserveNativeStyleOwnershipAndActivation()
    {
        var fixture = Generate([("NestedRules.akcss", Controls + """
            Border.frame.card
            {
                ^ > TextBlock.label
                {
                    FontSize: 17d;
                    ^:special { FontSize: 19d; }
                }
            }
            """)]);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var facade = assembly.GetType("HelloWorld.NestedRules")!;
            var styles = Assert.IsType<Styles>(facade.GetMethod("CreateStyles")!.Invoke(null, null));
            var root = Assert.IsType<Style>(Assert.Single(styles));
            var child = Assert.IsType<Style>(Assert.Single(root.Children));
            Assert.IsType<Style>(Assert.Single(child.Children));
            var text = new TextBlock { Classes = { "label" } };
            var border = new Border { Classes = { "frame", "card" }, Child = text };
            var window = new Window { Content = border, Styles = { styles } };
            try
            {
                window.Show();
                Assert.Equal(17d, text.FontSize);
                ((IPseudoClasses)text.Classes).Add(":special");
                Assert.Equal(19d, text.FontSize);
                ((IPseudoClasses)text.Classes).Remove(":special");
                Assert.Equal(17d, text.FontSize);
                border.Classes.Remove("frame");
                Assert.NotEqual(17d, text.FontSize);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TemplateMarkupBuildsConditionalChildrenAndIsolatesDeferredNames()
    {
        var fixture = Generate([("NestedTemplate.akcss", Controls + """
            @using Avalonia.Markup.Xaml.Templates;
            @theme Button Nested
            {
                @template {
                    <StackPanel x.Name="part">
                        $if (true) { <TextBlock x.Name="conditioned" Text="Selected" /> }
                        <ContentPresenter>
                            <ContentPresenter.ContentTemplate>
                                <DataTemplate><TextBlock x.Name="part" Text="Deferred" /></DataTemplate>
                            </ContentPresenter.ContentTemplate>
                        </ContentPresenter>
                    </StackPanel>
                }
            }
            """)]);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var theme = Assert.IsType<ControlTheme>(assembly.GetType("HelloWorld.NestedTemplate")!
                .GetProperty("Nested")!.GetValue(null));
            var factory = Assert.IsAssignableFrom<IControlTemplate>(Assert.Single(theme.Setters.Cast<Setter>()).Value);
            var built = factory.Build(new Button())!;
            var outer = Assert.IsType<StackPanel>(built.Result);
            Assert.Same(outer, built.NameScope.Find("part"));
            var conditional = Assert.IsType<TextBlock>(outer.Children[0]);
            Assert.Equal("Selected", conditional.Text);
            Assert.Same(conditional, built.NameScope.Find("conditioned"));
            var presenter = Assert.IsType<Avalonia.Controls.Presenters.ContentPresenter>(outer.Children[1]);
            var deferred = Assert.IsAssignableFrom<IDataTemplate>(presenter.ContentTemplate);
            var first = Assert.IsType<TextBlock>(deferred.Build(null));
            var second = Assert.IsType<TextBlock>(deferred.Build(null));
            Assert.NotSame(first, second);
            Assert.Equal("part", first.Name);
            Assert.Equal("Deferred", first.Text);
            Assert.Same(outer, built.NameScope.Find("part"));
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TemplateBindingUsesNativeConversionBetweenDifferentPropertyTypes()
    {
        var fixture = Generate([("Conversion.akcss", Controls + """
            @theme HelloWorld.TemplateSource Converted
            {
                @template { <HelloWorld.TemplateTarget>@template-bind Data;</HelloWorld.TemplateTarget> }
            }
            """)], """
            using Avalonia;
            using Avalonia.Controls;
            namespace HelloWorld;
            public sealed class TemplateSource : Button
            {
                public static readonly StyledProperty<string> DataProperty = AvaloniaProperty.Register<TemplateSource, string>("Data");
                public string Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
            }
            public sealed class TemplateTarget : Border
            {
                public static readonly StyledProperty<double> DataProperty = AvaloniaProperty.Register<TemplateTarget, double>("Data");
                public double Data => GetValue(DataProperty);
            }
            """);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var theme = Assert.IsType<ControlTheme>(assembly.GetType("HelloWorld.Conversion")!.GetProperty("Converted")!.GetValue(null));
            var sourceType = assembly.GetType("HelloWorld.TemplateSource")!;
            var button = Assert.IsAssignableFrom<Button>(Activator.CreateInstance(sourceType));
            button.Theme = theme;
            sourceType.GetProperty("Data")!.SetValue(button, "17");
            var window = new Window { Content = button };
            try
            {
                window.Show();
                button.ApplyTemplate();
                var root = Assert.Single(button.GetVisualChildren());
                var target = root.GetType().GetProperty("Data")!;
                Assert.Equal(17d, target.GetValue(root));
                sourceType.GetProperty("Data")!.SetValue(button, "23");
                Assert.Equal(23d, target.GetValue(root));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("@theme Button Invalid { FontSize: Amx.StaticResource(\"size\"); }", "ambient resource context")]
    [InlineData("@theme Button Invalid { FontSize: FontSize + 1d; }", "receiver-dependent")]
    [InlineData("@theme Button Invalid { @if (true) { FontSize: 16d; } }", "Reactive @if")]
    [InlineData("@theme Button Invalid { FontSize: 1d + Amx.DynamicResource<double>(\"size\"); }", "no supported native lowering")]
    [InlineData("(global::System.String) > Border { }", "closed Avalonia StyledElement")]
    [InlineData("@theme Button CreateStyles { }", "cannot be exported")]
    public void UnsupportedNativeConstructsHaveSourceMappedDiagnostics(string source, string message)
    {
        var fixture = Generate([("Unsupported.akcss", Controls + source)]);
        var diagnostic = Assert.Single(fixture.Compilation.GetDiagnostics(),
            item => item.Id == "CS1029" && item.GetMessage().Contains(message, StringComparison.Ordinal));
        Assert.EndsWith("Unsupported.akcss", diagnostic.Location.GetMappedLineSpan().Path);
    }

    [Fact]
    public void NativeMixinGenerationDiagnosticsKeepIncludeAndDefinitionSpans()
    {
        var definition = Controls + """
            @mixin Button Paint() { FontSize: Amx.StaticResource("size"); }
            """;
        var theme = Controls + """
            @using HelloWorld.Definitions.akcss;
            @theme Button Bad { @include Paint(); }
            """;
        var fixture = Generate([("Definitions.akcss", definition), ("Theme.akcss", theme)]);
        var diagnostic = Assert.Single(fixture.GetGenerationDiagnostics());
        Assert.Equal(ErrorCodes.AKBURA_GENERATION_AkcssUnsupported, diagnostic.Id);
        Assert.EndsWith("Theme.akcss", diagnostic.FilePath);
        Assert.Equal("@include Paint();", theme.Substring(diagnostic.Span.Start, diagnostic.Span.Length));
        var related = Assert.Single(diagnostic.AdditionalLocations);
        Assert.EndsWith("Definitions.akcss", related.FilePath);
        Assert.Contains("Amx.StaticResource", definition.Substring(related.Span.Start, related.Span.Length));
    }

    [Fact]
    public void StandaloneFacadeConflictsWithTypesNamespacesAndOtherPublicFacades()
    {
        const string declaration = Controls + "@theme Button TheTheme { }";
        var fixture = Generate([
            ("TypeConflict.akcss", declaration),
            ("Bad-Name.akcss", declaration),
            ("NamespaceConflict.akcss", declaration),
            ("Parent.akcss", declaration),
            ("Parent/Child.akcss", declaration),
            ("Folder.Shared.akcss", declaration),
            ("Folder/Shared.akcss", declaration),
        ], """
            namespace HelloWorld { public sealed class TypeConflict { } }
            namespace HelloWorld.NamespaceConflict { public sealed class Marker { } }
            """, validateSemantics: false);
        var diagnostics = fixture.GetGenerationDiagnostics();
        Assert.All(diagnostics, item => Assert.Equal(ErrorCodes.AKBURA_GENERATION_AkcssExport, item.Id));
        Assert.Contains(diagnostics, item => item.Message.Contains("existing C# type", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Message.Contains("valid public C# facade", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Message.Contains("existing C# namespace", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Message.Contains("already exported as a type", StringComparison.Ordinal) &&
            item.AdditionalLocations.Length == 1);
        Assert.Equal(2, diagnostics.Count(item => item.Message.Contains("Multiple AKCSS modules", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TemplateMarkupLoopsBuildFreshChildrenInOrder()
    {
        var fixture = Generate([("LoopTemplate.akcss", Controls + """
            @theme Button Loop
            {
                @template {
                    <StackPanel>
                        $if (true) {
                            $foreach (var word in new[] { "First", "Second" }) {
                                <TextBlock x.Name="iteration" Text={word + index.ToString()} />
                                $foreach (var length in new[] { word.Length }) {
                                    <TextBlock Text={length.ToString()} />
                                }
                            }
                        }
                        <TextBlock Text="Last" />
                    </StackPanel>
                }
            }
            """)]);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var theme = Assert.IsType<ControlTheme>(assembly.GetType("HelloWorld.LoopTemplate")!
                .GetProperty("Loop")!.GetValue(null));
            var factory = Assert.IsAssignableFrom<IControlTemplate>(Assert.Single(theme.Setters.Cast<Setter>()).Value);
            var built = factory.Build(new Button())!;
            var contents = Assert.IsType<StackPanel>(built.Result).Children.Cast<TextBlock>().ToArray();
            Assert.Equal(new[] { "First0", "5", "Second1", "6", "Last" }, contents.Select(static text => text.Text));
            Assert.Same(contents[0], NameScope.GetNameScope(contents[0])?.Find("iteration"));
            Assert.Same(contents[2], NameScope.GetNameScope(contents[2])?.Find("iteration"));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, null)]
    [InlineData(0, nameof(AkburaCsGenerator))]
    [InlineData(0, nameof(AkburaBlackSilenceGenerator))]
    [InlineData(1, nameof(AkburaBlackSilenceGenerator))]
    public async Task InlineNativeStylesInstallOncePerComponentAndPreserveLegacyActivation(int mode, string? generatorName)
    {
        var fixture = Generate([
            ("Imported.akcss", Controls + "Border > TextBlock { Opacity: 0.2d; }"),
            ("InlineView.akbura", """
                using Avalonia.Controls;
                @akcss {
                    @using HelloWorld.Imported.akcss;
                    Border > TextBlock { FontSize: 14d; }
                    .legacy { Opacity: 0.6d; }
                }
                @akcss {
                    Border TextBlock { FontSize: 18d; }
                }
                <StackPanel>
                    <Border><TextBlock x.Name="native" /></Border>
                    <TextBlock x.Name="legacy" class="legacy" />
                </StackPanel>
                """),
        ], """
            namespace HelloWorld;
            public partial class InlineView : Akbura.AkburaControl
            {
                public InlineView() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public void InitializeForTest() => InitializeIfNeeded();
                public void RenderAgain() => Update();
            }
            """, (ComponentGenerationMode)mode, generatorName: generatorName);
        var assembly = fixture.Compile();
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var type = assembly.GetType("HelloWorld.InlineView")!;
            var first = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            var second = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            var existing = new Style();
            first.Styles.Add(existing);
            type.GetMethod("InitializeForTest")!.Invoke(first, null);
            type.GetMethod("InitializeForTest")!.Invoke(second, null);
            var firstGroup = Assert.Single(first.Styles.OfType<Styles>());
            var secondGroup = Assert.Single(second.Styles.OfType<Styles>());
            Assert.NotSame(firstGroup, secondGroup);
            Assert.Same(existing, first.Styles[0]);
            Assert.Equal(2, firstGroup.Count);
            var window = new Window { Content = first };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var texts = first.GetVisualDescendants().OfType<TextBlock>().ToArray();
                var native = Assert.Single(texts, text => text.Name == "native");
                var legacy = Assert.Single(texts, text => text.Name == "legacy");
                Assert.Equal(18d, native.FontSize);
                Assert.Equal(1d, native.Opacity);
                Assert.Equal(0.6d, legacy.Opacity);
                type.GetMethod("RenderAgain")!.Invoke(first, null);
                Assert.Same(firstGroup, Assert.Single(first.Styles.OfType<Styles>()));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
