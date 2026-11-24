using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using SpecialType = Microsoft.CodeAnalysis.SpecialType;
using System.Collections.Immutable;
using CandidateReason = Akbura.Language.Symbols.CandidateReason;

namespace Akbura.Language.Binder;

internal sealed partial class AkcssStyleBinder
{
    private BoundNode BindAkcssSourceDeclaration(AkburaSyntax syntax)
    {
        var symbolInfo = SemanticModel.GetDeclarationSymbolInfo(syntax);
        if (symbolInfo.Symbol is not AkcssSourceDeclarationSymbol owner)
        {
            return new BoundDeclaration(syntax, this, symbolInfo);
        }

        var context = CreateAkcssContext(owner);
        BoundAkcssContextual result;
        switch (owner)
        {
            case AkcssSelectorRuleSymbol rule:
                result = BindAkcssSelectorRule(rule, context, symbolInfo);
                break;
            case AkcssThemeSymbol theme:
                result = BindAkcssTheme(theme, context, symbolInfo);
                break;
            case AkcssMixinSymbol mixin:
                result = BindAkcssMixin(mixin, context, symbolInfo);
                break;
            default:
                throw new System.InvalidOperationException("Unexpected AKCSS declaration owner.");
        }

        PublishAkcssContextualTree(result);
        return result;
    }

    private BoundAkcssContextual BindAkcssSelectorRule(
        AkcssSelectorRuleSymbol rule, AkcssBindingContext context, AkburaSymbolInfo symbolInfo)
    {
        var syntax = rule.DeclarationSyntax;
        var diagnostics = ArrayBuilder<AkburaSemanticDiagnostic>.GetInstance();
        try
        {
            var selector = BindAkcssSelector(syntax.Selector, context, diagnostics);
            var selected = EnterAkcssSelector(context, syntax, selector);
            return new BoundAkcssContextual(BoundKind.AkcssSelectorRule, syntax, this, selected,
                new AkcssSelectorData(selector), symbolInfo, diagnostics.ToImmutable(), BindContextualMembers(syntax.Members, selected));
        }
        finally
        {
            diagnostics.Free();
        }
    }

    private ImmutableArray<BoundNode> BindLegacyAkcssBody(
        AkburaSyntax syntax, SyntaxList<AkcssBodyMemberSyntax> members, AkcssBindingContext context, IAkcssSymbol symbol)
    {
        if (!AkcssSemanticFacts.HasContextualBody(syntax))
        {
            return CastBoundNodes(BindAkcssOperations(members, symbol));
        }

        var bound = BindContextualMembers(members, context);
        foreach (var member in bound)
        {
            PublishAkcssContextualTree(member);
        }

        return bound;
    }

    private ImmutableArray<BoundNode> BindContextualMembers(
        SyntaxList<AkcssBodyMemberSyntax> members, AkcssBindingContext context)
    {
        var results = ImmutableArray.CreateBuilder<BoundNode>(members.Count);
        foreach (var member in members)
        {
            results.Add(BindContextualMember(member, context));
        }

        return results.MoveToImmutable();
    }

    private BoundNode BindContextualMember(AkcssBodyMemberSyntax member, AkcssBindingContext context)
    {
        switch (member)
        {
            case AkcssAssignmentSyntax assignment:
                var data = BindAkcssSetter(assignment, context);
                return new BoundAkcssContextual(BoundKind.AkcssContextualSetter, assignment, this, context, data,
                    data.Property is null ? AkburaSymbolInfo.None(CandidateReason.NotFound) : AkburaSymbolInfo.Success(data.Property),
                    data.Diagnostics, hasErrors: assignment.ContainsDiagnostics || data.Property is null && !data.IsDeferred);
            case AkcssIfDirectiveSyntax condition:
                return BindContextualAkcssCondition(condition, context);
            case AkcssNestedStyleRuleSyntax rule:
                return BindNestedAkcssRule(rule, context);
            case AkcssIncludeDirectiveSyntax include:
                return BindAkcssInclude(include, include.Directive, context);
            case AkcssTemplateBindDirectiveSyntax binding:
                return BindAkcssTemplateBinding(binding, binding.Directive, context);
            case AkcssApplyDirectiveSyntax apply:
                return BindContextualAkcssApply(apply, context);
            case AkcssInterceptDirectiveSyntax intercept when context.Owner is IAkcssSymbol legacy &&
                ReferenceEquals(intercept.Parent, legacy.DeclarationSyntax) && context.Inclusion is null:
                return BindAkcssInterceptCore(intercept, legacy);
            case AkcssTemplateDirectiveSyntax:
                return InvalidAkcssMember(BoundKind.AkcssTemplate, member, context,
                    ErrorCodes.AKBURA_SEMANTIC_AkcssTemplate, "A template must be declared directly in a theme.");
            case AkcssBasedOnDirectiveSyntax:
                return InvalidAkcssMember(BoundKind.AkcssBasedOn, member, context,
                    ErrorCodes.AKBURA_SEMANTIC_AkcssBaseTheme, "A base theme must be declared directly in a theme.");
            default:
                return InvalidAkcssMember(BoundKind.AkcssInvalidMember, member, context,
                    ErrorCodes.AKBURA_SEMANTIC_AkcssSelector, $"'{member.Kind}' is unsupported in this AKCSS context.");
        }
    }

