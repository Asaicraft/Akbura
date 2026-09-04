using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class InjectServiceWriterTests
{
    [Fact]
    public void Write_UsesStableIdsAndPreservesRequiredAndOptionalNullability()
    {
        const string csharp =
            """
            namespace Demo;

            public interface IService<T>
            {
            }
            """;
        var plan = CreatePlan(
            """
            using Demo;

            inject IService<string> required;
            inject IService<string?>? optional;
            """,
            csharp);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 8,
        };
        var serviceWriter = new InjectServiceWriter(
            codeWriter,
            "global::Demo.Owner");

        for (var i = 0; i < plan.Services.Length; i++)
        {
            if (i > 0)
            {
                codeWriter.WriteLine();
            }

            ref readonly var service = ref plan.Services.ItemRef(i);
            serviceWriter.Write(service);
        }

        Assert.Equal(8, codeWriter.CurrentIndent);
        var output = codeWriter.GetText().ToString();

        Assert.Contains(
            "private global::Demo.IService<string>? __service0;",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "private global::Demo.IService<string?>? __service1;",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.ComponentTree.InjectService<",
            output,
            StringComparison.Ordinal);
        Assert.Contains("global::Demo.Owner,", output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Demo.IService<string>>",
            output,
            StringComparison.Ordinal);
        Assert.Contains("@requiredProperty =", output, StringComparison.Ordinal);
        Assert.Contains(
            "static __owner => __owner.__service0,",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "static (__owner, __value) =>",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "__owner.__SetService0(__value),",
            output,
            StringComparison.Ordinal);
        Assert.Contains("isOptional: false);", output, StringComparison.Ordinal);
        Assert.Contains("isOptional: true);", output, StringComparison.Ordinal);
        Assert.Contains(
            "SetAndRaise(@requiredProperty.AvaloniaProperty, ref __service0, value);",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "public global::Demo.IService<string> @required",
            output,
            StringComparison.Ordinal);
        Assert.Contains("get => __service0!;", output, StringComparison.Ordinal);
        Assert.Contains(
            "public global::Demo.IService<string?>? optional",
            output,
            StringComparison.Ordinal);
        Assert.Contains("get => __service1;", output, StringComparison.Ordinal);
        Assert.DoesNotContain("get => __service1!;", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__service_required", output, StringComparison.Ordinal);

        AssertGeneratedMembersCompile(output);
    }

    private static ComponentMemberPlan CreatePlan(
        string componentSource,
        string additionalCSharp)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            componentSource,
            additionalCSharp);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);

        return ComponentMemberPlanner.Create(component, fixture.SemanticModel);
    }

    private static void AssertGeneratedMembersCompile(string members)
    {
        var source =
            "#nullable enable\r\n" +
            "namespace Demo\r\n" +
            "{\r\n" +
            "    public interface IService<T>\r\n" +
            "    {\r\n" +
            "    }\r\n" +
            "\r\n" +
            "    public abstract class Owner : global::Akbura.AkburaControl\r\n" +
            "    {\r\n" +
            members +
            "    }\r\n" +
            "}\r\n";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "InjectServiceWriterOutput.g.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName: "InjectServiceWriterOutput",
            syntaxTrees: [syntaxTree],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var errors = compilation.GetDiagnostics()
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
}
