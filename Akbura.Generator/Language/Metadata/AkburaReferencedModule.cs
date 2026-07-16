using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.IO;

namespace Akbura.Language;

internal sealed class AkburaReferencedModule
{
    private readonly ImmutableArray<AkburaReferencedSource> _sources;

    private AkburaReferencedModule(
        PortableExecutableReference reference,
        AkburaModuleManifest manifest,
        ImmutableArray<AkburaReferencedSource> sources)
    {
        Reference = reference;
        Manifest = manifest;
        Location = new MetadataLocation(this);
        _sources = sources;
    }

    public PortableExecutableReference Reference { get; }

    public AkburaModuleManifest Manifest { get; }

    public MetadataLocation Location { get; }

    internal bool IsSyntaxTreeMaterialized(string sourceCodePath)
    {
        foreach (var source in _sources)
        {
            if (string.Equals(
                    source.Source.SourceCodePath,
                    sourceCodePath,
                    StringComparison.Ordinal))
            {
                return source.IsSyntaxTreeMaterialized;
            }
        }

        return false;
    }

    public bool TryGetComponentSymbol(
        string metadataName,
        out IAkburaComponentSymbol symbol)
    {
        foreach (var source in _sources)
        {
            if (source.TryGetComponentSymbol(metadataName, out symbol))
            {
                return true;
            }
        }

        symbol = null!;
        return false;
    }

    public ImmutableArray<AkcssSyntaxTree> GetAkcssSyntaxTreesByLogicalName(
        string logicalName)
    {
        using var builder = ImmutableArrayBuilder<AkcssSyntaxTree>.Rent();
        foreach (var source in _sources)
        {
            if (source.TryGetAkcssSyntaxTree(logicalName, out var syntaxTree))
            {
                builder.Add(syntaxTree);
            }
        }

        return builder.ToImmutable();
    }

    public bool TryGetComponentDeclaration(
        AkburaSyntaxTree syntaxTree,
        out AkburaModuleDeclaration declaration)
    {
        foreach (var source in _sources)
        {
            if (source.TryGetComponentDeclaration(syntaxTree, out declaration))
            {
                return true;
            }
        }

        declaration = null!;
        return false;
    }

    public bool TryGetDeclaration(
        AkburaSyntax syntax,
        out Declaration declaration)
    {
        foreach (var source in _sources)
        {
            if (source.TryGetDeclaration(syntax, out declaration))
            {
                return true;
            }
        }

        declaration = null!;
        return false;
    }

    public bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        out ImmutableArray<Declaration> path)
    {
        foreach (var source in _sources)
        {
            if (source.TryGetDeclarationPath(syntax, out path))
            {
                return true;
            }
        }

        path = default;
        return false;
    }

    public bool TryGetDeclarationPath(
        AkburaSyntax syntax,
        int position,
        out ImmutableArray<Declaration> path)
    {
        foreach (var source in _sources)
        {
            if (source.TryGetDeclarationPath(syntax, position, out path))
            {
                return true;
            }
        }

        path = default;
        return false;
    }

    public static ImmutableArray<AkburaReferencedModule> Load(CSharpCompilation compilation)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        using var modules = ImmutableArrayBuilder<AkburaReferencedModule>.Rent();
        foreach (var reference in compilation.References)
        {
            if (reference is PortableExecutableReference portableReference &&
                TryLoad(compilation, portableReference, out var module))
            {
                modules.Add(module);
            }
        }

        return modules.ToImmutable();
    }

    private static bool TryLoad(
        CSharpCompilation compilation,
        PortableExecutableReference reference,
        out AkburaReferencedModule module)
    {
        module = null!;
        if (!PortableExecutableResourceReader.TryOpenResource(
                reference,
                AkburaModuleManifest.ResourceName,
                out var manifestStream))
        {
            return false;
        }

        AkburaModuleManifest manifest;
        using (var stream = manifestStream!)
        {
            try
            {
                manifest = AkburaModuleManifestSerializer.Read(stream);
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        var typeResolver = new AkburaModuleTypeResolver(compilation);
        using var sources = ImmutableArrayBuilder<AkburaReferencedSource>.Rent(
            manifest.Sources.Length);
        foreach (var source in manifest.Sources)
        {
            sources.Add(new AkburaReferencedSource(reference, source, typeResolver));
        }

        module = new AkburaReferencedModule(
            reference,
            manifest,
            sources.ToImmutable());
        return true;
    }
}
