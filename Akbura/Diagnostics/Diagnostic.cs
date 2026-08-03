using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Diagnostics;

internal static partial class Diagnostic
{
    private const string IsEnabledSwitchName = "Akbura.Diagnostics.Diagnostic.IsEnabled";

    public static bool IsEnabled { get; }

    static Diagnostic()
    {
        IsEnabled = InitializeIsEnabled();
        if (!IsEnabled)
        {
            return;
        }

        InitActivitySource();
        InitMetrics();
    }

    private static bool InitializeIsEnabled()
    {
        return AppContext.TryGetSwitch(
                   IsEnabledSwitchName,
                   out var isEnabled) &&
               isEnabled;
    }

    private static string GetComponentTypeName(
        AkburaControl component)
    {
        var type = component.GetType();
        return type.FullName ?? type.Name;
    }
}