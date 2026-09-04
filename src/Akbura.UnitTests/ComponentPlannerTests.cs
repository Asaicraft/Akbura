using Akbura.Language.CodeGeneration;
using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using Akbura.Pools;

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
        Assert.Equal(ComponentContentTargetKind.Property, propertyElement.Content.Kind);
        var content = plan.PropertyContents[propertyElement.Content.Index];
        Assert.Equal(ComponentContentValueKind.Element, content.FirstUpdateValue.Kind);
        Assert.Equal(child.Id, content.FirstUpdateValue.Index);
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
    public void Create_KeepsLogicalNamesAndStoresEscapedCSharpIdentifiers()
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
        Assert.Equal("@yield", keyword.Identifier);
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
        Assert.Equal([content.Id], GetScopeRootIds(plan, deferred.ScopeId));
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
        Assert.Equal([deferredChild.Id], GetScopeRootIds(plan, deferred.ScopeId));
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
            GetScopeRootIds(plan, template.ScopeId).SequenceEqual([border.Id]));
        Assert.Contains(templates, template =>
            GetScopeRootIds(plan, template.ScopeId).SequenceEqual([textBlock.Id]));
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
        Assert.Equal([textBlock.Id], GetScopeRootIds(plan, template.ScopeId));
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
    public void Create_IndexesCSharpPayloadsWithinTypedArray()
    {
        const string component =
            """
            using Avalonia.Controls;

            state double opacity = 0.5;

            <Border
                Background=${DynamicResource AccentBrush}
                Width="42"
                Height="21"
                Opacity={opacity} />
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var element = Assert.Single(plan.Elements);
        var writes = GetWrites(plan.PropertyWrites, element.PropertyWrites);

        Assert.Equal(
            [
                ComponentPropertyValueKind.DynamicResource,
                ComponentPropertyValueKind.Constant,
                ComponentPropertyValueKind.Constant,
                ComponentPropertyValueKind.CSharpExpression,
            ],
            writes.Select(static write => write.ValueKind).ToArray());
        Assert.Equal([0, 0, 1, 2], writes.Select(static write => write.PayloadIndex).ToArray());
        Assert.Equal(3, plan.CSharpValues.Length);
        Assert.Single(plan.MarkupExtensions);
        Assert.Equal("42", plan.CSharpValues[0].LiteralValue);
        Assert.Equal("21", plan.CSharpValues[1].LiteralValue);
        Assert.Equal("opacity", plan.CSharpValues[2].Operation.ToDisplayString());
        Assert.NotEqual(1, writes[1].PayloadIndex);
        Assert.NotEqual(2, writes[2].PayloadIndex);
        Assert.NotEqual(3, writes[3].PayloadIndex);
    }

    [Fact]
    public void Create_SeparatesBindOutAndMarkupBindingSemantics()
    {
        const string component =
            """
            using Avalonia.Controls;

            state string bindValue = "";
            state string outValue = "";
            state string plainValue = "";

            <StackPanel>
                <TextBox x.Name="bindTarget" bind:Text={bindValue} />
                <TextBox x.Name="outTarget" out:Text={outValue} />
                <TextBlock x.Name="plainTarget" Text={plainValue} />
                <TextBlock x.Name="markupTarget" Text=${Binding Name} />
            </StackPanel>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var bindTarget = GetNamedElement(plan, "bindTarget");
        var outTarget = GetNamedElement(plan, "outTarget");
        var plainTarget = GetNamedElement(plan, "plainTarget");
        var markupTarget = GetNamedElement(plan, "markupTarget");
        var bindWrite = Assert.Single(GetWrites(plan.PropertyWrites, bindTarget.PropertyWrites));
        var plainWrite = Assert.Single(GetWrites(plan.PropertyWrites, plainTarget.PropertyWrites));
        var markupWrite = Assert.Single(GetWrites(plan.PropertyWrites, markupTarget.PropertyWrites));

        Assert.Equal(ComponentPropertyValueKind.CSharpExpression, bindWrite.ValueKind);
        Assert.Equal(ComponentPropertyWritePhase.Update, bindWrite.Phase);
        Assert.Empty(GetWrites(plan.PropertyWrites, outTarget.PropertyWrites));
        Assert.Equal(ComponentPropertyValueKind.CSharpExpression, plainWrite.ValueKind);
        Assert.Equal(ComponentPropertyValueKind.MarkupBinding, markupWrite.ValueKind);
        Assert.Equal(2, plan.CSharpValues.Length);
        Assert.Single(plan.Bindings);
        Assert.Collection(
            plan.PropertySubscriptions,
            subscription => AssertSubscription(
                subscription,
                bindTarget.Id,
                ComponentPropertySynchronizationKind.Bind,
                "bindValue"),
            subscription => AssertSubscription(
                subscription,
                outTarget.Id,
                ComponentPropertySynchronizationKind.Out,
                "outValue"));

        Assert.Equal(0, bindTarget.PropertySubscriptions.Start);
        Assert.Equal(1, bindTarget.PropertySubscriptions.Length);
        Assert.Equal(1, outTarget.PropertySubscriptions.Start);
        Assert.Equal(1, outTarget.PropertySubscriptions.Length);
        Assert.Equal(2, plainTarget.PropertySubscriptions.Start);
        Assert.True(plainTarget.PropertySubscriptions.IsEmpty);
        Assert.Equal(2, markupTarget.PropertySubscriptions.Start);
        Assert.True(markupTarget.PropertySubscriptions.IsEmpty);

        Assert.Equal(0, bindTarget.FirstUpdateActions.Start);
        Assert.Equal(2, bindTarget.FirstUpdateActions.Length);
        Assert.Equal(2, outTarget.FirstUpdateActions.Start);
        Assert.Equal(2, outTarget.FirstUpdateActions.Length);
        Assert.Equal(4, plainTarget.FirstUpdateActions.Start);
        Assert.Equal(1, plainTarget.FirstUpdateActions.Length);
        Assert.Equal(5, markupTarget.FirstUpdateActions.Start);
        Assert.Equal(2, markupTarget.FirstUpdateActions.Length);
        Assert.Collection(
            plan.FirstUpdateActions,
            action => AssertAction(action, ComponentFirstUpdateActionKind.NameAssignment, 0),
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertySubscription, 0),
            action => AssertAction(action, ComponentFirstUpdateActionKind.NameAssignment, 1),
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertySubscription, 1),
            action => AssertAction(action, ComponentFirstUpdateActionKind.NameAssignment, 2),
            action => AssertAction(action, ComponentFirstUpdateActionKind.NameAssignment, 3),
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertyWrite, 2));
    }

    [Fact]
    public void Create_PreservesPropertySourceOrderInFirstUpdateActions()
    {
        const string component =
            """
            using Avalonia.Controls;

            state double width = 0;
            state double opacity = 0;

            <Border
                bind:Width={width}
                Height="1"
                out:Opacity={opacity}
                MinWidth="2" />
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var element = Assert.Single(plan.Elements);
        var writes = GetWrites(plan.PropertyWrites, element.PropertyWrites);
        var subscriptions = GetSubscriptions(plan.PropertySubscriptions, element.PropertySubscriptions);
        var actions = GetActions(plan.FirstUpdateActions, element.FirstUpdateActions);

        Assert.Equal(3, writes.Length);
        Assert.Equal(ComponentPropertyWritePhase.Update, writes[0].Phase);
        Assert.Equal(ComponentPropertyWritePhase.FirstUpdate, writes[1].Phase);
        Assert.Equal(ComponentPropertyWritePhase.FirstUpdate, writes[2].Phase);
        Assert.Equal([0, 2], subscriptions.Select(static item => item.SourceOrder).ToArray());
        Assert.Collection(
            actions,
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertySubscription, 0),
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertyWrite, 1),
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertySubscription, 1),
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertyWrite, 2));
    }

    [Fact]
    public void Create_DynamicComponentParameterWritesInBothPhasesAndIsObservable()
    {
        const string component =
            """
            state string value = "";

            <Child bind:Value={value} />
            """;
        const string childComponent =
            """
            param bind string Value = "";
            """;
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var childTree = AkburaSyntaxTree.ParseText(childComponent, "Child.akbura");
        var compilation = new AkburaCompilation(
            baseFixture.CSharpCompilation,
            [baseFixture.ComponentTree, childTree],
            rootNamespace: "Demo");
        var fixture = new AkcssActivatorPlannerTests.PlannerFixture(
            baseFixture.CSharpCompilation,
            baseFixture.ComponentTree,
            externalAkcssTree: null,
            compilation.GetSemanticModel(baseFixture.ComponentTree));
        var plan = CreatePlan(fixture);
        var element = Assert.Single(plan.Elements);
        var write = Assert.Single(plan.PropertyWrites);
        var subscription = Assert.Single(plan.PropertySubscriptions);
        var actions = GetActions(plan.FirstUpdateActions, element.FirstUpdateActions);

        Assert.Equal(PropertyWriteKind.ComponentParameter, write.Destination.Kind);
        Assert.Equal(MarkupTargetPropertyKind.GeneratedParameter, write.Destination.TargetProperty.Kind);
        Assert.Equal("Value", write.Destination.TargetProperty.Text);
        Assert.True(Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
            element.Type,
            write.Destination.TargetProperty.Symbol));
        Assert.Equal(ComponentPropertyWritePhase.Both, write.Phase);
        Assert.True(write.WritesDuringFirstUpdate);
        Assert.True(write.WritesDuringUpdate);

        Assert.Equal(PropertyObservationKind.GeneratedParameter, subscription.Observation.Kind);
        Assert.Equal("Value", subscription.Observation.Name);
        Assert.True(Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
            element.Type,
            subscription.Observation.Symbol));
        Assert.Collection(
            actions,
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertySubscription, 0),
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertyWrite, 0));
    }

    [Fact]
    public void Create_ClrPropertyCreatesNotifyPropertyChangedObservation()
    {
        const string component =
            """
            using Demo;

            state string result = "";

            <NotifyControl out:Value={result} />
            """;
        const string csharp =
            """
            using System.ComponentModel;

            namespace Demo;

            public sealed class NotifyControl : INotifyPropertyChanged
            {
                public string Value { get; set; } = "";

                public event PropertyChangedEventHandler? PropertyChanged;
            }
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component, csharp));
        var element = Assert.Single(plan.Elements);
        var subscription = Assert.Single(plan.PropertySubscriptions);

        Assert.Empty(plan.PropertyWrites);
        Assert.Equal(PropertyObservationKind.NotifyPropertyChanged, subscription.Observation.Kind);
        Assert.Equal("Value", subscription.Observation.Symbol?.Name);
        Assert.Equal(element.Id, subscription.ElementId);
        Assert.Equal(0, subscription.SourceOrder);
        Assert.Equal(1, element.PropertySubscriptions.Length);
        Assert.Equal(1, element.FirstUpdateActions.Length);
        AssertAction(
            Assert.Single(plan.FirstUpdateActions),
            ComponentFirstUpdateActionKind.PropertySubscription,
            0);
    }

    [Fact]
    public void AttachedAvaloniaProperty_CreatesAvaloniaObservation()
    {
        const string component =
            """
            using Avalonia.Controls;

            state int column = 0;

            <TextBlock Grid.Column={column} />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var observation = CreatePropertyObservation(fixture);

        Assert.Equal(PropertyObservationKind.AvaloniaProperty, observation.Kind);
        Assert.Equal("ColumnProperty", observation.Symbol?.Name);
    }

    [Fact]
    public void AttachedGetterWithoutProperty_IsNotObservable()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo;

            state int value = 0;

            <TextBlock Attached.Value={value} />
            """;
        const string csharp =
            """
            namespace Demo;

            public sealed class AttachedProperty<T>
            {
            }

            public static class Attached
            {
                public static readonly AttachedProperty<int> ValueProperty = null!;

                public static int GetValue(Avalonia.Controls.Control target) => 0;

                public static void SetValue(Avalonia.Controls.Control target, int value)
                {
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, csharp);
        var observation = CreatePropertyObservation(fixture);

        Assert.False(observation.IsValid);
    }

    [Fact]
    public void DirectMemberWithoutProperty_IsNotObservable()
    {
        const string component =
            """
            state string result = "";

            <Child out:Refresh={result} />
            """;
        const string childComponent =
            """
            command string Refresh();
            """;
        var plan = CreatePlanWithChildComponent(component, childComponent);
        var element = Assert.Single(plan.Elements);

        Assert.Empty(plan.PropertySubscriptions);
        Assert.True(element.PropertySubscriptions.IsEmpty);
        Assert.Empty(plan.PropertyWrites);
        Assert.Empty(plan.FirstUpdateActions);
    }

    [Fact]
    public void InvalidSubscriptionDoesNotBreakRanges()
    {
        const string component =
            """
            using Avalonia.Controls;

            state string invalidResult = "";
            state string validResult = "";

            <StackPanel>
                <Child x.Name="invalid" out:Refresh={invalidResult} />
                <TextBox x.Name="valid" out:Text={validResult} />
            </StackPanel>
            """;
        const string childComponent =
            """
            command string Refresh();
            """;
        var plan = CreatePlanWithChildComponent(component, childComponent);
        var invalid = GetNamedElement(plan, "invalid");
        var valid = GetNamedElement(plan, "valid");

        Assert.Empty(GetSubscriptions(plan.PropertySubscriptions, invalid.PropertySubscriptions));
        var invalidAction = Assert.Single(GetActions(
            plan.FirstUpdateActions,
            invalid.FirstUpdateActions));
        Assert.Equal(0, invalid.PropertySubscriptions.Start);
        Assert.Equal(0, invalid.FirstUpdateActions.Start);
        AssertAction(invalidAction, ComponentFirstUpdateActionKind.NameAssignment, 0);

        var subscription = Assert.Single(GetSubscriptions(
            plan.PropertySubscriptions,
            valid.PropertySubscriptions));
        var actions = GetActions(plan.FirstUpdateActions, valid.FirstUpdateActions);

        Assert.Equal(0, valid.PropertySubscriptions.Start);
        Assert.Equal(1, valid.PropertySubscriptions.Length);
        Assert.Equal(1, valid.FirstUpdateActions.Start);
        Assert.Equal(2, valid.FirstUpdateActions.Length);
        Assert.Equal(PropertyObservationKind.AvaloniaProperty, subscription.Observation.Kind);
        Assert.Collection(
            actions,
            action => AssertAction(action, ComponentFirstUpdateActionKind.NameAssignment, 1),
            action => AssertAction(action, ComponentFirstUpdateActionKind.PropertySubscription, 0));
    }

    [Fact]
    public void BindingMarkupExtensionOnClrProperty_DoesNotCreateInvalidWrite()
    {
        const string component =
            """
            using Demo;

            <PlainControl Value=${Binding Name} />
            """;
        const string csharp =
            """
            namespace Demo;

            public sealed class PlainControl : Avalonia.Controls.Control
            {
                public string Value { get; set; } = "";
            }
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component, csharp));
        var element = Assert.Single(plan.Elements);

        Assert.Empty(plan.Bindings);
        Assert.Empty(plan.PropertyWrites);
        Assert.True(element.PropertyWrites.IsEmpty);
        Assert.Empty(plan.FirstUpdateActions);
    }

    [Fact]
    public void Create_CompiledBindingToLaterNamedElementUsesDirectSource()
    {
        const string component =
            """
            using Avalonia.Controls;

            <StackPanel>
                <TextBlock Text=${CompiledBinding #input.Text} />
                <TextBox x.Name="input" />
            </StackPanel>
            """;
        var plan = CreatePlan(AkcssActivatorPlannerTests.CreateFixture(component));
        var textBlock = GetElement(plan, "TextBlock");
        var write = Assert.Single(GetWrites(plan.PropertyWrites, textBlock.PropertyWrites));
        var binding = Assert.Single(plan.Bindings);

        Assert.Equal(ComponentPropertyValueKind.MarkupBinding, write.ValueKind);
        Assert.Equal(0, write.PayloadIndex);
        Assert.True(binding.IsValid);
        Assert.Equal("input", binding.SourceExpression);
        Assert.Equal(1, binding.PathElementStart);
        Assert.Equal("#input.Text", binding.Binding.Path);
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
        Assert.Equal(ComponentPropertyWritePhase.FirstUpdate, writes[0].Phase);
        Assert.Equal(ComponentPropertyWritePhase.Update, writes[1].Phase);
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
        Assert.Equal(parent.PropertyWrites.Start + parent.PropertyWrites.Length, child.PropertyWrites.Start);
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

    private static ComponentPlan CreatePlanWithChildComponent(
        string component,
        string childComponent)
    {
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var childTree = AkburaSyntaxTree.ParseText(childComponent, "Child.akbura");
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

    private static ComponentElementPlan GetNamedElement(ComponentPlan plan, string identifier)
    {
        return Assert.Single(plan.Elements, element => string.Equals(
            element.Identifier,
            identifier,
            StringComparison.Ordinal));
    }

    private static int[] GetIds(PooledImmutableList<int> ids, ComponentPlanRange range)
    {
        return ids.AsSpan(range.Start, range.Length).ToArray();
    }

    private static int[] GetScopeRootIds(in ComponentPlan plan, int scopeId)
    {
        ref readonly var scope = ref plan.Scopes.ItemRef(scopeId);
        return GetIds(plan.ScopeRootElementIds, scope.Roots);
    }

    private static ComponentPropertyWritePlan[] GetWrites(
        PooledImmutableList<ComponentPropertyWritePlan> writes,
        ComponentPlanRange range)
    {
        return writes.AsSpan(range.Start, range.Length).ToArray();
    }

    private static ComponentPropertySubscriptionPlan[] GetSubscriptions(
        PooledImmutableList<ComponentPropertySubscriptionPlan> subscriptions,
        ComponentPlanRange range)
    {
        return subscriptions.AsSpan(range.Start, range.Length).ToArray();
    }

    private static PropertyObservationPlan CreatePropertyObservation(
        AkcssActivatorPlannerTests.PlannerFixture fixture)
    {
        var syntax = Assert.Single(
            fixture.ComponentTree.GetRoot().DescendantNodes().OfType<MarkupElementSyntax>());
        var element = Assert.IsType<IMarkupComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(syntax).Symbol, exactMatch: false);
        var operation = Assert.Single(
            element.AttributeOperations.OfType<IMarkupPropertySetterOperation>());
        var property = Assert.IsType<Akbura.Language.Symbols.IPropertySymbol>(
            operation.Property, exactMatch: false);
        var ownerType = Assert.IsType<Microsoft.CodeAnalysis.ITypeSymbol>(
            element.ComponentType, exactMatch: false);

        return PropertyObservationPlan.Create(property, ownerType);
    }

    private static ComponentFirstUpdateActionPlan[] GetActions(PooledImmutableList<ComponentFirstUpdateActionPlan> actions, ComponentPlanRange range)
    {
        return actions.AsSpan(range.Start, range.Length).ToArray();
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

    private static void AssertSubscription(
        ComponentPropertySubscriptionPlan subscription,
        int elementId,
        ComponentPropertySynchronizationKind kind,
        string targetExpression)
    {
        Assert.Equal(elementId, subscription.ElementId);
        Assert.Equal(kind, subscription.Kind);
        Assert.Equal(PropertyObservationKind.AvaloniaProperty, subscription.Observation.Kind);
        Assert.Equal(targetExpression, subscription.TargetOperation.ToDisplayString());
        Assert.Equal(Microsoft.CodeAnalysis.SpecialType.System_String, subscription.ValueType.SpecialType);
    }

    private static void AssertAction(
        ComponentFirstUpdateActionPlan action,
        ComponentFirstUpdateActionKind kind,
        int index)
    {
        Assert.Equal(kind, action.Kind);
        Assert.Equal(index, action.Index);
    }
}
