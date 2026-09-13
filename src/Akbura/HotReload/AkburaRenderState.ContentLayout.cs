namespace Akbura.HotReload;

public sealed partial class AkburaRenderState
{
    /// <summary>Checks cached ownership without allocating a desired-root array.</summary>
    public bool IsCollectionLayoutCurrent(int ownerLocalId, string slot, object collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(collection);
        var key = new RenderSlotKey(GetNode(ownerLocalId).NodeId, slot);
        return _collections.TryGetValue(key, out var state) && state.IsContiguousLayout(collection);
    }
}
