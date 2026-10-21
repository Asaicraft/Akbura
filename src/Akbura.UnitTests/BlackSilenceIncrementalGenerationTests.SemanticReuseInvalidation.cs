using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
#if AKBURA_SEMANTIC_REUSE
    [Theory]
    [InlineData("method-body")]
    [InlineData("property-removal")]
    [InlineData("overload")]
    [InlineData("negative-repair")]
    [InlineData("conversion")]
    [InlineData("unrelated-type")]
    [InlineData("another-document")]
    public void SemanticReuseSupplement_CSharpReplacementBindsNormallyAndMatchesFresh(string change)
    {
        var project = new TestProject(publishDiagnostics: true);
        var supportText = SemanticReuseSupport.Replace("public string FirstName", "public string Read(int value) => FirstName;\r\n    public string FirstName");
        var support = CSharpSyntaxTree.ParseText(supportText, project.Options.ParseOptions, "Person.cs");
        var other = CSharpSyntaxTree.ParseText("namespace Demo; public class Other { public int Value => 1; }", project.Options.ParseOptions, "Other.cs");
        var compilation = project.Compilation.AddSyntaxTrees(support, other);
        var body = change == "negative-repair" ? SemanticReuseBody.Replace("FirstName", "Missing") : SemanticReuseBody;
        var original = project.File("Page.akbura", Component(body));
        var initial = ArtifactRun(project, CreateDriver(project.Options, original), compilation);
        var replacement = change switch
        {
            "method-body" => supportText.Replace("=> FirstName;", "=> LastName;"),
            "property-removal" => supportText.Replace("public string FirstName { get; set; } = \"First\";", "public string Removed { get; set; } = \"First\";"),
            "overload" => supportText.Replace("public string Read(int value)", "public string Read(string value) => LastName;\r\n    public string Read(int value)"),
            "negative-repair" => supportText.Replace("public string FirstName", "public string Missing => FirstName;\r\n    public string FirstName"),
            "conversion" => supportText.Replace("string FirstName", "object FirstName"),
            "unrelated-type" => supportText + "\r\npublic class Unrelated { }",
            _ => supportText,
        };
        compilation = change == "another-document"
            ? compilation.ReplaceSyntaxTree(other, CSharpSyntaxTree.ParseText("namespace Demo; public class Other { public string Value => \"changed\"; }", project.Options.ParseOptions, "Other.cs"))
            : compilation.ReplaceSyntaxTree(support, CSharpSyntaxTree.ParseText(replacement, project.Options.ParseOptions, "Person.cs"));
        var edited = project.File("Page.akbura", Component(body.Replace("10", "20")));
        TestRun current;
        using (var measurement = GenerationStatistics.BeginMeasurement())
        {
            current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(original, edited), compilation);
#if STATS
            Assert.Equal(0, measurement.GetSnapshot().GetCounterCount(GenerationStatisticCounter.CrossRevisionFirstBindingSkipped));
            Assert.True(measurement.GetSnapshot().GetStageInvocationCount(GenerationStatisticStage.ExpressionFactoryBinding) > 0);
#endif
        }
        AssertSemanticReuseParity(ArtifactRun(project, CreateDriver(project.Options, edited), compilation), current);
    }

    [Theory]
    [InlineData("global::Demo.Person.Current.FirstName.ToUpper()")]
    [InlineData("global::Demo.Person.Current.Read<int>()")]
    [InlineData("global::Demo.Person.Current?.FirstName")]
    [InlineData("global::Demo.Person.Current.FirstName + global::Demo.Person.Current.LastName")]
    [InlineData("global::Demo.Person.Current.FirstName.Extension()")]
    public void SemanticReuseSupplement_UnsupportedValidExpressionsBindNormallyAfterMarginEdit(string expression)
    {
        var project = new TestProject(publishDiagnostics: true);
        var support = SemanticReuseSupport.Replace("public string FirstName", "public string Read<T>() => LastName;\r\n    public string FirstName") +
            "\r\npublic static class Extensions { public static string Extension(this string value) => value; }";
        var compilation = project.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(support, project.Options.ParseOptions, "Person.cs"));
        var body = "using Demo; <TextBlock Margin=\"10\" Text={" + expression + "} />";
        var original = project.File("Page.akbura", Component(body));
        var initial = ArtifactRun(project, CreateDriver(project.Options, original), compilation);
        var edited = project.File("Page.akbura", Component(body.Replace("10", "20")));
        TestRun current;
        using (var measurement = GenerationStatistics.BeginMeasurement())
        {
            current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(original, edited), compilation);
#if STATS
            Assert.Equal(0, measurement.GetSnapshot().GetCounterCount(GenerationStatisticCounter.CrossRevisionFirstBindingSkipped));
            Assert.True(measurement.GetSnapshot().GetStageInvocationCount(GenerationStatisticStage.ExpressionFactoryBinding) > 0);
#endif
        }
        Assert.DoesNotContain(current.Output.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        AssertSemanticReuseParity(ArtifactRun(project, CreateDriver(project.Options, edited), compilation), current);
    }

    [Fact]
    public void SemanticReuseSupplement_RemovedDocumentsAndExpressionsLeaveTheLatestGeneration()
    {
        var project = new TestProject(publishDiagnostics: true);
        var compilation = project.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SemanticReuseSupport, project.Options.ParseOptions, "Person.cs"));
        var first = project.File("Page.akbura", Component(SemanticReuseBody));
        var second = project.File("Other.akbura", Component(SemanticReuseBody));
        var initial = ArtifactRun(project, CreateDriver(project.Options, first, second), compilation);
        var edited = project.File("Page.akbura", Component(SemanticReuseBody.Replace("<TextBlock Text={global::Demo.Person.Current.LastName} />", "")));
        var current = ArtifactRun(project, initial.Driver.RemoveAdditionalTexts([second]).ReplaceAdditionalText(first, edited), compilation);
        var latest = current.Request.State.ProbeStore!.Latest!;
        Assert.DoesNotContain(second.Path, latest.Documents.Keys);
        Assert.DoesNotContain(latest.Documents.Values.SelectMany(document => document.Entries.Keys), key => key.Text.Contains("LastName", StringComparison.Ordinal));
        AssertSemanticReuseParity(ArtifactRun(project, CreateDriver(project.Options, edited), compilation), current);
    }
#endif
}
