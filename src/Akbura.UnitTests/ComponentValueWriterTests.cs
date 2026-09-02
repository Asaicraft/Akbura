using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

public sealed class ComponentValueWriterTests
{
    [Fact]
    public void WriteConstant_PrefersConvertedValue()
    {
        var intType = CreateCompilation().GetSpecialType(SpecialType.System_Int32);
        var plan = new ComponentCSharpValuePlan(
            operation: default,
            convertedValue: 42,
            literalValue: "ignored",
            intType);

        Assert.Equal("42", WriteConstant(plan));
    }

    [Fact]
    public void WriteConstant_UsesBoundConstantThenLiteralFallback()
    {
        var fixture = CreateOperation("42");
        var constant = new ComponentCSharpValuePlan(
            fixture.Operation,
            convertedValue: null,
            literalValue: "ignored",
            fixture.Type);
        var literal = new ComponentCSharpValuePlan(
            operation: default,
            convertedValue: null,
            literalValue: "hello",
            fixture.Type);

        Assert.Equal("42", WriteConstant(constant));
        Assert.Equal("\"hello\"", WriteConstant(literal));
    }

    [Fact]
    public void WriteExpression_UsesBoundSyntaxAndDefaultsWhenMissing()
    {
        var fixture = CreateOperation("value + 1");
        var expression = new ComponentCSharpValuePlan(
            fixture.Operation,
            convertedValue: null,
            literalValue: null,
            fixture.Type);
        var missing = new ComponentCSharpValuePlan(
            operation: default,
            convertedValue: null,
            literalValue: null,
            fixture.Type);

        Assert.Equal("value + 1", WriteExpression(expression));
        Assert.Equal("default", WriteExpression(missing));
    }

    [Fact]
    public void WriteElementReference_EscapesKeywordWithoutChangingIndent()
    {
        using var codeWriter = new CodeWriter("\n")
        {
            CurrentIndent = 4,
        };
        var writer = new ComponentValueWriter(codeWriter);

        writer.WriteElementReference("class");

        Assert.Equal("@class", codeWriter.GetText().ToString());
        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    private static string WriteConstant(in ComponentCSharpValuePlan plan)
    {
        using var codeWriter = new CodeWriter("\n");
        var writer = new ComponentValueWriter(codeWriter);
        writer.WriteConstant(plan);
        return codeWriter.GetText().ToString();
    }

    private static string WriteExpression(in ComponentCSharpValuePlan plan)
    {
        using var codeWriter = new CodeWriter("\n");
        var writer = new ComponentValueWriter(codeWriter);
        writer.WriteExpression(plan);
        return codeWriter.GetText().ToString();
    }

    private static OperationFixture CreateOperation(string expressionText)
    {
        var source =
            $$"""
            namespace Demo;

            internal sealed class Values
            {
                private int Create(int value)
                {
                    return {{expressionText}};
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var compilation = CreateCompilation().AddSyntaxTrees(syntaxTree);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var expression = Assert.Single(
            syntaxTree.GetRoot().DescendantNodes().OfType<ReturnStatementSyntax>()).Expression;
        var operation = semanticModel.GetOperation(expression!);

        Assert.NotNull(operation);
        Assert.NotNull(operation.Type);
        return new OperationFixture(
            new CSharpOperationDefinition(operation),
            operation.Type);
    }

    private static CSharpCompilation CreateCompilation()
    {
        return CSharpCompilation.Create(
            assemblyName: "ComponentValueWriterTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private sealed record OperationFixture(
        CSharpOperationDefinition Operation,
        ITypeSymbol Type);
}
