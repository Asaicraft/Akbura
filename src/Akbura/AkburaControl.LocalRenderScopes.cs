using Akbura.Hooks;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using System.ComponentModel;

namespace Akbura;

public abstract partial class AkburaControl
{
    private List<WeakReference<LocalRenderScope>>? _localRenderScopes;
    private long _localRenderScopeFrame;

    /// <summary>Owns one generated template or deferred-content render instance.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    protected IDisposable __AkburaRegisterLocalRenderScope(Control root, Action render, Action cleanup,
        AkburaRenderState? lifetimeOwner = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(cleanup);
        var scope = new LocalRenderScope(this, root, render, cleanup, lifetimeOwner);
        (_localRenderScopes ??= []).Add(scope.Entry);
        return scope;
    }

    private void BeginLocalRenderScopeFrame() => _localRenderScopeFrame++;

    private void UpdateLocalRenderScopes()
    {
        if (_localRenderScopes is not { Count: > 0 } scopes)
        {
            return;
        }

        // Callbacks may construct nested instances or detach existing ones.
        // New instances already rendered in their builder must not render twice.
        for (var i = 0; i < scopes.Count;)
        {
            var entry = scopes[i];
            if (!entry.TryGetTarget(out var scope))
            {
                scopes.RemoveAt(i);
                continue;
            }

            scope.RenderCurrentFrame();
            if (i < scopes.Count && ReferenceEquals(scopes[i], entry))
            {
                i++;
            }
            else
            {
                i = 0;
            }
        }
    }

    private void SuspendLocalRenderScopes()
    {
        if (_localRenderScopes is not { Count: > 0 } scopes)
        {
            return;
        }

        List<Exception>? failures = null;
        while (scopes.Count > 0)
        {
            try
            {
                if (scopes[0].TryGetTarget(out var scope))
                {
                    scope.Suspend();
                }
                else
                {
                    scopes.RemoveAt(0);
                }
            }
            catch (Exception exception)
            {
                UseHookFailures.Capture(ref failures, exception);
            }
        }

        UseHookFailures.ThrowIfAny(failures, "Local render scopes could not be fully suspended.");
    }

    private sealed class LocalRenderScope : IDisposable
    {
        private readonly AkburaControl _owner;
        private readonly Control _root;
        private readonly AkburaRenderState? _lifetimeOwner;
        private Action? _render;
        private Action? _cleanup;
        private long _renderedFrame;
        private bool _active = true;
        private bool _pendingDetach;

        public LocalRenderScope(AkburaControl owner, Control root, Action render, Action cleanup,
            AkburaRenderState? lifetimeOwner)
        {
            _owner = owner;
            _root = root;
            _render = render;
            _cleanup = cleanup;
            _lifetimeOwner = lifetimeOwner;
            Entry = new(this);
            _renderedFrame = owner._localRenderScopeFrame;
            root.AttachedToVisualTree += OnAttached;
            root.DetachedFromVisualTree += OnDetached;
        }

        public WeakReference<LocalRenderScope> Entry { get; }

        public void RenderCurrentFrame()
        {
            if (_pendingDetach && _lifetimeOwner?.HasPendingRevision != true)
            {
                _pendingDetach = false;
                if (!_root.IsAttachedToVisualTree())
                {
                    Suspend();
                }
            }

            if (!_active || _render == null || _renderedFrame == _owner._localRenderScopeFrame)
            {
                return;
            }

            _renderedFrame = _owner._localRenderScopeFrame;
            _render();
        }

        public void Suspend()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            _owner._localRenderScopes?.Remove(Entry);
            _cleanup?.Invoke();
        }

        public void Dispose()
        {
            _root.AttachedToVisualTree -= OnAttached;
            _root.DetachedFromVisualTree -= OnDetached;
            try
            {
                Suspend();
            }
            finally
            {
                _render = null;
                _cleanup = null;
            }
        }

        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            _pendingDetach = false;
            if (_active || _render == null)
            {
                return;
            }

            _active = true;
            (_owner._localRenderScopes ??= []).Add(Entry);
            _renderedFrame = _owner._localRenderScopeFrame;
            _render();
        }

        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args)
        {
            if (_lifetimeOwner?.HasPendingRevision == true)
            {
                _pendingDetach = true;
                return;
            }

            Suspend();
        }
    }
}
