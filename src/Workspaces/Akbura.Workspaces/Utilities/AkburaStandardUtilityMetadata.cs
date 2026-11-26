namespace Akbura.Workspaces.Utilities;

// Only attach these descriptions to definitions originating in Akbura.Styles.akcss.
/// <summary>
/// Provides descriptions and search terms for standard AKCSS utility definitions.
/// </summary>
internal sealed record AkburaStandardUtilityMetadata(
    string Category,
    string Description,
    string[] Keywords,
    string? Example = null)
{
    private static readonly AkburaStandardUtilityMetadata Sizing = new(
        "sizing", "Sets a control dimension or size constraint.",
        ["size", "width", "height", "размер", "ширина", "высота"]);
    private static readonly AkburaStandardUtilityMetadata Margin = new(
        "spacing", "Sets the outer margin of the control.",
        ["margin", "outer spacing", "отступ", "внешний отступ"]);
    private static readonly AkburaStandardUtilityMetadata Padding = new(
        "spacing", "Sets the inner padding of a compatible control.",
        ["padding", "inner spacing", "внутренний отступ"]);
    private static readonly AkburaStandardUtilityMetadata Gap = new(
        "spacing", "Sets spacing between children of a compatible panel.",
        ["gap", "item spacing", "spacing between items", "spacing between cards",
         "расстояние между элементами", "расстояние между карточками", "интервал между элементами"]);
    private static readonly AkburaStandardUtilityMetadata Grid = new(
        "grid", "Sets an attached grid placement property.",
        ["grid", "grid placement", "row", "column", "сетка", "строка", "столбец"]);
    private static readonly AkburaStandardUtilityMetadata ColumnSpan = Grid with
    {
        Description = "Sets the number of grid columns spanned by the control.",
        Example = "col-span-6",
    };
    private static readonly AkburaStandardUtilityMetadata Alignment = new(
        "alignment", "Sets the control's alignment in its layout slot.",
        ["align", "alignment", "center", "выравнивание", "по центру"]);
    private static readonly AkburaStandardUtilityMetadata Visibility = new(
        "visibility", "Sets whether the control is visible.",
        ["show", "hide", "visibility", "показать", "скрыть", "видимость"]);
    private static readonly AkburaStandardUtilityMetadata Background = new(
        "appearance", "Sets the background of a compatible control.",
        ["appearance", "background", "color", "оформление", "фон", "цвет"]);

    public static AkburaStandardUtilityMetadata? Find(string name)
    {
        return name switch
        {
            "w" or "w-auto" or "w-full" or "w-px" or "h" or "h-auto" or "h-full" or "h-px"
                or "min-w" or "max-w" or "min-h" or "max-h" => Sizing,
            "m" or "mx" or "my" or "mt" or "mr" or "mb" or "ml" => Margin,
            "p" or "px" or "py" or "pt" or "pr" or "pb" or "pl" => Padding,
            "gap" or "gap-x" or "gap-y" => Gap,
            "col-span" => ColumnSpan,
            "col" or "row" or "row-span" => Grid,
            "self-start" or "self-center" or "self-end" or "self-auto" or "self-stretch"
                or "self-x-start" or "self-x-center" or "self-x-end" or "self-x-auto" or "self-x-stretch"
                or "self-y-start" or "self-y-center" or "self-y-end" or "self-y-auto" or "self-y-stretch" => Alignment,
            "hidden" or "visible" => Visibility,
            "bg" or "bg-transparent" or "bg-black" or "bg-white" => Background,
            _ => null,
        };
    }

    public bool MatchesCategory(string value)
    {
        return Category.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            (value.Equals("layout", StringComparison.OrdinalIgnoreCase) &&
             Category is "sizing" or "spacing" or "grid" or "alignment");
    }

    public bool MatchesQuery(string value)
    {
        if (MatchesCategory(value))
        {
            return true;
        }

        foreach (var keyword in Keywords)
        {
            if (keyword.Equals(value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
