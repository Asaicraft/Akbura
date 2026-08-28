namespace Akbura.Markup;

#pragma warning disable IDE1006 // Names intentionally match AKCSS prefixes.

/// <summary>Activates below the Tailwind <c>sm</c> width.</summary>
[UtilityVariant(
    -10d,
    ConflictGroup = "BreakpointsGroup",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class maxSmExtension : BreakpointMarkupExtension
{
    public maxSmExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width) => width < 640d;
}

/// <summary>Activates below the Tailwind <c>md</c> width.</summary>
[UtilityVariant(
    -20d,
    ConflictGroup = "BreakpointsGroup",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class maxMdExtension : BreakpointMarkupExtension
{
    public maxMdExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width) => width < 768d;
}

/// <summary>Activates below the Tailwind <c>lg</c> width.</summary>
[UtilityVariant(
    -30d,
    ConflictGroup = "BreakpointsGroup",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class maxLgExtension : BreakpointMarkupExtension
{
    public maxLgExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width) => width < 1024d;
}

/// <summary>Activates below the Tailwind <c>xl</c> width.</summary>
[UtilityVariant(
    -40d,
    ConflictGroup = "BreakpointsGroup",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class maxXlExtension : BreakpointMarkupExtension
{
    public maxXlExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width) => width < 1280d;
}

/// <summary>Activates below the Tailwind <c>2xl</c> width.</summary>
[UtilityVariant(
    -50d,
    ConflictGroup = "BreakpointsGroup",
    UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
public sealed class maxXxlExtension : BreakpointMarkupExtension
{
    public maxXxlExtension()
    {
        unsafe
        {
            IsActivatedPredicate = new BreakpointPredicate(&IsActivated);
        }
    }

    private static bool IsActivated(double width) => width < 1536d;
}

#pragma warning restore IDE1006 // Names intentionally match AKCSS prefixes.
