using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Playground;

internal sealed class PlaygroundExample
{
    private readonly string _name;

    public PlaygroundExample(string name, string entryFile, params string[] additionalFiles)
    {
        _name = name;
        EntryFile = new PlaygroundFile(entryFile);
        using var files = ImmutableArrayBuilder<PlaygroundFile>.Rent(additionalFiles.Length + 1);
        files.Add(EntryFile);
        foreach (var file in additionalFiles)
        {
            files.Add(new PlaygroundFile(file));
        }
        Files = files.ToImmutable();
    }

    public PlaygroundFile EntryFile
    {
        get;
    }

    public ImmutableArray<PlaygroundFile> Files
    {
        get;
    }

    public ImmutableArray<(string FilePath, SourceText Text)> GetSources()
    {
        using var sources = ImmutableArrayBuilder<(string, SourceText)>.Rent(Files.Length);
        foreach (var file in Files)
        {
            sources.Add((file.FilePath, file.Text));
        }
        return sources.ToImmutable();
    }

    public override string ToString() => _name;
}
