using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using CSharpAliasQualifiedNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.AliasQualifiedNameSyntax;
using CSharpArgumentSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax;
using CSharpExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax;
using CSharpGenericNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.GenericNameSyntax;
using CSharpIdentifierNameSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax;
using CSharpInvocationExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax;
using CSharpMemberAccessExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxRewriter = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxRewriter;
using SyntaxNode = Microsoft.CodeAnalysis.SyntaxNode;

namespace Akbura.Language.CodeGeneration;

internal sealed class AkcssFullyQualifiedExpressionRewriter : CSharpSyntaxRewriter
{
    private static readonly ObjectPool<AkcssFullyQualifiedExpressionRewriter> s_pool =
        new(static () => new AkcssFullyQualifiedExpressionRewriter(), 16);

    private SemanticModel _semanticModel = null!;

    private AkcssFullyQualifiedExpressionRewriter()
    {
    }

    public static AkcssFullyQualifiedExpressionRewriter GetInstance(SemanticModel semanticModel)
    {
        var rewriter = s_pool.Allocate();

        rewriter._semanticModel = semanticModel;

        return rewriter;
    }

    public void Free()
    {
        _semanticModel = null!;

        s_pool.Free(this);
    }

    public override SyntaxNode? VisitInvocationExpression(CSharpInvocationExpressionSyntax node)
    {
        var method = _semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol;

        if (method?.ReducedFrom == null ||
            node.Expression is not CSharpMemberAccessExpressionSyntax memberAccess)
        {
            return base.VisitInvocationExpression(node);
        }

        var arguments = ArrayBuilder<CSharpArgumentSyntax>.GetInstance(
            node.ArgumentList.Arguments.Count + 1);

        try
        {
            var receiver = Visit(memberAccess.Expression) as CSharpExpressionSyntax ??
                memberAccess.Expression;

            arguments.Add(CSharpSyntaxFactory.Argument(receiver));

            for (var i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                var argument = node.ArgumentList.Arguments[i];

                arguments.Add(
                    Visit(argument) as CSharpArgumentSyntax ??
                    argument);
            }

            var methodExpression = CSharpSyntaxFactory.ParseExpression(
                AkcssExpressionGenerator.GetMetadataTypeName(method.ContainingType) +
                "." +
                AkcssExpressionGenerator.EscapeIdentifier(method.Name));

            var invocation = CSharpSyntaxFactory.InvocationExpression(
                methodExpression,
                CSharpSyntaxFactory.ArgumentList(
                    CSharpSyntaxFactory.SeparatedList(arguments)));

            return invocation.WithTriviaFrom(node);
        }
        finally
        {
            arguments.Free();
        }
    }

    public override SyntaxNode? VisitMemberAccessExpression(CSharpMemberAccessExpressionSyntax node)
    {
        var symbol = _semanticModel.GetSymbolInfo(node).Symbol;

        if (symbol is not { IsStatic: true, ContainingType: { } containingType })
        {
            return base.VisitMemberAccessExpression(node);
        }

        var visitedName = Visit(node.Name)?.WithoutTrivia().ToString() ??
            node.Name.WithoutTrivia().ToString();

        return CSharpSyntaxFactory.ParseExpression(
            AkcssExpressionGenerator.GetMetadataTypeName(containingType) +
            "." +
            visitedName).WithTriviaFrom(node);
    }

    public override SyntaxNode? VisitQualifiedName(
        Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax node)
    {
        if (_semanticModel.GetSymbolInfo(node).Symbol is ITypeSymbol type)
        {
            return CSharpSyntaxFactory.ParseName(
                AkcssExpressionGenerator.GetMetadataTypeName(type)).WithTriviaFrom(node);
        }

        return base.VisitQualifiedName(node);
    }

    public override SyntaxNode? VisitAliasQualifiedName(
        CSharpAliasQualifiedNameSyntax node)
    {
        if (_semanticModel.GetSymbolInfo(node).Symbol is ITypeSymbol type)
        {
            return CSharpSyntaxFactory.ParseName(
                AkcssExpressionGenerator.GetMetadataTypeName(type)).WithTriviaFrom(node);
        }

        return base.VisitAliasQualifiedName(node);
    }

