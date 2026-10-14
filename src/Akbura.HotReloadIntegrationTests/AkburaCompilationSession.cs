using Akbura.BlackSilence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Akbura.HotReloadIntegrationTests;

/// <summary>
/// Runs the production incremental generator over successive versions of one additional file.
/// </summary>
internal sealed class AkburaCompilationSession
{
    private readonly CSharpCompilation _baseCompilation;
    private GeneratorDriver _driver;
    private AdditionalText _source;
    private readonly Dictionary<string, AdditionalText> _stylesheetSources = new(StringComparer.OrdinalIgnoreCase);

    public AkburaCompilationSession(
        string componentSource,
        string ownerSource,
        IReadOnlyDictionary<string, string>? stylesheetSources = null)
    {
        var projectDirectory = Path.Combine(Path.GetTempPath(), "AkburaClrHotReload", Guid.NewGuid().ToString("N"));
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)
            .WithPreprocessorSymbols("DEBUG");
        _baseCompilation = CSharpCompilation.Create("AkburaClrHotReload_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(SourceText.From(ownerSource, Encoding.UTF8), options,
                Path.Combine(projectDirectory, "Counter.akbura.cs"))],
            CreateReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, nullableContextOptions: NullableContextOptions.Enable));
        _source = new ComponentSource(Path.Combine(projectDirectory, "Counter.akbura"), componentSource);
        var additionalFiles = new List<AdditionalText> { _source };
        if (stylesheetSources != null)
        {
            foreach (var (path, text) in stylesheetSources)
            {
                var file = new ComponentSource(Path.Combine(projectDirectory, path), text);
                _stylesheetSources.Add(path, file);
                additionalFiles.Add(file);
            }
        }
        _driver = CSharpGeneratorDriver.Create(
            [new AkburaBlackSilenceGenerator().AsSourceGenerator()], additionalFiles, options,
            new ProjectOptionsProvider(projectDirectory));
        Compilation = Generate();
    }

    public Compilation Compilation { get; private set; }

    public Compilation Update(string componentSource)
    {
        var updated = new ComponentSource(_source.Path, componentSource);
        _driver = _driver.ReplaceAdditionalText(_source, updated);
        _source = updated;
        Compilation = Generate();
        return Compilation;
    }

    public Compilation UpdateAkcss(string path, string source)
    {
        var previous = _stylesheetSources[path];
        var updated = new ComponentSource(previous.Path, source);
        _driver = _driver.ReplaceAdditionalText(previous, updated);
        _stylesheetSources[path] = updated;
        Compilation = Generate();
        return Compilation;
    }

    private Compilation Generate()
    {
        _driver = _driver.RunGeneratorsAndUpdateCompilation(_baseCompilation, out var output, out var diagnostics);
        var result = Assert.Single(_driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.NotEmpty(result.GeneratedSources);
        AssertClean(diagnostics.Concat(result.Diagnostics).Concat(output.GetDiagnostics()));
        return output;
    }

    internal static void AssertClean(IEnumerable<Diagnostic> diagnostics)
    {
        var failures = diagnostics.Where(diagnostic => diagnostic.Severity is
            DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(failures.Length == 0, string.Join(Environment.NewLine, failures.AsEnumerable()));
    }

    private static IEnumerable<MetadataReference> CreateReferences()
    {
        var platformAssemblies = Assert.IsType<string>(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"));
        var paths = platformAssemblies.Split(Path.PathSeparator).ToHashSet(StringComparer.OrdinalIgnoreCase);
        paths.Add(typeof(AkburaControl).Assembly.Location);
        paths.Add(typeof(Avalonia.AvaloniaObject).Assembly.Location);
        paths.Add(typeof(Avalonia.Controls.Control).Assembly.Location);
        paths.Add(typeof(Avalonia.Markup.Xaml.Templates.DataTemplate).Assembly.Location);
        return paths.Select(path => MetadataReference.CreateFromFile(path));
    }

    private sealed class ComponentSource(string path, string source) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(source, Encoding.UTF8);
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }

    private sealed class ProjectOptionsProvider(string projectDirectory) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global = new Options(new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = "Demo",
            ["build_property.ProjectDir"] = projectDirectory,
        });
        private static readonly AnalyzerConfigOptions s_empty = new Options(new Dictionary<string, string>());
        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => s_empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => s_empty;
    }

    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
