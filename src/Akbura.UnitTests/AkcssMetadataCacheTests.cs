using Akbura.CompilerAnotations;
using Akbura.HotReload;
using Avalonia.Controls;
using System.Collections;
using System.Reflection;

namespace Akbura.UnitTests;

public sealed class AkcssMetadataCacheTests
{
    private static readonly Type SignalType = typeof(AkburaControl).Assembly.GetType("Akbura.Akcss.AkcssObservedPropertySignal")!;
    private static IDictionary Cache => (IDictionary)SignalType.GetField("s_propertyNames", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    [Fact]
    public void UpdatedStyle_InvalidatesItAndDerivedStylesButNotUnrelatedStyles()
    {
        Warm(typeof(Module.WidthStyle), typeof(DerivedStyle), typeof(UnrelatedStyle));
        var unrelated = Cache[typeof(UnrelatedStyle)];
        AkburaHotReloadRuntime.ClearAkcssMetadataCache([typeof(Module.WidthStyle)]);
        Assert.False(Cache.Contains(typeof(Module.WidthStyle)));
        Assert.False(Cache.Contains(typeof(DerivedStyle)));
        Assert.Same(unrelated, Cache[typeof(UnrelatedStyle)]);
    }

    [Fact]
    public void UpdatedModule_InvalidatesNestedStylesAndExternalDerivedStyles()
    {
        Warm(typeof(Module.WidthStyle), typeof(Module.HeightStyle), typeof(DerivedStyle), typeof(UnrelatedStyle));
        var unrelated = Cache[typeof(UnrelatedStyle)];
        AkburaHotReloadRuntime.ClearAkcssMetadataCache([typeof(Module)]);
        Assert.False(Cache.Contains(typeof(Module.WidthStyle)));
        Assert.False(Cache.Contains(typeof(Module.HeightStyle)));
        Assert.False(Cache.Contains(typeof(DerivedStyle)));
        Assert.Same(unrelated, Cache[typeof(UnrelatedStyle)]);
    }

    [Fact]
    public void EmptyUpdate_RetainsCachedNamesAndNullUpdateClearsUnknownScope()
    {
        Warm(typeof(UnrelatedStyle));
        var names = Cache[typeof(UnrelatedStyle)];
        AkburaHotReloadRuntime.ClearAkcssMetadataCache([]);
        Assert.Same(names, Cache[typeof(UnrelatedStyle)]);
        AkburaHotReloadRuntime.ClearAkcssMetadataCache(null);
        Assert.False(Cache.Contains(typeof(UnrelatedStyle)));
        Warm(typeof(UnrelatedStyle));
        Assert.NotSame(names, Cache[typeof(UnrelatedStyle)]);
    }

    private static void Warm(params Type[] types)
    {
        var create = SignalType.GetMethod("Create", BindingFlags.Static | BindingFlags.Public)!;
        foreach (var type in types)
        {
            create.Invoke(null, [type, new Border()]);
        }
    }

    private static class Module
    {
        [ObservesProperty("Width")]
        public class WidthStyle { }

        [ObservesProperty("Height")]
        public class HeightStyle { }
    }

    private sealed class DerivedStyle : Module.WidthStyle { }

    [ObservesProperty("Height")]
    private sealed class UnrelatedStyle { }
}
