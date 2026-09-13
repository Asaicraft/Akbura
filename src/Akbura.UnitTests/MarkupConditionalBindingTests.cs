using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed class MarkupConditionalBindingTests
{
    [Fact]
    public void NestedConditionalTemplateNameShadowing_PreservesCompletedParentContentBinding()
    {
        var fixture = CreateFixture(
            """
            param bool ready = true;
            <ItemsControl>
                <ItemsControl.ItemTemplate x.DataType="Group" x.ItemName="group">
                    <Border>
                        $if (ready)
                        {
                            <StackPanel>
                                <TextBox x.Name="outerSource" Text={group.Name} />
                                <ItemsControl>
                                    <ItemsControl.ItemTemplate x.DataType="Person" x.ItemName="person">
                                        <Border>
                                            $if (person.Selected)
                                            {
                                                <StackPanel>
                                                    <TextBox x.Name="outerSource" Text="inner scoped" />
                                                    <TextBlock Text=${Binding #outerSource.Text} />
                                                </StackPanel>
                                            }
                                        </Border>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        }
                    </Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """,
            "namespace Demo; public sealed class Group { public string Name => \"group\"; } " +
            "public sealed class Person { public bool Selected => true; }");

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
        var inner = fixture.GetRootElement().DescendantNodes().OfType<MarkupElementSyntax>().Last(element =>
            element.StartTag!.Name.ToFullString().Trim() == "StackPanel");
        var symbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(fixture.SemanticModel.GetSymbolInfo(inner).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupContentOperation>(fixture.SemanticModel.GetOperation(inner));

        Assert.Equal(2, symbol.Children.Length);
        Assert.Equal(2, operation.Content.Length);
        Assert.Equal("Children", operation.Property!.Name);
        Assert.Same(operation, fixture.SemanticModel.GetOperation(inner));
    }

    [Fact]
    public void OrdinaryNamedSiblings_PreserveCompletedParentContentBinding()
    {
        var fixture = CreateFixture(
            "<StackPanel><TextBox x.Name=\"source\" Text=\"current\" />" +
            "<TextBlock Text=${Binding #source.Text} /></StackPanel>");
        var root = fixture.GetRootElement();

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
        var symbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(fixture.SemanticModel.GetSymbolInfo(root).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupContentOperation>(fixture.SemanticModel.GetOperation(root));

        Assert.Equal(2, symbol.Children.Length);
        Assert.Equal(2, operation.Content.Length);
        Assert.Equal("Children", operation.Property!.Name);
        Assert.Same(operation, fixture.SemanticModel.GetOperation(root));
    }

    [Theory]
    [InlineData("person.Name is not { } name", "name.Length > 0")]
    [InlineData("person.Name == null", "person.Name.Length > 0")]
    public void ConditionalTypedTemplateRoot_UsesTheItemScopeAndCSharpNullableFlow(string first, string second)
    {
        var fixture = CreateFixture(
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
            "$if (" + first + ") { <TextBlock Text={person.Name} /> }" +
            "$else if (" + second + ") { <Button /> }" +
            "</ItemsControl.ItemTemplate></ItemsControl>",
            "namespace Demo; public sealed class Person { public string? Name { get; set; } }");
        var boundary = Assert.Single(fixture.GetChildElements(), element =>
            element.StartTag!.Name.ToFullString().Trim() == "ItemsControl.ItemTemplate");
        var facts = fixture.SemanticModel.GetConditionalTemplateRootInfo(boundary);
        var operation = Assert.IsAssignableFrom<IMarkupIfOperation>(
            fixture.SemanticModel.GetOperation(boundary.Body.OfType<MarkupIfStatementSyntax>().Single()));

        Assert.True(facts.IsSupported);
        Assert.True(facts.IsImplicitControlRoot);
        Assert.Equal(MarkupConditionalTemplateRootKind.DataTemplate, facts.Kind);
        Assert.Equal("Control", facts.ContentModel.AllowedChildType.Name);
        Assert.Equal(0, operation.Cardinality.Minimum);
        Assert.Equal(1, operation.Cardinality.Maximum);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("DataTemplate", (int)MarkupConditionalTemplateRootKind.DeferredDataTemplate)]
    [InlineData("ControlTemplate", (int)MarkupConditionalTemplateRootKind.DeferredControlTemplate)]
    public void NativeDeferredConditionalRoots_ExposeTheirSupportedHostContract(string template,
        int kind)
    {
        var fixture = CreateFixture("using Avalonia.Markup.Xaml.Templates; param bool ready = true; <" + template +
            ">$if (ready) { <TextBlock /> } $else { <Button /> }</" + template + ">");

        var facts = fixture.SemanticModel.GetConditionalTemplateRootInfo(fixture.GetRootElement());

        Assert.True(facts.IsSupported);
        Assert.True(facts.IsImplicitControlRoot);
        Assert.Equal((MarkupConditionalTemplateRootKind)kind, facts.Kind);
        Assert.Equal("Control", facts.ResultType!.Name);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void CompleteConditionalDataTemplateAssignments_StayOutsideTheTemplateFactory()
    {
        var fixture = CreateFixture(
            "using Avalonia.Markup.Xaml.Templates; param bool ready = true; <ItemsControl><ItemsControl.ItemTemplate>" +
            "$if (ready) { <DataTemplate><TextBlock /></DataTemplate> }" +
            "$else { <DataTemplate><Button /></DataTemplate> }" +
            "</ItemsControl.ItemTemplate></ItemsControl>");
        var boundary = Assert.Single(fixture.GetChildElements(), element =>
            element.StartTag!.Name.ToFullString().Trim() == "ItemsControl.ItemTemplate");

        var facts = fixture.SemanticModel.GetConditionalTemplateRootInfo(boundary);

        Assert.False(facts.IsImplicitControlRoot);
        Assert.False(fixture.SemanticModel.IsConditionalTemplateRootDestination(boundary));
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("$if (person.Name != null)")]
    [InlineData("$if (ready) { <DataTemplate><Button /></DataTemplate> } $else if (person.Name != null)")]
    public void WholeTemplateAssignmentConditions_CannotReadTheirOwnPerInstanceItemBeforeBuild(string prefix)
    {
        var fixture = CreateFixture(
            "using Avalonia.Markup.Xaml.Templates; param bool ready = true; " +
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
            prefix + " { <DataTemplate><TextBlock /></DataTemplate> }" +
            "</ItemsControl.ItemTemplate></ItemsControl>",
            "namespace Demo; public sealed class Person { public string? Name { get; set; } }");

        var diagnostic = Assert.Single(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_ConditionalTemplateItemOutsideBuild);

        Assert.IsType<CSharpExpressionSyntax>(diagnostic.Syntax);
        Assert.Contains("person", diagnostic.Message);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void WholeTemplateAssignments_StillAllowOwnerConditionsWithAnUnusedItemDirective()
    {
        var fixture = CreateFixture(
            "using Avalonia.Markup.Xaml.Templates; param bool ready = true; " +
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
            "$if (ready) { <DataTemplate><TextBlock /></DataTemplate> }" +
            "</ItemsControl.ItemTemplate></ItemsControl>",
            "namespace Demo; public sealed class Person { }");

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("{CreateTemplate(person)}")]
    [InlineData("<DataTemplate DataType={person.GetType()}><Button /></DataTemplate>")]
    [InlineData("<DataTemplate><DataTemplate.DataType>{person.GetType()}</DataTemplate.DataType><Button /></DataTemplate>")]
    public void WholeTemplateAssignmentValuesAndHeaders_CannotReadTheirOwnPerInstanceItem(string value)
    {
        var fixture = CreateFixture(
            "using Avalonia.Markup.Xaml.Templates; " +
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
            "$if (true) { " + value + " }" +
            "</ItemsControl.ItemTemplate></ItemsControl>",
            """
            namespace Demo;
            public partial class PlannerView
            {
                public Avalonia.Controls.Templates.IDataTemplate CreateTemplate(Person person) =>
                    new Avalonia.Controls.Templates.FuncDataTemplate<object>((value, scope) =>
                        new Avalonia.Controls.TextBlock { Text = person.Name });
            }
            public sealed class Person { public string Name => "name"; }
            """);

        var diagnostic = Assert.Single(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_ConditionalTemplateItemOutsideBuild);

        Assert.Contains("person", diagnostic.Message);
        Assert.True(diagnostic.Syntax is MarkupInlineExpressionSyntax or MarkupPlainAttributeSyntax);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void CompleteTemplateAlternatives_MayDeclareTheirOwnSameNamedBuildItem()
    {
        var fixture = CreateFixture(
            "using Avalonia.Markup.Xaml.Templates; " +
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
            "$if (true) { <DataTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
            "<Border>$if (person.Name != null) { <TextBlock Text={person.Name} /> }</Border>" +
            "</DataTemplate> }" +
            "</ItemsControl.ItemTemplate></ItemsControl>",
            "namespace Demo; public sealed class Person { public string? Name { get; set; } }");

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void WholeTemplateAssignments_ReportEachDistinctInvalidItemExpressionExactlyOnce()
    {
        var fixture = CreateFixture(
            "using Avalonia.Markup.Xaml.Templates; " +
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
            "$if (true) { <DataTemplate DataType={person.GetType()}><Button /></DataTemplate> }" +
            "$else { {CreateTemplate(person)} }" +
            "</ItemsControl.ItemTemplate></ItemsControl>",
            """
            namespace Demo;
            public partial class PlannerView
            {
                public Avalonia.Controls.Templates.IDataTemplate CreateTemplate(Person person) =>
                    new Avalonia.Controls.Templates.FuncDataTemplate<object>((value, scope) =>
                        new Avalonia.Controls.TextBlock { Text = person.Name });
            }
            public sealed class Person { public string Name => "name"; }
            """);
        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_ConditionalTemplateItemOutsideBuild)
            .ToArray();

        Assert.Equal(2, diagnostics.Length);
        Assert.Single(diagnostics, diagnostic => diagnostic.Syntax is MarkupPlainAttributeSyntax);
        Assert.Single(diagnostics, diagnostic => diagnostic.Syntax is MarkupInlineExpressionSyntax);
        Assert.False(diagnostics[0].Syntax.Span.OverlapsWith(diagnostics[1].Syntax.Span));
    }

    [Fact]
    public void CompleteTemplateValueAndHeader_MayReadAnEnclosingActualBuildItem()
    {
        var fixture = CreateFixture(
            "using Avalonia.Markup.Xaml.Templates; " +
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"outer\"><Border>" +
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
            "$if (true) { <DataTemplate DataType={outer.GetType()}><Button /></DataTemplate> }" +
            "$else { {CreateTemplate(outer)} }" +
            "</ItemsControl.ItemTemplate></ItemsControl>" +
            "</Border></ItemsControl.ItemTemplate></ItemsControl>",
            """
            namespace Demo;
            public partial class PlannerView
            {
                public Avalonia.Controls.Templates.IDataTemplate CreateTemplate(Person person) =>
                    new Avalonia.Controls.Templates.FuncDataTemplate<object>((value, scope) =>
                        new Avalonia.Controls.TextBlock { Text = person.Name });
            }
            public sealed class Person { public string Name => "name"; }
            """);

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void NativeDataTemplateConditionalRoot_DeclaresItsOwnTypedItemWithinBuild()
    {
        var fixture = CreateFixture(
            "using Avalonia.Markup.Xaml.Templates; " +
            "<DataTemplate x.DataType=\"Person\" x.ItemName=\"person\">" +
            "$if (person.Name is { } name) { <TextBlock Text={name} /> }" +
            "$else { <Button /> }</DataTemplate>",
            "namespace Demo; public sealed class Person { public string? Name { get; set; } }");

        var facts = fixture.SemanticModel.GetConditionalTemplateRootInfo(fixture.GetRootElement());
        var condition = fixture.GetRootElement().Body.OfType<MarkupIfStatementSyntax>().Single().Condition;
        var itemReference = Assert.Single(fixture.SemanticModel.GetCSharpSymbolReferences(condition),
            reference => reference.Name == "person");

        Assert.True(facts.IsImplicitControlRoot);
        Assert.True(facts.IsSupported);
        Assert.IsAssignableFrom<IMarkupItemSymbol>(itemReference.AkburaSymbol);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void WholeTemplateAssignmentCondition_MayCaptureAnEnclosingTemplateItem()
    {
        var fixture = CreateFixture(
            "using Avalonia.Markup.Xaml.Templates; " +
            "<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"outer\"><Border>" +
            "<ItemsControl><ItemsControl.ItemTemplate>" +
            "$if (outer.Name != null) { <DataTemplate><TextBlock /></DataTemplate> }" +
            "$else { <DataTemplate><Button /></DataTemplate> }" +
            "</ItemsControl.ItemTemplate></ItemsControl>" +
            "</Border></ItemsControl.ItemTemplate></ItemsControl>",
            "namespace Demo; public sealed class Person { public string? Name { get; set; } }");

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("$if (true) { } $else { }")]
    [InlineData("$if (false) { }")]
    public void EmptyConditionalTemplateRoots_StillDescribeOneNullableRootFactory(string content)
    {
        var fixture = CreateFixture("<ItemsControl><ItemsControl.ItemTemplate>" + content +
            "</ItemsControl.ItemTemplate></ItemsControl>");
        var boundary = Assert.Single(fixture.GetChildElements());
        var facts = fixture.SemanticModel.GetConditionalTemplateRootInfo(boundary);
        var conditional = Assert.IsAssignableFrom<IMarkupIfOperation>(
            fixture.SemanticModel.GetOperation(boundary.Body.OfType<MarkupIfStatementSyntax>().Single()));

        Assert.True(facts.IsSupported);
        Assert.True(facts.IsImplicitControlRoot);
        Assert.Equal(0, conditional.Cardinality.Minimum);
        Assert.Equal(0, conditional.Cardinality.Maximum);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("CreateControl()", true)]
    [InlineData("CreateTemplate()", false)]
    public void ConditionalRootExpressions_KeepControlFactoriesSeparateFromWholeTemplateAssignments(
        string expression, bool implicitRoot)
    {
        var fixture = CreateFixture("<ItemsControl><ItemsControl.ItemTemplate>" +
            "$if (true) { {" + expression + "} }" +
            "</ItemsControl.ItemTemplate></ItemsControl>",
            """
            namespace Demo;
            public partial class PlannerView
            {
                public Avalonia.Controls.Control CreateControl() => new Avalonia.Controls.TextBlock();
                public Avalonia.Controls.Templates.IDataTemplate CreateTemplate() =>
                    new Avalonia.Controls.Templates.FuncDataTemplate<object>((value, scope) => new Avalonia.Controls.Button());
            }
            """);
        var facts = fixture.SemanticModel.GetConditionalTemplateRootInfo(Assert.Single(fixture.GetChildElements()));

        Assert.Equal(implicitRoot, facts.IsImplicitControlRoot);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("<TextBlock /><Button />")]
    [InlineData("$if (true) { <TextBlock /><Button /> }")]
    public void ConditionalTemplateRoot_RejectsMultipleSimultaneousRootControls(string content)
    {
        var fixture = CreateFixture("<ItemsControl><ItemsControl.ItemTemplate>" +
            "$if (true) { " + content + " }" +
            "</ItemsControl.ItemTemplate></ItemsControl>");

        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupConditionalContentCardinality);
    }

    [Theory]
    [InlineData("ItemsPanelTemplate")]
    [InlineData("CustomDeferredTemplate")]
    public void ConditionalDeferredRoots_RequireAnExplicitReversibleHostContract(string template)
    {
        var fixture = CreateFixture("using Avalonia.Markup.Xaml.Templates; <" + template +
            ">$if (true) { <TextBlock /> }</" + template + ">",
            """
            namespace Demo;
            public sealed class CustomDeferredTemplate
            {
                [Avalonia.Metadata.Content, Avalonia.Metadata.TemplateContent]
                public object? Content { get; set; }
            }
            """);

        var facts = fixture.SemanticModel.GetConditionalTemplateRootInfo(fixture.GetRootElement());

        Assert.False(facts.IsSupported);
        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalTemplateRoot);
    }

    [Theory]
    [InlineData("$if (true) { <TextBlock /> }")]
    [InlineData("$if (false) { }")]
    [InlineData("$if (true) { } $else { }")]
    public void ConcreteDataTemplateDestination_CannotReceiveTheInterfaceRootAdapter(string content)
    {
        var fixture = CreateFixture("<ConcreteTemplateOwner><ConcreteTemplateOwner.ItemTemplate>" +
            content +
            "</ConcreteTemplateOwner.ItemTemplate></ConcreteTemplateOwner>",
            """
            namespace Demo;
            public sealed class ConcreteTemplateOwner
            {
                public Avalonia.Markup.Xaml.Templates.DataTemplate? ItemTemplate { get; set; }
            }
            """);
        var boundary = Assert.Single(fixture.GetChildElements());

        var facts = fixture.SemanticModel.GetConditionalTemplateRootInfo(boundary);

        Assert.True(facts.IsImplicitControlRoot);
        Assert.False(facts.IsSupported);
        Assert.Single(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalTemplateRoot);
    }

    [Fact]
    public void ConcreteDataTemplateDestination_StillAcceptsCompleteTemplateAlternatives()
    {
        var fixture = CreateFixture("using Avalonia.Markup.Xaml.Templates; " +
            "<ConcreteTemplateOwner><ConcreteTemplateOwner.ItemTemplate>" +
            "$if (true) { <DataTemplate><TextBlock /></DataTemplate> }" +
            "$else { <DataTemplate><Button /></DataTemplate> }" +
            "</ConcreteTemplateOwner.ItemTemplate></ConcreteTemplateOwner>",
            """
            namespace Demo;
            public sealed class ConcreteTemplateOwner
            {
                public Avalonia.Markup.Xaml.Templates.DataTemplate? ItemTemplate { get; set; }
            }
            """);
        var facts = fixture.SemanticModel.GetConditionalTemplateRootInfo(Assert.Single(fixture.GetChildElements()));

        Assert.False(facts.IsImplicitControlRoot);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void ConditionalContent_PreservesAlternativesAndSequenceCardinality()
    {
        var fixture = CreateFixture(
            """
            param bool ready = true;
            param bool other = false;
            <StackPanel>
                <TextBlock Text="before" />
                $if (ready)
                {
                    <TextBlock Text="first" />
                    <Button>second</Button>
                }
                $else if (other)
                {
                }
                $else
                {
                    <Border />
                }
                <TextBlock Text="after" />
            </StackPanel>
            """);
        var symbol = fixture.GetElementSymbol(fixture.GetRootElement());

        Assert.Equal(3, symbol.Children.Length);
        var conditional = Assert.IsAssignableFrom<IMarkupIfOperation>(symbol.Children[1].ConditionalOperation);
        Assert.Equal(MarkupChildKind.Conditional, symbol.Children[1].Kind);
        Assert.Equal(3, conditional.Branches.Length);
        Assert.Equal(2, conditional.Branches[0].Content.Length);
        Assert.Empty(conditional.Branches[1].Content);
        Assert.Single(conditional.Branches[2].Content);
        Assert.Null(conditional.Branches[2].ConditionSyntax);
        Assert.Equal(0, conditional.Cardinality.Minimum);
        Assert.Equal(2, conditional.Cardinality.Maximum);
        var cardinality = MarkupContentCardinality.FromSequence(symbol.Children);
        Assert.Equal(2, cardinality.Minimum);
        Assert.Equal(4, cardinality.Maximum);
        Assert.False(conditional.HasErrors);
    }

    [Fact]
    public void ScalarContent_AllowsDifferentConcreteTypesInExclusiveBranches()
    {
        var fixture = CreateFixture(
            """
            param bool ready = true;
            <Border>
                $if (ready) { <TextBlock Text="hello" /> }
                $else { <Button>world</Button> }
            </Border>
            """);
        var operation = Assert.IsAssignableFrom<IMarkupContentOperation>(
            fixture.SemanticModel.GetOperation(fixture.GetRootElement()));

        var conditional = Assert.Single(operation.Content).ConditionalOperation!;
        Assert.Equal(1, conditional.Cardinality.Minimum);
        Assert.Equal(1, conditional.Cardinality.Maximum);
        Assert.Equal("TextBlock", Assert.Single(conditional.Branches[0].Content).Type.Name);
        Assert.Equal("Button", Assert.Single(conditional.Branches[1].Content).Type.Name);
        Assert.False(operation.HasErrors);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("$if (ready) { <TextBlock /><Button /> } $else { <Border /> }")]
    [InlineData("$if (ready) { <TextBlock /> } $if (!ready) { <Button /> }")]
    public void ScalarContent_RejectsPotentiallySimultaneousContributions(string content)
    {
        var fixture = CreateFixture("param bool ready = true; <Border>" + content + "</Border>");

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupConditionalContentCardinality);
    }

    [Fact]
    public void NoElseAndNestedConditionals_HaveZeroMinimumWidth()
    {
        var fixture = CreateFixture(
            """
            param bool ready = true;
            <StackPanel>
                $if (ready)
                {
                    $if (!ready) { <TextBlock /> }
                }
            </StackPanel>
            """);
        var outer = Assert.Single(fixture.GetElementSymbol(fixture.GetRootElement()).Children).ConditionalOperation!;

        Assert.Equal(0, outer.Cardinality.Minimum);
        Assert.Equal(1, outer.Cardinality.Maximum);
        Assert.NotNull(Assert.Single(Assert.Single(outer.Branches).Content).ConditionalOperation);
    }

    [Fact]
    public void PropertyElement_UsesTheSameStructuredConditionalContract()
    {
        var fixture = CreateFixture(
            """
            param bool ready = true;
            <Border>
                <Border.Child>
                    $if (ready) { <TextBlock /> }
                    $else { <Button /> }
                </Border.Child>
            </Border>
            """);
        var propertyElement = Assert.Single(fixture.GetChildElements());
        var operation = Assert.IsAssignableFrom<IMarkupContentOperation>(fixture.SemanticModel.GetOperation(propertyElement));

        Assert.Equal("Child", operation.Property!.Name);
        Assert.Equal(1, Assert.Single(operation.Content).ConditionalOperation!.Cardinality.Maximum);
        Assert.False(operation.HasErrors);
    }

    [Theory]
    [InlineData("param bool ready = true;", "ready", false)]
    [InlineData("param bool? ready = true;", "ready", true)]
    [InlineData("param bool? ready = true;", "ready == true", false)]
    [InlineData("param int ready = 1;", "ready", true)]
    [InlineData("", "missing", true)]
    [InlineData("var ready = true;", "ready", false)]
    public void Conditions_UseRealCSharpIfSemantics(string declarations, string expression, bool hasErrors)
    {
        var fixture = CreateFixture(declarations + " <Border>$if (" + expression + ") { <TextBlock /> }</Border>");
        var conditional = fixture.GetRootElement().Body.OfType<MarkupIfStatementSyntax>().Single();

        var operation = Assert.IsAssignableFrom<IMarkupIfOperation>(fixture.SemanticModel.GetOperation(conditional));

        Assert.Equal(hasErrors, operation.HasErrors);
        Assert.NotNull(operation.Branches[0].Condition.Operation);
    }

    [Theory]
    [InlineData("Truthy")]
    [InlineData("ImplicitBool")]
    public void Conditions_SupportOperatorTrueAndImplicitBooleanConversions(string type)
    {
        const string csharp =
            """
            namespace Demo;
            public readonly struct Truthy
            {
                public static bool operator true(Truthy value) => true;
                public static bool operator false(Truthy value) => false;
            }
            public readonly struct ImplicitBool
            {
                public static implicit operator bool(ImplicitBool value) => true;
            }
            """;
        var fixture = CreateFixture("param " + type + " ready = default; <Border>$if (ready) { <TextBlock /> }</Border>", csharp);
        var conditional = fixture.GetRootElement().Body.OfType<MarkupIfStatementSyntax>().Single();

        var operation = Assert.IsAssignableFrom<IMarkupIfOperation>(fixture.SemanticModel.GetOperation(conditional));

        Assert.False(operation.HasErrors);
        Assert.NotNull(operation.Branches[0].Condition.Operation);
    }

    [Fact]
    public void PatternLocals_AreBoundInTheConnectedBranchScope()
    {
        var fixture = CreateFixture(
            """
            param object model = new Person();
            <StackPanel>
                $if (model is Person person)
                {
                    <TextBlock Text={person.Name} />
                }
                $else
                {
                    <TextBlock Text="none" />
                }
            </StackPanel>
            """, "namespace Demo; public sealed class Person { public string Name => \"person\"; }");
        var conditional = fixture.GetRootElement().Body.OfType<MarkupIfStatementSyntax>().Single();
        var expression = conditional.Body.DescendantNodes().OfType<InlineExpressionSyntax>().Single();

        var references = fixture.SemanticModel.GetCSharpSymbolReferences(expression);
        var person = Assert.Single(references.Where(reference => reference.Name == "person"));

        Assert.Equal(Microsoft.CodeAnalysis.SymbolKind.Local, person.CSharpDefinition.Symbol!.Kind);
        Assert.Contains(references, reference => reference.Name == "Name");
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
        var projection = fixture.SemanticModel.CreateCSharpCompletionProjection(expression.Expression,
            expression.Expression.FullSpan.Length);
        var origin = Assert.Single(projection.SymbolOrigins.Where(origin => origin.Name == "person"));
        Assert.Equal("person", fixture.ComponentTree.GetRoot().ToFullString()
            .Substring(origin.DeclarationSpan.Start, origin.DeclarationSpan.Length));
    }

    [Fact]
    public void PatternLocal_DoesNotLeakIntoTheElseBranch()
    {
        var fixture = CreateFixture(
            """
            param object model = new Person();
            <StackPanel>
                $if (model is Person person) { <TextBlock Text={person.Name} /> }
                $else { <TextBlock Text={person.Name} /> }
            </StackPanel>
            """, "namespace Demo; public sealed class Person { public string Name => \"person\"; }");

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Parameters.Any(argument =>
            argument?.ToString()?.Contains("person", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void ConditionalNames_AreLocalAndMayRepeatAcrossAlternatives()
    {
        var fixture = CreateFixture(
            """
            param bool ready = true;
            <StackPanel>
                $if (ready)
                {
                    <TextBlock x.Name="branch" Text="first" />
                    <TextBlock Text={branch.Text} />
                }
                $else
                {
                    <TextBlock x.Name="branch" Text="second" />
                }
            </StackPanel>
            """);

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        var names = fixture.SemanticModel.BindingSession.GetMarkupNameScope(
            fixture.ComponentTree.GetRoot().Members.OfType<MarkupRootSyntax>().Single());

        Assert.Empty(diagnostics);
        Assert.Equal(2, names.Declarations.Length);
        Assert.All(names.Declarations, declaration => Assert.True(declaration.IsValid));
        Assert.Empty(names.GetDeclaredSymbols(fixture.SemanticModel));
        Assert.IsType<BoundMarkupIfStatement>(fixture.SemanticModel.BindingSession.BindSemanticSyntax(
            fixture.GetRootElement().Body.OfType<MarkupIfStatementSyntax>().Single()));
    }

    [Fact]
    public void ScalarTextBranch_LowersMixedFragmentsIntoOneValue()
    {
        var fixture = CreateFixture(
            """
            param bool ready = true;
            param string name = "Akbura";
            <Button>
                $if (ready) { Hello {name}! }
                $else { Goodbye }
            </Button>
            """);
        var conditional = Assert.Single(fixture.GetElementSymbol(fixture.GetRootElement()).Children).ConditionalOperation!;

        Assert.Equal(1, conditional.Cardinality.Maximum);
        Assert.True(conditional.Branches[0].IsSynthesizedString);
        Assert.False(conditional.Branches[0].ValueOperation.IsDefault);
        Assert.Equal("Goodbye", conditional.Branches[1].LiteralValue);
        Assert.False(conditional.HasErrors);
    }

    [Fact]
    public void OutVariable_RemainsAssignedInLaterAlternatives()
    {
        var fixture = CreateFixture(
            """
            <StackPanel>
                $if (int.TryParse("1", out var parsed)) { <TextBlock Text={parsed.ToString()} /> }
                $else if (parsed == 0) { <TextBlock Text={parsed.ToString()} /> }
                $else { <TextBlock Text={parsed.ToString()} /> }
            </StackPanel>
            """);

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Empty(diagnostics);
        foreach (var expression in fixture.GetRootElement().DescendantNodes().OfType<InlineExpressionSyntax>())
        {
            Assert.Contains(fixture.SemanticModel.GetCSharpSymbolReferences(expression), reference =>
                reference.Name == "parsed" && reference.CSharpDefinition.Symbol is ILocalSymbol);
        }
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("$else {}", true)]
    [InlineData("$else { <Button /> }", false)]
    public void RequiredScalarContent_RequiresAContributionOnEveryAlternative(string finalClause, bool missing)
    {
        var fixture = CreateFixture("param bool ready = true; <ContentReceiver>$if (ready) { <TextBlock /> } " +
            finalClause + "</ContentReceiver>");
        var receiver = AkburaSyntaxTree.ParseText(
            "using Avalonia.Controls; param Control Content; <Border />", "ContentReceiver.akbura");
        var compilation = new AkburaCompilation(fixture.CSharpCompilation,
            [fixture.ComponentTree, receiver], rootNamespace: "Demo");

        var diagnostics = compilation.GetSemanticModel(fixture.ComponentTree)
            .GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Equal(missing, diagnostics.Any(diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet &&
            Equals(diagnostic.Parameters[0], "Content")));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("$else {}", true)]
    [InlineData("$else { <Button /> }", false)]
    public void RequiredScalarPropertyElement_RequiresAContributionOnEveryAlternative(string finalClause, bool missing)
    {
        var fixture = CreateFixture("param bool ready = true; <ContentReceiver><ContentReceiver.Content>" +
            "$if (ready) { <TextBlock /> } " + finalClause +
            "</ContentReceiver.Content></ContentReceiver>");
        var receiver = AkburaSyntaxTree.ParseText(
            "using Avalonia.Controls; param Control Content; <Border />", "ContentReceiver.akbura");
        var compilation = new AkburaCompilation(fixture.CSharpCompilation,
            [fixture.ComponentTree, receiver], rootNamespace: "Demo");

        var diagnostics = compilation.GetSemanticModel(fixture.ComponentTree)
            .GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Equal(missing, diagnostics.Any(diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet &&
            Equals(diagnostic.Parameters[0], "Content")));
    }

    [Theory]
    [InlineData("$if (ready) { <SolidColorBrush x.key=\"same\" Color=\"Red\" /> } $else { <SolidColorBrush x.key=\"same\" Color=\"Blue\" /> }", false)]
    [InlineData("<SolidColorBrush x.key=\"same\" Color=\"Red\" /> $if (ready) { <SolidColorBrush x.key=\"same\" Color=\"Blue\" /> }", true)]
    [InlineData("$if (ready) { <SolidColorBrush x.key=\"same\" Color=\"Red\" /> } $if (!ready) { <SolidColorBrush x.key=\"same\" Color=\"Blue\" /> }", true)]
    public void DictionaryKeys_AreComparedOnlyAcrossPotentiallyCoexistingBranches(string content, bool duplicate)
    {
        var fixture = CreateFixture("using Avalonia.Media; param bool ready = true; <Border><Border.Resources>" +
            content + "</Border.Resources></Border>");

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Equal(duplicate, diagnostics.Any(diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryDuplicateConstantKey));
        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryKeyRequired);
    }

    [Theory]
    [InlineData("<Border>$if (Hooks.Check()) { <TextBlock /> }</Border>")]
    [InlineData("param bool ready = true; <Border>$if (ready) { <TextBlock Text={Hooks.Check().ToString()} /> }</Border>")]
    public void ParentHookInvocations_AreDiagnosedInsideConditionalMarkup(string content)
    {
        var fixture = CreateFixture(content,
            "namespace Demo; public static class Hooks { [Akbura.CompilerAnotations.UseHook] public static bool Check() => true; }");

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupConditionalHookNotSupported);
    }

    [Theory]
    [InlineData("<Border>$if (System.Linq.Enumerable.Any(new[] { 1 }, value => { parent.useEffect((System.Action)(() => { })); return true; })) { <TextBlock /> }</Border>")]
    [InlineData("<Border>$if (ready) { <Button IsEnabled={System.Linq.Enumerable.Any(new[] { 1 }, value => { parent.useEffect((System.Action)(() => { })); return true; })} /> }</Border>")]
    public void EffectPrimitive_OnAnExplicitControlReceiver_IsStillAParentHookInConditionalExpressions(string content)
    {
        var fixture = CreateFixture("using Akbura.Hooks; param bool ready = true; " +
            "param Akbura.AkburaControl parent = null!; " + content);

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        var hook = Assert.Single(diagnostics,
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupConditionalHookNotSupported);
        Assert.Equal(AkburaDiagnosticSeverity.Error, hook.Severity);
        Assert.Contains("parent.useEffect", hook.Syntax!.ToFullString());
    }

    [Theory]
    [InlineData("int.TryParse(\"1\", out var parsed)", false, false)]
    [InlineData("ready && int.TryParse(\"1\", out var parsed)", false, true)]
    [InlineData("ready || int.TryParse(\"1\", out var parsed)", true, true)]
    [InlineData("!int.TryParse(\"1\", out var parsed)", false, false)]
    public void ConditionalOutVariables_FollowRealCSharpShortCircuitDefiniteAssignment(
        string condition, bool readTrueBranch, bool unassigned)
    {
        var reader = "<TextBlock Text={parsed.ToString()} />";
        var fixture = CreateFixture("param bool ready = true; <Border>$if (" + condition + ") { " +
            (readTrueBranch ? reader : "<Button />") + " } $else { " +
            (readTrueBranch ? "<Button />" : reader) + " }</Border>");

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        if (unassigned)
        {
            Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error &&
                diagnostic.Parameters.Any(parameter => parameter?.ToString()?.Contains("parsed", StringComparison.Ordinal) == true));
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    private static AkcssActivatorPlannerTests.PlannerFixture CreateFixture(string content, string? csharp = null) =>
        AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; using Demo; " + content, csharp);

    [Fact]
    public void AddOnlySelfDestination_ReportsUnsupportedDestinationAtConditional()
    {
        var fixture = CreateFixture("param bool ready = true; <AddOnlyOwner>$if (ready) { <Button /> }</AddOnlyOwner>",
            ConditionalDestinationSource);
        var syntax = Assert.Single(fixture.GetRootElement().Body.OfType<MarkupIfStatementSyntax>());

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        var unsupported = Assert.Single(diagnostics.Where(diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalContentDestination));

        Assert.Same(syntax, unsupported.Syntax);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddOnlyEnumerableDestination_IsRejectedInImplicitAndPropertyElementContent(bool propertyElement)
    {
        var content = "$if (ready) { <Button /> }";
        var fixture = CreateFixture("param bool ready = true; <EnumerableOwner>" +
            (propertyElement ? "<EnumerableOwner.Items>" + content + "</EnumerableOwner.Items>" : content) +
            "</EnumerableOwner>", ConditionalDestinationSource);

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalContentDestination &&
            diagnostic.Syntax is MarkupIfStatementSyntax);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MutableIListDestination_IsAcceptedInImplicitAndPropertyElementContent(bool propertyElement)
    {
        var content = "$if (ready) { <Button /> } $else { <TextBlock /> }";
        var fixture = CreateFixture("param bool ready = true; <ListOwner>" +
            (propertyElement ? "<ListOwner.Items>" + content + "</ListOwner.Items>" : content) +
            "</ListOwner>", ConditionalDestinationSource);

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void OwnedStyleDestination_PreservesTypedAddSelectionInsideConditional()
    {
        var fixture = CreateFixture(
            "using Avalonia.Styling; param bool ready = true; <Border><Border.Styles>" +
            "<Style Selector=\"Button\">$if (ready) { <Setter Property=\"Opacity\" Value=\"0.5\" /> }" +
            "$else { <Style Selector=\"^.active\" /> }</Style></Border.Styles><Button /></Border>");
        var conditionalSyntax = fixture.GetRootElement().DescendantNodes().OfType<MarkupIfStatementSyntax>().Single();
        var operation = Assert.IsAssignableFrom<IMarkupIfOperation>(fixture.SemanticModel.GetOperation(conditionalSyntax));

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
        Assert.Equal("SetterBase", Assert.Single(operation.Branches[0].Content).InsertionMethod!.Parameters[0].Type.Name);
        Assert.Equal("IStyle", Assert.Single(operation.Branches[1].Content).InsertionMethod!.Parameters[0].Type.Name);
    }

    private const string ConditionalDestinationSource =
        """
        namespace Demo;
        public sealed class AddOnlyOwner : Avalonia.Controls.Control
        {
            public void Add(Avalonia.Controls.Control value) { }
        }
        public sealed class AddOnlyEnumerable : System.Collections.Generic.IEnumerable<Avalonia.Controls.Control>
        {
            public void Add(Avalonia.Controls.Control value) { }
            public System.Collections.Generic.IEnumerator<Avalonia.Controls.Control> GetEnumerator() =>
                System.Linq.Enumerable.Empty<Avalonia.Controls.Control>().GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }
        public sealed class EnumerableOwner : Avalonia.Controls.Control
        {
            [Avalonia.Metadata.Content]
            public AddOnlyEnumerable Items { get; } = new();
        }
        public sealed class ListOwner : Avalonia.Controls.Control
        {
            [Avalonia.Metadata.Content]
            public System.Collections.Generic.IList<Avalonia.Controls.Control> Items { get; } =
                new System.Collections.Generic.List<Avalonia.Controls.Control>();
        }
        """;

    [Fact]
    public void DeclaredRenderLocals_ReuseBoundSymbolsAndPreserveInferredAndNullableTypes()
    {
        var fixture = CreateFixture("string? text = null; var copy = text; int left = 1, right = 2; <Border />");
        var facts = new List<CSharpLocalSymbol>();
        foreach (var member in fixture.ComponentTree.GetRoot().Members)
        {
            if (member is CSharpStatementSyntax statement)
            {
                var locals = fixture.SemanticModel.GetCSharpDeclaredLocals(statement);
                Assert.All(locals, local => Assert.Same(statement, local.DeclarationSyntax));
                facts.AddRange(locals);
            }
        }

        Assert.Equal(["text", "copy", "left", "right"], facts.Select(fact => fact.Name));
        Assert.All(facts.Take(2), fact =>
        {
            Assert.Equal(SpecialType.System_String, fact.Local.Type.SpecialType);
            Assert.Equal(NullableAnnotation.Annotated, fact.Local.Type.NullableAnnotation);
        });
        Assert.All(facts.Skip(2), fact => Assert.Equal(SpecialType.System_Int32, fact.Local.Type.SpecialType));
    }

    [Theory]
    [InlineData("var capture = new { Name = \"hello\" };", "capture.Name")]
    [InlineData("var capture = new[] { new { Name = \"hello\" } };", "capture[0].Name")]
    [InlineData("var capture = System.Linq.Enumerable.ToList(new[] { new { Name = \"hello\" } });", "capture[0].Name")]
    [InlineData("System.Span<int> capture = default;", "capture.Length.ToString()")]
    [InlineData("int* capture = null;", "((nint)capture).ToString()")]
    [InlineData("delegate*<void> capture = null;", "((nint)capture).ToString()")]
    public void ConditionalTemplates_RejectOnlyActuallyCapturedUnsupportedLocalTypes(string declaration, string expression)
    {
        var fixture = CreateFixture(declaration +
            "<ItemsControl><ItemsControl.ItemTemplate><Border>" +
            "$if (true) { <TextBlock Text={" + expression + "} /> }" +
            "</Border></ItemsControl.ItemTemplate></ItemsControl>");

        var diagnostic = Assert.Single(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalTemplateCapture);

        Assert.Contains("capture", diagnostic.Message);
        Assert.IsType<MarkupPlainAttributeSyntax>(diagnostic.Syntax);
    }

    [Theory]
    [InlineData("<ItemsControl><ItemsControl.ItemTemplate><TextBlock Text={capture.Name} /></ItemsControl.ItemTemplate></ItemsControl>")]
    [InlineData("<ItemsControl><ItemsControl.ItemTemplate><Border>$if (true) { <TextBlock Text=\"hello\" /> }</Border></ItemsControl.ItemTemplate></ItemsControl>")]
    [InlineData("<Border>$if (true) { <TextBlock Text={capture.Name} /> }</Border>")]
    public void UnsupportedLocals_OutsideConditionalTemplateCaptureKeepTheirExistingSemantics(string markup)
    {
        var fixture = CreateFixture("var capture = new { Name = \"hello\" }; " + markup);

        Assert.DoesNotContain(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalTemplateCapture);
    }

    [Fact]
    public void ConditionalTemplate_CapturesUnconditionalSiblingExpressionsAsWell()
    {
        var fixture = CreateFixture(
            "var capture = new { Name = \"hello\" }; " +
            "<ItemsControl><ItemsControl.ItemTemplate><StackPanel>" +
            "<TextBlock Text={capture.Name} /> $if (true) { <Border /> }" +
            "</StackPanel></ItemsControl.ItemTemplate></ItemsControl>");

        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalTemplateCapture);
    }

    [Fact]
    public void ConditionalOuterTemplate_CapturesReferencesClosedOverByOrdinaryInnerTemplate()
    {
        var fixture = CreateFixture(
            "var capture = new { Name = \"hello\" }; " +
            "<ItemsControl><ItemsControl.ItemTemplate><StackPanel>" +
            "$if (true) { <Border /> }" +
            "<ItemsControl><ItemsControl.ItemTemplate><TextBlock Text={capture.Name} />" +
            "</ItemsControl.ItemTemplate></ItemsControl>" +
            "</StackPanel></ItemsControl.ItemTemplate></ItemsControl>");

        Assert.Contains(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalTemplateCapture);
    }

    [Theory]
    [InlineData("TryGet(out var current);", "current")]
    [InlineData("var found = TryGet(out var current);", "found,current")]
    [InlineData("System.Action callback = () => { var hidden = 1; System.Console.WriteLine(hidden); };", "callback")]
    [InlineData("{ var hidden = 1; System.Console.WriteLine(hidden); }", "")]
    [InlineData("for (var hidden = 0; hidden < 1; hidden++) { }", "")]
    public void DeclaredRenderLocals_IncludeVisibleOutVariablesButExcludeNestedScopes(string statement, string names)
    {
        var fixture = CreateFixture(statement + " <Border />",
            "namespace Demo; public partial class PlannerView { public bool TryGet(out string value) { value = \"resolved\"; return true; } }");
        var syntax = fixture.ComponentTree.GetRoot().Members.OfType<CSharpStatementSyntax>().Single();

        var locals = fixture.SemanticModel.GetCSharpDeclaredLocals(syntax);

        Assert.Equal(names.Length == 0 ? [] : names.Split(','), locals.Select(local => local.Name));
        Assert.All(locals, local => Assert.Same(syntax, local.DeclarationSyntax));
        if (names.Contains("current", StringComparison.Ordinal))
        {
            Assert.Equal(SpecialType.System_String, Assert.Single(locals, local => local.Name == "current").Local.Type.SpecialType);
        }
    }

    [Fact]
    public void DeclaredConditionLocals_PreservePatternOutTypesAndOriginalBranchDeclarations()
    {
        var fixture = CreateFixture(
            "<Border>$if (Model is Person person) { <TextBlock Text={person.Name} /> }" +
            "$else if (TryGet(out var current)) { <TextBlock Text={current} /> }</Border>",
            """
            namespace Demo;
            public partial class PlannerView
            {
                public object? Model => new Person();
                public bool TryGet(out string value) { value = "resolved"; return true; }
            }
            public sealed class Person { public string Name => "name"; }
            """);
        var conditional = fixture.GetRootElement().Body.OfType<MarkupIfStatementSyntax>().Single();

        var pattern = Assert.Single(fixture.SemanticModel.GetCSharpDeclaredLocals(conditional.Condition));
        var outVariable = Assert.Single(fixture.SemanticModel.GetCSharpDeclaredLocals(conditional.ElseIfClauses[0].Condition));

        Assert.Equal("person", pattern.Name);
        Assert.Equal("Person", pattern.Local.Type.Name);
        Assert.Same(conditional.Condition, pattern.DeclarationSyntax);
        Assert.Equal("current", outVariable.Name);
        Assert.Equal(SpecialType.System_String, outVariable.Local.Type.SpecialType);
        Assert.Same(conditional.ElseIfClauses[0].Condition, outVariable.DeclarationSyntax);
    }

    [Fact]
    public void DeclaredConditionLocals_DoNotExportNestedLambdaPatternDeclarations()
    {
        var fixture = CreateFixture(
            "<Border>$if (System.Linq.Enumerable.Any(new[] { Model }, value => value is Person hidden)) { <TextBlock /> }</Border>",
            "namespace Demo; public partial class PlannerView { public object? Model => null; } public sealed class Person { }");
        var condition = fixture.GetRootElement().Body.OfType<MarkupIfStatementSyntax>().Single().Condition;

        Assert.Empty(fixture.SemanticModel.GetCSharpDeclaredLocals(condition));
    }

    [Fact]
    public void ConditionalOutVariableReferences_PreserveNullableFlowAtEachRead()
    {
        var fixture = CreateFixture(
            "<Border>$if (TryGet(out var current)) { <TextBlock Text={current.Name} /> }" +
            "$else { <TextBlock Text={current?.Name} /> }</Border>",
            """
            namespace Demo;
            public partial class PlannerView
            {
                public bool TryGet([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Person? value)
                {
                    value = null;
                    return false;
                }
            }
            public sealed class Person { public string Name => "name"; }
            """);
        var conditional = fixture.GetRootElement().Body.OfType<MarkupIfStatementSyntax>().Single();
        var bodyAttribute = conditional.Body.DescendantNodes().OfType<MarkupPlainAttributeSyntax>().Single();
        var elseAttribute = conditional.ElseClause!.Body.DescendantNodes().OfType<MarkupPlainAttributeSyntax>().Single();
        var reachedReference = Assert.Single(fixture.SemanticModel.GetCSharpSymbolReferences(bodyAttribute),
            reference => reference.Name == "current");
        var alternativeReference = Assert.Single(fixture.SemanticModel.GetCSharpSymbolReferences(elseAttribute),
            reference => reference.Name == "current");

        Assert.Equal(NullableAnnotation.Annotated, Assert.Single(
            fixture.SemanticModel.GetCSharpDeclaredLocals(conditional.Condition)).Local.Type.NullableAnnotation);
        Assert.Equal(NullableFlowState.NotNull, reachedReference.NullableFlowState);
        Assert.Equal(NullableFlowState.MaybeNull, alternativeReference.NullableFlowState);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("nameof(current)", true)]
    [InlineData("@nameof(current)", false)]
    [InlineData("nameof(current)", false)]
    public void ConditionalReferences_DistinguishNameOfOperandsFromRealMethodArguments(string expression,
        bool nameOfOperand)
    {
        var method = nameOfOperand ? string.Empty : "public string @nameof(Person person) => person.Name;";
        var fixture = CreateFixture("<Border>$if (Model is Person current) { <TextBlock Text={" +
            expression + "} /> }</Border>",
            $$"""
            namespace Demo;
            public partial class PlannerView
            {
                public object? Model => new Person();
                {{method}}
            }
            public sealed class Person { public string Name => "name"; }
            """);
        var attribute = Assert.Single(fixture.GetRootElement().DescendantNodes().OfType<MarkupPlainAttributeSyntax>());
        var reference = Assert.Single(fixture.SemanticModel.GetCSharpSymbolReferences(attribute),
            reference => reference.Name == "current");

        Assert.Equal(nameOfOperand, reference.IsNameOfOperand);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void ConditionalTemplate_RejectsUnsupportedAliasCapturedFromAnOuterMarkupCondition()
    {
        var fixture = CreateFixture(
            "var model = new { Name = \"hello\" }; " +
            "<Border>$if (model is var person) {" +
            "<ItemsControl><ItemsControl.ItemTemplate><Border>" +
            "$if (true) { <TextBlock Text={person.Name} /> }" +
            "</Border></ItemsControl.ItemTemplate></ItemsControl>" +
            "}</Border>");

        var diagnostic = Assert.Single(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalTemplateCapture);
        Assert.Contains("person", diagnostic.Message);
    }

    [Fact]
    public void ConnectedExpressionProbe_SelectsItsOwnReturnRatherThanPrecedingLambdaReturns()
    {
        var fixture = CreateFixture(
            "var getter = new System.Func<string>(() => { return \"captured\"; }); " +
            "<Border>$if (getter().Length > 0) { <TextBlock Text={getter()} /> }</Border>");

        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }
}
