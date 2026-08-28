// This file is ported and adopted from roslyn

namespace Akbura;
internal static class ObjectDisplayExtensions
{
    /// <summary>
    /// Determines if a flag is set on the <see cref="ObjectDisplayOptions"/> enum.
    /// </summary>
    /// <param name="options">The value to check.</param>
    /// <param name="flag">An enum field that specifies the flag.</param>
    /// <returns>Whether the <paramref name="flag"/> is set on the <paramref name="options"/>.</returns>
    internal static bool IncludesOption(this ObjectDisplayOptions options, ObjectDisplayOptions flag)
    {
        return (options & flag) == flag;
    }
}