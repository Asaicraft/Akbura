// This file is ported and adapted from (KirillOsenkov/XmlParser)

namespace Akbura.Language;

internal abstract class Buffer
{
    public abstract int Length
    {
        get;
    }

    public abstract char this[int index]
    {
        get;
    }

    public abstract string GetText(int start, int length);

    public abstract void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count);
}
