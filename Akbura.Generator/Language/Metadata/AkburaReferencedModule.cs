using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.IO;

namespace Akbura.Language;

internal sealed class AkburaReferencedModule
{
    private readonly ImmutableDictionary<AkburaSyntaxTree, AkburaModuleDeclaration> _componentDeclarations;

    private AkburaReferencedModule(
        PortableExecutableReference reference,
        AkburaModuleManifest manifest,
        ImmutableArray<AkburaSyntaxTree> componentSyntaxTrees,
        ImmutableArray<AkcssSyntaxTree> akcssSyntaxTrees,
        ImmutableDictionary<AkburaSyntaxTree, AkburaModuleDeclaration> componentDeclarations)
    {
        Reference = reference;
        Manifest = manifest;
        Location = new MetadataLocation(this);
        ComponentSyntaxTrees = componentSyntaxTrees;
        AkcssSyntaxTrees = akcssSyntaxTrees;
        _componentDeclarations = componentDeclarations;
    }

    public PortableExecutableReference Reference { get; }

    public AkburaModuleManifest Manifest { get; }

    public MetadataLocation Location { get; }

    public ImmutableArray<AkburaSyntaxTree> ComponentSyntaxTrees { get; }

    public ImmutableArray<AkcssSyntaxTree> AkcssSyntaxTrees { get; }

    public bool TryGetComponentDeclaration(
        AkburaSyntaxTree syntaxTree,
        out AkburaModuleDeclaration declaration)
    {
        return _componentDeclarations.TryGetValue(syntaxTree, out declaration!);
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
                TryLoad(portableReference, out var module))
            {
                modules.Add(module);
            }
        }

        return modules.ToImmutable();
    }

    private static bool TryLoad(
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

        var componentTrees = ImmutableArray.CreateBuilder<AkburaSyntaxTree>();
        var akcssTrees = ImmutableArray.CreateBuilder<AkcssSyntaxTree>();
        var componentDeclarations = ImmutableDictionary.CreateBuilder<AkburaSyntaxTree, AkburaModuleDeclaration>();

        foreach (var source in manifest.Sources)
        {
            if (!PortableExecutableResourceReader.TryOpenResource(
                    reference,
                    source.SourceCodePath,
                    out var sourceStream))
            {
                continue;
            }

            string text;
            using (sourceStream)
            using (var reader = new StreamReader(sourceStream))
            {
                text = reader.ReadToEnd();
            }

            if (source.Kind == AkburaModuleSourceKind.Component)
            {
                var declaration = GetSingleDeclaration(source, DeclarationKind.Component);
                if (declaration == null || declaration.Component == null)
                {
                    continue;
                }

                var syntaxTree = AkburaSyntaxTree.ParseText(text, source.SourceCodePath);
                componentTrees.Add(syntaxTree);
                componentDeclarations.Add(syntaxTree, declaration);
            }
            else if (source.Kind == AkburaModuleSourceKind.Akcss)
            {
                var declaration = GetSingleDeclaration(source, DeclarationKind.AkcssModule);
                if (declaration == null)
                {
                    continue;
                }

                var logicalName = declaration.MetadataName ?? source.SourceCodePath;
                akcssTrees.Add(AkcssSyntaxTree.ParseText(
                    text,
                    source.SourceCodePath,
                    logicalName));
            }
        }

        module = new AkburaReferencedModule(
            reference,
            manifest,
            componentTrees.ToImmutable(),
            akcssTrees.ToImmutable(),
            componentDeclarations.ToImmutable());
        return true;
    }

    private static AkburaModuleDeclaration? GetSingleDeclaration(
        AkburaModuleSource source,
        DeclarationKind kind)
    {
        AkburaModuleDeclaration? result = null;
        foreach (var declaration in source.Declarations)
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
}
