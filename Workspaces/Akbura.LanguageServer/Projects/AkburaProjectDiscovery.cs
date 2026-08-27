namespace Akbura.LanguageServer.Projects;

internal static class AkburaProjectDiscovery
{
    public static string? Discover(
        Uri workspaceFolder,
        string? explicitPath,
        out string? warning)
    {
        warning = null;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var fullExplicitPath = Path.GetFullPath(explicitPath);
            if (!File.Exists(fullExplicitPath))
            {
                throw new FileNotFoundException(
                    "The explicitly selected solution or project was not found.",
                    fullExplicitPath);
            }

            return fullExplicitPath;
        }

        if (!workspaceFolder.IsFile ||
            !Directory.Exists(workspaceFolder.LocalPath))
        {
            return null;
        }

        var root = Path.GetFullPath(workspaceFolder.LocalPath);
        var topLevel = FindCandidates(
            root,
            SearchOption.TopDirectoryOnly);
        if (topLevel.Length == 1)
        {
            return topLevel[0];
        }

        if (topLevel.Length > 1)
        {
            warning =
                $"Multiple solutions or projects were found in '{root}'. " +
                "Use --solution or --project to select one.";
            return null;
        }

        var recursive = FindCandidates(
            root,
            SearchOption.AllDirectories);
        if (recursive.Length == 1)
        {
            return recursive[0];
        }

        if (recursive.Length > 1)
        {
            warning =
                $"Multiple solutions or projects were found under '{root}'. " +
                "Use --solution or --project to select one.";
        }

        return null;
    }

    private static string[] FindCandidates(
        string root,
        SearchOption searchOption)
    {
        foreach (var pattern in new[]
                 {
                     "*.slnx",
                     "*.sln",
                     "*.csproj",
                 })
        {
            var matches = Directory
                .EnumerateFiles(root, pattern, searchOption)
                .Where(path => !IsIgnoredPath(root, path))
                .Select(Path.GetFullPath)
                .OrderBy(
                    static path => path,
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (matches.Length != 0)
            {
                return matches;
            }
        }

        return [];
    }

    private static bool IsIgnoredPath(
        string root,
        string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var segments = relativePath.Split(
            [
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar,
            ],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(static segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(
                "node_modules",
                StringComparison.OrdinalIgnoreCase));
    }
}
