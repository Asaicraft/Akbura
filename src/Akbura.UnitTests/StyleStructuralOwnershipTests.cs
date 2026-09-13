using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class StyleStructuralOwnershipTests
{
    [Fact]
    public async Task StructuralRevision_OmittingStylesAfterOrdinaryUpdatePreservesForeignStylesAndLiveControl()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var state = new AkburaRenderState();
            Begin(state, "with-styles");
            var border = state.GetRequired<Border>(0);
            var button = state.GetRequired<Button>(1);
            border.Child = button;
            var foreignBefore = new Style(selector => selector.OfType<Button>())
            {
                Setters = { new Setter(Visual.OpacityProperty, 0.75d), new Setter(Control.HeightProperty, 73d) },
            };
            var foreignAfter = new Style(selector => selector.OfType<Button>())
            {
                Setters = { new Setter(Control.WidthProperty, 85d) },
            };
            var initialOwned = CreateOwnedStyle(0.25);
            border.Styles.Add(foreignBefore);
            state.ReconcileCollection<IStyle>(0, "Styles", border.Styles, [initialOwned]);
            border.Styles.Add(foreignAfter);
            state.CompleteRevision();
            var window = new Window { Content = border };
            try
            {
                window.Show();
                AkburaRenderStyleHelper.ApplyStyling(border);
                Assert.Equal(0.25, button.Opacity);
                Assert.Equal(73d, button.Height);
                Assert.Equal(85d, button.Width);

                // Ordinary reactive updates replace the local Style objects, but
                // the structural slot must retain ownership of their replacements.
                var updatedOwned = CreateOwnedStyle(0.4);
                state.ReconcileCollection<IStyle>(0, "Styles", border.Styles, [updatedOwned]);
                AkburaRenderStyleHelper.ApplyStyling(border);
                Assert.Equal(0.4, button.Opacity);
                Assert.DoesNotContain(initialOwned, border.Styles);
                Assert.Equal<IStyle>([foreignBefore, updatedOwned, foreignAfter], border.Styles);

                // The new compiled revision has the same live controls and no
                // Styles assignment at all, rather than an explicit empty list.
                Begin(state, "without-styles");
                Assert.Same(border, state.GetRequired<Border>(0));
                Assert.Same(button, state.GetRequired<Button>(1));
                state.CompleteRevision();
                AkburaRenderStyleHelper.ApplyStyling(border);

                Assert.Same(button, border.Child);
                Assert.Equal<IStyle>([foreignBefore, foreignAfter], border.Styles);
                Assert.DoesNotContain(updatedOwned, border.Styles);
                Assert.Equal(0.75, button.Opacity);
                Assert.Equal(73d, button.Height);
                Assert.Equal(85d, button.Width);

                var restoredOwned = CreateOwnedStyle(0.6);
                Begin(state, "styles-restored");
                state.ReconcileCollection<IStyle>(0, "Styles", border.Styles, [restoredOwned]);
                state.CompleteRevision();
                AkburaRenderStyleHelper.ApplyStyling(border);

                Assert.Same(border, state.GetRequired<Border>(0));
                Assert.Same(button, state.GetRequired<Button>(1));
                Assert.Same(button, border.Child);
                Assert.Equal<IStyle>([foreignBefore, restoredOwned, foreignAfter], border.Styles);
                Assert.Equal(0.6, button.Opacity);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Style CreateOwnedStyle(double opacity) =>
        new(selector => selector.OfType<Button>())
        {
            Setters = { new Setter(Visual.OpacityProperty, opacity) },
        };

    private static void Begin(AkburaRenderState state, string revision) =>
        Assert.True(state.BeginRevision(revision, builder =>
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Border), null, "StyleOwner"));
            builder.Add(new AkburaRenderNodeDefinition(1, 0, "Child", typeof(Button), null, "LiveButton"));
        }, localId => localId == 0 ? new Border() : new Button()));
}
