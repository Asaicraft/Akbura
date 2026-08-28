using Akbura.Language.Symbols;

namespace Akbura.Language.Operations;

internal enum TailwindUtilityBindingPrioritySource
{
    None,
    Constant,
    Member,
}

internal readonly struct TailwindUtilityBindingPriority
{
    public TailwindUtilityBindingPriority(int constantValue)
    {
        Source = TailwindUtilityBindingPrioritySource.Constant;
        ConstantValue = constantValue;
        Member = default;
    }

    public TailwindUtilityBindingPriority(CSharpSymbolDefinition member)
    {
        Source = TailwindUtilityBindingPrioritySource.Member;
        ConstantValue = default;
        Member = member;
    }

    public TailwindUtilityBindingPrioritySource Source { get; }

    public int ConstantValue { get; }

    public CSharpSymbolDefinition Member { get; }

    public bool IsSpecified => Source != TailwindUtilityBindingPrioritySource.None;
}
