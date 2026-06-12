namespace Akbura;

/// <summary>
/// All methods in this class shoud be intercepted
/// </summary>
public static class Amx
{
    public static object? Extend<T>(params object[] args)
    {
        throw new InvalidOperationException("This method shoud be intercepted!");
    }

    public static T StaticResource<T>(object? key)
    {
        throw new InvalidOperationException("This method shoud be intercepted!");
    }

    public static T DynamicResource<T>(object? key)
    {
        throw new InvalidOperationException("This method shoud be intercepted!");
    }
}
