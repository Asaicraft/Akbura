using Akbura.Language.CodeGeneration;

namespace Akbura.BlackSilence;

internal readonly struct ComponentGenerationRequest
{
    public ComponentGenerationRequest(
        ComponentDocumentDescriptor descriptor,
        DocumentGenerationVersion version,
        GeneratedDocumentEntry? previous)
    {
        Descriptor = descriptor;
        Version = version;
        Previous = previous;
    }

    public ComponentDocumentDescriptor Descriptor { get; }
    public DocumentGenerationVersion Version { get; }
    public GeneratedDocumentEntry? Previous { get; }
    public string Identity => "component:" + Descriptor.SourcePath;
}
