using Akbura.Language.BoundTree;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using RoslynSemanticModel = Microsoft.CodeAnalysis.SemanticModel;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder : Binder
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

    public AkburaConversion ClassifyConversion(
        ITypeSymbol? sourceType,
        ITypeSymbol? targetType)
    {
        return Conversions.ClassifyConversion(sourceType, targetType);
    }

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

    public BoundExpression BindExpression(
        AkburaSyntax syntax,
        CSharp.ExpressionSyntax expression,
        ITypeSymbol? targetType = null,
        bool isBindingPath = true)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (expression == null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        var syntaxTree = CreateSyntaxTree(CreateReturnExpressionProbe(syntax, expression));
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeExpression = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.ReturnStatementSyntax>()
            .Single()
            .Expression;

        var boundExpression = probeExpression == null
            ? new BoundCSharpExpression(syntax, this, CSharpBindingResult.Empty)
            : BindExpressionTree(syntax, semanticModel, probeExpression, isBindingPath);

        if (targetType == null)
        {
            return boundExpression;
        }

        var conversion = Conversions.ClassifyConversion(boundExpression, targetType);
        return new BoundConversionExpression(
            syntax,
            this,
            boundExpression,
            conversion);
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

    public BoundStatement BindStatement(
        AkburaSyntax syntax,
        CSharp.StatementSyntax statement,
        bool isBindingPath = false)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (statement == null)
        {
            throw new ArgumentNullException(nameof(statement));
        }

        var syntaxTree = CreateSyntaxTree(CreateStatementProbe(syntax, statement));
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeStatement = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.MethodDeclarationSyntax>()
            .Single(methodDeclaration => methodDeclaration.Identifier.ValueText == "__akbura_statement_probe")
            .Body!
            .Statements
            .Last();

        return BindStatementTree(syntax, semanticModel, probeStatement, isBindingPath);
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

    private CSharp.CompilationUnitSyntax CreateReturnExpressionProbe(
        AkburaSyntax scope,
        CSharp.ExpressionSyntax expression)
    {
        var probeScope = CreateProbeScope(scope, expression);
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expression);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(CSharpSyntaxKind.ObjectKeyword)),
                "__akbura_probe")
            .WithBody(CreateProbeBlock(probeScope.LocalStatements, returnStatement));
        var type = CSharpSyntaxFactory.ClassDeclaration("__AkburaProbe")
            .WithMembers(CSharpSyntaxFactory.List(AddProbeMethod(probeScope.MemberDeclarations, method)));

        return CSharpSyntaxFactory.CompilationUnit()
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(type));
    }

    private CSharp.CompilationUnitSyntax CreateStatementProbe(
        AkburaSyntax scope,
        CSharp.StatementSyntax statement)
    {
        var probeScope = CreateProbeScope(scope, statement);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(CSharpSyntaxKind.VoidKeyword)),
                "__akbura_statement_probe")
            .WithBody(CreateProbeBlock(probeScope.LocalStatements, statement));
        var type = CSharpSyntaxFactory.ClassDeclaration("__AkburaStatementProbe")
            .WithMembers(CSharpSyntaxFactory.List(AddProbeMethod(probeScope.MemberDeclarations, method)));

        return CSharpSyntaxFactory.CompilationUnit()
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(type));
    }

    private BoundStatement BindStatementTree(
        AkburaSyntax syntax,
        RoslynSemanticModel semanticModel,
        CSharp.StatementSyntax statement,
        bool isBindingPath)
    {
        return statement switch
        {
            CSharp.LocalDeclarationStatementSyntax localDeclaration =>
                BindLocalDeclarationStatement(syntax, semanticModel, localDeclaration, isBindingPath),
            _ => BindCSharpStatement(syntax, semanticModel, statement),
        };
    }

    private BoundCSharpStatement BindCSharpStatement(
        AkburaSyntax syntax,
        RoslynSemanticModel semanticModel,
        CSharp.StatementSyntax statement)
    {
        var bindingResult = BindStatement(semanticModel, statement, symbol: null);
        return new BoundCSharpStatement(
            syntax,
            this,
            bindingResult,
            CreateStatementDiagnostics(syntax, bindingResult));
    }

    private BoundLocalDeclarationStatement BindLocalDeclarationStatement(
        AkburaSyntax syntax,
        RoslynSemanticModel semanticModel,
        CSharp.LocalDeclarationStatementSyntax statement,
        bool isBindingPath)
    {
        var locals = ArrayBuilder<ILocalSymbol>.GetInstance(statement.Declaration.Variables.Count);
        var initializers = ArrayBuilder<BoundExpression>.GetInstance(statement.Declaration.Variables.Count);

        foreach (var variable in statement.Declaration.Variables)
        {
            if (semanticModel.GetDeclaredSymbol(variable) is ILocalSymbol local)
            {
                locals.Add(local);
            }

            if (variable.Initializer != null)
            {
                initializers.Add(BindExpressionTree(
                    syntax,
                    semanticModel,
                    variable.Initializer.Value,
                    isBindingPath));
            }
        }

        var bindingResult = BindStatement(semanticModel, statement, locals.FirstOrDefault());
        return new BoundLocalDeclarationStatement(
            syntax,
            this,
            bindingResult,
            locals.ToImmutableAndFree(),
            initializers.ToImmutableAndFree(),
            CreateStatementDiagnostics(syntax, bindingResult));
    }

    private ImmutableArray<AkburaSemanticDiagnostic> CreateStatementDiagnostics(
        AkburaSyntax syntax,
        CSharpBindingResult bindingResult)
    {
        if (syntax.Kind == Akbura.Language.Syntax.SyntaxKind.CSharpStatementSyntax)
        {
            var statement = Unsafe.As<CSharpStatementSyntax>(syntax);
            var userHookDiagnostics = SemanticModel.CreateCSharpStatementUserHookDiagnostics(statement);
            if (!userHookDiagnostics.IsDefaultOrEmpty)
            {
                return userHookDiagnostics;
            }
        }

        using var builder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        AkburaSemanticModel.AddCSharpBindingDiagnostics(
            syntax,
            syntax.ToFullString().Trim(),
            bindingResult,
            builder);
        return builder.ToImmutable();
    }

    private BoundExpression BindExpressionTree(
        AkburaSyntax syntax,
        RoslynSemanticModel semanticModel,
        CSharp.ExpressionSyntax expression,
        bool isBindingPath)
    {
        var bindingResult = BindExpression(semanticModel, expression, isBindingPath);

        return expression switch
        {
            CSharp.LiteralExpressionSyntax literalExpression =>
                new BoundLiteralExpression(
                    syntax,
                    this,
                    bindingResult,
                    GetConstantValue(semanticModel, literalExpression)),
            CSharp.BinaryExpressionSyntax binaryExpression =>
                new BoundBinaryExpression(
                    syntax,
                    this,
                    bindingResult,
                    binaryExpression.Kind(),
                    BindExpressionTree(syntax, semanticModel, binaryExpression.Left, isBindingPath),
                    BindExpressionTree(syntax, semanticModel, binaryExpression.Right, isBindingPath)),
            CSharp.InvocationExpressionSyntax invocationExpression =>
                new BoundCallExpression(
                    syntax,
                    this,
                    bindingResult,
                    bindingResult.Symbol as IMethodSymbol,
                    BindInvocationReceiver(syntax, semanticModel, invocationExpression, isBindingPath),
                    BindInvocationArguments(syntax, semanticModel, invocationExpression, isBindingPath)),
            _ => new BoundCSharpExpression(
                syntax,
                this,
                bindingResult),
        };
    }

    private BoundExpression? BindInvocationReceiver(
        AkburaSyntax syntax,
        RoslynSemanticModel semanticModel,
        CSharp.InvocationExpressionSyntax invocationExpression,
        bool isBindingPath)
    {
        return invocationExpression.Expression switch
        {
            CSharp.MemberAccessExpressionSyntax memberAccess =>
                BindExpressionTree(syntax, semanticModel, memberAccess.Expression, isBindingPath),
            _ => null,
        };
    }

    private ImmutableArray<BoundExpression> BindInvocationArguments(
        AkburaSyntax syntax,
        RoslynSemanticModel semanticModel,
        CSharp.InvocationExpressionSyntax invocationExpression,
        bool isBindingPath)
    {
        var arguments = invocationExpression.ArgumentList.Arguments;
        if (arguments.Count == 0)
        {
            return ImmutableArray<BoundExpression>.Empty;
        }

        var builder = ArrayBuilder<BoundExpression>.GetInstance(arguments.Count);
        foreach (var argument in arguments)
        {
            builder.Add(BindExpressionTree(
                syntax,
                semanticModel,
                argument.Expression,
                isBindingPath));
        }

        return builder.ToImmutableAndFree();
    }

    private static object? GetConstantValue(
        RoslynSemanticModel semanticModel,
        CSharp.LiteralExpressionSyntax expression)
    {
        var constant = semanticModel.GetConstantValue(expression);
        return constant.HasValue
            ? constant.Value
            : expression.Token.Value;
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

    private static CSharpBindingResult BindStatement(
        RoslynSemanticModel semanticModel,
        CSharp.StatementSyntax statement,
        Microsoft.CodeAnalysis.ISymbol? symbol)
    {
        var operation = semanticModel.GetOperation(statement);
        var diagnostics = GetProbeDiagnostics(semanticModel, statement);

        return new CSharpBindingResult(
            typeSymbol: null,
            symbol,
            receiverType: null,
            isBindingPath: false,
            candidateSymbols: ImmutableArray<Microsoft.CodeAnalysis.ISymbol>.Empty,
            symbol == null ? AkburaCandidateReason.NotFound : AkburaCandidateReason.None,
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
