using Akbura.Collections;
using Avalonia;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Akbura;

public abstract partial class AkburaControl
{
    /// <summary>Used by generated list parameters, once per owned backing list.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    protected CollectionParameterBinding<T> CreateCollectionParameterBinding<T>(
        ObservableCollection<T> items, AvaloniaProperty property)
    {
        VerifyAccess();
        var queued = false;
        CollectionParameterBinding<T>? binding = null;

        bool IsCurrent()
        {
            foreach (var parameter in GetParameters())
                if (ReferenceEquals(parameter.AvaloniaProperty, property)) return true;
            return false;
        }
        void Stop()
        {
            AttachedToLogicalTree -= OnAttached;
            DetachedFromLogicalTree -= OnDetached;
            binding!.Dispose();
        }
        void Notify()
        {
            if (queued) return;
            queued = true;
            // Let all CollectionChanged listeners (including foreach) receive
            // the delta before a component-wide environment update is requested.
            Dispatcher.UIThread.Post(() =>
            {
                queued = false;
                if (!IsCurrent()) { Stop(); return; }
                OnParameterChanged();
            });
        }
        void OnAttached(object? sender, LogicalTreeAttachmentEventArgs args)
        {
            if (!IsCurrent()) { Stop(); return; }
            binding!.Resume();
        }
        void OnDetached(object? sender, LogicalTreeAttachmentEventArgs args) => binding!.Suspend();

        binding = new CollectionParameterBinding<T>(
            items,
            Notify,
            VerifyAccess,
            GetType().Name + "." + property.Name);
        AttachedToLogicalTree += OnAttached;
        DetachedFromLogicalTree += OnDetached;
        return binding;
    }

    /// <summary>Applies marker semantics before updating a generated collection source binding.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Browsable(false)]
    protected static void ApplyCollectionParameterSource<T>(
        CollectionParameterBinding<T> binding,
        object? source)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (ReferenceEquals(source, Avalonia.Data.BindingOperations.DoNothing)) return;
        if (ReferenceEquals(source, AvaloniaProperty.UnsetValue)) source = null;
        binding.SetSource(source);
    }
}
