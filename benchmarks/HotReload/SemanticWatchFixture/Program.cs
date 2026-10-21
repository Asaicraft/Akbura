using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Demo;
using System.Reflection;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var owner = new Counter();
        var window = new Window { Content = owner };
        window.Show();
        var panel = (StackPanel)((Border)owner.Child!).Child!;
        var target = (TextBlock)panel.Children[0];
        var input = (TextBox)panel.Children[1];
        var first = (TextBlock)panel.Children[3];
        var last = (TextBlock)panel.Children[4];
        input.Text = "edited by user";
        var count = typeof(Counter).GetProperty("count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        count.SetValue(owner, 42);
        string? previous = null;
        int? pending = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        void Observe()
        {
            if (pending is { } observed)
            {
                if (first.Text != WatchPerson.Current.FirstName || last.Text != WatchPerson.Current.LastName || WatchPerson.Current.Subscribers != 1)
                {
                    throw new InvalidOperationException("Reactive bindings are stale or subscriptions were duplicated.");
                }
                Console.WriteLine($"PROBE_VALUE {observed} pid={Environment.ProcessId} same_control=True state_preserved=True input_preserved=True live_bindings=2 subscriptions=1");
                pending = null;
            }
            var currentPanel = (StackPanel)((Border)owner.Child!).Child!;
            var current = (TextBlock)currentPanel.Children[0];
            if (current.Text == previous)
            {
                return;
            }
            previous = current.Text;
            if (!ReferenceEquals(current, target) || !ReferenceEquals(input, currentPanel.Children[1]) ||
                !ReferenceEquals(first, currentPanel.Children[3]) || !ReferenceEquals(last, currentPanel.Children[4]) ||
                input.Text != "edited by user" || (int)count.GetValue(owner)! != 42 || WatchPerson.Current.Subscribers != 1)
            {
                throw new InvalidOperationException("Control identity, state, input or independent subscription ownership changed.");
            }
            WatchPerson.Current.FirstName = "First " + current.Text;
            WatchPerson.Current.LastName = "Last " + current.Text;
            pending = current.Text == "Akbura Counter" ? 1 : int.Parse(current.Text!["Akbura Counter ".Length..]) + 1;
        }
        timer.Tick += (_, _) =>
        {
            try
            {
                Observe();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("Unhandled exception. " + exception);
                Environment.Exit(1);
            }
        };
        timer.Start();
        Console.WriteLine($"PROBE_READY pid={Environment.ProcessId}");
        Dispatcher.UIThread.MainLoop(CancellationToken.None);
    }
}
