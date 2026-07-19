using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Akbura.Language;

internal static class AkburaModuleManifestBuilder
{
    private static readonly SymbolDisplayFormat s_typeDisplayFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            (SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions &
             ~SymbolDisplayMiscellaneousOptions.UseSpecialTypes) |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static AkburaModuleManifest Build(
        string assemblyName,
        string rootNamespace,
        IEnumerable<AkburaModuleSourceText> sources,
        CSharpCompilation csharpCompilation)
    {
        if (sources == null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        if (csharpCompilation == null)
        {
            throw new ArgumentNullException(nameof(csharpCompilation));
        }

        var orderedSources = sources
            .OrderBy(static source => source.SourceCodePath, StringComparer.Ordinal)
            .ToImmutableArray();
        var componentTrees = new Dictionary<string, AkburaSyntaxTree>(StringComparer.Ordinal);
        var akcssTrees = new Dictionary<string, AkcssSyntaxTree>(StringComparer.Ordinal);

        foreach (var source in orderedSources)
        {
            var sourceCodePath = NormalizeSourceCodePath(source.SourceCodePath);
            var extension = Path.GetExtension(sourceCodePath);
            if (extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase))
            {
                componentTrees.Add(
                    sourceCodePath,
                    AkburaSyntaxTree.ParseText(source.Text, sourceCodePath));
            }
            else if (extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase))
            {
                var logicalName = GetAkcssMetadataName(rootNamespace, sourceCodePath);
                akcssTrees.Add(
                    sourceCodePath,
                    AkcssSyntaxTree.ParseText(source.Text, sourceCodePath, logicalName));
            }
            else
            {
                throw new ArgumentException(
                    $"Akbura module source '{sourceCodePath}' must have an .akbura or .akcss extension.",
                    nameof(sources));
            }
        }

        var compilation = new AkburaCompilation(
            csharpCompilation,
            componentTrees.Values,
            akcssTrees.Values,
            rootNamespace ?? string.Empty);

        using var builder = ImmutableArrayBuilder<AkburaModuleSource>.Rent(orderedSources.Length);
        foreach (var source in orderedSources)
        {
            var sourceCodePath = NormalizeSourceCodePath(source.SourceCodePath);
            builder.Add(componentTrees.TryGetValue(sourceCodePath, out var componentTree)
                ? BuildComponentSource(sourceCodePath, componentTree, compilation)
                : BuildAkcssSource(sourceCodePath, akcssTrees[sourceCodePath]));
        }

