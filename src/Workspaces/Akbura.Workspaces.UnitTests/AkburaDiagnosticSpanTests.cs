using Akbura.Diagnostics;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class AkburaDiagnosticSpanTests
{
    [Fact]
    public void CanonicalMetadataPreservesLegacySpanEqualityAndHashLookup()
    {
        var legacy = CreateSpan();
        var canonical = new AkburaDiagnosticRecord
        {
            Id = legacy.Code,
            Severity = legacy.Severity,
            Message = legacy.Message,
            FilePath = Path.GetFullPath("Diagnostic.akbura"),
            Span = legacy.Span,
            LineSpan = new LinePositionSpan(new LinePosition(0, 1), new LinePosition(0, 4)),
            Kind = AkburaDiagnosticKind.Semantic,
        };
        var projected = legacy with { CanonicalDiagnostic = canonical };
        var refreshed = projected with
        {
            CanonicalDiagnostic = canonical with
            {
                Provenance = DiagnosticProvenance.Workspaces | DiagnosticProvenance.VisualStudio,
                DocumentVersion = 42,
            },
        };
        var values = new HashSet<AkburaDiagnosticSpan> { legacy };

        Assert.Equal(legacy, projected);
        Assert.Equal(projected, refreshed);
        Assert.Equal(legacy.GetHashCode(), refreshed.GetHashCode());
        Assert.Contains(projected, values);
        Assert.Contains(refreshed, values);

        var differentModule = canonical with
        {
            Properties = ImmutableDictionary<string, string?>.Empty.Add("akbura.module-identity", "OtherModule"),
        };
        Assert.Equal(legacy, legacy with { CanonicalDiagnostic = differentModule });
        Assert.False(AkburaDiagnosticCanonicalComparer.Instance.Equals(canonical, differentModule));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryLegacyFieldStillParticipatesInEquality(int changedField)
    {
        var original = CreateSpan();
        var changed = changedField switch
        {
            0 => original with { Span = new TextSpan(2, 3) },
            1 => original with { Code = "AKBURA_OTHER" },
            2 => original with { Message = "Another error" },
            _ => original with { Severity = AkburaDiagnosticSeverity.Warning },
        };

        Assert.NotEqual(original, changed);
        Assert.DoesNotContain(changed, new HashSet<AkburaDiagnosticSpan> { original });
    }

    private static AkburaDiagnosticSpan CreateSpan() =>
        new(new TextSpan(1, 3), "AKBURA_TEST", "Unknown property", AkburaDiagnosticSeverity.Error);
}
