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
        var fixture = CreateRichComponentFixture();
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture);
        ref readonly var plan = ref writer.Plan;
        var regular = GetStyledElement(plan, requiresLocalContext: false);
        var local = GetStyledElement(plan, requiresLocalContext: true);

        Assert.True(writer.HasAkcss);
        Assert.Equal(plan.Elements, writer.Elements);
        AssertRange(regular.Akcss.Activators, start: 0, length: 5);
        AssertRange(regular.Akcss.MarkupExtensionSlots, start: 0, length: 5);
        AssertRange(local.Akcss.Activators, start: 5, length: 5);
        AssertRange(local.Akcss.MarkupExtensionSlots, start: 5, length: 5);
        Assert.Equal(10, plan.Akcss.Activators.Length);
        Assert.Equal(10, plan.Akcss.MarkupExtensionSlots.Length);

        var context = CreateWriteContext();
        Assert.True(writer.WriteFactoryMethods(regular.Id, context));
        Assert.False(writer.WriteFactoryMethods(local.Id, context));

        var output = codeWriter.GetText().ToString();
        Assert.Equal(5, CountOccurrences(output, "private "));
        Assert.Contains("__CreateAkcssValue0", output, StringComparison.Ordinal);
        Assert.Contains("__CreateAkcssValue4", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__CreateAkcssValue5", output, StringComparison.Ordinal);
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
        var writer = CreateWriter(codeWriter, fixture);
        var context = CreateWriteContext();
        var elementId = Assert.Single(writer.Elements).Id;

        Assert.False(writer.HasAkcss);
        Assert.False(writer.WriteStaticMembers());
        Assert.False(writer.WriteFactoryMethods(elementId, context));
        Assert.False(writer.WriteSetStyles(elementId, "__target", context));
        Assert.False(writer.WriteRefresh(elementId, "__target"));
        Assert.Equal(8, codeWriter.CurrentIndent);
        Assert.Equal(0, codeWriter.Length);
        Assert.Equal(string.Empty, codeWriter.GetText().ToString());
    }

    [Fact]
    public void WriteElementFields_WritesOnlyComponentElementsAndPreservesIndent()
    {
        var fixture = CreateRichComponentFixture();
        using var codeWriter = new CodeWriter
        {
            CurrentIndent = 4,
        };
        var writer = CreateWriter(codeWriter, fixture);
        var componentElements = writer.Plan.Elements.Where(static element => !element.IsLocal).ToArray();
        var localElements = writer.Plan.Elements.Where(static element => element.IsLocal).ToArray();

        Assert.NotEmpty(componentElements);
        Assert.NotEmpty(localElements);
        Assert.True(writer.WriteElementFields());
        Assert.Equal(4, codeWriter.CurrentIndent);

        var output = codeWriter.GetText().ToString();
        Assert.Equal(componentElements.Length, CountOccurrences(output, "private "));
        Assert.All(componentElements, element =>
            Assert.Contains(" " + element.Identifier + " = null!;", output, StringComparison.Ordinal));
        Assert.All(localElements, element =>
            Assert.DoesNotContain(" " + element.Identifier + " = null!;", output, StringComparison.Ordinal));
    }

    [Fact]
    public void WriteElementCreation_UsesPlannedLifetimeAndWritesSourceMappings()
    {
        var fixture = CreateRichComponentFixture();
        using var codeWriter = new CodeWriter
        {
            CurrentIndent = 4,
        };
        var writer = CreateWriter(codeWriter, fixture);
        var field = Assert.Single(
            writer.Plan.Elements,
            static element => !element.IsLocal && element.Syntax.StartTag?.Name.ToFullString().Trim() == "Border");
        var local = Assert.Single(
            writer.Plan.Elements,
            static element => element.IsLocal && element.Syntax.StartTag?.Name.ToFullString().Trim() == "Border");

        writer.WriteElementCreation(field.Id);
        writer.WriteElementCreation(local.Id);

        Assert.Equal(4, codeWriter.CurrentIndent);
        var output = codeWriter.GetText().ToString();
        Assert.Contains(
            field.Identifier + " = new global::Avalonia.Controls.Border();",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "var " + local.Identifier + " = new global::Avalonia.Controls.Border();",
            output,
            StringComparison.Ordinal);
        AssertSourceMappings(output, "PlannerView.akbura");
        Assert.Equal(2, CountOccurrences(output, "#line ("));
    }

    [Fact]
    public void ElementMethods_GenerateCompilableExplicitInitializationCalls()
    {
        var fixture = CreateInitializationFixture();
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture);

        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        Assert.True(writer.WriteElementFields());
        codeWriter.WriteLine();
        codeWriter.WriteLine("public void Build()");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 8;

        for (var i = 0; i < writer.Plan.Elements.Length; i++)
        {
            writer.WriteElementCreation(i);
            writer.WriteBeginInit(i);
        }

        for (var i = writer.Plan.Elements.Length - 1; i >= 0; i--)
        {
            writer.WriteEndInit(i);
        }

        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine("}");
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var generatedSource = codeWriter.GetText().ToString();
        var initializedElementCount = writer.Plan.Elements.Count(static element => element.SupportsInitialize);
        Assert.NotEqual(0, initializedElementCount);
        Assert.Equal(
            initializedElementCount * 2,
            CountOccurrences(generatedSource, "ISupportInitialize"));
        Assert.Contains(").BeginInit();", generatedSource, StringComparison.Ordinal);
        Assert.Contains(").EndInit();", generatedSource, StringComparison.Ordinal);
        AssertSourceMappings(generatedSource, "PlannerView.akbura");

        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "ComponentWriterElements.g.cs");
        var diagnostics = fixture.CSharpCompilation.AddSyntaxTrees(generatedTree).GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
    }

    [Fact]
    public void ElementMethods_RejectInvalidElementIds()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component);
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture);

        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteElementCreation(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteBeginInit(writer.Plan.Elements.Length));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.WriteEndInit(writer.Plan.Elements.Length));
    }

    [Fact]
    public void RegularElement_WritesMethodGroupsWithCompactSignaturesAndPreservesIndent()
    {
        var fixture = CreateRichComponentFixture();
        using var codeWriter = new CodeWriter()
        {
            CurrentIndent = 8,
        };
        var writer = CreateWriter(codeWriter, fixture);
        var context = CreateWriteContext();
        var elementId = GetStyledElement(writer.Plan, requiresLocalContext: false).Id;

        codeWriter.WriteLine("// containing type");
        Assert.True(writer.WriteFactoryMethods(elementId, context));
        Assert.Equal(8, codeWriter.CurrentIndent);
        codeWriter.WriteLine();
        Assert.True(writer.WriteSetStyles(elementId, "__target", context));
        Assert.Equal(8, codeWriter.CurrentIndent);
        Assert.True(writer.WriteRefresh(elementId, "__target"));
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
        var fixture = CreateRichComponentFixture();
        using var codeWriter = new CodeWriter()
        {
            CurrentIndent = 4,
        };
        var writer = CreateWriter(codeWriter, fixture);
        var context = CreateWriteContext();
        var elementId = GetStyledElement(writer.Plan, requiresLocalContext: true).Id;

        Assert.False(writer.WriteFactoryMethods(elementId, context));
        Assert.Equal(0, codeWriter.Length);
        Assert.True(writer.WriteSetStyles(elementId, "__target", context));
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
        var fixture = CreateRichComponentFixture();
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture);
        var context = CreateWriteContext();
        var regularId = GetStyledElement(writer.Plan, requiresLocalContext: false).Id;
        var localId = GetStyledElement(writer.Plan, requiresLocalContext: true).Id;

        Assert.True(writer.WriteFactoryMethods(regularId, context));
        var factoryOutput = codeWriter.GetText().ToString();
        var inlineStart = codeWriter.Length;
        Assert.True(writer.WriteSetStyles(localId, "__target", context));
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
        var fixture = CreateRichComponentFixture();
        using var codeWriter = new CodeWriter();
        var writer = CreateWriter(codeWriter, fixture);
        var context = CreateWriteContext();
        var regularId = GetStyledElement(writer.Plan, requiresLocalContext: false).Id;
        var localId = GetStyledElement(writer.Plan, requiresLocalContext: true).Id;

        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        Assert.True(writer.WriteFactoryMethods(regularId, context));
        codeWriter.WriteLine();
        codeWriter.WriteLine("private void Apply(global::Avalonia.Controls.Control __target)");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 8;
        Assert.True(writer.WriteSetStyles(localId, "__target", context));
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
        var writer = CreateWriter(codeWriter, fixture);
        var context = CreateWriteContext();
        var elements = GetStyledElements(writer.Plan, requiresLocalContext: false);

        Assert.Equal(2, elements.Length);
        Assert.Single(writer.Plan.Akcss.ClassCaches);
        Assert.Equal(2, writer.Plan.Akcss.ApplicationCaches.Length);
        Assert.Equal(4, writer.Plan.Akcss.Candidates.Length);
        Assert.True(writer.WriteStaticMembers());
        codeWriter.WriteLine();
        Assert.True(writer.WriteSetStyles(elements[0].Id, "__first", context));
        Assert.True(writer.WriteSetStyles(elements[1].Id, "__second", context));

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
        var writer = CreateWriter(codeWriter, fixture);
        var context = CreateWriteContext();
        var regularId = GetStyledElement(writer.Plan, requiresLocalContext: false).Id;
        var localId = GetStyledElement(writer.Plan, requiresLocalContext: true).Id;
        const string creation = "new global::Demo.Extensions.priorityExtension(";

        Assert.True(writer.WriteFactoryMethods(regularId, context));
        var methodOutput = codeWriter.GetText().ToString();
        var inlineStart = codeWriter.Length;
        Assert.True(writer.WriteSetStyles(localId, "__target", context));
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
        var writer = CreateWriter(codeWriter, fixture);
        var context = CreateWriteContext();
        var elementId = GetStyledElements(writer.Plan, requiresLocalContext: false)[0].Id;

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
        Assert.True(writer.WriteSetStyles(elementId, "__target", context));
        Assert.True(writer.WriteRefresh(elementId, "__target"));
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

            <StackPanel>
                <Border class="card" reset width-4 />
                <Border class="card" reset width-4 />
            </StackPanel>
            """;

        return AkcssActivatorPlannerTests.CreateFixture(component);
    }

    private static AkcssActivatorPlannerTests.PlannerFixture CreateInitializationFixture()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <ExplicitInitializableControl />
            <Border />
            """;
        const string csharp =
            """
            using Avalonia.Controls;
            using System.ComponentModel;

            namespace Demo;

            public sealed class ExplicitInitializableControl : Control, ISupportInitialize
            {
                void ISupportInitialize.BeginInit()
                {
                }

                void ISupportInitialize.EndInit()
                {
                }
            }
            """;

        return AkcssActivatorPlannerTests.CreateFixture(component, csharp);
    }

    private static AkcssActivatorPlannerTests.PlannerFixture CreateRichComponentFixture()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;
            using Demo.Extensions;

            @akcss {
                @using Avalonia.Controls;

                @utilities {
                    Control.direct-(double value) { Width: value; }
                    Control.late-(object value) { DataContext: value; }
                    Control.observable-(double value) { Width: value; }
                    Control.object-observable-(double value) { Width: value; }
                    Control.binding-(double value) { Width: value; }
                }
            }

            state double spacing = 4;

            <StackPanel>
                <Border
                    direct-${DirectPadding {spacing + 1}}
                    late-${ObjectPadding}
                    observable-${ObservablePadding}
                    object-observable-${ObjectObservablePadding}
                    binding-${BindingPadding} />
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border
                                direct-${DirectPadding {spacing + 1}}
                                late-${ObjectPadding}
                                observable-${ObservablePadding}
                                object-observable-${ObjectObservablePadding}
                                binding-${BindingPadding} />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            """;

        return AkcssActivatorPlannerTests.CreateFixture(
            component,
            AkcssActivatorPlannerTests.ExtensionSource);
    }

    private static AkcssActivatorPlannerTests.PlannerFixture CreatePriorityFixture()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;
            using Demo.Extensions;

            @akcss {
                @using Avalonia;
                @using Avalonia.Controls;

                @utilities {
                    Border.margin-(double value) { Margin: new Thickness(value); }
                }
            }

            <StackPanel>
                <Border ${priority Priority=Template}:margin-3 />
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border ${priority Priority=Template}:margin-3 />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
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
        AkcssActivatorPlannerTests.PlannerFixture fixture)
    {
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
            moduleTypeNames);
    }

    private static ComponentElementPlan GetStyledElement(
        in ComponentPlan plan,
        bool requiresLocalContext)
    {
        return Assert.Single(GetStyledElements(plan, requiresLocalContext));
    }

    private static ComponentElementPlan[] GetStyledElements(
        in ComponentPlan plan,
        bool requiresLocalContext)
    {
        return plan.Elements
            .Where(element =>
                !element.Akcss.Activators.IsEmpty &&
                element.RequiresLocalMarkupContext == requiresLocalContext)
            .ToArray();
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
        var mappingCount = CountOccurrences(output, "#line (");
        Assert.Equal(mappingCount, CountOccurrences(output, "#line default"));
        Assert.Equal(mappingCount, CountOccurrences(output, "#line hidden"));
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
