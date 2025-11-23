using Akbura.Language.Syntax.Green;
using CsharpRawNode = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxNode;

namespace Akbura.Language.Syntax;
internal static partial class SyntaxFactory
{
    /// <summary>
    /// A trivia with kind EndOfLineTrivia containing both the carriage return and line feed characters.
    /// </summary>
    public static SyntaxTrivia CarriageReturnLineFeed { get; } = GreenSyntaxFactory.CarriageReturnLineFeed;

    /// <summary>
    /// A trivia with kind EndOfLineTrivia containing a single line feed character.
    /// </summary>
    public static SyntaxTrivia LineFeed { get; } = GreenSyntaxFactory.LineFeed;

    /// <summary>
    /// A trivia with kind EndOfLineTrivia containing a single carriage return character.
    /// </summary>
    public static SyntaxTrivia CarriageReturn { get; } = GreenSyntaxFactory.CarriageReturn;

    /// <summary>
    /// A trivia with kind WhitespaceTrivia containing a single space character.
    /// </summary>
    public static SyntaxTrivia Space { get; } = GreenSyntaxFactory.Space;

    /// <summary>
    /// A trivia with kind WhitespaceTrivia containing a single tab character.
    /// </summary>
    public static SyntaxTrivia Tab { get; } = GreenSyntaxFactory.Tab;

    /// <summary>
    /// An elastic trivia (EndOfLineTrivia) containing both carriage return and line feed characters. 
    /// Elastic trivia is not preserved by formatting.
    /// </summary>
    public static SyntaxTrivia ElasticCarriageReturnLineFeed { get; } = GreenSyntaxFactory.ElasticCarriageReturnLineFeed;

    /// <summary>
    /// An elastic trivia (EndOfLineTrivia) containing a single line feed character.
    /// </summary>
    public static SyntaxTrivia ElasticLineFeed { get; } = GreenSyntaxFactory.ElasticLineFeed;

    /// <summary>
    /// An elastic trivia (EndOfLineTrivia) containing a single carriage return character.
    /// </summary>
    public static SyntaxTrivia ElasticCarriageReturn { get; } = GreenSyntaxFactory.ElasticCarriageReturn;

    /// <summary>
    /// An elastic trivia (WhitespaceTrivia) containing a single space character.
    /// </summary>
    public static SyntaxTrivia ElasticSpace { get; } = GreenSyntaxFactory.ElasticSpace;

    /// <summary>
    /// An elastic trivia (WhitespaceTrivia) containing a single tab character.
    /// </summary>
    public static SyntaxTrivia ElasticTab { get; } = GreenSyntaxFactory.ElasticTab;

    /// <summary>
    /// An elastic trivia with zero characters, used as an 'elastic marker'. Syntax formatting typically replaces it
    /// .</summary>
    public static SyntaxTrivia ElasticMarker { get; } = GreenSyntaxFactory.ElasticZeroSpace;

    /// <summary>
    /// Creates a trivia with kind EndOfLineTrivia containing the specified text.
    /// </summary>
    /// <param name="text">The text of the end of line. Only CR and LF are recognized as actual EOL by the parser.</param>
    public static SyntaxTrivia EndOfLine(string text)
    {
        return GreenSyntaxFactory.EndOfLine(text, elastic: false);
    }

    /// <summary>
    /// Creates an elastic trivia with kind EndOfLineTrivia containing the specified text.
    /// </summary>
    /// <param name="text">The text of the end of line. Only CR and LF are recognized as actual EOL by the parser.</param>
    public static SyntaxTrivia ElasticEndOfLine(string text)
    {
        return GreenSyntaxFactory.EndOfLine(text, elastic: true);
    }

    /// <summary>
    /// Creates a trivia with kind WhitespaceTrivia containing the specified text.
    /// </summary>
    /// <param name="text">The text of the whitespace. Only certain characters are recognized as whitespace by the parser.</param>
    public static SyntaxTrivia Whitespace(string text)
    {
        return GreenSyntaxFactory.Whitespace(text, elastic: false);
    }

    /// <summary>
    /// Creates an elastic trivia with kind WhitespaceTrivia containing the specified text.
    /// </summary>
    /// <param name="text">The text of the whitespace. Only certain characters are recognized as whitespace by the parser.</param>
    public static SyntaxTrivia ElasticWhitespace(string text)
    {
        return GreenSyntaxFactory.Whitespace(text, elastic: true);
    }

