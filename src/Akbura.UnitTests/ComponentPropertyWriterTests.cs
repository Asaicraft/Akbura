using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class ComponentPropertyWriterTests
{
    [Fact]
    public void WriteStructuralConstantValue_EmitsSemanticClrDeclaringType()
    {
        const string component =
            "using Demo;\r\n" +
            "\r\n" +
            "<PropertyHost Value=\"42\" />\r\n";
        const string csharp =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "namespace Demo;\r\n" +
            "\r\n" +
            "public class PropertyHost : Control\r\n" +
            "{\r\n" +
            "    public int Value { get; set; }\r\n" +
            "}\r\n";
        var fixture = CreateFixture(component, csharp);
        var plan = Assert.Single(fixture.Plan.PropertyWrites);

        var output = WriteStructuralConstantValue(
            fixture,
            plan,
            out var wroteAny);

        Assert.True(wroteAny);
        Assert.Equal(PropertyWriteKind.ClrProperty, plan.Destination.Kind);
        Assert.Contains(".ReconcileClrValue(", output, StringComparison.Ordinal);
        Assert.Contains(
            "typeof(global::Demo.PropertyHost)",
            output,
            StringComparison.Ordinal);
        Assert.Contains("\"Value\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteStructuralConstantValue_SetterOnlyClrPropertyUsesDirectFallback()
    {
        const string component =
            "using Demo;\r\n" +
            "\r\n" +
            "<SetterOnlyHost Value=\"42\" />\r\n";
        const string csharp =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "namespace Demo;\r\n" +
            "\r\n" +
            "public sealed class SetterOnlyHost : Control\r\n" +
            "{\r\n" +
            "    public int LastValue { get; private set; }\r\n" +
            "\r\n" +
            "    public int Value { set => LastValue = value; }\r\n" +
            "}\r\n";
        var fixture = CreateFixture(component, csharp);
        var plan = Assert.Single(fixture.Plan.PropertyWrites);

        var structuralOutput = WriteStructuralConstantValue(
            fixture,
            plan,
            out var wroteStructurally);
        var directOutput = WriteDirectValue(fixture, plan);

        Assert.Equal(PropertyWriteKind.ClrProperty, plan.Destination.Kind);
        Assert.Null(plan.Destination.ClrProperty?.GetMethod);
        Assert.False(wroteStructurally);
        Assert.Equal(string.Empty, structuralOutput);
        Assert.Contains(".Value = 42;", directOutput, StringComparison.Ordinal);
        AssertGeneratedStatementCompiles(fixture.Compilation, directOutput);
    }

    [Fact]
    public void WriteStructuralConstantValue_AttachedAccessorDocumentsDirectOnlyBoundary()
    {
        const string component =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<Grid Grid.Column=\"3\" />\r\n";
        var fixture = CreateFixture(component);
        var plan = Assert.Single(fixture.Plan.PropertyWrites);

        var structuralOutput = WriteStructuralConstantValue(
            fixture,
            plan,
            out var wroteStructurally);
        var directOutput = WriteDirectValue(fixture, plan);

        Assert.Equal(PropertyWriteKind.AttachedAccessor, plan.Destination.Kind);
        Assert.False(wroteStructurally);
        Assert.Equal(string.Empty, structuralOutput);
        Assert.Contains(
            "global::Avalonia.Controls.Grid.SetColumn(",
            directOutput,
            StringComparison.Ordinal);
        AssertGeneratedStatementCompiles(fixture.Compilation, directOutput);
    }

    private static WriterFixture CreateFixture(
        string component,
        string? csharp = null)
    {
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            csharp);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            baseFixture.SemanticModel.GetSymbolInfo(
                baseFixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentPlanner.Create(
            componentSymbol,
            baseFixture.SemanticModel,
            new Dictionary<AkburaSyntax, string>(),
            ComponentGenerationMode.DebugStructural);
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(
                baseFixture.ComponentTree,
                exactMatch: false));
        var environment = BindingWriterEnvironment.Create(
            baseFixture.CSharpCompilation,
            withinType: null);

        return new WriterFixture(
            plan,
            sourceMap,
            environment,
            baseFixture.CSharpCompilation);
    }

    private static string WriteStructuralConstantValue(
        WriterFixture fixture,
        in ComponentPropertyWritePlan plan,
        out bool wroteAny)
    {
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.Environment;
        var writer = new ComponentPropertyWriter(
            codeWriter,
            in environment,
            fixture.SourceMap);
        wroteAny = writer.WriteStructuralConstantValue(
            fixture.Plan,
            plan,
            ownerRuntimeId: 0,
            targetExpression: "target");
        return codeWriter.GetText().ToString();
    }

    private static string WriteDirectValue(
        WriterFixture fixture,
        in ComponentPropertyWritePlan plan)
    {
        using var codeWriter = new CodeWriter("\n")
        {
            CurrentIndent = 8,
        };
        var environment = fixture.Environment;
        var writer = new ComponentPropertyWriter(
            codeWriter,
            in environment,
            fixture.SourceMap);
        var context = default(MarkupExtensionWriteContext);
        writer.Write(
            fixture.Plan,
            plan,
            targetExpression: "target",
            in context);
        return codeWriter.GetText().ToString();
    }

    private static void AssertGeneratedStatementCompiles(
        CSharpCompilation compilation,
        string statement)
    {
        const string generatedStart =
            "#nullable enable\r\n" +
            "\r\n" +
            "namespace Demo.Generated;\r\n" +
            "\r\n" +
            "internal static class ComponentPropertyWriterOutput\r\n" +
            "{\r\n" +
            "    private static void Apply(object target)\r\n" +
            "    {\r\n";
        const string generatedEnd =
            "    }\r\n" +
            "}\r\n";
        var source = generatedStart + statement + generatedEnd;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.Preview),
            path: "ComponentPropertyWriterOutput.g.cs");
        var errors = compilation.AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + source);
    }

    private sealed record WriterFixture(
        ComponentPlan Plan,
        ComponentGenerationSourceMap SourceMap,
        BindingWriterEnvironment Environment,
        CSharpCompilation Compilation);
}
