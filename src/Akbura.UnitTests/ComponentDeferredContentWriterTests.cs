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

public sealed class ComponentDeferredContentWriterTests
{
    [Fact]
    public void ComponentScopeWriteContext_ForElementUsesEscapedTargetAndTypedHierarchy()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <Border x.Name="yield" />
            </DataTemplate>
            """;
        using var fixture = CreateFixture(component);
        var plan = fixture.Writer.Plan;
        var deferred = Assert.Single(plan.DeferredContents);
        ref readonly var scope = ref plan.Scopes.ItemRef(deferred.ScopeId);
        var elementId = Assert.Single(GetScopeElementIds(plan, scope));
        var context = new ComponentScopeWriteContext(
            intermediateRootExpression: "@yield",
            baseUriExpression: "__akburaBaseUri",
            fallbackServiceProviderExpression: "__services",
            nameScopeExpression: "__nameScope",
            scopeId: scope.Id,
            parentStackTraversalKind: MarkupParentStackTraversalKind.ExactScope,
            elements: plan.Elements.AsSpan(),
            elementReferences: plan.ElementReferences.AsSpan());

        var elementContext = context.ForElement(elementId);

        Assert.Equal("@yield", elementContext.TargetObjectExpression);
        Assert.Equal("@yield", elementContext.IntermediateRootExpression);
        Assert.Equal("__akburaBaseUri", elementContext.BaseUriExpression);
        Assert.Equal("__services", elementContext.FallbackServiceProviderExpression);
        Assert.Equal("__nameScope", elementContext.NameScopeExpression);
        Assert.Equal(scope.Id, elementContext.ScopeId);
        Assert.Equal(MarkupParentStackKind.ComponentHierarchy, elementContext.DirectParentsStack.Kind);
        Assert.Equal(
            MarkupParentStackTraversalKind.ExactScope,
            elementContext.DirectParentsStack.TraversalKind);
        Assert.Equal(elementId, elementContext.DirectParentsStack.ElementId);
        Assert.Equal(scope.Id, elementContext.DirectParentsStack.ScopeId);
    }

    [Fact]
    public void ComponentNameAssignment_DoesNotRegisterInANameScope()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border x.Name="yield" />
            """;
        using var fixture = CreateFixture(component);
        var plan = fixture.Writer.Plan;
        var element = Assert.Single(plan.Elements);
        var context = CreateMarkupContext(
            element,
            nameScopeExpression: "__nameScope",
            fallbackServiceProviderExpression: null);

