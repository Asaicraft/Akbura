using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ResourceOutVariableRegressionTests
{
    // The casts and the two independent 'found' declarations intentionally
    // match NavIcon. Do not replace them with predeclared/default-valued locals.
    private const string Source = """
        using Avalonia.Controls;
        using Avalonia.Media;
        namespace Demo;

        state IBrush? activeColor = null;
        state IBrush? inactiveColor = null;

        if (activeColor == null)
        {
            if (this.TryFindResource("--color-teal-300", this.ActualThemeVariant, out var found))
            {
                activeColor = (IBrush)found;
            }
        }

        if (inactiveColor == null)
        {
            if (this.TryFindResource("--color-gray-500", this.ActualThemeVariant, out var found))
            {
                inactiveColor = (IBrush)found;
            }
        }

        <Border Background={activeColor} BorderBrush={inactiveColor} />
        """;

    private const string Owner = """
        using Akbura;
        using Akbura.Engine;
        using Avalonia.Controls;
        namespace Demo;
        public partial class PlannerView : AkburaControl
        {
            public PlannerView() : base(AkburaEngine.Empty) { }
        }
        """;

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TwoIndependentFoundVariables_CompileAndSetBothBrushes(bool structural, bool crlf)
    {
        var source = Source.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (crlf) source = source.Replace("\n", "\r\n", StringComparison.Ordinal);
        var type = CompileThroughPlanner(source, structural);

        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            owner.Resources["--color-teal-300"] = Brushes.Red;
            owner.Resources["--color-gray-500"] = Brushes.Blue;
            var window = new Window { Content = owner };
            try
            {
                // Exercise OnInitialized -> FirstUpdate -> Update -> commit,
                // rather than calling the construction-only override directly.
                window.Show();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var border = Assert.IsType<Border>(owner.Child);
                Assert.Same(Brushes.Red, border.Background);
                Assert.Same(Brushes.Blue, border.BorderBrush);

                owner.Resources["--color-teal-300"] = Brushes.Green;
                owner.Resources["--color-gray-500"] = Brushes.Black;
                // Deliberately request another completed render to test the
                // ordinary C# guards. This is not a DynamicResource hook test.
                owner.InvalidState();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Same(border, owner.Child);
                Assert.Same(Brushes.Red, border.Background);
                Assert.Same(Brushes.Blue, border.BorderBrush);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlackSilence_ColdAndChangedAdditionalText_KeepBothScopes(bool structural)
    {
        var parseOptions = ParseOptions(structural);
        var fixture = AkcssActivatorPlannerTests.CreateFixture(Source, Owner);
        var input = fixture.CSharpCompilation.WithAssemblyName("ResourceDriver_" + Guid.NewGuid().ToString("N"));
        var file = new SourceFile(Path.Combine(Path.GetTempPath(), "AkburaResourceRegression", "PlannerView.akbura"), Source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new AkburaBlackSilenceGenerator().AsSourceGenerator() },
            additionalTexts: new AdditionalText[] { file },
            parseOptions: parseOptions,
            optionsProvider: new OptionsProvider());

        driver = RunAndCheck(driver, input);

        // A reused driver must not keep a diagnostic or symbol from the old header.
        var changed = new SourceFile(file.Path, Source.Replace("found", "resolved", StringComparison.Ordinal));
        driver = driver.ReplaceAdditionalText(file, changed);
        driver = RunAndCheck(driver, input);
        driver = driver.ReplaceAdditionalText(changed, file);
        _ = RunAndCheck(driver, input);
    }

    [Fact]
    public void FoundDoesNotLeakOutsideItsContainingBlock()
    {
        var source = Source.Replace("<Border Background={activeColor}",
            "activeColor = (IBrush)found;\n<Border Background={activeColor}", StringComparison.Ordinal);
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, Owner);
        var errors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(d => d.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.NotEmpty(errors);
        Assert.Contains(errors, d => d.Code == "AKBURA_SEMANTIC_CSharpExpressionError" &&
            d.Message.Contains("found", StringComparison.Ordinal));
    }

    private static GeneratorDriver RunAndCheck(GeneratorDriver driver, CSharpCompilation input)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        AssertNoErrors(diagnostics);
        AssertNoErrors(result.Diagnostics);
        AssertNoErrors(output.GetDiagnostics());
        var generated = Assert.Single(result.GeneratedSources.Where(s =>
            s.HintName.StartsWith("Akbura.Component.", StringComparison.Ordinal)));

        // The compiler must not 'succeed' by dropping the offending if bodies.
        Assert.Contains("--color-teal-300", generated.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("--color-gray-500", generated.SourceText.ToString(), StringComparison.Ordinal);
        using var stream = new MemoryStream();
        var emitted = output.Emit(stream);
        Assert.True(emitted.Success, Format(emitted.Diagnostics));
        return driver;
    }

    private static Type CompileThroughPlanner(string source, bool structural)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, Owner);
        var root = fixture.ComponentTree.GetRoot();
        Assert.Empty(root.DescendantNodesAndTokensAndSelf(descendIntoTrivia: true)
            .SelectMany(node => node.GetDiagnostics()));
        var semanticErrors = fixture.SemanticModel.GetSemanticDiagnostics(root)
            .Where(d => d.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.True(semanticErrors.Length == 0,
            string.Join(Environment.NewLine, semanticErrors.Select(d => d.Code + ": " + d.Message)));
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(fixture.SemanticModel.GetSymbolInfo(root).Symbol);
        var generated = ComponentDocumentWriter.Generate(component, fixture.SemanticModel,
            "Views/PlannerView.akbura", new Dictionary<AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var compilation = fixture.CSharpCompilation
            .WithAssemblyName("ResourceScopes_" + Guid.NewGuid().ToString("N"))
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated, ParseOptions(structural), "ResourceScopes.g.cs"));
        AssertNoErrors(compilation.GetDiagnostics());
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, Format(emitted.Diagnostics) + Environment.NewLine + generated);
        return Assert.IsAssignableFrom<Type>(Assembly.Load(stream.ToArray()).GetType("Demo.PlannerView"));
    }

    private static CSharpParseOptions ParseOptions(bool structural)
    {
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        return structural ? options.WithPreprocessorSymbols("DEBUG") : options;
    }

    private static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, Format(errors));
    }

    private static string Format(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));

    private sealed class SourceFile(string path, string source) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(source, System.Text.Encoding.UTF8);
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }

    private sealed class OptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions s_options = new Options();
        public override AnalyzerConfigOptions GlobalOptions => s_options;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_options;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_options;
    }

    private sealed class Options : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = key switch
            {
                "build_property.RootNamespace" => "Demo",
                "build_property.MSBuildProjectDirectory" => Path.Combine(Path.GetTempPath(), "AkburaResourceRegression"),
                "build_property.AkburaBlackSilenceDiagnostics" => "Publish",
                "build_property.DesignTimeBuild" => "false",
                _ => string.Empty,
            };
            return value.Length != 0;
        }
    }
}
