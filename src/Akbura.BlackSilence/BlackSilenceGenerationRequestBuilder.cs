using Akbura.Language.CodeGeneration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Akbura.BlackSilence;

internal static class BlackSilenceGenerationRequestBuilder
{
    public static BlackSilenceGenerationRequest? Create(
        ImmutableArray<DocumentSyntaxVersion> documents,
        BlackSilenceProjectState? state,
        GeneratorProjectOptions options,
        CancellationToken cancellationToken,
        bool computeDiagnostics = true)
    {
        if (state == null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var previous = state.TryGetSnapshot(options);
        var version = state.GetNextVersion();
        var orderedDocuments = OrderDocuments(documents, cancellationToken);
        var canonicalDocuments = CanonicalizeDocuments(orderedDocuments, previous, cancellationToken);
        AkburaProjectIndex index;

        if (previous != null && HaveSameDocuments(canonicalDocuments, previous.Documents))
        {
            index = previous.Index;
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.CompilationReused);
#endif
        }
        else
        {
            index = AkburaProjectIndex.Create(
                state.CSharpCompilation,
                canonicalDocuments,
                options.RootNamespace,
                options.ProjectDirectory,
                previous?.Index.Compilation,
                cancellationToken);
        }

        // Additions, removals, renames and changed lookup order can turn an
        // unresolved or ambiguous name into a different declaration.
        var environment = previous != null && HaveSameDeclarations(index, previous.Index)
            ? previous.DeclarationEnvironment
            : new object();
        var graph = AkburaDependencyGraph.Create(index, cancellationToken);
        var externalModuleTypeNames = GetExternalModuleTypeNames(index);
        var components = new ComponentGenerationRequest[index.ComponentDescriptors.Length];
        var external = new AkcssGenerationRequest[index.ExternalAkcssDescriptors.Length];
        var inline = new AkcssGenerationRequest[index.InlineAkcssDescriptors.Length];

        for (var i = 0; i < components.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = index.ComponentDescriptors[i];
            var identity = "component:" + descriptor.SourcePath;
            var generationVersion = CreateVersion(descriptor.DocumentVersion, graph, environment, options);
            components[i] = new ComponentGenerationRequest(
                descriptor,
                generationVersion,
                GetAkcssModuleTypeNames(
                    descriptor,
                    graph,
                    externalModuleTypeNames,
                    cancellationToken),
                FindPrevious(previous, identity, generationVersion));
        }

        FillAkcssRequests(index.ExternalAkcssDescriptors, external);
        FillAkcssRequests(index.InlineAkcssDescriptors, inline);
        var diagnostics = computeDiagnostics
            ? new DocumentDiagnosticRequest[canonicalDocuments.Length]
            : [];
        for (var i = 0; i < diagnostics.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = canonicalDocuments[i];
            var diagnosticVersion = new DocumentDiagnosticVersion(
                document, environment, state.CSharpCompilation, options, GetDependencyVersions(document, graph));
            var cached = previous != null && previous.DiagnosticEntries.TryGetValue(document.FilePath, out var entry) &&
                diagnosticVersion.Equals(entry.Version) ? entry : null;
            diagnostics[i] = new DocumentDiagnosticRequest(document, diagnosticVersion, cached);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new BlackSilenceGenerationRequest(
            state,
            version,
            options,
            index,
            canonicalDocuments,
            environment,
            components.ToImmutableArrayUnsafe(),
            external.ToImmutableArrayUnsafe(),
            inline.ToImmutableArrayUnsafe(),
            previous,
            diagnostics.ToImmutableArrayUnsafe(),
            computeDiagnostics);

        void FillAkcssRequests(ImmutableArray<AkcssDocumentDescriptor> descriptors, AkcssGenerationRequest[] requests)
        {
            for (var i = 0; i < requests.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var descriptor = descriptors[i];
                var identity = "akcss:" + descriptor.ModuleIdentity;
                var generationVersion = CreateVersion(descriptor.DocumentVersion, graph, environment, options);
                requests[i] = new AkcssGenerationRequest(
                    descriptor,
                    generationVersion,
                    FindPrevious(previous, identity, generationVersion));
            }
        }
    }

    private static Dictionary<DocumentSyntaxVersion, string> GetExternalModuleTypeNames(
        AkburaProjectIndex index)
    {
        var result = new Dictionary<DocumentSyntaxVersion, string>(
            index.ExternalAkcssDescriptors.Length);

        for (var i = 0; i < index.ExternalAkcssDescriptors.Length; i++)
        {
            var descriptor = index.ExternalAkcssDescriptors[i];
            result.Add(
                descriptor.DocumentVersion,
                descriptor.GeneratedTypeName);
        }

        return result;
    }

    private static ImmutableArray<string> GetAkcssModuleTypeNames(
        ComponentDocumentDescriptor descriptor,
        AkburaDependencyGraph graph,
        Dictionary<DocumentSyntaxVersion, string> externalModuleTypeNames,
        CancellationToken cancellationToken)
    {
        var dependencies = graph.GetDependencies(
            descriptor.DocumentVersion);
        var inlineModules = descriptor.InlineAkcssDescriptors;
        var typeNames = new string[
            dependencies.Length + inlineModules.Length];
        var count = 0;

        for (var i = 0; i < dependencies.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (externalModuleTypeNames.TryGetValue(
                    dependencies[i].Document,
                    out var typeName))
            {
                typeNames[count++] = typeName;
            }
        }

        for (var i = 0; i < inlineModules.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            typeNames[count++] = inlineModules[i].GeneratedTypeName;
        }

        if (count == 0)
        {
            return [];
        }

        Array.Sort(
            typeNames,
            0,
            count,
            StringComparer.Ordinal);

        return ImmutableArray.Create(
            typeNames,
            0,
            count);
    }

    private static DocumentGenerationVersion CreateVersion(
        DocumentSyntaxVersion document,
        AkburaDependencyGraph graph,
        object environment,
        GeneratorProjectOptions options)
    {
        return new DocumentGenerationVersion(document, environment, options, GetDependencyVersions(document, graph));
    }

    private static ImmutableArray<DependencyGenerationVersion> GetDependencyVersions(
        DocumentSyntaxVersion document,
        AkburaDependencyGraph graph)
    {
        var dependencies = graph.GetDependencies(document);
        var versions = new DependencyGenerationVersion[dependencies.Length];
        for (var i = 0; i < versions.Length; i++)
        {
            var dependency = dependencies[i];
            versions[i] = new DependencyGenerationVersion(
                dependency.Identity,
                dependency.Document,
                dependency.IncludesBody);
        }

        return versions.ToImmutableArrayUnsafe();
    }

    private static GeneratedDocumentEntry? FindPrevious(
        BlackSilenceProjectSnapshot? previous,
        string identity,
        DocumentGenerationVersion version)
    {
        return previous != null && previous.Entries.TryGetValue(identity, out var entry) &&
            version.Equals(entry.Version) ? entry : null;
    }

    private static ImmutableArray<DocumentSyntaxVersion> OrderDocuments(
        ImmutableArray<DocumentSyntaxVersion> documents,
        CancellationToken cancellationToken)
    {
        // Roslyn treats AdditionalTexts as a set and can preserve the old provider
        // order after a reorder. Never make declaration precedence depend on it.
        for (var i = 1; i < documents.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CompareDocuments(documents[i - 1], documents[i]) <= 0)
            {
                continue;
            }

            var ordered = documents.ToArray();
            Array.Sort(ordered, CompareDocuments);
            cancellationToken.ThrowIfCancellationRequested();
            return ordered.ToImmutableArrayUnsafe();
        }

        return documents;
    }