    /// <summary>
    /// Creates a token corresponding to a syntax kind with no explicit leading/trailing trivia. The token text is inferred from the kind.
    /// </summary>
    /// <param name="kind">A syntax kind for tokens (e.g. SomethingToken, SomethingKeyword).</param>
    public static SyntaxToken Token(SyntaxKind kind)
    {
        return new SyntaxToken(GreenSyntaxFactory.Token(ElasticMarker.UnderlyingNode, kind, ElasticMarker.UnderlyingNode));
    }

    /// <summary>
    /// Creates a token corresponding to a syntax kind, with specified leading and trailing trivia.
    /// </summary>
    /// <param name="leading">A list of trivia preceding the token.</param>
    /// <param name="kind">A syntax kind for tokens (e.g. SomethingToken, SomethingKeyword).</param>
    /// <param name="trailing">A list of trivia following the token.</param>
    public static SyntaxToken Token(SyntaxTriviaList leading, SyntaxKind kind, SyntaxTriviaList trailing)
    {
        if (kind == SyntaxKind.IdentifierToken || kind == SyntaxKind.CharLiteralToken || kind == SyntaxKind.NumericLiteralToken || !SyntaxFacts.IsAnyToken(kind))
        {
            ThrowHelper.ThrowArgumentException("kind", "Invalid token kind. Tokens must be lexemes, not literals.");
        }
        return new SyntaxToken(GreenSyntaxFactory.Token(leading.Node, kind, trailing.Node));
    }

    public static SyntaxToken TokenWithTrailingSpace(SyntaxKind kind)
    {
        return Token(default, kind, new(Space));
    }

    /// <summary>
    /// Creates an empty list of syntax nodes.
    /// </summary>
    /// <typeparam name="TNode">The specific type of the element nodes.</typeparam>
    public static SyntaxList<TNode> List<TNode>() where TNode : AkburaSyntax
    {
        return default;
    }

    /// <summary>
    /// Creates a singleton list of syntax nodes.
    /// </summary>
    /// <typeparam name="TNode">The specific type of the element nodes.</typeparam>
    /// <param name="node">The single element node.</param>
    /// <returns></returns>
    public static SyntaxList<TNode> SingletonList<TNode>(TNode node) where TNode : AkburaSyntax
    {
        return new SyntaxList<TNode>(node);
    }

    /// <summary>
    /// Creates a list of syntax nodes.
    /// </summary>
    /// <typeparam name="TNode">The specific type of the element nodes.</typeparam>
    /// <param name="nodes">A sequence of element nodes.</param>
    public static SyntaxList<TNode> List<TNode>(IEnumerable<TNode> nodes) where TNode : AkburaSyntax
    {
        return new SyntaxList<TNode>(nodes);
    }

    /// <summary>
    /// Creates an empty list of tokens.
    /// </summary>
    public static SyntaxTokenList TokenList()
    {
        return default;
    }

    /// <summary>
    /// Creates a singleton list of tokens.
    /// </summary>
    /// <param name="token">The single token.</param>
    public static SyntaxTokenList TokenList(SyntaxToken token)
    {
        return new SyntaxTokenList(token);
    }

    /// <summary>
    /// Creates a list of tokens.
    /// </summary>
    /// <param name="tokens">An array of tokens.</param>
    public static SyntaxTokenList TokenList(params SyntaxToken[] tokens)
    {
        return new SyntaxTokenList(tokens);
    }

    /// <summary>
    /// Creates a list of tokens.
    /// </summary>
    /// <param name="tokens"></param>
    /// <returns></returns>
    public static SyntaxTokenList TokenList(IEnumerable<SyntaxToken> tokens)
    {
        return new SyntaxTokenList(tokens);
    }

    /// <summary>
    /// Creates an empty list of trivia.
    /// </summary>
    public static SyntaxTriviaList TriviaList()
    {
        return default;
    }

    /// <summary>
    /// Creates a singleton list of trivia.
    /// </summary>
    /// <param name="trivia">A single trivia.</param>
    public static SyntaxTriviaList TriviaList(SyntaxTrivia trivia)
    {
        return new SyntaxTriviaList(trivia);
    }

    /// <summary>
    /// Creates a list of trivia.
    /// </summary>
    /// <param name="trivias">An array of trivia.</param>
    public static SyntaxTriviaList TriviaList(params SyntaxTrivia[] trivias) => new(trivias);

    /// <summary>
    /// Creates a list of trivia.
    /// </summary>
    /// <param name="trivias">A sequence of trivia.</param>
    public static SyntaxTriviaList TriviaList(IEnumerable<SyntaxTrivia> trivias) => new(trivias);

    /// <summary>
    /// Creates an empty separated list.
    /// </summary>
    /// <typeparam name="TNode">The specific type of the element nodes.</typeparam>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>() where TNode : AkburaSyntax
    {
        return default;
    }

