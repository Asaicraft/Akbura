using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed class SemanticBindingCache
{
    private readonly AkburaSemanticModel _semanticModel;
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfoCache;
    private readonly Dictionary<AkburaSyntax, IOperation?> _operationCache;
    private readonly Dictionary<AkburaSyntax, BoundNode> _boundNodeCache;
    private readonly Dictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> _diagnosticsCache;
    private readonly Dictionary<AkburaSyntax, SemanticReuseKey> _symbolInfoReuseKeys;
    private readonly Dictionary<AkburaSyntax, SemanticReuseKey> _operationReuseKeys;
    private readonly Dictionary<AkburaSyntax, SemanticReuseKey> _boundNodeReuseKeys;
    private readonly Dictionary<AkburaSyntax, SemanticReuseKey> _diagnosticsReuseKeys;

    public SemanticBindingCache(
        AkburaSemanticModel semanticModel,
        Dictionary<AkburaSyntax, AkburaSymbolInfo> symbolInfoCache,
        Dictionary<AkburaSyntax, IOperation?> operationCache,
        Dictionary<AkburaSyntax, BoundNode> boundNodeCache,
        Dictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> diagnosticsCache,
        Dictionary<AkburaSyntax, SemanticReuseKey> symbolInfoReuseKeys,
        Dictionary<AkburaSyntax, SemanticReuseKey> operationReuseKeys,
        Dictionary<AkburaSyntax, SemanticReuseKey> boundNodeReuseKeys,
        Dictionary<AkburaSyntax, SemanticReuseKey> diagnosticsReuseKeys)
    {
        _semanticModel = semanticModel;
        _symbolInfoCache = symbolInfoCache;
        _operationCache = operationCache;
        _boundNodeCache = boundNodeCache;
        _diagnosticsCache = diagnosticsCache;
        _symbolInfoReuseKeys = symbolInfoReuseKeys;
        _operationReuseKeys = operationReuseKeys;
        _boundNodeReuseKeys = boundNodeReuseKeys;
        _diagnosticsReuseKeys = diagnosticsReuseKeys;
    }

    public AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax, Func<AkburaSymbolInfo> bind)
    {
        var reuseKey = _semanticModel.BindingSession.GetSemanticCacheKey(syntax);
        if (_symbolInfoCache.TryGetValue(syntax, out var symbolInfo))
        {
            _symbolInfoReuseKeys[syntax] = reuseKey;
            return symbolInfo;
        }

        if (TryGetReusablePreviousSemanticModel(out var previousModel) &&
            previousModel.TryGetCachedSymbolInfoByGreen(syntax.Green, reuseKey, out symbolInfo))
        {
            _symbolInfoCache[syntax] = symbolInfo;
            _symbolInfoReuseKeys[syntax] = reuseKey;
            return symbolInfo;
        }

        symbolInfo = bind();
        _symbolInfoCache[syntax] = symbolInfo;
        _symbolInfoReuseKeys[syntax] = reuseKey;
        return symbolInfo;
    }

    public IOperation? GetOperation(AkburaSyntax syntax, Func<IOperation?> bind)
    {
        var reuseKey = _semanticModel.BindingSession.GetOperationCacheKey(syntax);
        if (_operationCache.TryGetValue(syntax, out var operation))
        {
            _operationReuseKeys[syntax] = reuseKey;
            return operation;
        }

        if (TryGetReusablePreviousSemanticModel(out var previousModel) &&
            previousModel.TryGetCachedOperationByGreen(syntax.Green, reuseKey, out operation))
        {
            _operationCache[syntax] = operation;
            _operationReuseKeys[syntax] = reuseKey;
            return operation;
        }

        operation = bind();
        _operationCache[syntax] = operation;
        _operationReuseKeys[syntax] = reuseKey;
        return operation;
    }

    public BoundNode GetBoundNode(
        AkburaSyntax syntax,
        Func<BoundNode> bind,
        SemanticReuseKey reuseKey)
    {
        if (_boundNodeCache.TryGetValue(syntax, out var boundNode))
        {
            _boundNodeReuseKeys[syntax] = reuseKey;
            return boundNode;
        }

        if (TryGetReusablePreviousSemanticModel(out var previousModel) &&
            previousModel.TryGetCachedBoundNodeByGreen(syntax.Green, reuseKey, out boundNode))
        {
            _boundNodeCache[syntax] = boundNode;
            _boundNodeReuseKeys[syntax] = reuseKey;
            return boundNode;
        }

        boundNode = bind();
        _boundNodeCache[syntax] = boundNode;
        _boundNodeReuseKeys[syntax] = reuseKey;
        return boundNode;
    }

    public ImmutableArray<AkburaSemanticDiagnostic> GetDiagnostics(
        AkburaSyntax syntax,
        Func<ImmutableArray<AkburaSemanticDiagnostic>> bind)
    {
        var reuseKey = _semanticModel.BindingSession.GetDiagnosticsCacheKey(syntax);
        if (_diagnosticsCache.TryGetValue(syntax, out var diagnostics))
        {
            _diagnosticsReuseKeys[syntax] = reuseKey;
            return diagnostics;
        }

        if (TryGetReusablePreviousSemanticModel(out var previousModel) &&
            previousModel.TryGetCachedDiagnosticsByGreen(syntax.Green, reuseKey, out diagnostics))
        {
            _diagnosticsCache[syntax] = diagnostics;
            _diagnosticsReuseKeys[syntax] = reuseKey;
            return diagnostics;
        }

        diagnostics = bind();
        _diagnosticsCache[syntax] = diagnostics;
        _diagnosticsReuseKeys[syntax] = reuseKey;
        return diagnostics;
    }

    private bool TryGetReusablePreviousSemanticModel(out AkburaSemanticModel previousModel)
    {
        previousModel = null!;
        var previousCompilation = _semanticModel.Compilation.PreviousCompilation;
        if (previousCompilation == null ||
            !ReferenceEquals(previousCompilation.CSharpCompilation, _semanticModel.Compilation.CSharpCompilation))
        {
            return false;
        }

        return previousCompilation.TryGetCachedSemanticModel(
            _semanticModel.SyntaxTree.FilePath,
            out previousModel);
    }
}
