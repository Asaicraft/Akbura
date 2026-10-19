using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Workspaces.Completion;

/// <summary>
/// Validates snapshot-translated C# completion edits, including a native brace
/// pair that changes the recovery parser's expression boundary.
/// </summary>
internal static class AkburaCSharpCompletionCommitFacts
{
    public static bool IsMatchingContext(
        SourceText sourceText,
        SourceText currentText,
        AkburaCSharpCompletionContext source,
        AkburaCSharpCompletionContext current,
        TextSpan translatedOwnerSpan,
        TextSpan translatedHostSpan,
        out AkburaCSharpCompletionRejectionReason reason)
    {
        if (source.Kind != current.Kind)
        {
            reason = AkburaCSharpCompletionRejectionReason.ContextKindChanged;
            return false;
        }

        if (source.LogicalSlot != current.LogicalSlot)
        {
            reason = AkburaCSharpCompletionRejectionReason.LogicalSlotChanged;
            return false;
        }

        var isDeclarationTypeSlot = source.LogicalSlot == AkburaCSharpCompletionLogicalSlot.DeclarationType;
        if (!isDeclarationTypeSlot && source.OwnerKind != current.OwnerKind)
        {
            reason = AkburaCSharpCompletionRejectionReason.OwnerKindChanged;
            return false;
        }

        if (IsMatchingPairedExpression(sourceText, currentText, source, current, translatedOwnerSpan, translatedHostSpan))
        {
            reason = AkburaCSharpCompletionRejectionReason.None;
            return true;
        }

        var currentOwnerSpan = isDeclarationTypeSlot ? current.LogicalOwnerSpan : current.OwnerSpan;
        if (translatedOwnerSpan != currentOwnerSpan)
        {
            reason = AkburaCSharpCompletionRejectionReason.OwnerChanged;
            return false;
        }

        if (translatedHostSpan != current.HostSpan)
        {
            reason = AkburaCSharpCompletionRejectionReason.HostSpanChanged;
            return false;
        }

        if (!TextOutsideSpanMatches(sourceText, currentText, source.HostSpan, current.HostSpan))
        {
            reason = AkburaCSharpCompletionRejectionReason.TextOutsideHostSpanChanged;
            return false;
        }

        reason = AkburaCSharpCompletionRejectionReason.None;
        return true;
    }

    /// <summary>
    /// Bounds a fragment-local edit after <see cref="IsMatchingContext"/> has
    /// validated its session. Imports and other mapped fragments are not edited here.
    /// </summary>
    public static bool TryGetReplacementSpan(SourceText text, AkburaCSharpCompletionContext context, TextSpan translatedSpan, out TextSpan replacementSpan)
    {
        replacementSpan = default;
        if ((uint)translatedSpan.End > (uint)text.Length || (uint)context.HostSpan.End > (uint)text.Length ||
            translatedSpan.Start < context.HostSpan.Start || translatedSpan.Start > context.HostSpan.End)
        {
            return false;
        }

        if (context.OwnerKind == SyntaxKind.MarkupExpressionBindingSyntax)
        {
            // The C# projection includes separator trivia owned by `bind`.
            // A recovery edit spanning the whole fragment must not remove it
            // and turn `x.bind Count` into the different token `x.bindCount`.
            // Roslyn trivia parsing also preserves comments and multiline input.
            var trivia = CSharpSyntaxFactory.ParseLeadingTrivia(text.ToString(context.HostSpan));
            var protectedEnd = Math.Min(context.HostSpan.Start + trivia.FullSpan.Length, context.HostPosition);
            if (protectedEnd < context.HostSpan.Start || protectedEnd > context.HostSpan.End ||
                translatedSpan.End < protectedEnd)
            {
                return false;
            }

            translatedSpan = TextSpan.FromBounds(Math.Max(translatedSpan.Start, protectedEnd), translatedSpan.End);
        }

        if (context.HostSpan.Contains(translatedSpan))
        {
            replacementSpan = translatedSpan;
            return true;
        }

        if (!IsBraceDelimitedExpression(text, context))
        {
            return false;
        }

        // The old C# projection can include skipped markup after a missing }.
        // Its inclusive edit must stop at the current expression boundary,
        // preserving both the paired delimiter and that original markup.
        replacementSpan = TextSpan.FromBounds(translatedSpan.Start, context.HostSpan.End);
        return true;
    }

