using Akbura.FeatureGallery.Routing;

namespace Akbura.UnitTests;

public sealed class GalleryRoutingTests
{
    [Theory]
    [InlineData("", "/overview")]
    [InlineData("/", "/overview")]
    [InlineData("commands", "/commands")]
    [InlineData(" /COMMANDS/ ", "/commands")]
    [InlineData("/commands?filter=1#section", "/commands")]
    [InlineData("/unknown/nested/", "/unknown/nested")]
    public void NormalizesLogicalRoutes(string value, string expected) => Assert.Equal(expected, GalleryRoutePath.Normalize(value));

    [Theory]
    [InlineData("https://evil.example/commands")]
    [InlineData("//evil.example/commands")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/../commands")]
    [InlineData("/a/./b")]
    [InlineData("/a//b")]
    [InlineData("/a\\b")]
    [InlineData("/%2e%2e/commands")]
    [InlineData("/a%2fb")]
    public void RejectsUnsafeLogicalRoutes(string value) => Assert.Throws<ArgumentException>(() => GalleryRoutePath.Normalize(value));

    [Fact]
    public void MockRouterNotifiesOnlyWhenEffectiveRouteChanges()
    {
        IRouter router = new MockRouter();
        var changes = new List<string?>();
        router.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        Assert.Equal("/overview", router.Url);
        router.Url = "OVERVIEW/";
        router.Url = "/Commands/";
        router.Url = "commands?ignored#also-ignored";
        Assert.Equal("/commands", router.Url);
        Assert.Equal([nameof(IRouter.Url)], changes);
        Assert.Throws<ArgumentException>(() => router.Url = "https://external/");
        Assert.Equal("/commands", router.Url);
    }

    [Theory]
    [InlineData("http://localhost:5235/", "http://localhost:5235/commands/")]
    [InlineData("https://owner.github.io/Akbura/Gallery/", "https://owner.github.io/Akbura/Gallery/commands/")]
    [InlineData("https://example.com/Gallery/", "https://example.com/Gallery/commands/")]
    [InlineData("https://example.com/apps/gallery/", "https://example.com/apps/gallery/commands/?q=1#part")]
    public void InitialLocationAndHistoryAreBidirectionalWithoutLoops(string baseUrl, string initial)
    {
        var browser = new FakeBrowser(baseUrl, initial);
        using var router = new BrowserRouter(browser);
        var changes = new List<string?>();
        router.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        Assert.Equal("/commands", router.Url);
        Assert.Empty(browser.Replaces);
        router.Url = "COMMANDS/";
        Assert.Empty(browser.Pushes);
        router.Url = "/overview";
        Assert.Equal([new Uri(new Uri(baseUrl), "overview/").AbsoluteUri], browser.Pushes);
        browser.Go(initial); // Back
        Assert.Equal("/commands", router.Url);
        browser.Go(new Uri(new Uri(baseUrl), "overview/").AbsoluteUri); // Forward
        Assert.Equal("/overview", router.Url);
        browser.Go(browser.CurrentUrl); // Duplicate callback
        Assert.Single(browser.Pushes);
        Assert.Equal(3, changes.Count);
        Assert.All(changes, name => Assert.Equal(nameof(IRouter.Url), name));
    }

    [Theory]
    [InlineData("https://host/Gallery/", "https://host/Gallery/", "/overview", "https://host/Gallery/overview/")]
    [InlineData("https://host/Gallery", "https://host/Gallery", "/overview", "https://host/Gallery/overview/")]
    [InlineData("https://host/Gallery/", "https://host/Gallery/COMMANDS?q=1#fragment", "/commands", "https://host/Gallery/commands/?q=1#fragment")]
    [InlineData("https://host/Gallery/", "https://host/Gallery/unknown/", "/unknown", null)]
    public void CanonicalizationReplacesOnlyAtStartup(string baseUrl, string initial, string route, string? replacement)
    {
        var browser = new FakeBrowser(baseUrl, initial);
        using var router = new BrowserRouter(browser);
        Assert.Equal(route, router.Url);
        Assert.Empty(browser.Pushes);
        Assert.Equal(replacement is null ? [] : new[] { replacement }, browser.Replaces);
        browser.Go("https://host/Gallery/COMMANDS?new#fragment");
        Assert.Equal("/commands", router.Url);
        Assert.Equal(replacement is null ? 0 : 1, browser.Replaces.Count);
        Assert.Empty(browser.Pushes);
    }

    [Theory]
    [InlineData("https://host/Gallery/", "https://host/GalleryElse/commands/")]
    [InlineData("https://host/Gallery/", "https://evil/Gallery/commands/")]
    [InlineData("https://host/Gallery/", "https://host/Other/Gallery/commands/")]
    [InlineData("https://host/Gallery/?base=bad", "https://host/Gallery/")]
    [InlineData("file:///Gallery/", "file:///Gallery/commands/")]
    public void RejectsLocationsOutsideApplication(string baseUrl, string initial) =>
        Assert.Throws<ArgumentException>(() => new BrowserRouter(new FakeBrowser(baseUrl, initial)));

    [Fact]
    public void FailedPushDoesNotCommitStateAndInvalidHistoryCallbacksAreIgnored()
    {
        var browser = new FakeBrowser("https://host/Gallery/", "https://host/Gallery/commands/") { FailPush = true };
        using var router = new BrowserRouter(browser);
        Assert.Throws<InvalidOperationException>(() => router.Url = "/overview");
        Assert.Equal("/commands", router.Url);
        browser.Go("https://external/Gallery/overview/");
        browser.Go("not a URL");
        browser.Go("https://host/GalleryElse/overview/");
        Assert.Equal("/commands", router.Url);
        Assert.Empty(browser.Pushes);
    }

    [Fact]
    public void DispatcherAndDisposalPreventLateCallbacksFromMutatingRouter()
    {
        var browser = new FakeBrowser("https://host/", "https://host/commands/");
        var queue = new Queue<Action>();
        var router = new BrowserRouter(browser, queue.Enqueue);
        Assert.Equal(1, browser.Subscribers);
        browser.Go("https://host/overview/");
        Assert.Equal("/commands", router.Url);
        queue.Dequeue()();
        Assert.Equal("/overview", router.Url);
        browser.Go("https://host/unknown/");
        router.Dispose();
        router.Dispose();
        queue.Dequeue()();
        Assert.Equal("/overview", router.Url);
        Assert.Equal(0, browser.Subscribers);
        Assert.Equal(1, browser.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => router.Url = "/commands");
    }

    private sealed class FakeBrowser(string baseUrl, string initial) : IJsBrowserUrl
    {
        private EventHandler<string>? _changed;
        public string BaseUrl => baseUrl;
        public string CurrentUrl { get; private set; } = initial;
        public List<string> Pushes { get; } = [];
        public List<string> Replaces { get; } = [];
        public int Subscribers { get; private set; }
        public int DisposeCount { get; private set; }
        public bool FailPush { get; init; }
        public event EventHandler<string>? UrlChanged
        {
            add { _changed += value; Subscribers++; }
            remove { _changed -= value; Subscribers--; }
        }
        public void PushUrl(string url)
        {
            if (FailPush) throw new InvalidOperationException("Simulated pushState failure.");
            Pushes.Add(url);
            CurrentUrl = url;
        }
        public void ReplaceUrl(string url) { Replaces.Add(url); CurrentUrl = url; }
        public void Go(string url) { CurrentUrl = url; _changed?.Invoke(this, url); }
        public void Dispose() => DisposeCount++;
    }
}
