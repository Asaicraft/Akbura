namespace Akbura.Language.CodeGeneration;

internal static class ComponentRuntimeScopeFacts
{
    internal static int GetConditionalRuntimeId(in ComponentPlan plan, int regionId)
    {
        if (regionId < 0)
        {
            return -1;
        }

        ref readonly var region = ref plan.ConditionalRegions.ItemRef(regionId);
        if (!plan.Elements.ItemRef(region.OwnerElementId).UsesRuntimeStorage)
        {
            return -1;
        }

        var rootScopeId = region.RuntimeStorageRootScopeId;
        var id = 0;
        for (var i = 0; i < regionId; i++)
        {
            ref readonly var previous = ref plan.ConditionalRegions.ItemRef(i);
            if (previous.RuntimeStorageRootScopeId == rootScopeId &&
                plan.Elements.ItemRef(previous.OwnerElementId).UsesRuntimeStorage)
            {
                id++;
            }
        }

        return id;
    }

    internal static int GetConditionalParentRuntimeId(in ComponentPlan plan, int regionId)
    {
        ref readonly var region = ref plan.ConditionalRegions.ItemRef(regionId);
        if (region.ParentRegionId < 0)
        {
            return -1;
        }

        ref readonly var parent = ref plan.ConditionalRegions.ItemRef(region.ParentRegionId);
        if (parent.RuntimeStorageRootScopeId != region.RuntimeStorageRootScopeId)
        {
            return -1;
        }

        return GetConditionalRuntimeId(plan, region.ParentRegionId);
    }

    internal static int GetRuntimeParentId(in ComponentPlan plan, in ComponentElementPlan element)
    {
        if (element.ParentId < 0)
        {
            return -1;
        }

        ref readonly var parent = ref plan.Elements.ItemRef(element.ParentId);
        if (parent.RuntimeStorageRootScopeId != element.RuntimeStorageRootScopeId || !parent.UsesRuntimeStorage)
        {
            return -1;
        }

        return parent.RuntimeStorageId;
    }
}
