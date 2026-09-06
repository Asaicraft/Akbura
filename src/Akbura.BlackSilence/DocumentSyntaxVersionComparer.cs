using Akbura.Language.CodeGeneration;
using System;
using System.Collections.Generic;

namespace Akbura.BlackSilence;

internal sealed class DocumentSyntaxVersionComparer : IEqualityComparer<DocumentSyntaxVersion>
{
    public static readonly DocumentSyntaxVersionComparer Instance = new();

    public bool Equals(DocumentSyntaxVersion? left, DocumentSyntaxVersion? right)
    {
        return ReferenceEquals(left, right) || left?.HasSameGenerationShape(right) == true;
    }

    public int GetHashCode(DocumentSyntaxVersion version) => StringComparer.Ordinal.GetHashCode(version.FilePath);
}
