#if DEBUG
using Akbura.Engine;
using Avalonia.Controls;

namespace Akbura.AvaloniaEdit.BrowserSmoke;

internal sealed partial class SmokePerformanceView : AkburaControl
{
    private readonly Func<Task> _measure;

    internal SmokePerformanceView(Func<Task> measure)
        : base(AkburaEngine.Empty)
    {
        _measure = measure;
        InitializeIfNeeded();
    }

    internal Button MeasureButton => measurePerformance;

    internal TextBox Output => performanceOutput;

    internal int DocumentSize => performanceSize.SelectedIndex switch
    {
        1 => 1000,
        2 => 5000,
        _ => 100,
    };
}
#endif
