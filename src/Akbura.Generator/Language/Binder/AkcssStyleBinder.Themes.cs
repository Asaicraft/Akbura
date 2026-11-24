using Akbura.Collections;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Linq;
using CandidateReason = Akbura.Language.Symbols.CandidateReason;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.Binder;

internal sealed partial class AkcssStyleBinder
{
    private BoundAkcssContextual BindAkcssTheme(
        AkcssThemeSymbol theme, AkcssBindingContext context, AkburaSymbolInfo symbolInfo)
    {
        var syntax = theme.DeclarationSyntax;
        GetThemeDirectives(syntax, out var baseDirective, out var templateDirective);
        var baseBinding = BindAkcssBaseReference(theme, baseDirective);
        var templateBinding = DescribeAkcssTemplate(theme, templateDirective);
        var (effectiveTemplate, cycle) = GetEffectiveAkcssTemplate(theme, templateDirective, templateBinding.Template,
            baseBinding.Diagnostics.IsEmpty ? baseBinding.Data : null);
        if (!cycle.IsEmpty)
        {
            baseBinding = (baseBinding.Data, [new AkburaSemanticDiagnostic(baseDirective!,
                ErrorCodes.AKBURA_SEMANTIC_AkcssBaseTheme,
                [$"Theme inheritance cycle: {string.Join(" -> ", cycle.Select(static item => item.Name))}."],
                relatedSyntax: cycle.SelectAsArray(static item => (AkburaSyntax)item.DeclarationSyntax))]);
        }

        context = context with { SubjectTemplate = effectiveTemplate };
        var children = ImmutableArray.CreateBuilder<BoundNode>(syntax.Members.Count);
        foreach (var member in syntax.Members)
        {
            switch (member)
            {
                case AkcssBasedOnDirectiveSyntax basedOn when ReferenceEquals(basedOn, baseDirective):
                    var sourceTheme = baseBinding.Data?.SourceTheme;
                    children.Add(new BoundAkcssContextual(BoundKind.AkcssBasedOn, basedOn, this, context,
                        baseBinding.Data, sourceTheme is null ? AkburaSymbolInfo.None(CandidateReason.None) : AkburaSymbolInfo.Success(sourceTheme),
                        baseBinding.Diagnostics, hasErrors: basedOn.ContainsDiagnostics));
                    break;
                case AkcssBasedOnDirectiveSyntax:
                    children.Add(InvalidAkcssMember(BoundKind.AkcssBasedOn, member, context,
                        ErrorCodes.AKBURA_SEMANTIC_AkcssBaseTheme, "A theme can declare only one base theme."));
                    break;
                case AkcssTemplateDirectiveSyntax template when ReferenceEquals(template, templateDirective):
                    children.Add(BindAkcssTemplateBody(template, context, templateBinding.Template, templateBinding.Diagnostics));
                    break;
                case AkcssTemplateDirectiveSyntax duplicateTemplate:
                    children.Add(BindAkcssTemplateBody(duplicateTemplate, context, null,
                        [CreateContextDiagnostic(member, context, ErrorCodes.AKBURA_SEMANTIC_AkcssTemplate,
                            "A theme can declare only one control template.", templateDirective)]));
                    break;
                default:
                    children.Add(BindContextualMember(member, context));
                    break;
            }
        }

        return new BoundAkcssContextual(BoundKind.AkcssTheme, syntax, this, context,
            new AkcssDeclarationData(theme, effectiveTemplate), symbolInfo,
            SemanticModel.GetCachedSemanticDiagnostics(syntax), children.MoveToImmutable());
    }

    private BoundAkcssContextual BindAkcssTemplateBody(AkcssTemplateDirectiveSyntax template,
        AkcssBindingContext context, AkcssTemplateContext? plan, ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        var children = ImmutableArray.CreateBuilder<BoundNode>();
        foreach (var content in template.Body.Content)
        {
            if (content is MarkupTextLiteralSyntax && string.IsNullOrWhiteSpace(content.ToFullString()))
            {
                continue;
            }

            var node = content is MarkupElementContentSyntax element ? (AkburaSyntax)element.Element : content;
            children.Add(SemanticModel.BindingSession.BindSemanticSyntax(node));
        }

        return new BoundAkcssContextual(BoundKind.AkcssTemplate, template, this, context, new AkcssTemplateData(plan),
            AkburaSymbolInfo.None(CandidateReason.None), diagnostics, children.ToImmutable(), hasErrors: template.ContainsDiagnostics);
    }

