using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.VisualStudio.Editor;

/// <summary>
/// Synchronizes one Visual Studio text buffer with one Akbura document.
///
/// Completion, Quick Info, diagnostics and navigation services should
/// reuse this instance through ITextBuffer.Properties.
/// </summary>
internal sealed class AkburaTextBufferContext
{
    private readonly object _gate = new();
    private readonly ITextBuffer _textBuffer;
    private readonly AkburaWorkspace _workspace;
    private readonly Uri _uri;
    private readonly Encoding? _encoding;

    private ITextSnapshot _textSnapshot;
    private AkburaDocumentSnapshot _document;

    public AkburaTextBufferContext(
        ITextBuffer textBuffer,
        ITextDocumentFactoryService textDocumentFactory,
        AkburaWorkspace workspace)
    {
        _textBuffer = textBuffer ?? throw new ArgumentNullException(nameof(textBuffer));

        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));

        if (textDocumentFactory == null)
        {
            throw new ArgumentNullException(nameof(textDocumentFactory));
        }

        if (textDocumentFactory.TryGetTextDocument(
                textBuffer,
                out var textDocument) &&
            !string.IsNullOrWhiteSpace(textDocument.FilePath))
        {
            _uri = new Uri(Path.GetFullPath(textDocument.FilePath));

            _encoding = textDocument.Encoding;
        }
        else
        {
            _uri = new Uri($"untitled://akbura/{Guid.NewGuid():N}.akbura");
        }

        _textSnapshot = textBuffer.CurrentSnapshot;

        _document = OpenOrChangeDocument(
            _textSnapshot,
            changes: null);

        _textBuffer.ChangedLowPriority +=
            OnTextBufferChangedLowPriority;
    }

    public event EventHandler<AkburaBufferChangedEventArgs>? Changed;

    public bool TryGetDocument(ITextSnapshot snapshot, out AkburaDocumentSnapshot document)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (!ReferenceEquals(snapshot.TextBuffer, _textBuffer))
        {
            throw new ArgumentException(
                "The snapshot belongs to another text buffer.",
                nameof(snapshot));
        }

        lock (_gate)
        {
            if (ReferenceEquals(snapshot, _textSnapshot))
            {
                document = _document;
                return true;
            }

            if (snapshot.Version.VersionNumber <
                _textSnapshot.Version.VersionNumber)
            {
                document = null!;
                return false;
            }

            _document = OpenOrChangeDocument(
                snapshot,
                changes: null);

            _textSnapshot = snapshot;
            document = _document;

            return true;
        }
    }

    private void OnTextBufferChangedLowPriority(
        object sender,
        TextContentChangedEventArgs e)
    {
        TextChangeRange[]? changeRanges = null;

        lock (_gate)
        {
            if (ReferenceEquals(_textSnapshot, e.Before))
            {
                changeRanges = [.. e.Changes
                    .Select(static change =>
                        new TextChangeRange(
                            new TextSpan(
                                change.OldPosition,
                                change.OldLength),
                            change.NewLength))];
            }

            _document = OpenOrChangeDocument(
                e.After,
                changeRanges);

            _textSnapshot = e.After;
        }

        Changed?.Invoke(
            this,
            new AkburaBufferChangedEventArgs(
                new SnapshotSpan(
                    e.After,
                    0,
                    e.After.Length)));
    }

    private AkburaDocumentSnapshot OpenOrChangeDocument(
        ITextSnapshot snapshot,
        IReadOnlyList<TextChangeRange>? changes)
    {
        var sourceText = SourceText.From(
            snapshot.GetText(),
            _encoding);

        return _workspace.OpenOrChangeDocument(
            _uri,
            sourceText,
            changes);
    }
}