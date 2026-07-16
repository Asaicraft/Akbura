using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;

namespace Akbura.Language;

internal sealed class AkburaReferencedSource
{
    private static readonly Encoding s_embeddedSourceEncoding =
        new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);

    private readonly PortableExecutableReference _reference;
    private readonly AkburaModuleTypeResolver _typeResolver;
    private readonly Lazy<AkburaSyntaxTree?> _lazySyntaxTree;
    private readonly Lazy<DeclarationTable?> _lazyDeclarationTable;
    private readonly Lazy<ModuleAkburaComponentSymbol?> _lazyComponentSymbol;

    public AkburaReferencedSource(
        PortableExecutableReference reference,
        AkburaModuleSource source,
        AkburaModuleTypeResolver typeResolver)
    {
        _reference = reference ?? throw new ArgumentNullException(nameof(reference));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _typeResolver = typeResolver ?? throw new ArgumentNullException(nameof(typeResolver));
        _lazySyntaxTree = new Lazy<AkburaSyntaxTree?>(
            ParseSyntaxTree,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _lazyDeclarationTable = new Lazy<DeclarationTable?>(
            CreateDeclarationTable,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _lazyComponentSymbol = new Lazy<ModuleAkburaComponentSymbol?>(
            CreateComponentSymbol,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public AkburaModuleSource Source { get; }

    public bool IsSyntaxTreeMaterialized => _lazySyntaxTree.IsValueCreated;

    public bool TryGetComponentSymbol(
        string metadataName,
        out IAkburaComponentSymbol symbol)
    {
        if (Source.Kind == AkburaModuleSourceKind.Component &&
            GetRootDeclaration(DeclarationKind.Component) is { MetadataName: { } declaredName } &&
            string.Equals(
                RemoveGlobalAlias(declaredName),
                RemoveGlobalAlias(metadataName),
                StringComparison.Ordinal) &&
            _lazyComponentSymbol.Value is { } componentSymbol)
        {
            symbol = componentSymbol;
            return true;
        }

        symbol = null!;
        return false;
    }

    public bool TryGetAkcssSyntaxTree(
        string logicalName,
        out AkcssSyntaxTree syntaxTree)
    {
        if (Source.Kind == AkburaModuleSourceKind.Akcss &&
            GetRootDeclaration(DeclarationKind.AkcssModule) is { } declaration &&
            string.Equals(
                declaration.MetadataName ?? Source.SourceCodePath,
                logicalName,
                StringComparison.Ordinal) &&
            _lazySyntaxTree.Value is AkcssSyntaxTree tree)
        {
            syntaxTree = tree;
            return true;
        }

        syntaxTree = null!;
        return false;
    }

    public bool TryGetComponentDeclaration(
        AkburaSyntaxTree syntaxTree,
        out AkburaModuleDeclaration declaration)
    {
        if (Source.Kind == AkburaModuleSourceKind.Component &&
            TryGetMaterializedSyntaxTree(out var materializedTree) &&
            ReferenceEquals(materializedTree, syntaxTree) &&
            GetRootDeclaration(DeclarationKind.Component) is { } componentDeclaration)
        {
            declaration = componentDeclaration;
            return true;
        }

        declaration = null!;
        return false;
    }

    public bool TryGetDeclaration(
        AkburaSyntax syntax,
        out Declaration declaration)
    {
        var table = GetDeclarationTable(syntax);
        if (table != null)
        {
            return table.TryGetDeclaration(syntax, out declaration);
        }

        declaration = null!;
        return false;
    }

    public bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        out ImmutableArray<Declaration> path)
    {
        var table = GetDeclarationTable(syntax);
        if (table != null)
        {
            return table.TryGetDeclarationPath(syntax, out path);
        }

        path = default;
        return false;
    }

    public bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        int position,
        out ImmutableArray<Declaration> path)
    {
        var table = GetDeclarationTable(syntax);
        if (table != null)
        {
            return table.TryGetDeclarationPath(syntax, position, out path);
        }

        path = default;
        return false;
    }

    public AkburaSyntaxTree GetSyntaxTree()
    {
        return _lazySyntaxTree.Value ?? throw new InvalidDataException(
            $"Could not read the embedded Akbura source '{Source.SourceCodePath}'.");
    }

    public TSyntax GetSyntax<TSyntax>(int sourceStart, int sourceLength)
        where TSyntax : AkburaSyntax
    {
        var root = GetSyntaxTree().GetRootSyntax();
        var span = new TextSpan(sourceStart, sourceLength);
        if (root is TSyntax rootSyntax && root.FullSpan == span)
        {
            return rootSyntax;
        }

        if (!root.FullSpan.Contains(span))
        {
            throw new InvalidDataException(
                $"Declaration span '{span}' is outside '{Source.SourceCodePath}'.");
        }

        var node = root.FindNode(span);
        for (var current = node; current != null; current = current.Parent)
        {
            if (current is TSyntax syntax && current.FullSpan == span)
            {
                return syntax;
            }
        }

        foreach (var candidate in root.DescendantNodesAndSelf(span))
        {
            if (candidate is TSyntax syntax && candidate.FullSpan == span)
            {
                return syntax;
            }
        }

        throw new InvalidDataException(
            $"Declaration '{typeof(TSyntax).Name}' at '{span}' was not found in '{Source.SourceCodePath}'.");
    }

    private bool TryGetMaterializedSyntaxTree(out AkburaSyntaxTree syntaxTree)
    {
        if (_lazySyntaxTree.IsValueCreated &&
            _lazySyntaxTree.Value is { } tree)
        {
            syntaxTree = tree;
            return true;
        }

        syntaxTree = null!;
        return false;
    }

    private DeclarationTable? GetDeclarationTable(AkburaSyntax syntax)
    {
        if (!TryGetMaterializedSyntaxTree(out var syntaxTree) ||
            !ReferenceEquals(syntaxTree.GetRootSyntax(), syntax.Root))
        {
            return null;
        }

        return _lazyDeclarationTable.Value;
    }

    private DeclarationTable? CreateDeclarationTable()
    {
        var syntaxTree = _lazySyntaxTree.Value;
        if (syntaxTree == null)
        {
            return null;
        }

        var declaration = syntaxTree switch
        {
            AkcssSyntaxTree akcssSyntaxTree =>
                DeclarationTreeBuilder.ForSyntaxDeclaration(akcssSyntaxTree),
            _ => DeclarationTreeBuilder.ForSyntaxDeclaration(syntaxTree),
        };
        return Source.Kind == AkburaModuleSourceKind.Component
            ? DeclarationTable.Create([declaration], ImmutableArray<Declaration>.Empty)
            : DeclarationTable.Create(ImmutableArray<Declaration>.Empty, [declaration]);
    }

    private ModuleAkburaComponentSymbol? CreateComponentSymbol()
    {
        var declaration = GetRootDeclaration(DeclarationKind.Component);
        return declaration?.Component == null
            ? null
            : new ModuleAkburaComponentSymbol(this, declaration, _typeResolver);
    }

    private AkburaSyntaxTree? ParseSyntaxTree()
    {
        if (!PortableExecutableResourceReader.TryOpenResource(
                _reference,
                Source.SourceCodePath,
                out var sourceStream))
        {
            return null;
        }

        using (sourceStream)
        {
            try
            {
                var sourceText = SourceText.From(sourceStream!, s_embeddedSourceEncoding);
                if (Source.Kind == AkburaModuleSourceKind.Component)
                {
                    return ComponentSyntaxTree.ParseText(
                        sourceText,
                        Source.SourceCodePath);
                }

                var declaration = GetRootDeclaration(DeclarationKind.AkcssModule);
                if (declaration == null)
                {
                    return null;
                }

                return AkcssSyntaxTree.ParseText(
                    sourceText,
                    Source.SourceCodePath,
                    declaration.MetadataName ?? Source.SourceCodePath);
            }
            catch (IOException)
            {
                return null;
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }
    }

    private AkburaModuleDeclaration? GetRootDeclaration(DeclarationKind kind)
    {
        AkburaModuleDeclaration? result = null;
        foreach (var declaration in Source.Declarations)
        {
            if (declaration.Kind != kind)
            {
                continue;
            }

            if (result != null)
            {
                return null;
            }

            result = declaration;
        }

        return result;
    }

    private static string RemoveGlobalAlias(string name)
    {
        return name.StartsWith("global::", StringComparison.Ordinal)
            ? name["global::".Length..]
            : name;
    }
}
