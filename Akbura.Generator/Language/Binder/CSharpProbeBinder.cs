using Akbura.Language.Operations;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynSemanticModel = Microsoft.CodeAnalysis.SemanticModel;

namespace Akbura.Language.Binder;

internal sealed class CSharpProbeBinder : Binder
{
    public CSharpProbeBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration: null,
            scopeDesignator: next.ScopeDesignator,
            flags: flags | AkburaBinderFlags.InCSharpProbe)
    {
    }

    public CSharpCompilation CSharpCompilation => Compilation.CSharpCompilation;

    public CSharpBindingResult BindFieldType(CSharp.CompilationUnitSyntax compilationUnit)
    {
        var syntaxTree = CreateSyntaxTree(compilationUnit);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeType = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.FieldDeclarationSyntax>()
            .Single()
            .Declaration
            .Type;

        var typeInfo = semanticModel.GetTypeInfo(probeType);
        var symbolInfo = semanticModel.GetSymbolInfo(probeType);
        var typeSymbol = typeInfo.Type?.TypeKind == TypeKind.Error
            ? null
            : typeInfo.Type;

        return new CSharpBindingResult(
            typeSymbol,
            symbolInfo.Symbol,
            receiverType: null,
            isBindingPath: true,
            symbolInfo.CandidateSymbols,
            ToAkburaCandidateReason(symbolInfo.CandidateReason),
            operationDefinition: default);
    }

    public CSharpBindingResult BindReturnExpression(
        CSharp.CompilationUnitSyntax compilationUnit,
        bool isBindingPath)
    {
        var syntaxTree = CreateSyntaxTree(compilationUnit);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeExpression = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.ReturnStatementSyntax>()
            .Single()
            .Expression;

        return probeExpression == null
            ? CSharpBindingResult.Empty
            : BindExpression(semanticModel, probeExpression, isBindingPath);
    }

    public CSharpBindingResult BindExpressionStatement(
        CSharp.CompilationUnitSyntax compilationUnit,
        bool isBindingPath)
    {
        var syntaxTree = CreateSyntaxTree(compilationUnit);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeExpression = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.ExpressionStatementSyntax>()
            .Single()
            .Expression;

        return BindExpression(semanticModel, probeExpression, isBindingPath);
    }

    public CSharpBindingResult BindMethodBlock(
        CSharp.CompilationUnitSyntax compilationUnit,
        string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            throw new ArgumentException("Probe method name cannot be empty.", nameof(methodName));
        }

        var syntaxTree = CreateSyntaxTree(compilationUnit);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeBlock = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.MethodDeclarationSyntax>()
            .Single(methodDeclaration => methodDeclaration.Identifier.ValueText == methodName)
            .Body;

        if (probeBlock == null)
        {
            return CSharpBindingResult.Empty;
        }

        var operation = semanticModel.GetOperation(probeBlock);
        var diagnostics = GetProbeDiagnostics(semanticModel, probeBlock);
        return new CSharpBindingResult(
            typeSymbol: null,
            symbol: null,
            receiverType: null,
            isBindingPath: false,
            candidateSymbols: ImmutableArray<Microsoft.CodeAnalysis.ISymbol>.Empty,
            candidateReason: AkburaCandidateReason.None,
            operation == null ? default : new CSharpOperationDefinition(operation),
            diagnostics);
    }

    private SyntaxTree CreateSyntaxTree(CSharp.CompilationUnitSyntax compilationUnit)
    {
        var parseOptions = CSharpCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ??
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        return CSharpSyntaxTree.Create(compilationUnit, parseOptions);
    }

    private RoslynSemanticModel CreateSemanticModel(SyntaxTree syntaxTree)
    {
        var probeCompilation = CSharpCompilation.AddSyntaxTrees(syntaxTree);
        return probeCompilation.GetSemanticModel(syntaxTree);
    }

    private static CSharpBindingResult BindExpression(
        RoslynSemanticModel semanticModel,
        CSharp.ExpressionSyntax expression,
        bool isBindingPath)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        var operation = semanticModel.GetOperation(expression);
        var receiverType = GetExpressionReceiverType(semanticModel, expression);
        var diagnostics = GetProbeDiagnostics(semanticModel, expression);
        var typeSymbol = typeInfo.Type?.TypeKind == TypeKind.Error
            ? null
            : typeInfo.Type;

        return new CSharpBindingResult(
            typeSymbol,
            symbolInfo.Symbol,
            receiverType,
            isBindingPath,
            symbolInfo.CandidateSymbols,
            ToAkburaCandidateReason(symbolInfo.CandidateReason),
            operation == null ? default : new CSharpOperationDefinition(operation),
            diagnostics);
    }

    private static ImmutableArray<Diagnostic> GetProbeDiagnostics(
        RoslynSemanticModel semanticModel,
        SyntaxNode syntax)
    {
        using var builder = ImmutableArrayBuilder<Diagnostic>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var diagnostic in semanticModel.GetDiagnostics(syntax.Span))
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error ||
                diagnostic.Id == "CS0012")
            {
                continue;
            }

            var key = diagnostic.Id + "|" + diagnostic.GetMessage() + "|" +
                diagnostic.Location.SourceSpan.ToString();
            if (seen.Add(key))
            {
                builder.Add(diagnostic);
            }
        }

        return builder.ToImmutable();
    }

    private static ITypeSymbol? GetExpressionReceiverType(
        RoslynSemanticModel semanticModel,
        CSharp.ExpressionSyntax expression)
    {
        return expression switch
        {
            CSharp.MemberAccessExpressionSyntax memberAccess =>
                semanticModel.GetTypeInfo(memberAccess.Expression).Type,
            CSharp.ConditionalAccessExpressionSyntax conditionalAccess =>
                semanticModel.GetTypeInfo(conditionalAccess.Expression).Type,
            _ => null,
        };
    }

    private static AkburaCandidateReason ToAkburaCandidateReason(Microsoft.CodeAnalysis.CandidateReason reason)
    {
        return reason == Microsoft.CodeAnalysis.CandidateReason.Ambiguous
            ? AkburaCandidateReason.Ambiguous
            : AkburaCandidateReason.NotFound;
    }
}
