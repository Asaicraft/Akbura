using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Akbura.Language.Syntax;
internal static class SyntaxReplacer
{
    internal static AkburaSyntax Replace<TNode>(
        AkburaSyntax root,
        IEnumerable<TNode>? nodes = null,
        Func<TNode, TNode, AkburaSyntax>? computeReplacementNode = null,
        IEnumerable<SyntaxToken>? tokens = null,
        Func<SyntaxToken, SyntaxToken, SyntaxToken>? computeReplacementToken = null,
        IEnumerable<SyntaxTrivia>? trivia = null,
        Func<SyntaxTrivia, SyntaxTrivia, SyntaxTrivia>? computeReplacementTrivia = null)
        where TNode : AkburaSyntax
    {
        var replacer = new Replacer<TNode>(
            nodes, computeReplacementNode,
            tokens, computeReplacementToken,
            trivia, computeReplacementTrivia);

        if (replacer.HasWork)
        {
            return replacer.Visit(root)!;
        }
        else
        {
            return root;
        }
    }

    internal static SyntaxToken Replace(
        SyntaxToken root,
        IEnumerable<AkburaSyntax>? nodes = null,
        Func<AkburaSyntax, AkburaSyntax, AkburaSyntax>? computeReplacementNode = null,
        IEnumerable<SyntaxToken>? tokens = null,
        Func<SyntaxToken, SyntaxToken, SyntaxToken>? computeReplacementToken = null,
        IEnumerable<SyntaxTrivia>? trivia = null,
        Func<SyntaxTrivia, SyntaxTrivia, SyntaxTrivia>? computeReplacementTrivia = null)
    {
        var replacer = new Replacer<AkburaSyntax>(
            nodes, computeReplacementNode,
            tokens, computeReplacementToken,
            trivia, computeReplacementTrivia);

        if (replacer.HasWork)
        {
            return replacer.VisitToken(root);
        }
        else
        {
            return root;
        }
    }

    private class Replacer<TNode> : SyntaxRewriter where TNode : AkburaSyntax
    {
        private readonly Func<TNode, TNode, AkburaSyntax>? _computeReplacementNode;
        private readonly Func<SyntaxToken, SyntaxToken, SyntaxToken>? _computeReplacementToken;
        private readonly Func<SyntaxTrivia, SyntaxTrivia, SyntaxTrivia>? _computeReplacementTrivia;

        private readonly HashSet<AkburaSyntax> _nodeSet;
        private readonly HashSet<SyntaxToken> _tokenSet;
        private readonly HashSet<SyntaxTrivia> _triviaSet;
        private readonly HashSet<TextSpan> _spanSet;

        private TextSpan _totalSpan;
        private bool _shouldVisitTrivia;

        public Replacer(
            IEnumerable<TNode>? nodes,
            Func<TNode, TNode, AkburaSyntax>? computeReplacementNode,
            IEnumerable<SyntaxToken>? tokens,
            Func<SyntaxToken, SyntaxToken, SyntaxToken>? computeReplacementToken,
            IEnumerable<SyntaxTrivia>? trivia,
            Func<SyntaxTrivia, SyntaxTrivia, SyntaxTrivia>? computeReplacementTrivia)
        {
            _computeReplacementNode = computeReplacementNode;
            _computeReplacementToken = computeReplacementToken;
            _computeReplacementTrivia = computeReplacementTrivia;

            _nodeSet = nodes != null ? new HashSet<AkburaSyntax>(nodes) : s_noNodes;
            _tokenSet = tokens != null ? new HashSet<SyntaxToken>(tokens) : s_noTokens;
            _triviaSet = trivia != null ? new HashSet<SyntaxTrivia>(trivia) : s_noTrivia;

            _spanSet = new HashSet<TextSpan>();

            CalculateVisitationCriteria();
        }

        private static readonly HashSet<AkburaSyntax> s_noNodes = new HashSet<AkburaSyntax>();
        private static readonly HashSet<SyntaxToken> s_noTokens = new HashSet<SyntaxToken>();
        private static readonly HashSet<SyntaxTrivia> s_noTrivia = new HashSet<SyntaxTrivia>();

        public bool HasWork
        {
            get
            {
                return _nodeSet.Count + _tokenSet.Count + _triviaSet.Count > 0;
            }
        }

