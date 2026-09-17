using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Syntax;

internal sealed partial class MarkupForeachHeaderSyntax
{
    public const int CSharpPrefixLength = 9;

    public CSharp.ForEachStatementSyntax? GetRawCSharpForeach() =>
        Green.Token is GreenSyntaxToken.CSharpRawToken { RawNode: CSharp.ForEachStatementSyntax statement }
            ? statement
            : CSharpFactory.ParseStatement("foreach (" + Token.ToFullString() + ") {}") as CSharp.ForEachStatementSyntax;

    public TextSpan GetAbsoluteCSharpSpan(TextSpan span) =>
        new(Token.Span.Start + span.Start - CSharpPrefixLength, span.Length);
}

internal sealed partial class MarkupCodeStatementSyntax
{
    public CSharp.StatementSyntax GetRawCSharpStatement() =>
        Green.Token is GreenSyntaxToken.CSharpRawToken { RawNode: CSharp.StatementSyntax statement }
            ? statement
            : CSharpFactory.ParseStatement(Token.ToFullString());

    public TextSpan GetAbsoluteCSharpSpan(TextSpan span) => new(Token.Span.Start + span.Start, span.Length);
}
