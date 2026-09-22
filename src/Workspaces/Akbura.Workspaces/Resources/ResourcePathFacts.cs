namespace Akbura.Workspaces.Resources;

internal readonly struct ResourcePathFacts
{
    public ResourcePathFacts(ResourceDocumentInput input)
    {
        Uri = input.Uri;
        PhysicalPath = input.PhysicalPath;
        Dictionary = input.DictionaryIdentity;
        ProjectKey = input.ProjectKey;
        TargetFramework = input.TargetFramework;

        var logicalPath = input.LogicalPath.Value;
        var separator = logicalPath.LastIndexOf('/');
        LogicalDirectory = separator < 0
            ? string.Empty
            : logicalPath.Substring(0, separator);
    }

    public Uri Uri { get; }

    public string PhysicalPath { get; }

    public ResourceDictionaryIdentity Dictionary { get; }

    public ResourcePath LogicalPath => Dictionary.Path;

    public string LogicalDirectory { get; }

    public ResourceAssemblyIdentity AssemblyIdentity =>
        Dictionary.Assembly;

    public string ProjectKey { get; }

    public string TargetFramework { get; }
}
