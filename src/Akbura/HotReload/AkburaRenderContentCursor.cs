namespace Akbura.HotReload;

/// <summary>Maps reserved virtual content slots to the current physical child count.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[System.ComponentModel.Browsable(false)]
public struct AkburaRenderContentCursor
{
    public int VirtualSlot { get; private set; }

    public int Delta { get; private set; }

    public readonly int PhysicalCount => VirtualSlot - Delta;

    public void AdvanceItem()
    {
        VirtualSlot++;
    }

    public void AdvanceRegion(int reservedCapacity, int activeCount)
    {
        if (reservedCapacity < 0 || activeCount < 0 || activeCount > reservedCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(activeCount));
        }

        VirtualSlot += reservedCapacity;
        Delta += reservedCapacity - activeCount;
    }
}
