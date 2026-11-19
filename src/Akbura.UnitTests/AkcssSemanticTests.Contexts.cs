using Akbura.Diagnostics;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed partial class AkcssSemanticTests
{
    [Fact]
    public void ImportedMixinUsesDefinitionImportsAndCallerArgumentScope()
    {
        var csharp = CreateCSharpCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            namespace Demo;
            public static class Definition
            {
                public const string Suffix = "definition-default";
                public static string Wrap(string value) => value;
            }
            public static class Caller
            {
                public const string Prefix = "caller-argument";
            }
            """));
        var imported = Parse(Controls + """
            @using Helpers = Demo.Definition;
            @mixin Button Imported(string prefix, string suffix = Helpers.Suffix) {
                Content: Helpers.Wrap(prefix + suffix);
            }
            """, "Lib.Shared");
        var fixture = Bind(Controls + """
            @using Lib.Shared.akcss;
            @using Helpers = Demo.Caller;
            @theme Button T { @include Imported(Helpers.Prefix); }
            """, csharp, imported);
        var include = Operation(fixture, Assert.Single(fixture.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>()));
        var data = Assert.IsType<AkcssIncludeData>(include.Data);
        Assert.Same(imported, data.Mixin!.DefinitionModel.SyntaxTree);
        Assert.Equal("caller-argument", data.Arguments[0].Binding.OperationDefinition.ConstantValue.Value);
        Assert.Equal("definition-default", data.Arguments[1].Binding.OperationDefinition.ConstantValue.Value);
        var invocation = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.Operations.IInvocationOperation>(Setter(include.Children[^1]).Value.Operation);
        Assert.Equal("Definition", invocation.TargetMethod.ContainingType.Name);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
    }

    [Fact]
    public void LocalNamesWinAndImportedAmbiguityDoesNotChooseARandomMixin()
    {
        var first = Parse(Controls + "@mixin Button Defaults() { Tag: \"first\"; }", "First");
        var second = Parse(Controls + "@mixin Button Defaults() { Tag: \"second\"; }", "Second");
        var imports = Controls + "@using First.akcss; @using Second.akcss;\n";
        var source = imports + "@theme Button T { @include Defaults(); }";
        var ambiguous = Bind(source, null, first, second);
        var directive = Assert.Single(ambiguous.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>());
        Assert.Contains("ambiguous", Assert.Single(ambiguous.Model.GetSemanticDiagnostics(directive)).Message);
        Assert.True(Operation(ambiguous, directive).HasErrors);

        var local = Bind(source + "\n@mixin Button Defaults() { Tag: \"local\"; }", null, first, second);
        directive = Assert.Single(local.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>());
        var include = Operation(local, directive);
        Assert.Same(local.Root.Members[^1], Assert.IsType<AkcssIncludeData>(include.Data).Mixin!.DeclarationSyntax);
        Assert.False(include.HasErrors);
    }

    [Fact]
    public void DuplicateHeadersAndInvalidDefaultsHaveDefinitionDiagnostics()
    {
        var csharp = CreateCSharpCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            namespace Demo;
            public static class Helper { public static double Get() => 2d; }
            """));
        var fixture = Bind(Controls + """
            @mixin Button Duplicate(double value = Demo.Helper.Get(), double value) { }
            @mixin Button Duplicate() { }
            @theme Button Repeated { }
            @theme Button Repeated { }
            """, csharp);
        var duplicates = fixture.Model.GetSemanticDiagnostics(fixture.Root);
        Assert.Equal(2, duplicates.Count(item => item.Code == ErrorCodes.AKBURA_SEMANTIC_DuplicateAkcssSymbol));
        Assert.Contains(duplicates, diagnostic => diagnostic.Message.Contains("already declared"));
        Assert.Contains(duplicates, diagnostic => diagnostic.Message.Contains("optional-parameter"));
        Assert.Contains(duplicates, diagnostic => diagnostic.Message.Contains("cannot follow an optional parameter"));
    }

    [Fact]
    public void CrossModuleIncludeCyclesRetainTheChain()
    {
        var shared = Parse(Controls + "@using Demo.Styles.akcss; @mixin Button B() { @include A(); }", "Shared");
        var fixture = Bind(Controls + """
            @using Shared.akcss;
            @mixin Button A() { @include B(); }
            @theme Button T { @include A(); }
            """, null, shared);
        var theme = Assert.IsType<AkcssThemeDeclarationSyntax>(fixture.Root.Members[^1]);
        var include = Assert.IsType<AkcssIncludeDirectiveSyntax>(Assert.Single(theme.Members));
        var operation = Operation(fixture, include);
        Assert.True(operation.HasErrors);
        var error = Assert.Single(fixture.Model.GetSemanticDiagnostics(include));
        Assert.Contains("A -> B -> A", error.Message);
        Assert.Same(include, error.Syntax);
        Assert.NotEmpty(error.RelatedSyntax);
    }

    [Fact]
    public void DirectAndIncludedTemplateBindingsRetainTheSourceAndDestination()
    {
        var fixture = Bind(Controls + """
            @mixin ContentPresenter Links() { @template-bind Background, Content; }
            @theme Button First {
                @template {
                    <ContentPresenter x.Name="Part">
                        @include Links();
                        @template-bind BorderBrush;
                    </ContentPresenter>
                }
                ^ /template/ #Part { Background: Amx.DynamicResource("background"); }
            }
            @theme Button Second {
                @template { <ContentPresenter x.Name="Part"/> }
            }
            """);
        var mixin = Assert.IsType<AkcssMixinDeclarationSyntax>(fixture.Root.Members[2]);
        var deferred = Assert.IsType<AkcssTemplateBindingData>(Operation(fixture, Assert.Single(mixin.Members)).Data);
        Assert.True(deferred.IsDeferred);
        Assert.Null(deferred.Properties[0].Source);
        Assert.Equal("BackgroundProperty", deferred.Properties[0].Destination!.AvaloniaPropertyDefinition.Name);

        var include = Operation(fixture, Assert.Single(fixture.Root.DescendantNodes().OfType<MarkupIncludeDirectiveSyntax>()));
        var instantiated = Assert.IsType<AkcssTemplateBindingData>(Assert.IsType<AkcssContextualOperation>(Assert.Single(include.Children)).Data);
        Assert.False(instantiated.IsDeferred);
        Assert.True(instantiated.IsOneWay);
        Assert.True(instantiated.UsesTemplatePriority);
        Assert.True(instantiated.UsesTargetConversion);
        Assert.Equal("Button", instantiated.Template!.TemplatedParentType.Name);
        Assert.Same(include.Context!.OwningElement, instantiated.Element);
        Assert.Equal("IBrush", instantiated.Properties[0].Source!.Type.Name);
        Assert.Equal("BackgroundProperty", instantiated.Properties[0].Destination!.AvaloniaPropertyDefinition.Name);

        var direct = Operation(fixture, Assert.Single(fixture.Root.DescendantNodes().OfType<MarkupTemplateBindDirectiveSyntax>()));
        Assert.Equal("BorderBrushProperty", Assert.IsType<AkcssTemplateBindingData>(direct.Data).Properties[0].Source!.AvaloniaPropertyDefinition.Name);
        var nested = Operation(fixture, Assert.Single(fixture.Root.DescendantNodes().OfType<AkcssNestedStyleRuleSyntax>()));
        var selector = Assert.IsType<AkcssSelectorData>(nested.Data).Selector;
        Assert.Equal(AkcssReceiverOrigin.TemplatePart, selector.Subject.Origin);
        Assert.Equal("ContentPresenter", selector.Subject.Type.Name);
        Assert.Equal(AkcssPredicateKind.Name, Assert.Single(selector.Steps[0].Target.Predicates).Kind);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
    }

    [Fact]
    public void MixinPropertyStaysBoundToItsDeclaredTargetOnASubtype()
    {
        var fixture = Bind(Controls + """
            @mixin Demo.Destination Configure() { Shared: 12; }
            @theme Demo.HiddenDestination Subtype { @include Configure(); }
            """, StyledPropertyTypes());
        var site = Assert.Single(fixture.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>());
        var setter = Setter(Assert.Single(Operation(fixture, site).Children));
        Assert.Equal("Int32", setter.Property!.Type.Name);
        var field = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IFieldSymbol>(setter.Property.AvaloniaPropertyDefinition.Symbol);
        Assert.Equal("Destination", field.ContainingType.Name);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
    }

    [Fact]
    public void OptionalParametersUseCSharpParameterDefaultRules()
    {
        var fixture = Bind(Controls + """
            @mixin Button Invalid(object value = 1) { }
            @mixin Button Valid(object value = null, int count = default) { }
            @theme Button T { @include Valid(); }
            """);
        var invalid = Assert.Single(fixture.Model.GetSemanticDiagnostics(fixture.Root));
        Assert.Contains("valid C# optional-parameter value", invalid.Message);
        var include = Assert.Single(fixture.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>());
        var data = Assert.IsType<AkcssIncludeData>(Operation(fixture, include).Data);
        Assert.All(data.Arguments, argument => Assert.True(argument.Binding.Conversion.IsImplicit));
    }

    [Fact]
    public void TemplateMarkupExpressionsUseDefinitionImports()
    {
        var csharp = CreateCSharpCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            namespace Demo;
            public static class TemplateValues
            {
                public const bool Visible = true;
                public const string Caption = "caption";
            }
            """));
        var fixture = Bind(Controls + """
            @using Values = Demo.TemplateValues;
            @theme Button Themed {
                @template {
                    <StackPanel>
                        $if (Values.Visible) { <TextBlock Text={Values.Caption}>@template-bind IsEnabled;</TextBlock> }
                    </StackPanel>
                }
            }
            """, csharp);

        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
        var conditional = Assert.Single(fixture.Root.DescendantNodes().OfType<MarkupIfStatementSyntax>());
        var branch = Assert.IsAssignableFrom<IMarkupIfOperation>(fixture.Model.GetOperation(conditional));
        var content = Assert.Single(branch.Children, child => child.Kind == OperationKind.MarkupContent);
        var element = Assert.Single(content.Children, child => child.Kind == OperationKind.MarkupElement);
        var templateBinding = Assert.Single(element.Children, child => child.Kind == OperationKind.AkcssTemplateBinding);
        Assert.Same(element, templateBinding.Parent);
        Assert.Same(branch, content.Parent);
        var attribute = Assert.Single(fixture.Root.DescendantNodes().OfType<MarkupPlainAttributeSyntax>());
        Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(fixture.Model.GetOperation(attribute));
    }

    [Fact]
    public void InvalidTemplateConfigurationHasRelatedLocationsAndPreservesStyleOrdering()
    {
        var fixture = Bind(Controls + """
            @mixin ContentPresenter Links() { @template-bind Background; }
            @theme Button T {
                Opacity: 0.6d;
                Opacity: 0.8d;
                @template {
                    <Grid>
                        <ContentPresenter Background="Red" x.Name="Part">
                            @include Links();
                            @template-bind Missing;
                        </ContentPresenter>
                        <TextBlock x.Name="Part" />
                    </Grid>
                }
            }
            """);
        var include = Assert.Single(fixture.Root.DescendantNodes().OfType<MarkupIncludeDirectiveSyntax>());
        var duplicate = Assert.Single(fixture.Model.GetSemanticDiagnostics(include));
        Assert.Contains("more than once", duplicate.Message);
        Assert.Contains(duplicate.RelatedSyntax, syntax => syntax is MarkupAttributeSyntax);
        var diagnostics = AkburaDiagnosticEngine.Collect(fixture.Tree, fixture.Model);
        Assert.Contains(diagnostics, item => item.Message.Contains("more than once") && item.AdditionalLocations.Length >= 2);
        Assert.Contains(diagnostics, item => item.Id == ErrorCodes.AKBURA_SEMANTIC_MarkupNameDuplicate);
        Assert.DoesNotContain(diagnostics, item => item.Message.Contains("Opacity"));
        Assert.Contains(diagnostics, item => item.Message.Contains("destination Avalonia property 'Missing'"));
        Assert.Contains(diagnostics, item => item.Message.Contains("source Avalonia property 'Missing'"));
    }

    [Fact]
    public void NestedDeferredTemplateNamesDoNotBecomeOuterParts()
    {
        var fixture = Bind(Controls + """
            @using Avalonia.Markup.Xaml.Templates;
            @theme Button T {
                @template {
                    <ContentPresenter>
                        <ContentPresenter.ContentTemplate>
                            <DataTemplate><TextBlock x.Name="Hidden" /></DataTemplate>
                        </ContentPresenter.ContentTemplate>
                    </ContentPresenter>
                }
                ^ /template/ #Hidden { }
            }
            """);
        var nested = Operation(fixture, Assert.Single(fixture.Root.DescendantNodes().OfType<AkcssNestedStyleRuleSyntax>()));
        Assert.True(nested.Context!.Subject.Type.IsDefault);
        Assert.False(nested.HasErrors);
        var template = Operation(fixture, Assert.Single(fixture.Root.DescendantNodes().OfType<AkcssTemplateDirectiveSyntax>()));
        var plan = Assert.IsType<AkcssTemplateData>(template.Data).Template!;
        Assert.Null(plan.NameScope.FindUniqueRootDeclaration("Hidden"));
        Assert.Contains(plan.NameScope.Declarations, item => item.Name == "Hidden");
    }

    [Fact]
    public void TemplateBindingsUseAvaloniaConversionAndKeepIndependentIncludes()
    {
        var types = StyledPropertyTypes();
        var fixture = Bind(Controls + """
            @using Demo;
            @mixin Destination Setup() { @template-bind Shared; }
            @theme TextSource Literals { @template { <Destination>@include Setup();</Destination> } }
            @theme NumberSource Numbers { @template { <Destination>@include Setup();</Destination> } }
            """, types);
        var includes = fixture.Root.DescendantNodes().OfType<MarkupIncludeDirectiveSyntax>()
            .Select(syntax => Operation(fixture, syntax)).ToArray();
        var text = Assert.IsType<AkcssTemplateBindingData>(Assert.IsType<AkcssContextualOperation>(Assert.Single(includes[0].Children)).Data);
        var number = Assert.IsType<AkcssTemplateBindingData>(Assert.IsType<AkcssContextualOperation>(Assert.Single(includes[1].Children)).Data);
        Assert.Equal("String", Assert.Single(text.Properties).Source!.Type.Name);
        Assert.Equal("Double", Assert.Single(number.Properties).Source!.Type.Name);
        Assert.Equal("Int32", Assert.Single(text.Properties).Destination!.Type.Name);
        Assert.Equal("Int32", Assert.Single(number.Properties).Destination!.Type.Name);
        Assert.NotSame(text.Template, number.Template);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
    }

    [Fact]
    public void MixinPartInferenceCanProduceDifferentResourceTypesAtEachInclude()
    {
        var fixture = Bind(Controls + """
            @using Demo;
            @mixin Button PartResource() {
                ^ /template/ #Part { Shared: Amx.DynamicResource("key"); }
            }
            @theme Button IntegerPart {
                @template { <Destination x.Name="Part"/> }
                @include PartResource();
            }
            @theme Button NumberPart {
                @template { <NumberSource x.Name="Part"/> }
                @include PartResource();
            }
            """, StyledPropertyTypes());
        var includes = fixture.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>()
            .Select(syntax => Operation(fixture, syntax)).ToArray();
        var actual = includes.Select(include => Assert.IsType<AkcssAmxInvocationValue>(
            Setter(Assert.Single(Assert.IsType<AkcssContextualOperation>(Assert.Single(include.Children)).Children)).ConvertedValue).TypeArgument.Name);
        Assert.Equal(new[] { "Int32", "Double" }, actual);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(fixture.Root));
    }

    [Fact]
    public void MarkupIncludesRejectSelectorsAndStyleIncludesRequireATemplateElement()
    {
        var fixture = Bind(Controls + """
            @mixin ContentPresenter StyleStates() { ^:pressed { Opacity: 1d; } }
            @mixin Button Bindings() { @template-bind Background; }
            @theme Button T {
                @template { <ContentPresenter>@include StyleStates();</ContentPresenter> }
                @include Bindings();
            }
            """);
        var markup = Assert.Single(fixture.Root.DescendantNodes().OfType<MarkupIncludeDirectiveSyntax>());
        var style = Assert.Single(fixture.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>());
        Assert.Contains(fixture.Model.GetSemanticDiagnostics(markup), error => error.Message.Contains("cannot introduce a selector"));
        Assert.Contains(fixture.Model.GetSemanticDiagnostics(style), error => error.Message.Contains("typed control-template parent"));
        Assert.True(Operation(fixture, markup).HasErrors);
    }

    [Fact]
    public void SourceBaseAndExternalThemeExpressionsAreDifferentCases()
    {
        var csharp = CreateCSharpCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            namespace Demo;
            public static class External
            {
                public static Avalonia.Styling.ControlTheme Theme => throw new System.Exception();
                public static string Wrong => "";
            }
            """));
        var imported = Parse(Controls + """
            @theme Button Library {
                @template { <ContentPresenter x.Name="Part" /> }
            }
            """, "Demo.Library");
        var fixture = Bind(Controls + """
            @theme Button Derived {
                @based-on global::Demo.Library.Library;
                ^ /template/ #Part { Opacity: 1d; }
            }
            @theme Button Existing { @based-on Demo.External.Theme; }
            @theme Control Untemplated { }
            @theme Button DerivedFromControl { @based-on Untemplated; }
            @theme Button Wrong { @based-on Demo.External.Wrong; }
            """, csharp, imported);
        var derived = Assert.IsType<AkcssThemeDeclarationSyntax>(fixture.Root.Members[2]);
        var source = Assert.IsType<AkcssBaseThemeData>(Operation(fixture, derived.Members[0]).Data);
        Assert.Equal("Library", source.SourceTheme!.Name);
        var selector = Assert.IsType<AkcssSelectorData>(Operation(fixture, derived.Members[1]).Data).Selector;
        Assert.Equal(AkcssReceiverOrigin.TemplatePart, selector.Subject.Origin);
        var external = Assert.IsType<AkcssThemeDeclarationSyntax>(fixture.Root.Members[3]);
        var binding = Assert.IsType<AkcssBaseThemeData>(Operation(fixture, Assert.Single(external.Members)).Data);
        Assert.Null(binding.SourceTheme);
        Assert.Equal("ControlTheme", binding.ExternalTheme.TypeSymbol?.Name);
        var wrong = Assert.IsType<AkcssThemeDeclarationSyntax>(fixture.Root.Members[^1]);
        Assert.Contains("ControlTheme", Assert.Single(fixture.Model.GetSemanticDiagnostics(wrong)).Message);
        Assert.Empty(fixture.Model.GetSemanticDiagnostics(derived));
    }

    [Fact]
    public void InvalidMarkupRootsAndConfigurationRemainQueryable()
    {
        var fixture = Bind(Controls + """
            @theme Button ConditionalRoot { @template { $if (true) { <Button /> } } }
            @theme Button ConfigurationRoot { @template { @template-bind Content; } }
            """);
        var templates = fixture.Root.DescendantNodes().OfType<AkcssTemplateDirectiveSyntax>().ToArray();
        var condition = Assert.IsAssignableFrom<IMarkupIfOperation>(Assert.Single(Operation(fixture, templates[0]).Children));
        Assert.True(condition.HasErrors);
        var configuration = Assert.IsType<AkcssContextualOperation>(Assert.Single(Operation(fixture, templates[1]).Children));
        Assert.True(configuration.HasErrors);
        Assert.Equal(OperationKind.AkcssTemplateBinding, configuration.Kind);
        Assert.Contains(fixture.Model.GetSemanticDiagnostics(fixture.Root), diagnostic => diagnostic.Message.Contains("exactly one markup element"));
    }

    [Fact]
    public void ThemeValidationHandlesInvalidPlacementInheritanceAndTemplateRoots()
    {
        var fixture = Bind(Controls + """
            @theme Button A { @based-on B; @based-on B; }
            @theme Button B { @based-on A; }
            @theme Button Empty { @template { } }
            @theme Button Multiple { @template { <Button/><Border/> } }
            @theme Control Unable { @template { <Border /> } }
            @theme Control WrongTarget { @based-on Empty; }
            .misplaced { @based-on Empty; @template { <Border /> } }
            """);
        var diagnostics = fixture.Model.GetSemanticDiagnostics(fixture.Root);
        Assert.Contains(diagnostics, error => error.Message.Contains("A -> B -> A"));
        Assert.Contains(diagnostics, error => error.Message.Contains("only one base theme"));
        Assert.Equal(2, diagnostics.Count(error => error.Message.Contains("exactly one markup element")));
        Assert.Contains(diagnostics, error => error.Message.Contains("derived from TemplatedControl"));
        Assert.Contains(diagnostics, error => error.Message.Contains("assignable from 'Control'"));
        Assert.Contains(diagnostics, error => error.Message.Contains("directly in a theme"));
    }

    [Fact]
    public void IncrementalAncestorEditRebindsTheReusedAssignment()
    {
        var fixture = Bind(Controls + """
            Button.card {
                ^:pressed { CommandParameter: "one"; }
            }
            """);
        var oldAssignment = Assert.Single(fixture.Root.DescendantNodes().OfType<AkcssAssignmentSyntax>());
        Assert.NotNull(Setter(Operation(fixture, oldAssignment)).Property);
        var edit = new TextChange(new TextSpan(fixture.Tree.Text.ToString().IndexOf("Button.card", StringComparison.Ordinal), "Button".Length), "TextBlock");
        var updatedText = fixture.Tree.Text.WithChanges(edit);
        var updated = Bind(fixture.Tree.WithChangedText(updatedText), fixture.Compilation.CSharpCompilation);
        var newAssignment = Assert.Single(updated.Root.DescendantNodes().OfType<AkcssAssignmentSyntax>());
        Assert.Same(oldAssignment.Green, newAssignment.Green);
        Assert.Null(Setter(Operation(updated, newAssignment)).Property);
        Assert.Contains("TextBlock", Assert.Single(updated.Model.GetSemanticDiagnostics(newAssignment)).Message);
    }

    [Fact]
    public void ReusedSourceTreeRefreshesImportedTemplateAndMixinBindings()
    {
        var library = Parse(Controls + """
            @theme Button Base { @template { <TextBlock x.Name="Part" /> } }
            @mixin Button Helpers() { Tag: "first"; }
            """, "Lib");
        var initial = Bind(Controls + """
            @using Lib.akcss;
            @theme Button Derived {
                @based-on Base;
                @include Helpers();
                ^ /template/ #Part { FontSize: 10d; }
            }
            Button.simple { Opacity: 0.5d; }
            """, null, library);
        var setters = initial.Root.DescendantNodes().OfType<AkcssAssignmentSyntax>().ToArray();
        Assert.False(Operation(initial, setters[0]).HasErrors);
        Assert.Empty(initial.Model.GetSemanticDiagnostics(setters[1]));
        var include = Assert.Single(initial.Root.DescendantNodes().OfType<AkcssIncludeDirectiveSyntax>());
        Assert.Equal("first", Setter(Assert.Single(Operation(initial, include).Children)).Value.ConstantValue.Value);
        var text = library.Text.ToString();
        var updatedLibrary = library.WithChangedText(library.Text.WithChanges(
            new TextChange(new TextSpan(text.IndexOf("<TextBlock", StringComparison.Ordinal) + 1, "TextBlock".Length), "Border"),
            new TextChange(new TextSpan(text.IndexOf("first", StringComparison.Ordinal), "first".Length), "second")));
        var next = new AkburaCompilation(initial.Compilation.CSharpCompilation, [],
            [initial.Tree, updatedLibrary], rootNamespace: "Demo", projectDirectory: Environment.CurrentDirectory);
        Assert.True(next.TryImportSemanticState(initial.Tree, initial.Model.GetReusableState()));
        var updated = new Fixture(initial.Tree, next, next.GetSemanticModel(initial.Tree));
        Assert.True(Operation(updated, setters[0]).HasErrors);
        Assert.Contains("Border", Assert.Single(updated.Model.GetSemanticDiagnostics(setters[0])).Message);
        Assert.Equal("second", Setter(Assert.Single(Operation(updated, include).Children)).Value.ConstantValue.Value);
        Assert.Same(setters[0], Assert.Single(updated.Root.DescendantNodes().OfType<AkcssAssignmentSyntax>(), node => node.PropertyName.ToString() == "FontSize"));
    }

    private static CSharpCompilation StyledPropertyTypes() =>
        CreateCSharpCompilation().AddSyntaxTrees(CSharpSyntaxTree.ParseText("""
            using Avalonia;
            using Avalonia.Controls;
            using Avalonia.Controls.Primitives;
            namespace Demo;

            public class TextSource : TemplatedControl
            {
                public static readonly StyledProperty<string> SharedProperty = AvaloniaProperty.Register<TextSource, string>(nameof(Shared));
                public string Shared => GetValue(SharedProperty);
            }
            public class NumberSource : TemplatedControl
            {
                public static readonly StyledProperty<double> SharedProperty = AvaloniaProperty.Register<NumberSource, double>(nameof(Shared));
                public double Shared => GetValue(SharedProperty);
            }
            public class Destination : Control
            {
                public static readonly StyledProperty<int> SharedProperty = AvaloniaProperty.Register<Destination, int>(nameof(Shared));
                public int Shared => GetValue(SharedProperty);
            }
            public class HiddenDestination : Destination
            {
                public new static readonly StyledProperty<string> SharedProperty =
                    AvaloniaProperty.Register<HiddenDestination, string>(nameof(Shared));
                public new string Shared => GetValue(SharedProperty);
            }
            """));
}
