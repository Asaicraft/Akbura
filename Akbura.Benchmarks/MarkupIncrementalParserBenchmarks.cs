using Akbura.Language;
using Akbura.Language.Syntax;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Akbura.Benchmarks;

#if ENABLE_QUICK_SCAN_BENCHMARK
[Config(typeof(MarkupIncrementalParserBenchmarkConfig))]
[MemoryDiagnoser]
public class MarkupIncrementalParserBenchmarks
{
    private SourceText _newSource = null!;
    private AkburaDocumentSyntax _oldTree = null!;
    private TextChangeRange[] _changes = null!;

    [Params(40, 200, 1_000)]
    public int ChildCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var oldCode = BuildComponentCorpus(ChildCount, changedClass: "primary");
        var newCode = BuildComponentCorpus(ChildCount, changedClass: "accent");
        var oldSource = SourceText.From(oldCode);
        _newSource = SourceText.From(newCode);

        using var oldLexer = new Lexer(oldSource);
        using var oldParser = new Parser(oldLexer, default);
        _oldTree = (AkburaDocumentSyntax)oldParser.ParseCompilationUnit().CreateRed();

        var oldText = "class=\"primary\"";
        var newText = "class=\"accent\"";
        var changeStart = oldCode.IndexOf(oldText, StringComparison.Ordinal);
        _changes =
        [
            new TextChangeRange(
                new TextSpan(changeStart, oldText.Length),
                newText.Length)
        ];
    }

    [Benchmark(Baseline = true)]
    public int FullParseAfterMarkupEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count;
    }

    [Benchmark]
    public int IncrementalParseAfterMarkupEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default, _oldTree, _changes);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count;
    }

    private static string BuildComponentCorpus(int childCount, string changedClass)
    {
        var builder = new StringBuilder();

        builder.AppendLine("using System;");
        builder.AppendLine("namespace Benchmark.Markup;");
        builder.AppendLine();
        builder.AppendLine("state bool isOpen = true;");
        builder.AppendLine("state int count = 0;");
        builder.AppendLine();
        builder.AppendLine("<StackPanel class=\"card\" gap-4 p-4>");

        for (var i = 0; i < childCount; i++)
        {
            var itemClass = i == childCount / 2 ? changedClass : "item";
            builder.AppendLine($"\t<Row class=\"{itemClass}\" gap-2 IsVisible={{isOpen}}>");
            builder.AppendLine($"\t\t<TextBlock Text=\"Item {i}\" class=\"label\"/>");
            builder.AppendLine("\t\t<Button OnClick={count++} class=\"button\" w-30>");
            builder.AppendLine("\t\t\tIncrement");
            builder.AppendLine("\t\t</Button>");
            builder.AppendLine("\t</Row>");
        }

        builder.AppendLine("</StackPanel>");

        return builder.ToString();
    }
}

public sealed class MarkupIncrementalParserBenchmarkConfig : ManualConfig
{
    public MarkupIncrementalParserBenchmarkConfig()
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
