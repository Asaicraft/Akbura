using System.Runtime.CompilerServices;

namespace Akbura.AvaloniaEdit.Infrastructure;

internal static class DocumentBindingRegistry
{
    private static readonly ConditionalWeakTable<AkburaWorkspace, HashSet<string>> Bindings = [];

    public static IDisposable Acquire(AkburaWorkspace workspace, Uri uri)
    {
        var bindings = Bindings.GetOrCreateValue(workspace);
        // Match Workspaces case-insensitive file URI identity, including on non-Windows hosts.
        var key = uri.IsFile ? uri.LocalPath.ToUpperInvariant() : uri.AbsoluteUri;
        lock (bindings)
        {
            if (!bindings.Add(key))
            {
                throw new InvalidOperationException($"An editable session is already bound to '{uri}'.");
            }
        }
        return new Lease(bindings, key);
    }

    private sealed class Lease(HashSet<string> bindings, string key) : IDisposable
    {
        public void Dispose()
        {
            lock (bindings)
            {
                bindings.Remove(key);
            }
        }
    }
}
