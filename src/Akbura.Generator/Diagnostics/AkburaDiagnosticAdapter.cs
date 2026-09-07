using Akbura.Collections;
using Akbura.Language.Syntax;
#if STATS
using Akbura.Language.CodeGeneration;
#endif
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;

namespace Akbura.Diagnostics;

public static class AkburaDiagnosticAdapter
{
    private static readonly ConcurrentDictionary<DescriptorKey, DiagnosticDescriptor> s_descriptors = new();

    public static Diagnostic ToRoslyn(in AkburaDiagnosticRecord diagnostic)
    {
        var descriptor = s_descriptors.GetOrAdd(
            new(diagnostic.Id, diagnostic.Severity),
            static key =>
            {
#if STATS
                GenerationStatistics.Increment(GenerationStatisticCounter.DiagnosticDescriptorCreated);
#endif
                return new DiagnosticDescriptor(
                    key.Id, key.Id, "{0}", "Akbura", GetSeverity(key.Severity), isEnabledByDefault: true);
            });
        var additionalLocations = diagnostic.AdditionalLocations.NullToEmpty();
        var locations = new Location[additionalLocations.Length];
        for (var i = 0; i < additionalLocations.Length; i++)
        {
            var location = additionalLocations[i];
            locations[i] = CreateLocation(location);
        }

#if STATS
        GenerationStatistics.Increment(GenerationStatisticCounter.RoslynDiagnosticCreated);
#endif
        return Diagnostic.Create(
            descriptor,
            CreateLocation(new(diagnostic.FilePath, diagnostic.Span, diagnostic.LineSpan)),
            locations,
            GetProperties(diagnostic),
            diagnostic.Message);
    }

    public static ImmutableDictionary<string, string?> GetProperties(in AkburaDiagnosticRecord diagnostic)
    {
        return (diagnostic.Properties ?? ImmutableDictionary<string, string?>.Empty)
            .SetItem("akbura.origin", GetOrigin(diagnostic.Provenance))
            .SetItem("akbura.kind", diagnostic.Kind.ToString().ToLowerInvariant())
            .SetItem("akbura.logical-id", diagnostic.LogicalId)
            .SetItem("akbura.document-version", diagnostic.DocumentVersion.ToString(CultureInfo.InvariantCulture));
    }

    private static Location CreateLocation(AkburaDiagnosticLocation location) =>
        string.IsNullOrEmpty(location.FilePath)
            ? Location.None
            : Location.Create(location.FilePath, location.Span, location.LineSpan);

    private static DiagnosticSeverity GetSeverity(AkburaDiagnosticSeverity severity) => severity switch
    {
        AkburaDiagnosticSeverity.Hidden => DiagnosticSeverity.Hidden,
        AkburaDiagnosticSeverity.Info => DiagnosticSeverity.Info,
        AkburaDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
        _ => DiagnosticSeverity.Error,
    };

    private static string GetOrigin(DiagnosticProvenance provenance)
    {
        return provenance switch
        {
            DiagnosticProvenance.AkburaCore => "akbura-core",
            DiagnosticProvenance.BlackSilenceGenerator => "black-silence",
            DiagnosticProvenance.Workspaces => "workspaces",
            DiagnosticProvenance.LanguageServer => "language-server",
            DiagnosticProvenance.VisualStudio => "visual-studio",
            _ => GetCombinedOrigin(provenance),
        };
    }

    private static string GetCombinedOrigin(DiagnosticProvenance provenance)
    {
        var origins = new List<string>();
        foreach (var origin in new[]
        {
            DiagnosticProvenance.AkburaCore,
            DiagnosticProvenance.BlackSilenceGenerator,
            DiagnosticProvenance.Workspaces,
            DiagnosticProvenance.LanguageServer,
            DiagnosticProvenance.VisualStudio,
        })
        {
            if ((provenance & origin) != 0)
            {
                origins.Add(GetOrigin(origin));
            }
        }

        return string.Join(",", origins);
    }

    private readonly record struct DescriptorKey(string Id, AkburaDiagnosticSeverity Severity);
}
