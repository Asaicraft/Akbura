using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class MarkupParentStackWriterTests
{
    [Fact]
    public void Expression_WritesExistingExpression()
    {
        using var codeWriter = new CodeWriter("\n");
        var plan = new MarkupParentStackPlan("__parents");
        var writer = new MarkupParentStackWriter(codeWriter);

        Assert.True(writer.Write(plan));
        Assert.Equal("__parents", codeWriter.GetText().ToString());
    }

    [Fact]
    public void ComponentHierarchy_WritesComponentThenRootToTarget()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border>
                <StackPanel>
                    <TextBlock />
                </StackPanel>
            </Border>
            """;
        var fixture = CreatePlan(component);
        var target = fixture.Plan.Elements[2];
        using var codeWriter = new CodeWriter("\n");
        var plan = new MarkupParentStackPlan(
            fixture.Plan.Elements.AsSpan(),
            target.Id,
            target.ScopeId,
            MarkupParentStackTraversalKind.ExactScope);
        var writer = new MarkupParentStackWriter(codeWriter);

        Assert.True(writer.Write(plan));
        Assert.Equal(
            "new global::System.Object[] { this, __element0, __element1, __element2 }",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void ComponentHierarchy_StopsAtExactScopeBoundary()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <StackPanel>
                    <Border>
                        <TextBlock />
                    </Border>
                </StackPanel>
            </DataTemplate>
            """;
        var fixture = CreatePlan(component);
        var target = fixture.Plan.Elements[3];
        using var codeWriter = new CodeWriter("\n");
        var plan = new MarkupParentStackPlan(
            fixture.Plan.Elements.AsSpan(),
            target.Id,
            target.ScopeId,
            MarkupParentStackTraversalKind.ExactScope);
        var writer = new MarkupParentStackWriter(codeWriter);

        Assert.True(writer.Write(plan));
        Assert.Equal(
            "new global::System.Object[] { __element1, __element2, __element3 }",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void ComponentHierarchy_FullHierarchyCrossesScopeBoundary()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <StackPanel>
                    <Border>
                        <TextBlock />
                    </Border>
                </StackPanel>
            </DataTemplate>
            """;
        var fixture = CreatePlan(component);
        var target = fixture.Plan.Elements[3];
        using var codeWriter = new CodeWriter("\n");
        var plan = new MarkupParentStackPlan(
            fixture.Plan.Elements.AsSpan(),
            target.Id,
            target.ScopeId,
            MarkupParentStackTraversalKind.FullHierarchy);
        var writer = new MarkupParentStackWriter(codeWriter);

        Assert.True(writer.Write(plan));
        Assert.Equal(
            "new global::System.Object[] { this, __element0, __element1, " +
            "__element2, __element3 }",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void ComponentHierarchy_DeeperThanStackCapacityPreservesOrderForBothTraversals()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border>
                <Border>
                    <Border>
                        <Border>
                            <Border>
                                <Border>
                                    <Border>
                                        <Border>
                                            <Border>
                                                <Border>
                                                    <Border>
                                                        <Border>
                                                            <Border>
                                                                <Border>
                                                                    <Border>
                                                                        <Border>
                                                                            <Border>
                                                                                <Border />
                                                                            </Border>
                                                                        </Border>
                                                                    </Border>
                                                                </Border>
                                                            </Border>
                                                        </Border>
                                                    </Border>
                                                </Border>
                                            </Border>
                                        </Border>
                                    </Border>
                                </Border>
                            </Border>
                        </Border>
                    </Border>
                </Border>
            </Border>
            """;
        var fixture = CreatePlan(component);
        var target = fixture.Plan.Elements[^1];
        using var exactScopeCodeWriter = new CodeWriter("\n");
        var exactScopePlan = new MarkupParentStackPlan(
            fixture.Plan.Elements.AsSpan(),
            target.Id,
            target.ScopeId,
            MarkupParentStackTraversalKind.ExactScope);
        var exactScopeWriter = new MarkupParentStackWriter(exactScopeCodeWriter);
        using var fullHierarchyCodeWriter = new CodeWriter("\n");
        var fullHierarchyPlan = new MarkupParentStackPlan(
            fixture.Plan.Elements.AsSpan(),
            target.Id,
            target.ScopeId,
            MarkupParentStackTraversalKind.FullHierarchy);
        var fullHierarchyWriter = new MarkupParentStackWriter(fullHierarchyCodeWriter);

        Assert.Equal(18, fixture.Plan.Elements.Length);
        Assert.True(exactScopeWriter.Write(exactScopePlan));
        Assert.True(fullHierarchyWriter.Write(fullHierarchyPlan));
        var expected =
            "new global::System.Object[] { this, " +
            "__element0, __element1, __element2, __element3, __element4, " +
            "__element5, __element6, __element7, __element8, __element9, " +
            "__element10, __element11, __element12, __element13, __element14, " +
            "__element15, __element16, __element17 }";
        Assert.Equal(expected, exactScopeCodeWriter.GetText().ToString());
        Assert.Equal(expected, fullHierarchyCodeWriter.GetText().ToString());
    }

    [Fact]
    public void ComponentHierarchy_GeneratedExpressionCompiles()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border>
                <StackPanel>
                    <TextBlock />
                </StackPanel>
            </Border>
            """;
        var fixture = CreatePlan(component);
        var target = fixture.Plan.Elements[2];
        using var codeWriter = new CodeWriter("\n");
        var plan = new MarkupParentStackPlan(
            fixture.Plan.Elements.AsSpan(),
            target.Id,
            target.ScopeId,
            MarkupParentStackTraversalKind.ExactScope);
        var writer = new MarkupParentStackWriter(codeWriter);

        Assert.True(writer.Write(plan));

        var generatedSource =
            $$"""
            #nullable enable

            namespace Generated;

            internal sealed class ParentStackOutput
            {
                private void Apply(
                    object __element0,
                    object __element1,
                    object __element2)
                {
                    Consume({{codeWriter.GetText()}});
                }

                private static void Consume(object[] parents)
                {
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "ParentStackOutput.g.cs");
        var diagnostics = fixture.Compilation.AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())));
    }

    private static PlannerFixture CreatePlan(string component)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var moduleTypeNames = new Dictionary<AkburaSyntax, string>();
        var plan = ComponentPlanner.Create(
            symbol,
            fixture.SemanticModel,
            moduleTypeNames);

        return new PlannerFixture(fixture.CSharpCompilation, plan);
    }

    private readonly record struct PlannerFixture(
        CSharpCompilation Compilation,
        ComponentPlan Plan);
}
