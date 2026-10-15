using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Xunit.Abstractions;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkcssOptimizationPerformanceTests(ITestOutputHelper output)
{
    internal static Task OnDispatcher(Action action) => AvaloniaHeadlessTestSession.GetSession().Dispatch(action, CancellationToken.None);

    [Theory]
    [InlineData("unchanged", 10)]
    [InlineData("reconcile", 10)]
    [InlineData("reconcile", 50)]
    [InlineData("reactive", 10)]
    [InlineData("same-key", 10)]
    [InlineData("different-keys", 10)]
    [InlineData("same-key-batch", 10)]
    [InlineData("different-keys-batch", 10)]
    [InlineData("scalar-reuse", 10)]
    [InlineData("scalar-replace", 10)]
    [InlineData("competing", 50)]
    [InlineData("multi-property", 10)]
    [InlineData("rollback", 10)]
    public Task Workloads_RecordComparableMeasurements(string workload, int size) => OnDispatcher(() =>
    {
        using var fixture = new Fixture(size, workload == "competing", workload == "multi-property",
            scalar: workload.StartsWith("scalar-", StringComparison.Ordinal), reuse: workload != "scalar-replace");
        for (var index = 0; index < 100; index++)
        {
            Update(index);
        }
        fixture.Counts.Clear();
        const int iterations = 1000;
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 100; index < iterations + 100; index++)
        {
            Update(index);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var counts = fixture.Counts.ToString();
        var collections = $"{GC.CollectionCount(0) - gen0}/{GC.CollectionCount(1) - gen1}/{GC.CollectionCount(2) - gen2}";
        var timer = Stopwatch.StartNew();
        for (var index = 100; index < iterations + 100; index++)
        {
            Update(index);
        }
        timer.Stop();
        output.WriteLine($"{workload}/{size}: {bytes / iterations} B/update; {timer.Elapsed.TotalMicroseconds / iterations:F2} us/update; GC={collections}; {counts}");
        Assert.Equal(size, fixture.Sources.Sum(source => source.Subscribers));

        void Update(int revision)
        {
            switch (workload)
            {
                case "unchanged":
                    AkcssRuntime.RefreshForHotReload(fixture.Target);
                    break;
                case "reactive":
                case "scalar-reuse":
                case "scalar-replace":
                    fixture.Sources[0].Publish(revision);
                    break;
                case "same-key":
                case "different-keys":
                case "same-key-batch":
                case "different-keys-batch":
                    using (workload.EndsWith("batch", StringComparison.Ordinal) ? AkburaControl.BeginAkcssUpdate(fixture.Target) : null)
                    {
                        for (var index = 0; index < 3; index++)
                        {
                            fixture.Sources[workload.StartsWith("same-key", StringComparison.Ordinal) ? 0 : index].Publish(revision + index);
                        }
                    }
                    break;
                case "rollback":
                    fixture.Change(revision, fail: true);
                    break;
                default:
                    fixture.Change(revision);
                    break;
            }
        }
    });

    [Fact]
    public Task AllocationProfile_SamplesRuntimeAllocationTypes() => OnDispatcher(() =>
    {
        using var listener = new AllocationListener();
        using var fixture = new Fixture(50);
        for (var index = 0; index < 3000; index++)
        {
            fixture.Change(index);
        }
        foreach (var pair in listener.Snapshot().OrderByDescending(pair => pair.Value).Take(15))
        {
            output.WriteLine($"{pair.Key}: {pair.Value} sampled bytes (GC allocation ticks, not exact attribution)");
        }
    });

    internal sealed class Fixture : IDisposable
    {
        internal readonly Border Target = new();
        internal readonly Counters Counts = new();
        internal readonly Signal[] Sources;
        internal readonly ImmutableArray<AkcssActivatorDescriptor> Original;
        private readonly bool _competing;
        private readonly bool _multiProperty;
        private readonly bool _scalar;
        private readonly bool _reuse;

        internal Fixture(int size, bool competing = false, bool multiProperty = false, bool scalar = false, bool reuse = true)
        {
            _competing = competing;
            _multiProperty = multiProperty;
            _scalar = scalar;
            _reuse = reuse;
            Sources = Enumerable.Range(0, size).Select(_ => new Signal(Counts)).ToArray();
            Original = Enumerable.Range(0, size).Select(index => new AkcssActivatorDescriptor(
                index.ToString(), index.ToString(), index.ToString(), "initial")).ToImmutableArray();
            AkcssRuntime.Reconcile(Target, Original, index => Create(index)).Commit();
            Target.PropertyChanged += (_, _) => Counts.Writes++;
        }

        internal AkcssUtilityCandidateActivator Create(int index, bool fail = false)
        {
            Counts.Constructions++;
            var utility = new Utility(_competing ? 0 : index, Counts, _multiProperty && index == 0, fail);
            foreach (var operation in utility.Operations)
            {
                ((Operation)operation).Scalar = _scalar;
                ((Operation)operation).Reuse = _reuse;
            }
            return new AkcssUtilityCandidateActivator(index.ToString(), index,
                [new AkcssUtilityApplication(utility, static (_, _) => { })],
                [AkcssUtilityValueSource.CreateObservable<double, double>(_ => Sources[index], AkcssUtilityValueSource.Identity)],
                bindingPriority: BindingPriority.Animation);
        }

        internal void Change(int revision, bool fail = false)
        {
            var descriptors = Original.SetItem(0, Original[0] with { Revision = revision.ToString() });
            try
            {
                AkcssRuntime.Reconcile(Target, descriptors, index => Create(index, fail)).Commit();
            }
            catch (InvalidOperationException) when (fail)
            {
                Counts.Failures++;
            }
        }

        public void Dispose() => AkcssRuntime.SetStyles(Target, []);
    }

    internal sealed class Counters
    {
        internal int Constructions, Installed, Released, Subscriptions, Disposals, Eligibility, Writes, Failures;
        internal void Clear() => Constructions = Installed = Released = Subscriptions = Disposals = Eligibility = Writes = Failures = 0;
        public override string ToString() => $"constructed={Constructions}, installed={Installed}, released={Released}, subscriptions={Subscriptions}, disposed={Disposals}, eligibility={Eligibility}, writes={Writes}, failures={Failures}";
    }

    internal sealed class Signal(Counters counts) : IObservable<double>
    {
        private readonly List<IObserver<double>> _observers = [];
        internal IObserver<double>? Stale;
        private double _value = 12;
        internal int Subscribers => _observers.Count;
        public IDisposable Subscribe(IObserver<double> observer)
        {
            counts.Subscriptions++;
            _observers.Add(observer);
            observer.OnNext(_value);
            return new Subscription(() =>
            {
                counts.Disposals++;
                _observers.Remove(observer);
                Stale = observer;
            });
        }
        internal void Publish(double value)
        {
            _value = value;
            foreach (var observer in _observers)
            {
                observer.OnNext(value);
            }
        }
    }

    internal sealed class Utility : AkcssUtility<double>
    {
        internal Utility(int key, Counters counts, bool multiProperty = false, bool fail = false)
        {
            Operations = multiProperty
                ? [new Operation(this, key, counts), new Operation(this, key + 1000, counts, fail)]
                : [new Operation(this, key, counts, fail: fail)];
        }
        public override ImmutableArray<AkcssUtilityOperation> Operations { get; }
        public override void Update(object target, double value) => throw new NotSupportedException();
    }

    internal sealed class Operation(AkcssUtility utility, int key, Counters counts, bool fail = false)
        : AkcssUtilityOperation(utility, "key:" + key, AkcssOperationPriority.Style, 0)
    {
        // Repeated conflict keys are intentional; property registration is per fixture key, not per update.
        internal static readonly Dictionary<int, StyledProperty<double>> Properties = [];
        internal readonly StyledProperty<double> Property = GetProperty(key);
        internal Action? Executing { get; set; }
        internal Func<bool>? Active { get; set; }
        internal bool Scalar { get; set; }
        internal bool Reuse { get; set; } = true;
        public override bool SupportsHotReloadReconciliation => true;
        public override bool SupportsOwnedContributions => true;
        public override bool SupportsContributionUpdates => Scalar && Reuse && Active == null;
        public override AvaloniaProperty HotReloadProperty => Property;
        public override bool IsActive(object target, IReadOnlyList<object?> arguments)
        {
            counts.Eligibility++;
            return Active?.Invoke() ?? true;
        }
        public override void Update(object target, IReadOnlyList<object?> arguments) => ((AvaloniaObject)target).SetValue(Property, arguments[0]);
        public override IDisposable Apply(object target, IReadOnlyList<object?> arguments, BindingPriority priority)
        {
            if (fail)
            {
                throw new InvalidOperationException("Measured failure");
            }
            Executing?.Invoke();
            counts.Installed++;
            if (Scalar)
            {
                return AkcssPropertyContribution.SetValue((AvaloniaObject)target, Property, arguments[0], priority);
            }
            return AkcssPropertyContribution.Bind((AvaloniaObject)target, Property, new ScalarSource(arguments[0], counts), priority);
        }
        public override bool TryUpdateContribution(object target, IReadOnlyList<object?> arguments,
            BindingPriority priority, IDisposable contribution)
        {
            if (!AkcssPropertyContribution.CanUpdate(contribution, (AvaloniaObject)target, Property, priority))
            {
                return false;
            }
            Executing?.Invoke();
            AkcssPropertyContribution.Update(contribution, arguments[0]);
            return true;
        }
        private static StyledProperty<double> GetProperty(int key)
        {
            if (!Properties.TryGetValue(key, out var property))
            {
#pragma warning disable AVP1001 // Test-only, dispatcher-confined, once per key across fixtures.
                property = AvaloniaProperty.Register<Border, double>("Optimization" + key);
#pragma warning restore AVP1001
                Properties.Add(key, property);
            }
            return property;
        }
    }

    private sealed class ScalarSource(object? value, Counters counts) : IObservable<object?>
    {
        public IDisposable Subscribe(IObserver<object?> observer)
        {
            observer.OnNext(value);
            return new Subscription(() => counts.Released++);
        }
    }

    private sealed class Subscription(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private sealed class AllocationListener : EventListener
    {
        internal readonly Dictionary<string, long> Samples = [];
        internal KeyValuePair<string, long>[] Snapshot()
        {
            lock (Samples)
            {
                return Samples.ToArray();
            }
        }
        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name == "Microsoft-Windows-DotNETRuntime")
            {
                EnableEvents(eventSource, EventLevel.Verbose, (EventKeywords)0x1);
            }
        }
        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) == true &&
                eventData.PayloadNames is { } names && eventData.Payload is { } values)
            {
                var type = names.IndexOf("TypeName");
                var amount = names.IndexOf("AllocationAmount64");
                if (type >= 0 && amount >= 0 && values[type] is string name && values[amount] is ulong bytes)
                {
                    lock (Samples)
                    {
                        Samples[name] = Samples.GetValueOrDefault(name) + (long)bytes;
                    }
                }
            }
        }
    }
}
