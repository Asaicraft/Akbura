using Akbura.LanguageServer.Hosting;
using System.Diagnostics;

namespace Akbura.LanguageServer;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        AkburaServerOptions options;
        try
        {
            options = AkburaServerOptions.Parse(args);
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.Message)
                .ConfigureAwait(false);
            return 2;
        }

        if (!options.UseStdio)
        {
            await Console.Error.WriteLineAsync(
                    "Only --stdio transport is supported in this release.")
                .ConfigureAwait(false);
            return 2;
        }

        if (options.WaitForDebugger)
        {
            while (!Debugger.IsAttached)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        try
        {
            return await AkburaLanguageServerHost.RunAsync(
                    Console.OpenStandardInput(),
                    Console.OpenStandardOutput(),
                    options,
                    Console.Error)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString())
                .ConfigureAwait(false);
            return 1;
        }
    }
}
