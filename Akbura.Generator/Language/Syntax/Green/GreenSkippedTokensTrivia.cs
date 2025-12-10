using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Language.Syntax.Green;

internal sealed class GreenSkippedTokensTrivia : GreenSyntaxTrivia
{
    public readonly GreenNode? Tokens;

    public GreenSkippedTokensTrivia(GreenNode? tokens, ImmutableArray<AkburaDiagnostic>? diagnostics = null, ImmutableArray<AkburaSyntaxAnnotation>? annotations = null) : base(SyntaxKind.SkippedTokensTrivia, null!, diagnostics, annotations)
    {
        Tokens = tokens;
        FullWidth = tokens?.FullWidth ?? 0;
        ContainsSkippedText = true;
    }

    public override string ToFullString()
    {
        return Tokens?.ToFullString() ?? string.Empty;
    }

    public override string ToString()
    {
        return Tokens?.ToString() ?? string.Empty;
    }

    public override int Width => Tokens?.FullWidth ?? 0;

    public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
    {
        return new GreenSkippedTokensTrivia(Tokens, GetDiagnostics(), annotations);
    }

    public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
    {
        return new GreenSkippedTokensTrivia(Tokens, diagnostics, GetAnnotations());
    }

    protected override void WriteTriviaTo(TextWriter writer)
    {
        Tokens?.WriteTo(writer);
    }

    public static GreenSkippedTokensTrivia Create(GreenSyntaxList<GreenNode> tokens)
    {
        return new GreenSkippedTokensTrivia(tokens.Node);
    }
}
