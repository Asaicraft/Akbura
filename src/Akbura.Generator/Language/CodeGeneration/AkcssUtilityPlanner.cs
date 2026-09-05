using Akbura.Language.Symbols;
using Akbura.Pools;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Creates independently resolvable runtime operations for one AKCSS utility.
/// </summary>
internal static class AkcssUtilityPlanner
{
    public static AkcssUtilityPlan Create(
        ITailwindUtilitySymbol utility,
        AkcssGenerationSourceMap sourceMap)
    {
        AkburaDebug.Assert(utility != null);
        AkburaDebug.Assert(sourceMap != null);

        var metadata = ArrayBuilder<AkcssOperationMetadataPlan>.GetInstance();
        var operations = ArrayBuilder<AkcssUtilityOperationPlan>.GetInstance();
        var identifierValues = ArrayBuilder<AkcssIdentifierValue>.GetInstance();
        var expansionPath = PooledHashSet<IAkcssSymbol>.GetInstance();

        try
        {
            var planner = new AkcssOperationMetadataPlanner(metadata, identifierValues, sourceMap);
            planner.Build(utility, expansionPath);

            var hasConditionalOperations = false;

            for (var i = 0; i < metadata.Count; i++)
            {
                var operation = metadata[i];

                Debug.Assert(operation.Order == i);

                if (operation.Kind == GeneratedAkcssOperationKind.If ||
                    operation.Priority == GeneratedAkcssOperationPriority.StyleTrigger)
                {
                    hasConditionalOperations = true;
                }

                if (operation.Kind != GeneratedAkcssOperationKind.Set ||
                    operation.HasErrors ||
                    operation.Setter?.Property is not { CanWrite: true } property)
                {
                    continue;
                }

                var writePlan = PropertyWritePlan.Create(property);

                if (writePlan.Kind is not (
                        PropertyWriteKind.ClrProperty or
                        PropertyWriteKind.AvaloniaProperty or
                        PropertyWriteKind.AttachedAccessor))
                {
                    continue;
                }

                var conflictKey = CreateConflictKey(property);

                if (conflictKey == null)
                {
                    continue;
                }

                operations.Add(new AkcssUtilityOperationPlan(
                    metadataIndex: i,
                    order: operations.Count,
                    conflictKey));
            }

            var pooledMetadata = default(PooledImmutableList<AkcssOperationMetadataPlan>);
            var pooledOperations = default(PooledImmutableList<AkcssUtilityOperationPlan>);

            try
            {
                pooledMetadata = metadata.ToPooledImmutableList();
                pooledOperations = operations.ToPooledImmutableList();

                return new AkcssUtilityPlan(
                    pooledMetadata,
                    pooledOperations,
                    hasConditionalOperations);
            }
            catch
            {
                pooledMetadata.ReturnToPool();
                pooledOperations.ReturnToPool();
                throw;
            }
        }
        finally
        {
            expansionPath.Free();
            identifierValues.Free();
            operations.Free();
            metadata.Free();
        }
    }

    private static string? CreateConflictKey(IPropertySymbol property)
    {
        var name = property.Name;

        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var pooled = PooledStringBuilder.GetInstance();

        pooled.Builder.Append("property:");

        if (property.IsAttachedProperty || property.WriteKind == PropertyAccessKind.AttachedAccessor)
        {
            var owner =
                property.AttachedPropertyDefinition.Symbol?.ContainingType ??
                property.AttachedSetterDefinition.Symbol?.ContainingType ??
                property.AttachedGetterDefinition.Symbol?.ContainingType;

            if (owner != null)
            {
                pooled.Builder.Append(owner.Name);
                pooled.Builder.Append('.');
            }
        }

        pooled.Builder.Append(name);

        return pooled.ToStringAndFree();
    }
}
