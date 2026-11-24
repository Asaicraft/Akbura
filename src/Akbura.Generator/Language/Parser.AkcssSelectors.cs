using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;

namespace Akbura.Language;

internal sealed partial class Parser
{
    private GreenAkcssTopLevelMemberSyntax ParseAkcssSelectorRule(bool incremental)
    {
        // The blender can expose merged names and raw C# types from the old rule.
        // Read lexical tokens so edits to selector trivia can change the production.
        ResetLookaheadForLexerModeChange();
        var selector = ParseAkcssSelector(relative: false);
        var openBrace = ReadAkcssSelectorOpenBrace(selector.FullWidth != 0, relative: false, incremental);
        ResetLookaheadForLexerModeChange();
        var (members, closeBrace) = ParseAkcssBracedMembers(openBrace, incremental);

        return !openBrace.ContainsSkippedText && GetAkcssStyleSelector(selector) is { } namedSelector
            ? GreenSyntaxFactory.AkcssStyleRuleSyntax(namedSelector, openBrace, members, closeBrace)
            : GreenSyntaxFactory.AkcssSelectorRuleSyntax(selector, openBrace, members, closeBrace);
    }

    private GreenAkcssNestedStyleRuleSyntax ParseAkcssNestedStyleRule(bool incremental)
    {
        ResetLookaheadForLexerModeChange();
        var selector = ParseAkcssSelector(relative: true);
        var openBrace = ReadAkcssSelectorOpenBrace(hasHeader: true, relative: true, incremental);
        ResetLookaheadForLexerModeChange();
        var (members, closeBrace) = ParseAkcssBracedMembers(openBrace, incremental);
        return GreenSyntaxFactory.AkcssNestedStyleRuleSyntax(selector, openBrace, members, closeBrace);
    }


    private GreenAkcssUtilityDeclarationSyntax ParseAkcssUtilityBody(GreenAkcssUtilitySelectorSyntax selector, bool incremental)
    {
        GreenSyntaxToken openBrace;
        if (selector.FullWidth == 0)
        {
            // An invalid utility header can start with a general selector
            // token. Keep it as skipped syntax instead of parsing it as a property.
            openBrace = ReadAkcssSelectorOpenBrace(hasHeader: false, relative: false, incremental);
            ResetLookaheadForLexerModeChange();
        }
        else
        {
            openBrace = incremental
                ? ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken)
                : EatAkcssOpenBraceToken();
        }

