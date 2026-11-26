namespace Akbura.Mcp;

internal sealed record ProjectOptions(string ProjectPath, string Configuration, string? TargetFramework)
{
    public const string Usage =
        "Usage: dotnet akbura-mcp.dll --project <absolute-csproj-path> " +
        "[--configuration <name>] [--target-framework <tfm>]";

    public static ProjectOptions Parse(string[] args)
    {
        string? project = null;
        string? configuration = null;
        string? framework = null;
        for (var index = 0; index < args.Length; index += 2)
        {
            var option = args[index];
            if (index + 1 == args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(args[index + 1]))
            {
                throw new ArgumentException($"A value is required for {option}.");
            }

            var value = args[index + 1];
            switch (option)
            {
                case "--project" when project == null:
                    project = value;
                    break;
                case "--configuration" when configuration == null:
                    configuration = value;
                    break;
                case "--target-framework" when framework == null:
                    framework = value;
                    break;
                default:
                    throw new ArgumentException($"Unknown or repeated option '{option}'.");
            }
        }

        if (project == null || !Path.IsPathFullyQualified(project) ||
            !Path.GetExtension(project).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("--project must specify an absolute .csproj path.");
        }

        project = Path.GetFullPath(project);
        if (!File.Exists(project))
        {
            throw new FileNotFoundException("The selected project does not exist.", project);
        }

        return new(project, configuration ?? "Debug", framework);
    }
}
