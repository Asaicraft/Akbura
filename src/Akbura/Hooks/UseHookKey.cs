namespace Akbura.Hooks;

/// <summary>
/// Provides reference identity for one compatible render-hook contract.
/// </summary>
/// <remarks>
/// A hook implementation should keep one static key for each distinct runtime contract.
/// The key validates slot compatibility; the position in the completed frame identifies
/// a particular invocation. Overloads that share the same state and behavior should share
/// the same key.
/// </remarks>
public sealed class UseHookKey
{
}
