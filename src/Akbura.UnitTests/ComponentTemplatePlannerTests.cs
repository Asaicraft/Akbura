using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;

namespace Akbura.UnitTests;

public sealed class ComponentTemplatePlannerTests
{
    [Fact]
    public void Create_StoresExplicitDataTypeAndDeclaredItemName()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                    <TextBlock />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        const string csharp =
            """
            namespace Demo;

            public sealed class Person
            {
            }
            """;
        var plan = CreatePlan(component, csharp);
        var template = Assert.Single(plan.Templates);
        var owner = GetElement(plan, "ItemsControl");
        ref readonly var scope = ref plan.Scopes.ItemRef(template.ScopeId);
        var rootId = Assert.Single(GetScopeRootIds(plan, scope));

        Assert.Equal("global::Demo.Person", GetTypeName(template.DataType));
        Assert.Equal("person", template.ItemName);
        Assert.Equal(owner.Id, template.OwnerElementId);
        Assert.Equal(ComponentElementScopeKind.DataTemplate, scope.Kind);
        Assert.Equal(template.ScopeId, plan.Elements[rootId].ScopeId);
        Assert.True(plan.Elements[rootId].IsControl);
    }

    [Fact]
    public void Create_UsesDefaultItemName()
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
        const string csharp =
            """
            namespace Demo;

            public sealed class Person
            {
            }
            """;
        var template = Assert.Single(CreatePlan(component, csharp).Templates);

        Assert.Equal("global::Demo.Person", GetTypeName(template.DataType));
        Assert.Equal("__item", template.ItemName);
    }

    [Fact]
    public void Create_InfersDataTypeFromItemsSource()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            inject PeopleViewModel ViewModel;

            <ItemsControl ItemsSource={ViewModel.People}>
                <ItemsControl.ItemTemplate x.ItemName="person">
                    <TextBlock Text={person.Name} />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        const string csharp =
            """
            using System.Collections.Generic;

            namespace Demo;

            public sealed class Person
            {
                public string Name { get; set; } = string.Empty;
            }

            public sealed class PeopleViewModel
            {
                public IReadOnlyList<Person> People { get; set; } = null!;
            }
            """;
        var template = Assert.Single(CreatePlan(component, csharp).Templates);

        Assert.Equal("global::Demo.Person", GetTypeName(template.DataType));
        Assert.Equal("person", template.ItemName);
    }

    [Fact]
    public void Create_UsesObjectWhenDataTypeCannotBeInferred()
    {
        const string component =
            """
            using Avalonia.Controls;

            <ContentControl>
                <ContentControl.ContentTemplate>
                    <Border />
                </ContentControl.ContentTemplate>
            </ContentControl>
            """;
        var template = Assert.Single(CreatePlan(component).Templates);

        Assert.Equal("object", template.DataType.ToDisplayString());
        Assert.Equal("__item", template.ItemName);
    }

    [Fact]
    public void Create_ExplicitDataTemplateIsNotWrappedAndWritesRuntimeDataTypeFirst()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;
            using Demo;

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <DataTemplate x.DataType="Person" x.Name="template">
                        <TextBlock />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        const string csharp =
            """
            namespace Demo;

            public sealed class Person
            {
            }
            """;
        var plan = CreatePlan(component, csharp);
        var dataTemplate = GetElement(plan, "DataTemplate");
        var actions = GetActions(plan, dataTemplate.FirstUpdateActions);
        var dataTypeAction = actions[0];
        var dataTypeWrite = plan.PropertyWrites[dataTypeAction.Index];
        var dataTypeValue = plan.CSharpValues[dataTypeWrite.PayloadIndex];

        Assert.Empty(plan.Templates);
        Assert.Single(plan.DeferredContents);
        Assert.Equal(ComponentFirstUpdateActionKind.PropertyWrite, dataTypeAction.Kind);
        Assert.Equal("DataType", dataTypeWrite.Destination.ClrProperty?.Name);
        Assert.Equal(ComponentPropertyValueKind.Constant, dataTypeWrite.ValueKind);
        Assert.Equal(
            ComponentPropertyWritePhase.FirstUpdate,
            dataTypeWrite.Phase);
        Assert.Equal(
            "global::Demo.Person",
            GetTypeName(Assert.IsAssignableFrom<ITypeSymbol>(dataTypeValue.ConvertedValue)));
        Assert.Equal(ComponentFirstUpdateActionKind.NameAssignment, actions[1].Kind);
    }

    [Fact]
    public void Create_ExplicitDataTemplateInheritsRuntimeDataTypeFromItemsSource()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;
            using Demo;

            inject PeopleViewModel ViewModel;

            <ItemsControl ItemsSource={ViewModel.People}>
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <TextBlock />
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        const string csharp =
            """
            using System.Collections.Generic;

            namespace Demo;

            public sealed class Person
            {
            }

            public sealed class PeopleViewModel
            {
                public IReadOnlyList<Person> People { get; set; } = null!;
            }
            """;
        var plan = CreatePlan(component, csharp);
        var dataTemplate = GetElement(plan, "DataTemplate");
        var action = Assert.Single(
            GetActions(plan, dataTemplate.FirstUpdateActions));
        var write = plan.PropertyWrites[action.Index];
        var value = plan.CSharpValues[write.PayloadIndex];

        Assert.Empty(plan.Templates);
        Assert.Equal(ComponentFirstUpdateActionKind.PropertyWrite, action.Kind);
        Assert.Equal("DataType", write.Destination.ClrProperty?.Name);
        Assert.Equal(
            "global::Demo.Person",
            GetTypeName(Assert.IsAssignableFrom<ITypeSymbol>(value.ConvertedValue)));
    }

    [Fact]
    public void Create_MultipleDirectTemplateRootsCreateNoTemplatePlan()
    {
        const string component =
            """
            using Avalonia.Controls;

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <Border />
                    <Button />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var plan = CreatePlan(component);

        Assert.Empty(plan.Templates);
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
        return Assert.Single(plan.Elements, element => string.Equals(
            element.Syntax.StartTag?.Name.ToFullString().Trim(),
            tagName,
            StringComparison.Ordinal));
    }

    private static ComponentFirstUpdateActionPlan[] GetActions(
        in ComponentPlan plan,
        ComponentPlanRange range)
    {
        return plan.FirstUpdateActions
            .AsSpan(range.Start, range.Length)
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

    private static string GetTypeName(ITypeSymbol type)
    {
        return type.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat);
    }
}
