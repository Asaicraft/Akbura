using System.ComponentModel;

namespace Akbura.HotReload;

/// <summary>
/// Represents the lifetime of a generated Hot Reload property update.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public readonly struct AkburaHotReloadPropertyUpdate : IDisposable
{
    /// <summary>
    /// Completes the generated Hot Reload property update.
    /// </summary>
    public void Dispose()
    {
    }
}
