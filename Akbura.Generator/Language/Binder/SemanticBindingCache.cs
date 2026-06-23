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

    public SemanticBindingCache(
        AkburaSemanticModel semanticModel,
        Dictionary<AkburaSyntax, AkburaSymbolInfo> symbolInfoCache,
        Dictionary<AkburaSyntax, IOperation?> operationCache,
        Dictionary<AkburaSyntax, BoundNode> boundNodeCache,
        Dictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> diagnosticsCache)
    {
        _semanticModel = semanticModel;
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

        if (TryGetReusablePreviousSemanticModel(out var previousModel) &&
            previousModel.TryGetCachedSymbolInfoByGreen(syntax.Green, out symbolInfo))
        {
            _symbolInfoCache[syntax] = symbolInfo;
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

        if (TryGetReusablePreviousSemanticModel(out var previousModel) &&
            previousModel.TryGetCachedOperationByGreen(syntax.Green, out operation))
        {
            _operationCache[syntax] = operation;
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

        if (TryGetReusablePreviousSemanticModel(out var previousModel) &&
            previousModel.TryGetCachedBoundNodeByGreen(syntax.Green, out boundNode))
        {
            _boundNodeCache[syntax] = boundNode;
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

        if (TryGetReusablePreviousSemanticModel(out var previousModel) &&
            previousModel.TryGetCachedDiagnosticsByGreen(syntax.Green, out diagnostics))
        {
            _diagnosticsCache[syntax] = diagnostics;
            return diagnostics;
        }

        diagnostics = bind();
        _diagnosticsCache[syntax] = diagnostics;
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
