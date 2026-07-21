namespace Akbura.Akcss;

/// <summary>
/// Applies a generated or hand-written <see cref="AkcssClass"/>.
/// </summary>
public sealed class AkcssClassActivator : AkcssStyleActivator
{
    public AkcssClassActivator(AkcssClass style)
        : base(style)
    {
        Class = style;
    }

    /// <summary>
    /// Gets the class represented by this application.
    /// </summary>
    public AkcssClass Class { get; }

    public override void Execute(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Class.Update(target);
    }
}
