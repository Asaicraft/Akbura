using Akbura.Diagnostics;
using Akbura.Language.CodeGeneration;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Akbura.BlackSilence;

internal static partial class DetachedArtifactReuse
{
    internal static ArtifactValidation Validate(BlackSilenceGenerationRequest request, DetachedProjectArtifacts? candidate, CancellationToken cancellationToken = default)
    {
        if (candidate == null)
        {
            return new(ArtifactReuseDecision.MissingArtifact, "NoDetachedArtifacts", [], []);
        }
        if (candidate.Schema != DetachedProjectArtifacts.CurrentSchema)
        {
            return new(ArtifactReuseDecision.RequiresRebinding, "ArtifactSchemaChanged", [], []);
        }
        if (candidate.Options != request.Options || candidate.Documents.Length != request.DetachedDocuments.Length ||
            candidate.Documents.Where((document, index) => !document.HasSameDeclarations(request.DetachedDocuments[index])).Any())
        {
            return new(ArtifactReuseDecision.RequiresRebinding, "DocumentInputsOrProjectOptionsChanged", [], []);
        }
        var validation = candidate.CSharpInputs.Validate(request.ArtifactInputs!, cancellationToken);
        if (validation.Decision is ArtifactReuseDecision.ValidatedEquivalent or ArtifactReuseDecision.ValidateDocumentDependencies)
        {
            var implicitNames = request.Index.ComponentDescriptors.Select(static descriptor => descriptor.ComponentMetadataName)
                .Concat(request.Index.ExternalAkcssDescriptors.Select(static descriptor => descriptor.GeneratedTypeName))
                .Concat(request.Index.InlineAkcssDescriptors.Select(static descriptor => descriptor.GeneratedTypeName))
                .SelectMany(static lookup => Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseTokens(lookup)
                    .Select(static token => token.ValueText).Prepend(lookup));
            var changedTypes = validation.ChangedTypes.Concat(validation.MemberTypes.IsDefault ? [] : validation.MemberTypes)
                .Concat(validation.ChangeSet?.Changes.SelectMany(static change => new[] { change.MetadataName, change.MetadataName.Split('.').Last() }) ?? []);
            if (implicitNames.Any(lookup => changedTypes.Any(name => lookup.IndexOf(name, System.StringComparison.Ordinal) >= 0)))
            {
                return new(ArtifactReuseDecision.UnsupportedDependency, "GeneratedDeclarationLookupCollision", [], []);
            }
        }
        return validation;
    }

    internal static ImmutableArray<ComponentGenerationRequest> Components(BlackSilenceGenerationRequest request, DetachedProjectArtifacts? candidate, CancellationToken cancellationToken)
    {
        return request.Components.Select(component => new ComponentGenerationRequest(component.Descriptor, component.Version,
            component.AkcssModuleTypeNames, Source(request, candidate, component.Identity, component.Version, cancellationToken))).ToImmutableArray();
    }

    internal static ImmutableArray<AkcssGenerationRequest> Akcss(BlackSilenceGenerationRequest request,
        ImmutableArray<AkcssGenerationRequest> modules, DetachedProjectArtifacts? candidate, CancellationToken cancellationToken)
    {
        return modules.Select(module => new AkcssGenerationRequest(module.Descriptor, module.Version,
            Source(request, candidate, module.Identity, module.Version, cancellationToken))).ToImmutableArray();
    }

    private static GeneratedDocumentEntry? Source(BlackSilenceGenerationRequest request, DetachedProjectArtifacts? candidate,
        string identity, DocumentGenerationVersion version, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decision = Decision(request, candidate, version.Document.FilePath);
        if (candidate == null || !candidate.Entries.TryGetValue(identity, out var artifact))
        {
            decision = ArtifactReuseDecision.MissingArtifact;
        }
        else if (decision == ArtifactReuseDecision.ValidatedEquivalent && !DetachedDependencyInput.Matches(artifact.Dependencies, version.Dependencies))
        {
            decision = ArtifactReuseDecision.RequiresRebinding;
        }
        else if (decision == ArtifactReuseDecision.ValidatedEquivalent)
        {
            decision = ValidateSemanticDependencies(request, candidate, version.Document.FilePath, artifact.SemanticDependencies, version.Dependencies, diagnostic: false, cancellationToken);
            if (decision == ArtifactReuseDecision.ValidatedEquivalent)
            {
#if STATS
                using var selectiveMeasurement = GenerationStatistics.Measure(GenerationStatisticStage.SelectiveArtifactReuse);
#endif
                Observe(request, version.Document.FilePath, identity, decision, diagnostic: false);
                // Wrap the detached characters with the CURRENT compilation's version.
                return new GeneratedDocumentEntry(identity, version, artifact.HintName, artifact.Source, artifact.SemanticDependencies);
            }
        }
        Observe(request, version.Document.FilePath, identity, decision, diagnostic: false);
        return null;
    }

