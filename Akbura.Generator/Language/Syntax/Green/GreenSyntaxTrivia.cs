using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax.Green;
internal partial class GreenSyntaxTrivia : GreenNode
{
    public readonly string Text;

    public GreenSyntaxTrivia(
        SyntaxKind kind,
        string text,
        ImmutableArray<AkburaDiagnostic>? diagnostics = null,
        ImmutableArray<AkburaSyntaxAnnotation>? annotations = null)
        : base((ushort)kind, diagnostics, annotations)
    {
        Text = text;
        FullWidth = text.Length;
    }

    internal override bool IsTrivia => true;

    public static GreenSyntaxTrivia Create(SyntaxKind kind, string text)
    {
        return new GreenSyntaxTrivia(kind, text);
    }

    public override string ToFullString()
    {
        return Text;
    }

    public override string ToString()
    {
        return Text;
    }

    public override GreenNode? GetSlot(int index)
    {
        return ThrowHelper.ThrowUnreachableException<GreenNode>();
    }

    public override int Width
    {
        get
        {
            Debug.Assert(FullWidth == Text.Length);
            return FullWidth;
        }
    }

    public override int GetLeadingTriviaWidth() => 0;
    public override int GetTrailingTriviaWidth() => 0;

    public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
    {
        return new GreenSyntaxTrivia(Kind, Text, diagnostics, GetAnnotations());
    }

    public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
    {
        return new GreenSyntaxTrivia(Kind, Text, GetDiagnostics(), annotations);
    }

    protected override void WriteTriviaTo(TextWriter writer)
    {
        writer.Write(Text);
    }

    public static implicit operator SyntaxTrivia(GreenSyntaxTrivia trivia)
    {
        return new SyntaxTrivia(token: default, trivia, position: 0, index: 0);
    }

    public override bool IsEquivalentTo(GreenNode? other)
    {
        if (!base.IsEquivalentTo(other)) return false;
        if (Text != ((GreenSyntaxTrivia)other).Text) return false;
        return true;
    }

    public override AkburaSyntax CreateRed(AkburaSyntax? parent, int position)
    {
        return ThrowHelper.ThrowUnreachableException<AkburaSyntax>();
    }
}