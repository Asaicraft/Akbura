using System;
using System.Collections.Generic;

namespace Akbura.BlackSilence;

internal sealed class GenerationRequestComparer : IEqualityComparer<BlackSilenceGenerationRequest?>
{
    public static readonly GenerationRequestComparer Instance = new();

    public bool Equals(BlackSilenceGenerationRequest? left, BlackSilenceGenerationRequest? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null || !ReferenceEquals(left.State, right.State) ||
            left.Options != right.Options || left.Components.Length != right.Components.Length ||
            left.ExternalAkcss.Length != right.ExternalAkcss.Length || left.InlineAkcss.Length != right.InlineAkcss.Length ||
            left.ComputeDiagnostics != right.ComputeDiagnostics || left.Diagnostics.Length != right.Diagnostics.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Components.Length; i++)
        {
            if (left.Components[i].Identity != right.Components[i].Identity ||
                !left.Components[i].Version.Equals(right.Components[i].Version))
            {
                return false;
            }
        }

        for (var i = 0; i < left.ExternalAkcss.Length; i++)
        {
            if (left.ExternalAkcss[i].Identity != right.ExternalAkcss[i].Identity ||
                !left.ExternalAkcss[i].Version.Equals(right.ExternalAkcss[i].Version))
            {
                return false;
            }
        }

        for (var i = 0; i < left.InlineAkcss.Length; i++)
        {
            if (left.InlineAkcss[i].Identity != right.InlineAkcss[i].Identity ||
                !left.InlineAkcss[i].Version.Equals(right.InlineAkcss[i].Version))
            {
                return false;
            }
        }

        for (var i = 0; i < left.Diagnostics.Length; i++)
        {
            if (left.Diagnostics[i].Document.FilePath != right.Diagnostics[i].Document.FilePath ||
                !left.Diagnostics[i].Version.Equals(right.Diagnostics[i].Version))
            {
                return false;
            }
        }

        return true;
    }

    public int GetHashCode(BlackSilenceGenerationRequest? request) => request?.Components.Length ?? 0;
}
