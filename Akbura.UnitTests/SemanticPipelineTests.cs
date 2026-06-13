using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;

namespace Akbura.UnitTests;

public class SemanticPipelineTests
{
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
        Assert.False(symbol.Type.IsDefault);
        Assert.Equal("Boolean", symbol.Type.Name);
        Assert.True(SymbolEqualityComparer.Default.Equals(
            semanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean),
            symbol.Type.Symbol));
        Assert.Same(state, symbol.DeclarationSyntax);
        Assert.True(symbol.CSharpDefinition.IsDefault);
        Assert.Equal("state Boolean isOpen", symbol.ToDisplayString());

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(state);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
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

    private static AkburaSemanticModel CreateSemanticModel(AkburaSyntaxTree syntaxTree)
    {
        return CreateSemanticModel(syntaxTree, CreateCSharpCompilation());
    }

    private static AkburaSemanticModel CreateSemanticModel(
        AkburaSyntaxTree syntaxTree,
        CSharpCompilation csharpCompilation)
    {
        var compilation = new AkburaCompilation(csharpCompilation, [syntaxTree]);
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

    private static void AssertResolvedAvaloniaButton(
        AkburaSemanticModel semanticModel,
        MarkupComponentSymbol symbol)
    {
        var avaloniaButton = semanticModel.Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Button");
        Assert.NotNull(avaloniaButton);
        Assert.True(SymbolEqualityComparer.Default.Equals(avaloniaButton, symbol.CSharpDefinition.Symbol));
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
