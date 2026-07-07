// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Runtime.CompilerServices;

namespace Akbura.Language;

internal sealed class SourceLocation : Location, IEquatable<SourceLocation?>
{
    private readonly AkburaSyntax _syntax;
    private readonly TextSpan _span;

    public SourceLocation(AkburaSyntax syntax)
        : this(syntax, syntax?.Span ?? default)
    {
    }

    public SourceLocation(AkburaSyntax syntax, TextSpan span)
    {
        _syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        _span = span;
    }

    public override LocationKind Kind
    {
        get
        {
            return LocationKind.SourceFile;
        }
    }

    public override AkburaSyntax SourceSyntax
    {
        get
        {
            return _syntax;
        }
    }

    public AkburaSyntax Syntax
    {
        get
        {
            return _syntax;
        }
    }

    public override TextSpan SourceSpan
    {
        get
        {
            return _span;
        }
    }

    public TextSpan Span => SourceSpan;

    public bool Equals(SourceLocation? other)
    {
        return ReferenceEquals(this, other) ||
               other != null &&
               ReferenceEquals(_syntax, other._syntax) &&
               _span == other._span;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as SourceLocation);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(RuntimeHelpers.GetHashCode(_syntax), _span.GetHashCode());
    }

    protected override string GetDebuggerDisplay()
    {
        var text = _syntax.Root.ToFullString();
        var value = _span.End <= text.Length
            ? text.Substring(_span.Start, _span.Length)
            : string.Empty;
        return base.GetDebuggerDisplay() + " \"" + value + "\"";
    }
}
