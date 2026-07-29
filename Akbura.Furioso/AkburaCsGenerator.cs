using Akbura.Language;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Akbura.Furioso;

[Generator(LanguageNames.CSharp)]
public sealed class AkburaCsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var projectOptions =
            context.AnalyzerConfigOptionsProvider
                .Select(static (provider, _) =>
                    GeneratorProjectOptions.Create(
                        provider.GlobalOptions));

        var syntaxTrees = context.AdditionalTextsProvider
            .Where(static file =>
                IsAkburaSourcePath(file.Path))
            .Combine(projectOptions)
            .Select(static (input, cancellationToken) =>
                ParseSyntaxTree(
                    input.Left,
                    input.Right,
                    cancellationToken))
            .Where(static syntaxTree =>
                syntaxTree != null)
            .Select(static (syntaxTree, _) =>
                syntaxTree!)
            .Collect();

        var generationInput = syntaxTrees
            .Combine(context.CompilationProvider)
            .Combine(projectOptions);

        context.RegisterSourceOutput(
            generationInput,
            static (context, snapshot) =>
                GenerateSources(
                    context,
                    snapshot.Left.Left,
                    snapshot.Left.Right,
                    snapshot.Right));
    }

    private static AkburaSyntaxTree? ParseSyntaxTree(
        AdditionalText file,
        GeneratorProjectOptions projectOptions,
        CancellationToken cancellationToken)
    {
        var sourceText = file.GetText(cancellationToken);
        if (sourceText == null)
        {
            return null;
        }

        var extension = Path.GetExtension(file.Path);

        if (extension.Equals(
            ".akcss",
            StringComparison.OrdinalIgnoreCase))
        {
            var sourcePath = GetProjectRelativeSourcePath(
                file.Path,
                projectOptions.ProjectDirectory);

            var logicalName =
                AkcssGeneratedModuleNames.GetMetadataName(
                    projectOptions.RootNamespace,
                    sourcePath);

            return AkcssSyntaxTree.ParseText(
                sourceText,
                file.Path,
                logicalName,
                cancellationToken);
        }

        if (extension.Equals(
            ".akbura",
            StringComparison.OrdinalIgnoreCase))
        {
            return ComponentSyntaxTree.ParseText(
                sourceText,
                file.Path,
                cancellationToken);
        }

        return null;
    }

    private static string GetProjectRelativeSourcePath(
        string filePath,
        string projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectDirectory) &&
            !string.IsNullOrWhiteSpace(filePath))
        {
            var projectPath = Path
                .GetFullPath(projectDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            var fullSourcePath = Path.GetFullPath(filePath);
            var projectPrefix =
                projectPath + Path.DirectorySeparatorChar;

            if (fullSourcePath.StartsWith(
                projectPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                return AkcssGeneratedModuleNames
                    .NormalizeSourcePath(
                        fullSourcePath[
                            projectPrefix.Length..]);
            }
        }

        return AkcssGeneratedModuleNames
            .NormalizeSourcePath(
                Path.GetFileName(filePath));
    }

    private static void GenerateSources(
        SourceProductionContext context,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        Compilation compilation,
        GeneratorProjectOptions projectOptions)
    {
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            return;
        }

        var componentTrees = ImmutableArray.CreateBuilder<AkburaSyntaxTree>();
        var sourceComponentTrees = ImmutableArray.CreateBuilder<ComponentSyntaxTree>();
        var akcssTrees = ImmutableArray.CreateBuilder<AkcssSyntaxTree>();
        var sourceAkcssTrees = ImmutableArray.CreateBuilder<AkcssSyntaxTree>();

        foreach (var syntaxTree in syntaxTrees)
        {
            switch (syntaxTree)
            {
                case ComponentSyntaxTree componentSyntaxTree:
                    componentTrees.Add(syntaxTree);
                    if (!GlobalUsings.IsComponentFile(componentSyntaxTree))
                    {
                        sourceComponentTrees.Add(componentSyntaxTree);
                    }

                    break;
                case AkcssSyntaxTree akcssSyntaxTree:
                    akcssTrees.Add(akcssSyntaxTree);
                    if (!GlobalUsings.IsAkcssFile(akcssSyntaxTree))
                    {
                        sourceAkcssTrees.Add(akcssSyntaxTree);
                    }

                    break;
            }
        }

        var semanticHost = sourceComponentTrees.Count > 0
            ? sourceComponentTrees[0]
            : CreateSemanticHost(projectOptions.ProjectDirectory, context.CancellationToken);

        if (sourceComponentTrees.Count == 0)
        {
            componentTrees.Add(semanticHost);
        }

        var componentSyntaxTrees = componentTrees.ToImmutable();
        var sourceComponents = sourceComponentTrees.ToImmutable();
        var akcssSyntaxTrees = akcssTrees.ToImmutable();
        var sourceAkcssSyntaxTrees = sourceAkcssTrees.ToImmutable();
        var akburaCompilation = new AkburaCompilation(
            csharpCompilation,
            componentSyntaxTrees,
            akcssSyntaxTrees,
            projectOptions.RootNamespace,
            projectOptions.ProjectDirectory);
        var semanticModel = akburaCompilation.GetSemanticModel(semanticHost);
        var mappedSyntaxTrees = new List<AkburaSyntaxTree>(
            componentSyntaxTrees.Length + akcssSyntaxTrees.Length);
        mappedSyntaxTrees.AddRange(componentSyntaxTrees);
        mappedSyntaxTrees.AddRange(akcssSyntaxTrees);
        var sourceMap = new AkcssGenerationSourceMap(mappedSyntaxTrees);
        var akcssModuleTypeNames = new Dictionary<Akbura.Language.Syntax.AkburaSyntax, string>();
        var componentInputs = new ComponentGenerationInput?[sourceComponents.Length];
        var externalAkcssInputs = new AkcssGenerationInput?[sourceAkcssSyntaxTrees.Length];
        var inlineAkcssInputs = new List<AkcssGenerationInput>();

        for (var index = 0; index < sourceComponents.Length; index++)
        {
            var syntaxTree = sourceComponents[index];
            var model = akburaCompilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            if (model.GetDeclaredSymbol(root) is not IAkburaComponentSymbol symbol)
            {
                continue;
            }

            var sourcePath = GetSourcePath(syntaxTree, projectOptions.ProjectDirectory);
            componentInputs[index] = new ComponentGenerationInput(
                model,
                symbol,
                sourcePath);
            for (var moduleIndex = 0; moduleIndex < symbol.AkcssModules.Length; moduleIndex++)
            {
                var module = symbol.AkcssModules[moduleIndex];
                var moduleIdentity = GetInlineAkcssModuleIdentity(sourcePath, moduleIndex);
                akcssModuleTypeNames[module.DeclaringSyntax] =
                    AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(
                        projectOptions.RootNamespace,
                        moduleIdentity);
                sourceMap.RegisterModule(module);
                inlineAkcssInputs.Add(new AkcssGenerationInput(
                    module,
                    sourcePath,
                    moduleIdentity,
                    model.GetAkcssCSharpUsingDirectives(module)));
            }
        }

        for (var index = 0; index < sourceAkcssSyntaxTrees.Length; index++)
        {
            var syntaxTree = sourceAkcssSyntaxTrees[index];
            var root = syntaxTree.GetRootSyntax();
            if (semanticModel.GetDeclaredSymbol(root) is not IAkcssModuleSymbol symbol)
            {
                continue;
            }

            var sourcePath = GetSourcePath(syntaxTree, projectOptions.ProjectDirectory);
            externalAkcssInputs[index] = new AkcssGenerationInput(
                symbol,
                sourcePath,
                sourcePath,
                semanticModel.GetAkcssCSharpUsingDirectives(symbol));

            akcssModuleTypeNames[symbol.DeclaringSyntax] =
                AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(
                    projectOptions.RootNamespace,
                    sourcePath);
            sourceMap.RegisterModule(symbol);
        }

        var componentResults = new GeneratedSource?[sourceComponents.Length];
        var akcssResults = new GeneratedSource?[sourceAkcssSyntaxTrees.Length];
        var inlineAkcssResults = new GeneratedSource?[inlineAkcssInputs.Count];

        Parallel.For(
            0,
            componentInputs.Length,
            new ParallelOptions
            {
                CancellationToken = context.CancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, EnvironmentProcessorCount.Count / 2),
            },
            index =>
            {
                if (componentInputs[index] is not { } input)
                {
                    return;
                }

                var source = ComponentGenerator.Generate(
                    input.Symbol,
                    input.SemanticModel,
                    input.SourcePath,
                    akcssModuleTypeNames);
                componentResults[index] = new GeneratedSource(
                    ComponentGenerator.GetHintName(input.Symbol, input.SourcePath),
                    SourceText.From(source, Encoding.UTF8));
            });

        Parallel.For(
            0,
            sourceAkcssSyntaxTrees.Length,
            new ParallelOptions
            {
                CancellationToken = context.CancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, EnvironmentProcessorCount.Count / 2),
            },
            index =>
            {
                if (externalAkcssInputs[index] is not { } input)
                {
                    return;
                }

                var source = AkcssGenerator.Generate(
                    input.Symbol,
                    sourceMap,
                    input.SourcePath,
                    projectOptions.RootNamespace,
                    input.ModuleIdentity,
                    input.UsingDirectives);

                akcssResults[index] = new GeneratedSource(
                    AkcssGenerator.GetHintName(
                        input.Symbol,
                        input.SourcePath,
                        input.ModuleIdentity),
                    SourceText.From(source, Encoding.UTF8));
            });

        Parallel.For(
            0,
            inlineAkcssInputs.Count,
            new ParallelOptions
            {
                CancellationToken = context.CancellationToken,
                MaxDegreeOfParallelism = Math.Max(1, EnvironmentProcessorCount.Count / 2),
            },
            index =>
            {
                var input = inlineAkcssInputs[index];
                var source = AkcssGenerator.Generate(
                    input.Symbol,
                    sourceMap,
                    input.SourcePath,
                    projectOptions.RootNamespace,
                    input.ModuleIdentity,
                    input.UsingDirectives);
                inlineAkcssResults[index] = new GeneratedSource(
                    AkcssGenerator.GetHintName(
                        input.Symbol,
                        input.SourcePath,
                        input.ModuleIdentity),
                    SourceText.From(source, Encoding.UTF8));
            });

        AddGeneratedSources(context, componentResults);
        AddGeneratedSources(context, akcssResults);
        AddGeneratedSources(context, inlineAkcssResults);
        ReportGlobalUsingsDiagnostics(context, syntaxTrees);

        foreach (var syntaxTree in sourceComponents)
        {
            var model = akburaCompilation.GetSemanticModel(syntaxTree);
            foreach (var diagnostic in model.GetSemanticDiagnostics(syntaxTree.GetRoot()))
            {
                context.ReportDiagnostic(CreateDiagnostic(syntaxTree, diagnostic));
            }
        }

        foreach (var syntaxTree in sourceAkcssSyntaxTrees)
        {
            var model = akburaCompilation.GetSemanticModel(syntaxTree);
            foreach (var diagnostic in model.GetSemanticDiagnostics(syntaxTree.GetRoot()))
            {
                context.ReportDiagnostic(CreateDiagnostic(syntaxTree, diagnostic));
            }
        }
    }

    private static void AddGeneratedSources(
        SourceProductionContext context,
        IEnumerable<GeneratedSource?> results)
    {
        foreach (var result in results)
        {
            if (result is { } generatedSource)
            {
                context.AddSource(generatedSource.HintName, generatedSource.SourceText);
            }
        }
    }

    private static Diagnostic CreateDiagnostic(
        AkburaSyntaxTree syntaxTree,
        AkburaSemanticDiagnostic diagnostic)
    {
        var severity = diagnostic.Severity switch
        {
            Akbura.Language.Syntax.AkburaDiagnosticSeverity.Hidden => DiagnosticSeverity.Hidden,
            Akbura.Language.Syntax.AkburaDiagnosticSeverity.Info => DiagnosticSeverity.Info,
            Akbura.Language.Syntax.AkburaDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Error,
        };
        var descriptor = new DiagnosticDescriptor(
            diagnostic.Code,
            diagnostic.Code,
            diagnostic.Message,
            "Akbura",
            severity,
            isEnabledByDefault: true);
        var span = diagnostic.Syntax.Span;
        var location = Microsoft.CodeAnalysis.Location.Create(
            syntaxTree.FilePath,
            span,
            syntaxTree.Text.Lines.GetLinePositionSpan(span));
        return Diagnostic.Create(descriptor, location);
    }

    private static void ReportGlobalUsingsDiagnostics(
        SourceProductionContext context,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees)
    {
        foreach (var syntaxTree in syntaxTrees)
        {
            if (GlobalUsings.IsComponentFile(syntaxTree))
            {
                foreach (var member in syntaxTree.GetRoot().Members)
                {
                    if (member is not Akbura.Language.Syntax.UsingDirectiveSyntax)
                    {
                        context.ReportDiagnostic(
                            CreateInvalidGlobalUsingsDiagnostic(
                                syntaxTree,
                                member));
                    }
                }
            }
            else if (syntaxTree is AkcssSyntaxTree akcssSyntaxTree &&
                     GlobalUsings.IsAkcssFile(akcssSyntaxTree))
            {
                foreach (var member in akcssSyntaxTree.GetRoot().Members)
                {
                    if (member is not Akbura.Language.Syntax.AkcssUsingDirectiveSyntax)
                    {
                        context.ReportDiagnostic(
                            CreateInvalidGlobalUsingsDiagnostic(
                                syntaxTree,
                                member));
                    }
                }
            }
        }
    }

    private static Diagnostic CreateInvalidGlobalUsingsDiagnostic(
        AkburaSyntaxTree syntaxTree,
        Akbura.Language.Syntax.AkburaSyntax syntax)
    {
        var descriptor = new DiagnosticDescriptor(
            ErrorCodes.AKBURA_SEMANTIC_GlobalUsingsFileContainsNonUsing,
            ErrorCodes.AKBURA_SEMANTIC_GlobalUsingsFileContainsNonUsing,
            "Global usings file '{0}' may contain only using directives",
            "Akbura",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
        var span = syntax.Span;
        var location = Microsoft.CodeAnalysis.Location.Create(
            syntaxTree.FilePath,
            span,
            syntaxTree.Text.Lines.GetLinePositionSpan(span));
        return Diagnostic.Create(
            descriptor,
            location,
            Path.GetFileName(syntaxTree.FilePath));
    }

    private static ComponentSyntaxTree CreateSemanticHost(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var path = string.IsNullOrWhiteSpace(projectDirectory)
            ? "__AkburaGeneratorHost.akbura"
            : Path.Combine(projectDirectory, "__AkburaGeneratorHost.akbura");
        return ComponentSyntaxTree.ParseText(SourceText.From(string.Empty), path, cancellationToken);
    }

    private static string GetSourcePath(
        AkcssSyntaxTree syntaxTree,
        string projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectDirectory) &&
            !string.IsNullOrWhiteSpace(syntaxTree.FilePath))
        {
            var projectPath = Path.GetFullPath(projectDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var sourcePath = Path.GetFullPath(syntaxTree.FilePath);
            var projectPrefix = projectPath + Path.DirectorySeparatorChar;
            if (sourcePath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return AkcssGeneratedModuleNames.NormalizeSourcePath(
                    sourcePath.Substring(projectPrefix.Length));
            }
        }

        if (!string.IsNullOrWhiteSpace(syntaxTree.LogicalName))
        {
            return AkcssGeneratedModuleNames.NormalizeSourcePath(syntaxTree.LogicalName);
        }

        return AkcssGeneratedModuleNames.NormalizeSourcePath(
            Path.GetFileName(syntaxTree.FilePath));
    }

    private static string GetSourcePath(
        ComponentSyntaxTree syntaxTree,
        string projectDirectory)
    {
        if (!string.IsNullOrWhiteSpace(projectDirectory) &&
            !string.IsNullOrWhiteSpace(syntaxTree.FilePath))
        {
            var projectPath = Path.GetFullPath(projectDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var sourcePath = Path.GetFullPath(syntaxTree.FilePath);
            var projectPrefix = projectPath + Path.DirectorySeparatorChar;
            if (sourcePath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return AkcssGeneratedModuleNames.NormalizeSourcePath(
                    sourcePath.Substring(projectPrefix.Length));
            }
        }

        return AkcssGeneratedModuleNames.NormalizeSourcePath(
            Path.GetFileName(syntaxTree.FilePath));
    }

    private static string GetInlineAkcssModuleIdentity(
        string componentSourcePath,
        int moduleIndex)
    {
        return AkcssGeneratedModuleNames.NormalizeSourcePath(
            componentSourcePath + ".inline." +
            moduleIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ".akcss");
    }

    private static bool IsAkburaSourcePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct ComponentGenerationInput(
    AkburaSemanticModel SemanticModel,
    IAkburaComponentSymbol Symbol,
    string SourcePath);

internal readonly record struct AkcssGenerationInput(
    IAkcssModuleSymbol Symbol,
    string SourcePath,
    string ModuleIdentity,
    ImmutableArray<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax> UsingDirectives);

internal readonly record struct AkburaAndAkcssFile(SourceText? SourceText, string Path)
{
    public AkburaSyntaxTree ToSyntaxTree(CancellationToken cancellationToken = default)
    {
        var sourceText = SourceText ?? throw new InvalidOperationException("The source text is unavailable.");
        var extension = System.IO.Path.GetExtension(Path);

        if (extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase))
        {
            return AkcssSyntaxTree.ParseText(sourceText, Path, cancellationToken);
        }

        if (extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase))
        {
            return AkburaSyntaxTree.ParseText(sourceText, Path, cancellationToken);
        }

        throw new UnreachableException();
    }
}

internal readonly record struct GeneratorProjectOptions(
    string RootNamespace,
    string ProjectDirectory)
{
    private const string RootNamespaceProperty = "build_property.RootNamespace";
    private const string ProjectDirectoryProperty = "build_property.ProjectDir";

    public static GeneratorProjectOptions Create(AnalyzerConfigOptions options)
    {
        options.TryGetValue(RootNamespaceProperty, out var rootNamespace);
        options.TryGetValue(ProjectDirectoryProperty, out var projectDirectory);
        return new GeneratorProjectOptions(
            rootNamespace ?? string.Empty,
            projectDirectory ?? string.Empty);
    }
}
