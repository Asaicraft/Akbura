using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Completion;

/// <summary>
/// Describes one syntax-only AKCSS completion request.
/// </summary>
public readonly struct AkcssSyntacticCompletionContext
{
    internal AkcssSyntacticCompletionContext(
        AkcssCompletionContextKind kind,
        TextSpan applicableSpan,
        string prefix,
        SyntaxKind ownerKind = SyntaxKind.None,
        TextSpan ownerSpan = default,
        TextSpan containingDeclarationSpan = default,
        string? qualifier = null,
        string? propertyName = null)
    {
        Kind = kind;
        ApplicableSpan = applicableSpan;
        Prefix = prefix ?? string.Empty;
        OwnerKind = ownerKind;
        OwnerSpan = ownerSpan;
        ContainingDeclarationSpan = containingDeclarationSpan;
        Qualifier = qualifier ?? string.Empty;
        PropertyName = propertyName ?? string.Empty;
    }

    public AkcssCompletionContextKind Kind { get; }

    public TextSpan ApplicableSpan { get; }

    public string Prefix { get; }

    public string Qualifier { get; }

    public string PropertyName { get; }

    public bool IsDefault => Kind == AkcssCompletionContextKind.None;

    internal SyntaxKind OwnerKind { get; }

    internal TextSpan OwnerSpan { get; }

    internal TextSpan ContainingDeclarationSpan { get; }
}