    private static bool IsMatchingPairedExpression(
        SourceText sourceText,
        SourceText currentText,
        AkburaCSharpCompletionContext source,
        AkburaCSharpCompletionContext current,
        TextSpan translatedOwnerSpan,
        TextSpan translatedHostSpan)
    {
        if (!IsBraceDelimitedExpression(currentText, current) ||
            source.Kind != AkburaCSharpCompletionContextKind.Expression ||
            !IsExpressionOwner(source.OwnerKind) ||
            source.HostSpan.Start < 1 || source.HostSpan.End > sourceText.Length ||
            !HasExpressionOpener(sourceText, source) ||
            source.HostPosition < source.HostSpan.Start || source.HostPosition > source.HostSpan.End ||
            (source.HostSpan.End < sourceText.Length && sourceText[source.HostSpan.End] == '}') ||
            translatedHostSpan.Start != current.HostSpan.Start ||
            translatedHostSpan.End <= current.HostSpan.End || translatedHostSpan.End > currentText.Length ||
            translatedOwnerSpan.Start != current.OwnerSpan.Start || translatedOwnerSpan.End > currentText.Length ||
            !translatedOwnerSpan.Contains(current.OwnerSpan) || !current.OwnerSpan.Contains(current.HostSpan))
        {
            return false;
        }

        // Before pairing, the recovery parser may consume /> into the C#
        // fragment. The pair was inserted at the original caret, not at the
        // recovered span's end. Everything after that caret must survive.
        return TextOutsideSpanMatches(
            sourceText,
            currentText,
            TextSpan.FromBounds(source.HostSpan.Start, source.HostPosition),
            TextSpan.FromBounds(current.HostSpan.Start, current.HostSpan.End + 1));
    }

    private static bool IsBraceDelimitedExpression(SourceText text, AkburaCSharpCompletionContext context)
    {
        return context.Kind == AkburaCSharpCompletionContextKind.Expression &&
            IsExpressionOwner(context.OwnerKind) &&
            context.HostSpan.Start > 0 && context.HostSpan.End < text.Length &&
            HasExpressionOpener(text, context) && text[context.HostSpan.End] == '}';
    }

    private static bool IsExpressionOwner(SyntaxKind kind) =>
        kind is SyntaxKind.CSharpExpressionSyntax or SyntaxKind.MarkupExpressionBindingSyntax;

    private static bool HasExpressionOpener(SourceText text, AkburaCSharpCompletionContext context)
    {
        // The parser records the Akbura opener separately from the C# fragment:
        // ${x.bind ...} can contain arbitrary trivia between these boundaries.
        var opener = context.ExpressionOpenBracePosition ?? context.HostSpan.Start - 1;
        return opener >= 0 && opener < context.HostSpan.Start && opener < text.Length &&
            text[opener] == '{' &&
            (!context.ExpressionOpenBracePosition.HasValue || opener > 0 && text[opener - 1] == '$');
    }

    private static bool TextOutsideSpanMatches(SourceText sourceText, SourceText currentText, TextSpan sourceSpan, TextSpan currentSpan)
    {
        if (sourceSpan.Start != currentSpan.Start ||
            sourceSpan.End > sourceText.Length || currentSpan.End > currentText.Length ||
            sourceText.Length - sourceSpan.End != currentText.Length - currentSpan.End)
        {
            return false;
        }

        for (var index = 0; index < sourceSpan.Start; index++)
        {
            if (sourceText[index] != currentText[index])
            {
                return false;
            }
        }

        for (int sourceIndex = sourceSpan.End, currentIndex = currentSpan.End; sourceIndex < sourceText.Length; sourceIndex++, currentIndex++)
        {
            if (sourceText[sourceIndex] != currentText[currentIndex])
            {
                return false;
            }
        }

        return true;
    }
}
