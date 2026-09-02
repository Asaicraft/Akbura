using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;

namespace Akbura.UnitTests;

public sealed class ComponentScopePlannerTests
{
    [Fact]
    public void Create_GroupsExactScopeElementsInPreorder()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <StackPanel>
                    <ItemsControl>
                        <ItemsControl.ItemTemplate>
                            <TextBlock />
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Border />
                </StackPanel>
            </DataTemplate>
            """;
        var plan = CreatePlan(component);
        var dataTemplate = GetElement(plan, "DataTemplate");
        var stackPanel = GetElement(plan, "StackPanel");
        var itemsControl = GetElement(plan, "ItemsControl");
        var textBlock = GetElement(plan, "TextBlock");
        var border = GetElement(plan, "Border");
        var deferred = Assert.Single(plan.DeferredContents);
        var template = Assert.Single(plan.Templates);

        Assert.Equal(
            [dataTemplate.Id],
            GetScopeElementIds(plan, plan.Scopes[0]));
        Assert.Equal(
            [stackPanel.Id, itemsControl.Id, border.Id],
            GetScopeElementIds(plan, plan.Scopes[deferred.ScopeId]));
        Assert.Equal(
            [textBlock.Id],
            GetScopeElementIds(plan, plan.Scopes[template.ScopeId]));
    }

    [Fact]
    public void Create_ExcludesNestedDeferredScopeFromParent()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <ContentControl>
                    <ContentControl.Content>
                        <DataTemplate>
                            <Border />
                        </DataTemplate>
                    </ContentControl.Content>
                </ContentControl>
            </DataTemplate>
            """;
        var plan = CreatePlan(component);
        var dataTemplates = GetElements(plan, "DataTemplate");
        var contentControl = GetElement(plan, "ContentControl");
        var border = GetElement(plan, "Border");
        var deferred = plan.DeferredContents
            .OrderBy(static item => item.ScopeId)
            .ToArray();

