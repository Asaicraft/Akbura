using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;

namespace Akbura.Language;

internal sealed class AkburaReferencedModule
{
    private const string AkcssModuleReferenceAttributeName =
        "Akbura.CompilerAnotations.AkcssModuleReferenceAttribute";

    private readonly Lazy<EmbeddedModuleData> _lazyEmbeddedData;
    private readonly ImmutableArray<IAkcssModuleSymbol> _akcssModules;

    private AkburaReferencedModule(
        PortableExecutableReference reference,
        Lazy<EmbeddedModuleData> lazyEmbeddedData,
        ImmutableArray<IAkcssModuleSymbol> akcssModules)
    {
        Reference = reference;
        _lazyEmbeddedData = lazyEmbeddedData;
        Location = new MetadataLocation(this);
        _akcssModules = akcssModules.IsDefault
            ? ImmutableArray<IAkcssModuleSymbol>.Empty
            : akcssModules;
    }

    public PortableExecutableReference Reference { get; }

    public AkburaModuleManifest Manifest => _lazyEmbeddedData.Value.Manifest;

    public MetadataLocation Location { get; }

    public ImmutableArray<IAkcssModuleSymbol> GetAkcssModuleSymbolsByLogicalName(
        string logicalName)
    {
        using var builder = ImmutableArrayBuilder<IAkcssModuleSymbol>.Rent();
        foreach (var module in _akcssModules)
        {
            if (string.Equals(module.MetadataName, logicalName, StringComparison.Ordinal) ||
                string.Equals(module.Path, logicalName, StringComparison.Ordinal))
            {
                builder.Add(module);
            }
        }

        return builder.ToImmutable();
    }

