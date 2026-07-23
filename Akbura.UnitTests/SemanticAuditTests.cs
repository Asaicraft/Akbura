using Akbura.Language;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

namespace Akbura.UnitTests;

public sealed class SemanticAuditTests
{
    [Fact]
    public void CodegenContract_ExposesResolvedAccessConversionsAndStructuredValues()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Demo;

            inject ViewModel Vm;
            state int count = 1;

            <ItemsControl ItemsSource={Vm.Items}>
                <ItemsControl.ItemTemplate x.ItemName="item">
                    <Grid Grid.Column={count} Width={count} IsVisible="true" Opacity="0.5" ColumnDefinitions="*, min-max(10, *, 100)">
                        <TextBlock Text=${Binding History[0].Name} />
                        <TextBlock Text=${Format 1, Value={count}} />
                        <TextBlock Text={item.Details.Name} />
                    </Grid>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <LiteralTarget Value="hello" />
            """;
        const string csharpCode =
            """
            namespace Demo;

            public sealed class ViewModel
            {
                public System.Collections.Generic.IReadOnlyList<Item> Items { get; } = null!;
            }

            public sealed class Item
            {
                public Details Details { get; } = new();
                public System.Collections.Generic.IReadOnlyList<Details> History { get; } = null!;
            }

            public sealed class Details
            {
                public string Name { get; } = "";
            }

            public sealed class FormatExtension
            {
                public FormatExtension(int seed) { }
                public FormatExtension(string seed) { }

                public int Value { get; set; }

                public string ProvideValue(System.IServiceProvider serviceProvider) => Value.ToString();
            }

            public sealed class LiteralTarget
            {
                public ParsedValue Value { get; set; } = null!;
            }

