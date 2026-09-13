using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal void AddMarkupConditionalTemplateCaptureDiagnostics(MarkupIfStatementSyntax syntax,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        var boundary = GetConditionalTemplateCaptureBoundary(syntax);
        if (boundary == null || SyntaxTree.GetRoot() is not AkburaDocumentSyntax document)
        {
            return;
        }

        // One validation per local boundary; nested boundaries validate their own captures.
        foreach (var node in boundary.DescendantNodes())
        {
            if (node is MarkupIfStatementSyntax conditional &&
                ReferenceEquals(BindingSession.MarkupTemplateContent.GetLocalNameScopeOwner(conditional), boundary))
            {
                if (!ReferenceEquals(conditional, syntax))
                {
                    return;
                }

                break;
            }
        }

        var unsupported = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in document.Members)
        {
            if (member is not CSharpStatementSyntax statement)
            {
                continue;
            }

            foreach (var local in GetCSharpDeclaredLocals(statement))
            {
                if (TryGetUnsupportedConditionalCaptureType(local.Local.Type, out var reason))
                {
                    unsupported[local.Name] = reason;
                }
            }
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in boundary.DescendantNodes())
        {
            if (!ReferenceEquals(GetConditionalTemplateCaptureBoundary(node), boundary))
            {
                continue;
            }

            var references = node switch
            {
                MarkupAttributeSyntax attribute => GetCSharpSymbolReferences(attribute),
                InlineExpressionSyntax expression => GetCSharpSymbolReferences(expression),
                CSharpExpressionSyntax expression => GetCSharpSymbolReferences(expression),
                _ => ImmutableArray<CSharpSymbolReference>.Empty,
            };
            foreach (var reference in references)
            {
                if (reference.CSharpDefinition.Symbol is not ILocalSymbol local)
                {
                    continue;
                }

                var invalid = unsupported.TryGetValue(local.Name, out var reason);
                if (!invalid && IsProjectedLocalDeclaredOutsideBoundary(local, boundary))
                {
                    invalid = TryGetUnsupportedConditionalCaptureType(local.Type, out reason);
                }

                if (invalid && reported.Add(local.Name))
                {
                    diagnostics.Add(new AkburaSemanticDiagnostic(node,
                        ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalTemplateCapture, [local.Name, reason]));
                }
            }
        }
    }

    internal static bool IsProjectedLocalDeclaredOutsideBoundary(ILocalSymbol local, MarkupElementSyntax boundary)
    {
        foreach (var declaration in local.DeclaringSyntaxReferences)
        {
            foreach (var annotation in declaration.GetSyntax().GetAnnotations(CSharpProbeBinder.ProjectedSymbolAnnotationKind))
            {
                if (CSharpProbeSymbolOrigin.TryParse(annotation.Data, out var origin) &&
                    !boundary.FullSpan.Contains(origin.DeclarationSpan))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private MarkupElementSyntax? GetConditionalTemplateCaptureBoundary(AkburaSyntax syntax)
    {
        for (var boundary = BindingSession.MarkupTemplateContent.GetLocalNameScopeOwner(syntax);
             boundary != null;
             boundary = BindingSession.MarkupTemplateContent.GetLocalNameScopeOwner(boundary))
        {
            foreach (var node in boundary.DescendantNodes())
            {
                if (node is MarkupIfStatementSyntax conditional &&
                    ReferenceEquals(BindingSession.MarkupTemplateContent.GetLocalNameScopeOwner(conditional), boundary))
                {
                    return boundary;
                }
            }
        }

        return null;
    }

    internal static bool TryGetUnsupportedConditionalCaptureType(ITypeSymbol type, out string reason)
    {
        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol)
        {
            reason = "pointer and function-pointer types cannot be stored in a generic capture cell";
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return TryGetUnsupportedConditionalCaptureType(array.ElementType, out reason);
        }

        if (type is INamedTypeSymbol named)
        {
            if (named.IsRefLikeType)
            {
                reason = "ref-like values cannot be captured on the heap";
                return true;
            }

            if (named.IsAnonymousType)
            {
                reason = "anonymous types cannot currently be named in generated capture cells";
                return true;
            }

            foreach (var argument in named.TypeArguments)
            {
                if (TryGetUnsupportedConditionalCaptureType(argument, out reason))
                {
                    return true;
                }
            }
        }

        reason = string.Empty;
        return false;
    }
}
