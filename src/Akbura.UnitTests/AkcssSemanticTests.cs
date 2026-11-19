using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed partial class AkcssSemanticTests : SemanticArchitectureTestBase
{
    private const string Controls = """
        @using Avalonia.Controls;
        @using Avalonia.Controls.Presenters;
        """ + "\n";

    [Fact]
    public void UtilityApplicationAndNestedSelectorHaveDifferentSubjects()
    {
        var fixture = Bind(Controls + """
            @utilities {
                Button.bg-red-300 {
                    @if (IsPointerOver) {
                        ^ /template/ ContentPresenter {
                            Background: Amx.DynamicResource("--color-red-300");
                            ^:pressed { Opacity: 0.8d; }
                        }
                    }
                }
            }
            """);
        var utility = Assert.Single(fixture.Root.DescendantNodes().OfType<AkcssUtilityDeclarationSyntax>());
        var operation = Operation(fixture, utility);
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(fixture.Model.GetDeclaredSymbol(utility));
        Assert.Equal("Button", symbol.TargetType.Name);
        var outerCondition = Assert.Single(operation.Children.OfType<AkcssContextualOperation>());
        Assert.Equal("Button", outerCondition.Context!.Subject.Type.Name);
        Assert.Equal("Boolean", Assert.IsType<AkcssConditionData>(outerCondition.Data).Condition.TypeSymbol?.Name);

        var traversal = Assert.Single(outerCondition.Children.OfType<AkcssContextualOperation>());
        var selector = Assert.IsType<AkcssSelectorData>(traversal.Data).Selector;
        Assert.Equal(AkcssParentAnchorKind.Application, selector.Initial.Anchor!.Kind);
        Assert.Same(symbol, traversal.Context!.Application!.Declaration);
        Assert.Equal("Button", traversal.Context.Application.TargetType.Name);
        Assert.Equal(AkcssSelectorRelationship.Template, Assert.Single(selector.Steps).Relationship);
        Assert.Equal("ContentPresenter", selector.Subject.Type.Name);

        var setter = Setter(traversal.Children[0]);
        Assert.Equal("BackgroundProperty", setter.Property!.AvaloniaPropertyDefinition.Name);
        Assert.Equal("IBrush", setter.Property.Type.Name);
        var resource = Assert.IsType<AkcssAmxInvocationValue>(setter.ConvertedValue);
        Assert.Equal("DynamicResource", resource.MethodSymbol!.Name);
        Assert.Equal("IBrush", resource.TypeArgument.Name);
        Assert.Equal("--color-red-300", resource.Key.ConstantValue.Value);

        var nested = Assert.IsType<AkcssContextualOperation>(traversal.Children[1]);
        var innerSelector = Assert.IsType<AkcssSelectorData>(nested.Data).Selector;
        Assert.Same(selector, innerSelector.Initial.Anchor!.EnclosingSelector);
        Assert.Equal("ContentPresenter", innerSelector.Subject.Type.Name);
        Assert.Same(traversal, nested.Parent);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
    }

    [Fact]
    public void SelectorChainPreservesWrittenPredicatesAndFinalReceiver()
    {
        var fixture = Bind(Controls + """
            Border.unknown Button > Border /template/ ContentPresenter#Missing:native-state TextBlock {
                FontSize: 14d;
            }
            """);
        var rule = Assert.IsType<AkcssSelectorRuleSyntax>(fixture.Root.Members[^1]);
        var operation = Operation(fixture, rule);
        var selector = Assert.IsType<AkcssSelectorData>(operation.Data).Selector;
        Assert.Equal(new[] { AkcssSelectorRelationship.Descendant, AkcssSelectorRelationship.Child,
            AkcssSelectorRelationship.Template, AkcssSelectorRelationship.Descendant }, selector.Steps.Select(step => step.Relationship));
        Assert.Equal(new[] { AkcssPredicateKind.ExactType, AkcssPredicateKind.Name, AkcssPredicateKind.PseudoClass },
            selector.Steps[2].Target.Predicates.Select(predicate => predicate.Kind));
        Assert.Equal("TextBlock", selector.Subject.Type.Name);
        Assert.Equal("FontSizeProperty", Setter(Assert.Single(operation.Children)).Property!.AvaloniaPropertyDefinition.Name);
        var module = Assert.IsAssignableFrom<IAkcssModuleSymbol>(fixture.Model.GetDeclaredSymbol(fixture.Root));
        var symbol = Assert.IsType<AkcssSelectorRuleSymbol>(fixture.Model.GetDeclaredSymbol(rule));
        Assert.Same(module, symbol.ContainingSymbol);
        Assert.Empty(module.AkcssSymbols);
        Assert.Same(symbol, Assert.Single(module.Declarations));
        Assert.Same(rule, Assert.Single(Operation(fixture, fixture.Root).Children).Syntax);
        Assert.False(symbol.CanBeReferencedByName);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
    }

    [Fact]
    public void WhitespaceChangesParentSuffixIntoUnknownDescendant()
    {
        var fixture = Bind(Controls + """
            Button.example {
                ^:pointerover { Opacity: 1d; }
                ^ :pointerover { Opacity: 0.5d; }
                ^ .anything { Grid.Row: 1; }
            }
            """);
        var legacy = Assert.IsType<AkcssStyleRuleSyntax>(fixture.Root.Members[^1]);
        var operation = Operation(fixture, legacy);
        Assert.Equal(AkcssPredicateKind.ExactType, operation.Context!.Selector!.Initial.Predicates[0].Kind);
        var first = Assert.IsType<AkcssContextualOperation>(operation.Children[0]);
        var second = Assert.IsType<AkcssContextualOperation>(operation.Children[1]);
        var third = Assert.IsType<AkcssContextualOperation>(operation.Children[2]);
        Assert.Equal("Button", first.Context!.Subject.Type.Name);
        Assert.True(second.Context!.Subject.Type.IsDefault);
        Assert.True(second.HasErrors);
        Assert.Contains("no known type", Assert.Single(fixture.Model.GetSemanticDiagnostics(second.Syntax)).Message);
        Assert.Equal(AkcssSetterPropertyKind.Attached, Setter(Assert.Single(third.Children)).PropertyKind);
    }

    [Theory]
    [InlineData("^:pointerover", false)]
    [InlineData("^ /template/ Border", false)]
    [InlineData("^ /template/ Border > TextBlock", false)]
    [InlineData("^ /template/ Border TextBlock", false)]
    [InlineData("^ Border", true)]
    [InlineData("^ > Border", true)]
    [InlineData("^ Border /template/ ContentPresenter", true)]
    [InlineData("^ /template/ Button /template/ Border", true)]
    public void NativeThemeRestrictionsApplyToTheImmediateStructuralParent(string header, bool invalid)
    {
        var fixture = Bind(Controls + "@theme Button T { @if (true) { " + header + " { } } ^.state { ^ > Border { } } }");
        var rules = fixture.Root.DescendantNodes().OfType<AkcssNestedStyleRuleSyntax>().ToArray();
        var direct = Operation(fixture, rules[0]);
        var nested = Operation(fixture, rules[^1]);
        Assert.Equal(invalid, direct.HasErrors);
        Assert.False(nested.HasErrors);
        Assert.Equal(AkcssParentAnchorKind.ThemeRoot, Assert.IsType<AkcssSelectorData>(direct.Data).Selector.Initial.Anchor!.Kind);
        Assert.Equal(AkcssStyleParentKind.Style, Assert.IsType<AkcssSelectorData>(nested.Data).Selector.StructuralParent.Kind);
    }

    [Fact]
    public void ForwardIncludesKeepDistinctArgumentsAndOperationParents()
    {
        var fixture = Bind(Controls + """
            @theme Button T {
                @include Labels("first");
                ^:pressed { @include Labels("second", "alt"); }
            }
            @mixin Button Labels(string Content, string suffix = "!") {
                Tag: Content + suffix;
                ^:pointerover { Opacity: 0.8d; }
            }
            """);
        var declaration = Assert.IsType<AkcssMixinDeclarationSyntax>(fixture.Root.Members[^1]);
        var mixin = Assert.IsType<AkcssMixinSymbol>(fixture.Model.GetDeclaredSymbol(declaration));
        Assert.Same(mixin.Parameters[0], fixture.Model.GetDeclaredSymbol(declaration.Parameters.Parameters[0]));
        var includes = fixture.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>().ToArray();
        var first = Operation(fixture, includes[0]);
        var second = Operation(fixture, includes[1]);
        var firstData = Assert.IsType<AkcssIncludeData>(first.Data);
        var secondData = Assert.IsType<AkcssIncludeData>(second.Data);
        Assert.Same(mixin, firstData.Mixin);
        Assert.Same(mixin, secondData.Mixin);
        Assert.Equal("first", firstData.Arguments[0].Binding.OperationDefinition.ConstantValue.Value);
        Assert.Equal("second", secondData.Arguments[0].Binding.OperationDefinition.ConstantValue.Value);
        Assert.True(firstData.Arguments[1].IsDefault);
        Assert.Equal("!", firstData.Arguments[1].Binding.OperationDefinition.ConstantValue.Value);
        Assert.False(secondData.Arguments[1].IsDefault);
        Assert.Same(mixin.Parameters[0], secondData.Arguments[0].Parameter);
        var firstBody = Assert.IsType<AkcssContextualOperation>(first.Children[^1]);
        var secondBody = Assert.IsType<AkcssContextualOperation>(second.Children[^1]);
        Assert.Same(first, firstBody.Parent);
        Assert.Same(second, secondBody.Parent);
        Assert.NotSame(firstBody, secondBody);
        Assert.Equal(AkcssParentAnchorKind.ThemeRoot, Assert.IsType<AkcssSelectorData>(firstBody.Data).Selector.Initial.Anchor!.Kind);
        Assert.Equal(AkcssParentAnchorKind.Style, Assert.IsType<AkcssSelectorData>(secondBody.Data).Selector.Initial.Anchor!.Kind);
        var definitionRule = Operation(fixture, Assert.Single(declaration.Members.OfType<AkcssNestedStyleRuleSyntax>()));
        Assert.Equal(AkcssParentAnchorKind.Deferred,
            Assert.IsType<AkcssSelectorData>(definitionRule.Data).Selector.Initial.Anchor!.Kind);
        var definitionSetter = Setter(Operation(fixture, Assert.Single(declaration.Members.OfType<AkcssAssignmentSyntax>())));
        var binary = Assert.IsAssignableFrom<IBinaryOperation>(definitionSetter.Value.Operation);
        Assert.Equal("Content", Assert.IsAssignableFrom<IParameterReferenceOperation>(binary.LeftOperand).Parameter.Name);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
    }

    [Fact]
    public void FailingIncludesRemainQueryable()
    {
        var fixture = Bind(Controls + """
            @mixin ContentPresenter Presenter(string value) { Background: value; }
            @mixin Button Number(double value) { Width: value; }
            @theme Button T {
                @include Presenter("x");
                @include Number("invalid");
                @include Number();
                @include Missing();
            }
            """);
        var includes = fixture.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>().ToArray();
        Assert.Equal(4, includes.Length);
        foreach (var include in includes)
        {
            Assert.True(Operation(fixture, include).HasErrors);
            Assert.NotEmpty(fixture.Model.GetSemanticDiagnostics(include));
        }

        Assert.Contains("assignable", Assert.Single(fixture.Model.GetSemanticDiagnostics(includes[0])).Message);
        Assert.Contains("convertible", Assert.Single(fixture.Model.GetSemanticDiagnostics(includes[1])).Message);
    }

    [Fact]
    public void ResourceInferenceUsesMethodIdentityAndTheWholePropertyValue()
    {
        var csharp = CreateCSharpCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            namespace Demo;
            public static class FakeAmx
            {
                public static double DynamicResource(object key) => 3d;
                public static double Wrap(double value) => value;
            }
            """));
        var fixture = Bind(Controls + """
            @using ResourceApi = Akbura.Amx;
            @using Fake = Demo.FakeAmx;
            Border TextBlock {
                Foreground: (ResourceApi.DynamicResource("brush"));
                FontSize: global::Akbura.Amx.StaticResource("size");
                FontSize: Amx.StaticResource<double>("explicit");
                FontSize: Fake.DynamicResource("lookalike");
                FontSize: Fake.Wrap(Amx.DynamicResource<double>("nested"));
            }
            """, csharp);
        var operations = Operation(fixture, fixture.Root.Members[^1]);
        var properties = operations.Children.Select(Setter).ToArray();
        Assert.True(properties[0].ConvertedValue is AkcssAmxInvocationValue,
            string.Join(Environment.NewLine, fixture.Model.GetSemanticDiagnostics(fixture.Root).Select(diagnostic => diagnostic.Message)));
        Assert.Equal("IBrush", Assert.IsType<AkcssAmxInvocationValue>(properties[0].ConvertedValue).TypeArgument.Name);
        Assert.Equal("Double", Assert.IsType<AkcssAmxInvocationValue>(properties[1].ConvertedValue).TypeArgument.Name);
        Assert.Equal("StaticResource", Assert.IsType<AkcssAmxInvocationValue>(properties[2].ConvertedValue).MethodSymbol!.Name);
        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, properties[3].ValueKind);
        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, properties[4].ValueKind);
        Assert.Equal("Wrap", Assert.IsAssignableFrom<IInvocationOperation>(properties[4].Value.Operation).TargetMethod.Name);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
    }

    [Fact]
    public void ValidSemanticsDoNotClaimRuntimeGeneration()
    {
        var fixture = Bind(Controls + "Border TextBlock { FontSize: 14d; }");
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
        var module = Assert.IsAssignableFrom<IAkcssModuleSymbol>(fixture.Model.GetDeclaredSymbol(fixture.Root));
        var input = new AkcssGenerationInput(module, fixture.Model, "Styles.akcss", "Styles.akcss", []);
        var plan = AkcssModulePlanner.Create(input, "Demo");
        try
        {
            Assert.Equal(ErrorCodes.AKBURA_GENERATION_AkcssUnsupported, plan.Unsupported?.Code);
            Assert.True(plan.Symbols.IsEmpty);
        }
        finally
        {
            plan.ReturnToPool();
        }
    }

    private static Fixture Bind(string source, CSharpCompilation? csharp = null, params AkcssSyntaxTree[] imports) =>
        Bind(Parse(source, "Demo.Styles"), csharp, imports);

    private static Fixture Bind(AkcssSyntaxTree tree, CSharpCompilation? csharp = null, params AkcssSyntaxTree[] imports)
    {
        var compilation = new AkburaCompilation(csharp ?? CreateCSharpCompilation(), [],
            ImmutableArray.Create(tree).AddRange(imports), rootNamespace: "Demo", projectDirectory: Environment.CurrentDirectory);
        return new Fixture(tree, compilation, compilation.GetSemanticModel(tree));
    }

    private static AkcssSyntaxTree Parse(string source, string logicalName) =>
        AkcssSyntaxTree.ParseText(SourceText.From(source), logicalName + ".akcss");

    private static AkcssContextualOperation Operation(Fixture fixture, AkburaSyntax syntax) =>
        Assert.IsType<AkcssContextualOperation>(fixture.Model.GetOperation(syntax));

    private static AkcssSetterData Setter(IOperation operation) =>
        Assert.IsType<AkcssSetterData>(Assert.IsType<AkcssContextualOperation>(operation).Data);

    private sealed record Fixture(AkcssSyntaxTree Tree, AkburaCompilation Compilation, AkburaSemanticModel Model)
    {
        public AkcssDocumentSyntax Root => Tree.GetRoot();
    }
}
