using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Language.Syntax.Green;
partial class GreenSyntaxToken
{
    public class SyntaxTokenWithTrivia : GreenSyntaxToken
    {
        protected readonly GreenNode? LeadingField;
        protected readonly GreenNode? TrailingField;


        /// <summary>Initializes a new instance of the SyntaxTokenWithTrivia class.</summary>
        public SyntaxTokenWithTrivia(SyntaxKind kind, GreenNode? leading, GreenNode? trailing)
            : base(kind)
        {
            var fullWidth = FullWidth;
            var flags = Flags;


            if (leading != null)
            {
                AdjustWidthAndFlags(leading, ref fullWidth, ref flags);
                LeadingField = leading;
            }
            if (trailing != null)
            {
                AdjustWidthAndFlags(trailing, ref fullWidth, ref flags);
                TrailingField = trailing;
            }

            FullWidth = fullWidth;
            Flags = flags;
        }

        /// <summary>Initializes a new instance with diagnostics and annotations.</summary>
        public SyntaxTokenWithTrivia(SyntaxKind kind, GreenNode? leading, GreenNode? trailing, ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(kind, diagnostics, annotations)
        {
            var fullWidth = FullWidth;
            var flags = Flags;

            if (leading is not null)
            {
                AdjustWidthAndFlags(leading, ref fullWidth, ref flags);
                LeadingField = leading;
            }
            if (trailing is not null)
            {
                AdjustWidthAndFlags(trailing, ref fullWidth, ref flags);
                TrailingField = trailing;
            }

            FullWidth = fullWidth;
            Flags = flags;
        }

        /// <summary>Gets the leading trivia.</summary>
        public override GreenNode? GetLeadingTrivia() => LeadingField;

        /// <summary>Gets the trailing trivia.</summary>
        public override GreenNode? GetTrailingTrivia() => TrailingField;

        /// <summary>Creates a new token with updated leading trivia.</summary>
        public override GreenSyntaxToken TokenWithLeadingTrivia(GreenNode? trivia)
        {
            return new SyntaxTokenWithTrivia(
                Kind,
                trivia,
                TrailingField,
                GetDiagnostics(),
                GetAnnotations());
        }

        /// <summary>Creates a new token with updated trailing trivia.</summary>
        public override GreenSyntaxToken TokenWithTrailingTrivia(GreenNode trivia)
        {
            return new SyntaxTokenWithTrivia(
                Kind,
                LeadingField,
                trivia,
                GetDiagnostics(),
                GetAnnotations());
        }

        /// <summary>Creates a new token with updated diagnostics.</summary>
        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
        {
            return new SyntaxTokenWithTrivia(
                Kind,
                LeadingField,
                TrailingField,
                diagnostics,
                GetAnnotations());
        }

        /// <summary>Creates a new token with updated annotations.</summary>
        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new SyntaxTokenWithTrivia(
                Kind,
                LeadingField,
                TrailingField,
                GetDiagnostics(),
                annotations);
        }
    }
}
