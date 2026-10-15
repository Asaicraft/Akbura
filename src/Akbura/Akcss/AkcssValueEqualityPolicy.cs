namespace Akbura.Akcss;

/// <summary>
/// Controls redundant notifications for a value source; arbitrary sources notify by default.
/// </summary>
public enum AkcssValueEqualityPolicy
{
    AlwaysNotify,
    ImmutableScalar,
    ReferenceEquality,
}

internal static class AkcssValueEquality
{
    internal static bool IsEquivalent(object? left, object? right, AkcssValueEqualityPolicy policy)
    {
        if (policy == AkcssValueEqualityPolicy.ReferenceEquality)
        {
            return ReferenceEquals(left, right);
        }
        if (policy != AkcssValueEqualityPolicy.ImmutableScalar)
        {
            return false;
        }
        if (left == null || right == null)
        {
            return left == null && right == null;
        }
        var type = left.GetType();
        return type == right.GetType() &&
            (type.IsPrimitive || type.IsEnum || left is string or decimal or DateTime or DateTimeOffset or TimeSpan or Guid) &&
            left.Equals(right);
    }
}
