using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Engine;
using Akbura.Workspaces.Projects;
using Avalonia;
using Avalonia.Browser;

namespace Akbura.Playground.Browser;

internal static class Program
{
    private const string ReferencePrefix = "PlaygroundReferences.";

    private static Task Main() => AppBuilder.Configure<PlaygroundApp>(() => new PlaygroundApp
        {
            PrepareReferences = ReadCompilerReferences,
#if AKBURA_WASM_THREADS
            Scheduler = new AkburaThreadPoolScheduler(),
#endif
        })
        .WithInterFont()
        .UseAkbura()
        .StartBrowserAppAsync("out");

    private static AkburaReferenceSet ReadCompilerReferences()
    {
        return AkburaReferenceSet.FromImages(ReadImages());

        static IEnumerable<byte[]> ReadImages()
        {
            var assembly = typeof(Program).Assembly;
            var found = false;
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(ReferencePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                found = true;
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Embedded metadata '{name}' is missing.");
                var image = new byte[checked((int)stream.Length)];
                stream.ReadExactly(image);
                yield return image;
            }

            if (!found)
            {
                throw new InvalidOperationException("The build did not embed compiler metadata.");
            }
        }
    }
}
