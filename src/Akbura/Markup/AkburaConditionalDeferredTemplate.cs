using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Akbura.Markup;

/// <summary>Preserves native deferred-template matching while supplying its actual host to each instance.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public sealed class AkburaConditionalDeferredTemplate : IDeferredContent
{
    private readonly Func<Control, INameScope, AkburaConditionalTemplateInstance> _builder;
    private readonly ConditionalWeakTable<TemplatedControl, ControlHostTemplate> _hosts = new();
    private static readonly ConditionalWeakTable<Avalonia.Markup.Xaml.Templates.ControlTemplate,
        List<WeakReference<ControlHostTemplate>>> s_controlTemplates = new();

    static AkburaConditionalDeferredTemplate()
    {
        TemplatedControl.TemplateProperty.Changed.AddClassHandler<TemplatedControl>(OnTemplateChanged);
    }

    public AkburaConditionalDeferredTemplate(Func<Control, INameScope, AkburaConditionalTemplateInstance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AkburaConditionalDataTemplate.EnsureInitialized();
        _builder = builder;
    }

    public object? Build(IServiceProvider? serviceProvider) => null;

    internal static void NotifyTemplateContentChanged(object nativeTemplate)
    {
        if (nativeTemplate is Avalonia.Markup.Xaml.Templates.DataTemplate dataTemplate)
        {
            AkburaConditionalDataTemplate.NotifyNativeDataTemplateContentChanged(dataTemplate);
        }
        else if (nativeTemplate is Avalonia.Markup.Xaml.Templates.ControlTemplate controlTemplate &&
            s_controlTemplates.TryGetValue(controlTemplate, out var hosts))
        {
            foreach (var entry in hosts.ToArray())
            {
                if (!entry.TryGetTarget(out var host))
                {
                    hosts.Remove(entry);
                }
                else
                {
                    host.NotifyContentChanged(controlTemplate);
                }
            }
        }
    }

    internal AkburaConditionalTemplateInstance CreateInstance(Control host, INameScope nameScope) =>
        _builder(host, nameScope);

    private static void OnTemplateChanged(TemplatedControl host, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.GetOldValue<IControlTemplate?>() is RevisionTemplate old && !old.Owner.IsCurrent())
        {
            old.Owner.RequestRetirement();
        }

        if (host.Template is Avalonia.Markup.Xaml.Templates.ControlTemplate
            { Content: AkburaConditionalDeferredTemplate marker })
        {
            var adapter = marker._hosts.GetValue(host, marker.CreateHostTemplate);
            host.SetCurrentValue(TemplatedControl.TemplateProperty, adapter.InitialTemplate);
            host.ApplyTemplate();
        }
    }

    private ControlHostTemplate CreateHostTemplate(TemplatedControl host)
    {
        var template = (Avalonia.Markup.Xaml.Templates.ControlTemplate)host.Template!;
        var adapter = new ControlHostTemplate(this, host);
        s_controlTemplates.GetValue(template, static _ => []).Add(new(adapter));
        return adapter;
    }

    private sealed class ControlHostTemplate : IDisposable
    {
        private readonly AkburaConditionalDeferredTemplate _marker;
        private readonly TemplatedControl _host;
        private readonly RevisionTemplate _first;
        private readonly RevisionTemplate _second;
        private readonly Func<bool> _isCurrent;
        private readonly Action _updateHost;
        private readonly Func<bool> _isDetached;
        private AkburaConditionalTemplateInstance? _instance;
        private INameScope? _nameScope;

        public ControlHostTemplate(AkburaConditionalDeferredTemplate marker, TemplatedControl host)
        {
            _marker = marker;
            _host = host;
            _first = new(this);
            _second = new(this);
            _isCurrent = IsCurrent;
            _isDetached = IsDetached;
            _updateHost = UpdateHost;
        }

        public IControlTemplate InitialTemplate => _first;

        public bool IsCurrent() => _host.Template is RevisionTemplate template && ReferenceEquals(template.Owner, this);

        public void NotifyContentChanged(Avalonia.Markup.Xaml.Templates.ControlTemplate nativeTemplate)
        {
            if (IsCurrent() && !ReferenceEquals(nativeTemplate.Content, _marker))
            {
                _host.SetCurrentValue(TemplatedControl.TemplateProperty, nativeTemplate);
                _host.ApplyTemplate();
            }
        }

        public TemplateResult<Control>? Build()
        {
            if (_instance == null)
            {
                var nameScope = new NameScope();
                var instance = _marker.CreateInstance(_host, nameScope);
                _instance = instance;
                _nameScope = nameScope;
                instance.AttachHost(_host, _isCurrent, _updateHost, null);
                try
                {
                    instance.UpdateForBuild();
                    nameScope.Complete();
                }
                catch
                {
                    _instance = null;
                    _nameScope = null;
                    instance.Dispose();
                    throw;
                }
            }

            return _instance.Root is { } root ? new(root, NameScope.GetNameScope(root) ?? _nameScope!) : null;
        }

        public void RequestRetirement()
        {
            if (_instance?.ParentLifetimeOwner is { } owner)
            {
                owner.DeferLocalRenderScopeDisposal(this, _isDetached);
            }
            else if (IsDetached())
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            _instance?.Dispose();
            _instance = null;
            _nameScope = null;
            _marker._hosts.Remove(_host);
        }

        private bool IsDetached() => !IsCurrent();

        private void UpdateHost()
        {
            // ApplyTemplate only rebuilds for a different template identity.
            // Two cached nonvisual adapters advance that identity without a new
            // generated class or an allocation on an unchanged render frame.
            var next = ReferenceEquals(_host.Template, _first) ? _second : _first;
            _host.SetCurrentValue(TemplatedControl.TemplateProperty, next);
            _host.ApplyTemplate();
        }
    }

    private sealed class RevisionTemplate(ControlHostTemplate owner) : IControlTemplate
    {
        public ControlHostTemplate Owner => owner;

        public TemplateResult<Control>? Build(TemplatedControl control) => owner.Build();
    }
}
