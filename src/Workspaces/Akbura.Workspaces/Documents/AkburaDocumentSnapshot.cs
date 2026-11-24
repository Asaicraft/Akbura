using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Documents;

/// <summary>
/// Immutable snapshot of one Akbura language document.
/// </summary>
public sealed class AkburaDocumentSnapshot
{
    internal AkburaDocumentSnapshot(
        AkburaDocumentId id,
        AkburaProjectId projectId,
        Uri uri,
        string filePath,
        VersionStamp version,
        SourceText text,
        AkburaSyntaxTree syntaxTree,
        bool isOpen)
    {
        Id = id;
        ProjectId = projectId;
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        FilePath = filePath ?? string.Empty;
        Version = version;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        SyntaxTree = syntaxTree ??
            throw new ArgumentNullException(nameof(syntaxTree));
        IsOpen = isOpen;
    }

    public AkburaDocumentId Id { get; }

    public AkburaProjectId ProjectId { get; }

    public Uri Uri { get; }

    public string FilePath { get; }

    public VersionStamp Version { get; }

    public SourceText Text { get; }

    public bool IsOpen { get; }

    internal AkburaSyntaxTree SyntaxTree { get; }

    internal static AkburaDocumentSnapshot Create(
        AkburaProjectId projectId,
        Uri uri,
        SourceText text,
        string rootNamespace,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var filePath = DocumentUri.GetFilePath(uri);

        var syntaxTree =
            CreateSyntaxTree(
                text,
                filePath,
                rootNamespace,
                projectDirectory,
                cancellationToken);

        return new AkburaDocumentSnapshot(
            AkburaDocumentId.CreateNew(),
            projectId,
            uri,
            filePath,
            VersionStamp.Create(),
            text,
            syntaxTree,
            isOpen: true);
    }

    internal AkburaDocumentSnapshot WithText(
        SourceText newText,
        IEnumerable<TextChangeRange>? changes,
        CancellationToken cancellationToken)
    {
        if (newText == null)
        {
            throw new ArgumentNullException(nameof(newText));
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            "Document.WithText: ContentEquals started");

        var contentEquals =
        newText.ContentEquals(Text);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            $"Document.WithText: ContentEquals completed, " +
            $"equal={contentEquals}");

        if (contentEquals)
        {
            return IsOpen
                ? this
                : WithOpenState(isOpen: true);
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            "Document.WithText: WithChangedText started");

        var syntaxTree =
            WithChangedText(
                SyntaxTree,
                newText,
                changes,
                cancellationToken);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            "Document.WithText: WithChangedText completed");

        return new AkburaDocumentSnapshot(
            Id,
            ProjectId,
            Uri,
            FilePath,
            VersionStamp.Create(),
            newText,
            syntaxTree,
            isOpen: true);
    }

    internal AkburaDocumentSnapshot WithOpenState(bool isOpen)
    {
        if (isOpen == IsOpen)
        {
            return this;
        }

        return new AkburaDocumentSnapshot(
            Id,
            ProjectId,
            Uri,
            FilePath,
            VersionStamp.Create(),
            Text,
            SyntaxTree,
            isOpen);
    }

    internal AkburaDocumentSnapshot WithProjectIdentity(string rootNamespace, string projectDirectory)
    {
        if (SyntaxTree is not AkcssSyntaxTree akcss)
        {
            return this;
        }

        var tree = akcss.WithLogicalName(GetAkcssLogicalName(FilePath, rootNamespace, projectDirectory));
        return ReferenceEquals(tree, akcss) ? this :
            new AkburaDocumentSnapshot(Id, ProjectId, Uri, FilePath, Version, Text, tree, IsOpen);
    }

    internal static AkburaSyntaxTree CreateSyntaxTree(
        SourceText text,
        string filePath,
        string rootNamespace,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                Path.GetExtension(filePath),
                ".akcss",
                StringComparison.OrdinalIgnoreCase))
        {
            return AkcssSyntaxTree.ParseText(
                text, filePath, GetAkcssLogicalName(filePath, rootNamespace, projectDirectory), cancellationToken);
        }

        return ComponentSyntaxTree.ParseText(
            text,
            filePath,
            cancellationToken);
    }

    private static string GetAkcssLogicalName(string filePath, string rootNamespace, string projectDirectory) =>
        AkcssGeneratedModuleNames.GetMetadataName(rootNamespace, GetProjectRelativeSourcePath(filePath, projectDirectory));

    private static string GetProjectRelativeSourcePath(
        string filePath,
        string projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectDirectory) &&
            !string.IsNullOrWhiteSpace(filePath))
        {
            var projectPath = Path
                .GetFullPath(projectDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            var fullSourcePath = Path.GetFullPath(filePath);
            var projectPrefix =
                projectPath + Path.DirectorySeparatorChar;

            if (fullSourcePath.StartsWith(
                    projectPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return AkcssGeneratedModuleNames
                    .NormalizeSourcePath(
                        fullSourcePath[
                            projectPrefix.Length..]);
            }
        }

        return AkcssGeneratedModuleNames
            .NormalizeSourcePath(
                Path.GetFileName(filePath));
    }

    private static AkburaSyntaxTree WithChangedText(
        AkburaSyntaxTree syntaxTree,
        SourceText newText,
        IEnumerable<TextChangeRange>? changes,
        CancellationToken cancellationToken)
    {
        return syntaxTree switch
        {
            ComponentSyntaxTree componentTree =>
                componentTree.WithChangedText(
                    newText,
                    changes,
                    cancellationToken),

            AkcssSyntaxTree akcssTree =>
                akcssTree.WithChangedText(
                    newText,
                    changes,
                    cancellationToken),

            _ => throw new InvalidOperationException(
                $"Unsupported syntax tree type " +
                $"'{syntaxTree.GetType().FullName}'."),
        };
    }
}
