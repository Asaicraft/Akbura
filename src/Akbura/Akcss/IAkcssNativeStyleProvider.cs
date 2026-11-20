using Avalonia.Styling;
using System.ComponentModel;

namespace Akbura.Akcss;

/// <summary>
/// Optional capability of an AKCSS definition compiled with a native application body.
/// The runtime supplies a fresh anchored root and a stable snapshot of the utility arguments.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public interface IAkcssNativeStyleProvider
{
    Type NativeTargetType { get; }
    void BuildNativeStyle(Style root, IReadOnlyList<object?> arguments);
}
