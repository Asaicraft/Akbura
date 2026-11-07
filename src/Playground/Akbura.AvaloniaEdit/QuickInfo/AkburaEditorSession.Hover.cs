using Akbura.AvaloniaEdit.QuickInfo;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Akbura.AvaloniaEdit;

public sealed partial class AkburaEditorSession
{
    private CancellationTokenSource? _hoverCancellation;
    private long _hoverSequence;
    private int? _hoverPosition;
    private object? _previousToolTip;
    private TextBlock? _hoverTip;
    public AkburaEditorHover? Hover
    {
        get; private set;
    }

    private void AttachHover()
    {
        _previousToolTip = ToolTip.GetTip(_editor);
        _editor.PointerMoved += PointerMoved;
        _editor.PointerExited += PointerExited;
    }

    private void DetachHover()
    {
        _editor.PointerMoved -= PointerMoved;
        _editor.PointerExited -= PointerExited;
        if (ReferenceEquals(ToolTip.GetTip(_editor), _hoverTip)) ToolTip.SetTip(_editor, _previousToolTip);
    }

    private void DismissHover()
    {
        _hoverCancellation?.Cancel();
        _hoverSequence++;
        _hoverPosition = null;
        Hover = null;
        if (ReferenceEquals(ToolTip.GetTip(_editor), _hoverTip)) ToolTip.SetIsOpen(_editor, false);
    }

    private void PointerExited(object? sender, PointerEventArgs e) => DismissHover();

    private void PointerMoved(object? sender, PointerEventArgs e)
    {
        var position = _editor.GetPositionFromPoint(e.GetPosition(_editor));
        var offset = position == null ? (int?)null : _document.GetOffset(position.Value.Location);
        if (_hoverPosition == offset) return;
        DismissHover();
        if (offset is not { } value) return;
        _hoverPosition = value;
        _ = ShowHoverAsync(value);
    }

    private async Task ShowHoverAsync(int position)
    {
        var result = await GetHoverAsync(position, _options.HoverDelay, CancellationToken.None);
        if (result == null || _disposed || _hoverPosition != position) return;
        var parts = new[] { result.Signature }.Concat(result.Details)
            .Concat(result.Diagnostics.Select(issue => $"{issue.Code}: {issue.Message}"));
        _hoverTip = new TextBlock { Text = string.Join(Environment.NewLine, parts.Where(part => !string.IsNullOrEmpty(part))),
            MaxWidth = 600, TextWrapping = TextWrapping.Wrap };
        ToolTip.SetTip(_editor, _hoverTip);
        ToolTip.SetIsOpen(_editor, true);
    }

    public Task<AkburaEditorHover?> GetHoverAsync(int position, CancellationToken cancellationToken = default) =>
        GetHoverAsync(position, TimeSpan.Zero, cancellationToken);

    private async Task<AkburaEditorHover?> GetHoverAsync(int position, TimeSpan delay, CancellationToken cancellationToken)
    {
        var snapshot = Capture(position);
        if (position < 0 || position > snapshot.Text.Length) throw new ArgumentOutOfRangeException(nameof(position));
        _hoverCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _hoverCancellation = cancellation;
        var sequence = ++_hoverSequence;
        AkburaDocumentContext? context = null;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellation.Token);
            context = await SynchronizeAsync(snapshot, cancellation.Token);
            var info = await _options.Scheduler.RunAsync(async token =>
            {
                var syntax = AkburaSyntacticDocument.Parse(snapshot.Text, context.Document.FilePath, token);
                var projected = await _workspace.LanguageServices.ProjectedCSharp.GetQuickInfoAsync(
                    syntax, context, position, token).ConfigureAwait(false);
                return projected ?? _workspace.LanguageServices.QuickInfo.GetQuickInfo(context, position, token);
            }, cancellation.Token);
            if (cancellation.IsCancellationRequested || sequence != _hoverSequence || !IsCurrent(snapshot, context)) return null;
            var diagnostics = Diagnostics.Where(issue => issue.Span.Contains(position) ||
                issue.Span.Length == 0 && issue.Span.Start == position).ToImmutableArray();
            if (info == null && diagnostics.IsEmpty) return null;
            Hover = new(info?.SourceSpan ?? diagnostics[0].Span, info?.Signature ?? "", info?.Details ?? [], diagnostics);
            return Hover;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !IsCurrent(snapshot)) { return null; }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && sequence == _hoverSequence)
                ReportFailure("Quick info", error, snapshot, context);
            return null;
        }
        finally { if (ReferenceEquals(_hoverCancellation, cancellation)) _hoverCancellation = null; }
    }
}
