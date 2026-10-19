using Akbura.Language;
using Akbura.Language.CodeGeneration;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Akbura.BlackSilence;

internal static partial class DetachedArtifactReuse
{
    private static ArtifactReuseDecision ValidateSemanticDependencies(BlackSilenceGenerationRequest request, DetachedProjectArtifacts candidate,
        string path, DetachedCSharpDependencySet? facts, ImmutableArray<DependencyGenerationVersion> dependencies, bool diagnostic, CancellationToken cancellationToken)
    {
        if (request.ArtifactValidation.ChangeSet is not { } changes)
        {
            return ArtifactReuseDecision.ValidatedEquivalent;
        }
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.DocumentDependencyValidation);
#endif
        cancellationToken.ThrowIfCancellationRequested();
        var decision = ValidateFacts(facts, changes, cancellationToken);
        if (decision == ArtifactReuseDecision.ValidatedEquivalent)
        {
#if STATS
            using var propagation = GenerationStatistics.Measure(GenerationStatisticStage.DependencyGraphPropagation);
#endif
            // These are the current graph's transitive edges, not a separately
            // guessed positive-reference graph or a previous-version chain.
            foreach (var dependency in dependencies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (dependency.Document.FilePath == path)
                {
                    continue;
                }
                if (GlobalUsings.IsComponentFile(dependency.Document.SyntaxTree) || GlobalUsings.IsAkcssFile(dependency.Document.SyntaxTree))
                {
                    // Namespace/type membership is fixed by the change set.
                    // Active static/alias imports close the consumer proof itself.
                    continue;
                }
                var sourceFacts = candidate.Entries.Values.Where(artifact => artifact.DocumentPath == dependency.Document.FilePath)
                    .Select(static artifact => artifact.SemanticDependencies).ToArray();
                if (sourceFacts.Length == 0)
                {
                    decision = ArtifactReuseDecision.IncompleteSemanticDependencies;
                    break;
                }
                foreach (var dependencyFacts in sourceFacts)
                {
                    decision = dependency.IncludesBody ? ValidateFacts(dependencyFacts, changes, cancellationToken) :
                        dependencyFacts?.HasFixedExportedSurface == true ? ArtifactReuseDecision.ValidatedEquivalent : ArtifactReuseDecision.IncompleteSemanticDependencies;
                    if (decision != ArtifactReuseDecision.ValidatedEquivalent)
                    {
                        break;
                    }
                }
                if (decision != ArtifactReuseDecision.ValidatedEquivalent)
                {
                    break;
                }
            }
        }
#if STATS
        if (!diagnostic)
        {
            request.ObserveSemanticDocument(path, decision);
        }
        if (decision != ArtifactReuseDecision.ValidatedEquivalent)
        {
            GenerationStatistics.Increment(GenerationStatisticCounter.SemanticDependencyValidationFailures);
        }
#endif
        return decision;
    }

    private static ArtifactReuseDecision ValidateFacts(DetachedCSharpDependencySet? facts, DetachedCSharpChangeSet changes, CancellationToken cancellationToken)
    {
        if (facts?.IsComplete != true || facts.Schema != DetachedCSharpDependencySet.CurrentSchema)
        {
            return ArtifactReuseDecision.IncompleteSemanticDependencies;
        }
#if STATS
        using var negativeMeasurement = GenerationStatistics.Measure(GenerationStatisticStage.NegativeLookupValidation);
        using var candidateMeasurement = GenerationStatistics.Measure(GenerationStatisticStage.CandidateSetValidation);
#endif
        cancellationToken.ThrowIfCancellationRequested();
        // The class/type/import universe is fixed. Every receiver's entire source
        // contract, including an empty candidate space, is guarded by this key.
        foreach (var dependency in facts.SourceTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dependency.AssemblyIdentity != changes.AssemblyIdentity)
            {
                continue;
            }
            foreach (var change in changes.Changes)
            {
                if (dependency.MetadataName == change.MetadataName)
                {
                    return change.Kind == "MemberCandidateSetChanged" ? ArtifactReuseDecision.AffectedByLookupCandidateChange :
                        ArtifactReuseDecision.AffectedByChangedSymbol;
                }
            }
        }
        return ArtifactReuseDecision.ValidatedEquivalent;
    }
}
