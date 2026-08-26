namespace Akbura.LanguageServer.Hosting;

internal sealed class AkburaServerOptions
{
    public bool UseStdio { get; private set; } = true;

    public int? ClientProcessId { get; private set; }

    public string? SolutionPath { get; private set; }

    public string? ProjectPath { get; private set; }

    public string? LogFile { get; private set; }

    public AkburaServerLogLevel LogLevel { get; private set; } =
        AkburaServerLogLevel.Warning;

    public bool WaitForDebugger { get; private set; }

    public static AkburaServerOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new AkburaServerOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--stdio":
                    options.UseStdio = true;
                    break;
                case "--clientProcessId":
                    options.ClientProcessId = int.Parse(
                        ReadValue(args, ref index, argument));
                    break;
                case "--solution":
                    options.SolutionPath = Path.GetFullPath(
                        ReadValue(args, ref index, argument));
                    break;
                case "--project":
                    options.ProjectPath = Path.GetFullPath(
                        ReadValue(args, ref index, argument));
                    break;
                case "--log-file":
                    options.LogFile = Path.GetFullPath(
                        ReadValue(args, ref index, argument));
                    break;
                case "--log-level":
                    options.LogLevel = Enum.Parse<AkburaServerLogLevel>(
                        ReadValue(args, ref index, argument),
                        ignoreCase: true);
                    break;
                case "--wait-for-debugger":
                    options.WaitForDebugger = true;
                    break;
                default:
                    throw new ArgumentException(
                        $"Unknown argument '{argument}'.");
            }
        }

        if (options.SolutionPath != null &&
            options.ProjectPath != null)
        {
            throw new ArgumentException(
                "Specify either --solution or --project, not both.");
        }

        return options;
    }

    private static string ReadValue(
        string[] args,
        ref int index,
        string argument)
    {
        index++;
        if (index >= args.Length ||
            string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException(
                $"Argument '{argument}' requires a value.");
        }

        return args[index];
    }
}