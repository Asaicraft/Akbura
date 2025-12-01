using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CsharpRawNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace Akbura.Language.Syntax.Green;
partial class GreenSyntaxToken
{
    public sealed class CSharpRawToken : GreenSyntaxToken
    {
        private readonly GreenNode? _leading;
        private readonly GreenNode? _trailing;
        private readonly string _rawText;
        private readonly CsharpRawNode? _rawNode;

        public CSharpRawToken(string rawText, GreenNode? leading, GreenNode? trailing, ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(SyntaxKind.CSharpRawToken, diagnostics, annotations)
        {
            _rawText = rawText;
            FullWidth = rawText.Length;

            _leading = leading;
            _trailing = trailing;
        }

        public CSharpRawToken(CsharpRawNode rawText, GreenNode? leading, GreenNode? trailing, ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(SyntaxKind.CSharpRawToken, diagnostics, annotations)
        {
            _rawNode = rawText;
            _rawText = _rawNode.ToFullString();

            _leading = leading;
            _trailing = trailing;
        }

        public override string Text => _rawText;

        public CsharpRawNode? RawNode => _rawNode;

        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new CSharpRawToken(_rawText, _leading, _trailing, GetDiagnostics(), annotations);
        }

        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
        {
            return new CSharpRawToken(_rawText, _leading, _trailing, diagnostics, GetAnnotations());
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
            if(_rawNode != null)
            {
                return new CSharpRawToken(_rawNode, trivia, _trailing, GetDiagnostics(), GetAnnotations());
            }

            return new CSharpRawToken(_rawText, trivia, _trailing, GetDiagnostics(), GetAnnotations());
        }

        public override GreenSyntaxToken TokenWithTrailingTrivia(GreenNode? trivia)
        {
            if (_rawNode != null)
            {
                return new CSharpRawToken(_rawNode, _leading, trivia, GetDiagnostics(), GetAnnotations());
            }

            return new CSharpRawToken(_rawText, _leading, trivia, GetDiagnostics(), GetAnnotations());
        }
    }
}
