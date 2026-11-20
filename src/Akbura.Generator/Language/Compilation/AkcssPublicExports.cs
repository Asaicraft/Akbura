using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Language;

internal sealed record AkcssThemeExport(
    AkcssThemeDeclarationSyntax Declaration, string MemberName, INamedTypeSymbol? ResultType)
{
    public AkcssThemeSymbol? Resolve(AkburaSemanticModel model) =>
        model.GetDeclaredSymbol(Declaration) as AkcssThemeSymbol;
}

internal sealed record AkcssModuleExport(
    string MetadataName, string Namespace, string TypeName,
    ImmutableArray<AkcssThemeExport> Themes, ImmutableArray<AkburaSemanticDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.IsEmpty;
}

internal sealed class AkcssPublicExports(ImmutableDictionary<AkcssSyntaxTree, AkcssModuleExport> modules)
{
    public AkcssModuleExport? Find(AkcssSyntaxTree tree) =>
        modules.TryGetValue(tree, out var module) ? module : null;

    public static AkcssPublicExports Create(CSharpCompilation compilation, ImmutableArray<AkcssSyntaxTree> trees)
    {
        var result = ImmutableDictionary.CreateBuilder<AkcssSyntaxTree, AkcssModuleExport>();
        var controlTheme = compilation.GetTypeByMetadataName("Avalonia.Styling.ControlTheme");
        foreach (var tree in trees)
        {
            var root = tree.GetRoot();
            var first = root.Members.FirstOrDefault(static member =>
                member is AkcssThemeDeclarationSyntax or AkcssSelectorRuleSyntax);
            var logicalName = tree.LogicalName;
            if (first is null || logicalName.Length == 0 || GlobalUsings.IsAkcssFile(tree))
            {
                continue;
            }

            var metadataName = logicalName.EndsWith(".akcss", StringComparison.OrdinalIgnoreCase)
                ? logicalName[..^".akcss".Length]
                : logicalName;
            var segments = metadataName.Split('.');
            var diagnostics = ImmutableArray.CreateBuilder<AkburaSemanticDiagnostic>();
            if (segments.Any(static name => !IsIdentifier(name)))
            {
                diagnostics.Add(Error(first, $"The module '{logicalName}' does not form a valid public C# facade name."));
            }
            else
            {
                for (var count = 1; count <= segments.Length; count++)
                {
                    var prefix = string.Join(".", segments, 0, count);
                    if (!compilation.GetTypesByMetadataName(prefix).IsEmpty)
                    {
                        diagnostics.Add(Error(first, $"AKCSS facade '{metadataName}' conflicts with existing C# type '{prefix}'."));
                        break;
                    }
                }

                var existingNamespace = compilation.Assembly.GlobalNamespace;
                foreach (var segment in segments)
                {
                    existingNamespace = existingNamespace?.GetNamespaceMembers()
                        .FirstOrDefault(candidate => string.Equals(candidate.Name, segment, StringComparison.Ordinal));
                }

                if (existingNamespace != null)
                {
                    diagnostics.Add(Error(first, $"AKCSS facade '{metadataName}' conflicts with an existing C# namespace."));
                }
            }

            var reserved = new HashSet<string>(StringComparer.Ordinal) { "CreateStyles", segments[^1] };
            var names = new Dictionary<string, AkcssThemeDeclarationSyntax>(StringComparer.Ordinal);
            var themes = ImmutableArray.CreateBuilder<AkcssThemeExport>();
            foreach (var declaration in root.Members.OfType<AkcssThemeDeclarationSyntax>())
            {
                var name = declaration.Name.Identifier.ValueText;
                if (!IsIdentifier(name) || reserved.Contains(name))
                {
                    diagnostics.Add(Error(declaration.Name, $"Theme '{name}' cannot be exported as a member of '{metadataName}'."));
                }
                else if (names.TryGetValue(name, out var previous))
                {
                    diagnostics.Add(Error(declaration.Name, $"Theme export '{metadataName}.{name}' is ambiguous.", [previous.Name]));
                }
                else
                {
                    names.Add(name, declaration);
                    themes.Add(new AkcssThemeExport(declaration, AkcssExpressionGenerator.EscapeIdentifier(name), controlTheme));
                }
            }

            var namespaceName = string.Join(".", segments.Take(segments.Length - 1)
                .Select(AkcssExpressionGenerator.EscapeIdentifier));
            result.Add(tree, new AkcssModuleExport(metadataName, namespaceName,
                AkcssExpressionGenerator.EscapeIdentifier(segments[^1]), themes.ToImmutable(), diagnostics.ToImmutable()));
        }

        var byName = result.ToLookup(static item => item.Value.MetadataName, StringComparer.Ordinal);
        foreach (var group in byName)
        {
            foreach (var item in group)
            {
                var diagnostics = item.Value.Diagnostics;
                var root = item.Key.GetRoot();
                var related = group.Where(other => other.Key != item.Key)
                    .Select(static other => (AkburaSyntax)other.Key.GetRoot()).ToImmutableArray();
                if (!related.IsEmpty)
                {
                    diagnostics = diagnostics.Add(Error(root, $"Multiple AKCSS modules export the C# facade '{group.Key}'.", related));
                }

                for (var split = group.Key.LastIndexOf('.'); split > 0; split = group.Key.LastIndexOf('.', split - 1))
                {
                    var prefix = group.Key[..split];
                    var containingFacades = byName[prefix].Select(static other => (AkburaSyntax)other.Key.GetRoot()).ToImmutableArray();
                    if (!containingFacades.IsEmpty)
                    {
                        diagnostics = diagnostics.Add(Error(root,
                            $"AKCSS facade '{group.Key}' needs namespace '{prefix}', already exported as a type.", containingFacades));
                    }

                }

                result[item.Key] = item.Value with { Diagnostics = diagnostics };
            }
        }

        return new AkcssPublicExports(result.ToImmutable());
    }

    public CSharpCompilation AddProbeDeclarations(CSharpCompilation compilation)
    {
        var source = new StringBuilder();
        foreach (var module in modules.Values.OrderBy(static item => item.MetadataName, StringComparer.Ordinal))
        {
            if (!module.IsValid)
            {
                continue;
            }

            if (module.Namespace.Length != 0)
            {
                source.Append("namespace ").Append(module.Namespace).AppendLine(" {");
            }

            source.Append("public static class ").Append(module.TypeName).AppendLine(" {");
            foreach (var theme in module.Themes)
            {
                source.Append("public static global::Avalonia.Styling.ControlTheme ").Append(theme.MemberName)
                    .AppendLine(" => throw new global::System.NotSupportedException();");
            }

            source.AppendLine("public static global::Avalonia.Styling.Styles CreateStyles() => throw new global::System.NotSupportedException();");
            source.AppendLine("}");
            if (module.Namespace.Length != 0)
            {
                source.AppendLine("}");
            }
        }

        if (source.Length == 0)
        {
            return compilation;
        }

        var options = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var tree = CSharpSyntaxTree.ParseText(source.ToString(), options, path: "__AkburaAkcssExports.g.cs");
        return compilation.AddSyntaxTrees(tree);
    }

    private static bool IsIdentifier(string name) =>
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name) ||
        Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(name) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None;

    private static AkburaSemanticDiagnostic Error(AkburaSyntax syntax, string message,
        ImmutableArray<AkburaSyntax> related = default) =>
        new(syntax, ErrorCodes.AKBURA_GENERATION_AkcssExport, [message], relatedSyntax: related);
}
