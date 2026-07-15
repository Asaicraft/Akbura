using Akbura.Language;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Akbura.Build;

public sealed class GenerateAkburaModuleManifest : Microsoft.Build.Utilities.Task
{
    [Required]
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

    public override bool Execute()
    {
        try
        {
            var sourceTexts = ReadSources();
            var csharpCompilation = CreateCSharpCompilation();
            if (Log.HasLoggedErrors)
            {
                return false;
            }

            var manifest = AkburaModuleManifestBuilder.Build(
                AssemblyName,
                RootNamespace,
                sourceTexts,
                csharpCompilation);
            WriteIfChanged(manifest);
            return !Log.HasLoggedErrors;
        }
        catch (Exception exception)
        {
            Log.LogErrorFromException(
                exception,
                showStackTrace: true,
                showDetail: true,
                file: null);
            return false;
        }
    }

    private IReadOnlyList<AkburaModuleSourceText> ReadSources()
    {
        var result = new List<AkburaModuleSourceText>(Sources.Length);
        var paths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in Sources.OrderBy(
                     static source => GetSourceCodePath(source),
                     StringComparer.Ordinal))
        {
            var fullPath = GetFullPath(source);
            var sourceCodePath = GetSourceCodePath(source);
            if (string.IsNullOrWhiteSpace(sourceCodePath))
            {
                sourceCodePath = Path.GetRelativePath(ProjectDirectory, fullPath)
                    .Replace('\\', '/');
            }

            if (!paths.Add(sourceCodePath))
            {
                Log.LogError(
                    $"Akbura source resource path '{sourceCodePath}' is included more than once.");
                continue;
            }

            result.Add(new AkburaModuleSourceText(
                sourceCodePath,
                File.ReadAllText(fullPath)));
        }

        return result;
    }

    private CSharpCompilation CreateCSharpCompilation()
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(GetLanguageVersion())
            .WithPreprocessorSymbols(GetPreprocessorSymbols());
        var syntaxTrees = new List<SyntaxTree>(CSharpSources.Length);
        var sourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in CSharpSources)
        {
            var fullPath = GetFullPath(source);
            if (!File.Exists(fullPath) || !sourcePaths.Add(fullPath))
            {
                continue;
            }

            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                File.ReadAllText(fullPath),
                parseOptions,
                fullPath));
        }

        var references = new List<MetadataReference>(References.Length);
        var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in References)
        {
            var fullPath = GetFullPath(reference);
            if (!File.Exists(fullPath) || !referencePaths.Add(fullPath))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(fullPath));
        }

        return CSharpCompilation.Create(
            AssemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: AllowUnsafeBlocks,
                nullableContextOptions: GetNullableContextOptions()));
    }

    private Microsoft.CodeAnalysis.CSharp.LanguageVersion GetLanguageVersion()
    {
        if (string.IsNullOrWhiteSpace(LanguageVersion))
        {
            return Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview;
        }

        if (LanguageVersionFacts.TryParse(LanguageVersion, out var languageVersion))
        {
            return languageVersion;
        }

        if (Version.TryParse(LanguageVersion, out _))
        {
            Log.LogMessage(
                MessageImportance.Low,
                $"C# language version '{LanguageVersion}' is newer than the metadata binder; using preview.");
            return Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview;
        }

        Log.LogWarning(
            $"Unknown C# language version '{LanguageVersion}'; using preview for Akbura metadata binding.");
        return Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview;
    }

    private IEnumerable<string> GetPreprocessorSymbols()
    {
        return DefineConstants.Split(
            [';', ',', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private NullableContextOptions GetNullableContextOptions()
    {
        return Nullable.Trim().ToLowerInvariant() switch
        {
            "enable" => NullableContextOptions.Enable,
            "annotations" => NullableContextOptions.Annotations,
            "warnings" => NullableContextOptions.Warnings,
            _ => NullableContextOptions.Disable,
        };
    }

    private string GetFullPath(ITaskItem item)
    {
        var fullPath = item.GetMetadata("FullPath");
        return string.IsNullOrWhiteSpace(fullPath)
            ? Path.GetFullPath(item.ItemSpec, ProjectDirectory)
            : fullPath;
    }

    private void WriteIfChanged(AkburaModuleManifest manifest)
    {
        using var stream = new MemoryStream();
        AkburaModuleManifestSerializer.Write(stream, manifest);
        var content = stream.ToArray();

        if (File.Exists(OutputPath) &&
            File.ReadAllBytes(OutputPath).AsSpan().SequenceEqual(content))
        {
            return;
        }

        var directory = Path.GetDirectoryName(OutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(OutputPath, content);
    }

    private static string GetSourceCodePath(ITaskItem source)
    {
        return source.GetMetadata("LogicalName")
            .Replace('\\', '/');
    }
}
