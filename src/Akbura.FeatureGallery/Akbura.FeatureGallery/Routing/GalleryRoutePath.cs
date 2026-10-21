using System;

namespace Akbura.FeatureGallery.Routing;

/// <summary>Logical routes only: never an origin or a deployment prefix.</summary>
public static class GalleryRoutePath
{
    public const string Overview = "/overview";

    public static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value = value.Trim();
        // Queries and fragments do not select gallery pages.
        var suffix = value.IndexOfAny(['?', '#']);
        if (suffix >= 0) value = value[..suffix];
        if (value.StartsWith("//", StringComparison.Ordinal)) throw InvalidRoute();
        value = value.Trim('/');
        if (value.Length == 0) return Overview;
        foreach (var segment in value.Split('/'))
        {
            if (segment.Length == 0) throw InvalidRoute();
            foreach (var character in segment)
            {
                // Current page slugs are ASCII identifiers. Reject protocols,
                // escaping, dot segments and encoded path separators outright.
                if (!char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_') throw InvalidRoute();
            }
        }
        return "/" + value.ToLowerInvariant();
    }

    private static ArgumentException InvalidRoute() => new("Expected a logical gallery route, not an external URL or traversal path.", "value");
}
