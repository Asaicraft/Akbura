using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Akbura.Language.Syntax;

[DebuggerDisplay("{GetDebuggerDisplay(), nq}")]
[StructLayout(LayoutKind.Auto)]
internal readonly struct SyntaxTrivia : IEquatable<SyntaxTrivia>
{
    public static readonly Func<SyntaxTrivia, bool> Any = t => true;

    public SyntaxTrivia(in SyntaxToken token, GreenNode? triviaNode, int position, int index)
    {
        Token = token;
        UnderlyingNode = triviaNode;
        Position = position;
        Index = index;

        Debug.Assert(RawKind != 0 || Equals(default));
    }

    /// <summary>
    /// An integer representing the language specific kind of this trivia.
    /// </summary>
    public ushort RawKind => UnderlyingNode?.RawKind ?? 0;

    public SyntaxKind Kind => UnderlyingNode?.Kind ?? SyntaxKind.None;

    private string GetDebuggerDisplay()
    {
        return GetType().Name + " " + (UnderlyingNode?.KindText ?? "None") + " " + ToString();
    }

    /// <summary>
    /// The parent token that contains this token in its LeadingTrivia or TrailingTrivia collection.
    /// </summary>
    public SyntaxToken Token { get; }

    public GreenNode? UnderlyingNode { get; }

    public GreenNode RequiredUnderlyingNode
    {
        get
        {
            var node = UnderlyingNode;
            Debug.Assert(node is not null);
            return node!;
        }
    }

    public int Position { get; }

    public int Index { get; }

    /// <summary>
    /// The width of this trivia in characters. If this trivia is a structured trivia then the returned width will
    /// not include the widths of any leading or trailing trivia present on the child non-terminal node of this
    /// trivia.
    /// </summary>
    public int Width => UnderlyingNode?.Width ?? 0;

    /// <summary>
    /// The width of this trivia in characters. If this trivia is a structured trivia then the returned width will
    /// include the widths of any leading or trailing trivia present on the child non-terminal node of this trivia.
    /// </summary>
    public int FullWidth => UnderlyingNode?.FullWidth ?? 0;

    /// <summary>
    /// The absolute span of this trivia in characters. If this trivia is a structured trivia then the returned span
    /// will not include spans of any leading or trailing trivia present on the child non-terminal node of this
    /// trivia.
    /// </summary>
    public TextSpan Span => UnderlyingNode != null
        ? new(Position + UnderlyingNode.GetLeadingTriviaWidth(), UnderlyingNode.Width)
        : default;

    /// <summary>
    /// Same as accessing <see cref="TextSpan.Start"/> on <see cref="Span"/>.
    /// </summary>
    /// <remarks>
    /// Slight performance improvement.
    /// </remarks>
    public int SpanStart
    {
        get
        {
            return UnderlyingNode != null
                ? Position + UnderlyingNode.GetLeadingTriviaWidth()
                : 0; // default(TextSpan).Start
        }
    }

    /// <summary>
    /// The absolute span of this trivia in characters. If this trivia is a structured trivia then the returned span
    /// will include spans of any leading or trailing trivia present on the child non-terminal node of this trivia.
    /// </summary>
    public TextSpan FullSpan => UnderlyingNode != null ? new(Position, UnderlyingNode.FullWidth) : default;

    /// <summary>
    /// Determines whether this trivia has any diagnostics on it. If this trivia is a structured trivia then the
    /// returned value will indicate whether this trivia or any of its descendant nodes, tokens or trivia have any
    /// diagnostics on them.
    /// </summary>>
    public bool ContainsDiagnostics => UnderlyingNode?.ContainsDiagnostics ?? false;

    /// <summary>
    /// Determines whether this trivia or any of its structure has annotations.
    /// </summary>
    public bool ContainsAnnotations => UnderlyingNode?.ContainsAnnotations ?? false;

    /// <summary>
    /// Determines where this trivia has annotations of the specified annotation kind.
    /// </summary>
    public bool HasAnnotations(string annotationKind)
    {
        return UnderlyingNode?.HasAnnotations(annotationKind) ?? false;
    }

    /// <summary>
    /// Determines where this trivia has any annotations of the specified annotation kinds.
    /// </summary>
    public bool HasAnnotations(params string[] annotationKinds)
    {
        return UnderlyingNode?.HasAnnotations(annotationKinds) ?? false;
    }

    /// <summary>
    /// Determines whether this trivia has the specific annotation.
    /// </summary>
    public bool HasAnnotation([NotNullWhen(true)] AkburaSyntaxAnnotation? annotation)
    {
        return UnderlyingNode?.HasAnnotation(annotation) ?? false;
    }

    /// <summary>
    /// Get all the annotations of the specified annotation kind.
    /// </summary>
    public IEnumerable<AkburaSyntaxAnnotation> GetAnnotations(string annotationKind)
    {
        return UnderlyingNode != null
            ? UnderlyingNode.GetAnnotations(annotationKind)
            : [];
    }

    /// <summary>
    /// Get all the annotations of the specified annotation kinds.
    /// </summary>
    public IEnumerable<AkburaSyntaxAnnotation> GetAnnotations(params string[] annotationKinds)
    {
        return UnderlyingNode != null
            ? UnderlyingNode.GetAnnotations(annotationKinds)
            : [];
    }

    /// <summary> 
    /// Returns the string representation of this trivia. If this trivia is structured trivia then the returned string
    /// will not include any leading or trailing trivia present on the StructuredTriviaSyntax node of this trivia.
    /// </summary>
    /// <returns>The string representation of this trivia.</returns>
    /// <remarks>The length of the returned string is always the same as Span.Length</remarks>
    public override string ToString()
    {
        return UnderlyingNode != null ? UnderlyingNode.ToString() : string.Empty;
    }

    /// <summary> 
    /// Returns the full string representation of this trivia. If this trivia is structured trivia then the returned string will
    /// include any leading or trailing trivia present on the StructuredTriviaSyntax node of this trivia.
    /// </summary>
    /// <returns>The full string representation of this trivia.</returns>
    /// <remarks>The length of the returned string is always the same as FullSpan.Length</remarks>
    public string ToFullString()
    {
        return UnderlyingNode != null ? UnderlyingNode.ToFullString() : string.Empty;
    }

    /// <summary>
    /// Writes the full text of this trivia to the specified TextWriter.
    /// </summary>
    public void WriteTo(System.IO.TextWriter writer)
    {
        UnderlyingNode?.WriteTo(writer);
    }

    /// <summary>
    /// Determines whether two <see cref="SyntaxTrivia"/>s are equal.
    /// </summary>
    public static bool operator ==(SyntaxTrivia left, SyntaxTrivia right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two <see cref="SyntaxTrivia"/>s are unequal.
    /// </summary>
    public static bool operator !=(SyntaxTrivia left, SyntaxTrivia right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Determines whether the supplied <see cref="SyntaxTrivia"/> is equal to this
    /// <see cref="SyntaxTrivia"/>.
    /// </summary>
    public bool Equals(SyntaxTrivia other)
    {
        return Token == other.Token && UnderlyingNode == other.UnderlyingNode && Position == other.Position && Index == other.Index;
    }

    /// <summary>
    /// Determines whether the supplied <see cref="SyntaxTrivia"/> is equal to this
    /// <see cref="SyntaxTrivia"/>.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is SyntaxTrivia trivia && Equals(trivia);
    }

    /// <summary>
    /// Serves as hash function for <see cref="SyntaxTrivia"/>.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Token, UnderlyingNode, Position, Index);
    }

    #region Annotations 
    /// <summary>
    /// Creates a new SyntaxTrivia with the specified annotations.
    /// </summary>
    public SyntaxTrivia WithAdditionalAnnotations(params AkburaSyntaxAnnotation[] annotations)
    {
        return WithAdditionalAnnotations((IEnumerable<AkburaSyntaxAnnotation>)annotations);
    }

    /// <summary>
    /// Creates a new SyntaxTrivia with the specified annotations.
    /// </summary>
    public SyntaxTrivia WithAdditionalAnnotations(IEnumerable<AkburaSyntaxAnnotation> annotations)
    {
        if(annotations == null)
        {
            throw new ArgumentNullException(nameof(annotations));
        }

        if (UnderlyingNode != null)
        {
            return new SyntaxTrivia(
                token: default,
                triviaNode: UnderlyingNode.AddAnnotations(annotations),
                position: 0, index: 0);
        }

        return default;
    }

    /// <summary>
    /// Creates a new SyntaxTrivia without the specified annotations.
    /// </summary>
    public SyntaxTrivia WithoutAnnotations(params AkburaSyntaxAnnotation[] annotations)
    {
        return WithoutAnnotations((IEnumerable<AkburaSyntaxAnnotation>)annotations);
    }

    /// <summary>
    /// Creates a new SyntaxTrivia without the specified annotations.
    /// </summary>
    public SyntaxTrivia WithoutAnnotations(IEnumerable<AkburaSyntaxAnnotation> annotations)
    {
        if(annotations == null)
        {
            throw new ArgumentNullException(nameof(annotations));
        }

        if (UnderlyingNode != null)
        {
            return new SyntaxTrivia(
                token: default,
                triviaNode: UnderlyingNode.WithoutAnnotations(annotations),
                position: 0, index: 0);
        }

        return default;
    }

    /// <summary>
    /// Creates a new SyntaxTrivia without annotations of the specified kind.
    /// </summary>
    public SyntaxTrivia WithoutAnnotations(string annotationKind)
    {
        if(annotationKind == null)
        {
            throw new ArgumentNullException(nameof(annotationKind));
        }

        if (HasAnnotations(annotationKind))
        {
            return WithoutAnnotations(GetAnnotations(annotationKind));
        }

        return this;
    }

    /// <summary>
    /// Copies all SyntaxAnnotations, if any, from this SyntaxTrivia instance and attaches them to a new instance based on <paramref name="trivia" />.
    /// </summary>
    public SyntaxTrivia CopyAnnotationsTo(SyntaxTrivia trivia)
    {
        if (trivia.UnderlyingNode == null)
        {
            return default;
        }

        if (UnderlyingNode == null)
        {
            return trivia;
        }

        var annotations = UnderlyingNode.GetAnnotations();
        if (annotations == null || annotations.Length == 0)
        {
            return trivia;
        }

        return new SyntaxTrivia(
            token: default,
            triviaNode: trivia.UnderlyingNode.AddAnnotations(annotations),
            position: 0, index: 0);
    }
    #endregion


    /// <summary>
    /// Gets a list of all the diagnostics associated with this trivia.
    /// This method does not filter diagnostics based on #pragmas and compiler options
    /// like nowarn, warnaserror etc.
    /// </summary>
    public IEnumerable<AkburaDiagnostic> GetDiagnostics()
    {
        var diagnostics = UnderlyingNode?.GetDiagnostics();

        return diagnostics ?? [];
    }

    /// <summary>
    /// Determines if this trivia is equivalent to the specified trivia.
    /// </summary>
    public bool IsEquivalentTo(SyntaxTrivia trivia)
    {
        return
            (UnderlyingNode == null && trivia.UnderlyingNode == null) ||
            (UnderlyingNode != null && trivia.UnderlyingNode != null && UnderlyingNode.IsEquivalentTo(trivia.UnderlyingNode));
    }
}