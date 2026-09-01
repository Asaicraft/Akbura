using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class AkcssActivatorWriterTests
{
    private const string OwnerTypeName = "global::Demo.PlannerView";

    [Fact]
    public void WriteStaticMembers_CachesClassesApplicationsAndUsesStaticUtilityLambdas()
    {
        var fixture = CreateBasicFixture();

        var output = WriteStaticMembers(fixture);

        Assert.Contains(
            "private static readonly global::Akbura.Akcss.AkcssClassActivator s_akcssClass0",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "ImmutableArray<global::Akbura.Akcss.AkcssUtilityApplication> s_akcssApplications0",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "ImmutableArray<global::Akbura.Akcss.AkcssUtilityApplication> s_akcssApplications1",
            output,
            StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(output, "static (__target, __arguments) =>"));
        Assert.Contains("global::Akbura.Akcss.ZeroAkcssUtility", output, StringComparison.Ordinal);
        Assert.Contains("global::Akbura.Akcss.AkcssUtility<double>", output, StringComparison.Ordinal);
        Assert.Contains("(double)__arguments[0]!", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSetStyles_WritesEveryMarkupExtensionValueSourceAndMethodGroups()
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateRichMarkupExtensionFixture();
        var fixture = CreateWriterFixture(
            semanticFixture,
            semanticFixture.GetRootElement(),
            requiresLocalMarkupExtensionContext: false);
        var output = WriteElement(fixture, elementIndex: 0, writeFactories: true);

        Assert.Contains(
            "global::Akbura.Akcss.AkcssUtilityValueSource.Create<double>(",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.Akcss.AkcssUtilityValueSource.CreateObject<",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.Akcss.AkcssUtilityValueSource.CreateObservable<",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.Akcss.AkcssUtilityValueSource.CreateObservableObject<",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.Akcss.AkcssUtilityValueSource.CreateBinding<",
            output,
            StringComparison.Ordinal);
        Assert.Contains("private double __CreateAkcssValue0(", output, StringComparison.Ordinal);
        Assert.Contains("__CreateAkcssValue1(", output, StringComparison.Ordinal);
        Assert.Contains(
            "__CreateAkcssValue2(",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "__CreateAkcssValue4(",
            output,
            StringComparison.Ordinal);
        Assert.Contains("recreateOnRefresh: true", output, StringComparison.Ordinal);
        Assert.Contains("recreateOnRefresh: false", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSetStyles_UsesObjectTargetDirectFactoryForOrdinaryObjects()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;
            using Demo.Extensions;

            @akcss {
                @using Demo;

                @utilities {
                    StyleData.value-(int value) { Value: value; }
                    StyleData.direct-(int value) { Value: value; }
                }
            }

            state int current = 5;

            <Button>
                <StyleData value-{current} direct-${DirectNumber} />
            </Button>
            """;
        const string csharp =
            """
            namespace Demo
            {
                public sealed class StyleData
                {
                    public int Value { get; set; }
                }
            }

            namespace Demo.Extensions
            {
                public sealed class DirectNumberExtension
                {
                    public int ProvideValue(System.IServiceProvider services) => 7;
                }
            }
            """;
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(component, csharp);
        var element = Assert.Single(semanticFixture.GetChildElements());
        var fixture = CreateWriterFixture(
            semanticFixture,
            element,
            requiresLocalMarkupExtensionContext: false);

        var output = WriteElement(fixture, elementIndex: 0, writeFactories: true);

        Assert.Contains(
            "global::Akbura.Akcss.AkcssUtilityValueSource.CreateForObject<int>(__CreateAkcssValue0",
            output,
            StringComparison.Ordinal);
        Assert.Contains("__CreateAkcssValue0", output, StringComparison.Ordinal);
        Assert.Contains("private int __CreateAkcssValue0(", output, StringComparison.Ordinal);
        Assert.Contains("object __target)", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "global::Akbura.Akcss.AkcssUtilityValueSource.Create<int>(",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSetStyles_WritesConditionVariantAndConstantBindingPriority()
    {
        var fixture = CreateVariantFixture(requiresLocalMarkupExtensionContext: false);
        var output = WriteElement(fixture, elementIndex: 0, writeFactories: true);

        Assert.Contains("condition: () => enabled", output, StringComparison.Ordinal);
        Assert.Contains("variant: global::Akbura.Akcss.AkcssUtilityValueSource.", output, StringComparison.Ordinal);
        Assert.Contains("order: 12d", output, StringComparison.Ordinal);
        Assert.Contains("conflictGroup: \"Tests\"", output, StringComparison.Ordinal);
        Assert.Contains(
            "unprefixedPrecedence: global::Akbura.Markup.UnprefixedUtilityPrecedence.Above",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "bindingPriority: (global::Avalonia.Data.BindingPriority)1",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PriorityMarkupExtension_UsesOneInstanceInMethodAndInlineFactories()
    {
        var fixture = CreateVariantFixture(
            requiresLocalMarkupExtensionContext: false,
            addLocalContextCopy: true);
        var methodOutput = WriteElement(fixture, elementIndex: 0, writeFactories: true);
        var inlineOutput = WriteElement(fixture, elementIndex: 1, writeFactories: false);
        const string creation = "new global::Demo.Extensions.priorityExtension(";

        Assert.Equal(1, CountOccurrences(methodOutput, creation));
        Assert.Contains("var __extension = " + creation, methodOutput, StringComparison.Ordinal);
        Assert.Contains("__extension.Priority", methodOutput, StringComparison.Ordinal);
        Assert.Contains(
            "CreateWithPriority<bool>(__CreateAkcssValue",
            methodOutput,
            StringComparison.Ordinal);

        Assert.Equal(1, CountOccurrences(inlineOutput, creation));
        Assert.Contains("__target => { var __extension = " + creation, inlineOutput, StringComparison.Ordinal);
        Assert.Contains("__extension.Priority", inlineOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("__CreateAkcssValue", inlineOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteSetStylesAndRefresh_WriteCallsAndSkipEmptyRanges()
    {
        var fixture = CreateBasicFixture();
        var output = WriteElement(fixture, elementIndex: 0, writeFactories: false, writeRefresh: true);

        Assert.Contains("global::Akbura.AkburaControl.SetAkcssStyles(", output, StringComparison.Ordinal);
        Assert.Contains(
            "global::System.Collections.Immutable.ImmutableArray.Create<" +
            "global::Akbura.Akcss.AkcssStyleActivator>(",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.AkburaControl.ExecuteAkcssStyles(__target);",
            output,
            StringComparison.Ordinal);

        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.Environment;
        var writer = new AkcssActivatorWriter(
            codeWriter,
            in environment,
            OwnerTypeName);
        var context = CreateWriteContext();
        var empty = new AkcssPlanRange(0, 0);

        Assert.False(
            writer.WriteFactoryMethods(
                fixture.Plan,
                fixture.Plan.Elements[0],
                context));
        writer.WriteSetStyles(fixture.Plan, empty, "", context);
        writer.WriteRefresh(empty, "");

        Assert.Equal(string.Empty, codeWriter.GetText().ToString());
    }

    [Fact]
    public void WriteFactoryMethods_UsesElementSlotRangeAndReturnsWhetherItWrites()
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateRichMarkupExtensionFixture();
        var fixture = CreateWriterFixture(
            semanticFixture,
            semanticFixture.GetRootElement(),
            false,
            true);
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.Environment;
        var writer = new AkcssActivatorWriter(
            codeWriter,
            in environment,
            OwnerTypeName);
        var context = CreateWriteContext();

        Assert.True(
            writer.WriteFactoryMethods(
                fixture.Plan,
                fixture.Plan.Elements[0],
                context));
        var lengthAfterFirstElement = codeWriter.Length;
        Assert.False(
            writer.WriteFactoryMethods(
                fixture.Plan,
                fixture.Plan.Elements[1],
                context));

        Assert.Equal(lengthAfterFirstElement, codeWriter.Length);
        Assert.Equal(
            5,
            CountOccurrences(
                codeWriter.GetText().ToString(),
                "private "));
    }

    [Fact]
    public void WriteStaticMembers_UsesExplicitOwnerTypeName()
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateRichMarkupExtensionFixture();
        var fixture = CreateWriterFixture(
            semanticFixture,
            semanticFixture.GetRootElement(),
            requiresLocalMarkupExtensionContext: false);

        var output = WriteStaticMembers(
            fixture,
            "global::Demo.GeneratedOwner");

        Assert.Contains(
            "RegisterAttached<global::Demo.GeneratedOwner, " +
            "global::Avalonia.Controls.Control, object?>",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedStaticMembersAndActivatorCalls_Compile()
    {
        var fixture = CreateBasicFixture();
        var members = Indent(WriteStaticMembers(fixture), 4);
        var body = Indent(
            WriteElement(fixture, elementIndex: 0, writeFactories: false, writeRefresh: true),
            8);
        var generatedSource =
            $$"""
            #nullable enable

            namespace Demo;

            public partial class PlannerView
            {
            {{members}}

                private static void Apply(global::Avalonia.Controls.Control __target)
                {
            {{body}}
                }
            }

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
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "AkcssActivatorWriterOutput.g.cs");
        var compilation = fixture.SemanticFixture.CSharpCompilation.AddSyntaxTrees(syntaxTree);
        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
    }

    private static WriterFixture CreateBasicFixture()
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
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(component);

        return CreateWriterFixture(
            semanticFixture,
            semanticFixture.GetRootElement(),
            requiresLocalMarkupExtensionContext: false);
    }

    private static WriterFixture CreateVariantFixture(
        bool requiresLocalMarkupExtensionContext,
        bool addLocalContextCopy = false)
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

            state bool enabled = true;

            <Border
                {enabled}:margin-1
                ${important}:margin-2
                ${priority Priority=Template}:margin-3 />
            """;
        const string csharp =
            """
            using Akbura.Markup;
            using Avalonia.Data;
            using System;

            namespace Demo.Extensions;

            [UtilityVariant(
                12d,
                ConflictGroup = "Tests",
                UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
            [UtilityBindingPriority(Priority = BindingPriority.StyleTrigger)]
            public sealed class importantExtension
            {
                public bool ProvideValue(IServiceProvider services) => true;
            }

            [UtilityBindingPriority(PriorityMember = nameof(Priority))]
            public sealed class priorityExtension
            {
                public BindingPriority Priority { get; set; }

                public bool ProvideValue(IServiceProvider services) => true;
            }
            """;
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(component, csharp);
        var element = semanticFixture.GetRootElement();
        var localContexts = addLocalContextCopy
            ? new[] { requiresLocalMarkupExtensionContext, true }
            : new[] { requiresLocalMarkupExtensionContext };
        return CreateWriterFixture(semanticFixture, element, localContexts);

    }

    private static WriterFixture CreateWriterFixture(
        AkcssActivatorPlannerTests.PlannerFixture semanticFixture,
        MarkupElementSyntax element,
        params bool[] requiresLocalMarkupExtensionContext)
    {
        var inlineAkcss = Assert.Single(
            semanticFixture.ComponentTree.GetRoot().Members.OfType<InlineAkcssBlockSyntax>());
        var inputs = ImmutableArray.CreateBuilder<AkcssActivatorElementInput>(
            requiresLocalMarkupExtensionContext.Length);
        var symbol = semanticFixture.GetElementSymbol(element);

        for (var i = 0; i < requiresLocalMarkupExtensionContext.Length; i++)
        {
            inputs.Add(new AkcssActivatorElementInput(
                i,
                symbol,
                requiresLocalMarkupExtensionContext[i]));
        }

        var plan = AkcssActivatorPlanner.Create(
            semanticFixture.SemanticModel,
            inputs.MoveToImmutable(),
            new Dictionary<AkburaSyntax, string>
            {
                [inlineAkcss] = "global::Demo.WriterStyles",
            });

        return new WriterFixture(
            semanticFixture,
            plan,
            semanticFixture.CreateBindingEnvironment());
    }

    private static string WriteStaticMembers(
        WriterFixture fixture,
        string ownerTypeName = OwnerTypeName)
    {
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.Environment;
        var writer = new AkcssActivatorWriter(
            codeWriter,
            in environment,
            ownerTypeName);

        writer.WriteStaticMembers(fixture.Plan);

        return codeWriter.GetText().ToString();
    }

    private static string WriteElement(
        WriterFixture fixture,
        int elementIndex,
        bool writeFactories,
        bool writeRefresh = false)
    {
        using var codeWriter = new CodeWriter("\n");
        var environment = fixture.Environment;
        var writer = new AkcssActivatorWriter(
            codeWriter,
            in environment,
            OwnerTypeName);
        var context = CreateWriteContext();
        var element = fixture.Plan.Elements[elementIndex];

        if (writeFactories &&
            writer.WriteFactoryMethods(
                fixture.Plan,
                element,
                context))
        {
            codeWriter.WriteLine();
        }

        writer.WriteSetStyles(fixture.Plan, element.Activators, "__target", context);

        if (writeRefresh)
        {
            writer.WriteRefresh(element.Activators, "__target");
        }

        return codeWriter.GetText().ToString();
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

    private static string Indent(string text, int spaces)
    {
        var indentation = new string(' ', spaces);
        return indentation + text.TrimEnd().Replace("\n", "\n" + indentation, StringComparison.Ordinal);
    }

    private sealed class WriterFixture(
        AkcssActivatorPlannerTests.PlannerFixture semanticFixture,
        AkcssComponentActivatorPlan plan,
        BindingWriterEnvironment environment)
    {
        public AkcssActivatorPlannerTests.PlannerFixture SemanticFixture { get; } = semanticFixture;

        public AkcssComponentActivatorPlan Plan { get; } = plan;

        public BindingWriterEnvironment Environment { get; } = environment;
    }
}
