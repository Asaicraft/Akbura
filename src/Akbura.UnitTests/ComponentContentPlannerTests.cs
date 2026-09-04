using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class ComponentContentPlannerTests
{
    [Fact]
    public void Create_LowersImplicitSingleElementToPropertyContent()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border>
                <Button />
            </Border>
            """;
        var plan = CreatePlan(component);
        var owner = GetElement(plan, "Border");
        var child = GetElement(plan, "Button");

        Assert.Equal(ComponentContentTargetKind.Property, owner.Content.Kind);
        var content = plan.PropertyContents[owner.Content.Index];
        Assert.Equal(owner.Id, content.OwnerElementId);
        Assert.True(content.Destination.IsValid);
        Assert.Equal(ComponentContentValueKind.Element, content.FirstUpdateValue.Kind);
        Assert.Equal(child.Id, content.FirstUpdateValue.Index);
        Assert.False(content.UpdateValue.IsValid);
        Assert.Empty(plan.CollectionContents);
        Assert.Empty(plan.ContentItems);
    }

    [Fact]
    public void Create_LowersImplicitCollectionInSemanticOrder()
    {
        const string component =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBlock />
                <Button />
                <Border />
            </StackPanel>
            """;
        var plan = CreatePlan(component);
        var owner = GetElement(plan, "StackPanel");
        var children = new[]
        {
            GetElement(plan, "TextBlock").Id,
            GetElement(plan, "Button").Id,
            GetElement(plan, "Border").Id,
        };

        Assert.Equal(ComponentContentTargetKind.Collection, owner.Content.Kind);
        var content = plan.CollectionContents[owner.Content.Index];
        var items = GetItems(plan, content.Items);
        Assert.Equal(owner.Id, content.OwnerElementId);
        Assert.Equal(CollectionWriteKind.Property, content.Destination.Kind);
        Assert.True(content.Destination.Property.IsValid);
        Assert.Equal(
            children,
            items.Select(static item => item.Value.Index).ToArray());
        Assert.All(items, static item =>
            Assert.Equal(ComponentContentValueKind.Element, item.Value.Kind));
    }

    [Fact]
    public void Create_LowersExplicitPropertyElementToItsOwner()
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
        var plan = CreatePlan(component);
        var owner = GetElement(plan, "ContentControl");
        var child = GetElement(plan, "Border");
        ComponentPropertyElementPlan propertyElement = Assert.Single(plan.PropertyElements);

        Assert.False(owner.Content.IsValid);
        AssertPropertyElementContract(propertyElement, owner.Id);
        Assert.Equal(ComponentContentTargetKind.Property, propertyElement.Content.Kind);
        var content = plan.PropertyContents[propertyElement.Content.Index];
        Assert.Equal(owner.Id, content.OwnerElementId);
        Assert.Equal(ComponentContentValueKind.Element, content.FirstUpdateValue.Kind);
        Assert.Equal(child.Id, content.FirstUpdateValue.Index);
    }

    [Fact]
    public void Create_LowersExplicitCollectionPropertyInSourceOrder()
    {
        const string component =
            """
            using Avalonia.Controls;

            <StackPanel>
                <StackPanel.Children>
                    <TextBlock />
                    <Button />
                </StackPanel.Children>
            </StackPanel>
            """;
        var plan = CreatePlan(component);
        var owner = GetElement(plan, "StackPanel");
        var textBlock = GetElement(plan, "TextBlock");
        var button = GetElement(plan, "Button");
        var propertyElement = Assert.Single(plan.PropertyElements);

        Assert.False(owner.Content.IsValid);
        AssertPropertyElementContract(propertyElement, owner.Id);
        Assert.Equal(ComponentContentTargetKind.Collection, propertyElement.Content.Kind);
        var content = plan.CollectionContents[propertyElement.Content.Index];
        var items = GetItems(plan, content.Items);
        Assert.Equal(CollectionWriteKind.Property, content.Destination.Kind);
        Assert.Equal(
            [textBlock.Id, button.Id],
            items.Select(static item => item.Value.Index).ToArray());
        Assert.All(items, static item =>
            Assert.Equal(ComponentContentValueKind.Element, item.Value.Kind));
    }

    [Fact]
    public void Create_LowersLiteralContentToFirstUpdateConstant()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Button>Hello world</Button>
            """;
        var plan = CreatePlan(component);
        var owner = Assert.Single(plan.Elements);
        var content = Assert.Single(plan.PropertyContents);

        Assert.Equal(ComponentContentTargetKind.Property, owner.Content.Kind);
        Assert.Equal(ComponentContentValueKind.Constant, content.FirstUpdateValue.Kind);
        Assert.False(content.UpdateValue.IsValid);
        var value = plan.CSharpValues[content.FirstUpdateValue.Index];
        Assert.Equal("Hello world", value.LiteralValue);
    }

    [Fact]
    public void Create_LowersExpressionContentToUpdateValue()
    {
        const string component =
            """
            using Avalonia.Controls;

            state string label = "Ready";

            <TextBlock>{label}</TextBlock>
            """;
        var plan = CreatePlan(component);
        var content = Assert.Single(plan.PropertyContents);

        Assert.False(content.FirstUpdateValue.IsValid);
        Assert.Equal(ComponentContentValueKind.CSharpExpression, content.UpdateValue.Kind);
        var value = plan.CSharpValues[content.UpdateValue.Index];
        Assert.False(value.Operation.IsDefault);
        Assert.Equal("label", value.Operation.ToDisplayString());
    }

    [Fact]
    public void Create_KeepsSynthesizedStringAsBoundUpdateOperation()
    {
        const string component =
            """
            using Avalonia.Controls;

            state int count = 1;

            <TextBlock>Count: {count}</TextBlock>
            """;
        var plan = CreatePlan(component);
        var content = Assert.Single(plan.PropertyContents);

        Assert.False(content.FirstUpdateValue.IsValid);
        Assert.Equal(ComponentContentValueKind.CSharpExpression, content.UpdateValue.Kind);
        var value = plan.CSharpValues[content.UpdateValue.Index];
        Assert.False(value.Operation.IsDefault);
        Assert.Contains("count", value.Operation.ToDisplayString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_PreservesMixedTextElementExpressionElementSemanticOrder()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            state string suffix = "C";

            <MixedContentHost>A<Button />{suffix}<Border /></MixedContentHost>
            """;
        const string csharp =
            """
            using Avalonia.Metadata;
            using System.Collections.Generic;

            namespace Demo;

            public sealed class MixedContentHost
            {
                [Content]
                public List<object> Items { get; } = new();
            }
            """;
        var plan = CreatePlan(component, csharp);
        var owner = GetElement(plan, "MixedContentHost");
        var button = GetElement(plan, "Button");
        var border = GetElement(plan, "Border");

        Assert.Equal(ComponentContentTargetKind.Collection, owner.Content.Kind);
        var content = plan.CollectionContents[owner.Content.Index];
        var items = GetItems(plan, content.Items);
        Assert.Equal(
            [
                ComponentContentValueKind.Constant,
                ComponentContentValueKind.Element,
                ComponentContentValueKind.CSharpExpression,
                ComponentContentValueKind.Element,
            ],
            items.Select(static item => item.Value.Kind).ToArray());
        Assert.Equal("A", plan.CSharpValues[items[0].Value.Index].LiteralValue);
        Assert.Equal(button.Id, items[1].Value.Index);
        Assert.Equal(
            "suffix",
            plan.CSharpValues[items[2].Value.Index].Operation.ToDisplayString());
        Assert.Equal(border.Id, items[3].Value.Index);
    }

    [Fact]
    public void Create_LowersComponentCollectionParameterToStableHelperName()
    {
        const string component =
            """
            using Avalonia.Controls;

            <CollectionHost>
                <TextBlock />
                <Button />
            </CollectionHost>
            """;

        const string collectionHost =
            """
            using Avalonia.Controls;
            using System.Collections.Generic;

            param string Header;
            param IList<Control> Content;
            """;

        var plan = CreatePlanWithChildComponent(
            component,
            collectionHost,
            "CollectionHost.akbura");

        var owner = GetElement(plan, "CollectionHost");
        var textBlock = GetElement(plan, "TextBlock");
        var button = GetElement(plan, "Button");

        Assert.Equal(ComponentContentTargetKind.Collection, owner.Content.Kind);

        var content = plan.CollectionContents[owner.Content.Index];

        Assert.Equal(owner.Id, content.OwnerElementId);
        Assert.Equal(
            CollectionWriteKind.ComponentParameter,
            content.Destination.Kind);
        Assert.Equal(
            "Content",
            content.Destination.ComponentParameterName);
        Assert.False(content.Destination.Property.IsValid);
        Assert.Equal(
            [textBlock.Id, button.Id],
            [.. GetItems(plan, content.Items).Select(static item => item.Value.Index)]);
    }

    [Fact]
    public void Create_RepresentsDeferredAndTemplateContentWithoutEagerValues()
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
                    <TextBlock />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var plan = CreatePlan(component);
        var deferred = Assert.Single(plan.DeferredContents);
        var template = Assert.Single(plan.Templates);
        var values = plan.PropertyContents
            .Select(static content => content.FirstUpdateValue)
            .Where(static value => value.IsValid)
            .ToArray();

        Assert.Contains(values, value =>
            value.Kind == ComponentContentValueKind.DeferredContent &&
            value.Index == deferred.Id &&
            !value.IsEager);
        Assert.Contains(values, value =>
            value.Kind == ComponentContentValueKind.Template &&
            value.Index == template.Id &&
            !value.IsEager);
        Assert.DoesNotContain(values, static value => value.IsEager);
        Assert.Empty(plan.CollectionContents);
        Assert.Empty(plan.ContentItems);
    }

    [Fact]
    public void Create_EmptyDeferredContentDoesNotCreateTarget()
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
        Assert.Empty(plan.PropertyContents);
        Assert.Empty(plan.CollectionContents);
        Assert.Empty(plan.ContentItems);
    }

    [Theory]
    [InlineData("<Border />")]
    [InlineData("<Border>invalid text</Border>")]
    public void Create_EmptyOrInvalidContentDoesNotCreateTarget(string markup)
    {
        var component =
            "using Avalonia.Controls;\r\n\r\n" + markup;
        var plan = CreatePlan(component);
        var owner = Assert.Single(plan.Elements);

        Assert.False(owner.Content.IsValid);
        Assert.Empty(plan.PropertyContents);
        Assert.Empty(plan.CollectionContents);
        Assert.Empty(plan.ContentItems);
    }

    private static ComponentPlan CreatePlan(
        string component,
        string? additionalCSharp = null)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            additionalCSharp);

        return CreatePlan(fixture);
    }

    private static ComponentPlan CreatePlanWithChildComponent(
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
        var fixture = new AkcssActivatorPlannerTests.PlannerFixture(
            baseFixture.CSharpCompilation,
            baseFixture.ComponentTree,
            externalAkcssTree: null,
            compilation.GetSemanticModel(baseFixture.ComponentTree));

        return CreatePlan(fixture);
    }

    private static ComponentPlan CreatePlan(
        AkcssActivatorPlannerTests.PlannerFixture fixture)
    {
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

    private static ComponentContentItemPlan[] GetItems(
        in ComponentPlan plan,
        ComponentPlanRange range)
    {
        return plan.ContentItems.AsSpan(range.Start, range.Length).ToArray();
    }

    private static void AssertPropertyElementContract(
        in ComponentPropertyElementPlan propertyElement,
        int ownerElementId)
    {
        // The final property-element contract exposes only lowered identity,
        // syntax, and content-target data; semantic operations stay in planning.
        int id = propertyElement.Id;
        int ownerId = propertyElement.OwnerElementId;
        MarkupElementSyntax syntax = propertyElement.Syntax;
        ComponentContentTargetReference content = propertyElement.Content;

        Assert.True(id >= 0);
        Assert.Equal(ownerElementId, ownerId);
        Assert.NotNull(syntax);
        Assert.True(content.IsValid);
    }
}
