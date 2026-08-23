using Akbura.Language.Syntax;
using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Microsoft.VisualStudio.Text;
using System.Collections.Immutable;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Diagnostics;

[Export(typeof(AkburaDiagnosticTableDataSource))]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class AkburaDiagnosticTableDataSource :
    ITableDataSource
{
    private const string SourceIdentifier =
        "Akbura.Diagnostics.ErrorTableDataSource";

    private static readonly string[] s_columns =
    [
        StandardTableColumnDefinitions.ErrorSeverity,
        StandardTableColumnDefinitions.ErrorCode,
        StandardTableColumnDefinitions.Text,
        StandardTableColumnDefinitions.ProjectName,
        StandardTableColumnDefinitions.DocumentName,
        StandardTableColumnDefinitions.Line,
        StandardTableColumnDefinitions.Column,
        StandardTableColumnDefinitions.ErrorSource,
        StandardTableColumnDefinitions.BuildTool,
        StandardTableColumnDefinitions.ErrorCategory,
        StandardTableColumnDefinitions.ErrorRank,
    ];

    private readonly object _gate = new();

    private readonly Dictionary<
        AkburaTextBufferContext,
        ImmutableArray<AkburaDiagnosticTableEntry>> _documents = new();

    private readonly List<ITableDataSink> _sinks = new();

    private AkburaDiagnosticTableEntriesSnapshot _snapshot =
        AkburaDiagnosticTableEntriesSnapshot.Empty;

    private int _version;

    [ImportingConstructor]
    public AkburaDiagnosticTableDataSource(
        ITableManagerProvider tableManagerProvider)
    {
        if (tableManagerProvider == null)
        {
            throw new ArgumentNullException(
                nameof(tableManagerProvider));
        }

        var manager = tableManagerProvider.GetTableManager(
            StandardTables.ErrorsTable);

        manager.AddSource(this, s_columns);
    }

    public string SourceTypeIdentifier =>
        StandardTableDataSources.ErrorTableDataSource;

    public string Identifier => SourceIdentifier;

    public string DisplayName => "Akbura";

    public IDisposable Subscribe(ITableDataSink sink)
    {
        if (sink == null)
        {
            throw new ArgumentNullException(nameof(sink));
        }

        lock (_gate)
        {
            _sinks.Add(sink);
            sink.AddSnapshot(
                _snapshot,
                true);
            sink.IsStable = true;
        }

        return new Subscription(this, sink);
    }

    internal void Update(
        AkburaTextBufferContext bufferContext,
        ITextSnapshot requestedSnapshot)
    {
        if (!bufferContext.TryGetPublishedClassificationState(
                requestedSnapshot,
                out var state))
        {
            return;
        }

        var projectName = state is AkburaParsedBufferState parsedState
            ? parsedState.Project.CSharpCompilation.AssemblyName ??
                string.Empty
            : GetPublishedProjectName(bufferContext);

        var entries = CreateEntries(
            state,
            bufferContext.FilePath,
            projectName);

        lock (_gate)
        {
            _documents[bufferContext] = entries;
            PublishSnapshotLocked();
        }
    }

    internal void Remove(
        AkburaTextBufferContext bufferContext)
    {
        lock (_gate)
        {
            if (_documents.Remove(bufferContext))
            {
                PublishSnapshotLocked();
            }
        }
    }

    private static string GetPublishedProjectName(
        AkburaTextBufferContext bufferContext)
    {
        return bufferContext.TryGetLatestDocumentContext(
                out var context,
                out _)
            ? context.Project.CSharpCompilation.AssemblyName ??
                string.Empty
            : string.Empty;
    }

    private static ImmutableArray<AkburaDiagnosticTableEntry>
        CreateEntries(
            AkburaClassifiedBufferState state,
            string filePath,
            string projectName)
    {
        if (state.Diagnostics.IsDefaultOrEmpty)
        {
            return [];
        }

        using var builder = ImmutableArrayBuilder<
            AkburaDiagnosticTableEntry>.Rent();

        foreach (var diagnostic in state.Diagnostics)
        {
            if (diagnostic.Severity ==
                AkburaDiagnosticSeverity.Hidden)
            {
                continue;
            }

            var position = Math.Max(
                0,
                Math.Min(
                    diagnostic.Span.Start,
                    state.Text.Length));
            var line = state.Text.Lines.GetLineFromPosition(
                position);

            builder.Add(new AkburaDiagnosticTableEntry(
                filePath,
                projectName,
                diagnostic,
                line.LineNumber,
                position - line.Start,
                state.Text.ToString(line.Span)));
        }

        return builder.ToImmutable();
    }

    private void PublishSnapshotLocked()
    {
        var entries = _documents.Values
            .SelectMany(static document => document)
            .OrderBy(static entry => entry.FilePath,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.Line)
            .ThenBy(static entry => entry.Column)
            .ThenBy(static entry => entry.Code,
                StringComparer.Ordinal)
            .ToImmutableArray();

        var previous = _snapshot;
        var current = new AkburaDiagnosticTableEntriesSnapshot(
            ++_version,
            entries);
        _snapshot = current;

        foreach (var sink in _sinks)
        {
            sink.ReplaceSnapshot(previous, current);
            sink.IsStable = true;
        }
    }

    private void Unsubscribe(ITableDataSink sink)
    {
        lock (_gate)
        {
            if (_sinks.Remove(sink))
            {
                sink.RemoveSnapshot(_snapshot);
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private AkburaDiagnosticTableDataSource? _source;

        private readonly ITableDataSink _sink;

        public Subscription(
            AkburaDiagnosticTableDataSource source,
            ITableDataSink sink)
        {
            _source = source;
            _sink = sink;
        }

        public void Dispose()
        {
            Interlocked.Exchange(
                    ref _source,
                    null)?
                .Unsubscribe(_sink);
        }
    }
}

internal sealed class AkburaDiagnosticTableEntriesSnapshot :
    ITableEntriesSnapshot
{
    public static readonly AkburaDiagnosticTableEntriesSnapshot Empty =
        new(
            versionNumber: 0,
            ImmutableArray<AkburaDiagnosticTableEntry>.Empty);

    private readonly ImmutableArray<AkburaDiagnosticTableEntry> _entries;

    public AkburaDiagnosticTableEntriesSnapshot(
        int versionNumber,
        ImmutableArray<AkburaDiagnosticTableEntry> entries)
    {
        VersionNumber = versionNumber;
        _entries = entries;
    }

    public int Count => _entries.Length;

    public int VersionNumber { get; }

    public void StartCaching()
    {
    }

    public void StopCaching()
    {
    }

    public bool TryGetValue(
        int index,
        string keyName,
        out object? content)
    {
        if ((uint)index >= (uint)_entries.Length)
        {
            content = null;
            return false;
        }

        var entry = _entries[index];

        switch (keyName)
        {
            case StandardTableKeyNames.ErrorSeverity:
                content = GetErrorCategory(entry.Severity);
                return true;

            case StandardTableKeyNames.ErrorCode:
                content = entry.Code;
                return true;

            case StandardTableKeyNames.Text:
                content = entry.Message;
                return true;

            case StandardTableKeyNames.DocumentName:
                content = entry.FilePath;
                return true;

            case StandardTableKeyNames.ProjectName:
                content = entry.ProjectName;
                return entry.ProjectName.Length != 0;

            case StandardTableKeyNames.Line:
                content = entry.Line;
                return true;

            case StandardTableKeyNames.Column:
                content = entry.Column;
                return true;

            case StandardTableKeyNames.LineText:
                content = entry.LineText;
                return true;

            case StandardTableKeyNames.BuildTool:
                content = "Akbura";
                return true;

            case StandardTableKeyNames.ErrorCategory:
                content = "Akbura";
                return true;

            case StandardTableKeyNames.ErrorSource:
                content = ErrorSource.Other;
                return true;

            case StandardTableKeyNames.ErrorRank:
                content = entry.Code.StartsWith(
                    "AKBURA_SEMANTIC_",
                    StringComparison.Ordinal)
                    ? ErrorRank.Semantic
                    : ErrorRank.Syntactic;
                return true;

            case StandardTableKeyNames.IsActiveContext:
                content = true;
                return true;

            default:
                content = null;
                return false;
        }
    }

    public int IndexOf(
        int currentIndex,
        ITableEntriesSnapshot newSnapshot)
    {
        if ((uint)currentIndex >= (uint)_entries.Length ||
            newSnapshot is not AkburaDiagnosticTableEntriesSnapshot next)
        {
            return -1;
        }

        var current = _entries[currentIndex];
        for (var index = 0;
             index < next._entries.Length;
             index++)
        {
            if (next._entries[index] == current)
            {
                return index;
            }
        }

        return -1;
    }

    public void Dispose()
    {
    }

    private static __VSERRORCATEGORY GetErrorCategory(
        AkburaDiagnosticSeverity severity)
    {
        return severity switch
        {
            AkburaDiagnosticSeverity.Error =>
                __VSERRORCATEGORY.EC_ERROR,
            AkburaDiagnosticSeverity.Warning =>
                __VSERRORCATEGORY.EC_WARNING,
            _ => __VSERRORCATEGORY.EC_MESSAGE,
        };
    }
}

internal readonly struct AkburaDiagnosticTableEntry :
    IEquatable<AkburaDiagnosticTableEntry>
{
    public AkburaDiagnosticTableEntry(
        string filePath,
        string projectName,
        AkburaDiagnosticSpan diagnostic,
        int line,
        int column,
        string lineText)
    {
        FilePath = filePath;
        ProjectName = projectName;
        Diagnostic = diagnostic;
        Line = line;
        Column = column;
        LineText = lineText;
    }

    public string FilePath { get; }

    public string ProjectName { get; }

    public AkburaDiagnosticSpan Diagnostic { get; }

    public int Line { get; }

    public int Column { get; }

    public string LineText { get; }

    public string Code => Diagnostic.Code;

    public string Message => Diagnostic.Message;

    public AkburaDiagnosticSeverity Severity => Diagnostic.Severity;

    public bool Equals(AkburaDiagnosticTableEntry other)
    {
        return StringComparer.OrdinalIgnoreCase.Equals(
                   FilePath,
                   other.FilePath) &&
               string.Equals(
                   ProjectName,
                   other.ProjectName,
                   StringComparison.Ordinal) &&
               Diagnostic.Equals(other.Diagnostic) &&
               Line == other.Line &&
               Column == other.Column &&
               string.Equals(
                   LineText,
                   other.LineText,
                   StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is AkburaDiagnosticTableEntry other &&
               Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = StringComparer.OrdinalIgnoreCase
                .GetHashCode(FilePath);
            hash = (hash * 397) ^ ProjectName.GetHashCode();
            hash = (hash * 397) ^ Diagnostic.GetHashCode();
            hash = (hash * 397) ^ Line;
            hash = (hash * 397) ^ Column;
            hash = (hash * 397) ^ LineText.GetHashCode();
            return hash;
        }
    }

    public static bool operator ==(
        AkburaDiagnosticTableEntry left,
        AkburaDiagnosticTableEntry right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(
        AkburaDiagnosticTableEntry left,
        AkburaDiagnosticTableEntry right)
    {
        return !left.Equals(right);
    }
}
