using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Diagnostics;

internal static partial class Diagnostic
{
    private const string ActivitySourceName = "Akbura.Diagnostic.Source";

    private const string MeterName = "Akbura.Diagnostic.Meter";

    internal static class Activities
    {
        public const string ComponentInitialize = "Akbura.Component.Initialize";

        public const string ComponentUpdateBatch = "Akbura.Component.UpdateBatch";
    }

    internal static class Meters
    {
        public const string MillisecondsUnit = "ms";
        public const string UpdateUnit = "{update}";
        public const string ComponentUnit = "{component}";
        public const string EventUnit = "{event}";

        public const string ComponentInitializationDurationName = "akbura.component.initialize.duration";

        public const string ComponentInitializationDurationDescription = "Duration of Akbura component initialization.";

        public const string ComponentUpdateBatchDurationName = "akbura.component.update.batch.duration";

        public const string ComponentUpdateBatchDurationDescription = "Duration of one synchronous Akbura component update batch.";

        public const string ComponentUpdateBatchSizeName = "akbura.component.update.batch.size";

        public const string ComponentUpdateBatchSizeDescription = "Number of Update() passes executed in one synchronous batch.";

        public const string ComponentUpdateLimitExceededName = "akbura.component.update.limit_exceeded.count";

        public const string ComponentUpdateLimitExceededDescription = "Number of component update batches that exceeded the configured update limit.";

        public const string AttachedComponentCountName = "akbura.component.attached.count";

        public const string AttachedComponentCountDescription = "Number of Akbura components currently attached to a visual tree.";
    }

    internal static class Tags
    {
        public const string ComponentType = "akbura.component.type";

        public const string UpdateCount = "akbura.update.count";

        public const string UpdateLimit = "akbura.update.limit";

        public const string ErrorType = "error.type";
    }
}