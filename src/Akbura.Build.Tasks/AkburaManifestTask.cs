using Microsoft.Build.Framework;

namespace Akbura.Build;

public abstract class AkburaManifestTask : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private readonly CancellationTokenSource _cancellation = new();

    internal CancellationToken CancellationToken => _cancellation.Token;

    public void Cancel()
    {
        _cancellation.Cancel();
    }

    public ITaskItem[] Sources { get; set; } = [];

    public ITaskItem[] CSharpSources { get; set; } = [];
    public ITaskItem[] References { get; set; } = [];

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string AssemblyName { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = string.Empty;
    public string DefineConstants { get; set; } = string.Empty;
    public string LanguageVersion { get; set; } = string.Empty;
    public string Nullable { get; set; } = string.Empty;
    public bool AllowUnsafeBlocks { get; set; }
    public string ProjectPath { get; set; } = string.Empty;
    public string IntermediatePath { get; set; } = string.Empty;
    public string Configuration { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public string RuntimeIdentifier { get; set; } = string.Empty;

    protected internal string GetFullPath(ITaskItem item)
    {
        var fullPath = item.GetMetadata("FullPath");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(fullPath) ? item.ItemSpec : fullPath, ProjectDirectory);
    }
}
