using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language;

internal static class AkburaComponentProbeCompilationBuilder
{
    public static CSharpCompilation Build(
        CSharpCompilation compilation,
        ImmutableArray<AkburaSyntaxTree> syntaxTrees,
        string rootNamespace,
        string projectDirectory)
    {
        using var builder =
            ImmutableArrayBuilder<SyntaxTree>.Rent();
        var globalUsings =
            GetGlobalUsingDirectives(syntaxTrees);
        var parseOptions =
            compilation.SyntaxTrees.FirstOrDefault()?.Options
                as CSharpParseOptions ??
            CSharpParseOptions.Default.WithLanguageVersion(
                LanguageVersion.Preview);

        foreach (var syntaxTree in syntaxTrees)
        {
            if (GlobalUsings.IsComponentFile(syntaxTree) ||
                string.IsNullOrWhiteSpace(
                    syntaxTree.ComponentName))
            {
                continue;
            }

            builder.Add(CreateSyntaxTree(
                compilation,
                syntaxTree,
                globalUsings,
                rootNamespace,
                projectDirectory,
                parseOptions));
        }

        var probeTrees = builder.ToImmutable();
        return probeTrees.IsDefaultOrEmpty
            ? compilation
            : compilation.AddSyntaxTrees(probeTrees);
    }

    private static SyntaxTree CreateSyntaxTree(
        CSharpCompilation compilation,
        AkburaSyntaxTree syntaxTree,
        ImmutableArray<CSharp.UsingDirectiveSyntax> globalUsings,
        string rootNamespace,
        string projectDirectory,
        CSharpParseOptions parseOptions)
    {
        var namespaceName = GetNamespaceName(
            syntaxTree,
            rootNamespace,
            projectDirectory);
        var metadataName = namespaceName.Length == 0
            ? syntaxTree.ComponentName
            : namespaceName + "." +
              syntaxTree.ComponentName;
        var componentTypeInfo =
            AkburaComponentTypeResolver.Resolve(
                compilation,
                metadataName);
        var component = CreateComponentDeclaration(
            syntaxTree,
            componentTypeInfo);
        CSharp.MemberDeclarationSyntax member =
            namespaceName.Length == 0
                ? component
                : CSharpSyntaxFactory
                    .FileScopedNamespaceDeclaration(
                        CSharpSyntaxFactory.ParseName(
                            namespaceName))
                    .WithMembers(
                        CSharpSyntaxFactory.SingletonList<
                            CSharp.MemberDeclarationSyntax>(
                            component));

        var compilationUnit =
            CSharpSyntaxFactory.CompilationUnit()
                .WithUsings(CSharpSyntaxFactory.List(
                    GetUsingDirectives(
                        syntaxTree,
                        globalUsings)))
                .WithMembers(
                    CSharpSyntaxFactory.SingletonList(
                        member));

        return CSharpSyntaxTree.Create(
            compilationUnit,
            parseOptions,
            path: syntaxTree.FilePath +
                  ".AkburaProbe.g.cs");
    }

    private static CSharp.ClassDeclarationSyntax
        CreateComponentDeclaration(
            AkburaSyntaxTree syntaxTree,
            AkburaComponentTypeInfo componentTypeInfo)
    {
        var componentName =
            ToCSharpIdentifier(syntaxTree.ComponentName);
        var component =
            CSharpSyntaxFactory.ClassDeclaration(
                    componentName)
                .WithModifiers(
                    CSharpSyntaxFactory.TokenList(
                        GetAccessibilityToken(
                            componentTypeInfo.DeclaredType),
                        CSharpSyntaxFactory.Token(
                            CSharpSyntaxKind.PartialKeyword)))
                .WithMembers(CSharpSyntaxFactory.List(
                    CreateParameterMembers(
                        syntaxTree.GetRoot(),
                        componentName)));

        if (componentTypeInfo
                .ShouldDeclareAkburaControlBase &&
            componentTypeInfo.AkburaControlType != null)
        {
            var baseType =
                CSharpSyntaxFactory.ParseTypeName(
                    componentTypeInfo.AkburaControlType
                        .ToDisplayString(
                            SymbolDisplayFormat
                                .FullyQualifiedFormat));
            component = component.WithBaseList(
                CSharpSyntaxFactory.BaseList(
                    CSharpSyntaxFactory
                        .SingletonSeparatedList<
                            CSharp.BaseTypeSyntax>(
                            CSharpSyntaxFactory
                                .SimpleBaseType(
                                    baseType))));
        }

        return component;
    }

