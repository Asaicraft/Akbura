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

        var root = workspaceFolder.LocalPath;
        foreach (var pattern in new[] { "*.slnx", "*.sln", "*.csproj" })
        {
            var matches = Directory
                .EnumerateFiles(
                    root,
                    pattern,
                    SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (matches.Length == 1)
            {
                return Path.GetFullPath(matches[0]);
            }

            if (matches.Length > 1)
            {
                warning =
                    $"Multiple '{pattern}' files were found in '{root}'. " +
                    "Use --solution or --project to select one.";
                return null;
            }
        }

        return null;
    }
}
