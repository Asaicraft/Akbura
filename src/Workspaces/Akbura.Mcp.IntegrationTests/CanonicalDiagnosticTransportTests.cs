using Akbura.Diagnostics;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text.Json;

namespace Akbura.Mcp.IntegrationTests;

/// <summary>
/// Verifies that MCP diagnostic serialization preserves canonical issue data and counts.
/// </summary>
public sealed class CanonicalDiagnosticTransportTests
{
    [Fact]
    public void DiagnosticMappingKeepsOwnershipRelatedLocationsAndCountsAcrossSerialization()
    {
        var requested = Path.GetFullPath("View.akbura");
        var foreign = Path.GetFullPath("Other.akcss");
        var generated = Path.GetFullPath("Generated.g.cs");
        var foreignRange = new LinePositionSpan(new(12, 4), new(12, 7));
        var locationless = new AkburaDiagnosticRecord
        {
            Id = "AKBURA_TEST",
            Severity = AkburaDiagnosticSeverity.Warning,
            Message = "Project diagnostic",
            FilePath = string.Empty,
            Span = default,
            LineSpan = default,
            Kind = AkburaDiagnosticKind.Infrastructure,
        };
        var imported = locationless with
        {
            FilePath = foreign,
            Severity = AkburaDiagnosticSeverity.Error,
            Kind = AkburaDiagnosticKind.Semantic,
            Span = new TextSpan(140, 3),
            LineSpan = foreignRange,
            AdditionalLocations = [new(generated, new TextSpan(20, 2), new(new(4, 0), new(4, 2)))],
        };
        var embedded = imported with
        {
            FilePath = requested,
            Properties = imported.Properties.Add("akbura.module-identity", "ReferenceLibrary"),
        };
        var unmapped = imported with { FilePath = generated };
        var canonical = AkburaDiagnosticCollection.Deduplicate([locationless, imported, embedded, unmapped]).ToImmutableArray();
        var payload = McpDiagnosticMapper.MapDiagnostics(canonical, requested, "document", 4, CancellationToken.None);
        var data = JsonSerializer.SerializeToElement(payload.Diagnostics, McpTools.JsonOptions);
        var issues = data.EnumerateArray().ToArray();

        Assert.Equal(4, issues.Length);
        var noLocation = Assert.Single(issues, issue => issue.GetProperty("sourceOwnership").GetString() == "locationless");
        Assert.Equal(JsonValueKind.Null, noLocation.GetProperty("location").ValueKind);
        var source = Assert.Single(issues, issue => issue.GetProperty("logicalId").GetString() == imported.LogicalId);
        Assert.Equal("foreign", source.GetProperty("sourceOwnership").GetString());
        Assert.Equal(foreign, source.GetProperty("location").GetProperty("path").GetString());
        Assert.Equal(12, source.GetProperty("location").GetProperty("originalRange").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(140, source.GetProperty("location").GetProperty("originalSpan").GetProperty("start").GetInt32());
        var related = Assert.Single(source.GetProperty("relatedLocations").EnumerateArray());
        Assert.Equal(generated, related.GetProperty("path").GetString());
        Assert.Equal(JsonValueKind.Null, related.GetProperty("originalRange").ValueKind);
        Assert.NotEmpty(related.GetProperty("mappingLimitation").GetString()!);

        var module = Assert.Single(issues, issue => issue.GetProperty("sourceOwnership").GetString() == "referenced_module");
        Assert.Equal("ReferenceLibrary", module.GetProperty("properties").GetProperty("akbura.module-identity").GetString());
        Assert.Equal(JsonValueKind.Null, module.GetProperty("location").GetProperty("uri").ValueKind);

        var limited = McpDiagnosticMapper.MapDiagnostics([locationless, imported], requested, "document", 1, CancellationToken.None);
        Assert.Single(limited.Diagnostics);
        Assert.Equal(2, limited.Counts.Total);
        Assert.Equal(1, limited.Counts.Errors);
        Assert.Equal(1, limited.Counts.Warnings);
    }
}
