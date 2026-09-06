using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Project-wide syntax identities and infrastructure, without eagerly binding documents.
/// Semantic inputs are materialized only after generation decides a document is dirty.
/// </summary>
internal sealed class AkburaProjectIndex
{
    private AkburaProjectIndex(
        AkburaCompilation compilation,
        ImmutableArray<DocumentSyntaxVersion> documents,
        ImmutableArray<ComponentDocumentDescriptor> components,
        ImmutableArray<AkcssDocumentDescriptor> externalAkcss,
        ImmutableArray<AkcssDocumentDescriptor> inlineAkcss,
        IReadOnlyDictionary<AkburaSyntax, string> moduleTypeNames,
        AkburaSourceTreeMap sourceTreeMap)
    {
        Compilation = compilation;
        Documents = documents;
        ComponentDescriptors = components;
        ExternalAkcssDescriptors = externalAkcss;
        InlineAkcssDescriptors = inlineAkcss;
        AkcssModuleTypeNames = moduleTypeNames;
        SourceTreeMap = sourceTreeMap;
        AkcssSourceMap = new AkcssGenerationSourceMap(
            sourceTreeMap,
            new AkcssGenerationSymbolResolver(sourceTreeMap, compilation));
    }

    public AkburaCompilation Compilation { get; }
    public string RootNamespace => Compilation.RootNamespace;
    public string ProjectDirectory => Compilation.ProjectDirectory;
    public ImmutableArray<DocumentSyntaxVersion> Documents { get; }
    public ImmutableArray<ComponentDocumentDescriptor> ComponentDescriptors { get; }
    public ImmutableArray<AkcssDocumentDescriptor> ExternalAkcssDescriptors { get; }
    public ImmutableArray<AkcssDocumentDescriptor> InlineAkcssDescriptors { get; }
    public IReadOnlyDictionary<AkburaSyntax, string> AkcssModuleTypeNames { get; }
    public AkburaSourceTreeMap SourceTreeMap { get; }
    public AkcssGenerationSourceMap AkcssSourceMap { get; }

    public static AkburaProjectIndex Create(
        CSharpCompilation csharpCompilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        string rootNamespace,
        string projectDirectory,
        AkburaCompilation? reuseFrom = null,
        CancellationToken cancellationToken = default)
    {
        using var documents = ImmutableArrayBuilder<DocumentSyntaxVersion>.Rent();
        if (!syntaxTrees.IsDefault)
        {
            for (var i = 0; i < syntaxTrees.Length; i++)
            {
                documents.Add(DocumentSyntaxVersion.Create(syntaxTrees[i], cancellationToken));
            }
        }

        return Create(
            csharpCompilation,
            documents.ToImmutable(),
            rootNamespace,
            projectDirectory,
            reuseFrom,
            cancellationToken);
    }

    public static AkburaProjectIndex Create(
        CSharpCompilation csharpCompilation,
        ImmutableArray<DocumentSyntaxVersion> documents,
        string rootNamespace,
        string projectDirectory,
        AkburaCompilation? reuseFrom = null,
        CancellationToken cancellationToken = default)
    {
#if STATS
        using var indexMeasurement = GenerationStatistics.Measure(GenerationStatisticStage.Catalog);
#endif
        cancellationToken.ThrowIfCancellationRequested();
        documents = documents.IsDefault ? [] : documents;

        using var componentTrees = ImmutableArrayBuilder<AkburaSyntaxTree>.Rent();
        using var akcssTrees = ImmutableArrayBuilder<AkcssSyntaxTree>.Rent();
        using var components = ImmutableArrayBuilder<ComponentDocumentDescriptor>.Rent();
        using var externalAkcss = ImmutableArrayBuilder<AkcssDocumentDescriptor>.Rent();
        using var inlineAkcss = ImmutableArrayBuilder<AkcssDocumentDescriptor>.Rent();
        var moduleTypeNames = new Dictionary<AkburaSyntax, string>();

        for (var i = 0; i < documents.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var version = documents[i];
            switch (version.SyntaxTree)
            {
                case ComponentSyntaxTree componentTree:
                    componentTrees.Add(componentTree);
                    if (!GlobalUsings.IsComponentFile(componentTree) &&
                        !string.IsNullOrWhiteSpace(componentTree.ComponentName))
                    {
                        components.Add(CreateComponentDescriptor(
                            componentTree,
                            version,
                            rootNamespace,
                            projectDirectory,
                            moduleTypeNames,
                            inlineAkcss,
                            cancellationToken));
                    }

                    break;

                case AkcssSyntaxTree akcssTree:
                    akcssTrees.Add(akcssTree);
                    if (!GlobalUsings.IsAkcssFile(akcssTree))
                    {
                        var sourcePath = AkburaGenerationCatalogBuilder.GetSourcePath(akcssTree, projectDirectory);
                        var descriptor = CreateAkcssDescriptor(
                            akcssTree,
                            akcssTree.GetRootSyntax(),
                            sourcePath,
                            sourcePath,
                            rootNamespace,
                            version);
                        externalAkcss.Add(descriptor);
                        moduleTypeNames[descriptor.Root] = descriptor.GeneratedTypeName;
                    }

                    break;
            }
        }

        var componentSyntaxTrees = componentTrees.ToImmutable();
        var akcssSyntaxTrees = akcssTrees.ToImmutable();
#if STATS
        using var compilationMeasurement = GenerationStatistics.Measure(GenerationStatisticStage.Compilation);
#endif
        var compilation = new AkburaCompilation(
            csharpCompilation,
            componentSyntaxTrees,
            akcssSyntaxTrees,
            rootNamespace,
            projectDirectory,
            reuseFrom);
#if STATS
        compilationMeasurement.Dispose();
#endif
        cancellationToken.ThrowIfCancellationRequested();

        return new AkburaProjectIndex(
            compilation,
            documents,
            components.ToImmutable(),
            externalAkcss.ToImmutable(),
            inlineAkcss.ToImmutable(),
            moduleTypeNames,
            new AkburaSourceTreeMap(componentSyntaxTrees, akcssSyntaxTrees));
    }

