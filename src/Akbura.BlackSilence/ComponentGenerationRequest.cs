using Akbura.Language.CodeGeneration;
using System.Collections.Immutable;

namespace Akbura.BlackSilence;

internal readonly struct ComponentGenerationRequest
{
    public ComponentGenerationRequest(
        ComponentDocumentDescriptor descriptor,
        DocumentGenerationVersion version,
        ImmutableArray<string> akcssModuleTypeNames,
        GeneratedDocumentEntry? previous)
    {
        Descriptor = descriptor;
        Version = version;
        AkcssModuleTypeNames = akcssModuleTypeNames;
        Previous = previous;
    }

    public ComponentDocumentDescriptor Descriptor { get; }
    public DocumentGenerationVersion Version { get; }
    public ImmutableArray<string> AkcssModuleTypeNames { get; }
    public GeneratedDocumentEntry? Previous { get; }
    public string Identity => "component:" + Descriptor.SourcePath;
}
