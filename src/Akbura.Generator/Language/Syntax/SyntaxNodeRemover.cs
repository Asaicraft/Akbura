using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax;
internal static class SyntaxNodeRemover
{
    internal static AkburaSyntax? RemoveNodes(
    AkburaSyntax root,
    IEnumerable<AkburaSyntax> nodes,
    SyntaxRemoveOptions options)
    {
        if (nodes is null)
        {
            return root;
        }

        var nodeArray = nodes as AkburaSyntax[] ?? nodes.ToArray();

        if (nodeArray.Length == 0)
        {
            return root;
        }

        var remover = new SyntaxRemover(nodeArray, options);
        var result = remover.Visit(root);
        var residualTrivia = remover.ResidualTrivia;

        // the result of the SyntaxRemover will be null when the root node is removed.
        if (result != null && residualTrivia.Count > 0)
        {
            // Assume AkburaSyntax has WithTrailingTrivia like Roslyn's SyntaxNode.
            result = result.WithTrailingTrivia(
                result.GetTrailingTrivia().Concat(residualTrivia));
        }

        return result;
    }


    private sealed class SyntaxRemover : SyntaxRewriter
    {
        private readonly HashSet<AkburaSyntax> _nodesToRemove;
        private readonly SyntaxRemoveOptions _options;
        private readonly TextSpan _searchSpan;
        private readonly SyntaxTriviaListBuilder _residualTrivia;

        public SyntaxRemover(
            AkburaSyntax[] nodesToRemove,
            SyntaxRemoveOptions options)
        {
            _nodesToRemove = new HashSet<AkburaSyntax>(nodesToRemove);
            _options = options;
            _searchSpan = ComputeTotalSpan(nodesToRemove);
            _residualTrivia = SyntaxTriviaListBuilder.Create();
        }

        private static TextSpan ComputeTotalSpan(AkburaSyntax[] nodes)
        {
            var span0 = nodes[0].FullSpan;
            var start = span0.Start;
            var end = span0.End;

            for (var i = 1; i < nodes.Length; i++)
            {
                var span = nodes[i].FullSpan;
                start = Math.Min(start, span.Start);
                end = Math.Max(end, span.End);
            }

            return new TextSpan(start, end - start);
        }

        internal SyntaxTriviaList ResidualTrivia
        {
            get
            {
                if (_residualTrivia != null)
                {
                    return _residualTrivia.ToList();
                }
                else
                {
                    return default;
                }
            }
        }

        private void AddResidualTrivia(SyntaxTriviaList trivia, bool requiresNewLine = false)
        {
            if (requiresNewLine)
            {
                // Try to preserve an end-of-line trivia if one exists,
                // otherwise add a default CRLF.
                var eol = GetEndOfLine(trivia) ?? SyntaxFactory.CarriageReturnLineFeed;
                AddEndOfLine(eol);
            }

            _residualTrivia.Add(trivia);
        }

        private void AddEndOfLine(SyntaxTrivia? eolTrivia)
        {
            if (!eolTrivia.HasValue)
            {
                return;
            }

            if (_residualTrivia.Count == 0 || !IsEndOfLine(_residualTrivia[_residualTrivia.Count - 1]))
            {
                _residualTrivia.Add(eolTrivia.Value);
            }
        }

        /// <summary>
        /// Returns whether the specified <see cref="SyntaxTrivia"/> token is also the end of the line.  This will
        /// be true for <see cref="SyntaxKind.EndOfLineTrivia"/>
        /// </summary>
        private static bool IsEndOfLine(SyntaxTrivia trivia)
        {
            return trivia.Kind == SyntaxKind.EndOfLineTrivia;
        }

        /// <summary>
        /// Returns the first end of line found in a <see cref="SyntaxTriviaList"/>.
        /// </summary>
        private static SyntaxTrivia? GetEndOfLine(SyntaxTriviaList list)
        {
            foreach (var trivia in list)
            {
                if (trivia.Kind == SyntaxKind.EndOfLineTrivia)
                {
                    return trivia;
                }
            }

            return null;
        }