    public override SyntaxNode? VisitIdentifierName(CSharpIdentifierNameSyntax node)
    {
        var alias = _semanticModel.GetAliasInfo(node);
        var symbol = alias?.Target ?? _semanticModel.GetSymbolInfo(node).Symbol;

        if (symbol is ITypeSymbol type &&
            node.Parent is not Microsoft.CodeAnalysis.CSharp.Syntax.QualifiedNameSyntax &&
            node.Parent is not CSharpAliasQualifiedNameSyntax)
        {
            return CSharpSyntaxFactory.ParseName(
                AkcssExpressionGenerator.GetMetadataTypeName(type)).WithTriviaFrom(node);
        }

        var isMemberName =
            node.Parent is CSharpMemberAccessExpressionSyntax memberAccess &&
            ReferenceEquals(memberAccess.Name, node);

        if (!isMemberName &&
            symbol is { IsStatic: true, ContainingType: { } containingType })
        {
            return CSharpSyntaxFactory.ParseExpression(
                AkcssExpressionGenerator.GetMetadataTypeName(containingType) +
                "." +
                AkcssExpressionGenerator.EscapeIdentifier(symbol.Name)).WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
    }

    public override SyntaxNode? VisitPredefinedType(
        Microsoft.CodeAnalysis.CSharp.Syntax.PredefinedTypeSyntax node)
    {
        return _semanticModel.GetTypeInfo(node).Type is { } type
            ? CSharpSyntaxFactory.ParseTypeName(
                AkcssExpressionGenerator.GetMetadataTypeName(type)).WithTriviaFrom(node)
            : base.VisitPredefinedType(node);
    }
}

internal sealed class AkcssMetadataExpressionRewriter : CSharpSyntaxRewriter
{
    private static readonly ObjectPool<AkcssMetadataExpressionRewriter> s_pool =
        new(static () => new AkcssMetadataExpressionRewriter(), 16);

    private string _targetName = null!;
    private ITailwindUtilitySymbol? _utility;
    private ArrayBuilder<AkcssIdentifierValue>? _identifierValues;
    private int _identifierValueCount;

    private AkcssMetadataExpressionRewriter()
    {
    }

    public static AkcssMetadataExpressionRewriter GetInstance(
        string targetName,
        IAkcssSymbol containingSymbol,
        ArrayBuilder<AkcssIdentifierValue>? identifierValues,
        int identifierValueCount)
    {
        var rewriter = s_pool.Allocate();

        rewriter._targetName = targetName;
        rewriter._utility = containingSymbol as ITailwindUtilitySymbol;
        rewriter._identifierValues = identifierValues;
        rewriter._identifierValueCount = identifierValueCount;

        return rewriter;
    }

    public void Free()
    {
        _targetName = null!;
        _utility = null;
        _identifierValues = null;
        _identifierValueCount = 0;

        s_pool.Free(this);
    }

    public override SyntaxNode? VisitIdentifierName(CSharpIdentifierNameSyntax node)
    {
        return StringComparer.Ordinal.Equals(
            node.Identifier.ValueText,
            AkcssExpressionGenerator.MetadataTargetName)
                ? CSharpSyntaxFactory.IdentifierName(_targetName).WithTriviaFrom(node)
                : base.VisitIdentifierName(node);
    }

