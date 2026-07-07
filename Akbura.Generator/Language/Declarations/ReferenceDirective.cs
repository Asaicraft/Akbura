// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

namespace Akbura.Language;

internal readonly struct ReferenceDirective
{
    public ReferenceDirective(string file)
    {
        File = file ?? string.Empty;
    }

    public string File { get; }
}
