using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.UnitTests;

public sealed class MarkupPropertyAssignmentTests
{
    [Fact]
    public void MetadataReader_InheritsOverridesAndDeduplicatesDependencies()
    {
        const string csharp =
            """
            using Avalonia;
            using Avalonia.Data;
            using Avalonia.Metadata;
            namespace Demo;
            public class BaseAssignment
            {
                public AvaloniaProperty? Target { get; set; }
                [Content, AssignBinding, DependsOn(nameof(Target))]
                public virtual object? Payload { get; set; }
            }
            public sealed class CustomAssignment : BaseAssignment
            {
                [DependsOn(nameof(Target))]
                public override object? Payload { get; set; }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture("<object />", csharp);
        var type = fixture.CSharpCompilation.GetTypeByMetadataName("Demo.CustomAssignment")!;
        var property = Assert.IsAssignableFrom<RoslynPropertySymbol>(Assert.Single(type.GetMembers("Payload")));
        var reader = new MarkupPropertyMetadataReader();

        var metadata = reader.GetMetadata(property);

        Assert.True(metadata.IsContent);
        Assert.True(metadata.AssignBinding);
        Assert.Equal("Target", Assert.Single(metadata.Dependencies).Name);
        Assert.Empty(metadata.InvalidDependencies);
        Assert.Equal(SpecialType.System_Object, property.Type.SpecialType);
    }

    [Fact]
    public void MetadataReader_ReportsUnknownDependencyWithoutInventingMember()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture("<object />",
            "namespace Demo; public class Assignment { [Avalonia.Metadata.DependsOn(\"Absent\")] public object? Payload { get; set; } }");
        var property = fixture.CSharpCompilation.GetTypeByMetadataName("Demo.Assignment")!.GetMembers("Payload").Single();

        var metadata = new MarkupPropertyMetadataReader().GetMetadata(property);

        Assert.Empty(metadata.Dependencies);
        Assert.Equal("Absent", Assert.Single(metadata.InvalidDependencies));
    }

    [Theory]
    [InlineData("Button.Background", "IBrush")]
    [InlineData("Grid.Row", "Int32")]
    [InlineData("Button.IsVisible", "Boolean")]
    [InlineData("TextBox.Text", "String")]
    public void QualifiedReference_UsesActualFieldAndValueType(string text, string valueType)
    {
        var fixture = CreateAssignmentFixture($"Target=\"{text}\" Payload=\"Red\"");
        var element = fixture.GetChildElements().Single();

        var reference = fixture.SemanticModel.ResolveMarkupAvaloniaPropertyReference(text, element);

        Assert.NotNull(reference);
        Assert.Equal(valueType, reference.ValueType.Name);
        Assert.EndsWith("Property", reference.Field.Name, StringComparison.Ordinal);
        Assert.True(reference.Field.IsStatic);
    }

    [Fact]
    public void Reference_PreservesOwnerAndPropertySpansAndInheritedDeclaringType()
    {
        var fixture = CreateAssignmentFixture("Target=\"Button.Background\" Payload=\"Red\"");
        var element = fixture.GetChildElements().Single();
        var attribute = element.StartTag!.Attributes.OfType<MarkupPlainAttributeSyntax>()
            .Single(attribute => attribute.Name.ToString() == "Target");
        var literal = Assert.IsType<MarkupLiteralAttributeValueSyntax>(attribute.Value);

        var reference = fixture.SemanticModel.GetMarkupAvaloniaPropertyReference(literal);

        Assert.NotNull(reference);
        var source = fixture.ComponentTree.GetRoot().ToFullString();
        Assert.Equal("Button", source.Substring(reference.OwnerSpan.Start, reference.OwnerSpan.Length));
        Assert.Equal("Background", source.Substring(reference.PropertySpan.Start, reference.PropertySpan.Length));
        Assert.Equal("Button", reference.LookupOwner.Name);
        Assert.NotEqual(reference.LookupOwner, reference.Field.ContainingType);
    }

    [Theory]
    [InlineData("Button /template/ Border", "Border")]
    [InlineData("Button > Border", "Border")]
    [InlineData("Button Border", "Border")]
    [InlineData(":is(Button):pointerover", "Button")]
    [InlineData("Button.primary", "Button")]
    public void SelectorContext_UsesRightmostTypedCompound(string selector, string target)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            $"using Avalonia.Controls; using Avalonia.Styling; <Style Selector=\"{selector}\"><Setter Property=\"Background\" Value=\"Red\" /></Style>");
        var setter = fixture.GetChildElements().Single();

        var context = fixture.SemanticModel.GetMarkupStyleTargetContext(setter);

        Assert.False(context.IsUnknown);
        Assert.Equal(target, Assert.Single(context.Types).Name);
        Assert.NotNull(fixture.SemanticModel.ResolveMarkupAvaloniaPropertyReference("Background", setter));
    }

    [Fact]
    public void SelectorList_DoesNotGuessWhenReferencesDifferAcrossBranches()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Avalonia.Styling; <Style Selector=\"Button, TextBlock\"><Setter Property=\"Background\" Value=\"Red\" /></Style>");
        var setter = fixture.GetChildElements().Single();

        var context = fixture.SemanticModel.GetMarkupStyleTargetContext(setter);

        Assert.True(context.IsAmbiguous);
        Assert.Null(fixture.SemanticModel.ResolveMarkupAvaloniaPropertyReference("Background", setter));
        Assert.NotNull(fixture.SemanticModel.ResolveMarkupAvaloniaPropertyReference("Button.Background", setter));
    }

    [Fact]
    public void NestedSelector_InheritsParentTargetViaNestingNode()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Avalonia.Styling; <Style Selector=\"Button\"><Style Selector=\"^:pointerover\"><Setter Property=\"Background\" Value=\"Red\" /></Style></Style>");
        var nested = fixture.GetChildElements().Single();
        var setter = nested.Body.OfType<MarkupElementContentSyntax>().Single().Element;

        var context = fixture.SemanticModel.GetMarkupStyleTargetContext(setter);

        Assert.Equal("Button", Assert.Single(context.Types).Name);
        Assert.False(context.IsUnknown);
    }

    [Fact]
    public void ControlThemeTargetType_ProvidesNestedSelectorContext()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Avalonia.Styling; <ControlTheme TargetType={typeof(Button)}><Style Selector=\"^:pointerover\"><Setter Property=\"Background\" Value=\"Red\" /></Style></ControlTheme>");
        var nested = fixture.GetChildElements().Single();
        var setter = nested.Body.OfType<MarkupElementContentSyntax>().Single().Element;

        var context = fixture.SemanticModel.GetMarkupStyleTargetContext(setter);

        Assert.Equal("Button", Assert.Single(context.Types).Name);
        Assert.False(context.IsUnknown);
    }

    [Fact]
    public void DynamicAndTypelessSelectors_LeaveUnqualifiedTargetUnknown()
    {
        foreach (var selector in new[] { "Selector={null}", "Selector=\".primary\"" })
        {
            var fixture = AkcssActivatorPlannerTests.CreateFixture(
                $"using Avalonia.Controls; using Avalonia.Styling; <Style {selector}><Setter Property=\"Background\" Value=\"Red\" /></Style>");
            var setter = fixture.GetChildElements().Single();
            Assert.True(fixture.SemanticModel.GetMarkupStyleTargetContext(setter).IsUnknown);
            Assert.Null(fixture.SemanticModel.ResolveMarkupAvaloniaPropertyReference("Background", setter));
            Assert.NotNull(fixture.SemanticModel.ResolveMarkupAvaloniaPropertyReference("Button.Background", setter));
        }
    }

    [Theory]
    [InlineData("Target=\"Button.Background\" Payload=\"Red\"", false)]
    [InlineData("Payload=\"Red\" Target=\"Button.Background\"", false)]
    [InlineData("Payload=\"Red\"><CustomAssignment.Target>{Button.BackgroundProperty}</CustomAssignment.Target></CustomAssignment", true)]
    [InlineData("Payload=\"Red\"><CustomAssignment.Target>Button.Background</CustomAssignment.Target></CustomAssignment", true)]
    public void RenamedCustomAssignment_KeepsDeclaredObjectAndResolvesContext(
        string attributes, bool openElement = false)
    {
        var fixture = CreateAssignmentFixture(attributes, openElement);
        var element = fixture.GetChildElements().Single();
        var attribute = element.StartTag!.Attributes.OfType<MarkupPlainAttributeSyntax>()
            .Single(attribute => attribute.Name.ToString() == "Payload");
        var property = Assert.IsAssignableFrom<Akbura.Language.Symbols.IPropertySymbol>(
            fixture.SemanticModel.GetSymbolInfo(attribute).Symbol);

        var contract = fixture.SemanticModel.GetMarkupPropertyAssignmentContract(property, element);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(fixture.SemanticModel.GetOperation(attribute));

        Assert.Equal(SpecialType.System_Object, contract.DeclaredType!.SpecialType);
        Assert.Equal(SpecialType.System_Object, ((ITypeSymbol)property.Type.Symbol!).SpecialType);
        Assert.Equal("IBrush", contract.ContextualValueType!.Name);
        Assert.True(contract.AssignBinding);
        Assert.Equal("Target", Assert.Single(contract.Dependencies).Name);
        Assert.IsType<MarkupLiteralValue>(operation.ConvertedValue);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void NamespaceQualifiedComponent_ImplicitContentUsesItsOwnAssignmentContract()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; <ContentControl><Demo.CustomAssignment Target=\"Button.Background\">Red</Demo.CustomAssignment></ContentControl>",
            AssignmentSource);
        var element = fixture.GetChildElements().Single();

        var contract = fixture.SemanticModel.GetMarkupPropertyAssignmentContract(element);

        Assert.Equal(SpecialType.System_Object, contract.DeclaredType!.SpecialType);
        Assert.Equal("IBrush", contract.ContextualValueType!.Name);
        Assert.Equal("BackgroundProperty", contract.TargetPropertyReference!.Field.Name);
        Assert.True(contract.AssignBinding);
    }

    [Theory]
    [InlineData("Payload={42} />")]
    [InlineData("><CustomAssignment.Payload>{42}</CustomAssignment.Payload></CustomAssignment>")]
    [InlineData(">{42}</CustomAssignment>")]
    public void ContextualAssignment_RejectsIncompatibleTypedValuesOnEveryRoute(string assignment)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Demo; <ContentControl><CustomAssignment Target=\"Button.Background\" " +
            assignment + "</ContentControl>", AssignmentSource);

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code ==
            "AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert" &&
            diagnostic.Message.Contains("Payload", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Payload=\"Red\" />")]
    [InlineData("><CustomAssignment.Payload>Red</CustomAssignment.Payload></CustomAssignment>")]
    [InlineData(">Red</CustomAssignment>")]
    public void ContextualAssignment_ReportsUnknownDynamicDependencyOnEveryLiteralRoute(string assignment)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Demo; <ContentControl><CustomAssignment Target={default(Avalonia.AvaloniaProperty)} " +
            assignment + "</ContentControl>", AssignmentSource);

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code ==
            "AKBURA_SEMANTIC_MarkupPropertyContextualTypeUnknown");
    }

    [Theory]
    [InlineData("Payload=\"Red\" />")]
    [InlineData("><CustomAssignment.Payload>Red</CustomAssignment.Payload></CustomAssignment>")]
    [InlineData(">Red</CustomAssignment>")]
    public void ContextualAssignment_ReportsConflictingDependencyValueTypesOnEveryLiteralRoute(string assignment)
    {
        const string custom =
            """
            namespace Demo
            {
                public sealed class CustomAssignment
                {
                    public Avalonia.AvaloniaProperty? Target { get; set; }
                    public Avalonia.AvaloniaProperty? OtherTarget { get; set; }
                    [Avalonia.Metadata.Content, Avalonia.Data.AssignBinding]
                    [Avalonia.Metadata.DependsOn(nameof(Target)), Avalonia.Metadata.DependsOn(nameof(OtherTarget))]
                    public object? Payload { get; set; }
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Demo; <ContentControl><CustomAssignment Target=\"Button.Background\" OtherTarget=\"Button.Width\" " +
            assignment + "</ContentControl>", custom);

        var diagnostics = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code ==
            "AKBURA_SEMANTIC_MarkupPropertyContextualTypeAmbiguous");
    }

