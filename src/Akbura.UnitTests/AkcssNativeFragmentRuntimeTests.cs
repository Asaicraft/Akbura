using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed partial class AkcssNativeFragmentRuntimeTests
{
    private static readonly AttachedProperty<object?> s_identity =
        AvaloniaProperty.RegisterAttached<Control, object?>("AkcssTestAnchor", typeof(AkcssNativeFragmentRuntimeTests));

    [Fact]
    public async Task NativeAnchorRetiresFramesOnAlreadyInstantiatedFluentTemplateParts()
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            Application.Current!.Styles.Add(new FluentTheme());
            var target = new Button { Content = "First" };
            var sibling = new Button { Content = "Other" };
            var window = new Window { Content = new StackPanel { Children = { target, sibling } } };
            try
            {
                window.Show();
                target.ApplyTemplate();
                sibling.ApplyTemplate();
                Pump(window);
                var presenter = Presenter(target);
                var other = Presenter(sibling);
                var original = presenter.Background;
                var otherBackground = other.Background;
                var identity = new object();
                target.SetValue(s_identity, identity);
                var container = new Styles();
                target.Styles.Add(container);
                var pseudo = (IPseudoClasses)target.Classes;
                for (var index = 0; index < 3; index++)
                {
                    var gate = ":akcss-test-" + index;
                    var root = new Style(selector => selector.PropertyEquals(s_identity, identity).Class(gate))
                    {
                        Children =
                        {
                            new Style(selector => selector.Nesting().Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
                            {
                                Setters = { new Setter(ContentPresenter.BackgroundProperty, new DynamicResourceExtension("fragment")) },
                            },
                        },
                    };
                    window.Resources["fragment"] = Brushes.Red;
                    container.Add(root);
                    Pump(window);
                    Assert.Equal(original, presenter.Background);

                    pseudo.Add(gate);
                    Assert.Equal(Brushes.Red, presenter.Background);
                    Assert.Equal(otherBackground, other.Background);
                    window.Resources["fragment"] = Brushes.Blue;
                    Assert.Equal(Brushes.Blue, presenter.Background);

                    pseudo.Remove(gate);
                    Assert.Equal(original, presenter.Background);
                    container.Remove(root);
                    // Explicitly retire frames in the visual branch that is outside the logical traversal.
                    ((IStyleHost)presenter).StylesRemoved([root]);
                    pseudo.Add(gate);
                    window.Resources["fragment"] = Brushes.Green;
                    Assert.Equal(original, presenter.Background);
                    pseudo.Remove(gate);
                }
                Assert.Empty(container);
                target.Styles.Remove(container);
                target.ClearValue(s_identity);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static ContentPresenter Presenter(Button button) => Assert.Single(
        button.GetVisualDescendants().OfType<ContentPresenter>(), part => part.Name == "PART_ContentPresenter");

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
