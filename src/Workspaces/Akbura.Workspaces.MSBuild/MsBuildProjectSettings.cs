using Microsoft.Build.Evaluation;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Workspaces.MSBuild;

/// <summary>
/// Records the configuration, target framework, and inputs of an evaluated MSBuild project.
/// </summary>
public sealed class MsBuildProjectSettings
{
    private MsBuildProjectSettings(
        string configuration,
        string targetFramework,
        string? assetsFile,
        ImmutableDictionary<string, string> globalProperties,
        ImmutableArray<string> evaluationInputs)
    {
        Configuration = configuration;
        TargetFramework = targetFramework;
        AssetsFile = assetsFile;
        GlobalProperties = globalProperties;
        EvaluationInputs = evaluationInputs;
    }

    public string Configuration { get; }

    public string TargetFramework { get; }

    public string? AssetsFile { get; }

    public ImmutableDictionary<string, string> GlobalProperties { get; }

    public ImmutableArray<string> EvaluationInputs { get; }

    public static MsBuildProjectSettings Evaluate(string projectPath, string configuration, string? targetFramework = null)
    {
        MsBuildProjectContextProvider.EnsureMSBuildRegistered();
        return EvaluateCore(Path.GetFullPath(projectPath), configuration, targetFramework);
    }

    // Keep references to MSBuild types out of the method that registers the SDK.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MsBuildProjectSettings EvaluateCore(string projectPath, string configuration, string? targetFramework)
    {
        using var collection = new ProjectCollection();
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configuration,
            ["DesignTimeBuild"] = "true",
            ["AkburaWorkspaceDiagnosticsActive"] = "true",
        };
        var project = collection.LoadProject(projectPath, properties, toolsVersion: null);
        var frameworks = project.GetPropertyValue("TargetFrameworks")
            .Split([';'], StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < frameworks.Length; index++)
        {
            frameworks[index] = frameworks[index].Trim();
        }
        if (frameworks.Length == 0)
        {
            var framework = project.GetPropertyValue("TargetFramework");
            if (string.IsNullOrEmpty(framework))
            {
                var legacyVersion = project.GetPropertyValue("TargetFrameworkVersion");
                if (legacyVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                {
                    framework = "net" + legacyVersion.Substring(1).Replace(".", string.Empty);
                }
            }
            if (string.IsNullOrEmpty(framework))
            {
                throw new InvalidOperationException("MSBuild did not report a target framework for the project.");
            }
            frameworks = [framework];
        }

        if (targetFramework == null && frameworks.Length != 1)
        {
            throw new InvalidOperationException(
                $"Select --target-framework. The project targets {string.Join(", ", frameworks)}.");
        }

        var requestedFramework = targetFramework ?? frameworks[0];
        var selectedFramework = Array.Find(frameworks,
            framework => framework.Equals(requestedFramework, StringComparison.OrdinalIgnoreCase));
        if (selectedFramework == null)
        {
            throw new InvalidOperationException(
                $"Target framework '{requestedFramework}' is not declared by the project. " +
                $"Available frameworks are {string.Join(", ", frameworks)}.");
        }

        var inputs = new HashSet<string>(Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);
        AddInputs(project);
        if (!string.IsNullOrEmpty(project.GetPropertyValue("TargetFrameworks")))
        {
            properties["TargetFramework"] = selectedFramework;
            project = collection.LoadProject(projectPath, properties, toolsVersion: null);
            AddInputs(project);
        }

        var assetsFile = project.GetPropertyValue("ProjectAssetsFile");
        if (!string.IsNullOrEmpty(assetsFile) &&
            (project.GetPropertyValue("UsingMicrosoftNETSdk") == "true" ||
             project.GetItems("PackageReference").Count != 0 || File.Exists(assetsFile)))
        {
            assetsFile = Path.GetFullPath(Path.Combine(project.DirectoryPath, assetsFile));
            inputs.Add(assetsFile);
        }
        else
        {
            assetsFile = null;
        }

        return new(configuration, selectedFramework, assetsFile,
            properties.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase),
            inputs.ToImmutableArray());

        void AddInputs(Project evaluated)
        {
            inputs.Add(evaluated.FullPath);
            foreach (var import in evaluated.Imports)
            {
                inputs.Add(import.ImportedProject.FullPath);
            }
        }
    }
}
