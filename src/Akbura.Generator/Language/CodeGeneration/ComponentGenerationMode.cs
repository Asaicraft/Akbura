using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.Language.CodeGeneration;

internal enum ComponentGenerationMode : byte
{
    ReleaseDirect,
    DebugStructural,
    ReleaseConditional,
}

internal static class ComponentGenerationModeExtensions
{
    public static bool UsesStructuralRuntime(this ComponentGenerationMode mode) =>
        mode is ComponentGenerationMode.DebugStructural or ComponentGenerationMode.ReleaseConditional;

    public static ComponentGenerationMode ForComponent(
        this ComponentGenerationMode mode, IAkburaComponentSymbol component)
    {
        if (mode == ComponentGenerationMode.ReleaseDirect)
        {
            foreach (var syntax in component.DeclarationSyntax.DescendantNodesAndSelf())
            {
                if (syntax is MarkupIfStatementSyntax)
                {
                    return ComponentGenerationMode.ReleaseConditional;
                }
            }
        }

        return mode;
    }
}
