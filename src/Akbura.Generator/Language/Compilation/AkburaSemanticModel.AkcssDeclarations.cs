using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;
using System.Collections.Immutable;
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
        var usings = GetAkcssCSharpUsingDirectives(mixin);
        var names = PooledHashSet<string>.GetInstance();
        try
        {
            foreach (var parameter in parameters)
            {
                var type = BindCSharpType(parameter.Type.ToCSharp(), usings).TypeSymbol;
                AkburaSemanticDiagnostic? typeError = null;
                if (type is null or { TypeKind: Microsoft.CodeAnalysis.TypeKind.Error })
                {
                    typeError = new AkburaSemanticDiagnostic(parameter.Type,
                        ErrorCodes.AKBURA_SEMANTIC_AkcssMixin, [$"Cannot resolve parameter type '{parameter.Type.ToFullString().Trim()}'."]);
                    type = null;
                }

                var name = parameter.Name.Identifier.ValueText;
                AkburaSemanticDiagnostic? nameError = null;
                if (!names.Add(name))
                {
                    nameError = new AkburaSemanticDiagnostic(parameter.Name,
                        ErrorCodes.AKBURA_SEMANTIC_AkcssMixin, [$"Mixin parameter '{name}' is already declared."]);
                }

                ImmutableArray<AkburaSemanticDiagnostic> diagnostics = (typeError, nameError) switch
                {
                    (null, null) => [],
                    ({ } error, null) => [error],
                    (null, { } error) => [error],
                    ({ } first, { } second) => [first, second],
                };
                SetSemanticDiagnosticsIfAbsent(parameter, diagnostics);
                types.Add(type is null ? default : new CSharpSymbolDefinition(type));
            }

            return types.MoveToImmutable();
        }
        finally
        {
            names.Free();
        }
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

    internal ImmutableArray<TSymbol> LookupAkcssSourceDeclarations<TSymbol>(AkburaSyntax site, string? name, string? moduleName = null)
        where TSymbol : AkcssSourceDeclarationSymbol
    {
        var matches = ArrayBuilder<TSymbol>.GetInstance();
        PooledHashSet<AkcssSyntaxTree>? visited = null;
        PooledHashSet<string>? localNames = null;
        try
        {
            if (moduleName is null)
            {
                AddDeclarations(this, GetContainingAkcssTopLevelMembers(site));
                if (name is not null && matches.Count != 0)
                {
                    return matches.ToImmutable();
                }

                if (matches.Count != 0)
                {
                    localNames = PooledHashSet<string>.GetInstance();
                    foreach (var match in matches)
                    {
                        localNames.Add(match.Name);
                    }
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
        }
        finally
        {
            localNames?.Free();
            visited?.Free();
            matches.Free();
        }

        void AddModule(string logicalName)
        {
            foreach (var tree in Compilation.GetLocalAkcssSyntaxTreesByLogicalName(logicalName))
            {
                if ((visited ??= PooledHashSet<AkcssSyntaxTree>.GetInstance()).Add(tree))
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
                    (name is null || theme.Name.Identifier.ValueText == name) ||
                    member is AkcssMixinDeclarationSyntax mixin && typeof(TSymbol) == typeof(AkcssMixinSymbol) &&
                    (name is null || mixin.Name.Identifier.ValueText == name)) &&
                    model.GetDeclarationSymbolInfo(member).Symbol is TSymbol match &&
                    localNames?.Contains(match.Name) != true)
                {
                    matches.Add(match);
                }
            }
        }
    }

    internal ImmutableArray<CSharp.UsingDirectiveSyntax> GetAkcssDefinitionUsings(AkburaSyntax declaration) =>
        GetAkcssCSharpUsingDirectives(declaration);
}
