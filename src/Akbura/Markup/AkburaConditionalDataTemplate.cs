using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Akbura.HotReload;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Akbura.Markup;

/// <summary>Adopts a framework-owned presenter as a conditional template's stable sink.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public abstract class AkburaConditionalDataTemplate : IDataTemplate
{
    private readonly ConditionalWeakTable<ContentPresenter, HostTemplate> _hosts = new();
    private static readonly ConditionalWeakTable<Avalonia.Markup.Xaml.Templates.DataTemplate, DeferredTemplateMap>
        s_deferredTemplates = new();

    static AkburaConditionalDataTemplate()
    {
        ContentPresenter.ContentTemplateProperty.Changed.AddClassHandler<ContentPresenter>(OnTemplateChanged);
        ContentPresenter.ContentProperty.Changed.AddClassHandler<ContentPresenter>(static (host, _) => TryAdopt(host));
        Control.LoadedEvent.AddClassHandler<ContentPresenter>(static (host, _) => TryAdopt(host));
    }

    public abstract bool Match(object? data);

    internal static void EnsureInitialized()
    {
    }

    internal static void NotifyNativeDataTemplateContentChanged(Avalonia.Markup.Xaml.Templates.DataTemplate template)
    {
        if (s_deferredTemplates.TryGetValue(template, out var descriptors))
        {
            descriptors.NotifyContentChanged();
        }
    }

    // The native host has not supplied its identity at this point. In particular,
    // returning a provisional control would execute the selected constructor twice.
    public Control? Build(object? data) => null;

    public IDataTemplate BindHost(ContentPresenter host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return _hosts.GetValue(host, CreateHostTemplate);
    }

    protected abstract AkburaConditionalTemplateInstance CreateInstance(object? data, INameScope nameScope,
        ContentPresenter host);

    private HostTemplate CreateHostTemplate(ContentPresenter host) => new(this, host);

    private static void OnTemplateChanged(ContentPresenter host, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.GetOldValue<IDataTemplate?>() is HostTemplate previous &&
            !ReferenceEquals(host.ContentTemplate, previous))
        {
            previous.RequestRetirement();
        }

        TryAdopt(host);
    }

    private static void TryAdopt(ContentPresenter host)
    {
        if (host.ContentTemplate is HostTemplate current)
        {
            current.RetireNonmatchingData();
            return;
        }

        var selected = host.FindDataTemplate(host.Content, host.ContentTemplate);
        var template = selected as AkburaConditionalDataTemplate;
        if (selected is Avalonia.Markup.Xaml.Templates.DataTemplate { Content: AkburaConditionalDeferredTemplate } native)
        {
            var descriptors = s_deferredTemplates.GetValue(native, static value => new(value));
            template = descriptors.Get((AkburaConditionalDeferredTemplate)native.Content!);
        }

        if (template != null)
        {
            host.SetCurrentValue(ContentPresenter.ContentTemplateProperty, template.BindHost(host));
            host.UpdateChild();
        }
    }

    private sealed class HostTemplate(AkburaConditionalDataTemplate template, ContentPresenter host) :
        IRecyclingDataTemplate, IDisposable
    {
        private AkburaConditionalTemplateInstance? _instance;
        private object? _data;
        private INameScope? _nameScope;
        private Func<bool>? _retirementPredicate;
        private List<RetiredInstance>? _retiredInstances;

        public bool Match(object? data) => template.Match(data);

        public Control? Build(object? data) => Build(data, null);

        public Control? Build(object? data, Control? existing)
        {
            if (_instance != null && ReferenceEquals(_data, data))
            {
                return _instance.Root;
            }

            var previous = _instance;
            var previousData = _data;
            var previousNameScope = _nameScope;
            var retained = _retiredInstances?.Find(value => ReferenceEquals(value.Data, data) && !value.Instance.IsDisposed);
            var nameScope = retained?.NameScope ?? new NameScope();
            var instance = retained?.Instance ?? template.CreateInstance(data, nameScope, host);
            if (retained != null)
            {
                retained.Remove();
            }

            _instance = instance;
            _data = data;
            _nameScope = nameScope;
            if (retained == null)
            {
                // Each item's owned root has a null baseline. The previous
                // item's root belongs to a different lifetime, not this one.
                instance.AttachHost(host,
                    () => ReferenceEquals(_instance, instance) && ReferenceEquals(_data, host.Content) && IsCurrent(),
                    host.UpdateChild, previousRoot: null);
            }
            try
            {
                instance.UpdateForBuild();
                if (retained == null)
                {
                    nameScope.Complete();
                }
                if (instance.Root is { } root && NameScope.GetNameScope(root) == null)
                {
                    NameScope.SetNameScope(root, nameScope);
                }
            }
            catch
            {
                _instance = previous;
                _data = previousData;
                _nameScope = previousNameScope;
                instance.Dispose();
                throw;
            }

            if (previous != null && !ReferenceEquals(previous, instance))
            {
                var retired = new RetiredInstance(this, previousData, previous, previousNameScope!);
                if (previous.ParentLifetimeOwner is { HasPendingRevision: true } owner)
                {
                    (_retiredInstances ??= []).Add(retired);
                    previous.NotifyWhenDisposed(retired.Remove);
                    owner.DeferLocalRenderScopeDisposal(retired, retired.IsDetached);
                }
                else
                {
                    retired.Dispose();
                }
            }

            return instance.Root;
        }

        public void RetireNonmatchingData()
        {
            if (_instance is not { } instance || Match(host.Content))
            {
                return;
            }

            var retired = new RetiredInstance(this, _data, instance, _nameScope!);
            _instance = null;
            _data = null;
            _nameScope = null;
            if (instance.ParentLifetimeOwner is { HasPendingRevision: true } owner)
            {
                (_retiredInstances ??= []).Add(retired);
                instance.NotifyWhenDisposed(retired.Remove);
                owner.DeferLocalRenderScopeDisposal(retired, retired.IsDetached);
            }
            else
            {
                retired.Dispose();
            }
        }

        public void Dispose()
        {
            _instance?.Dispose();
            _instance = null;
            _data = null;
            _nameScope = null;
            if (_retiredInstances is { } retired)
            {
                _retiredInstances = null;
                foreach (var instance in retired.ToArray())
                {
                    instance.Dispose();
                }
            }

            template._hosts.Remove(host);
        }

        public void RequestRetirement()
        {
            var owner = _instance?.ParentLifetimeOwner;
            if (owner == null && _retiredInstances is { } retired)
            {
                foreach (var instance in retired)
                {
                    if (instance.Instance.ParentLifetimeOwner is { } lifetimeOwner)
                    {
                        owner = lifetimeOwner;
                        break;
                    }
                }
            }

            if (owner != null)
            {
                owner.DeferLocalRenderScopeDisposal(this, _retirementPredicate ??= IsDetached);
            }
            else if (IsDetached())
            {
                Dispose();
            }
        }

        private bool IsCurrent() => ReferenceEquals(host.ContentTemplate, this);

        private bool IsDetached() => !IsCurrent();

        private sealed class RetiredInstance(HostTemplate owner, object? data, AkburaConditionalTemplateInstance instance,
            INameScope nameScope) : IDisposable
        {
            public object? Data => data;
            public AkburaConditionalTemplateInstance Instance => instance;
            public INameScope NameScope => nameScope;

            public bool IsDetached() => !ReferenceEquals(owner._instance, instance);

            public void Remove()
            {
                owner._retiredInstances?.Remove(this);
                instance.RemoveDisposeNotification(Remove);
            }

            public void Dispose()
            {
                Remove();
                instance.Dispose();
            }
        }
    }

    private void NotifyBoundHosts(IDataTemplate nativeTemplate)
    {
        foreach (var entry in _hosts)
        {
            if (ReferenceEquals(entry.Key.ContentTemplate, entry.Value))
            {
                entry.Key.SetCurrentValue(ContentPresenter.ContentTemplateProperty, nativeTemplate);
                entry.Key.UpdateChild();
            }
        }
    }

    private sealed class DeferredTemplateMap(Avalonia.Markup.Xaml.Templates.DataTemplate template)
    {
        private readonly ConditionalWeakTable<AkburaConditionalDeferredTemplate, DeferredDataTemplate> _descriptors = new();

        public DeferredDataTemplate Get(AkburaConditionalDeferredTemplate marker) => _descriptors.GetValue(marker, Create);

        public void NotifyContentChanged()
        {
            foreach (var entry in _descriptors)
            {
                if (!ReferenceEquals(entry.Key, template.Content))
                {
                    ((AkburaConditionalDataTemplate)entry.Value).NotifyBoundHosts(template);
                }
            }
        }

        private DeferredDataTemplate Create(AkburaConditionalDeferredTemplate marker) => new(template, marker);
    }

    private sealed class DeferredDataTemplate(Avalonia.Markup.Xaml.Templates.DataTemplate template,
        AkburaConditionalDeferredTemplate marker) :
        AkburaConditionalDataTemplate
    {
        public override bool Match(object? data) => template.Match(data);

        protected override AkburaConditionalTemplateInstance CreateInstance(object? data, INameScope nameScope,
            ContentPresenter host) => marker.CreateInstance(host, nameScope);
    }
}

