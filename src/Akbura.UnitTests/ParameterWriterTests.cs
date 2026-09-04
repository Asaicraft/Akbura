using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;

namespace Akbura.UnitTests;

public sealed class ParameterWriterTests
{
    [Fact]
    public void RegularParameter_WritesCachedDescriptorAndProperty()
    {
        var fixture = CreateFixture("param string Title;");

        var output = Write(fixture, parameterIndex: 0);

        Assert.Contains(
            "public static readonly global::Akbura.ComponentTree.Parameter<" +
            "global::Demo.PlannerView, string> TitleProperty =",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "global::Akbura.ComponentTree.Parameter.Create<" +
            "global::Demo.PlannerView, string>(",
            output,
            StringComparison.Ordinal);
        Assert.Contains("\"Title\",\r\n", output, StringComparison.Ordinal);
        Assert.Contains("default,\r\n", output, StringComparison.Ordinal);
        Assert.Contains(
            "get => GetValue(TitleProperty.AvaloniaProperty);",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "set => SetValue(TitleProperty.AvaloniaProperty, value);",
            output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "In")]
    [InlineData("bind ", "Bind")]
    [InlineData("out ", "Out")]
    public void Binding_WritesSemanticDirection(
        string bindingModifier,
        string expectedBinding)
    {
        var fixture = CreateFixture($"param {bindingModifier}int Value;");

        var output = Write(fixture, parameterIndex: 0);

        Assert.Contains(
            "global::Akbura.ComponentTree.ParameterBinding." + expectedBinding,
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultValue_WritesOptionalWithExactSourceMapping()
    {
        var fixture = CreateFixture(
            "param string Title =\r\n" +
            "    string.Concat(\r\n" +
            "        \"De\",\r\n" +
            "        \"fault\");");

        var output = Write(fixture, parameterIndex: 0, currentIndent: 4);

        Assert.Contains(
            "new global::Avalonia.Data.Optional<string>(",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "#line (2,5)-(",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"PlannerView.akbura\"",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "string.Concat(\r\n",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"De\",\r\n",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"fault\")\r\n",
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
    }

    [Fact]
    public void ContentParameter_WritesAttributeCallbackAndSingleLogicalChildHandler()
    {
        var fixture = CreateFixture(
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "param Control? Content;");

        var output = Write(fixture, parameterIndex: 0);

        Assert.Contains(
            "changed: static (__owner, __change) =>",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "__owner.__OnContentChanged(__change));",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::Avalonia.Metadata.Content]\r\n" +
            "public global::Avalonia.Controls.Control? Content",
            output,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(output, "__OnContentChanged"));
        Assert.Contains(
            "LogicalChildren.Remove(__oldContent);",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "LogicalChildren.Add(__newContent);",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ObservableCollectionContent_UsesStableNameAndSubscribesOnce()
    {
        var fixture = CreateFixture(
            "using Avalonia.Controls;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "param string Caption;\r\n" +
            "param IList<Control> Content;");

        var output = Write(fixture, parameterIndex: 1);

        Assert.Contains(
            "private readonly global::System.Collections.ObjectModel." +
            "ObservableCollection<global::Avalonia.Controls.Control> " +
            "__collection1 = [];",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "global::Akbura.ComponentTree.Parameter.CreateReadOnly<" +
            "global::Demo.PlannerView, global::System.Collections.Generic." +
            "IList<global::Avalonia.Controls.Control>>(",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "static __owner => __owner.Content);",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "public void __AkburaAddCollection_Content(" +
            "global::Avalonia.Controls.Control __value)",
            output,
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            CountOccurrences(
                output,
                "__collection1.CollectionChanged += " +
                "__OnContentCollectionChanged1;"));

        Assert.Contains(
            "if (!__contentSubscribed1)",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "__contentSubscribed1 = true;",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionContent_WritesLogicalChildrenSynchronization()
    {
        var fixture = CreateFixture(
            "using Avalonia.Controls;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "param IList<Control> Content;");

        var output = Write(fixture, parameterIndex: 0);

        Assert.Contains(
            "private readonly global::System.Collections.Generic.List<" +
            "global::Avalonia.Controls.Control> " +
            "__contentLogicalChildren0 = [];",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "private void __SynchronizeContentLogicalChildren0()",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "LogicalChildren.Remove(__oldContent);",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (__item is global::Avalonia.Controls.Control __contentControl &&",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "LogicalChildren.Add(__contentControl);",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "__contentLogicalChildren0.Add(__contentControl);",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "case global::System.Collections.Specialized." +
            "NotifyCollectionChangedAction.Reset:",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NonObservableCollectionContent_SynchronizesFromPublicAddHelper()
    {
        var fixture = CreateFixture(
            "using Avalonia.Controls;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "param List<Control> Content;");

        var output = Write(fixture, parameterIndex: 0);

        Assert.DoesNotContain(
            "CollectionChanged +=",
            output,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "__contentSubscribed0",
            output,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "__OnContentCollectionChanged0",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "public void __AkburaAddCollection_Content(" +
            "global::Avalonia.Controls.Control __value)\r\n" +
            "{\r\n" +
            "    Content.Add(__value);\r\n" +
            "    __SynchronizeContentLogicalChildren0();\r\n" +
            "}",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionTypes_PreserveNullableElementAnnotations()
    {
        var fixture = CreateFixture(
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "param IList<string?> Items;");

        var output = Write(fixture, parameterIndex: 0);

        Assert.Contains(
            "global::System.Collections.ObjectModel.ObservableCollection<string?> " +
            "__collection0",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "global::System.Collections.Generic.IList<string?> Items",
            output,
            StringComparison.Ordinal);

        Assert.Contains(
            "public void __AkburaAddCollection_Items(string? __value)",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedRegularAndCollectionParameters_Compile()
    {
        var regularFixture = CreateFixture(
            "param string Title = \"Default\";\r\n" +
            "param bind int Value;");
        var collectionFixture = CreateFixture(
            "using Avalonia.Controls;\r\n" +
            "using System.Collections.Generic;\r\n" +
            "\r\n" +
            "param IList<Control> Content;");

        AssertGeneratedParametersCompile(regularFixture);
        AssertGeneratedParametersCompile(collectionFixture);
    }

    private static WriterFixture CreateFixture(string componentSource)
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(componentSource);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticFixture.SemanticModel.GetSymbolInfo(
                semanticFixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentMemberPlanner.Create(
            componentSymbol,
            semanticFixture.SemanticModel);
        var syntaxTree = Assert.IsType<ComponentSyntaxTree>(
            semanticFixture.ComponentTree,
            exactMatch: false);

        return new WriterFixture(
            semanticFixture,
            plan,
            new ComponentGenerationSourceMap(syntaxTree));
    }

    private static string Write(
        WriterFixture fixture,
        int parameterIndex,
        int currentIndent = 0)
    {
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = currentIndent,
        };
        var writer = new ParameterWriter(
            codeWriter,
            fixture.SourceMap,
            "global::Demo.PlannerView");

        writer.Write(fixture.Plan.Parameters.ItemRef(parameterIndex));

        Assert.Equal(currentIndent, codeWriter.CurrentIndent);
        return codeWriter.GetText().ToString();
    }

    private static void AssertGeneratedParametersCompile(WriterFixture fixture)
    {
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var writer = new ParameterWriter(
            codeWriter,
            fixture.SourceMap,
            "global::Demo.PlannerView");

        for (var i = 0; i < fixture.Plan.Parameters.Length; i++)
        {
            if (i > 0)
            {
                codeWriter.WriteLine();
            }

            writer.Write(fixture.Plan.Parameters.ItemRef(i));
        }

        var generatedMembers = codeWriter.GetText().ToString();
        var generatedSource =
            "#nullable enable\r\n" +
            "\r\n" +
            "namespace Demo;\r\n" +
            "\r\n" +
            "public abstract partial class PlannerView : " +
            "global::Akbura.AkburaControl\r\n" +
            "{\r\n" +
            generatedMembers +
            "}\r\n";
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "ParameterWriterOutput.g.cs",
            encoding: Encoding.UTF8);
        var errors = fixture.SemanticFixture.CSharpCompilation
            .AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(
                Environment.NewLine,
                errors.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            generatedSource);
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
        AkcssActivatorPlannerTests.PlannerFixture SemanticFixture,
        ComponentMemberPlan Plan,
        ComponentGenerationSourceMap SourceMap);
}
