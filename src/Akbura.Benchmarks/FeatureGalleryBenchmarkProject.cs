using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace Akbura.Benchmarks;

internal enum FeatureGalleryEditTarget : byte
{
    None,
    Component,
    Akcss,
}

internal sealed class FeatureGalleryBenchmarkProject
{
    private const string RepositoryRootMetadataName = "AkburaRepositoryRoot";

    private const string FeatureGalleryProjectRelativePath =
        "src/Akbura.FeatureGallery/Akbura.FeatureGallery/Akbura.FeatureGallery.csproj";

    private static int s_snapshotId;

    private readonly ImmutableArray<FeatureGallerySourceFile> _sourceFiles;
    private readonly int _componentEditIndex;
    private readonly int _akcssEditIndex;

    private FeatureGalleryBenchmarkProject(
        CSharpCompilation compilation,
        CSharpParseOptions parseOptions,
        string rootNamespace,
        string projectDirectory,
        ImmutableArray<FeatureGallerySourceFile> sourceFiles,
        int componentEditIndex,
        int akcssEditIndex)
    {
        Compilation = compilation;
        ParseOptions = parseOptions;
        RootNamespace = rootNamespace;
        ProjectDirectory = projectDirectory;

        _sourceFiles = sourceFiles;
        _componentEditIndex = componentEditIndex;
        _akcssEditIndex = akcssEditIndex;
    }

    public CSharpCompilation Compilation { get; }

    public CSharpParseOptions ParseOptions { get; }

    public string RootNamespace { get; }

    public string ProjectDirectory { get; }

    public int AdditionalFileCount => _sourceFiles.Length;

    public string ComponentEditFile => _sourceFiles[_componentEditIndex].RelativePath;

    public string AkcssEditFile => _sourceFiles[_akcssEditIndex].RelativePath;

    public static FeatureGalleryBenchmarkProject Load()
    {
        RegisterMSBuild();

        var repositoryRoot = GetRepositoryRoot();
        var projectPath = Path.GetFullPath(Path.Combine(repositoryRoot, FeatureGalleryProjectRelativePath));

        var globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = "Release",
            ["AkburaEmbedSourceFiles"] = "false",
        };

        var workspaceDiagnostics = new ConcurrentQueue<string>();

        using var workspace = MSBuildWorkspace.Create(globalProperties);

        workspace.LoadMetadataForReferencedProjects = true;
        using var diagnosticRegistration = workspace.RegisterWorkspaceFailedHandler(
            args => workspaceDiagnostics.Enqueue(args.Diagnostic.Message));

        var project = workspace.OpenProjectAsync(projectPath).GetAwaiter().GetResult();
        project = RemoveBenchmarkedGenerators(project);

        var compilation = project.GetCompilationAsync().GetAwaiter().GetResult() as CSharpCompilation;

        if (compilation == null)
        {
            throw new InvalidOperationException(CreateProjectLoadError(projectPath, workspaceDiagnostics.ToArray()));
        }

        var projectDirectory = Path.GetDirectoryName(project.FilePath ?? projectPath)!;

        var rootNamespace = string.IsNullOrWhiteSpace(project.DefaultNamespace)
            ? project.Name
            : project.DefaultNamespace!;

        var parseOptions = project.ParseOptions as CSharpParseOptions ??
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        var sourceFiles = LoadSourceFiles(project, projectDirectory);

        if (sourceFiles.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Project '{projectPath}' did not expose any .akbura or .akcss AdditionalFiles.");
        }

        var componentEditIndex = FindLargestEditableFileIndex(sourceFiles, ".akbura");
        var akcssEditIndex = FindLargestEditableFileIndex(sourceFiles, ".akcss");