        private void CalculateVisitationCriteria()
        {
            _spanSet.Clear();
            foreach (var node in _nodeSet)
            {
                _spanSet.Add(node.FullSpan);
            }

            foreach (var token in _tokenSet)
            {
                _spanSet.Add(token.FullSpan);
            }

            foreach (var trivia in _triviaSet)
            {
                _spanSet.Add(trivia.FullSpan);
            }

            var first = true;
            var start = 0;
            var end = 0;

            foreach (var span in _spanSet)
            {
                if (first)
                {
                    start = span.Start;
                    end = span.End;
                    first = false;
                }
                else
                {
                    start = Math.Min(start, span.Start);
                    end = Math.Max(end, span.End);
                }
            }

            _totalSpan = new TextSpan(start, end - start);

            // No structured trivia in this syntax.
            _shouldVisitTrivia = _triviaSet.Count > 0;
        }

        private bool ShouldVisit(TextSpan span)
        {
            // first do quick check against total span
            if (!span.IntersectsWith(_totalSpan))
            {
                // if the node is outside the total span of the nodes to be replaced
                // then we won't find any nodes to replace below it.
                return false;
            }

            foreach (var s in _spanSet)
            {
                if (span.IntersectsWith(s))
                {
                    // node's full span intersects with at least one node to be replaced
                    // so we need to visit node's children to find it.
                    return true;
                }
            }

            return false;
        }

        [return: NotNullIfNotNull(nameof(node))]
        public override AkburaSyntax? Visit(AkburaSyntax? node)
        {
            var rewritten = node;

            if (node != null)
            {
                var isReplacedNode = _nodeSet.Remove(node);

                if (isReplacedNode)
                {
                    // If node is in _nodeSet, then it contributed to the calculation of _spanSet.
                    // We are currently processing that node, so it no longer needs to contribute
                    // to _spanSet and affect determination of inward visitation. This is done before
                    // calling ShouldVisit to avoid walking into the node if there aren't any remaining
                    // spans inside it representing items to replace.
                    CalculateVisitationCriteria();
                }

                if (this.ShouldVisit(node.FullSpan))
                {
                    rewritten = base.Visit(node);
                }

                if (isReplacedNode && _computeReplacementNode != null)
                {
                    rewritten = (AkburaSyntax)_computeReplacementNode((TNode)node, (TNode)rewritten!);
                }
            }

            return rewritten;
        }

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var rewritten = token;
            var isReplacedToken = _tokenSet.Remove(token);

            if (isReplacedToken)
            {
                // If token is in _tokenSet, then it contributed to the calculation of _spanSet.
                // We are currently processing that token, so it no longer needs to contribute
                // to _spanSet and affect determination of inward visitation. This is done before
                // calling ShouldVisit to avoid walking into the token if there aren't any remaining
                // spans inside it representing items to replace.
                CalculateVisitationCriteria();
            }

            if (_shouldVisitTrivia && this.ShouldVisit(token.FullSpan))
            {
                rewritten = base.VisitToken(token);
            }

            if (isReplacedToken && _computeReplacementToken != null)
            {
                rewritten = _computeReplacementToken(token, rewritten);
            }

            return rewritten;
        }

