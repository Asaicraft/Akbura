// This file is ported and adopted from KirillOsenkov/XmlParser

using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Language.Syntax;

/// <summary>
/// A SyntaxAnnotation is used to annotate syntax elements with additional information.
///
/// Since syntax elements are immutable, annotating them requires creating new instances of them
/// with the annotations attached.
/// </summary>
[DebuggerDisplay("{GetDebuggerDisplay(), nq}")]
public sealed class AkburaSyntaxAnnotation : IEquatable<AkburaSyntaxAnnotation>
{
    /// <summary>
    /// A predefined syntax annotation that indicates whether the syntax element has elastic trivia.
    /// </summary>
    public static AkburaSyntaxAnnotation ElasticAnnotation { get; } = new();

    // use a value identity instead of object identity so a deserialized instance matches the original instance.
    private readonly long _id;
    private static long s_nextId;

    // use a value identity instead of object identity so a deserialized instance matches the original instance.
    public string? Kind { get; }
    public string? Data { get; }

    public AkburaSyntaxAnnotation()
    {
        _id = System.Threading.Interlocked.Increment(ref s_nextId);
    }

    public AkburaSyntaxAnnotation(string kind)
        : this()
    {
        this.Kind = kind;
    }

    public AkburaSyntaxAnnotation(string kind, string data)
        : this(kind)
    {
        this.Data = data;
    }

    private string GetDebuggerDisplay()
    {
        return string.Format("Annotation: Kind='{0}' Data='{1}'", this.Kind ?? "", this.Data ?? "");
    }

    public bool Equals(AkburaSyntaxAnnotation? other)
    {
        return other is not null && _id == other._id;
    }

    public static bool operator ==(AkburaSyntaxAnnotation left, AkburaSyntaxAnnotation right)
    {
        if ((object)left == (object)right)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    public static bool operator !=(AkburaSyntaxAnnotation left, AkburaSyntaxAnnotation right)
    {
        if ((object)left == (object)right)
        {
            return false;
        }

        if (left is null || right is null)
        {
            return true;
        }

        return !left.Equals(right);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as AkburaSyntaxAnnotation);
    }

    public override int GetHashCode()
    {
        return _id.GetHashCode();
    }
}