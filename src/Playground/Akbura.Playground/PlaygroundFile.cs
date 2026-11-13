using Avalonia.Platform;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Playground;

internal sealed class PlaygroundFile
{
    public PlaygroundFile(string assetPath)
    {
        FilePath = Path.GetFullPath(assetPath);
        using var reader = new StreamReader(AssetLoader.Open(new Uri("avares://Akbura.Playground/" + assetPath)));
        Text = SourceText.From(reader.ReadToEnd());
    }

    public string FilePath
    {
        get;
    }

    public SourceText Text
    {
        get; set;
    }

    public override string ToString() => Path.GetFileName(FilePath);

    public static Uri GetDocumentUri(string filePath) =>
        new UriBuilder { Scheme = Uri.UriSchemeFile, Host = string.Empty, Path = filePath }.Uri;
}
