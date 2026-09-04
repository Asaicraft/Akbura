using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class ComponentUserMemberWriterTests
{
    [Fact]
    public void Write_LocalFunctionPreservesIndentationAndMapsOnlyTheMember()
    {
        var fixture = CreateFixture(
            """
            int Add(
                int left,
                int right)
            {
                return left + right;
            }
            """);
        var member = Assert.Single(fixture.Plan.UserMembers);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var writer = new ComponentUserMemberWriter(
            codeWriter,
            fixture.SourceMap);

        writer.Write(member);

        var output = codeWriter.GetText().ToString();
        Assert.Contains(
            "    int Add(\r\n" +
            "        int left,\r\n" +
            "        int right)\r\n" +
            "    {\r\n" +
            "        return left + right;\r\n" +
            "    }\r\n",
            output,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(output, "#line ("));
        Assert.Equal(1, CountOccurrences(output, "#line default"));
        Assert.Equal(1, CountOccurrences(output, "#line hidden"));
        Assert.Contains("\"PlannerView.akbura\"", output, StringComparison.Ordinal);
        Assert.EndsWith(
            "    #line default\r\n" +
            "    #line hidden\r\n",
            output,
            StringComparison.Ordinal);
        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    [Fact]
    public void GeneratedLocalFunctionText_CompilesAsClassMember()
    {
        var fixture = CreateFixture(
            """
            static string Format(int value)
            {
                return value.ToString();
            }
            """);
        var member = Assert.Single(fixture.Plan.UserMembers);
        using var codeWriter = new CodeWriter("\r\n");
        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        var writer = new ComponentUserMemberWriter(
            codeWriter,
            fixture.SourceMap);
        writer.Write(member);
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var generatedSource = codeWriter.GetText().ToString();
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "ComponentUserMemberWriterOutput.g.cs");
        var diagnostics = fixture.SemanticFixture.CSharpCompilation
            .AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
    }

    private static WriterFixture CreateFixture(string component)
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticFixture.SemanticModel.GetSymbolInfo(
                semanticFixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentMemberPlanner.Create(
            componentSymbol,
            semanticFixture.SemanticModel);

        return new WriterFixture(
            semanticFixture,
            plan,
            new ComponentGenerationSourceMap(
                Assert.IsType<ComponentSyntaxTree>(semanticFixture.ComponentTree)));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;

        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private readonly record struct WriterFixture(
        AkcssActivatorPlannerTests.PlannerFixture SemanticFixture,
        ComponentMemberPlan Plan,
        ComponentGenerationSourceMap SourceMap);
}
