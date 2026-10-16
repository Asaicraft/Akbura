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
        var root = (Border)owner.Child!;
        var panel = (StackPanel)root.Child!;
        var target = (TextBlock)panel.Children[0];
        var input = (TextBox)panel.Children[1];
        input.Text = "edited by user";
        var count = typeof(Counter).GetProperty("count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        count.SetValue(owner, 42);
        string? previous = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            var current = (TextBlock)((StackPanel)((Border)owner.Child!).Child!).Children[0];
            if (current.Text == previous)
            {
                return;
            }
            previous = current.Text;
            if (input.Text != "edited by user" || (int)count.GetValue(owner)! != 42)
            {
                throw new InvalidOperationException("User input or component state was lost.");
            }
            var value = current.Text == "Akbura Counter" ? 1 : int.Parse(current.Text!["Akbura Counter ".Length..]) + 1;
            Console.WriteLine($"PROBE_VALUE {value} pid={Environment.ProcessId} same_control={ReferenceEquals(current, target)} state_preserved=True input_preserved=True");
        };
        timer.Start();
        Console.WriteLine($"PROBE_READY pid={Environment.ProcessId}");
        Dispatcher.UIThread.MainLoop(CancellationToken.None);
    }
}
