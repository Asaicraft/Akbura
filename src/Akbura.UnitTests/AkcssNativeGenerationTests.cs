using Akbura.BlackSilence;
using Akbura.Diagnostics;
using Akbura.Language.Syntax;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed partial class AkcssNativeGenerationTests
{
    private const string RootNamespace = "HelloWorld";
    private const string Controls = """
        @using Avalonia.Controls;
        @using Avalonia.Controls.Presenters;
        """ + "\n";

    private const string Hello = Controls + """
        @mixin ContentPresenter DefaultContentPresenterTBind()
        {
            @template-bind Background, BorderBrush, BorderThickness, CornerRadius,
                Content, ContentTemplate, Padding,
                HorizontalContentAlignment, VerticalContentAlignment;
        }

        @theme Button MyTheme
        {
            Background: Amx.DynamicResource("ButtonBackground");
            @template
            {
                <ContentPresenter x.Name="PART_ContentPresenter" RecognizesAccessKey={true}>
                    @include DefaultContentPresenterTBind();
                </ContentPresenter>
            }

            ^:pointerover /template/ #PART_ContentPresenter
            {
                Background: Amx.DynamicResource("ButtonBackgroundPointerOver");
            }

            ^:pressed /template/ #PART_ContentPresenter
            {
                Background: Amx.DynamicResource("ButtonBackgroundPressed");
            }
        }

        (global::Avalonia.Controls.Border) > TextBlock
        {
            FontSize: 14d;
        }
        """;

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public async Task RequiredThemeCompilesAndRunsWithNativeStateAndResources(int mode, bool useBlackSilence)
    {
        var files = new (string Name, string Text)[]
        {
            ("Hello.akcss", Hello),
            ("ThemeReferences.akbura", """
                using Avalonia.Controls;
                using ThemeModule = HelloWorld.Hello;
                <StackPanel>
                    <Button Theme={global::HelloWorld.Hello.MyTheme} />
                    <Button Theme={HelloWorld.Hello.MyTheme} />
                    <Button Theme={Hello.MyTheme} />
                    <Button Theme={ThemeModule.MyTheme} />
                </StackPanel>
                """),
        };
        var fixture = Generate(files, mode: (ComponentGenerationMode)mode, useBlackSilence: useBlackSilence, host: """
            namespace HelloWorld;
            public partial class ThemeReferences : Akbura.AkburaControl
            {
                public ThemeReferences() : base(Akbura.Engine.AkburaEngine.Empty) { }
                public void InitializeForTest() => InitializeIfNeeded();
            }
            """);
        var assembly = fixture.Compile();
        Assert.DoesNotContain("Akbura.Amx.DynamicResource", fixture.Text);
        Assert.DoesNotContain("OfType", fixture.Text.Split("new global::Avalonia.Controls.Templates.FuncControlTemplate")[^1]);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var facade = assembly.GetType("HelloWorld.Hello", throwOnError: true)!;
            var exported = facade.GetProperty("MyTheme")!;
            var theme = Assert.IsType<ControlTheme>(exported.GetValue(null));
            Assert.Same(theme, exported.GetValue(null));
            var referenceType = assembly.GetType("HelloWorld.ThemeReferences", throwOnError: true)!;
            var referenceView = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(referenceType));
            referenceType.GetMethod("InitializeForTest")!.Invoke(referenceView, null);
            var references = Assert.IsType<StackPanel>(referenceView.Child).Children.OfType<Button>().ToArray();
            Assert.Equal(4, references.Length);
            Assert.All(references, button => Assert.Same(theme, button.Theme));

            var createStyles = facade.GetMethod("CreateStyles")!;
            var styles = Assert.IsType<Styles>(createStyles.Invoke(null, null));
            var otherStyles = Assert.IsType<Styles>(createStyles.Invoke(null, null));
            Assert.NotSame(styles, otherStyles);
            Assert.NotSame(Assert.Single(styles), Assert.Single(otherStyles));

            var first = new Button { Theme = theme, Content = "First", Padding = new Thickness(4d) };
            var second = new Button { Theme = theme, Content = "Second" };
            var firstText = new TextBlock();
            var secondText = new TextBlock();
            var firstWindow = new Window
            {
                Content = new StackPanel { Children = { first, new Border { Child = firstText } } },
                Styles = { styles },
            };
            var secondWindow = new Window
            {
                Content = new StackPanel { Children = { second, new Border { Child = secondText } } },
                Styles = { otherStyles },
            };
            AddResources(firstWindow, Brushes.Red);
            AddResources(secondWindow, Brushes.Green);
            try
            {
                firstWindow.Show();
                secondWindow.Show();
                first.ApplyTemplate();
                second.ApplyTemplate();
                Dispatcher.UIThread.RunJobs();
                var firstPresenter = Assert.IsType<ContentPresenter>(Assert.Single(first.GetVisualChildren()));
                var secondPresenter = Assert.IsType<ContentPresenter>(Assert.Single(second.GetVisualChildren()));
                Assert.NotSame(firstPresenter, secondPresenter);
                Assert.Equal(14d, firstText.FontSize);
                Assert.Equal(14d, secondText.FontSize);
                Assert.Equal("PART_ContentPresenter", firstPresenter.Name);
                Assert.Equal("PART_ContentPresenter", secondPresenter.Name);
                Assert.Equal("First", firstPresenter.Content);
                Assert.Equal("Second", secondPresenter.Content);
                Assert.Equal(Brushes.Red, firstPresenter.Background);
                Assert.Equal(Brushes.Green, secondPresenter.Background);
                Assert.Equal(new Thickness(4d), firstPresenter.Padding);
                first.Padding = new Thickness(8d);
                Assert.Equal(new Thickness(8d), firstPresenter.Padding);

                var pseudo = (IPseudoClasses)first.Classes;
                pseudo.Add(":pointerover");
                Assert.Equal(Brushes.Blue, firstPresenter.Background);
                firstWindow.Resources["ButtonBackgroundPointerOver"] = Brushes.Cyan;
                Assert.Equal(Brushes.Cyan, firstPresenter.Background);
                pseudo.Add(":pressed");
                Assert.Equal(Brushes.Purple, firstPresenter.Background);
                firstPresenter.Background = Brushes.White;
                Assert.Equal(Brushes.White, firstPresenter.Background);
                firstPresenter.ClearValue(ContentPresenter.BackgroundProperty);
                Assert.Equal(Brushes.Purple, firstPresenter.Background);
                pseudo.Remove(":pressed");
                Assert.Equal(Brushes.Cyan, firstPresenter.Background);
                pseudo.Remove(":pointerover");
                Assert.Equal(Brushes.Red, firstPresenter.Background);
                Assert.Equal(Brushes.Green, secondPresenter.Background);

                var factory = Assert.IsAssignableFrom<IControlTemplate>(Assert.Single(theme.Setters.OfType<Setter>(),
                    setter => setter.Property == TemplatedControl.TemplateProperty).Value);
                var firstBuild = factory.Build(first)!;
                var secondBuild = factory.Build(second)!;
                Assert.NotSame(firstBuild.Result, secondBuild.Result);
                Assert.Same(firstBuild.Result, firstBuild.NameScope.Find("PART_ContentPresenter"));
                Assert.Same(secondBuild.Result, secondBuild.NameScope.Find("PART_ContentPresenter"));
            }
            finally
            {
                firstWindow.Close();
                secondWindow.Close();
            }
        }, CancellationToken.None);
    }

    private static void AddResources(Window window, IBrush background)
    {
        window.Resources["ButtonBackground"] = background;
        window.Resources["ButtonBackgroundPointerOver"] = Brushes.Blue;
        window.Resources["ButtonBackgroundPressed"] = Brushes.Purple;
    }

    private static GenerationFixture Generate((string Name, string Text)[] files, string host = "",
        ComponentGenerationMode mode = ComponentGenerationMode.ReleaseDirect, bool validateSemantics = true, bool useBlackSilence = false,
        MetadataReference[]? references = null)
    {
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (mode == ComponentGenerationMode.DebugStructural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }
        var path = Path.Combine(Path.GetTempPath(), "AkcssNativeGenerationTests");
        var compilation = CSharpCompilation.Create("AkcssNative_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(host, options)], SymbolTests.CreateAvaloniaReferences().Concat(references ?? []),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var trees = files.Select(file => file.Name.EndsWith(".akcss", StringComparison.Ordinal)
            ? (AkburaSyntaxTree)AkcssSyntaxTree.ParseText(SourceText.From(file.Text), Path.Combine(path, file.Name),
                AkcssGeneratedModuleNames.GetMetadataName(RootNamespace, file.Name))
            : AkburaSyntaxTree.ParseText(file.Text, Path.Combine(path, file.Name))).ToImmutableArray();
        var catalog = AkburaGenerationCatalogBuilder.Create(compilation, trees, RootNamespace, path);
        if (validateSemantics)
        {
            foreach (var tree in trees)
            {
                var model = catalog.Compilation.GetSemanticModel(tree);
                var errors = model.GetSemanticDiagnostics(tree.GetRootSyntax())
                    .Where(static diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error);
                Assert.True(!errors.Any(), string.Join(Environment.NewLine, errors.Select(static error => error.Message)));
            }
        }

        if (useBlackSilence)
        {
            var generator = new AkburaBlackSilenceGenerator();
            var additionalTexts = files.Select(file => (AdditionalText)new AkburaBlackSilenceGeneratorTests.TestAdditionalText(
                Path.Combine(path, file.Name), SourceText.From(file.Text))).ToImmutableArray();
            var driver = CSharpGeneratorDriver.Create(
                [generator.AsSourceGenerator()], additionalTexts, options,
                new AkburaBlackSilenceGeneratorTests.TestAnalyzerConfigOptionsProvider(RootNamespace, path));
            var run = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics).GetRunResult();
            Assert.DoesNotContain(diagnostics, static item => item.Severity == DiagnosticSeverity.Error);
            return new GenerationFixture((CSharpCompilation)output,
                string.Join(Environment.NewLine, run.GeneratedTrees.Select(static tree => tree.ToString())), catalog.Compilation, trees);
        }

        var generated = new List<SyntaxTree>();
        foreach (var input in catalog.ExternalAkcssModules.Concat(catalog.InlineAkcssModules))
        {
            generated.Add(CSharpSyntaxTree.ParseText(AkcssDocumentWriter.Generate(input, catalog.AkcssSourceMap,
                RootNamespace, generationMode: mode), options, AkcssDocumentWriter.GetHintName(input)));
        }
        foreach (var input in catalog.Components)
        {
            generated.Add(CSharpSyntaxTree.ParseText(ComponentDocumentWriter.Generate(input.Component, input.SemanticModel,
                input.SourcePath, catalog.AkcssModuleTypeNames, mode: mode),
                options, ComponentDocumentWriter.GetHintName(input.Component, input.SourcePath)));
        }

        return new GenerationFixture(compilation.AddSyntaxTrees(generated),
            string.Join(Environment.NewLine, generated.Select(static tree => tree.ToString())), catalog.Compilation, trees);
    }

    private sealed record GenerationFixture(CSharpCompilation Compilation, string Text,
        AkburaCompilation SemanticCompilation, ImmutableArray<AkburaSyntaxTree> Trees)
    {
        public AkburaDiagnosticRecord[] GetGenerationDiagnostics() => Trees
            .SelectMany(tree => AkburaDiagnosticEngine.Collect(tree, SemanticCompilation.GetSemanticModel(tree)))
            .Where(static item => item.Kind == AkburaDiagnosticKind.Generation)
            .ToArray();

        public Assembly Compile() => Assembly.Load(Emit());

        public byte[] Emit()
        {
            using var output = new MemoryStream();
            var result = Compilation.Emit(output);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics) + Environment.NewLine + Text);
            return output.ToArray();
        }
    }
}
