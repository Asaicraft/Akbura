using Akbura.BlackSilence;
using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Akbura.UnitTests;

public sealed class AkburaBlackSilenceGeneratorTests
{
    private const string SyntaxTreesTrackingName = "BlackSilence.SyntaxTrees";

    [Fact]
    public void UpdatingOneAdditionalFile_ReparsesOnlyThatFile()
    {
        var a = new TestAdditionalText(
            "A.akbura",
            SourceText.From(
                "using System;\r\n" +
                "<Border />"));
        var oldBText = SourceText.From(
            "using System;\r\n" +
            "state int count = 0;\r\n" +
            "using Demo;");
        var b = new TestAdditionalText(
            "B.akbura",
            oldBText);
        var c = new TestAdditionalText(
            "C.akcss",
            SourceText.From(
                ".button {\r\n" +
                "    Width: 10;\r\n" +
                "}"));
        var compilation = CSharpCompilation.Create(
            "AkburaBlackSilenceGeneratorTests",
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaBlackSilenceGenerator()
                    .AsSourceGenerator(),
            ],
            additionalTexts:
            [
                a,
                b,
                c,
            ],
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);

        var initial = GetSyntaxTreeOutputs(driver);
        Assert.Equal(3, initial.Count);
        Assert.All(
            initial.Values,
            static output =>
                Assert.Equal(
                    IncrementalStepRunReason.New,
                    output.Reason));
        Assert.Equal(1, a.ReadCount);
        Assert.Equal(1, b.ReadCount);
        Assert.Equal(1, c.ReadCount);

        var changeStart = oldBText
            .ToString()
            .IndexOf("0;", StringComparison.Ordinal);
        var newBText = oldBText.WithChanges(
            new TextChange(
                new TextSpan(changeStart, length: 1),
                "1"));
        var updatedB = new TestAdditionalText(
            b.Path,
            newBText);

        driver = driver.ReplaceAdditionalText(
            b,
            updatedB);
        driver = driver.RunGenerators(compilation);

        var afterAdditionalTextChange =
            GetSyntaxTreeOutputs(driver);
        Assert.Equal(
            IncrementalStepRunReason.Cached,
            afterAdditionalTextChange[a.Path].Reason);
        Assert.Equal(
            IncrementalStepRunReason.Modified,
            afterAdditionalTextChange[b.Path].Reason);
        Assert.Equal(
            IncrementalStepRunReason.Cached,
            afterAdditionalTextChange[c.Path].Reason);
        Assert.Same(
            initial[a.Path].SyntaxTree,
            afterAdditionalTextChange[a.Path].SyntaxTree);
        Assert.NotSame(
            initial[b.Path].SyntaxTree,
            afterAdditionalTextChange[b.Path].SyntaxTree);
        Assert.Same(
            initial[c.Path].SyntaxTree,
            afterAdditionalTextChange[c.Path].SyntaxTree);
        Assert.Equal(1, a.ReadCount);
        Assert.Equal(1, b.ReadCount);
        Assert.Equal(1, updatedB.ReadCount);
        Assert.Equal(1, c.ReadCount);

        var oldBTree = Assert.IsType<ComponentSyntaxTree>(
            initial[b.Path].SyntaxTree);
        var newBTree = Assert.IsType<ComponentSyntaxTree>(
            afterAdditionalTextChange[b.Path].SyntaxTree);
        Assert.Same(
            oldBTree.GreenRoot.Members[0],
            newBTree.GreenRoot.Members[0]);
        Assert.NotSame(
            oldBTree.GreenRoot.Members[1],
            newBTree.GreenRoot.Members[1]);
        Assert.Same(
            oldBTree.GreenRoot.Members[2],
            newBTree.GreenRoot.Members[2]);

        var changedCompilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                "internal sealed class Changed { }"));

        driver = driver.RunGenerators(changedCompilation);

        var afterCompilationChange =
            GetSyntaxTreeOutputs(driver);
        Assert.All(
            afterCompilationChange.Values,
            static output =>
                Assert.Equal(
                    IncrementalStepRunReason.Cached,
                    output.Reason));
        Assert.Equal(1, a.ReadCount);
        Assert.Equal(1, b.ReadCount);
        Assert.Equal(1, updatedB.ReadCount);
        Assert.Equal(1, c.ReadCount);
    }

    private static Dictionary<string, (AkburaSyntaxTree SyntaxTree, IncrementalStepRunReason Reason)> GetSyntaxTreeOutputs(GeneratorDriver driver)
    {
        var runResult = driver.GetRunResult();
        Assert.Empty(runResult.GeneratedTrees);

        var generatorResult = Assert.Single(
            runResult.Results);
        Assert.Empty(generatorResult.GeneratedSources);
        Assert.True(
            generatorResult.TrackedSteps.TryGetValue(
                SyntaxTreesTrackingName,
                out var steps));

        return steps
            .SelectMany(static step =>
                step.Outputs)
            .Select(static output =>
                (
                    SyntaxTree:
                        Assert.IsType<AkburaSyntaxTree>(output.Value, exactMatch: false),
                     output.Reason))
            .ToDictionary(
                static output =>
                    output.SyntaxTree.FilePath,
                static output =>
                    output,
                StringComparer.Ordinal);
    }

    private sealed class TestAdditionalText(string path, SourceText sourceText) : AdditionalText
    {
        private int _readCount;

        public override string Path { get; } = path;

        public int ReadCount => Volatile.Read(ref _readCount);

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);
            return sourceText;
        }
    }
}
