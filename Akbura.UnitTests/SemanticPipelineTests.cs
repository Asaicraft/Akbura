using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.IO;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;

namespace Akbura.UnitTests;

public class SemanticPipelineTests
{
    private const string UserHookCSharpCode =
        "using Akbura.CompilerAnotations;\n" +
        "\n" +
        "namespace Hooks;\n" +
        "\n" +
        "[UserHook]\n" +
        "public struct UseNameHook\n" +
        "{\n" +
        "    public string UseHook<T>(object component, T state)\n" +
        "    {\n" +
        "        return \"name\";\n" +
        "    }\n" +
        "}";

    [Fact]
    public void SyntaxTree_ParseText_PreservesRootFullWidth()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var root = syntaxTree.GetRoot();

        Assert.Equal(code.Length, root.FullWidth);
        Assert.Equal(code, root.ToFullString());
    }

    [Fact]
    public void SemanticModel_AkburaComponentSymbol_CollectsTopLevelAndConditionalMarkup()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state bool isLoaded = true;\n" +
            "\n" +
            "if(!isLoaded)\n" +
            "{\n" +
            "    <TextBlock Text=\"Loading...\"/>\n" +
            "}\n" +
            "\n" +
            "<TextBlock Text=\"Loaded\"/>";
        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Views/Counter.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [syntaxTree],
            rootNamespace: "Demo");
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(syntaxTree.GetRoot());

        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaSymbolKind.AkburaComponent, symbol.Kind);
        Assert.Equal("Counter", symbol.Name);
        Assert.Equal("Demo.Views.Counter", symbol.MetadataName);
        Assert.Equal("Demo.Views", symbol.NamespaceName);
        Assert.Equal(2, symbol.MarkupRoots.Length);
        Assert.All(symbol.MarkupRoots, markupRoot => Assert.Equal("TextBlock", markupRoot.Name));
        Assert.Single(symbol.States);
        Assert.Equal("isLoaded", symbol.States[0].Name);
    }

    [Fact]
    public void SemanticModel_AkburaComponentSymbol_ResolvesDefaultNamespacePartialType()
    {
        const string code =
            "inject ILogger<Counter> logger;\n" +
            "\n" +
            "<Button />";
        const string csharpCode =
            "namespace RootNamespace.MyNamespace;\n" +
            "\n" +
            "public interface ILogger<T> { }\n" +
            "\n" +
            "public partial class Counter\n" +
            "{\n" +
            "    public void First() { }\n" +
            "}\n" +
            "\n" +
            "public partial class Counter\n" +
            "{\n" +
            "    public void Second() { }\n" +
            "}";
        var syntaxTree = AkburaSyntaxTree.ParseText(code, "MyNamespace/Counter.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(csharpCode),
            [syntaxTree],
            rootNamespace: "RootNamespace");
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(syntaxTree.GetRoot());

        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(symbolInfo.Symbol);
        var partialType = Assert.Single(symbol.PartialTypes);
        Assert.Equal("RootNamespace.MyNamespace.Counter", partialType.ToDisplayString());
        Assert.Equal(2, partialType.DeclaringSyntaxReferences.Length);
        Assert.True(SymbolEqualityComparer.Default.Equals(partialType, symbol.CSharpDefinition.Symbol));

        var inject = Assert.Single(symbol.InjectedServices);
        Assert.Contains("RootNamespace.MyNamespace.Counter", inject.Type.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_ComponentWithoutCSharpPart_InheritsAkburaControlMembers()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            Width = count * 50;

            <TextBlock Text={count} />
            """;
        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(root).Symbol);
        var statement = Assert.Single(root.Members.OfType<CSharpStatementSyntax>());

        Assert.Empty(component.PartialTypes);
        Assert.False(component.HasExplicitBaseType);
        Assert.Equal("Akbura.AkburaControl", component.BaseType.Symbol?.ToDisplayString());
        Assert.DoesNotContain(
            semanticModel.GetSemanticDiagnostics(statement),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError);

        var references = semanticModel.GetCSharpSymbolReferences(statement);
        var width = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(
            Assert.Single(references, reference => reference.Name == "Width").CSharpDefinition.Symbol);
        Assert.Equal("Width", width.Name);
        Assert.IsAssignableFrom<IStateSymbol>(
            Assert.Single(references, reference => reference.Name == "count").AkburaSymbol);
    }

    [Fact]
    public void SemanticModel_ComponentPartialClassWithoutBase_InheritsAkburaControlAndBindsPrivateMembers()
    {
        const string code =
            """
            state int count = a;

            Width = count * a;
            """;
        const string csharpCode =
            """
            partial class Counter
            {
                private int a = 34;
            }
            """;
        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var root = syntaxTree.GetRoot();
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(root).Symbol);
        var state = Assert.Single(root.Members.OfType<StateDeclarationSyntax>());
        var statement = Assert.Single(root.Members.OfType<CSharpStatementSyntax>());

        Assert.False(component.HasExplicitBaseType);
        Assert.Equal("Akbura.AkburaControl", component.BaseType.Symbol?.ToDisplayString());
        Assert.DoesNotContain(
            semanticModel.GetSemanticDiagnostics(state),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError);
        Assert.DoesNotContain(
            semanticModel.GetSemanticDiagnostics(statement),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError);

        var references = semanticModel.GetCSharpSymbolReferences(statement);
        var field = Assert.IsAssignableFrom<IFieldSymbol>(
            Assert.Single(references, reference => reference.Name == "a").CSharpDefinition.Symbol);
        Assert.Equal(Accessibility.Private, field.DeclaredAccessibility);
        Assert.Equal("Counter", field.ContainingType.Name);
        var width = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(
            Assert.Single(references, reference => reference.Name == "Width").CSharpDefinition.Symbol);
        Assert.Equal("Width", width.Name);
    }

    [Fact]
    public void SemanticModel_ComponentPartialClassWithAkburaControlBase_UsesDeclaredBaseType()
    {
        const string code = "Width = a;";
        const string csharpCode =
            """
            class CounterBase : Akbura.AkburaControl
            {
                protected override Avalonia.Controls.Control Update() => null!;
            }

            partial class Counter : CounterBase
            {
                private int a = 34;
            }
            """;
        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var root = syntaxTree.GetRoot();
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(root).Symbol);
        var statement = Assert.Single(root.Members.OfType<CSharpStatementSyntax>());

        Assert.True(component.HasExplicitBaseType);
        Assert.Equal("CounterBase", component.BaseType.Name);
        Assert.DoesNotContain(
            semanticModel.GetSemanticDiagnostics(root),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_ComponentBaseTypeInvalid);
        Assert.DoesNotContain(
            semanticModel.GetSemanticDiagnostics(statement),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError);

        var field = Assert.IsAssignableFrom<IFieldSymbol>(
            Assert.Single(
                semanticModel.GetCSharpSymbolReferences(statement),
                reference => reference.Name == "a").CSharpDefinition.Symbol);
        Assert.Equal(Accessibility.Private, field.DeclaredAccessibility);
    }

    [Fact]
    public void SemanticModel_ComponentPartialClassWithInvalidBase_ProducesDiagnostic()
    {
        const string csharpCode =
            """
            class PlainBase
            {
            }

            partial class Counter : PlainBase
            {
            }
            """;
        var syntaxTree = AkburaSyntaxTree.ParseText("state int count = 0;", "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var root = syntaxTree.GetRoot();
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(root).Symbol);

        Assert.True(component.HasExplicitBaseType);
        Assert.Equal("PlainBase", component.BaseType.Name);
        var diagnostic = Assert.Single(
            semanticModel.GetSemanticDiagnostics(root),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_ComponentBaseTypeInvalid);
        Assert.Contains("Counter", diagnostic.Message);
        Assert.Contains("PlainBase", diagnostic.Message);
        Assert.Contains("AkburaControl", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromRealAvaloniaAssembly()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.IsAssignableFrom<IMarkupComponentSymbol>(symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal("Button", symbol.Name);

        var avaloniaButton = semanticModel.Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Button");
        Assert.NotNull(avaloniaButton);
        Assert.True(SymbolEqualityComparer.Default.Equals(avaloniaButton, symbol.CSharpDefinition.Symbol));

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(element);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_ThroughUsingAlias()
    {
        const string code =
            "using Btn = Avalonia.Controls.Button;\n" +
            "\n" +
            "<Btn />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal("Btn", symbol.Name);
        AssertResolvedAvaloniaButton(semanticModel, symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromQualifiedComponentName()
    {
        const string code = "<Avalonia.Controls.Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal("Avalonia.Controls.Button", symbol.Name);
        AssertResolvedAvaloniaButton(semanticModel, symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromGlobalAliasQualifiedComponentName()
    {
        const string code = "<global::Avalonia.Controls.Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal("global::Avalonia.Controls.Button", symbol.Name);
        AssertResolvedAvaloniaButton(semanticModel, symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromExternAliasQualifiedComponentName()
    {
        const string code = "<av::Avalonia.Controls.Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CSharpCompilation.Create(
                assemblyName: "TestAssembly",
                references: CreateExternAliasAvaloniaReferences("av")));
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal("av::Avalonia.Controls.Button", symbol.Name);
        Assert.Equal("Button", symbol.CSharpDefinition.Name);
        Assert.Equal("Avalonia.Controls.Button", symbol.CSharpDefinition.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromGenericComponentName()
    {
        const string code =
            "using System.Collections.Generic;\n" +
            "\n" +
            "<List{string} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        var componentType = Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.CSharpDefinition.Symbol);
        Assert.Equal("List{string}", symbol.Name);
        Assert.Equal("List", componentType.Name);
        Assert.True(componentType.IsGenericType);
        Assert.Equal(SpecialType.System_String, componentType.TypeArguments.Single().SpecialType);
    }

    [Fact]
    public void SemanticModel_AmbiguousMarkupComponentName_ReturnsCandidateSymbols()
    {
        const string code =
            "using First;\n" +
            "using Second;\n" +
            "\n" +
            "<Clash />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "namespace First { public class Clash { } }\n" +
                "namespace Second { public class Clash { } }"));
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        Assert.Null(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.Ambiguous, symbolInfo.CandidateReason);
        Assert.Equal(2, symbolInfo.CandidateSymbols.Length);
        Assert.All(symbolInfo.CandidateSymbols, symbol =>
        {
            var component = Assert.IsType<MarkupComponentSymbol>(symbol);
            Assert.Equal("Clash", component.Name);
            Assert.Equal("Clash", component.CSharpDefinition.Name);
        });
    }

    [Fact]
    public void SemanticModel_ButtonContent_AllowsTextChild()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button>Hello !</Button>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);
        var diagnostics = semanticModel.GetSemanticDiagnostics(element);

        Assert.True(diagnostics.IsEmpty);
        Assert.Equal("Content", symbol.ContentModel.ContentProperty.Name);
        Assert.Equal(SpecialType.System_Object, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.ContentModel.AllowedChildType.Symbol).SpecialType);
        Assert.True(symbol.ContentModel.AllowsText);
        var child = Assert.Single(symbol.Children);
        Assert.Equal(MarkupChildKind.Text, child.Kind);
        Assert.Equal("Hello !", child.Text);
    }

    [Fact]
    public void SemanticModel_ButtonMixedContent_CreatesContentOperation()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            <Button Click={count++}>Count is {count}</Button>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var clickAttribute = element.StartTag!.Attributes.Single();

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);
        var contentOperation = Assert.IsAssignableFrom<IMarkupContentOperation>(
            semanticModel.GetOperation(element));
        var clickOperation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(clickAttribute));

        Assert.True(semanticModel.GetSemanticDiagnostics(element).IsEmpty);
        Assert.Equal("Content", symbol.ContentModel.ContentProperty.Name);
        Assert.Equal("Content", contentOperation.Property?.Name);
        Assert.True(contentOperation.IsSynthesizedString);
        Assert.Equal(SpecialType.System_String,
            Assert.IsAssignableFrom<INamedTypeSymbol>(contentOperation.ValueType.Symbol).SpecialType);
        Assert.Equal(2, contentOperation.Content.Length);
        Assert.Equal(MarkupChildKind.Text, contentOperation.Content[0].Kind);
        Assert.Equal(MarkupChildKind.Expression, contentOperation.Content[1].Kind);
        Assert.False(contentOperation.HasErrors);
        Assert.Same(contentOperation.Property, contentOperation.TargetSymbol);
        AssertCSharpRootChild(contentOperation, contentOperation.ValueOperationTree);
        Assert.Contains(EnumerateCSharpOperations(contentOperation),
            operation => operation.TargetSymbol is IStateSymbol { Name: "count" });
        Assert.Equal("Click", clickOperation.Event.Name);
    }

    [Fact]
    public void SemanticModel_TextBlockMixedContent_UsesTextProperty()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            <TextBlock>Count is {count}</TextBlock>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);
        var contentOperation = Assert.IsAssignableFrom<IMarkupContentOperation>(
            semanticModel.GetOperation(element));

        Assert.True(semanticModel.GetSemanticDiagnostics(element).IsEmpty);
        Assert.Equal("Text", symbol.ContentModel.ContentProperty.Name);
        Assert.Equal("Text", contentOperation.Property?.Name);
        Assert.True(contentOperation.IsSynthesizedString);
        Assert.Equal(SpecialType.System_String,
            Assert.IsAssignableFrom<INamedTypeSymbol>(contentOperation.ValueType.Symbol).SpecialType);
        Assert.False(contentOperation.HasErrors);
        AssertCSharpRootChild(contentOperation, contentOperation.ValueOperationTree);
        Assert.Contains(EnumerateCSharpOperations(contentOperation),
            operation => operation.TargetSymbol is IStateSymbol { Name: "count" });
    }

    [Fact]
    public void SemanticModel_TextBlockRunChild_UsesInlinesContentModel()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Avalonia.Controls.Documents;

            <TextBlock>
                <Run Text="Hello"/>
            </TextBlock>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);

        Assert.True(semanticModel.GetSemanticDiagnostics(element).IsEmpty);
        Assert.Equal("Inlines", symbol.ContentModel.ContentProperty.Name);
        Assert.Null(semanticModel.GetOperation(element));
    }

    [Fact]
    public void SemanticModel_BorderMixedContent_RejectsStringContentOperation()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            <Border>Count is {count}</Border>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var operation = Assert.IsAssignableFrom<IMarkupContentOperation>(
            semanticModel.GetOperation(element));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(element));

        Assert.Equal("Child", operation.Property?.Name);
        Assert.True(operation.IsSynthesizedString);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild, diagnostic.Code);
        Assert.Contains("string", diagnostic.Message);
        Assert.Contains("Avalonia.Controls.Control", diagnostic.Message);
        AssertCSharpRootChild(operation, operation.ValueOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation),
            csharpOperation => csharpOperation.TargetSymbol is IStateSymbol { Name: "count" });
    }

    [Fact]
    public void SemanticModel_BorderExpressionContent_ChecksExpressionType()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            <Border>{count}</Border>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var operation = Assert.IsAssignableFrom<IMarkupContentOperation>(
            semanticModel.GetOperation(element));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(element));

        Assert.Equal("Child", operation.Property?.Name);
        Assert.False(operation.IsSynthesizedString);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild, diagnostic.Code);
        Assert.Contains("int", diagnostic.Message);
        Assert.Contains("Avalonia.Controls.Control", diagnostic.Message);
        AssertCSharpRootChild(operation, operation.ValueOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation),
            csharpOperation => csharpOperation.TargetSymbol is IStateSymbol { Name: "count" });
    }

    [Fact]
    public void SemanticModel_BorderContent_RejectsTextChild()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Border>Hello world!</Border>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(element));

        Assert.Equal("Child", symbol.ContentModel.ContentProperty.Name);
        Assert.Equal("Control", symbol.ContentModel.AllowedChildType.Name);
        Assert.False(symbol.ContentModel.AllowsText);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild, diagnostic.Code);
        Assert.Contains("string", diagnostic.Message);
        Assert.Contains("Avalonia.Controls.Control", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_BorderContent_AllowsControlChild()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Border><TextBlock /></Border>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);

        Assert.True(semanticModel.GetSemanticDiagnostics(element).IsEmpty);
        var child = Assert.Single(symbol.Children);
        Assert.Equal(MarkupChildKind.Element, child.Kind);
        Assert.NotNull(child.ComponentSymbol);
        Assert.Equal("TextBlock", child.ComponentSymbol!.Name);
        Assert.Equal("Control", symbol.ContentModel.AllowedChildType.Name);
    }

    [Fact]
    public void SemanticModel_NonAvaloniaList_AllowsElementChildrenOfItemType()
    {
        const string code =
            "using System.Collections.Generic;\n" +
            "\n" +
            "<List{MyObject}><MyObject/><MyObject/></List>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation("public class MyObject { }"));
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);

        Assert.True(semanticModel.GetSemanticDiagnostics(element).IsEmpty);
        Assert.True(symbol.ContentModel.IsCollection);
        Assert.True(symbol.ContentModel.ContentProperty.IsDefault);
        Assert.Equal("MyObject", symbol.ContentModel.AllowedChildType.Name);
        Assert.Equal(2, symbol.Children.Length);
        Assert.All(symbol.Children, child =>
        {
            Assert.Equal(MarkupChildKind.Element, child.Kind);
            Assert.Equal("MyObject", child.ComponentSymbol?.Name);
        });
    }

    [Fact]
    public void SemanticModel_NonAvaloniaList_RejectsElementChildOfDifferentType()
    {
        const string code =
            "using System.Collections.Generic;\n" +
            "\n" +
            "<List{MyObject}><OtherObject/></List>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public class MyObject { }\n" +
                "public class OtherObject { }"));
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(element));

        Assert.Equal("MyObject", symbol.ContentModel.AllowedChildType.Name);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild, diagnostic.Code);
        Assert.Contains("OtherObject", diagnostic.Message);
        Assert.Contains("MyObject", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ResolvesStateSymbol()
    {
        const string code =
            "state bool isOpen = false;\n" +
            "\n" +
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var state = Assert.IsType<StateDeclarationSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is StateDeclarationSyntax));

        var symbolInfo = semanticModel.GetSymbolInfo(state);

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal(AkburaSymbolKind.State, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("isOpen", symbol.Name);
        Assert.True(symbol.HasExplicitType);
        Assert.False(symbol.IsBindable);
        Assert.Equal(StateBindingKind.None, symbol.BindingKind);
        Assert.False(symbol.Type.IsDefault);
        Assert.Equal("Boolean", symbol.Type.Name);
        Assert.False(symbol.InitializerType.IsDefault);
        Assert.Equal("Boolean", symbol.InitializerType.Name);
        Assert.True(SymbolEqualityComparer.Default.Equals(
            semanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean),
            symbol.Type.Symbol));
        Assert.True(SymbolEqualityComparer.Default.Equals(
            semanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean),
            symbol.InitializerType.Symbol));
        Assert.Same(state, symbol.DeclarationSyntax);
        Assert.Same(state.Initializer, symbol.InitializerSyntax);
        Assert.Same(state.Initializer.Expression, symbol.InitializerExpression);
        Assert.True(symbol.CSharpDefinition.IsDefault);
        Assert.Equal("state Boolean isOpen", symbol.ToDisplayString());

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(state);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesBindableImplicitStateSymbol()
    {
        const string code = "state isBusy = bind true;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var state = Assert.IsType<StateDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbolInfo = semanticModel.GetSymbolInfo(state);

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.Equal("isBusy", symbol.Name);
        Assert.False(symbol.HasExplicitType);
        Assert.True(symbol.IsBindable);
        Assert.Equal(StateBindingKind.Bind, symbol.BindingKind);
        Assert.False(symbol.Type.IsDefault);
        Assert.False(symbol.InitializerType.IsDefault);
        Assert.Equal("Boolean", symbol.Type.Name);
        Assert.Equal("Boolean", symbol.InitializerType.Name);
        Assert.True(SymbolEqualityComparer.Default.Equals(
            semanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean),
            symbol.Type.Symbol));
        Assert.Same(state.Initializer, symbol.InitializerSyntax);
        Assert.Same(state.Initializer.Expression, symbol.InitializerExpression);
        Assert.Equal("state Boolean isBusy", symbol.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_StateInitializerUserHook_ResolvesCSharpHookSymbol()
    {
        const string code =
            "using Hooks;\n" +
            "\n" +
            "state string query = \"\";\n" +
            "state string name = useName(query);";
        const string csharpCode =
            "using Akbura.CompilerAnotations;\n" +
            "\n" +
            "namespace Hooks;\n" +
            "\n" +
            "[UserHook]\n" +
            "public struct UseNameHook\n" +
            "{\n" +
            "    public string UseHook<T>(object component, T state)\n" +
            "    {\n" +
            "        return \"name\";\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var state = GetStateDeclaration(syntaxTree, "name");

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(semanticModel.GetSymbolInfo(state).Symbol);
        var hook = Assert.IsAssignableFrom<IUserHookSymbol>(symbol.UserHook);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(syntaxTree.GetRoot()).Symbol);

        Assert.Equal(AkburaSymbolKind.UserHook, hook.Kind);
        Assert.Equal("useName", hook.InvocationName);
        Assert.Equal("useName", hook.Name);
        Assert.Equal("UseNameHook", hook.HookType.Name);
        Assert.Equal("UseHook", hook.UseHookMethod.Name);
        Assert.Equal("String", hook.ReturnType.Name);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Same(hook, component.States.Single(stateSymbol => stateSymbol.Name == "name").UserHook);
    }

    [Fact]
    public void SemanticModel_StateInitializerUserHook_RequiresUsePrefixedHookType()
    {
        const string code =
            "using Hooks;\n" +
            "\n" +
            "state string name = useName();";
        const string csharpCode =
            "using Akbura.CompilerAnotations;\n" +
            "\n" +
            "namespace Hooks;\n" +
            "\n" +
            "[UserHook]\n" +
            "public struct NameHook\n" +
            "{\n" +
            "    public string UseHook()\n" +
            "    {\n" +
            "        return \"name\";\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var state = GetStateDeclaration(syntaxTree, "name");

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(semanticModel.GetSymbolInfo(state).Symbol);

        Assert.Null(symbol.UserHook);
    }

    [Fact]
    public void SemanticModel_UserHookInsideIfBlock_ProducesDiagnostic()
    {
        const string code =
            "using Hooks;\n" +
            "\n" +
            "state string query = \"\";\n" +
            "\n" +
            "if(true)\n" +
            "{\n" +
            "    useName(query);\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(UserHookCSharpCode));
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is CSharpStatementSyntax));
        var hookStatement = Assert.IsType<CSharpStatementSyntax>(Assert.Single(ifStatement.Body!.Tokens));

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(hookStatement));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_UserHookMustBeTopLevel, diagnostic.Code);
        Assert.Contains("useName", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_UserHookInsideForeachBlock_ProducesDiagnostic()
    {
        const string code =
            "using Hooks;\n" +
            "\n" +
            "foreach(var item in items)\n" +
            "{\n" +
            "    useName(item);\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(UserHookCSharpCode));
        var foreachStatement = Assert.IsType<CSharpStatementSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is CSharpStatementSyntax));
        var hookStatement = Assert.IsType<CSharpStatementSyntax>(Assert.Single(foreachStatement.Body!.Tokens));

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(hookStatement));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_UserHookMustBeTopLevel, diagnostic.Code);
        Assert.Contains("useName", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ResolvesDefaultParamSymbol()
    {
        const string code = "param int UserId = 1;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var param = Assert.IsType<ParamDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbolInfo = semanticModel.GetSymbolInfo(param);

        var symbol = Assert.IsAssignableFrom<IParamSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal(AkburaSymbolKind.Parameter, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("UserId", symbol.Name);
        Assert.Equal(ParamBindingKind.Default, symbol.BindingKind);
        Assert.True(symbol.ReceivesValueFromParent);
        Assert.False(symbol.SendsValueToParent);
        Assert.False(symbol.IsTwoWayBinding);
        Assert.True(symbol.HasExplicitType);
        Assert.True(symbol.HasDefaultValue);
        Assert.False(symbol.Type.IsDefault);
        Assert.False(symbol.DefaultValueType.IsDefault);
        Assert.Equal("Int32", symbol.Type.Name);
        Assert.Equal("Int32", symbol.DefaultValueType.Name);
        Assert.Same(param, symbol.DeclarationSyntax);
        Assert.Same(param.DefaultValue, symbol.DefaultValueSyntax);
        Assert.Equal("param Int32 UserId", symbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(param).IsEmpty);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(param);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesBindParamSymbol()
    {
        const string code = "param bind string Search = \"\";";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var param = Assert.IsType<ParamDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbol = Assert.IsAssignableFrom<IParamSymbol>(semanticModel.GetSymbolInfo(param).Symbol);

        Assert.Equal("Search", symbol.Name);
        Assert.Equal(ParamBindingKind.Bind, symbol.BindingKind);
        Assert.True(symbol.ReceivesValueFromParent);
        Assert.True(symbol.SendsValueToParent);
        Assert.True(symbol.IsTwoWayBinding);
        Assert.True(symbol.HasExplicitType);
        Assert.True(symbol.HasDefaultValue);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Equal("String", symbol.DefaultValueType.Name);
        Assert.Equal("param bind String Search", symbol.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_ResolvesOutParamSymbol()
    {
        const string code = "param out SelectedTask;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var param = Assert.IsType<ParamDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbol = Assert.IsAssignableFrom<IParamSymbol>(semanticModel.GetSymbolInfo(param).Symbol);

        Assert.Equal("SelectedTask", symbol.Name);
        Assert.Equal(ParamBindingKind.Out, symbol.BindingKind);
        Assert.False(symbol.ReceivesValueFromParent);
        Assert.True(symbol.SendsValueToParent);
        Assert.False(symbol.IsTwoWayBinding);
        Assert.False(symbol.HasExplicitType);
        Assert.False(symbol.HasDefaultValue);
        Assert.True(symbol.Type.IsDefault);
        Assert.True(symbol.DefaultValueType.IsDefault);
        Assert.Null(symbol.DefaultValueSyntax);
        Assert.Equal("param out SelectedTask", symbol.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_InfersImplicitParamTypeFromDefaultValue()
    {
        const string code = "param Search = \"\";";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var param = Assert.IsType<ParamDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbol = Assert.IsAssignableFrom<IParamSymbol>(semanticModel.GetSymbolInfo(param).Symbol);

        Assert.Equal("Search", symbol.Name);
        Assert.False(symbol.HasExplicitType);
        Assert.True(symbol.HasDefaultValue);
        Assert.Equal(ParamBindingKind.Default, symbol.BindingKind);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Equal("String", symbol.DefaultValueType.Name);
        Assert.Equal("param String Search", symbol.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_ResolvesInjectSymbol()
    {
        const string code = "inject ILogger<MyComponent> logger;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public interface ILogger<T> { }\n" +
                "public sealed class MyComponent { }"));
        var inject = Assert.IsType<InjectDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbolInfo = semanticModel.GetSymbolInfo(inject);

        var symbol = Assert.IsAssignableFrom<IInjectSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal(AkburaSymbolKind.InjectedService, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("logger", symbol.Name);
        Assert.True(symbol.IsRequired);
        Assert.False(symbol.Type.IsDefault);
        Assert.Equal("ILogger", symbol.Type.Name);
        var loggerType = Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol);
        Assert.True(loggerType.IsGenericType);
        Assert.Equal("MyComponent", loggerType.TypeArguments.Single().Name);
        Assert.Same(inject, symbol.DeclarationSyntax);
        Assert.True(symbol.CSharpDefinition.IsDefault);
        Assert.Equal("inject global::ILogger<global::MyComponent> logger", symbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(inject).IsEmpty);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(inject);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesCurrentComponentType_FromFileNameInInject()
    {
        const string code = "inject ILogger<Counter> logger;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation("public interface ILogger<T> { }"));
        var inject = Assert.IsType<InjectDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbol = Assert.IsAssignableFrom<IInjectSymbol>(
            semanticModel.GetSymbolInfo(inject).Symbol);

        Assert.Equal("Counter", syntaxTree.ComponentName);
        Assert.Equal("ILogger", symbol.Type.Name);
        var loggerType = Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol);
        var componentType = Assert.IsAssignableFrom<INamedTypeSymbol>(loggerType.TypeArguments.Single());
        Assert.Equal("Counter", componentType.Name);
        Assert.Equal("Counter", componentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        Assert.True(semanticModel.GetSemanticDiagnostics(inject).IsEmpty);
    }

    [Fact]
    public void SemanticModel_CSharpStatementReferences_MapBackToAkburaSymbols()
    {
        const string code =
            "using Demo;\n" +
            "\n" +
            "inject ILogger<Counter> logger;\n" +
            "\n" +
            "state count = 0;\n" +
            "\n" +
            "logger.LogInformation(\"Counts is {0}\", count);";
        const string csharpCode =
            "namespace Demo\n" +
            "{\n" +
            "    public interface ILogger<T>\n" +
            "    {\n" +
            "        void LogInformation(string message, params object[] args);\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var root = syntaxTree.GetRoot();
        var inject = Assert.IsType<InjectDeclarationSyntax>(root.Members[1]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[2]);
        var statement = Assert.IsType<CSharpStatementSyntax>(root.Members[3]);

        var references = semanticModel.GetCSharpSymbolReferences(statement);

        var loggerReference = Assert.Single(references, reference => reference.Name == "logger");
        var loggerLocal = Assert.IsAssignableFrom<ILocalSymbol>(loggerReference.CSharpDefinition.Symbol);
        var loggerSymbol = Assert.IsAssignableFrom<IInjectSymbol>(loggerReference.AkburaSymbol);
        Assert.Equal("logger", loggerLocal.Name);
        Assert.Same(semanticModel.GetSymbolInfo(inject).Symbol, loggerSymbol);
        var loggerType = Assert.IsAssignableFrom<INamedTypeSymbol>(loggerSymbol.Type.Symbol);
        Assert.Equal("Counter", loggerType.TypeArguments.Single().Name);

        var countReference = Assert.Single(references, reference => reference.Name == "count");
        var countLocal = Assert.IsAssignableFrom<ILocalSymbol>(countReference.CSharpDefinition.Symbol);
        var countSymbol = Assert.IsAssignableFrom<IStateSymbol>(countReference.AkburaSymbol);
        Assert.Equal("count", countLocal.Name);
        Assert.Same(semanticModel.GetSymbolInfo(state).Symbol, countSymbol);
        Assert.Equal("Int32", countSymbol.Type.Name);

        var methodReference = Assert.Single(references, reference => reference.Name == "LogInformation");
        Assert.IsAssignableFrom<IMethodSymbol>(methodReference.CSharpDefinition.Symbol);
        Assert.Null(methodReference.AkburaSymbol);
    }

    [Fact]
    public void SemanticModel_CSharpStatementReferences_MapCommandFacadeMembersBackToCommandSymbol()
    {
        const string code =
            "command bool IsHi();\n" +
            "\n" +
            "var result = IsHi.Execute();\n" +
            "var canExecute = IsHi.CanExecute;\n" +
            "var isExecuting = IsHi.IsExecuting;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var command = Assert.IsType<CommandDeclarationSyntax>(root.Members[0]);
        var commandSymbol = Assert.IsAssignableFrom<ICommandSymbol>(
            semanticModel.GetSymbolInfo(command).Symbol);

        var references = root.Members
            .OfType<CSharpStatementSyntax>()
            .SelectMany(statement => semanticModel.GetCSharpSymbolReferences(statement))
            .ToArray();

        var commandReceivers = references.Where(reference => reference.Name == "IsHi").ToArray();
        Assert.Equal(3, commandReceivers.Length);
        Assert.All(commandReceivers, reference =>
        {
            Assert.Same(commandSymbol, reference.AkburaSymbol);
            Assert.True(reference.CSharpDefinition.Symbol is IFieldSymbol);
        });

        var execute = Assert.Single(references, reference => reference.Name == "Execute");
        Assert.IsAssignableFrom<IMethodSymbol>(execute.CSharpDefinition.Symbol);
        Assert.Same(commandSymbol, execute.AkburaSymbol);

        var canExecute = Assert.Single(references, reference => reference.Name == "CanExecute");
        var canExecuteProperty = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(
            canExecute.CSharpDefinition.Symbol);
        Assert.Equal("IObservable", canExecuteProperty.Type.Name);
        Assert.Same(commandSymbol, canExecute.AkburaSymbol);

        var isExecuting = Assert.Single(references, reference => reference.Name == "IsExecuting");
        var isExecutingProperty = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(
            isExecuting.CSharpDefinition.Symbol);
        Assert.Equal("IObservable", isExecutingProperty.Type.Name);
        Assert.Same(commandSymbol, isExecuting.AkburaSymbol);
    }

    [Fact]
    public void SemanticModel_MarkupEventHandlerReferences_MapBackToAkburaSymbols()
    {
        const string code =
            """
            using System.Threading.Tasks;
            using Avalonia.Controls;
            using Demo;

            inject ILogger<Counter> logger;
            state int count = 0;

            <Button Click={async (sender, args) => {
                logger.LogInformation("Clicked {0}", count);
                sender?.ToString();
                args.ToString();
                count++;
                await Task.Yield();
            }} />
            """;
        const string csharpCode =
            """
            namespace Demo;

            public interface ILogger<T>
            {
                void LogInformation(string message, params object[] args);
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var root = syntaxTree.GetRoot();
        var inject = Assert.IsType<InjectDeclarationSyntax>(root.Members[3]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[4]);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));
        var value = Assert.IsType<MarkupDynamicAttributeValueSyntax>(attribute.Value);

        var references = semanticModel.GetCSharpSymbolReferences(value.Expression);

        var loggerReference = Assert.Single(references, reference => reference.Name == "logger");
        var loggerSymbol = Assert.IsAssignableFrom<IInjectSymbol>(loggerReference.AkburaSymbol);
        Assert.Same(semanticModel.GetSymbolInfo(inject).Symbol, loggerSymbol);

        var countReferences = references.Where(reference => reference.Name == "count").ToArray();
        Assert.Equal(2, countReferences.Length);
        var countSymbol = Assert.IsAssignableFrom<IStateSymbol>(countReferences[0].AkburaSymbol);
        Assert.Same(semanticModel.GetSymbolInfo(state).Symbol, countSymbol);
        Assert.All(countReferences, reference => Assert.Same(countSymbol, reference.AkburaSymbol));

        var senderReference = Assert.Single(references, reference => reference.Name == "sender");
        Assert.IsAssignableFrom<IParameterSymbol>(senderReference.CSharpDefinition.Symbol);
        Assert.Null(senderReference.AkburaSymbol);

        var argsReference = Assert.Single(references, reference => reference.Name == "args");
        var argsParameter = Assert.IsAssignableFrom<IParameterSymbol>(argsReference.CSharpDefinition.Symbol);
        Assert.Equal("RoutedEventArgs", argsParameter.Type.Name);
        Assert.Null(argsReference.AkburaSymbol);

        Assert.Contains(references, reference =>
            reference.Name == "LogInformation" &&
            reference.CSharpDefinition.Symbol is IMethodSymbol &&
            reference.AkburaSymbol == null);
        Assert.Contains(references, reference =>
            reference.Name == "Yield" &&
            reference.CSharpDefinition.Symbol is IMethodSymbol);
    }

    [Fact]
    public void SemanticModel_MarkupCommandHandlerReferences_MapCommandFacadeMembersBackToCommandSymbol()
    {
        const string aCode =
            """
            namespace SomeNs;

            command int Click(int a);
            """;
        const string bCode =
            """
            using SomeNs;

            command int CustomClick(int a);
            state int clicked = 0;

            <A Click={async x => await CustomClick.Execute(clicked + x)}/>
            """;

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var root = bSyntaxTree.GetRoot();
        var command = Assert.IsType<CommandDeclarationSyntax>(root.Members[1]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[2]);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));
        var value = Assert.IsType<MarkupDynamicAttributeValueSyntax>(attribute.Value);

        var references = semanticModel.GetCSharpSymbolReferences(attribute);

        var commandSymbol = Assert.IsAssignableFrom<ICommandSymbol>(
            semanticModel.GetSymbolInfo(command).Symbol);
        var commandReceiver = Assert.Single(references, reference => reference.Name == "CustomClick");
        Assert.Same(commandSymbol, commandReceiver.AkburaSymbol);
        Assert.IsAssignableFrom<IFieldSymbol>(commandReceiver.CSharpDefinition.Symbol);

        var execute = Assert.Single(references, reference => reference.Name == "Execute");
        Assert.Same(commandSymbol, execute.AkburaSymbol);
        var executeMethod = Assert.IsAssignableFrom<IMethodSymbol>(execute.CSharpDefinition.Symbol);
        Assert.Equal("ValueTask", executeMethod.ReturnType.Name);

        var clicked = Assert.Single(references, reference => reference.Name == "clicked");
        var clickedSymbol = Assert.IsAssignableFrom<IStateSymbol>(clicked.AkburaSymbol);
        Assert.Same(semanticModel.GetSymbolInfo(state).Symbol, clickedSymbol);

        var parameter = Assert.Single(references, reference => reference.Name == "x");
        var parameterSymbol = Assert.IsAssignableFrom<IParameterSymbol>(parameter.CSharpDefinition.Symbol);
        Assert.Equal("Int32", parameterSymbol.Type.Name);
        Assert.Null(parameter.AkburaSymbol);

        Assert.Equal(
            references.Select(reference => reference.Name).OrderBy(static name => name),
            semanticModel.GetCSharpSymbolReferences(value.Expression)
                .Select(reference => reference.Name)
                .OrderBy(static name => name));
    }

    [Fact]
    public void SemanticModel_ResolvesCommandSymbol()
    {
        const string code = "command int Click(int a);";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var command = Assert.IsType<CommandDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbolInfo = semanticModel.GetSymbolInfo(command);

        var symbol = Assert.IsAssignableFrom<ICommandSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.Command, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("Click", symbol.Name);
        Assert.False(symbol.IsVoid);
        Assert.True(symbol.IsAsyncLike);
        Assert.True(symbol.HasResult);
        Assert.True(symbol.SupportsIsExecuting);
        Assert.Equal("Int32", symbol.ReturnType.Name);
        Assert.Equal("Int32", symbol.ResultType.Name);
        Assert.Same(command, symbol.DeclarationSyntax);

        var parameter = Assert.Single(symbol.Parameters);
        Assert.Equal(0, parameter.Ordinal);
        Assert.Equal(AkburaSymbolKind.CommandParameter, parameter.Kind);
        Assert.Equal("a", parameter.Name);
        Assert.Equal("Int32", parameter.Type.Name);
        Assert.Equal("Int32 a", parameter.ToDisplayString());
        Assert.Equal("command Int32 Click", symbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(command).IsEmpty);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(command);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesUseEffectSymbol()
    {
        const string code =
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "inject IService service;\n" +
            "command void Refresh(int userId);\n" +
            "\n" +
            "useEffect(UserId, count, service.Value, Refresh.IsExecuting) {\n" +
            "    System.Console.WriteLine(count);\n" +
            "} cancel {\n" +
            "    System.Console.WriteLine(\"cancel\");\n" +
            "} finally {\n" +
            "    System.Console.WriteLine(\"finally\");\n" +
            "}";
        const string csharpCode =
            "public interface IService\n" +
            "{\n" +
            "    int Value { get; }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var root = syntaxTree.GetRoot();
        var useEffect = Assert.IsType<UseEffectDeclarationSyntax>(root.Members[4]);

        var symbolInfo = semanticModel.GetSymbolInfo(useEffect);

        var symbol = Assert.IsAssignableFrom<IUseEffectSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.UseEffect, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("useEffect", symbol.Name);
        Assert.Same(useEffect, symbol.DeclarationSyntax);
        Assert.Same(useEffect.Arguments, symbol.ArgumentsSyntax);
        Assert.Same(useEffect.Body, symbol.Body);
        Assert.True(symbol.HasCancelBlock);
        Assert.True(symbol.HasFinallyBlock);
        Assert.NotNull(symbol.CancelBlock);
        Assert.NotNull(symbol.FinallyBlock);

        Assert.Equal(
            ["UserId", "count", "service.Value", "Refresh.IsExecuting"],
            symbol.Dependencies.Select(static dependency => dependency.ExpressionText));
        Assert.IsAssignableFrom<IParamSymbol>(symbol.Dependencies[0].AkburaSymbol);
        Assert.IsAssignableFrom<IStateSymbol>(symbol.Dependencies[1].AkburaSymbol);
        Assert.IsAssignableFrom<IInjectSymbol>(symbol.Dependencies[2].AkburaSymbol);
        Assert.IsAssignableFrom<ICommandSymbol>(symbol.Dependencies[3].AkburaSymbol);
        Assert.Equal("UserId", symbol.Dependencies[0].CSharpDefinition.Name);
        Assert.Equal("count", symbol.Dependencies[1].CSharpDefinition.Name);
        Assert.Equal("Value", symbol.Dependencies[2].CSharpDefinition.Name);
        Assert.True(symbol.Dependencies[3].IsResolved);
        Assert.Equal("useEffect(UserId, count, service.Value, Refresh.IsExecuting)", symbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(useEffect).IsEmpty);

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(root).Symbol);
        Assert.Same(symbol, Assert.Single(component.UseEffects));

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(useEffect);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesAkcssStyleSymbols()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: \"Red\";\n" +
            "    }\n" +
            "\n" +
            "    Button.hello {\n" +
            "        Background: \"Blue\";\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var globalRule = Assert.IsType<AkcssStyleRuleSyntax>(inlineAkcss.Members[0]);
        var buttonRule = Assert.IsType<AkcssStyleRuleSyntax>(inlineAkcss.Members[1]);

        var globalSymbolInfo = semanticModel.GetSymbolInfo(globalRule);
        var globalSymbol = Assert.IsType<AkcssStyleSymbol>(globalSymbolInfo.Symbol);
        Assert.IsAssignableFrom<IAkcssSymbol>(globalSymbol);
        Assert.Equal(AkburaCandidateReason.None, globalSymbolInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.AkcssClass, globalSymbol.Kind);
        Assert.Equal(SymbolLanguage.Akcss, globalSymbol.Language);
        Assert.Equal("hello", globalSymbol.Name);
        Assert.Equal("hello", globalSymbol.MetadataName);
        Assert.False(globalSymbol.HasTargetType);
        Assert.True(globalSymbol.TargetType.IsDefault);
        Assert.Same(globalRule, globalSymbol.DeclarationSyntax);
        Assert.Equal("style hello", globalSymbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(globalRule).IsEmpty);

        var buttonSymbolInfo = semanticModel.GetSymbolInfo(buttonRule);
        var buttonSymbol = Assert.IsType<AkcssStyleSymbol>(buttonSymbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, buttonSymbolInfo.CandidateReason);
        Assert.True(buttonSymbol.HasTargetType);
        Assert.Equal("Button", buttonSymbol.TargetType.Name);
        Assert.Equal("Button.hello", buttonSymbol.MetadataName);
        Assert.Equal("style Button.hello", buttonSymbol.ToDisplayString());
        AssertResolvedAvaloniaButton(semanticModel, buttonSymbol.TargetType);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(buttonRule);
        Assert.Same(buttonSymbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesInlineAkcssModuleSymbol()
    {
        const string code =
            "namespace Demo;\n" +
            "\n" +
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: \"Red\";\n" +
            "    }\n" +
            "\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            Width: value;\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(
            root.Members.Single(member => member is InlineAkcssBlockSyntax));

        var moduleInfo = semanticModel.GetSymbolInfo(inlineAkcss);

        var module = Assert.IsAssignableFrom<IAkcssModuleSymbol>(moduleInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, moduleInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.AkcssModule, module.Kind);
        Assert.True(module.IsInlined);
        Assert.Null(module.Path);
        Assert.Same(inlineAkcss, module.DeclaringSyntax);
        Assert.Equal(2, module.AkcssSymbols.Length);
        Assert.Contains(module.AkcssSymbols, symbol => symbol is AkcssStyleSymbol { Name: "hello" });
        Assert.Contains(module.AkcssSymbols, symbol => symbol is ITailwindUtilitySymbol { Name: "w" });

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(root).Symbol);
        var componentModule = Assert.Single(component.AkcssModules);
        Assert.Same(module, componentModule);
        Assert.Same(component, module.ContainingSymbol);
    }

    [Fact]
    public void SemanticModel_AkcssStyleTargetType_MustBeAvaloniaControl()
    {
        const string code =
            "@akcss {\n" +
            "    PlainObject.hello {\n" +
            "        Background: \"Red\";\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation("public sealed class PlainObject { }"));
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var rule = Assert.IsType<AkcssStyleRuleSyntax>(Assert.Single(inlineAkcss.Members));

        var symbolInfo = semanticModel.GetSymbolInfo(rule);

        Assert.Null(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.NotFound, symbolInfo.CandidateReason);
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(rule));
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssSelectorTargetNotFound, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_ResolvesTailwindUtilitySymbols()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            Width: value;\n" +
            "        }\n" +
            "\n" +
            "        Button.btn-(string variant) {\n" +
            "            Width: 10;\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(Assert.Single(inlineAkcss.Members));

        var widthUtilityInfo = semanticModel.GetSymbolInfo(utilities.Utilities[0]);
        var widthUtility = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(widthUtilityInfo.Symbol);
        Assert.IsAssignableFrom<IAkcssSymbol>(widthUtility);
        Assert.Equal(AkburaCandidateReason.None, widthUtilityInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.AkcssUtility, widthUtility.Kind);
        Assert.Equal(SymbolLanguage.Akcss, widthUtility.Language);
        Assert.Equal("w", widthUtility.Name);
        Assert.Equal("w", widthUtility.MetadataName);
        Assert.False(widthUtility.HasTargetType);
        Assert.True(widthUtility.TargetType.IsDefault);
        Assert.Same(utilities.Utilities[0], widthUtility.DeclarationSyntax);
        var widthParameter = Assert.Single(widthUtility.Parameters);
        Assert.Equal("value", widthParameter.Name);
        Assert.Equal(0, widthParameter.Ordinal);
        Assert.Equal(SymbolLanguage.Akcss, widthParameter.Language);
        Assert.Equal("Double", widthParameter.Type.Name);
        Assert.NotNull(widthParameter.CSharpParameter);
        Assert.Equal("Double value", widthParameter.ToDisplayString());
        Assert.Equal("utility w/1", widthUtility.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(utilities.Utilities[0]).IsEmpty);

        var buttonUtility = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utilities.Utilities[1]).Symbol);
        Assert.Equal("btn", buttonUtility.Name);
        Assert.True(buttonUtility.HasTargetType);
        Assert.Equal("Button", buttonUtility.TargetType.Name);
        AssertResolvedAvaloniaButton(semanticModel, buttonUtility.TargetType);
        Assert.Equal("Button.btn", buttonUtility.MetadataName);
        var variantParameter = Assert.Single(buttonUtility.Parameters);
        Assert.Equal("variant", variantParameter.Name);
        Assert.Equal("String", variantParameter.Type.Name);
        Assert.NotNull(variantParameter.CSharpParameter);
    }

    [Fact]
    public void SemanticModel_AkcssDefaultStyle_BackgroundWhite_CreatesColorSetterOperation()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: White;\n" +
            "    }\n" +
            "}";

        var (semanticModel, assignment, operation, symbol) = GetSingleStyleOperation(code);

        Assert.False(symbol.HasTargetType);
        Assert.Equal("Background", operation.Property?.Name);
        Assert.Equal(SymbolLanguage.Akcss, operation.Property?.Language);
        Assert.True(operation.Property?.IsAvaloniaProperty);
        Assert.Equal("BackgroundProperty", operation.Property?.AvaloniaPropertyDefinition.Name);
        Assert.Equal(AkcssPropertyValueKind.ColorLiteral, operation.ValueKind);
        Assert.True(operation.RequiresBrushConversion);
        Assert.Equal("Color", operation.ValueType.Name);
        var colorSymbol = Assert.IsType<CSharpSymbolDefinition>(operation.ConvertedValue);
        Assert.Equal("White", colorSymbol.Name);
        AssertAvaloniaColorsProperty(colorSymbol, "White");
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(assignment).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssButtonStyle_BackgroundWhite_ResolvesTargetAndProperty()
    {
        const string code =
            "@akcss {\n" +
            "    Button.hello {\n" +
            "        Background: White;\n" +
            "    }\n" +
            "}";

        var (semanticModel, _, operation, symbol) = GetSingleStyleOperation(code);

        Assert.True(symbol.HasTargetType);
        AssertResolvedAvaloniaButton(semanticModel, symbol.TargetType);
        Assert.Equal("Background", operation.Property?.Name);
        Assert.Equal("BackgroundProperty", operation.Property?.AvaloniaPropertyDefinition.Name);
        Assert.Equal(AkcssPropertyValueKind.ColorLiteral, operation.ValueKind);
        var colorSymbol = Assert.IsType<CSharpSymbolDefinition>(operation.ConvertedValue);
        AssertAvaloniaColorsProperty(colorSymbol, "White");
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssAvaloniaAttachedProperty_ResolvesHelpers()
    {
        const string code =
            """
            @akcss {
                @using Avalonia.Controls;

                TextBlock.item {
                    Grid.Column: 1;
                }
            }
            """;

        var (semanticModel, assignment, operation, _) = GetSingleStyleOperation(code);
        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(operation.Property);

        Assert.Equal("Column", property.Name);
        Assert.True(property.IsAttachedProperty);
        Assert.True(property.IsAvaloniaProperty);
        Assert.Equal("ColumnProperty", property.AttachedPropertyDefinition.Name);
        Assert.Equal("ColumnProperty", property.AvaloniaPropertyDefinition.Name);
        Assert.Equal("GetColumn", property.AttachedGetterDefinition.Name);
        Assert.Equal("SetColumn", property.AttachedSetterDefinition.Name);
        Assert.Equal("Control", property.AttachedTargetType.Name);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(property.Type.Symbol).SpecialType);
        Assert.Equal("Int32", operation.ValueType.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(assignment).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssCustomAttachedProperty_ResolvesHelpers()
    {
        const string code =
            """
            @akcss {
                @using MyControls;

                MyControl.item {
                    MyAttached.A: 1;
                }
            }
            """;
        const string csharpCode =
            """
            namespace MyControls;

            public sealed class MyControl : Avalonia.Controls.Control
            {
            }

            public sealed class AttachedProperty<T>
            {
            }

            public static class MyAttached
            {
                public static readonly AttachedProperty<int> AProperty = null!;

                public static void Set(object element, int value)
                {
                }

                public static int Get(object element)
                {
                    return 0;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(Assert.Single(symbol.Operations));
        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(operation.Property);

        Assert.Equal("A", property.Name);
        Assert.True(property.IsAttachedProperty);
        Assert.False(property.IsAvaloniaProperty);
        Assert.Equal("AProperty", property.AttachedPropertyDefinition.Name);
        Assert.Equal("Get", property.AttachedGetterDefinition.Name);
        Assert.Equal("Set", property.AttachedSetterDefinition.Name);
        Assert.Equal(SpecialType.System_Object, Assert.IsAssignableFrom<INamedTypeSymbol>(property.AttachedTargetType.Symbol).SpecialType);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(property.Type.Symbol).SpecialType);
        Assert.Equal("Int32", operation.ValueType.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(operation.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssUsing_ResolvesSelectorAndQualifiedProperty()
    {
        const string code =
            "@akcss {\n" +
            "    @using MyComponents;\n" +
            "    @using MyNs;\n" +
            "\n" +
            "    MyComponent.multiClass {\n" +
            "        MyClass.MyProperty: Expression.Hello;\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyComponents\n" +
            "{\n" +
            "    public sealed class MyComponent : Avalonia.Controls.Control { }\n" +
            "}\n" +
            "\n" +
            "namespace MyNs\n" +
            "{\n" +
            "    public static class MyClass\n" +
            "    {\n" +
            "        public static readonly Avalonia.StyledProperty<double> MyProperty = null!;\n" +
            "    }\n" +
            "    public static class Expression\n" +
            "    {\n" +
            "        public static double Hello => 1;\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));

        Assert.True(symbol.HasTargetType);
        Assert.Equal("multiClass", symbol.ClassName);
        Assert.Equal("global::MyComponents.MyComponent",
            symbol.TargetType.Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Equal("MyProperty", operation.Property?.Name);
        Assert.Equal("MyProperty", operation.Property?.AvaloniaPropertyDefinition.Name);
        Assert.Equal("Double", operation.ValueType.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(operation.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssGenericQualifiedProperty_ResolvesProperty()
    {
        const string code =
            """
            @akcss {
                .class {
                    global::Ns.Class<int>.Property: 1.0;
                }
            }
            """;
        const string csharpCode =
            """
            namespace Ns;

            public sealed class Class<T> : Avalonia.Controls.Control
            {
                public static readonly Avalonia.StyledProperty<double> PropertyProperty = null!;
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(Assert.Single(rule.Members));
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));
        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(operation.Property);

        Assert.Equal(code, syntaxTree.GetRoot().ToFullString());
        Assert.Equal("global::Ns.Class<int>.Property", assignment.PropertyName.ToFullString().Trim());
        Assert.False(symbol.HasTargetType);
        Assert.Equal("Property", property.Name);
        Assert.True(property.IsAvaloniaProperty);
        Assert.False(property.IsAttachedProperty);
        Assert.Equal("PropertyProperty", property.AvaloniaPropertyDefinition.Name);
        var propertyField = Assert.IsAssignableFrom<IFieldSymbol>(property.AvaloniaPropertyDefinition.Symbol);
        Assert.Equal("global::Ns.Class<T>",
            propertyField.ContainingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Equal(SpecialType.System_Double, Assert.IsAssignableFrom<INamedTypeSymbol>(property.Type.Symbol).SpecialType);
        Assert.Equal("Double", operation.ValueType.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(assignment).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssParenthesizedSelectors_ResolveTypedAndTypedClassStyles()
    {
        const string code =
            "@akcss {\n" +
            "    (global::MyComponents.MyComponent) {\n" +
            "    }\n" +
            "\n" +
            "    (global::MyComponents.MyComponent).withClass {\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyComponents;\n" +
            "public sealed class MyComponent : Avalonia.Controls.Control { }";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var rules = inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>().ToArray();

        var typedOnly = Assert.IsAssignableFrom<IAkcssSymbol>(
            semanticModel.GetSymbolInfo(rules[0]).Symbol);
        var typedWithClass = Assert.IsAssignableFrom<IAkcssSymbol>(
            semanticModel.GetSymbolInfo(rules[1]).Symbol);

        Assert.True(typedOnly.HasTargetType);
        Assert.Null(typedOnly.ClassName);
        Assert.Equal("MyComponent", typedOnly.Name);
        Assert.True(typedWithClass.HasTargetType);
        Assert.Equal("withClass", typedWithClass.ClassName);
        Assert.Equal("MyComponent.withClass", typedWithClass.MetadataName);
    }

    [Fact]
    public void SemanticModel_AkcssApply_ResolvesLocalThenImportedStylesAndUtilities()
    {
        const string code =
            "@akcss {\n" +
            "    @using Shared.Styles.akcss;\n" +
            "\n" +
            "    .local {\n" +
            "    }\n" +
            "\n" +
            "    .multi {\n" +
            "        @apply local imported w-5;\n" +
            "    }\n" +
            "}";
        const string importedAkcss =
            ".imported {\n" +
            "}\n" +
            "\n" +
            "@utilities {\n" +
            "    .w-(double value) { Width: value; }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var akcssTree = AkcssSyntaxTree.ParseText(
            importedAkcss,
            "Styles.akcss",
            "Shared.Styles.akcss");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [akcssTree]);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var multiRule = inlineAkcss.Members
            .OfType<AkcssStyleRuleSyntax>()
            .Single(rule => rule.Selector.Name?.Identifier.ValueText == "multi");
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(multiRule).Symbol);
        var apply = Assert.IsAssignableFrom<IAkcssApplyOperation>(Assert.Single(symbol.Operations));

        Assert.Equal(["local", "imported", "w-5"], apply.Items.AsEnumerable());
        Assert.Equal(3, apply.AppliedSymbols.Length);
        Assert.Equal("local", apply.AppliedSymbols[0].ClassName);
        Assert.Equal("imported", apply.AppliedSymbols[1].ClassName);
        Assert.IsAssignableFrom<ITailwindUtilitySymbol>(apply.AppliedSymbols[2]);
        Assert.False(apply.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(apply.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssApply_ResolvesHyphenatedStaticUtility()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        Control.self-start {\n" +
            "            HorizontalAlignment: Left;\n" +
            "        }\n" +
            "\n" +
            "        Control.min-w-(double value) {\n" +
            "            MinWidth: value;\n" +
            "        }\n" +
            "    }\n" +
            "\n" +
            "    .target {\n" +
            "        @apply self-start min-w-2;\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var targetRule = inlineAkcss.Members
            .OfType<AkcssStyleRuleSyntax>()
            .Single(rule => rule.Selector.Name?.Identifier.ValueText == "target");
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(targetRule).Symbol);
        var apply = Assert.IsAssignableFrom<IAkcssApplyOperation>(Assert.Single(symbol.Operations));

        Assert.Equal(2, apply.AppliedSymbols.Length);

        var appliedUtility = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            apply.AppliedSymbols[0]);
        var appliedParameterizedUtility = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            apply.AppliedSymbols[1]);

        Assert.Equal("self-start", appliedUtility.Name);
        Assert.True(appliedUtility.Parameters.IsEmpty);
        Assert.Equal("min-w", appliedParameterizedUtility.Name);
        Assert.Single(appliedParameterizedUtility.Parameters);
        Assert.False(apply.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(apply.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssApply_DuplicateSameLayerCandidatesProduceDiagnostic()
    {
        const string code =
            "@akcss {\n" +
            "    .dup { }\n" +
            "    .dup { }\n" +
            "    .target { @apply dup; }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var targetRule = inlineAkcss.Members
            .OfType<AkcssStyleRuleSyntax>()
            .Single(rule => rule.Selector.Name?.Identifier.ValueText == "target");
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(targetRule).Symbol);
        var apply = Assert.IsAssignableFrom<IAkcssApplyOperation>(Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(apply.Syntax));

        Assert.True(apply.HasErrors);
        Assert.Empty(apply.AppliedSymbols);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssApplyItemAmbiguous, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_AkcssIntercept_ResolvesAkcssStyleSubtype()
    {
        const string code =
            "@akcss {\n" +
            "    @using MyStyles;\n" +
            "\n" +
            "    .myVeryComplexClass {\n" +
            "        @intercept MyVeryComplexClass;\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyStyles;\n" +
            "public sealed class MyVeryComplexClass : Akbura.Akcss.AkcssClass\n" +
            "{\n" +
            "    public override void Update(object control) { }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssInterceptOperation>(Assert.Single(symbol.Operations));

        Assert.True(symbol.IsIntercepted);
        Assert.Equal("global::MyStyles.MyVeryComplexClass",
            symbol.InterceptType.Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Same(symbol.InterceptType.Symbol, operation.InterceptType.Symbol);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(operation.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssIntercept_IgnoresOtherStyleMembersWithWarnings()
    {
        const string code =
            "@akcss {\n" +
            "    @using MyStyles;\n" +
            "\n" +
            "    .myVeryComplexClass {\n" +
            "        Background: White;\n" +
            "        @apply other;\n" +
            "        @if(true) {\n" +
            "            Opacity: 1;\n" +
            "        }\n" +
            "        @intercept MyVeryComplexClass;\n" +
            "    }\n" +
            "\n" +
            "    .other { Opacity: 1; }\n" +
            "}";
        const string csharpCode =
            "namespace MyStyles;\n" +
            "public sealed class MyVeryComplexClass : Akbura.Akcss.AkcssClass\n" +
            "{\n" +
            "    public override void Update(object control) { }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree, "myVeryComplexClass");
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var intercept = Assert.IsAssignableFrom<IAkcssInterceptOperation>(Assert.Single(symbol.Operations));

        Assert.True(symbol.IsIntercepted);
        Assert.False(intercept.HasErrors);

        var ignoredMembers = rule.Members
            .Where(static member => member is not AkcssInterceptDirectiveSyntax)
            .ToArray();
        Assert.Equal(3, ignoredMembers.Length);
        foreach (var ignoredMember in ignoredMembers)
        {
            var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(ignoredMember));
            Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptIgnoresMember, diagnostic.Code);
            Assert.Equal(AkburaDiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Null(semanticModel.GetOperation(ignoredMember));
        }
    }

    [Fact]
    public void SemanticModel_AkcssUtilityIntercept_RequiresAkcssUtilitySubtype()
    {
        const string code =
            "@akcss {\n" +
            "    @using MyStyles;\n" +
            "\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            Width: value;\n" +
            "            @intercept WidthUtility;\n" +
            "        }\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyStyles;\n" +
            "public abstract class WidthUtility : Akbura.Akcss.AkcssUtility<double> { }";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utility).Symbol);
        var intercept = Assert.IsAssignableFrom<IAkcssInterceptOperation>(Assert.Single(symbol.Operations));
        var ignoredMember = Assert.IsType<AkcssAssignmentSyntax>(
            Assert.Single(utility.Members, static member => member is AkcssAssignmentSyntax));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(ignoredMember));

        Assert.True(symbol.IsIntercepted);
        Assert.Equal("global::MyStyles.WidthUtility",
            symbol.InterceptType.Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Same(symbol.InterceptType.Symbol, intercept.InterceptType.Symbol);
        Assert.False(intercept.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptIgnoresMember, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_AkcssIntercept_InvalidTypeProducesDiagnostic()
    {
        const string code =
            "@akcss {\n" +
            "    .myVeryComplexClass {\n" +
            "        @intercept global::MyStyles.NotAStyle;\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyStyles;\n" +
            "public sealed class NotAStyle { }";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssInterceptOperation>(Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(operation.Syntax));

        Assert.False(symbol.IsIntercepted);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptTypeInvalid, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_AkcssQualifiedProperty_MissingOwnerPropertyProducesDiagnostic()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        global::MyNs.MyClass.MissingProperty: 1;\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyNs;\n" +
            "public static class MyClass { }";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(operation.Syntax));

        Assert.Null(operation.Property);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_GetAkcssOperationBeforeSymbolInfo_ReusesSymbolOperation()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: White;\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(Assert.Single(rule.Members));

        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            semanticModel.GetOperation(assignment));
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(
            semanticModel.GetSymbolInfo(rule).Symbol);

        Assert.Same(operation, Assert.Single(symbol.Operations));
    }

    [Fact]
    public void SemanticModel_AkcssColorStringLiteral_ParsesColorValue()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: \"#FFAA\";\n" +
            "    }\n" +
            "}";

        var (semanticModel, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal(AkcssPropertyValueKind.ColorLiteral, operation.ValueKind);
        var color = Assert.IsType<AkcssColorValue>(operation.ConvertedValue);
        Assert.Equal(0xFFFFAAAAu, color.ToUInt32());
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssNamedColorStringLiteral_ResolvesAvaloniaColorsSymbol()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: \"DodgerBlue\";\n" +
            "    }\n" +
            "}";

        var (semanticModel, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal(AkcssPropertyValueKind.ColorLiteral, operation.ValueKind);
        var colorSymbol = Assert.IsType<CSharpSymbolDefinition>(operation.ConvertedValue);
        AssertAvaloniaColorsProperty(colorSymbol, "DodgerBlue");
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssColorCSharpExpression_BindsThroughImplicitAvaloniaMediaUsing()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Foreground: Color.FromRgb(33, 11, 231);\n" +
            "    }\n" +
            "}";

        var (_, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, operation.ValueKind);
        Assert.True(operation.RequiresBrushConversion);
        Assert.Equal("Color", operation.ValueType.Name);
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssExpectedTypeStaticMember_BindsBareFontWeight()
    {
        const string code =
            """
            @akcss {
                TextBlock.hello {
                    FontWeight: Bold;
                }
            }
            """;

        var (semanticModel, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal("FontWeight", operation.Property?.Name);
        Assert.Equal("FontWeight", operation.ValueType.Name);
        Assert.False(operation.ValueOperation.IsDefault);
        var member = Assert.IsType<CSharpSymbolDefinition>(operation.ConvertedValue);
        Assert.Equal("Bold", member.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(operation.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssEnumValues_BindQualifiedAndCastExpressions()
    {
        const string code =
            """
            @akcss {
                TextBlock.hello {
                    FontWeight: FontWeight.Bold;
                    FontWeight: (FontWeight)700;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operations = symbol.Operations
            .Cast<IAkcssPropertySetterOperation>()
            .ToArray();

        Assert.Equal(2, operations.Length);
        Assert.All(operations, operation =>
        {
            Assert.Equal("FontWeight", operation.Property?.Name);
            Assert.Equal("FontWeight", operation.ValueType.Name);
            Assert.False(operation.ValueOperation.IsDefault);
            Assert.False(operation.HasErrors);
        });
    }

    [Fact]
    public void SemanticModel_AkcssEnumValues_BindAvaloniaAlignmentProperties()
    {
        const string code =
            """
            @akcss {
                Button.hello {
                    HorizontalAlignment: Center;
                    HorizontalAlignment: HorizontalAlignment.Right;
                    VerticalAlignment: (VerticalAlignment)2;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operations = symbol.Operations
            .Cast<IAkcssPropertySetterOperation>()
            .ToArray();

        Assert.Equal(3, operations.Length);
        Assert.Equal("HorizontalAlignment", operations[0].Property?.Name);
        Assert.Equal("HorizontalAlignment", operations[0].ValueType.Name);
        var horizontalMember = Assert.IsType<CSharpSymbolDefinition>(operations[0].ConvertedValue);
        Assert.Equal("Center", horizontalMember.Name);

        Assert.Equal("HorizontalAlignment", operations[1].Property?.Name);
        Assert.Equal("HorizontalAlignment", operations[1].ValueType.Name);

        Assert.Equal("VerticalAlignment", operations[2].Property?.Name);
        Assert.Equal("VerticalAlignment", operations[2].ValueType.Name);

        Assert.All(operations, operation =>
        {
            Assert.False(operation.ValueOperation.IsDefault);
            Assert.False(operation.HasErrors);
        });
    }

    [Fact]
    public void SemanticModel_AkcssEnumValues_BindCustomEnumProperties()
    {
        const string code =
            """
            @akcss {
                @using MyControls;

                MyControl.hello {
                    Density: Compact;
                    Density: MyControls.MyEnums.Density.Comfortable;
                    Density: (global::MyControls.MyEnums.Density)1;
                }
            }
            """;
        const string csharpCode =
            """
            namespace MyControls
            {
                public sealed class MyControl : Avalonia.Controls.Control
                {
                    public static readonly Avalonia.StyledProperty<MyEnums.Density> DensityProperty = null!;
                }
            }

            namespace MyControls.MyEnums
            {
                public enum Density
                {
                    Compact,
                    Comfortable
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operations = symbol.Operations
            .Cast<IAkcssPropertySetterOperation>()
            .ToArray();

        Assert.Equal(3, operations.Length);
        var member = Assert.IsType<CSharpSymbolDefinition>(operations[0].ConvertedValue);
        Assert.Equal("Compact", member.Name);
        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, operations[1].ValueKind);
        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, operations[2].ValueKind);
        Assert.All(operations, operation =>
        {
            Assert.Equal("Density", operation.Property?.Name);
            Assert.Equal("Density", operation.ValueType.Name);
            Assert.False(operation.ValueOperation.IsDefault);
            Assert.False(operation.HasErrors);
        });
    }

    [Fact]
    public void SemanticModel_AkcssInvalidCSharpExpression_ProducesDiagnostic()
    {
        const string code =
            """
            @akcss {
                .myText {
                    Padding: FontWeight.Bold * NotExisting.Constant;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(operation.Syntax));

        Assert.Equal("Padding", operation.Property?.Name);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssExpressionError, diagnostic.Code);
        Assert.Contains("NotExisting", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_AkcssValueCannotConvert_ProducesDiagnostic()
    {
        const string code =
            """
            @akcss {
                .myText {
                    Padding: FontWeight.Bold;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(Assert.Single(rule.Members));

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(assignment));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssValueCannotConvert, diagnostic.Code);
        Assert.Contains("Padding", diagnostic.Message);
        Assert.Contains("FontWeight", diagnostic.Message);
        Assert.Contains("Thickness", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_AkcssIfInvalidCSharpExpression_ProducesDiagnostic()
    {
        const string code =
            """
            @akcss {
                .myText {
                    @if(missingFlag) {
                        Width: 10;
                    }
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var ifDirective = Assert.IsType<AkcssIfDirectiveSyntax>(Assert.Single(rule.Members));
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssIfOperation>(Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(ifDirective));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssExpressionError, diagnostic.Code);
        Assert.Contains("missingFlag", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_AkcssUtilityInvalidCSharpExpression_ProducesDiagnostic()
    {
        const string code =
            """
            @akcss {
                @utilities {
                    .w-(double value) {
                        Width: missing + value;
                    }
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(semanticModel.GetSymbolInfo(utility).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(operation.Syntax));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssExpressionError, diagnostic.Code);
        Assert.Contains("missing", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_DuplicateAkcssSymbols_ProduceDiagnostics()
    {
        const string code =
            """
            @akcss {
                .card {
                    Width: 1;
                }

                .card {
                    Width: 2;
                }

                @utilities {
                    .w-(double value) {
                        Width: value;
                    }

                    .w-(double value) {
                        Width: value;
                    }
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(Assert.Single(syntaxTree.GetRoot().Members));
        var rules = inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>().ToArray();
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(
            inlineAkcss.Members.Single(member => member is AkcssUtilitiesSectionSyntax)).Utilities;

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_DuplicateAkcssSymbol,
            Assert.Single(semanticModel.GetSemanticDiagnostics(rules[1])).Code);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_DuplicateAkcssSymbol,
            Assert.Single(semanticModel.GetSemanticDiagnostics(utilities[1])).Code);
        Assert.Equal(2, semanticModel.GetSemanticDiagnostics(inlineAkcss).Count(static diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_DuplicateAkcssSymbol));
    }

    [Fact]
    public void SemanticModel_InaccessibleAkcssProperty_ProducesDiagnostic()
    {
        const string code =
            """
            @akcss {
                (global::Demo.SecretControl).card {
                    Secret: "x";
                }
            }
            """;
        const string csharpCode =
            """
            namespace Demo
            {
                public sealed class SecretControl : Avalonia.Controls.Control
                {
                    private string Secret { get; set; } = "";
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(Assert.Single(rule.Members));
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            semanticModel.GetOperation(assignment));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(assignment));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_InaccessibleMember, diagnostic.Code);
        Assert.Contains("Secret", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_AkcssThicknessTuples_CreateConvertedValues()
    {
        const string code =
            "@akcss {\n" +
            "    Button.hello {\n" +
            "        Padding: (10, 20);\n" +
            "        Margin: (10, 20, 30, 40);\n" +
            "        Padding: (top: 10, bottom: 30);\n" +
            "        Margin: (vertical: 5);\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);

        Assert.Equal(4, symbol.Operations.Length);
        AssertThickness(symbol.Operations[0], 10, 20, 10, 20);
        AssertThickness(symbol.Operations[1], 10, 20, 30, 40);
        AssertThickness(symbol.Operations[2], 0, 10, 0, 30);
        AssertThickness(symbol.Operations[3], 0, 5, 0, 5);
        Assert.All(rule.Members.OfType<AkcssAssignmentSyntax>(), assignment =>
            Assert.True(semanticModel.GetSemanticDiagnostics(assignment).IsEmpty));
    }

    [Fact]
    public void SemanticModel_AkcssThicknessTuple_AllowsRuntimeSideExpressions()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .px-(double width) {\n" +
            "            Padding: (horizontal: Amx.DynamicResource<double>(\"--spacing\") * width);\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utility).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));

        Assert.Equal(AkcssPropertyValueKind.ThicknessTuple, operation.ValueKind);
        Assert.Equal("Thickness", operation.ValueType.Name);
        var value = Assert.IsType<AkcssThicknessExpressionValue>(operation.ConvertedValue);
        Assert.Equal("Amx.DynamicResource<double>(\"--spacing\") * width", value.Left.ToString());
        Assert.Equal("0", value.Top.ToString());
        Assert.Equal("Amx.DynamicResource<double>(\"--spacing\") * width", value.Right.ToString());
        Assert.Equal("0", value.Bottom.ToString());
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssThicknessCSharpExpression_RemainsCSharpExpression()
    {
        const string code =
            "@akcss {\n" +
            "    Button.hello {\n" +
            "        Margin: new Thickness(10, 5) - new Thickness(3, 17);\n" +
            "    }\n" +
            "}";

        var (_, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, operation.ValueKind);
        Assert.Equal("Thickness", operation.ValueType.Name);
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssUtilityAssignment_ResolvesParameterAndDetectsAmxInvocation()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            Width: value * Amx.DynamicResource<double>(\"--spacing\");\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utility).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));

        Assert.Equal("value", Assert.Single(symbol.Parameters).Name);
        Assert.Equal("Width", operation.Property?.Name);
        Assert.Equal(AkcssPropertyValueKind.AmxInvocation, operation.ValueKind);
        Assert.Equal("Double", operation.ValueType.Name);
        var amx = Assert.IsType<AkcssAmxInvocationValue>(operation.ConvertedValue);
        Assert.Equal(AkcssAmxInvocationKind.DynamicResource, amx.Kind);
        Assert.Equal("Double", amx.TypeArgument.Name);
        Assert.Equal("\"--spacing\"", Assert.Single(amx.Arguments).ToString());
        AssertCSharpRootChild(operation, operation.ValueOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.TargetSymbol is ITailwindUtilityParameterSymbol { Name: "value" });
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.CSharpTargetDefinition.Symbol is IMethodSymbol { Name: "DynamicResource" });
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssIfDirective_CreatesNestedOperations()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        @if(true) {\n" +
            "            Background: White;\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var ifDirective = Assert.IsType<AkcssIfDirectiveSyntax>(Assert.Single(rule.Members));
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var ifOperation = Assert.IsAssignableFrom<IAkcssIfOperation>(Assert.Single(symbol.Operations));

        Assert.Same(ifDirective, ifOperation.Syntax);
        Assert.Same(ifOperation, semanticModel.GetOperation(ifDirective));
        Assert.Equal(AkcssPropertyValueKind.ColorLiteral,
            Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(Assert.Single(ifOperation.Operations)).ValueKind);
        Assert.Equal("Boolean", ifOperation.ConditionType.Name);
        Assert.False(ifOperation.ConditionOperation.IsDefault);
        AssertCSharpRootChild(ifOperation, ifOperation.ConditionOperationTree);
        Assert.Equal(2, ifOperation.Children.Length);
        Assert.Same(ifOperation.ConditionOperationTree, ifOperation.Children[0]);
        Assert.Same(Assert.Single(ifOperation.Operations), ifOperation.Children[1]);
        Assert.False(ifOperation.HasErrors);
        Assert.Equal("@if(true)", ifOperation.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_AkcssUtilityIfDirective_BindsConditionInUtilityParameterScope()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            @if(value > 0) {\n" +
            "                Width: value;\n" +
            "            }\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var ifDirective = Assert.IsType<AkcssIfDirectiveSyntax>(Assert.Single(utility.Members));
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utility).Symbol);
        var ifOperation = Assert.IsAssignableFrom<IAkcssIfOperation>(Assert.Single(symbol.Operations));

        Assert.Equal("value", Assert.Single(symbol.Parameters).Name);
        Assert.Same(ifDirective, ifOperation.Syntax);
        Assert.Equal("Boolean", ifOperation.ConditionType.Name);
        Assert.False(ifOperation.ConditionOperation.IsDefault);
        AssertCSharpRootChild(ifOperation, ifOperation.ConditionOperationTree);
        Assert.Contains(EnumerateCSharpOperations(ifOperation), operation =>
            operation.TargetSymbol is ITailwindUtilityParameterSymbol { Name: "value" });
        var setter = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(Assert.Single(ifOperation.Operations));
        Assert.Equal("Width", setter.Property?.Name);
        Assert.Equal("Double", setter.ValueType.Name);
        Assert.False(ifOperation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssInvalidValues_ProduceDiagnostics()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Missing: 1;\n" +
            "        Background: \"#not\";\n" +
            "        Padding: (foo: 1);\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);

        Assert.Equal(3, symbol.Operations.Length);
        var assignments = rule.Members.OfType<AkcssAssignmentSyntax>().ToArray();

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound,
            Assert.Single(semanticModel.GetSemanticDiagnostics(assignments[0])).Code);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInvalidColor,
            Assert.Single(semanticModel.GetSemanticDiagnostics(assignments[1])).Code);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInvalidThickness,
            Assert.Single(semanticModel.GetSemanticDiagnostics(assignments[2])).Code);
        Assert.All(symbol.Operations, operation => Assert.True(operation.HasErrors));
    }

    [Fact]
    public void SemanticModel_BindStateWithInpcSource_HasNoBindingDiagnostics()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string name = bind vm.Name;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "using System.ComponentModel;\n" +
                "public sealed class MyViewModel : INotifyPropertyChanged\n" +
                "{\n" +
                "    public event PropertyChangedEventHandler? PropertyChanged;\n" +
                "    public string Name { get; set; } = \"\";\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "name");

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(semanticModel.GetSymbolInfo(state).Symbol);

        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
        Assert.True(symbol.IsBindable);
        Assert.False(symbol.IsReadOnly);
        Assert.Equal(StateBindingKind.Bind, symbol.BindingKind);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Equal("String", symbol.InitializerType.Name);
    }

    [Fact]
    public void SemanticModel_BindStateWithoutObservableSource_ProducesWarning()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string name = bind vm.Name;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public string Name { get; set; } = \"\";\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "name");

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_StateBindingSourceNotObservable, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("vm.Name", diagnostic.Message);
        Assert.Contains("string", diagnostic.Message);
        Assert.Contains("MyViewModel", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_OutStateWithObservableSource_IsReadonlyAndHasNoBindingDiagnostics()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string fullName = out vm.FullName;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "using System;\n" +
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public IObservable<string> FullName { get; } = null!;\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "fullName");

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(semanticModel.GetSymbolInfo(state).Symbol);

        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
        Assert.True(symbol.IsBindable);
        Assert.True(symbol.IsReadOnly);
        Assert.Equal(StateBindingKind.Out, symbol.BindingKind);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Equal("IObservable", symbol.InitializerType.Name);
    }

    [Fact]
    public void SemanticModel_InStateWithGetOnlyProperty_ProducesWritableTargetError()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string surname = in vm.Surname;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public string Surname { get; } = \"\";\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "surname");

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_StateBindingTargetNotWritable, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("vm.Surname", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_InStateWithReadonlyField_ProducesWritableTargetError()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string surname = in vm.Surname;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public readonly string Surname = \"\";\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "surname");

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_StateBindingTargetNotWritable, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void SemanticModel_InState_AllowsBindingPathExpressions()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state MyViewModel sameVm = in vm;\n" +
            "state string name = in vm.Name;\n" +
            "state string firstName = in vm.Persons[0].Name;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "using System.Collections.Generic;\n" +
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public string Name { get; set; } = \"\";\n" +
                "    public List<Person> Persons { get; } = new();\n" +
                "}\n" +
                "public sealed class Person\n" +
                "{\n" +
                "    public string Name { get; set; } = \"\";\n" +
                "}"));

        Assert.True(semanticModel.GetSemanticDiagnostics(GetStateDeclaration(syntaxTree, "sameVm")).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(GetStateDeclaration(syntaxTree, "name")).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(GetStateDeclaration(syntaxTree, "firstName")).IsEmpty);
    }

    [Fact]
    public void SemanticModel_BindableStateWithNonPathExpression_ProducesBindingExpressionError()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state int age = in vm.Age + 1;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public int Age { get; set; }\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "age");

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_StateBindingExpressionExpected, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("vm.Age + 1", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ComponentCSharpExpressions_ProduceDiagnostics()
    {
        const string code =
            """
            state int count = missing + 1;
            param int UserId = missing;

            useEffect(missing.Name) {
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[0]);
        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[1]);
        var useEffect = Assert.IsType<UseEffectDeclarationSyntax>(root.Members[2]);

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError,
            Assert.Single(semanticModel.GetSemanticDiagnostics(state)).Code);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError,
            Assert.Single(semanticModel.GetSemanticDiagnostics(param)).Code);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError,
            Assert.Single(semanticModel.GetSemanticDiagnostics(useEffect)).Code);

        var rootDiagnostics = semanticModel.GetSemanticDiagnostics(root);
        Assert.Equal(3, rootDiagnostics.Count(static diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError));
    }

    [Fact]
    public void SemanticModel_DuplicateComponentMembers_ProduceDiagnostics()
    {
        const string code =
            """
            using System;

            state int count = 0;
            param int count = 1;
            inject IServiceProvider count;
            command void count();
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        _ = semanticModel.GetSymbolInfo(root);

        var duplicateDiagnostics = semanticModel.GetSemanticDiagnostics(root)
            .Where(static diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_DuplicateComponentMember)
            .ToArray();

        Assert.Equal(3, duplicateDiagnostics.Length);
        Assert.All(duplicateDiagnostics, diagnostic => Assert.Contains("count", diagnostic.Message));
    }

    [Fact]
    public void SemanticModel_SameComponentMemberNamesInDifferentComponents_AreAllowed()
    {
        const string code = "state int count = 0;";
        var firstTree = AkburaSyntaxTree.ParseText(code, "First.akbura");
        var secondTree = AkburaSyntaxTree.ParseText(code, "Second.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [firstTree, secondTree]);

        Assert.True(compilation.GetSemanticModel(firstTree).GetSemanticDiagnostics(firstTree.GetRoot()).IsEmpty);
        Assert.True(compilation.GetSemanticModel(secondTree).GetSemanticDiagnostics(secondTree.GetRoot()).IsEmpty);
    }

    [Fact]
    public void SemanticModel_DuplicateCommandParameters_ProduceDiagnostic()
    {
        const string code = "command void Save(int id, string id);";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var command = Assert.IsType<CommandDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(command));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_DuplicateCommandParameter, diagnostic.Code);
        Assert.Contains("id", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ResolvesAvaloniaMarkupPropertySymbol()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button Content=\"Hello\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbolInfo = semanticModel.GetSymbolInfo(attribute);

        var symbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.Property, symbol.Kind);
        Assert.Equal(SymbolLanguage.Markup, symbol.Language);
        Assert.Equal("Content", symbol.Name);
        Assert.True(symbol.IsAvaloniaProperty);
        Assert.True(symbol.IsClrProperty);
        Assert.False(symbol.IsParameter);
        Assert.True(symbol.CanRead);
        Assert.True(symbol.CanWrite);
        Assert.Equal("ContentProperty", symbol.AvaloniaPropertyDefinition.Name);
        Assert.Equal("Content", symbol.ClrPropertyDefinition.Name);
        Assert.Equal(SpecialType.System_Object, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol).SpecialType);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(attribute);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_PropertyElementNode_BindsAsPropertySetter()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Avalonia.Media;

            <Border>
                <Border.Background>
                    <SolidColorBrush Color="#E4000000" />
                </Border.Background>
            </Border>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var border = GetOnlyMarkupElement(syntaxTree);
        var propertyContent = Assert.IsType<MarkupElementContentSyntax>(Assert.Single(border.Body));
        var propertyElement = propertyContent.Element;
        var brushContent = Assert.IsType<MarkupElementContentSyntax>(Assert.Single(propertyElement.Body));

        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(propertyElement).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupContentOperation>(
            semanticModel.GetOperation(propertyElement));
        var child = Assert.Single(operation.Content);

        Assert.Equal("Border.Background", propertyElement.StartTag!.Name.ToFullString());
        Assert.Equal("Background", property.Name);
        Assert.Equal("Background", property.ClrPropertyDefinition.Name);
        Assert.Equal("BackgroundProperty", property.AvaloniaPropertyDefinition.Name);
        Assert.Equal("IBrush", property.Type.Name);
        Assert.True(property.CanWrite);
        Assert.Same(property, operation.Property);
        Assert.Equal("Border", operation.ContainingComponent!.Name);
        Assert.False(operation.ContentModel.IsCollection);
        Assert.Equal(MarkupChildKind.Element, child.Kind);
        Assert.Same(brushContent, child.Syntax);
        Assert.Equal("SolidColorBrush", child.ComponentSymbol!.Name);
        Assert.Equal("SolidColorBrush", child.Type.Name);
        Assert.True(operation.ValueConversion.IsImplicit);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(propertyElement).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(syntaxTree.GetRoot()).IsEmpty);
    }

    [Fact]
    public void SemanticModel_PropertyElementNode_ReportsInvalidChildType()
    {
        const string code =
            """
            using Avalonia.Controls;

            <Border>
                <Border.Background>
                    <Button />
                </Border.Background>
            </Border>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var border = GetOnlyMarkupElement(syntaxTree);
        var propertyElement = Assert.IsType<MarkupElementContentSyntax>(
            Assert.Single(border.Body)).Element;

        var operation = Assert.IsAssignableFrom<IMarkupContentOperation>(
            semanticModel.GetOperation(propertyElement));
        var diagnostic = Assert.Single(
            semanticModel.GetSemanticDiagnostics(propertyElement),
            static candidate => candidate.Code == ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild);

        Assert.Equal("Background", operation.Property!.Name);
        Assert.True(operation.HasErrors);
        Assert.Contains("Button", diagnostic.Message);
        Assert.Contains("IBrush", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ResolvesAvaloniaAttachedPropertySymbol()
    {
        const string code =
            """
            using Avalonia.Controls;

            <Grid>
                <TextBlock Grid.Column={1}/>
            </Grid>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var grid = GetOnlyMarkupElement(syntaxTree);
        var textBlockContent = Assert.IsType<MarkupElementContentSyntax>(Assert.Single(grid.Body));
        var textBlock = textBlockContent.Element;
        var attribute = Assert.IsType<MarkupAttachedPropertyAttributeSyntax>(Assert.Single(textBlock.StartTag!.Attributes));

        var symbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Equal("Grid", attribute.OwnerType.ToFullString());
        Assert.Equal("Column", symbol.Name);
        Assert.True(symbol.IsAttachedProperty);
        Assert.True(symbol.IsAvaloniaProperty);
        Assert.Equal("ColumnProperty", symbol.AttachedPropertyDefinition.Name);
        Assert.Equal("ColumnProperty", symbol.AvaloniaPropertyDefinition.Name);
        Assert.Equal("GetColumn", symbol.AttachedGetterDefinition.Name);
        Assert.Equal("SetColumn", symbol.AttachedSetterDefinition.Name);
        Assert.Equal("Control", symbol.AttachedTargetType.Name);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol).SpecialType);
        Assert.True(symbol.CanRead);
        Assert.True(symbol.CanWrite);

        var operation = Assert.IsAssignableFrom<IPropertySetterOperation>(semanticModel.GetOperation(attribute));
        var operationProperty = Assert.IsAssignableFrom<AkburaPropertySymbol>(operation.Property);
        Assert.Equal("Column", operationProperty.Name);
        Assert.True(operationProperty.IsAttachedProperty);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(operation.ValueType.Symbol).SpecialType);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_ResolvesCustomAttachedPropertySymbol()
    {
        const string code =
            """
            using MyControls;

            <MyControl global::MyControls.MyAttachedGeneric{int}.Nested.A={1}/>
            """;
        const string csharpCode =
            """
            namespace MyControls;

            public sealed class MyControl : Avalonia.Controls.Control
            {
            }

            public sealed class AttachedProperty<T>
            {
            }

            public static class MyAttachedGeneric<T>
            {
                public static class Nested
                {
                    public static readonly AttachedProperty<int> AProperty = null!;

                    public static void Set(object element, int value)
                    {
                    }

                    public static int Get(object element)
                    {
                        return 0;
                    }
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupAttachedPropertyAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Equal("global::MyControls.MyAttachedGeneric{int}.Nested", attribute.OwnerType.ToFullString());
        Assert.Equal("A", symbol.Name);
        Assert.True(symbol.IsAttachedProperty);
        Assert.False(symbol.IsAvaloniaProperty);
        Assert.Equal("AProperty", symbol.AttachedPropertyDefinition.Name);
        Assert.Equal("Get", symbol.AttachedGetterDefinition.Name);
        Assert.Equal("Set", symbol.AttachedSetterDefinition.Name);
        Assert.Equal(SpecialType.System_Object, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.AttachedTargetType.Symbol).SpecialType);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol).SpecialType);
        Assert.True(symbol.CanRead);
        Assert.True(symbol.CanWrite);

        var operation = Assert.IsAssignableFrom<IPropertySetterOperation>(semanticModel.GetOperation(attribute));
        var operationProperty = Assert.IsAssignableFrom<AkburaPropertySymbol>(operation.Property);
        Assert.Equal("A", operationProperty.Name);
        Assert.True(operationProperty.IsAttachedProperty);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(operation.ValueType.Symbol).SpecialType);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_ResolvesAkburaControlAvaloniaPropertySymbol()
    {
        const string code =
            "using Akbura;\n" +
            "\n" +
            "<AkburaControl Padding=\"12\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Equal("Padding", symbol.Name);
        Assert.True(symbol.IsAvaloniaProperty);
        Assert.True(symbol.IsClrProperty);
        Assert.False(symbol.IsParameter);
        Assert.True(symbol.CanRead);
        Assert.True(symbol.CanWrite);
        Assert.Equal("PaddingProperty", symbol.AvaloniaPropertyDefinition.Name);
        Assert.Equal("Padding", symbol.ClrPropertyDefinition.Name);
        Assert.Equal("Thickness", symbol.Type.Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_ResolvesPlainCSharpMarkupPropertySymbol()
    {
        const string code = "<PlainComponent Title=\"Hello\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class PlainComponent\n" +
                "{\n" +
                "    public string Title { get; set; } = \"\";\n" +
                "}"));
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Equal("Title", symbol.Name);
        Assert.False(symbol.IsAvaloniaProperty);
        Assert.True(symbol.IsClrProperty);
        Assert.False(symbol.IsParameter);
        Assert.True(symbol.CanRead);
        Assert.True(symbol.CanWrite);
        Assert.True(symbol.AvaloniaPropertyDefinition.IsDefault);
        Assert.Equal("Title", symbol.ClrPropertyDefinition.Name);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol).SpecialType);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MissingMarkupProperty_ProducesDiagnostic()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button Missing=\"1\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbolInfo = semanticModel.GetSymbolInfo(attribute);
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.Null(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.NotFound, symbolInfo.CandidateReason);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Missing", diagnostic.Message);
        Assert.Contains("Button", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_MarkupPlainAttribute_CreatesPropertySetterOperation()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button Content=\"Hello\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(Akbura.Language.Operations.OperationKind.MarkupAttribute, operation.Kind);
        Assert.Equal(OperationLanguage.Markup, operation.Language);
        Assert.Equal(MarkupAttributeBindingKind.None, operation.BindingKind);
        Assert.Equal(MarkupAttributeValueKind.Literal, operation.ValueKind);
        Assert.Equal("Hello", operation.LiteralValue);
        Assert.Equal("Content", operation.Property?.Name);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(operation.ValueType.Symbol).SpecialType);
        Assert.False(operation.HasErrors);
        Assert.Same(operation, semanticModel.GetOperation(attribute));
    }

    [Fact]
    public void SemanticModel_MarkupGridDefinitionsLiteral_ConvertsToDefinitionListTypes()
    {
        const string columnDefinitions = "*, 100, Auto, min(100), max(300), min-max(100, 300), min-max(100, *, 300), min-max(0, auto, 100)";
        const string rowDefinitions = "Auto * 2* 48 min-max(0, auto, 100)";
        const string code =
            $$"""
            using Avalonia.Controls;

            <Grid ColumnDefinitions="{{columnDefinitions}}" RowDefinitions="{{rowDefinitions}}" />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var attributes = GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes
            .Cast<MarkupPlainAttributeSyntax>()
            .ToArray();

        Assert.Equal(2, attributes.Length);

        var columnOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attributes[0]));
        var rowOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attributes[1]));

        Assert.Equal("ColumnDefinitions", columnOperation.Property?.Name);
        Assert.Equal(columnDefinitions, columnOperation.LiteralValue);
        Assert.Equal(MarkupAttributeValueKind.Literal, columnOperation.ValueKind);
        Assert.Equal("Avalonia.Controls.ColumnDefinitions", columnOperation.ValueType.Symbol?.ToDisplayString());
        var columnValue = Assert.IsType<GridDefinitionListValue>(columnOperation.ConvertedValue);
        Assert.Equal(columnValue, columnOperation.ConstantValue);
        Assert.Equal(8, columnValue.Definitions.Length);
        AssertDefinition(columnValue.Definitions[0], GridDefinitionUnitType.Star, 1);
        AssertDefinition(columnValue.Definitions[1], GridDefinitionUnitType.Pixel, 100);
        AssertDefinition(columnValue.Definitions[2], GridDefinitionUnitType.Auto, 0);
        AssertDefinition(columnValue.Definitions[3], GridDefinitionUnitType.Star, 1, min: 100);
        AssertDefinition(columnValue.Definitions[4], GridDefinitionUnitType.Star, 1, max: 300);
        AssertDefinition(columnValue.Definitions[5], GridDefinitionUnitType.Star, 1, min: 100, max: 300);
        AssertDefinition(columnValue.Definitions[6], GridDefinitionUnitType.Star, 1, min: 100, max: 300);
        AssertDefinition(columnValue.Definitions[7], GridDefinitionUnitType.Auto, 0, min: 0, max: 100);
        Assert.False(columnOperation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attributes[0]).IsEmpty);

        Assert.Equal("RowDefinitions", rowOperation.Property?.Name);
        Assert.Equal(rowDefinitions, rowOperation.LiteralValue);
        Assert.Equal(MarkupAttributeValueKind.Literal, rowOperation.ValueKind);
        Assert.Equal("Avalonia.Controls.RowDefinitions", rowOperation.ValueType.Symbol?.ToDisplayString());
        var rowValue = Assert.IsType<GridDefinitionListValue>(rowOperation.ConvertedValue);
        Assert.Equal(rowValue, rowOperation.ConstantValue);
        Assert.Equal(5, rowValue.Definitions.Length);
        AssertDefinition(rowValue.Definitions[0], GridDefinitionUnitType.Auto, 0);
        AssertDefinition(rowValue.Definitions[1], GridDefinitionUnitType.Star, 1);
        AssertDefinition(rowValue.Definitions[2], GridDefinitionUnitType.Star, 2);
        AssertDefinition(rowValue.Definitions[3], GridDefinitionUnitType.Pixel, 48);
        AssertDefinition(rowValue.Definitions[4], GridDefinitionUnitType.Auto, 0, min: 0, max: 100);
        Assert.False(rowOperation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attributes[1]).IsEmpty);

        static void AssertDefinition(
            GridDefinitionValue actual,
            GridDefinitionUnitType unitType,
            double value,
            double? min = null,
            double? max = null)
        {
            Assert.Equal(unitType, actual.Length.UnitType);
            Assert.Equal(value, actual.Length.Value);
            Assert.Equal(min, actual.Min);
            Assert.Equal(max, actual.Max);
        }
    }

    [Fact]
    public void SemanticModel_MarkupGridDefinitionsLiteral_InvalidDefinitionProducesDiagnostic()
    {
        const string code =
            """
            using Avalonia.Controls;

            <Grid ColumnDefinitions="*, nope" RowDefinitions="min-max(100, auto)" />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var attributes = GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes
            .Cast<MarkupPlainAttributeSyntax>()
            .ToArray();

        Assert.Equal(2, attributes.Length);

        foreach (var attribute in attributes)
        {
            var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
                semanticModel.GetOperation(attribute));
            var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

            Assert.True(operation.HasErrors);
            Assert.Null(operation.ConvertedValue);
            Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert, diagnostic.Code);
            Assert.Contains(attribute.Name.Identifier.ValueText, diagnostic.Message);
        }
    }

    [Fact]
    public void SemanticModel_MarkupExtensionAttribute_ResolvesExtensionSuffixAndProvideValue()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            <Button Content=${StaticResource 123} />
            """;
        const string csharpCode =
            """
            namespace Demo.Extensions;

            public sealed class StaticResourceExtension
            {
                public StaticResourceExtension(object resourceKey)
                {
                    ResourceKey = resourceKey;
                }

                public object? ResourceKey { get; set; }

                public string ProvideValue(System.IServiceProvider serviceProvider)
                {
                    return ResourceKey?.ToString() ?? "";
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupAttributeValueKind.MarkupExtension, operation.ValueKind);
        Assert.Equal("Content", operation.Property?.Name);
        Assert.True(
            operation.ConvertedValue is MarkupExtensionValue,
            string.Join(" | ", semanticModel.GetSemanticDiagnostics(attribute).Select(diagnostic => diagnostic.Message)));
        var value = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);
        Assert.Equal(value, operation.ConstantValue);
        Assert.Equal("StaticResource", value.Name);
        Assert.Equal("StaticResourceExtension", value.ExtensionType.Name);
        Assert.Equal(".ctor", value.Constructor.Name);
        Assert.Equal("ProvideValue", value.ProvideValueMethod.Name);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(value.ResultType.Symbol).SpecialType);
        var argument = Assert.Single(value.Arguments);
        Assert.Equal("123", argument.Text);
        Assert.Equal("resourceKey", argument.Parameter.Name);
        Assert.Empty(value.Properties);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupExtensionAttribute_ResolvesGlobalQualifiedExtension()
    {
        const string code =
            """
            using Avalonia.Controls;

            <Button Content=${global::Demo.Extensions.StaticResourceExtension 123} />
            """;
        const string csharpCode =
            """
            namespace Demo.Extensions;

            public sealed class StaticResourceExtension
            {
                public StaticResourceExtension(object resourceKey)
                {
                }

                public object? ProvideValue(System.IServiceProvider serviceProvider)
                {
                    return null;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var value = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);

        Assert.Equal(MarkupAttributeValueKind.MarkupExtension, operation.ValueKind);
        Assert.Equal("global::Demo.Extensions.StaticResourceExtension", value.Name);
        Assert.Equal("Demo.Extensions.StaticResourceExtension", value.ExtensionType.Symbol?.ToDisplayString());
        Assert.Equal("123", Assert.Single(value.Arguments).Text);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_MarkupExtensionAttribute_FindsProvideValueOnBaseType()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            <Button Content=${Derived Key=Hello} />
            """;
        const string csharpCode =
            """
            namespace Demo.Extensions;

            public abstract class BaseExtension
            {
                public object? ProvideValue(System.IServiceProvider serviceProvider)
                {
                    return null;
                }
            }

            public sealed class DerivedExtension : BaseExtension
            {
                public string? Key { get; set; }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var value = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);

        Assert.Equal("DerivedExtension", value.ExtensionType.Name);
        Assert.Equal("BaseExtension", value.ProvideValueMethod.Symbol?.ContainingType.Name);
        var property = Assert.Single(value.Properties);
        Assert.Equal("Key", property.Name);
        Assert.Equal("Hello", property.Value);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_MarkupExtensionAttribute_PreservesBindingRichSyntax()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            <TextBlock Text=${Binding #input.Text, Mode=TwoWay, StringFormat='{}{0} items'} />
            """;
        const string csharpCode =
            """
            namespace Demo.Extensions;

            public sealed class BindingExtension
            {
                public BindingExtension(object path)
                {
                    Path = path;
                }

                public object? Path { get; }

                public string? Mode { get; set; }

                public string? StringFormat { get; set; }

                public object? ProvideValue(System.IServiceProvider serviceProvider)
                {
                    return null;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var value = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);

        Assert.Equal(MarkupAttributeValueKind.MarkupExtension, operation.ValueKind);
        Assert.Equal("Binding", value.Name);
        Assert.Equal("BindingExtension", value.ExtensionType.Name);
        Assert.Equal("#input.Text", Assert.Single(value.Arguments).Text);
        Assert.Collection(
            value.Properties,
            mode =>
            {
                Assert.Equal("Mode", mode.Name);
                Assert.Equal("TwoWay", mode.Value);
            },
            stringFormat =>
            {
                Assert.Equal("StringFormat", stringFormat.Name);
                Assert.Equal("'{}{0} items'", stringFormat.Value);
            });
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupExtensionAttribute_BindsNamedExpressionAndNestedExtension()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            state int mystate = 1;

            <Button Content=${MyMx 123, Hello, Property={mystate + 1}, Binding=${Binding Hello}} />
            """;
        const string csharpCode =
            """
            namespace Demo.Extensions;

            public sealed class MyMxExtension
            {
                public MyMxExtension(object first, object second)
                {
                }

                public int Property { get; set; }

                public object? Binding { get; set; }

                public object? ProvideValue(System.IServiceProvider serviceProvider)
                {
                    return null;
                }
            }

            public sealed class BindingExtension
            {
                public BindingExtension(object path)
                {
                }

                public object? ProvideValue(System.IServiceProvider serviceProvider)
                {
                    return null;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes));
        var syntaxValue = Assert.IsType<MarkupExtensionAttributeValueSyntax>(attribute.Value);

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var value = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);

        Assert.Equal("MyMx", value.Name);
        Assert.Equal(2, value.Arguments.Length);
        Assert.Equal(["123", "Hello"], value.Arguments.Select(argument => argument.Text).ToArray());
        Assert.Collection(
            value.Properties,
            property =>
            {
                Assert.Equal("Property", property.Name);
                Assert.Equal("int", property.Type.Symbol?.ToDisplayString());
                Assert.False(property.Operation.IsDefault);
            },
            property =>
            {
                Assert.Equal("Binding", property.Name);
                Assert.NotNull(property.NestedValue);
                Assert.Equal("Binding", property.NestedValue!.Name);
                Assert.Equal("Hello", Assert.Single(property.NestedValue.Arguments).Text);
            });

        var references = semanticModel.GetCSharpSymbolReferences(attribute);
        Assert.Contains(references, reference => reference.AkburaSymbol is IStateSymbol { Name: "mystate" });
        Assert.DoesNotContain(references, reference => reference.Name == "Hello");
        Assert.False(operation.HasErrors, string.Join(" | ", semanticModel.GetSemanticDiagnostics(attribute).Select(diagnostic => diagnostic.Message)));
        Assert.True(semanticModel.GetSemanticDiagnostics(syntaxValue).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupExtensionAttribute_BindingWithoutDataTypeCreatesReflectionBindingPayload()
    {
        const string code =
            """
            using Avalonia.Controls;

            <TextBlock Text=${Binding Name, Mode=TwoWay} />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(AvaloniaBindingCSharpCode));
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var value = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);
        var binding = value.Binding;
        Assert.NotNull(binding);

        Assert.Equal(MarkupAttributeValueKind.MarkupExtension, operation.ValueKind);
        Assert.Equal(MarkupBindingKind.Reflection, binding!.Kind);
        Assert.Equal("Name", binding.Path);
        Assert.Equal("Binding", binding.BindingType.Name);
        Assert.Equal("BindingBase", value.ResultType.Name);
        Assert.True(binding.SourceType.IsDefault);
        Assert.Collection(
            value.Properties,
            mode =>
            {
                Assert.Equal("Mode", mode.Name);
                Assert.Equal("TwoWay", mode.Value);
                Assert.Equal("Avalonia.Data.BindingMode", mode.Type.Symbol?.ToDisplayString());
                Assert.False(mode.Operation.IsDefault);
            });
        Assert.True(
            semanticModel.GetSemanticDiagnostics(attribute).IsEmpty,
            string.Join(" | ", semanticModel.GetSemanticDiagnostics(attribute).Select(diagnostic => diagnostic.Message)));
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_MarkupExtensionAttribute_BindingWithDataTypeCreatesCompiledBindingPath()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Demo;

            <StackPanel x.DataType="Demo.ViewModel">
                <TextBlock Text=${Binding User.Name} />
            </StackPanel>
            """;
        const string csharpCode =
            """
            namespace Demo;

            public sealed class ViewModel
            {
                public User User { get; } = new();
            }

            public sealed class User
            {
                public string Name { get; } = "";
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(AvaloniaBindingCSharpCode, csharpCode));
        var stackPanel = GetOnlyMarkupElement(syntaxTree);
        var dataTypeAttribute = Assert.IsType<MarkupAttachedPropertyAttributeSyntax>(
            Assert.Single(stackPanel.StartTag!.Attributes));
        var textBlock = Assert.IsType<MarkupElementContentSyntax>(
            Assert.Single(stackPanel.Body)).Element;
        var textAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(textBlock.StartTag!.Attributes));

        var dataTypeOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(dataTypeAttribute));
        var dataTypeValue = Assert.IsType<CSharpSymbolDefinition>(dataTypeOperation.ConvertedValue);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(textAttribute));
        var value = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);
        var binding = value.Binding;
        Assert.NotNull(binding);

        Assert.Equal("Demo.ViewModel", dataTypeValue.Symbol?.ToDisplayString());
        Assert.Equal(MarkupBindingKind.Compiled, binding!.Kind);
        Assert.Equal("User.Name", binding.Path);
        Assert.Equal("CompiledBinding", binding.BindingType.Name);
        Assert.Equal("Demo.ViewModel", binding.SourceType.Symbol?.ToDisplayString());
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(binding.ResultType.Symbol).SpecialType);
        Assert.Collection(
            binding.PathElements,
            user =>
            {
                Assert.Equal(MarkupBindingPathElementKind.Property, user.Kind);
                Assert.Equal("User", user.Text);
                Assert.Equal("Demo.User", user.Type.Symbol?.ToDisplayString());
            },
            name =>
            {
                Assert.Equal(MarkupBindingPathElementKind.Property, name.Kind);
                Assert.Equal("Name", name.Text);
                Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(name.Type.Symbol).SpecialType);
            });
        Assert.True(semanticModel.GetSemanticDiagnostics(dataTypeAttribute).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(textAttribute).IsEmpty);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_ItemTemplateBinding_InheritsDataTypeFromItemsSource()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Demo;

            inject ViewModel Vm;

            <ItemsControl ItemsSource={Vm.Items}>
                <ItemsControl.ItemTemplate>
                    <TextBlock Text=${Binding FullName} />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        const string csharpCode =
            """
            namespace Demo;

            public sealed class ViewModel
            {
                public System.Collections.Generic.IReadOnlyList<VmItem> Items { get; } = null!;
            }

            public sealed class VmItem
            {
                public string FullName { get; } = "";
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(AvaloniaBindingCSharpCode, csharpCode));
        var itemsControl = GetOnlyMarkupElement(syntaxTree);
        var itemTemplate = Assert.IsType<MarkupElementContentSyntax>(
            Assert.Single(itemsControl.Body, static content =>
                content.Kind == Akbura.Language.Syntax.SyntaxKind.MarkupElementContentSyntax)).Element;
        var textBlock = Assert.IsType<MarkupElementContentSyntax>(
            Assert.Single(itemTemplate.Body, static content =>
                content.Kind == Akbura.Language.Syntax.SyntaxKind.MarkupElementContentSyntax)).Element;
        var textAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(textBlock.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(textAttribute));
        var extension = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);
        var binding = Assert.IsType<MarkupBindingValue>(extension.Binding);

        Assert.Equal(MarkupBindingKind.Compiled, binding.Kind);
        Assert.Equal("Demo.VmItem", binding.SourceType.Symbol?.ToDisplayString());
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(binding.ResultType.Symbol).SpecialType);
        var fullName = Assert.Single(binding.PathElements);
        Assert.Equal(MarkupBindingPathElementKind.Property, fullName.Kind);
        Assert.Equal("FullName", fullName.Symbol.Name);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(fullName.Type.Symbol).SpecialType);
        Assert.True(
            semanticModel.GetSemanticDiagnostics(textAttribute).IsEmpty,
            string.Join(" | ", semanticModel.GetSemanticDiagnostics(textAttribute).Select(static diagnostic => diagnostic.Message)));
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_ItemTemplateItemName_DeclaresTypedMarkupItemForCSharpExpressions()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Demo;

            inject ViewModel Vm;

            <ItemsControl ItemsSource={Vm.Items}>
                <ItemsControl.ItemTemplate x.ItemName="item">
                    <TextBlock Text={item.FullName + " without MarkupExtension!"} />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        const string csharpCode =
            """
            namespace Demo;

            public sealed class ViewModel
            {
                public System.Collections.Generic.IEnumerable<VmItem> Items { get; } = null!;
            }

            public sealed class VmItem
            {
                public string FullName { get; } = "";
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var itemsControl = GetOnlyMarkupElement(syntaxTree);
        var itemTemplate = Assert.IsType<MarkupElementContentSyntax>(
            Assert.Single(itemsControl.Body, static content =>
                content.Kind == Akbura.Language.Syntax.SyntaxKind.MarkupElementContentSyntax)).Element;
        var itemNameAttribute = Assert.IsType<MarkupAttachedPropertyAttributeSyntax>(
            Assert.Single(itemTemplate.StartTag!.Attributes));
        var textBlock = Assert.IsType<MarkupElementContentSyntax>(
            Assert.Single(itemTemplate.Body, static content =>
                content.Kind == Akbura.Language.Syntax.SyntaxKind.MarkupElementContentSyntax)).Element;
        var textAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(textBlock.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(textAttribute));
        var itemOperation = Assert.Single(
            EnumerateCSharpOperations(operation),
            static candidate => candidate.TargetSymbol is IMarkupItemSymbol);
        var itemSymbol = Assert.IsAssignableFrom<IMarkupItemSymbol>(itemOperation.TargetSymbol);
        var references = semanticModel.GetCSharpSymbolReferences(textAttribute);

        Assert.Equal("ItemName", semanticModel.GetSymbolInfo(itemNameAttribute).Symbol?.Name);
        Assert.Equal("item", itemSymbol.Name);
        Assert.Equal("Demo.VmItem", itemSymbol.Type.Symbol?.ToDisplayString());
        var itemReference = Assert.Single(references, static reference =>
            reference.AkburaSymbol is IMarkupItemSymbol { Name: "item" });
        Assert.Same(itemSymbol, itemReference.AkburaSymbol);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(operation.ValueType.Symbol).SpecialType);
        Assert.True(
            semanticModel.GetSemanticDiagnostics(textAttribute).IsEmpty,
            string.Join(" | ", semanticModel.GetSemanticDiagnostics(textAttribute).Select(static diagnostic => diagnostic.Message)));
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_ItemTemplateBinding_UsesConfiguredItemsAncestorType()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Demo;

            inject ViewModel Vm;

            <ItemsHost ItemsSource={Vm.Items}>
                <TemplateCarrier>
                    <TemplateCarrier.ItemTemplate>
                        <TextBlock Text=${Binding FullName} />
                    </TemplateCarrier.ItemTemplate>
                </TemplateCarrier>
            </ItemsHost>
            """;
        const string csharpCode =
            """
            namespace Avalonia.Metadata
            {
                [System.AttributeUsage(System.AttributeTargets.Property)]
                public sealed class InheritDataTypeFromItemsAttribute : System.Attribute
                {
                    public InheritDataTypeFromItemsAttribute(string ancestorItemsProperty)
                    {
                        AncestorItemsProperty = ancestorItemsProperty;
                    }

                    public string AncestorItemsProperty { get; }

                    public System.Type? AncestorType { get; set; }
                }
            }

            namespace Demo
            {
                public sealed class ViewModel
                {
                    public System.Collections.Generic.IEnumerable<VmItem> Items { get; } = null!;
                }

                public sealed class VmItem
                {
                    public string FullName { get; } = "";
                }

                public sealed class ItemsHost
                {
                    public System.Collections.IEnumerable? ItemsSource { get; set; }
                }

                public sealed class TemplateCarrier
                {
                    [Avalonia.Metadata.InheritDataTypeFromItems(
                        "ItemsSource",
                        AncestorType = typeof(ItemsHost))]
                    public object? ItemTemplate { get; set; }
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(AvaloniaBindingCSharpCode, csharpCode));
        var itemsHost = GetOnlyMarkupElement(syntaxTree);
        var templateCarrier = Assert.IsType<MarkupElementContentSyntax>(
            Assert.Single(itemsHost.Body, static content =>
                content.Kind == Akbura.Language.Syntax.SyntaxKind.MarkupElementContentSyntax)).Element;
        var itemTemplate = Assert.IsType<MarkupElementContentSyntax>(
            Assert.Single(templateCarrier.Body, static content =>
                content.Kind == Akbura.Language.Syntax.SyntaxKind.MarkupElementContentSyntax)).Element;
        var textBlock = Assert.IsType<MarkupElementContentSyntax>(
            Assert.Single(itemTemplate.Body, static content =>
                content.Kind == Akbura.Language.Syntax.SyntaxKind.MarkupElementContentSyntax)).Element;
        var textAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(textBlock.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(textAttribute));
        var extension = Assert.IsType<MarkupExtensionValue>(operation.ConvertedValue);
        var binding = Assert.IsType<MarkupBindingValue>(extension.Binding);

        Assert.Equal(MarkupBindingKind.Compiled, binding.Kind);
        Assert.Equal("Demo.VmItem", binding.SourceType.Symbol?.ToDisplayString());
        Assert.Equal("FullName", Assert.Single(binding.PathElements).Symbol.Name);
        Assert.True(
            semanticModel.GetSemanticDiagnostics(textAttribute).IsEmpty,
            string.Join(" | ", semanticModel.GetSemanticDiagnostics(textAttribute).Select(static diagnostic => diagnostic.Message)));
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_MarkupDynamicAttribute_BindsInComponentScope()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state bool isOpen = false;\n" +
            "\n" +
            "<Button IsVisible={isOpen} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, operation.ValueKind);
        Assert.Equal("IsVisible", operation.Property?.Name);
        Assert.Equal(SpecialType.System_Boolean, Assert.IsAssignableFrom<INamedTypeSymbol>(operation.ValueType.Symbol).SpecialType);
        Assert.False(operation.ValueOperation.IsDefault);
        AssertCSharpRootChild(operation, operation.ValueOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.TargetSymbol is IStateSymbol { Name: "isOpen" });
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupDynamicAttribute_CreatesCSharpOperationTreeForInterpolatedString()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            <TextBlock Text={$"Count: {count}"} />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, operation.ValueKind);
        Assert.Equal("String", operation.ValueType.Name);
        AssertCSharpRootChild(operation, operation.ValueOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.RoslynKind == Microsoft.CodeAnalysis.OperationKind.InterpolatedString);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.TargetSymbol is IStateSymbol { Name: "count" });
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupDynamicAttribute_BindsCSharpBlockScope()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int total = 1;

            if(total > 0)
            {
                int local = 2;
                <TextBlock Tag={local + total} />
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(syntaxTree.GetRoot().Members[2]);
        Assert.NotNull(ifStatement.Body);
        var markup = Assert.IsType<MarkupRootSyntax>(ifStatement.Body!.Tokens[1]);
        var attribute = Assert.Single(markup.Element.StartTag!.Attributes);

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, operation.ValueKind);
        Assert.Equal("Tag", operation.Property?.Name);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(operation.ValueType.Symbol).SpecialType);
        Assert.False(operation.ValueOperation.IsDefault);
        AssertCSharpRootChild(operation, operation.ValueOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.TargetSymbol is IStateSymbol { Name: "total" });
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.CSharpTargetDefinition.Symbol is ILocalSymbol { Name: "local" });
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_BindsCSharpBlockScope()
    {
        const string code =
            """
            using Avalonia.Controls;

            @akcss {
                @utilities {
                    .w-(double value) { Width: value; }
                }
            }

            state int total = 1;

            if(total > 0)
            {
                int local = 2;
                <Button w-{local + total} {total > local}:w-5 />
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(syntaxTree.GetRoot().Members[3]);
        Assert.NotNull(ifStatement.Body);
        var markup = Assert.IsType<MarkupRootSyntax>(ifStatement.Body!.Tokens[1]);
        var attributes = markup.Element.StartTag!.Attributes;
        var expressionAttribute = Assert.IsAssignableFrom<TailwindAttributeSyntax>(attributes[0]);
        var conditionalAttribute = Assert.IsAssignableFrom<TailwindAttributeSyntax>(attributes[1]);

        var expressionOperation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(expressionAttribute));
        var conditionalOperation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(conditionalAttribute));

        var argument = Assert.Single(expressionOperation.Arguments);
        Assert.Equal("w", expressionOperation.UtilityName);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(argument.Type.Symbol).SpecialType);
        Assert.False(argument.ValueOperation.IsDefault);
        AssertCSharpRootChild(expressionOperation, argument.ValueOperationTree);
        Assert.Contains(EnumerateCSharpOperations(expressionOperation), csharpOperation =>
            csharpOperation.TargetSymbol is IStateSymbol { Name: "total" });
        Assert.Contains(EnumerateCSharpOperations(expressionOperation), csharpOperation =>
            csharpOperation.CSharpTargetDefinition.Symbol is ILocalSymbol { Name: "local" });
        Assert.False(expressionOperation.HasErrors);

        Assert.True(conditionalOperation.HasCondition);
        Assert.Equal(SpecialType.System_Boolean, Assert.IsAssignableFrom<INamedTypeSymbol>(conditionalOperation.ConditionType.Symbol).SpecialType);
        Assert.False(conditionalOperation.ConditionOperation.IsDefault);
        AssertCSharpRootChild(conditionalOperation, conditionalOperation.ConditionOperationTree);
        Assert.Contains(EnumerateCSharpOperations(conditionalOperation), csharpOperation =>
            csharpOperation.TargetSymbol is IStateSymbol { Name: "total" });
        Assert.Contains(EnumerateCSharpOperations(conditionalOperation), csharpOperation =>
            csharpOperation.CSharpTargetDefinition.Symbol is ILocalSymbol { Name: "local" });
        Assert.False(conditionalOperation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(expressionAttribute).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(conditionalAttribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_BindsEnumIdentifierSegments()
    {
        const string code =
            """
            using Demo;
            using Avalonia.Controls;

            @akcss {
                @utilities {
                    .mypad-(MyEnum myEnum) {
                        @if(myEnum == MyEnum.horizontal) {
                            Padding: (horizontal: 10);
                        }

                        @if(myEnum == MyEnum.vertical) {
                            Padding: (vertical: 10);
                        }
                    }
                }
            }

            <TextBlock mypad-horizontal mypad-{MyEnum.vertical}/>
            """;
        const string csharpCode =
            """
            namespace Demo;

            public enum MyEnum
            {
                horizontal,
                vertical,
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var utilitySymbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utility).Symbol);
        var attributes = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TailwindFullAttributeSyntax>()
            .ToArray();

        Assert.Equal("MyEnum", Assert.Single(utilitySymbol.Parameters).Type.Name);
        Assert.Equal(2, utilitySymbol.Operations.Length);
        Assert.All(utilitySymbol.Operations, operation =>
        {
            var ifOperation = Assert.IsAssignableFrom<IAkcssIfOperation>(operation);
            Assert.False(ifOperation.HasErrors);
            Assert.True(semanticModel.GetSemanticDiagnostics(ifOperation.Syntax).IsEmpty);
        });

        Assert.Equal(2, attributes.Length);
        var plainOperation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attributes[0]));
        var expressionOperation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attributes[1]));
        var plainArgument = Assert.Single(plainOperation.Arguments);
        var expressionArgument = Assert.Single(expressionOperation.Arguments);

        Assert.Equal("MyEnum", plainArgument.Type.Name);
        Assert.Equal(0, plainArgument.ConstantValue);
        Assert.Equal("MyEnum", expressionArgument.Type.Name);
        Assert.False(expressionArgument.ValueOperation.IsDefault);
        Assert.False(plainOperation.HasErrors);
        Assert.False(expressionOperation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attributes[0]).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(attributes[1]).IsEmpty);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_InvalidEnumSegment_ProducesArgumentMismatch()
    {
        const string code =
            """
            using Avalonia.Controls;

            @akcss {
                @utilities {
                    .mypad-(MyEnum myEnum) {
                        Padding: (horizontal: 10);
                    }
                }
            }

            <TextBlock mypad-unknown/>
            """;
        const string csharpCode =
            """
            public enum MyEnum
            {
                horizontal,
                vertical,
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var attribute = Assert.Single(syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TailwindFullAttributeSyntax>());

        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityArgumentMismatch, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_MarkupInterpolatedStringAndEventExpression_BindComponentScope()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            <StackPanel>
                <TextBlock Text={$"Count: {count}"}/>
                <Button Click={count++}>Increment</Button>
            </StackPanel>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var attributes = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MarkupPlainAttributeSyntax>()
            .ToArray();
        var textAttribute = Assert.Single(attributes, attribute => attribute.Name.Identifier.ValueText == "Text");
        var clickAttribute = Assert.Single(attributes, attribute => attribute.Name.Identifier.ValueText == "Click");

        var textOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(textAttribute));
        var clickOperation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(clickAttribute));

        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, textOperation.ValueKind);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(textOperation.ValueType.Symbol).SpecialType);
        Assert.False(textOperation.ValueOperation.IsDefault);
        Assert.False(textOperation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(textAttribute).IsEmpty);

        Assert.Equal(MarkupCommandHandlerKind.Expression, clickOperation.HandlerKind);
        Assert.False(clickOperation.HandlerOperation.IsDefault);
        Assert.False(clickOperation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(clickAttribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupInvalidCSharpExpression_ProducesDiagnostic()
    {
        const string code =
            """
            using Avalonia.Controls;

            <TextBlock Text={$"Count: {missing}"} />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError, diagnostic.Code);
        Assert.Contains("missing", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_MarkupEventInvalidCSharpExpression_ProducesDiagnostic()
    {
        const string code =
            """
            using Avalonia.Controls;

            <Button Click={() => missing.Method()} />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(
            semanticModel.GetSemanticDiagnostics(attribute),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError);

        Assert.True(operation.HasErrors);
        Assert.Contains("missing", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_DocumentDiagnostics_AggregateNestedMarkupAndAkcssDiagnostics()
    {
        const string code =
            """
            using Avalonia.Controls;

            @akcss {
                .bad {
                    Padding: FontWeight.Bold;
                }
            }

            <StackPanel>
                <TextBlock Text={missingAttribute}/>
                <TextBlock>{missingContent}</TextBlock>
            </StackPanel>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);

        var diagnostics = semanticModel.GetSemanticDiagnostics(syntaxTree.GetRoot());

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError &&
            diagnostic.Message.Contains("missingAttribute"));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError &&
            diagnostic.Message.Contains("missingContent"));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_AkcssValueCannotConvert &&
            diagnostic.Message.Contains("Padding"));
    }

    [Fact]
    public void SemanticModel_TailwindInvalidExpressionSegments_ProduceDiagnostics()
    {
        const string code =
            """
            using Avalonia.Controls;

            @akcss {
                @utilities {
                    .w-(double value) { Width: value; }
                    .hidden { IsVisible: false; }
                }
            }

            <Button w-{missingValue} {missingCondition}:hidden />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var attributes = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TailwindAttributeSyntax>()
            .ToArray();

        Assert.Equal(2, attributes.Length);
        foreach (var attribute in attributes)
        {
            var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
                semanticModel.GetOperation(attribute));
            var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

            Assert.True(operation.HasErrors);
            Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError, diagnostic.Code);
        }
    }

    [Fact]
    public void SemanticModel_InaccessibleMarkupPropertyOrEvent_ProducesDiagnostic()
    {
        const string code =
            """
            using Demo;

            <SecretControl Secret="x" Hidden={() => {}} />
            """;
        const string csharpCode =
            """
            namespace Demo
            {
                public sealed class SecretControl : Avalonia.Controls.Control
                {
                    private string Secret { get; set; } = "";
                    internal event System.EventHandler? Hidden;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var attributes = GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes
            .Cast<MarkupPlainAttributeSyntax>()
            .ToArray();

        foreach (var attribute in attributes)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(attribute);
            var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

            Assert.Null(symbolInfo.Symbol);
            Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_InaccessibleMember, diagnostic.Code);
            Assert.Contains(attribute.Name.Identifier.ValueText, diagnostic.Message);
        }
    }

    [Fact]
    public void SemanticModel_MarkupAttributeInvalidConversions_ProduceDiagnostics()
    {
        const string code =
            """
            using Avalonia.Controls;

            <Button Width={"wide"} IsVisible={1} />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var attributes = GetOnlyMarkupElement(syntaxTree).StartTag!.Attributes
            .Cast<MarkupPlainAttributeSyntax>()
            .ToArray();

        Assert.Equal(2, attributes.Length);
        foreach (var attribute in attributes)
        {
            var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
                semanticModel.GetOperation(attribute));
            var diagnostics = semanticModel.GetSemanticDiagnostics(attribute);

            Assert.True(operation.HasErrors);
            Assert.Contains(diagnostics, diagnostic =>
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert &&
                diagnostic.Message.Contains(attribute.Name.Identifier.ValueText));
        }
    }

    [Fact]
    public void SemanticModel_ResolvesAvaloniaRoutedEventSymbol()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button Click={count++} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbol = Assert.IsAssignableFrom<IRoutedEventSymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Equal(AkburaSymbolKind.Event, symbol.Kind);
        Assert.Equal(SymbolLanguage.Markup, symbol.Language);
        Assert.Equal("Click", symbol.Name);
        Assert.True(symbol.IsAvaloniaRoutedEvent);
        Assert.True(symbol.IsClrEvent);
        Assert.Equal("ClickEvent", symbol.RoutedEventDefinition.Name);
        Assert.Equal("Click", symbol.ClrEventDefinition.Name);
        Assert.Equal("EventHandler", symbol.HandlerType.Name);
        Assert.Equal("RoutedEventArgs", symbol.EventArgsType.Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupRoutedEventAttribute_BindsSimpleExpressionHandler()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button Click={count++} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(Akbura.Language.Operations.OperationKind.MarkupEventBinding, operation.Kind);
        Assert.Equal("Click", operation.Event.Name);
        Assert.Same(operation.Event, operation.TargetSymbol);
        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, operation.ValueKind);
        Assert.Equal(MarkupCommandHandlerKind.Expression, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.IgnoresCommandArgument, operation.ArgumentMode);
        Assert.Equal(0, operation.HandlerParameterCount);
        Assert.False(operation.IsAsync);
        Assert.False(operation.ContainsAwait);
        Assert.False(operation.HandlerOperation.IsDefault);
        AssertCSharpRootChild(operation, operation.HandlerOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.TargetSymbol is IStateSymbol { Name: "count" });
        Assert.Equal("RoutedEventArgs", operation.EventArgsType.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Theory]
    [InlineData("(sender, args) => count++", 2, false)]
    [InlineData("(_, args) => { if(count == 5) { Console.WriteLine(\"Hello!\"); } count++; }", 2, false)]
    public void SemanticModel_MarkupRoutedEventAttribute_BindsLambdaHandlers(
        string handler,
        int expectedParameterCount,
        bool expectedAsync)
    {
        var code =
            "using Avalonia.Controls;\n" +
            "using System;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button Click={" + handler + "} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal("Click", operation.Event.Name);
        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.ReceivesCommandArgument, operation.ArgumentMode);
        Assert.Equal(expectedParameterCount, operation.HandlerParameterCount);
        Assert.Equal(expectedAsync, operation.IsAsync);
        Assert.False(operation.HandlerOperation.IsDefault);
        AssertCSharpRootChild(operation, operation.HandlerOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.TargetSymbol is IStateSymbol { Name: "count" });
        Assert.Equal("EventHandler", operation.HandlerType.Name);
        Assert.Equal("RoutedEventArgs", operation.EventArgsType.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupRoutedEventAttribute_BindsCSharpBlockLocalScope()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            if(count >= 0)
            {
                var delta = 2;
                <Button Click={(sender, args) => { count += delta; }} />
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var conditional = Assert.IsType<CSharpStatementSyntax>(syntaxTree.GetRoot().Members[2]);
        var markupRoot = Assert.IsType<MarkupRootSyntax>(
            Assert.Single(conditional.Body!.Tokens.OfType<MarkupRootSyntax>()));
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(markupRoot.Element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(2, operation.HandlerParameterCount);
        Assert.False(operation.HandlerOperation.IsDefault);
        AssertCSharpRootChild(operation, operation.HandlerOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.TargetSymbol is IStateSymbol { Name: "count" });
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.CSharpTargetDefinition.Symbol is ILocalSymbol { Name: "delta" });
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Theory]
    [InlineData("bind:Click={count++}")]
    [InlineData("out:Click={count++}")]
    public void SemanticModel_MarkupRoutedEventAttribute_RejectsDirectionalBinding(string attributeText)
    {
        var code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button " + attributeText + " />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<MarkupAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupEventBindingNotAllowed, diagnostic.Code);
        Assert.Contains("Click", diagnostic.Message);
    }

    [Theory]
    [InlineData("(sender) => count++")]
    [InlineData("(string sender, args) => count++")]
    public void SemanticModel_MarkupRoutedEventAttribute_RejectsInvalidHandlerSignature(string handler)
    {
        var code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button Click={" + handler + "} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupEventHandlerSignatureMismatch, diagnostic.Code);
        Assert.Contains("Click", diagnostic.Message);
        Assert.Contains("EventArgs", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_MarkupPrefixedAttributes_PreserveBindingKind()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state string name = \"\";\n" +
            "\n" +
            "<TextBox bind:Text={name} out:Watermark={name} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attributes = element.StartTag!.Attributes;

        var bindOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attributes[0]));
        var outOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attributes[1]));

        Assert.Equal(MarkupAttributeBindingKind.Bind, bindOperation.BindingKind);
        Assert.Equal("Text", bindOperation.Property?.Name);
        Assert.Equal(MarkupAttributeBindingKind.Out, outOperation.BindingKind);
        Assert.Equal("Watermark", outOperation.Property?.Name);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_ResolvesFromCompanionAkcss()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button w-30 />";
        const string akcss =
            "@utilities {\n" +
            "    .w-(double value) { Width: value; }\n" +
            "}";

        var componentPath = Path.Combine("C:\\Project", "Button.akbura");
        var akcssPath = Path.Combine("C:\\Project", "Button.akcss");
        var syntaxTree = AkburaSyntaxTree.ParseText(code, componentPath);
        var akcssTree = AkcssSyntaxTree.ParseText(akcss, akcssPath);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [akcssTree]);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFullAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal("w", operation.UtilityName);
        Assert.NotNull(operation.Utility);
        Assert.Equal("w", operation.Utility!.Name);
        Assert.Single(operation.Arguments);
        Assert.Equal("30", operation.Arguments[0].Text);
        Assert.False(operation.HasCondition);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_ResolvesFromAkcssUsingImport()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "using My.NameSpace.FileName.akcss;\n" +
            "\n" +
            "<Button flex />";
        const string akcss =
            "@utilities {\n" +
            "    .flex { Width: 10; }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Root.akbura");
        var akcssTree = AkcssSyntaxTree.ParseText(
            akcss,
            "FileName.akcss",
            "My.NameSpace.FileName.akcss");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [akcssTree]);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFlagAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));

        var componentSymbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(element).Symbol);
        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal("Button", componentSymbol.Name);
        Assert.Equal("flex", operation.UtilityName);
        Assert.NotNull(operation.Utility);
        Assert.Equal("flex", operation.Utility!.Name);
        Assert.True(operation.Arguments.IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssClassAndUtilityAttributes_ResolveAllCompatibleSources()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Style.akcss;

            <StackPanel utility>
                <Border class="awesomeBg" utility />
                <Button class="awesomeBg" utility/>
            </StackPanel>
            """;
        const string akcss =
            """
            .awesomeBg {
                Background: White;
            }

            Button.awesomeBg {
                Background: Red;
                Padding: 10;
            }

            @utilities {
                .utility {
                    Width: 100;
                    Height: 100;
                }

                Border.utility {
                    Width: 150;
                    Margin: 20;
                }

                Button.utility {
                    CornerRadius: 10;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "MyComponent.akbura");
        var akcssTree = AkcssSyntaxTree.ParseText(akcss, "Style.akcss", "Style.akcss");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [akcssTree]);
        var stackPanel = GetOnlyMarkupElement(syntaxTree);
        var children = stackPanel.Body
            .OfType<MarkupElementContentSyntax>()
            .Select(static content => content.Element)
            .ToArray();
        var border = Assert.Single(children, static element => element.StartTag!.Name.ToFullString().Trim() == "Border");
        var button = Assert.Single(children, static element => element.StartTag!.Name.ToFullString().Trim() == "Button");

        var stackUtility = AssertTailwindOperation(stackPanel, "utility");
        var borderClass = AssertClassOperation(border);
        var borderUtility = AssertTailwindOperation(border, "utility");
        var buttonClass = AssertClassOperation(button);
        var buttonUtility = AssertTailwindOperation(button, "utility");

        Assert.Single(stackUtility.Utilities);
        Assert.Contains(stackUtility.Utilities, static utility => !utility.HasTargetType);

        Assert.Single(borderClass.AppliedAkcssSymbols);
        Assert.Contains(borderClass.AppliedAkcssSymbols, static symbol => !symbol.HasTargetType);
        Assert.Equal(2, borderUtility.Utilities.Length);
        Assert.Contains(borderUtility.Utilities, static utility => !utility.HasTargetType);
        Assert.Contains(borderUtility.Utilities, static utility => utility.TargetType.Name == "Border");

        Assert.Equal(2, buttonClass.AppliedAkcssSymbols.Length);
        Assert.Contains(buttonClass.AppliedAkcssSymbols, static symbol => !symbol.HasTargetType);
        Assert.Contains(buttonClass.AppliedAkcssSymbols, static symbol => symbol.TargetType.Name == "Button");
        Assert.Equal(2, buttonUtility.Utilities.Length);
        Assert.Contains(buttonUtility.Utilities, static utility => !utility.HasTargetType);
        Assert.Contains(buttonUtility.Utilities, static utility => utility.TargetType.Name == "Button");

        Assert.True(semanticModel.GetSemanticDiagnostics(stackPanel.StartTag!.Attributes[0]).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(border.StartTag!.Attributes[0]).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(border.StartTag!.Attributes[1]).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(button.StartTag!.Attributes[0]).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(button.StartTag!.Attributes[1]).IsEmpty);

        ITailwindUtilityAttributeOperation AssertTailwindOperation(
            MarkupElementSyntax element,
            string name)
        {
            var attribute = element.StartTag!.Attributes.Single(attribute => AttributeName(attribute) == name);
            var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
                semanticModel.GetOperation(attribute));
            Assert.NotNull(operation.Utility);
            Assert.False(operation.HasErrors);
            return operation;
        }

        IMarkupPropertySetterOperation AssertClassOperation(MarkupElementSyntax element)
        {
            var attribute = element.StartTag!.Attributes.Single(attribute => AttributeName(attribute) == "class");
            var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
                semanticModel.GetOperation(attribute));
            Assert.Equal("Classes", operation.Property?.Name);
            Assert.False(operation.HasErrors);
            return operation;
        }

        static string AttributeName(MarkupAttributeSyntax attribute)
        {
            return attribute.Kind switch
            {
                Akbura.Language.Syntax.SyntaxKind.MarkupPlainAttributeSyntax =>
                    ((MarkupPlainAttributeSyntax)attribute).Name.Identifier.ValueText,
                Akbura.Language.Syntax.SyntaxKind.TailwindFlagAttributeSyntax =>
                    ((TailwindFlagAttributeSyntax)attribute).Name.Identifier.ValueText,
                Akbura.Language.Syntax.SyntaxKind.TailwindFullAttributeSyntax =>
                    ((TailwindFullAttributeSyntax)attribute).Name.Identifier.ValueText,
                _ => string.Empty,
            };
        }
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_LocalAkcssWinsOverImportedAkcss()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "using Shared.Utilities.akcss;\n" +
            "\n" +
            "<Button w-30 />";
        const string localAkcss =
            "@utilities { .w-(double value) { Width: value; } }";
        const string importedAkcss =
            "@utilities { .w-(double value) { Height: value; } }";

        var componentPath = Path.Combine("C:\\Project", "Button.akbura");
        var syntaxTree = AkburaSyntaxTree.ParseText(code, componentPath);
        var localTree = AkcssSyntaxTree.ParseText(
            localAkcss,
            Path.Combine("C:\\Project", "Button.akcss"));
        var importedTree = AkcssSyntaxTree.ParseText(
            importedAkcss,
            "Utilities.akcss",
            "Shared.Utilities.akcss");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [localTree, importedTree]);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFullAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));
        var localUtility = Assert.Single(Assert.IsType<AkcssUtilitiesSectionSyntax>(
            Assert.Single(localTree.GetRoot().Members)).Utilities);

        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));

        Assert.NotNull(operation.Utility);
        Assert.Same(localUtility, operation.Utility!.DeclarationSyntax);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_DuplicateLocalUtilitiesProduceAmbiguity()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "@akcss { @utilities { .w-(double value) { Width: value; } } }\n" +
            "\n" +
            "<Button w-30 />";
        const string companionAkcss =
            "@utilities { .w-(double value) { Height: value; } }";

        var componentPath = Path.Combine("C:\\Project", "Button.akbura");
        var syntaxTree = AkburaSyntaxTree.ParseText(code, componentPath);
        var companionTree = AkcssSyntaxTree.ParseText(
            companionAkcss,
            Path.Combine("C:\\Project", "Button.akcss"));
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [companionTree]);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFullAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.Null(operation.Utility);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityAmbiguous, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_MissingAkcssImportProducesDiagnostic()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "using Missing.Utilities.akcss;\n" +
            "\n" +
            "<Button flex />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Root.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFlagAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostics = semanticModel.GetSemanticDiagnostics(attribute);

        Assert.Null(operation.Utility);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_AkcssImportNotFound);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityNotFound);
    }

    [Fact]
    public void SemanticModel_ResolvesAkburaComponentParamAsMarkupProperty()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param int Hello;";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "namespace Hi;\n" +
            "\n" +
            "<A Hello={1}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var componentSymbolInfo = semanticModel.GetSymbolInfo(element);
        var componentSymbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(componentSymbolInfo.Symbol);
        var akburaComponent = Assert.IsAssignableFrom<IAkburaComponentSymbol>(componentSymbol.AkburaComponent);
        var paramSymbol = Assert.Single(akburaComponent.Parameters);
        var propertySymbolInfo = semanticModel.GetSymbolInfo(attribute);
        var propertySymbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(propertySymbolInfo.Symbol);

        Assert.Equal(AkburaCandidateReason.None, componentSymbolInfo.CandidateReason);
        Assert.Equal("A", componentSymbol.Name);
        Assert.Equal("SomeNs.A", componentSymbol.MetadataName);
        Assert.Same(aSyntaxTree, akburaComponent.SyntaxTree);
        Assert.Single(componentSymbol.AttributeOperations);
        Assert.Equal("Hello", paramSymbol.Name);
        Assert.Equal("Int32", paramSymbol.Type.Name);

        Assert.Equal(AkburaCandidateReason.None, propertySymbolInfo.CandidateReason);
        Assert.Equal("Hello", propertySymbol.Name);
        Assert.True(propertySymbol.IsParameter);
        Assert.False(propertySymbol.IsAvaloniaProperty);
        Assert.False(propertySymbol.IsClrProperty);
        Assert.Same(paramSymbol, propertySymbol.Parameter);
        Assert.Equal("Int32", propertySymbol.Type.Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
        Assert.DoesNotContain(
            semanticModel.GetSemanticDiagnostics(element),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet);
    }

    [Fact]
    public void SemanticModel_ReportsMissingRequiredAkburaComponentParam()
    {
        const string componentCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param int A;\n" +
            "param int B = 11;";
        const string usageCode =
            "using SomeNs;\n" +
            "\n" +
            "<OneParam B={4}/>";

        var componentTree = AkburaSyntaxTree.ParseText(componentCode, "OneParam.akbura");
        var usageTree = AkburaSyntaxTree.ParseText(usageCode, "FromOther.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [componentTree, usageTree]);
        var semanticModel = compilation.GetSemanticModel(usageTree);
        var element = GetOnlyMarkupElement(usageTree);

        var diagnostic = Assert.Single(
            semanticModel.GetSemanticDiagnostics(element),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet);
        var boundComponent = Assert.IsType<BoundMarkupComponent>(
            semanticModel.BindingSession.BindSemanticSyntax(element));

        Assert.Same(element.StartTag!.Name, diagnostic.Syntax);
        Assert.Contains("A", diagnostic.Message);
        Assert.Contains("SomeNs.OneParam", diagnostic.Message);
        Assert.True(boundComponent.HasErrors);
    }

    [Fact]
    public void SemanticModel_DoesNotRequireDefaultedOrOutAkburaComponentParams()
    {
        const string componentCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param int A;\n" +
            "param int B = 11;\n" +
            "param out int Result;";
        const string usageCode =
            "using SomeNs;\n" +
            "\n" +
            "<OneParam A={10}/>";

        var componentTree = AkburaSyntaxTree.ParseText(componentCode, "OneParam.akbura");
        var usageTree = AkburaSyntaxTree.ParseText(usageCode, "FromOther.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [componentTree, usageTree]);
        var semanticModel = compilation.GetSemanticModel(usageTree);
        var element = GetOnlyMarkupElement(usageTree);

        Assert.DoesNotContain(
            semanticModel.GetSemanticDiagnostics(element),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet);
    }

    [Fact]
    public void SemanticModel_OutParam_IsReadonlyForPlainMarkupAttribute()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param out int Result;";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "<A Result={1}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.True(property.IsParameter);
        Assert.Equal(ParamBindingKind.Out, property.Parameter!.BindingKind);
        Assert.False(property.CanWrite);
        Assert.True(property.CanRead);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeBindingNotAllowed, diagnostic.Code);
        Assert.Contains("param out", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_OutPrefixedAttribute_AllowsOutParam()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param out int Result;";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "state int value = 0;\n" +
            "\n" +
            "<A out:Result={value}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPrefixedAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(ParamBindingKind.Out, property.Parameter!.BindingKind);
        Assert.Equal(MarkupAttributeBindingKind.Out, operation.BindingKind);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_BindPrefixedAttribute_RejectsDefaultParam()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param int Value;";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "state int value = 0;\n" +
            "\n" +
            "<A bind:Value={value}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPrefixedAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.Equal(ParamBindingKind.Default, property.Parameter!.BindingKind);
        Assert.True(property.CanWrite);
        Assert.False(property.CanRead);
        Assert.Equal(MarkupAttributeBindingKind.Bind, operation.BindingKind);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeBindingNotAllowed, diagnostic.Code);
        Assert.Contains("param", diagnostic.Message);
    }

    [Theory]
    [InlineData("Text=\"Hello\"", (int)MarkupAttributeBindingKind.None)]
    [InlineData("bind:Text={text}", (int)MarkupAttributeBindingKind.Bind)]
    [InlineData("out:Text={text}", (int)MarkupAttributeBindingKind.Out)]
    public void SemanticModel_BindParam_AllowsEveryMarkupBindingDirection(
        string attributeText,
        int expectedBindingKind)
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param bind string Text = \"\";";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "state string text = \"\";\n" +
            "\n" +
            "<A ";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode + attributeText + "/>", "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.Single(element.StartTag!.Attributes);

        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.True(property.IsParameter);
        Assert.Equal(ParamBindingKind.Bind, property.Parameter!.BindingKind);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.Equal((MarkupAttributeBindingKind)expectedBindingKind, operation.BindingKind);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Theory]
    [InlineData("<TextBlock bind:Text={mystate} out:Text={myotherState}/>", false)]
    [InlineData("<TextBlock bind:Text={mystate} Text={myotherState}/>", true)]
    [InlineData("<TextBlock Text={mystate} Text={myotherState}/>", true)]
    [InlineData("<TextBlock Text={mystate} out:Text={myotherState}/>", false)]
    [InlineData("<TextBlock out:Text={mystate} out:Text={myotherState}/>", false)]
    public void SemanticModel_DetectsOnlyDuplicateMarkupPropertySetters(
        string markup,
        bool expectDuplicateSetter)
    {
        var code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state string mystate = \"\";\n" +
            "state string myotherState = \"\";\n" +
            "\n" +
            markup;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attributes = element.StartTag!.Attributes;

        Assert.Equal(2, attributes.Count);

        var firstDiagnostics = semanticModel.GetSemanticDiagnostics(attributes[0]);
        var secondDiagnostics = semanticModel.GetSemanticDiagnostics(attributes[1]);

        Assert.DoesNotContain(firstDiagnostics, diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDuplicatePropertySetter);

        if (expectDuplicateSetter)
        {
            var diagnostic = Assert.Single(secondDiagnostics, diagnostic =>
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDuplicatePropertySetter);
            Assert.Contains("Text", diagnostic.Message);
        }
        else
        {
            Assert.DoesNotContain(secondDiagnostics, diagnostic =>
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDuplicatePropertySetter);
        }
    }

    [Fact]
    public void SemanticModel_ResolvesAkburaComponentCommandAsMarkupProperty()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "command int Click(int a);";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "<A Click={x => x * 2}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var componentSymbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(element).Symbol);
        var akburaComponent = Assert.IsAssignableFrom<IAkburaComponentSymbol>(componentSymbol.AkburaComponent);
        var commandSymbol = Assert.Single(akburaComponent.Commands);
        var propertySymbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Single(componentSymbol.AttributeOperations);
        Assert.Equal("Click", commandSymbol.Name);
        Assert.Equal("Int32", commandSymbol.ReturnType.Name);
        Assert.Equal("a", Assert.Single(commandSymbol.Parameters).Name);

        Assert.Equal("Click", propertySymbol.Name);
        Assert.True(propertySymbol.IsCommand);
        Assert.False(propertySymbol.IsParameter);
        Assert.False(propertySymbol.IsAvaloniaProperty);
        Assert.False(propertySymbol.IsClrProperty);
        Assert.Same(commandSymbol, propertySymbol.Command);
        Assert.Equal("Int32", propertySymbol.Type.Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupCommandAttribute_CreatesCommandBindingOperation()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "command int Click(int a);";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "<A Click={x => x * 2}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(Akbura.Language.Operations.OperationKind.MarkupCommandBinding, operation.Kind);
        Assert.Equal("Click", operation.Command.Name);
        Assert.Same(operation.Command, operation.TargetSymbol);
        Assert.True(operation.Property.IsCommand);
        Assert.Same(operation.Command, operation.Property.Command);
        Assert.Equal("Int32", operation.ReturnType.Name);
        Assert.Equal("Int32", operation.ResultType.Name);
        Assert.Equal("a", Assert.Single(operation.Parameters).Name);
        Assert.Equal(MarkupAttributeBindingKind.None, operation.BindingKind);
        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, operation.ValueKind);
        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.ReceivesCommandArgument, operation.ArgumentMode);
        Assert.Equal(MarkupCommandResultMode.ReturnsResult, operation.ResultMode);
        Assert.Equal(1, operation.HandlerParameterCount);
        Assert.False(operation.IsAsync);
        Assert.False(operation.ContainsAwait);
        Assert.Equal("Int32", operation.HandlerResultType.Name);
        AssertCSharpRootChild(operation, operation.HandlerOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.CSharpTargetDefinition.Symbol is IParameterSymbol { Name: "x" });
        Assert.Same(attribute.Value, operation.ValueSyntax);
        Assert.False(operation.HasErrors);
        Assert.Same(operation, semanticModel.GetOperation(attribute));
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupCommandAttribute_BindsCSharpBlockLocalScope()
    {
        const string aCode =
            """
            namespace SomeNs;

            command int Click(int a);
            """;
        const string bCode =
            """
            using SomeNs;

            state int count = 0;

            if(count >= 0)
            {
                var delta = 2;
                <A Click={x => x + delta}/>
            }
            """;

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var conditional = Assert.IsType<CSharpStatementSyntax>(bSyntaxTree.GetRoot().Members[2]);
        var markupRoot = Assert.IsType<MarkupRootSyntax>(
            Assert.Single(conditional.Body!.Tokens.OfType<MarkupRootSyntax>()));
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            Assert.Single(markupRoot.Element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.ReceivesCommandArgument, operation.ArgumentMode);
        Assert.Equal(MarkupCommandResultMode.ReturnsResult, operation.ResultMode);
        Assert.Equal("Int32", operation.HandlerResultType.Name);
        Assert.False(operation.HandlerOperation.IsDefault);
        AssertCSharpRootChild(operation, operation.HandlerOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.CSharpTargetDefinition.Symbol is ILocalSymbol { Name: "delta" });
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.CSharpTargetDefinition.Symbol is IParameterSymbol { Name: "x" });
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Theory]
    [InlineData("() => Vm.Notify()", (int)MarkupCommandHandlerKind.Lambda, (int)MarkupCommandArgumentMode.IgnoresCommandArgument, (int)MarkupCommandResultMode.NoResult, 0, false, false, null)]
    [InlineData("x => Vm.NotifyWith(x)", (int)MarkupCommandHandlerKind.Lambda, (int)MarkupCommandArgumentMode.ReceivesCommandArgument, (int)MarkupCommandResultMode.NoResult, 1, false, false, null)]
    [InlineData("x => x * 2", (int)MarkupCommandHandlerKind.Lambda, (int)MarkupCommandArgumentMode.ReceivesCommandArgument, (int)MarkupCommandResultMode.ReturnsResult, 1, false, false, "Int32")]
    [InlineData("async x => await Vm.Fetch(x)", (int)MarkupCommandHandlerKind.Lambda, (int)MarkupCommandArgumentMode.ReceivesCommandArgument, (int)MarkupCommandResultMode.ReturnsResult, 1, true, true, "Int32")]
    [InlineData("Vm.MyCommand", (int)MarkupCommandHandlerKind.DirectReference, (int)MarkupCommandArgumentMode.None, (int)MarkupCommandResultMode.Unknown, 0, false, false, null)]
    public void SemanticModel_MarkupCommandAttribute_AnalyzesHandlerShape(
        string handler,
        int expectedHandlerKind,
        int expectedArgumentMode,
        int expectedResultMode,
        int expectedParameterCount,
        bool expectedIsAsync,
        bool expectedContainsAwait,
        string? expectedResultTypeName)
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "command int Click(int a);";
        var bCode =
            "using SomeNs;\n" +
            "\n" +
            "inject RootVm Vm;\n" +
            "\n" +
            "<A Click={" + handler + "}/>";
        const string csharpCode =
            "public sealed class RootVm\n" +
            "{\n" +
            "    public void Notify() { }\n" +
            "    public void NotifyWith(int value) { }\n" +
            "    public System.Threading.Tasks.Task<int> Fetch(int value) => System.Threading.Tasks.Task.FromResult(value);\n" +
            "    public System.Func<int, int> MyCommand { get; } = value => value * 2;\n" +
            "}";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(csharpCode), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal((MarkupCommandHandlerKind)expectedHandlerKind, operation.HandlerKind);
        Assert.Equal((MarkupCommandArgumentMode)expectedArgumentMode, operation.ArgumentMode);
        Assert.Equal((MarkupCommandResultMode)expectedResultMode, operation.ResultMode);
        Assert.Equal(expectedParameterCount, operation.HandlerParameterCount);
        Assert.Equal(expectedIsAsync, operation.IsAsync);
        Assert.Equal(expectedContainsAwait, operation.ContainsAwait);
        if (expectedResultTypeName == null)
        {
            Assert.True(operation.HandlerResultType.IsDefault);
        }
        else
        {
            Assert.Equal(expectedResultTypeName, operation.HandlerResultType.Name);
        }
    }

    [Theory]
    [InlineData("(x, y) => x")]
    [InlineData("(string x) => x")]
    [InlineData("x => \"bad\"")]
    public void SemanticModel_MarkupCommandAttribute_RejectsInvalidHandlerSignature(string handler)
    {
        const string aCode =
            """
            namespace SomeNs;

            command int Click(int a);
            """;
        var bCode =
            "using SomeNs;\n" +
            "\n" +
            "<A Click={" + handler + "}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(
            semanticModel.GetSemanticDiagnostics(attribute),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupCommandHandlerSignatureMismatch);

        Assert.True(operation.HasErrors);
        Assert.Contains("Click", diagnostic.Message);
    }

    [Theory]
    [InlineData("bind:Click={x => x * 2}")]
    [InlineData("out:Click={x => x * 2}")]
    public void SemanticModel_MarkupCommandAttribute_RejectsDirectionalBinding(string attributeText)
    {
        const string aCode =
            """
            namespace SomeNs;

            command int Click(int a);
            """;
        var bCode =
            "using SomeNs;\n" +
            "\n" +
            "<A " + attributeText + "/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsAssignableFrom<MarkupAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(
            semanticModel.GetSemanticDiagnostics(attribute),
            diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupCommandBindingNotAllowed);

        Assert.True(operation.HasErrors);
        Assert.Equal("Click", operation.Command.Name);
        Assert.Contains("Click", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_LocalCommandIsExecuting_BindsInMarkupExpression()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "command int CustomClick(int a);\n" +
            "\n" +
            "<TextBlock Tag={CustomClick.IsExecuting} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attributes = element.StartTag!.Attributes;

        var tag = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(Assert.Single(attributes)));

        Assert.Equal("IObservable", tag.ValueType.Name);
        var observable = Assert.IsAssignableFrom<INamedTypeSymbol>(tag.ValueType.Symbol);
        Assert.Equal("Boolean", Assert.Single(observable.TypeArguments).Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(Assert.Single(attributes)).IsEmpty);
    }

    [Fact]
    public void SemanticModel_LocalCommandExecute_BindsAsAwaitableInCommandHandler()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "command int Click(int a);";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "command int CustomClick(int a);\n" +
            "state int clicked = 0;\n" +
            "\n" +
            "<A Click={async x => await CustomClick.Execute(clicked)}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.ReceivesCommandArgument, operation.ArgumentMode);
        Assert.Equal(MarkupCommandResultMode.ReturnsResult, operation.ResultMode);
        Assert.True(operation.IsAsync);
        Assert.True(operation.ContainsAwait);
        Assert.Equal("Int32", operation.HandlerResultType.Name);
        AssertCSharpRootChild(operation, operation.HandlerOperationTree);
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.TargetSymbol is ICommandSymbol { Name: "CustomClick" });
        Assert.Contains(EnumerateCSharpOperations(operation), csharpOperation =>
            csharpOperation.TargetSymbol is IStateSymbol { Name: "clicked" });
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_UnresolvedMarkupComponent_ReturnsNotFound()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<MissingControl />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        Assert.Null(symbolInfo.Symbol);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal(AkburaCandidateReason.NotFound, symbolInfo.CandidateReason);
    }

    [Fact]
    public void Compilation_GetSemanticModel_RejectsForeignSyntaxTree()
    {
        var ownedTree = AkburaSyntaxTree.ParseText("<Button />");
        var foreignTree = AkburaSyntaxTree.ParseText("<TextBlock />");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [ownedTree]);

        Assert.Throws<ArgumentException>(() => compilation.GetSemanticModel(foreignTree));
    }

    private static (
        AkburaSemanticModel SemanticModel,
        AkcssAssignmentSyntax Assignment,
        IAkcssPropertySetterOperation Operation,
        IAkcssSymbol Symbol) GetSingleStyleOperation(string code)
    {
        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(Assert.Single(rule.Members));
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));

        Assert.Same(assignment, operation.Syntax);
        Assert.Same(operation, semanticModel.GetOperation(assignment));
        return (semanticModel, assignment, operation, symbol);
    }

    private static AkcssStyleRuleSyntax GetOnlyAkcssStyleRule(AkburaSyntaxTree syntaxTree)
    {
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is InlineAkcssBlockSyntax));
        return Assert.IsType<AkcssStyleRuleSyntax>(
            Assert.Single(inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>()));
    }

    private static AkcssStyleRuleSyntax GetOnlyAkcssStyleRule(
        AkburaSyntaxTree syntaxTree,
        string className)
    {
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is InlineAkcssBlockSyntax));
        return Assert.Single(
            inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>(),
            rule => rule.Selector.Name?.Identifier.ValueText == className);
    }

    private static AkcssUtilityDeclarationSyntax GetOnlyAkcssUtility(AkburaSyntaxTree syntaxTree)
    {
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is InlineAkcssBlockSyntax));
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(
            Assert.Single(inlineAkcss.Members.OfType<AkcssUtilitiesSectionSyntax>()));
        return Assert.Single(utilities.Utilities);
    }

    private static void AssertThickness(
        IAkcssOperation operation,
        double left,
        double top,
        double right,
        double bottom)
    {
        var setter = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(operation);
        Assert.Equal(AkcssPropertyValueKind.ThicknessTuple, setter.ValueKind);
        var value = Assert.IsType<AkcssThicknessValue>(setter.ConvertedValue);
        Assert.Equal(left, value.Left);
        Assert.Equal(top, value.Top);
        Assert.Equal(right, value.Right);
        Assert.Equal(bottom, value.Bottom);
        Assert.False(setter.HasErrors);
    }

    private static void AssertAvaloniaColorsProperty(
        CSharpSymbolDefinition definition,
        string name)
    {
        var property = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(definition.Symbol);
        Assert.Equal(name, property.Name);
        Assert.Equal("global::Avalonia.Media.Colors",
            property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static void AssertCSharpRootChild(
        Akbura.Language.Operations.IOperation owner,
        ICSharpOperation? csharpOperation)
    {
        Assert.NotNull(csharpOperation);
        Assert.Same(owner, csharpOperation!.Parent);
        Assert.Contains(csharpOperation, owner.Children);
    }

    private static IEnumerable<ICSharpOperation> EnumerateCSharpOperations(
        Akbura.Language.Operations.IOperation operation)
    {
        return EnumerateOperations(operation).OfType<ICSharpOperation>();
    }

    private static IEnumerable<Akbura.Language.Operations.IOperation> EnumerateOperations(
        Akbura.Language.Operations.IOperation operation)
    {
        yield return operation;

        foreach (var child in operation.Children)
        {
            foreach (var descendant in EnumerateOperations(child))
            {
                yield return descendant;
            }
        }
    }

    private static AkburaSemanticModel CreateSemanticModel(AkburaSyntaxTree syntaxTree)
    {
        return CreateSemanticModel(syntaxTree, CreateCSharpCompilation());
    }

    private const string AvaloniaBindingCSharpCode =
        """
        namespace Avalonia.Data;

        public abstract class BindingBase
        {
        }

        public class ReflectionBinding : BindingBase
        {
            public ReflectionBinding()
            {
            }

            public ReflectionBinding(string path)
            {
                Path = path;
            }

            public string Path { get; set; } = "";

            public BindingMode Mode { get; set; }

            public string? StringFormat { get; set; }
        }

        public class Binding : ReflectionBinding
        {
            public Binding()
            {
            }

            public Binding(string path)
                : base(path)
            {
            }
        }

        public sealed class CompiledBindingPath
        {
        }

        public class CompiledBinding : BindingBase
        {
            public CompiledBinding()
            {
            }

            public CompiledBinding(CompiledBindingPath path)
            {
                Path = path;
            }

            public CompiledBindingPath? Path { get; set; }

            public BindingMode Mode { get; set; }

            public string? StringFormat { get; set; }
        }
        """;

    private static AkburaSemanticModel CreateSemanticModel(
        AkburaSyntaxTree syntaxTree,
        CSharpCompilation csharpCompilation)
    {
        var compilation = new AkburaCompilation(csharpCompilation, [syntaxTree]);
        return compilation.GetSemanticModel(syntaxTree);
    }

    private static AkburaSemanticModel CreateSemanticModel(
        AkburaSyntaxTree syntaxTree,
        CSharpCompilation csharpCompilation,
        IEnumerable<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        var compilation = new AkburaCompilation(csharpCompilation, [syntaxTree], akcssSyntaxTrees);
        return compilation.GetSemanticModel(syntaxTree);
    }

    private static CSharpCompilation CreateCSharpCompilation(params string[] sources)
    {
        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            references: SymbolTests.CreateAvaloniaReferences(),
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))));
    }

    private static MarkupElementSyntax GetOnlyMarkupElement(AkburaSyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRoot();
        var markupRoot = Assert.IsType<MarkupRootSyntax>(root.Members.Single(member => member is MarkupRootSyntax));
        return markupRoot.Element;
    }

    private static StateDeclarationSyntax GetStateDeclaration(AkburaSyntaxTree syntaxTree, string name)
    {
        return Assert.IsType<StateDeclarationSyntax>(
            syntaxTree.GetRoot().Members.Single(member =>
                member is StateDeclarationSyntax state &&
                state.Name.Identifier.ValueText == name));
    }

    private static void AssertResolvedAvaloniaButton(
        AkburaSemanticModel semanticModel,
        MarkupComponentSymbol symbol)
    {
        AssertResolvedAvaloniaButton(semanticModel, symbol.CSharpDefinition);
    }

    private static void AssertResolvedAvaloniaButton(
        AkburaSemanticModel semanticModel,
        CSharpSymbolDefinition definition)
    {
        var avaloniaButton = semanticModel.Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Button");
        Assert.NotNull(avaloniaButton);
        Assert.True(SymbolEqualityComparer.Default.Equals(avaloniaButton, definition.Symbol));
    }

    private static MetadataReference[] CreateExternAliasAvaloniaReferences(string alias)
    {
        var avaloniaControlsAssembly = typeof(Akbura.AkburaControl).BaseType!.Assembly;

        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(
                avaloniaControlsAssembly.Location,
                new MetadataReferenceProperties(
                    MetadataImageKind.Assembly,
                    aliases: ImmutableArray.Create(alias))),
        ];
    }
}