[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaConditionalDataTemplate<T>(
    Func<T, INameScope, ContentPresenter, AkburaConditionalTemplateInstance> builder) : AkburaConditionalDataTemplate
{
    private readonly Func<T, INameScope, ContentPresenter, AkburaConditionalTemplateInstance> _builder =
        builder ?? throw new ArgumentNullException(nameof(builder));

    public override bool Match(object? data) => data is T || data == null && default(T) is null;

    protected override AkburaConditionalTemplateInstance CreateInstance(object? data, INameScope nameScope,
        ContentPresenter host) => _builder((T)data!, nameScope, host);
}

/// <summary>Publishes a nullable template root without inserting a visual container.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaConditionalTemplateInstance : IDisposable
{
    private Action? _render;
    private Action? _cleanup;
    private Control? _host;
    private Func<bool>? _isHostCurrent;
    private Action? _updateHost;
    private IDisposable? _lifetime;
    private bool _rendering;
    private bool _building;
    private bool _disposed;
    private Control? _root;
    private Action? _afterDispose;

    public AkburaConditionalTemplateInstance(Action render, Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(cleanup);
        _render = render;
        _cleanup = cleanup;
    }

    public Control? Root
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value))
            {
                return;
            }

            _root = value;
            if (!_building && _isHostCurrent?.Invoke() == true)
            {
                _updateHost!();
            }
        }
    }

    internal AkburaRenderState? ParentLifetimeOwner { get; private set; }

    internal bool IsDisposed => _disposed;

    public void SetLifetime(IDisposable lifetime, AkburaRenderState? parentState = null)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        if (_lifetime != null)
        {
            throw new InvalidOperationException("A template instance already owns its render lifetime.");
        }

        _lifetime = lifetime;
        ParentLifetimeOwner = parentState;
    }

    public void Update()
    {
        var render = _render;
        if (_disposed || render == null || _rendering || _host == null || _isHostCurrent?.Invoke() != true)
        {
            return;
        }

        var previous = Root;
        _rendering = true;
        try
        {
            render();
        }
        catch
        {
            _root = previous;
            if (!_building && _isHostCurrent?.Invoke() == true)
            {
                _updateHost!();
            }

            throw;
        }
        finally
        {
            _rendering = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var cleanup = _cleanup;
        var lifetime = _lifetime;
        _render = null;
        _cleanup = null;
        _lifetime = null;
        try
        {
            lifetime?.Dispose();
        }
        finally
        {
            try
            {
                cleanup?.Invoke();
            }
            finally
            {
                _host = null;
                _isHostCurrent = null;
                _updateHost = null;
                ParentLifetimeOwner = null;
                _root = null;
                var afterDispose = _afterDispose;
                _afterDispose = null;
                afterDispose?.Invoke();
            }
        }
    }

    internal void NotifyWhenDisposed(Action callback)
    {
        if (_disposed)
        {
            callback();
        }
        else
        {
            _afterDispose += callback;
        }
    }

    internal void RemoveDisposeNotification(Action callback) => _afterDispose -= callback;

    internal void AttachHost(Control host, Func<bool> isCurrent, Action updateHost, Control? previousRoot)
    {
        _host = host;
        _isHostCurrent = isCurrent;
        _updateHost = updateHost;
        _root = previousRoot;
    }

    internal void UpdateForBuild()
    {
        // The native caller will attach the returned root after Build returns.
        // Publishing into that same caller reentrantly would attach it twice.
        _building = true;
        try
        {
            Update();
        }
        finally
        {
            _building = false;
        }
    }
}