    private static (AkcssTemplateContext? Template, ImmutableArray<AkcssThemeSymbol> Cycle)
        GetEffectiveAkcssTemplate(AkcssThemeSymbol owner, AkcssTemplateDirectiveSyntax? directive,
            AkcssTemplateContext? local, AkcssBaseThemeData? baseTheme)
    {
        if (baseTheme?.SourceTheme is not { } source)
        {
            return (local, []);
        }

        var (inherited, cycle) = FindInheritedAkcssTemplate(owner, source);
        return (directive is null ? inherited : local, cycle);
    }

    private static (AkcssTemplateContext? Template, ImmutableArray<AkcssThemeSymbol> Cycle)
        FindInheritedAkcssTemplate(AkcssThemeSymbol derived, AkcssThemeSymbol firstBase)
    {
        var chain = ArrayBuilder<AkcssThemeSymbol>.GetInstance();
        var seen = PooledHashSet<AkcssThemeSymbol>.GetInstance();
        try
        {
            seen.Add(derived);
            chain.Add(derived);
            AkcssTemplateContext? inherited = null;
            var foundTemplate = false;
            for (AkcssThemeSymbol? current = firstBase; current is not null;)
            {
                chain.Add(current);
                if (!seen.Add(current))
                {
                    return (null, chain.ToImmutable());
                }

                GetThemeDirectives(current.DeclarationSyntax, out var baseDirective, out var template);
                if (!foundTemplate && template is not null)
                {
                    foundTemplate = true;
                    inherited = DescribeAkcssTemplate(current, template).Template;
                }

                var next = BindAkcssBaseReference(current, baseDirective);
                current = next.Diagnostics.IsEmpty ? next.Data?.SourceTheme : null;
            }

            return (inherited, []);
        }
        finally
        {
            seen.Free();
            chain.Free();
        }
    }

    private static void GetThemeDirectives(AkcssThemeDeclarationSyntax theme,
        out AkcssBasedOnDirectiveSyntax? baseDirective, out AkcssTemplateDirectiveSyntax? template)
    {
        baseDirective = null;
        template = null;
        foreach (var member in theme.Members)
        {
            if (member is AkcssBasedOnDirectiveSyntax basedOn)
            {
                baseDirective ??= basedOn;
            }
            else if (member is AkcssTemplateDirectiveSyntax local)
            {
                template ??= local;
            }

            if (baseDirective is not null && template is not null)
            {
                break;
            }
        }
    }

    private static (AkcssBaseThemeData? Data, ImmutableArray<AkburaSemanticDiagnostic> Diagnostics)
        BindAkcssBaseReference(AkcssThemeSymbol owner, AkcssBasedOnDirectiveSyntax? directive)
    {
        if (directive?.Expression.GetRawCSharpExpression() is not { } expression)
        {
            return (null, []);
        }

        var model = owner.DefinitionModel;
        var candidate = expression;
        while (candidate is CSharp.ParenthesizedExpressionSyntax parentheses)
        {
            candidate = parentheses.Expression;
        }

        if (TryGetAkcssThemeReference(candidate, out var name, out var module))
        {
            var matches = model.LookupAkcssSourceDeclarations<AkcssThemeSymbol>(directive, name, module);
            if (matches.Length > 1)
            {
                return (null, [Error($"The source theme '{candidate}' is ambiguous.",
                    matches.SelectAsArray(static match => (AkburaSyntax)match.DeclarationSyntax))]);
            }

            if (matches.Length == 1)
            {
                var source = matches[0];
                var data = new AkcssBaseThemeData(source, CSharpBindingResult.Empty);
                if (source.TargetType.Symbol is ITypeSymbol baseType && owner.TargetType.Symbol is ITypeSymbol type &&
                    AkburaSemanticModel.IsAssignableTo(type, baseType))
                {
                    return (data, []);
                }

                return (data, [Error($"The base theme '{source.Name}' must target a type assignable from '{owner.TargetType.Symbol?.Name}'.",
                    [source.DeclarationSyntax.TargetType])]);
            }
        }

        var themeType = model.Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Styling.ControlTheme");
        var expressionContext = new AkcssBindingContext(owner, model);
        var binding = model.BindAkcssExpression(expression, expressionContext, themeType);
        var external = new AkcssBaseThemeData(null, binding);
        if (IsAkcssArgumentConvertible(binding, themeType))
        {
            return (external, []);
        }

        var missingName = binding.Diagnostics.Any(static diagnostic => diagnostic.Id is "CS0103" or "CS0234" or "CS0246" or "CS0117");
        var message = missingName && TryGetAkcssThemeReference(candidate, out _, out _)
            ? $"The source theme '{candidate}' could not be found."
            : "The base-theme expression must be implicitly convertible to Avalonia.Styling.ControlTheme.";
        return (external, [Error(message)]);

        AkburaSemanticDiagnostic Error(string message, ImmutableArray<AkburaSyntax> related = default) =>
            new(directive.Expression, ErrorCodes.AKBURA_SEMANTIC_AkcssBaseTheme, [message], relatedSyntax: related);
    }

