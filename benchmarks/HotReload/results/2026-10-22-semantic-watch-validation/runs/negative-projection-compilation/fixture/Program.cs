using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Demo;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    private sealed record Command(int Revision, double Margin, double Width, string Title, int CompilationMarker, string Phase);

    [STAThread]
    public static void Main()
    {
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        var owner = new Counter();
        var window = new Window { Content = owner };
        window.Show();
        var border = (Border)owner.Child!;
        var panel = (StackPanel)border.Child!;
        var controls = panel.Children.ToArray();
        var input = (TextBox)controls[1];
        input.Text = "edited by user";
        var count = typeof(Counter).GetProperty("count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        count.SetValue(owner, 42);
        var commandPath = Environment.GetEnvironmentVariable("AKBURA_WATCH_COMMAND")!;
        var previousWrite = DateTime.MinValue;
        var revision = 0;
        using var process = Process.GetCurrentProcess();
        Command? command = null;
        var notified = false;
        void Observe()
        {
            var modified = File.GetLastWriteTimeUtc(commandPath);
            if (modified != previousWrite && File.Exists(commandPath))
            {
                var incoming = JsonSerializer.Deserialize<Command>(File.ReadAllText(commandPath))!;
                if (incoming.Revision != revision + 1)
                {
                    throw new InvalidOperationException("Duplicate, stale or out-of-order observer command.");
                }
                command = incoming;
                previousWrite = modified;
                notified = false;
            }
            if (command == null || border.Margin != new Thickness(command.Margin) || border.Width != command.Width ||
                ((TextBlock)controls[0]).Text != command.Title || CompilationControl.Marker != command.CompilationMarker)
            {
                return;
            }
            if (!ReferenceEquals(owner.Child, border) || !ReferenceEquals(border.Child, panel) ||
                !controls.SequenceEqual(panel.Children, ReferenceEqualityComparer.Instance) ||
                input.Text != "edited by user" || (int)count.GetValue(owner)! != 42 || WatchPerson.Current.Subscribers != 1)
            {
                throw new InvalidOperationException("Control identity, input, state or shared subscription ownership changed.");
            }
            if (!notified)
            {
                notified = true;
                WatchPerson.Current.FirstName = "First " + command.Revision;
                WatchPerson.Current.LastName = "Last " + command.Revision;
                return;
            }
            if (((TextBlock)controls[3]).Text != WatchPerson.Current.FirstName || ((TextBlock)controls[4]).Text != WatchPerson.Current.LastName ||
                ((TextBlock)controls[5]).Text != WatchPerson.Current.FirstName || ((TextBlock)controls[6]).Text != WatchPerson.Current.LastName)
            {
                return;
            }
            var heap = command.Phase == "memory" ? GC.GetTotalMemory(true) : GC.GetTotalMemory(false);
            process.Refresh();
            Console.WriteLine("PROBE_JSON " + JsonSerializer.Serialize(new { Kind = "ack", command.Revision, command.Phase,
                Pid = Environment.ProcessId, ActualMargin = border.Margin.Left, StatePreserved = true, InputPreserved = true,
                ControlsPreserved = true, BindingsValid = true, Subscriptions = WatchPerson.Current.Subscribers,
                ManagedHeapBytes = heap, PostGc = command.Phase == "memory", WorkingSetBytes = process.WorkingSet64,
                AllocatedBytes = GC.GetTotalAllocatedBytes(false), Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2) }));
            revision = command.Revision;
            command = null;
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            try
            {
                Observe();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("PROBE_FAILURE " + exception);
                Environment.Exit(1);
            }
        };
        timer.Start();
        Console.WriteLine("PROBE_JSON " + JsonSerializer.Serialize(new { Kind = "ready", Pid = Environment.ProcessId, Margin = border.Margin.Left }));
        Dispatcher.UIThread.MainLoop(CancellationToken.None);
    }
}