    internal ImmutableArray<string> GetAkcssModuleNames()
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var module in _akcssModules)
        {
            if (names.Add(module.MetadataName))
            {
                builder.Add(module.MetadataName);
            }
        }

        if (_akcssModules.Length != 0)
        {
            return builder.ToImmutable();
        }

        foreach (var source in Manifest.Sources)
        {
            if (source.Kind != AkburaModuleSourceKind.Akcss)
            {
                continue;
            }

            foreach (var declaration in source.Declarations)
            {
                if (declaration.Kind != DeclarationKind.AkcssModule)
                {
                    continue;
                }

                var name = declaration.MetadataName ??
                    source.SourceCodePath;
                if (names.Add(name))
                {
                    builder.Add(name);
                }
            }
        }

        return builder.ToImmutable();
    }

    internal bool IsSyntaxTreeMaterialized(string sourceCodePath)
    {
        if (!_lazyEmbeddedData.IsValueCreated)
        {
            return false;
        }

        foreach (var source in _lazyEmbeddedData.Value.Sources)
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
        foreach (var source in _lazyEmbeddedData.Value.Sources)
        {
            if (source.TryGetComponentSymbol(metadataName, out symbol))
            {
                return true;
            }
        }

        symbol = null!;
        return false;
    }

    internal IEnumerable<IAkburaComponentSymbol> GetComponentSymbols()
    {
        foreach (var source in _lazyEmbeddedData.Value.Sources)
        {
            foreach (var declaration in source.Source.Declarations)
            {
                if (declaration.Kind == DeclarationKind.Component &&
                    declaration.MetadataName is { Length: > 0 } metadataName &&
                    source.TryGetComponentSymbol(metadataName, out var symbol))
                {
                    yield return symbol;
                    break;
                }
            }
        }
    }

    public ImmutableArray<AkcssSyntaxTree> GetAkcssSyntaxTreesByLogicalName(
        string logicalName)
    {
        using var builder = ImmutableArrayBuilder<AkcssSyntaxTree>.Rent();
        foreach (var source in _lazyEmbeddedData.Value.Sources)
        {
            if (source.TryGetAkcssSyntaxTree(logicalName, out var syntaxTree))
            {
                builder.Add(syntaxTree);
            }
        }

        return builder.ToImmutable();
    }

    internal bool ContainsAkcssSyntaxTree(AkcssSyntaxTree syntaxTree)
    {
        foreach (var source in _lazyEmbeddedData.Value.Sources)
        {
            if (source.ContainsAkcssSyntaxTree(syntaxTree))
            {
                return true;
            }
        }

        return false;
    }

    internal bool TryGetSource(
        AkburaSyntax syntax,
        out AkburaReferencedSource source)
    {
        foreach (var candidate in _lazyEmbeddedData.Value.Sources)
        {
            if (candidate.ContainsSyntax(syntax))
            {
                source = candidate;
                return true;
            }
        }

        source = null!;
        return false;
    }

    public bool TryGetComponentDeclaration(
        AkburaSyntaxTree syntaxTree,
        out AkburaModuleDeclaration declaration)
    {
        foreach (var source in _lazyEmbeddedData.Value.Sources)
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
        foreach (var source in _lazyEmbeddedData.Value.Sources)
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
        foreach (var source in _lazyEmbeddedData.Value.Sources)
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
        foreach (var source in _lazyEmbeddedData.Value.Sources)
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

        var result = modules.ToImmutable();
        InitializeAnnotatedAkcssOperations(compilation, result);
        return result;
    }

    private static bool TryLoad(
        CSharpCompilation compilation,
        PortableExecutableReference reference,
        out AkburaReferencedModule module)
    {
        module = null!;
        var assembly = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
        var akcssModules = assembly == null
            ? ImmutableArray<IAkcssModuleSymbol>.Empty
            : LoadAnnotatedAkcssModules(assembly);
        if (!akcssModules.IsDefaultOrEmpty)
        {
            module = new AkburaReferencedModule(
                reference,
                new Lazy<EmbeddedModuleData>(
                    () => LoadEmbeddedDataOrEmpty(
                        compilation,
                        reference,
                        assembly?.Name ?? string.Empty),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                akcssModules);
            return true;
        }

        if (!TryLoadEmbeddedData(compilation, reference, out var embeddedData))
        {
            return false;
        }

        module = new AkburaReferencedModule(
            reference,
            new Lazy<EmbeddedModuleData>(
                () => embeddedData,
                LazyThreadSafetyMode.ExecutionAndPublication),
            ImmutableArray<IAkcssModuleSymbol>.Empty);
        return true;
    }

    private static ImmutableArray<IAkcssModuleSymbol> LoadAnnotatedAkcssModules(
        IAssemblySymbol assembly)
    {
        using var modules = ImmutableArrayBuilder<IAkcssModuleSymbol>.Rent();
        foreach (var attribute in assembly.GetAttributes())
        {
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    AkcssModuleReferenceAttributeName,
                    StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol moduleType ||
                !MetadataAkcssModuleSymbol.TryCreate(moduleType, out var module))
            {
                continue;
            }

            modules.Add(module);
        }

        return modules.ToImmutable();
    }

    private static EmbeddedModuleData LoadEmbeddedDataOrEmpty(
        CSharpCompilation compilation,
        PortableExecutableReference reference,
        string assemblyName)
    {
        return TryLoadEmbeddedData(compilation, reference, out var data)
            ? data
            : new EmbeddedModuleData(
                new AkburaModuleManifest(
                    AkburaModuleManifest.CurrentFormatVersion,
                    assemblyName,
                    ImmutableArray<AkburaModuleSource>.Empty),
                ImmutableArray<AkburaReferencedSource>.Empty);
    }

    private static bool TryLoadEmbeddedData(
        CSharpCompilation compilation,
        PortableExecutableReference reference,
        out EmbeddedModuleData data)
    {
        data = default;
        if (!PortableExecutableResourceReader.TryOpenResource(
                reference,
                AkburaModuleManifest.ResourceName,
                out var manifestStream))
        {
            return false;
        }

        AkburaModuleManifest manifest;
        using (manifestStream)
        {
            try
            {
                manifest = AkburaModuleManifestSerializer.Read(manifestStream!);
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

        data = new EmbeddedModuleData(manifest, sources.ToImmutable());
        return true;
    }

    private static void InitializeAnnotatedAkcssOperations(
        CSharpCompilation compilation,
        ImmutableArray<AkburaReferencedModule> referencedModules)
    {
        using var symbols = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
        foreach (var referencedModule in referencedModules)
        {
            foreach (var module in referencedModule._akcssModules)
            {
                symbols.AddRange(module.AkcssSymbols);
            }
        }

        var availableSymbols = symbols.ToImmutable();
        foreach (var referencedModule in referencedModules)
        {
            foreach (var module in referencedModule._akcssModules)
            {
                if (module is MetadataAkcssModuleSymbol metadataModule)
                {
                    metadataModule.InitializeOperations(
                        compilation,
                        availableSymbols);
                }
            }
        }
    }

    private readonly struct EmbeddedModuleData
    {
        public EmbeddedModuleData(
            AkburaModuleManifest manifest,
            ImmutableArray<AkburaReferencedSource> sources)
        {
            Manifest = manifest;
            Sources = sources;
        }

        public AkburaModuleManifest Manifest { get; }

        public ImmutableArray<AkburaReferencedSource> Sources { get; }
    }
}