        Assert.True(fixture.Writer.WriteFirstUpdateActions(element.Id, context));

        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains("@yield.Name = \"yield\";", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__nameScope.Register", output, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteDeferredContentBuilders_WritesExactScopeInLifecycleOrder()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <Border x.Name="yield">
                    <TextBlock x.Name="title" Text="Hello" />
                </Border>
            </DataTemplate>
            """;
        using var fixture = CreateFixture(component, currentIndent: 8);
        var plan = fixture.Writer.Plan;
        var deferred = Assert.Single(plan.DeferredContents);
        ref readonly var scope = ref plan.Scopes.ItemRef(deferred.ScopeId);
        var elementIds = GetScopeElementIds(plan, scope);

        Assert.True(scope.RequiresNameScope);
        Assert.True(fixture.Writer.WriteDeferredContentBuilders());
        Assert.Equal(8, fixture.CodeWriter.CurrentIndent);

        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains(
            "private object __BuildDeferredContent" + deferred.Id +
            "(global::System.IServiceProvider __services)",
            output,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(output, "var __nameScope ="));
        Assert.Equal(
            1,
            CountOccurrences(
                output,
                "__services.GetService(typeof(global::Avalonia.Controls.INameScope))"));

        var lastCreation = -1;
        foreach (var elementId in elementIds)
        {
            var element = plan.Elements[elementId];
            var creation = GetIdentifier(element) + " = new " + GetTypeName(element.Type) + "();";
            var creationIndex = output.IndexOf(creation, StringComparison.Ordinal);

            Assert.True(creationIndex > lastCreation, output);
            lastCreation = creationIndex;
        }

        var initializeElements = elementIds
            .Select(elementId => plan.Elements[elementId])
            .Where(static element => element.SupportsInitialize)
            .ToArray();
        Assert.NotEmpty(initializeElements);

        var beginIndices = initializeElements
            .Select(element => IndexOfLifecycleCall(output, element, "BeginInit"))
            .ToArray();
        var endIndices = initializeElements
            .Select(element => IndexOfLifecycleCall(output, element, "EndInit"))
            .ToArray();

        AssertStrictlyIncreasing(beginIndices);
        AssertStrictlyDecreasing(endIndices);
        Assert.True(lastCreation < beginIndices[0], output);

        var nameAssignment = output.IndexOf("@yield.Name = \"yield\";", StringComparison.Ordinal);
        var registration = output.IndexOf(
            "__nameScope.Register(\"yield\", @yield);",
            StringComparison.Ordinal);
        Assert.True(nameAssignment > beginIndices[^1], output);
        Assert.True(registration > nameAssignment, output);
        Assert.True(endIndices[^1] > registration, output);

        var rootId = plan.ScopeRootElementIds[scope.Roots.Start];
        var returnIndex = output.IndexOf(
            "return " + GetIdentifier(plan.Elements[rootId]) + ";",
            StringComparison.Ordinal);
        Assert.True(returnIndex > endIndices[0], output);
        AssertBalancedSourceMappings(output);
    }

    [Fact]
    public void WriteFirstUpdateContent_WritesTypedStaticFactoryAndFallbackProvider()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <Border />
            </DataTemplate>
            """;
        using var fixture = CreateFixture(component, currentIndent: 12);
        var plan = fixture.Writer.Plan;
        var deferred = Assert.Single(plan.DeferredContents);
        var owner = plan.Elements[deferred.TargetElementId];
        var context = CreateMarkupContext(
            owner,
            nameScopeExpression: null,
            fallbackServiceProviderExpression: "__fallback");

        Assert.True(fixture.Writer.WriteFirstUpdateContent(owner.Id, context));
        Assert.Equal(12, fixture.CodeWriter.CurrentIndent);

        var output = fixture.CodeWriter.GetText().ToString();
        var resultType = GetTypeName(deferred.ResultType);
        Assert.Contains("CreateDeferredContent<" + resultType + ">(", output, StringComparison.Ordinal);
        Assert.Contains(
            "static __services => ((global::Demo.PlannerView)" +
            "((global::Avalonia.Markup.Xaml.IRootObjectProvider)" +
            "__services.GetService(typeof(global::Avalonia.Markup.Xaml.IRootObjectProvider))!)." +
            "RootObject).__BuildDeferredContent" + deferred.Id + "(__services),",
            output,
            StringComparison.Ordinal);
        Assert.Contains("CreateMarkupServiceProvider(targetObject: ", output, StringComparison.Ordinal);
        Assert.Contains("fallbackServiceProvider: __fallback", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__services => this.", output, StringComparison.Ordinal);
        AssertBalancedSourceMappings(output);
    }

    [Fact]
    public void WriteBuilder_ExcludesElementsOwnedByNestedScope()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <ContentControl x.Name="outer">
                    <DataTemplate>
                        <Border x.Name="inner" />
                    </DataTemplate>
                </ContentControl>
            </DataTemplate>
            """;
        using var fixture = CreateFixture(component);
        var plan = fixture.Writer.Plan;
        var outer = Assert.Single(
            plan.DeferredContents,
            item => plan.Scopes[item.ScopeId].ParentScopeId == 0);
        var nested = Assert.Single(
            plan.DeferredContents,
            item => plan.Scopes[item.ScopeId].ParentScopeId == outer.ScopeId);
        ref readonly var outerScope = ref plan.Scopes.ItemRef(outer.ScopeId);
        ref readonly var nestedScope = ref plan.Scopes.ItemRef(nested.ScopeId);
        var environment = fixture.SemanticFixture.CreateBindingEnvironment();
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(fixture.SemanticFixture.ComponentTree));
        var writer = new DeferredContentWriter(
            fixture.CodeWriter,
            in environment,
            sourceMap,
            "global::Demo.PlannerView");

        writer.WriteBuilder(plan, outer);

        var output = fixture.CodeWriter.GetText().ToString();
        foreach (var elementId in GetScopeElementIds(plan, outerScope))
        {
            Assert.Contains(
                GetIdentifier(plan.Elements[elementId]) + " = new ",
                output,
                StringComparison.Ordinal);
        }

        foreach (var elementId in GetScopeElementIds(plan, nestedScope))
        {
            Assert.DoesNotContain(
                GetIdentifier(plan.Elements[elementId]) + " = new ",
                output,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "RootObject).__BuildDeferredContent" + nested.Id + "(__services),",
            output,
            StringComparison.Ordinal);
        Assert.Contains("fallbackServiceProvider: __services", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private object __BuildDeferredContent" + nested.Id,
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WriteValue_DirectMemberWritesCompleteFactoryWithNullTargetProperty()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <Border />
            </DataTemplate>
            """;
        using var fixture = CreateFixture(component);
        var plan = fixture.Writer.Plan;
        var deferred = Assert.Single(plan.DeferredContents);
        var originalContent = Assert.Single(
            plan.PropertyContents,
            content => content.FirstUpdateValue.Kind ==
                ComponentContentValueKind.DeferredContent);
        var destination = CreateDirectMemberPropertyWritePlan(
            fixture.SemanticFixture,
            "DeferredSlot");
        var content = new ComponentPropertyContentPlan(
            originalContent.Id,
            originalContent.OwnerElementId,
            destination,
            originalContent.FirstUpdateValue,
            originalContent.UpdateValue,
            originalContent.Syntax);
        var owner = plan.Elements[content.OwnerElementId];
        var context = CreateMarkupContext(
            owner,
            nameScopeExpression: null,
            fallbackServiceProviderExpression: "__fallback");
        var environment = fixture.SemanticFixture.CreateBindingEnvironment();
        var sourceMap = new ComponentGenerationSourceMap(
            Assert.IsType<ComponentSyntaxTree>(fixture.SemanticFixture.ComponentTree));
        var writer = new DeferredContentWriter(
            fixture.CodeWriter,
            in environment,
            sourceMap,
            "global::Demo.PlannerView");

        Assert.Equal(PropertyWriteKind.DirectMember, destination.Kind);
        Assert.False(destination.TargetProperty.IsValid);
        Assert.True(writer.WriteValue(plan, content, deferred, context));

        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains(
            GetIdentifier(owner) + ".DeferredSlot = CreateDeferredContent<",
            output,
            StringComparison.Ordinal);
        Assert.Contains("targetProperty: null!", output, StringComparison.Ordinal);
        Assert.Contains("fallbackServiceProvider: __fallback));", output, StringComparison.Ordinal);
        AssertBalancedSourceMappings(output);
    }

