using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language;

internal sealed class AkburaSemanticModel
{
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfoCache = new();
    private ImmutableArray<string> _usingNamespaces;

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

        var typeName = typeSyntax.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return default;
        }

        var typeSymbol = ResolveCSharpType(typeName);
        return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
    }

    private ITypeSymbol? ResolveCSharpType(string typeName)
    {
        return typeName switch
        {
            "bool" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean),
            "byte" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Byte),
            "char" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Char),
            "decimal" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Decimal),
            "double" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Double),
            "float" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Single),
            "int" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Int32),
            "long" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Int64),
            "object" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Object),
            "sbyte" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_SByte),
            "short" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Int16),
            "string" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_String),
            "uint" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_UInt32),
            "ulong" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_UInt64),
            "ushort" => Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_UInt16),
            _ => Compilation.CSharpCompilation.GetTypeByMetadataName(typeName),
        };
    }

    private AkburaSymbolInfo ResolveMarkupComponent(MarkupElementSyntax markupElement)
    {
        var startTag = markupElement.StartTag;
        if (startTag == null)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var componentName = startTag.Name.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var candidates = ResolveComponentCandidates(componentName);
        if (candidates.Length == 0)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
        }

        if (candidates.Length > 1)
        {
            return AkburaSymbolInfo.Candidates(candidates, AkburaCandidateReason.Ambiguous);
        }

        return AkburaSymbolInfo.Success(candidates[0]);
    }

    private ImmutableArray<AkburaSymbol> ResolveComponentCandidates(string componentName)
    {
        using var builder = ImmutableArrayBuilder<AkburaSymbol>.Rent();
        var seenMetadataNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var metadataName in GetComponentMetadataNames(componentName))
        {
            if (!seenMetadataNames.Add(metadataName))
            {
                continue;
            }

            var symbol = Compilation.CSharpCompilation.GetTypeByMetadataName(metadataName);
            if (symbol == null)
            {
                continue;
            }

            builder.Add(new MarkupComponentSymbol(
                componentName,
                new CSharpSymbolDefinition(symbol)));
        }

        return builder.ToImmutable();
    }

    private IEnumerable<string> GetComponentMetadataNames(string componentName)
    {
        if (componentName.Contains("."))
        {
            yield return componentName;
            yield break;
        }

        foreach (var @namespace in GetUsingNamespaces())
        {
            yield return @namespace + "." + componentName;
        }

        yield return componentName;
    }

    private ImmutableArray<string> GetUsingNamespaces()
    {
        if (!_usingNamespaces.IsDefault)
        {
            return _usingNamespaces;
        }

        using var builder = ImmutableArrayBuilder<string>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member is not UsingDirectiveSyntax usingDirective)
            {
                continue;
            }

            if (usingDirective.StaticKeyword.RawKind != 0 ||
                usingDirective.Alias != null)
            {
                continue;
            }

            var name = usingDirective.Name.ToFullString().Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                builder.Add(name);
            }
        }

        return _usingNamespaces = builder.ToImmutable();
    }
}
