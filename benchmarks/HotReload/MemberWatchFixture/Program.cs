using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Demo;
using System.Reflection;
using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(MemberMetadataObserver))]

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
        input.Text = "edited by user";
        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var count = typeof(Counter).GetProperty("count", flags)!;
        var tick = typeof(Counter).GetProperty("tick", flags)!;
        count.SetValue(owner, 42);
        string? previous = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            // A normal application state update drives rendering. Updating a service
            // method alone is not claimed to extend the runtime's type-selection graph.
            tick.SetValue(owner, (int)tick.GetValue(owner)! + 1);
            var current = (TextBlock)((StackPanel)((Border)owner.Child!).Child!).Children[0];
            if (current.Text == previous)
            {
                return;
            }
            previous = current.Text;
            if (!ReferenceEquals(target, current) || input.Text != "edited by user" || (int)count.GetValue(owner)! != 42 ||
                current.Text != MemberViewModel.Current.GetName())
            {
                throw new InvalidOperationException("Live member behavior, control identity, input or state was not preserved.");
            }
            var value = int.Parse(current.Text!["Akbura Counter ".Length..]) + 1;
            Console.WriteLine($"PROBE_VALUE {value} pid={Environment.ProcessId} same_control=True state_preserved=True input_preserved=True member_result=True utc={DateTime.UtcNow:o}");
        };
        timer.Start();
        Console.WriteLine($"PROBE_READY pid={Environment.ProcessId}");
        Dispatcher.UIThread.MainLoop(CancellationToken.None);
    }
}

internal static class MemberMetadataObserver
{
    public static void ClearCache(Type[]? updatedTypes)
    {
        Console.WriteLine($"MEMBER_METADATA_CLEAR pid={Environment.ProcessId} utc={DateTime.UtcNow:o}");
    }

    public static void UpdateApplication(Type[]? updatedTypes)
    {
        Console.WriteLine($"MEMBER_METADATA_APPLIED pid={Environment.ProcessId} utc={DateTime.UtcNow:o}");
    }
}
