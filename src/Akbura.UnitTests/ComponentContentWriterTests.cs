using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class ComponentContentWriterTests
{
    [Fact]
    public void WriteProperty_ElementWritesOnlyDuringFirstUpdateAndEscapesIdentifiers()
    {
        const string component =
            """
            using Avalonia.Controls;

            <ContentControl x.Name="yield">
                <Border x.Name="async" />
            </ContentControl>
            """;
        var fixture = CreateFixture(component);
        var plan = Assert.Single(fixture.Plan.PropertyContents);

        var firstUpdate = WriteProperty(fixture, plan, isFirstUpdate: true, out var wroteFirstUpdate);
        var update = WriteProperty(fixture, plan, isFirstUpdate: false, out var wroteUpdate);

        Assert.True(wroteFirstUpdate);
        Assert.False(wroteUpdate);
        Assert.Contains("@yield", firstUpdate, StringComparison.Ordinal);
        Assert.Contains("@async", firstUpdate, StringComparison.Ordinal);
        Assert.Contains("ContentControl.ContentProperty", firstUpdate, StringComparison.Ordinal);
        Assert.Equal(string.Empty, update);
    }

    [Fact]
    public void WriteProperty_ConstantWritesOnlyDuringFirstUpdate()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Button x.Name="button">Save</Button>
            """;
        var fixture = CreateFixture(component);
        var plan = Assert.Single(fixture.Plan.PropertyContents);

        var firstUpdate = WriteProperty(fixture, plan, isFirstUpdate: true, out var wroteFirstUpdate);
        var update = WriteProperty(fixture, plan, isFirstUpdate: false, out var wroteUpdate);

        Assert.Equal(ComponentContentValueKind.Constant, plan.FirstUpdateValue.Kind);
        Assert.True(wroteFirstUpdate);
        Assert.False(wroteUpdate);
        Assert.Contains(
            "global::Avalonia.Controls.ContentControl.ContentProperty, \"Save\");",
            firstUpdate,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, update);
    }

    [Fact]
    public void WriteProperty_ExpressionWritesOnlyDuringUpdate()
    {
        const string component =
            """
            using Avalonia.Controls;

            state string caption = "";

            <Button x.Name="button">{caption}</Button>
            """;
        var fixture = CreateFixture(component);
        var plan = Assert.Single(fixture.Plan.PropertyContents);

        var firstUpdate = WriteProperty(fixture, plan, isFirstUpdate: true, out var wroteFirstUpdate);
        var update = WriteProperty(fixture, plan, isFirstUpdate: false, out var wroteUpdate);

        Assert.Equal(ComponentContentValueKind.CSharpExpression, plan.UpdateValue.Kind);
        Assert.False(wroteFirstUpdate);
        Assert.True(wroteUpdate);
        Assert.Equal(string.Empty, firstUpdate);
        Assert.Contains(
            "global::Avalonia.Controls.ContentControl.ContentProperty, caption);",
            update,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WriteCollection_PreservesItemOrderAndMapsEveryItem()
    {
        const string component =
            """
            using Avalonia.Controls;

            <StackPanel x.Name="panel">
                <Border x.Name="first" />
                <Button x.Name="second" />
            </StackPanel>
            """;
        var fixture = CreateFixture(component);
        var plan = Assert.Single(fixture.Plan.CollectionContents);

        var output = WriteCollection(fixture, plan, out var wroteAny);

        Assert.True(wroteAny);
        Assert.Equal(2, CountOccurrences(output, ".Add("));
        Assert.Equal(2, CountOccurrences(output, "#line ("));
        Assert.Equal(2, CountOccurrences(output, "#line default"));
        Assert.Equal(2, CountOccurrences(output, "#line hidden"));
        Assert.True(
            output.IndexOf("first);", StringComparison.Ordinal) <
            output.IndexOf("second);", StringComparison.Ordinal));
        Assert.Contains("\"PlannerView.akbura\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteProperty_DeferredAndTemplateContentProduceNoEagerOutput()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <Border />
            </DataTemplate>

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <Border />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var fixture = CreateFixture(component);
        var deferred = Assert.Single(
            fixture.Plan.PropertyContents,
            static plan => plan.FirstUpdateValue.Kind == ComponentContentValueKind.DeferredContent);
        var template = Assert.Single(
            fixture.Plan.PropertyContents,
            static plan => plan.FirstUpdateValue.Kind == ComponentContentValueKind.Template);

        AssertNoEagerOutput(fixture, deferred);
        AssertNoEagerOutput(fixture, template);
    }

    [Fact]
    public void GeneratedContentStatements_Compile()
    {
        const string component =
            """
            using Avalonia.Controls;

            <ContentControl x.Name="content">
                <Border x.Name="border" />
            </ContentControl>

            <Button x.Name="button">Save</Button>

            <StackPanel x.Name="panel">
                <TextBlock x.Name="text" />
            </StackPanel>
            """;
        var fixture = CreateFixture(component);
        using var codeWriter = new CodeWriter("\n")
        {
            CurrentIndent = 8,
        };
        var contentWriter = new ComponentContentWriter(codeWriter, fixture.SourceMap);

        for (var i = 0; i < fixture.Plan.PropertyContents.Length; i++)
        {
            contentWriter.WriteProperty(
                fixture.Plan,
                fixture.Plan.PropertyContents.ItemRef(i),
                isFirstUpdate: true);
        }

        for (var i = 0; i < fixture.Plan.CollectionContents.Length; i++)
        {
            contentWriter.WriteCollection(
                fixture.Plan,
                fixture.Plan.CollectionContents.ItemRef(i));
        }

        var statements = codeWriter.GetText().ToString();
        const string generatedStart =
            """
            #nullable enable

            namespace Demo;

            public partial class PlannerView
            {
                private static void Apply()
                {
                    var content = new global::Avalonia.Controls.ContentControl();
                    var border = new global::Avalonia.Controls.Border();
                    var button = new global::Avalonia.Controls.Button();
                    var panel = new global::Avalonia.Controls.StackPanel();
                    var text = new global::Avalonia.Controls.TextBlock();

            """;
        const string generatedEnd =
            """
                }
            }
            """;
        var generatedSource = generatedStart + statements + generatedEnd;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "ComponentContentWriterOutput.g.cs");
        var compilation = fixture.Compilation.AddSyntaxTrees(syntaxTree);
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
    }

    private static WriterFixture CreateFixture(
        string component,
        string? additionalCSharp = null)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, additionalCSharp);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentPlanner.Create(
            componentSymbol,
            fixture.SemanticModel,
            new Dictionary<AkburaSyntax, string>());
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(fixture.ComponentTree, exactMatch: false));

        return new WriterFixture(plan, sourceMap, fixture.CSharpCompilation);
    }

    private static string WriteProperty(
        WriterFixture fixture,
        in ComponentPropertyContentPlan plan,
        bool isFirstUpdate,
        out bool wroteAny)
    {
        using var codeWriter = new CodeWriter("\n");
        var writer = new ComponentContentWriter(codeWriter, fixture.SourceMap);
        wroteAny = writer.WriteProperty(fixture.Plan, plan, isFirstUpdate);
        return codeWriter.GetText().ToString();
    }

    private static string WriteCollection(
        WriterFixture fixture,
        in ComponentCollectionContentPlan plan,
        out bool wroteAny)
    {
        using var codeWriter = new CodeWriter("\n");
        var writer = new ComponentContentWriter(codeWriter, fixture.SourceMap);
        wroteAny = writer.WriteCollection(fixture.Plan, plan);
        return codeWriter.GetText().ToString();
    }

    private static void AssertNoEagerOutput(
        WriterFixture fixture,
        in ComponentPropertyContentPlan plan)
    {
        var firstUpdate = WriteProperty(fixture, plan, isFirstUpdate: true, out var wroteFirstUpdate);
        var update = WriteProperty(fixture, plan, isFirstUpdate: false, out var wroteUpdate);

        Assert.False(wroteFirstUpdate);
        Assert.False(wroteUpdate);
        Assert.Equal(string.Empty, firstUpdate);
        Assert.Equal(string.Empty, update);
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

    private sealed record WriterFixture(
        ComponentPlan Plan,
        ComponentGenerationSourceMap SourceMap,
        CSharpCompilation Compilation);
}
