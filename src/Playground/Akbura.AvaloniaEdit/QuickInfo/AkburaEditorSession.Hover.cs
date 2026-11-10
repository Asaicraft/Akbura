using Akbura.Pools;
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

    private static string FormatHover(AkburaEditorHover result)
    {
        using var parts = ImmutableArrayBuilder<string>.Rent();
        if (!string.IsNullOrEmpty(result.Signature))
        {
            parts.Add(result.Signature);
        }
        foreach (var detail in result.Details)
        {
            if (!string.IsNullOrEmpty(detail))
            {
                parts.Add(detail);
            }
        }
        foreach (var issue in result.Diagnostics)
        {
            parts.Add($"{issue.Code}: {issue.Message}");
        }
        return string.Join(Environment.NewLine, parts.AsEnumerable());
    }

    private void PointerExited(object? sender, PointerEventArgs e) => DismissHover();

    private void PointerMoved(object? sender, PointerEventArgs e)
    {
        var position = _editor.GetPositionFromPoint(e.GetPosition(_editor));
        var offset = position == null ? (int?)null : _document.GetOffset(position.Value.Location);
        if (_hoverPosition == offset)
        {
            return;
        }

        DismissHover();
        if (offset is not { } value)
        {
            return;
        }

        _hoverPosition = value;
        _ = ShowHoverAsync(value);
    }

    private async Task ShowHoverAsync(int position)
    {
        var result = await GetHoverAsync(position, _options.HoverDelay, CancellationToken.None);
        if (result == null || _disposed || _hoverPosition != position)
        {
            return;
        }

        _hoverTip = new TextBlock
        {
            Text = FormatHover(result),
            MaxWidth = 600,
            TextWrapping = TextWrapping.Wrap,
        };
        ToolTip.SetTip(_editor, _hoverTip);
        ToolTip.SetIsOpen(_editor, true);
    }

    public Task<AkburaEditorHover?> GetHoverAsync(int position, CancellationToken cancellationToken = default) =>
        GetHoverAsync(position, TimeSpan.Zero, cancellationToken);

    private async Task<AkburaEditorHover?> GetHoverAsync(int position, TimeSpan delay, CancellationToken cancellationToken)
    {
        var snapshot = Capture(position);
        if (position < 0 || position > snapshot.Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        _hoverCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _hoverCancellation = cancellation;
        var sequence = ++_hoverSequence;
        AkburaDocumentContext? context = null;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellation.Token);
            }

            context = await SynchronizeAsync(snapshot, cancellation.Token, "hover-sync");
            var syntax = await GetSyntaxAsync(snapshot, context, cancellation.Token);
            var info = await _services.RunAsync("hover", 0, async token =>
            {
                var projected = await _workspace.LanguageServices.ProjectedCSharp.GetQuickInfoAsync(
                    syntax, context, position, token).ConfigureAwait(false);
                return projected ?? _workspace.LanguageServices.QuickInfo.GetQuickInfo(context, position, token);
            }, cancellation.Token);

            if (cancellation.IsCancellationRequested || sequence != _hoverSequence || !IsCurrent(snapshot, context))
            {
                return null;
            }

            using var diagnosticBuilder = ImmutableArrayBuilder<AkburaDiagnosticSpan>.Rent();
            foreach (var issue in Diagnostics)
            {
                if (issue.Span.Contains(position) || issue.Span.Length == 0 && issue.Span.Start == position)
                {
                    diagnosticBuilder.Add(issue);
                }
            }

            var diagnostics = diagnosticBuilder.ToImmutable();
            if (info == null && diagnostics.IsEmpty)
            {
                return null;
            }

            Hover = new(info?.SourceSpan ?? diagnostics[0].Span, info?.Signature ?? "", info?.Details ?? [], diagnostics);
            return Hover;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !IsCurrent(snapshot))
        {
            return null;
        }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && sequence == _hoverSequence)
            {
                ReportFailure("Quick info", error, snapshot, context);
            }

            return null;
        }
        finally
        {
            if (ReferenceEquals(_hoverCancellation, cancellation))
            {
                _hoverCancellation = null;
            }
        }
    }
}
