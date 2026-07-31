using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Markup;

public enum UnprefixedUtilityPrecedence
{
    Below = -1,
    SourceOrder = 0,
    Above = 1
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class UtilityVariantAttribute : Attribute
{
    public UtilityVariantAttribute(double order)
    {
        Order = order;
    }

    public double Order
    {
        get;
    }

    public string? ConflictGroup
    {
        get;
        init;
    }

    public UnprefixedUtilityPrecedence UnprefixedPrecedence
    {
        get;
        init;
    } = UnprefixedUtilityPrecedence.SourceOrder;
}