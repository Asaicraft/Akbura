// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

namespace Akbura.Language;

/// <summary>
/// Specifies the kind of location.
/// </summary>
internal enum LocationKind : byte
{
    None = 0,
    SourceFile = 1,
    ExternalFile = 2,
}
