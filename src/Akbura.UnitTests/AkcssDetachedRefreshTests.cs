using Akbura.Akcss;
using Avalonia.Controls;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkcssDetachedRefreshTests
{
    [Fact]
    public async Task DebugRefresh_LeavesDetachedDynamicSourcesUnsubscribed()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var control = new Border();
            var signal = new CountingSignal();
            var factories = 0;
            var utility = new WidthUtility();
            var candidate = new AkcssUtilityCandidateActivator("width", 0,
                [new AkcssUtilityApplication(utility, (target, values) => utility.Update(target, (double)values[0]!))],
                [AkcssUtilityValueSource.CreateObservable<double, double>(_ =>
                {
                    factories++;
                    return signal;
                }, static value => value, recreateOnRefresh: true)]);
            AkburaControl.SetAkcssStyles(control, [candidate]);
            var window = new Window { Content = control };
            try
            {
                window.Show();
                var attachedFactories = factories;
                Assert.Equal(1, signal.Subscribers);
                window.Content = null;
                Assert.Equal(0, signal.Subscribers);
                AkburaControl.RefreshAkcssStylesForHotReload(control);
                Assert.Equal(0, signal.Subscribers);
                Assert.Equal(attachedFactories, factories);
                window.Content = control;
                Assert.Equal(1, signal.Subscribers);
                Assert.Equal(attachedFactories + 1, factories);
                Assert.Equal(12, control.Width);
            }
            finally
            {
                window.Close();
            }
            Assert.Equal(0, signal.Subscribers);
        }, CancellationToken.None);
    }

    private sealed class WidthUtility : AkcssUtility<double>
    {
        public override void Update(object target, double value) => ((Border)target).Width = value;
        public override void Reset(object target) => ((Border)target).ClearValue(Control.WidthProperty);
    }

    private sealed class CountingSignal : IObservable<double>
    {
        public int Subscribers { get; private set; }

        public IDisposable Subscribe(IObserver<double> observer)
        {
            Subscribers++;
            observer.OnNext(12);
            return new Subscription(() => Subscribers--);
        }
    }

    private sealed class Subscription(Action detach) : IDisposable
    {
        private Action? _detach = detach;
        public void Dispose() => Interlocked.Exchange(ref _detach, null)?.Invoke();
    }
}
