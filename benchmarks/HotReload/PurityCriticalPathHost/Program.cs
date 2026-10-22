using Akbura;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PurityUIDashborad;
using PurityUIDashborad.Infrastructure;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
    private sealed record Command(int Revision, string Nonce, string Scenario, double Margin, string Title);

    [STAThread]
    public static void Main()
    {
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).UseAkburaApplication().SetupWithoutStarting();
        var owner = (AkburaControl)Activator.CreateInstance(typeof(App).Assembly.GetType("PurityUIDashborad.Components.Sidebar")!, nonPublic: true)!;
        var window = new Window { Content = owner, Width = 1100, Height = 720 };
        window.Show();
        var state = owner.GetType().GetProperty("isExpanded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var commandPath = Environment.GetEnvironmentVariable("AKBURA_CRITICAL_COMMAND")!;
        var revision = 0;
        Control? originalRoot = null;
        Control? dashboard = null;
        TextBlock? label = null;
        AkburaControl[]? components = null;
        object? originalState = null;
        string? foreground = null;
        Command? command = null;
        long observedAt = 0;
        var previousAllocated = GC.GetTotalAllocatedBytes(false);
        using var process = Process.GetCurrentProcess();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (originalRoot == null)
                {
                    originalRoot = owner.Child!;
                    dashboard = owner.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "dashboard");
                    label = dashboard.GetVisualDescendants().OfType<TextBlock>().First(control => control.Text == "Dashboard");
                    components = owner.GetVisualDescendants().OfType<AkburaControl>().ToArray();
                    originalState = state.GetValue(owner);
                    foreground = label.Foreground?.ToString();
                    Emit(new { Kind = "ready", Pid = Environment.ProcessId, Margin = originalRoot.Margin.Left, Components = components.Length });
                }
                if (command == null)
                {
                    var text = ReadCommand(commandPath);
                    if (text == null)
                    {
                        return;
                    }
                    var incoming = JsonSerializer.Deserialize<Command>(text)!;
                    if (incoming.Revision == revision)
                    {
                        return;
                    }
                    if (incoming.Revision != revision + 1)
                    {
                        throw new InvalidOperationException("Stale, skipped or duplicate critical-path revision.");
                    }
                    command = incoming;
                    observedAt = Stopwatch.GetTimestamp();
                }
                if (Stopwatch.GetElapsedTime(observedAt).TotalMinutes > 2)
                {
                    throw new TimeoutException("UI did not reach the expected Sidebar value: " + JsonSerializer.Serialize(command));
                }
                if (originalRoot.Margin != new Thickness(command.Margin) || label!.Text != command.Title)
                {
                    return;
                }
                var validationStarted = Stopwatch.GetTimestamp();
                if (!ReferenceEquals(window.Content, owner) || !ReferenceEquals(owner.Child, originalRoot) ||
                    !ReferenceEquals(dashboard, owner.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "dashboard")) ||
                    !components!.SequenceEqual(owner.GetVisualDescendants().OfType<AkburaControl>(), ReferenceEqualityComparer.Instance) ||
                    !Equals(originalState, state.GetValue(owner)) || foreground != label.Foreground?.ToString())
                {
                    throw new InvalidOperationException("Sidebar owner, named control, component identity, state or foreground changed.");
                }
                var brand = components!.First(control => control.GetType().Name == "SidebarBrand");
                var toggle = (IAkburaCommand)brand.GetType().GetProperty("ToggleSidebar")!.GetValue(brand)!;
                toggle.Execute().GetAwaiter().GetResult();
                if (Equals(originalState, state.GetValue(owner)))
                {
                    throw new InvalidOperationException("ToggleSidebar did not change the original owner's state.");
                }
                toggle.Execute().GetAwaiter().GetResult();
                if (!Equals(originalState, state.GetValue(owner)) || !ReferenceEquals(owner.Child, originalRoot) || foreground != label.Foreground?.ToString())
                {
                    throw new InvalidOperationException("ToggleSidebar did not restore the live component.");
                }
                process.Refresh();
                var allocated = GC.GetTotalAllocatedBytes(false);
                Emit(new { Kind = "ack", command.Revision, command.Nonce, command.Scenario, Pid = Environment.ProcessId,
                    ActualMargin = originalRoot.Margin.Left, ActualTitle = label.Text, OwnerPreserved = true, ControlsPreserved = true,
                    StatePreserved = true, CommandsValid = true, ValidationMs = Stopwatch.GetElapsedTime(validationStarted).TotalMilliseconds,
                    ApplicationAllocatedBytes = allocated, ApplicationAllocatedSincePreviousAck = allocated - previousAllocated,
                    ApplicationHeapBytes = GC.GetTotalMemory(false), ApplicationWorkingSetBytes = process.WorkingSet64,
                    CompilerAllocationsMeaning = "Unavailable in this application process; do not substitute application counters" });
                previousAllocated = allocated;
                revision = command.Revision;
                command = null;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("PROBE_FAILURE " + error);
                Environment.Exit(1);
            }
        };
        timer.Start();
        Dispatcher.UIThread.MainLoop(CancellationToken.None);
    }

    private static string? ReadCommand(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException && (error.HResult & 0xFFFF) is 2 or 3 or 5 or 32 or 33)
        {
            return null;
        }
    }

    private static void Emit(object value)
    {
        Console.WriteLine("CRITICAL_JSON " + JsonSerializer.Serialize(value));
    }
}
