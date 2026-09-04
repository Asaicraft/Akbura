using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Text;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.UnitTests;

public sealed class TemplateWriterTests
{
    [Fact]
    public void WritePropertyElements_WritesTypedTemplateUsingScopeLifecycle()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                    <StackPanel x.Name="panel">
                        <TextBlock x.Name="message" Text={person.Name} />
                    </StackPanel>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        using var fixture = CreateFixture(component, PersonSource, currentIndent: 8);
        var plan = fixture.Writer.Plan;
        var template = Assert.Single(plan.Templates);
        ref readonly var scope = ref plan.Scopes.ItemRef(template.ScopeId);
        var owner = plan.Elements[template.OwnerElementId];
        var context = CreateMarkupContext(owner);
        var elementIds = GetScopeElementIds(plan, scope);

        Assert.True(fixture.Writer.WritePropertyElements(owner.Id, context));
        Assert.Equal(8, fixture.CodeWriter.CurrentIndent);

        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains(
            "new global::Avalonia.Controls.Templates.FuncDataTemplate<global::Demo.Person>" +
            "((person, __nameScope) =>",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("supportsRecycling", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__template", output, StringComparison.Ordinal);
        Assert.DoesNotContain("DataContext =", output, StringComparison.Ordinal);
        Assert.Contains("panel.Name = \"panel\";", output, StringComparison.Ordinal);
        Assert.Contains(
            "__nameScope.Register(\"panel\", panel);",
            output,
            StringComparison.Ordinal);
        Assert.Contains("message.Name = \"message\";", output, StringComparison.Ordinal);
        Assert.Contains(
            "__nameScope.Register(\"message\", message);",
            output,
            StringComparison.Ordinal);
        Assert.Contains("person.Name", output, StringComparison.Ordinal);

        var lastCreation = -1;
        foreach (var elementId in elementIds)
        {
            ref readonly var element = ref plan.Elements.ItemRef(elementId);
            var creation = GetIdentifier(element) + " = new " + GetTypeName(element.Type) + "();";
            var creationIndex = output.IndexOf(creation, StringComparison.Ordinal);

            Assert.True(creationIndex > lastCreation, output);
            lastCreation = creationIndex;
        }

        var initializable = elementIds
            .Select(elementId => plan.Elements[elementId])
            .Where(static element => element.SupportsInitialize)
            .ToArray();
        var beginIndices = initializable
            .Select(element => IndexOfLifecycleCall(output, element, "BeginInit"))
            .ToArray();
        var endIndices = initializable
            .Select(element => IndexOfLifecycleCall(output, element, "EndInit"))
            .ToArray();

        Assert.NotEmpty(initializable);
        AssertStrictlyIncreasing(beginIndices);
        AssertStrictlyDecreasing(endIndices);
        Assert.True(lastCreation < beginIndices[0], output);

        var rootId = plan.ScopeRootElementIds[scope.Roots.Start];
        var returnIndex = output.IndexOf(
            "return " + GetIdentifier(plan.Elements[rootId]) + ";",
            StringComparison.Ordinal);
        Assert.True(returnIndex > endIndices[0], output);
        AssertBalancedSourceMappings(output);
    }

    [Fact]
    public void WritePropertyElements_UsesDefaultItemName()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Person">
                    <TextBlock />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        using var fixture = CreateFixture(component, PersonSource);
        var plan = fixture.Writer.Plan;
        var template = Assert.Single(plan.Templates);
        var owner = plan.Elements[template.OwnerElementId];

        Assert.True(fixture.Writer.WritePropertyElements(owner.Id, CreateMarkupContext(owner)));

        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains(
            "FuncDataTemplate<global::Demo.Person>((__item, __nameScope) =>",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ComponentTemplate_MarkupExtensionUsesFullParentHierarchy()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <ItemsControl x.Name="items">
                <ItemsControl.ItemTemplate x.DataType="Person">
                    <Border x.Name="templateRoot"
                            Background=${DynamicResource AccentBrush} />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        using var fixture = CreateFixture(component, PersonSource);
        var plan = fixture.Writer.Plan;
        var template = Assert.Single(plan.Templates);
        var owner = plan.Elements[template.OwnerElementId];

        Assert.True(fixture.Writer.WritePropertyElements(owner.Id, CreateMarkupContext(owner)));

        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains(
            "directParentsStack: new global::System.Object[] { this, items, templateRoot }",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("fallbackServiceProvider:", output, StringComparison.Ordinal);
    }

    [Fact]
    public void DeferredTemplate_MarkupExtensionUsesExactHierarchyAndFallbackProvider()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;
            using Demo;

            <DataTemplate>
                <ItemsControl x.Name="deferredOwner">
                    <ItemsControl.ItemTemplate x.DataType="Person">
                        <Border x.Name="templateRoot"
                                Background=${DynamicResource AccentBrush} />
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </DataTemplate>
            """;
        using var fixture = CreateFixture(component, PersonSource);

        Assert.True(fixture.Writer.WriteDeferredContentBuilders());

        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains(
            "directParentsStack: new global::System.Object[] { templateRoot }",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "fallbackServiceProvider: __services",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "directParentsStack: new global::System.Object[] { deferredOwner, templateRoot }",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitDataTemplate_WritesRuntimeDataTypeWithoutFuncWrapper()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;
            using Demo;

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <DataTemplate x.DataType="Person">
                        <TextBlock />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        using var fixture = CreateFixture(component, PersonSource);
        var plan = fixture.Writer.Plan;

        Assert.Empty(plan.Templates);
        Assert.Single(plan.DeferredContents);

        WriteComponentScope(fixture);

        var output = fixture.CodeWriter.GetText().ToString();
        var dataTypeIndex = output.IndexOf(
            ".DataType = typeof(global::Demo.Person);",
            StringComparison.Ordinal);
        var deferredContentIndex = output.IndexOf(
            "CreateDeferredContent<",
            StringComparison.Ordinal);

        Assert.True(dataTypeIndex >= 0, output);
        Assert.True(deferredContentIndex > dataTypeIndex, output);
        Assert.DoesNotContain("FuncDataTemplate<", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplateElementNameBinding_UsesTheTemplateNameScopeAndCompiles()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Person">
                    <StackPanel>
                        <TextBox x.Name="input" Text="Hello" />
                        <TextBlock Text=${CompiledBinding #input.Text} />
                    </StackPanel>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        using var fixture = CreateFixture(component, PersonSource, currentIndent: 8);
        var plan = fixture.Writer.Plan;
        var template = Assert.Single(plan.Templates);
        var owner = plan.Elements[template.OwnerElementId];

        Assert.True(fixture.Writer.WritePropertyElements(owner.Id, CreateMarkupContext(owner)));

        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains("input.Name = \"input\";", output, StringComparison.Ordinal);
        Assert.Contains(
            "__nameScope.Register(\"input\", input);",
            output,
            StringComparison.Ordinal);
        Assert.Contains("new global::Avalonia.Data.CompiledBinding(", output, StringComparison.Ordinal);
        Assert.Contains("Source = input", output, StringComparison.Ordinal);

        fixture.CodeWriter.CurrentIndent = 4;
        Assert.True(fixture.Writer.WriteStaticMembers());
        var completeOutput = fixture.CodeWriter.GetText().ToString();
        var staticMembers = completeOutput[output.Length..];

        AssertGeneratedTemplateCompiles(
            fixture,
            owner,
            output,
            staticMembers);
    }

    [Fact]
    public void GeneratedNestedTemplates_CompileWithoutWarnings()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Group" x.ItemName="group">
                    <ItemsControl ItemsSource={group.Items}>
                        <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                            <TextBlock Text={$"{group.Name}: {person.Name}"} />
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        using var fixture = CreateFixture(component, NestedItemSource, currentIndent: 8);
        var plan = fixture.Writer.Plan;
        Assert.Equal(2, plan.Templates.Length);

        var outerTemplate = Assert.Single(
            plan.Templates,
            template => plan.Scopes[template.ScopeId].ParentScopeId == 0);
        var innerTemplate = Assert.Single(
            plan.Templates,
            template => plan.Scopes[template.ScopeId].ParentScopeId == outerTemplate.ScopeId);
        var owner = plan.Elements[outerTemplate.OwnerElementId];

        Assert.True(fixture.Writer.WritePropertyElements(owner.Id, CreateMarkupContext(owner)));

        var output = fixture.CodeWriter.GetText().ToString();
        var outerHeader =
            "FuncDataTemplate<global::Demo.Group>((@group, __nameScope) =>";
        var innerHeader =
            "FuncDataTemplate<global::Demo.Person>((person, __nameScope) =>";
        var outerHeaderIndex = output.IndexOf(outerHeader, StringComparison.Ordinal);
        var innerHeaderIndex = output.IndexOf(innerHeader, StringComparison.Ordinal);
        var outerRoot = GetScopeRoot(plan, outerTemplate.ScopeId);
        var innerRoot = GetScopeRoot(plan, innerTemplate.ScopeId);
        var outerCreationIndex = output.IndexOf(
            GetIdentifier(outerRoot) + " = new " + GetTypeName(outerRoot.Type) + "();",
            StringComparison.Ordinal);
        var innerCreationIndex = output.IndexOf(
            GetIdentifier(innerRoot) + " = new " + GetTypeName(innerRoot.Type) + "();",
            StringComparison.Ordinal);

        Assert.True(outerHeaderIndex >= 0, output);
        Assert.True(outerCreationIndex > outerHeaderIndex, output);
        Assert.True(innerHeaderIndex > outerCreationIndex, output);
        Assert.True(innerCreationIndex > innerHeaderIndex, output);
        Assert.Equal(1, CountOccurrences(output, GetIdentifier(outerRoot) + " = new "));
        Assert.Equal(1, CountOccurrences(output, GetIdentifier(innerRoot) + " = new "));
        Assert.Contains("group.Items", output, StringComparison.Ordinal);
        Assert.Contains("group.Name", output, StringComparison.Ordinal);
        Assert.Contains("person.Name", output, StringComparison.Ordinal);

        AssertGeneratedTemplateCompiles(fixture, owner, output);
        AssertBalancedSourceMappings(output);
    }

    private const string PersonSource =
        """
        namespace Demo;

        public sealed class Person
        {
            public string Name { get; set; } = string.Empty;
        }
        """;

    private const string NestedItemSource =
        """
        namespace Demo;

        public sealed class Group
        {
            public string Name { get; set; } = string.Empty;

            public System.Collections.Generic.IReadOnlyList<Person> Items { get; set; } = [];
        }

        public sealed class Person
        {
            public string Name { get; set; } = string.Empty;
        }
        """;

    private static WriterFixture CreateFixture(
        string component,
        string? additionalCSharp = null,
        int currentIndent = 0)
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            additionalCSharp);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticFixture.SemanticModel.GetSymbolInfo(
                semanticFixture.ComponentTree.GetRoot()).Symbol);
        var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = currentIndent,
        };
        var writer = new ComponentWriter(
            codeWriter,
            componentSymbol,
            semanticFixture.SemanticModel,
            "PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>());

        return new WriterFixture(semanticFixture, codeWriter, writer);
    }

    private static MarkupExtensionWriteContext CreateMarkupContext(
        in ComponentElementPlan owner)
    {
        var identifier = GetIdentifier(owner);
        return new MarkupExtensionWriteContext(
            targetObjectExpression: identifier,
            targetProperty: MarkupTargetPropertyPlan.CreateExpression("__property"),
            intermediateRootExpression: "this",
            baseUriExpression: "__akburaBaseUri",
            directParentsStackExpression:
                "new global::System.Object[] { this, " + identifier + " }",
            fallbackServiceProviderExpression: null,
            nameScopeExpression: null,
            scopeId: owner.ScopeId);
    }

    private static void WriteComponentScope(WriterFixture fixture)
    {
        ref readonly var plan = ref fixture.Writer.Plan;
        ref readonly var scope = ref plan.Scopes.ItemRef(0);
        var rootId = plan.ScopeRootElementIds[scope.Roots.Start];
        ref readonly var root = ref plan.Elements.ItemRef(rootId);
        var environment = fixture.SemanticFixture.CreateBindingEnvironment();
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(fixture.SemanticFixture.ComponentTree));
        var writer = new ComponentScopeWriter(
            fixture.CodeWriter,
            in environment,
            sourceMap,
            "global::Demo.PlannerView");
        var context = new ComponentScopeWriteContext(
            intermediateRootExpression: GetIdentifier(root),
            baseUriExpression: "__akburaBaseUri",
            fallbackServiceProviderExpression: null,
            nameScopeExpression: null,
            scopeId: scope.Id,
            parentStackTraversalKind: MarkupParentStackTraversalKind.ExactScope,
            elements: plan.Elements.AsSpan(),
            elementReferences: plan.ElementReferences.AsSpan());

        writer.WriteComponentInitialState(plan, scope, context);
    }

    private static void AssertGeneratedTemplateCompiles(
        WriterFixture fixture,
        in ComponentElementPlan owner,
        string output,
        string? staticMembers = null)
    {
        var generatedSource =
            """
            #nullable enable

            namespace Demo;

            public partial class PlannerView
            {
            """ +
            staticMembers +
            """
                private void ApplyTemplate()
                {
            """ +
            "        var " + GetIdentifier(owner) + " = new " + GetTypeName(owner.Type) + "();\r\n" +
            output +
            """
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "PlannerView.Template.g.cs",
            encoding: Encoding.UTF8);
        var diagnostics = fixture.SemanticFixture.CSharpCompilation
            .AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            generatedSource);
    }

    private static ComponentElementPlan GetScopeRoot(
        in ComponentPlan plan,
        int scopeId)
    {
        ref readonly var scope = ref plan.Scopes.ItemRef(scopeId);
        var rootId = plan.ScopeRootElementIds[scope.Roots.Start];
        return plan.Elements[rootId];
    }

    private static int[] GetScopeElementIds(
        in ComponentPlan plan,
        in ComponentScopePlan scope)
    {
        var result = new int[scope.Elements.Length];
        plan.ScopeElementIds.AsSpan(scope.Elements.Start, scope.Elements.Length).CopyTo(result);
        return result;
    }

    private static int IndexOfLifecycleCall(
        string output,
        in ComponentElementPlan element,
        string method)
    {
        var pattern =
            "((global::System.ComponentModel.ISupportInitialize)" +
            GetIdentifier(element) + ")." + method + "();";
        var index = output.IndexOf(pattern, StringComparison.Ordinal);

        Assert.True(index >= 0, output);
        return index;
    }

    private static string GetIdentifier(in ComponentElementPlan element)
    {
        return CSharpSyntaxFacts.GetKeywordKind(element.Identifier) != CSharpSyntaxKind.None ||
            CSharpSyntaxFacts.GetContextualKeywordKind(element.Identifier) != CSharpSyntaxKind.None
                ? "@" + element.Identifier
                : element.Identifier;
    }

    private static string GetTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static void AssertStrictlyIncreasing(ReadOnlySpan<int> values)
    {
        for (var i = 1; i < values.Length; i++)
        {
            Assert.True(values[i] > values[i - 1]);
        }
    }

    private static void AssertStrictlyDecreasing(ReadOnlySpan<int> values)
    {
        for (var i = 1; i < values.Length; i++)
        {
            Assert.True(values[i] < values[i - 1]);
        }
    }

    private static void AssertBalancedSourceMappings(string output)
    {
        var mappingCount = CountOccurrences(output, "#line (");

        Assert.NotEqual(0, mappingCount);
        Assert.Equal(mappingCount, CountOccurrences(output, "#line default"));
        Assert.Equal(mappingCount, CountOccurrences(output, "#line hidden"));
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

    private sealed record WriterFixture(
        AkcssActivatorPlannerTests.PlannerFixture SemanticFixture,
        CodeWriter CodeWriter,
        ComponentWriter Writer) : IDisposable
    {
        public void Dispose()
        {
            Writer.Dispose();
            CodeWriter.Dispose();
        }
    }
}
