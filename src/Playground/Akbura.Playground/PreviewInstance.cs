using Avalonia.Controls;
using System.Runtime.Loader;

namespace Akbura.Playground;

/// <summary>
/// Owns one preview control and any collectible assembly load context.
/// </summary>
internal sealed class PreviewInstance : IDisposable
{
    private AssemblyLoadContext? _context;
    private Control? _view;

    public PreviewInstance(AssemblyLoadContext? context, Control view)
    {
        _context = context;
        _view = view;
    }

    public Control View => _view ?? throw new ObjectDisposedException(nameof(PreviewInstance));

    /// <summary>
    /// Releases this preview after the host detaches its view.
    /// </summary>
    public void Dispose()
    {
        _view = null;
        var context = _context;
        _context = null;
        context?.Unload();
    }
}
