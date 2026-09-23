using Akbura.Language.Syntax;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Workspaces.Completion;

internal static class AkburaHookCompletionFacts
{
    public static bool TryGetStateInitializerContext(
        AkburaSyntacticDocument document,
        AkburaCSharpCompletionContext csharpContext,
        int position,
        out AkburaHookCompletionContext context,
        CancellationToken cancellationToken = default)
    {
        context = default;
        if (csharpContext.Kind != AkburaCSharpCompletionContextKind.Expression)
        {
            return false;
        }

        StateDeclarationSyntax? declaration = null;
        var declarations = document.SyntaxTree
            .GetRootSyntax()
            .DescendantNodes()
            .OfType<StateDeclarationSyntax>();
        foreach (var candidate in declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Initializer.Expression.FullSpan == csharpContext.OwnerSpan)
            {
                declaration = candidate;
                break;
            }
        }

        if (declaration == null ||
            !EmbeddedCSharpSyntaxFacts.TryGetExpression(
                declaration.Initializer.Expression,
                out var expression,
                out var hostSpan) ||
            expression is not CSharp.IdentifierNameSyntax identifier)
        {
            return false;
        }

        var identifierSpan = new TextSpan(
            hostSpan.Start + identifier.Identifier.Span.Start,
            identifier.Identifier.Span.Length);
        if (position < identifierSpan.Start || position > identifierSpan.End)
        {
            return false;
        }

        context = new AkburaHookCompletionContext(
            declaration.FullSpan,
            identifierSpan,
            document.Text.ToString(TextSpan.FromBounds(
                identifierSpan.Start,
                position)));
        return true;
    }
}

internal readonly struct AkburaHookCompletionContext
{
    public AkburaHookCompletionContext(
        TextSpan stateDeclarationSpan,
        TextSpan applicableSpan,
        string prefix)
    {
        StateDeclarationSpan = stateDeclarationSpan;
        ApplicableSpan = applicableSpan;
        Prefix = prefix;
    }

    public TextSpan StateDeclarationSpan { get; }

    public TextSpan ApplicableSpan { get; }

    public string Prefix { get; }
}
