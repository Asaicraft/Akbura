using Akbura.Language.CodeGeneration;
using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class ComponentPlannerTests
{
    [Fact]
    public void DefaultPlan_IsEmpty()
    {
        Assert.True(default(ComponentPlan).IsEmpty);
    }

    [Fact]
    public void Create_AssignsDensePreorderIdsAndStoresDirectChildIds()
    {
        const string component =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Border>
                    <Grid>
                        <Button />
                    </Grid>
                </Border>
                <TextBlock />
            </StackPanel>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));

        Assert.Equal([0, 1, 2, 3, 4], plan.Elements.Select(static element => element.Id));
        Assert.Equal([0], plan.RootElementIds.ToArray());
        Assert.Equal([1, 4], GetIds(plan.ChildElementIds, plan.Elements[0].Children));
        Assert.Equal([2], GetIds(plan.ChildElementIds, plan.Elements[1].Children));
        Assert.Equal([3], GetIds(plan.ChildElementIds, plan.Elements[2].Children));
        Assert.True(plan.Elements[0].IsRoot);
        Assert.Equal([-1, 0, 1, 2, 0], plan.Elements.Select(static element => element.ParentId));
    }

    [Fact]
    public void Create_SeparatesPropertyElementsFromElements()
    {
        const string component =
            """
            using Avalonia.Controls;

            <ContentControl>
                <ContentControl.Content>
                    <Border />
                </ContentControl.Content>
            </ContentControl>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var owner = plan.Elements[0];
        var child = plan.Elements[1];
        var propertyElement = Assert.Single(plan.PropertyElements);

        Assert.Equal(2, plan.Elements.Length);
        Assert.Empty(GetIds(plan.ChildElementIds, owner.Children));
        Assert.Equal(0, child.ParentId);
        Assert.Equal(0, propertyElement.OwnerElementId);
        Assert.Equal([1], GetIds(plan.ChildElementIds, propertyElement.Children));
        Assert.Equal(1, owner.PropertyElements.Length);
        Assert.DoesNotContain(plan.Elements, element => ReferenceEquals(element.Syntax, propertyElement.Syntax));
    }

    [Fact]
    public void Create_PreservesMultipleRoots()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border />
            <Button />
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));

        Assert.Equal([0, 1], plan.RootElementIds.ToArray());
        Assert.All(plan.Elements, static element =>
        {
            Assert.Equal(-1, element.ParentId);
            Assert.True(element.IsRoot);
        });
    }

    [Fact]
    public void Create_UsesLogicalNamesAndEscapedIdentifiersForElementReferences()
    {
        const string component =
            """
            using Avalonia.Controls;

            <StackPanel>
                <Border x.Name="header" />
                <Button x.Name="yield" />
            </StackPanel>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var header = GetElement(plan, "Border");
        var keyword = GetElement(plan, "Button");

        Assert.Equal("header", header.Identifier);
        Assert.Equal("yield", keyword.Identifier);
        Assert.True(header.HasName);
        Assert.True(keyword.HasName);
        Assert.Collection(
            plan.ElementReferences,
            reference => AssertReference(reference, "header", "header"),
            reference => AssertReference(reference, "yield", "@yield"));
    }

    [Fact]
    public void Create_NamedTemplateElementCreatesLocalReference()
    {
        const string component =
            """
            using Avalonia.Controls;

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <Border x.Name="yield" />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var element = GetElement(plan, "Border");
        var reference = Assert.Single(plan.ElementReferences);

        Assert.True(element.IsLocal);
        Assert.True(element.HasName);
        Assert.Equal(element.ScopeId, reference.ScopeId);
        Assert.Equal("yield", reference.Name);
        Assert.Equal("@yield", reference.Expression);
        Assert.False(reference.IsClassMember);
    }

    [Fact]
    public void Create_ClassifiesControlInitializationAndDataTemplateFlags()
    {
        const string component =
            """
            using Avalonia.Markup.Xaml.Templates;
            using Demo;

            <InitializableControl />
            <DataTemplate />
            """;
        const string csharp =
            """
            using Avalonia.Controls;
            using System.ComponentModel;

            namespace Demo;

            public sealed class InitializableControl : Control, ISupportInitialize
            {
                public void BeginInit() { }
                public void EndInit() { }
            }
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component, csharp));
        var initializable = GetElement(plan, "InitializableControl");
        var template = GetElement(plan, "DataTemplate");

        Assert.True(initializable.Flags.HasFlag(ComponentElementFlags.IsControl));
        Assert.True(initializable.SupportsInitialize);
        Assert.False(initializable.IsTemplateElement);
        Assert.True(template.IsTemplateElement);
        Assert.False(template.SupportsInitialize);
    }

    [Fact]
    public void Create_MarksRealDataTemplateContentAsDeferred()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <ContentControl>
                <ContentControl.ContentTemplate>
                    <DataTemplate>
                        <Border />
                    </DataTemplate>
                </ContentControl.ContentTemplate>
            </ContentControl>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var template = GetElement(plan, "DataTemplate");
        var content = GetElement(plan, "Border");
        var deferred = Assert.Single(plan.DeferredContents);

        Assert.True(template.IsTemplateElement);
        Assert.True(content.IsDeferred);
        Assert.Equal(ComponentElementScopeKind.DeferredContent, content.ScopeKind);
        Assert.True(content.IsLocal);
        Assert.True(content.RequiresLocalMarkupContext);
        Assert.Equal(deferred.ScopeId, content.ScopeId);
        Assert.Equal([content.Id], GetIds(plan.ChildElementIds, deferred.Roots));
        Assert.Empty(plan.Templates);
    }

    [Fact]
    public void Create_DoesNotApplyImplicitDeferredScopeToOrdinaryPropertyElement()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <DeferredHost>
                <Border />
                <DeferredHost.Details>
                    <Button />
                </DeferredHost.Details>
            </DeferredHost>
            """;
        const string csharp =
            """
            using Avalonia.Metadata;

            namespace Demo;

            public sealed class DeferredHost
            {
                [Content]
                [TemplateContent]
                public object Content { get; set; } = null!;

                public object Details { get; set; } = null!;
            }
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component, csharp));
        var deferredChild = GetElement(plan, "Border");
        var ordinaryPropertyChild = GetElement(plan, "Button");
        var deferred = Assert.Single(plan.DeferredContents);

        Assert.True(deferredChild.IsDeferred);
        Assert.Equal([deferredChild.Id], GetIds(plan.ChildElementIds, deferred.Roots));
        Assert.False(ordinaryPropertyChild.IsDeferred);
        Assert.Equal(ComponentElementScopeKind.Component, ordinaryPropertyChild.ScopeKind);
        Assert.Equal(0, ordinaryPropertyChild.ScopeId);
    }

    [Fact]
    public void Create_CreatesDistinctScopesForDirectDataTemplateProperties()
    {
        const string component =
            """
            using Avalonia.Controls;

            <StackPanel>
                <ContentControl>
                    <ContentControl.ContentTemplate>
                        <Border />
                    </ContentControl.ContentTemplate>
                </ContentControl>
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <TextBlock />
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var templates = plan.Templates.OrderBy(static template => template.ScopeId).ToArray();
        var border = GetElement(plan, "Border");
        var textBlock = GetElement(plan, "TextBlock");

        Assert.Equal(2, templates.Length);
        Assert.NotEqual(templates[0].ScopeId, templates[1].ScopeId);
        Assert.Equal(ComponentElementScopeKind.DataTemplate, border.ScopeKind);
        Assert.Equal(ComponentElementScopeKind.DataTemplate, textBlock.ScopeKind);
        Assert.False(border.IsTemplateElement);
        Assert.False(textBlock.IsTemplateElement);
        Assert.NotEqual(border.ScopeId, textBlock.ScopeId);
        Assert.All([border, textBlock], static element =>
            Assert.True(element.IsLocal && element.RequiresLocalMarkupContext));
        Assert.Contains(templates, template =>
            GetIds(plan.ChildElementIds, template.Roots).SequenceEqual([border.Id]));
        Assert.Contains(templates, template =>
            GetIds(plan.ChildElementIds, template.Roots).SequenceEqual([textBlock.Id]));
    }

    [Fact]
    public void Create_UsesNestedDirectTemplateScopeInsideDeferredContent()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <TextBlock />
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </DataTemplate>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var itemsControl = GetElement(plan, "ItemsControl");
        var textBlock = GetElement(plan, "TextBlock");
        var template = Assert.Single(plan.Templates);

        Assert.True(itemsControl.IsDeferred);
        Assert.False(textBlock.IsDeferred);
        Assert.Equal(ComponentElementScopeKind.DataTemplate, textBlock.ScopeKind);
        Assert.Equal(template.ScopeId, textBlock.ScopeId);
        Assert.Equal([textBlock.Id], GetIds(plan.ChildElementIds, template.Roots));
    }

    [Fact]
    public void Create_RealDataTemplateKeepsOuterDeferredScopeAndCreatesItsOwnContentScope()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </DataTemplate>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var dataTemplates = plan.Elements.Where(element => string.Equals(
            element.Syntax.StartTag?.Name.ToFullString().Trim(),
            "DataTemplate",
            StringComparison.Ordinal)).ToArray();
        var border = GetElement(plan, "Border");
        var deferred = plan.DeferredContents.OrderBy(static item => item.ScopeId).ToArray();

        Assert.Equal(2, dataTemplates.Length);
        Assert.Equal(2, deferred.Length);
        var innerTemplate = dataTemplates[1];
        Assert.True(innerTemplate.IsTemplateElement);
        Assert.True(innerTemplate.IsDeferred);
        Assert.Equal(deferred[0].ScopeId, innerTemplate.ScopeId);
        Assert.Equal(deferred[1].ScopeId, border.ScopeId);
        Assert.Empty(plan.Templates);
    }

    [Fact]
    public void Create_RealDataTemplateKeepsOuterSyntheticTemplateScope()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <ContentControl>
                        <ContentControl.ContentTemplate>
                            <DataTemplate>
                                <Border />
                            </DataTemplate>
                        </ContentControl.ContentTemplate>
                    </ContentControl>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var outerTemplate = Assert.Single(plan.Templates);
        var dataTemplate = GetElement(plan, "DataTemplate");
        var border = GetElement(plan, "Border");

        Assert.Equal(ComponentElementScopeKind.DataTemplate, dataTemplate.ScopeKind);
        Assert.Equal(outerTemplate.ScopeId, dataTemplate.ScopeId);
        Assert.False(dataTemplate.IsDeferred);
        Assert.Equal(ComponentElementScopeKind.DeferredContent, border.ScopeKind);
        Assert.NotEqual(outerTemplate.ScopeId, border.ScopeId);
    }

    [Fact]
    public void Create_UsesResolvedSourceComponentTypeForAkcssControlTarget()
    {
        const string component =
            """
            using Avalonia.Controls;

            @akcss {
                @using Avalonia.Controls;

                @utilities {
                    .probe-(double value) { Width: value; }
                }
            }

            <SourceOnlyControl probe-4 />
            """;
        const string sourceOnlyComponent =
            """
            using Avalonia.Controls;

            <Border />
            """;
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var sourceOnlyTree = AkburaSyntaxTree.ParseText(
            sourceOnlyComponent,
            "SourceOnlyControl.akbura");
        var compilation = new AkburaCompilation(
            baseFixture.CSharpCompilation,
            [baseFixture.ComponentTree, sourceOnlyTree],
            rootNamespace: "Demo");
        var fixture = new AkcssActivatorPlannerTests.PlannerFixture(
            baseFixture.CSharpCompilation,
            baseFixture.ComponentTree,
            externalAkcssTree: null,
            compilation.GetSemanticModel(baseFixture.ComponentTree));
        var plan = CreatePlan(fixture);
        var element = Assert.Single(plan.Elements);

        Assert.True(element.Flags.HasFlag(ComponentElementFlags.IsControl));
        Assert.True(Assert.Single(plan.Akcss.ValueSources).IsControlTarget);
    }

    [Fact]
    public void Create_StoresPropertyWriteRangeAndValueKinds()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border Width="42" Height={21} />
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var element = Assert.Single(plan.Elements);
        var writes = GetWrites(plan.PropertyWrites, element.PropertyWrites);

        Assert.Equal(2, writes.Length);
        Assert.Equal(ComponentPropertyValueKind.Constant, writes[0].ValueKind);
        Assert.Equal(ComponentPropertyValueKind.CSharpExpression, writes[1].ValueKind);
        Assert.True(writes[0].IsFirstUpdate);
        Assert.False(writes[1].IsFirstUpdate);
        Assert.All(writes, static write =>
        {
            Assert.True(write.Destination.IsValid);
            Assert.Equal(PropertyWriteKind.AvaloniaProperty, write.Destination.Kind);
        });
    }

    [Fact]
    public void Create_KeepsParentAndChildPropertyWriteRangesSeparate()
    {
        const string component =
            """
            using Avalonia.Controls;

            <StackPanel Spacing="8">
                <Border Width="42" />
            </StackPanel>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var parent = GetElement(plan, "StackPanel");
        var child = GetElement(plan, "Border");

        Assert.Equal(0, parent.PropertyWrites.Start);
        Assert.Equal(1, parent.PropertyWrites.Length);
        Assert.Equal(1, child.PropertyWrites.Start);
        Assert.Equal(1, child.PropertyWrites.Length);
        Assert.Equal(2, plan.PropertyWrites.Length);
    }

    [Fact]
    public void Create_MergesAkcssPlansByDenseElementId()
    {
        const string component =
            """
            using Avalonia.Controls;

            @akcss {
                @using Avalonia.Controls;

                .card { Width: 10; }
            }

            <StackPanel>
                <Border class="card" />
                <Button />
            </StackPanel>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var styled = GetElement(plan, "Border");

        Assert.Equal(plan.Elements.Length, plan.Akcss.Elements.Length);
        for (var i = 0; i < plan.Elements.Length; i++)
        {
            Assert.Equal(i, plan.Elements[i].Akcss.ElementId);
            Assert.Equal(plan.Akcss.Elements[i].ElementId, plan.Elements[i].Akcss.ElementId);
            Assert.Equal(plan.Akcss.Elements[i].Activators.Start, plan.Elements[i].Akcss.Activators.Start);
            Assert.Equal(plan.Akcss.Elements[i].Activators.Length, plan.Elements[i].Akcss.Activators.Length);
        }

        Assert.False(styled.Akcss.Activators.IsEmpty);
    }

    private static ComponentPlan CreatePlan(AkcssActivatorPlannerTests.PlannerFixture fixture)
    {
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var moduleTypeNames = new Dictionary<AkburaSyntax, string>();
        var moduleIndex = 0;

        foreach (var inlineAkcss in fixture.ComponentTree.GetRoot().Members.OfType<InlineAkcssBlockSyntax>())
        {
            moduleTypeNames.Add(inlineAkcss, "global::Demo.ComponentPlannerStyles" + moduleIndex++);
        }

        if (fixture.ExternalAkcssTree != null)
        {
            moduleTypeNames.Add(fixture.ExternalAkcssTree.GetRoot(), "global::Demo.ExternalComponentPlannerStyles");
        }

        var plan = ComponentPlanner.Create(component, fixture.SemanticModel, moduleTypeNames);
        Assert.All(plan.Elements, static element =>
        {
            Assert.Equal(element.ParentId < 0, element.IsRoot);
            Assert.Equal(element.ScopeId > 0, element.IsLocal);
            Assert.Equal(element.ScopeKind != ComponentElementScopeKind.Component, element.IsLocal);
            Assert.Equal(element.IsLocal, element.RequiresLocalMarkupContext);
        });
        return plan;
    }

    private static ComponentElementPlan GetElement(ComponentPlan plan, string tagName)
    {
        return Assert.Single(plan.Elements, element => string.Equals(
            element.Syntax.StartTag?.Name.ToFullString().Trim(),
            tagName,
            StringComparison.Ordinal));
    }

    private static int[] GetIds(ImmutableArray<int> ids, ComponentPlanRange range)
    {
        return ids.AsSpan(range.Start, range.Length).ToArray();
    }

    private static ComponentPropertyWritePlan[] GetWrites(
        ImmutableArray<ComponentPropertyWritePlan> writes,
        ComponentPlanRange range)
    {
        return writes.AsSpan(range.Start, range.Length).ToArray();
    }

    private static void AssertReference(
        BindingElementReference reference,
        string name,
        string expression)
    {
        Assert.Equal(name, reference.Name);
        Assert.Equal(expression, reference.Expression);
        Assert.Equal(0, reference.ScopeId);
        Assert.True(reference.IsClassMember);
    }
}
