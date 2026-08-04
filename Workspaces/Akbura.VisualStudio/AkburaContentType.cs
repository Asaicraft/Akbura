using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Utilities;

namespace Akbura.VisualStudio;

internal static class AkburaContentTypeNames
{
    public const string Akbura = "Akbura";
}

internal static class AkburaContentTypeDefinitions
{
    [Export]
    [Name(AkburaContentTypeNames.Akbura)]
    [BaseDefinition(StandardContentTypeNames.Code)]
    internal static ContentTypeDefinition AkburaContentType = null!;

    [Export]
    [FileExtension(".akbura")]
    [ContentType(AkburaContentTypeNames.Akbura)]
    internal static FileExtensionToContentTypeDefinition
        AkburaFileExtension = null!;
}