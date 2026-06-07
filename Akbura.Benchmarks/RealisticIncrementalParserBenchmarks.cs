using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.TestUtilities;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Benchmarks;

#if ENABLE_QUICK_SCAN_BENCHMARK
[Config(typeof(RealisticIncrementalParserBenchmarkConfig))]
[MemoryDiagnoser]
public class RealisticIncrementalParserBenchmarks
{
    private SourceText _newSource = null!;
    private AkburaDocumentSyntax _oldTree = null!;
    private TextChangeRange[] _changes = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var scenario = RealisticIncrementalScenario.Create(stableSectionCount: 160);
        var oldSource = SourceText.From(scenario.OldCode);
        _newSource = SourceText.From(scenario.NewCode);
        _changes = scenario.Changes;

        using var oldLexer = new Lexer(oldSource);
        using var oldParser = new Parser(oldLexer, default);
        _oldTree = (AkburaDocumentSyntax)oldParser.ParseCompilationUnit().CreateRed();
    }

    [Benchmark(Baseline = true)]
    public int FullReparseAfterRealisticInvalidEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count + (syntax.ContainsDiagnostics ? 17 : 0);
    }

    [Benchmark]
    public int IncrementalParseAfterRealisticInvalidEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default, _oldTree, _changes);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count + (syntax.ContainsDiagnostics ? 17 : 0);
    }

}

public sealed class RealisticIncrementalParserBenchmarkConfig : ManualConfig
{
    public RealisticIncrementalParserBenchmarkConfig()
    {
        AddJob(
            Job.Default
                .WithMsBuildArguments(
                    "/p:EnableQuickScanBenchmark=true",
                    "/p:EnableAkburaStats=true",
                    "/p:DefineConstants=ENABLE_QUICK_SCAN_BENCHMARK"));
    }
}
#endif