        private bool IsForRemoval(AkburaSyntax node)
        {
            return _nodesToRemove.Contains(node);
        }

        private bool ShouldVisit(AkburaSyntax node)
        {
            // visit nodes that intersect the total span of nodes we are removing
            // or if we still have residual trivia to attach somewhere
            return node.FullSpan.IntersectsWith(_searchSpan) || _residualTrivia.Count > 0;
        }

        [return: NotNullIfNotNull(nameof(node))]
        public override AkburaSyntax? Visit(AkburaSyntax? node)
        {
            AkburaSyntax? result = node;

            if (node != null)
            {
                if (IsForRemoval(node))
                {
                    AddTrivia(node);
                    result = null;
                }
                else if (ShouldVisit(node))
                {
                    result = base.Visit(node);
                }
            }

            return result;
        }

        public override SyntaxToken VisitToken(SyntaxToken token)
        {
            var result = token;

            // No structured trivia in AkburaSyntax, so we don't need to recurse into token trivia here.

            // the next token gets the accrued trivia.
            if (result.Kind != SyntaxKind.None && _residualTrivia.Count > 0)
            {
                _residualTrivia.Add(result.LeadingTrivia);
                result = result.WithLeadingTrivia(_residualTrivia.ToList());
                _residualTrivia.Clear();
            }

            return result;
        }

        // deal with separated lists and removal of associated separators
        public override SeparatedSyntaxList<TNode> VisitList<TNode>(SeparatedSyntaxList<TNode> list)
        {
            var withSeps = list.GetWithSeparators();
            var removeNextSeparator = false;

            SyntaxNodeOrTokenListBuilder? alternate = null;
            var n = withSeps.Count;

            for (var i = 0; i < n; i++)
            {
                var item = withSeps[i];
                SyntaxNodeOrToken visited;

                if (item.IsToken)
                {
                    // separator
                    if (removeNextSeparator)
                    {
                        removeNextSeparator = false;
                        visited = default;
                    }
                    else
                    {
                        visited = VisitListSeparator(item.AsToken());
                    }
                }
                else
                {
                    var node = (TNode)item.AsNode()!;

                    if (IsForRemoval(node))
                    {
                        if (alternate == null)
                        {
                            alternate = new SyntaxNodeOrTokenListBuilder(n);
                            alternate.Add(withSeps, 0, i);
                        }

                        // Simple heuristic without directives: prefer to remove the separator after the node if present,
                        // otherwise remove the separator before the node if present.
                        var hasPrevSeparator = i - 1 >= 0 && withSeps[i - 1].IsToken;
                        var hasNextSeparator = i + 1 < n && withSeps[i + 1].IsToken;

                        if (hasPrevSeparator && alternate.Count > 0 && alternate[alternate.Count - 1].IsToken && !hasNextSeparator)
                        {
                            // use previous separator (already added to alternate)
                            var separator = alternate[alternate.Count - 1].AsToken();
                            AddTrivia(separator, node);
                            alternate.RemoveLast();
                        }
                        else if (hasNextSeparator)
                        {
                            // use next separator
                            var separator = withSeps[i + 1].AsToken();
                            AddTrivia(node, separator);
                            removeNextSeparator = true;
                        }
                        else
                        {
                            // no separators, just remove node but keep trivia if requested
                            AddTrivia(node);
                        }

                        visited = default;
                    }
                    else
                    {
                        visited = VisitListElement(node);
                    }
                }

                if (!item.Equals(visited) && alternate == null)
                {
                    alternate = new SyntaxNodeOrTokenListBuilder(n);
                    alternate.Add(withSeps, 0, i);
                }

                if (alternate != null && visited.Kind != SyntaxKind.None)
                {
                    alternate.Add(visited);
                }
            }

            if (alternate != null)
            {
                return alternate.ToList().AsSeparatedList<TNode>();
            }

            return list;
        }

