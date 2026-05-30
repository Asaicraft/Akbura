using Akbura.Language;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Akbura.Benchmarks;

internal static class Program
{
    public static void Main(string[] args)
    {
#if ENABLE_QUICK_SCAN_BENCHMARK
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
#else
		Console.WriteLine("Quick scan benchmark is disabled. Run with /p:EnableQuickScanBenchmark=true.");
#endif
    }
}

#if ENABLE_QUICK_SCAN_BENCHMARK
[Config(typeof(QuickScanBenchmarkConfig))]
[MemoryDiagnoser]
public class LexerQuickScannerBenchmarks
{
    private SourceText _source = null!;

    [Params(80, 800, 8_000)]
    public int Repetitions { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _source = SourceText.From(BuildComponentCorpus(Repetitions));
    }

    [Benchmark(Baseline = true)]
    public int ParseWithQuickScannerDisabled()
    {
        return Parse(enableQuickScanner: false);
    }

    [Benchmark]
    public int ParseWithQuickScannerEnabled()
    {
        return Parse(enableQuickScanner: true);
    }

    private int Parse(bool enableQuickScanner)
    {
        using var lexer = new Lexer(_source, enableQuickScanner);
        using var parser = new Parser(lexer, default);
        var syntax = parser.ParseCompilationUnit();

        return syntax.FullWidth;
    }

    private static string BuildComponentCorpus(int repetitions)
    {
        var builder = new StringBuilder();

        builder.AppendLine("using System;");
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
        builder.AppendLine("state int count = 0;");
        builder.AppendLine("state bool isOpen = false;");
        builder.AppendLine();

        for (var i = 0; i < repetitions; i++)
        {
            builder.AppendLine($"<StackPanel class=\"card\" gap-4 w-{i % 40} {{isOpen}}:hidden>");
            builder.AppendLine($"\t<TextBlock Text=\"Item {i}\" />");
            builder.AppendLine("\t<Button OnClick={count++} class=\"primary\" w-30>");
            builder.AppendLine("\t\tIncrement");
            builder.AppendLine("\t</Button>");
            builder.AppendLine("</StackPanel>");
            builder.AppendLine();
        }

        return builder.ToString();
    }
}

public sealed class QuickScanBenchmarkConfig : ManualConfig
{
    public QuickScanBenchmarkConfig()
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
