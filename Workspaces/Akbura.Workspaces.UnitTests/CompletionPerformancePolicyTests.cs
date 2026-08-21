using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class CompletionPerformancePolicyTests
{
    [Fact]
    public void LatestRequest_CancelsPreviousRequest()
    {
        using var coordinator =
            new AkburaLatestRequestCancellation();
        using var first = coordinator.Begin(
            CancellationToken.None);
        using var second = coordinator.Begin(
            CancellationToken.None);

        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(second.Token.IsCancellationRequested);
        Assert.False(first.IsCurrent);
        Assert.True(second.IsCurrent);
    }

    [Fact]
    public void CompletingStaleRequest_DoesNotClearCurrentRequest()
    {
        using var coordinator =
            new AkburaLatestRequestCancellation();
        var first = coordinator.Begin(
            CancellationToken.None);
        using var second = coordinator.Begin(
            CancellationToken.None);

        first.Dispose();

        Assert.True(second.IsCurrent);
        Assert.False(second.Token.IsCancellationRequested);
    }

    [Fact]
    public void SnapshotCancellation_CancelsCurrentRequest()
    {
        using var coordinator =
            new AkburaLatestRequestCancellation();
        using var request = coordinator.Begin(
            CancellationToken.None);

        coordinator.CancelCurrent();

        Assert.True(request.Token.IsCancellationRequested);
        Assert.False(request.IsCurrent);
    }

    [Fact]
    public void ExternalCancellation_IsLinkedToRequest()
    {
        using var source = new CancellationTokenSource();
        using var coordinator =
            new AkburaLatestRequestCancellation();
        using var request = coordinator.Begin(source.Token);

        source.Cancel();

        Assert.True(request.Token.IsCancellationRequested);
        Assert.False(request.IsCurrent);
    }

    [Fact]
    public void Dispose_CancelsRequestAndRejectsNewRequests()
    {
        var coordinator =
            new AkburaLatestRequestCancellation();
        using var request = coordinator.Begin(
            CancellationToken.None);

        coordinator.Dispose();

        Assert.True(request.Token.IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(
            () => coordinator.Begin(CancellationToken.None));
    }

    [Theory]
    [InlineData(
        true,
        false,
        false,
        false,
        "Explicit")]
    [InlineData(
        false,
        true,
        false,
        false,
        "IncompleteSession")]
    [InlineData(
        false,
        false,
        true,
        true,
        "Triggered")]
    [InlineData(
        false,
        false,
        false,
        false,
        "UnsupportedInsertion")]
    [InlineData(
        false,
        false,
        true,
        false,
        "RoslynSuppressed")]
    public void Preflight_UsesExplicitAndIncompleteSessionFirst(
        bool isExplicit,
        bool isIncompleteSession,
        bool isSupportedInsertion,
        bool shouldTrigger,
        string expected)
    {
        var actual =
            AkburaRoslynCompletionTriggerPolicy.Evaluate(
                isExplicit,
                isIncompleteSession,
                isSupportedInsertion,
                shouldTrigger);

        Assert.Equal(expected, actual.ToString());
    }

    [Theory]
    [InlineData(' ', false)]
    [InlineData('\t', false)]
    [InlineData('1', false)]
    [InlineData('.', true)]
    [InlineData('a', true)]
    public void Preflight_FiltersMeaninglessInsertionCharacters(
        char character,
        bool expected)
    {
        Assert.Equal(
            expected,
            AkburaRoslynCompletionTriggerPolicy
                .IsSupportedInsertionCharacter(character));
    }
    [Theory]
    [InlineData("FB", "FB", "Exact")]
    [InlineData("FBeta", "FB", "Prefix")]
    [InlineData(
        "fBeta",
        "FB",
        "PrefixIgnoreCase")]
    [InlineData(
        "FooBar",
        "FB",
        "CamelCase")]
    [InlineData(
        "Frob",
        "FB",
        "Subsequence")]
    [InlineData("Alpha", "ZX", "None")]
    public void Selector_ClassifiesFuzzyMatches(
        string candidate,
        string prefix,
        string expected)
    {
        Assert.Equal(
            expected,
            AkburaRoslynCompletionItemSelector.GetMatchKind(
                candidate,
                prefix).ToString());
    }

    [Fact]
    public void Selector_OrdersMatchesAndExcludesIrrelevantItems()
    {
        var text = SourceText.From("FB");
        var span = new TextSpan(0, 2);
        var list = CreateList(
            span,
            CreateItem("Frob", span),
            CreateItem("fBeta", span),
            CreateItem("FooBar", span),
            CreateItem("FBeta", span),
            CreateItem("FB", span),
            CreateItem("Alpha", span));

        var selection =
            AkburaRoslynCompletionItemSelector.Select(
                list,
                text,
                position: 2,
                isExplicit: false,
                CancellationToken.None);

        Assert.Equal(
            ["FB", "FBeta", "fBeta", "FooBar", "Frob"],
            selection.Items.Select(static item =>
                item.DisplayText));
        Assert.Equal("FB", selection.Prefix);
        Assert.Equal(6, selection.RawItemCount);
        Assert.False(selection.IsIncomplete);
    }

    [Fact]
    public void Selector_UsesMatchPriorityThenRoslynOrder()
    {
        var text = SourceText.From("Al");
        var span = new TextSpan(0, 2);
        var ordinaryRules = CompletionItemRules.Default
            .WithMatchPriority(0);
        var preferredRules = CompletionItemRules.Default
            .WithMatchPriority(10);
        var list = CreateList(
            span,
            CreateItem("AlphaFirst", span, ordinaryRules),
            CreateItem("AlphaPreferred", span, preferredRules),
            CreateItem("AlphaSecond", span, ordinaryRules));

        var selection =
            AkburaRoslynCompletionItemSelector.Select(
                list,
                text,
                position: 2,
                isExplicit: false,
                CancellationToken.None);

        Assert.Equal(
            ["AlphaPreferred", "AlphaFirst", "AlphaSecond"],
            selection.Items.Select(static item =>
                item.DisplayText));
    }

    [Fact]
    public void Selector_UsesMatchPriorityForEmptyPrefix()
    {
        var span = new TextSpan(0, 0);
        var ordinaryRules = CompletionItemRules.Default
            .WithMatchPriority(0);
        var preferredRules = CompletionItemRules.Default
            .WithMatchPriority(10);
        var list = CreateList(
            span,
            CreateItem("First", span, ordinaryRules),
            CreateItem("Preferred", span, preferredRules),
            CreateItem("Second", span, ordinaryRules));

        var selection =
            AkburaRoslynCompletionItemSelector.Select(
                list,
                SourceText.From(string.Empty),
                position: 0,
                isExplicit: false,
                CancellationToken.None);

        Assert.Equal(
            ["Preferred", "First", "Second"],
            selection.Items.Select(static item =>
                item.DisplayText));
    }
    [Fact]
    public void Selector_LimitsAutomaticCompletionTo256Items()
    {
        var span = new TextSpan(0, 0);
        var rawItems = Enumerable
            .Range(0, 300)
            .Select(index => CreateItem(
                $"Item{index:D3}",
                span))
            .ToArray();
        var list = CreateList(span, rawItems);

        var selection =
            AkburaRoslynCompletionItemSelector.Select(
                list,
                SourceText.From(string.Empty),
                position: 0,
                isExplicit: false,
                CancellationToken.None);

        Assert.Equal(256, selection.Items.Length);
        Assert.Equal(300, selection.RawItemCount);
        Assert.True(selection.IsIncomplete);
        Assert.Equal(
            "Item000",
            selection.Items[0].DisplayText);
        Assert.Equal(
            "Item255",
            selection.Items[^1].DisplayText);
    }

    [Fact]
    public void Selector_LeavesExplicitCompletionUnbounded()
    {
        var span = new TextSpan(0, 0);
        var list = CreateList(
            span,
            Enumerable
                .Range(0, 300)
                .Select(index => CreateItem(
                    $"Item{index:D3}",
                    span))
                .ToArray());

        var selection =
            AkburaRoslynCompletionItemSelector.Select(
                list,
                SourceText.From(string.Empty),
                position: 0,
                isExplicit: true,
                CancellationToken.None);

        Assert.Equal(300, selection.Items.Length);
        Assert.False(selection.IsIncomplete);
    }

    [Fact]
    public void Selector_ObservesCancellation()
    {
        var span = new TextSpan(0, 0);
        var list = CreateList(
            span,
            CreateItem("Item", span));
        using var source =
            new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => AkburaRoslynCompletionItemSelector.Select(
                list,
                SourceText.From(string.Empty),
                position: 0,
                isExplicit: false,
                source.Token));
    }

    private static CompletionList CreateList(
        TextSpan span,
        params CompletionItem[] items)
    {
        return CompletionList.Create(
            span,
            items.ToImmutableArray(),
            CompletionRules.Default,
            suggestionModeItem: null);
    }

    private static CompletionItem CreateItem(
        string displayText,
        TextSpan span,
        CompletionItemRules? rules = null)
    {
        return CompletionItem.Create(
            displayText,
            filterText: displayText,
            sortText: displayText,
            properties:
                ImmutableDictionary<string, string>.Empty,
            tags: ImmutableArray<string>.Empty,
            rules ?? CompletionItemRules.Default);
    }
}
