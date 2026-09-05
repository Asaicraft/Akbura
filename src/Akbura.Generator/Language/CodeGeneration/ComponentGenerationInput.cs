using Akbura.Language.Symbols;

namespace Akbura.Language.CodeGeneration;

internal readonly struct ComponentGenerationInput
{
    public ComponentGenerationInput(
        IAkburaComponentSymbol component,
        AkburaSemanticModel semanticModel,
        string sourcePath)
    {
        Component = component;
        SemanticModel = semanticModel;
        SourcePath = sourcePath;
    }

    public IAkburaComponentSymbol Component { get; }

    public AkburaSemanticModel SemanticModel { get; }

    public string SourcePath { get; }
}
