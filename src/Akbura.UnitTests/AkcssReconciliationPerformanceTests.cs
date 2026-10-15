using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using System.Collections.Immutable;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkcssReconciliationPerformanceTests(ITestOutputHelper output)
{
    private static Task OnDispatcher(Action action) => AvaloniaHeadlessTestSession.GetSession().Dispatch(action, CancellationToken.None);

    private static readonly AvaloniaProperty[] s_properties =
    [
        Control.WidthProperty, Control.HeightProperty, Control.MinWidthProperty, Control.MinHeightProperty,
        Control.MaxWidthProperty, Control.MaxHeightProperty, Visual.OpacityProperty,
        Border.PaddingProperty, Border.CornerRadiusProperty, TextElement.FontSizeProperty,
    ];

    [Fact]
    public Task OneOfTenUtilities_ComparesFullReplacementWithIncrementalReconciliation() => OnDispatcher(() =>
    {
        const int iterations = 1000;
        var full = Measure(incremental: false, iterations);
        var incremental = Measure(incremental: true, iterations);
        Assert.Equal(iterations * 10, full.Constructions);
        Assert.Equal(iterations, incremental.Constructions);
        Assert.Equal(iterations * 9, full.Subscriptions);
        Assert.Equal(0, incremental.Subscriptions);
        Assert.Equal(iterations * 10, full.Executions);
        Assert.Equal(iterations, incremental.Executions);
        Assert.Equal(iterations, incremental.Resets);
        Assert.True(full.Resets >= iterations * 10);
        output.WriteLine("Runtime/ownership microbenchmark; 10 independent Avalonia properties, 9 unchanged observable arguments.");
        Write("Full replacement", full, iterations);
        Write("Incremental compatibility path", incremental, iterations);
    });

    [Fact]
    public Task OneOfTenOwnedUtilities_ComparesDisposableHandlesWithSuspendableContributions() => OnDispatcher(() =>
    {
        const int iterations = 1000;
        var handles = Measure(true, iterations, priority: true);
        var owned = Measure(true, iterations, priority: true, owned: true);
        Assert.Equal(iterations, handles.Constructions);
        Assert.Equal(iterations, owned.Constructions);
        Assert.Equal(0, owned.Subscriptions);
        Assert.Equal(iterations, owned.Executions);
        Assert.Equal(iterations, owned.Installations);
        Assert.Equal(iterations, owned.Releases);
        Write("Disposable priority handles", handles, iterations);
        Write("Suspendable owned contributions", owned, iterations);
    });

    private void Write(string label, Measurement result, int iterations) =>
        output.WriteLine($"{label}: {result.Bytes / iterations} B/update; {result.Elapsed.TotalMicroseconds / iterations:F2} us/update; " +
            $"constructed={result.Constructions / iterations}, subscriptions={result.Subscriptions / iterations}, " +
            $"reset={result.Resets / iterations}, execute={result.Executions / iterations}, " +
            $"installed={result.Installations / iterations}, released={result.Releases / iterations}");

    private static Measurement Measure(bool incremental, int iterations, bool priority = false, bool owned = false)
    {
        using var state = new AkburaRenderState();
        var counters = new Counters();
        var signal = new Signal(counters);
        var unchanged = s_properties.Select(property => new AkcssActivatorDescriptor(
            property.Name, property.Name, property.Name, "12")).ToArray();
        for (var index = 0; index < 100; index++)
        {
            Apply(index);
        }
        counters.Clear();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (var index = 100; index < iterations + 100; index++)
        {
            Apply(index);
        }
        timer.Stop();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(9, signal.Subscribers);
        return new Measurement(bytes, timer.Elapsed, counters.Constructions, counters.Subscriptions,
            counters.Resets, counters.Executions, counters.Installations, counters.Releases);

        void Apply(int revision)
        {
            var identity = revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            AkcssReconciliationTests.Begin(state, identity);
            var target = state.GetRequired<Border>(0);
            if (incremental)
            {
                var descriptors = unchanged.ToArray();
                descriptors[0] = descriptors[0] with { Revision = identity };
                state.ReconcileAkcssStylesOperation(0, target, ImmutableArray.Create(descriptors), Factory, true);
            }
            else
            {
                var styles = ImmutableArray.CreateBuilder<AkcssStyleActivator>(s_properties.Length);
                for (var index = 0; index < s_properties.Length; index++)
                {
                    styles.Add(Factory(index));
                }
                state.ApplyAkcssStylesOperation(0, target, styles.MoveToImmutable(), true);
            }
            state.CompleteRevision();

            AkcssStyleActivator Factory(int index)
            {
                counters.Constructions++;
                var utility = new Utility(s_properties[index], counters, owned);
                var argument = index == 0
                    ? AkcssUtilityValueSource.Create<double>(_ => revision + 12)
                    : AkcssUtilityValueSource.CreateObservable<double, double>(_ => signal, AkcssUtilityValueSource.Identity);
                return new AkcssUtilityCandidateActivator(s_properties[index].Name, index,
                    [new AkcssUtilityApplication(utility, static (_, _) => { })], [argument],
                    bindingPriority: priority ? Avalonia.Data.BindingPriority.Style : null);
            }
        }
    }

    private readonly record struct Measurement(long Bytes, TimeSpan Elapsed, int Constructions,
        int Subscriptions, int Resets, int Executions, int Installations, int Releases);

    private sealed class Counters
    {
        public int Constructions;
        public int Subscriptions;
        public int Resets;
        public int Executions;
        public int Installations;
        public int Releases;
        public void Clear() => Constructions = Subscriptions = Resets = Executions = Installations = Releases = 0;
    }

    private sealed class Signal(Counters counters) : IObservable<double>
    {
        public int Subscribers { get; private set; }
        public IDisposable Subscribe(IObserver<double> observer)
        {
            Subscribers++;
            counters.Subscriptions++;
            observer.OnNext(12);
            return new Subscription(() => Subscribers--);
        }
    }

    private sealed class Subscription(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private sealed class Utility : AkcssUtility<double>
    {
        public Utility(AvaloniaProperty property, Counters counters, bool owned) => Operations = [new Operation(this, property, counters, owned)];
        public override ImmutableArray<AkcssUtilityOperation> Operations { get; }
        public override void Update(object target, double value) => Operations[0].Update(target, [value]);
    }

    private sealed class Operation(AkcssUtility utility, AvaloniaProperty property, Counters counters, bool owned)
        : AkcssUtilityOperation(utility, property.Name, AkcssOperationPriority.Style, 0)
    {
        public override bool SupportsHotReloadReconciliation => true;
        public override bool SupportsOwnedContributions => owned;
        public override AvaloniaProperty HotReloadProperty => property;
        public override bool IsActive(object target, IReadOnlyList<object?> arguments) => true;
        public override void Update(object target, IReadOnlyList<object?> arguments)
        {
            counters.Executions++;
            ((AvaloniaObject)target).SetValue(property, Convert((double)arguments[0]!));
        }
        private object Convert(double value) => property.PropertyType == typeof(Thickness) ? new Thickness(value)
                : property.PropertyType == typeof(CornerRadius) ? new CornerRadius(value)
                : property == Visual.OpacityProperty ? 0.5 : value;
        public override IDisposable Apply(object target, IReadOnlyList<object?> arguments, Avalonia.Data.BindingPriority priority)
        {
            counters.Executions++;
            counters.Installations++;
            var value = Convert((double)arguments[0]!);
            if (owned)
            {
                return AkcssPropertyContribution.Bind((AvaloniaObject)target, property, new ValueSource(value, counters), priority);
            }
            var handle = ((AvaloniaObject)target).SetValue(property, value, priority)!;
            return new Subscription(() =>
            {
                counters.Releases++;
                handle.Dispose();
            });
        }
        public override void Reset(object target)
        {
            counters.Resets++;
            ((AvaloniaObject)target).ClearValue(property);
        }
        public override void Reset(object target, AvaloniaProperty capturedProperty)
        {
            counters.Resets++;
            base.Reset(target, capturedProperty);
        }
    }

    private sealed class ValueSource(object value, Counters counters) : IObservable<object?>
    {
        public IDisposable Subscribe(IObserver<object?> observer)
        {
            observer.OnNext(value);
            return new Subscription(() => counters.Releases++);
        }
    }
}
