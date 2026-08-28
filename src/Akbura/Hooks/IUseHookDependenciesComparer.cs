namespace Akbura.Hooks;

public interface IUseHookDependenciesComparer
{
    bool Equals(
        ReadOnlySpan<object?> previousDependencies,
        ReadOnlySpan<object?> currentDependencies);
}
