using Akbura.Language.CodeGeneration;
using System.Collections.Immutable;

namespace Akbura.BlackSilence;

internal sealed class BlackSilenceGenerationRequest
{
    public BlackSilenceGenerationRequest(
        BlackSilenceProjectState state,
        long version,
        GeneratorProjectOptions options,
        AkburaProjectIndex index,
        ImmutableArray<DocumentSyntaxVersion> documents,
        object declarationEnvironment,
        ImmutableArray<ComponentGenerationRequest> components,
        ImmutableArray<AkcssGenerationRequest> externalAkcss,
        ImmutableArray<AkcssGenerationRequest> inlineAkcss,
        BlackSilenceProjectSnapshot? previousSnapshot,
        ImmutableArray<DocumentDiagnosticRequest> diagnostics = default,
        bool computeDiagnostics = true)
    {
        State = state;
        Version = version;
        Options = options;
        Index = index;
        Documents = documents;
        DeclarationEnvironment = declarationEnvironment;
        Components = components;
        ExternalAkcss = externalAkcss;
        InlineAkcss = inlineAkcss;
        PreviousSnapshot = previousSnapshot;
        Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        ComputeDiagnostics = computeDiagnostics;
    }

    public BlackSilenceProjectState State { get; }
    public long Version { get; }
    public GeneratorProjectOptions Options { get; }
    public AkburaProjectIndex Index { get; }
    public ImmutableArray<DocumentSyntaxVersion> Documents { get; }
    public object DeclarationEnvironment { get; }
    public ImmutableArray<ComponentGenerationRequest> Components { get; }
    public ImmutableArray<AkcssGenerationRequest> ExternalAkcss { get; }
    public ImmutableArray<AkcssGenerationRequest> InlineAkcss { get; }
    public BlackSilenceProjectSnapshot? PreviousSnapshot { get; }
    public ImmutableArray<DocumentDiagnosticRequest> Diagnostics { get; }
    public bool ComputeDiagnostics { get; }
}