            public sealed class ParsedValue
            {
                public static ParsedValue Parse(string value) => new();
            }
            """;

        var (tree, model) = CreateModel(code, csharpCode);
        var elements = tree.GetRoot().DescendantNodesAndSelf().OfType<MarkupElementSyntax>().ToArray();
        var grid = Assert.Single(elements, static element => GetElementName(element) == "Grid");

        var width = GetAttribute(grid, "Width");
        var widthOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(model.GetOperation(width));
        Assert.Equal(PropertyAccessKind.AvaloniaProperty, widthOperation.Property?.WriteKind);
        Assert.Equal("WidthProperty", widthOperation.Property?.WriteDefinition.Name);
        Assert.True(widthOperation.ValueConversion.IsImplicit);
        Assert.Equal(SpecialType.System_Int32, widthOperation.ValueConversion.SourceType?.SpecialType);
        Assert.Equal(SpecialType.System_Double, widthOperation.ValueConversion.TargetType?.SpecialType);

        var attached = GetAttribute(grid, "Column");
        var attachedOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(model.GetOperation(attached));
        var attachedProperty = Assert.IsAssignableFrom<AkburaPropertySymbol>(attachedOperation.Property);
        Assert.True(attachedProperty.IsAttachedProperty);
        Assert.Equal("ColumnProperty", attachedProperty.AttachedPropertyDefinition.Name);
        Assert.Equal("GetColumn", attachedProperty.AttachedGetterDefinition.Name);
        Assert.Equal("SetColumn", attachedProperty.AttachedSetterDefinition.Name);
        Assert.Equal("Avalonia.Controls.Control", attachedProperty.AttachedTargetType.Symbol?.ToDisplayString());
        Assert.Equal(PropertyAccessKind.AttachedAccessor, attachedProperty.ReadKind);
        Assert.Equal(PropertyAccessKind.AttachedAccessor, attachedProperty.WriteKind);
        Assert.Equal("SetColumn", attachedProperty.WriteDefinition.Name);

        var definitions = GetAttribute(grid, "ColumnDefinitions");
        var definitionsOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(model.GetOperation(definitions));
        var definitionsValue = Assert.IsType<GridDefinitionListValue>(definitionsOperation.ConvertedValue);
        Assert.Equal(2, definitionsValue.Definitions.Length);
        Assert.Equal(GridDefinitionUnitType.Star, definitionsValue.Definitions[0].Length.UnitType);
        Assert.Equal(10, definitionsValue.Definitions[1].Min);
        Assert.Equal(100, definitionsValue.Definitions[1].Max);

        var visibilityOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            model.GetOperation(GetAttribute(grid, "IsVisible")));
        Assert.Equal(true, visibilityOperation.ConvertedValue);
        var opacityOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            model.GetOperation(GetAttribute(grid, "Opacity")));
        Assert.Equal(0.5d, opacityOperation.ConvertedValue);

        var literalTarget = Assert.Single(elements, static element => GetElementName(element) == "LiteralTarget");
        var parsedLiteralOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            model.GetOperation(GetAttribute(literalTarget, "Value")));
        var parsedLiteral = Assert.IsType<MarkupLiteralValue>(parsedLiteralOperation.ConvertedValue);
        Assert.Equal(MarkupLiteralConverterKind.ParseMethod, parsedLiteral.ConverterKind);
        Assert.Equal("Parse", parsedLiteral.Converter.Name);
        Assert.Equal("Demo.ParsedValue", parsedLiteral.TargetType.Symbol?.ToDisplayString());

        var textBlocks = elements.Where(static element => GetElementName(element) == "TextBlock").ToArray();
        var bindingOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            model.GetOperation(GetAttribute(textBlocks[0], "Text")));
        var bindingExtension = Assert.IsType<MarkupExtensionValue>(bindingOperation.ConvertedValue);
        var binding = Assert.IsType<MarkupBindingValue>(bindingExtension.Binding);
        Assert.Equal(MarkupBindingKind.Compiled, binding.Kind);
        Assert.Equal("Demo.Item", binding.SourceType.Symbol?.ToDisplayString());
        Assert.Equal(
            SpecialType.System_String,
            Assert.IsAssignableFrom<ITypeSymbol>(binding.ResultType.Symbol).SpecialType);
        Assert.Collection(
            binding.PathElements,
            history =>
            {
                Assert.Equal("History", history.Symbol.Name);
                Assert.Equal("System.Collections.Generic.IReadOnlyList<Demo.Details>", history.Type.Symbol?.ToDisplayString());
            },
            indexer =>
            {
                Assert.Equal(MarkupBindingPathElementKind.Indexer, indexer.Kind);
                Assert.Equal("Item", indexer.Symbol.Symbol?.MetadataName);
                Assert.Equal("Demo.Details", indexer.Type.Symbol?.ToDisplayString());
                Assert.Equal("0", Assert.Single(indexer.Arguments));
                var argument = Assert.Single(indexer.BoundArguments);
                Assert.Equal(
                    SpecialType.System_Int32,
                    Assert.IsAssignableFrom<ITypeSymbol>(argument.Type.Symbol).SpecialType);
                Assert.True(argument.Conversion.IsIdentity);
                Assert.Equal(0, argument.ConvertedValue);
                Assert.False(argument.Operation.IsDefault);
            },
            name =>
            {
                Assert.Equal("Name", name.Symbol.Name);
                Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<ITypeSymbol>(name.Type.Symbol).SpecialType);
            });

        var extensionOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            model.GetOperation(GetAttribute(textBlocks[1], "Text")));
        var extension = Assert.IsType<MarkupExtensionValue>(extensionOperation.ConvertedValue);
        Assert.Equal("FormatExtension", extension.ExtensionType.Name);
        Assert.Equal(".ctor", extension.Constructor.Name);
        Assert.Equal(
            SpecialType.System_Int32,
            Assert.Single(Assert.IsAssignableFrom<IMethodSymbol>(extension.Constructor.Symbol).Parameters).Type.SpecialType);
        Assert.Equal("ProvideValue", extension.ProvideValueMethod.Name);
        var extensionProperty = Assert.Single(extension.Properties);
        Assert.Equal("Value", extensionProperty.Property.Name);
        Assert.True(extensionProperty.Conversion.IsIdentity);
        Assert.False(extensionProperty.Operation.IsDefault);

        var itemOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            model.GetOperation(GetAttribute(textBlocks[2], "Text")));
        var itemReference = Assert.Single(
            EnumerateCSharpOperations(itemOperation),
            static operation => operation.TargetSymbol is IMarkupItemSymbol);
        Assert.Equal("Demo.Item", Assert.IsAssignableFrom<IMarkupItemSymbol>(itemReference.TargetSymbol).Type.Symbol?.ToDisplayString());

        foreach (var attribute in new[]
        {
            width,
            attached,
            definitions,
            GetAttribute(grid, "IsVisible"),
            GetAttribute(grid, "Opacity"),
            GetAttribute(literalTarget, "Value"),
            GetAttribute(textBlocks[0], "Text"),
            GetAttribute(textBlocks[1], "Text"),
            GetAttribute(textBlocks[2], "Text"),
        })
        {
            Assert.True(
                model.GetSemanticDiagnostics(attribute).IsEmpty,
                string.Join(" | ", model.GetSemanticDiagnostics(attribute).Select(static diagnostic => diagnostic.Message)));
        }
    }

    [Fact]
    public void SemanticCoverage_RealisticDocumentHasExplicitSurfaceForEverySemanticContext()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Akbura.Hooks;
            using Demo;

            inject System.IServiceProvider services;
            param int UserId = 1;
            state int count = 0;
            command int Save(int value);

            useEffect(() => {
                var snapshot = count;
            }, [count]);

            @akcss {
                Button.card {
                    Width: 10;
                    @if(true) {
                        Opacity: 1;
                    }
                    @apply card;
                }

                Button.intercepted {
                    @intercept CardStyle;
                }

                @utilities {
                    .w-(double value) {
                        Width: value;
                    }
                }
            }

            <StackPanel>
                <Button Width={count} />
            </StackPanel>
            """;
        const string csharpCode =
            """
            namespace Demo;

            public sealed class CardStyle : Akbura.Akcss.AkcssClass
            {
                public override void Update(object control) { }
            }
            """;

