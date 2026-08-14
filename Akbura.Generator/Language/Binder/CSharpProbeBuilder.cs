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
        return CreateStatementProbe(
            scope,
            statement,
            includeAllVisibleSymbols: false);
    }

    private CSharp.CompilationUnitSyntax CreateStatementProbe(
        AkburaSyntax scope,
        CSharp.StatementSyntax statement,
        bool includeAllVisibleSymbols)
    {
        var precedingLocals = GetPrecedingLocalDeclarations(scope);
        var containingMethod = GetContainingComponentMethodProbe(scope);
        var excludedNames = GetParameterNames(containingMethod);
        var analyzedBlock = CSharpProbeBinder.CreateProbeBlock(
            ImmutableArray<CSharp.StatementSyntax>.Empty,
            precedingLocals,
            statement);
        var probeScope = includeAllVisibleSymbols
            ? _binder.CreateCompletionProbeScope(
                scope,
                analyzedBlock,
                excludedNames)
            : _binder.CreateProbeScope(
                scope,
                analyzedBlock,
                excludedNames);
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
        var annotation = new SyntaxAnnotation(
            CompletionAnnotationKind);
        var annotatedExpression = expression
            .WithAdditionalAnnotations(annotation);
        var root = CreateReturnExpressionProbe(
            scope,
            annotatedExpression,
            targetType: null,
            includeAllVisibleSymbols: true);
        return CreateProjection(
            root,
            annotatedExpression,
            annotation,
            relativePosition);
    }

    public CSharpProbeProjection CreateStatementProjection(
        AkburaSyntax scope,
        CSharp.StatementSyntax statement,
        int relativePosition)
    {
        var annotation = new SyntaxAnnotation(
            CompletionAnnotationKind);
        var annotatedStatement = statement
            .WithAdditionalAnnotations(annotation);
        var root = CreateStatementProbe(
            scope,
            annotatedStatement,
            includeAllVisibleSymbols: true);
        return CreateProjection(
            root,
            annotatedStatement,
            annotation,
            relativePosition);
    }

    public CSharpProbeProjection CreateTypeProjection(
        CSharp.TypeSyntax type,
        int relativePosition)
    {
        var annotation = new SyntaxAnnotation(
            CompletionAnnotationKind);
        var annotatedType = type
            .WithAdditionalAnnotations(annotation);
        var declaration = CSharpSyntaxFactory.VariableDeclaration(
                annotatedType)
            .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                CSharpSyntaxFactory.VariableDeclarator(
                    "__akbura_type_probe")));
        var field = CSharpSyntaxFactory.FieldDeclaration(declaration)
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(
                    CSharpSyntaxKind.PrivateKeyword)));
        var root = _binder.CreateComponentProbeCompilationUnit(
            ImmutableArray.Create<CSharp.MemberDeclarationSyntax>(field),
            "__AkburaTypeProbe");
        return CreateProjection(
            root,
            annotatedType,
            annotation,
            relativePosition);
    }

    public CSharpProbeProjection CreateReturnTypeProjection(
        CSharp.TypeSyntax type,
        int relativePosition)
    {
        var annotation = new SyntaxAnnotation(
            CompletionAnnotationKind);
        var annotatedType = type
            .WithAdditionalAnnotations(annotation);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                annotatedType,
                "__akbura_return_type_probe")
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(
                    CSharpSyntaxKind.PrivateKeyword)))
            .WithBody(CSharpSyntaxFactory.Block(
                CSharpSyntaxFactory.ThrowStatement(
                    CSharpSyntaxFactory.LiteralExpression(
                        CSharpSyntaxKind.NullLiteralExpression))));
        var root = _binder.CreateComponentProbeCompilationUnit(
            ImmutableArray.Create<CSharp.MemberDeclarationSyntax>(method),
            "__AkburaReturnTypeProbe");
        return CreateProjection(
            root,
            annotatedType,
            annotation,
            relativePosition);
    }

    public CSharpProbeProjection CreateUsingDirectiveProjection(
        UsingDirectiveSyntax usingSyntax,
        int relativePosition)
    {
        var annotation = new SyntaxAnnotation(
            CompletionAnnotationKind);
        var type = usingSyntax.Name.ToCSharp();
        var annotatedType = type
            .WithAdditionalAnnotations(annotation);
        var unannotatedUsing = usingSyntax.ToCSharp();
        var currentUsing = CSharpSyntaxFactory.UsingDirective(
            unannotatedUsing.GlobalKeyword,
            unannotatedUsing.UsingKeyword,
            unannotatedUsing.StaticKeyword,
            unannotatedUsing.UnsafeKeyword,
            unannotatedUsing.Alias,
            annotatedType,
            unannotatedUsing.SemicolonToken);
        var usingDirectives = _binder.SemanticModel
            .GetCSharpUsingDirectivesBefore(usingSyntax)
            .Add(currentUsing);
        var root = _binder.CreateComponentProbeCompilationUnit(
            ImmutableArray<CSharp.MemberDeclarationSyntax>.Empty,
            "__AkburaUsingProbe",
            usingDirectives);
        return CreateProjection(
            root,
            annotatedType,
            annotation,
            relativePosition);
    }

    public CSharpProbeProjection CreateCommandParameterProjection(
        CommandDeclarationSyntax command,
        CSharp.ParameterListSyntax parameters,
        int relativePosition)
    {
        var annotation = new SyntaxAnnotation(
            CompletionAnnotationKind);
        var hostOffset = command.Parameters.Parameters.FullSpan.Start;
        var parametersWithOrigins = parameters.ReplaceNodes(
            parameters.Parameters,
            (original, _) => original.WithAdditionalAnnotations(
                new SyntaxAnnotation(
                    CSharpProbeBinder.ProjectedSymbolAnnotationKind,
                    new CSharpProbeSymbolOrigin(
                        Guid.NewGuid().ToString("N"),
                        Akbura.Language.Symbols.SymbolKind.CommandParameter,
                        original.Identifier.ValueText,
                        new TextSpan(
                            hostOffset + original.Identifier.Span.Start,
                            original.Identifier.Span.Length))
                    .Serialize())));
        var annotatedParameters = parametersWithOrigins
            .WithAdditionalAnnotations(annotation);
        CSharp.TypeSyntax returnType;
        try
        {
            returnType = command.ReturnType.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            returnType = CSharpSyntaxFactory.PredefinedType(
                CSharpSyntaxFactory.Token(
                    CSharpSyntaxKind.VoidKeyword));
        }
        catch (ArgumentException)
        {
            returnType = CSharpSyntaxFactory.PredefinedType(
                CSharpSyntaxFactory.Token(
                    CSharpSyntaxKind.VoidKeyword));
        }
        catch (InvalidCastException)
        {
            returnType = CSharpSyntaxFactory.PredefinedType(
                CSharpSyntaxFactory.Token(
                    CSharpSyntaxKind.VoidKeyword));
        }

        var method = CSharpSyntaxFactory.MethodDeclaration(
                returnType,
                "__akbura_command_probe")
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(
                    CSharpSyntaxKind.PrivateKeyword)))
            .WithParameterList(annotatedParameters)
            .WithBody(CSharpSyntaxFactory.Block());
        var root = _binder.CreateComponentProbeCompilationUnit(
            ImmutableArray.Create<CSharp.MemberDeclarationSyntax>(method),
            "__AkburaCommandProbe");
        return CreateProjection(
            root,
            annotatedParameters,
            annotation,
            relativePosition);
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

    private static CSharpProbeProjection CreateProjection<TNode>(
        CSharp.CompilationUnitSyntax root,
        TNode sourceNode,
        SyntaxAnnotation annotation,
        int relativePosition)
        where TNode : SyntaxNode
    {
        if (relativePosition < 0 ||
            relativePosition > sourceNode.FullSpan.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativePosition));
        }

        var normalizedRoot = root.NormalizeWhitespace();
        var normalizedNode = normalizedRoot
            .GetAnnotatedNodes(annotation)
            .OfType<TNode>()
            .Single();
        root = normalizedRoot.ReplaceNode(
            normalizedNode,
            sourceNode);
        var projectedNode = root
            .GetAnnotatedNodes(annotation)
            .OfType<TNode>()
            .Single();
        var stateNames = root
            .GetAnnotatedNodes(
                CSharpProbeBinder.StateCompletionAnnotationKind)
            .OfType<CSharp.VariableDeclaratorSyntax>()
            .Select(static declarator =>
                declarator.Identifier.ValueText)
            .Where(static name =>
                !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        using var symbolOrigins =
            ImmutableArrayBuilder<CSharpProbeSymbolOrigin>.Rent();
        foreach (var node in root.GetAnnotatedNodes(
                     CSharpProbeBinder.ProjectedSymbolAnnotationKind))
        {
            foreach (var symbolAnnotation in node.GetAnnotations(
                         CSharpProbeBinder.ProjectedSymbolAnnotationKind))
            {
                if (CSharpProbeSymbolOrigin.TryParse(
                        symbolAnnotation.Data,
                        out var origin))
                {
                    symbolOrigins.Add(origin);
                }
            }
        }

        return new CSharpProbeProjection(
            root,
            projectedNode.FullSpan,
            projectedNode.FullSpan.Start + relativePosition,
            stateNames,
            annotation,
            symbolOrigins.ToImmutable());
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
                var hostOffset = statement.Tokens.FullSpan.Start;
                builder.Add(localDeclaration.ReplaceNodes(
                    localDeclaration.Declaration.Variables,
                    (original, _) => original.WithAdditionalAnnotations(
                        new SyntaxAnnotation(
                            CSharpProbeBinder.ProjectedSymbolAnnotationKind,
                            new CSharpProbeSymbolOrigin(
                                Guid.NewGuid().ToString("N"),
                                Akbura.Language.Symbols.SymbolKind.CSharpSymbol,
                                original.Identifier.ValueText,
                                new TextSpan(
                                    hostOffset + original.Identifier.Span.Start,
                                    original.Identifier.Span.Length))
                            .Serialize()))));
            }
        }
    }
}

internal readonly struct CSharpProbeProjection
{
    public CSharpProbeProjection(
        CSharp.CompilationUnitSyntax root,
        TextSpan projectedSpan,
        int projectedPosition,
        ImmutableArray<string> stateNames,
        SyntaxAnnotation activeAnnotation,
        ImmutableArray<CSharpProbeSymbolOrigin> symbolOrigins)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        ProjectedSpan = projectedSpan;
        ProjectedPosition = projectedPosition;
        StateNames = stateNames.IsDefault
            ? ImmutableArray<string>.Empty
            : stateNames;
        ActiveAnnotation = activeAnnotation ??
            throw new ArgumentNullException(nameof(activeAnnotation));
        SymbolOrigins = symbolOrigins.IsDefault
            ? ImmutableArray<CSharpProbeSymbolOrigin>.Empty
            : symbolOrigins;
    }

    public CSharp.CompilationUnitSyntax Root { get; }

    public TextSpan ProjectedSpan { get; }

    public int ProjectedPosition { get; }

    public ImmutableArray<string> StateNames { get; }

    public SyntaxAnnotation ActiveAnnotation { get; }

    public ImmutableArray<CSharpProbeSymbolOrigin> SymbolOrigins { get; }
}
