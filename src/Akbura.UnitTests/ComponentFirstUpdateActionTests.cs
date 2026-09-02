using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class ComponentFirstUpdateActionTests
{
    [Fact]
    public void CreateAndWrite_PreservesSourceOrderAndEscapesClrEventAndNameIdentifiers()
    {
        const string component =
            """
            using Demo;

            state int count = 0;

            <EventControl x.Name="yield" Width="42" event={count++} />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, EventTypesSource);
        using var codeWriter = new CodeWriter("\n");
        var writer = CreateWriter(codeWriter, fixture);
        var element = Assert.Single(writer.Plan.Elements);
        var actions = writer.Plan.FirstUpdateActions
            .AsSpan(element.FirstUpdateActions.Start, element.FirstUpdateActions.Length)
            .ToArray();

        Assert.Equal(
            [
                ComponentFirstUpdateActionKind.NameAssignment,
                ComponentFirstUpdateActionKind.PropertyWrite,
                ComponentFirstUpdateActionKind.RoutedEvent,
            ],
            actions.Select(static action => action.Kind));
        var routedEvent = Assert.Single(writer.Plan.RoutedEvents);
        Assert.Equal(ComponentRoutedEventKind.ClrEvent, routedEvent.Kind);
        Assert.Equal("event", routedEvent.EventSymbol?.Name);

        var output = WriteFirstUpdateActionsAndAssertCompiles(codeWriter, writer, fixture);
        var nameIndex = output.IndexOf("@yield.Name = \"yield\";", StringComparison.Ordinal);
        var propertyIndex = output.IndexOf("WidthProperty", StringComparison.Ordinal);
        var eventIndex = output.IndexOf(").@event +=", StringComparison.Ordinal);

        Assert.True(nameIndex >= 0, output);
        Assert.True(propertyIndex > nameIndex, output);
        Assert.True(eventIndex > propertyIndex, output);
        AssertSourceMappings(output, expectedCount: 3);
    }

    [Fact]
    public void WriteFirstUpdateActions_EmitsAvaloniaRoutedEventRegistration()
    {
        const string component =
            """
            using Demo;

            state int count = 0;

            <RoutedOnlyControl Activated={count++} />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, EventTypesSource);
        using var codeWriter = new CodeWriter("\n");
        var writer = CreateWriter(codeWriter, fixture);
        var routedEvent = Assert.Single(writer.Plan.RoutedEvents);

        Assert.Equal(ComponentRoutedEventKind.AvaloniaRoutedEvent, routedEvent.Kind);
        Assert.Equal("ActivatedEvent", routedEvent.EventSymbol?.Name);

        var output = WriteFirstUpdateActionsAndAssertCompiles(codeWriter, writer, fixture);

        Assert.Contains(
            "((global::Avalonia.Interactivity.Interactive)__element0).AddHandler(" +
                "global::Demo.RoutedOnlyControl.ActivatedEvent, " +
                "(__eventArgument0, __eventArgument1) => { count++; });",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(".Activated +=", output, StringComparison.Ordinal);
        AssertSourceMappings(output, expectedCount: 1);
    }

    [Theory]
    [InlineData("Zero", "() => { count++; }")]
    [InlineData("One", "(__eventArgument0) => { count++; }")]
    [InlineData("Two", "(__eventArgument0, __eventArgument1) => { count++; }")]
    [InlineData("Three", "(__eventArgument0, __eventArgument1, __eventArgument2) => { count++; }")]
    public void ExpressionHandler_UsesDelegateArityAndGeneratedSnippetCompiles(
        string eventName,
        string expectedHandler)
    {
        var component =
            "using Demo;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<EventControl " + eventName + "={count++} />";
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, EventTypesSource);
        using var codeWriter = new CodeWriter("\n");
        var writer = CreateWriter(codeWriter, fixture);
        var routedEvent = Assert.Single(writer.Plan.RoutedEvents);

        Assert.Equal(ComponentRoutedEventKind.ClrEvent, routedEvent.Kind);
        Assert.Equal(eventName, routedEvent.EventSymbol?.Name);
        Assert.Equal(expectedHandler, routedEvent.HandlerExpression);

        var output = WriteFirstUpdateActionsAndAssertCompiles(codeWriter, writer, fixture);

        Assert.Contains("." + eventName + " += " + expectedHandler + ";", output, StringComparison.Ordinal);
        AssertSourceMappings(output, expectedCount: 1);
    }

    private static ComponentWriter CreateWriter(
        CodeWriter codeWriter,
        AkcssActivatorPlannerTests.PlannerFixture fixture)
    {
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);

        return new ComponentWriter(
            codeWriter,
            component,
            fixture.SemanticModel,
            "PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());
    }

    private static string WriteFirstUpdateActionsAndAssertCompiles(
        CodeWriter codeWriter,
        ComponentWriter writer,
        AkcssActivatorPlannerTests.PlannerFixture fixture)
    {
        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine("private int count;");
        writer.WriteElementFields();
        codeWriter.WriteLine();
        codeWriter.WriteLine("public void Build()");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 8;

        for (var i = 0; i < writer.Plan.Elements.Length; i++)
        {
            writer.WriteElementCreation(i);
        }

        var context = CreateWriteContext();
        var actionsStart = codeWriter.Length;
        for (var i = 0; i < writer.Plan.Elements.Length; i++)
        {
            Assert.True(writer.WriteFirstUpdateActions(i, context));
        }

        var actionsEnd = codeWriter.Length;

        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine("}");
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var generatedSource = codeWriter.GetText().ToString();
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "ComponentFirstUpdateActions.g.cs");
        var diagnostics = fixture.CSharpCompilation.AddSyntaxTrees(generatedTree).GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
        return generatedSource.Substring(actionsStart, actionsEnd - actionsStart);
    }

    private static MarkupExtensionWriteContext CreateWriteContext()
    {
        return new MarkupExtensionWriteContext(
            targetObjectExpression: "__target",
            targetProperty: MarkupTargetPropertyPlan.CreateExpression("__property"),
            intermediateRootExpression: "__root",
            baseUriExpression: "__baseUri",
            directParentsStackExpression: "__parents",
            fallbackServiceProviderExpression: null,
            nameScopeExpression: null,
            scopeId: 0);
    }

    private static void AssertSourceMappings(string output, int expectedCount)
    {
        Assert.Equal(expectedCount, CountOccurrences(output, "#line ("));
        Assert.Equal(expectedCount, CountOccurrences(output, "#line default"));
        Assert.Equal(expectedCount, CountOccurrences(output, "#line hidden"));
        Assert.Contains("\"PlannerView.akbura\"", output, StringComparison.Ordinal);
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

    private const string EventTypesSource =
        """
        namespace Demo;

        public delegate void ZeroEventHandler();

        public delegate void OneEventHandler(int value);

        public delegate void TwoEventHandler(object sender, System.EventArgs args);

        public delegate void ThreeEventHandler(int first, int second, int third);

        public sealed class EventControl : Avalonia.Controls.Control
        {
            public event System.EventHandler @event
            {
                add { }
                remove { }
            }

            public event ZeroEventHandler Zero
            {
                add { }
                remove { }
            }

            public event OneEventHandler One
            {
                add { }
                remove { }
            }

            public event TwoEventHandler Two
            {
                add { }
                remove { }
            }

            public event ThreeEventHandler Three
            {
                add { }
                remove { }
            }
        }

        public sealed class RoutedOnlyControl : Avalonia.Controls.Control
        {
            public static readonly Avalonia.Interactivity.RoutedEvent<Avalonia.Interactivity.RoutedEventArgs>
                ActivatedEvent = null!;
        }
        """;
}