        return new AkburaModuleManifest(
            AkburaModuleManifest.CurrentFormatVersion,
            assemblyName ?? string.Empty,
            builder.ToImmutable());
    }

    private static AkburaModuleSource BuildComponentSource(
        string sourceCodePath,
        AkburaSyntaxTree syntaxTree,
        AkburaCompilation compilation)
    {
        var root = syntaxTree.GetRoot();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        if (semanticModel.GetDeclaredSymbol(root) is not IAkburaComponentSymbol component)
        {
            throw new InvalidOperationException(
                $"Could not create the component symbol for '{sourceCodePath}'.");
        }

        var metadataName = component.MetadataName.Length == 0
            ? string.Empty
            : "global::" + component.MetadataName;
        var declaration = new AkburaModuleDeclaration(
            DeclarationKind.Component,
            component.Name,
            metadataName,
            root.FullSpan.Start,
            root.FullSpan.Length,
            ImmutableArray<AkburaModuleDeclaration>.Empty,
            component: CreateComponentSignature(component));

        return new AkburaModuleSource(
            sourceCodePath,
            AkburaModuleSourceKind.Component,
            [declaration]);
    }

    private static AkburaModuleComponent CreateComponentSignature(
        IAkburaComponentSymbol component)
    {
        using var parameters = ImmutableArrayBuilder<AkburaModuleComponentParameter>.Rent(
            component.Parameters.Length);
        for (var ordinal = 0; ordinal < component.Parameters.Length; ordinal++)
        {
            var parameter = component.Parameters[ordinal];
            parameters.Add(new AkburaModuleComponentParameter(
                ordinal,
                parameter.Name,
                GetRequiredTypeName(parameter.Type, $"parameter '{parameter.Name}'"),
                parameter.BindingKind,
                parameter.HasDefaultValue,
                parameter.DeclarationSyntax.FullSpan.Start,
                parameter.DeclarationSyntax.FullSpan.Length));
        }

        using var injectedServices = ImmutableArrayBuilder<AkburaModuleComponentInject>.Rent(
            component.InjectedServices.Length);
        for (var ordinal = 0; ordinal < component.InjectedServices.Length; ordinal++)
        {
            var injectedService = component.InjectedServices[ordinal];
            injectedServices.Add(new AkburaModuleComponentInject(
                ordinal,
                injectedService.Name,
                GetRequiredTypeName(injectedService.Type, $"injected service '{injectedService.Name}'"),
                injectedService.IsOptional,
                injectedService.DeclarationSyntax.FullSpan.Start,
                injectedService.DeclarationSyntax.FullSpan.Length));
        }

        return new AkburaModuleComponent(
            GetRequiredTypeName(component.BaseType, $"component '{component.MetadataName}' base type"),
            parameters.ToImmutable(),
            injectedServices.ToImmutable());
    }

    private static string GetRequiredTypeName(
        CSharpSymbolDefinition definition,
        string description)
    {
        if (definition.Symbol is not ITypeSymbol { TypeKind: not TypeKind.Error } type)
        {
            throw new InvalidOperationException(
                $"Could not resolve the {description} while creating the Akbura module manifest.");
        }

        return type.ToDisplayString(s_typeDisplayFormat);
    }

    private static AkburaModuleSource BuildAkcssSource(
        string sourceCodePath,
        AkcssSyntaxTree syntaxTree)
    {
        var declaration = DeclarationTreeBuilder.ForSyntaxDeclaration(syntaxTree);
        return new AkburaModuleSource(
            sourceCodePath,
            AkburaModuleSourceKind.Akcss,
            [CreateAkcssDeclaration(declaration, syntaxTree.LogicalName)]);
    }

    private static AkburaModuleDeclaration CreateAkcssDeclaration(
        Declaration declaration,
        string? metadataName = null)
    {
        var syntax = DeclarationFacts.GetSyntax(declaration);
        using var children = ImmutableArrayBuilder<AkburaModuleDeclaration>.Rent();
        foreach (var child in declaration.Children)
        {
            children.Add(CreateAkcssDeclaration(child));
        }

        var akcssUtility = declaration.Kind == DeclarationKind.AkcssUtility
            ? CreateAkcssUtility(Unsafe.As<AkcssUtilityDeclarationSyntax>(syntax))
            : null;

        return new AkburaModuleDeclaration(
            declaration.Kind,
            declaration.Name,
            metadataName,
            syntax.FullSpan.Start,
            syntax.FullSpan.Length,
            children.ToImmutable(),
            akcssUtility);
    }

    private static AkburaModuleAkcssUtility CreateAkcssUtility(
        AkcssUtilityDeclarationSyntax utility)
    {
        var selector = utility.Selector;
        using var parameters = ImmutableArrayBuilder<AkburaModuleAkcssUtilityParameter>.Rent(
            selector.Parameters.Count);
        for (var ordinal = 0; ordinal < selector.Parameters.Count; ordinal++)
        {
            var parameter = selector.Parameters[ordinal];
            parameters.Add(new AkburaModuleAkcssUtilityParameter(
                ordinal,
                parameter.ParamName.Identifier.ValueText,
                parameter.Type.ToCSharp().ToString(),
                parameter.FullSpan.Start,
                parameter.FullSpan.Length));
        }

        return new AkburaModuleAkcssUtility(
            selector.TargetType?.ToCSharp().ToString(),
            parameters.ToImmutable());
    }

    private static string GetAkcssMetadataName(
        string rootNamespace,
        string sourceCodePath)
    {
        var pathWithoutExtension = sourceCodePath.EndsWith(
            ".akcss",
            StringComparison.OrdinalIgnoreCase)
            ? sourceCodePath[..^".akcss".Length]
            : sourceCodePath;
        var name = pathWithoutExtension
            .Replace('/', '.')
            .Replace('\\', '.')
            .Trim('.');

        return string.IsNullOrWhiteSpace(rootNamespace)
            ? name + ".akcss"
            : rootNamespace.Trim('.') + "." + name + ".akcss";
    }

    private static string NormalizeSourceCodePath(string path)
    {
        var normalized = (path ?? string.Empty).Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        normalized = normalized.TrimStart('/');
        if (normalized.Length == 0 ||
            normalized == ".." ||
            normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized.Contains("/../", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Akbura source resource path '{path}' is not project-relative.",
                nameof(path));
        }

        return normalized;
    }
}