        return new FeatureGalleryBenchmarkProject(
            compilation,
            parseOptions,
            rootNamespace,
            projectDirectory,
            sourceFiles,
            componentEditIndex,
            akcssEditIndex);
    }

    public FeatureGalleryBenchmarkSnapshot CreateSnapshot(
        FeatureGalleryEditTarget editTarget = FeatureGalleryEditTarget.None)
    {
        var snapshotId = Interlocked.Increment(ref s_snapshotId);

        var virtualProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "Akbura.Benchmarks",
            "FeatureGallery",
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            snapshotId.ToString(CultureInfo.InvariantCulture));

        var additionalTexts = new AdditionalText[_sourceFiles.Length];

        for (var i = 0; i < _sourceFiles.Length; i++)
        {
            var sourceFile = _sourceFiles[i];

            var relativePath = sourceFile.RelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar);

            var filePath = Path.Combine(virtualProjectDirectory, relativePath);

            additionalTexts[i] = new BenchmarkAdditionalText(
                filePath,
                sourceFile.SourceText);
        }

        var componentOriginal = (BenchmarkAdditionalText)additionalTexts[_componentEditIndex];
        var akcssOriginal = (BenchmarkAdditionalText)additionalTexts[_akcssEditIndex];

        if (editTarget == FeatureGalleryEditTarget.Component)
        {
            MoveToEnd(additionalTexts, componentOriginal);
        }
        else if (editTarget == FeatureGalleryEditTarget.Akcss)
        {
            MoveToEnd(additionalTexts, akcssOriginal);
        }

        var optionsProvider = new BenchmarkAnalyzerConfigOptionsProvider(
            RootNamespace,
            virtualProjectDirectory);

        return new FeatureGalleryBenchmarkSnapshot(
            virtualProjectDirectory,
            ImmutableArray.CreateRange(additionalTexts),
            optionsProvider,
            componentOriginal,
            CreateWhitespaceEdit(componentOriginal),
            akcssOriginal,
            CreateWhitespaceEdit(akcssOriginal));
    }

    public CSharpCompilation CreateCSharpEditCompilation()
    {
        var source =
            "namespace " +
            RootNamespace +
            ";\r\ninternal sealed class __FeatureGalleryGeneratorBenchmarkEdit { }\r\n";

        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            ParseOptions,
            "__FeatureGalleryGeneratorBenchmarkEdit.cs");

        return Compilation.AddSyntaxTrees(syntaxTree);
    }

    private static Project RemoveBenchmarkedGenerators(Project project)
    {
        var analyzerReferences = project.AnalyzerReferences
            .Where(static reference => !IsBenchmarkedGeneratorReference(reference))
            .ToImmutableArray();

        return project.WithAnalyzerReferences(analyzerReferences);
    }

    private static bool IsBenchmarkedGeneratorReference(AnalyzerReference reference)
    {
        return ContainsBenchmarkedGeneratorName(reference.FullPath) ||
            ContainsBenchmarkedGeneratorName(reference.Display);
    }

    private static bool ContainsBenchmarkedGeneratorName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.IndexOf("Akbura.Furioso", StringComparison.OrdinalIgnoreCase) >= 0 ||
            value.IndexOf("Akbura.BlackSilence", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static ImmutableArray<FeatureGallerySourceFile> LoadSourceFiles(
        Project project,
        string projectDirectory)
    {
        var sourceFiles = new List<FeatureGallerySourceFile>();

        foreach (var document in project.AdditionalDocuments)
        {
            var filePath = document.FilePath;

            if (string.IsNullOrWhiteSpace(filePath) || !IsAkburaSourcePath(filePath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(filePath);
            var relativePath = Path.GetRelativePath(projectDirectory, fullPath).Replace('\\', '/');
            var sourceText = document.GetTextAsync().GetAwaiter().GetResult();

            sourceFiles.Add(new FeatureGallerySourceFile(relativePath, sourceText));
        }

        sourceFiles.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));

        return ImmutableArray.CreateRange(sourceFiles);
    }

    private static int FindLargestEditableFileIndex(
        ImmutableArray<FeatureGallerySourceFile> sourceFiles,
        string extension)
    {
        var result = -1;
        var largestLength = -1;

        for (var i = 0; i < sourceFiles.Length; i++)
        {
            var sourceFile = sourceFiles[i];

            if (!Path.GetExtension(sourceFile.RelativePath).Equals(
                    extension,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = Path.GetFileName(sourceFile.RelativePath);

            if (fileName.StartsWith("GlobalUsings.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (sourceFile.SourceText.Length <= largestLength)
            {
                continue;
            }

            result = i;
            largestLength = sourceFile.SourceText.Length;
        }

        if (result < 0)
        {
            throw new InvalidOperationException(
                $"FeatureGallery does not contain an editable '{extension}' file.");
        }

        return result;
    }

    private static BenchmarkAdditionalText CreateWhitespaceEdit(
        BenchmarkAdditionalText original)
    {
        var sourceText = original.SourceText;

        var changedText = sourceText.WithChanges(
            new TextChange(
                new TextSpan(sourceText.Length, 0),
                Environment.NewLine));

        return new BenchmarkAdditionalText(original.Path, changedText);
    }

    private static void MoveToEnd(AdditionalText[] sourceFiles, AdditionalText target)
    {
        var targetIndex = -1;

        for (var i = 0; i < sourceFiles.Length; i++)
        {
            if (ReferenceEquals(sourceFiles[i], target))
            {
                targetIndex = i;
                break;
            }
        }

        if (targetIndex < 0 || targetIndex == sourceFiles.Length - 1)
        {
            return;
        }

        Array.Copy(
            sourceFiles,
            targetIndex + 1,
            sourceFiles,
            targetIndex,
            sourceFiles.Length - targetIndex - 1);

        sourceFiles[sourceFiles.Length - 1] = target;
    }

    private static bool IsAkburaSourcePath(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        return extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase);
    }

    private static void RegisterMSBuild()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
    }

    private static string GetRepositoryRoot()
    {
        var environmentRoot = Environment.GetEnvironmentVariable(
            "AKBURA_REPOSITORY_ROOT");

        if (IsRepositoryRoot(environmentRoot))
        {
            return Path.GetFullPath(environmentRoot!);
        }

        var assembly = typeof(FeatureGalleryBenchmarkProject).Assembly;

        foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (metadata.Key == RepositoryRootMetadataName &&
                IsRepositoryRoot(metadata.Value))
            {
                return Path.GetFullPath(metadata.Value!);
            }
        }

        var currentDirectoryRoot = FindRepositoryRoot(Environment.CurrentDirectory);

        if (currentDirectoryRoot != null)
        {
            return currentDirectoryRoot;
        }

        var assemblyDirectoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);

        if (assemblyDirectoryRoot != null)
        {
            return assemblyDirectoryRoot;
        }

        throw new InvalidOperationException(
            "Could not locate the Akbura repository. " +
            "Set the AKBURA_REPOSITORY_ROOT environment variable.");
    }

    private static string? FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));

        while (directory != null)
        {
            if (IsRepositoryRoot(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsRepositoryRoot(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            File.Exists(Path.Combine(path, FeatureGalleryProjectRelativePath));
    }

    private static string CreateProjectLoadError(
        string projectPath,
        IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return $"Could not create a C# compilation for '{projectPath}'.";
        }

        return
            $"Could not create a C# compilation for '{projectPath}'." +
            Environment.NewLine +
            string.Join(Environment.NewLine, diagnostics);
    }

    private readonly struct FeatureGallerySourceFile
    {
        public FeatureGallerySourceFile(string relativePath, SourceText sourceText)
        {
            RelativePath = relativePath;
            SourceText = sourceText;
        }

        public string RelativePath { get; }

        public SourceText SourceText { get; }
    }
}

internal sealed class FeatureGalleryBenchmarkSnapshot
{
    public FeatureGalleryBenchmarkSnapshot(
        string projectDirectory,
        ImmutableArray<AdditionalText> additionalTexts,
        AnalyzerConfigOptionsProvider optionsProvider,
        BenchmarkAdditionalText componentOriginal,
        BenchmarkAdditionalText componentModified,
        BenchmarkAdditionalText akcssOriginal,
        BenchmarkAdditionalText akcssModified)
    {
        ProjectDirectory = projectDirectory;
        AdditionalTexts = additionalTexts;
        OptionsProvider = optionsProvider;
        ComponentOriginal = componentOriginal;
        ComponentModified = componentModified;
        AkcssOriginal = akcssOriginal;
        AkcssModified = akcssModified;
    }

    public string ProjectDirectory { get; }

    public ImmutableArray<AdditionalText> AdditionalTexts { get; }

    public AnalyzerConfigOptionsProvider OptionsProvider { get; }

    public BenchmarkAdditionalText ComponentOriginal { get; }

    public BenchmarkAdditionalText ComponentModified { get; }

    public BenchmarkAdditionalText AkcssOriginal { get; }

    public BenchmarkAdditionalText AkcssModified { get; }
}

internal sealed class BenchmarkAdditionalText : AdditionalText
{
    public BenchmarkAdditionalText(string path, SourceText sourceText)
    {
        Path = path;
        SourceText = sourceText;
    }

    public override string Path { get; }

    public SourceText SourceText { get; }

    public override SourceText GetText(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SourceText;
    }
}

internal sealed class BenchmarkAnalyzerConfigOptionsProvider :
    AnalyzerConfigOptionsProvider
{
    private static readonly AnalyzerConfigOptions s_emptyOptions =
        new EmptyBenchmarkAnalyzerConfigOptions();

    private readonly AnalyzerConfigOptions _globalOptions;

    public BenchmarkAnalyzerConfigOptionsProvider(
        string rootNamespace,
        string projectDirectory)
    {
        _globalOptions = new BenchmarkAnalyzerConfigOptions(
            rootNamespace,
            projectDirectory);
    }

    public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return s_emptyOptions;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return s_emptyOptions;
    }
}

internal sealed class BenchmarkAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly string _rootNamespace;
    private readonly string _projectDirectory;

    public BenchmarkAnalyzerConfigOptions(
        string rootNamespace,
        string projectDirectory)
    {
        _rootNamespace = rootNamespace;
        _projectDirectory = projectDirectory;
    }

    public override bool TryGetValue(string key, out string value)
    {
        switch (key)
        {
            case "build_property.RootNamespace":
                value = _rootNamespace;
                return true;

            case "build_property.ProjectDir":
                value = _projectDirectory;
                return true;

            default:
                value = null!;
                return false;
        }
    }
}

internal sealed class EmptyBenchmarkAnalyzerConfigOptions :
    AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, out string value)
    {
        value = null!;
        return false;
    }
}