    private static bool TryGetAkcssThemeReference(
        CSharp.ExpressionSyntax expression, out string name, out string? module)
    {
        name = string.Empty;
        module = null;
        if (expression is CSharp.IdentifierNameSyntax identifier)
        {
            name = identifier.Identifier.ValueText;
            return true;
        }

        if (expression is CSharp.MemberAccessExpressionSyntax { Name: CSharp.IdentifierNameSyntax member } access &&
            TryGetModuleName(access.Expression) is { } moduleName)
        {
            name = member.Identifier.ValueText;
            module = moduleName;
            return true;
        }

        return false;

        static string? TryGetModuleName(CSharp.ExpressionSyntax node) => node switch
        {
            CSharp.IdentifierNameSyntax simple => simple.Identifier.ValueText,
            CSharp.AliasQualifiedNameSyntax { Alias.Identifier.ValueText: "global", Name: CSharp.IdentifierNameSyntax root } =>
                root.Identifier.ValueText,
            CSharp.MemberAccessExpressionSyntax { Name: CSharp.IdentifierNameSyntax part } qualified
                when TryGetModuleName(qualified.Expression) is { } prefix => prefix + "." + part.Identifier.ValueText,
            _ => null,
        };
    }

    private static (AkcssTemplateContext? Template, ImmutableArray<AkburaSemanticDiagnostic> Diagnostics)
        DescribeAkcssTemplate(AkcssThemeSymbol theme, AkcssTemplateDirectiveSyntax? directive)
    {
        if (directive is null)
        {
            return (null, []);
        }

        var model = theme.DefinitionModel;
        var compilation = model.Compilation.CSharpCompilation;
        var diagnostics = ImmutableArray.CreateBuilder<AkburaSemanticDiagnostic>();
        if (compilation.GetTypeByMetadataName("Avalonia.Controls.Primitives.TemplatedControl") is not { } templatedControl ||
            theme.TargetType.Symbol is not ITypeSymbol receiver || !AkburaSemanticModel.IsAssignableTo(receiver, templatedControl))
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(directive, ErrorCodes.AKBURA_SEMANTIC_AkcssTemplate,
                ["A control template requires a theme target derived from TemplatedControl."]));
        }

        AkburaSyntax? rootContent = null;
        foreach (var content in directive.Body.Content)
        {
            if (content is MarkupTextLiteralSyntax && string.IsNullOrWhiteSpace(content.ToFullString()))
            {
                continue;
            }

            if (rootContent is not null)
            {
                rootContent = null;
                break;
            }

            rootContent = content;
        }

        if (rootContent is not MarkupElementContentSyntax { Element: { } root })
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(directive.Body, ErrorCodes.AKBURA_SEMANTIC_AkcssTemplate,
                ["A control template requires exactly one markup element as its root."]));
            return (null, diagnostics.ToImmutable());
        }

        if (compilation.GetTypeByMetadataName("Avalonia.Controls.Control") is not { } control ||
            model.GetSymbolInfo(root).Symbol is not IMarkupComponentSymbol { ComponentType: { } rootType } ||
            !AkburaSemanticModel.IsAssignableTo(rootType, control))
        {
            diagnostics.Add(new AkburaSemanticDiagnostic((AkburaSyntax?)root.StartTag?.Name ?? root,
                ErrorCodes.AKBURA_SEMANTIC_AkcssTemplate, ["A control template root must resolve to an Avalonia Control."]));
            return (null, diagnostics.ToImmutable());
        }

        var scope = model.BindingSession.GetMarkupNameScope(directive.Body);
        var context = new AkcssTemplateContext(theme, directive, root, scope);
        return (diagnostics.Count == 0 ? context : null, diagnostics.ToImmutable());
    }
}
