using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

internal sealed class AkburaDynamicDelimiterSessionManager : IDisposable
{
    private readonly ITextView _textView;
    private readonly ITextBuffer _subjectBuffer;
    private readonly List<AkburaDynamicDelimiterSession> _sessions = new();
    private int _commandEditDepth;
    private bool _disposed;

    private AkburaDynamicDelimiterSessionManager(
        ITextView textView,
        ITextBuffer subjectBuffer)
    {
        _textView = textView;
        _subjectBuffer = subjectBuffer;
        _textView.Closed += OnTextViewClosed;
        _textView.Caret.PositionChanged += OnCaretPositionChanged;
        _subjectBuffer.Changed += OnBufferChanged;
    }

    public static AkburaDynamicDelimiterSessionManager GetOrCreate(
        ITextView textView,
        ITextBuffer subjectBuffer)
    {
        return textView.Properties.GetOrCreateSingletonProperty(
            () => new AkburaDynamicDelimiterSessionManager(
                textView,
                subjectBuffer));
    }

    public IDisposable BeginCommandEdit()
    {
        if (_disposed)
        {
            return EmptyScope.Instance;
        }

        _commandEditDepth++;
        return new CommandEditScope(this);
    }

    public void SetSession(AkburaDynamicDelimiterSession session)
    {
        RemoveSession(session.Kind);
        _sessions.Add(session);
    }

    public bool TryGetSession(
        AkburaDynamicDelimiterKind kind,
        out AkburaDynamicDelimiterSession session)
    {
        for (var index = _sessions.Count - 1;
             index >= 0;
             index--)
        {
            var candidate = _sessions[index];
            if (candidate.Kind != kind)
            {
                continue;
            }

            if (!candidate.TryGetSpans(
                    _subjectBuffer.CurrentSnapshot,
                    out _,
                    out _))
            {
                _sessions.RemoveAt(index);
                break;
            }

            session = candidate;
            return true;
        }

        session = null!;
        return false;
    }

    public void RemoveSession(AkburaDynamicDelimiterKind kind)
    {
        for (var index = _sessions.Count - 1;
             index >= 0;
             index--)
        {
            if (_sessions[index].Kind == kind)
            {
                _sessions.RemoveAt(index);
            }
        }
    }

    public void Clear()
    {
        _sessions.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
        _textView.Closed -= OnTextViewClosed;
        _textView.Caret.PositionChanged -= OnCaretPositionChanged;
        _subjectBuffer.Changed -= OnBufferChanged;
    }

    private void OnTextViewClosed(object? sender, EventArgs eventArgs)
    {
        Dispose();
    }

    private void OnCaretPositionChanged(
        object? sender,
        CaretPositionChangedEventArgs eventArgs)
    {
        if (_commandEditDepth != 0 || _disposed)
        {
            return;
        }

        var point = eventArgs.NewPosition.BufferPosition;
        if (!ReferenceEquals(
                point.Snapshot.TextBuffer,
                _subjectBuffer))
        {
            Clear();
            return;
        }

        for (var index = _sessions.Count - 1;
             index >= 0;
             index--)
        {
            if (!_sessions[index].ContainsCaret(
                    _subjectBuffer.CurrentSnapshot,
                    point.Position))
            {
                _sessions.RemoveAt(index);
            }
        }
    }

    private void OnBufferChanged(
        object? sender,
        TextContentChangedEventArgs eventArgs)
    {
        if (_commandEditDepth == 0)
        {
            Clear();
        }
    }

    private void EndCommandEdit()
    {
        if (_commandEditDepth == 0)
        {
            return;
        }

        _commandEditDepth--;
        if (_commandEditDepth != 0)
        {
            return;
        }

        var snapshot = _subjectBuffer.CurrentSnapshot;
        for (var index = _sessions.Count - 1;
             index >= 0;
             index--)
        {
            if (!_sessions[index].TryGetSpans(
                    snapshot,
                    out _,
                    out _))
            {
                _sessions.RemoveAt(index);
            }
        }
    }

    private sealed class CommandEditScope : IDisposable
    {
        private AkburaDynamicDelimiterSessionManager? _owner;

        public CommandEditScope(
            AkburaDynamicDelimiterSessionManager owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?
                .EndCommandEdit();
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
