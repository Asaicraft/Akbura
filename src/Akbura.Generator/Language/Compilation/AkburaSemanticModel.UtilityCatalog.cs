using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;

namespace Akbura.Language;

/// <summary>
/// Provides syntax, symbol, and operation information for one Akbura syntax tree.
/// </summary>
internal partial class AkburaSemanticModel
{
    internal INamedTypeSymbol? ResolveUtilityCatalogTarget(
        string targetType,
        out bool ambiguous)
    {
        var syntax = CSharpSyntaxFactory.ParseTypeName(targetType);
        var binding = syntax.ContainsDiagnostics
            ? CSharpBindingResult.Empty
            : BindCSharpType(syntax);
        ambiguous = binding.CandidateReason == AkburaCandidateReason.Ambiguous;
        return !ambiguous && TryGetMarkupComponentType(binding, out var type)
            ? type
            : null;
    }

    internal TailwindUtilityDefinitionLookup LookupTailwindUtilityDefinitions(
        INamedTypeSymbol targetType,
        MarkupElementSyntax? element,
        string? additionalImport,
        IAkcssModuleSymbol? additionalModule,
        CancellationToken cancellationToken)
    {
        if (element != null)
        {
            for (var binder = BindingSession.GetBinder(element);
                 binder != null;
                 binder = binder.Next)
            {
                if (binder is MarkupBinder markupBinder)
                {
                    return markupBinder.LookupTailwindUtilityDefinitions(targetType,
                        additionalImport, additionalModule, cancellationToken);
                }
            }
        }

        var root = SyntaxTree.GetRoot();
        if (!Compilation.TryGetDeclaration(root, out var declaration))
        {
            return new(ImmutableArray<TailwindUtilityDefinition>.Empty,
                HasUnresolvedDefinitions: true, HasUnresolvedImports: false);
        }

        var documentBinder = new MarkupBinder(this, BindingSession.GetBinder(root), declaration);
        return documentBinder.LookupTailwindUtilityDefinitions(targetType,
            additionalImport, additionalModule, cancellationToken);
    }
}
