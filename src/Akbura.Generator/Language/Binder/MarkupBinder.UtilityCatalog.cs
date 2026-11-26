using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Threading;

namespace Akbura.Language.Binder;

internal sealed partial class MarkupBinder
{
    internal TailwindUtilityDefinitionLookup LookupTailwindUtilityDefinitions(
        INamedTypeSymbol targetType,
        string? additionalImport,
        IAkcssModuleSymbol? additionalModule,
        CancellationToken cancellationToken)
    {
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        using var definitions = ImmutableArrayBuilder<TailwindUtilityDefinition>.Rent();
        var claimed = PooledHashSet<(string Name, int Arity)>.GetInstance();
        var layerSelectors = PooledHashSet<(string Name, int Arity)>.GetInstance();
        var unresolved = false;
        try
        {
            var imports = GetAkcssImportNames();
            AddDeclarations(GetLocalAkcssUtilityDeclarations(diagnostics), definitions);
            foreach (var layer in GetImportedAkcssUtilitySymbolLayers(imports))
            {
                AddSymbols(layer, definitions);
            }

            // A new metadata import follows existing metadata imports and precedes imports
            // backed by source trees, just as it would in normal markup binding.
            if (additionalModule != null && additionalImport != null &&
                !imports.Contains(additionalImport) &&
                Compilation.GetLocalAkcssSyntaxTreesByLogicalName(additionalImport).IsEmpty)
            {
                AddSymbols(additionalModule.AkcssSymbols, definitions, additionalImport);
            }

            foreach (var layer in GetImportedAkcssUtilityDeclarationLayers(diagnostics, imports))
            {
                AddDeclarations(layer, definitions);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new(definitions.ToImmutable(), unresolved, diagnostics.Count != 0);
        }
        finally
        {
            layerSelectors.Free();
            claimed.Free();
        }

        void AddDeclarations(ImmutableArray<AkcssUtilityDeclarationSyntax> declarations,
            ImmutableArrayBuilder<TailwindUtilityDefinition> definitions)
        {
            foreach (var declaration in declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var selector = declaration.Selector;
                var name = selector.Name.Identifier.ValueText;
                if (string.IsNullOrWhiteSpace(name) || claimed.Contains((name, selector.Parameters.Count)))
                {
                    continue;
                }

                if (!SemanticModel.TryResolveAkcssTargetType(selector.TargetType, out var declaredTarget))
                {
                    unresolved = true;
                    continue;
                }

                if (declaredTarget.Symbol is ITypeSymbol type &&
                    !AkburaSemanticModel.IsAssignableTo(targetType, type))
                {
                    continue;
                }

                layerSelectors.Add((name, selector.Parameters.Count));
                if (HasMissingParameterName(declaration))
                {
                    unresolved = true;
                    continue;
                }

                var definition = new TailwindUtilitySymbol(declaration, declaredTarget,
                    SemanticModel.CreateTailwindUtilityParameters(declaration),
                    ImmutableArray<IAkcssOperation>.Empty);
                AddDefinition(definition, definitions, requiredImport: null);
            }
            ClaimLayer();
        }

        void AddSymbols<TSymbol>(ImmutableArray<TSymbol> symbols,
            ImmutableArrayBuilder<TailwindUtilityDefinition> definitions, string? requiredImport = null)
            where TSymbol : IAkcssSymbol
        {
            foreach (var symbol in symbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (symbol is ITailwindUtilitySymbol utility &&
                    !claimed.Contains((utility.Name, utility.Parameters.Length)) &&
                    IsAkcssTargetCompatibleWithType(utility, targetType))
                {
                    if (requiredImport == null)
                    {
                        layerSelectors.Add((utility.Name, utility.Parameters.Length));
                    }
                    AddDefinition(utility, definitions, requiredImport);
                }
            }

            if (requiredImport == null)
            {
                ClaimLayer();
            }
        }

        void AddDefinition(ITailwindUtilitySymbol symbol,
            ImmutableArrayBuilder<TailwindUtilityDefinition> definitions, string? requiredImport)
        {
            if (symbol is IMetadataAkcssSymbol { HasErrors: true })
            {
                unresolved = true;
                return;
            }

            foreach (var parameter in symbol.Parameters)
            {
                if (parameter.Type.Symbol is not ITypeSymbol { TypeKind: not TypeKind.Error })
                {
                    unresolved = true;
                    return;
                }
            }
            definitions.Add(new(symbol, requiredImport));
        }

        void ClaimLayer()
        {
            claimed.UnionWith(layerSelectors);
            layerSelectors.Clear();
        }
    }

    private static bool HasMissingParameterName(AkcssUtilityDeclarationSyntax declaration)
    {
        foreach (var parameter in declaration.Selector.Parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.ParamName.Identifier.ValueText))
            {
                return true;
            }
        }
        return false;
    }
}
