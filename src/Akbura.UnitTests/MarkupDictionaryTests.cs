using Akbura.Language;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class MarkupDictionaryTests
{
    [Theory]
    [InlineData("x.key=\"a b\"", typeof(GreenMarkupLiteralAttributeValueSyntax))]
    [InlineData("x.Key={count + 1}", typeof(GreenMarkupDynamicAttributeValueSyntax))]
    public void ExistingGrammar_RoundTripsDictionaryKey(string text, Type valueType)
    {
        using var parser = ParserHelper.MakeParser(text);
        var attribute = Assert.IsType<GreenMarkupAttachedPropertyAttributeSyntax>(parser.ParseMarkupAttributeSyntax());
        Assert.IsType(valueType, attribute.Value);
        Assert.Equal(text, attribute.ToFullString());
    }

    [Theory]
    [InlineData("\"a b\"", "{count + 1}")]
    [InlineData("{count + 1}", "\"a b\"")]
    public void IncrementalKeyFormEdit_MatchesFullParse(string oldValue, string newValue)
    {
        var oldText = $"<Border x.Key={oldValue} Width=\"12\"/>";
        var newText = $"<Border x.Key={newValue} Width=\"12\"/>";
        using var oldParser = ParserHelper.MakeParser(oldText);
        var oldGreen = oldParser.ParseCompilationUnit();
        var oldTree = (AkburaDocumentSyntax)oldGreen.CreateRed();
        var change = new TextChangeRange(new TextSpan(oldText.IndexOf(oldValue, StringComparison.Ordinal),
            oldValue.Length), newValue.Length);
        using var incremental = ParserHelper.MakeIncrementalParser(newText, oldTree, [change]);
        using var full = ParserHelper.MakeParser(newText);
        var incrementalTree = incremental.ParseCompilationUnit();
        var fullTree = full.ParseCompilationUnit();
        Assert.Equal(fullTree.ToFullString(), incrementalTree.ToFullString());
        Assert.Equal(newText, incrementalTree.ToFullString());
        Assert.Equal(fullTree.ContainsDiagnostics, incrementalTree.ContainsDiagnostics);
    }

    [Theory]
    [InlineData("<Border x.key=\"unfinished")]
    [InlineData("<Border x.Key={count +")]
    [InlineData("<Border x.k")]
    public void IncompleteKey_DoesNotCrashOrLoseText(string text)
    {
        using var parser = ParserHelper.MakeParser(text);
        Assert.Equal(text, parser.ParseCompilationUnit().ToFullString());
    }

    [Theory]
    [InlineData("System.Collections.Generic.IDictionary<int, string>", "System.Int32", "System.String", true)]
    [InlineData("System.Collections.Generic.Dictionary<int, string>", "System.Int32", "System.String", true)]
    [InlineData("System.Collections.IDictionary", "System.Object", "System.Object", false)]
    [InlineData("System.Collections.Hashtable", "System.Object", "System.Object", false)]
    public void DictionaryShape_PreservesExactWriteContract(string typeName, string keyName,
        string valueName, bool generic)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; <Border />",
            $"namespace Demo; public class Holder {{ public {typeName} Value => null!; }}");
        var holder = fixture.CSharpCompilation.GetTypeByMetadataName("Demo.Holder")!;
        var type = ((Microsoft.CodeAnalysis.IPropertySymbol)Assert.Single(holder.GetMembers("Value"))).Type;
        var shape = MarkupDictionaryShape.Create(type, fixture.CSharpCompilation);
        Assert.True(shape.IsDictionary);
        Assert.False(shape.IsAmbiguous);
        Assert.False(shape.IsReadOnlyOnly);
        Assert.Equal(keyName, shape.KeyType!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.None)));
        Assert.Equal(valueName, shape.ValueType!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat
            .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.None)));
        Assert.Equal(generic, shape.IsGeneric);
    }

    [Fact]
    public void DictionaryShape_PreservesNullableKeyAndValueAnnotations()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; <Border />",
            "namespace Demo; public class Holder { public System.Collections.Generic.IDictionary<string?, object?> Value => null!; }");
        var holder = fixture.CSharpCompilation.GetTypeByMetadataName("Demo.Holder")!;
        var type = ((Microsoft.CodeAnalysis.IPropertySymbol)Assert.Single(holder.GetMembers("Value"))).Type;
        var shape = MarkupDictionaryShape.Create(type, fixture.CSharpCompilation);
        Assert.Equal(NullableAnnotation.Annotated, shape.KeyType!.NullableAnnotation);
        Assert.Equal(NullableAnnotation.Annotated, shape.ValueType!.NullableAnnotation);
    }

    [Fact]
    public void ReadOnlyContract_IsNotAWriteSink()
    {
        var fixture = Create("<ReadOnlyHost><SolidColorBrush x.key={1} /></ReadOnlyHost>");
        var model = fixture.GetElementSymbol(fixture.GetRootElement()).ContentModel;
        Assert.Equal(MarkupContentKind.Dictionary, model.Kind);
        Assert.True(model.DictionaryShape.IsReadOnlyOnly);
        Assert.Contains(Diagnostics(fixture), static diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryReadOnlyTarget);
    }

    [Fact]
    public void MultipleMutableContracts_AreAmbiguousInsteadOfSelectingFirst()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; <Border />",
            """
            using System.Collections.Generic;
            namespace Demo;
            public interface Ambiguous : IDictionary<int, string>, IDictionary<long, string> { }
            """);
        var type = fixture.CSharpCompilation.GetTypeByMetadataName("Demo.Ambiguous")!;
        var shape = MarkupDictionaryShape.Create(type, fixture.CSharpCompilation);
        Assert.True(shape.IsDictionary);
        Assert.True(shape.IsAmbiguous);
        Assert.Null(shape.ContractType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComponentParameterDictionary_UsesParameterWriteContract(bool propertyElement)
    {
        const string host =
            """
            using System.Collections.Generic;
            using Avalonia.Media;
            param IDictionary<int, SolidColorBrush> Content;
            """;
        var source = propertyElement
            ? """
              using Avalonia.Media;
              <DictionaryHost>
                  <DictionaryHost.Content><SolidColorBrush x.key={1} /></DictionaryHost.Content>
              </DictionaryHost>
              """
            : """
              using Avalonia.Media;
              <DictionaryHost><SolidColorBrush x.key={1} /></DictionaryHost>
              """;
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(source);
        var hostTree = AkburaSyntaxTree.ParseText(host, "DictionaryHost.akbura");
        var compilation = new AkburaCompilation(baseFixture.CSharpCompilation,
            [baseFixture.ComponentTree, hostTree], rootNamespace: "Demo");
        var model = compilation.GetSemanticModel(baseFixture.ComponentTree);
        Assert.Empty(model.GetSemanticDiagnostics(baseFixture.ComponentTree.GetRoot()));
        var root = baseFixture.GetRootElement();
        var component = Assert.IsAssignableFrom<IMarkupComponentSymbol>(model.GetSymbolInfo(root).Symbol);
        Assert.Equal(MarkupContentKind.Dictionary, component.ContentModel.Kind);
        Assert.Equal("Content", component.ContentModel.ContentParameter!.Name);
        Assert.Equal("Int32", component.ContentModel.DictionaryShape.KeyType!.Name);
        Assert.Equal("SolidColorBrush", component.ContentModel.DictionaryShape.ValueType!.Name);
    }

    [Fact]
    public void EmptyComponentDictionaryContent_DoesNotRequireAnExternalContentAssignment()
    {
        const string host =
            """
            using System.Collections.Generic;
            using Avalonia.Media;
            param IDictionary<int, SolidColorBrush> Content;
            param string Title;
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture("<DictionaryHost Title=\"configured\" />");
        var hostTree = AkburaSyntaxTree.ParseText(host, "DictionaryHost.akbura");
        var compilation = new AkburaCompilation(fixture.CSharpCompilation,
            [fixture.ComponentTree, hostTree], rootNamespace: "Demo");
        Assert.Empty(compilation.GetSemanticModel(fixture.ComponentTree)
            .GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Theory]
    [InlineData("<IntHost><SolidColorBrush x.key={1} /></IntHost>")]
    [InlineData("<SelfDictionary><SolidColorBrush x.key={1} /></SelfDictionary>")]
    [InlineData("<IntHost><IntHost.Items><SolidColorBrush x.Key={1} /></IntHost.Items></IntHost>")]
    public void DictionarySink_UsesValueType_NotKeyValuePair(string markup)
    {
        var fixture = Create(markup);
        Assert.Empty(Diagnostics(fixture));
        var root = fixture.GetRootElement();
        var parent = root.Body.OfType<MarkupElementContentSyntax>().Single().Element;
        var child = parent.StartTag!.Name.ToFullString().Trim().Contains('.', StringComparison.Ordinal)
            ? parent.Body.OfType<MarkupElementContentSyntax>().Single().Element : parent;
        Assert.True(fixture.SemanticModel.TryGetMarkupDictionaryContext(child, out var context));
        Assert.Equal("SolidColorBrush", context.AllowedChildType.Name);
        Assert.Equal("Int32", context.DictionaryShape.KeyType!.Name);
        var key = Assert.Single(fixture.GetElementSymbol(child).AttributeOperations
            .OfType<IMarkupDictionaryKeyOperation>());
        Assert.False(key.HasErrors);
        Assert.NotNull(key.ValueOperationTree);
        Assert.Same(key, key.ValueOperationTree!.Parent);
        var visitor = new KeyVisitor();
        key.Accept(visitor);
        Assert.Equal(1, visitor.Keys);
        Assert.Equal(1, key.Accept(new KeyVisitorWithParameter(), 1));
    }

    [Theory]
    [InlineData("<IntHost><SolidColorBrush /></IntHost>", "AKBURA_SEMANTIC_MarkupDictionaryKeyRequired")]
    [InlineData("<IntHost><SolidColorBrush x.key=\"42\" /></IntHost>", "AKBURA_SEMANTIC_MarkupDictionaryKeyTypeMismatch")]
    [InlineData("<IntHost><SolidColorBrush x.key={1} x.Key={2} /></IntHost>", "AKBURA_SEMANTIC_MarkupDictionaryKeyDuplicateDirective")]
    [InlineData("<IntHost><Border x.key={1} /></IntHost>", "AKBURA_SEMANTIC_MarkupDictionaryValueTypeMismatch")]
    [InlineData("<StackPanel><Border x.key=\"A\" /></StackPanel>", "AKBURA_SEMANTIC_MarkupDictionaryKeyOutsideDictionary")]
    [InlineData("<IntHost><SolidColorBrush x.key={1} /><SolidColorBrush x.Key={1} /></IntHost>", "AKBURA_SEMANTIC_MarkupDictionaryDuplicateConstantKey")]
    public void InvalidDictionaryDeclaration_ReportsCanonicalSemanticDiagnostic(string markup, string code)
    {
        Assert.Contains(Diagnostics(Create(markup)), diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public void QuotedObjectKeys_PreserveWhitespaceAndCase()
    {
        var fixture = Create("<ObjectHost><SolidColorBrush x.key=\" Ab C \" /></ObjectHost>");
        Assert.Empty(Diagnostics(fixture));
        var child = fixture.GetChildElements().Single();
        var key = Assert.Single(fixture.GetElementSymbol(child).AttributeOperations
            .OfType<IMarkupDictionaryKeyOperation>());
        Assert.Equal(" Ab C ", key.LiteralValue);
        Assert.Equal(" Ab C ", key.ConstantValue);
    }

    [Fact]
    public void BoxedObjectKeyConstants_AreNotUniversallyDuplicateUnderReferenceComparers()
    {
        var fixture = Create("<ObjectHost><SolidColorBrush x.key={42} /><SolidColorBrush x.Key={42} /></ObjectHost>");
        Assert.DoesNotContain(Diagnostics(fixture), static diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryDuplicateConstantKey);
    }

    [Fact]
    public void FloatingKeyConstants_LeaveSignedZeroEqualityToTheNativeComparer()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Media; using Demo; <FloatingHost>" +
            "<SolidColorBrush x.key={0d} /><SolidColorBrush x.Key={-0d} /></FloatingHost>",
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;
            using Avalonia.Metadata;
            namespace Demo;
            public class FloatingHost : Border
            {
                [Content] public IDictionary<double, SolidColorBrush> Entries { get; } =
                    new Dictionary<double, SolidColorBrush>();
            }
            """);
        Assert.DoesNotContain(Diagnostics(fixture), static diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryDuplicateConstantKey);
    }

    [Theory]
    [InlineData("byte", "0")]
    [InlineData("short", "1")]
    [InlineData("int?", "null")]
    public void DynamicKey_UsesOrdinaryContextualCSharpConversion(string keyType, string expression)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            "using Avalonia.Media; using Demo; <TypedHost><SolidColorBrush x.key={" + expression +
            "} /></TypedHost>",
            "using System.Collections.Generic; using Avalonia.Controls; using Avalonia.Media;" +
            "using Avalonia.Metadata; namespace Demo; public class TypedHost : Border {" +
            "[Content] public IDictionary<" + keyType + ", SolidColorBrush> Entries => null!; }");
        Assert.Empty(Diagnostics(fixture));
    }

    [Theory]
    [InlineData("<IntHost><SolidColorBrush x.key={1} Color=\"Red\" /></IntHost>")]
    [InlineData("<SelfDictionary><SolidColorBrush x.key={1} Color=\"Red\" /></SelfDictionary>")]
    public void DictionaryChildBinding_RetainsItsPropertyAttributeOperation(string markup)
    {
        var fixture = Create(markup);
        Assert.Empty(Diagnostics(fixture));
        var child = fixture.GetChildElements().Single();
        var operations = fixture.GetElementSymbol(child).AttributeOperations;
        Assert.Single(operations.OfType<IMarkupDictionaryKeyOperation>());
        var color = Assert.Single(operations.OfType<IMarkupPropertySetterOperation>(),
            static operation => operation.Property?.Name == "Color");
        Assert.False(color.HasErrors);
        Assert.Equal("Red", color.LiteralValue);
    }

    [Fact]
    public void KeyRewriter_PreservesBoundIdentityAndDiagnosticChildren()
    {
        var fixture = Create("<IntHost><SolidColorBrush x.key={missing} /></IntHost>");
        var child = fixture.GetChildElements().Single();
        var attribute = child.StartTag!.Attributes.Single();
        var bound = Assert.IsType<BoundMarkupDictionaryKey>(
            fixture.SemanticModel.BindingSession.BindOperationSyntax(attribute));
        Assert.True(bound.HasErrors);
        Assert.Same(bound, new BoundTreeRewriter().Visit(bound));
        Assert.Contains(Diagnostics(fixture), static diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError &&
            diagnostic.Message.Contains("missing", StringComparison.Ordinal));
        Assert.DoesNotContain(Diagnostics(fixture), static diagnostic =>
            diagnostic.Code is ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryKeyRequired or
                ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryKeyTypeMismatch);
    }

    private static AkcssActivatorPlannerTests.PlannerFixture Create(string markup) =>
        AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; using Avalonia.Media; using Demo; " + markup,
            HostSource);

    private static IEnumerable<AkburaSemanticDiagnostic> Diagnostics(AkcssActivatorPlannerTests.PlannerFixture fixture) =>
        fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());

    private sealed class KeyVisitor : OperationVisitor
    {
        public int Keys { get; private set; }
        public override void VisitMarkupDictionaryKey(IMarkupDictionaryKeyOperation operation) => Keys++;
    }

    private sealed class KeyVisitorWithParameter : OperationVisitor<int, int>
    {
        public override int VisitMarkupDictionaryKey(IMarkupDictionaryKeyOperation operation, int parameter) => parameter;
    }

    private const string HostSource =
        """
        using System.Collections.Generic;
        using Avalonia.Controls;
        using Avalonia.Media;
        using Avalonia.Metadata;
        namespace Demo;
        public class IntHost : Border
        {
            [Content] public IDictionary<int, SolidColorBrush> Items { get; } = new Dictionary<int, SolidColorBrush>();
        }
        public class ObjectHost : Border
        {
            [Content] public IDictionary<object, object> Items { get; } = new Dictionary<object, object>();
        }
        public class ReadOnlyHost : Border
        {
            [Content] public IReadOnlyDictionary<int, SolidColorBrush> Items { get; } = new Dictionary<int, SolidColorBrush>();
        }
        public class SelfDictionary : Dictionary<int, SolidColorBrush> { }
        """;
}
