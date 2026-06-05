using Akbura.Language;
using Akbura.Language.Syntax;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Akbura.Benchmarks;

#if ENABLE_QUICK_SCAN_BENCHMARK
[Config(typeof(CSharpBlockIncrementalParserBenchmarkConfig))]
[MemoryDiagnoser]
public class CSharpBlockIncrementalParserBenchmarks
{
    private SourceText _newSource = null!;
    private AkburaDocumentSyntax _oldTree = null!;
    private TextChangeRange[] _changes = null!;

    [Params(40, 200, 1_000)]
    public int StatementCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var oldCode = BuildBlockCorpus(StatementCount, changedName: "oldValue");
        var newCode = BuildBlockCorpus(StatementCount, changedName: "newValue");
        var oldSource = SourceText.From(oldCode);
        _newSource = SourceText.From(newCode);

        using var oldLexer = new Lexer(oldSource);
        using var oldParser = new Parser(oldLexer, default);
        _oldTree = (AkburaDocumentSyntax)oldParser.ParseCompilationUnit().CreateRed();

        var changedLine = $"var oldValue = {StatementCount / 2};";
        var newLine = $"var newValue = {StatementCount / 2};";
        var changeStart = oldCode.IndexOf(changedLine, StringComparison.Ordinal);
        _changes =
        [
            new TextChangeRange(
                new TextSpan(changeStart, changedLine.Length),
                newLine.Length)
        ];
    }

    [Benchmark(Baseline = true)]
    public int FullParseAfterCSharpBlockEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count;
    }

    [Benchmark]
    public int IncrementalParseAfterCSharpBlockEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default, _oldTree, _changes);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count;
    }

    private static string BuildBlockCorpus(int statementCount, string changedName)
    {
        var builder = new StringBuilder();
        var changedStatement = statementCount / 2;

        builder.AppendLine("state bool isOpen = true;");
        builder.AppendLine();
        builder.AppendLine("if(isOpen)");
        builder.AppendLine("{");

        for (var i = 0; i < statementCount; i++)
        {
            var name = i == changedStatement ? changedName : "value" + i;

            builder.AppendLine($"\tvar {name} = {i};");

            if (i % 10 == 0)
            {
                builder.AppendLine($"\t<TextBlock Text=\"Item {i}\"/>");
            }
        }

        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("<Button OnClick={isOpen = false}>Close</Button>");

        return builder.ToString();
    }
}

public sealed class CSharpBlockIncrementalParserBenchmarkConfig : ManualConfig
{
    public CSharpBlockIncrementalParserBenchmarkConfig()
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
