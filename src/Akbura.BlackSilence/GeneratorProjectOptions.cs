using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Akbura.BlackSilence;

internal readonly record struct GeneratorProjectOptions(
    string RootNamespace,
    string ProjectDirectory,
    ComponentGenerationMode ComponentGenerationMode =
        ComponentGenerationMode.ReleaseDirect)
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

        return new GeneratorProjectOptions(
            rootNamespace ?? string.Empty,
            projectDirectory ?? string.Empty,
            componentGenerationMode);
    }
}
