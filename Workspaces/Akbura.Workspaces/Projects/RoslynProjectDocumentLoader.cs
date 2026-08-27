using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Reads .akbura and .akcss inputs from Roslyn documents and additional
/// documents while preserving GlobalUsings ordering.
/// </summary>
public sealed class RoslynProjectDocumentLoader
{
    public async Task<ImmutableArray<AkburaDocumentInput>> LoadAsync(
        Project project,
        Func<Uri, SourceText?>? openTextProvider,
        Uri? excludedDocument,
        CancellationToken cancellationToken)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        var documents = GetAkburaDocuments(project)
            .OrderBy(static document =>
                IsGlobalUsingsDocument(document.FilePath) ? 0 : 1)
            .ThenBy(static document => document.FilePath,
                StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();

        var loadTasks = documents
            .Select(LoadDocumentAsync)
            .ToArray();
        var loadedDocuments = await Task.WhenAll(loadTasks)
            .ConfigureAwait(false);

        using var inputs =
            ImmutableArrayBuilder<AkburaDocumentInput>.Rent(
                loadedDocuments.Length);
        foreach (var input in loadedDocuments)
        {
            if (input.HasValue)
            {
                inputs.Add(input.Value);
            }
        }

        return inputs.ToImmutable();

        async Task<AkburaDocumentInput?> LoadDocumentAsync(
            TextDocument document)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(document.FilePath))
            {
                return null;
            }

            var uri = new Uri(Path.GetFullPath(document.FilePath!));
            if (excludedDocument != null &&
                DocumentUri.Equals(uri, excludedDocument))
            {
                return null;
            }

            var text = openTextProvider?.Invoke(uri) ??
                await document
                    .GetTextAsync(cancellationToken)
                    .ConfigureAwait(false);
            return text == null
                ? null
                : new AkburaDocumentInput(uri, text);
        }
    }

    public static bool IsAkburaDocument(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath);
        return string.Equals(
                   extension,
                   ".akbura",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   extension,
                   ".akcss",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGlobalUsingsDocument(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        return string.Equals(
                   fileName,
                   "GlobalUsings.akbura",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   fileName,
                   "GlobalUsings.akcss",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static ImmutableArray<TextDocument> GetAkburaDocuments(
        Project project)
    {
        using var builder =
            ImmutableArrayBuilder<TextDocument>.Rent();
        var paths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        AddDocuments(project.Documents, paths, builder);
        AddDocuments(project.AdditionalDocuments, paths, builder);

        return builder.ToImmutable();
    }

    private static void AddDocuments(
        IEnumerable<TextDocument> documents,
        HashSet<string> paths,
        ImmutableArrayBuilder<TextDocument> builder)
    {
        foreach (var document in documents)
        {
            if (!IsAkburaDocument(document.FilePath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(document.FilePath!);
            if (paths.Add(fullPath))
            {
                builder.Add(document);
            }
        }
    }
}