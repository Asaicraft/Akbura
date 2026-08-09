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

    public Task<AkburaSyntacticDocument> GetSyntacticDocumentAsync(ITextSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(
                nameof(snapshot));
        }

        var cache = snapshot.TextBuffer.Properties
            .GetOrCreateSingletonProperty(
                static () => new SnapshotCache());

        return cache.GetOrCreateAsync(
            snapshot,
            CreateDocumentTaskAsync);
    }

    private Task<AkburaSyntacticDocument> CreateDocumentTaskAsync(ITextSnapshot snapshot)
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

        return "untitled.akbura";
    }

    private sealed class SnapshotCache
    {
        private readonly object _gate = new();

        private readonly ConditionalWeakTable<
            ITextSnapshot,
            Task<AkburaSyntacticDocument>> _documents = new();

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
