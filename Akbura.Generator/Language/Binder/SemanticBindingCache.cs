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
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfoCache = new();
    private readonly Dictionary<AkburaSyntax, IOperation?> _operationCache = new();
    private readonly Dictionary<AkburaSyntax, BoundNode> _boundNodeCache = new();
    private readonly Dictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> _diagnosticsCache = new();

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

    public bool TryGetSymbolInfo(AkburaSyntax syntax, out AkburaSymbolInfo symbolInfo)
    {
        return _symbolInfoCache.TryGetValue(syntax, out symbolInfo);
    }

    public void SetSymbolInfo(AkburaSyntax syntax, AkburaSymbolInfo symbolInfo)
    {
        _symbolInfoCache[syntax] = symbolInfo;
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

    public bool TryGetOperation(AkburaSyntax syntax, out IOperation? operation)
    {
        return _operationCache.TryGetValue(syntax, out operation);
    }

    public void SetOperation(AkburaSyntax syntax, IOperation? operation)
    {
        _operationCache[syntax] = operation;
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

    public bool TryGetBoundNode(AkburaSyntax syntax, out BoundNode boundNode)
    {
        return _boundNodeCache.TryGetValue(syntax, out boundNode!);
    }

    public void SetBoundNode(AkburaSyntax syntax, BoundNode boundNode)
    {
        _boundNodeCache[syntax] = boundNode;
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

    public bool TryGetDiagnostics(
        AkburaSyntax syntax,
        out ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        return _diagnosticsCache.TryGetValue(syntax, out diagnostics);
    }

    public void SetDiagnostics(
        AkburaSyntax syntax,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        _diagnosticsCache[syntax] = diagnostics;
    }

    public bool ContainsDiagnostics(AkburaSyntax syntax)
    {
        return _diagnosticsCache.ContainsKey(syntax);
    }
}