        private void AddTrivia(AkburaSyntax node)
        {
            if ((_options & SyntaxRemoveOptions.KeepLeadingTrivia) != 0)
            {
                AddResidualTrivia(node.GetLeadingTrivia());
            }
            else if ((_options & SyntaxRemoveOptions.KeepEndOfLine) != 0)
            {
                AddEndOfLine(GetEndOfLine(node.GetLeadingTrivia()));
            }

            // No directives in AkburaSyntax – nothing to keep here.

            if ((_options & SyntaxRemoveOptions.KeepTrailingTrivia) != 0)
            {
                AddResidualTrivia(node.GetTrailingTrivia());
            }
            else if ((_options & SyntaxRemoveOptions.KeepEndOfLine) != 0)
            {
                AddEndOfLine(GetEndOfLine(node.GetTrailingTrivia()));
            }

            if ((_options & SyntaxRemoveOptions.AddElasticMarker) != 0)
            {
                AddResidualTrivia(SyntaxFactory.TriviaList(SyntaxFactory.ElasticMarker));
            }
        }

        private void AddTrivia(SyntaxToken token, AkburaSyntax node)
        {
            Debug.Assert(node.Parent is not null);

            if ((_options & SyntaxRemoveOptions.KeepLeadingTrivia) != 0)
            {
                AddResidualTrivia(token.LeadingTrivia);
                AddResidualTrivia(token.TrailingTrivia);
                AddResidualTrivia(node.GetLeadingTrivia());
            }
            else if ((_options & SyntaxRemoveOptions.KeepEndOfLine) != 0)
            {
                // For retrieving an EOL we don't need to check the node leading trivia as
                // it can be always retrieved from the token trailing trivia, if one exists.
                var eol = GetEndOfLine(token.LeadingTrivia) ??
                          GetEndOfLine(token.TrailingTrivia);
                AddEndOfLine(eol);
            }

            // No directives in AkburaSyntax – skip directive handling.

            if ((_options & SyntaxRemoveOptions.KeepTrailingTrivia) != 0)
            {
                AddResidualTrivia(node.GetTrailingTrivia());
            }
            else if ((_options & SyntaxRemoveOptions.KeepEndOfLine) != 0)
            {
                AddEndOfLine(GetEndOfLine(node.GetTrailingTrivia()));
            }

            if ((_options & SyntaxRemoveOptions.AddElasticMarker) != 0)
            {
                AddResidualTrivia(SyntaxFactory.TriviaList(SyntaxFactory.ElasticMarker));
            }
        }

        private void AddTrivia(AkburaSyntax node, SyntaxToken token)
        {
            Debug.Assert(node.Parent is not null);

            if ((_options & SyntaxRemoveOptions.KeepLeadingTrivia) != 0)
            {
                AddResidualTrivia(node.GetLeadingTrivia());
            }
            else if ((_options & SyntaxRemoveOptions.KeepEndOfLine) != 0)
            {
                AddEndOfLine(GetEndOfLine(node.GetLeadingTrivia()));
            }

            // No directives in AkburaSyntax – skip directive handling.

            if ((_options & SyntaxRemoveOptions.KeepTrailingTrivia) != 0)
            {
                AddResidualTrivia(node.GetTrailingTrivia());
                AddResidualTrivia(token.LeadingTrivia);
                AddResidualTrivia(token.TrailingTrivia);
            }
            else if ((_options & SyntaxRemoveOptions.KeepEndOfLine) != 0)
            {
                // For retrieving an EOL we don't need to check the token leading trivia as
                // it can be always retrieved from the node trailing trivia, if one exists.
                var eol = GetEndOfLine(node.GetTrailingTrivia()) ??
                          GetEndOfLine(token.TrailingTrivia);
                AddEndOfLine(eol);
            }

            if ((_options & SyntaxRemoveOptions.AddElasticMarker) != 0)
            {
                AddResidualTrivia(SyntaxFactory.TriviaList(SyntaxFactory.ElasticMarker));
            }
        }
    }

}
