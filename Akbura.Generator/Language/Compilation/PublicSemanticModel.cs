namespace Akbura.Language;

/// <summary>
/// Public-safe semantic model layer. Instances of this type can be exposed as the facade for external semantic queries.
/// </summary>
internal abstract class PublicSemanticModel : AkburaSemanticModel
{
    protected PublicSemanticModel(
        AkburaCompilation compilation,
        AkburaSyntaxTree syntaxTree)
        : base(compilation, syntaxTree)
    {
    }
}