    private static ImmutableArray<
        CSharp.MemberDeclarationSyntax>
        CreateParameterMembers(
            AkburaDocumentSyntax root,
            string componentName)
    {
        using var builder =
            ImmutableArrayBuilder<
                CSharp.MemberDeclarationSyntax>.Rent();

        foreach (var parameter in root.Members
                     .OfType<ParamDeclarationSyntax>())
        {
            var name = parameter.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var type = GetParameterType(parameter);
            var escapedName = ToCSharpIdentifier(name);
            var parameterDescriptorType =
                CSharpSyntaxFactory.ParseTypeName(
                    "global::Akbura.ComponentTree.Parameter<" +
                    componentName +
                    ", " +
                    type +
                    ">");

            builder.Add(
                CSharpSyntaxFactory.FieldDeclaration(
                        CSharpSyntaxFactory
                            .VariableDeclaration(
                                parameterDescriptorType)
                            .WithVariables(
                                CSharpSyntaxFactory
                                    .SingletonSeparatedList(
                                        CSharpSyntaxFactory
                                            .VariableDeclarator(
                                                escapedName +
                                                "Property"))))
                    .WithModifiers(
                        CSharpSyntaxFactory.TokenList(
                            CSharpSyntaxFactory.Token(
                                CSharpSyntaxKind
                                    .PublicKeyword),
                            CSharpSyntaxFactory.Token(
                                CSharpSyntaxKind
                                    .StaticKeyword),
                            CSharpSyntaxFactory.Token(
                                CSharpSyntaxKind
                                    .ReadOnlyKeyword))));

            builder.Add(
                CSharpSyntaxFactory.PropertyDeclaration(
                        type,
                        escapedName)
                    .WithModifiers(
                        CSharpSyntaxFactory.TokenList(
                            CSharpSyntaxFactory.Token(
                                CSharpSyntaxKind
                                    .PublicKeyword)))
                    .WithAccessorList(
                        CSharpSyntaxFactory.AccessorList(
                            CSharpSyntaxFactory.List(
                            [
                                CSharpSyntaxFactory
                                    .AccessorDeclaration(
                                        CSharpSyntaxKind
                                            .GetAccessorDeclaration)
                                    .WithSemicolonToken(
                                        CSharpSyntaxFactory
                                            .Token(
                                                CSharpSyntaxKind
                                                    .SemicolonToken)),
                                CSharpSyntaxFactory
                                    .AccessorDeclaration(
                                        CSharpSyntaxKind
                                            .SetAccessorDeclaration)
                                    .WithSemicolonToken(
                                        CSharpSyntaxFactory
                                            .Token(
                                                CSharpSyntaxKind
                                                    .SemicolonToken)),
                            ]))));
        }

        return builder.ToImmutable();
    }

    private static CSharp.TypeSyntax GetParameterType(
        ParamDeclarationSyntax parameter)
    {
        if (parameter.Type == null)
        {
            return CSharpSyntaxFactory.PredefinedType(
                CSharpSyntaxFactory.Token(
                    CSharpSyntaxKind.ObjectKeyword));
        }

        try
        {
            return parameter.Type.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return CSharpSyntaxFactory.PredefinedType(
                CSharpSyntaxFactory.Token(
                    CSharpSyntaxKind.ObjectKeyword));
        }
    }

