using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.AotTest;

[AkburaSource("Counter.akbura", AssemblyName = "Akbura.AotTest")]
[StaticMountComponent]
internal sealed class Counter: AkburaComponent
{
    [AkburaState("count", DefaultValue = 0)]
    private int __state_count = 0;

    private __Counter_view_ __view__0;

    public Counter()
    {
        __view__0 = new __Counter_view_(this);
    }

    public override ViewContainer Update()
    {
        return new(__view__0, isStaticMountComponent: true);
    }

    [AkburaEvent("Click", Line = 3, Character = 9)]
    private void __event__0()
    {
        __state_count++;
        InvalidState(States.From("counter"));
    }

    private class __Counter_view_ : AvaloniaAkburaView
    {
        private Button _button;
        private readonly Counter _counter;

        public __Counter_view_(Counter counter)
        {
            _counter = counter;
        }

        public override void MountToTree(StyledElement? parent)
        {
            _button = new Button();
            _button.Content = $"{_counter.__state_count}";
            _button.Click += _counter.__event__0;
            parent.Children.Add(_button);
        }

        public override void UpdateView(States states)
        {
            if(states.Contains("counter"))
            {
                _button.Content = $"{_counter.__state_count}";
            }
        }

        public override void UnmountFromTree()
        {
            _button.Click -= _counter.__event__0;
            _button.Parent.Children.Remove(_button);
        }
    }
}
