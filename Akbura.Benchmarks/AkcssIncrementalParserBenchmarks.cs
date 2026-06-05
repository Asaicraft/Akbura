using Akbura.Language;
using Akbura.Language.Syntax;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Akbura.Benchmarks;

#if ENABLE_QUICK_SCAN_BENCHMARK
[Config(typeof(AkcssIncrementalParserBenchmarkConfig))]
[MemoryDiagnoser]
public class AkcssIncrementalParserBenchmarks
{
    private SourceText _newSource = null!;
    private AkburaDocumentSyntax _oldTree = null!;
    private TextChangeRange[] _changes = null!;

    [Params(40, 200, 1_000)]
    public int RuleCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var oldCode = BuildAkcssCorpus(RuleCount, changedColor: "Primary");
        var newCode = BuildAkcssCorpus(RuleCount, changedColor: "Accent");
        var oldSource = SourceText.From(oldCode);
        _newSource = SourceText.From(newCode);

        using var oldLexer = new Lexer(oldSource);
        using var oldParser = new Parser(oldLexer, default);
        _oldTree = (AkburaDocumentSyntax)oldParser.ParseCompilationUnit().CreateRed();

        const string oldSnippet = "Background: \"Primary\";";
        const string newSnippet = "Background: \"Accent\";";
        var changeStart = oldCode.IndexOf(oldSnippet, StringComparison.Ordinal);
        _changes =
        [
            new TextChangeRange(
                new TextSpan(changeStart, oldSnippet.Length),
                newSnippet.Length)
        ];
    }

    [Benchmark(Baseline = true)]
    public int FullParseAfterAkcssEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count;
    }

    [Benchmark]
    public int IncrementalParseAfterAkcssEdit()
    {
        using var lexer = new Lexer(_newSource);
        using var parser = new Parser(lexer, default, _oldTree, _changes);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth + syntax.Members.Count;
    }

    private static string BuildAkcssCorpus(int ruleCount, string changedColor)
    {
        var builder = new StringBuilder();
        var changedRule = ruleCount / 2;

        builder.AppendLine("@akcss {");

        for (var i = 0; i < ruleCount; i++)
        {
            var background = i == changedRule ? changedColor : "White";

            builder.AppendLine($"\t.card{i} {{");
            builder.AppendLine($"\t\tPadding: {i % 16};");
            builder.AppendLine($"\t\tBackground: \"{background}\";");
            builder.AppendLine("\t\t@if(IsHovered) {");
            builder.AppendLine("\t\t\tBorderBrush: \"DodgerBlue\";");
            builder.AppendLine("\t\t\tOpacity: 0.95;");
            builder.AppendLine("\t\t}");
            builder.AppendLine("\t}");
        }

        builder.AppendLine();
        builder.AppendLine("\t@utilities {");

        for (var i = 0; i < ruleCount; i++)
        {
            builder.AppendLine($"\t\t.gap{i}-(double value) {{");
            builder.AppendLine("\t\t\tRowGap: value * Spacing;");
            builder.AppendLine("\t\t\tColumnGap: value * Spacing;");
            builder.AppendLine("\t\t}");
        }

        builder.AppendLine("\t}");
        builder.AppendLine("}");

        return builder.ToString();
    }
}

public sealed class AkcssIncrementalParserBenchmarkConfig : ManualConfig
{
    public AkcssIncrementalParserBenchmarkConfig()
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