    [Fact]
    public void GeneratedDeferredContentMembers_CompileWithoutWarnings()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <StackPanel>
                    <TextBlock Text=${CompiledBinding #source.Text} />
                    <TextBox x.Name="source" Text="Hello" />
                    <ContentControl>
                        <DataTemplate>
                            <Border Background=${DynamicResource AccentBrush} />
                        </DataTemplate>
                    </ContentControl>
                </StackPanel>
            </DataTemplate>
            """;
        using var fixture = CreateFixture(component);
        var plan = fixture.Writer.Plan;
        Assert.Equal(2, plan.DeferredContents.Length);
        var deferred = Assert.Single(
            plan.DeferredContents,
            item => plan.Scopes[item.ScopeId].ParentScopeId == 0);
        var owner = plan.Elements[deferred.TargetElementId];
        var context = CreateMarkupContext(
            owner,
            nameScopeExpression: null,
            fallbackServiceProviderExpression: "__fallback");

        fixture.CodeWriter.WriteLine("#nullable enable");
        fixture.CodeWriter.WriteLine();
        fixture.CodeWriter.WriteLine("namespace Demo;");
        fixture.CodeWriter.WriteLine();
        fixture.CodeWriter.WriteLine(
            "public abstract partial class PlannerView : global::Akbura.AkburaControl");
        fixture.CodeWriter.WriteLine("{");
        fixture.CodeWriter.CurrentIndent = 4;

        // DeferredContentWriter consumes this member by name. The surrounding
        // component generator must provide a System.Uri; using object here would
        // hide an invalid integration with AkburaControl.CreateMarkupServiceProvider.
        fixture.CodeWriter.WriteLine(
            "private static readonly global::System.Uri __akburaBaseUri = " +
            "new(\"avares://Demo/PlannerView.akbura\");");
        Assert.True(fixture.Writer.WriteStaticMembers());
        fixture.Writer.WriteElementFields();
        fixture.CodeWriter.WriteLine();
        fixture.CodeWriter.WriteLine(
            "private void Attach(global::System.IServiceProvider __fallback)");
        fixture.CodeWriter.WriteLine("{");
        fixture.CodeWriter.CurrentIndent = 8;
        Assert.True(fixture.Writer.WriteFirstUpdateContent(owner.Id, context));
        fixture.CodeWriter.CurrentIndent = 4;
        fixture.CodeWriter.WriteLine("}");
        fixture.CodeWriter.WriteLine();
        Assert.True(fixture.Writer.WriteDeferredContentBuilders());
        fixture.CodeWriter.CurrentIndent = 0;
        fixture.CodeWriter.WriteLine("}");

        var generatedSource = fixture.CodeWriter.GetText().ToString();
        Assert.Contains(
            "private static readonly global::System.Uri __akburaBaseUri",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Contains("baseUri: __akburaBaseUri", generatedSource, StringComparison.Ordinal);
        Assert.Contains("Source = source", generatedSource, StringComparison.Ordinal);
        Assert.Contains("new global::Avalonia.Data.CompiledBinding(", generatedSource, StringComparison.Ordinal);
        Assert.Contains(
            "new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(",
            generatedSource,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            CountOccurrences(generatedSource, "private object __BuildDeferredContent"));
        Assert.Contains("fallbackServiceProvider: __services", generatedSource, StringComparison.Ordinal);

        var syntaxTree = CSharpSyntaxTree.ParseText(
            generatedSource,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            path: "PlannerView.DeferredContent.g.cs",
            encoding: Encoding.UTF8);
        var diagnostics = fixture.SemanticFixture.CSharpCompilation
            .AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity is
                DiagnosticSeverity.Warning or
                DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            generatedSource);
    }

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

    private static PropertyWritePlan CreateDirectMemberPropertyWritePlan(
        AkcssActivatorPlannerTests.PlannerFixture fixture,
        string memberName)
    {
        var syntaxTree = ComponentSyntaxTree.ParseText(
            "param object " + memberName + ";");
        var syntax = Assert.Single(
            syntaxTree.GetRoot().Members.OfType<ParamDeclarationSyntax>());
        var objectType = fixture.CSharpCompilation.GetSpecialType(
            SpecialType.System_Object);
        var parameter = new ParamSymbol(
            syntax,
            new CSharpSymbolDefinition(objectType),
            defaultValueType: default,
            hasExplicitType: true,
            bindingKind: ParamBindingKind.Default);
        var property = new PropertySymbol(
            memberName,
            new CSharpSymbolDefinition(objectType),
            parameter: parameter);

        return PropertyWritePlan.Create(property);
    }

    private static MarkupExtensionWriteContext CreateMarkupContext(
        in ComponentElementPlan target,
        string? nameScopeExpression,
        string? fallbackServiceProviderExpression)
    {
        return new MarkupExtensionWriteContext(
            targetObjectExpression: GetIdentifier(target),
            targetProperty: MarkupTargetPropertyPlan.CreateExpression("__property"),
            intermediateRootExpression: "this",
            baseUriExpression: "__akburaBaseUri",
            directParentsStackExpression:
                "new global::System.Object[] { this, " + GetIdentifier(target) + " }",
            fallbackServiceProviderExpression,
            nameScopeExpression,
            scopeId: target.ScopeId);
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

    private sealed class WriterFixture : IDisposable
    {
        public WriterFixture(
            AkcssActivatorPlannerTests.PlannerFixture semanticFixture,
            CodeWriter codeWriter,
            ComponentWriter writer)
        {
            SemanticFixture = semanticFixture;
            CodeWriter = codeWriter;
            Writer = writer;
        }

        public AkcssActivatorPlannerTests.PlannerFixture SemanticFixture { get; }

        public CodeWriter CodeWriter { get; }

        public ComponentWriter Writer { get; }

        public void Dispose()
        {
            Writer.Dispose();
            CodeWriter.Dispose();
        }
    }
}
