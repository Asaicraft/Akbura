using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Akbura.Language.Syntax.Green;
partial class GreenSyntaxToken
{
    /// <summary>Represents a token with an associated value.</summary>
    public class SyntaxTokenWithValue<T> : GreenSyntaxToken
    {
        protected readonly string TextField;
        protected readonly T ValueField;


        /// <summary>Initializes a new instance of the SyntaxTokenWithValue class.</summary>
        public SyntaxTokenWithValue(SyntaxKind kind, string text, T value)
            : base(kind)
        {
            TextField = text;
            ValueField = value;
            FullWidth = text.Length;
        }

        /// <summary>Initializes a new instance with diagnostics and annotations.</summary>
        public SyntaxTokenWithValue(SyntaxKind kind, string text, T value, ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(kind, diagnostics, annotations)
        {
            TextField = text;
            ValueField = value;
            FullWidth = text.Length;
        }

        /// <summary>Gets the text representation of the token.</summary>
        public override string Text => TextField;

        /// <summary>Gets the associated value of the token.</summary>
        public override object? Value => ValueField;

        /// <summary>Gets the string representation of the value.</summary>
        public override string? ValueText => Convert.ToString(ValueField, CultureInfo.InvariantCulture);

        /// <summary>Creates a new token with leading trivia.</summary>
        public override GreenSyntaxToken TokenWithLeadingTrivia(GreenNode trivia)
        {
            return new SyntaxTokenWithValueAndTrivia<T>(
                Kind,
                TextField,
                ValueField,
                trivia,
                null,
                GetDiagnostics(),
                GetAnnotations());
        }

        /// <summary>Creates a new token with trailing trivia.</summary>
        public override GreenSyntaxToken TokenWithTrailingTrivia(GreenNode trivia)
        {
            return new SyntaxTokenWithValueAndTrivia<T>(
                Kind,
                TextField,
                ValueField,
                null,
                trivia,
                GetDiagnostics(),
                GetAnnotations());
        }

        /// <summary>Creates a new token with updated diagnostics.</summary>
        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
        {
            return new SyntaxTokenWithValue<T>(
                Kind,
                TextField,
                ValueField,
                diagnostics,
                GetAnnotations());
        }

        /// <summary>Creates a new token with updated annotations.</summary>
        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new SyntaxTokenWithValue<T>(
                Kind,
                TextField,
                ValueField,
                GetDiagnostics(),
                annotations);
        }
    }
}
