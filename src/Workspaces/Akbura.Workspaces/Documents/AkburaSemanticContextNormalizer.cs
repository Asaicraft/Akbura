using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Documents;

internal enum AkburaSemanticContextMode
{
    Exact,
    Rebased,
    Unavailable,
}

internal enum AkburaSemanticContextFailureReason
{
    None,
    NoSemanticContext,
    DocumentMismatch,
    ProjectMismatch,
    RebaseFailed,
}

internal readonly struct AkburaSemanticContextNormalizationResult
{
    public AkburaSemanticContextNormalizationResult(AkburaDocumentContext? context, AkburaSemanticContextMode mode, AkburaSemanticContextFailureReason failureReason, bool textMatches)
    {
        Context = context;
        Mode = mode;
        FailureReason = failureReason;
        TextMatches = textMatches;
    }

    public AkburaDocumentContext? Context { get; }

    public AkburaSemanticContextMode Mode { get; }

    public AkburaSemanticContextFailureReason FailureReason { get; }

    public bool TextMatches { get; }
}

internal static class AkburaSemanticContextNormalizer
{
    internal const int MaximumCachedContexts = 8;

    private static readonly StringComparison PathComparison = Path.DirectorySeparatorChar == '\\'
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static readonly object CacheGate = new();
    private static readonly LinkedList<CacheEntry> Cache = new();

    public static AkburaSemanticContextNormalizationResult Normalize(AkburaSyntacticDocument document, AkburaDocumentContext? semanticContext, CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (semanticContext == null)
        {
            return Unavailable(AkburaSemanticContextFailureReason.NoSemanticContext);
        }

        if (!IsCoherent(semanticContext))
        {
            return Unavailable(AkburaSemanticContextFailureReason.ProjectMismatch);
        }

        var semanticDocument = semanticContext.Document;
        if (semanticDocument.SyntaxTree.Kind != document.SyntaxTree.Kind || !FilePathsEqual(semanticDocument.FilePath, document.FilePath))
        {
            return Unavailable(AkburaSemanticContextFailureReason.DocumentMismatch);
        }

        if (ReferenceEquals(semanticDocument.Text, document.Text))
        {
            return new AkburaSemanticContextNormalizationResult(semanticContext, AkburaSemanticContextMode.Exact, AkburaSemanticContextFailureReason.None, textMatches: true);
        }

        if (TryGetCached(semanticContext, document.Text, out var cachedEntry))
        {
            return GetOrCreateRebasedResult(cachedEntry, document, semanticContext, cancellationToken);
        }

        if (semanticDocument.Text.ContentEquals(document.Text))
        {
            return new AkburaSemanticContextNormalizationResult(semanticContext, AkburaSemanticContextMode.Exact, AkburaSemanticContextFailureReason.None, textMatches: true);
        }

        var entry = GetOrAddCacheEntry(semanticContext, document.Text);
        return GetOrCreateRebasedResult(entry, document, semanticContext, cancellationToken);
    }

    private static AkburaSemanticContextNormalizationResult Unavailable(AkburaSemanticContextFailureReason failureReason)
    {
        return new AkburaSemanticContextNormalizationResult(context: null, AkburaSemanticContextMode.Unavailable, failureReason, textMatches: false);
    }

    private static bool IsCoherent(AkburaDocumentContext context)
    {
        var document = context.Document;
        var project = context.Project;
        return document.ProjectId == project.Id && context.Solution.TryGetProject(project.Id, out var solutionProject) && ReferenceEquals(solutionProject, project) && project.TryGetDocument(document.Id, out var projectDocument) && ReferenceEquals(projectDocument, document);
    }

    private static bool FilePathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static AkburaSemanticContextNormalizationResult GetOrCreateRebasedResult(CacheEntry entry, AkburaSyntacticDocument document, AkburaDocumentContext semanticContext, CancellationToken cancellationToken)
    {
        try
        {
            return entry.GetOrCreateReserved(
                () => CreateRebasedResult(document, semanticContext, cancellationToken),
                cancellationToken);
        }
        catch
        {
            RemoveIfUninitialized(entry);
            throw;
        }
        finally
        {
            TouchAndTrim(entry);
        }
    }