        Assert.Equal(2, deferred.Length);
        Assert.Equal(
            [contentControl.Id, dataTemplates[1].Id],
            GetScopeElementIds(plan, plan.Scopes[deferred[0].ScopeId]));
        Assert.Equal(
            [border.Id],
            GetScopeElementIds(plan, plan.Scopes[deferred[1].ScopeId]));
    }

    [Fact]
    public void Create_StoresScopeRootsAndParentRelationships()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <StackPanel>
                    <ItemsControl>
                        <ItemsControl.ItemTemplate>
                            <TextBlock />
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </DataTemplate>
            """;
        var plan = CreatePlan(component);
        var dataTemplate = GetElement(plan, "DataTemplate");
        var stackPanel = GetElement(plan, "StackPanel");
        var itemsControl = GetElement(plan, "ItemsControl");
        var textBlock = GetElement(plan, "TextBlock");
        var deferred = Assert.Single(plan.DeferredContents);
        var template = Assert.Single(plan.Templates);
        var componentScope = plan.Scopes[0];
        var deferredScope = plan.Scopes[deferred.ScopeId];
        var templateScope = plan.Scopes[template.ScopeId];

        Assert.Equal(-1, componentScope.ParentScopeId);
        Assert.Equal(-1, componentScope.OwnerElementId);
        Assert.Equal(ComponentElementScopeKind.Component, componentScope.Kind);
        Assert.False(componentScope.RequiresNameScope);
        Assert.Equal([dataTemplate.Id], GetScopeRootIds(plan, componentScope));

        Assert.Equal(0, deferredScope.ParentScopeId);
        Assert.Equal(dataTemplate.Id, deferredScope.OwnerElementId);
        Assert.Equal(ComponentElementScopeKind.DeferredContent, deferredScope.Kind);
        Assert.True(deferredScope.RequiresNameScope);
        Assert.Equal([stackPanel.Id], GetScopeRootIds(plan, deferredScope));

        Assert.Equal(deferredScope.Id, templateScope.ParentScopeId);
        Assert.Equal(itemsControl.Id, templateScope.OwnerElementId);
        Assert.Equal(ComponentElementScopeKind.DataTemplate, templateScope.Kind);
        Assert.True(templateScope.RequiresNameScope);
        Assert.Equal([textBlock.Id], GetScopeRootIds(plan, templateScope));
    }

    [Fact]
    public void Create_AssignsDenseScopeIdsAndValidFlatRanges()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <Border />

            <DataTemplate>
                <Button />
            </DataTemplate>

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <TextBlock />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var plan = CreatePlan(component);

        Assert.Equal(
            Enumerable.Range(0, plan.Scopes.Length),
            plan.Scopes.Select(static scope => scope.Id));
        Assert.Equal(plan.Elements.Length, plan.ScopeElementIds.Length);

        for (var scopeId = 0; scopeId < plan.Scopes.Length; scopeId++)
        {
            var scope = plan.Scopes[scopeId];
            Assert.InRange(scope.Elements.Start, 0, plan.ScopeElementIds.Length);
            Assert.InRange(
                scope.Elements.Start + scope.Elements.Length,
                0,
                plan.ScopeElementIds.Length);
            Assert.All(
                GetScopeElementIds(plan, scope),
                elementId => Assert.Equal(scopeId, plan.Elements[elementId].ScopeId));
        }

        Assert.Equal(
            Enumerable.Range(0, plan.Elements.Length),
            plan.ScopeElementIds.Order());
    }

    [Fact]
    public void Create_EveryElementAppearsInExactlyOneScopeRange()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <StackPanel>
                    <Border />
                    <ItemsControl>
                        <ItemsControl.ItemTemplate>
                            <TextBlock />
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                    <Button />
                </StackPanel>
            </DataTemplate>
            """;
        var plan = CreatePlan(component);
        var occurrences = new int[plan.Elements.Length];

        foreach (var scope in plan.Scopes)
        {
            foreach (var elementId in GetScopeElementIds(plan, scope))
            {
                occurrences[elementId]++;
            }
        }

        Assert.All(occurrences, static count => Assert.Equal(1, count));
    }

    [Fact]
    public void Create_EmptyDeferredContentCreatesNoContentValue()
    {
        const string component =
            """
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate />
            """;
        var plan = CreatePlan(component);
        var owner = Assert.Single(plan.Elements);

        Assert.False(owner.Content.IsValid);
        Assert.Empty(plan.DeferredContents);
        Assert.Equal([0], plan.Scopes.Select(static scope => scope.Id));
    }

    [Fact]
    public void Create_TextOnlyDeferredContentCreatesNoDeferredPlan()
    {
        const string component =
            """
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>invalid text</DataTemplate>
            """;
        var plan = CreatePlan(component);
        var owner = Assert.Single(plan.Elements);

        Assert.False(owner.Content.IsValid);
        Assert.Empty(plan.DeferredContents);
    }

    [Fact]
    public void Create_DeferredContentWithMultipleRootsCreatesNoDeferredPlan()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <Border />
                <Button />
            </DataTemplate>
            """;
        var plan = CreatePlan(component);
        var owner = GetElement(plan, "DataTemplate");

        Assert.False(owner.Content.IsValid);
        Assert.Empty(plan.DeferredContents);
    }

    [Fact]
    public void Create_TemplateContentWithoutResultTypeUsesControl()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <Button />
            </DataTemplate>
            """;
        var deferred = Assert.Single(CreatePlan(component).DeferredContents);

        Assert.Equal(
            "global::Avalonia.Controls.Control",
            deferred.ResultType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat));
    }

    [Fact]
    public void Create_TemplateContentWithResultTypeUsesDeclaredType()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <DeferredHost>
                <Border />
            </DeferredHost>
            """;
        const string csharp =
            """
            using Avalonia.Controls;
            using Avalonia.Metadata;

            namespace Demo;

            public sealed class DeferredHost
            {
                [Content]
                [TemplateContent(TemplateResultType = typeof(Border))]
                public object Content { get; set; } = null!;
            }
            """;
        var deferred = Assert.Single(CreatePlan(component, csharp).DeferredContents);

        Assert.Equal(
            "global::Avalonia.Controls.Border",
            deferred.ResultType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat));
    }

    [Fact]
    public void Create_NameAssignmentPreservesLogicalKeywordName()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border x.Name="yield" />
            """;
        var plan = CreatePlan(component);
        var name = Assert.Single(plan.NameAssignments);

        Assert.Equal("yield", name.Name);
    }

    private static ComponentPlan CreatePlan(
        string component,
        string? additionalCSharp = null)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            additionalCSharp);
        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);

        return ComponentPlanner.Create(
            symbol,
            fixture.SemanticModel,
            new Dictionary<AkburaSyntax, string>());
    }

    private static ComponentElementPlan GetElement(
        in ComponentPlan plan,
        string tagName)
    {
        return Assert.Single(GetElements(plan, tagName));
    }

    private static ComponentElementPlan[] GetElements(
        in ComponentPlan plan,
        string tagName)
    {
        return plan.Elements
            .Where(element => string.Equals(
                element.Syntax.StartTag?.Name.ToFullString().Trim(),
                tagName,
                StringComparison.Ordinal))
            .ToArray();
    }

    private static int[] GetScopeElementIds(
        in ComponentPlan plan,
        in ComponentScopePlan scope)
    {
        return plan.ScopeElementIds
            .AsSpan(scope.Elements.Start, scope.Elements.Length)
            .ToArray();
    }

    private static int[] GetScopeRootIds(
        in ComponentPlan plan,
        in ComponentScopePlan scope)
    {
        return plan.ScopeRootElementIds
            .AsSpan(scope.Roots.Start, scope.Roots.Length)
            .ToArray();
    }
}
