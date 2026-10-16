using Akbura;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PurityUIDashborad;
using PurityUIDashborad.Infrastructure;
using System.Reflection;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .UseAkburaApplication().SetupWithoutStarting();
        var sidebar = Environment.GetEnvironmentVariable("AKBURA_TIMING_COMPONENT") == "Sidebar";
        var name = sidebar ? "Sidebar" : "NeedHelp";
        var component = (Control)Activator.CreateInstance(typeof(App).Assembly.GetType("PurityUIDashborad.Components." + name)!, nonPublic: true)!;
        var window = new Window { Content = component, Width = 1100, Height = 720 };
        window.Show();
        var original = FindTarget();
        var originalNamed = sidebar ? FindNamed() : null;
        var foreground = (original as TextBlock)?.Foreground?.ToString();
        var expanded = sidebar ? component.GetType().GetProperty("isExpanded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) : null;
        var originalState = expanded?.GetValue(component);
        var previous = double.NaN;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            var current = FindTarget();
            var text = (current as TextBlock)?.Text;
            var value = sidebar ? text == "Dashboard" ? 1 : double.Parse(text!["Dashboard ".Length..]) : ((Ellipse)current).StrokeThickness;
            if (value == previous)
            {
                return;
            }
            previous = value;
            if (!ReferenceEquals(window.Content, component) || !ReferenceEquals(original, current))
            {
                throw new InvalidOperationException("The live owner or target control was replaced.");
            }
            if (sidebar)
            {
                if (!ReferenceEquals(originalNamed, FindNamed()) || !Equals(originalState, expanded!.GetValue(component)) ||
                    foreground != ((TextBlock)current).Foreground?.ToString())
                {
                    throw new InvalidOperationException("Sidebar identity, state or foreground was not preserved.");
                }
                var brand = component.GetVisualDescendants().OfType<AkburaControl>().First(control => control.GetType().Name == "SidebarBrand");
                var command = (IAkburaCommand)brand.GetType().GetProperty("ToggleSidebar")!.GetValue(brand)!;
                command.Execute().GetAwaiter().GetResult();
                if (Equals(originalState, expanded.GetValue(component)))
                {
                    throw new InvalidOperationException("ToggleSidebar did not update the live state.");
                }
                command.Execute().GetAwaiter().GetResult();
                if (!Equals(originalState, expanded.GetValue(component)) || foreground != ((TextBlock)current).Foreground?.ToString())
                {
                    throw new InvalidOperationException("ToggleSidebar did not restore state and foreground.");
                }
            }
            Console.WriteLine($"PROBE_VALUE {value:R} pid={Environment.ProcessId} same_control=True same_owner=True sidebar_checks={sidebar}");
        };
        timer.Start();
        Console.WriteLine($"PROBE_READY pid={Environment.ProcessId}");
        Dispatcher.UIThread.MainLoop(CancellationToken.None);

        Control FindNamed() => component.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "dashboard");
        Control FindTarget() => sidebar
            ? component.GetVisualDescendants().OfType<TextBlock>().First(control => control.Text?.StartsWith("Dashboard", StringComparison.Ordinal) == true)
            : component.GetVisualDescendants().OfType<Ellipse>().First();
    }
}
