using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class ComponentWriterTests
{
    [Fact]
    public void Constructor_BuildsOnePlanWithContiguousElementRanges()
    {
        var fixture = AkcssActivatorPlannerTests.CreateRichMarkupExtensionFixture();
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture, false, false);
        ref readonly var plan = ref writer.AkcssPlan;

        Assert.True(writer.HasAkcss);
        Assert.Equal(2, writer.Elements.Length);
        AssertRange(writer.Elements[0].Activators, start: 0, length: 5);
        AssertRange(writer.Elements[0].MarkupExtensionSlots, start: 0, length: 5);
        AssertRange(writer.Elements[1].Activators, start: 5, length: 5);
        AssertRange(writer.Elements[1].MarkupExtensionSlots, start: 5, length: 5);
        Assert.Equal(10, plan.Activators.Length);
        Assert.Equal(10, plan.MarkupExtensionSlots.Length);

        var context = CreateWriteContext();
        Assert.True(writer.WriteFactoryMethods(0, context));
        Assert.True(writer.WriteFactoryMethods(1, context));

        var output = codeWriter.GetText().ToString();
        Assert.Equal(10, CountOccurrences(output, "private "));
        Assert.Contains("__CreateAkcssValue0", output, StringComparison.Ordinal);
        Assert.Contains("__CreateAkcssValue9", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPlan_WriteMethodsReturnFalseAndDoNotChangeOutput()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component);
        using var codeWriter = new CodeWriter()
        {
            CurrentIndent = 8,
        };
        var writer = CreateWriter(codeWriter, fixture, false);
        var context = CreateWriteContext();

        Assert.False(writer.HasAkcss);
        Assert.False(writer.WriteStaticMembers());
        Assert.False(writer.WriteFactoryMethods(0, context));
        Assert.False(writer.WriteSetStyles(0, "__target", context));
        Assert.False(writer.WriteRefresh(0, "__target"));
        Assert.Equal(8, codeWriter.CurrentIndent);
        Assert.Equal(0, codeWriter.Length);
        Assert.Equal(string.Empty, codeWriter.GetText().ToString());
    }

    [Fact]
    public void RegularElement_WritesMethodGroupsWithCompactSignaturesAndPreservesIndent()
    {
        var fixture = AkcssActivatorPlannerTests.CreateRichMarkupExtensionFixture();
        using var codeWriter = new CodeWriter()
        {
            CurrentIndent = 8,
        };
        var writer = CreateWriter(codeWriter, fixture, false);
        var context = CreateWriteContext();

        codeWriter.WriteLine("// containing type");
        Assert.True(writer.WriteFactoryMethods(0, context));
        Assert.Equal(8, codeWriter.CurrentIndent);
        codeWriter.WriteLine();
        Assert.True(writer.WriteSetStyles(0, "__target", context));
        Assert.Equal(8, codeWriter.CurrentIndent);
        Assert.True(writer.WriteRefresh(0, "__target"));
        Assert.Equal(8, codeWriter.CurrentIndent);

        var output = codeWriter.GetText().ToString();
        Assert.Contains(
            "        private double __CreateAkcssValue0(" +
            "global::Avalonia.Controls.Control __target)\r\n",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__CreateAkcssValue0(\r\n", output, StringComparison.Ordinal);
        Assert.Contains("Create<double>(__CreateAkcssValue0", output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.AkburaControl.ExecuteAkcssStyles(__target);",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LocalElement_WritesInlineMarkupExtensionsWithoutFactoryMethods()
    {
        var fixture = AkcssActivatorPlannerTests.CreateRichMarkupExtensionFixture();
        using var codeWriter = new CodeWriter()
        {
            CurrentIndent = 4,
        };
        var writer = CreateWriter(codeWriter, fixture, true);
        var context = CreateWriteContext();

        Assert.False(writer.WriteFactoryMethods(0, context));
        Assert.Equal(0, codeWriter.Length);
        Assert.True(writer.WriteSetStyles(0, "__target", context));
        Assert.Equal(4, codeWriter.CurrentIndent);

        var output = codeWriter.GetText().ToString();
        Assert.Contains("new global::Demo.Extensions.DirectPaddingExtension(", output, StringComparison.Ordinal);
        Assert.Contains("__target =>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("private ", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__CreateAkcssValue", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryAndInlineMarkupExtensions_WriteSourceMappings()
    {
        var fixture = AkcssActivatorPlannerTests.CreateRichMarkupExtensionFixture();
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture, false, true);
        var context = CreateWriteContext();

        Assert.True(writer.WriteFactoryMethods(0, context));
        var factoryOutput = codeWriter.GetText().ToString();
        var inlineStart = codeWriter.Length;
        Assert.True(writer.WriteSetStyles(1, "__target", context));
        var output = codeWriter.GetText().ToString();
        var inlineOutput = output.Substring(inlineStart);

        AssertSourceMappings(factoryOutput, "PlannerView.akbura");
        AssertSourceMappings(inlineOutput, "PlannerView.akbura");
        Assert.Contains("__CreateAkcssValue0", factoryOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("__CreateAkcssValue", inlineOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void FactoryAndInlineSourceMappings_ParseInsideArgumentLists()
    {
        var fixture = AkcssActivatorPlannerTests.CreateRichMarkupExtensionFixture();
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture, false, true);
        var context = CreateWriteContext();

        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        Assert.True(writer.WriteFactoryMethods(0, context));
        codeWriter.WriteLine();
        codeWriter.WriteLine("private void Apply(global::Avalonia.Controls.Control __target)");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 8;
        Assert.True(writer.WriteSetStyles(1, "__target", context));
        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine("}");
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var source = codeWriter.GetText().ToString();
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "ComponentWriterMappings.g.cs");
        var errors = syntaxTree.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + source);
    }

    [Fact]
    public void TwoElements_CreateSeparateCandidatesAndShareStaticCaches()
    {
        var fixture = CreateBasicFixture();
        using var codeWriter = new CodeWriter()
        {
            CurrentIndent = 4,
        };
        var writer = CreateWriter(codeWriter, fixture, false, false);
        var context = CreateWriteContext();

        Assert.Single(writer.AkcssPlan.ClassCaches);
        Assert.Equal(2, writer.AkcssPlan.ApplicationCaches.Length);
        Assert.Equal(4, writer.AkcssPlan.Candidates.Length);
        Assert.True(writer.WriteStaticMembers());
        codeWriter.WriteLine();
        Assert.True(writer.WriteSetStyles(0, "__first", context));
        Assert.True(writer.WriteSetStyles(1, "__second", context));

        var output = codeWriter.GetText().ToString();
        Assert.Equal(
            1,
            CountOccurrences(
                output,
                "private static readonly global::Akbura.Akcss.AkcssClassActivator " +
                "s_akcssClass0"));
        Assert.Equal(
            1,
            CountOccurrences(
                output,
                "ImmutableArray<global::Akbura.Akcss.AkcssUtilityApplication> " +
                "s_akcssApplications0"));
        Assert.Equal(
            4,
            CountOccurrences(
                output,
                "new global::Akbura.Akcss.AkcssUtilityCandidateActivator("));
        Assert.Contains("__first,", output, StringComparison.Ordinal);
        Assert.Contains("__second,", output, StringComparison.Ordinal);
    }

    [Fact]
    public void PriorityExtension_IsCreatedOnceInMethodAndInlineFactories()
    {
        var fixture = CreatePriorityFixture();
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture, false, true);
        var context = CreateWriteContext();
        const string creation = "new global::Demo.Extensions.priorityExtension(";

        Assert.True(writer.WriteFactoryMethods(0, context));
        var methodOutput = codeWriter.GetText().ToString();
        var inlineStart = codeWriter.Length;
        Assert.True(writer.WriteSetStyles(1, "__target", context));
        var inlineOutput = codeWriter.GetText().ToString().Substring(inlineStart);

        Assert.Equal(1, CountOccurrences(methodOutput, creation));
        Assert.Contains("var __extension = " + creation, methodOutput, StringComparison.Ordinal);
        Assert.Contains("__extension.Priority", methodOutput, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(inlineOutput, creation));
        Assert.Contains("__extension.Priority", inlineOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("__CreateAkcssValue", inlineOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedStaticMembersAndActivatorCalls_Compile()
    {
        var fixture = CreateBasicFixture();
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture, false);
        var context = CreateWriteContext();

        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        Assert.True(writer.WriteStaticMembers());
        codeWriter.WriteLine();
        codeWriter.WriteLine("private static void Apply(global::Avalonia.Controls.Control __target)");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 8;
        Assert.True(writer.WriteSetStyles(0, "__target", context));
        Assert.True(writer.WriteRefresh(0, "__target"));
        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine("}");
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");
        codeWriter.WriteLine();
        codeWriter.WriteLine(
            """
            internal static class WriterStyles
            {
                public static readonly global::System.Collections.Immutable.ImmutableArray<
                    global::Akbura.Akcss.AkcssStyle> Styles =
                    global::System.Collections.Immutable.ImmutableArray.Create<global::Akbura.Akcss.AkcssStyle>(
                        new TestClass(),
                        new TestZeroUtility(),
                        new TestTypedUtility());

                private sealed class TestClass : global::Akbura.Akcss.AkcssClass
                {
                    public override void Update(object target)
                    {
                    }
                }

                private sealed class TestZeroUtility : global::Akbura.Akcss.ZeroAkcssUtility
                {
                    public override void Update(object target)
                    {
                    }
                }

                private sealed class TestTypedUtility : global::Akbura.Akcss.AkcssUtility<double>
                {
                    public override void Update(object target, double value)
                    {
                    }
                }
            }
            """);

        var generatedSource = codeWriter.GetText().ToString();
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "ComponentWriterOutput.g.cs");
        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(syntaxTree);
        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
    }

    private static AkcssActivatorPlannerTests.PlannerFixture CreateBasicFixture()
    {
        const string component =
            """
            using Avalonia.Controls;

            @akcss {
                @using Avalonia.Controls;

                .card { Height: 20; }

                @utilities {
                    Control.reset { Opacity: 1; }
                    Control.width-(double value) { Width: value; }
                }
            }

            <Border class="card" reset width-4 />
            """;

        return AkcssActivatorPlannerTests.CreateFixture(component);
    }

    private static AkcssActivatorPlannerTests.PlannerFixture CreatePriorityFixture()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            @akcss {
                @using Avalonia;
                @using Avalonia.Controls;

                @utilities {
                    Border.margin-(double value) { Margin: new Thickness(value); }
                }
            }

            <Border ${priority Priority=Template}:margin-3 />
            """;
        const string csharp =
            """
            using Akbura.Markup;
            using Avalonia.Data;
            using System;

            namespace Demo.Extensions;

            [UtilityBindingPriority(PriorityMember = nameof(Priority))]
            public sealed class priorityExtension
            {
                public BindingPriority Priority { get; set; }

                public bool ProvideValue(IServiceProvider services) => true;
            }
            """;

        return AkcssActivatorPlannerTests.CreateFixture(component, csharp);
    }

    private static ComponentWriter CreateWriter(
        CodeWriter codeWriter,
        AkcssActivatorPlannerTests.PlannerFixture fixture,
        params bool[] localContexts)
    {
        var element = fixture.GetRootElement();
        var elementSymbol = fixture.GetElementSymbol(element);
        var inputs = new AkcssActivatorElementInput[localContexts.Length];

        for (var i = 0; i < localContexts.Length; i++)
        {
            inputs[i] = new AkcssActivatorElementInput(i, elementSymbol, localContexts[i]);
        }

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var moduleTypeNames = new Dictionary<AkburaSyntax, string>();

        foreach (var inlineAkcss in fixture.ComponentTree.GetRoot().Members.OfType<InlineAkcssBlockSyntax>())
        {
            moduleTypeNames.Add(inlineAkcss, "global::Demo.WriterStyles");
        }

        return new ComponentWriter(
            codeWriter,
            component,
            fixture.SemanticModel,
            inputs,
            moduleTypeNames);
    }

    private static MarkupExtensionWriteContext CreateWriteContext()
    {
        return new MarkupExtensionWriteContext(
            targetObjectExpression: "__target",
            targetPropertyExpression: "__property",
            intermediateRootExpression: "__root",
            baseUriExpression: "__baseUri",
            directParentsStackExpression: "__parents",
            fallbackServiceProviderExpression: null,
            nameScopeExpression: null,
            scopeId: 0);
    }

    private static void AssertRange(AkcssPlanRange range, int start, int length)
    {
        Assert.Equal(start, range.Start);
        Assert.Equal(length, range.Length);
    }

    private static void AssertSourceMappings(string output, string sourcePath)
    {
        Assert.Contains("#line (", output, StringComparison.Ordinal);
        Assert.Contains("\"" + sourcePath + "\"", output, StringComparison.Ordinal);
        Assert.Contains("#line default", output, StringComparison.Ordinal);
        Assert.Contains("#line hidden", output, StringComparison.Ordinal);
        Assert.Equal(
            CountOccurrences(output, "#line default"),
            CountOccurrences(output, "#line hidden"));
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
}
