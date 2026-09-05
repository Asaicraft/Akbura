using Akbura.Language.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Contains all semantic generation inputs for one project snapshot.
/// </summary>
internal sealed class AkburaGenerationCatalog
{
    public AkburaGenerationCatalog(
        AkburaCompilation compilation,
        string rootNamespace,
        string projectDirectory,
        ImmutableArray<ComponentGenerationInput> components,
        ImmutableArray<AkcssGenerationInput> externalAkcssModules,
        ImmutableArray<AkcssGenerationInput> inlineAkcssModules,
        IReadOnlyDictionary<AkburaSyntax, string> akcssModuleTypeNames,
        AkcssGenerationSourceMap akcssSourceMap)
    {
        Compilation = compilation;
        RootNamespace = rootNamespace;
        ProjectDirectory = projectDirectory;

        Components = components.IsDefault
            ? []
            : components;

        ExternalAkcssModules = externalAkcssModules.IsDefault
            ? []
            : externalAkcssModules;

        InlineAkcssModules = inlineAkcssModules.IsDefault
            ? []
            : inlineAkcssModules;

        AkcssModuleTypeNames = akcssModuleTypeNames;
        AkcssSourceMap = akcssSourceMap;
    }

    public AkburaCompilation Compilation { get; }

    public string RootNamespace { get; }

    public string ProjectDirectory { get; }

    public ImmutableArray<ComponentGenerationInput> Components { get; }

    public ImmutableArray<AkcssGenerationInput> ExternalAkcssModules { get; }

    public ImmutableArray<AkcssGenerationInput> InlineAkcssModules { get; }

    public IReadOnlyDictionary<AkburaSyntax, string> AkcssModuleTypeNames { get; }

    public AkcssGenerationSourceMap AkcssSourceMap { get; }

    public bool HasComponents => !Components.IsEmpty;

    public bool HasAkcss =>
        !ExternalAkcssModules.IsEmpty ||
        !InlineAkcssModules.IsEmpty;
}
