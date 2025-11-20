using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura;
internal static class EnumerableExtensions
{
    public static bool IsEmpty<T>(this IEnumerable<T> source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return !source.Any();
    }
}
