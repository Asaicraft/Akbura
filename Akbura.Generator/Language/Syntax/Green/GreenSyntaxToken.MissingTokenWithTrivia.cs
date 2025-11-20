using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Language.Syntax.Green;

partial class GreenSyntaxToken
{
    public sealed class MissingTokenWithTrivia : SyntaxTokenWithTrivia
    {
        public MissingTokenWithTrivia(SyntaxKind kind, GreenNode? leading, GreenNode? trailing)
            : base(kind, leading, trailing)
        {
            IsMissing = true;
        }

        public MissingTokenWithTrivia(SyntaxKind kind, GreenNode? leading, GreenNode? trailing, ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(kind, leading, trailing, diagnostics, annotations)
        {
            IsMissing = true;
        }

        public override string Text => string.Empty;

        public override object? Value => Kind switch
        {
            SyntaxKind.IdentifierToken => string.Empty,
            _ => null,
        };

        public override GreenSyntaxToken TokenWithLeadingTrivia(GreenNode? trivia)
        {
            return new MissingTokenWithTrivia(Kind, trivia, TrailingField, GetDiagnostics(), GetAnnotations());
        }

        public override GreenSyntaxToken TokenWithTrailingTrivia(GreenNode? trivia)
        {
            return new MissingTokenWithTrivia(Kind, LeadingField, trivia, GetDiagnostics(), GetAnnotations());
        }

        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
        {
            return new MissingTokenWithTrivia(Kind, LeadingField, TrailingField, diagnostics, GetAnnotations());
        }

        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new MissingTokenWithTrivia(Kind, LeadingField, TrailingField, GetDiagnostics(), annotations);
        }
    }
}
