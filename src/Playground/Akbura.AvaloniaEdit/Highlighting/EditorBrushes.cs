using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Akbura.AvaloniaEdit.Highlighting;

internal static class EditorBrushes
{
    public static IImmutableBrush Get(AkburaCodeEditor editor, string category)
    {
        if (editor.TryFindResource($"AkburaEditor.{category}Brush", editor.ActualThemeVariant, out var resource) &&
            resource is IBrush brush)
        {
            return brush.ToImmutable();
        }

        return (editor.Foreground ?? Brushes.Transparent).ToImmutable();
    }
}
