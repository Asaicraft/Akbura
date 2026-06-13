using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language;

internal sealed class AkburaSemanticModel
{
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfoCache = new();

    public AkburaSemanticModel(AkburaCompilation compilation, AkburaSyntaxTree syntaxTree)
    {
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
    }

    public AkburaCompilation Compilation { get; }

    public AkburaSyntaxTree SyntaxTree { get; }

    public AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (!ReferenceEquals(syntax.Root, SyntaxTree.GetRoot()))
        {
            throw new ArgumentException("Syntax node is not part of this semantic model syntax tree.", nameof(syntax));
        }

        if (_symbolInfoCache.TryGetValue(syntax, out var symbolInfo))
        {
            return symbolInfo;
        }

        symbolInfo = syntax switch
        {
            StateDeclarationSyntax stateDeclaration => ResolveState(stateDeclaration),
            MarkupElementSyntax markupElement => ResolveMarkupComponent(markupElement),
            _ => AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
        };

        _symbolInfoCache.Add(syntax, symbolInfo);
        return symbolInfo;
    }

    private AkburaSymbolInfo ResolveState(StateDeclarationSyntax stateDeclaration)
    {
        var name = stateDeclaration.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var hasExplicitType = stateDeclaration.Type != null;
        var type = ResolveExplicitStateType(stateDeclaration);
        return AkburaSymbolInfo.Success(new StateSymbol(stateDeclaration, type, hasExplicitType));
    }

    private CSharpSymbolDefinition ResolveExplicitStateType(StateDeclarationSyntax stateDeclaration)
    {
        var typeSyntax = stateDeclaration.Type;
        if (typeSyntax == null)
        {
            return default;
        }

        CSharp.TypeSyntax csharpType;
        try
        {
            csharpType = typeSyntax.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return default;
        }

        var binding = BindCSharpType(csharpType);
        var typeSymbol = binding.TypeSymbol;
        return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
    }

    private AkburaSymbolInfo ResolveMarkupComponent(MarkupElementSyntax markupElement)
    {
        var startTag = markupElement.StartTag;
        if (startTag == null)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var componentName = startTag.Name;
        var componentNameText = componentName.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(componentNameText))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        CSharp.TypeSyntax csharpType;
        try
        {
            csharpType = componentName.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var binding = BindCSharpType(csharpType);
        if (binding.TypeSymbol is INamedTypeSymbol namedType &&
            namedType.TypeKind != TypeKind.Error)
        {
            return AkburaSymbolInfo.Success(new MarkupComponentSymbol(
                componentNameText,
                new CSharpSymbolDefinition(namedType)));
        }

        var candidates = CreateMarkupComponentCandidates(componentNameText, binding.CandidateSymbols);
        if (candidates.Length > 0)
        {
            return AkburaSymbolInfo.Candidates(candidates, binding.CandidateReason);
        }

        return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
    }

    private CSharpTypeBinding BindCSharpType(CSharp.TypeSyntax typeSyntax)
    {
        var field = CSharpSyntaxFactory.FieldDeclaration(
            CSharpSyntaxFactory.VariableDeclaration(typeSyntax)
                .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                    CSharpSyntaxFactory.VariableDeclarator("__akbura_value"))));

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(field));

        var compilationUnit = CSharpSyntaxFactory.CompilationUnit()
            .WithExterns(CSharpSyntaxFactory.List(GetCSharpExternAliases()))
            .WithUsings(CSharpSyntaxFactory.List(GetCSharpUsingDirectives()));

        var namespaceDeclaration = GetCSharpNamespaceDeclaration();
        if (namespaceDeclaration != null)
        {
            compilationUnit = compilationUnit.WithMembers(
                CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(
                    namespaceDeclaration.WithMembers(
                        CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(probeClass))));
        }
        else
        {
            compilationUnit = compilationUnit.WithMembers(
                CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(probeClass));
        }

        var parseOptions = Compilation.CSharpCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ??
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        var syntaxTree = CSharpSyntaxTree.Create(compilationUnit, parseOptions);

        var probeCompilation = Compilation.CSharpCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = probeCompilation.GetSemanticModel(syntaxTree);
        var probeType = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.FieldDeclarationSyntax>()
            .Single()
            .Declaration
            .Type;

        var typeInfo = semanticModel.GetTypeInfo(probeType);
        var symbolInfo = semanticModel.GetSymbolInfo(probeType);
        var typeSymbol = typeInfo.Type?.TypeKind == TypeKind.Error
            ? null
            : typeInfo.Type;

        return new CSharpTypeBinding(
            typeSymbol,
            symbolInfo.CandidateSymbols,
            symbolInfo.CandidateReason == Microsoft.CodeAnalysis.CandidateReason.Ambiguous
                ? AkburaCandidateReason.Ambiguous
                : AkburaCandidateReason.NotFound);
    }

    private ImmutableArray<CSharp.UsingDirectiveSyntax> GetCSharpUsingDirectives()
    {
        using var builder = ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member is UsingDirectiveSyntax usingDirective)
            {
                builder.Add(usingDirective.ToCSharp());
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<CSharp.ExternAliasDirectiveSyntax> GetCSharpExternAliases()
    {
        using var builder = ImmutableArrayBuilder<CSharp.ExternAliasDirectiveSyntax>.Rent();
        var seenAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in Compilation.CSharpCompilation.References)
        {
            foreach (var alias in reference.Properties.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias) ||
                    alias == "global" ||
                    !seenAliases.Add(alias))
                {
                    continue;
                }

                builder.Add(CSharpSyntaxFactory.ExternAliasDirective(
                    CSharpSyntaxFactory.Identifier(alias)));
            }
        }

        return builder.ToImmutable();
    }

    private CSharp.FileScopedNamespaceDeclarationSyntax? GetCSharpNamespaceDeclaration()
    {
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member is NamespaceDeclarationSyntax namespaceDeclaration)
            {
                return namespaceDeclaration.ToCSharp();
            }
        }

        return null;
    }

    private static ImmutableArray<AkburaSymbol> CreateMarkupComponentCandidates(
        string componentName,
        ImmutableArray<RoslynSymbol> csharpCandidates)
    {
        if (csharpCandidates.IsDefaultOrEmpty)
        {
            return ImmutableArray<AkburaSymbol>.Empty;
        }

        using var builder = ImmutableArrayBuilder<AkburaSymbol>.Rent();
        var seenSymbols = new HashSet<RoslynSymbol>(SymbolEqualityComparer.Default);
        foreach (var candidate in csharpCandidates)
        {
            if (candidate is not INamedTypeSymbol namedType ||
                namedType.TypeKind == TypeKind.Error ||
                !seenSymbols.Add(namedType))
            {
                continue;
            }

            builder.Add(new MarkupComponentSymbol(
                componentName,
                new CSharpSymbolDefinition(namedType)));
        }

        return builder.ToImmutable();
    }

    private readonly struct CSharpTypeBinding
    {
        public CSharpTypeBinding(
            ITypeSymbol? typeSymbol,
            ImmutableArray<RoslynSymbol> candidateSymbols,
            AkburaCandidateReason candidateReason)
        {
            TypeSymbol = typeSymbol;
            CandidateSymbols = candidateSymbols.IsDefault
                ? ImmutableArray<RoslynSymbol>.Empty
                : candidateSymbols;
            CandidateReason = candidateReason;
        }

        public ITypeSymbol? TypeSymbol { get; }

        public ImmutableArray<RoslynSymbol> CandidateSymbols { get; }

        public AkburaCandidateReason CandidateReason { get; }
    }
}
