using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Concurrent;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

internal sealed class AkburaModuleTypeResolver
{
    private readonly CSharpCompilation _compilation;
    private readonly ConcurrentDictionary<string, CSharpSymbolDefinition> _types =
        new(StringComparer.Ordinal);

    public AkburaModuleTypeResolver(CSharpCompilation compilation)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
    }

    public CSharpSymbolDefinition Resolve(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return default;
        }

        return _types.GetOrAdd(typeName, ResolveCore);
    }

    private CSharpSymbolDefinition ResolveCore(string typeName)
    {
        var metadataName = typeName.Trim();
        var isNullable = metadataName.EndsWith("?", StringComparison.Ordinal);
        if (isNullable)
        {
            metadataName = metadataName[..^1];
        }

        if (!isNullable && metadataName.IndexOfAny(['<', '[', '*', ',']) < 0)
        {
            metadataName = metadataName.Replace("global::", string.Empty);
            if (_compilation.GetTypeByMetadataName(metadataName) is { } metadataType)
            {
                return new CSharpSymbolDefinition(metadataType);
            }
        }

        var parsedType = CSharpSyntaxFactory.ParseTypeName(typeName);
        if (parsedType.ContainsDiagnostics)
        {
            return default;
        }

        var field = CSharpSyntaxFactory.FieldDeclaration(
                CSharpSyntaxFactory.VariableDeclaration(parsedType)
                    .AddVariables(CSharpSyntaxFactory.VariableDeclarator("__value")))
            .AddModifiers(CSharpSyntaxFactory.Token(SyntaxKind.PrivateKeyword));
        var probe = CSharpSyntaxFactory.ClassDeclaration("__AkburaModuleTypeProbe")
            .AddMembers(field);
        var syntaxTree = CSharpSyntaxTree.Create(
            CSharpSyntaxFactory.CompilationUnit().AddMembers(probe));
        var probeCompilation = _compilation.AddSyntaxTrees(syntaxTree);
        var root = syntaxTree.GetCompilationUnitRoot();
        var boundType = ((CSharp.FieldDeclarationSyntax)
            ((CSharp.ClassDeclarationSyntax)root.Members[0]).Members[0]).Declaration.Type;
        var type = probeCompilation.GetSemanticModel(syntaxTree).GetTypeInfo(boundType).Type;

        return type is { TypeKind: not TypeKind.Error }
            ? new CSharpSymbolDefinition(type)
            : default;
    }
}
