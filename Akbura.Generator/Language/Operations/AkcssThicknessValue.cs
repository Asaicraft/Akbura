namespace Akbura.Language.Operations;

internal readonly struct AkcssThicknessValue
{
    public AkcssThicknessValue(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public double Left { get; }

    public double Top { get; }

    public double Right { get; }

    public double Bottom { get; }
}
