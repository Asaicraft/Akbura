using Akbura.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using CsharpRawNode = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxNode;

namespace Akbura.Language.Syntax.Green;

internal static partial class GreenSyntaxFactory
{
    private const string CrLf = "\r\n";

    public static readonly GreenSyntaxTrivia CarriageReturnLineFeed = EndOfLine(CrLf);
    public static readonly GreenSyntaxTrivia LineFeed = EndOfLine("\n");
    public static readonly GreenSyntaxTrivia CarriageReturn = EndOfLine("\r");
    public static readonly GreenSyntaxTrivia Space = Whitespace(" ");
    public static readonly GreenSyntaxTrivia Tab = Whitespace("\t");

    public static readonly GreenSyntaxTrivia ElasticCarriageReturnLineFeed = EndOfLine(CrLf, elastic: true);
    public static readonly GreenSyntaxTrivia ElasticLineFeed = EndOfLine("\n", elastic: true);
    public static readonly GreenSyntaxTrivia ElasticCarriageReturn = EndOfLine("\r", elastic: true);
    public static readonly GreenSyntaxTrivia ElasticSpace = Whitespace(" ", elastic: true);
    public static readonly GreenSyntaxTrivia ElasticTab = Whitespace("\t", elastic: true);

    public static readonly GreenSyntaxTrivia ElasticZeroSpace = Whitespace(string.Empty, elastic: true);

    public static readonly GreenSyntaxToken EndOfFile = Token(null, SyntaxKind.EndOfFileToken, null);

    public static GreenSyntaxTrivia EndOfLine(string text, bool elastic = false)
    {
        GreenSyntaxTrivia? trivia = null;

        // use predefined trivia
        switch (text)
        {
            case "\r":
            {
                trivia = elastic ? ElasticCarriageReturn : CarriageReturn;
                break;
            }
            case "\n":
            {
                trivia = elastic ? ElasticLineFeed : LineFeed;
                break;
            }
            case "\r\n":
            {
                trivia = elastic ? ElasticCarriageReturnLineFeed : CarriageReturnLineFeed;
                break;
            }
        }

        // note: predefined trivia might not yet be defined during initialization
        if (trivia != null)
        {
            return trivia;
        }

        trivia = GreenSyntaxTrivia.Create(SyntaxKind.EndOfLineTrivia, text);
        if (!elastic)
        {
            return trivia;
        }

        return (GreenSyntaxTrivia)trivia.WithAnnotations([AkburaSyntaxAnnotation.ElasticAnnotation]);
    }

    public static GreenSyntaxTrivia Whitespace(string text, bool elastic = false)
    {
        var trivia = GreenSyntaxTrivia.Create(SyntaxKind.WhitespaceTrivia, text);
        if (!elastic)
        {
            return trivia;
        }

        return (GreenSyntaxTrivia)trivia.WithAnnotations([AkburaSyntaxAnnotation.ElasticAnnotation]);
    }

