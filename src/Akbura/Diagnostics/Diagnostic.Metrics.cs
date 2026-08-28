using Akbura.ComponentTree;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;

namespace Akbura.Diagnostics;

internal static partial class Diagnostic
{
    private static Meter? s_meter;

    private static Histogram<double>? s_componentInitializationDuration;

    private static Histogram<double>? s_componentUpdateBatchDuration;

    private static Histogram<int>? s_componentUpdateBatchSize;

    private static Counter<long>? s_componentUpdateLimitExceeded;

    private static void InitMetrics()
    {
        var meter = new Meter(MeterName);
        s_meter = meter;

        s_componentInitializationDuration =
            meter.CreateHistogram<double>(
                Meters.ComponentInitializationDurationName,
                Meters.MillisecondsUnit,
                Meters.ComponentInitializationDurationDescription);

        s_componentUpdateBatchDuration =
            meter.CreateHistogram<double>(
                Meters.ComponentUpdateBatchDurationName,
                Meters.MillisecondsUnit,
                Meters.ComponentUpdateBatchDurationDescription);

        s_componentUpdateBatchSize =
            meter.CreateHistogram<int>(
                Meters.ComponentUpdateBatchSizeName,
                Meters.UpdateUnit,
                Meters.ComponentUpdateBatchSizeDescription);

        s_componentUpdateLimitExceeded =
            meter.CreateCounter<long>(
                Meters.ComponentUpdateLimitExceededName,
                Meters.EventUnit,
                Meters.ComponentUpdateLimitExceededDescription);

        meter.CreateObservableUpDownCounter(
            Meters.AttachedComponentCountName,
            AkburaComponentRegistry.GetAttachedComponentCount,
            Meters.ComponentUnit,
            Meters.AttachedComponentCountDescription);
    }

    internal static HistogramReportDisposable BeginComponentInitialization(AkburaControl component)
    {
        return Begin(
            s_componentInitializationDuration,
            component);
    }

    internal static HistogramReportDisposable BeginComponentUpdateBatch(AkburaControl component)
    {
        return Begin(
            s_componentUpdateBatchDuration,
            component);
    }

    internal static void RecordComponentUpdateBatchSize(AkburaControl component, int updateCount)
    {
        if (updateCount <= 0)
        {
            return;
        }

        var histogram = s_componentUpdateBatchSize;
        if (histogram is not { Enabled: true })
        {
            return;
        }

        histogram.Record(
            updateCount,
            new KeyValuePair<string, object?>(
                Tags.ComponentType,
                GetComponentTypeName(component)));
    }

    internal static void RecordComponentUpdateLimitExceeded(AkburaControl component)
    {
        var counter = s_componentUpdateLimitExceeded;
        if (counter is not { Enabled: true })
        {
            return;
        }

        counter.Add(
            1,
            new KeyValuePair<string, object?>(
                Tags.ComponentType,
                GetComponentTypeName(component)));
    }

    private static HistogramReportDisposable Begin(Histogram<double>? histogram, AkburaControl component)
    {
        if (histogram is not { Enabled: true })
        {
            return default;
        }

        return new HistogramReportDisposable(
            histogram,
            GetComponentTypeName(component));
    }

    internal readonly ref struct HistogramReportDisposable
    {
        private readonly Histogram<double>? _histogram;
        private readonly string? _componentType;
        private readonly long _timestamp;

        public HistogramReportDisposable(
            Histogram<double> histogram,
            string componentType)
        {
            _histogram = histogram;
            _componentType = componentType;
            _timestamp = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var histogram = _histogram;
            if (histogram is null)
            {
                return;
            }

            var elapsed =
                Stopwatch.GetElapsedTime(_timestamp)
                    .TotalMilliseconds;

            histogram.Record(
                elapsed,
                new KeyValuePair<string, object?>(
                    Tags.ComponentType,
                    _componentType));
        }
    }
}