    [Theory]
    [InlineData("Red")]
    [InlineData("<CustomAssignment.Payload>Red</CustomAssignment.Payload>")]
    public void RenamedCustomAssignment_ConvertsBothContentRoutes(string content)
    {
        var fixture = CreateAssignmentFixture($"Target=\"Button.Background\">{content}</CustomAssignment", true);
        var element = fixture.GetChildElements().Single();
        var target = content.StartsWith('<') ? element.Body.OfType<MarkupElementContentSyntax>().Single().Element : element;
        var component = Assert.IsType<BoundMarkupComponent>(fixture.SemanticModel.BindingSession.BindSemanticSyntax(target));
        var bound = Assert.Single(component.Children.OfType<BoundMarkupContentSetter>());

        Assert.False(bound.HasErrors);
        Assert.Null(bound.LiteralValue);
        Assert.Contains("Brush.Parse", bound.ValueOperation.Syntax!.ToString(), StringComparison.Ordinal);
        Assert.Equal(SpecialType.System_Object, ((ITypeSymbol)bound.Property!.Type.Symbol!).SpecialType);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("<CustomAssignment.Payload>NaN</CustomAssignment.Payload>")]
    public void ContextualFloatingPointContent_BindsNonFiniteValuesAsCSharpConstants(string content)
    {
        var fixture = CreateAssignmentFixture("Target=\"Button.Width\">" + content + "</CustomAssignment", true);
        var element = fixture.GetChildElements().Single();
        var target = content.StartsWith('<') ? element.Body.OfType<MarkupElementContentSyntax>().Single().Element : element;
        var component = Assert.IsType<BoundMarkupComponent>(fixture.SemanticModel.BindingSession.BindSemanticSyntax(target));
        var bound = Assert.Single(component.Children.OfType<BoundMarkupContentSetter>());

        Assert.False(bound.HasErrors);
        Assert.Null(bound.LiteralValue);
        Assert.True(bound.ValueOperation.ConstantValue.HasValue);
        Assert.True(double.IsNaN(Assert.IsType<double>(bound.ValueOperation.ConstantValue.Value)));
    }

