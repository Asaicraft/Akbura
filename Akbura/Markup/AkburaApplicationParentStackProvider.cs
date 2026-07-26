using Avalonia;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Markup;

internal sealed class AkburaApplicationParentStackProvider :
    IAvaloniaXamlIlEagerParentStackProvider,
    IReadOnlyList<object>
{
    private readonly Application _application;

    public AkburaApplicationParentStackProvider(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        _application = application;
    }

    public int Count => 1;

    public object this[int index]
    {
        get
        {
            if (index != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _application;
        }
    }

    public IReadOnlyList<object> DirectParentsStack => this;

    public IAvaloniaXamlIlEagerParentStackProvider? ParentProvider => null;

    public IEnumerable<object> Parents
    {
        get
        {
            yield return _application;
        }
    }

    public IEnumerator<object> GetEnumerator() => Parents.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        => GetEnumerator();
}