    public override SyntaxNode? VisitElementAccessExpression(
        Microsoft.CodeAnalysis.CSharp.Syntax.ElementAccessExpressionSyntax node)
    {
        if (_utility == null ||
            node.Expression is not CSharpIdentifierNameSyntax identifier ||
            !StringComparer.Ordinal.Equals(
                identifier.Identifier.ValueText,
                AkcssExpressionGenerator.MetadataArgumentsName) ||
            node.ArgumentList.Arguments.Count != 1 ||
            node.ArgumentList.Arguments[0].Expression is not
                Microsoft.CodeAnalysis.CSharp.Syntax.LiteralExpressionSyntax literal ||
            literal.Token.Value is not int ordinal)
        {
            return base.VisitElementAccessExpression(node);
        }

        var parameters = _utility.Parameters;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            if (parameter.Ordinal != ordinal)
            {
                continue;
            }

            if (AkcssIdentifierValueLookup.TryGet(
                    _identifierValues,
                    _identifierValueCount,
                    parameter.Name,
                    out var value) ||
                AkcssIdentifierValueLookup.TryGet(
                    _identifierValues,
                    _identifierValueCount,
                    parameter.CSharpName,
                    out value))
            {
                return value.WithTriviaFrom(node);
            }

            return CSharpSyntaxFactory
                .IdentifierName(AkcssExpressionGenerator.GetParameterName(parameter))
                .WithTriviaFrom(node);
        }

        return base.VisitElementAccessExpression(node);
    }
}

internal sealed class AkcssAmxExpressionRewriter : CSharpSyntaxRewriter
{
    private const string ResourceValueParameter = "__resourceValue";

    private static readonly ObjectPool<AkcssAmxExpressionRewriter> s_pool =
        new(static () => new AkcssAmxExpressionRewriter(), 16);

    private static readonly CSharpExpressionSyntax s_findResourceExpression =
        CSharpSyntaxFactory.ParseExpression(
            "global::Avalonia.Controls.ResourceNodeExtensions.FindResource");

    private static readonly Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax s_resourceHostType =
        CSharpSyntaxFactory.ParseTypeName(
            "global::Avalonia.Controls.IResourceHost");

    private string _targetName = null!;
    private bool _observeDynamicResource;
    private string _sourceTargetName = null!;
    private ArrayBuilder<AkcssIdentifierValue>? _identifierValues;
    private int _identifierValueCount;
    private bool _preserveResourceInvocations;

    private AkcssAmxExpressionRewriter()
    {
    }

    public AkcssDynamicResourceBinding? DynamicResource { get; private set; }

    public bool RequiresResourceHost { get; private set; }

    public static AkcssAmxExpressionRewriter GetInstance(
        string targetName,
        bool observeDynamicResource,
        string sourceTargetName,
        ArrayBuilder<AkcssIdentifierValue>? identifierValues,
        int identifierValueCount,
        bool preserveResourceInvocations)
    {
        var rewriter = s_pool.Allocate();

        rewriter._targetName = targetName;
        rewriter._observeDynamicResource = observeDynamicResource;
        rewriter._sourceTargetName = sourceTargetName;
        rewriter._identifierValues = identifierValues;
        rewriter._identifierValueCount = identifierValueCount;
        rewriter._preserveResourceInvocations = preserveResourceInvocations;
        rewriter.DynamicResource = null;
        rewriter.RequiresResourceHost = false;

        return rewriter;
    }

    public void Free()
    {
        _targetName = null!;
        _observeDynamicResource = false;
        _sourceTargetName = null!;
        _identifierValues = null;
        _identifierValueCount = 0;
        _preserveResourceInvocations = false;
        DynamicResource = null;
        RequiresResourceHost = false;

        s_pool.Free(this);
    }