    private static AkburaSemanticContextNormalizationResult CreateRebasedResult(AkburaSyntacticDocument document, AkburaDocumentContext semanticContext, CancellationToken cancellationToken)
    {
        try
        {
            var semanticDocument = semanticContext.Document;
            var syntaxTree = document.SyntaxTree;
            if (semanticDocument.SyntaxTree is AkcssSyntaxTree semanticAkcssTree)
            {
                syntaxTree = semanticAkcssTree.WithChangedText(
                    document.Text,
                    document.Text.GetChangeRanges(semanticDocument.Text),
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var currentDocument = new AkburaDocumentSnapshot(
                semanticDocument.Id,
                semanticDocument.ProjectId,
                semanticDocument.Uri,
                semanticDocument.FilePath,
                VersionStamp.Create(),
                document.Text,
                syntaxTree,
                isOpen: true);
            var currentProject = semanticContext.Project.ReplaceDocument(currentDocument);
            cancellationToken.ThrowIfCancellationRequested();
            var currentSolution = semanticContext.Solution.WithProject(currentProject);
            var currentContext = new AkburaDocumentContext(currentSolution, currentProject, currentDocument);
            return new AkburaSemanticContextNormalizationResult(currentContext, AkburaSemanticContextMode.Rebased, AkburaSemanticContextFailureReason.None, textMatches: false);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return Unavailable(AkburaSemanticContextFailureReason.RebaseFailed);
        }
    }

    private static bool TryGetCached(AkburaDocumentContext sourceContext, SourceText currentText, out CacheEntry entry)
    {
        lock (CacheGate)
        {
            var node = Cache.First;
            while (node != null)
            {
                if (node.Value.Matches(sourceContext, currentText))
                {
                    entry = node.Value;
                    entry.Reserve();
                    Cache.Remove(node);
                    Cache.AddFirst(node);
                    return true;
                }

                node = node.Next;
            }
        }

        entry = null!;
        return false;
    }

    private static CacheEntry GetOrAddCacheEntry(AkburaDocumentContext sourceContext, SourceText currentText)
    {
        lock (CacheGate)
        {
            var node = Cache.First;
            while (node != null)
            {
                if (node.Value.Matches(sourceContext, currentText))
                {
                    node.Value.Reserve();
                    Cache.Remove(node);
                    Cache.AddFirst(node);
                    return node.Value;
                }

                node = node.Next;
            }

            var entry = new CacheEntry(sourceContext, currentText);
            entry.Reserve();
            Cache.AddFirst(entry);
            return entry;
        }
    }

    private static void TouchAndTrim(CacheEntry entry)
    {
        lock (CacheGate)
        {
            var node = Cache.Find(entry);
            if (node != null)
            {
                Cache.Remove(node);
                Cache.AddFirst(node);
            }

            while (Cache.Count > MaximumCachedContexts)
            {
                var candidate = Cache.Last;
                while (candidate != null && (!candidate.Value.IsInitialized || ReferenceEquals(candidate.Value, entry)))
                {
                    candidate = candidate.Previous;
                }

                if (candidate == null)
                {
                    break;
                }

                Cache.Remove(candidate);
            }
        }
    }

    private static void RemoveIfUninitialized(CacheEntry entry)
    {
        if (!entry.CanRemove)
        {
            return;
        }

        lock (CacheGate)
        {
            if (entry.CanRemove)
            {
                Cache.Remove(entry);
            }
        }
    }

    private sealed class CacheEntry
    {
        private readonly object _gate = new();
        private AkburaSemanticContextNormalizationResult _result;
        private int _activeCallers;
        private int _isInitialized;

        public CacheEntry(AkburaDocumentContext sourceContext, SourceText currentText)
        {
            SourceSolution = sourceContext.Solution;
            SourceProject = sourceContext.Project;
            SourceDocument = sourceContext.Document;
            CurrentText = currentText;
        }

        public AkburaSolutionSnapshot SourceSolution { get; }

        public AkburaProjectSnapshot SourceProject { get; }

        public AkburaDocumentSnapshot SourceDocument { get; }

        public SourceText CurrentText { get; }

        public bool IsInitialized => Volatile.Read(ref _isInitialized) != 0;

        public bool CanRemove => !IsInitialized && Volatile.Read(ref _activeCallers) == 0;

        public void Reserve()
        {
            Interlocked.Increment(ref _activeCallers);
        }

        public AkburaSemanticContextNormalizationResult GetOrCreateReserved(Func<AkburaSemanticContextNormalizationResult> valueFactory, CancellationToken cancellationToken)
        {
            try
            {
                lock (_gate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsInitialized)
                    {
                        return _result;
                    }

                    var result = valueFactory();
                    _result = result;
                    Volatile.Write(ref _isInitialized, 1);
                    cancellationToken.ThrowIfCancellationRequested();
                    return result;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeCallers);
            }
        }

        public bool Matches(AkburaDocumentContext sourceContext, SourceText currentText)
        {
            return ReferenceEquals(SourceSolution, sourceContext.Solution) && ReferenceEquals(SourceProject, sourceContext.Project) && ReferenceEquals(SourceDocument, sourceContext.Document) && ReferenceEquals(CurrentText, currentText);
        }
    }
}
