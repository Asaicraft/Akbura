using Akbura.Language.Binder;
using Akbura.Language.Operations;
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
        CSharpBindingResult originalBinding, out CSharpBindingResult valueBinding, out AkcssAmxInvocationValue value)
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
            : BindAkcssExpression(invocation, context);
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
            if (name is null || !binding.CandidateSymbols.OfType<IMethodSymbol>().Any(candidate =>
                    GetRealAkcssAmxMethod(candidate, out var candidateKind) is not null &&
                    candidateKind is AkcssAmxInvocationKind.StaticResource or AkcssAmxInvocationKind.DynamicResource))
            {
                return false;
            }

            var typeName = CSharpFactory.ParseTypeName(expectedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            var typedName = CSharpFactory.GenericName(name.Identifier,
                CSharpFactory.TypeArgumentList(CSharpFactory.SingletonSeparatedList(typeName))).WithTriviaFrom(name);
            var typedExpression = invocation.ReplaceNode(name, typedName);
            valueBinding = BindAkcssExpression(typedExpression, context, expectedType);
            method = GetRealAkcssAmxMethod(GetInvocation(valueBinding)?.TargetMethod, out kind);
        }
        else
        {
            valueBinding = BindAkcssExpression(invocation, context, expectedType);
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
        value = new AkcssAmxInvocationValue(kind,
            typeArgument, invocation.ArgumentList.Arguments.Select(static argument => argument.Expression).ToImmutableArray(),
            method, key);
        return true;

        IMethodSymbol? GetRealAkcssAmxMethod(IMethodSymbol? candidate, out AkcssAmxInvocationKind candidateKind)
        {
            candidateKind = AkcssAmxInvocationKind.None;
            if (candidate is not { IsStatic: true, Arity: 1 } ||
                Compilation.CSharpCompilation.GetTypeByMetadataName("Akbura.Amx") is not { } amx ||
                !SymbolEqualityComparer.Default.Equals(candidate.ContainingType, amx))
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
