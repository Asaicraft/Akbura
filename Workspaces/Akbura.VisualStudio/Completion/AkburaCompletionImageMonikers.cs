using Microsoft.VisualStudio.Imaging.Interop;

namespace Akbura.VisualStudio.Completion;

internal static class AkburaCompletionImageMonikers
{
    private static readonly Guid s_imageCatalogGuid =
        new("A4BE0232-1E36-4B93-93D8-6614639B6B32");

    public static ImageMoniker State { get; } = new()
    {
        Guid = s_imageCatalogGuid,
        Id = 3,
    };
}
