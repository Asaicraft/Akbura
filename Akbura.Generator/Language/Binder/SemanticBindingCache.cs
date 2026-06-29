using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed class SemanticBindingCache
{
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfoCache;
    private readonly Dictionary<AkburaSyntax, IOperation?> _operationCache;
    private readonly Dictionary<AkburaSyntax, BoundNode> _boundNodeCache;
    private readonly Dictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> _diagnosticsCache;

    public SemanticBindingCache(
        Dictionary<AkburaSyntax, AkburaSymbolInfo> symbolInfoCache,
        Dictionary<AkburaSyntax, IOperation?> operationCache,
        Dictionary<AkburaSyntax, BoundNode> boundNodeCache,
        Dictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> diagnosticsCache)
    {
        _symbolInfoCache = symbolInfoCache;
        _operationCache = operationCache;
        _boundNodeCache = boundNodeCache;
        _diagnosticsCache = diagnosticsCache;
    }

    public AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax, Func<AkburaSymbolInfo> bind)
    {
        if (_symbolInfoCache.TryGetValue(syntax, out var symbolInfo))
        {
            return symbolInfo;
        }

        symbolInfo = bind();
        _symbolInfoCache[syntax] = symbolInfo;
        return symbolInfo;
    }

    public IOperation? GetOperation(AkburaSyntax syntax, Func<IOperation?> bind)
    {
        if (_operationCache.TryGetValue(syntax, out var operation))
        {
            return operation;
        }

        operation = bind();
        _operationCache[syntax] = operation;
        return operation;
    }

    public BoundNode GetBoundNode(AkburaSyntax syntax, Func<BoundNode> bind)
    {
        if (_boundNodeCache.TryGetValue(syntax, out var boundNode))
        {
            return boundNode;
        }

        boundNode = bind();
        _boundNodeCache[syntax] = boundNode;
        return boundNode;
    }

    public ImmutableArray<AkburaSemanticDiagnostic> GetDiagnostics(
        AkburaSyntax syntax,
        Func<ImmutableArray<AkburaSemanticDiagnostic>> bind)
    {
        if (_diagnosticsCache.TryGetValue(syntax, out var diagnostics))
        {
            return diagnostics;
        }

        diagnostics = bind();
        _diagnosticsCache[syntax] = diagnostics;
        return diagnostics;
    }
}
