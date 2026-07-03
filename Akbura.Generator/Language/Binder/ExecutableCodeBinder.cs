using Akbura.Collections;
using Akbura.Language.Declarations;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;
using System.Collections.Immutable;
using System.Threading;

namespace Akbura.Language.Binder;

/// <summary>
/// Owns a lazy syntax-to-binder map for one executable declaration root.
/// </summary>
internal sealed class ExecutableCodeBinder : Binder
{
    private readonly BindingSession _bindingSession;
    private readonly ImmutableArray<AkburaDeclaration> _rootPath;
    private readonly AkburaDeclaration _rootDeclaration;
    private readonly BinderUsage _usage;
    private SmallDictionary<AkburaSyntax, Binder>? _lazyBinderMap;

    public ExecutableCodeBinder(
        BindingSession bindingSession,
        ImmutableArray<AkburaDeclaration> rootPath,
        Binder next,
        BinderUsage usage)
        : base(
            next?.SemanticModel ?? throw new ArgumentNullException(nameof(next)),
            next,
            GetRootDeclaration(rootPath),
            GetRootDeclaration(rootPath).Syntax,
            next.Flags)
    {
        _bindingSession = bindingSession ?? throw new ArgumentNullException(nameof(bindingSession));
        if (rootPath.IsDefaultOrEmpty)
        {
            throw new ArgumentException("Executable root path cannot be empty.", nameof(rootPath));
        }

        _rootPath = rootPath;
        _rootDeclaration = rootPath[rootPath.Length - 1];
        _usage = usage;
    }

    public override Binder? GetBinder(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        var map = BinderMap;
        return map.TryGetValue(syntax, out var binder)
            ? binder
            : NextRequired;
    }

    private void ComputeBinderMap()
    {
        var map = new SmallDictionary<AkburaSyntax, Binder>();
        var path = ArrayBuilder<AkburaDeclaration>.GetInstance();
        try
        {
            for (var index = 0; index < _rootPath.Length - 1; index++)
            {
                path.Add(_rootPath[index]);
            }

            AddDeclarationBinders(map, path, _rootDeclaration);
        }
        finally
        {
            path.Free();
        }

        Interlocked.CompareExchange(ref _lazyBinderMap, map, null);
    }

    private void AddDeclarationBinders(
        SmallDictionary<AkburaSyntax, Binder> map,
        ArrayBuilder<AkburaDeclaration> path,
        AkburaDeclaration declaration)
    {
        path.Add(declaration);
        map[declaration.Syntax] = _bindingSession.GetOrCreateBinder(
            path.ToImmutable(),
            _usage);

        foreach (var child in declaration.Children)
        {
            AddDeclarationBinders(map, path, child);
        }

        path.RemoveLast();
    }

    private SmallDictionary<AkburaSyntax, Binder> BinderMap
    {
        get
        {
            if (_lazyBinderMap == null)
            {
                ComputeBinderMap();
            }

            return _lazyBinderMap!;
        }
    }

    private static AkburaDeclaration GetRootDeclaration(ImmutableArray<AkburaDeclaration> rootPath)
    {
        return !rootPath.IsDefaultOrEmpty
            ? rootPath[rootPath.Length - 1]
            : throw new ArgumentException("Executable root path cannot be empty.", nameof(rootPath));
    }
}
