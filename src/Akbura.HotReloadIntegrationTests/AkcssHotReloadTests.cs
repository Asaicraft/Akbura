using Akbura.Akcss;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.HotReloadIntegrationTests;

public sealed class AkcssHotReloadTests
{
    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task UnrelatedMarkup_RetainsTheCascadeAndItsSubscriptions()
    {
        var testName = TestName(nameof(UnrelatedMarkup_RetainsTheCascadeAndItsSubscriptions));
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = CreateCompilation(Component("before"), Styles(10));
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter", includeAkcssTypes: true);
        await Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                var styled = root.Children[0];
                var text = Assert.IsType<TextBlock>(root.Children[1]);
                var activators = AkburaControl.GetAkcssStyles(styled);
                Assert.Equal(2, activators.Length);
                var subscriptions = Read<int>(reload, "Subscriptions");
                var markerWrites = Read<int>(styled, "MarkerWrites");

                reload.ApplyUpdate(compilation.Update(Component("after")));
                reload.DispatchMetadataUpdateHandlers();

                Assert.Same(root, owner.Child);
                Assert.Same(styled, root.Children[0]);
                Assert.Equal("after", text.Text);
                Assert.True(activators == AkburaControl.GetAkcssStyles(styled));
                Assert.Equal(subscriptions, Read<int>(reload, "Subscriptions"));
                Assert.Equal(markerWrites, Read<int>(styled, "MarkerWrites"));
                Assert.Equal(1, Read<int>(reload, "Subscribers"));
                Publish(reload, 35);
                Assert.Equal(35, styled.Width);
                Assert.True(activators == AkburaControl.GetAkcssStyles(styled));
            }
            finally
            {
                window.Close();
            }
            Assert.Equal(0, Read<int>(reload, "Subscribers"));
        });
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task ModuleOnlyDelta_UpdatesTheOriginalStyledControl()
    {
        var testName = TestName(nameof(ModuleOnlyDelta_UpdatesTheOriginalStyledControl));
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = CreateCompilation(Component("same markup"), Styles(10));
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter", includeAkcssTypes: true);
        await Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var styled = Assert.IsType<StackPanel>(owner.Child).Children[0];
                Assert.Equal(10, Read<double>(styled, "Marker"));
                var activators = AkburaControl.GetAkcssStyles(styled);
                reload.ApplyUpdate(compilation.UpdateAkcss("Styles.akcss", Styles(20)));
                Assert.DoesNotContain("Demo.Counter", reload.UpdatedTypeNames);
                Assert.Contains(reload.UpdatedTypeNames, name => name.Contains("Akcss", StringComparison.Ordinal));
                Assert.Equal(10, Read<double>(styled, "Marker"));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Equal(20, Read<double>(styled, "Marker"));
                Assert.Same(styled, Assert.IsType<StackPanel>(owner.Child).Children[0]);
                Assert.False(activators == AkburaControl.GetAkcssStyles(styled));
                Assert.Equal(1, Read<int>(reload, "Subscribers"));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static AkburaCompilationSession CreateCompilation(string component, string styles) =>
        new(component, OwnerSource, new Dictionary<string, string> { ["Styles.akcss"] = styles });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task UtilityDefinitionDelta_UpdatesTheSameControl() =>
        RunScenario(nameof(UtilityDefinitionDelta_UpdatesTheSameControl), (compilation, reload, owner, root) =>
        {
            var styled = root.Children[0];
            Assert.Equal(12, styled.Width);
            reload.ApplyUpdate(compilation.UpdateAkcss("Styles.akcss",
                Styles(10).Replace("Width: value;", "Width: value * 2;", StringComparison.Ordinal)));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Same(styled, root.Children[0]);
            Assert.Equal(24, styled.Width);
            Publish(reload, 17);
            Assert.Equal(34, styled.Width);
            Assert.Equal(1, Read<int>(reload, "Subscribers"));
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task DeclarationRemoval_ReleasesSubscriptionsAndKeepsOtherStyling() =>
        RunScenario(nameof(DeclarationRemoval_ReleasesSubscriptionsAndKeepsOtherStyling), (compilation, reload, owner, root) =>
        {
            var styled = root.Children[0];
            var withoutUtility = Component("removed").Replace(" metric-${Stream}", "", StringComparison.Ordinal);
            reload.ApplyUpdate(compilation.Update(withoutUtility));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Same(styled, root.Children[0]);
            Assert.True(double.IsNaN(styled.Width));
            Assert.Equal(11, styled.Height);
            Assert.Equal(10, Read<double>(styled, "Marker"));
            Assert.Single(AkburaControl.GetAkcssStyles(styled));
            Assert.Equal(0, Read<int>(reload, "Subscribers"));
            Publish(reload, 55);
            PublishStale(reload, 66);
            Assert.True(double.IsNaN(styled.Width));

            reload.ApplyUpdate(compilation.Update(withoutUtility.Replace(" class=\"card\"", "", StringComparison.Ordinal)));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Empty(AkburaControl.GetAkcssStyles(styled));
            Assert.True(double.IsNaN(styled.Height));
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task TwentyModuleDeltas_KeepSubscriptionsBoundedAndRejectStaleCallbacks() =>
        RunScenario(nameof(TwentyModuleDeltas_KeepSubscriptionsBoundedAndRejectStaleCallbacks), (compilation, reload, owner, root) =>
        {
            var styled = root.Children[0];
            var previous = AkburaControl.GetAkcssStyles(styled);
            var subscriptions = Read<int>(reload, "Subscriptions");
            var disposals = Read<int>(reload, "Disposals");
            for (var index = 0; index < 20; index++)
            {
                reload.ApplyUpdate(compilation.UpdateAkcss("Styles.akcss", Styles(20 + index)));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Same(root, owner.Child);
                Assert.Same(styled, root.Children[0]);
                var current = AkburaControl.GetAkcssStyles(styled);
                Assert.Equal(2, current.Length);
                Assert.False(previous == current);
                Assert.Equal(1, Read<int>(reload, "Subscribers"));
                Assert.Equal(subscriptions + index + 1, Read<int>(reload, "Subscriptions"));
                Assert.Equal(disposals + index + 1, Read<int>(reload, "Disposals"));
                Publish(reload, index + 30);
                var writes = Read<int>(styled, "MarkerWrites");
                PublishStale(reload, 500);
                Assert.Equal(index + 30, styled.Width);
                Assert.Equal(writes, Read<int>(styled, "MarkerWrites"));
                Assert.Equal(20 + index, Read<double>(styled, "Marker"));
                previous = current;
            }
            Assert.Equal(20, reload.AppliedUpdates);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task FailedCascadeRefresh_RestoresOwnedCascadeAndAllowsRetry() =>
        RunScenario(nameof(FailedCascadeRefresh_RestoresOwnedCascadeAndAllowsRetry), (compilation, reload, owner, root) =>
        {
            var styled = root.Children[0];
            var original = AkburaControl.GetAkcssStyles(styled);
            reload.ApplyUpdate(compilation.Update(Component("failed").Replace("${Stream}", "${ThrowingStream}", StringComparison.Ordinal)));
            var failure = Assert.Throws<TargetInvocationException>(reload.DispatchMetadataUpdateHandlers);
            Assert.Contains("akcss refresh failed", failure.ToString(), StringComparison.Ordinal);
            Assert.Same(root, owner.Child);
            Assert.Same(styled, root.Children[0]);
            Assert.True(original == AkburaControl.GetAkcssStyles(styled));
            Assert.Equal(1, Read<int>(reload, "Subscribers"));
            Publish(reload, 42);
            Assert.Equal(42, styled.Width);

            reload.ApplyUpdate(compilation.Update(Component("retry")));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Equal("retry", Assert.IsType<TextBlock>(root.Children[1]).Text);
            Assert.Equal(1, Read<int>(reload, "Subscribers"));
            Publish(reload, 43);
            Assert.Equal(43, styled.Width);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task UnreferencedStyleDelta_RetainsCascadeAndSurvivesReattachment() =>
        RunScenario(nameof(UnreferencedStyleDelta_RetainsCascadeAndSurvivesReattachment), (compilation, reload, owner, root) =>
        {
            var styled = root.Children[0];
            var original = AkburaControl.GetAkcssStyles(styled);
            var writes = Read<int>(styled, "MarkerWrites");
            var subscriptions = Read<int>(reload, "Subscriptions");
            reload.ApplyUpdate(compilation.UpdateAkcss("Styles.akcss", Styles(10) + "\nCountedBorder.unused { Marker: 77; }"));
            reload.DispatchMetadataUpdateHandlers();
            Assert.True(original == AkburaControl.GetAkcssStyles(styled));
            Assert.Equal(writes, Read<int>(styled, "MarkerWrites"));
            Assert.Equal(subscriptions, Read<int>(reload, "Subscriptions"));
            var window = Assert.IsType<Window>(owner.Parent);
            window.Content = null;
            Assert.Equal(0, Read<int>(reload, "Subscribers"));
            window.Content = owner;
            Assert.True(original == AkburaControl.GetAkcssStyles(styled));
            Assert.Equal(1, Read<int>(reload, "Subscribers"));
            Publish(reload, 67);
            Assert.Equal(67, styled.Width);
            PublishStale(reload, 500);
            Assert.Equal(67, styled.Width);

            window.Content = null;
            Assert.Equal(0, Read<int>(reload, "Subscribers"));
            reload.ApplyUpdate(compilation.UpdateAkcss("Styles.akcss", Styles(25)));
            reload.DispatchMetadataUpdateHandlers();
            window.Content = owner;
            Assert.Same(root, owner.Child);
            Assert.Same(styled, root.Children[0]);
            Assert.Equal(25, Read<double>(styled, "Marker"));
            Assert.False(original == AkburaControl.GetAkcssStyles(styled));
            Assert.Equal(1, Read<int>(reload, "Subscribers"));
            Publish(reload, 68);
            PublishStale(reload, 600);
            Assert.Equal(68, styled.Width);
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task TransitiveApplyDelta_RefreshesTheDependentLiveCascade() =>
        RunScenario(nameof(TransitiveApplyDelta_RefreshesTheDependentLiveCascade), (compilation, reload, owner, root) =>
        {
            var styled = root.Children[0];
            var original = AkburaControl.GetAkcssStyles(styled);
            Assert.Equal(10, Read<double>(styled, "Marker"));
            reload.ApplyUpdate(compilation.UpdateAkcss("Base.akcss",
                "@using Demo; CountedBorder.seed { Marker: 20; Height: 11; }"));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Same(styled, root.Children[0]);
            Assert.False(original == AkburaControl.GetAkcssStyles(styled));
            Assert.Equal(20, Read<double>(styled, "Marker"));
            Assert.Equal(1, Read<int>(reload, "Subscribers"));
        }, stylesheetSources: new Dictionary<string, string>
        {
            ["Styles.akcss"] = "@using Demo; @using Demo.Base.akcss; CountedBorder.card { @apply seed; } " +
                "@utilities { CountedBorder.metric-(double value) { Width: value; } }",
            ["Base.akcss"] = "@using Demo; CountedBorder.seed { Marker: 10; Height: 11; }",
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task StyleIndexInsertionAndRemoval_UseCurrentStyleFactories() =>
        RunScenario(nameof(StyleIndexInsertionAndRemoval_UseCurrentStyleFactories), (compilation, reload, owner, root) =>
        {
            var styled = root.Children[0];
            reload.ApplyUpdate(compilation.UpdateAkcss("Styles.akcss",
                "@using Demo; CountedBorder.before { Marker: 77; } CountedBorder.card { Marker: 20; }"));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Same(styled, root.Children[0]);
            Assert.Equal(20, Read<double>(styled, "Marker"));
            reload.ApplyUpdate(compilation.UpdateAkcss("Styles.akcss", "@using Demo; CountedBorder.card { Marker: 30; }"));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Same(styled, root.Children[0]);
            Assert.Equal(30, Read<double>(styled, "Marker"));
            Assert.Single(AkburaControl.GetAkcssStyles(styled));
        }, component: Component("indexes").Replace(" metric-${Stream}", "", StringComparison.Ordinal),
        stylesheetSources: new Dictionary<string, string>
        {
            ["Styles.akcss"] = "@using Demo; CountedBorder.card { Marker: 10; }",
        });

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public Task FormattingOnlyStyleEdit_RetainsTheInstalledCascade() =>
        RunScenario(nameof(FormattingOnlyStyleEdit_RetainsTheInstalledCascade), (compilation, reload, owner, root) =>
        {
            var styled = root.Children[0];
            var original = AkburaControl.GetAkcssStyles(styled);
            var writes = Read<int>(styled, "MarkerWrites");
            var subscriptions = Read<int>(reload, "Subscriptions");
            reload.ApplyUpdate(compilation.UpdateAkcss("Styles.akcss", Styles(10).Replace(";", ";\n", StringComparison.Ordinal)));
            reload.DispatchMetadataUpdateHandlers();
            Assert.True(original == AkburaControl.GetAkcssStyles(styled));
            Assert.Equal(writes, Read<int>(styled, "MarkerWrites"));
            Assert.Equal(subscriptions, Read<int>(reload, "Subscriptions"));
        });

    private static async Task RunScenario(
        string name,
        Action<AkburaCompilationSession, ClrHotReloadSession, AkburaControl, StackPanel> scenario,
        string? component = null,
        IReadOnlyDictionary<string, string>? stylesheetSources = null)
    {
        var testName = TestName(name);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }
        var compilation = new AkburaCompilationSession(component ?? Component("before"), OwnerSource,
            stylesheetSources ?? new Dictionary<string, string> { ["Styles.akcss"] = Styles(10) });
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter", includeAkcssTypes: true);
        await Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                scenario(compilation, reload, owner, Assert.IsType<StackPanel>(owner.Child));
            }
            finally
            {
                window.Close();
            }
            Assert.Equal(0, Read<int>(reload, "Subscribers"));
        });
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task ObservedPropertyMetadataDelta_ReplacesWidthObservationWithHeight()
    {
        var testName = TestName(nameof(ObservedPropertyMetadataDelta_ReplacesWidthObservationWithHeight));
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var component = Component("observed").Replace(" metric-${Stream}", "", StringComparison.Ordinal);
        var compilation = CreateCompilation(component, "@using Demo;\nCountedBorder.card { Marker: Width; }");
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter", includeAkcssTypes: true);
        await Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var styled = Assert.IsType<StackPanel>(owner.Child).Children[0];
                styled.Width = 10;
                styled.Height = 20;
                Assert.Equal(10, Read<double>(styled, "Marker"));
                var styleType = AkburaControl.GetAkcssStyles(styled)[0].Style.GetType();
                reload.ApplyUpdate(compilation.UpdateAkcss("Styles.akcss", "@using Demo;\nCountedBorder.card { Marker: Height; }"));
                reload.DispatchMetadataUpdateHandlers();
                var attributes = styleType.GetCustomAttributes<Akbura.CompilerAnotations.ObservesPropertyAttribute>().ToArray();
                Assert.Equal("Height", Assert.Single(attributes).PropertyName);
                Assert.Equal(20, Read<double>(styled, "Marker"));
                var writes = Read<int>(styled, "MarkerWrites");
                styled.Width = 11;
                Assert.Equal(writes, Read<int>(styled, "MarkerWrites"));
                styled.Height = 21;
                Assert.Equal(21, Read<double>(styled, "Marker"));
                Assert.Same(styled, Assert.IsType<StackPanel>(owner.Child).Children[0]);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static string Component(string text) =>
        "using Avalonia.Controls;\nusing Demo;\nusing Demo.Styles.akcss;\nnamespace Demo;\n" +
        "<StackPanel><CountedBorder class=\"card\" metric-${Stream} />" +
        "<TextBlock Text=\"" + text + "\" /></StackPanel>";

    private static string Styles(int marker) =>
        "@using Demo;\nCountedBorder.card { Marker: " + marker + "; Height: 11; }\n" +
        "@utilities { CountedBorder.metric-(double value) { Width: value; } }";

    private static string TestName(string method) => typeof(AkcssHotReloadTests).FullName + "." + method;

    private static Task Dispatch(Action action) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AkcssHotReloadTests).Assembly)
            .Dispatch(action, CancellationToken.None);

    private static AkburaControl CreateOwner(ClrHotReloadSession reload) =>
        Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));

    private static T Read<T>(object target, string property) =>
        Assert.IsType<T>(target.GetType().GetProperty(property)!.GetValue(target));

    private static T Read<T>(ClrHotReloadSession reload, string property) =>
        Assert.IsType<T>(reload.Assembly.GetType("Demo.Probe")!.GetProperty(property)!.GetValue(null));

    private static void Publish(ClrHotReloadSession reload, double value) =>
        reload.Assembly.GetType("Demo.Probe")!.GetMethod("Publish")!.Invoke(null, [value]);

    private static void PublishStale(ClrHotReloadSession reload, double value) =>
        reload.Assembly.GetType("Demo.Probe")!.GetMethod("PublishStale")!.Invoke(null, [value]);

    private const string OwnerSource =
        """
        using System;
        using System.Collections.Generic;
        using Akbura;
        using Akbura.Engine;
        using Avalonia.Controls;
        namespace Demo;
        public partial class Counter : AkburaControl
        {
            public Counter() : base(AkburaEngine.Empty) { }
        }
        public sealed class CountedBorder : Border
        {
            private double _marker;
            public int MarkerWrites { get; private set; }
            public double Marker
            {
                get => _marker;
                set
                {
                    MarkerWrites++;
                    if (value == 999)
                    {
                        throw new InvalidOperationException("akcss failed");
                    }
                    _marker = value;
                }
            }
        }
        public sealed class StreamExtension
        {
            public IObservable<double> ProvideValue(IServiceProvider services) => Probe.Source;
        }
        public sealed class ThrowingStreamExtension
        {
            public IObservable<double> ProvideValue(IServiceProvider services)
            {
                if (Probe.ThrowOnce)
                {
                    Probe.ThrowOnce = false;
                    throw new InvalidOperationException("akcss refresh failed");
                }
                return Probe.Source;
            }
        }
        public static class Probe
        {
            public static bool ThrowOnce { get; set; } = true;
            public static CountingSource Source { get; } = new();
            public static int Subscribers => Source.Subscribers;
            public static int Subscriptions => Source.Subscriptions;
            public static int Disposals => Source.Disposals;
            public static void Publish(double value) => Source.Publish(value);
            public static void PublishStale(double value) => Source.PublishStale(value);
        }
        public sealed class CountingSource : IObservable<double>
        {
            private readonly List<IObserver<double>> _active = new();
            private readonly List<IObserver<double>> _disposed = new();
            private double _value = 12;
            public int Subscribers => _active.Count;
            public int Subscriptions { get; private set; }
            public int Disposals { get; private set; }
            public IDisposable Subscribe(IObserver<double> observer)
            {
                _active.Add(observer);
                Subscriptions++;
                observer.OnNext(_value);
                return new Subscription(this, observer);
            }
            public void Publish(double value)
            {
                _value = value;
                foreach (var observer in _active.ToArray())
                {
                    observer.OnNext(value);
                }
            }
            public void PublishStale(double value)
            {
                foreach (var observer in _disposed.ToArray())
                {
                    observer.OnNext(value);
                }
            }
            private sealed class Subscription(CountingSource source, IObserver<double> observer) : IDisposable
            {
                public void Dispose()
                {
                    if (source._active.Remove(observer))
                    {
                        source._disposed.Add(observer);
                        source.Disposals++;
                    }
                }
            }
        }
        """;
}
