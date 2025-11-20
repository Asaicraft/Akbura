using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Language.Syntax.Green;
partial class GreenSyntaxToken
{
    public class SyntaxIdentifierExtended : SyntaxIdentifier
    {
        protected readonly SyntaxKind _contextualKind;
        protected readonly string _valueText;

        public SyntaxIdentifierExtended(
            SyntaxKind contextualKind,
            string text,
            string valueText)
            : base(text)
        {
            _contextualKind = contextualKind;
            _valueText = valueText;
        }

        public SyntaxIdentifierExtended(
            SyntaxKind contextualKind,
            string text,
            string valueText,
            ImmutableArray<AkburaDiagnostic>? diagnostics,
            ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(text, diagnostics, annotations)
        {
            _contextualKind = contextualKind;
            _valueText = valueText;
        }

        public override SyntaxKind ContextualKind => _contextualKind;

        public override string ValueText => _valueText;

        public override object Value => _valueText;

        public override GreenSyntaxToken TokenWithLeadingTrivia(GreenNode? trivia)
        {
            return new SyntaxIdentifierWithTrivia(
                _contextualKind,
                TextField,
                _valueText,
                trivia,
                null,
                GetDiagnostics(),
                GetAnnotations());
        }

        public override GreenSyntaxToken TokenWithTrailingTrivia(GreenNode? trivia)
        {
            return new SyntaxIdentifierWithTrivia(
                _contextualKind,
                TextField,
                _valueText,
                null,
                trivia,
                GetDiagnostics(),
                GetAnnotations());
        }

        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
        {
            return new SyntaxIdentifierExtended(
                _contextualKind,
                TextField,
                _valueText,
                diagnostics,
                GetAnnotations());
        }

        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new SyntaxIdentifierExtended(
                _contextualKind,
                TextField,
                _valueText,
                GetDiagnostics(),
                annotations);
        }
    }
}
