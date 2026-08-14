using Akbura.Pools;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Workspaces;

internal sealed class AkburaCSharpMappedChange
{
    public AkburaCSharpMappedChange(
        ImmutableArray<TextChange> changes,
        int? newHostPosition,
        bool includesCommitCharacter)
    {
        Changes = changes.IsDefault
            ? ImmutableArray<TextChange>.Empty
            : changes;
        NewHostPosition = newHostPosition;
        IncludesCommitCharacter = includesCommitCharacter;
    }

    public ImmutableArray<TextChange> Changes { get; }

    public int? NewHostPosition { get; }

    public bool IncludesCommitCharacter { get; }
}

internal static class AkburaCSharpCompletionChangeMapper
{
    public static bool TryMapCompletionChange(
        SourceText hostText,
        SourceText projectedText,
        AkburaCSharpProjection projection,
        CompletionChange completionChange,
        out AkburaCSharpMappedChange mapped)
    {
        if (hostText == null)
        {
            throw new ArgumentNullException(nameof(hostText));
        }

        if (projectedText == null)
        {
            throw new ArgumentNullException(nameof(projectedText));
        }

        if (projection == null)
        {
            throw new ArgumentNullException(nameof(projection));
        }

        var projectedChanges = completionChange.TextChanges.IsDefaultOrEmpty
            ? ImmutableArray.Create(completionChange.TextChange)
            : completionChange.TextChanges;
        using var mappedChanges = ImmutableArrayBuilder<TextChange>.Rent(
            projectedChanges.Length + 1);
        using var directProjectedChanges =
            ImmutableArrayBuilder<TextChange>.Rent(projectedChanges.Length);
        var hasOutsideChanges = false;

        foreach (var change in projectedChanges)
        {
            if (projection.TryMapToHost(change.Span, out var hostSpan))
            {
                mappedChanges.Add(new TextChange(
                    hostSpan,
                    change.NewText ?? string.Empty));
                directProjectedChanges.Add(change);
                continue;
            }

            hasOutsideChanges = true;
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                "Projected edit is outside a " +
                $"known mapping: {change.Span}.");
        }

        var importTextLength = 0;
        var importChange = default(TextChange);
        if (hasOutsideChanges &&
            !TryMapImportChanges(
                hostText,
                projectedText,
                projection,
                projectedChanges,
                directProjectedChanges.ToImmutable(),
                out importChange))
        {
            mapped = null!;
            return false;
        }

        if (hasOutsideChanges)
        {
            if (!string.IsNullOrEmpty(importChange.NewText))
            {
                mappedChanges.Add(importChange);
                importTextLength = importChange.NewText!.Length;
            }
        }

        var mappedArray = mappedChanges.ToImmutable();
        int? newHostPosition = null;
        if (completionChange.NewPosition is { } projectedPosition)
        {
            if (!TryTransformSpan(
                    projection.ActiveMapping.ProjectedSpan,
                    projectedChanges,
                    out var projectedActiveSpan) ||
                projectedPosition < projectedActiveSpan.Start ||
                projectedPosition > projectedActiveSpan.End)
            {
                mapped = null!;
                return false;
            }

            var activeHostChanges = mappedArray
                .Where(change =>
                    Contains(
                        projection.ActiveMapping.HostSpan,
                        change.Span))
                .ToImmutableArray();
            if (!TryTransformSpan(
                    projection.ActiveMapping.HostSpan,
                    activeHostChanges,
                    out var hostActiveSpan))
            {
                mapped = null!;
                return false;
            }

            if (importTextLength != 0 &&
                projection.ImportContext.InsertionPosition <=
                    projection.ActiveMapping.HostSpan.Start)
            {
                hostActiveSpan = new TextSpan(
                    hostActiveSpan.Start + importTextLength,
                    hostActiveSpan.Length);
            }

            var relativePosition = projectedPosition -
                projectedActiveSpan.Start;
            if (relativePosition > hostActiveSpan.Length)
            {
                mapped = null!;
                return false;
            }

            newHostPosition = hostActiveSpan.Start + relativePosition;
        }

