using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Resources;

internal readonly struct ResourceDocumentInput
{
    public ResourceDocumentInput(Uri uri, string physicalPath, string logicalPath, ResourceAssemblyIdentity assemblyIdentity, SourceText text, VersionStamp version, string projectKey, string targetFramework)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        if (string.IsNullOrWhiteSpace(physicalPath))
        {
            throw new ArgumentException(
                "A physical resource path is required.",
                nameof(physicalPath));
        }

        if (!ResourcePath.TryCreateExportPath(
                logicalPath,
                out var resourcePath))
        {
            throw new ArgumentException(
                "The logical resource path must be relative to the " +
                "assembly resource root.",
                nameof(logicalPath));
        }

        if (assemblyIdentity.IsDefault)
        {
            throw new ArgumentException(
                "An assembly identity is required.",
                nameof(assemblyIdentity));
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        if (string.IsNullOrWhiteSpace(projectKey))
        {
            throw new ArgumentException(
                "A project key is required.",
                nameof(projectKey));
        }

        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            throw new ArgumentException(
                "A target framework is required.",
                nameof(targetFramework));
        }

        Uri = uri;
        PhysicalPath = Path.GetFullPath(physicalPath);
        LogicalPath = resourcePath;
        AssemblyIdentity = assemblyIdentity;
        Text = text;
        Version = version;
        ProjectKey = projectKey;
        TargetFramework = targetFramework;
    }

    public Uri Uri { get; }

    public string PhysicalPath { get; }

    public ResourcePath LogicalPath { get; }

    public ResourceAssemblyIdentity AssemblyIdentity { get; }

    public ResourceDictionaryIdentity DictionaryIdentity =>
        new(AssemblyIdentity, LogicalPath);

    public SourceText Text { get; }

    public VersionStamp Version { get; }

    public string ProjectKey { get; }

    public string TargetFramework { get; }

    internal ResourceDocumentInput WithText(SourceText text, VersionStamp version)
    {
        return new ResourceDocumentInput(
            Uri,
            PhysicalPath,
            LogicalPath.Value,
            AssemblyIdentity,
            text,
            version,
            ProjectKey,
            TargetFramework);
    }
}