    private static int CompareDocuments(DocumentSyntaxVersion left, DocumentSyntaxVersion right)
    {
        var comparison = left.Kind.CompareTo(right.Kind);
        if (comparison == 0)
        {
            comparison = StringComparer.Ordinal.Compare(left.FilePath, right.FilePath);
        }

        if (comparison == 0)
        {
            comparison = StringComparer.Ordinal.Compare(left.LogicalName, right.LogicalName);
        }

        return comparison;
    }

    private static ImmutableArray<DocumentSyntaxVersion> CanonicalizeDocuments(
        ImmutableArray<DocumentSyntaxVersion> documents,
        BlackSilenceProjectSnapshot? previous,
        CancellationToken cancellationToken)
    {
        if (previous == null)
        {
            return documents;
        }

        var oldDocuments = new Dictionary<string, DocumentSyntaxVersion>(StringComparer.Ordinal);
        for (var i = 0; i < previous.Documents.Length; i++)
        {
            var document = previous.Documents[i];
            oldDocuments[document.FilePath] = document;
        }

        var canonical = new DocumentSyntaxVersion[documents.Length];
        for (var i = 0; i < documents.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = documents[i];
            canonical[i] = oldDocuments.TryGetValue(current.FilePath, out var old) &&
                current.HasSameGenerationShape(old) ? old : current;
        }

        return canonical.ToImmutableArrayUnsafe();
    }

    private static bool HaveSameDocuments(
        ImmutableArray<DocumentSyntaxVersion> left,
        ImmutableArray<DocumentSyntaxVersion> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HaveSameDeclarations(AkburaProjectIndex left, AkburaProjectIndex right)
    {
        if (left.Documents.Length != right.Documents.Length ||
            left.ComponentDescriptors.Length != right.ComponentDescriptors.Length ||
            left.ExternalAkcssDescriptors.Length != right.ExternalAkcssDescriptors.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Documents.Length; i++)
        {
            if (left.Documents[i].Kind != right.Documents[i].Kind ||
                left.Documents[i].FilePath != right.Documents[i].FilePath ||
                left.Documents[i].LogicalName != right.Documents[i].LogicalName)
            {
                return false;
            }
        }

        for (var i = 0; i < left.ComponentDescriptors.Length; i++)
        {
            if (left.ComponentDescriptors[i].ComponentMetadataName != right.ComponentDescriptors[i].ComponentMetadataName)
            {
                return false;
            }
        }

        return true;
    }
}
