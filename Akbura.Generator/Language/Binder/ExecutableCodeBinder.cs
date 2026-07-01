using Akbura.Collections;
using Akbura.Language.Declarations;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Akbura.Language.Binder;

/// <summary>
/// Owns a lazy syntax-to-binder map for one executable declaration root.
/// </summary>
internal sealed class ExecutableCodeBinder : Binder
{
    private readonly BindingSession _bindingSession;
    private readonly AkburaDeclaration _rootDeclaration;
    private readonly BinderUsage _usage;
    private SmallDictionary<AkburaSyntax, Binder>? _lazyBinderMap;

    public ExecutableCodeBinder(
        BindingSession bindingSession,
        AkburaDeclaration rootDeclaration,
        Binder next,
        BinderUsage usage)
        : base(
            next?.SemanticModel ?? throw new ArgumentNullException(nameof(next)),
            next,
            rootDeclaration,
            rootDeclaration?.Syntax,
            next.Flags)
    {
        _bindingSession = bindingSession ?? throw new ArgumentNullException(nameof(bindingSession));
        _rootDeclaration = rootDeclaration ?? throw new ArgumentNullException(nameof(rootDeclaration));
        _usage = usage;
    }

    public Binder GetBinder(AkburaSyntax syntax)
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

    public Binder GetBinder(
        AkburaSyntax syntax,
        int position)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (!_bindingSession.TryFindDeclarationPath(
                _rootDeclaration,
                syntax,
                position,
                out var path))
        {
            return NextRequired;
        }

        var scopeSyntax = path[path.Length - 1].Syntax;
        var map = BinderMap;
        return map.TryGetValue(scopeSyntax, out var binder)
            ? binder
            : NextRequired;
    }

    private void ComputeBinderMap()
    {
        var map = new SmallDictionary<AkburaSyntax, Binder>(
            AkburaSyntaxGreenComparer.Instance);
        var path = ArrayBuilder<AkburaDeclaration>.GetInstance();
        try
        {
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

    private sealed class AkburaSyntaxGreenComparer : IEqualityComparer<AkburaSyntax>
    {
        public static readonly AkburaSyntaxGreenComparer Instance = new();

        public bool Equals(AkburaSyntax? x, AkburaSyntax? y)
        {
            return ReferenceEquals(x, y) ||
                   x != null &&
                   y != null &&
                   ReferenceEquals(x.Green, y.Green);
        }

        public int GetHashCode(AkburaSyntax obj)
        {
            return RuntimeHelpers.GetHashCode(obj.Green);
        }
    }
}
