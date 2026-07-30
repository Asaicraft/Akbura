using Akbura.Language.BoundTree;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
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

    public CSharpCompilation CSharpCompilation =>
        Compilation.CSharpProbeCompilation;

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
        var diagnostics = GetProbeDiagnostics(semanticModel, probeType);
        var typeSymbol = ContainsErrorType(typeInfo.Type)
            ? null
            : typeInfo.Type;

        return new CSharpBindingResult(
            typeSymbol,
            symbolInfo.Symbol,
            receiverType: null,
            isBindingPath: true,
            symbolInfo.CandidateSymbols,
            ToAkburaCandidateReason(symbolInfo.CandidateReason),
            operationDefinition: default,
            diagnostics);
    }

    public CSharpBindingResult BindReturnExpression(
        CSharp.CompilationUnitSyntax compilationUnit,
        bool isBindingPath,
        ITypeSymbol? targetType = null)
    {
        var syntaxTree = CreateSyntaxTree(compilationUnit);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeExpression = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.ReturnStatementSyntax>()
            .Single()
            .Expression;

        var binding = probeExpression == null
            ? CSharpBindingResult.Empty
            : BindExpression(
                semanticModel,
                probeExpression,
                isBindingPath,
                suppressTopLevelConversionDiagnostic: targetType != null);
        return targetType == null || probeExpression == null
            ? binding
            : binding.WithConversion(Conversions.ClassifyConversion(
                binding.TypeSymbol,
                targetType,
                semanticModel.GetConversion(probeExpression)));
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

        var syntaxTree = CreateSyntaxTree(CreateReturnExpressionProbe(
            syntax,
            expression,
            targetType));
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeExpression = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.ReturnStatementSyntax>()
            .Single()
            .Expression;

        var boundExpression = probeExpression == null
            ? new BoundCSharpExpression(syntax, this, CSharpBindingResult.Empty)
            : BindExpressionTree(
                syntax,
                semanticModel,
                probeExpression,
                isBindingPath,
                suppressTopLevelConversionDiagnostic: targetType != null);

        if (targetType == null || probeExpression == null)
        {
            return boundExpression;
        }

        var conversion = Conversions.ClassifyConversion(
            boundExpression.Type,
            targetType,
            semanticModel.GetConversion(probeExpression));
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

        if (TryBindComponentMethodStatement(
                syntax,
                isBindingPath,
                out var componentMethodStatement))
        {
            return componentMethodStatement;
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

    private bool TryBindComponentMethodStatement(
        AkburaSyntax syntax,
        bool isBindingPath,
        out BoundStatement boundStatement)
    {
        boundStatement = null!;
        if (syntax is not CSharpStatementSyntax ||
            !TryGetContainingComponentMethod(
                syntax,
                out var methodSyntax,
                out var method))
        {
            return false;
        }

        var relativeSpan = new TextSpan(
            syntax.Span.Start - methodSyntax.Position,
            syntax.Span.Length);
        var statement = method
            .DescendantNodes()
            .OfType<CSharp.StatementSyntax>()
            .FirstOrDefault(candidate => candidate.Span == relativeSpan);
        if (statement == null)
        {
            return false;
        }

        var annotation = new SyntaxAnnotation();
        method = method.ReplaceNode(
            statement,
            statement.WithAdditionalAnnotations(annotation));
        var probeScope = CreateProbeScope(
            syntax,
            method,
            ImmutableArray.Create(method.Identifier.ValueText));
        if (!probeScope.LocalStatements.IsDefaultOrEmpty &&
            method.Body != null)
        {
            method = method.WithBody(method.Body.WithStatements(
                method.Body.Statements.InsertRange(
                    0,
                    probeScope.LocalStatements)));
        }

        var syntaxTree = CreateSyntaxTree(CreateComponentProbeCompilationUnit(
            AddProbeMethod(probeScope.MemberDeclarations, method),
            "__AkburaComponentMethodProbe"));
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeStatement = syntaxTree
            .GetRoot()
            .GetAnnotatedNodes(annotation)
            .OfType<CSharp.StatementSyntax>()
            .Single();
        boundStatement = BindStatementTree(
            syntax,
            semanticModel,
            probeStatement,
            isBindingPath);
        return true;
    }

    private static bool TryGetContainingComponentMethod(
        AkburaSyntax syntax,
        out CSharpStatementSyntax methodSyntax,
        out CSharp.MethodDeclarationSyntax method)
    {
        for (var current = syntax.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is not CSharpStatementSyntax statement ||
                statement.Parent is not AkburaDocumentSyntax)
            {
                continue;
            }

            try
            {
                var parsedMethod = CSharpSyntaxFactory.ParseMemberDeclaration(
                    statement.ToFullString()) as CSharp.MethodDeclarationSyntax;
                if (parsedMethod != null)
                {
                    methodSyntax = statement;
                    method = parsedMethod;
                    return true;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        methodSyntax = null!;
        method = null!;
        return false;
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
        CSharp.ExpressionSyntax expression,
        ITypeSymbol? targetType)
    {
        var precedingLocals = GetPrecedingLocalDeclarations(scope);
        var containingMethod = GetContainingComponentMethodProbe(scope);
        var probeScope = CreateProbeScope(
            scope,
            expression,
            GetParameterNames(containingMethod));
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expression);
        var returnType = targetType == null
            ? CSharpSyntaxFactory.PredefinedType(
                CSharpSyntaxFactory.Token(CSharpSyntaxKind.ObjectKeyword))
            : CSharpSyntaxFactory.ParseTypeName(
                targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        var method = CSharpSyntaxFactory.MethodDeclaration(
                returnType,
                "__akbura_probe")
            .WithBody(CreateProbeBlock(
                probeScope.LocalStatements,
                precedingLocals,
                returnStatement));
        method = ApplyContainingMethodContext(method, containingMethod);
        return CreateComponentProbeCompilationUnit(
            AddProbeMethod(probeScope.MemberDeclarations, method),
            "__AkburaProbe");
    }

    private CSharp.CompilationUnitSyntax CreateStatementProbe(
        AkburaSyntax scope,
        CSharp.StatementSyntax statement)
    {
        var precedingLocals = GetPrecedingLocalDeclarations(scope);
        var analyzedBlock = CreateProbeBlock(
            ImmutableArray<CSharp.StatementSyntax>.Empty,
            precedingLocals,
            statement);
        var containingMethod = GetContainingComponentMethodProbe(scope);
        var probeScope = CreateProbeScope(
            scope,
            analyzedBlock,
            GetParameterNames(containingMethod));
        var method = CSharpSyntaxFactory.MethodDeclaration(
                containingMethod?.ReturnType ??
                    CSharpSyntaxFactory.PredefinedType(
                        CSharpSyntaxFactory.Token(CSharpSyntaxKind.VoidKeyword)),
                "__akbura_statement_probe")
            .WithBody(CreateProbeBlock(
                probeScope.LocalStatements,
                precedingLocals,
                statement));
        method = ApplyContainingMethodContext(method, containingMethod);
        return CreateComponentProbeCompilationUnit(
            AddProbeMethod(probeScope.MemberDeclarations, method),
            "__AkburaStatementProbe");
    }

    private static CSharp.MethodDeclarationSyntax ApplyContainingMethodContext(
        CSharp.MethodDeclarationSyntax probeMethod,
        CSharp.MethodDeclarationSyntax? containingMethod)
    {
        if (containingMethod == null)
        {
            return probeMethod;
        }

        return probeMethod
            .WithAttributeLists(containingMethod.AttributeLists)
            .WithModifiers(FilterProbeMethodModifiers(containingMethod.Modifiers))
            .WithTypeParameterList(containingMethod.TypeParameterList)
            .WithParameterList(containingMethod.ParameterList)
            .WithConstraintClauses(containingMethod.ConstraintClauses);
    }

    private static Microsoft.CodeAnalysis.SyntaxTokenList FilterProbeMethodModifiers(
        Microsoft.CodeAnalysis.SyntaxTokenList modifiers)
    {
        return CSharpSyntaxFactory.TokenList(
            modifiers.Where(static modifier =>
                modifier.IsKind(CSharpSyntaxKind.StaticKeyword) ||
                modifier.IsKind(CSharpSyntaxKind.AsyncKeyword) ||
                modifier.IsKind(CSharpSyntaxKind.UnsafeKeyword)));
    }

    private static ImmutableArray<string> GetParameterNames(
        CSharp.MethodDeclarationSyntax? method)
    {
        if (method == null ||
            method.ParameterList.Parameters.Count == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        return method.ParameterList.Parameters
            .Select(static parameter => parameter.Identifier.ValueText)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToImmutableArray();
    }

    private static CSharp.MethodDeclarationSyntax? GetContainingComponentMethodProbe(
        AkburaSyntax scope)
    {
        for (var current = scope.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is CSharpStatementSyntax statement &&
                statement.Parent is AkburaDocumentSyntax &&
                TryCreateComponentMethodProbe(statement, out var method))
            {
                return method;
            }
        }

        return null;
    }

    private static ImmutableArray<CSharp.StatementSyntax> GetPrecedingLocalDeclarations(
        AkburaSyntax scope)
    {
        using var builder =
            ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        AddPrecedingLocalDeclarations(
            scope,
            builder);
        return builder.ToImmutable();
    }

    private static void AddPrecedingLocalDeclarations(
        AkburaSyntax scope,
        ImmutableArrayBuilder<CSharp.StatementSyntax> builder)
    {
        var parent = scope.Parent;
        if (parent == null)
        {
            return;
        }

        AddPrecedingLocalDeclarations(
            parent,
            builder);
        if (parent is AkburaDocumentSyntax document)
        {
            AddPrecedingLocalDeclarationsFromList(
                document.Members,
                scope,
                builder);
        }
        else if (parent is CSharpBlockSyntax block)
        {
            AddPrecedingLocalDeclarationsFromList(
                block.Tokens,
                scope,
                builder);
        }
    }

    private static void AddPrecedingLocalDeclarationsFromList<TSyntax>(
        Akbura.Language.Syntax.SyntaxList<TSyntax> members,
        AkburaSyntax scope,
        ImmutableArrayBuilder<CSharp.StatementSyntax> builder)
        where TSyntax : AkburaSyntax
    {
        foreach (var member in members)
        {
            if (member.Position >= scope.Position)
            {
                break;
            }

            if (member is CSharpStatementSyntax statement &&
                statement.GetRawCSharpStatement() is
                    CSharp.LocalDeclarationStatementSyntax localDeclaration)
            {
                builder.Add(localDeclaration);
            }
        }
    }

    internal CSharp.CompilationUnitSyntax CreateComponentProbeCompilationUnit(
        ImmutableArray<CSharp.MemberDeclarationSyntax> members,
        string fallbackTypeName,
        ImmutableArray<CSharp.UsingDirectiveSyntax> usingDirectives = default)
    {
        var componentName = SemanticModel.SyntaxTree.ComponentName;
        var componentTypeInfo = AkburaComponentTypeResolver.Resolve(
            CSharpCompilation,
            SemanticModel.GetAkburaComponentMetadataName(SemanticModel.SyntaxTree));
        CSharp.ClassDeclarationSyntax probeType;
        if (string.IsNullOrWhiteSpace(componentName))
        {
            probeType = CSharpSyntaxFactory.ClassDeclaration(fallbackTypeName)
                .WithMembers(CSharpSyntaxFactory.List(members));
        }
        else
        {
            probeType = CSharpSyntaxFactory.ClassDeclaration(ToCSharpIdentifier(componentName))
                .WithModifiers(CSharpSyntaxFactory.TokenList(
                    CSharpSyntaxFactory.Token(CSharpSyntaxKind.PartialKeyword)))
                .WithMembers(CSharpSyntaxFactory.List(members));
            if (componentTypeInfo.ShouldDeclareAkburaControlBase &&
                componentTypeInfo.AkburaControlType != null)
            {
                var baseType = CSharpSyntaxFactory.ParseTypeName(
                    componentTypeInfo.AkburaControlType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat));
                probeType = probeType.WithBaseList(CSharpSyntaxFactory.BaseList(
                    CSharpSyntaxFactory.SingletonSeparatedList<CSharp.BaseTypeSyntax>(
                        CSharpSyntaxFactory.SimpleBaseType(baseType))));
            }
        }

        if (string.IsNullOrWhiteSpace(componentName) &&
            componentTypeInfo.ShouldDeclareAkburaControlBase &&
            componentTypeInfo.AkburaControlType != null)
        {
            var baseType = CSharpSyntaxFactory.ParseTypeName(
                componentTypeInfo.AkburaControlType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat));
            probeType = probeType.WithBaseList(CSharpSyntaxFactory.BaseList(
                CSharpSyntaxFactory.SingletonSeparatedList<CSharp.BaseTypeSyntax>(
                    CSharpSyntaxFactory.SimpleBaseType(baseType))));
        }

        var compilationUnit = CSharpSyntaxFactory.CompilationUnit()
            .WithExterns(CSharpSyntaxFactory.List(SemanticModel.GetCSharpExternAliases()))
            .WithUsings(CSharpSyntaxFactory.List(
                usingDirectives.IsDefault
                    ? SemanticModel.GetCSharpUsingDirectives()
                    : usingDirectives));
        var namespaceName = SemanticModel.GetAkburaNamespaceText(
            SemanticModel.SyntaxTree.GetRoot(),
            SemanticModel.SyntaxTree);
        if (namespaceName.Length == 0)
        {
            return compilationUnit.WithMembers(
                CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(probeType));
        }

        var namespaceDeclaration = CSharpSyntaxFactory.FileScopedNamespaceDeclaration(
                CSharpSyntaxFactory.ParseName(namespaceName))
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(probeType));
        return compilationUnit.WithMembers(
            CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(namespaceDeclaration));
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
        bool isBindingPath,
        bool suppressTopLevelConversionDiagnostic = false)
    {
        var bindingResult = BindExpression(
            semanticModel,
            expression,
            isBindingPath,
            suppressTopLevelConversionDiagnostic);

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
        bool isBindingPath,
        bool suppressTopLevelConversionDiagnostic = false)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression);
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        var operation = semanticModel.GetOperation(expression);
        var receiverType = GetExpressionReceiverType(semanticModel, expression);
        var diagnostics = GetProbeDiagnostics(
            semanticModel,
            expression,
            suppressTopLevelConversionDiagnostic);
        var typeSymbol = typeInfo.Type ?? typeInfo.ConvertedType;
        if (ContainsErrorType(typeSymbol))
        {
            typeSymbol = null;
        }

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
        SyntaxNode syntax,
        bool suppressTopLevelConversionDiagnostic = false)
    {
        using var builder = ImmutableArrayBuilder<Diagnostic>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var diagnostic in semanticModel.GetDiagnostics(syntax.Span))
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
            {
                continue;
            }

            if (suppressTopLevelConversionDiagnostic &&
                diagnostic.Location.SourceSpan == syntax.Span &&
                diagnostic.Id is "CS0029" or "CS0266")
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

    private static bool ContainsErrorType(ITypeSymbol? type)
    {
        if (type == null)
        {
            return false;
        }

        if (type is IErrorTypeSymbol ||
            type.TypeKind == TypeKind.Error)
        {
            return true;
        }

        return type switch
        {
            IArrayTypeSymbol array =>
                ContainsErrorType(array.ElementType),
            IPointerTypeSymbol pointer =>
                ContainsErrorType(pointer.PointedAtType),
            INamedTypeSymbol named =>
                named.TypeArguments.Any(ContainsErrorType),
            IFunctionPointerTypeSymbol functionPointer =>
                ContainsErrorType(
                    functionPointer.Signature.ReturnType) ||
                functionPointer.Signature.Parameters.Any(
                    static parameter =>
                        ContainsErrorType(parameter.Type)),
            _ => false,
        };
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
