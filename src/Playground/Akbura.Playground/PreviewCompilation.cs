using Avalonia.Controls;
using Avalonia.Threading;
using System.Collections.Immutable;
using System.Runtime.Loader;

namespace Akbura.Playground;

internal sealed class PreviewCompilation
{
    private readonly byte[]? _image;
    private readonly string? _componentTypeName;

    public PreviewCompilation(ImmutableArray<string> diagnostics)
    {
        Diagnostics = diagnostics;
    }

    public PreviewCompilation(ImmutableArray<string> diagnostics, byte[] image, string componentTypeName)
        : this(diagnostics)
    {
        _image = image;
        _componentTypeName = componentTypeName;
    }

    public ImmutableArray<string> Diagnostics
    {
        get;
    }

    public bool Success => _image != null;

    /// <summary>
    /// Creates the generated Avalonia control on the UI thread.
    /// </summary>
    public PreviewInstance CreatePreview()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_image == null || _componentTypeName == null)
        {
            throw new InvalidOperationException("The preview assembly did not compile.");
        }

        // Browser Mono keeps loaded assemblies until the page is reloaded.
        var collectibleContext = OperatingSystem.IsBrowser()
            ? null
            : new AssemblyLoadContext("Akbura.Playground.Preview", isCollectible: true);
        var context = collectibleContext ?? AssemblyLoadContext.Default;
        try
        {
            using var stream = new MemoryStream(_image, writable: false);
            var assembly = context.LoadFromStream(stream);
            var type = assembly.GetType(_componentTypeName, throwOnError: true)!;
            if (Activator.CreateInstance(type) is not Control view)
            {
                throw new InvalidOperationException("The generated component is not an Avalonia control.");
            }

            return new PreviewInstance(collectibleContext, view);
        }
        catch
        {
            collectibleContext?.Unload();
            throw;
        }
    }
}
