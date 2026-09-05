using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Builds all component and AKCSS generation inputs for one project snapshot.
/// </summary>
internal static class AkburaGenerationCatalogBuilder
{
    public static AkburaGenerationCatalog Create(
        CSharpCompilation csharpCompilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        string rootNamespace,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        using var componentTrees =
            ImmutableArrayBuilder<AkburaSyntaxTree>.Rent(
                syntaxTrees.Length);

        using var sourceComponentTrees =
            ImmutableArrayBuilder<ComponentSyntaxTree>.Rent(
                syntaxTrees.Length);

        using var akcssTrees =
            ImmutableArrayBuilder<AkcssSyntaxTree>.Rent(
                syntaxTrees.Length);

        using var sourceAkcssTrees =
            ImmutableArrayBuilder<AkcssSyntaxTree>.Rent(
                syntaxTrees.Length);

        for (var i = 0; i < syntaxTrees.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (syntaxTrees[i])
            {
                case ComponentSyntaxTree componentSyntaxTree:
                    componentTrees.Add(componentSyntaxTree);

                    if (!GlobalUsings.IsComponentFile(
                            componentSyntaxTree))
                    {
                        sourceComponentTrees.Add(
                            componentSyntaxTree);
                    }

                    break;

                case AkcssSyntaxTree akcssSyntaxTree:
                    akcssTrees.Add(akcssSyntaxTree);

                    if (!GlobalUsings.IsAkcssFile(
                            akcssSyntaxTree))
                    {
                        sourceAkcssTrees.Add(
                            akcssSyntaxTree);
                    }

                    break;
            }
        }

        var componentSyntaxTrees = componentTrees.ToImmutable();
        var sourceComponentSyntaxTrees = sourceComponentTrees.ToImmutable();
        var akcssSyntaxTrees = akcssTrees.ToImmutable();
        var sourceAkcssSyntaxTrees = sourceAkcssTrees.ToImmutable();

        var compilation = new AkburaCompilation(
            csharpCompilation,
            componentSyntaxTrees,
            akcssSyntaxTrees,
            rootNamespace,
            projectDirectory);

        var sourceMap = new AkcssGenerationSourceMap(
            componentSyntaxTrees,
            akcssSyntaxTrees);

        var moduleTypeNames = new Dictionary<AkburaSyntax, string>(
            sourceAkcssSyntaxTrees.Length +
            sourceComponentSyntaxTrees.Length);

        using var components =
            ImmutableArrayBuilder<ComponentGenerationInput>.Rent(
                sourceComponentSyntaxTrees.Length);

        using var externalAkcssModules =
            ImmutableArrayBuilder<AkcssGenerationInput>.Rent(
                sourceAkcssSyntaxTrees.Length);

        using var inlineAkcssModules =
            ImmutableArrayBuilder<AkcssGenerationInput>.Rent();

        for (var i = 0; i < sourceComponentSyntaxTrees.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var syntaxTree = sourceComponentSyntaxTrees[i];
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            if (semanticModel.GetDeclaredSymbol(syntaxTree.GetRoot()) is not IAkburaComponentSymbol component)
            {
                continue;
            }

            var sourcePath = GetSourcePath(syntaxTree, projectDirectory);

            components.Add(new ComponentGenerationInput(
                component,
                semanticModel,
                sourcePath));

            var modules = component.AkcssModules;

            for (var moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
            {
                var module = modules[moduleIndex];

                if (!module.IsInlined ||
                    module.DeclaringSyntax is not InlineAkcssBlockSyntax moduleSyntax)
                {
                    continue;
                }

                var moduleIdentity = GetInlineAkcssModuleIdentity(sourcePath, moduleIndex);

                moduleTypeNames[moduleSyntax] =
                    AkcssGeneratedModuleNames
                        .GetFullyQualifiedTypeName(
                            rootNamespace,
                            moduleIdentity);

                sourceMap.RegisterModule(module);

                inlineAkcssModules.Add(
                    new AkcssGenerationInput(
                        module,
                        semanticModel,
                        sourcePath,
                        moduleIdentity,
                        semanticModel.GetAkcssCSharpUsingDirectives(module)));
            }
        }

        for (var i = 0; i < sourceAkcssSyntaxTrees.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var syntaxTree = sourceAkcssSyntaxTrees[i];
            var semanticModel = compilation.GetSemanticModel(
                syntaxTree);
            var root = syntaxTree.GetRootSyntax();

            if (semanticModel.GetDeclaredSymbol(root) is not
                IAkcssModuleSymbol module ||
                module.DeclaringSyntax is not { } moduleSyntax)
            {
                continue;
            }

            var sourcePath = GetSourcePath(
                syntaxTree,
                projectDirectory);

            moduleTypeNames[moduleSyntax] =
                AkcssGeneratedModuleNames
                    .GetFullyQualifiedTypeName(
                        rootNamespace,
                        sourcePath);

            sourceMap.RegisterModule(module);

            externalAkcssModules.Add(
                new AkcssGenerationInput(
                    module,
                    semanticModel,
                    sourcePath,
                    sourcePath,
                    semanticModel.GetAkcssCSharpUsingDirectives(
                        module)));
        }

        return new AkburaGenerationCatalog(
            compilation,
            rootNamespace,
            projectDirectory,
            components.ToImmutable(),
            externalAkcssModules.ToImmutable(),
            inlineAkcssModules.ToImmutable(),
            moduleTypeNames,
            sourceMap);
    }

    private static string GetSourcePath(
        ComponentSyntaxTree syntaxTree,
        string projectDirectory)
    {
        if (TryGetProjectRelativeSourcePath(
                syntaxTree.FilePath,
                projectDirectory,
                out var sourcePath))
        {
            return sourcePath;
        }

        return AkcssGeneratedModuleNames.NormalizeSourcePath(
            Path.GetFileName(syntaxTree.FilePath));
    }

    private static string GetSourcePath(
        AkcssSyntaxTree syntaxTree,
        string projectDirectory)
    {
        if (TryGetProjectRelativeSourcePath(
                syntaxTree.FilePath,
                projectDirectory,
                out var sourcePath))
        {
            return sourcePath;
        }

        if (!string.IsNullOrWhiteSpace(
                syntaxTree.LogicalName))
        {
            return AkcssGeneratedModuleNames.NormalizeSourcePath(
                syntaxTree.LogicalName);
        }

        return AkcssGeneratedModuleNames.NormalizeSourcePath(
            Path.GetFileName(syntaxTree.FilePath));
    }

    private static bool TryGetProjectRelativeSourcePath(
        string filePath,
        string projectDirectory,
        out string sourcePath)
    {
        sourcePath = string.Empty;

        if (string.IsNullOrWhiteSpace(projectDirectory) ||
            string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var projectPath = Path
            .GetFullPath(projectDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var fullSourcePath = Path.GetFullPath(filePath);
        var projectPrefix = projectPath + Path.DirectorySeparatorChar;

        if (!fullSourcePath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        sourcePath = AkcssGeneratedModuleNames.NormalizeSourcePath(
            fullSourcePath[projectPrefix.Length..]);

        return true;
    }

    private static string GetInlineAkcssModuleIdentity(
        string componentSourcePath,
        int moduleIndex)
    {
        return AkcssGeneratedModuleNames.NormalizeSourcePath(
            componentSourcePath +
            ".inline." +
            moduleIndex.ToString(
                CultureInfo.InvariantCulture) +
            ".akcss");
    }
}
