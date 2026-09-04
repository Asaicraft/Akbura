using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class CommandWriterTests
{
    [Fact]
    public void Write_UsesFlatParameterRangesAndTypedCommandInterfaces()
    {
        var plan = CreatePlan(
            """
            command void Reset();
            command int Sum(int left, string? label);
            command void Notify(double value);
            """);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 8,
        };
        var commandWriter = new CommandWriter(
            codeWriter,
            "global::Demo.Owner");

        for (var i = 0; i < plan.Commands.Length; i++)
        {
            if (i > 0)
            {
                codeWriter.WriteLine();
            }

            ref readonly var command = ref plan.Commands.ItemRef(i);
            commandWriter.Write(plan, command);
        }

        Assert.Equal(8, codeWriter.CurrentIndent);
        var output = codeWriter.GetText().ToString();

        Assert.Contains(
            "global::Avalonia.StyledProperty<",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.IAkburaCommand>",
            output,
            StringComparison.Ordinal);
        Assert.Contains("ResetProperty =", output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.AvaloniaProperty.Register<",
            output,
            StringComparison.Ordinal);
        Assert.Contains("global::Demo.Owner,", output, StringComparison.Ordinal);
        Assert.Contains(
            "public global::Akbura.IAkburaCommand Reset",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "public global::Akbura.IAkburaCommand<int, string?, int> Sum",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "public global::Akbura.IAkburaCommand<double, object> Notify",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "(global::Akbura.IAkburaCommand<int, string?, int>)",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "GetValue(SumProperty)!;",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "NotifyProperty,",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "IAkburaCommand<int, string?, double",
            output,
            StringComparison.Ordinal);

        AssertGeneratedMembersCompile(output);
    }

    private static ComponentMemberPlan CreatePlan(string componentSource)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(componentSource);
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
            "    public abstract class Owner : global::Akbura.AkburaControl\r\n" +
            "    {\r\n" +
            members +
            "    }\r\n" +
            "}\r\n";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "CommandWriterOutput.g.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName: "CommandWriterOutput",
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