        mapped = new AkburaCSharpMappedChange(
            mappedArray,
            newHostPosition,
            completionChange.IncludesCommitCharacter);
        return true;
    }

    private static bool TryMapImportChanges(
        SourceText hostText,
        SourceText projectedText,
        AkburaCSharpProjection projection,
        ImmutableArray<TextChange> projectedChanges,
        ImmutableArray<TextChange> directProjectedChanges,
        out TextChange importChange)
    {
        importChange = default;
        SourceText changedProjectedText;
        SourceText directChangedProjectedText;
        try
        {
            changedProjectedText = projectedText.WithChanges(projectedChanges);
            directChangedProjectedText = directProjectedChanges.IsDefaultOrEmpty
                ? projectedText
                : projectedText.WithChanges(directProjectedChanges);
        }
        catch (ArgumentException)
        {
            return false;
        }

        var directRoot = CSharpSyntaxFactory
            .ParseCompilationUnit(directChangedProjectedText.ToString());
        var changedRoot = CSharpSyntaxFactory
            .ParseCompilationUnit(changedProjectedText.ToString());
        if (!CSharpSyntaxFactory.AreEquivalent(
                directRoot.WithUsings(default),
                changedRoot.WithUsings(default)))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                "Unsupported projected edit changed " +
                "the generated C# wrapper.");
            return false;
        }

        var oldKeys = directRoot.Usings
            .Select(CSharpUsingKey.Create)
            .ToImmutableArray();
        var newKeys = changedRoot.Usings
            .Select(CSharpUsingKey.Create)
            .ToImmutableArray();
        if (!IsSubsequence(oldKeys, newKeys))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                "Unsupported projected edit removed, " +
                "rewrote, or reordered a using directive.");
            return false;
        }

        var oldSet = oldKeys.ToImmutableHashSet();
        using var added = ImmutableArrayBuilder<CSharpUsingKey>.Rent();
        foreach (var key in newKeys)
        {
            if (oldSet.Contains(key) ||
                projection.ImportContext.ExistingImports.Contains(key))
            {
                continue;
            }

            if (key.IsGlobal ||
                key.IsStatic ||
                key.IsUnsafe ||
                key.Alias != null ||
                string.IsNullOrWhiteSpace(key.Name))
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Completion,
                    "Unsupported auto-import using: " +
                    key.Name);
                return false;
            }

            added.Add(key);
        }

        if (added.Count == 0)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                "Projected changes outside known " +
                "mappings did not add a supported using directive.");
            return false;
        }

        var importContext = projection.ImportContext;
        if ((uint)importContext.InsertionPosition > (uint)hostText.Length)
        {
            return false;
        }

        var text = CreateUsingText(added.WrittenSpan, importContext);
        importChange = new TextChange(
            new TextSpan(importContext.InsertionPosition, 0),
            text);
        return true;
    }

    private static string CreateUsingText(
        ReadOnlySpan<CSharpUsingKey> imports,
        AkburaCSharpImportContext context)
    {
        var builder = new StringBuilder();
        if (context.NeedsLeadingLineBreak)
        {
            builder.Append(context.NewLine);
        }

        for (var i = 0; i < imports.Length; i++)
        {
            if (i != 0)
            {
                builder.Append(context.NewLine);
            }

            builder.Append("using ");
            builder.Append(imports[i].Name);
            builder.Append(';');
        }

        if (context.NeedsTrailingLineBreak)
        {
            builder.Append(context.NewLine);
        }

        return builder.ToString();
    }

    private static bool IsSubsequence(
        ImmutableArray<CSharpUsingKey> oldKeys,
        ImmutableArray<CSharpUsingKey> newKeys)
    {
        var oldIndex = 0;
        foreach (var key in newKeys)
        {
            if (oldIndex < oldKeys.Length &&
                key.Equals(oldKeys[oldIndex]))
            {
                oldIndex++;
            }
        }

        return oldIndex == oldKeys.Length;
    }

    private static bool TryTransformSpan(
        TextSpan span,
        IEnumerable<TextChange> changes,
        out TextSpan transformedSpan)
    {
        var startDelta = 0;
        var endDelta = 0;
        foreach (var change in changes.OrderBy(static change => change.Span.Start))
        {
            var delta = (change.NewText?.Length ?? 0) - change.Span.Length;
            if (change.Span.End <= span.Start &&
                change.Span.Start < span.Start)
            {
                startDelta += delta;
                endDelta += delta;
                continue;
            }

            if (change.Span.Start >= span.Start &&
                change.Span.End <= span.End)
            {
                endDelta += delta;
                continue;
            }

            if (change.Span.Start >= span.End)
            {
                continue;
            }

            transformedSpan = default;
            return false;
        }

        var start = span.Start + startDelta;
        var end = span.End + endDelta;
        if (end < start)
        {
            transformedSpan = default;
            return false;
        }

        transformedSpan = TextSpan.FromBounds(start, end);
        return true;
    }

    private static bool Contains(TextSpan container, TextSpan value)
    {
        return value.Start >= container.Start &&
            value.End <= container.End;
    }
}
