using Akbura.Language;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Akbura.Build;

public sealed class GenerateAkburaModuleManifest : Microsoft.Build.Utilities.Task
{
    private const int CopyBufferLength = 4096;
    private static readonly Encoding s_embeddedSourceEncoding =
        new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);

    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    public ITaskItem[] CSharpSources { get; set; } = [];

    public ITaskItem[] References { get; set; } = [];

    [Required]
    public string OutputPath { get; set; } = string.Empty;

    [Required]
    public string EmbeddedSourcesDirectory { get; set; } = string.Empty;

    [Required]
    public string AssemblyName { get; set; } = string.Empty;

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    public string RootNamespace { get; set; } = string.Empty;

    public string DefineConstants { get; set; } = string.Empty;

    public string LanguageVersion { get; set; } = string.Empty;

    public string Nullable { get; set; } = string.Empty;

    public bool AllowUnsafeBlocks { get; set; }

    [Output]
    public ITaskItem[] EmbeddedSources { get; private set; } = [];

    public override bool Execute()
    {
        EmbeddedSources = [];
        try
        {
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
        var embeddedSources = new List<ITaskItem>(Sources.Length);
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

            var embeddedPath = GetEmbeddedSourcePath(sourceCodePath);
            WriteEmbeddedSourceIfChanged(embeddedPath, sourceText);
            var embeddedSource = new TaskItem(embeddedPath);
            embeddedSource.SetMetadata("LogicalName", sourceCodePath);
            embeddedSources.Add(embeddedSource);
        }

        EmbeddedSources = embeddedSources.ToArray();
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

    private string GetEmbeddedSourcePath(string sourceCodePath)
    {
        var pathBytes = Encoding.UTF8.GetBytes(sourceCodePath);
        var fileName = Convert.ToHexString(SHA256.HashData(pathBytes)) +
                       Path.GetExtension(sourceCodePath);
        return Path.Combine(EmbeddedSourcesDirectory, fileName);
    }

    private static void WriteEmbeddedSourceIfChanged(
        string outputPath,
        SourceText sourceText)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = outputPath + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(
                       stream,
                       s_embeddedSourceEncoding,
                       CopyBufferLength))
            {
                WriteSourceText(writer, sourceText);
            }

            if (File.Exists(outputPath) && FilesEqual(outputPath, temporaryPath))
            {
                return;
            }

            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteSourceText(TextWriter writer, SourceText sourceText)
    {
        var buffer = ArrayPool<char>.Shared.Rent(CopyBufferLength);
        try
        {
            for (var position = 0; position < sourceText.Length;)
            {
                var count = Math.Min(buffer.Length, sourceText.Length - position);
                sourceText.CopyTo(position, buffer, 0, count);
                writer.Write(buffer, 0, count);
                position += count;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static bool FilesEqual(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        var leftBuffer = ArrayPool<byte>.Shared.Rent(CopyBufferLength);
        var rightBuffer = ArrayPool<byte>.Shared.Rent(CopyBufferLength);
        try
        {
            using var left = File.OpenRead(leftPath);
            using var right = File.OpenRead(rightPath);
            while (true)
            {
                var leftCount = left.Read(leftBuffer, 0, leftBuffer.Length);
                var rightCount = right.Read(rightBuffer, 0, rightBuffer.Length);
                if (leftCount != rightCount)
                {
                    return false;
                }

                if (leftCount == 0)
                {
                    return true;
                }

                if (!leftBuffer.AsSpan(0, leftCount).SequenceEqual(
                        rightBuffer.AsSpan(0, rightCount)))
                {
                    return false;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(leftBuffer);
            ArrayPool<byte>.Shared.Return(rightBuffer);
        }
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