        var (tree, model) = CreateModel(code, csharpCode);
        var root = tree.GetRoot();

        AssertSemanticNode<BoundComponentDeclaration>(model, root, hasSymbol: true, hasOperation: false);
        AssertSemanticNode<BoundInjectDeclaration>(model, root.Members.OfType<InjectDeclarationSyntax>().Single(), true, false);

        var parameter = root.Members.OfType<ParamDeclarationSyntax>().Single();
        AssertSemanticNode<BoundParamDeclaration>(model, parameter, true, false);
        Assert.IsType<BoundParamDefaultValue>(model.BindingSession.BindSemanticSyntax(parameter.DefaultValue!));

        var state = root.Members.OfType<StateDeclarationSyntax>().Single();
        AssertSemanticNode<BoundStateDeclaration>(model, state, true, false);
        Assert.IsType<BoundStateInitializer>(model.BindingSession.BindSemanticSyntax(state.Initializer));

        AssertSemanticNode<BoundCommandDeclaration>(model, root.Members.OfType<CommandDeclarationSyntax>().Single(), true, false);

        var effect = root.Members.OfType<CSharpStatementSyntax>().Single();
        var boundEffect = AssertSemanticNode<BoundUseHookStatement>(model, effect, true, true);
        Assert.Equal(BoundKind.UseHookInvocation, boundEffect.Invocation.Kind);
        Assert.Equal("useEffect", boundEffect.Invocation.Hook.InvocationName);
        Assert.IsAssignableFrom<IUseHookOperation>(model.GetOperation(effect));
        Assert.Contains(model.GetCSharpSymbolReferences(effect), static reference => reference.Name == "count");

        var inlineAkcss = root.Members.OfType<InlineAkcssBlockSyntax>().Single();
        AssertSemanticNode<BoundAkcssModule>(model, inlineAkcss, true, false);
        var styles = inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>().ToArray();
        Assert.Equal(2, styles.Length);
        var style = styles[0];
        var interceptedStyle = styles[1];
        AssertSemanticNode<BoundAkcssStyle>(model, style, true, false);
        AssertSemanticNode<BoundAkcssStyle>(model, interceptedStyle, true, false);
        var utility = inlineAkcss.Members
            .OfType<AkcssUtilitiesSectionSyntax>()
            .Single()
            .Utilities
            .Single();
        AssertSemanticNode<BoundAkcssUtility>(model, utility, true, false);

