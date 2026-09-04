using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class StateWriterTests
{
    [Fact]
    public void Write_FactoryMapsInitializerExpressionInsteadOfStateDeclaration()
    {
        var fixture = CreateFixture(
            "state int count =\r\n" +
            "    CreateInitialCount();\r\n" +
            "\r\n" +
            "int CreateInitialCount()\r\n" +
            "{\r\n" +
            "    return 42;\r\n" +
            "}");

        ref readonly var state = ref fixture.Plan.States.ItemRef(0);

        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };

        var writer = new StateWriter(
            codeWriter,
            fixture.SourceMap,
            "global::Demo.PlannerView");

        writer.Write(state);

        var output = codeWriter.GetText().ToString();

        Assert.Contains(
            "#line (2,5)-(",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "return CreateInitialCount();",
            output,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "#line (1,1)-(",
            output,
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            CountOccurrences(output, "#line ("));

        Assert.Equal(
            1,
            CountOccurrences(output, "#line default"));

        Assert.Equal(
            1,
            CountOccurrences(output, "#line hidden"));

        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    [Fact]
    public void Write_ReadOnlyStateOmitsValueSetter()
    {
        const string csharp =
            """
            namespace Demo;

            public partial class PlannerView : Avalonia.Controls.Control
            {
            }
            """;
        var fixture = CreateFixture(
            """
            using Avalonia.Controls;

            state double width = out Width;
            """,
            csharp);
        ref readonly var state = ref fixture.Plan.States.ItemRef(0);
        using var codeWriter = new CodeWriter("\r\n");
        var writer = new StateWriter(
            codeWriter,
            fixture.SourceMap,
            "global::Demo.PlannerView");

        writer.Write(state);

        var output = codeWriter.GetText().ToString();
        var propertyStart = output.IndexOf("private double width", StringComparison.Ordinal);
        var factoryStart = output.IndexOf("private double __CreateStateValue0", StringComparison.Ordinal);

        Assert.True(state.IsReadOnly);
        Assert.True(propertyStart >= 0, output);
        Assert.True(factoryStart > propertyStart, output);
        var property = output[propertyStart..factoryStart];
        Assert.Contains("get => __State0.Value;", property, StringComparison.Ordinal);
        Assert.DoesNotContain("set =>", property, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_HookStateUsesEffectiveInvocationAndFromStateFactory()
    {
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
                public static class PlannerHooks
                {
                    [UseHook]
                    public static State<double> useControlValue<T>(
                        [Self] T control,
                        AvaloniaProperty<double> property)
                        where T : Control => null!;
                }
            }
            """;
        var fixture = CreateFixture(
            """
            using Hooks;

            state double width = useControlValue(Width);
            """,
            csharp);
        var stateSyntax = Assert.Single(
            fixture.SemanticFixture.ComponentTree.GetRoot().Members
                .OfType<StateDeclarationSyntax>());
        var operation = Assert.IsAssignableFrom<IUseHookOperation>(
            fixture.SemanticFixture.SemanticModel.GetOperation(
                stateSyntax.Initializer));
        ref readonly var state = ref fixture.Plan.States.ItemRef(0);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var writer = new StateWriter(
            codeWriter,
            fixture.SourceMap,
            "global::Demo.PlannerView");

        writer.Write(state);

        var output = codeWriter.GetText().ToString();
        Assert.True(state.UsesHook);
        Assert.Equal(ComponentStateFactoryKind.State, state.FactoryKind);
        Assert.Equal(
            operation.EffectiveInvocation.ToFullString(),
            state.Initializer.ToFullString());
        Assert.Contains(
            "global::Akbura.ComponentTree.StateInfo<double>.FromState(",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            ".__CreateState0());",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "private global::Akbura.ComponentTree.State<double> __CreateState0()",
            output,
            StringComparison.Ordinal);
        Assert.Contains("this,", output, StringComparison.Ordinal);
        Assert.Contains("WidthProperty", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__CreateStateValue0", output, StringComparison.Ordinal);
        AssertSourceMappings(output);
        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    [Fact]
    public void GeneratedStateAndDescriptorMembers_Compile()
    {
        var fixture = CreateFixture("state int count = 0;");
        using var codeWriter = new CodeWriter("\r\n");
        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine(
            "public partial class PlannerView : global::Akbura.AkburaControl");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;

        ref readonly var state = ref fixture.Plan.States.ItemRef(0);
        var stateWriter = new StateWriter(
            codeWriter,
            fixture.SourceMap,
            "global::Demo.PlannerView");
        stateWriter.Write(state);
        codeWriter.WriteLine();
        var descriptorWriter = new DescriptorArrayWriter(codeWriter);
        descriptorWriter.Write(fixture.Plan);
        codeWriter.WriteLine();
        codeWriter.WriteLine(
            "protected override global::Avalonia.Controls.Control FirstUpdate() => new();");
        codeWriter.WriteLine(
            "protected override global::Avalonia.Controls.Control Update() => new();");
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        AssertCompiles(
            fixture.SemanticFixture.CSharpCompilation,
            codeWriter.GetText().ToString());
    }

    private static WriterFixture CreateFixture(
        string component,
        string? additionalCSharp = null)
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            additionalCSharp);
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

    private static void AssertSourceMappings(string output)
    {
        var mappingCount = CountOccurrences(output, "#line (");

        Assert.Equal(1, mappingCount);
        Assert.Equal(mappingCount, CountOccurrences(output, "#line default"));
        Assert.Equal(mappingCount, CountOccurrences(output, "#line hidden"));
        Assert.Contains("\"PlannerView.akbura\"", output, StringComparison.Ordinal);
    }

    private static void AssertCompiles(
        CSharpCompilation compilation,
        string generatedSource)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "StateWriterOutput.g.cs");
        var diagnostics = compilation.AddSyntaxTrees(syntaxTree).GetDiagnostics()
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