    public override SyntaxNode? VisitIdentifierName(CSharpIdentifierNameSyntax node)
    {
        if (AkcssIdentifierValueLookup.TryGet(
                _identifierValues,
                _identifierValueCount,
                node.Identifier.ValueText,
                out var value))
        {
            return value.WithTriviaFrom(node);
        }

        if (!StringComparer.Ordinal.Equals(_sourceTargetName, _targetName) &&
            StringComparer.Ordinal.Equals(node.Identifier.ValueText, _sourceTargetName))
        {
            return CSharpSyntaxFactory.IdentifierName(_targetName).WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
    }

    public override SyntaxNode? VisitInvocationExpression(CSharpInvocationExpressionSyntax node)
    {
        if (!TryGetAmxInvocation(node, out var methodName, out var genericName) ||
            node.ArgumentList.Arguments.Count != 1)
        {
            return base.VisitInvocationExpression(node);
        }

        if (_preserveResourceInvocations &&
            methodName is "DynamicResource" or "StaticResource")
        {
            return base.VisitInvocationExpression(node);
        }

        if (methodName is "DynamicResource" or "StaticResource")
        {
            RequiresResourceHost = true;
        }

        var keySyntax = node.ArgumentList.Arguments[0].Expression;
        var keyExpression = Visit(keySyntax) as CSharpExpressionSyntax ?? keySyntax;

        if (methodName == "DynamicResource" &&
            _observeDynamicResource &&
            DynamicResource == null)
        {
            DynamicResource = new AkcssDynamicResourceBinding(
                keyExpression.ToString(),
                ResourceValueParameter);

            var resourceValue = CSharpSyntaxFactory.PostfixUnaryExpression(
                CSharpSyntaxKind.SuppressNullableWarningExpression,
                CSharpSyntaxFactory.IdentifierName(ResourceValueParameter));

            return CSharpSyntaxFactory.CastExpression(
                genericName.TypeArgumentList.Arguments[0].WithoutTrivia(),
                resourceValue).WithTriviaFrom(node);
        }

        if (methodName is "DynamicResource" or "StaticResource")
        {
            return CreateStaticResourceAccess(node, genericName, keyExpression);
        }

        return base.VisitInvocationExpression(node);
    }

    private CSharpExpressionSyntax CreateStaticResourceAccess(
        CSharpInvocationExpressionSyntax original,
        CSharpGenericNameSyntax genericName,
        CSharpExpressionSyntax keyExpression)
    {
        var arguments = ArrayBuilder<CSharpArgumentSyntax>.GetInstance(2);

        try
        {
            var target = CSharpSyntaxFactory.CastExpression(
                s_resourceHostType,
                CSharpSyntaxFactory.IdentifierName(_targetName));

            arguments.Add(CSharpSyntaxFactory.Argument(target));
            arguments.Add(CSharpSyntaxFactory.Argument(keyExpression.WithoutTrivia()));

            var invocation = CSharpSyntaxFactory.InvocationExpression(
                s_findResourceExpression,
                CSharpSyntaxFactory.ArgumentList(
                    CSharpSyntaxFactory.SeparatedList(arguments)));

            var resource = CSharpSyntaxFactory.PostfixUnaryExpression(
                CSharpSyntaxKind.SuppressNullableWarningExpression,
                invocation);

            return CSharpSyntaxFactory.CastExpression(
                genericName.TypeArgumentList.Arguments[0].WithoutTrivia(),
                resource).WithTriviaFrom(original);
        }
        finally
        {
            arguments.Free();
        }
    }

    private static bool TryGetAmxInvocation(
        CSharpInvocationExpressionSyntax invocation,
        out string methodName,
        out CSharpGenericNameSyntax genericName)
    {
        methodName = string.Empty;
        genericName = null!;

        if (invocation.Expression is not CSharpMemberAccessExpressionSyntax
            {
                Expression: { } receiver,
                Name: CSharpGenericNameSyntax name,
            } ||
            name.TypeArgumentList.Arguments.Count != 1 ||
            !IsAmxReceiver(receiver))
        {
            return false;
        }

        methodName = name.Identifier.ValueText;
        genericName = name;

        return true;
    }

    private static bool IsAmxReceiver(CSharpExpressionSyntax receiver)
    {
        if (receiver is CSharpIdentifierNameSyntax identifier)
        {
            return identifier.Identifier.ValueText == "Amx";
        }

        if (receiver is not CSharpMemberAccessExpressionSyntax memberAccess ||
            memberAccess.Expression is not CSharpAliasQualifiedNameSyntax aliasQualified)
        {
            return false;
        }

        return aliasQualified.Alias.Identifier.ValueText == "global" &&
            aliasQualified.Name.Identifier.ValueText == "Akbura" &&
            memberAccess.Name.Identifier.ValueText == "Amx";
    }
}