        var assignment = style.Members.OfType<AkcssAssignmentSyntax>().First();
        var boundAssignment = AssertSemanticNode<BoundAkcssPropertySetter>(model, assignment, true, true, bindAsOperation: true);
        var assignmentOperation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(model.GetOperation(assignment));
        Assert.Equal(boundAssignment.ValueConversion, assignmentOperation.ValueConversion);
        Assert.True(assignmentOperation.ValueConversion.IsImplicit);
        var ifDirective = style.Members.OfType<AkcssIfDirectiveSyntax>().Single();
        AssertSemanticNode<BoundAkcssIf>(model, ifDirective, false, true, bindAsOperation: true);
        AssertSemanticNode<BoundAkcssApply>(model, style.Members.OfType<AkcssApplyDirectiveSyntax>().Single(), false, true, bindAsOperation: true);
        AssertSemanticNode<BoundAkcssIntercept>(model, interceptedStyle.Members.OfType<AkcssInterceptDirectiveSyntax>().Single(), false, true, bindAsOperation: true);

        var markupRoot = root.Members.OfType<MarkupRootSyntax>().Single();
        AssertSemanticNode<BoundMarkupRoot>(model, markupRoot, false, false);
        AssertSemanticNode<BoundMarkupComponent>(model, markupRoot.Element, true, true);
        var childContent = Assert.IsType<MarkupElementContentSyntax>(Assert.Single(markupRoot.Element.Body));
        AssertSemanticNode<BoundMarkupContent>(model, childContent, false, false);
        AssertSemanticNode<BoundMarkupComponent>(model, childContent.Element, true, false);
        var markupAttribute = Assert.Single(childContent.Element.StartTag!.Attributes);
        AssertSemanticNode<BoundMarkupPropertySetter>(model, markupAttribute, true, true, bindAsOperation: true);

