using Akbura.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Akbura.Diagnostics;

/// <summary>
/// Exact logical equality excludes publisher provenance and snapshot version.
/// Transport properties are derived from fields, never a second identity source.
/// </summary>
public sealed class AkburaDiagnosticCanonicalComparer :
    IEqualityComparer<AkburaDiagnosticRecord>, IComparer<AkburaDiagnosticRecord>
{
    public static readonly AkburaDiagnosticCanonicalComparer Instance = new();

    private static readonly bool s_ignorePathCase = Path.DirectorySeparatorChar == '\\';
    private static readonly StringComparer s_pathComparer = s_ignorePathCase
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public bool Equals(AkburaDiagnosticRecord x, AkburaDiagnosticRecord y)
    {
        if (!string.Equals(x.Id, y.Id, StringComparison.Ordinal) ||
            x.Severity != y.Severity || x.Kind != y.Kind ||
            !string.Equals(x.Message, y.Message, StringComparison.Ordinal) ||
            !PathsEqual(x.FilePath, y.FilePath) ||
            x.Span != y.Span || x.LineSpan != y.LineSpan)
        {
            return false;
        }

        var leftLocations = x.AdditionalLocations.NullToEmpty();
        var rightLocations = y.AdditionalLocations.NullToEmpty();
        if (leftLocations.Length != rightLocations.Length)
        {
            return false;
        }

        for (var i = 0; i < leftLocations.Length; i++)
        {
            var left = leftLocations[i];
            var right = rightLocations[i];
            if (!PathsEqual(left.FilePath, right.FilePath) ||
                left.Span != right.Span || left.LineSpan != right.LineSpan)
            {
                return false;
            }
        }

        return PropertiesEqual(x.Properties, y.Properties);
    }

    public int GetHashCode(AkburaDiagnosticRecord value)
    {
        var hash = GetFingerprint(value);
        return unchecked((int)hash ^ (int)(hash >> 32));
    }

    public int Compare(AkburaDiagnosticRecord x, AkburaDiagnosticRecord y)
    {
        var result = CompareLocation(new(x.FilePath, x.Span, x.LineSpan), new(y.FilePath, y.Span, y.LineSpan));
        if (result != 0) return result;
        result = y.Severity.CompareTo(x.Severity);
        if (result != 0) return result;
        result = string.CompareOrdinal(x.Id, y.Id);
        if (result != 0) return result;
        result = string.CompareOrdinal(x.Message, y.Message);
        if (result != 0) return result;
        result = x.Kind.CompareTo(y.Kind);
        if (result != 0) return result;

        var leftLocations = x.AdditionalLocations.NullToEmpty();
        var rightLocations = y.AdditionalLocations.NullToEmpty();
        result = leftLocations.Length.CompareTo(rightLocations.Length);
        if (result != 0) return result;
        for (var i = 0; i < leftLocations.Length; i++)
        {
            result = CompareLocation(leftLocations[i], rightLocations[i]);
            if (result != 0) return result;
        }

        using var leftProperties = GetCanonicalProperties(x.Properties).GetEnumerator();
        using var rightProperties = GetCanonicalProperties(y.Properties).GetEnumerator();
        while (true)
        {
            var hasLeft = leftProperties.MoveNext();
            var hasRight = rightProperties.MoveNext();
            result = hasLeft.CompareTo(hasRight);
            if (result != 0 || !hasLeft) return result;
            result = string.CompareOrdinal(leftProperties.Current.Key, rightProperties.Current.Key);
            if (result != 0) return result;
            result = string.CompareOrdinal(leftProperties.Current.Value, rightProperties.Current.Value);
            if (result != 0) return result;
        }
    }

    private static int CompareLocation(AkburaDiagnosticLocation left, AkburaDiagnosticLocation right)
    {
        var result = s_pathComparer.Compare(NormalizePath(left.FilePath), NormalizePath(right.FilePath));
        if (result != 0) return result;
        result = left.Span.Start.CompareTo(right.Span.Start);
        if (result != 0) return result;
        result = left.Span.Length.CompareTo(right.Span.Length);
        if (result != 0) return result;
        result = left.LineSpan.Start.CompareTo(right.LineSpan.Start);
        return result != 0 ? result : left.LineSpan.End.CompareTo(right.LineSpan.End);
    }

    internal static string GetLogicalId(in AkburaDiagnosticRecord value) =>
        GetFingerprint(value).ToString("x16", CultureInfo.InvariantCulture);

    internal static bool IsTransportProperty(string key) => key is
        "akbura.origin" or "akbura.kind" or "akbura.logical-id" or "akbura.document-version";

    internal static string NormalizePath(string? path)
    {
        // Do not resolve relative paths against the process working directory.
        // Backslash is a valid filename character on Unix, not a separator.
        return s_ignorePathCase ? (path ?? string.Empty).Replace('\\', '/') : path ?? string.Empty;
    }

    public static bool PathsEqual(string? left, string? right) =>
        s_pathComparer.Equals(NormalizePath(left), NormalizePath(right));

    private static bool PropertiesEqual(
        ImmutableDictionary<string, string?>? left,
        ImmutableDictionary<string, string?>? right)
    {
        var leftProperties = GetCanonicalProperties(left);
        var rightProperties = GetCanonicalProperties(right);
        return leftProperties.SequenceEqual(rightProperties);
    }

    private static IEnumerable<KeyValuePair<string, string?>> GetCanonicalProperties(
        ImmutableDictionary<string, string?>? properties) =>
        (properties ?? ImmutableDictionary<string, string?>.Empty)
            .Where(static pair => !IsTransportProperty(pair.Key))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal);

    private static ulong GetFingerprint(in AkburaDiagnosticRecord value)
    {
        var hash = 14695981039346656037UL;
        AddString(ref hash, value.Id);
        AddInt32(ref hash, (int)value.Severity);
        AddInt32(ref hash, (int)value.Kind);
        AddString(ref hash, value.Message);
        AddLocation(ref hash, new(value.FilePath, value.Span, value.LineSpan));

        var locations = value.AdditionalLocations.NullToEmpty();
        AddInt32(ref hash, locations.Length);
        foreach (var location in locations)
        {
            AddLocation(ref hash, location);
        }

        foreach (var pair in GetCanonicalProperties(value.Properties))
        {
            AddString(ref hash, pair.Key);
            AddString(ref hash, pair.Value);
        }

        return hash;
    }

    private static void AddLocation(ref ulong hash, AkburaDiagnosticLocation location)
    {
        var path = NormalizePath(location.FilePath);
        AddString(ref hash, s_ignorePathCase ? path.ToUpperInvariant() : path);
        AddInt32(ref hash, location.Span.Start);
        AddInt32(ref hash, location.Span.Length);
        AddInt32(ref hash, location.LineSpan.Start.Line);
        AddInt32(ref hash, location.LineSpan.Start.Character);
        AddInt32(ref hash, location.LineSpan.End.Line);
        AddInt32(ref hash, location.LineSpan.End.Character);
    }

    private static void AddString(ref ulong hash, string? value)
    {
        AddInt32(ref hash, value?.Length ?? -1);
        if (value == null)
        {
            return;
        }

        foreach (var character in value)
        {
            hash = unchecked((hash ^ (byte)character) * 1099511628211UL);
            hash = unchecked((hash ^ (byte)(character >> 8)) * 1099511628211UL);
        }
    }

    private static void AddInt32(ref ulong hash, int value)
    {
        for (var shift = 0; shift < 32; shift += 8)
        {
            hash = unchecked((hash ^ (byte)(value >> shift)) * 1099511628211UL);
        }
    }
}
