using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class ComponentEagerContentIntegrationTests
{
    [Fact]
    public void ComponentWriter_WritesDynamicPropertyElementDuringUpdate()
    {
        const string component =
            """
            using Avalonia.Controls;

            state string title = "Current";

            <TextBlock>
                <TextBlock.Text>
                    {title}
                </TextBlock.Text>
            </TextBlock>
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component);
        using var codeWriter = new CodeWriter("\n");
        var writer = CreateWriter(codeWriter, fixture);
        var element = Assert.Single(writer.Plan.Elements);

        Assert.False(writer.WriteFirstUpdateContent(element.Id));
        Assert.False(writer.WritePropertyElements(element.Id));
        var firstUpdate = codeWriter.GetText().ToString();

        Assert.True(writer.WriteUpdateContent(element.Id));
        var update = codeWriter.GetText().ToString()[firstUpdate.Length..];

        Assert.DoesNotContain("TextBlock.TextProperty", firstUpdate, StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.Controls.TextBlock.TextProperty, title);",
            update,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentWriter_GeneratesCompleteEagerContentPipelineAndCompiles()
    {
        const string component =
            """
            using Avalonia.Controls;

            state string title = "Current";

            <StackPanel>
                <TextBlock>
                    {title}
                </TextBlock>

                <ContentControl>
                    <ContentControl.Content>
                        <Border />
                    </ContentControl.Content>
                </ContentControl>

                <Button>
                    Save
                </Button>
            </StackPanel>
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component);
        using var codeWriter = new CodeWriter("\n");
        var writer = CreateWriter(codeWriter, fixture);
        ref readonly var plan = ref writer.Plan;
        var stackPanel = GetElement(plan, "StackPanel");
        var textBlock = GetElement(plan, "TextBlock");
        var contentControl = GetElement(plan, "ContentControl");
        var border = GetElement(plan, "Border");
        var button = GetElement(plan, "Button");

        Assert.Equal(5, plan.Elements.Length);
        Assert.DoesNotContain(
            plan.Elements,
            static element => GetTagName(element).Contains('.', StringComparison.Ordinal));
        var propertyElement = Assert.Single(plan.PropertyElements);
        Assert.Equal(contentControl.Id, propertyElement.OwnerElementId);

        var stackPanelContent = Assert.Single(
            plan.CollectionContents,
            content => content.OwnerElementId == stackPanel.Id);
        Assert.Equal(3, stackPanelContent.Items.Length);
        Assert.Equal(
            [textBlock.Id, contentControl.Id, button.Id],
            plan.ContentItems
                .AsSpan(stackPanelContent.Items.Start, stackPanelContent.Items.Length)
                .ToArray()
                .Select(static item => item.Value.Index));

        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine();
        codeWriter.WriteLine("namespace Demo;");
        codeWriter.WriteLine();
        codeWriter.WriteLine("public partial class PlannerView");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine("private string title = \"Current\";");
        writer.WriteElementFields();
        codeWriter.WriteLine();
        codeWriter.WriteLine("public global::Avalonia.Controls.StackPanel Build()");
        codeWriter.WriteLine("{");
        codeWriter.CurrentIndent = 8;

        for (var i = 0; i < plan.Elements.Length; i++)
        {
            writer.WriteElementCreation(i);
            writer.WriteBeginInit(i);
        }

        var context = CreateWriteContext();
        var firstUpdateStart = codeWriter.Length;
        for (var i = 0; i < plan.Elements.Length; i++)
        {
            ref readonly var element = ref plan.Elements.ItemRef(i);
            writer.WriteFirstUpdateActions(i, context);
            writer.WriteFirstUpdateContent(i);
            writer.WritePropertyElements(i);
            writer.WriteSetStyles(i, element.Identifier, context);
        }

        var firstUpdateEnd = codeWriter.Length;
        for (var i = 0; i < plan.Elements.Length; i++)
        {
            ref readonly var element = ref plan.Elements.ItemRef(i);
            writer.WriteUpdateProperties(i, context);
            writer.WriteUpdateContent(i);
            writer.WriteRefresh(i, element.Identifier);
        }

        var updateEnd = codeWriter.Length;
        for (var i = plan.Elements.Length - 1; i >= 0; i--)
        {
            writer.WriteEndInit(i);
        }

        codeWriter.WriteLine("return " + stackPanel.Identifier + ";");
        codeWriter.CurrentIndent = 4;
        codeWriter.WriteLine("}");
        codeWriter.CurrentIndent = 0;
        codeWriter.WriteLine("}");

        var generatedSource = codeWriter.GetText().ToString();
        var firstUpdate = generatedSource.Substring(
            firstUpdateStart,
            firstUpdateEnd - firstUpdateStart);
        var update = generatedSource.Substring(
            firstUpdateEnd,
            updateEnd - firstUpdateEnd);

        Assert.Equal(3, CountOccurrences(firstUpdate, ".Add("));
        Assert.Contains(".Add(" + textBlock.Identifier + ");", firstUpdate, StringComparison.Ordinal);
        Assert.Contains(".Add(" + contentControl.Identifier + ");", firstUpdate, StringComparison.Ordinal);
        Assert.Contains(".Add(" + button.Identifier + ");", firstUpdate, StringComparison.Ordinal);
        Assert.DoesNotContain(".Add(" + border.Identifier + ");", firstUpdate, StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.Controls.ContentControl.ContentProperty, " + border.Identifier + ");",
            firstUpdate,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.Controls.ContentControl.ContentProperty, \"Save\");",
            firstUpdate,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TextBlock.TextProperty", firstUpdate, StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.Controls.TextBlock.TextProperty, title);",
            update,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ContentControl.ContentProperty", update, StringComparison.Ordinal);
        AssertSourceMappings(firstUpdate, expectedCount: 5);
        AssertSourceMappings(update, expectedCount: 1);

        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "ComponentEagerContentIntegration.g.cs");
        var diagnostics = fixture.CSharpCompilation.AddSyntaxTrees(generatedTree).GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
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

    private static ComponentElementPlan GetElement(in ComponentPlan plan, string tagName)
    {
        return Assert.Single(
            plan.Elements,
            element => string.Equals(GetTagName(element), tagName, StringComparison.Ordinal));
    }

    private static string GetTagName(ComponentElementPlan element)
    {
        return element.Syntax.StartTag?.Name.ToFullString().Trim() ?? string.Empty;
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
}