        var rootDiagnostics = model.GetSemanticDiagnostics(root);
        Assert.True(
            rootDiagnostics.IsEmpty,
            string.Join(" | ", rootDiagnostics.Select(static diagnostic => diagnostic.Message)));
    }

    [Fact]
    public void NegativePath_InvalidCompiledBindingPathReportsDiagnosticWithoutThrowing()
    {
        const string code =
            """
            using Avalonia.Controls;
            <StackPanel x.DataType="Demo.ViewModel">
                <TextBlock Text=${Binding Missing.Name} />
            </StackPanel>
            """;
        const string csharpCode = "namespace Demo; public sealed class ViewModel { public string Name { get; } = \"\"; }";
        var (tree, model) = CreateModel(code, AvaloniaBindingCSharpCode, csharpCode);
        var attribute = FindAttribute(tree, "Text");

        var operation = GetOperationWithoutException(model, attribute);
        var extension = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);
        Assert.Equal(MarkupBindingPathElementKind.Unknown, extension.Binding!.PathElements[0].Kind);
        AssertDiagnostic(model, attribute, ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError, "Missing");
    }

    [Fact]
    public void NegativePath_MalformedBindingPathReportsDiagnosticWithoutThrowing()
    {
        const string code =
            """
            using Avalonia.Controls;
            <StackPanel x.DataType="Demo.ViewModel">
                <TextBlock Text=${Binding Name..Length} />
            </StackPanel>
            """;
        const string csharpCode = "namespace Demo; public sealed class ViewModel { public string Name { get; } = \"\"; }";
        var (tree, model) = CreateModel(code, AvaloniaBindingCSharpCode, csharpCode);
        var attribute = FindAttribute(tree, "Text");

        GetOperationWithoutException(model, attribute);
        AssertDiagnostic(model, attribute, ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError, "empty member segment");
    }

    [Fact]
    public void NegativePath_IncompatibleTemplateItemsReportDiagnosticWithoutThrowing()
    {
        const string code =
            """
            using Avalonia.Controls;
            state int count = 1;
            <ItemsControl ItemsSource={count}>
                <ItemsControl.ItemTemplate>
                    <TextBlock Text=${Binding Name} />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        var (tree, model) = CreateModel(code, AvaloniaBindingCSharpCode);
        var attribute = FindAttribute(tree, "Text");

        GetOperationWithoutException(model, attribute);
        AssertDiagnostic(model, attribute, ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError, "IEnumerable<T>");
    }

    [Fact]
    public void NegativePath_AmbiguousMarkupExtensionReportsDiagnosticWithoutThrowing()
    {
        const string code =
            """
            using Avalonia.Controls;
            using First;
            using Second;
            <Button Content=${Value} />
            """;
        const string csharpCode =
            """
            namespace First { public sealed class ValueExtension { public object ProvideValue() => new(); } }
            namespace Second { public sealed class ValueExtension { public object ProvideValue() => new(); } }
            """;
        var (tree, model) = CreateModel(code, csharpCode);
        var attribute = FindAttribute(tree, "Content");

        GetOperationWithoutException(model, attribute);
        AssertDiagnostic(model, attribute, ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError, "ambiguous");
    }

    [Fact]
    public void NegativePath_InvalidAttachedSetterReportsDiagnosticWithoutThrowing()
    {
        const string code =
            """
            using Demo;
            <Target BadOwner.Flag={1} />
            """;
        const string csharpCode =
            """
            namespace Demo;
            public sealed class AttachedProperty<T> { }
            public sealed class Target { }
            public static class BadOwner
            {
                public static readonly AttachedProperty<int> FlagProperty = new();
                public static int GetFlag(object target) => 0;
                public static void SetFlag(object target, string value) { }
            }
            """;
        var (tree, model) = CreateModel(code, csharpCode);
        var attribute = FindAttribute(tree, "Flag");

        var operation = GetOperationWithoutException(model, attribute);
        Assert.True(operation.Property!.AttachedSetterDefinition.IsDefault);
        Assert.Equal(PropertyAccessKind.None, operation.Property.WriteKind);
        AssertDiagnostic(model, attribute, ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyAccessNotSupported, "public setter");
    }

    [Fact]
    public void NegativePath_InaccessibleMemberReportsDiagnosticWithoutThrowing()
    {
        const string code = "using Demo; <Target Secret={1} />";
        const string csharpCode = "namespace Demo; public sealed class Target { private int Secret { get; set; } }";
        var (tree, model) = CreateModel(code, csharpCode);
        var attribute = FindAttribute(tree, "Secret");

        GetOperationWithoutException(model, attribute);
        AssertDiagnostic(model, attribute, ErrorCodes.AKBURA_SEMANTIC_InaccessibleMember, "Secret");
    }

    [Fact]
    public void NegativePath_ImpossibleConversionReportsDiagnosticWithoutThrowing()
    {
        const string code = "using Demo; state string text = \"x\"; <Target Count={text} />";
        const string csharpCode = "namespace Demo; public sealed class Target { public int Count { get; set; } }";
        var (tree, model) = CreateModel(code, csharpCode);
        var attribute = FindAttribute(tree, "Count");

        var operation = GetOperationWithoutException(model, attribute);
        Assert.False(operation.ValueConversion.Exists);
        AssertDiagnostic(model, attribute, ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert, "Count");
    }

    [Fact]
    public void NegativePath_InvalidExecutableStatementReportsDiagnosticAtStatementAndRoot()
    {
        const string code =
            """
            using Akbura.Hooks;
            useEffect(() => {
                var value = missing + 1;
            });
            """;
        var (tree, model) = CreateModel(code);
        var effect = tree.GetRoot().Members.OfType<CSharpStatementSyntax>().Single();
        AssertDiagnostic(model, effect, ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError, "missing");
        Assert.Contains(
            model.GetSemanticDiagnostics(tree.GetRoot()),
            diagnostic =>
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError &&
                diagnostic.Message.Contains("missing", StringComparison.Ordinal));
    }

    private static TBound AssertSemanticNode<TBound>(
        AkburaSemanticModel model,
        AkburaSyntax syntax,
        bool hasSymbol,
        bool hasOperation,
        bool bindAsOperation = false)
        where TBound : BoundNode
    {
        TBound? bound = null;
        AkburaSymbolInfo symbolInfo = default;
        Akbura.Language.Operations.IOperation? operation = null;
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default;
        var exception = Record.Exception(() =>
        {
            symbolInfo = model.GetSymbolInfo(syntax);
            bound = Assert.IsType<TBound>(bindAsOperation
                ? model.BindingSession.BindOperationSyntax(syntax)
                : model.BindingSession.BindSemanticSyntax(syntax));
            operation = model.GetOperation(syntax);
            diagnostics = model.GetSemanticDiagnostics(syntax);
        });

        Assert.Null(exception);
        Assert.NotNull(bound);
        Assert.Equal(hasSymbol, symbolInfo.Symbol != null);
        Assert.Equal(hasOperation, operation != null);
        Assert.False(diagnostics.IsDefault);
        return bound;
    }

    private static IMarkupPropertySetterOperation GetOperationWithoutException(
        AkburaSemanticModel model,
        MarkupAttributeSyntax attribute)
    {
        IMarkupPropertySetterOperation? operation = null;
        var exception = Record.Exception(() =>
            operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(model.GetOperation(attribute)));
        Assert.Null(exception);
        Assert.NotNull(operation);
        Assert.True(operation.HasErrors);
        return operation;
    }

    private static void AssertDiagnostic(
        AkburaSemanticModel model,
        AkburaSyntax syntax,
        string code,
        string messagePart)
    {
        var diagnostics = model.GetSemanticDiagnostics(syntax);
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == code &&
            diagnostic.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase));
    }

    private static (AkburaSyntaxTree Tree, AkburaSemanticModel Model) CreateModel(
        string code,
        params string[] csharpSources)
    {
        var tree = AkburaSyntaxTree.ParseText(code, "Audit.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(csharpSources),
            [tree],
            rootNamespace: "Demo");
        return (tree, compilation.GetSemanticModel(tree));
    }

    private static CSharpCompilation CreateCSharpCompilation(params string[] sources)
    {
        return CSharpCompilation.Create(
            "SemanticAuditTests",
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))),
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MarkupAttributeSyntax FindAttribute(AkburaSyntaxTree tree, string name)
    {
        return Assert.Single(
            tree.GetRoot().DescendantNodes().OfType<MarkupAttributeSyntax>(),
            attribute => string.Equals(GetAttributeName(attribute), name, StringComparison.Ordinal));
    }

    private static MarkupAttributeSyntax GetAttribute(MarkupElementSyntax element, string name)
    {
        return Assert.Single(
            element.StartTag!.Attributes,
            attribute => string.Equals(GetAttributeName(attribute), name, StringComparison.Ordinal));
    }

    private static string GetAttributeName(MarkupAttributeSyntax attribute)
    {
        return attribute.Kind switch
        {
            Akbura.Language.Syntax.SyntaxKind.MarkupPlainAttributeSyntax =>
                ((MarkupPlainAttributeSyntax)attribute).Name.Identifier.ValueText,
            Akbura.Language.Syntax.SyntaxKind.MarkupAttachedPropertyAttributeSyntax =>
                ((MarkupAttachedPropertyAttributeSyntax)attribute).Name.Identifier.ValueText,
            Akbura.Language.Syntax.SyntaxKind.MarkupPrefixedAttributeSyntax =>
                ((MarkupPrefixedAttributeSyntax)attribute).Name.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    private static string GetElementName(MarkupElementSyntax element)
    {
        return element.StartTag?.Name.ToFullString().Trim() ?? string.Empty;
    }

    private static IEnumerable<ICSharpOperation> EnumerateCSharpOperations(
        Akbura.Language.Operations.IOperation operation)
    {
        if (operation is ICSharpOperation csharpOperation)
        {
            yield return csharpOperation;
        }

        foreach (var child in operation.Children)
        {
            foreach (var descendant in EnumerateCSharpOperations(child))
            {
                yield return descendant;
            }
        }
    }

    private const string AvaloniaBindingCSharpCode =
        """
        namespace Avalonia.Data
        {
            public abstract class BindingBase { }
            public enum BindingMode { Default, OneWay, TwoWay }
            public sealed class CompiledBindingPath { }

            public class Binding : BindingBase
            {
                public Binding() { }
                public Binding(string path) { Path = path; }
                public string Path { get; set; } = "";
                public BindingMode Mode { get; set; }
            }

            public class CompiledBinding : BindingBase
            {
                public CompiledBinding() { }
                public CompiledBinding(CompiledBindingPath path) { Path = path; }
                public CompiledBindingPath? Path { get; set; }
                public BindingMode Mode { get; set; }
            }
        }
        """;
}
