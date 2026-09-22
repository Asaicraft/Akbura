using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Collections;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AppShellPageListTests
{
    private const string MainViewSource =
        """
        using Avalonia.Controls;

        namespace Demo;

        <Border />
        """;

    private const string AppShellSource =
        """
        using Demo;
        using Avalonia.Controls;
        using PageList = Avalonia.Collections.AvaloniaList<Avalonia.Controls.Page>;

        namespace Demo;

        inject MainViewModel Vm;

        <PageNavigationHost
            DataContext={Vm}
            x.DataType="MainViewModel">

            <PageNavigationHost.Page>
                <TabbedPage>
                    <TabbedPage.Pages>
                        <PageList>
                            <ContentPage Header="Home">
                                <MainView DataContext={Vm} />
                            </ContentPage>
                            <ContentPage Header="Settings">
                                <TextBlock
                                    Text="Settings for your Akbura application"
                                    Margin="24" />
                            </ContentPage>
                        </PageList>
                    </TabbedPage.Pages>
                </TabbedPage>
            </PageNavigationHost.Page>
        </PageNavigationHost>
        """;

    private const string ViewModelSource =
        """
        namespace Demo;

        public sealed class MainViewModel
        {
        }

        public partial class MainView : global::Akbura.AkburaControl
        {
            public MainView()
                : base(global::Akbura.Engine.AkburaEngine.Empty)
            {
            }
        }

        public partial class AppShell : global::Akbura.AkburaControl
        {
            public AppShell()
                : base(global::Akbura.Engine.AkburaEngine.Empty)
            {
            }
        }
        """;

    [Fact]
    public void PageListAlias_BindsConcreteAvaloniaListOfPages()
    {
        var compilation = CreateCSharpCompilation(debug: false);
        var mainView = ComponentSyntaxTree.ParseText(
            MainViewSource,
            GetComponentPath("MainView.akbura"));
        var appShell = ComponentSyntaxTree.ParseText(
            AppShellSource,
            GetComponentPath("AppShell.akbura"));
        var semanticModel = new AkburaCompilation(
            compilation,
            [mainView, appShell]).GetSemanticModel(appShell);
        var pageList = Assert.Single(
            appShell.GetRoot().DescendantNodes()
                .OfType<MarkupElementSyntax>(),
            static element =>
                element.StartTag?.Name.ToFullString().Trim() == "PageList");

        var rootElement = GetOnlyMarkupElement(appShell);
        Assert.Empty(semanticModel.GetSemanticDiagnostics(rootElement));
        var symbol = Assert.IsType<MarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(pageList).Symbol);
        var type = Assert.IsAssignableFrom<INamedTypeSymbol>(
            symbol.CSharpDefinition.Symbol);

        Assert.Equal(
            "global::Avalonia.Collections.AvaloniaList<global::Avalonia.Controls.Page>",
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.True(symbol.ContentModel.IsCollection);
        Assert.Equal(
            "global::Avalonia.Controls.Page",
            symbol.ContentModel.AllowedChildType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageListAlias_GeneratesAndRealizesNativePages(bool debug)
    {
        var compilation = CreateCSharpCompilation(debug);
        var parseOptions = CreateParseOptions(debug);
        var projectDirectory = GetProjectDirectory();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaBlackSilenceGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    GetComponentPath("MainView.akbura"),
                    SourceText.From(MainViewSource)),
                new TestAdditionalText(
                    GetComponentPath("AppShell.akbura"),
                    SourceText.From(AppShellSource)),
            ],
            parseOptions: parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(
                "Demo",
                projectDirectory));

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(
            updatedCompilation.GetDiagnostics(),
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        using var assemblyStream = new MemoryStream();
        var emitResult = updatedCompilation.Emit(assemblyStream);
        Assert.True(
            emitResult.Success,
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        var assembly = Assembly.Load(assemblyStream.ToArray());

        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(
            () =>
            {
                var viewModel = Activator.CreateInstance(
                    assembly.GetType("Demo.MainViewModel", throwOnError: true)!)!;
                var shell = Assert.IsAssignableFrom<AkburaControl>(
                    Activator.CreateInstance(
                        assembly.GetType("Demo.AppShell", throwOnError: true)!));
                shell.GetType().GetProperty("Vm")!.SetValue(shell, viewModel);
                shell.DataContext = viewModel;
                var window = new Window { Content = shell };

                try
                {
                    window.Show();
                    var host = Assert.IsType<PageNavigationHost>(shell.Child);
                    Assert.Same(viewModel, host.DataContext);
                    var tabs = Assert.IsType<TabbedPage>(host.Page);
                    var pages = Assert.IsType<AvaloniaList<Page>>(tabs.Pages);
                    Assert.Equal(2, pages.Count);
                    var home = Assert.IsType<ContentPage>(pages[0]);
                    var settings = Assert.IsType<ContentPage>(pages[1]);
                    Assert.Equal("Home", home.Header);
                    Assert.Equal("Settings", settings.Header);
                    var mainView = Assert.IsAssignableFrom<AkburaControl>(home.Content);
                    Assert.Same(viewModel, mainView.DataContext);
                    var settingsText = Assert.IsType<TextBlock>(settings.Content);
                    Assert.Equal(
                        "Settings for your Akbura application",
                        settingsText.Text);
                    Assert.Equal(new Avalonia.Thickness(24), settingsText.Margin);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public void PageListAlias_RejectsTextBlockChild()
    {
        const string source =
            """
            using Avalonia.Controls;
            using PageList = Avalonia.Collections.AvaloniaList<Avalonia.Controls.Page>;

            <PageList>
                <TextBlock />
            </PageList>
            """;
        var syntaxTree = ComponentSyntaxTree.ParseText(
            source,
            GetComponentPath("InvalidPageList.akbura"));
        var semanticModel = new AkburaCompilation(
            CreateCSharpCompilation(debug: false),
            [syntaxTree]).GetSemanticModel(syntaxTree);
        var rootElement = GetOnlyMarkupElement(syntaxTree);
        var diagnostic = Assert.Single(
            semanticModel.GetSemanticDiagnostics(rootElement));

        Assert.Equal(
            ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild,
            diagnostic.Code);
        Assert.Contains("TextBlock", diagnostic.Message);
        Assert.Contains("Page", diagnostic.Message);
    }

    private static CSharpCompilation CreateCSharpCompilation(bool debug)
    {
        return CSharpCompilation.Create(
            "AppShellPageListTests_" + (debug ? "Debug" : "Release"),
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    ViewModelSource,
                    CreateParseOptions(debug)),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static CSharpParseOptions CreateParseOptions(bool debug)
    {
        var options = CSharpParseOptions.Default.WithLanguageVersion(
            LanguageVersion.Preview);
        return debug
            ? options.WithPreprocessorSymbols("DEBUG")
            : options;
    }

    private static string GetProjectDirectory()
    {
        return Path.Combine(
            Path.GetTempPath(),
            nameof(AppShellPageListTests));
    }

    private static string GetComponentPath(string fileName)
    {
        return Path.Combine(GetProjectDirectory(), fileName);
    }

    private static MarkupElementSyntax GetOnlyMarkupElement(
        AkburaSyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRoot();
        var markupRoot = Assert.IsType<MarkupRootSyntax>(
            root.Members.Single(member => member is MarkupRootSyntax));
        return markupRoot.Element;
    }

    private sealed class TestAdditionalText(
        string path,
        SourceText sourceText) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(
            CancellationToken cancellationToken = default)
        {
            return sourceText;
        }
    }

    private sealed class TestAnalyzerConfigOptionsProvider(
        string rootNamespace,
        string projectDirectory) : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions s_empty =
            new TestAnalyzerConfigOptions(
                new Dictionary<string, string>());

        private readonly AnalyzerConfigOptions _global =
            new TestAnalyzerConfigOptions(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["build_property.RootNamespace"] = rootNamespace,
                    ["build_property.ProjectDir"] = projectDirectory,
                });

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return s_empty;
        }

        public override AnalyzerConfigOptions GetOptions(
            AdditionalText textFile)
        {
            return s_empty;
        }
    }

    private sealed class TestAnalyzerConfigOptions(
        IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (values.TryGetValue(key, out var result))
            {
                value = result;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
