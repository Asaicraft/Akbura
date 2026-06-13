using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [syntaxTree]);
        return compilation.GetSemanticModel(syntaxTree);
    }

    private static CSharpCompilation CreateCSharpCompilation()
    {
        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            references: SymbolTests.CreateAvaloniaReferences());
    }

    private static MarkupElementSyntax GetOnlyMarkupElement(AkburaSyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRoot();
        var markupRoot = Assert.IsType<MarkupRootSyntax>(root.Members.Single(member => member is MarkupRootSyntax));
        return markupRoot.Element;
    }
}