    internal static ImmutableArray<DocumentDiagnosticRequest> Diagnostics(BlackSilenceGenerationRequest request, DetachedProjectArtifacts? candidate, CancellationToken cancellationToken)
    {
        return request.Diagnostics.Select(diagnostic =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = Decision(request, candidate, diagnostic.Document.FilePath);
            DiagnosticDocumentEntry? previous = null;
            if (candidate == null || !candidate.DiagnosticsComputed || !candidate.Diagnostics.TryGetValue(diagnostic.Document.FilePath, out var artifact))
            {
                decision = ArtifactReuseDecision.MissingArtifact;
            }
            else if (decision == ArtifactReuseDecision.ValidatedEquivalent)
            {
                decision = ValidateSemanticDependencies(request, candidate, diagnostic.Document.FilePath, artifact.SemanticDependencies,
                    diagnostic.Version.Dependencies, diagnostic: true, cancellationToken);
                // Diagnostics have their own mapping dependencies, independently
                // of generated-source eligibility. Never restore stale locations.
                if (decision == ArtifactReuseDecision.ValidatedEquivalent && (!DetachedDependencyInput.Matches(artifact.Dependencies, diagnostic.Version.Dependencies) ||
                    artifact.Records.Any(record => request.ArtifactValidation.ChangedPaths.Contains(record.FilePath) ||
                    record.AdditionalLocations.Any(location => request.ArtifactValidation.ChangedPaths.Contains(location.FilePath)))))
                {
                    decision = ArtifactReuseDecision.RequiresRebinding;
                }
                else if (decision == ArtifactReuseDecision.ValidatedEquivalent)
                {
#if STATS
                    using var selectiveMeasurement = GenerationStatistics.Measure(GenerationStatisticStage.SelectiveDiagnosticReuse);
#endif
                    previous = new DiagnosticDocumentEntry(diagnostic.Version,
                        artifact.Records.Select(record => record with { DocumentVersion = request.Version }).ToImmutableArray(), artifact.SemanticDependencies);
                }
            }
            Observe(request, diagnostic.Document.FilePath, diagnostic.Document.FilePath, decision, diagnostic: true);
            return new DocumentDiagnosticRequest(diagnostic.Document, diagnostic.Version, previous);
        }).ToImmutableArray();
    }

    private static ArtifactReuseDecision Decision(BlackSilenceGenerationRequest request, DetachedProjectArtifacts? candidate, string path)
    {
        if (request.ArtifactValidation.Decision is not (ArtifactReuseDecision.ValidatedEquivalent or ArtifactReuseDecision.ValidateDocumentDependencies))
        {
            return request.ArtifactValidation.Decision;
        }
        var document = request.DetachedDocuments.Single(document => document.Path == path);
        var previous = candidate?.Documents.SingleOrDefault(document => document.Path == path);
        if (previous == null || !document.Matches(previous))
        {
            return ArtifactReuseDecision.RequiresRebinding;
        }
        return document.Excludes(request.ArtifactValidation.ChangedTypes)
            ? ArtifactReuseDecision.ValidatedEquivalent : ArtifactReuseDecision.UnsupportedDependency;
    }

    private static void Observe(BlackSilenceGenerationRequest request, string path, string identity,
        ArtifactReuseDecision decision, bool diagnostic)
    {
#if STATS
        if (decision == ArtifactReuseDecision.ValidatedEquivalent)
        {
            GenerationStatistics.Increment(diagnostic ? GenerationStatisticCounter.DetachedDiagnosticsReused : GenerationStatisticCounter.CrossCompilationArtifactHit);
        }
        else if (!diagnostic && request.HasArtifactCandidate)
        {
            GenerationStatistics.Increment(GenerationStatisticCounter.ArtifactValidationFallback);
        }
        if (GenerationStatistics.IsInvalidationTrackingEnabled)
        {
            var reason = request.ArtifactValidation.Decision is ArtifactReuseDecision.ValidatedEquivalent or ArtifactReuseDecision.ValidateDocumentDependencies
                ? decision switch
                {
                    ArtifactReuseDecision.UnsupportedDependency => "DocumentOrGlobalLookupNotProven",
                    ArtifactReuseDecision.RequiresRebinding => diagnostic ? "DiagnosticInputsOrMappingChanged" : "DocumentInputsOrDependenciesChanged",
                    ArtifactReuseDecision.MissingArtifact => diagnostic ? "DetachedDiagnosticsUnavailable" : "DetachedSourceUnavailable",
                    ArtifactReuseDecision.AffectedByChangedSymbol => "AffectedByChangedTypeContract",
                    ArtifactReuseDecision.AffectedByLookupCandidateChange => "AffectedByLookupCandidateChange",
                    ArtifactReuseDecision.IncompleteSemanticDependencies => "IncompleteSemanticDependencies",
                    _ => request.ArtifactValidation.Reason,
                }
                : request.ArtifactValidation.Reason;
            BlackSilenceInvalidationTrace.Record(diagnostic ? "DetachedDiagnosticValidation" : "DetachedArtifactValidation", path,
                decision.ToString(), [reason], ("Identity", identity));
        }
#endif
    }
}
