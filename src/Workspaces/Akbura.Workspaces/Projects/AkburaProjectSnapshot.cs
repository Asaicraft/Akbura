using Akbura.Language;
using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Immutable Akbura view of one C# project.
/// </summary>
public sealed class AkburaProjectSnapshot
{
    internal AkburaProjectSnapshot(AkburaProjectId id, VersionStamp version, ProjectContext context, AkburaCompilation compilation, ImmutableDictionary<AkburaDocumentId, AkburaDocumentSnapshot> documents, ImmutableDictionary<ResourceDictionaryIdentity, ResourceDocumentSnapshot> resourceDocuments, object? resourceInputsIdentity = null)
    {
        Id = id;
        Version = version;
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Compilation = compilation ??
            throw new ArgumentNullException(nameof(compilation));

        Documents = documents ??
            throw new ArgumentNullException(nameof(documents));

        ResourceDocuments = resourceDocuments ??
            throw new ArgumentNullException(nameof(resourceDocuments));

        ResourceInputsIdentity = resourceInputsIdentity ?? new object();
    }

    public AkburaProjectId Id { get; }

    public VersionStamp Version { get; }

    public ProjectContext Context { get; }

    public CSharpCompilation CSharpCompilation => Context.CSharpCompilation;

    public ImmutableDictionary<AkburaDocumentId, AkburaDocumentSnapshot> Documents { get; }

    internal ImmutableDictionary<
        ResourceDictionaryIdentity,
        ResourceDocumentSnapshot> ResourceDocuments { get; }

    internal object ResourceInputsIdentity { get; }

    internal AkburaCompilation Compilation { get; }

    internal static AkburaProjectSnapshot Create(ProjectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var compilation = new AkburaCompilation(
            context.CSharpCompilation,
            ImmutableArray<AkburaSyntaxTree>.Empty,
            context.RootNamespace,
            context.ProjectDirectory);

        return new AkburaProjectSnapshot(
            AkburaProjectId.FromRoslyn(context.RoslynProjectId),
            VersionStamp.Create(),
            context,
            compilation,
            ImmutableDictionary<
                AkburaDocumentId,
                AkburaDocumentSnapshot>.Empty,
            ImmutableDictionary<
                ResourceDictionaryIdentity,
                ResourceDocumentSnapshot>.Empty);
    }

    public bool TryGetDocument(AkburaDocumentId documentId, out AkburaDocumentSnapshot document)
    {
        return Documents.TryGetValue(documentId, out document!);
    }

    public bool TryGetDocument(Uri uri, out AkburaDocumentSnapshot document)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        foreach (var candidate in Documents.Values)
        {
            if (DocumentUri.Equals(candidate.Uri, uri))
            {
                document = candidate;
                return true;
            }
        }

