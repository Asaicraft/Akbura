namespace Akbura.Language.CodeGeneration;

internal readonly struct ComponentHotReloadPlan
{
    public ComponentHotReloadPlan(
        string descriptorShape,
        string stateShape)
    {
        DescriptorShape = descriptorShape;
        StateShape = stateShape;
    }

    public string DescriptorShape { get; }

    public string StateShape { get; }

    public static ComponentHotReloadPlan Create(in ComponentMemberPlan plan)
    {
        return new ComponentHotReloadPlan(
            ComponentHotReloadIdentity.CreateDescriptorFingerprint(plan),
            ComponentHotReloadIdentity.CreateStateFingerprint(plan));
    }
}
