using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Language.Syntax.Green;
partial class GreenSyntaxToken
{
    public class SyntaxIdentifierWithTrivia : SyntaxIdentifierExtended
    {
        private readonly GreenNode? _leading;
        private readonly GreenNode? _trailing;

        public SyntaxIdentifierWithTrivia(
            SyntaxKind contextualKind,
            string text,
            string valueText,
            GreenNode? leading,
            GreenNode? trailing)
            : base(contextualKind, text, valueText)
        {
            var fullWidth = FullWidth;
            var flags = Flags;

            if (leading != null)
            {
                AdjustWidthAndFlags(leading, ref fullWidth, ref flags);
                _leading = leading;
            }

            if (trailing != null)
            {
                AdjustWidthAndFlags(trailing, ref fullWidth, ref flags);
                _trailing = trailing;
            }

            FullWidth = fullWidth;
            Flags = flags;
        }

        public SyntaxIdentifierWithTrivia(
            SyntaxKind contextualKind,
            string text,
            string valueText,
            GreenNode? leading,
            GreenNode? trailing,
            ImmutableArray<AkburaDiagnostic>? diagnostics,
            ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(contextualKind, text, valueText, diagnostics, annotations)
        {
            var fullWidth = FullWidth;
            var flags = Flags;

            if (leading != null)
            {
                AdjustWidthAndFlags(leading, ref fullWidth, ref flags);
                _leading = leading;
            }

            if (trailing != null)
            {
                AdjustWidthAndFlags(trailing, ref fullWidth, ref flags);
                _trailing = trailing;
            }

            FullWidth = fullWidth;
            Flags = flags;
        }

        public override GreenNode? GetLeadingTrivia()
        {
            return _leading;
        }

        public override GreenNode? GetTrailingTrivia()
        {
            return _trailing;
        }

        public override GreenSyntaxToken TokenWithLeadingTrivia(GreenNode? trivia)
        {
            return new SyntaxIdentifierWithTrivia(
                _contextualKind,
                TextField,
                _valueText,
                trivia,
                _trailing,
                GetDiagnostics(),
                GetAnnotations());
        }

        public override GreenSyntaxToken TokenWithTrailingTrivia(GreenNode? trivia)
        {
            return new SyntaxIdentifierWithTrivia(
                _contextualKind,
                TextField,
                _valueText,
                _leading,
                trivia,
                GetDiagnostics(),
                GetAnnotations());
        }

        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
        {
            return new SyntaxIdentifierWithTrivia(
                _contextualKind,
                TextField,
                _valueText,
                _leading,
                _trailing,
                diagnostics,
                GetAnnotations());
        }

        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new SyntaxIdentifierWithTrivia(
                _contextualKind,
                TextField,
                _valueText,
                _leading,
                _trailing,
                GetDiagnostics(),
                annotations);
        }
    }
}
