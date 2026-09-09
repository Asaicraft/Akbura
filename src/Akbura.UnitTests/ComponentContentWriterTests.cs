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
    public void WriteStructuralConstantValue_UsesRuntimeValueReconciliation()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Button>Save</Button>
            """;
        var fixture = CreateFixture(
            component,
            generationMode: ComponentGenerationMode.DebugStructural);
        var plan = Assert.Single(fixture.Plan.PropertyContents);

        var output = WriteStructuralConstantValue(
            fixture,
            plan,
            out var wroteAny);

        Assert.True(wroteAny);
        Assert.Contains(".ReconcileAvaloniaValue(", output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.Controls.ContentControl.ContentProperty",
            output,
            StringComparison.Ordinal);
        Assert.Contains("\"Save\");", output, StringComparison.Ordinal);
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
    public void WriteStructuralCollection_PreservesMixedEagerItemOrder()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            state string suffix = "B";

            <MixedContentHost>A<Button />{suffix}<Border />A</MixedContentHost>
            """;
        const string csharp =
            """
            using Avalonia.Controls;
            using Avalonia.Metadata;
            using System.Collections.Generic;

            namespace Demo;

            public sealed class MixedContentHost : Control
            {
                [Content]
                public List<object> Items { get; } = new();
            }
            """;
        var fixture = CreateFixture(
            component,
            csharp,
            ComponentGenerationMode.DebugStructural);
        var plan = Assert.Single(fixture.Plan.CollectionContents);

        var output = WriteStructuralCollection(
            fixture,
            plan,
            out var wroteAny);

        Assert.True(wroteAny);
        Assert.Contains(".ReconcileCollection(", output, StringComparison.Ordinal);
        Assert.Contains("new object[]", output, StringComparison.Ordinal);

        var firstText = output.IndexOf("\"A\",", StringComparison.Ordinal);
        var button = output.IndexOf(
            "GetRequired<global::Avalonia.Controls.Button>",
            StringComparison.Ordinal);
        var expression = output.IndexOf("suffix,", StringComparison.Ordinal);
        var border = output.IndexOf(
            "GetRequired<global::Avalonia.Controls.Border>",
            StringComparison.Ordinal);
        var lastText = output.LastIndexOf("\"A\",", StringComparison.Ordinal);

        Assert.True(firstText >= 0);
        Assert.True(firstText < button);
        Assert.True(button < expression);
        Assert.True(expression < border);
        Assert.True(border < lastText);
    }

    [Fact]
    public void WriteStructuralCollection_GenericOnlyIListUsesTypedRuntimePathAndCompiles()
    {
        const string component =
            "using Avalonia.Controls;\r\n" +
            "using Demo;\r\n" +
            "\r\n" +
            "<GenericOnlyContentHost>\r\n" +
            "    <TextBlock />\r\n" +
            "    <Button />\r\n" +
            "</GenericOnlyContentHost>\r\n";
        const string csharp =
            "using Avalonia.Controls;\r\n" +
            "using Avalonia.Metadata;\r\n" +
            "using System.Collections;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "namespace Demo;\r\n" +
            "\r\n" +
            "public sealed class GenericOnlyContentHost : Control\r\n" +
            "{\r\n" +
            "    [Content]\r\n" +
            "    public GenericOnlyList<Control> Items { get; } = new();\r\n" +
            "}\r\n" +
            "\r\n" +
            "public sealed class GenericOnlyList<T> : IList<T>\r\n" +
            "{\r\n" +
            "    private readonly List<T> _items = new();\r\n" +
            "    public T this[int index] { get => _items[index]; set => _items[index] = value; }\r\n" +
            "    public int Count => _items.Count;\r\n" +
            "    public bool IsReadOnly => false;\r\n" +
            "    public void Add(T item) => _items.Add(item);\r\n" +
            "    public void Clear() => _items.Clear();\r\n" +
            "    public bool Contains(T item) => _items.Contains(item);\r\n" +
            "    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);\r\n" +
            "    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();\r\n" +
            "    public int IndexOf(T item) => _items.IndexOf(item);\r\n" +
            "    public void Insert(int index, T item) => _items.Insert(index, item);\r\n" +
            "    public bool Remove(T item) => _items.Remove(item);\r\n" +
            "    public void RemoveAt(int index) => _items.RemoveAt(index);\r\n" +
            "    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();\r\n" +
            "}\r\n";
        var fixture = CreateFixture(
            component,
            csharp,
            ComponentGenerationMode.DebugStructural);
        var plan = Assert.Single(fixture.Plan.CollectionContents);
        using var codeWriter = new CodeWriter("\n")
        {
            CurrentIndent = 8,
        };
        var contentWriter = new ComponentContentWriter(
            codeWriter,
            fixture.SourceMap);
        var wroteAny = contentWriter.WriteStructuralCollection(
            fixture.Plan,
            plan);
        var output = codeWriter.GetText().ToString();

        Assert.True(wroteAny);
        Assert.False(plan.Destination.SupportsUntypedReconciliation);
        Assert.True(plan.Destination.SupportsTypedReconciliation);
        Assert.Contains(
            ".ReconcileCollection<global::Avalonia.Controls.Control>(",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::Avalonia.Controls.Control[]",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new object[]", output, StringComparison.Ordinal);

        var generatedSource =
            "#nullable enable\r\n" +
            "namespace Demo;\r\n" +
            "internal static class StructuralCollectionOutput\r\n" +
            "{\r\n" +
            "    private static readonly global::Akbura.HotReload.AkburaRenderState " +
            "__akburaRenderState = new();\r\n" +
            "    private static void Apply()\r\n" +
            "    {\r\n" +
            output +
            "    }\r\n" +
            "}\r\n";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "StructuralCollectionOutput.g.cs");
        var errors = fixture.Compilation.AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
    }

    [Fact]
    public void WriteStructuralCollection_GenericOnlyValueIListUsesTypedRuntimePathAndCompiles()
    {
        const string component =
            "using Demo;\r\n" +
            "\r\n" +
            "<GenericOnlyValueContentHost>{1}</GenericOnlyValueContentHost>\r\n";
        const string csharp =
            "using Avalonia.Controls;\r\n" +
            "using Avalonia.Metadata;\r\n" +
            "using System.Collections;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "namespace Demo;\r\n" +
            "\r\n" +
            "public sealed class GenericOnlyValueContentHost : Control\r\n" +
            "{\r\n" +
            "    [Content]\r\n" +
            "    public GenericOnlyList<int> Items { get; } = new();\r\n" +
            "}\r\n" +
            "\r\n" +
            "public sealed class GenericOnlyList<T> : IList<T>\r\n" +
            "{\r\n" +
            "    private readonly List<T> _items = new();\r\n" +
            "    public T this[int index] { get => _items[index]; set => _items[index] = value; }\r\n" +
            "    public int Count => _items.Count;\r\n" +
            "    public bool IsReadOnly => false;\r\n" +
            "    public void Add(T item) => _items.Add(item);\r\n" +
            "    public void Clear() => _items.Clear();\r\n" +
            "    public bool Contains(T item) => _items.Contains(item);\r\n" +
            "    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);\r\n" +
            "    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();\r\n" +
            "    public int IndexOf(T item) => _items.IndexOf(item);\r\n" +
            "    public void Insert(int index, T item) => _items.Insert(index, item);\r\n" +
            "    public bool Remove(T item) => _items.Remove(item);\r\n" +
            "    public void RemoveAt(int index) => _items.RemoveAt(index);\r\n" +
            "    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();\r\n" +
            "}\r\n";
        var fixture = CreateFixture(
            component,
            csharp,
            ComponentGenerationMode.DebugStructural);
        var plan = Assert.Single(fixture.Plan.CollectionContents);
        using var codeWriter = new CodeWriter("\n")
        {
            CurrentIndent = 8,
        };
        var contentWriter = new ComponentContentWriter(
            codeWriter,
            fixture.SourceMap);
        var wroteAny = contentWriter.WriteStructuralCollection(
            fixture.Plan,
            plan);
        var output = codeWriter.GetText().ToString();

        Assert.True(wroteAny);
        Assert.False(plan.Destination.SupportsUntypedReconciliation);
        Assert.True(plan.Destination.SupportsTypedReconciliation);
        Assert.Contains(
            ".ReconcileCollection<int>(",
            output,
            StringComparison.Ordinal);
        Assert.Contains("new int[]", output, StringComparison.Ordinal);
        Assert.DoesNotContain("new object[]", output, StringComparison.Ordinal);

        var generatedSource =
            "#nullable enable\r\n" +
            "namespace Demo;\r\n" +
            "internal static class StructuralValueCollectionOutput\r\n" +
            "{\r\n" +
            "    private static readonly global::Akbura.HotReload.AkburaRenderState " +
            "__akburaRenderState = new();\r\n" +
            "    private static void Apply()\r\n" +
            "    {\r\n" +
            output +
            "    }\r\n" +
            "}\r\n";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "StructuralValueCollectionOutput.g.cs");
        var errors = fixture.Compilation.AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine + generatedSource);
    }

    [Fact]
    public void WriteStructuralCollection_ComponentParameterUsesRuntimeReconciliation()
    {
        const string component =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<CollectionHost>\r\n" +
            "    <TextBlock />\r\n" +
            "    <Button />\r\n" +
            "</CollectionHost>\r\n";
        const string collectionHost =
            "using Avalonia.Controls;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "param List<Control> Content;\r\n";
        var fixture = CreateFixtureWithChildComponent(
            component,
            collectionHost,
            "CollectionHost.akbura");
        var plan = Assert.Single(fixture.Plan.CollectionContents);

        var output = WriteStructuralCollection(
            fixture,
            plan,
            out var wroteAny);

        Assert.True(wroteAny);
        Assert.Equal(
            CollectionWriteKind.ComponentParameter,
            plan.Destination.Kind);
        Assert.Contains(
            ".ReconcileComponentCollection(",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "((global::System.Collections.Generic.List<" +
            "global::Avalonia.Controls.Control>)",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "__AkburaCompleteCollectionReconciliation_",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WriteStructuralCollection_GenericOnlyComponentParameterUsesTypedRuntimePath()
    {
        const string component =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "<CollectionHost>\r\n" +
            "    <TextBlock />\r\n" +
            "    <Button />\r\n" +
            "</CollectionHost>\r\n";
        const string collectionHost =
            "using Avalonia.Controls;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "param IList<Control> Content;\r\n";
        var fixture = CreateFixtureWithChildComponent(
            component,
            collectionHost,
            "CollectionHost.akbura");
        var plan = Assert.Single(fixture.Plan.CollectionContents);

        var output = WriteStructuralCollection(
            fixture,
            plan,
            out var wroteAny);

        Assert.True(wroteAny);
        Assert.Equal(
            CollectionWriteKind.ComponentParameter,
            plan.Destination.Kind);
        Assert.False(plan.Destination.SupportsUntypedReconciliation);
        Assert.True(plan.Destination.SupportsTypedReconciliation);
        Assert.Contains(
            ".ReconcileComponentCollection<global::Avalonia.Controls.Control>(",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "new global::Avalonia.Controls.Control[]",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new object[]", output, StringComparison.Ordinal);
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
        string? additionalCSharp = null,
        ComponentGenerationMode generationMode =
            ComponentGenerationMode.ReleaseDirect)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, additionalCSharp);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentPlanner.Create(
            componentSymbol,
            fixture.SemanticModel,
            new Dictionary<AkburaSyntax, string>(),
            generationMode);
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(fixture.ComponentTree, exactMatch: false));

        return new WriterFixture(plan, sourceMap, fixture.CSharpCompilation);
    }

    private static WriterFixture CreateFixtureWithChildComponent(
        string component,
        string childComponent,
        string childFileName)
    {
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var childTree = AkburaSyntaxTree.ParseText(
            childComponent,
            childFileName);
        var compilation = new AkburaCompilation(
            baseFixture.CSharpCompilation,
            [baseFixture.ComponentTree, childTree],
            rootNamespace: "Demo");
        var semanticModel = compilation.GetSemanticModel(
            baseFixture.ComponentTree);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(
                baseFixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentPlanner.Create(
            componentSymbol,
            semanticModel,
            new Dictionary<AkburaSyntax, string>(),
            ComponentGenerationMode.DebugStructural);
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(
                baseFixture.ComponentTree,
                exactMatch: false));

        return new WriterFixture(
            plan,
            sourceMap,
            baseFixture.CSharpCompilation);
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

    private static string WriteStructuralCollection(
        WriterFixture fixture,
        in ComponentCollectionContentPlan plan,
        out bool wroteAny)
    {
        using var codeWriter = new CodeWriter("\n");
        var writer = new ComponentContentWriter(codeWriter, fixture.SourceMap);
        wroteAny = writer.WriteStructuralCollection(fixture.Plan, plan);
        return codeWriter.GetText().ToString();
    }

    private static string WriteStructuralConstantValue(
        WriterFixture fixture,
        in ComponentPropertyContentPlan plan,
        out bool wroteAny)
    {
        using var codeWriter = new CodeWriter("\n");
        var writer = new ComponentContentWriter(codeWriter, fixture.SourceMap);
        wroteAny = writer.WriteStructuralConstantValue(fixture.Plan, plan);
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
