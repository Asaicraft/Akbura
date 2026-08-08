using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Diagnostics;

namespace Akbura.Workspaces;

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
        CancellationToken cancellationToken)
    {
        var filePath = DocumentUri.GetFilePath(uri);

        var syntaxTree =
            CreateSyntaxTree(
                text,
                filePath,
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

        Debug.WriteLine("[Akbura] Document.WithText: ContentEquals started");

        var contentEquals =
        newText.ContentEquals(Text);

        Debug.WriteLine(
            $"[Akbura] Document.WithText: ContentEquals completed, " +
            $"equal={contentEquals}");

        if (contentEquals)
        {
            return IsOpen
                ? this
                : WithOpenState(isOpen: true);
        }

        Debug.WriteLine("[Akbura] Document.WithText: WithChangedText started");

        var syntaxTree =
            WithChangedText(
                SyntaxTree,
                newText,
                changes,
                cancellationToken);

        Debug.WriteLine("[Akbura] Document.WithText: WithChangedText completed");

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

    internal static AkburaSyntaxTree CreateSyntaxTree(
        SourceText text,
        string filePath,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                Path.GetExtension(filePath),
                ".akcss",
                StringComparison.OrdinalIgnoreCase))
        {
            return AkcssSyntaxTree.ParseText(
                text,
                filePath,
                cancellationToken);
        }

        return ComponentSyntaxTree.ParseText(
            text,
            filePath,
            cancellationToken);
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
