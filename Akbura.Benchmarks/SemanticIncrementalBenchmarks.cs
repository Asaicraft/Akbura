using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Language.Symbols;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Akbura.Benchmarks;

[Config(typeof(SemanticIncrementalBenchmarkConfig))]
[MemoryDiagnoser]
public class SemanticIncrementalBenchmarks
{
    private string _newCode = null!;
    private AkburaCompilation _oldCompilation = null!;
    private AkburaSyntaxTree _incrementalTree = null!;

    [Params(40, 200)]
    public int StateCount { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        var oldCode = BuildStateCorpus(StateCount, changedValue: 0);
        _newCode = BuildStateCorpus(StateCount, changedValue: 1);
        var oldTree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");

        var oldSnippet = "state int changedCounter = 0;";
        var newSnippet = "state int changedCounter = 1;";
        var changeStart = oldCode.IndexOf(oldSnippet, StringComparison.Ordinal);
        var change = new TextChangeRange(
            new TextSpan(changeStart, oldSnippet.Length),
            newSnippet.Length);

        _incrementalTree = oldTree.WithChangedText(SourceText.From(_newCode), [change]);
        _oldCompilation = CreateCompilation(oldTree);

        // Prime the previous semantic model so the incremental compilation has
        // real cached symbols/operations to reuse.
        _ = QueryComponent(_oldCompilation, oldTree);
    }

    [Benchmark(Baseline = true)]
    public int FullSemanticAfterEdit()
    {
        var tree = AkburaSyntaxTree.ParseText(_newCode, "Counter.akbura");
        var compilation = CreateCompilation(tree);

        return QueryComponent(compilation, tree);
    }

    [Benchmark]
    public int IncrementalSemanticAfterEdit()
    {
        var compilation = _oldCompilation.WithSyntaxTrees([_incrementalTree]);

        return QueryComponent(compilation, _incrementalTree);
    }

    private static int QueryComponent(AkburaCompilation compilation, AkburaSyntaxTree tree)
    {
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var symbol = (IAkburaComponentSymbol?)model.GetSymbolInfo(root).Symbol;

        var checksum = root.FullWidth + root.Members.Count;
        if (symbol != null)
        {
            checksum += symbol.States.Length * 31;
            checksum += symbol.Name.Length;
        }

        foreach (var member in root.Members)
        {
            if (member is MarkupRootSyntax markupRoot &&
                     markupRoot.Element.StartTag?.Attributes.Count > 0)
            {
                var operation = model.GetOperation(markupRoot.Element.StartTag.Attributes[0]);
                checksum += operation == null ? 0 : (int)operation.Kind;
            }
        }

        return checksum;
    }

    private static AkburaCompilation CreateCompilation(AkburaSyntaxTree tree)
    {
        return new AkburaCompilation(
            CreateCSharpCompilation(),
            [tree],
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
    }

    private static CSharpCompilation CreateCSharpCompilation()
    {
        return CSharpCompilation.Create(
            "SemanticIncrementalBenchmarks",
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string BuildStateCorpus(int stateCount, int changedValue)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System;");
        builder.AppendLine("namespace Demo;");
        builder.AppendLine();
        builder.AppendLine("param int Value = 0;");

        for (var i = 0; i < stateCount; i++)
        {
            builder.Append("state int item");
            builder.Append(i);
            builder.Append(" = ");
            builder.Append(i);
            builder.AppendLine(";");
        }

        builder.Append("state int changedCounter = ");
        builder.Append(changedValue);
        builder.AppendLine(";");
        builder.AppendLine();
        builder.AppendLine("<Counter Value={item0} />");
        return builder.ToString();
    }
}

public sealed class SemanticIncrementalBenchmarkConfig : ManualConfig
{
    public SemanticIncrementalBenchmarkConfig()
    {
        AddJob(
            Job.ShortRun.WithMsBuildArguments(
                "/p:EnableQuickScanBenchmark=true",
                "/p:EnableAkburaStats=true",
                "/p:DefineConstants=ENABLE_QUICK_SCAN_BENCHMARK",
                "/p:EnableSourceLink=false",
                "/p:ContinuousIntegrationBuild=false"));
    }
}
