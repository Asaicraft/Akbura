using Akbura.Akcss;
using Akbura.Markup;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace Akbura.UnitTests;

public sealed partial class AkcssNativeFragmentRuntimeTests
{
    [Fact]
    public async Task ArgumentsAndConditionsHaveIndependentLifetimesAndFailedUpdatesKeepPreviousTree()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentTheme());
            var target = new Button { Content = "Target" };
            var definition = new ResourceFragment();
            var keys = new CountedSignal<object>();
            var activation = new CountedSignal<bool>();
            var candidate = new AkcssUtilityCandidateActivator("paint", 0,
                [ApplicationFor(definition)],
                arguments: [AkcssUtilityValueSource.CreateObservable<object, object>(_ => keys, static value => value)],
                variant: AkcssUtilityValueSource.CreateObservable<bool, bool>(_ => activation, static value => value));
            var panel = new StackPanel { Children = { target } };
            var window = new Window { Content = panel };
            var red = new object();
            var blue = new object();
            window.Resources[red] = Brushes.Red;
            window.Resources[blue] = Brushes.Blue;
            try
            {
                AkburaControl.SetAkcssTemplateStyles(target, [candidate]);
                Assert.Equal(0, keys.Subscriptions);
                Assert.Equal(0, activation.Subscriptions);
                window.Show();
                target.ApplyTemplate();
                Pump(window);
                var part = Presenter(target);
                var original = part.Background;
                Assert.Empty(target.Styles);
                Assert.Equal(0, definition.Builds);
                Assert.Equal(0, candidate.OperationCount);
                Assert.Empty(candidate.ConflictKeys);
                Assert.False(candidate.SupportsHotReloadReconciliation);

                keys.Send(red);
                activation.Send(false);
                Pump(window);
                var styles = Assert.IsType<Styles>(Assert.Single(target.Styles));
                var initial = Assert.Single(styles);
                Assert.Equal(original, part.Background);
                Assert.Equal(1, definition.Builds);
                activation.Send(true);
                Assert.Equal(Brushes.Red, part.Background);
                activation.Send(false);
                Assert.Equal(original, part.Background);
                activation.Send(true);
                AkburaControl.ExecuteAkcssStyles(target);
                Assert.Same(initial, Assert.Single(styles));
                Assert.Equal(1, definition.Builds);

                PublishFromWorker(() => keys.Send(blue));
                Assert.Equal(1, definition.Builds);
                Assert.Same(initial, Assert.Single(styles));
                Pump(window);
                var replacement = Assert.Single(styles);
                Assert.NotSame(initial, replacement);
                Assert.Same(red, definition.FirstSnapshot![0]);
                Assert.Equal(Brushes.Blue, part.Background);
                Assert.Throws<InvalidOperationException>(() => keys.Send(ResourceFragment.BadKey));
                Assert.Same(replacement, Assert.Single(styles));
                Assert.Equal(Brushes.Blue, part.Background);
                keys.Send(red);
                Pump(window);
                Assert.Equal(Brushes.Red, part.Background);

                PublishFromWorker(() => keys.Send(blue));
                panel.Children.Remove(target);
                Pump(window);
                Assert.Empty(target.Styles);
                Assert.Equal(0, keys.Count);
                Assert.Equal(0, activation.Count);
                Assert.Equal(1, keys.Subscriptions);
                Assert.Equal(1, activation.Subscriptions);
                keys.SendToRetired(blue);
                activation.SendToRetired(true);
                Assert.Empty(target.Styles);

                panel.Children.Add(target);
                Pump(window);
                Assert.Equal(1, keys.Count);
                Assert.Equal(1, activation.Count);
                Assert.Equal(2, keys.Subscriptions);
                Assert.Equal(2, activation.Subscriptions);
                Assert.Empty(target.Styles);
                keys.Send(blue);
                activation.Send(true);
                Pump(window);
                Assert.Equal(Brushes.Blue, Presenter(target).Background);
                Assert.Single(target.Styles.OfType<Styles>());
                AkburaControl.ReplaceAkcssStylesForHotReload(target, []);
                Assert.Empty(target.Styles);
                Assert.Equal(0, keys.Count);
                Assert.Equal(0, activation.Count);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FragmentsOrderPerDestinationAndValidateProposedMetadataBeforeInstallation()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var left = new Border { Classes = { "tile" } };
            var overlapping = new Border { Classes = { "tile", "accent" } };
            var child = new StackPanel { Children = { left, overlapping } };
            var host = new StackPanel { Children = { child } };
            var window = new Window { Content = host };
            var colors = new CountedSignal<bool>();
            var lower = new AkcssUtilityCandidateActivator("base", 0,
                [ApplicationFor(new DescendantFragment("tile", Brushes.Red))]);
            var higher = new AkcssUtilityCandidateActivator("variant", 1,
                [ApplicationFor(new DescendantFragment("accent", Brushes.Blue))],
                variant: AkcssUtilityValueSource.CreateObservable<bool, bool>(_ => colors, static value => value));
            var old = System.Collections.Immutable.ImmutableArray.Create<AkcssStyleActivator>(lower, higher);
            try
            {
                window.Show();
                AkburaControl.SetAkcssStyles(host, old);
                colors.Send(true);
                Pump(window);
                Assert.Equal(Brushes.Red, left.Background);
                Assert.Equal(Brushes.Blue, overlapping.Background);
                Assert.Null(child.Background);
                var container = Assert.IsType<Styles>(Assert.Single(host.Styles));
                var lowerStyle = container[0];
                var higherStyle = container[1];
                colors.Send(false);
                Assert.Equal(Brushes.Red, overlapping.Background);
                colors.Send(true);
                Assert.Equal(Brushes.Blue, overlapping.Background);
                Assert.Same(lowerStyle, container[0]);
                Assert.Same(higherStyle, container[1]);

                var another = new Border { Classes = { "tile", "accent" } };
                child.Children.Add(another);
                Pump(window);
                Assert.Equal(Brushes.Blue, another.Background);

                var invalid = new AkcssUtilityCandidateActivator("grouped", 2,
                    [ApplicationFor(new DescendantFragment("accent", Brushes.Green))],
                    condition: static () => true, order: 10d, conflictGroup: "interaction",
                    unprefixedPrecedence: UnprefixedUtilityPrecedence.Above);
                Assert.Throws<InvalidOperationException>(() => AkburaControl.ReplaceAkcssStylesForHotReload(host, [lower, higher, invalid]));
                Assert.Equal(old, AkburaControl.GetAkcssStyles(host));
                Assert.Equal(Brushes.Blue, another.Background);
                Assert.Same(container, Assert.Single(host.Styles));

                var pendingVariant = new CountedSignal<bool>();
                var runtimePriority = new AkcssUtilityCandidateActivator("runtime", 3,
                    [ApplicationFor(new DescendantFragment("tile", Brushes.Green))],
                    variant: AkcssUtilityValueSource.CreateObservableWithPriority<bool, bool>(_ =>
                        new AkcssUtilityPrefixInvocation<IObservable<bool>?>(pendingVariant, BindingPriority.Template), static value => value));
                Assert.Throws<InvalidOperationException>(() => AkcssRuntime.SetStyles(host, [lower, runtimePriority]));
                Assert.Equal(0, pendingVariant.Count);
                Assert.Equal(old, AkcssRuntime.GetStyles(host));
                Assert.Same(container, Assert.Single(host.Styles));
                Assert.Equal(Brushes.Blue, another.Background);
                Assert.Equal(1, colors.Count);
                colors.Send(true);
                Pump(window);

                AkburaControl.ReplaceAkcssStylesForHotReload(host, [higher, lower]);
                colors.Send(true);
                Pump(window);
                Assert.Equal(Brushes.Red, overlapping.Background);
                Assert.Equal(Brushes.Red, another.Background);
                Assert.Same(container, Assert.Single(host.Styles));
                AkburaControl.ReplaceAkcssStylesForHotReload(host, [higher]);
                colors.Send(true);
                Pump(window);
                Assert.Null(left.Background);
                Assert.Equal(Brushes.Blue, another.Background);

                var below = new AkcssUtilityCandidateActivator("below", 4,
                    [ApplicationFor(new DescendantFragment("accent", Brushes.Green))],
                    condition: static () => true, order: 20d, conflictGroup: "low",
                    unprefixedPrecedence: UnprefixedUtilityPrecedence.Below);
                AkburaControl.ReplaceAkcssStylesForHotReload(host, [lower, below]);
                Pump(window);
                Assert.Equal(Brushes.Red, another.Background);
                AkburaControl.ReplaceAkcssStylesForHotReload(host, [below]);
                Pump(window);
                Assert.Null(left.Background);
                Assert.Equal(Brushes.Green, another.Background);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }


    [Fact]
    public async Task FullReconciliationCanRollbackFragmentDeployment()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var part = new Border { Classes = { "tile" } };
            var target = new StackPanel { Children = { part } };
            var window = new Window { Content = target };
            var original = new AkcssUtilityCandidateActivator("candidate", 0,
                [ApplicationFor(new DescendantFragment("tile", Brushes.Red))]);
            var replacement = new AkcssUtilityCandidateActivator("candidate", 0,
                [ApplicationFor(new DescendantFragment("tile", Brushes.Blue))]);
            try
            {
                window.Show();
                AkburaControl.SetAkcssStyles(target, [original]);
                Pump(window);
                var fragmentContainer = Assert.Single(target.Styles.OfType<Styles>());
                Assert.Equal(Brushes.Red, part.Background);

                var transition = AkcssRuntime.Reconcile(target, [new("slot", "utility", "changed", "revision")],
                    _ => replacement);
                Pump(window);
                Assert.Equal(Brushes.Blue, part.Background);
                Assert.Same(replacement, Assert.Single(AkburaControl.GetAkcssStyles(target)));

                transition.Rollback();
                Pump(window);
                Assert.Equal(Brushes.Red, part.Background);
                Assert.Same(original, Assert.Single(AkburaControl.GetAkcssStyles(target)));
                Assert.Same(fragmentContainer, Assert.Single(target.Styles.OfType<Styles>()));
                Assert.Single(fragmentContainer);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static void PublishFromWorker(Action publish)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                Assert.False(Dispatcher.UIThread.CheckAccess());
                publish();
                done.SetResult();
            }
            catch (Exception error)
            {
                done.SetException(error);
            }
        }) { IsBackground = true };
        worker.Start();
        done.Task.GetAwaiter().GetResult();
    }

    private static AkcssUtilityApplication ApplicationFor(AkcssUtility definition) =>
        new(definition, static (_, _) => throw new InvalidOperationException("A native fragment used the scalar execution path."));

    private sealed class ResourceFragment : AkcssUtility<object>, IAkcssNativeStyleProvider
    {
        public static readonly object BadKey = new();
        public Type NativeTargetType => typeof(Button);
        public int Builds { get; private set; }
        public IReadOnlyList<object?>? FirstSnapshot { get; private set; }

        public override void Update(object target, object key) => throw new NotSupportedException();

        public void BuildNativeStyle(Style root, IReadOnlyList<object?> arguments)
        {
            Builds++;
            FirstSnapshot ??= arguments;
            if (ReferenceEquals(arguments[0], BadKey))
            {
                throw new InvalidOperationException("Test factory failure.");
            }
            root.Children.Add(new Style(selector => selector.Nesting().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
            {
                Setters = { new Setter(ContentPresenter.BackgroundProperty, new DynamicResourceExtension(arguments[0]!)) },
            });
        }
    }

    private sealed class DescendantFragment(string targetClass, IBrush background) : ZeroAkcssUtility, IAkcssNativeStyleProvider
    {
        public Type NativeTargetType => typeof(StackPanel);

        public override void Update(object target) => throw new NotSupportedException();

        public void BuildNativeStyle(Style root, IReadOnlyList<object?> arguments) =>
            root.Children.Add(new Style(selector => selector.Nesting().Descendant().OfType<Border>().Class(targetClass))
            {
                Setters = { new Setter(Border.BackgroundProperty, background) },
            });
    }

    internal sealed class CountedSignal<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _current = [];
        private readonly List<IObserver<T>> _retired = [];
        public int Count => _current.Count;
        public int Subscriptions { get; private set; }

        public IDisposable Subscribe(IObserver<T> observer)
        {
            Subscriptions++;
            _current.Add(observer);
            return new Subscription(this, observer);
        }

        public void Send(T value)
        {
            foreach (var observer in _current.ToArray())
            {
                observer.OnNext(value);
            }
        }

        public void SendToRetired(T value)
        {
            foreach (var observer in _retired)
            {
                observer.OnNext(value);
            }
        }

        private sealed class Subscription(CountedSignal<T> owner, IObserver<T> observer) : IDisposable
        {
            public void Dispose()
            {
                if (owner._current.Remove(observer))
                {
                    owner._retired.Add(observer);
                }
            }
        }
    }
}
