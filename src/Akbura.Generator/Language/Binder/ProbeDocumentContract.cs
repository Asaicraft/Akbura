using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Akbura.Language.Binder;

/// <summary>
/// Exact structural correspondence for a deliberately narrow projection fast path.
/// Only same-width numeric literal Margin values may differ. Every scope and
/// declaration retains its exact text and source position. No markup node is retained.
/// </summary>
internal sealed class ProbeDocumentContract(ImmutableArray<string> segments, ImmutableArray<TextSpan> holes)
{
    private readonly ImmutableArray<string> _segments = segments;
    private readonly ImmutableArray<TextSpan> _holes = holes;

    internal static ProbeDocumentContract? Create(AkburaSyntaxTree tree, CancellationToken cancellationToken = default)
    {
        var root = tree.GetRootSyntax();
        if (tree.Kind != SyntaxTreeKind.Component || root.ContainsDiagnostics || root.ContainsSkippedText || root.ContainsAnnotations)
        {
            return null;
        }
        var ranges = ImmutableArray.CreateBuilder<TextSpan>();
        foreach (var node in root.DescendantNodesAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is MarkupContentSyntax && node is not (MarkupElementContentSyntax or MarkupTextLiteralSyntax or MarkupInlineExpressionSyntax or
                MarkupForeachStatementSyntax or MarkupIfStatementSyntax or MarkupCodeStatementSyntax or MarkupCodeIfStatementSyntax) ||
                node is AkTopLevelMemberSyntax && node is not (UsingDirectiveSyntax or NamespaceDeclarationSyntax or ParamDeclarationSyntax or
                    StateDeclarationSyntax or CommandDeclarationSyntax or InjectDeclarationSyntax or CSharpStatementSyntax or MarkupRootSyntax or InlineAkcssBlockSyntax))
            {
                return null;
            }
            if (node is MarkupPlainAttributeSyntax attribute && attribute.Name.ToString() == "Margin" &&
                attribute.Value is MarkupLiteralAttributeValueSyntax literal &&
                Regex.IsMatch(literal.ToString(), "^(?:\"[-+0-9., ]+\"|'[-+0-9., ]+')\\s*$", RegexOptions.CultureInvariant))
            {
                ranges.Add(literal.Span);
            }
        }
        var source = tree.Text.ToString();
        var pieces = ImmutableArray.CreateBuilder<string>(ranges.Count + 1);
        var position = 0;
        foreach (var range in ranges)
        {
            pieces.Add(source.Substring(position, range.Start - position));
            position = range.End;
        }
        pieces.Add(source.Substring(position));
        return new ProbeDocumentContract(pieces.ToImmutable(), ranges.ToImmutable());
    }

    internal bool Matches(ProbeDocumentContract other) => _segments.SequenceEqual(other._segments, StringComparer.Ordinal) &&
        _holes.Length == other._holes.Length && _holes.Zip(other._holes, static (left, right) => left.Length == right.Length).All(static equal => equal);

    internal bool TryGetRegionPosition(AkburaSyntax syntax, out int position)
    {
        position = syntax.Position;
        foreach (var hole in _holes)
        {
            if (hole.Contains(syntax.SpanStart))
            {
                return false;
            }
            if (hole.End <= syntax.Position)
            {
                position -= hole.Length;
            }
        }
        return true;
    }
}
