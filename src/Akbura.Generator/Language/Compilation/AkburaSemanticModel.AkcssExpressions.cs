using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using System.Linq;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal IMarkupContentOperation? GetControlTemplateContentOperation(Syntax.MarkupElementSyntax element) =>
        BindingSession.BindSemanticSyntax(element) is BoundTree.BoundMarkupComponent component
            ? AkburaOperationFactory.CreateMarkupContentOperationForTemplate(component, _operationFactory)
            : null;

    internal CSharpBindingResult BindAkcssExpression(
        CSharp.ExpressionSyntax expression, AkcssBindingContext context, ITypeSymbol? expectedType = null) =>
        BindAkcssExpressionCore(expression, context.Owner, context.ExpressionReceiver, expectedType);

    private CSharpBindingResult BindAkcssExpressionCore(
        CSharp.ExpressionSyntax expression, IAkcssDeclarationOwner owner, CSharpSymbolDefinition receiver,
        ITypeSymbol? expectedType = null)
    {
        var compilationUnit = CreateAkcssExpressionProbe(
            expression, owner, expectedType, includeCompletionMembers: false, completionScope: null,
            receiver);
        return BindingSession.GetCSharpProbeBinder(owner.DeclarationSyntax!, BinderUsage.Akcss)
            .BindReturnExpression(compilationUnit, isBindingPath: true, expectedType);
    }

    internal CSharpBindingResult BindAkcssOptionalDefault(
        AkcssMixinParameterSymbol parameter, CSharp.ExpressionSyntax expression, ITypeSymbol expectedType)
    {
        var annotation = new SyntaxAnnotation();
        var probeParameter = CSharpFactory.Parameter(CSharpFactory.Identifier("__akbura_value"))
            .WithType(CreateCSharpProbeReturnType(expectedType))
            .WithDefault(CSharpFactory.EqualsValueClause(expression))
            .WithAdditionalAnnotations(annotation);
        var method = CSharpFactory.MethodDeclaration(
                CSharpFactory.PredefinedType(CSharpFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaOptionalParameterProbe")
            .WithParameterList(CSharpFactory.ParameterList(CSharpFactory.SingletonSeparatedList(probeParameter)))
            .WithBody(CSharpFactory.Block());
        var probeClass = CSharpFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpFactory.SingletonList<CSharp.MemberDeclarationSyntax>(method));
        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass, GetAkcssCSharpUsingDirectives(parameter.DeclarationSyntax));
        return BindingSession.GetCSharpProbeBinder(parameter.DeclarationSyntax, BinderUsage.Akcss)
            .BindOptionalParameterDefault(compilationUnit, annotation, expectedType);
    }

    internal bool TryBindAkcssAmxValue(
        CSharp.ExpressionSyntax expression, AkcssBindingContext context, ITypeSymbol expectedType,
        CSharpBindingResult originalBinding, out CSharpBindingResult valueBinding, out AkcssAmxInvocationValue value) =>
        TryBindAkcssAmxValue(expression, expectedType, originalBinding,
            (candidate, target) => BindAkcssExpression(candidate, context, target), out valueBinding, out value);

    internal bool TryBindTemplateAmxValue(Syntax.MarkupAttributeSyntax attribute,
        CSharp.ExpressionSyntax expression, ITypeSymbol expectedType, CSharpBindingResult originalBinding,
        out CSharpBindingResult valueBinding, out AkcssAmxInvocationValue value) =>
        TryBindAkcssAmxValue(expression, expectedType, originalBinding,
            (candidate, target) => BindMarkupAttributeExpression(attribute, candidate, target), out valueBinding, out value);

    private bool TryBindAkcssAmxValue(CSharp.ExpressionSyntax expression, ITypeSymbol expectedType,
        CSharpBindingResult originalBinding, Func<CSharp.ExpressionSyntax, ITypeSymbol?, CSharpBindingResult> bind,
        out CSharpBindingResult valueBinding, out AkcssAmxInvocationValue value)
    {
        value = default;
        valueBinding = originalBinding;
        var unwrapped = expression;
        while (unwrapped is CSharp.ParenthesizedExpressionSyntax parenthesized)
        {
            unwrapped = parenthesized.Expression;
        }

        if (unwrapped is not CSharp.InvocationExpressionSyntax invocation)
        {
            return false;
        }

        var binding = ReferenceEquals(expression, invocation)
            ? originalBinding
            : bind(invocation, null);
        var method = GetRealAkcssAmxMethod(binding.Symbol as IMethodSymbol, out var kind);
        if (method is null)
        {
            if (!binding.Diagnostics.Any(static diagnostic => diagnostic.Id == "CS0411"))
            {
                return false;
            }

            var name = invocation.Expression switch
            {
                CSharp.MemberAccessExpressionSyntax access => access.Name as CSharp.IdentifierNameSyntax,
                CSharp.IdentifierNameSyntax identifier => identifier,
                _ => null,
            };
            if (name is null || !binding.CandidateSymbols.Any(
                    static (candidate, model) => model.IsAkcssResourceMethod(candidate as IMethodSymbol), this))
            {
                return false;
            }

            var typeName = CSharpFactory.ParseTypeName(expectedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            var typedName = CSharpFactory.GenericName(name.Identifier,
                CSharpFactory.TypeArgumentList(CSharpFactory.SingletonSeparatedList(typeName))).WithTriviaFrom(name);
            var typedExpression = invocation.ReplaceNode(name, typedName);
            valueBinding = bind(typedExpression, expectedType);
            method = GetRealAkcssAmxMethod(GetInvocation(valueBinding)?.TargetMethod, out kind);
        }
        else
        {
            valueBinding = bind(invocation, expectedType);
            if (GetRealAkcssAmxMethod(GetInvocation(valueBinding)?.TargetMethod, out var reboundKind) is { } rebound)
            {
                method = rebound;
                kind = reboundKind;
            }
        }

        if (method is null)
        {
            // Retain diagnostics from the typed probe even when an argument could not be bound.
            return true;
        }

        var operation = GetInvocation(valueBinding);
        var keyOperation = operation?.Arguments.FirstOrDefault()?.Value;
        while (keyOperation is IConversionOperation { IsImplicit: true } conversion)
        {
            keyOperation = conversion.Operand;
        }

        var key = keyOperation is null ? default : new CSharpOperationDefinition(keyOperation);
        var typeArgument = method.TypeArguments.Length == 1 ? new CSharpSymbolDefinition(method.TypeArguments[0]) : default;
        var sourceArguments = invocation.ArgumentList.Arguments;
        var arguments = ImmutableArray.CreateBuilder<CSharp.ExpressionSyntax>(sourceArguments.Count);
        foreach (var argument in sourceArguments)
        {
            arguments.Add(argument.Expression);
        }

        value = new AkcssAmxInvocationValue(kind, typeArgument, arguments.MoveToImmutable(), method, key);
        return true;

        static IInvocationOperation? GetInvocation(CSharpBindingResult result)
        {
            var operation = result.OperationDefinition.Operation;
            while (true)
            {
                switch (operation)
                {
                    case IConversionOperation conversion:
                        operation = conversion.Operand;
                        break;
                    case IParenthesizedOperation parenthesized:
                        operation = parenthesized.Operand;
                        break;
                    default:
                        return operation as IInvocationOperation;
                }
            }
        }
    }

    private static IMethodSymbol? GetRealAkcssAmxMethod(IMethodSymbol? candidate, out AkcssAmxInvocationKind candidateKind)
    {
        candidateKind = AkcssAmxInvocationKind.None;
        // A source symbol from a probe has a different identity in the original compilation.
        if (candidate is not { IsStatic: true, Arity: 1, ContainingAssembly: { Name: "Akbura" } assembly } ||
            !SymbolEqualityComparer.Default.Equals(candidate.ContainingType, assembly.GetTypeByMetadataName("Akbura.Amx")))
        {
            return null;
        }

        candidateKind = candidate.Name switch
        {
            "DynamicResource" => AkcssAmxInvocationKind.DynamicResource,
            "StaticResource" => AkcssAmxInvocationKind.StaticResource,
            "Extend" => AkcssAmxInvocationKind.Extend,
            _ => AkcssAmxInvocationKind.None,
        };
        return candidateKind == AkcssAmxInvocationKind.None ? null : candidate;
    }

    internal bool TryGetAmxResourceKey(CSharpExpressionSyntax expression, int position,
        out TextSpan span, out ITypeSymbol? expectedType, CancellationToken cancellationToken)
    {
        span = default;
        expectedType = null;
        if (!EmbeddedCSharpSyntaxFacts.TryGetExpression(expression, out var source, out var host) ||
            position < host.Start || position > host.End)
        {
            return false;
        }

        var local = position - host.Start + source.FullSpan.Start;
        CSharp.InvocationExpressionSyntax? candidate = null;
        foreach (var node in source.DescendantNodesAndSelf())
        {
            if (node is CSharp.InvocationExpressionSyntax call && call.ArgumentList.Arguments.Count != 0 &&
                call.ArgumentList.Arguments[0].Expression is CSharp.LiteralExpressionSyntax literal &&
                literal.Token.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralToken) &&
                literal.Span.Start < local && local <= literal.Span.End)
            {
                candidate = call;
            }
        }

        if (candidate?.ArgumentList.Arguments[0].Expression is not CSharp.LiteralExpressionSyntax key ||
            !key.Token.Text.StartsWith("\"", StringComparison.Ordinal))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var projection = CreateCSharpCompletionProjection(expression, position - host.Start);
        var model = CreateReferenceProbeSemanticModel(projection.Root, out var tree);
        var root = tree.GetRoot(cancellationToken);
        var projectedStart = projection.ProjectedSpan.Start + candidate.Span.Start - source.FullSpan.Start;
        CSharp.InvocationExpressionSyntax? invocation = null;
        for (var node = root.FindToken(projectedStart).Parent; node is not null; node = node.Parent)
        {
            if (node is CSharp.InvocationExpressionSyntax call &&
                call.Span.Start == projectedStart && call.Span.Length == candidate.Span.Length)
            {
                invocation = call;
                break;
            }
        }

        if (invocation is null)
        {
            return false;
        }

        var info = model.GetSymbolInfo(invocation, cancellationToken);
        var method = info.Symbol as IMethodSymbol ?? info.CandidateSymbols.FirstOrDefault(
            static (symbol, model) => model.IsAkcssResourceMethod(symbol as IMethodSymbol), this) as IMethodSymbol;
        if (!IsAkcssResourceMethod(method))
        {
            return false;
        }

        var start = host.Start + key.Token.SpanStart - source.FullSpan.Start + 1;
        var end = start - 1 + key.Token.Span.Length;
        if (key.Token.Text.Length > 1 && key.Token.Text.EndsWith("\"", StringComparison.Ordinal) && !key.ContainsDiagnostics)
        {
            end--;
        }

        if (position < start || position > end)
        {
            return false;
        }

        span = TextSpan.FromBounds(start, end);
        expectedType = method!.TypeArguments[0] is { TypeKind: not TypeKind.TypeParameter and not TypeKind.Error } type
            ? type
            : GetAkcssEditorContext(expression) is { } context
                ? GetAkcssExpressionExpectedType(expression, context)
                : null;
        return true;
    }

    private bool IsAkcssResourceMethod(IMethodSymbol? candidate) =>
        GetRealAkcssAmxMethod(candidate, out var kind) is not null &&
        kind is AkcssAmxInvocationKind.DynamicResource or AkcssAmxInvocationKind.StaticResource;

    internal bool TryGetAkcssPropertyReference(
        string name, INamedTypeSymbol receiver, IAkcssDeclarationOwner owner, out Symbols.IPropertySymbol property)
    {
        var field = FindAvaloniaPropertyField(receiver, name);
        if (field is null)
        {
            property = null!;
            return false;
        }

        var clrProperty = FindPublicClrProperty(receiver, name);
        property = new PropertySymbol(name, GetMarkupPropertyType(null, null, null, field, null),
            avaloniaPropertyDefinition: new CSharpSymbolDefinition(field),
            clrPropertyDefinition: clrProperty is null ? default : new CSharpSymbolDefinition(clrProperty),
            language: SymbolLanguage.Akcss,
            containingSymbol: owner);
        return true;
    }
}
