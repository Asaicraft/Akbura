using Akbura.Language.BoundTree;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

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

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        if (syntax.Kind != AkburaSyntaxKind.CSharpStatementSyntax)
        {
            return base.BindSemanticSyntax(syntax);
        }

        if (TryGetBoundNodeFromMap(syntax, out var cached))
        {
            return cached;
        }

        var statement = Unsafe.As<CSharpStatementSyntax>(syntax);
        var csharpStatement = statement.GetRawCSharpStatement();
        if (csharpStatement == null)
        {
            var badStatement = new BoundBadStatement(
                statement,
                GetBinder(statement, BinderUsage.Expression),
                System.Collections.Immutable.ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            AddBoundTreeToMap(badStatement);
            return badStatement;
        }

        var boundNode = BindingSession
            .GetUseHookBinder(statement, BinderUsage.Expression)
            .BindStatement(statement, csharpStatement);
        if (statement.Body != null && boundNode.Kind == BoundKind.CSharpStatement)
        {
            var boundStatement = Unsafe.As<BoundCSharpStatement>(boundNode);
            boundNode = boundStatement.Update(
                boundStatement.BindingResult,
                System.Collections.Immutable.ImmutableArray.Create(
                    BindingSession.BindSemanticSyntax(statement.Body)));
        }

        AddBoundTreeToMap(boundNode);
        return boundNode;
    }

    public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind == AkburaSyntaxKind.CSharpStatementSyntax
            ? BindSemanticSyntax(syntax)
            : base.BindOperationSyntax(syntax);
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
