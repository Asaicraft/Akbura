using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using RoslynTextSpan = Microsoft.CodeAnalysis.Text.TextSpan;

namespace Akbura.VisualStudio.Classification;

internal sealed class AkburaClassifier : IClassifier
{
    private readonly AkburaTextBufferContext _bufferContext;
    private readonly IAkburaClassificationService
        _classificationService;
    private readonly AkburaClassificationTypeMap _typeMap;

    public AkburaClassifier(
        AkburaTextBufferContext bufferContext,
        IAkburaClassificationService classificationService,
        AkburaClassificationTypeMap typeMap)
    {
        _bufferContext = bufferContext ??
            throw new ArgumentNullException(nameof(bufferContext));

        _classificationService = classificationService ??
            throw new ArgumentNullException(
                nameof(classificationService));

        _typeMap = typeMap ??
            throw new ArgumentNullException(nameof(typeMap));

        _bufferContext.Changed += OnBufferContextChanged;
    }

    public event EventHandler<ClassificationChangedEventArgs>?
        ClassificationChanged;

    public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
    {
        if (!_bufferContext.TryGetDocument(
                span.Snapshot,
                out var document))
        {
            return Array.Empty<ClassificationSpan>();
        }

        var requestedSpan = new RoslynTextSpan(
            span.Start.Position,
            span.Length);

        var classifications =
            _classificationService.GetClassifications(
                document,
                requestedSpan);

        if (classifications.IsDefaultOrEmpty)
        {
            return Array.Empty<ClassificationSpan>();
        }

        var result = new List<ClassificationSpan>(
            classifications.Length);

        foreach (var classification in classifications)
        {
            var start = Math.Max(
                requestedSpan.Start,
                classification.Span.Start);

            var end = Math.Min(
                requestedSpan.End,
                classification.Span.End);

            if (start >= end ||
                start < 0 ||
                end > span.Snapshot.Length)
            {
                continue;
            }

            var visualStudioSpan = new SnapshotSpan(
                span.Snapshot,
                Span.FromBounds(start, end));

            result.Add(new ClassificationSpan(
                visualStudioSpan,
                _typeMap.Get(classification.Kind)));
        }

        return result;
    }

    private void OnBufferContextChanged(
        object sender,
        AkburaBufferChangedEventArgs e)
    {
        ClassificationChanged?.Invoke(
            this,
            new ClassificationChangedEventArgs(e.Span));
    }
}