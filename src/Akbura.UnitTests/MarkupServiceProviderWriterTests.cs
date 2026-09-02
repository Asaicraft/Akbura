using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class MarkupServiceProviderWriterTests
{
    [Fact]
    public void ExpressionParentStack_PreservesExistingEmission()
    {
        using var codeWriter = new CodeWriter("\n");
        var context = new MarkupExtensionWriteContext(
            targetObjectExpression: "__element0",
            targetProperty: MarkupTargetPropertyPlan.CreateExpression(
                "global::Avalonia.Controls.Border.BackgroundProperty"),
            intermediateRootExpression: "__root",
            baseUriExpression: "__baseUri",
            directParentsStackExpression: "__parents",
            fallbackServiceProviderExpression: null,
            nameScopeExpression: null,
            scopeId: 0);
        var writer = new MarkupServiceProviderWriter(codeWriter);

        Assert.True(writer.Write(context));
        Assert.Equal(
            "CreateMarkupServiceProvider(" +
            "targetObject: __element0, " +
            "targetProperty: global::Avalonia.Controls.Border.BackgroundProperty, " +
            "intermediateRootObject: __root, " +
            "baseUri: __baseUri, " +
            "directParentsStack: __parents)",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void ComponentHierarchy_WritesTypedParentStackAndFallbackProvider()
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
        var parentStack = new MarkupParentStackPlan(
            fixture.Plan.Elements.AsSpan(),
            target.Id,
            target.ScopeId,
            MarkupParentStackTraversalKind.ExactScope);
        var context = new MarkupExtensionWriteContext(
            targetObjectExpression: target.Identifier,
            targetProperty: MarkupTargetPropertyPlan.CreateExpression(
                "global::Avalonia.Controls.TextBlock.TextProperty"),
            intermediateRootExpression: "__root",
            baseUriExpression: "__baseUri",
            directParentsStack: parentStack,
            fallbackServiceProviderExpression: "__services",
            nameScopeExpression: null,
            scopeId: target.ScopeId);
        using var codeWriter = new CodeWriter("\n");
        var writer = new MarkupServiceProviderWriter(codeWriter);

        Assert.True(writer.Write(context));
        Assert.Equal(
            "CreateMarkupServiceProvider(" +
            "targetObject: __element2, " +
            "targetProperty: global::Avalonia.Controls.TextBlock.TextProperty, " +
            "intermediateRootObject: __root, " +
            "baseUri: __baseUri, " +
            "directParentsStack: new global::System.Object[] { " +
            "this, __element0, __element1, __element2 }, " +
            "fallbackServiceProvider: __services)",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void ComponentHierarchy_GeneratedServiceProviderExpressionCompiles()
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
        var parentStack = new MarkupParentStackPlan(
            fixture.Plan.Elements.AsSpan(),
            target.Id,
            target.ScopeId,
            MarkupParentStackTraversalKind.ExactScope);
        var context = new MarkupExtensionWriteContext(
            targetObjectExpression: target.Identifier,
            targetProperty: MarkupTargetPropertyPlan.CreateExpression(
                "global::Avalonia.Controls.TextBlock.TextProperty"),
            intermediateRootExpression: "__root",
            baseUriExpression: "__baseUri",
            directParentsStack: parentStack,
            fallbackServiceProviderExpression: "__services",
            nameScopeExpression: null,
            scopeId: target.ScopeId);
        using var codeWriter = new CodeWriter("\n");
        var writer = new MarkupServiceProviderWriter(codeWriter);

        Assert.True(writer.Write(context));

        var generatedSource =
            $$"""
            #nullable enable

            namespace Generated;

            internal sealed class MarkupServiceProviderOutput
            {
                private void Apply(
                    global::Avalonia.Controls.Border __element0,
                    global::Avalonia.Controls.StackPanel __element1,
                    global::Avalonia.Controls.TextBlock __element2,
                    object __root,
                    object __baseUri,
                    global::System.IServiceProvider __services)
                {
                    _ = {{codeWriter.GetText()}};
                }

                private static global::System.IServiceProvider CreateMarkupServiceProvider(
                    object targetObject,
                    object targetProperty,
                    object intermediateRootObject,
                    object baseUri,
                    object directParentsStack,
                    global::System.IServiceProvider fallbackServiceProvider)
                {
                    return null!;
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "MarkupServiceProviderOutput.g.cs");
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
