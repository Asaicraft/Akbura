using Microsoft.CodeAnalysis;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Akbura.TestUtilities;

/// <summary>
/// Exact value serialization for test/benchmark oracles. The publisher's volatile
/// revision stamp is validated separately, not compared to a fresh driver's one.
/// JSON escaping keeps arbitrary messages/property values unambiguous.
/// </summary>
internal static class DiagnosticParity
{
    internal static string Key(Diagnostic diagnostic) => JsonSerializer.Serialize(new
    {
        diagnostic.Id,
        diagnostic.Severity,
        diagnostic.WarningLevel,
        diagnostic.IsSuppressed,
        Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
        diagnostic.Descriptor.Category,
        diagnostic.Descriptor.DefaultSeverity,
        diagnostic.Descriptor.IsEnabledByDefault,
        diagnostic.Descriptor.HelpLinkUri,
        Tags = diagnostic.Descriptor.CustomTags.ToArray(),
        Title = diagnostic.Descriptor.Title.ToString(CultureInfo.InvariantCulture),
        Description = diagnostic.Descriptor.Description.ToString(CultureInfo.InvariantCulture),
        Primary = Location(diagnostic.Location),
        Additional = diagnostic.AdditionalLocations.Select(Location).ToArray(),
        Properties = diagnostic.Properties.Where(static pair => pair.Key != "akbura.document-version")
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => new { pair.Key, pair.Value }).ToArray(),
    });

    private static object Location(Microsoft.CodeAnalysis.Location location) => new
    {
        location.Kind,
        location.SourceSpan,
        Original = location.GetLineSpan(),
        Mapped = location.GetMappedLineSpan(),
    };
}
