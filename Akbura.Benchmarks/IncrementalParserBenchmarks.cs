using Akbura.Language;
using Akbura.Language.Syntax;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Akbura.Benchmarks;

#if ENABLE_QUICK_SCAN_BENCHMARK
[Config(typeof(IncrementalParserBenchmarkConfig))]
[MemoryDiagnoser]
public class IncrementalParserBenchmarks
{
    private SourceText _newSource = null!;
    private AkburaDocumentSyntax _oldTree = null!;
    private TextChangeRange[] _changes = null!;

    [Params(80, 800, 8_000)]
    public int Repetitions { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var oldCode = BuildComponentCorpus(Repetitions, changedValue: 0);
        var newCode = BuildComponentCorpus(Repetitions, changedValue: 1);
        var oldSource = SourceText.From(oldCode);
        _newSource = SourceText.From(newCode);

        using var oldLexer = new Lexer(oldSource);
        using var oldParser = new Parser(oldLexer, default);
        _oldTree = (AkburaDocumentSyntax)oldParser.ParseCompilationUnit().CreateRed();

        var oldSnippet = "state int changedCounter = 0;";
        var newSnippet = "state int changedCounter = 1;";
        var changeStart = oldCode.IndexOf(oldSnippet, StringComparison.Ordinal);
        _changes =
        [
            new TextChangeRange(
                new TextSpan(changeStart, oldSnippet.Length),
                newSnippet.Length)
        ];
    }

    [Benchmark(Baseline = true)]
    public int FullParseAfterEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count;
    }

    [Benchmark]
    public int IncrementalParseAfterEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default, _oldTree, _changes);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count;
    }

    private static string BuildComponentCorpus(int repetitions, int changedValue)
    {
        var builder = new StringBuilder();

        builder.AppendLine("using System;");
        builder.AppendLine("global using static System.Math;");
        builder.AppendLine("namespace Benchmark.App;");
        builder.AppendLine();
        builder.AppendLine("@akcss {");
        builder.AppendLine("\t.card { Padding: 12; MarginLeft: 4; MarginTop: 2; }");
        builder.AppendLine("\t@utilities {");
        builder.AppendLine("\t\t.w-(double width) { Width: width * Spacing; }");
        builder.AppendLine("\t\t.gap-(int value) { RowGap: value * Spacing; }");
        builder.AppendLine("\t}");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("state bool isOpen = false;");
        builder.AppendLine();

        for (var i = 0; i < repetitions; i++)
        {
            builder.AppendLine("state int item" + i + " = " + i + ";");
            builder.AppendLine();
            builder.AppendLine("<StackPanel class=\"card\" gap-4 {isOpen}:hidden>");
            builder.AppendLine("\t<TextBlock Text=\"Item " + i + "\" />");
            builder.AppendLine("\t<Button OnClick={changedCounter++} class=\"primary\" w-30>");
            builder.AppendLine("\t\tIncrement");
            builder.AppendLine("\t</Button>");
            builder.AppendLine("</StackPanel>");
            builder.AppendLine();
        }

        builder.AppendLine("state int changedCounter = " + changedValue + ";");

        return builder.ToString();
    }
}

public sealed class IncrementalParserBenchmarkConfig : ManualConfig
{
    public IncrementalParserBenchmarkConfig()
    {
        AddJob(
            Job.LongRun
                .WithMsBuildArguments(
                    "/p:EnableQuickScanBenchmark=true",
                    "/p:EnableAkburaStats=true",
                    "/p:DefineConstants=ENABLE_QUICK_SCAN_BENCHMARK"));
    }
}
#endif
