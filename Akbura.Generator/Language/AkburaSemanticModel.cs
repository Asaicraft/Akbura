using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
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
            MarkupElementSyntax markupElement => ResolveMarkupComponent(markupElement),
            _ => AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
        };

        _symbolInfoCache.Add(syntax, symbolInfo);
        return symbolInfo;
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