    public bool TryResolveComponent(
        ComponentDocumentDescriptor descriptor,
        out ComponentGenerationInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.SemanticBinding);
#endif
        var semanticModel = Compilation.GetSemanticModel(descriptor.SyntaxTree);
        if (semanticModel.GetDeclaredSymbol(descriptor.Root) is not IAkburaComponentSymbol component)
        {
            input = default;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        input = new ComponentGenerationInput(component, semanticModel, descriptor.SourcePath);
        return true;
    }

    public bool TryResolveAkcss(
        AkcssDocumentDescriptor descriptor,
        out AkcssGenerationInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.SemanticBinding);
#endif
        var semanticModel = Compilation.GetSemanticModel(descriptor.SyntaxTree);
        if (semanticModel.GetDeclaredSymbol(descriptor.Root) is not IAkcssModuleSymbol module)
        {
            input = default;
            return false;
        }

        var usingDirectives = semanticModel.GetAkcssCSharpUsingDirectives(module);
        cancellationToken.ThrowIfCancellationRequested();
        AkcssSourceMap.RegisterModule(module);
        input = new AkcssGenerationInput(
            module,
            semanticModel,
            descriptor.SourcePath,
            descriptor.ModuleIdentity,
            usingDirectives);
        return true;
    }

    private static ComponentDocumentDescriptor CreateComponentDescriptor(
        ComponentSyntaxTree syntaxTree,
        DocumentSyntaxVersion version,
        string rootNamespace,
        string projectDirectory,
        Dictionary<AkburaSyntax, string> moduleTypeNames,
        ImmutableArrayBuilder<AkcssDocumentDescriptor> allInlineModules,
        CancellationToken cancellationToken)
    {
        var sourcePath = AkburaGenerationCatalogBuilder.GetSourcePath(syntaxTree, projectDirectory);
        var namespaceName = AkburaComponentProbeCompilationBuilder.GetNamespaceName(
            syntaxTree, rootNamespace, projectDirectory);
        var metadataName = namespaceName.Length == 0
            ? syntaxTree.ComponentName
            : namespaceName + "." + syntaxTree.ComponentName;
        using var inlineModules = ImmutableArrayBuilder<AkcssDocumentDescriptor>.Rent();

        foreach (var member in syntaxTree.GetRoot().Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not InlineAkcssBlockSyntax inlineBlock)
            {
                continue;
            }

            var moduleIdentity = AkburaGenerationCatalogBuilder.GetInlineAkcssModuleIdentity(
                sourcePath, inlineModules.Count);
            var descriptor = CreateAkcssDescriptor(
                syntaxTree, inlineBlock, sourcePath, moduleIdentity, rootNamespace, version);
            moduleTypeNames[inlineBlock] = descriptor.GeneratedTypeName;
            inlineModules.Add(descriptor);
            allInlineModules.Add(descriptor);
        }

        return new ComponentDocumentDescriptor(
            syntaxTree, sourcePath, metadataName, version, inlineModules.ToImmutable());
    }

    private static AkcssDocumentDescriptor CreateAkcssDescriptor(
        AkburaSyntaxTree syntaxTree,
        AkburaSyntax root,
        string sourcePath,
        string moduleIdentity,
        string rootNamespace,
        DocumentSyntaxVersion version)
    {
        return new AkcssDocumentDescriptor(
            syntaxTree,
            root,
            sourcePath,
            moduleIdentity,
            AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(rootNamespace, moduleIdentity),
            version);
    }
}
