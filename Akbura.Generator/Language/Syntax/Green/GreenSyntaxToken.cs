using Akbura.Collections;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.Collections.Immutable;

namespace Akbura.Language.Syntax.Green;
internal partial class GreenSyntaxToken : GreenNode
{
    public GreenSyntaxToken(SyntaxKind kind, ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations) : base((ushort)kind, diagnostics, annotations)
    {
        FullWidth = Text.Length;
        IsMissing = false;
    }

    public GreenSyntaxToken(SyntaxKind kind): this(kind, null, null)
    {
        FullWidth = Text.Length;
        IsMissing = false;
    }


    /// <summary>Indicates whether this is a token.</summary>
    internal override bool IsToken => true;

    /// <summary>Gets the text representation of the token.</summary>
    public virtual string Text => SyntaxFacts.GetText(Kind);

    /// <summary>Gets the width of the token.</summary>
    public override int Width => Text.Length;

    /// <summary>Gets the value text of the token.</summary>
    public virtual string? ValueText => Text;

    /// <summary>Gets the value associated with the token based on its kind.</summary>
    public virtual object? Value => SyntaxFacts.GetValue(Kind, Text);

    /// <summary>Retrieves a specific slot of this syntax token.</summary>
    public override GreenNode? GetSlot(int index)
    {
        throw new UnreachableException();
    }

    /// <summary>Creates a red node representation.</summary>
    public override AkburaSyntax CreateRed(AkburaSyntax? parent, int position)
    {
        throw new UnreachableException();
    }

    public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
    {
        return new GreenSyntaxToken(Kind, GetDiagnostics(), annotations);
    }

    public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
    {
        return new GreenSyntaxToken(Kind, diagnostics, GetAnnotations());
    }

    /// <summary>Creates a syntax token.</summary>
    public static GreenSyntaxToken Create(SyntaxKind kind)
    {
        if (kind > LastTokenWithWellKnownText)
        {
            if (!SyntaxFacts.IsAnyToken(kind))
            {
                ThrowHelper.ThisMethodCanOnlyBeUsedToCreateTokens(kind, nameof(kind));
            }

            return CreateMissing(kind);
        }

        return s_tokensWithNoTrivia[(int)kind].Value;
    }

    /// <summary>Creates a syntax token with trivia.</summary>
    public static GreenSyntaxToken Create(SyntaxKind kind, GreenNode? leading, GreenNode? trailing)
    {
        if (kind > LastTokenWithWellKnownText)
        {
            if (!SyntaxFacts.IsAnyToken(kind))
            {
                ThrowHelper.ThisMethodCanOnlyBeUsedToCreateTokens(kind, nameof(kind));
            }

            return CreateMissing(kind, leading, trailing);
        }

        if (leading == null)
        {
            if (trailing == null)
            {
                return s_tokensWithNoTrivia[(int)kind].Value;
            }
            else if (trailing == GreenSyntaxFactory.Space)
            {
                return s_tokensWithSingleTrailingSpace[(int)kind].Value;
            }
            else if (trailing == GreenSyntaxFactory.CarriageReturnLineFeed)
            {
                return s_tokensWithSingleTrailingCRLF[(int)kind].Value;
            }
        }

        if (leading == GreenSyntaxFactory.ElasticZeroSpace && trailing == GreenSyntaxFactory.ElasticZeroSpace)
        {
            return s_tokensWithElasticTrivia[(int)kind].Value;
        }

        return new SyntaxTokenWithTrivia(kind, leading, trailing);
    }

    /// <summary>Creates a missing token.</summary>
    public static GreenSyntaxToken CreateMissing(SyntaxKind kind)
    {
        if (kind <= LastTokenWithWellKnownText)
        {
            return s_missingTokensWithNoTrivia[(int)kind].Value;
        }
        else if (kind == SyntaxKind.IdentifierToken)
        {
            return s_missingIdentifierTokenWithNoTrivia;
        }

        return new MissingTokenWithTrivia(kind, leading: null, trailing: null);
    }

    /// <summary>Creates a missing token with trivia.</summary>
    public static GreenSyntaxToken CreateMissing(SyntaxKind kind, GreenNode? leading, GreenNode? trailing)
    {
        return new MissingTokenWithTrivia(kind, leading, trailing);
    }

    /// <summary>Creates a token with a value.</summary>
    public static GreenSyntaxToken WithValue<T>(SyntaxKind kind, string text, T value)
    {
        return new SyntaxTokenWithValue<T>(kind, text, value);
    }

    /// <summary>Creates a token with a value and trivia.</summary>
    public static GreenSyntaxToken WithValue<T>(SyntaxKind kind, GreenNode? leading, string text, T value, GreenNode? trailing)
    {
        return new SyntaxTokenWithValueAndTrivia<T>(kind, text, value, leading, trailing);
    }

    /// <summary>Creates a string literal token.</summary>
    public static GreenSyntaxToken StringLiteral(string text)
    {
        return new SyntaxTokenWithValue<string>(SyntaxKind.StringLiteralToken, text, text);
    }

    /// <summary>Creates a string literal token with trivia.</summary>
    public static GreenSyntaxToken StringLiteral(GreenNode leading, string text, GreenNode trailing)
    {
        return new SyntaxTokenWithValueAndTrivia<string>(SyntaxKind.StringLiteralToken, text, text, leading, trailing);
    }

    /// <summary>Gets the leading trivia.</summary>
    public GreenSyntaxList<GreenNode> LeadingTrivia => new(GetLeadingTrivia());

    /// <summary>Gets the trailing trivia.</summary>
    public GreenSyntaxList<GreenNode> TrailingTrivia => new(GetTrailingTrivia());