    private static ImmutableArray<
        CSharp.UsingDirectiveSyntax>
        GetGlobalUsingDirectives(
            ImmutableArray<AkburaSyntaxTree> syntaxTrees)
    {
        using var builder =
            ImmutableArrayBuilder<
                CSharp.UsingDirectiveSyntax>.Rent();
        var seen = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var syntaxTree in syntaxTrees)
        {
            var isGlobalUsingsFile =
                GlobalUsings.IsComponentFile(syntaxTree);
            foreach (var usingDirective in syntaxTree
                         .GetRoot()
                         .Members
                         .OfType<UsingDirectiveSyntax>())
            {
                if (!isGlobalUsingsFile &&
                    usingDirective.GlobalKeyword.RawKind == 0)
                {
                    continue;
                }

                AddUsingDirective(
                    builder,
                    seen,
                    usingDirective);
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<
        CSharp.UsingDirectiveSyntax>
        GetUsingDirectives(
            AkburaSyntaxTree syntaxTree,
            ImmutableArray<CSharp.UsingDirectiveSyntax>
                globalUsings)
    {
        using var builder =
            ImmutableArrayBuilder<
                CSharp.UsingDirectiveSyntax>.Rent();
        var seen = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var usingDirective in globalUsings)
        {
            AddUsingDirective(
                builder,
                seen,
                usingDirective);
        }

        foreach (var usingDirective in syntaxTree
                     .GetRoot()
                     .Members
                     .OfType<UsingDirectiveSyntax>())
        {
            if (usingDirective.GlobalKeyword.RawKind == 0)
            {
                AddUsingDirective(
                    builder,
                    seen,
                    usingDirective);
            }
        }

        return builder.ToImmutable();
    }

    private static void AddUsingDirective(
        ImmutableArrayBuilder<
            CSharp.UsingDirectiveSyntax> builder,
        HashSet<string> seen,
        UsingDirectiveSyntax usingDirective)
    {
        if (IsAkcssUsing(usingDirective))
        {
            return;
        }

        AddUsingDirective(
            builder,
            seen,
            usingDirective.ToCSharp()
                .WithGlobalKeyword(default));
    }

    private static void AddUsingDirective(
        ImmutableArrayBuilder<
            CSharp.UsingDirectiveSyntax> builder,
        HashSet<string> seen,
        CSharp.UsingDirectiveSyntax usingDirective)
    {
        var key = usingDirective
            .WithoutTrivia()
            .ToFullString();
        if (seen.Add(key))
        {
            builder.Add(usingDirective);
        }
    }

    private static bool IsAkcssUsing(
        UsingDirectiveSyntax usingDirective)
    {
        if (usingDirective.Alias != null ||
            usingDirective.StaticKeyword.RawKind != 0)
        {
            return false;
        }

        return usingDirective.Name
            .ToFullString()
            .Trim()
            .EndsWith(
                ".akcss",
                StringComparison.Ordinal);
    }

    private static string GetNamespaceName(
        AkburaSyntaxTree syntaxTree,
        string rootNamespace,
        string projectDirectory)
    {
        foreach (var member in syntaxTree
                     .GetRoot()
                     .Members)
        {
            if (member is NamespaceDeclarationSyntax
                namespaceDeclaration)
            {
                return namespaceDeclaration.Name
                    .ToFullString()
                    .Trim();
            }
        }

        using var builder =
            ImmutableArrayBuilder<string>.Rent();
        AddNamespaceSegments(
            builder,
            rootNamespace);
        AddNamespaceSegments(
            builder,
            GetRelativeDirectory(
                syntaxTree.FilePath,
                projectDirectory));
        return string.Join(
            ".",
            builder.ToImmutable());
    }

    private static string GetRelativeDirectory(
        string filePath,
        string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(filePath))
        {
            if (string.IsNullOrWhiteSpace(
                    projectDirectory))
            {
                return string.Empty;
            }

            var projectPath =
                Path.GetFullPath(projectDirectory)
                    .TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar);
            var fullFilePath =
                Path.GetFullPath(filePath);
            var prefix =
                projectPath +
                Path.DirectorySeparatorChar;
            if (!fullFilePath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            filePath =
                fullFilePath[prefix.Length..];
        }

        return Path.GetDirectoryName(filePath) ??
               string.Empty;
    }

    private static void AddNamespaceSegments(
        ImmutableArrayBuilder<string> builder,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value
            .Replace(
                Path.DirectorySeparatorChar,
                '.')
            .Replace(
                Path.AltDirectorySeparatorChar,
                '.');
        foreach (var segment in normalized.Split(
                     ['.'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length > 0)
            {
                builder.Add(trimmed);
            }
        }
    }

    private static Microsoft.CodeAnalysis.SyntaxToken
        GetAccessibilityToken(
        INamedTypeSymbol? declaredType)
    {
        return CSharpSyntaxFactory.Token(
            declaredType?.DeclaredAccessibility ==
                Accessibility.Internal
                ? CSharpSyntaxKind.InternalKeyword
                : CSharpSyntaxKind.PublicKeyword);
    }

    private static string ToCSharpIdentifier(
        string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "_";
        }

        var result = new char[value.Length];
        for (var index = 0;
             index < value.Length;
             index++)
        {
            var character = value[index];
            result[index] = index == 0
                ? CSharpSyntaxFacts
                    .IsIdentifierStartCharacter(
                        character)
                    ? character
                    : '_'
                : CSharpSyntaxFacts
                    .IsIdentifierPartCharacter(
                        character)
                    ? character
                    : '_';
        }

        var identifier = new string(result);
        return CSharpSyntaxFacts.GetKeywordKind(
                   identifier) !=
               CSharpSyntaxKind.None
            ? "@" + identifier
            : identifier;
    }
}
