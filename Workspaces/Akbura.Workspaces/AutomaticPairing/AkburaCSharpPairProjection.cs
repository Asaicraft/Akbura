using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces;

internal sealed class AkburaCSharpPairProjection
{
    private AkburaCSharpPairProjection(
        SyntaxNode root,
        TextSpan hostSpan,
        TextSpan projectedSpan,
        int hostPosition,
        int projectedPosition)
    {
        Root = root;
        HostSpan = hostSpan;
        ProjectedSpan = projectedSpan;
        HostPosition = hostPosition;
        ProjectedPosition = projectedPosition;
    }

    public SyntaxNode Root { get; }

    public TextSpan HostSpan { get; }

    public TextSpan ProjectedSpan { get; }

    public int HostPosition { get; }

    public int ProjectedPosition { get; }

    public SyntaxToken TypedToken => Root.FindToken(
        ProjectedPosition,
        findInsideTrivia: true);

    public SyntaxToken FindTokenAtOrBeforePosition()
    {
        var position = ProjectedPosition;
        if (position > ProjectedSpan.Start)
        {
            position--;
        }

        return Root.FindToken(
            position,
            findInsideTrivia: true);
    }

    public int MapProjectedPositionToHost(int projectedPosition)
    {
        if (projectedPosition < ProjectedSpan.Start ||
            projectedPosition > ProjectedSpan.End)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectedPosition));
        }

        return HostSpan.Start +
            projectedPosition -
            ProjectedSpan.Start;
    }

    public bool IsInsideComment
    {
        get
        {
            var trivia = Root.FindTrivia(
                ProjectedPosition,
                findInsideTrivia: true);
            return trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);
        }
    }

    public bool IsInsideStringText
    {
        get
        {
            var token = TypedToken;
            return token.Parent is LiteralExpressionSyntax ||
                token.Parent is InterpolatedStringTextSyntax;
        }
    }

    public bool IsInterpolationBrace
    {
        get
        {
            var token = TypedToken;
            return token.IsKind(SyntaxKind.OpenBraceToken) &&
                token.Parent is InterpolationSyntax;
        }
    }

    public bool IsToken(SyntaxKind kind)
    {
        var token = TypedToken;
        return token.IsKind(kind) &&
            token.Span.Start <= ProjectedPosition &&
            ProjectedPosition < token.Span.End;
    }

    public bool IsStringStart()
    {
        var token = TypedToken;
        if (token.Parent is LiteralExpressionSyntax)
        {
            return token.Span.Start == ProjectedPosition ||
                token.Text.StartsWith("@\"", StringComparison.Ordinal) &&
                token.Span.Start + 1 == ProjectedPosition;
        }

        if (token.Parent is not InterpolatedStringExpressionSyntax interpolated ||
            token != interpolated.StringStartToken)
        {
            return false;
        }

        return ProjectedPosition == token.Span.End - 1;
    }

    public bool IsGenericLessThan()
    {
        if (!IsToken(SyntaxKind.LessThanToken))
        {
            return false;
        }

        for (SyntaxNode? current = TypedToken.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is TypeArgumentListSyntax or
                TypeParameterListSyntax or
                FunctionPointerParameterListSyntax)
            {
                return true;
            }

            if (current.Span.Start < ProjectedSpan.Start ||
                current.Span.End > ProjectedSpan.End &&
                current is not IdentifierNameSyntax and
                not GenericNameSyntax)
            {
                break;
            }
        }

        var lessThan = TypedToken;
        var previous = lessThan.GetPreviousToken(
            includeZeroWidth: true,
            includeSkipped: true,
            includeDirectives: true,
            includeDocumentationComments: true);
        var next = lessThan.GetNextToken(
            includeZeroWidth: true,
            includeSkipped: true,
            includeDirectives: true,
            includeDocumentationComments: true);
        return previous.IsKind(SyntaxKind.IdentifierToken) &&
            next.IsKind(SyntaxKind.OpenParenToken) &&
            previous.Span.End == lessThan.Span.Start;
    }

    public static bool TryCreate(
        AkburaSyntacticDocument document,
        AkburaEmbeddedCSharpContext context,
        char typedCharacter,
        out AkburaCSharpPairProjection projection)
    {
        return TryCreateCore(
            document,
            context,
            typedCharacter.ToString(),
            out projection);
    }

    public static bool TryCreateCurrent(
        AkburaSyntacticDocument document,
        AkburaEmbeddedCSharpContext context,
        out AkburaCSharpPairProjection projection)
    {
        return TryCreateCore(
            document,
            context,
            string.Empty,
            out projection);
    }

    private static bool TryCreateCore(
        AkburaSyntacticDocument document,
        AkburaEmbeddedCSharpContext context,
        string insertionText,
        out AkburaCSharpPairProjection projection)
    {
        var relativePosition = context.RelativePosition;
        if ((uint)relativePosition > (uint)context.HostSpan.Length)
        {
            projection = null!;
            return false;
        }

        var fragment = document.Text.ToString(context.HostSpan);
        if (insertionText.Length != 0)
        {
            fragment = fragment.Insert(
                relativePosition,
                insertionText);
        }

        var (prefix, suffix) = context.Kind switch
        {
            AkburaCSharpCompletionContextKind.Expression =>
                ("class __AkburaPairHost { object? M() => ", "; }"),
            AkburaCSharpCompletionContextKind.Statement =>
                ("class __AkburaPairHost { void M() {\n", "\n} }"),
            AkburaCSharpCompletionContextKind.Type =>
                ("class __AkburaPairHost { ", " __value; }"),
            AkburaCSharpCompletionContextKind.UsingDirectiveName =>
                ("using ", ";"),
            AkburaCSharpCompletionContextKind.CommandParameterList =>
                ("class __AkburaPairHost { void M", " { } }"),
            _ => default,
        };
        if (prefix == null || suffix == null)
        {
            projection = null!;
            return false;
        }

        var projectedText = prefix + fragment + suffix;
        var tree = CSharpSyntaxTree.ParseText(
            projectedText,
            new CSharpParseOptions(LanguageVersion.Preview));
        var projectedSpan = new TextSpan(
            prefix.Length,
            fragment.Length);
        projection = new AkburaCSharpPairProjection(
            tree.GetRoot(),
            context.HostSpan,
            projectedSpan,
            context.HostPosition,
            prefix.Length + relativePosition);
        return true;
    }
}
