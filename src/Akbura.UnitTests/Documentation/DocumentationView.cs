using Akbura.ComponentTree;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Akbura.UnitTests;

/// <summary>
/// A real initialized component in a native headless Window. Mutations are followed
/// only by dispatcher/layout processing, never by manually calling generated Update().
/// </summary>
internal sealed class DocumentationView : IDisposable
{
    private readonly Window _window;

    public DocumentationView(CompiledDocumentationExample example, object? dataContext = null)
    {
        var application = Assert.IsAssignableFrom<Application>(Application.Current);
        if (!application.Styles.OfType<FluentTheme>().Any())
        {
            application.Styles.Add(new FluentTheme());
        }

        Owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(example.Type));
        Owner.DataContext = dataContext;
        _window = new Window { Width = 640d, Height = 480d };
        try
        {
            _window.Content = Owner;
            _window.Show();
            Flush();
            Assert.NotNull(Owner.Child);
        }
        catch
        {
            _window.Close();
            _window.Content = null;
            throw;
        }
    }

    public AkburaControl Owner { get; }
    public Control Root => Assert.IsAssignableFrom<Control>(Owner.Child);

    public void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        _window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        _window.UpdateLayout();
    }

    public void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Flush();
    }

    public State GetState(string name)
    {
        var method = Owner.GetType().GetMethod("DocumentationStates")
            ?? throw new MissingMethodException(Owner.GetType().FullName, "DocumentationStates");
        var states = (ImmutableArray<State>)Invoke(method, Owner)!;
        return Assert.Single(states, state => state.Info?.Name == name);
    }

    public T Value<T>(string name) => Assert.IsType<State<T>>(GetState(name)).Value;

    public void SetValue<T>(string name, T value)
    {
        Assert.IsType<State<T>>(GetState(name)).Value = value;
        Flush();
    }

    public object? Value(string name)
    {
        var state = GetState(name);
        return state.GetType().GetProperty("Value")!.GetValue(state);
    }

    // The fixture Person type belongs to the emitted example assembly; do not
    // replace it with an unrelated type from the test assembly.
    public void SetObjectValue(string name, object value)
    {
        var state = GetState(name);
        Invoke(state.GetType().GetProperty("Value")!.SetMethod!, state, value);
        Flush();
    }

    public object CreatePerson(string name, bool selected, bool canEdit)
    {
        var type = Owner.GetType().Assembly.GetType("Demo.Person", throwOnError: true)!;
        var person = Activator.CreateInstance(type)!;
        type.GetProperty("Name")!.SetValue(person, name);
        type.GetProperty("Selected")!.SetValue(person, selected);
        type.GetProperty("CanEdit")!.SetValue(person, canEdit);
        return person;
    }

    public static string[] DirectText(StackPanel panel) =>
        panel.Children.OfType<TextBlock>().Select(text => text.Text ?? string.Empty).ToArray();

    public static void AssertDirectText(StackPanel panel, params string[] expected)
    {
        Assert.Equal(expected, DirectText(panel));
    }

    public static void AssertDirectParents(StackPanel panel)
    {
        foreach (var child in panel.Children)
        {
            Assert.Same(panel, child.Parent);
            Assert.Same(panel, child.GetVisualParent());
        }
    }

    public void Dispose()
    {
        _window.Close();
        _window.Content = null;
        Dispatcher.UIThread.RunJobs();
    }

    private static object? Invoke(MethodInfo method, object target, params object?[] arguments)
    {
        try
        {
            return method.Invoke(target, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}
