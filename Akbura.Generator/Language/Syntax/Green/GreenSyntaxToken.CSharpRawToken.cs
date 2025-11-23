using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using CsharpRawNode = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxNode;

namespace Akbura.Language.Syntax.Green;
partial class GreenSyntaxToken
{
    public sealed class CSharpRawToken : GreenSyntaxToken
    {
        private readonly string _rawText;
        private CsharpRawNode? _rawNode;

        public CSharpRawToken(string rawText, ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(SyntaxKind.CSharpRawToken, diagnostics, annotations)
        {
            _rawText = rawText;
            FullWidth = rawText.Length;
        }

        public CSharpRawToken(CsharpRawNode rawText, ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations)
            : base(SyntaxKind.CSharpRawToken, diagnostics, annotations)
        {
            _rawNode = rawText;
            _rawText = _rawNode.ToFullString();
        }

        public override string Text => _rawText;

        public CsharpRawNode? RawNode => _rawNode;

        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new CSharpRawToken(_rawText, GetDiagnostics(), annotations);
        }

        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
        {
            return new CSharpRawToken(_rawText, diagnostics, GetAnnotations());
        }
    }
}
