namespace Akbura.Hooks;

/// <summary>
/// Provides reference identity for one logical render hook.
/// </summary>
/// <remarks>
/// A hook implementation should keep one static key for each distinct runtime contract.
/// Overloads that share the same state and behavior should share the same key.
/// </remarks>
public sealed class UseHookKey
{
}