    public static GreenSyntaxToken Token(SyntaxKind kind)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));
        Debug.Assert(kind != SyntaxKind.IdentifierToken);
        Debug.Assert(kind != SyntaxKind.CharLiteralToken);
        Debug.Assert(kind != SyntaxKind.NumericLiteralToken);

        return GreenSyntaxToken.Create(kind);
    }

    public static GreenSyntaxToken TokenWithTrailingSpace(SyntaxKind kind)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));
        Debug.Assert(kind != SyntaxKind.IdentifierToken);
        Debug.Assert(kind != SyntaxKind.CharLiteralToken);
        Debug.Assert(kind != SyntaxKind.NumericLiteralToken);

        return GreenSyntaxToken.CreateTokenWithTrailingSpace(kind);
    }

    public static GreenSyntaxToken Token(GreenNode? leading, SyntaxKind kind, GreenNode? trailing)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));
        Debug.Assert(kind != SyntaxKind.IdentifierToken);
        Debug.Assert(kind != SyntaxKind.CharLiteralToken);
        Debug.Assert(kind != SyntaxKind.NumericLiteralToken);

        return GreenSyntaxToken.Create(kind, leading, trailing);
    }

    /// <summary>Creates a token whose Text and ValueText are the same.</summary>
    public static GreenSyntaxToken Token(GreenNode leading, SyntaxKind kind, string text, GreenNode trailing)
    {
        return Token(leading, kind, text, text, trailing);
    }

    public static GreenSyntaxToken Token(GreenNode leading, SyntaxKind kind, string text, string valueText, GreenNode trailing)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));
        Debug.Assert(kind != SyntaxKind.IdentifierToken);
        Debug.Assert(kind != SyntaxKind.CharLiteralToken);
        Debug.Assert(kind != SyntaxKind.NumericLiteralToken);

        var defaultText = SyntaxFacts.GetText(kind);

        return kind >= GreenSyntaxToken.FirstTokenWithWellKnownText &&
               kind <= GreenSyntaxToken.LastTokenWithWellKnownText &&
               text == defaultText &&
               valueText == defaultText
            ? Token(leading, kind, trailing)
            : GreenSyntaxToken.WithValue(kind, leading, text, valueText, trailing);
    }

    public static GreenSyntaxToken MissingToken(SyntaxKind kind)
    {
        return GreenSyntaxToken.CreateMissing(kind);
    }

    public static GreenSyntaxToken MissingToken(GreenNode leading, SyntaxKind kind, GreenNode trailing)
    {
        return GreenSyntaxToken.CreateMissing(kind, leading, trailing);
    }

    public static GreenSyntaxToken Identifier(string text)
    {
        return Identifier(SyntaxKind.IdentifierToken, null, text, text, null);
    }

    public static GreenSyntaxToken Identifier(GreenNode? leading, string text, GreenNode? trailing)
    {
        return Identifier(SyntaxKind.IdentifierToken, leading, text, text, trailing);
    }

    public static GreenSyntaxToken Identifier(
        SyntaxKind contextualKind,
        GreenNode? leading,
        string text,
        string valueText,
        GreenNode? trailing)
    {
        return GreenSyntaxToken.Identifier(contextualKind, leading, text, valueText, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode? leading, string text, int value, GreenNode? trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.NumericLiteralToken, leading, text, value, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode leading, string text, uint value, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.NumericLiteralToken, leading, text, value, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode leading, string text, long value, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.NumericLiteralToken, leading, text, value, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode leading, string text, ulong value, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.NumericLiteralToken, leading, text, value, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode leading, string text, float value, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.NumericLiteralToken, leading, text, value, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode leading, string text, double value, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.NumericLiteralToken, leading, text, value, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode leading, string text, decimal value, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.NumericLiteralToken, leading, text, value, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode leading, string text, string value, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.StringLiteralToken, leading, text, value, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode leading, string text, SyntaxKind kind, string value, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(kind, leading, text, value, trailing);
    }

    public static GreenSyntaxToken Literal(GreenNode leading, string text, char value, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.CharLiteralToken, leading, text, value, trailing);
    }

    public static GreenSyntaxToken BadToken(GreenNode leading, string text, GreenNode trailing)
    {
        return GreenSyntaxToken.WithValue(SyntaxKind.BadToken, leading, text, text, trailing);
    }

    public static GreenSyntaxList<TNode> List<TNode>() where TNode : GreenNode
    {
        return default;
    }

    public static GreenSyntaxList<TNode> List<TNode>(TNode node) where TNode : GreenNode
    {
        return new GreenSyntaxList<TNode>(GreenSyntaxList.List(node));
    }

    public static GreenSyntaxList<TNode> List<TNode>(TNode node0, TNode node1) where TNode : GreenNode
    {
        return new GreenSyntaxList<TNode>(GreenSyntaxList.List(node0, node1));
    }

    public static GreenNode ListNode(GreenNode node0, GreenNode node1)
    {
        return GreenSyntaxList.List(node0, node1);
    }

    public static GreenSyntaxList<TNode> List<TNode>(TNode node0, TNode node1, TNode node2) where TNode : GreenNode
    {
        return new GreenSyntaxList<TNode>(GreenSyntaxList.List(node0, node1, node2));
    }

    public static GreenNode ListNode(GreenNode node0, GreenNode node1, GreenNode node2)
    {
        return GreenSyntaxList.List(node0, node1, node2);
    }

    public static GreenSyntaxList<TNode> List<TNode>(params TNode[] nodes) where TNode : GreenNode
    {
        if (nodes != null)
        {
            return new GreenSyntaxList<TNode>(GreenSyntaxList.List(nodes));
        }

        return default;
    }

    public static GreenNode ListNode(params ArrayElement<GreenNode>[] nodes)
    {
        return GreenSyntaxList.List(nodes);
    }

    public static SeparatedGreenSyntaxList<TNode> SeparatedList<TNode>(TNode node) where TNode : GreenNode
    {
        return new SeparatedGreenSyntaxList<TNode>(new GreenSyntaxList<GreenNode>(node));
    }

    public static SeparatedGreenSyntaxList<TNode> SeparatedList<TNode>(GreenSyntaxToken token) where TNode : GreenNode
    {
        return new SeparatedGreenSyntaxList<TNode>(new GreenSyntaxList<GreenNode>(token));
    }

    public static SeparatedGreenSyntaxList<TNode> SeparatedList<TNode>(
        TNode node1,
        GreenSyntaxToken token,
        TNode node2)
        where TNode : GreenNode
    {
        return new SeparatedGreenSyntaxList<TNode>(
            new GreenSyntaxList<GreenNode>(GreenSyntaxList.List(node1, token, node2)));
    }

    public static SeparatedGreenSyntaxList<TNode> SeparatedList<TNode>(params GreenNode[] nodes)
        where TNode : GreenNode
    {
        if (nodes != null)
        {
            return new SeparatedGreenSyntaxList<TNode>(GreenSyntaxList.List(nodes));
        }

        return default;
    }

    public static IEnumerable<GreenSyntaxTrivia> GetWellKnownTrivia()
    {
        yield return CarriageReturnLineFeed;
        yield return LineFeed;
        yield return CarriageReturn;
        yield return Space;
        yield return Tab;

        yield return ElasticCarriageReturnLineFeed;
        yield return ElasticLineFeed;
        yield return ElasticCarriageReturn;
        yield return ElasticSpace;
        yield return ElasticTab;

        yield return ElasticZeroSpace;
    }

    public static IEnumerable<GreenSyntaxToken> GetWellKnownTokens()
    {
        return GreenSyntaxToken.GetWellKnownTokens();
    }

    public static GreenSyntaxToken CSharpRawToken(string text)
    {
        return GreenSyntaxToken.CreateCSharpRawToken(text);
    }

    public static GreenSyntaxToken CSharpRawToken(CsharpRawNode csharpRawNode)
    {
        return GreenSyntaxToken.CreateCSharpRawToken(csharpRawNode);
    }

    public static GreenNode? AkTextLiteral(string text)
    {
        return GreenSyntaxToken.AkTextLiteral(text);
    }

    public static GreenNode? AkTextLiteralToken(string text, string value)
    {
        return GreenSyntaxToken.AkTextLiteralToken(text, value);
    }
}