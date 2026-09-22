namespace Akbura.Workspaces.Resources;

internal static class ResourceIncludeResolver
{
    private const string AvaresPrefix = "avares://";

    public static bool TryResolve(string? source, ResourceDictionaryIdentity containingDictionary, IEnumerable<ResourceAssemblyIdentity> availableAssemblies, out ResourceDictionaryIdentity dictionary)
    {
        dictionary = default;
        if (availableAssemblies == null)
        {
            throw new ArgumentNullException(nameof(availableAssemblies));
        }

        if (source == null ||
            string.IsNullOrWhiteSpace(source) ||
            !string.Equals(source, source.Trim(), StringComparison.Ordinal) ||
            source.IndexOf('?') >= 0 ||
            source.IndexOf('#') >= 0 ||
            source.IndexOf('\0') >= 0 ||
            ContainsEncodedSeparator(source))
        {
            return false;
        }

        var assembly = containingDictionary.Assembly;
        string path;
        if (source.StartsWith(
                AvaresPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            var authorityStart = AvaresPrefix.Length;
            var slash = source.IndexOf('/', authorityStart);
            if (slash <= authorityStart || slash == source.Length - 1)
            {
                return false;
            }

            var assemblyName = source.Substring(
                authorityStart,
                slash - authorityStart);
            if (assemblyName.IndexOfAny(['/', '\\', ':', '@']) >= 0 ||
                !TryResolveAssembly(
                    assemblyName,
                    containingDictionary.Assembly,
                    availableAssemblies,
                    out assembly))
            {
                return false;
            }

            path = source.Substring(slash + 1);
        }
        else if (source.IndexOf(':') >= 0 ||
                 source.StartsWith("//", StringComparison.Ordinal) ||
                 source.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return false;
        }
        else if (source[0] is '/' or '\\')
        {
            if (source.Length > 1 && source[1] is '/' or '\\')
            {
                return false;
            }

            path = source.Substring(1);
        }
        else
        {
            var containingPath = containingDictionary.Path.Value;
            var separator = containingPath.LastIndexOf('/');
            path = separator < 0
                ? source
                : containingPath.Substring(0, separator + 1) + source;
        }

        if (!TryDecodePath(path, out var decoded) ||
            !ResourcePath.TryCreateExportPath(
                decoded,
                out var resourcePath))
        {
            return false;
        }

        dictionary = new ResourceDictionaryIdentity(
            assembly,
            resourcePath);
        return true;
    }

    private static bool TryResolveAssembly(string assemblyName, ResourceAssemblyIdentity containingAssembly, IEnumerable<ResourceAssemblyIdentity> availableAssemblies, out ResourceAssemblyIdentity assembly)
    {
        assembly = default;
        var found = false;

        if (containingAssembly.HasSimpleName(assemblyName))
        {
            assembly = containingAssembly;
            found = true;
        }

        foreach (var candidate in availableAssemblies)
        {
            if (!candidate.HasSimpleName(assemblyName) ||
                found && candidate == assembly)
            {
                continue;
            }

            if (found)
            {
                assembly = default;
                return false;
            }

            assembly = candidate;
            found = true;
        }

        return found;
    }

    private static bool TryDecodePath(string path, out string decoded)
    {
        decoded = string.Empty;
        for (var index = 0; index < path.Length; index++)
        {
            if (path[index] != '%')
            {
                continue;
            }

            if (index + 2 >= path.Length ||
                !IsHexDigit(path[index + 1]) ||
                !IsHexDigit(path[index + 2]))
            {
                return false;
            }

            index += 2;
        }

        try
        {
            decoded = Uri.UnescapeDataString(path);
            return decoded.IndexOf('%') < 0;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool IsHexDigit(char value)
    {
        return value is >= '0' and <= '9' or
            >= 'A' and <= 'F' or
            >= 'a' and <= 'f';
    }

    private static bool ContainsEncodedSeparator(string source)
    {
        return source.IndexOf(
                   "%2f",
                   StringComparison.OrdinalIgnoreCase) >= 0 ||
               source.IndexOf(
                   "%5c",
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
