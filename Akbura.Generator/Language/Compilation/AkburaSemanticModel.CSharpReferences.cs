using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    private const string CSharpReferenceProbeMethodName = "__AkburaSemanticProbe";
    private const string MarkupInlineReferenceProbeMethodName = "__AkburaMarkupInlineReferenceProbe";

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
            return ImmutableArray<CSharpSymbolReference>.Empty;
        }

        using var classMembersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        using var statementsBuilder = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        var akburaSymbolsByName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        var akburaSymbolsByCommandTypeName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        foreach (var local in CreateCSharpProbeMembersBefore(
            statementSyntax,
            classMembersBuilder,
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName))
        {
            statementsBuilder.Add(local);
        }

        foreach (var previousStatement in CreateCSharpBlockProbeStatementsBefore(statementSyntax))
        {
            statementsBuilder.Add(previousStatement);
        }

        statementsBuilder.Add(statement);

        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaSemanticProbe")
            .WithBody(CSharpSyntaxFactory.Block(CSharpSyntaxFactory.List(statementsBuilder.ToImmutable())));

        classMembersBuilder.Add(method);
        var compilationUnit = BindingSession
            .GetCSharpProbeBinder(statementSyntax, BinderUsage.Expression)
            .CreateComponentProbeCompilationUnit(
                classMembersBuilder.ToImmutable(),
                "__AkburaSemanticProbe");

        var semanticModel = CreateReferenceProbeSemanticModel(compilationUnit, out var syntaxTree);
        var probeStatement = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.MethodDeclarationSyntax>()
            .Single(methodDeclaration => methodDeclaration.Identifier.ValueText == CSharpReferenceProbeMethodName)
            .Body!
            .Statements
            .Last();

        return CollectCSharpSymbolReferences(
            semanticModel,
            [probeStatement],
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);
    }

    public ImmutableArray<CSharpSymbolReference> GetCSharpSymbolReferences(MarkupAttributeSyntax markupAttribute)
    {
        if (markupAttribute == null)
        {
            throw new ArgumentNullException(nameof(markupAttribute));
        }

        ValidateSyntaxTreeOwnership(markupAttribute);

        var value = GetMarkupAttributeValue(markupAttribute);
        if (value == null)
        {
            return ImmutableArray<CSharpSymbolReference>.Empty;
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

        return ImmutableArray<CSharpSymbolReference>.Empty;
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
            return ImmutableArray<CSharpSymbolReference>.Empty;
        }

        return TryGetContainingMarkupAttribute(inlineExpressionSyntax, out var markupAttribute)
            ? GetMarkupInlineExpressionCSharpSymbolReferences(markupAttribute, expression)
            : GetMarkupExpressionCSharpSymbolReferences(inlineExpressionSyntax, expression);
    }

    private ImmutableArray<CSharpSymbolReference> GetMarkupInlineExpressionCSharpSymbolReferences(
        MarkupAttributeSyntax markupAttribute,
        CSharp.ExpressionSyntax expression)
    {
        var attributeSymbol = GetSymbolInfo(markupAttribute).Symbol;
        var isHandler = attributeSymbol is IRoutedEventSymbol ||
            attributeSymbol is Symbols.IPropertySymbol { Command: not null };
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
            parameterNames = ImmutableArray<string>.Empty;
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
        var targetNodes = GetMarkupInlineReferenceTargetNodes(
            probeMethod,
            referenceNode,
            probeScope.LocalStatements.Length);
        var akburaSymbolsByName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        var akburaSymbolsByCommandTypeName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        AddCSharpProbeRootSymbolMappings(akburaSymbolsByName, akburaSymbolsByCommandTypeName);
        AddMarkupScopeSymbolMappings(markupAttribute, akburaSymbolsByName);

        return CollectCSharpSymbolReferences(
            semanticModel,
            targetNodes,
            akburaSymbolsByName,
            akburaSymbolsByCommandTypeName);
    }

    private ImmutableArray<CSharpSymbolReference> GetMarkupExpressionCSharpSymbolReferences(
        AkburaSyntax scopeSyntax,
        CSharp.ExpressionSyntax expression)
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
            return ImmutableArray<CSharpSymbolReference>.Empty;
        }

        var akburaSymbolsByName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        var akburaSymbolsByCommandTypeName = new Dictionary<string, AkburaSymbol>(StringComparer.Ordinal);
        AddCSharpProbeRootSymbolMappings(akburaSymbolsByName, akburaSymbolsByCommandTypeName);
        AddMarkupScopeSymbolMappings(scopeSyntax, akburaSymbolsByName);

        return CollectCSharpSymbolReferences(
            semanticModel,
            [targetExpression],
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
        var probeCompilation = Compilation.CSharpCompilation.AddSyntaxTrees(syntaxTree);
        return probeCompilation.GetSemanticModel(syntaxTree);
    }

    private ImmutableArray<CSharpSymbolReference> CollectCSharpSymbolReferences(
        Microsoft.CodeAnalysis.SemanticModel semanticModel,
        IEnumerable<SyntaxNode> targetNodes,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        using var references = ImmutableArrayBuilder<CSharpSymbolReference>.Rent();
        var seenReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var targetNode in targetNodes)
        {
            foreach (var identifier in targetNode.DescendantNodesAndSelf().OfType<CSharp.IdentifierNameSyntax>())
            {
                AddCSharpSymbolReference(
                    references,
                    seenReferences,
                    identifier,
                    semanticModel.GetSymbolInfo(identifier).Symbol,
                    akburaSymbolsByName,
                    akburaSymbolsByCommandTypeName);
            }

            foreach (var memberAccess in targetNode.DescendantNodesAndSelf().OfType<CSharp.MemberAccessExpressionSyntax>())
            {
                if (memberAccess.Name is CSharp.IdentifierNameSyntax name)
                {
                    var symbol = GetBestSymbolInfo(semanticModel, memberAccess);
                    if (symbol == null &&
                        memberAccess.Parent is CSharp.InvocationExpressionSyntax invocation &&
                        invocation.Expression == memberAccess)
                    {
                        symbol = GetBestSymbolInfo(semanticModel, invocation);
                    }

                    AddCSharpSymbolReference(
                        references,
                        seenReferences,
                        name,
                        symbol,
                        akburaSymbolsByName,
                        akburaSymbolsByCommandTypeName);
                }
            }
        }

        return references.ToImmutable();
    }

    private CSharp.MethodDeclarationSyntax CreateMarkupInlineReferenceProbeMethod(
        MarkupAttributeSyntax markupAttribute,
        AkburaSymbol? attributeSymbol,
        SyntaxNode referenceNode,
        ImmutableArray<CSharp.StatementSyntax> localStatements,
        ImmutableArray<string> parameterNames,
        bool isAsync)
    {
        var returnType = isAsync
            ? CSharpSyntaxFactory.ParseTypeName("global::System.Threading.Tasks.Task<object>")
            : CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword));
        var method = CSharpSyntaxFactory.MethodDeclaration(returnType, MarkupInlineReferenceProbeMethodName)
            .WithParameterList(CreateMarkupInlineReferenceProbeParameterList(
                markupAttribute,
                attributeSymbol,
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
        ImmutableArray<string> parameterNames)
    {
        if (attributeSymbol is IRoutedEventSymbol routedEvent)
        {
            return CreateEventHandlerProbeParameterList(routedEvent, parameterNames);
        }

        if (attributeSymbol is Symbols.IPropertySymbol { Command: { } command })
        {
            return CreateCommandHandlerProbeParameterList(command, parameterNames);
        }

        return CSharpSyntaxFactory.ParameterList();
    }

    private static SyntaxNode GetMarkupHandlerReferenceNode(
        CSharp.ExpressionSyntax expression,
        out ImmutableArray<string> parameterNames,
        out bool isAsync)
    {
        switch (expression)
        {
            case CSharp.ParenthesizedLambdaExpressionSyntax lambda:
                parameterNames = lambda.ParameterList.Parameters
                    .Select(static parameter => parameter.Identifier.ValueText)
                    .ToImmutableArray();
                isAsync = lambda.AsyncKeyword.RawKind != 0 || ContainsAwaitExpression(lambda.Body);
                return lambda.Body;

            case CSharp.SimpleLambdaExpressionSyntax lambda:
                parameterNames = ImmutableArray.Create(lambda.Parameter.Identifier.ValueText);
                isAsync = lambda.AsyncKeyword.RawKind != 0 || ContainsAwaitExpression(lambda.Body);
                return lambda.Body;

            case CSharp.AnonymousMethodExpressionSyntax anonymousMethod:
                parameterNames = anonymousMethod.ParameterList?.Parameters
                    .Select(static parameter => parameter.Identifier.ValueText)
                    .ToImmutableArray() ?? ImmutableArray<string>.Empty;
                isAsync = anonymousMethod.AsyncKeyword.RawKind != 0 || ContainsAwaitExpression(anonymousMethod.Body);
                return anonymousMethod.Body;

            default:
                parameterNames = ImmutableArray<string>.Empty;
                isAsync = ContainsAwaitExpression(expression);
                return expression;
        }
    }

    private static ImmutableArray<SyntaxNode> GetMarkupInlineReferenceTargetNodes(
        CSharp.MethodDeclarationSyntax probeMethod,
        SyntaxNode referenceNode,
        int generatedLocalCount)
    {
        if (probeMethod.Body == null)
        {
            return ImmutableArray<SyntaxNode>.Empty;
        }

        if (referenceNode is CSharp.BlockSyntax)
        {
            return probeMethod.Body.Statements
                .Skip(generatedLocalCount)
                .Cast<SyntaxNode>()
                .ToImmutableArray();
        }

        var expression = probeMethod.Body
            .Statements
            .OfType<CSharp.ReturnStatementSyntax>()
            .LastOrDefault()
            ?.Expression;

        return expression == null
            ? ImmutableArray<SyntaxNode>.Empty
            : ImmutableArray.Create<SyntaxNode>(expression);
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
        foreach (var member in SyntaxTree.GetRoot().Members)
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
        if (scopeSyntax == null)
        {
            return;
        }

        var scope = GetMarkupBindingScope(scopeSyntax);
        for (var binder = BindingSession.GetSemanticBinder(scope); binder != null; binder = binder.Next)
        {
            if (binder is not MarkupBinder markupBinder ||
                markupBinder.GetDeclaredItemSymbol() is not { } itemSymbol)
            {
                continue;
            }

            akburaSymbolsByName[itemSymbol.Name] = itemSymbol;
            return;
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

    private static RoslynSymbol? GetBestSymbolInfo(
        Microsoft.CodeAnalysis.SemanticModel semanticModel,
        CSharp.ExpressionSyntax syntax)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(syntax);
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }

    private static void AddCSharpSymbolReference(
        ImmutableArrayBuilder<CSharpSymbolReference> references,
        HashSet<string> seenReferences,
        CSharp.ExpressionSyntax syntax,
        RoslynSymbol? symbol,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        if (symbol == null)
        {
            return;
        }

        var key = syntax.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" +
            syntax.Span.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ":" +
            symbol.Kind.ToString() +
            ":" +
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!seenReferences.Add(key))
        {
            return;
        }

        references.Add(new CSharpSymbolReference(
            syntax,
            new CSharpSymbolDefinition(symbol),
            TryGetReferencedAkburaSymbol(
                symbol,
                akburaSymbolsByName,
                akburaSymbolsByCommandTypeName),
            GetCSharpReferenceName(syntax)));
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

    private static ImmutableArray<CSharp.StatementSyntax> CreateCSharpBlockProbeStatementsBefore(
        CSharpStatementSyntax statementSyntax)
    {
        if (statementSyntax.Parent?.Kind != AkburaSyntaxKind.CSharpBlockSyntax)
        {
            return ImmutableArray<CSharp.StatementSyntax>.Empty;
        }

        using var builder = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        var block = Unsafe.As<CSharpBlockSyntax>(statementSyntax.Parent);
        foreach (var member in block.Tokens)
        {
            if (ReferenceEquals(member, statementSyntax) ||
                SemanticSyntaxIdentity.Equals(member, statementSyntax) ||
                member.Position >= statementSyntax.Position)
            {
                break;
            }

            if (member.Kind != AkburaSyntaxKind.CSharpStatementSyntax)
            {
                continue;
            }

            var previousStatement = ParseCSharpStatement(Unsafe.As<CSharpStatementSyntax>(member));
            if (previousStatement != null)
            {
                builder.Add(previousStatement);
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<CSharp.StatementSyntax> CreateCSharpProbeMembersBefore(
        AkburaSyntax scope,
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> classMembersBuilder,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName)
    {
        using var builder = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Position >= scope.Position)
            {
                break;
            }

            switch (member.Kind)
            {
                case AkburaSyntaxKind.StateDeclarationSyntax:
                    var stateDeclaration = Unsafe.As<StateDeclarationSyntax>(member);
                    AddCSharpProbeLocal(
                        builder,
                        akburaSymbolsByName,
                        stateDeclaration.Name.Identifier.ValueText,
                        GetStateProbeFieldType(stateDeclaration),
                        GetSymbolInfo(stateDeclaration).Symbol);
                    break;

                case AkburaSyntaxKind.ParamDeclarationSyntax:
                    var paramDeclaration = Unsafe.As<ParamDeclarationSyntax>(member);
                    AddCSharpProbeLocal(
                        builder,
                        akburaSymbolsByName,
                        paramDeclaration.Name.Identifier.ValueText,
                        GetParamProbeType(paramDeclaration),
                        GetSymbolInfo(paramDeclaration).Symbol);
                    break;

                case AkburaSyntaxKind.InjectDeclarationSyntax:
                    var injectDeclaration = Unsafe.As<InjectDeclarationSyntax>(member);
                    AddCSharpProbeLocal(
                        builder,
                        akburaSymbolsByName,
                        injectDeclaration.Name.Identifier.ValueText,
                        GetInjectProbeType(injectDeclaration),
                        GetSymbolInfo(injectDeclaration).Symbol);
                    break;

                case AkburaSyntaxKind.CommandDeclarationSyntax:
                    var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
                    AddCSharpCommandProbeMembers(
                        classMembersBuilder,
                        akburaSymbolsByName,
                        akburaSymbolsByCommandTypeName,
                        commandDeclaration);
                    break;
            }
        }

        return builder.ToImmutable();
    }

    private void AddCSharpCommandProbeMembers(
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> classMembersBuilder,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        Dictionary<string, AkburaSymbol> akburaSymbolsByCommandTypeName,
        CommandDeclarationSyntax commandDeclaration)
    {
        if (GetSymbolInfo(commandDeclaration).Symbol is not ICommandSymbol command)
        {
            return;
        }

        foreach (var member in CreateCommandProbeMembers(commandDeclaration))
        {
            classMembersBuilder.Add(member);
        }

        akburaSymbolsByName[command.Name] = command;
        akburaSymbolsByCommandTypeName["__AkburaCommand_" + ToCSharpIdentifier(command.Name)] = command;
    }

    private static void AddCSharpProbeLocal(
        ImmutableArrayBuilder<CSharp.StatementSyntax> builder,
        Dictionary<string, AkburaSymbol> akburaSymbolsByName,
        string name,
        CSharp.TypeSyntax? type,
        AkburaSymbol? symbol)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            type == null ||
            symbol == null)
        {
            return;
        }

        builder.Add(CSharpSyntaxFactory.LocalDeclarationStatement(
            CSharpSyntaxFactory.VariableDeclaration(type)
                .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                    CSharpSyntaxFactory.VariableDeclarator(CSharpSyntaxFactory.Identifier(name))
                        .WithInitializer(CSharpSyntaxFactory.EqualsValueClause(
                            CSharpSyntaxFactory.LiteralExpression(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression)))))));
        akburaSymbolsByName[name] = symbol;
    }

    private CSharp.TypeSyntax? GetParamProbeType(ParamDeclarationSyntax paramDeclaration)
    {
        if (paramDeclaration.Type != null)
        {
            try
            {
                return paramDeclaration.Type.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        return GetSymbolInfo(paramDeclaration).Symbol is IParamSymbol paramSymbol &&
            paramSymbol.Type.Symbol is ITypeSymbol typeSymbol
                ? CSharpSyntaxFactory.ParseTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : null;
    }

    private static CSharp.TypeSyntax? GetInjectProbeType(InjectDeclarationSyntax injectDeclaration)
    {
        try
        {
            return injectDeclaration.Type.ToCSharp();
        }
        catch (InvalidOperationException)
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
            return symbol;
        }

        if (csharpSymbol is IFieldSymbol field &&
            akburaSymbolsByName.TryGetValue(field.Name, out symbol))
        {
            return symbol;
        }

        if (csharpSymbol is IParameterSymbol parameter &&
            akburaSymbolsByName.TryGetValue(parameter.Name, out symbol))
        {
            return symbol;
        }

        if (csharpSymbol.ContainingType != null &&
            akburaSymbolsByCommandTypeName.TryGetValue(csharpSymbol.ContainingType.Name, out symbol))
        {
            return symbol;
        }

        return null;
    }
}
