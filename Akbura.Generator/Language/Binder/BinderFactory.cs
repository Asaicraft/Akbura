using Akbura.Language.Declarations;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed partial class BinderFactory
{
    private static readonly ObjectPool<BinderFactoryVisitor> s_binderFactoryVisitorPool = new(
        () => new BinderFactoryVisitor(),
        size: 64);

    private readonly AkburaSemanticModel _semanticModel;
    private readonly BindingSession _bindingSession;
    private readonly ObjectPool<BinderFactoryVisitor> _binderFactoryVisitorPool;

    public BinderFactory(AkburaSemanticModel semanticModel)
        : this(semanticModel, semanticModel?.BindingSession!)
    {
    }

    internal BinderFactory(
        AkburaSemanticModel semanticModel,
        BindingSession bindingSession,
        ObjectPool<BinderFactoryVisitor>? binderFactoryVisitorPool = null)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
        _bindingSession = bindingSession ?? throw new ArgumentNullException(nameof(bindingSession));
        _binderFactoryVisitorPool = binderFactoryVisitorPool ?? s_binderFactoryVisitorPool;
    }

    public Binder GetBinder(AkburaSyntax syntax)
    {
        return GetBinder(syntax, BinderUsage.Default);
    }

    public Binder GetBinder(AkburaSyntax syntax, BinderUsage usage)
    {
        return _bindingSession.GetBinder(syntax, usage);
    }

    public Binder GetBinder(AkburaSyntax syntax, int position)
    {
        return GetBinder(syntax, position, BinderUsage.Default);
    }

    public Binder GetBinder(AkburaSyntax syntax, int position, BinderUsage usage)
    {
        return _bindingSession.GetBinder(syntax, position, usage);
    }

    internal Binder CreateBinder(
        ImmutableArray<AkburaDeclaration> path,
        BinderUsage usage)
    {
        var visitor = GetBinderFactoryVisitor(path, usage);
        try
        {
            return visitor.VisitPath();
        }
        finally
        {
            ClearBinderFactoryVisitor(visitor);
        }
    }

    internal AkburaSemanticModel SemanticModel => _semanticModel;

    internal CompilationBinder RootBinder => _bindingSession.RootBinder;

    private BinderFactoryVisitor GetBinderFactoryVisitor(
        ImmutableArray<AkburaDeclaration> path,
        BinderUsage usage)
    {
        var visitor = _binderFactoryVisitorPool.Allocate();
        visitor.Initialize(this, path, usage);
        return visitor;
    }

    private void ClearBinderFactoryVisitor(BinderFactoryVisitor visitor)
    {
        visitor.Clear();
        _binderFactoryVisitorPool.Free(visitor);
    }
}
