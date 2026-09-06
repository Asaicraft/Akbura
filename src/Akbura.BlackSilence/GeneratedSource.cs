using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;

namespace Akbura.BlackSilence;

internal readonly struct GeneratedSource
{
    public GeneratedSource(string hintName, SourceText sourceText)
    {
        HintName = hintName;
        SourceText = sourceText;
    }

    public string HintName { get; }

    public SourceText SourceText { get; }
}

internal sealed class GeneratedSourceComparer : IEqualityComparer<GeneratedSource>
{
    public static readonly GeneratedSourceComparer Instance = new();

    private GeneratedSourceComparer()
    {
    }

    public bool Equals(GeneratedSource left, GeneratedSource right)
    {
        if (!StringComparer.Ordinal.Equals(left.HintName, right.HintName))
        {
            return false;
        }

        return ReferenceEquals(left.SourceText, right.SourceText) ||
            left.SourceText.ContentEquals(right.SourceText);
    }

    public int GetHashCode(GeneratedSource source)
    {
        return StringComparer.Ordinal.GetHashCode(source.HintName);
    }
}
