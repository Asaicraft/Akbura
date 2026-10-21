using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Language.Binder;

/// <summary>
/// Roslyn-only payload owned by one exact base compilation. No markup wrapper,
/// binder, dependency collector or diagnostic is retained. The operation may
/// intentionally retain its own bounded synthetic probe compilation and model.
/// </summary>
internal sealed record ReusableProbeBinding(CSharpBindingResult Result, DetachedCSharpDependencySet Dependencies, TextSpan QuerySpan, string ProjectionText)
{
    internal const int MaxProjectionCharacters = 8192;
    internal const int MaxOperationNodes = 32;

    internal static bool IsMemberChain(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax or ThisExpressionSyntax => true,
        MemberAccessExpressionSyntax { Name: IdentifierNameSyntax } member => IsMemberChain(member.Expression),
        AliasQualifiedNameSyntax { Alias.Identifier.ValueText: "global", Name: IdentifierNameSyntax } => true,
        _ => false,
    };

    internal static bool CanRetain(CSharpBindingResult result)
    {
        // The existing binder maps Roslyn's successful None reason to NotFound.
        // Preserve that API behavior; require a selected property/field, no
        // candidates and no diagnostics rather than interpreting that flag alone.
        if (!result.Diagnostics.IsEmpty || !result.CandidateSymbols.IsEmpty ||
            result.CandidateReason is not (Symbols.CandidateReason.None or Symbols.CandidateReason.NotFound) ||
            !result.IsBindingPath || result.TypeSymbol == null || result.Conversion.IsUserDefined ||
            result.Conversion.TargetType != null && (!result.Conversion.Exists || !result.Conversion.IsImplicit) ||
            !IsSupportedSymbol(result.Symbol) ||
            result.OperationDefinition.Operation is not { } operation)
        {
            return false;
        }
        var remaining = MaxOperationNodes;
        return IsSupportedOperation(operation, ref remaining);
    }

    private static bool IsSupportedOperation(IOperation operation, ref int remaining)
    {
        if (--remaining < 0 || operation is not (IPropertyReferenceOperation or IFieldReferenceOperation or IInstanceReferenceOperation or
            IConversionOperation { OperatorMethod: null } or ILocalReferenceOperation) ||
            operation is ILocalReferenceOperation local && !IsProjectedComponentLocal(local.Local))
        {
            return false;
        }
        foreach (var child in operation.ChildOperations)
        {
            if (!IsSupportedOperation(child, ref remaining))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsSupportedSymbol(ISymbol? symbol) => symbol is
        IPropertySymbol { IsIndexer: false, ReturnsByRef: false, ReturnsByRefReadonly: false } or IFieldSymbol ||
        symbol is ILocalSymbol local && IsProjectedComponentLocal(local);

    private static bool IsProjectedComponentLocal(ILocalSymbol local)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            foreach (var annotation in reference.GetSyntax().GetAnnotations(CSharpProbeBinder.ProjectedSymbolAnnotationKind))
            {
                if (CSharpProbeSymbolOrigin.TryParse(annotation.Data, out var origin) && origin.Name == local.Name &&
                    origin.DeclarationSpan.Length != 0 && origin.Kind is Symbols.SymbolKind.InjectedService or Symbols.SymbolKind.State or Symbols.SymbolKind.Parameter)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
