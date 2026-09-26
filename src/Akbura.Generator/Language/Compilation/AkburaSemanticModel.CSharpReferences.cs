using Akbura.Language.Binder;
using Akbura.Language.Symbols;
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
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    private const string MarkupInlineReferenceProbeMethodName = "__AkburaMarkupInlineReferenceProbe";

    public ImmutableArray<CSharpSymbolReference> GetCSharpSymbolReferences(StateDeclarationSyntax declaration)
    {
        if (declaration == null)
        {
            throw new ArgumentNullException(nameof(declaration));
        }

        ValidateSyntaxTreeOwnership(declaration);
        if (GetDeclaredSymbol(declaration) is not IStateSymbol state ||
            !EmbeddedCSharpSyntaxFacts.TryGetExpression(
                state.InitializerExpression,
                out var expression,
                out var hostSpan))
        {
            return [];
        }

        var targetType = state.UseHook == null &&
            state.HasExplicitType &&
            state.BindingKind == StateBindingKind.None
                ? state.Type.Symbol as ITypeSymbol
                : null;
        var binder = BindingSession.GetCSharpProbeBinder(
            declaration,
            BinderUsage.Expression);
        var compilationUnit = new CSharpProbeBuilder(binder)
            .CreateReturnExpressionProbe(
                declaration,
                expression,
                targetType);
        var semanticModel = CreateReferenceProbeSemanticModel(
            compilationUnit,
            out var syntaxTree);
        var probeExpression = CSharpProbeBuilder.GetReturnProbeExpression(
            syntaxTree.GetCompilationUnitRoot());
        if (probeExpression == null)
        {
            return [];
        }

        var akburaSymbolsByName = new Dictionary<string, AkburaSymbol>(
            StringComparer.Ordinal);
        var akburaSymbolsByCommandTypeName = new Dictionary<string, AkburaSymbol>(
            StringComparer.Ordinal);
        AddCSharpProbeRootSymbolMappings(
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);

        var sourcePositionOffset = hostSpan.Start - expression.FullSpan.Start;
        var references = CollectCSharpSymbolReferences(
            semanticModel,
            [
                new CSharpReferenceTarget(
                    expression,
                    probeExpression,
                    sourcePositionOffset),
            ],
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);
        return AddUseHookReference(
            state.UseHook,
            expression,
            sourcePositionOffset,
            references);
    }

    public ImmutableArray<CSharpSymbolReference> GetCSharpSymbolReferences(CSharpStatementSyntax statementSyntax)
    {
        if (statementSyntax == null)
        {
            throw new ArgumentNullException(nameof(statementSyntax));
        }

        ValidateSyntaxTreeOwnership(statementSyntax);

        var statement = ParseCSharpStatement(statementSyntax);
        if (statement == null)
        {
            return [];
        }

        var akburaSymbolsByName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        var akburaSymbolsByCommandTypeName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        AddCSharpProbeRootSymbolMappings(
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);
        AddMarkupScopeSymbolMappings(
            statementSyntax,
            akburaSymbolsByName);

        var binder = BindingSession.GetCSharpProbeBinder(
            statementSyntax,
            BinderUsage.Expression);
        var compilationUnit = new CSharpProbeBuilder(binder)
            .CreateStatementProbe(
                statementSyntax,
                statement);

        var semanticModel = CreateReferenceProbeSemanticModel(compilationUnit, out var syntaxTree);
        var probeStatement = syntaxTree
            .GetCompilationUnitRoot()
            .GetAnnotatedNodes(
                CSharpProbeBuilder.StatementProbeAnnotationKind)
            .OfType<CSharp.StatementSyntax>()
            .Single();

        var sourcePositionOffset =
            statementSyntax.Tokens.FullSpan.Start -
            statement.FullSpan.Start;

        var references = CollectCSharpSymbolReferences(
            semanticModel,
            [
                new CSharpReferenceTarget(
                    statement,
                    probeStatement,
                    sourcePositionOffset)
            ],
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);

        return AddUseHookReference(
            statementSyntax,
            statement,
            sourcePositionOffset,
            references);
    }

    private ImmutableArray<CSharpSymbolReference> AddUseHookReference(
        CSharpStatementSyntax statementSyntax,
        CSharp.StatementSyntax statement,
        int sourcePositionOffset,
        ImmutableArray<CSharpSymbolReference> references)
    {
        if (statement is not CSharp.ExpressionStatementSyntax { Expression: { } expression })
        {
            return references;
        }

        return AddUseHookReference(
            GetSymbolInfo(statementSyntax).Symbol as IUseHookSymbol,
            expression,
            sourcePositionOffset,
            references);
    }

    private static ImmutableArray<CSharpSymbolReference> AddUseHookReference(
        IUseHookSymbol? hook,
        CSharp.ExpressionSyntax expression,
        int sourcePositionOffset,
        ImmutableArray<CSharpSymbolReference> references)
    {
        if (hook == null ||
            expression is not CSharp.InvocationExpressionSyntax invocation ||
            !TryGetInvocationName(
                invocation.Expression,
                out var name))
        {
            return references;
        }

        var sourceSpan =
            new TextSpan(
                sourcePositionOffset +
                name.Identifier.Span.Start,
                name.Identifier.Span.Length);

        foreach (var reference in references)
        {
            if (reference.SourceSpan ==
                sourceSpan)
            {
                return references;
            }
        }

        return references.Add(
            new CSharpSymbolReference(
                name,
                sourceSpan,
                new CSharpSymbolDefinition(
                    hook.Method),
                hook,
                name.Identifier.ValueText));
    }

    private static bool TryGetInvocationName(
        CSharp.ExpressionSyntax expression,
        out CSharp.SimpleNameSyntax name)
    {
        switch (expression)
        {
            case CSharp.SimpleNameSyntax simpleName:
                name = simpleName;
                return true;

            case CSharp.MemberAccessExpressionSyntax memberAccess:
                name = memberAccess.Name;
                return true;

            default:
                name = null!;
                return false;
        }
    }

    public ImmutableArray<CSharpSymbolReference> GetCSharpSymbolReferences(MarkupAttributeSyntax markupAttribute)
    {
        if (markupAttribute == null)
        {
            throw new ArgumentNullException(nameof(markupAttribute));
        }

        ValidateSyntaxTreeOwnership(markupAttribute);

        if (markupAttribute.Kind ==
            AkburaSyntaxKind.TailwindFullAttributeSyntax)
        {
            var tailwindAttribute =
                Unsafe.As<TailwindFullAttributeSyntax>(markupAttribute);
            using var builder =
                ImmutableArrayBuilder<CSharpSymbolReference>.Rent();

            if (tailwindAttribute.Prefix?.Kind ==
                AkburaSyntaxKind.ExpressionConditionalPrefixSyntax)
            {
                builder.AddRange(GetCSharpSymbolReferences(
                    Unsafe.As<ExpressionConditionalPrefixSyntax>(
                            tailwindAttribute.Prefix)
                        .Expression));
            }
            else if (tailwindAttribute.Prefix?.Kind ==
                AkburaSyntaxKind.MarkupExtensionConditionalPrefixSyntax)
            {
                AddMarkupExtensionCSharpSymbolReferences(
                    Unsafe.As<MarkupExtensionConditionalPrefixSyntax>(
                            tailwindAttribute.Prefix)
                        .Extension,
                    builder);
            }

            foreach (var segment in tailwindAttribute.Segments)
            {
                switch (segment.Kind)
                {
                    case AkburaSyntaxKind.TailwindExpressionSegmentSyntax:
                        builder.AddRange(GetCSharpSymbolReferences(
                            Unsafe.As<TailwindExpressionSegmentSyntax>(
                                    segment)
                                .Expression));
                        break;

                    case AkburaSyntaxKind.TailwindMarkupExtensionSegmentSyntax:
                        AddMarkupExtensionCSharpSymbolReferences(
                            Unsafe.As<TailwindMarkupExtensionSegmentSyntax>(
                                    segment)
                                .Extension,
                            builder);
                        break;
                }
            }

            return builder.ToImmutable();
        }

        var value = GetMarkupAttributeValue(markupAttribute);
        if (value == null)
        {
            return [];
        }

        if (value.Kind == AkburaSyntaxKind.MarkupDynamicAttributeValueSyntax)
        {
            var dynamicValue = Unsafe.As<MarkupDynamicAttributeValueSyntax>(value);
            return GetCSharpSymbolReferences(dynamicValue.Expression);
        }

        if (value.Kind == AkburaSyntaxKind.MarkupExtensionAttributeValueSyntax)
        {
            var extensionValue = Unsafe.As<MarkupExtensionAttributeValueSyntax>(value);
            using var builder = ImmutableArrayBuilder<CSharpSymbolReference>.Rent();
            AddMarkupExtensionCSharpSymbolReferences(extensionValue.Extension, builder);
            return builder.ToImmutable();
        }

        return [];
    }

    private void AddMarkupExtensionCSharpSymbolReferences(
        MarkupExtensionSyntax extensionSyntax,
        ImmutableArrayBuilder<CSharpSymbolReference> builder)
    {
        foreach (var argument in extensionSyntax.Arguments)
        {
            var value = argument.Kind switch
            {
                AkburaSyntaxKind.MarkupExtensionPositionalArgumentSyntax => Unsafe.As<MarkupExtensionPositionalArgumentSyntax>(argument).Value,
                AkburaSyntaxKind.MarkupExtensionPropertyArgumentSyntax => Unsafe.As<MarkupExtensionPropertyArgumentSyntax>(argument).Value,
                _ => null,
            };

            if (value == null)
            {
                continue;
            }

            switch (value.Kind)
            {
                case AkburaSyntaxKind.MarkupExtensionExpressionValueSyntax:
                    builder.AddRange(GetCSharpSymbolReferences(Unsafe.As<MarkupExtensionExpressionValueSyntax>(value).Expression));
                    break;

                case AkburaSyntaxKind.MarkupExtensionNestedValueSyntax:
                    AddMarkupExtensionCSharpSymbolReferences(
                        Unsafe.As<MarkupExtensionNestedValueSyntax>(value).Extension,
                        builder);
                    break;
            }
        }
    }

    public ImmutableArray<CSharpSymbolReference> GetCSharpSymbolReferences(InlineExpressionSyntax inlineExpressionSyntax)
    {
        if (inlineExpressionSyntax == null)
        {
            throw new ArgumentNullException(nameof(inlineExpressionSyntax));
        }

        ValidateSyntaxTreeOwnership(inlineExpressionSyntax);

        var expression = ParseInlineExpression(inlineExpressionSyntax);
        if (expression == null)
        {
            return [];
        }

        var sourcePositionOffset = inlineExpressionSyntax
                .Expression
                .Tokens
                .FullSpan
                .Start -
            expression.FullSpan.Start;

        if (CSharpProbeBuilder.HasMarkupConditionalScope(inlineExpressionSyntax))
        {
            return GetConnectedMarkupExpressionReferences(inlineExpressionSyntax, expression, sourcePositionOffset);
        }

        return TryGetContainingMarkupAttribute(inlineExpressionSyntax, out var markupAttribute)
            ? GetMarkupInlineExpressionCSharpSymbolReferences(markupAttribute, expression, sourcePositionOffset)
            : GetMarkupExpressionCSharpSymbolReferences(inlineExpressionSyntax, expression, sourcePositionOffset);
    }

    private ImmutableArray<CSharpSymbolReference> GetMarkupInlineExpressionCSharpSymbolReferences(
        MarkupAttributeSyntax markupAttribute,
        CSharp.ExpressionSyntax expression,
        int sourcePositionOffset)
    {
        var attributeSymbol = GetSymbolInfo(markupAttribute).Symbol;
        var isHandler = attributeSymbol is IRoutedEventSymbol ||
            attributeSymbol is Symbols.IPropertySymbol { Command: not null } ||
            IsICommandProperty(attributeSymbol);
        ImmutableArray<string> parameterNames;
        bool isAsync;
        SyntaxNode referenceNode;
        if (isHandler)
        {
            referenceNode = GetMarkupHandlerReferenceNode(expression, out parameterNames, out isAsync);
        }
        else
        {
            referenceNode = expression;
            parameterNames = [];
            isAsync = ContainsAwaitExpression(expression);
        }

        var probeScope = CreateMarkupHandlerProbeScope(
            markupAttribute,
            referenceNode,
            parameterNames);

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        var method = CreateMarkupInlineReferenceProbeMethod(
            markupAttribute,
            attributeSymbol,
            expression,
            referenceNode,
            probeScope.LocalStatements,
            parameterNames,
            isAsync);

        membersBuilder.Add(method);

        var compilationUnit = BindingSession
            .GetCSharpProbeBinder(GetMarkupBindingScope(markupAttribute), BinderUsage.Markup)
            .CreateComponentProbeCompilationUnit(
                membersBuilder.ToImmutable(),
                "__AkburaSemanticProbe");

        var semanticModel = CreateReferenceProbeSemanticModel(compilationUnit, out var syntaxTree);

        var probeMethod = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.MethodDeclarationSyntax>()
            .Single(methodDeclaration => methodDeclaration.Identifier.ValueText == MarkupInlineReferenceProbeMethodName);

        var targets = GetMarkupInlineReferenceTargets(
            probeMethod,
            referenceNode,
            probeScope.LocalStatements.Length,
            sourcePositionOffset);

        var akburaSymbolsByName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        var akburaSymbolsByCommandTypeName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);

        AddCSharpProbeRootSymbolMappings(akburaSymbolsByName, akburaSymbolsByCommandTypeName);
        AddMarkupScopeSymbolMappings(markupAttribute, akburaSymbolsByName);

        return CollectCSharpSymbolReferences(
            semanticModel,
            targets,
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);
    }

    private ImmutableArray<CSharpSymbolReference> GetMarkupExpressionCSharpSymbolReferences(
        AkburaSyntax scopeSyntax,
        CSharp.ExpressionSyntax expression,
        int sourcePositionOffset)
    {
        var scope = GetMarkupBindingScope(scopeSyntax);
        var probeBinder = BindingSession.GetCSharpProbeBinder(scope, BinderUsage.Markup);
        var probeScope = probeBinder.CreateProbeScope(scope, expression);
        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        var method = CSharpSyntaxFactory.MethodDeclaration(
                ContainsAwaitExpression(expression)
                    ? CSharpSyntaxFactory.ParseTypeName("global::System.Threading.Tasks.Task<object>")
                    : CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                MarkupInlineReferenceProbeMethodName)
            .WithBody(CreateMarkupHandlerProbeBlock(
                probeScope.LocalStatements,
                CSharpSyntaxFactory.ReturnStatement(expression)));
        if (ContainsAwaitExpression(expression))
        {
            method = method.WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsyncKeyword)));
        }

        membersBuilder.Add(method);
        var compilationUnit = probeBinder.CreateComponentProbeCompilationUnit(
            membersBuilder.ToImmutable(),
            "__AkburaSemanticProbe");
        var semanticModel = CreateReferenceProbeSemanticModel(compilationUnit, out var syntaxTree);
        var targetExpression = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.MethodDeclarationSyntax>()
            .Single(methodDeclaration => methodDeclaration.Identifier.ValueText == MarkupInlineReferenceProbeMethodName)
            .Body!
            .Statements
            .OfType<CSharp.ReturnStatementSyntax>()
            .Single()
            .Expression;
        if (targetExpression == null)
        {
            return [];
        }

        var akburaSymbolsByName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        var akburaSymbolsByCommandTypeName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        AddCSharpProbeRootSymbolMappings(akburaSymbolsByName, akburaSymbolsByCommandTypeName);
        AddMarkupScopeSymbolMappings(scopeSyntax, akburaSymbolsByName);

        return CollectCSharpSymbolReferences(
            semanticModel,
            [
                new CSharpReferenceTarget(
                    expression,
                    targetExpression,
                    sourcePositionOffset)
            ],
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);
    }

    private Microsoft.CodeAnalysis.SemanticModel CreateReferenceProbeSemanticModel(
        CSharp.CompilationUnitSyntax compilationUnit,
        out SyntaxTree syntaxTree)
    {
        var parseOptions = Compilation.CSharpCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ??
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        syntaxTree = CSharpSyntaxTree.Create(compilationUnit, parseOptions);
        var probeCompilation =
            Compilation.CSharpProbeCompilation
                .AddSyntaxTrees(syntaxTree);
        return probeCompilation.GetSemanticModel(syntaxTree);
    }

    private ImmutableArray<CSharpSymbolReference> CollectCSharpSymbolReferences(
        SemanticModel semanticModel,
        IEnumerable<CSharpReferenceTarget> targets,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        using var references =
            ImmutableArrayBuilder<
                CSharpSymbolReference>.Rent();

        var seenReferences =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (var target in targets)
        {
            foreach (var name in target.ProbeNode.DescendantNodesAndSelf().OfType<CSharp.SimpleNameSyntax>())
            {
                if (IsVarTypeName(name))
                {
                    continue;
                }

                var symbolInfo = GetReferenceSymbolInfo(semanticModel, name);

                AddCSharpSymbolReference(
                    semanticModel,
                    references,
                    seenReferences,
                    name,
                    target.MapToSource(
                        name.Identifier.Span),
                    symbolInfo,
                    akburaSymbolsByName,
                    akburaSymbolsByCommandTypeName);
            }
        }

        return references.ToImmutable();
    }

    private static CSharpReferenceSymbolInfo GetReferenceSymbolInfo(SemanticModel semanticModel, CSharp.SimpleNameSyntax name)
    {
        if (name.Parent is
                CSharp.MemberAccessExpressionSyntax
                memberAccess &&
            ReferenceEquals(
                memberAccess.Name,
                name))
        {
            var memberInfo =
                GetBestSymbolInfo(
                    semanticModel,
                    memberAccess);

            if (memberInfo.Symbol == null &&
                memberAccess.Parent is
                    CSharp.InvocationExpressionSyntax invocation &&
                ReferenceEquals(
                    invocation.Expression,
                    memberAccess))
            {
                var invocationInfo = GetBestSymbolInfo(
                    semanticModel,
                    invocation);
                return PreferResolvedSymbol(
                    memberInfo,
                    invocationInfo);
            }

            return memberInfo;
        }

        if (name.Parent is
                CSharp.InvocationExpressionSyntax invocationExpression &&
            ReferenceEquals(
                invocationExpression.Expression,
                name))
        {
            var nameInfo = GetBestSymbolInfo(
                semanticModel,
                name);
            var invocationInfo = GetBestSymbolInfo(
                semanticModel,
                invocationExpression);
            return PreferResolvedSymbol(
                nameInfo,
                invocationInfo);
        }

        return GetBestSymbolInfo(
            semanticModel,
            name);
    }

    private static bool IsVarTypeName(CSharp.SimpleNameSyntax name)
    {
        if (name is not
                CSharp.IdentifierNameSyntax identifier ||
                CSharpSyntaxFacts.GetContextualKeywordKind(identifier.Identifier.ValueText) !=
            Microsoft.CodeAnalysis.CSharp
                .SyntaxKind.VarKeyword)
        {
            return false;
        }

        return identifier.Parent switch
        {
            CSharp.VariableDeclarationSyntax declaration =>
                declaration.Type.Span ==
                identifier.Span,

            CSharp.ForEachStatementSyntax statement =>
                statement.Type.Span ==
                identifier.Span,

            CSharp.DeclarationExpressionSyntax declaration =>
                declaration.Type.Span ==
                identifier.Span,

            _ => false,
        };
    }

    private CSharp.MethodDeclarationSyntax CreateMarkupInlineReferenceProbeMethod(
        MarkupAttributeSyntax markupAttribute,
        AkburaSymbol? attributeSymbol,
        CSharp.ExpressionSyntax handlerExpression,
        SyntaxNode referenceNode,
        ImmutableArray<CSharp.StatementSyntax> localStatements,
        ImmutableArray<string> parameterNames,
        bool isAsync)
    {
        var returnType = isAsync
            ? CSharpSyntaxFactory.ParseTypeName("global::System.Threading.Tasks.Task<object>")
            : CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword));
        if (IsMarkupDictionaryKeyDirective(markupAttribute) &&
            attributeSymbol is Symbols.IPropertySymbol { Type.Symbol: ITypeSymbol keyType })
        {
            var keyTypeName = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            returnType = CSharpSyntaxFactory.ParseTypeName(isAsync
                ? "global::System.Threading.Tasks.Task<" + keyTypeName + ">"
                : keyTypeName);
        }

        var method = CSharpSyntaxFactory.MethodDeclaration(returnType, MarkupInlineReferenceProbeMethodName)
            .WithParameterList(CreateMarkupInlineReferenceProbeParameterList(
                markupAttribute,
                attributeSymbol,
                handlerExpression,
                parameterNames));

        if (referenceNode is CSharp.BlockSyntax block)
        {
            method = method.WithBody(PrependMarkupHandlerProbeLocals(block, localStatements));
        }
        else
        {
            method = method.WithBody(CreateMarkupHandlerProbeBlock(
                localStatements,
                CSharpSyntaxFactory.ReturnStatement((CSharp.ExpressionSyntax)referenceNode)));
        }

        return isAsync
            ? method.WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsyncKeyword)))
            : method;
    }

    private CSharp.ParameterListSyntax CreateMarkupInlineReferenceProbeParameterList(
        MarkupAttributeSyntax markupAttribute,
        AkburaSymbol? attributeSymbol,
        CSharp.ExpressionSyntax handlerExpression,
        ImmutableArray<string> parameterNames)
    {
        if (attributeSymbol is IRoutedEventSymbol routedEvent)
        {
            return CreateEventHandlerProbeParameterList(routedEvent, parameterNames);
        }

        if (attributeSymbol is Symbols.IPropertySymbol { Command: { } command })
        {
            return CreateCommandHandlerProbeParameterList(
                command.Parameters.Select(static parameter => parameter.Type).ToImmutableArray(),
                parameterNames);
        }

        if (IsICommandProperty(attributeSymbol))
        {
            var handler = AnalyzeMarkupICommandHandler(markupAttribute, handlerExpression);
            return CreateCommandHandlerProbeParameterList(handler.ParameterTypes, parameterNames);
        }

        return CSharpSyntaxFactory.ParameterList();
    }

    private bool IsICommandProperty(AkburaSymbol? symbol)
    {
        return symbol is Symbols.IPropertySymbol { Command: null, Type.Symbol: INamedTypeSymbol type } &&
            IsSystemWindowsInputICommand(type);
    }

    internal bool IsSystemWindowsInputICommand(ITypeSymbol? type)
    {
        var contract = Compilation.CSharpCompilation.GetTypeByMetadataName(
            "System.Windows.Input.ICommand");
        return type != null &&
            contract != null &&
            SymbolEqualityComparer.Default.Equals(type, contract);
    }

    private static SyntaxNode GetMarkupHandlerReferenceNode(
        CSharp.ExpressionSyntax expression,
        out ImmutableArray<string> parameterNames,
        out bool isAsync)
    {
        switch (expression)
        {
            case CSharp.ParenthesizedLambdaExpressionSyntax lambda:
                parameterNames = [.. lambda.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText)];
                isAsync = lambda.AsyncKeyword.RawKind != 0 || ContainsAwaitExpression(lambda.Body);
                return lambda.Body;

            case CSharp.SimpleLambdaExpressionSyntax lambda:
                parameterNames = [lambda.Parameter.Identifier.ValueText];
                isAsync = lambda.AsyncKeyword.RawKind != 0 || ContainsAwaitExpression(lambda.Body);
                return lambda.Body;

            case CSharp.AnonymousMethodExpressionSyntax anonymousMethod:
                parameterNames = anonymousMethod.ParameterList?.Parameters
                    .Select(static parameter => parameter.Identifier.ValueText)
                    .ToImmutableArray() ?? [];
                isAsync = anonymousMethod.AsyncKeyword.RawKind != 0 || ContainsAwaitExpression(anonymousMethod.Body);
                return anonymousMethod.Body;

            default:
                parameterNames = [];
                isAsync = ContainsAwaitExpression(expression);
                return expression;
        }
    }

    private static ImmutableArray<CSharpReferenceTarget> GetMarkupInlineReferenceTargets(
        CSharp.MethodDeclarationSyntax probeMethod,
        SyntaxNode sourceReferenceNode,
        int generatedLocalCount,
        int sourcePositionOffset)
    {
        if (probeMethod.Body == null)
        {
            return [];
        }

        if (sourceReferenceNode is CSharp.BlockSyntax sourceBlock)
        {
            var probeStatements =
                probeMethod.Body.Statements
                    .Skip(generatedLocalCount)
                    .ToImmutableArray();

            var count = Math.Min(
                sourceBlock.Statements.Count,
                probeStatements.Length);

            if (count == 0)
            {
                return [];
            }

            using var builder =
                ImmutableArrayBuilder<CSharpReferenceTarget>.Rent(count);

            for (var index = 0; index < count; index++)
            {
                builder.Add(
                    new CSharpReferenceTarget(
                        sourceBlock.Statements[index],
                        probeStatements[index],
                        sourcePositionOffset));
            }

            return builder.ToImmutable();
        }

        var probeExpression =
            probeMethod.Body
                .Statements
                .OfType<CSharp.ReturnStatementSyntax>()
                .LastOrDefault()
                ?.Expression;

        if (probeExpression == null)
        {
            return [];
        }

        return
        [
            new CSharpReferenceTarget(
            sourceReferenceNode,
            probeExpression,
            sourcePositionOffset)
        ];
    }

    private static bool TryGetContainingMarkupAttribute(
        AkburaSyntax syntax,
        out MarkupAttributeSyntax markupAttribute)
    {
        for (var node = syntax.Parent; node != null; node = node.Parent)
        {
            switch (node.Kind)
            {
                case AkburaSyntaxKind.MarkupPlainAttributeSyntax:
                case AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax:
                case AkburaSyntaxKind.MarkupPrefixedAttributeSyntax:
                case AkburaSyntaxKind.TailwindFlagAttributeSyntax:
                case AkburaSyntaxKind.TailwindFullAttributeSyntax:
                    markupAttribute = Unsafe.As<MarkupAttributeSyntax>(node);
                    return true;
            }
        }

        markupAttribute = null!;
        return false;
    }

    private void AddCSharpProbeRootSymbolMappings(
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        if (SyntaxTree.GetRootSyntax() is not AkburaDocumentSyntax root)
        {
            return;
        }

        foreach (var member in root.Members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.StateDeclarationSyntax:
                    AddCSharpProbeRootSymbolMapping(
                        akburaSymbolsByName,
                        Unsafe.As<StateDeclarationSyntax>(member).Name.Identifier.ValueText,
                        GetSymbolInfo(member).Symbol);
                    break;

                case AkburaSyntaxKind.ParamDeclarationSyntax:
                    AddCSharpProbeRootSymbolMapping(
                        akburaSymbolsByName,
                        Unsafe.As<ParamDeclarationSyntax>(member).Name.Identifier.ValueText,
                        GetSymbolInfo(member).Symbol);
                    break;

                case AkburaSyntaxKind.InjectDeclarationSyntax:
                    AddCSharpProbeRootSymbolMapping(
                        akburaSymbolsByName,
                        Unsafe.As<InjectDeclarationSyntax>(member).Name.Identifier.ValueText,
                        GetSymbolInfo(member).Symbol);
                    break;

                case AkburaSyntaxKind.CommandDeclarationSyntax:
                    var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
                    if (GetSymbolInfo(commandDeclaration).Symbol is ICommandSymbol command)
                    {
                        akburaSymbolsByName[command.Name] = command;
                        akburaSymbolsByCommandTypeName["__AkburaCommand_" + ToCSharpIdentifier(command.Name)] = command;
                    }

                    break;
            }
        }
    }

    internal Func<RoslynSymbol, AkburaSymbol?> CreateCSharpOperationSymbolMapper(
        AkburaSyntax scopeSyntax,
        IAkcssSymbol? containingAkcssSymbol = null)
    {
        var akburaSymbolsByName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        var akburaSymbolsByCommandTypeName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        AddCSharpProbeRootSymbolMappings(
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);
        AddMarkupScopeSymbolMappings(scopeSyntax, akburaSymbolsByName);

        if (containingAkcssSymbol is ITailwindUtilitySymbol utility)
        {
            foreach (var parameter in utility.Parameters)
            {
                if (!string.IsNullOrWhiteSpace(parameter.Name))
                {
                    akburaSymbolsByName[parameter.Name] = parameter;
                }
            }
        }

        return symbol => TryGetReferencedAkburaSymbol(
            symbol,
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);
    }

    private void AddMarkupScopeSymbolMappings(
        AkburaSyntax scopeSyntax,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName)
    {
        if (scopeSyntax == null ||
            SyntaxTree.Kind != SyntaxTreeKind.Component)
        {
            return;
        }

        var scope = GetMarkupBindingScope(scopeSyntax);
        var mappedMarkupNames = new HashSet<string>(StringComparer.Ordinal);
        for (var binder = BindingSession.GetSemanticBinder(scope); binder != null; binder = binder.Next)
        {
            if (binder is MarkupBinder markupBinder)
            {
                if (markupBinder.GetDeclaredItemSymbol() is { } itemSymbol &&
                    mappedMarkupNames.Add(itemSymbol.Name))
                {
                    akburaSymbolsByName[itemSymbol.Name] = itemSymbol;
                }

                AddMarkupNameSymbolMappings(
                    markupBinder.GetDeclaredNameSymbols(scope),
                    akburaSymbolsByName,
                    mappedMarkupNames);
                continue;
            }

            if (binder is ComponentBinder componentBinder)
            {
                AddMarkupNameSymbolMappings(
                    componentBinder.GetDeclaredMarkupNameSymbols(),
                    akburaSymbolsByName,
                    mappedMarkupNames);
            }
        }
    }

    private static void AddMarkupNameSymbolMappings(
        ImmutableArray<AkburaSymbol> symbols,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        HashSet<string> mappedMarkupNames)
    {
        foreach (var symbol in symbols)
        {
            if (symbol is not IMarkupNameSymbol markupName ||
                !mappedMarkupNames.Add(markupName.Name))
            {
                continue;
            }

            akburaSymbolsByName[markupName.Name] = markupName;
        }
    }

    private static void AddCSharpProbeRootSymbolMapping(
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        string name,
        AkburaSymbol? symbol)
    {
        if (!string.IsNullOrWhiteSpace(name) &&
            symbol != null)
        {
            akburaSymbolsByName[name] = symbol;
        }
    }

    private static CSharpReferenceSymbolInfo PreferResolvedSymbol(CSharpReferenceSymbolInfo first, CSharpReferenceSymbolInfo second)
    {
        if (first.Symbol != null)
        {
            return first;
        }

        if (second.Symbol != null)
        {
            return second;
        }

        return first.IsMethodGroup
            ? first
            : second;
    }

    private static CSharpReferenceSymbolInfo GetBestSymbolInfo(Microsoft.CodeAnalysis.SemanticModel semanticModel, CSharp.ExpressionSyntax syntax)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(syntax);
        if (symbolInfo.Symbol != null)
        {
            return new CSharpReferenceSymbolInfo(
                symbolInfo.Symbol,
                isMethodGroup: false);
        }

        if (symbolInfo.CandidateSymbols.Length == 1)
        {
            return new CSharpReferenceSymbolInfo(
                symbolInfo.CandidateSymbols[0],
                isMethodGroup: false);
        }

        var isMethodGroup = symbolInfo.CandidateSymbols.Length > 1 &&
            symbolInfo.CandidateSymbols.All(static candidate =>
                candidate is IMethodSymbol
                {
                    MethodKind: not MethodKind.Constructor and
                        not MethodKind.StaticConstructor,
                });
        return new CSharpReferenceSymbolInfo(
            symbol: null,
            isMethodGroup);
    }

    private static void AddCSharpSymbolReference(
        SemanticModel semanticModel,
        ImmutableArrayBuilder<CSharpSymbolReference> references,
        HashSet<string> seenReferences,
        CSharp.ExpressionSyntax syntax,
        TextSpan sourceSpan,
        CSharpReferenceSymbolInfo symbolInfo,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        var symbol = symbolInfo.Symbol;
        if (symbol == null &&
            !symbolInfo.IsMethodGroup)
        {
            return;
        }

        string key;
        if (symbol == null)
        {
            key = sourceSpan.Start.ToString(
                System.Globalization.CultureInfo.InvariantCulture) +
                ":" +
                sourceSpan.Length.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) +
                ":MethodGroup";
        }
        else
        {
            key = sourceSpan.Start.ToString(
                System.Globalization.CultureInfo
                    .InvariantCulture) +
                ":" +
                sourceSpan.Length.ToString(
                    System.Globalization.CultureInfo
                        .InvariantCulture) +
                ":" +
                symbol.Kind +
                ":" +
                symbol.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat);
        }

        if (!seenReferences.Add(key))
        {
            return;
        }

        var akburaSymbol = symbol == null
            ? null
            : TryGetReferencedAkburaSymbol(
                symbol,
                akburaSymbolsByName,
                akburaSymbolsByCommandTypeName);
        if (akburaSymbol == null &&
            symbol != null)
        {
            akburaSymbol = TryGetCommandReceiverSymbol(
                semanticModel,
                syntax,
                akburaSymbolsByName,
                akburaSymbolsByCommandTypeName);
        }

        references.Add(
            new CSharpSymbolReference(
                syntax,
                sourceSpan,
                symbol == null
                    ? default
                    : new CSharpSymbolDefinition(symbol),
                akburaSymbol,
                GetCSharpReferenceName(syntax),
                symbol is ILocalSymbol
                    ? semanticModel.GetTypeInfo(syntax).Nullability.FlowState
                    : NullableFlowState.None,
                IsCSharpNameOfOperand(semanticModel, syntax),
                symbolInfo.IsMethodGroup));
    }

    private static AkburaSymbol? TryGetCommandReceiverSymbol(SemanticModel semanticModel, CSharp.ExpressionSyntax syntax, Dictionary<string, AkburaSymbol> akburaSymbolsByName, Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        var receiver = GetMemberReceiver(syntax);
        if (receiver == null)
        {
            return null;
        }

        var receiverInfo = GetReceiverSymbolInfo(
            semanticModel,
            receiver);
        if (receiverInfo.Symbol is not (IFieldSymbol or ILocalSymbol or IParameterSymbol))
        {
            return null;
        }

        return TryGetReferencedAkburaSymbol(
            receiverInfo.Symbol,
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName) is ICommandSymbol command
                ? command
                : null;
    }

    private static CSharp.ExpressionSyntax? GetMemberReceiver(CSharp.ExpressionSyntax syntax)
    {
        if (syntax.Parent is CSharp.MemberAccessExpressionSyntax memberAccess &&
            ReferenceEquals(memberAccess.Name, syntax))
        {
            return memberAccess.Expression;
        }

        if (syntax.Parent is not CSharp.MemberBindingExpressionSyntax memberBinding ||
            !ReferenceEquals(memberBinding.Name, syntax))
        {
            return null;
        }

        for (var current = memberBinding.Parent; current != null; current = current.Parent)
        {
            if (current is CSharp.ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.WhenNotNull.Span.Contains(syntax.Span))
            {
                return conditionalAccess.Expression;
            }

            if (current is CSharp.StatementSyntax)
            {
                break;
            }
        }

        return null;
    }

    private static CSharpReferenceSymbolInfo GetReceiverSymbolInfo(SemanticModel semanticModel, CSharp.ExpressionSyntax receiver)
    {
        var symbolInfo = GetBestSymbolInfo(
            semanticModel,
            receiver);
        if (symbolInfo.Symbol != null)
        {
            return symbolInfo;
        }

        return receiver switch
        {
            CSharp.ParenthesizedExpressionSyntax parenthesized =>
                GetReceiverSymbolInfo(
                    semanticModel,
                    parenthesized.Expression),
            CSharp.PostfixUnaryExpressionSyntax postfix when postfix.IsKind(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression) =>
                GetReceiverSymbolInfo(
                    semanticModel,
                    postfix.Operand),
            _ => symbolInfo,
        };
    }

    private static bool IsCSharpNameOfOperand(SemanticModel semanticModel, CSharp.ExpressionSyntax syntax)
    {
        for (var ancestor = syntax.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is CSharp.InvocationExpressionSyntax invocation &&
                invocation.Expression is CSharp.IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText == "nameof" &&
                semanticModel.GetOperation(invocation) is Microsoft.CodeAnalysis.Operations.INameOfOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetCSharpReferenceName(CSharp.ExpressionSyntax syntax)
    {
        return syntax switch
        {
            CSharp.IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
            CSharp.GenericNameSyntax genericName => genericName.Identifier.ValueText,
            CSharp.MemberAccessExpressionSyntax memberAccess => GetCSharpReferenceName(memberAccess.Name),
            _ => syntax.ToString()
        };
    }

    private static CSharp.StatementSyntax? ParseCSharpStatement(CSharpStatementSyntax statementSyntax)
    {
        var text = statementSyntax.Tokens.ToFullString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return CSharpSyntaxFactory.ParseStatement(text);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static AkburaSymbol? TryGetReferencedAkburaSymbol(
        RoslynSymbol csharpSymbol,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        if (csharpSymbol is ILocalSymbol local &&
            akburaSymbolsByName.TryGetValue(local.Name, out var symbol))
        {
            return MatchesProjectedSymbolOrigin(
                local,
                symbol,
                allowUnannotated: true)
                    ? symbol
                    : null;
        }

        if (csharpSymbol is IFieldSymbol field &&
            akburaSymbolsByName.TryGetValue(field.Name, out symbol))
        {
            return symbol;
        }

        if (csharpSymbol is IParameterSymbol parameter &&
            akburaSymbolsByName.TryGetValue(parameter.Name, out symbol))
        {
            return MatchesProjectedSymbolOrigin(
                parameter,
                symbol,
                allowUnannotated:
                    symbol is ITailwindUtilityParameterSymbol or
                        ICommandParameterSymbol)
                    ? symbol
                    : null;
        }

        if (csharpSymbol.ContainingType != null &&
            akburaSymbolsByCommandTypeName.TryGetValue(csharpSymbol.ContainingType.Name, out symbol))
        {
            return symbol;
        }

        return null;
    }

    private static bool MatchesProjectedSymbolOrigin(RoslynSymbol symbol, AkburaSymbol candidate, bool allowUnannotated)
    {
        var hasAnnotation = false;
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            // Only this symbol's declaration carries its origin. An annotation
            // on an enclosing foreach or method belongs to another symbol.
            var declaration = reference.GetSyntax();

            foreach (var annotation in declaration.GetAnnotations(CSharpProbeBinder.ProjectedSymbolAnnotationKind))
            {
                hasAnnotation = true;
                if (CSharpProbeSymbolOrigin.TryParse(annotation.Data, out var origin) &&
                    origin.Kind == candidate.Kind &&
                    CSharpProbeBinder.TryGetDeclarationSpan(candidate, out var declarationSpan) &&
                    origin.DeclarationSpan == declarationSpan)
                {
                    return true;
                }
            }
        }

        // Preserve name-based mapping for legacy probes without origin metadata.
        return allowUnannotated &&
            !hasAnnotation;
    }

    private readonly struct CSharpReferenceTarget
    {
        public CSharpReferenceTarget(
            SyntaxNode sourceNode,
            SyntaxNode probeNode,
            int sourcePositionOffset)
        {
            SourceNode = sourceNode ??
                throw new ArgumentNullException(
                    nameof(sourceNode));

            ProbeNode = probeNode ??
                throw new ArgumentNullException(
                    nameof(probeNode));

            SourcePositionOffset =
                sourcePositionOffset;
        }

        public SyntaxNode SourceNode { get; }

        public SyntaxNode ProbeNode { get; }

        /// <summary>
        /// Maps positions from the detached C# source tree into the
        /// original Akbura document.
        /// </summary>
        public int SourcePositionOffset { get; }

        public TextSpan MapToSource(
            TextSpan probeSpan)
        {
            var relativeStart =
                probeSpan.Start -
                ProbeNode.FullSpan.Start;

            var sourceStart =
                SourcePositionOffset +
                SourceNode.FullSpan.Start +
                relativeStart;

            return new TextSpan(
                sourceStart,
                probeSpan.Length);
        }
    }

    private readonly struct CSharpReferenceSymbolInfo
    {
        public CSharpReferenceSymbolInfo(RoslynSymbol? symbol, bool isMethodGroup)
        {
            Symbol = symbol;
            IsMethodGroup = isMethodGroup;
        }

        public RoslynSymbol? Symbol { get; }

        public bool IsMethodGroup { get; }
    }
}
