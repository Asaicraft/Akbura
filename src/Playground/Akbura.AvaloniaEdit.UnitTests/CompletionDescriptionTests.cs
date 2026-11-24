using Akbura.Collections;
using Akbura.Pools;
using System.Collections.Immutable;
using Akbura.AvaloniaEdit.Completion;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using AvaloniaEdit.CodeCompletion;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class CompletionDescriptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UnsuccessfulDescriptionCanBeRetried(bool faulted) => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor();
        using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///DescriptionRetry.akbura"),
            new() { AutomaticCompletion = false });
        await session.Ready;
        await session.RequestCompletionAsync();
        var window = new Window { Content = editor, Width = 800, Height = 400 };
        CompletionWindow? completion = null;
        try
        {
            window.Show();
            completion = new CompletionWindow(editor.TextArea) { ShouldUseOverlayLayer = true, CloseAutomatically = false };
            var items = NativeItems(session, 2);
            Assert.Equal(2, items.Length);
            var calls = 0;
            using var description = new CompletionDescriptionPopup(completion, (item, _) =>
            {
                if (!ReferenceEquals(item, items[0])) return Task.FromResult<string?>("Other documentation");
                if (++calls > 1) return Task.FromResult<string?>("Retried documentation");
                return faulted ? Task.FromException<string?>(new InvalidOperationException("Temporary failure"))
                    : Task.FromResult<string?>(null);
            });
            description.Popup.ShouldUseOverlayLayer = true;
            foreach (var item in items) completion.CompletionList.CompletionData.Add(item);
            completion.Show();
            completion.CompletionList.SelectedItem = items[0];
            completion.CompletionList.SelectedItem = items[1];
            completion.CompletionList.SelectedItem = items[0];
            await Until(() => IsArranged(description.Popup));
            await Until(() => DescriptionText(description.Popup) == "Retried documentation");
            Assert.Equal(2, calls);
        }
        finally { completion?.Hide(); window.Close(); }
    });

    [Fact]
    public Task ReturningToAnItemSharesItsPendingAndSuccessfulDescription() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor();
        using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///DescriptionCache.akbura"),
            new() { AutomaticCompletion = false });
        await session.Ready;
        await session.RequestCompletionAsync();
        var window = new Window { Content = editor, Width = 800, Height = 400 };
        CompletionWindow? completion = null;
        try
        {
            window.Show();
            completion = new CompletionWindow(editor.TextArea) { ShouldUseOverlayLayer = true, CloseAutomatically = false };
            var items = NativeItems(session, 2);
            var pending = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            using var description = new CompletionDescriptionPopup(completion, (_, _) => { calls++; return pending.Task; });
            description.Popup.ShouldUseOverlayLayer = true;
            foreach (var item in items) completion.CompletionList.CompletionData.Add(item);
            completion.Show();
            completion.CompletionList.SelectedItem = items[0];
            completion.CompletionList.SelectedItem = items[1];
            completion.CompletionList.SelectedItem = items[0];
            Assert.Equal(2, calls);
            pending.TrySetResult("Cached documentation");
            await Until(() => IsArranged(description.Popup));
            await Until(() => DescriptionText(description.Popup) == "Cached documentation");
            completion.CompletionList.SelectedItem = items[1];
            completion.CompletionList.SelectedItem = items[0];
            await Until(() => DescriptionText(description.Popup) == "Cached documentation");
            Assert.Equal(2, calls);
        }
        finally { completion?.Hide(); window.Close(); }
    });

    [Theory]
    [InlineData(32, false)]
    [InlineData(650, true)]
    public Task OverlayDescriptionStaysBesideTheListAndFlipsAtTheRightEdge(int leftMargin, bool flipped) => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor { Margin = new Thickness(leftMargin, 20, 0, 0) };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Placement.akbura"),
            new() { AutomaticCompletion = false });
        await session.Ready;
        await session.RequestCompletionAsync();
        var window = new Window { Content = editor, Width = 900, Height = 500 };
        CompletionWindow? completion = null;
        try
        {
            window.Show();
            editor.TextArea.Focus();
            completion = new CompletionWindow(editor.TextArea) { ShouldUseOverlayLayer = true, CloseAutomatically = false };
            using var description = new CompletionDescriptionPopup(completion, (item, _) =>
                Task.FromResult<string?>("Documentation describing the selected completion: " + item.DisplayText));
            description.Popup.ShouldUseOverlayLayer = true;
            var items = NativeItems(session, 3);
            Assert.Equal(3, items.Length);
            foreach (var item in items)
            {
                completion.CompletionList.CompletionData.Add(item);
            }
            completion.Show();
            completion.CompletionList.SelectedItem = items[0];
            await Until(() => IsArranged(description.Popup));

            await Until(() => description.Popup.Child!.TranslatePoint(default, window) is { X: > 0 });
            var listBounds = BoundsInWindow(completion.CompletionList, window);
            var tipBounds = BoundsInWindow(description.Popup.Child!, window);
            var viewport = BoundsInWindow(window, window);
            Assert.Same(window, description.Popup.PlacementTarget);
            Assert.True(tipBounds.Left >= viewport.Left - 1);
            Assert.True(tipBounds.Right <= viewport.Right + 1);
            if (flipped)
            {
                Assert.InRange(tipBounds.Right - listBounds.Left, -8, 8);
                window.Width = 1250;
                await Until(() => BoundsInWindow(description.Popup.Child!, window).Left -
                    BoundsInWindow(completion.CompletionList, window).Right is >= 0 and <= 8);
                window.Width = 900;
                await Until(() => Math.Abs(BoundsInWindow(description.Popup.Child!, window).Right -
                    BoundsInWindow(completion.CompletionList, window).Left) <= 8);
            }
            else
            {
                Assert.InRange(tipBounds.Left - listBounds.Right, 0, 8);
            }

            completion.CompletionList.SelectedItem = items[2];
            await Until(() => DescriptionText(description.Popup)?.EndsWith(items[2].DisplayText, StringComparison.Ordinal) == true);
            var row = completion.CompletionList.ListBox.ContainerFromIndex(2)!;
            await Until(() => Math.Abs(BoundsInWindow(description.Popup.Child!, window).Top - BoundsInWindow(row, window).Top) <= 1);
            completion.Hide();
            Assert.False(description.Popup.IsOpen);
            Assert.Null(description.Popup.Child);
        }
        finally
        {
            completion?.Hide();
            window.Close();
        }
    });

    [Fact]
    public Task ChangingSelectionAndClosingRejectLateDocumentation() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor();
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Placement.akbura"),
            new() { AutomaticCompletion = false });
        await session.Ready;
        await session.RequestCompletionAsync();
        var window = new Window { Content = editor, Width = 800, Height = 400 };
        CompletionWindow? completion = null;
        try
        {
            window.Show();
            completion = new CompletionWindow(editor.TextArea) { ShouldUseOverlayLayer = true, CloseAutomatically = false };
            var items = NativeItems(session, 2);
            var first = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var description = new CompletionDescriptionPopup(completion, (item, _) =>
                ReferenceEquals(item, items[0]) ? first.Task : second.Task);
            description.Popup.ShouldUseOverlayLayer = true;
            foreach (var item in items)
            {
                completion.CompletionList.CompletionData.Add(item);
            }
            completion.Show();
            completion.CompletionList.SelectedItem = items[0];
            completion.CompletionList.SelectedItem = items[1];
            second.SetResult("Current documentation");
            await Until(() => IsArranged(description.Popup));
            first.SetResult("Obsolete documentation");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            Assert.Equal("Current documentation", DescriptionText(description.Popup));
            completion.Hide();
            Assert.False(description.Popup.IsOpen);
        }
        finally
        {
            completion?.Hide();
            window.Close();
        }
    });

    private static bool IsArranged(Popup popup) => popup.IsOpen && popup.Child is { Bounds.Width: > 0, Bounds.Height: > 0 };

    private static ImmutableArray<AkburaEditorCompletionItem> NativeItems(AkburaEditorSession session, int count)
    {
        using var items = ImmutableArrayBuilder<AkburaEditorCompletionItem>.Rent(count);
        foreach (var item in session.Completions)
        {
            if (!item.IsProjected)
            {
                items.Add(item);
                if (items.Count == count)
                {
                    break;
                }
            }
        }
        return items.ToImmutable();
    }

    private static string? DescriptionText(Popup popup)
    {
        if (popup.Child == null)
        {
            return null;
        }
        using var blocks = ImmutableArrayBuilder<ArrayElement<TextBlock>>.Rent();
        foreach (var visual in popup.Child.GetVisualDescendants())
        {
            if (visual is TextBlock block)
            {
                blocks.Add(new() { Value = block });
            }
        }
        return Assert.Single(blocks.AsEnumerable()).Value.Text;
    }

    private static Rect BoundsInWindow(Control control, Window window)
    {
        var point = control.TranslatePoint(default, window) ?? throw new InvalidOperationException("Expected an overlay popup in the window.");
        return new Rect(point.X, point.Y, control.Bounds.Width, control.Bounds.Height);
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
