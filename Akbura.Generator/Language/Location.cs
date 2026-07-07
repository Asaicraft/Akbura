// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Diagnostics;

namespace Akbura.Language;

/// <summary>
/// A program location.
/// </summary>
[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
internal abstract class Location
{
    public static Location None => NoLocation.Singleton;

    public abstract LocationKind Kind { get; }

    public virtual AkburaSyntax? SourceSyntax
    {
        get
        {
            return null;
        }
    }

    public virtual TextSpan SourceSpan
    {
        get
        {
            return default;
        }
    }

    public virtual FileLinePositionSpan GetLineSpan()
    {
        return default;
    }

    protected virtual string GetDebuggerDisplay()
    {
        return Kind.ToString();
    }

    public override string ToString()
    {
        return GetDebuggerDisplay();
    }
}
