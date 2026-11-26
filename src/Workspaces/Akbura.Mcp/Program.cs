using Akbura.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

Console.SetOut(Console.Error);
if (args is ["--help"] or ["-h"])
{
    Console.Error.WriteLine(ProjectOptions.Usage);
    return 0;
}

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += OnCancel;
try
{
    var options = ProjectOptions.Parse(args);
    Environment.CurrentDirectory = Path.GetDirectoryName(options.ProjectPath)!;
    using var session = await ProjectSession.OpenAsync(options, shutdown.Token);

    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
    builder.Logging.AddConsole(settings => settings.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddSingleton(session);
    builder.Services.AddMcpServer(settings =>
    {
        settings.ServerInfo = new Implementation { Name = "Akbura MCP", Version = McpTools.ServerVersion };
        settings.ServerInstructions = McpTools.Instructions;
    })
        .WithStdioServerTransport()
        .WithTools<McpTools>(McpTools.JsonOptions);

    using var host = builder.Build();
    await host.RunAsync(shutdown.Token);
    return 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Akbura MCP: {exception.Message}");
    return 1;
}
finally
{
    Console.CancelKeyPress -= OnCancel;
}

void OnCancel(object? sender, ConsoleCancelEventArgs args)
{
    args.Cancel = true;
    shutdown.Cancel();
}
