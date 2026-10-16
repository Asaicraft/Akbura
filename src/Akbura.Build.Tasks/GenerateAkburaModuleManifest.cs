using Akbura.Language;
using Microsoft.Build.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Akbura.Build;

public sealed class GenerateAkburaModuleManifest : AkburaManifestTask
{
    public string InputFingerprint { get; set; } = string.Empty;

    public override bool Execute()
    {
        var cache = new AkburaManifestCache(this);
        try
        {
            using var outputLock = cache.AcquireOutputLock();
            cache.Invalidate();
            var fingerprint = cache.ComputeFingerprint();
            if (InputFingerprint.Length != 0 && InputFingerprint != fingerprint)
            {
                throw new IOException("Akbura manifest inputs changed during the build. Retry with stable inputs.");
            }

            var sourceTexts = ReadSources();
            if (Log.HasLoggedErrors)
            {
                return false;
            }

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
            if (fingerprint != cache.ComputeFingerprint())
            {
                throw new IOException("Akbura manifest inputs changed during generation. No cache was published; retry the build.");
            }

            cache.Publish(fingerprint);
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

            SourceText sourceText;
            using (var stream = File.OpenRead(fullPath))
            {
                sourceText = SourceText.From(stream, encoding: null);
            }

            result.Add(new AkburaModuleSourceText(sourceCodePath, sourceText));
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
            if (!sourcePaths.Add(fullPath))
            {
                continue;
            }

            SourceText sourceText;
            using (var stream = File.OpenRead(fullPath))
            {
                sourceText = SourceText.From(stream, encoding: null);
            }

            syntaxTrees.Add(CSharpSyntaxTree.ParseText(
                sourceText,
                parseOptions,
                fullPath));
        }

        var references = new List<MetadataReference>(References.Length);
        var referencePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in References)
        {
            var fullPath = GetFullPath(reference);
            if (!referencePaths.Add(fullPath))
            {
                continue;
            }

            // CreateFromFile is lazy; validate the image before publishing a successful manifest.
            using (var stream = File.OpenRead(fullPath))
            using (var image = new PEReader(stream))
            {
                _ = image.GetMetadataReader();
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

        AkburaManifestCache.WriteAtomically(OutputPath, content);
    }

    private static string GetSourceCodePath(ITaskItem source)
    {
        return source.GetMetadata("LogicalName")
            .Replace('\\', '/');
    }
}
