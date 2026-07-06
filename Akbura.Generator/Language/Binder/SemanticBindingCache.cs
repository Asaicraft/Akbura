using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Akbura.Language.Binder;

internal sealed class SemanticBindingCache
{
    private readonly ReaderWriterLockSlim _cacheLock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfoCache = new();
    private readonly Dictionary<AkburaSyntax, IOperation?> _operationCache = new();
    private readonly Dictionary<AkburaSyntax, BoundNode> _boundNodeCache = new();
    private readonly Dictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> _diagnosticsCache = new();

    public AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax, Func<AkburaSymbolInfo> bind)
    {
        _cacheLock.EnterReadLock();
        try
        {
            if (_symbolInfoCache.TryGetValue(syntax, out var symbolInfo))
            {
                return symbolInfo;
            }
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }

        var created = bind();
        _cacheLock.EnterWriteLock();
        try
        {
            if (_symbolInfoCache.TryGetValue(syntax, out var symbolInfo))
            {
                return symbolInfo;
            }

            _symbolInfoCache[syntax] = created;
            return created;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public bool TryGetSymbolInfo(AkburaSyntax syntax, out AkburaSymbolInfo symbolInfo)
    {
        _cacheLock.EnterReadLock();
        try
        {
            return _symbolInfoCache.TryGetValue(syntax, out symbolInfo);
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }
    }

    public void SetSymbolInfo(AkburaSyntax syntax, AkburaSymbolInfo symbolInfo)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            _symbolInfoCache[syntax] = symbolInfo;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public IOperation? GetOperation(AkburaSyntax syntax, Func<IOperation?> bind)
    {
        _cacheLock.EnterReadLock();
        try
        {
            if (_operationCache.TryGetValue(syntax, out var operation))
            {
                return operation;
            }
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }

        var created = bind();
        _cacheLock.EnterWriteLock();
        try
        {
            if (_operationCache.TryGetValue(syntax, out var operation))
            {
                return operation;
            }

            _operationCache[syntax] = created;
            return created;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public bool TryGetOperation(AkburaSyntax syntax, out IOperation? operation)
    {
        _cacheLock.EnterReadLock();
        try
        {
            return _operationCache.TryGetValue(syntax, out operation);
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }
    }

    public void SetOperation(AkburaSyntax syntax, IOperation? operation)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            _operationCache[syntax] = operation;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public BoundNode GetBoundNode(AkburaSyntax syntax, Func<BoundNode> bind)
    {
        _cacheLock.EnterReadLock();
        try
        {
            if (_boundNodeCache.TryGetValue(syntax, out var boundNode))
            {
                return boundNode;
            }
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }

        var created = bind();
        _cacheLock.EnterWriteLock();
        try
        {
            if (_boundNodeCache.TryGetValue(syntax, out var boundNode))
            {
                return boundNode;
            }

            _boundNodeCache[syntax] = created;
            return created;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public bool TryGetBoundNode(AkburaSyntax syntax, out BoundNode boundNode)
    {
        _cacheLock.EnterReadLock();
        try
        {
            return _boundNodeCache.TryGetValue(syntax, out boundNode!);
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }
    }

    public void SetBoundNode(AkburaSyntax syntax, BoundNode boundNode)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            _boundNodeCache[syntax] = boundNode;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public ImmutableArray<AkburaSemanticDiagnostic> GetDiagnostics(
        AkburaSyntax syntax,
        Func<ImmutableArray<AkburaSemanticDiagnostic>> bind)
    {
        _cacheLock.EnterReadLock();
        try
        {
            if (_diagnosticsCache.TryGetValue(syntax, out var diagnostics))
            {
                return diagnostics;
            }
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }

        var created = bind();
        _cacheLock.EnterWriteLock();
        try
        {
            if (_diagnosticsCache.TryGetValue(syntax, out var diagnostics))
            {
                return diagnostics;
            }

            _diagnosticsCache[syntax] = created;
            return created;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public bool TryGetDiagnostics(
        AkburaSyntax syntax,
        out ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        _cacheLock.EnterReadLock();
        try
        {
            return _diagnosticsCache.TryGetValue(syntax, out diagnostics);
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }
    }

    public void SetDiagnostics(
        AkburaSyntax syntax,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        _cacheLock.EnterWriteLock();
        try
        {
            _diagnosticsCache[syntax] = diagnostics;
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }
    }

    public bool ContainsDiagnostics(AkburaSyntax syntax)
    {
        _cacheLock.EnterReadLock();
        try
        {
            return _diagnosticsCache.ContainsKey(syntax);
        }
        finally
        {
            _cacheLock.ExitReadLock();
        }
    }
}
