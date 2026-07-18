using Avalonia.Collections;

namespace Akbura.ComponentTree;

public interface IComponentTree
{
    public IComponentTree? ComponentParent
    {
        get;
    }

    public IAvaloniaReadOnlyList<IComponentTree> ComponentChildren
    {
        get;
    }
}

