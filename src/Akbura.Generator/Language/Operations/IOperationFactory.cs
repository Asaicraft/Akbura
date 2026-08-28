using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.Operations;

internal interface IOperationFactory
{
    IOperation? CreateOperation(BoundNode boundNode);

    ImmutableArray<IAkcssOperation> CreateAkcssOperations(
        SyntaxList<AkcssBodyMemberSyntax> members,
        IAkcssSymbol containingSymbol,
        IOperationFactoryContext context);
}

internal interface IOperationFactoryContext
{
    bool TryGetCachedOperation(AkburaSyntax syntax, out IOperation? operation);

    void SetCachedBoundNode(AkburaSyntax syntax, BoundNode boundNode);

    void SetCachedOperation(AkburaSyntax syntax, IOperation? operation);

    BinderType GetBinder(AkburaSyntax syntax, BinderUsage usage);

    void SetAkcssInterceptIgnoredDiagnostics(
        AkcssBodyMemberSyntax member,
        IAkcssSymbol containingSymbol);
}
