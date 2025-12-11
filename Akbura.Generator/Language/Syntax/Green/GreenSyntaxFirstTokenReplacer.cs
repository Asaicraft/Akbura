using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Akbura.Language.Syntax.Green;

internal sealed class GreenSyntaxFirstTokenReplacer: GreenSyntaxRewriter
{
    private readonly GreenSyntaxToken _oldToken;
    private readonly GreenSyntaxToken _newToken;
    private readonly int _diagnosticOffsetDelta;
    private bool _foundOldToken;

    public GreenSyntaxFirstTokenReplacer(GreenSyntaxToken oldToken, GreenSyntaxToken newToken, int diagnosticOffsetDelta)
    {
        _oldToken = oldToken;
        _newToken = newToken;
        _diagnosticOffsetDelta = diagnosticOffsetDelta;
    }

    internal static TRoot Replace<TRoot>(TRoot root, GreenSyntaxToken oldToken, GreenSyntaxToken newToken, int diagnosticOffsetDelta)
            where TRoot : GreenNode
    {
        var replacer = new GreenSyntaxFirstTokenReplacer(oldToken, newToken, diagnosticOffsetDelta);
        var newRoot = (TRoot)replacer.Visit(root)!;
        Debug.Assert(replacer._foundOldToken);
        return newRoot;
    }

    public override GreenNode? Visit(GreenNode? node)
    {
        if (node != null)
        {
            if (!_foundOldToken)
            {
                if (node is GreenSyntaxToken token)
                {
                    Debug.Assert(token == _oldToken);
                    _foundOldToken = true;
                    return _newToken; // NB: diagnostic offsets have already been updated (by SyntaxParser.AddSkippedSyntax)
                }

                return UpdateDiagnosticOffset(base.Visit(node)!, _diagnosticOffsetDelta);
            }
        }

        return node;
    }

    private static TSyntax UpdateDiagnosticOffset<TSyntax>(TSyntax node, int diagnosticOffsetDelta) where TSyntax : GreenNode
    {
        var oldDiagnostics = node.GetDiagnostics();
        if (oldDiagnostics.IsDefaultOrEmpty)
        {
            return node;
        }

        var numDiagnostics = oldDiagnostics.Length;
        var newDiagnostics = new AkburaDiagnostic[numDiagnostics];
        for (var i = 0; i < numDiagnostics; i++)
        {
            var oldDiagnostic = oldDiagnostics[i];
            newDiagnostics[i] = oldDiagnostic is not SyntaxDiagnosticInfo oldSyntaxDiagnostic ?
                oldDiagnostic :
                new SyntaxDiagnosticInfo(
                    oldSyntaxDiagnostic.Position + diagnosticOffsetDelta,
                    oldSyntaxDiagnostic.Width,
                    oldSyntaxDiagnostic.Code,
                    oldSyntaxDiagnostic.Parameters);
        }
        return (TSyntax)node.WithDiagnostics(newDiagnostics.ToImmutableArrayUnsafe());
    }
}
