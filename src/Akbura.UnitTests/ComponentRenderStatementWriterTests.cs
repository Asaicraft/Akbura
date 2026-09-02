using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.UnitTests;

public sealed class ComponentRenderStatementWriterTests
{
    [Fact]
    public void Statement_TrimsOuterWhitespaceAndDoesNotAddSecondSemicolon()
    {
        const string source = "\r\n    DoSomething();   \r\n\r\n";
        var fixture = CreateStatementFixture(source);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 8,
        };
        var plan = new ComponentRenderStatementPlan(
            ComponentRenderStatementKind.Statement,
            fixture.Node,
            fixture.Syntax);
        var writer = new ComponentRenderStatementWriter(
            codeWriter,
            new ComponentGenerationSourceMap(fixture.Tree));
        codeWriter.WriteLine();
        var outputStart = codeWriter.Length;

        writer.Write(plan);

        Assert.Equal(8, codeWriter.CurrentIndent);
        Assert.Equal(
            "        DoSomething();\r\n",
            codeWriter.GetText().ToString().Substring(outputStart));
    }

    [Fact]
    public void Statement_IndentsEveryBlockLineAndPreservesRelativeIndentation()
    {
        const string source =
            "if (condition)\r\n" +
            "{\r\n" +
            "    Execute();\r\n" +
            "\r\n" +
            "    Complete();\r\n" +
            "}";
        var fixture = CreateStatementFixture(source);
        var node = CSharpSyntaxFactory.ParseStatement("  " + source + "  \r\n");
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var plan = new ComponentRenderStatementPlan(
            ComponentRenderStatementKind.Statement,
            node,
            fixture.Syntax);
        var writer = new ComponentRenderStatementWriter(
            codeWriter,
            new ComponentGenerationSourceMap(fixture.Tree));
        codeWriter.WriteLine();
        var outputStart = codeWriter.Length;

        writer.Write(plan);

        Assert.Equal(4, codeWriter.CurrentIndent);
        Assert.Equal(
            "    if (condition)\r\n" +
            "    {\r\n" +
            "        Execute();\r\n" +
            "\r\n" +
            "        Complete();\r\n" +
            "    }\r\n",
            codeWriter.GetText().ToString().Substring(outputStart));
    }

    [Fact]
    public void Statement_MultilineSourceMappingKeepsLifecycleIndent()
    {
        const string source =
            "if (condition)\r\n" +
            "{\r\n" +
            "    Execute();\r\n" +
            "}";
        var fixture = CreateStatementFixture(
            source,
            "Views/RenderComponent.akbura");
        var node = CSharpSyntaxFactory.ParseStatement(source);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 8,
        };
        var plan = new ComponentRenderStatementPlan(
            ComponentRenderStatementKind.Statement,
            node,
            fixture.Syntax);
        var writer = new ComponentRenderStatementWriter(
            codeWriter,
            new ComponentGenerationSourceMap(fixture.Tree));

        writer.Write(plan);

        var output = codeWriter.GetText().ToString();
        Assert.Contains(
            "        if (condition)\r\n" +
            "        {\r\n" +
            "            Execute();\r\n" +
            "        }\r\n",
            output,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "        #line default\r\n" +
            "        #line hidden\r\n",
            output,
            StringComparison.Ordinal);
        Assert.Equal(8, codeWriter.CurrentIndent);
    }

    [Fact]
    public void UseHookInvocation_WritesEffectiveInvocationWithOneSemicolon()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Hooks;

            useControlValue(
                Width);

            <Border />
            """;
        const string csharp =
            """
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;
            using Avalonia;
            using Avalonia.Controls;

            namespace Demo
            {
                public partial class PlannerView : Control
                {
                }
            }

            namespace Hooks
            {
                public static class ControlHooks
                {
                    [UseHook]
                    public static void useControlValue<T>(
                        [Self] T control,
                        AvaloniaProperty<double> property)
                        where T : Control
                    {
                    }
                }
            }
            """;
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            csharp);
        var syntax = Assert.Single(
            semanticFixture.ComponentTree.GetRoot().Members
                .OfType<CSharpStatementSyntax>());
        var operation = Assert.IsAssignableFrom<IUseHookOperation>(
            semanticFixture.SemanticModel.GetOperation(syntax));
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var plan = new ComponentRenderStatementPlan(
            ComponentRenderStatementKind.UseHookInvocation,
            operation.EffectiveInvocation,
            syntax);
        var writer = new ComponentRenderStatementWriter(
            codeWriter,
            new ComponentGenerationSourceMap(
                Assert.IsType<ComponentSyntaxTree>(semanticFixture.ComponentTree)));

        writer.Write(plan);

        var output = codeWriter.GetText().ToString();
        Assert.True(operation.HasSyntheticSelf);
        Assert.True(operation.HasPropertyArgumentSubstitution);
        Assert.Contains("    useControlValue(\r\n", output, StringComparison.Ordinal);
        Assert.Contains("\r\n    this,", output, StringComparison.Ordinal);
        Assert.Contains("WidthProperty);\r\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain(";;", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nthis,", output, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nglobal::", output, StringComparison.Ordinal);
        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    [Fact]
    public void Statement_WritesSourceMappingAndRestoresGeneratedLocation()
    {
        var fixture = CreateStatementFixture(
            "DoSomething();",
            "Views/RenderComponent.akbura");
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var plan = new ComponentRenderStatementPlan(
            ComponentRenderStatementKind.Statement,
            fixture.Node,
            fixture.Syntax);
        var writer = new ComponentRenderStatementWriter(
            codeWriter,
            new ComponentGenerationSourceMap(fixture.Tree));

        writer.Write(plan);

        var output = codeWriter.GetText().ToString();
        Assert.Contains("#line (", output, StringComparison.Ordinal);
        Assert.Contains("\"Views/RenderComponent.akbura\"", output, StringComparison.Ordinal);
        Assert.Contains("    DoSomething();\r\n", output, StringComparison.Ordinal);
        Assert.EndsWith(
            "    #line default\r\n" +
            "    #line hidden\r\n",
            output,
            StringComparison.Ordinal);
        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    private static StatementFixture CreateStatementFixture(
        string source,
        string path = "")
    {
        var tree = ComponentSyntaxTree.ParseText(source, path);
        var syntax = Assert.Single(
            tree.GetRoot().Members.OfType<CSharpStatementSyntax>());
        var node = syntax.GetRawCSharpStatement();

        Assert.NotNull(node);
        return new StatementFixture(tree, syntax, node!);
    }

    private readonly record struct StatementFixture(
        ComponentSyntaxTree Tree,
        CSharpStatementSyntax Syntax,
        CSharpSyntaxNode Node);
}
