using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Loads evaluated Avalonia resource inputs without treating XAML as Akbura
/// syntax. Only Roslyn additional documents supplied by the project are used.
/// </summary>
internal sealed class RoslynResourceDocumentLoader
{
    public async Task<ImmutableArray<ResourceDocumentInput>> LoadAsync(Project project, CSharpCompilation compilation, Func<Uri, SourceText?>? openTextProvider, CancellationToken cancellationToken)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        var assemblyIdentity = ResourceAssemblyIdentity.Create(
            compilation.Assembly.Identity);
        var projectDirectory = GetProjectDirectory(project);
        var targetFramework = GetTargetFramework(project);
        var projectKey = project.Id.Id.ToString("N");
        var inputs = new List<ResourceDocumentInput>();

        foreach (var document in project.AdditionalDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAvaloniaResourceDocument(document.FilePath) ||
                string.IsNullOrWhiteSpace(document.FilePath) ||
                !TryGetLogicalPath(
                    document,
                    projectDirectory,
                    out var logicalPath))
            {
                continue;
            }

            var physicalPath = Path.GetFullPath(document.FilePath!);
            var uri = new Uri(physicalPath);
            var text = openTextProvider?.Invoke(uri) ??
                await document.GetTextAsync(cancellationToken)
                    .ConfigureAwait(false);
            if (text == null)
            {
                continue;
            }

            var version = await document
                .GetTextVersionAsync(cancellationToken)
                .ConfigureAwait(false);
            inputs.Add(new ResourceDocumentInput(
                uri,
                physicalPath,
                logicalPath,
                assemblyIdentity,
                text,
                version,
                projectKey,
                targetFramework));
        }

        return inputs.ToImmutableArray();
    }

    internal static bool IsAvaloniaResourceDocument(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) &&
            string.Equals(
                Path.GetExtension(filePath),
                ".axaml",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetLogicalPath(TextDocument document, string projectDirectory, out string logicalPath)
    {
        var physicalPath = Path.GetFullPath(document.FilePath!);
        var physicalFileName = Path.GetFileName(physicalPath);
        if (document.Folders.Count > 0 ||
            !string.Equals(
                document.Name,
                physicalFileName,
                StringComparison.Ordinal))
        {
            logicalPath = string.Join(
                "/",
                document.Folders.Concat(new[] { document.Name }));
            return ResourcePath.TryCreateExportPath(
                logicalPath,
                out _);
        }

        var root = projectDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!physicalPath.StartsWith(
                root,
                Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            // A root-level Link has no folders. Roslyn still exposes its
            // logical file name through TextDocument.Name, so keep the
            // evaluated project item instead of falling back to a physical
            // path outside the project.
            logicalPath = document.Name;
            return ResourcePath.TryCreateExportPath(
                logicalPath,
                out _);
        }

        logicalPath = physicalPath.Substring(root.Length)
            .Replace('\\', '/');
        return ResourcePath.TryCreateExportPath(
            logicalPath,
            out _);
    }

    private static string GetProjectDirectory(Project project)
    {
        return string.IsNullOrWhiteSpace(project.FilePath)
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(
                  Path.GetFullPath(project.FilePath!)) ??
              Environment.CurrentDirectory;
    }

    private static string GetTargetFramework(Project project)
    {
        return project.AnalyzerOptions
                   .AnalyzerConfigOptionsProvider
                   .GlobalOptions
                   .TryGetValue(
                       "build_property.TargetFramework",
                       out var targetFramework) &&
               !string.IsNullOrWhiteSpace(targetFramework)
            ? targetFramework
            : "unknown";
    }
}
