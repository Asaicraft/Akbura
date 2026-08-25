using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;

namespace Akbura.VisualStudio.Editor;

/// <summary>
/// Shares one syntax-only parse task between all editor features that request
/// the same immutable Visual Studio text snapshot.
/// </summary>
[Export]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class AkburaParserService
{
    private readonly ITextDocumentFactoryService _textDocumentFactory;

    [ImportingConstructor]
    public AkburaParserService(ITextDocumentFactoryService textDocumentFactory)
    {
        _textDocumentFactory = textDocumentFactory ??
            throw new ArgumentNullException(
                nameof(textDocumentFactory));
    }

    public bool TryGetCachedSyntacticDocument(
        ITextSnapshot snapshot,
        out AkburaSyntacticDocument document)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return GetSnapshotCache(snapshot.TextBuffer)
            .TryGetCompleted(snapshot, out document);
    }

    public AkburaSyntacticDocument GetSyntacticDocument(
        ITextSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        var cache = GetSnapshotCache(snapshot.TextBuffer);
        if (cache.TryGetCompleted(snapshot, out var document))
        {
            return document;
        }

        cancellationToken.ThrowIfCancellationRequested();
        document = AkburaSyntacticDocument.Parse(
            snapshot.AsText(),
            GetFilePath(snapshot.TextBuffer),
            cancellationToken);
        cache.StoreCompleted(snapshot, document);
        return document;
    }

    public Task<AkburaSyntacticDocument> GetSyntacticDocumentAsync(
        ITextSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(
                nameof(snapshot));
        }

        return GetSnapshotCache(snapshot.TextBuffer)
            .GetOrCreateAsync(
                snapshot,
                CreateDocumentTaskAsync);
    }

    private static SnapshotCache GetSnapshotCache(ITextBuffer textBuffer)
    {
        return textBuffer.Properties.GetOrCreateSingletonProperty(
            static () => new SnapshotCache());
    }

    private Task<AkburaSyntacticDocument> CreateDocumentTaskAsync(
        ITextSnapshot snapshot)
    {
        var filePath = GetFilePath(snapshot.TextBuffer);

        return Task.Run(
            () => AkburaSyntacticDocument.Parse(
                snapshot.AsText(),
                filePath));
    }

    private string GetFilePath(ITextBuffer textBuffer)
    {
        if (_textDocumentFactory.TryGetTextDocument(
                textBuffer,
                out var document) &&
            !string.IsNullOrWhiteSpace(
                document.FilePath))
        {
            return document.FilePath;
        }

        return AkburaEditorDocumentKindFacts.GetUntitledFileName(
            AkburaEditorDocumentKindFacts.GetOrDefault(textBuffer));
    }

    private sealed class SnapshotCache
    {
        private readonly object _gate = new();

        private readonly ConditionalWeakTable<
            ITextSnapshot,
            Task<AkburaSyntacticDocument>> _documents = new();

        public bool TryGetCompleted(
            ITextSnapshot snapshot,
            out AkburaSyntacticDocument document)
        {
            lock (_gate)
            {
                if (_documents.TryGetValue(snapshot, out var task) &&
                    task.Status == TaskStatus.RanToCompletion)
                {
#pragma warning disable VSTHRD002 // Completion was checked immediately above.
                    document = task.GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                    return true;
                }
            }

            document = null!;
            return false;
        }

        public void StoreCompleted(
            ITextSnapshot snapshot,
            AkburaSyntacticDocument document)
        {
            lock (_gate)
            {
                if (!_documents.TryGetValue(snapshot, out _))
                {
                    _documents.Add(
                        snapshot,
                        Task.FromResult(document));
                }
            }
        }

        public Task<AkburaSyntacticDocument> GetOrCreateAsync(
            ITextSnapshot snapshot,
            ConditionalWeakTable<
                ITextSnapshot,
                Task<AkburaSyntacticDocument>>
                .CreateValueCallback valueFactory)
        {
            lock (_gate)
            {
                return _documents.GetValue(
                    snapshot,
                    valueFactory);
            }
        }
    }
}
