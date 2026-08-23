using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Akbura.VisualStudio.Indentation;

internal sealed class AkburaSmartIndent : ISmartIndent
{
    private static readonly TimeSpan ParseWaitTimeout =
        TimeSpan.FromMilliseconds(100);

    private readonly ITextView _textView;

    private readonly AkburaParserService _parserService;

    public AkburaSmartIndent(
        ITextView textView,
        AkburaParserService parserService)
    {
        _textView = textView ??
            throw new ArgumentNullException(
                nameof(textView));
        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));
    }

    public int? GetDesiredIndentation(
        ITextSnapshotLine line)
    {
        if (line == null)
        {
            throw new ArgumentNullException(
                nameof(line));
        }

        var task = _parserService
            .GetSyntacticDocumentAsync(
                line.Snapshot);

#pragma warning disable VSTHRD002 // Deliberately bounded to 100 ms for synchronous editor API.
        try
        {
            if (!task.IsCompleted &&
                !task.Wait(ParseWaitTimeout))
            {
                return null;
            }

            if (task.Status != TaskStatus.RanToCompletion)
            {
                return null;
            }

            var indentationLevel = task.Result
                .GetDesiredIndentationLevel(
                    line.LineNumber);
            var indentationSize = _textView.Options
                .GetOptionValue(
                    DefaultOptions.IndentSizeOptionId);

            return indentationLevel * indentationSize;
        }
        catch (AggregateException)
        {
            return null;
        }
#pragma warning restore VSTHRD002
    }

    public void Dispose()
    {
    }
}