    [Theory]
    [InlineData("<Button>Hello world</Button>", false)]
    [InlineData("<Button><Button.Content>Hello world</Button.Content></Button>", true)]
    public void OrdinaryContent_IdentityStringConversionPreservesLiteralText(string markup, bool propertyElement)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; " + markup);
        var element = propertyElement ? fixture.GetChildElements().Single() : fixture.GetRootElement();
        var component = Assert.IsType<BoundMarkupComponent>(fixture.SemanticModel.BindingSession.BindSemanticSyntax(element));
        var bound = Assert.Single(component.Children.OfType<BoundMarkupContentSetter>());

        Assert.False(bound.HasErrors);
        Assert.Equal("Hello world", bound.LiteralValue);
        Assert.Equal("Hello world", Assert.IsType<string>(bound.ValueOperation.ConstantValue.Value));
    }

    [Theory]
    [InlineData("<Button>Count is {count}</Button>", false)]
    [InlineData("<Button><Button.Content>Count is {count}</Button.Content></Button>", true)]
    public void OrdinaryMixedContent_NonPropertyDependenciesPreserveStringType(string markup, bool propertyElement)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; state int count = 0; " + markup);
        var element = propertyElement ? fixture.GetChildElements().Single() : fixture.GetRootElement();
        var component = Assert.IsType<BoundMarkupComponent>(fixture.SemanticModel.BindingSession.BindSemanticSyntax(element));
        var bound = Assert.Single(component.Children.OfType<BoundMarkupContentSetter>());

        Assert.False(bound.HasErrors);
        Assert.True(bound.IsSynthesizedString);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<ITypeSymbol>(bound.ValueType.Symbol).SpecialType);
        Assert.Equal(2, bound.Content.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AssignBinding_ExtensionsAndObjectResultsGenerateDirectAssignment(bool debugStructural)
    {
        const string source =
            """
            using Avalonia.Controls;
            using Demo;
            <StackPanel>
                <ContentControl><CustomAssignment Target="Button.Background" Payload=${Binding AccentBrush} /></ContentControl>
                <ContentControl><CustomAssignment Target="Button.Background" Payload=${ObjectBinding} /></ContentControl>
            </StackPanel>
            """;
        const string extension =
            """
            namespace Demo
            {
                public sealed class ObjectBindingExtension
                {
                    public object ProvideValue(System.IServiceProvider services) => new Avalonia.Data.Binding("AccentBrush");
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, AssignmentSource + "\r\n" + extension);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var semanticErrors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.True(semanticErrors.Length == 0, string.Join(Environment.NewLine,
            semanticErrors.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var holders = fixture.ComponentTree.GetRoot().DescendantNodes().OfType<MarkupElementSyntax>()
            .Where(element => element.StartTag?.Name.ToFullString().Trim() == "CustomAssignment").ToArray();
        Assert.Equal(2, holders.Length);
        foreach (var holder in holders)
        {
            var holderSymbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(
                fixture.SemanticModel.GetSymbolInfo(holder).Symbol);
            var payload = Assert.Single(holderSymbol.AttributeOperations.OfType<IMarkupPropertySetterOperation>(),
                operation => operation.Property?.Name == "Payload");
            Assert.True(PropertyWritePlan.Create(payload.Property!).AssignBinding);
            Assert.IsType<MarkupExtensionValue>(payload.ConvertedValue);
        }

        var generated = ComponentDocumentWriter.Generate(component, fixture.SemanticModel,
            "PlannerView.akbura", new Dictionary<AkburaSyntax, string>(), mode: debugStructural
                ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect).ToString();

        Assert.Contains("ObjectBindingExtension", generated, StringComparison.Ordinal);
        Assert.Contains("ProvideValue", generated, StringComparison.Ordinal);
        Assert.Contains(".Payload =", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("BindReflection", generated, StringComparison.Ordinal);
    }

    private static AkcssActivatorPlannerTests.PlannerFixture CreateAssignmentFixture(
        string attributes, bool openElement = false) =>
        AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Controls; using Demo; <ContentControl><CustomAssignment " +
            attributes + (openElement ? "></ContentControl>" : " /></ContentControl>"), AssignmentSource);

    internal const string AssignmentSource =
        """
        namespace Demo
        {
            public sealed class CustomAssignment
            {
                public Avalonia.AvaloniaProperty? Target { get; set; }
                [Avalonia.Metadata.Content]
                [Avalonia.Data.AssignBinding]
                [Avalonia.Metadata.DependsOn(nameof(Target))]
                public object? Payload { get; set; }
            }
        }
        """;
}
