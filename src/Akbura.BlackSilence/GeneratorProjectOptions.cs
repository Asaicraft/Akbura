using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Akbura.BlackSilence;

internal readonly record struct GeneratorProjectOptions(
    string RootNamespace,
    string ProjectDirectory,
    ComponentGenerationMode ComponentGenerationMode =
        ComponentGenerationMode.ReleaseDirect,
    bool CrossCompilationReuse = true)
{
    private const string RootNamespaceProperty = "build_property.RootNamespace";
    private const string ProjectDirectoryProperty = "build_property.ProjectDir";

    public static GeneratorProjectOptions Create(
        AnalyzerConfigOptions options,
        ComponentGenerationMode componentGenerationMode =
            ComponentGenerationMode.ReleaseDirect)
    {
        options.TryGetValue(RootNamespaceProperty, out var rootNamespace);
        options.TryGetValue(ProjectDirectoryProperty, out var projectDirectory);
        options.TryGetValue("build_property.AkburaCrossCompilationReuse", out var reuse);

        return new GeneratorProjectOptions(
            rootNamespace ?? string.Empty,
            projectDirectory ?? string.Empty,
            componentGenerationMode,
            !string.Equals(reuse, "false", System.StringComparison.OrdinalIgnoreCase));
    }
}