        var (members, closeBrace) = ParseAkcssBracedMembers(openBrace, incremental);
        return GreenSyntaxFactory.AkcssUtilityDeclarationSyntax(selector, openBrace, members, closeBrace);
    }

    private static GreenAkcssStyleSelectorSyntax? GetAkcssStyleSelector(GreenAkcssSelectorSyntax selector)
    {
        if (selector.Steps.Count != 0 || selector.Initial is not GreenAkcssCompoundSelectorSyntax initial)
        {
            return null;
        }

        var components = initial.Components;
        if (components.Count == 1 && components[0] is GreenAkcssClassSelectorSyntax classOnly)
        {
            return GreenSyntaxFactory.AkcssStyleSelectorSyntax(null, null, null, classOnly.DotToken, classOnly.Name);
        }

        if (components.Count is 0 or > 2 || components[0] is not GreenAkcssTypeSelectorSyntax type)
        {
            return null;
        }

        var classSelector = components.Count == 2 ? components[1] as GreenAkcssClassSelectorSyntax : null;
        if (components.Count == 2 && classSelector is null)
        {
            return null;
        }

        var parenthesized = type.Type as GreenAkcssParenthesizedSelectorTypeSyntax;
        return GreenSyntaxFactory.AkcssStyleSelectorSyntax(
            parenthesized?.OpenParen, parenthesized?.Type ?? (GreenCSharpTypeSyntax)type.Type,
            parenthesized?.CloseParen, classSelector?.DotToken, classSelector?.Name);
    }

    private GreenAkcssSelectorSyntax ParseAkcssSelector(bool relative)
    {
        GreenAkcssSelectorCompoundSyntax initial;
        GreenSyntaxToken? last;
        if (relative)
        {
            last = CurrentToken;
            var anchor = GreenSyntaxFactory.AkcssParentSelectorSyntax(EatTokenAsKind(SyntaxKind.CaretToken));
            initial = GreenSyntaxFactory.AkcssParentCompoundSelectorSyntax(anchor, ParseAkcssSelectorComponents(ref last));
        }
        else
        {
            initial = ParseAkcssSelectorCompound(out last);
        }

        var steps = _pool.Allocate<GreenAkcssSelectorStepSyntax>();
        try
        {
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (CurrentToken.Kind == SyntaxKind.GreaterThanToken)
                {
                    var greaterThan = EatToken();
                    steps.Add(GreenSyntaxFactory.AkcssChildSelectorSyntax(greaterThan, ParseAkcssSelectorCompound(out last)));
                }
                else if (CurrentToken.Kind == SyntaxKind.SlashToken || IsAkcssMissingTemplateOpenSlash())
                {
                    var openSlash = EatToken(SyntaxKind.SlashToken);
                    var keyword = EatContextualToken(SyntaxKind.TemplateKeyword);
                    var closeSlash = EatToken(SyntaxKind.SlashToken);
                    steps.Add(GreenSyntaxFactory.AkcssTemplateTraversalSyntax(
                        openSlash, keyword, closeSlash, ParseAkcssSelectorCompound(out last)));
                }
                else if (last is not null && HasAkcssSelectorWhitespace(last, CurrentToken) &&
                    IsAkcssCompoundStart(CurrentToken))
                {
                    steps.Add(GreenSyntaxFactory.AkcssDescendantSelectorSyntax(ParseAkcssSelectorCompound(out last)));
                }
                else
                {
                    break;
                }
            }

            return GreenSyntaxFactory.AkcssSelectorSyntax(initial, steps.ToList());
        }
        finally
        {
            _pool.Free(steps);
        }
    }

    private bool IsAkcssMissingTemplateOpenSlash() =>
        CurrentToken.ContextualKind == SyntaxKind.TemplateKeyword &&
        PeekToken(1).Kind == SyntaxKind.SlashToken &&
        (PeekToken(2).ContextualKind != SyntaxKind.TemplateKeyword || PeekToken(3).Kind != SyntaxKind.SlashToken);

    private GreenAkcssCompoundSelectorSyntax ParseAkcssSelectorCompound(out GreenSyntaxToken? last)
    {
        last = null;
        GreenAkcssTypeSelectorSyntax? type = null;
        if (CurrentToken.Kind == SyntaxKind.OpenParenToken)
        {
            var openParen = EatToken();
            var csharpType = ReadAkcssCSharpType(incremental: false, allowTuple: true);
            var closeParen = EatToken(SyntaxKind.CloseParenToken);
            type = GreenSyntaxFactory.AkcssTypeSelectorSyntax(
                GreenSyntaxFactory.AkcssParenthesizedSelectorTypeSyntax(openParen, csharpType, closeParen));
            last = closeParen;
        }
        else if (IsAkcssNameToken(CurrentToken))
        {
            last = EatToken();
            type = GreenSyntaxFactory.AkcssTypeSelectorSyntax(CreateAkcssCSharpTypeSyntax(last.ToFullString()));
        }

        var components = ParseAkcssSelectorComponents(ref last, type);
        var compound = GreenSyntaxFactory.AkcssCompoundSelectorSyntax(components);
        return components.Count == 0
            ? AddError(compound, 0, 0, ErrorCodes.ERR_SyntaxError, "selector compound")
            : compound;
    }

    private GreenSyntaxList<GreenAkcssSelectorComponentSyntax> ParseAkcssSelectorComponents(
        ref GreenSyntaxToken? last, GreenAkcssTypeSelectorSyntax? type = null)
    {
        var components = _pool.Allocate<GreenAkcssSelectorComponentSyntax>();
        try
        {
            if (type is not null)
            {
                components.Add(type);
            }

            while (IsAkcssSelectorSuffix(CurrentToken.Kind) &&
                (last is null || !HasAkcssSelectorWhitespace(last, CurrentToken)))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var sigil = EatToken();
                if (IsAkcssNameToken(CurrentToken) && HasAkcssSelectorWhitespace(sigil, CurrentToken))
                {
                    sigil = AddError(sigil, ErrorCodes.ERR_SyntaxError, "adjacent selector name");
                }

                var name = ParseAkcssUtilityName();
                GreenAkcssSelectorComponentSyntax component = sigil.Kind switch
                {
                    SyntaxKind.DotToken => GreenSyntaxFactory.AkcssClassSelectorSyntax(sigil, name),
                    SyntaxKind.HashToken => GreenSyntaxFactory.AkcssNameSelectorSyntax(sigil, name),
                    _ => GreenSyntaxFactory.AkcssPseudoClassSelectorSyntax(sigil, name),
                };
                components.Add(component);
                last = name.Identifier.IsMissing ? sigil : name.Identifier;
            }

            return components.ToList();
        }
        finally
        {
            _pool.Free(components);
        }
    }

    private GreenSyntaxToken ReadAkcssSelectorOpenBrace(bool hasHeader, bool relative, bool incremental)
    {
        if (CurrentToken.Kind == SyntaxKind.OpenBraceToken)
        {
            // Claim the body delimiter through the blender so it can enter
            // the old rule and reuse its unchanged members.
            ResetLookaheadForLexerModeChange();
            return ReadAkcssToken(SyntaxKind.OpenBraceToken, incremental);
        }

        var skipped = _pool.Allocate<GreenSyntaxToken>();
        try
        {
            while (CurrentToken.Kind is not (SyntaxKind.EndOfFileToken or SyntaxKind.OpenBraceToken))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if ((hasHeader || skipped.Count > 0) &&
                    (CurrentToken.Kind is SyntaxKind.CloseBraceToken or SyntaxKind.AtToken ||
                        IsAfterAkcssLineBreak(CurrentToken) &&
                        HasAkcssRuleHeader(allowRelative: relative, allowUtilityParameters: relative)))
                {
                    break;
                }

                var token = EatToken();
                skipped.Add(token);
                if (token.Kind is SyntaxKind.CloseBraceToken or SyntaxKind.SemicolonToken)
                {
                    var missing = CreateMissingToken(SyntaxKind.OpenBraceToken, token.Kind);
                    return AddLeadingSkippedSyntax(missing, skipped.ToList().Node!);
                }
            }

            var brace = EatToken(SyntaxKind.OpenBraceToken);
            return skipped.ToList().Node is { } tokens
                ? AddLeadingSkippedSyntax(AddError(brace, ErrorCodes.ERR_SyntaxError, "selector header"), tokens)
                : brace;
        }
        finally
        {
            _pool.Free(skipped);
        }
    }

    private static bool IsAkcssCompoundStart(GreenSyntaxToken token) =>
        token.Kind == SyntaxKind.OpenParenToken || IsAkcssSelectorSuffix(token.Kind) || IsAkcssNameToken(token);

    private static bool IsAkcssSelectorSuffix(SyntaxKind kind) =>
        kind is SyntaxKind.DotToken or SyntaxKind.HashToken or SyntaxKind.ColonToken;

    private static bool IsAkcssSelectorAnchor(SyntaxKind kind) =>
        kind is SyntaxKind.CaretToken or SyntaxKind.AmpersandToken;

    private static bool HasAkcssSelectorWhitespace(GreenSyntaxToken left, GreenSyntaxToken right) =>
        HasAkcssWhitespace(left.TrailingTrivia) || HasAkcssWhitespace(right.LeadingTrivia);

    private static bool HasAkcssWhitespace(GreenSyntaxList<GreenNode> trivia) =>
        trivia.Any((int)SyntaxKind.WhitespaceTrivia) || trivia.Any((int)SyntaxKind.EndOfLineTrivia);

    private bool IsAfterAkcssLineBreak(GreenSyntaxToken token) =>
        new GreenSyntaxList<GreenNode>(_prevTokenTrailingTrivia).Any((int)SyntaxKind.EndOfLineTrivia) ||
        token.LeadingTrivia.Any((int)SyntaxKind.EndOfLineTrivia);

    private bool IsAkcssBodyEnd(bool incremental)
    {
        var token = PeekAkcssToken(incremental);
        if (token.Kind is SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken ||
            token.Kind == SyntaxKind.AtToken && IsAkcssTopLevelDirectiveStart(incremental))
        {
            return true;
        }

        if (!IsAfterAkcssLineBreak(token) ||
            !IsAkcssCompoundStart(token) && token.Kind != SyntaxKind.CSharpRawToken ||
            PeekAkcssToken(incremental, 1).Kind == SyntaxKind.ColonToken)
        {
            return false;
        }

        try
        {
            return HasAkcssRuleHeader(allowRelative: false, allowUtilityParameters: true, preferAssignment: true);
        }
        finally
        {
            if (incremental)
            {
                ResetLookaheadForLexerModeChange();
            }
        }
    }

    // Recovery probes only recognize complete headers. They never consume input
    // or invoke the C# parser and have a fixed lookahead budget.
    private bool HasAkcssRuleHeader(bool allowRelative, bool allowUtilityParameters, bool preferAssignment = false)
    {
        var offset = 0;
        if (allowRelative && IsAkcssSelectorAnchor(PeekAkcssHeaderToken(0).Kind))
        {
            offset++;
            if (!ScanSuffixes(allowLeadingSuffix: false, allowPseudoClass: true))
            {
                return false;
            }
        }
        else if (!ScanCompound(preferAssignment))
        {
            return false;
        }

        while (true)
        {
            var token = PeekAkcssHeaderToken(offset);
            if (token.Kind == SyntaxKind.OpenBraceToken)
            {
                return true;
            }

            if (allowUtilityParameters && token.Kind == SyntaxKind.MinusToken &&
                PeekAkcssHeaderToken(offset + 1).Kind == SyntaxKind.OpenParenToken)
            {
                do
                {
                    offset++;
                    if (!ScanAkcssHeaderParentheses(ref offset))
                    {
                        return false;
                    }
                }
                while (PeekAkcssHeaderToken(offset).Kind == SyntaxKind.MinusToken &&
                    PeekAkcssHeaderToken(offset + 1).Kind == SyntaxKind.OpenParenToken);

                return PeekAkcssHeaderToken(offset).Kind == SyntaxKind.OpenBraceToken;
            }

            if (token.Kind == SyntaxKind.GreaterThanToken)
            {
                offset++;
            }
            else if (token.Kind == SyntaxKind.SlashToken &&
                PeekAkcssHeaderToken(offset + 1).ContextualKind == SyntaxKind.TemplateKeyword &&
                PeekAkcssHeaderToken(offset + 2).Kind == SyntaxKind.SlashToken)
            {
                offset += 3;
            }
            else if (!HasAkcssSelectorWhitespace(PeekAkcssHeaderToken(offset - 1), token))
            {
                return false;
            }

            if (!ScanCompound(preferAssignment: false))
            {
                return false;
            }
        }

        bool ScanCompound(bool preferAssignment)
        {
            var first = PeekAkcssHeaderToken(offset);
            if (IsAkcssNameToken(first))
            {
                offset++;
                return ScanSuffixes(allowLeadingSuffix: false, allowPseudoClass: !preferAssignment);
            }

            if (first.Kind == SyntaxKind.OpenParenToken)
            {
                return ScanAkcssHeaderParentheses(ref offset) &&
                    ScanSuffixes(allowLeadingSuffix: false, allowPseudoClass: true);
            }

            return IsAkcssSelectorSuffix(first.Kind) &&
                ScanSuffixes(allowLeadingSuffix: true, allowPseudoClass: true);
        }

        bool ScanSuffixes(bool allowLeadingSuffix, bool allowPseudoClass)
        {
            while (IsAkcssSelectorSuffix(PeekAkcssHeaderToken(offset).Kind))
            {
                var sigil = PeekAkcssHeaderToken(offset);
                if (!allowLeadingSuffix && HasAkcssSelectorWhitespace(PeekAkcssHeaderToken(offset - 1), sigil))
                {
                    break;
                }

                var name = PeekAkcssHeaderToken(offset + 1);
                if ((!allowPseudoClass && sigil.Kind == SyntaxKind.ColonToken) ||
                    !IsAkcssNameToken(name) || HasAkcssSelectorWhitespace(sigil, name))
                {
                    return false;
                }

                allowLeadingSuffix = false;
                offset += 2;
                while (PeekAkcssHeaderToken(offset) is { Kind: SyntaxKind.MinusToken } minus &&
                    AreAdjacent(PeekAkcssHeaderToken(offset - 1), minus) &&
                    IsAkcssUtilityNamePartToken(PeekAkcssHeaderToken(offset + 1)) &&
                    AreAdjacent(minus, PeekAkcssHeaderToken(offset + 1)))
                {
                    offset += 2;
                    while (IsAkcssUtilityNamePartToken(PeekAkcssHeaderToken(offset)) &&
                        AreAdjacent(PeekAkcssHeaderToken(offset - 1), PeekAkcssHeaderToken(offset)))
                    {
                        offset++;
                    }
                }
            }

            return true;
        }
    }

    private GreenSyntaxToken PeekAkcssHeaderToken(int offset) =>
        offset < 64 ? PeekToken(offset) : GreenSyntaxFactory.Token(SyntaxKind.EndOfFileToken);

    private bool ScanAkcssHeaderParentheses(ref int offset)
    {
        var depth = 0;
        do
        {
            switch (PeekAkcssHeaderToken(offset++).Kind)
            {
                case SyntaxKind.OpenParenToken:
                    depth++;
                    break;
                case SyntaxKind.CloseParenToken:
                    depth--;
                    break;
                case SyntaxKind.EndOfFileToken:
                case SyntaxKind.OpenBraceToken:
                case SyntaxKind.CloseBraceToken:
                case SyntaxKind.SemicolonToken:
                    return false;
            }
        }
        while (depth > 0);

        return true;
    }

    private bool HasAkcssNamedDeclarationHeader(SyntaxKind keyword)
    {
        for (var offset = 2; ; offset++)
        {
            if (PeekAkcssHeaderToken(offset).Kind == SyntaxKind.OpenParenToken)
            {
                if (ScanAkcssHeaderParentheses(ref offset))
                {
                    offset--;
                    continue;
                }

                return false;
            }

            var token = PeekAkcssHeaderToken(offset);
            if (offset > 2 && IsAkcssNameToken(token))
            {
                var afterName = offset + 1;
                if (keyword == SyntaxKind.MixinKeyword &&
                    PeekAkcssHeaderToken(afterName).Kind == SyntaxKind.OpenParenToken &&
                    ScanAkcssHeaderParentheses(ref afterName) &&
                    PeekAkcssHeaderToken(afterName).Kind == SyntaxKind.OpenBraceToken ||
                    keyword == SyntaxKind.ThemeKeyword &&
                    PeekAkcssHeaderToken(afterName).Kind == SyntaxKind.OpenBraceToken)
                {
                    return true;
                }
            }

            if (!IsAkcssNameToken(token) && token.Kind is not (SyntaxKind.DotToken or SyntaxKind.DoubleColonToken or
                SyntaxKind.LessThanToken or SyntaxKind.GreaterThanToken or SyntaxKind.CommaToken or
                SyntaxKind.OpenBracketToken or SyntaxKind.CloseBracketToken or SyntaxKind.QuestionToken))
            {
                return false;
            }
        }
    }
}