    /// <summary>
    /// Creates a singleton separated list.
    /// </summary>
    /// <typeparam name="TNode">The specific type of the element nodes.</typeparam>
    /// <param name="node">A single node.</param>
    public static SeparatedSyntaxList<TNode> SingletonSeparatedList<TNode>(TNode node) where TNode : AkburaSyntax
    {
        return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(node, index: 0));
    }

    /// <summary>
    /// Creates a separated list of nodes from a sequence of nodes, synthesizing comma separators in between.
    /// </summary>
    /// <typeparam name="TNode">The specific type of the element nodes.</typeparam>
    /// <param name="nodes">A sequence of syntax nodes.</param>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<TNode>? nodes) where TNode : AkburaSyntax
    {
        if (nodes == null)
        {
            return default;
        }

        var collection = nodes as ICollection<TNode>;

        if (collection != null && collection.Count == 0)
        {
            return default;
        }

        using var enumerator = nodes.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return default;
        }

        var firstNode = enumerator.Current;

        if (!enumerator.MoveNext())
        {
            return SingletonSeparatedList<TNode>(firstNode);
        }

        var builder = new SeparatedSyntaxListBuilder<TNode>(collection != null ? collection.Count : 3);

        builder.Add(firstNode);

        var commaToken = Token(SyntaxKind.CommaToken);

        do
        {
            builder.AddSeparator(commaToken);
            builder.Add(enumerator.Current);
        }
        while (enumerator.MoveNext());

        return builder.ToList();
    }

    /// <summary>
    /// Creates a separated list of nodes from a sequence of nodes and a sequence of separator tokens.
    /// </summary>
    /// <typeparam name="TNode">The specific type of the element nodes.</typeparam>
    /// <param name="nodes">A sequence of syntax nodes.</param>
    /// <param name="separators">A sequence of token to be interleaved between the nodes. The number of tokens must
    /// be one less than the number of nodes.</param>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<TNode>? nodes, IEnumerable<SyntaxToken>? separators) where TNode : AkburaSyntax
    {
        // Interleave the nodes and the separators.  The number of separators must be equal to or 1 less than the number of nodes or
        // an argument exception is thrown.

        if (nodes != null)
        {
            var enumerator = nodes.GetEnumerator();
            var builder = SeparatedSyntaxListBuilder<TNode>.Create();
            if (separators != null)
            {
                foreach (var token in separators)
                {
                    if (!enumerator.MoveNext())
                    {
                        throw new ArgumentException($"{nameof(nodes)} must not be empty.", nameof(nodes));
                    }

                    builder.Add(enumerator.Current);
                    builder.AddSeparator(token);
                }
            }

            if (enumerator.MoveNext())
            {
                builder.Add(enumerator.Current);
                if (enumerator.MoveNext())
                {
                    throw new ArgumentException($"{nameof(separators)} must have 1 fewer element than {nameof(nodes)}", nameof(separators));
                }
            }

            return builder.ToList();
        }

        if (separators != null)
        {
            throw new ArgumentException($"When {nameof(nodes)} is null, {nameof(separators)} must also be null.", nameof(separators));
        }

        return default;
    }

    /// <summary>
    /// Creates a separated list from a sequence of nodes and tokens, starting with a node and alternating between additional nodes and separator tokens.
    /// </summary>
    /// <typeparam name="TNode">The specific type of the element nodes.</typeparam>
    /// <param name="nodesAndTokens">A sequence of nodes or tokens, alternating between nodes and separator tokens.</param>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>(IEnumerable<SyntaxNodeOrToken> nodesAndTokens) where TNode : AkburaSyntax
    {
        return SeparatedList<TNode>(NodeOrTokenList(nodesAndTokens));
    }

    /// <summary>
    /// Creates a separated list from a <see cref="SyntaxNodeOrTokenList"/>, where the list elements start with a node and then alternate between
    /// additional nodes and separator tokens.
    /// </summary>
    /// <typeparam name="TNode">The specific type of the element nodes.</typeparam>
    /// <param name="nodesAndTokens">The list of nodes and tokens.</param>
    public static SeparatedSyntaxList<TNode> SeparatedList<TNode>(SyntaxNodeOrTokenList nodesAndTokens) where TNode : AkburaSyntax
    {
        if (!HasSeparatedNodeTokenPattern(nodesAndTokens))
        {
            throw new ArgumentException("Node or token out of sequence.");
        }

        if (!NodesAreCorrectType<TNode>(nodesAndTokens))
        {
            throw new ArgumentException("Unexpected type of node in list.");
        }

        return new SeparatedSyntaxList<TNode>(nodesAndTokens);
    }

    private static bool NodesAreCorrectType<TNode>(SyntaxNodeOrTokenList list)
    {
        for (int i = 0, n = list.Count; i < n; i++)
        {
            var element = list[i];
            if (element.IsNode && element.AsNode() is not TNode)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasSeparatedNodeTokenPattern(SyntaxNodeOrTokenList list)
    {
        for (int i = 0, n = list.Count; i < n; i++)
        {
            var element = list[i];
            if (element.IsToken == ((i & 1) == 0))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Creates an empty <see cref="SyntaxNodeOrTokenList"/>.
    /// </summary>
    public static SyntaxNodeOrTokenList NodeOrTokenList()
    {
        return default;
    }

    /// <summary>
    /// Create a <see cref="SyntaxNodeOrTokenList"/> from a sequence of <see cref="SyntaxNodeOrToken"/>.
    /// </summary>
    /// <param name="nodesAndTokens">The sequence of nodes and tokens</param>
    public static SyntaxNodeOrTokenList NodeOrTokenList(IEnumerable<SyntaxNodeOrToken> nodesAndTokens)
    {
        return new SyntaxNodeOrTokenList(nodesAndTokens);
    }

    /// <summary>
    /// Create a <see cref="SyntaxNodeOrTokenList"/> from one or more <see cref="SyntaxNodeOrToken"/>.
    /// </summary>
    /// <param name="nodesAndTokens">The nodes and tokens</param>
    public static SyntaxNodeOrTokenList NodeOrTokenList(params SyntaxNodeOrToken[] nodesAndTokens)
    {
        return new SyntaxNodeOrTokenList(nodesAndTokens);
    }

    /// <summary>
    /// Creates an IdentifierNameSyntax node.
    /// </summary>
    /// <param name="name">The identifier name.</param>
    public static IdentifierNameSyntax IdentifierName(string name)
    {
        return IdentifierName(Identifier(name));
    }

    /// <summary>
    /// Creates a token with kind IdentifierToken containing the specified text.
    /// </summary>
    /// <param name="leading">A list of trivia immediately preceding the token.</param>
    /// <param name="text">The raw text of the identifier name, including any escapes or leading '@'
    /// character.</param>
    /// <param name="trailing">A list of trivia immediately following the token.</param>
    public static SyntaxToken Identifier(SyntaxTriviaList leading, string text, SyntaxTriviaList trailing)
    {
        return new SyntaxToken(GreenSyntaxFactory.Identifier(leading.Node, text, trailing.Node));
    }

    /// <summary>
    /// Creates a token with kind IdentifierToken containing the specified text.
    /// </summary>
    /// <param name="leading">A list of trivia immediately preceding the token.</param>
    /// <param name="contextualKind">An alternative SyntaxKind that can be inferred for this token in special
    /// contexts. These are usually keywords.</param>
    /// <param name="text">The raw text of the identifier name, including any escapes or leading '@'
    /// character.</param>
    /// <param name="valueText">The text of the identifier name without escapes or leading '@' character.</param>
    /// <param name="trailing">A list of trivia immediately following the token.</param>
    /// <returns></returns>
    public static SyntaxToken Identifier(SyntaxTriviaList leading, SyntaxKind contextualKind, string text, string valueText, SyntaxTriviaList trailing)
    {
        return new SyntaxToken(GreenSyntaxFactory.Identifier(contextualKind, leading.Node, text, valueText, trailing.Node));
    }

    /// <summary>
    /// Creates a token with kind IdentifierToken containing the specified text.
    /// </summary>
    /// <param name="text">The raw text of the identifier name, including any escapes or leading '@' character.</param>        
    public static SyntaxToken Identifier(string text)
    {
        return new SyntaxToken(GreenSyntaxFactory.Identifier(ElasticMarker.UnderlyingNode, text, ElasticMarker.UnderlyingNode));
    }

    public static SyntaxToken IdentifierWithTrailingSpace(string text)
    {
        return Identifier(default, text, new(Space));
    }

    public static SyntaxToken NumericLiteralToken(string text, int value)
    {
        return new SyntaxToken(GreenSyntaxFactory.Literal(null, text, value, null));
    }

    public static SyntaxToken CSharpRawToken(string text)
    {
        return new SyntaxToken(GreenSyntaxFactory.CSharpRawToken(text));
    }

    public static SyntaxToken CSharpRawToken(CsharpRawNode csharpRawNode)
    {
        return new SyntaxToken(GreenSyntaxFactory.CSharpRawToken(csharpRawNode));
    }

    public static SyntaxToken EndOfFileToken()
    {
        return new SyntaxToken(GreenSyntaxFactory.EndOfFile);
    }
}

