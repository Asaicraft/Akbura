using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    private AkburaSymbolInfo ResolveAkcssSourceDeclaration(AkcssTopLevelMemberSyntax syntax)
    {
        var targetSyntax = syntax switch
        {
            AkcssThemeDeclarationSyntax theme => theme.TargetType,
            AkcssMixinDeclarationSyntax mixin => mixin.TargetType,
            _ => null,
        };
        var diagnostics = CreateDuplicateAkcssSymbolDiagnostics(syntax);
        if (!TryResolveAkcssTargetType(targetSyntax, out var target))
        {
            diagnostics = diagnostics.Add(CreateAkcssSelectorTargetNotFoundDiagnostic(
                syntax, targetSyntax));
        }

        var module = (AkburaSyntax)syntax.Parent!;
        AkcssSourceDeclarationSymbol symbol = syntax switch
        {
            AkcssThemeDeclarationSyntax theme => new AkcssThemeSymbol(this, module, theme, target),
            AkcssMixinDeclarationSyntax mixin => new AkcssMixinSymbol(
                this, module, mixin, target, ResolveAkcssMixinParameterTypes(mixin)),
            AkcssSelectorRuleSyntax rule => new AkcssSelectorRuleSymbol(this, module, rule),
            _ => throw new InvalidOperationException("Expected an AKCSS source declaration."),
        };

        SetSemanticDiagnosticsIfAbsent(syntax, diagnostics);
        return AkburaSymbolInfo.Success(symbol);
    }

    private ImmutableArray<CSharpSymbolDefinition> ResolveAkcssMixinParameterTypes(AkcssMixinDeclarationSyntax mixin)
    {
        var parameters = mixin.Parameters.Parameters;
        var types = ImmutableArray.CreateBuilder<CSharpSymbolDefinition>(parameters.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var usings = GetAkcssCSharpUsingDirectives(mixin);
        foreach (var parameter in parameters)
        {
            var type = BindCSharpType(parameter.Type.ToCSharp(), usings).TypeSymbol;
            var diagnostics = ImmutableArray.CreateBuilder<AkburaSemanticDiagnostic>();
            if (type is null or { TypeKind: Microsoft.CodeAnalysis.TypeKind.Error })
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(parameter.Type,
                    ErrorCodes.AKBURA_SEMANTIC_AkcssMixin, [$"Cannot resolve parameter type '{parameter.Type.ToFullString().Trim()}'."]));
                type = null;
            }

            var name = parameter.Name.Identifier.ValueText;
            if (!names.Add(name))
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(parameter.Name,
                    ErrorCodes.AKBURA_SEMANTIC_AkcssMixin, [$"Mixin parameter '{name}' is already declared."]));
            }

            SetSemanticDiagnosticsIfAbsent(parameter, diagnostics.ToImmutable());
            types.Add(type is null ? default : new CSharpSymbolDefinition(type));
        }

        return types.MoveToImmutable();
    }

    private AkburaSymbolInfo ResolveAkcssMixinParameter(AkcssMixinParameterSyntax parameter)
    {
        var mixin = parameter.Parent?.Parent as AkcssMixinDeclarationSyntax;
        if (mixin is not null && GetDeclarationSymbolInfo(mixin).Symbol is AkcssMixinSymbol symbol)
        {
            foreach (var candidate in symbol.Parameters)
            {
                if (ReferenceEquals(candidate.DeclarationSyntax, parameter))
                {
                    return AkburaSymbolInfo.Success(candidate);
                }
            }
        }

        return AkburaSymbolInfo.None(Symbols.CandidateReason.NotFound);
    }

    internal ImmutableArray<TSymbol> LookupAkcssSourceDeclarations<TSymbol>(AkburaSyntax site, string name, string? moduleName = null)
        where TSymbol : AkcssSourceDeclarationSymbol
    {
        var matches = ImmutableArray.CreateBuilder<TSymbol>();
        var visited = new HashSet<AkcssSyntaxTree>();
        if (moduleName is null)
        {
            AddDeclarations(this, GetContainingAkcssTopLevelMembers(site));
            if (matches.Count != 0)
            {
                return matches.ToImmutable();
            }

            foreach (var import in GetAkcssImportNames(site))
            {
                AddModule(import);
            }
        }
        else
        {
            AddModule(moduleName + ".akcss");
        }

        return matches.ToImmutable();

        void AddModule(string logicalName)
        {
            foreach (var tree in Compilation.GetLocalAkcssSyntaxTreesByLogicalName(logicalName))
            {
                if (visited.Add(tree))
                {
                    AddDeclarations(Compilation.GetSemanticModel(tree), tree.GetRoot().Members);
                }
            }
        }

        void AddDeclarations(AkburaSemanticModel model, SyntaxList<AkcssTopLevelMemberSyntax> members)
        {
            foreach (var member in members)
            {
                if ((member is AkcssThemeDeclarationSyntax theme && typeof(TSymbol) == typeof(AkcssThemeSymbol) &&
                    theme.Name.Identifier.ValueText == name ||
                    member is AkcssMixinDeclarationSyntax mixin && typeof(TSymbol) == typeof(AkcssMixinSymbol) &&
                    mixin.Name.Identifier.ValueText == name) &&
                    model.GetDeclarationSymbolInfo(member).Symbol is TSymbol match)
                {
                    matches.Add(match);
                }
            }
        }
    }

    internal ImmutableArray<CSharp.UsingDirectiveSyntax> GetAkcssDefinitionUsings(AkburaSyntax declaration) =>
        GetAkcssCSharpUsingDirectives(declaration);
}
