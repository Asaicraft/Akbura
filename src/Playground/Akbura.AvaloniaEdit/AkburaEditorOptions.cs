using Avalonia.Input;
using Akbura.AvaloniaEdit.Infrastructure;

namespace Akbura.AvaloniaEdit;

public sealed record AkburaEditorOptions
{
#if DEBUG
    internal EditorPerformance? Performance
    {
        get; init;
    }
#endif

    public TimeSpan AnalysisDelay
    {
        get; init;
    } = TimeSpan.FromMilliseconds(150);

    public TimeSpan HoverDelay
    {
        get; init;
    } = TimeSpan.FromMilliseconds(350);

    public bool AutomaticCompletion
    {
        get; init;
    } = true;

    public KeyGesture? CompletionGesture
    {
        get; init;
    } = new(Key.Space, KeyModifiers.Control);

    public IAkburaEditorScheduler Scheduler
    {
        get; init;
    } = new AkburaThreadPoolScheduler();
}
