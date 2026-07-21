using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class AkcssRuntimeTests
{
    [Fact]
    public void ClassesAndUtilities_RunAsOneOrderedReactiveCascade()
    {
        var control = new Border();
        var events = new List<string>();
        var classSignal = new TestSignal<object?>();
        var style = new LoggingClass(events, classSignal);
        var utility = new LoggingUtility(events);
        var utilityActivator = new LoggingUtilityActivator(utility, 42);

        AkburaControl.SetAkcssStyles(
            control,
            [new AkcssClassActivator(style), utilityActivator]);

        Assert.Equal(
            ["utility:reset", "class:reset", "class:update", "utility:update:42"],
            events);

        events.Clear();
        classSignal.Emit(AvaloniaProperty.UnsetValue);

        Assert.Equal(
            ["utility:reset", "class:reset", "class:update", "utility:update:42"],
            events);

        events.Clear();
        control.ClearValue(AkburaControl.AkcssStylesProperty);

        Assert.Equal(["utility:reset", "class:reset"], events);

        events.Clear();
        classSignal.Emit(AvaloniaProperty.UnsetValue);
        Assert.Empty(events);
    }

    [Fact]
    public void ConditionalUtility_ResetsWhenConditionBecomesFalse()
    {
        var control = new Border();
        var condition = new TestSignal<bool>();
        var utility = new CountingUtility();
        var activator = new ConditionalUtilityActivator(utility, condition);

        AkburaControl.SetAkcssStyles(control, [activator]);

        Assert.Equal(1, utility.ResetCount);
        Assert.Equal(0, utility.UpdateCount);

        condition.Emit(true);

        Assert.Equal(2, utility.ResetCount);
        Assert.Equal(1, utility.UpdateCount);

        condition.Emit(false);

        Assert.Equal(3, utility.ResetCount);
        Assert.Equal(1, utility.UpdateCount);
    }

    [Fact]
    public void ObservesPropertyAttribute_ReappliesAkcssClass()
    {
        var control = new Border();
        var style = new WidthObservingClass();

        AkburaControl.SetAkcssStyles(
            control,
            [new AkcssClassActivator(style)]);

        Assert.Equal(1, style.UpdateCount);

        control.Width = 120;

        Assert.Equal(2, style.UpdateCount);
        Assert.Equal(2, style.ResetCount);

        control.Width = 120;
        Assert.Equal(2, style.UpdateCount);
    }

    [Fact]
    public void UtilityActivator_ValidatesArgumentCountAndTypes()
    {
        var utility = new ArgumentUtility();

        Assert.Throws<ArgumentException>(
            () => new RawUtilityActivator(utility, []));
        Assert.Throws<ArgumentException>(
            () => new RawUtilityActivator(utility, ["wrong"]));

        _ = new RawUtilityActivator(utility, [42]);
    }

    [Fact]
    public void AkcssStyles_CanOnlyBeSetOnceWithoutClearing()
    {
        var control = new Border();
        var styles = ImmutableArray.Create<AkcssStyleActivator>(
            new AkcssClassActivator(new WidthObservingClass()));

        AkburaControl.SetAkcssStyles(control, styles);

        Assert.Equal(styles, AkburaControl.GetAkcssStyles(control));
        Assert.Throws<InvalidOperationException>(
            () => AkburaControl.SetAkcssStyles(control, styles));
    }

    private sealed class LoggingClass : AkcssClass
    {
        private readonly List<string> _events;
        private readonly IObservable<object?> _signal;

        public LoggingClass(
            List<string> events,
            IObservable<object?> signal)
        {
            _events = events;
            _signal = signal;
        }

        public override void Update(object control)
        {
            _events.Add("class:update");
        }

        public override void Reset(object target)
        {
            _events.Add("class:reset");
        }

        public override IObservable<object?> Watch(object target)
        {
            return _signal;
        }
    }

    private sealed class LoggingUtility : AkcssUtility<int>
    {
        private readonly List<string> _events;

        public LoggingUtility(List<string> events)
        {
            _events = events;
        }

        public override void Update(object target, int value)
        {
            _events.Add($"utility:update:{value}");
        }

        public override void Reset(object target)
        {
            _events.Add("utility:reset");
        }
    }

    private sealed class LoggingUtilityActivator : TailwindUtilityActivator
    {
        public LoggingUtilityActivator(LoggingUtility utility, int value)
            : base(utility, isConditional: false, [value])
        {
        }

        public override bool Condition => true;

        public override void Execute(Control control)
        {
            ((LoggingUtility)Utility).Update(control, (int)Arguments[0]!);
        }
    }

    private sealed class CountingUtility : ZeroAkcssUtility
    {
        public int UpdateCount { get; private set; }

        public int ResetCount { get; private set; }

        public override void Update(object target)
        {
            UpdateCount++;
        }

        public override void Reset(object target)
        {
            ResetCount++;
        }
    }

    private sealed class ArgumentUtility : AkcssUtility<int>
    {
        public override void Update(object target, int value)
        {
        }
    }

    private sealed class ConditionalUtilityActivator : SimpleTailwindUtilityActivator
    {
        public ConditionalUtilityActivator(
            CountingUtility utility,
            IObservable<bool> condition)
            : base(utility, [], condition)
        {
        }

        public override void Execute(Control control)
        {
            ((CountingUtility)Utility).Update(control);
        }
    }

    private sealed class RawUtilityActivator : TailwindUtilityActivator
    {
        public RawUtilityActivator(
            AkcssUtility utility,
            ImmutableArray<object?> arguments)
            : base(utility, isConditional: false, arguments)
        {
        }

        public override bool Condition => true;

        public override void Execute(Control control)
        {
        }
    }

    [ObservesProperty(nameof(Control.Width))]
    private sealed class WidthObservingClass : AkcssClass
    {
        public int UpdateCount { get; private set; }

        public int ResetCount { get; private set; }

        public override void Update(object control)
        {
            UpdateCount++;
        }

        public override void Reset(object target)
        {
            ResetCount++;
        }
    }

    private sealed class TestSignal<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _observers = [];

        public IDisposable Subscribe(IObserver<T> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            _observers.Add(observer);
            return new Subscription(_observers, observer);
        }

        public void Emit(T value)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(value);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private List<IObserver<T>>? _observers;
            private IObserver<T>? _observer;

            public Subscription(
                List<IObserver<T>> observers,
                IObserver<T> observer)
            {
                _observers = observers;
                _observer = observer;
            }

            public void Dispose()
            {
                var observers = Interlocked.Exchange(ref _observers, null);
                var observer = Interlocked.Exchange(ref _observer, null);
                if (observers != null && observer != null)
                {
                    observers.Remove(observer);
                }
            }
        }
    }
}
