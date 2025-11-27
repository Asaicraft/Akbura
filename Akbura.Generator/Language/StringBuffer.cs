using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language;
internal sealed class StringBuffer: Buffer
{
    private readonly string _text;
    public StringBuffer(string text)
    {
        _text = text;
    }

    public override int Length => _text.Length;

    public override char this[int index] => _text[index];

    public override string GetText(int start, int length)
    {
        return _text.Substring(start, length);
    }

    public override void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
    {
        _text.CopyTo(sourceIndex, destination, destinationIndex, count);
    }
}
