using Akbura.Language.BoundTree;
using Akbura.Language.Syntax;
using System;
using System.Diagnostics;

namespace Akbura.Language;

internal sealed class ExecutableMemberSemanticModel : BinderBackedMemberSemanticModel
{
    public ExecutableMemberSemanticModel(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSyntax scope,
        AkburaSyntax root)
        : base(semanticModel, scope, root)
    {
    }

    public IncrementalBinder CreateIncrementalBinder(Akbura.Language.Binder.Binder next)
    {
        return new IncrementalBinder(this, next);
    }

    internal sealed class IncrementalBinder : Akbura.Language.Binder.Binder
    {
        private readonly ExecutableMemberSemanticModel _memberModel;

        public IncrementalBinder(
            ExecutableMemberSemanticModel memberModel,
            Akbura.Language.Binder.Binder next)
            : base(
                next?.SemanticModel ?? throw new ArgumentNullException(nameof(next)),
                next,
                next.Declaration,
                memberModel.Root,
                next.Flags)
        {
            _memberModel = memberModel ?? throw new ArgumentNullException(nameof(memberModel));
        }

        public override Akbura.Language.Binder.Binder? GetBinder(AkburaSyntax syntax)
        {
            var binder = NextRequired.GetBinder(syntax);
            if (binder == null)
            {
                return null;
            }

            Debug.Assert(binder is not IncrementalBinder);
            return new IncrementalBinder(_memberModel, binder);
        }

        public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
        {
            if (_memberModel.TryGetBoundNodeFromMap(syntax, out var cached))
            {
                return cached;
            }

            var boundNode = NextRequired.BindSemanticSyntax(syntax);
            _memberModel.AddBoundTreeToMap(boundNode);
            return boundNode;
        }

        public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
        {
            if (_memberModel.TryGetBoundNodeFromMap(syntax, out var cached))
            {
                return cached;
            }

            var boundNode = NextRequired.BindOperationSyntax(syntax);
            _memberModel.AddBoundTreeToMap(boundNode);
            return boundNode;
        }
    }
}