        document = null!;
        return false;
    }

    internal AkburaProjectSnapshot AddDocument(AkburaDocumentSnapshot document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (document.ProjectId != Id)
        {
            throw new ArgumentException(
                "The document belongs to another Akbura project.",
                nameof(document));
        }

        if (Documents.ContainsKey(document.Id))
        {
            throw new InvalidOperationException(
                $"Document '{document.Id}' already exists.");
        }

        var compilation =
            AddSyntaxTree(
                Compilation,
                document.SyntaxTree);

        return new AkburaProjectSnapshot(
            Id,
            VersionStamp.Create(),
            Context,
            compilation,
            Documents.Add(document.Id, document),
            ResourceDocuments,
            ResourceInputsIdentity);
    }

    internal AkburaProjectSnapshot ReplaceDocument(AkburaDocumentSnapshot document)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (!Documents.TryGetValue(
                document.Id,
                out var oldDocument))
        {
            throw new KeyNotFoundException($"Document '{document.Id}' was not found.");
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            "Project.ReplaceDocument: compilation started");

        var compilation =
            ReferenceEquals(
                oldDocument.SyntaxTree,
                document.SyntaxTree)
                    ? Compilation
                    : ReplaceSyntaxTree(
                        Compilation,
                        oldDocument.SyntaxTree,
                        document.SyntaxTree);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            "Project.ReplaceDocument: compilation completed");

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            "Project.ReplaceDocument: dictionary started");

        var documents =
            Documents.SetItem(
                document.Id,
                document);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            "Project.ReplaceDocument: dictionary completed");

        return new AkburaProjectSnapshot(
            Id,
            VersionStamp.Create(),
            Context,
            compilation,
            documents,
            ResourceDocuments,
            ResourceInputsIdentity);
    }

    internal AkburaProjectSnapshot RemoveDocument(AkburaDocumentId documentId)
    {
        if (!Documents.TryGetValue(
                documentId,
                out var document))
        {
            return this;
        }

        var compilation =
            RemoveSyntaxTree(
                Compilation,
                document.SyntaxTree);

        return new AkburaProjectSnapshot(
            Id,
            VersionStamp.Create(),
            Context,
            compilation,
            Documents.Remove(documentId),
            ResourceDocuments,
            ResourceInputsIdentity);
    }

    internal AkburaProjectSnapshot WithContext(ProjectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (ReferenceEquals(Context, context))
        {
            return this;
        }

        AkburaCompilation compilation;

        if (string.Equals(
                Context.RootNamespace,
                context.RootNamespace,
                StringComparison.Ordinal) &&
            string.Equals(
                Context.ProjectDirectory,
                context.ProjectDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            compilation = Compilation.WithCSharpCompilation(
                context.CSharpCompilation);
        }
        else
        {
            var componentTrees =
                Documents.Values
                    .Select(
                        static document =>
                            document.SyntaxTree)
                    .Where(
                        static syntaxTree =>
                            syntaxTree is not
                                AkcssSyntaxTree)
                    .ToImmutableArray();

            var akcssTrees =
                Documents.Values
                    .Select(
                        static document =>
                            document.SyntaxTree)
                    .OfType<AkcssSyntaxTree>()
                    .ToImmutableArray();

            compilation =
                new AkburaCompilation(
                    context.CSharpCompilation,
                    componentTrees,
                    akcssTrees,
                    context.RootNamespace,
                    context.ProjectDirectory,
                    reuseFrom: Compilation);
        }

        return new AkburaProjectSnapshot(
            Id,
            VersionStamp.Create(),
            context,
            compilation,
            Documents,
            ResourceDocuments);
    }

    internal AkburaProjectSnapshot WithDocuments(ImmutableDictionary<AkburaDocumentId, AkburaDocumentSnapshot> documents)
    {
        if (documents == null)
        {
            throw new ArgumentNullException(nameof(documents));
        }

        if (ReferenceEquals(documents, Documents))
        {
            return this;
        }

        var componentTrees = documents.Values
            .Select(static document => document.SyntaxTree)
            .Where(static syntaxTree => syntaxTree is not AkcssSyntaxTree)
            .ToImmutableArray();
        var akcssTrees = documents.Values
            .Select(static document => document.SyntaxTree)
            .OfType<AkcssSyntaxTree>()
            .ToImmutableArray();
        var compilation = new AkburaCompilation(
            Context.CSharpCompilation,
            componentTrees,
            akcssTrees,
            Context.RootNamespace,
            Context.ProjectDirectory,
            reuseFrom: Compilation);

        return new AkburaProjectSnapshot(
            Id,
            VersionStamp.Create(),
            Context,
            compilation,
            documents,
            ResourceDocuments,
            ResourceInputsIdentity);
    }

    internal AkburaProjectSnapshot WithResourceDocuments(ImmutableDictionary<ResourceDictionaryIdentity, ResourceDocumentSnapshot> resourceDocuments)
    {
        if (resourceDocuments == null)
        {
            throw new ArgumentNullException(nameof(resourceDocuments));
        }

        if (ReferenceEquals(resourceDocuments, ResourceDocuments))
        {
            return this;
        }

        return new AkburaProjectSnapshot(
            Id,
            VersionStamp.Create(),
            Context,
            Compilation,
            Documents,
            resourceDocuments);
    }

    internal AkburaProjectSnapshot WithCompilationReferences(ImmutableArray<AkburaCompilationReference> references)
    {
        var compilation =
            Compilation.WithCompilationReferences(references);

        return ReferenceEquals(compilation, Compilation)
            ? this
            : new AkburaProjectSnapshot(
                Id,
                VersionStamp.Create(),
                Context,
                compilation,
                Documents,
                ResourceDocuments,
                ResourceInputsIdentity);
    }

    private static AkburaCompilation AddSyntaxTree(AkburaCompilation compilation, AkburaSyntaxTree syntaxTree)
    {
        return syntaxTree switch
        {
            AkcssSyntaxTree akcssTree =>
                compilation.AddAkcssSyntaxTrees(
                    [
                    akcssTree,
                    ]),

            _ =>
                compilation.AddSyntaxTrees(
                    [
                    syntaxTree,
                    ]),
        };
    }

    private static AkburaCompilation ReplaceSyntaxTree(AkburaCompilation compilation, AkburaSyntaxTree oldTree, AkburaSyntaxTree newTree)
    {
        if (oldTree is AkcssSyntaxTree oldAkcssTree)
        {
            if (newTree is not
                AkcssSyntaxTree newAkcssTree)
            {
                throw new InvalidOperationException(
                    "The document syntax tree kind cannot change.");
            }

            return compilation.ReplaceAkcssSyntaxTree(
                oldAkcssTree,
                newAkcssTree);
        }

        if (newTree is AkcssSyntaxTree)
        {
            throw new InvalidOperationException(
                "The document syntax tree kind cannot change.");
        }

        return compilation.ReplaceSyntaxTree(
            oldTree,
            newTree);
    }

    private static AkburaCompilation RemoveSyntaxTree(AkburaCompilation compilation, AkburaSyntaxTree syntaxTree)
    {
        return syntaxTree switch
        {
            AkcssSyntaxTree akcssTree =>
                compilation.RemoveAkcssSyntaxTrees(
                    [
                    akcssTree,
                    ]),

            _ =>
                compilation.RemoveSyntaxTrees(
                    [
                    syntaxTree,
                    ]),
        };
    }
}