    /// <summary>Creates a new token with leading trivia.</summary>
    public sealed override GreenNode WithLeadingTrivia(GreenNode? trivia)
    {
        if (trivia is not GreenNode green) return this;
        return TokenWithLeadingTrivia(green);
    }

    /// <summary>Creates a new token with leading trivia.</summary>
    public virtual GreenSyntaxToken TokenWithLeadingTrivia(GreenNode? trivia)
    {
        return new SyntaxTokenWithTrivia(Kind, trivia, null, GetDiagnostics(), GetAnnotations());
    }

    /// <summary>Creates a new token with trailing trivia.</summary>
    public sealed override GreenNode WithTrailingTrivia(GreenNode? trivia)
    {
        if (trivia is not GreenNode green) return this;
        return TokenWithTrailingTrivia(green);
    }

    /// <summary>Creates a new token with trailing trivia.</summary>
    public virtual GreenSyntaxToken TokenWithTrailingTrivia(GreenNode? trivia)
    {
        return new SyntaxTokenWithTrivia(Kind, null, trivia, GetDiagnostics(), GetAnnotations());
    }

    /// <summary>Defines the range of well-known tokens.</summary>
    public const SyntaxKind FirstTokenWithWellKnownText = SyntaxKind.FirstTokenWithWellKnownText;
    public const SyntaxKind LastTokenWithWellKnownText = SyntaxKind.LastTokenWithWellKnownText;

    /// <summary>Predefined token arrays.</summary>
    private static readonly ArrayElement<GreenSyntaxToken>[] s_tokensWithNoTrivia = new ArrayElement<GreenSyntaxToken>[(int)LastTokenWithWellKnownText + 1];
    private static readonly ArrayElement<GreenSyntaxToken>[] s_tokensWithSingleTrailingSpace = new ArrayElement<GreenSyntaxToken>[(int)LastTokenWithWellKnownText + 1];
    private static readonly ArrayElement<GreenSyntaxToken>[] s_tokensWithElasticTrivia = new ArrayElement<GreenSyntaxToken>[(int)LastTokenWithWellKnownText + 1];
    private static readonly ArrayElement<GreenSyntaxToken>[] s_tokensWithSingleTrailingCRLF = new ArrayElement<GreenSyntaxToken>[(int)LastTokenWithWellKnownText + 1];
    private static readonly ArrayElement<GreenSyntaxToken>[] s_missingTokensWithNoTrivia = new ArrayElement<GreenSyntaxToken>[(int)LastTokenWithWellKnownText + 1];
    private static readonly GreenSyntaxToken s_missingIdentifierTokenWithNoTrivia = new MissingTokenWithTrivia(SyntaxKind.IdentifierToken, leading: null, trailing: null);

    /// <summary>Static constructor initializing predefined tokens.</summary>
    static GreenSyntaxToken()
    {
        for (var kind = FirstTokenWithWellKnownText; kind <= LastTokenWithWellKnownText; kind++)
        {
            s_tokensWithNoTrivia[(int)kind].Value = new GreenSyntaxToken(kind);
            s_tokensWithElasticTrivia[(int)kind].Value = new SyntaxTokenWithTrivia(kind, GreenSyntaxFactory.ElasticZeroSpace, GreenSyntaxFactory.ElasticZeroSpace);
            s_tokensWithSingleTrailingSpace[(int)kind].Value = new SyntaxTokenWithTrivia(kind, null, GreenSyntaxFactory.Space);
            s_tokensWithSingleTrailingCRLF[(int)kind].Value = new SyntaxTokenWithTrivia(kind, null, GreenSyntaxFactory.CarriageReturnLineFeed);
            s_missingTokensWithNoTrivia[(int)kind].Value = new MissingTokenWithTrivia(kind, leading: null, trailing: null);
        }
    }

    /// <summary>Returns all well-known tokens.</summary>
    public static IEnumerable<GreenSyntaxToken> GetWellKnownTokens()
    {
        foreach (var element in s_tokensWithNoTrivia)
        {
            if (element.Value != null) yield return element.Value;
        }
        foreach (var element in s_tokensWithSingleTrailingSpace)
        {
            if (element.Value != null) yield return element.Value;
        }
        foreach (var element in s_tokensWithSingleTrailingCRLF)
        {
            if (element.Value != null) yield return element.Value;
        }
    }

    public static GreenSyntaxToken Identifier(string text)
    {
        return new SyntaxIdentifier(text);
    }

    public static GreenSyntaxToken Identifier(GreenNode? leading, string text, GreenNode? trailing)
    {
        if (leading == null)
        {
            if (trailing == null)
            {
                return Identifier(text);
            }
            else
            {
                return new SyntaxIdentifierWithTrailingTrivia(text, trailing);
            }
        }
        return new SyntaxIdentifierWithTrivia(SyntaxKind.IdentifierToken, text, text, leading, trailing);
    }

    public static GreenSyntaxToken Identifier(SyntaxKind contextualKind, GreenNode? leading, string text, string valueText, GreenNode? trailing)
    {
        if (contextualKind == SyntaxKind.IdentifierToken && valueText == text)
        {
            return Identifier(leading, text, trailing);
        }
        return new SyntaxIdentifierWithTrivia(contextualKind, text, valueText, leading, trailing);
    }

    public override string ToString()
    {
        return Text;
    }

    public override object? GetValue() => Value;

    public override string? GetValueText() => ValueText;

    public override int GetLeadingTriviaWidth()
    {
        var leading = GetLeadingTrivia();
        return leading != null ? leading.FullWidth : 0;
    }

    public override int GetTrailingTriviaWidth()
    {
        var trailing = GetTrailingTrivia();
        return trailing != null ? trailing.FullWidth : 0;
    }
}