using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class CSharpProbeBuilder
{
    private const string CompletionAnnotationKind =
        "AkburaCSharpCompletionTarget";

    private readonly CSharpProbeBinder _binder;

    public CSharpProbeBuilder(CSharpProbeBinder binder)
    {
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
    }

    public CSharp.CompilationUnitSyntax CreateReturnExpressionProbe(
        AkburaSyntax scope,
        CSharp.ExpressionSyntax expression,
        ITypeSymbol? targetType)
    {
        return CreateReturnExpressionProbe(
            scope,
            expression,
            targetType,
            includeAllVisibleSymbols: false);
    }

    public CSharp.CompilationUnitSyntax CreateStatementProbe(
        AkburaSyntax scope,
        CSharp.StatementSyntax statement)
    {
        var precedingLocals = GetPrecedingLocalDeclarations(scope);
        var analyzedBlock = CSharpProbeBinder.CreateProbeBlock(
            ImmutableArray<CSharp.StatementSyntax>.Empty,
            precedingLocals,
            statement);
        var containingMethod = GetContainingComponentMethodProbe(scope);
        var probeScope = _binder.CreateProbeScope(
            scope,
            analyzedBlock,
            GetParameterNames(containingMethod));
        var method = CSharpSyntaxFactory.MethodDeclaration(
                containingMethod?.ReturnType ??
                    CSharpSyntaxFactory.PredefinedType(
                        CSharpSyntaxFactory.Token(CSharpSyntaxKind.VoidKeyword)),
                "__akbura_statement_probe")
            .WithBody(CSharpProbeBinder.CreateProbeBlock(
                probeScope.LocalStatements,
                precedingLocals,
                statement));
        method = ApplyContainingMethodContext(method, containingMethod);
        return _binder.CreateComponentProbeCompilationUnit(
            CSharpProbeBinder.AddProbeMethod(
                probeScope.MemberDeclarations,
                method),
            "__AkburaStatementProbe");
    }

    public CSharpProbeProjection CreateExpressionProjection(
        AkburaSyntax scope,
        CSharp.ExpressionSyntax expression,
        int relativePosition)
    {
        if (relativePosition < 0 ||
            relativePosition > expression.FullSpan.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativePosition));
        }

        var annotation = new SyntaxAnnotation(
            CompletionAnnotationKind);
        var annotatedExpression = expression
            .WithAdditionalAnnotations(annotation);
        var root = CreateReturnExpressionProbe(
            scope,
            annotatedExpression,
            targetType: null,
            includeAllVisibleSymbols: true);
        var normalizedRoot = root.NormalizeWhitespace();
        var normalizedExpression = normalizedRoot
            .GetAnnotatedNodes(annotation)
            .OfType<CSharp.ExpressionSyntax>()
            .Single();
        root = normalizedRoot.ReplaceNode(
            normalizedExpression,
            annotatedExpression);
        var projectedExpression = root
            .GetAnnotatedNodes(annotation)
            .OfType<CSharp.ExpressionSyntax>()
            .Single();
        var projectedSpan = projectedExpression.FullSpan;

        return new CSharpProbeProjection(
            root,
            projectedSpan,
            projectedSpan.Start + relativePosition);
    }

    private CSharp.CompilationUnitSyntax CreateReturnExpressionProbe(
        AkburaSyntax scope,
        CSharp.ExpressionSyntax expression,
        ITypeSymbol? targetType,
        bool includeAllVisibleSymbols)
    {
        var precedingLocals = GetPrecedingLocalDeclarations(scope);
        var containingMethod = GetContainingComponentMethodProbe(scope);
        var excludedNames = GetParameterNames(containingMethod);
        var probeScope = includeAllVisibleSymbols
            ? _binder.CreateCompletionProbeScope(
                scope,
                expression,
                excludedNames)
            : _binder.CreateProbeScope(
                scope,
                expression,
                excludedNames);
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expression);
        var returnType = targetType == null
            ? CSharpSyntaxFactory.PredefinedType(
                CSharpSyntaxFactory.Token(CSharpSyntaxKind.ObjectKeyword))
            : CSharpSyntaxFactory.ParseTypeName(
                targetType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat));
        var method = CSharpSyntaxFactory.MethodDeclaration(
                returnType,
                "__akbura_probe")
            .WithBody(CSharpProbeBinder.CreateProbeBlock(
                probeScope.LocalStatements,
                precedingLocals,
                returnStatement));
        method = ApplyContainingMethodContext(method, containingMethod);
        return _binder.CreateComponentProbeCompilationUnit(
            CSharpProbeBinder.AddProbeMethod(
                probeScope.MemberDeclarations,
                method),
            "__AkburaProbe");
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
            .WithModifiers(FilterProbeMethodModifiers(
                containingMethod.Modifiers))
            .WithTypeParameterList(containingMethod.TypeParameterList)
            .WithParameterList(containingMethod.ParameterList)
            .WithConstraintClauses(containingMethod.ConstraintClauses);
    }

    private static Microsoft.CodeAnalysis.SyntaxTokenList
        FilterProbeMethodModifiers(
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
            .Select(static parameter =>
                parameter.Identifier.ValueText)
            .Where(static name =>
                !string.IsNullOrWhiteSpace(name))
            .ToImmutableArray();
    }

    private static CSharp.MethodDeclarationSyntax?
        GetContainingComponentMethodProbe(
            AkburaSyntax scope)
    {
        for (var current = scope.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is CSharpStatementSyntax statement &&
                statement.Parent is AkburaDocumentSyntax &&
                CSharpProbeBinder.TryCreateComponentMethodProbe(
                    statement,
                    out var method))
            {
                return method;
            }
        }

        return null;
    }

    private static ImmutableArray<CSharp.StatementSyntax>
        GetPrecedingLocalDeclarations(
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
}

internal readonly struct CSharpProbeProjection
{
    public CSharpProbeProjection(
        CSharp.CompilationUnitSyntax root,
        TextSpan projectedSpan,
        int projectedPosition)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        ProjectedSpan = projectedSpan;
        ProjectedPosition = projectedPosition;
    }

    public CSharp.CompilationUnitSyntax Root { get; }

    public TextSpan ProjectedSpan { get; }

    public int ProjectedPosition { get; }
}
