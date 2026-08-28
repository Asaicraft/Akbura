using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;

namespace Akbura.Language;

internal abstract class BinderBackedMemberSemanticModel : MemberSemanticModel
{
    protected BinderBackedMemberSemanticModel(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSyntax scope,
        AkburaSyntax root)
        : base(semanticModel, scope, root)
    {
    }

    public override AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax)
    {
        return GetSyntaxTreeSymbolInfo(syntax);
    }

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        if (TryGetBoundNodeFromMap(syntax, out var cached))
        {
            return cached;
        }

        var boundNode = BindingSession.GetSemanticBinder(syntax)
            .BindSemanticSyntax(syntax);
        AddBoundTreeToMap(boundNode);
        return boundNode;
    }

    public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        if (TryGetBoundNodeFromMap(syntax, out var cached))
        {
            return cached;
        }

        var boundNode = BindingSession.GetOperationBinder(syntax)
            .BindOperationSyntax(syntax);
        AddBoundTreeToMap(boundNode);
        return boundNode;
    }
}
