using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal readonly struct AkcssGenerationInput
{
    public AkcssGenerationInput(
        IAkcssModuleSymbol module,
        AkburaSemanticModel semanticModel,
        string sourcePath,
        string moduleIdentity,
        ImmutableArray<UsingDirectiveSyntax> usingDirectives)
    {
        Module = module;
        SemanticModel = semanticModel;
        SourcePath = sourcePath;
        ModuleIdentity = moduleIdentity;
        UsingDirectives = usingDirectives.IsDefault
            ? []
            : usingDirectives;
    }

    public IAkcssModuleSymbol Module { get; }

    public AkburaSemanticModel SemanticModel { get; }

    /// <summary>
    /// Source path used by diagnostics and generated metadata.
    /// For an inline module, this is the containing component path.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Stable identity used to generate the module type and hint name.
    /// </summary>
    public string ModuleIdentity { get; }

    public ImmutableArray<UsingDirectiveSyntax> UsingDirectives { get; }
}