    private BoundAkcssContextual BindNestedAkcssRule(AkcssNestedStyleRuleSyntax rule, AkcssBindingContext context)
    {
        var diagnostics = ArrayBuilder<AkburaSemanticDiagnostic>.GetInstance();
        try
        {
            var selector = BindAkcssSelector(rule.Selector, context, diagnostics);
            if (context.OwningElement is not null)
            {
                diagnostics.Add(CreateContextDiagnostic(rule, context, ErrorCodes.AKBURA_SEMANTIC_AkcssMixin,
                    "A markup include configures its owning element and cannot introduce a selector rule."));
                return new BoundAkcssContextual(BoundKind.AkcssNestedRule, rule, this, context, new AkcssSelectorData(selector),
                    AkburaSymbolInfo.None(CandidateReason.UnsupportedSyntax), diagnostics.ToImmutable());
            }

            var selected = EnterAkcssSelector(context, rule, selector);
            return new BoundAkcssContextual(BoundKind.AkcssNestedRule, rule, this, selected, new AkcssSelectorData(selector),
                AkburaSymbolInfo.None(CandidateReason.None), diagnostics.ToImmutable(), BindContextualMembers(rule.Members, selected),
                hasErrors: rule.Selector.ContainsDiagnostics);
        }
        finally
        {
            diagnostics.Free();
        }
    }

    private BoundAkcssContextual BindContextualAkcssCondition(AkcssIfDirectiveSyntax syntax, AkcssBindingContext context)
    {
        var expression = syntax.Condition.GetRawCSharpExpression();
        var boolean = SemanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean);
        var binding = expression is null || context.IsMixinDefinition && context.PropertyReceiver.IsDefault
            ? CSharpBindingResult.Empty
            : SemanticModel.BindAkcssExpression(expression, context, boolean);
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        SemanticModel.AddAkcssExpressionDiagnostics(syntax.Condition, syntax.Condition.ToFullString().Trim(), binding, diagnostics);
        if (binding.Conversion.TargetType is not null && !binding.Conversion.IsImplicit)
        {
            diagnostics.Add(CreateContextDiagnostic(syntax.Condition, context, ErrorCodes.AKBURA_SEMANTIC_AkcssSetter,
                "An AKCSS condition must be implicitly convertible to bool."));
        }

        return new BoundAkcssContextual(BoundKind.AkcssContextualIf, syntax, this, context, new AkcssConditionData(binding),
            AkburaSymbolInfo.None(CandidateReason.None), diagnostics.ToImmutable(),
            BindContextualMembers(syntax.Members, context with { IsConditional = true }), hasErrors: expression is null);
    }

    private BoundAkcssContextual BindContextualAkcssApply(AkcssApplyDirectiveSyntax syntax, AkcssBindingContext context)
    {
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        using var names = ImmutableArrayBuilder<string>.Rent();
        using var symbols = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
        foreach (var item in AkcssApplyItemFacts.GetItems(SemanticModel.SyntaxTree.Text, syntax))
        {
            names.Add(item.Text);
            if (SemanticModel.ResolveAkcssApplyItem(syntax, item.Text, context.Subject.Type, diagnostics) is { } symbol)
            {
                symbols.Add(symbol);
            }
        }

        return new BoundAkcssContextual(BoundKind.AkcssContextualApply, syntax, this, context,
            new AkcssApplyData(names.ToImmutable(), symbols.ToImmutable()),
            AkburaSymbolInfo.None(CandidateReason.None), diagnostics.ToImmutable());
    }

    private BoundAkcssContextual InvalidAkcssMember(
        BoundKind kind, AkburaSyntax syntax, AkcssBindingContext context, string code, string message) =>
        new(kind, syntax, this, context, null, AkburaSymbolInfo.None(CandidateReason.UnsupportedSyntax),
            [CreateContextDiagnostic(syntax, context, code, message)]);

    private void PublishAkcssContextualTree(BoundNode bound)
    {
        if (bound is BoundAkcssContextual contextual)
        {
            if (contextual.Context.Inclusion is not null)
            {
                return;
            }

            SemanticModel.SetCachedBoundNode(bound.Syntax, bound);
            SemanticModel.SetSemanticDiagnostics(bound.Syntax, bound.Diagnostics);
            var directive = bound.Syntax switch
            {
                AkcssIncludeDirectiveSyntax include => (AkburaSyntax)include.Directive,
                MarkupIncludeDirectiveSyntax include => include.Directive,
                AkcssTemplateBindDirectiveSyntax binding => binding.Directive,
                MarkupTemplateBindDirectiveSyntax binding => binding.Directive,
                _ => null,
            };
            if (directive is not null)
            {
                SemanticModel.SetCachedBoundNode(directive, bound);
            }
        }

        foreach (var child in bound.Children)
        {
            PublishAkcssContextualTree(child);
        }
    }
}
