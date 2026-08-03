using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Akbura.Diagnostics;

internal static partial class Diagnostic
{
    private static ActivitySource? s_activitySource;

    private static void InitActivitySource()
    {
        s_activitySource = new ActivitySource(
            ActivitySourceName);
    }

    internal static Activity? StartComponentInitialization(
        AkburaControl component)
    {
        return StartComponentActivity(
            Activities.ComponentInitialize,
            component);
    }

    internal static Activity? StartComponentUpdateBatch(
        AkburaControl component)
    {
        return StartComponentActivity(
            Activities.ComponentUpdateBatch,
            component);
    }

    internal static void SetActivityError(
        Activity? activity,
        Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error);
        activity.SetTag(
            Tags.ErrorType,
            exception.GetType().FullName);
    }

    private static Activity? StartComponentActivity(
        string name,
        AkburaControl component)
    {
        var activity =
            s_activitySource?.StartActivity(name);

        if (activity is null)
        {
            return null;
        }

        activity.SetTag(
            Tags.ComponentType,
            GetComponentTypeName(component));

        return activity;
    }
}