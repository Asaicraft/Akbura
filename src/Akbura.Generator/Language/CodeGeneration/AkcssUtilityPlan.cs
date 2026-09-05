using Akbura.Pools;

namespace Akbura.Language.CodeGeneration;

internal readonly struct AkcssUtilityOperationPlan
{
    public AkcssUtilityOperationPlan(int metadataIndex, int order, string conflictKey)
    {
        MetadataIndex = metadataIndex;
        Order = order;
        ConflictKey = conflictKey;
    }

    public int MetadataIndex { get; }

    public int Order { get; }

    public string ConflictKey { get; }
}

internal readonly struct AkcssUtilityPlan
{
    public AkcssUtilityPlan(
        PooledImmutableList<AkcssOperationMetadataPlan> metadata,
        PooledImmutableList<AkcssUtilityOperationPlan> operations,
        bool hasConditionalOperations)
    {
        Metadata = metadata;
        Operations = operations;
        HasConditionalOperations = hasConditionalOperations;
    }

    public PooledImmutableList<AkcssOperationMetadataPlan> Metadata { get; }

    public PooledImmutableList<AkcssUtilityOperationPlan> Operations { get; }

    public bool HasConditionalOperations { get; }

    public bool IsEmpty => Operations.IsEmpty;

    internal void ReturnToPool()
    {
        Metadata.ReturnToPool();
        Operations.ReturnToPool();
    }
}
