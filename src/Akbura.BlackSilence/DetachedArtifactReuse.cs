using Akbura.Diagnostics;
using Akbura.Language.CodeGeneration;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.BlackSilence;

internal static class DetachedArtifactReuse
{
    internal static ArtifactValidation Validate(BlackSilenceGenerationRequest request, DetachedProjectArtifacts? candidate)
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
            candidate.Documents.Where((document, index) => !document.Matches(request.DetachedDocuments[index])).Any())
        {
            return new(ArtifactReuseDecision.RequiresRebinding, "DocumentInputsOrProjectOptionsChanged", [], []);
        }
        var validation = candidate.CSharpInputs.Validate(request.ArtifactInputs!);
        if (validation.Decision == ArtifactReuseDecision.ValidatedEquivalent)
        {
            var implicitNames = request.Index.ComponentDescriptors.Select(static descriptor => descriptor.ComponentMetadataName)
                .Concat(request.Index.ExternalAkcssDescriptors.Select(static descriptor => descriptor.GeneratedTypeName))
                .Concat(request.Index.InlineAkcssDescriptors.Select(static descriptor => descriptor.GeneratedTypeName))
                .SelectMany(static lookup => Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseTokens(lookup)
                    .Select(static token => token.ValueText).Prepend(lookup));
            if (implicitNames.Any(lookup => validation.ChangedTypes.Any(name => lookup.IndexOf(name, System.StringComparison.Ordinal) >= 0)))
            {
                return new(ArtifactReuseDecision.UnsupportedDependency, "GeneratedDeclarationLookupCollision", [], []);
            }
        }
        return validation;
    }

    internal static ImmutableArray<ComponentGenerationRequest> Components(BlackSilenceGenerationRequest request, DetachedProjectArtifacts? candidate)
    {
        return request.Components.Select(component => new ComponentGenerationRequest(component.Descriptor, component.Version,
            component.AkcssModuleTypeNames, Source(request, candidate, component.Identity, component.Version))).ToImmutableArray();
    }

    internal static ImmutableArray<AkcssGenerationRequest> Akcss(BlackSilenceGenerationRequest request,
        ImmutableArray<AkcssGenerationRequest> modules, DetachedProjectArtifacts? candidate)
    {
        return modules.Select(module => new AkcssGenerationRequest(module.Descriptor, module.Version,
            Source(request, candidate, module.Identity, module.Version))).ToImmutableArray();
    }

    private static GeneratedDocumentEntry? Source(BlackSilenceGenerationRequest request, DetachedProjectArtifacts? candidate,
        string identity, DocumentGenerationVersion version)
    {
        var decision = Decision(request, version.Document.FilePath);
        if (candidate == null || !candidate.Entries.TryGetValue(identity, out var artifact))
        {
            decision = ArtifactReuseDecision.MissingArtifact;
        }
        else if (decision == ArtifactReuseDecision.ValidatedEquivalent)
        {
            Observe(request, version.Document.FilePath, identity, decision, diagnostic: false);
            // Wrap the detached characters with the CURRENT compilation's version.
            return new GeneratedDocumentEntry(identity, version, artifact.HintName, artifact.Source);
        }
        Observe(request, version.Document.FilePath, identity, decision, diagnostic: false);
        return null;
    }

    internal static ImmutableArray<DocumentDiagnosticRequest> Diagnostics(BlackSilenceGenerationRequest request, DetachedProjectArtifacts? candidate)
    {
        return request.Diagnostics.Select(diagnostic =>
        {
            var decision = Decision(request, diagnostic.Document.FilePath);
            DiagnosticDocumentEntry? previous = null;
            if (candidate == null || !candidate.DiagnosticsComputed || !candidate.Diagnostics.TryGetValue(diagnostic.Document.FilePath, out var records))
            {
                decision = ArtifactReuseDecision.MissingArtifact;
            }
            else if (decision == ArtifactReuseDecision.ValidatedEquivalent)
            {
                // Diagnostics have their own mapping dependencies, independently
                // of generated-source eligibility. Never restore stale locations.
                if (records.Any(record => request.ArtifactValidation.ChangedPaths.Contains(record.FilePath) ||
                    record.AdditionalLocations.Any(location => request.ArtifactValidation.ChangedPaths.Contains(location.FilePath))))
                {
                    decision = ArtifactReuseDecision.RequiresRebinding;
                }
                else
                {
                    previous = new DiagnosticDocumentEntry(diagnostic.Version,
                        records.Select(record => record with { DocumentVersion = request.Version }).ToImmutableArray());
                }
            }
            Observe(request, diagnostic.Document.FilePath, diagnostic.Document.FilePath, decision, diagnostic: true);
            return new DocumentDiagnosticRequest(diagnostic.Document, diagnostic.Version, previous);
        }).ToImmutableArray();
    }

    private static ArtifactReuseDecision Decision(BlackSilenceGenerationRequest request, string path)
    {
        if (request.ArtifactValidation.Decision != ArtifactReuseDecision.ValidatedEquivalent)
        {
            return request.ArtifactValidation.Decision;
        }
        var document = request.DetachedDocuments.Single(document => document.Path == path);
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
            var reason = request.ArtifactValidation.Decision == ArtifactReuseDecision.ValidatedEquivalent
                ? decision switch
                {
                    ArtifactReuseDecision.UnsupportedDependency => "DocumentOrGlobalLookupNotProven",
                    ArtifactReuseDecision.RequiresRebinding => "DiagnosticMappingChanged",
                    ArtifactReuseDecision.MissingArtifact => diagnostic ? "DetachedDiagnosticsUnavailable" : "DetachedSourceUnavailable",
                    _ => request.ArtifactValidation.Reason,
                }
                : request.ArtifactValidation.Reason;
            BlackSilenceInvalidationTrace.Record(diagnostic ? "DetachedDiagnosticValidation" : "DetachedArtifactValidation", path,
                decision.ToString(), [reason], ("Identity", identity));
        }
#endif
    }
}