        public override SyntaxTrivia VisitListElement(SyntaxTrivia trivia)
        {
            var rewritten = trivia;
            var isReplacedTrivia = _triviaSet.Remove(trivia);

            if (isReplacedTrivia)
            {
                // If trivia is in _triviaSet, then it contributed to the calculation of _spanSet.
                // We are currently processing that trivia, so it no longer needs to contribute
                // to _spanSet and affect determination of inward visitation.
                CalculateVisitationCriteria();
            }

            // No structured trivia in this syntax, so we don't recurse into trivia structure.

            if (isReplacedTrivia && _computeReplacementTrivia != null)
            {
                rewritten = _computeReplacementTrivia(trivia, rewritten);
            }

            return rewritten;
        }
    }

    internal static AkburaSyntax ReplaceNodeInList(AkburaSyntax root, AkburaSyntax originalNode, IEnumerable<AkburaSyntax> newNodes)
    {
        return new NodeListEditor(originalNode, newNodes, ListEditKind.Replace).Visit(root)!;
    }

    internal static AkburaSyntax InsertNodeInList(AkburaSyntax root, AkburaSyntax nodeInList, IEnumerable<AkburaSyntax> nodesToInsert, bool insertBefore)
    {
        return new NodeListEditor(nodeInList, nodesToInsert, insertBefore ? ListEditKind.InsertBefore : ListEditKind.InsertAfter)
            .Visit(root)!;
    }

    public static AkburaSyntax ReplaceTokenInList(AkburaSyntax root, SyntaxToken tokenInList, IEnumerable<SyntaxToken> newTokens)
    {
        return new TokenListEditor(tokenInList, newTokens, ListEditKind.Replace).Visit(root)!;
    }

    public static AkburaSyntax InsertTokenInList(AkburaSyntax root, SyntaxToken tokenInList, IEnumerable<SyntaxToken> newTokens, bool insertBefore)
    {
        return new TokenListEditor(tokenInList, newTokens, insertBefore ? ListEditKind.InsertBefore : ListEditKind.InsertAfter)
            .Visit(root)!;
    }

    public static AkburaSyntax ReplaceTriviaInList(AkburaSyntax root, SyntaxTrivia triviaInList, IEnumerable<SyntaxTrivia> newTrivia)
    {
        return new TriviaListEditor(triviaInList, newTrivia, ListEditKind.Replace).Visit(root)!;
    }

    public static AkburaSyntax InsertTriviaInList(AkburaSyntax root, SyntaxTrivia triviaInList, IEnumerable<SyntaxTrivia> newTrivia, bool insertBefore)
    {
        return new TriviaListEditor(triviaInList, newTrivia, insertBefore ? ListEditKind.InsertBefore : ListEditKind.InsertAfter)
            .Visit(root)!;
    }

    public static SyntaxToken ReplaceTriviaInList(SyntaxToken root, SyntaxTrivia triviaInList, IEnumerable<SyntaxTrivia> newTrivia)
    {
        return new TriviaListEditor(triviaInList, newTrivia, ListEditKind.Replace).VisitToken(root);
    }

    public static SyntaxToken InsertTriviaInList(SyntaxToken root, SyntaxTrivia triviaInList, IEnumerable<SyntaxTrivia> newTrivia, bool insertBefore)
    {
        return new TriviaListEditor(triviaInList, newTrivia, insertBefore ? ListEditKind.InsertBefore : ListEditKind.InsertAfter)
            .VisitToken(root);
    }

    private enum ListEditKind
    {
        InsertBefore,
        InsertAfter,
        Replace
    }

    private static InvalidOperationException GetItemNotListElementException()
    {
        return new InvalidOperationException("Item is not a list element.");
    }

    private static InvalidOperationException GetTokenNotListElementException()
    {
        return new InvalidOperationException("Token is not a list element.");
    }

    private abstract class BaseListEditor : SyntaxRewriter
    {
        private readonly TextSpan _elementSpan;
        private readonly bool _visitTrivia;

        protected readonly ListEditKind editKind;

        public BaseListEditor(
            TextSpan elementSpan,
            ListEditKind editKind,
            bool visitTrivia)
        {
            _elementSpan = elementSpan;
            this.editKind = editKind;
            _visitTrivia = visitTrivia;
        }

        private bool ShouldVisit(TextSpan span)
        {
            if (span.IntersectsWith(_elementSpan))
            {
                // node's full span intersects with at least one node to be replaced
                // so we need to visit node's children to find it.
                return true;
            }

            return false;
        }

        [return: NotNullIfNotNull(nameof(node))]
        public override AkburaSyntax? Visit(AkburaSyntax? node)
        {
            AkburaSyntax? rewritten = node;

            if (node != null)
            {
                if (this.ShouldVisit(node.FullSpan))
                {
                    rewritten = base.Visit(node);
                }
            }

            return rewritten;
        }

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var rewritten = token;

            if (_visitTrivia && this.ShouldVisit(token.FullSpan))
            {
                rewritten = base.VisitToken(token);
            }

            return rewritten;
        }

        public override SyntaxTrivia VisitListElement(SyntaxTrivia trivia)
        {
            // No structured trivia support in this syntax, so just return the trivia.
            return trivia;
        }
    }

    private class NodeListEditor : BaseListEditor
    {
        private readonly AkburaSyntax _originalNode;
        private readonly IEnumerable<AkburaSyntax> _newNodes;

        public NodeListEditor(
            AkburaSyntax originalNode,
            IEnumerable<AkburaSyntax> replacementNodes,
            ListEditKind editKind)
            : base(originalNode.Span, editKind, visitTrivia: false)
        {
            _originalNode = originalNode;
            _newNodes = replacementNodes;
        }

        [return: NotNullIfNotNull(nameof(node))]
        public override AkburaSyntax? Visit(AkburaSyntax? node)
        {
            if (node == _originalNode)
            {
                throw GetItemNotListElementException();
            }

            return base.Visit(node);
        }

        public override SeparatedSyntaxList<TNode> VisitList<TNode>(SeparatedSyntaxList<TNode> list)
        {
            if (_originalNode is TNode original)
            {
                var index = list.IndexOf(original);
                if (index >= 0 && index < list.Count)
                {
                    switch (this.editKind)
                    {
                        case ListEditKind.Replace:
                            return list.ReplaceRange(original, _newNodes.Cast<TNode>());

                        case ListEditKind.InsertAfter:
                            return list.InsertRange(index + 1, _newNodes.Cast<TNode>());

                        case ListEditKind.InsertBefore:
                            return list.InsertRange(index, _newNodes.Cast<TNode>());
                    }
                }
            }

            return base.VisitList(list);
        }

        public override SyntaxList<TNode> VisitList<TNode>(SyntaxList<TNode> list)
        {
            if (_originalNode is TNode original)
            {
                var index = list.IndexOf(original);
                if (index >= 0 && index < list.Count)
                {
                    switch (this.editKind)
                    {
                        case ListEditKind.Replace:
                            return list.ReplaceRange(original, _newNodes.Cast<TNode>());

                        case ListEditKind.InsertAfter:
                            return list.InsertRange(index + 1, _newNodes.Cast<TNode>());

                        case ListEditKind.InsertBefore:
                            return list.InsertRange(index, _newNodes.Cast<TNode>());
                    }
                }
            }

            return base.VisitList(list);
        }
    }

    private class TokenListEditor : BaseListEditor
    {
        private readonly SyntaxToken _originalToken;
        private readonly IEnumerable<SyntaxToken> _newTokens;

        public TokenListEditor(
            SyntaxToken originalToken,
            IEnumerable<SyntaxToken> newTokens,
            ListEditKind editKind)
            : base(originalToken.Span, editKind, visitTrivia: false)
        {
            _originalToken = originalToken;
            _newTokens = newTokens;
        }

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            if (token == _originalToken)
            {
                throw GetTokenNotListElementException();
            }

            return base.VisitToken(token);
        }

        public override SyntaxTokenList VisitList(SyntaxTokenList list)
        {
            var index = list.IndexOf(_originalToken);
            if (index >= 0 && index < list.Count)
            {
                switch (this.editKind)
                {
                    case ListEditKind.Replace:
                        return list.ReplaceRange(_originalToken, _newTokens);

                    case ListEditKind.InsertAfter:
                        return list.InsertRange(index + 1, _newTokens);

                    case ListEditKind.InsertBefore:
                        return list.InsertRange(index, _newTokens);
                }
            }

            return base.VisitList(list);
        }
    }

    private class TriviaListEditor : BaseListEditor
    {
        private readonly SyntaxTrivia _originalTrivia;
        private readonly IEnumerable<SyntaxTrivia> _newTrivia;

        public TriviaListEditor(
            SyntaxTrivia originalTrivia,
            IEnumerable<SyntaxTrivia> newTrivia,
            ListEditKind editKind)
            : base(originalTrivia.Span, editKind, visitTrivia: true)
        {
            _originalTrivia = originalTrivia;
            _newTrivia = newTrivia;
        }

        public override SyntaxTriviaList VisitList(SyntaxTriviaList list)
        {
            var index = list.IndexOf(_originalTrivia);
            if (index >= 0 && index < list.Count)
            {
                switch (this.editKind)
                {
                    case ListEditKind.Replace:
                        return list.ReplaceRange(_originalTrivia, _newTrivia);

                    case ListEditKind.InsertAfter:
                        return list.InsertRange(index + 1, _newTrivia);

                    case ListEditKind.InsertBefore:
                        return list.InsertRange(index, _newTrivia);
                }
            }

            return base.VisitList(list);
        }
    }
}