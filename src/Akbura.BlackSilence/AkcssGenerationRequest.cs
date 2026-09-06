using Akbura.Language.CodeGeneration;

namespace Akbura.BlackSilence;

internal readonly struct AkcssGenerationRequest
{
    public AkcssGenerationRequest(
        AkcssDocumentDescriptor descriptor,
        DocumentGenerationVersion version,
        GeneratedDocumentEntry? previous)
    {
        Descriptor = descriptor;
        Version = version;
        Previous = previous;
    }

    public AkcssDocumentDescriptor Descriptor { get; }
    public DocumentGenerationVersion Version { get; }
    public GeneratedDocumentEntry? Previous { get; }
    public string Identity => "akcss:" + Descriptor.ModuleIdentity;
}
