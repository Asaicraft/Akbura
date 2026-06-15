namespace Akbura.Language.Operations;

internal readonly struct AkcssColorValue
{
    public AkcssColorValue(byte a, byte r, byte g, byte b)
    {
        A = a;
        R = r;
        G = g;
        B = b;
    }

    public byte A { get; }

    public byte R { get; }

    public byte G { get; }

    public byte B { get; }

    public uint ToUInt32()
    {
        return ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;
    }
}
