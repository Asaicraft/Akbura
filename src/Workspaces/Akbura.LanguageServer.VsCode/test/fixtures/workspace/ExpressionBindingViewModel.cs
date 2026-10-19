namespace Gallery;

public sealed class ExpressionBindingViewModel
{
    /// <summary>The displayed expression-binding name.</summary>
    public string Name { get; set; } = "Ada";

    public string Format(string name) => name;
}
