using System.Reflection;
using Akbura.CompilerAnotations;
using Akbura.Hooks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Akbura.UnitTests;

public sealed class ControlEventHookCoverageTests
{
    [Fact]
    public void PublicControlEvents_HaveExactlyThreeTypedHookOverloads()
    {
        var events = typeof(Control).GetEvents(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(47, events.Length);

        var expectedNames = events
            .Select(static @event => $"use{@event.Name}")
            .Append("useRequestBringIntoView")
            .ToHashSet(StringComparer.Ordinal);
        var hookMethods = typeof(ControlEventHooks)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        var actualNames = hookMethods
            .Select(static method => method.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(expectedNames.Order(), actualNames.Order());
        foreach (var @event in events)
        {
            AssertHookFamily(
                hookMethods,
                $"use{@event.Name}",
                GetEventArgsType(@event.EventHandlerType!),
                @event.EventHandlerType!,
                FindRoutedEvent(@event.Name) != null,
                isInitialized: @event.Name == nameof(Control.Initialized));
        }

        var requestBringIntoViewEvent = Assert.IsType<RoutedEvent<RequestBringIntoViewEventArgs>>(
            typeof(Control).GetField(
                nameof(Control.RequestBringIntoViewEvent),
                BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        AssertHookFamily(
            hookMethods,
            "useRequestBringIntoView",
            typeof(RequestBringIntoViewEventArgs),
            typeof(EventHandler<RequestBringIntoViewEventArgs>),
            isRouted: true,
            isInitialized: false);
        Assert.Same(Control.RequestBringIntoViewEvent, requestBringIntoViewEvent);
    }

    private static void AssertHookFamily(MethodInfo[] methods, string name, Type eventArgsType, Type eventHandlerType, bool isRouted, bool isInitialized)
    {
        var overloads = methods.Where(method => method.Name == name).ToArray();
        Assert.Equal(3, overloads.Length);

        var callbackTypes = overloads
            .Select(static method => method.GetParameters()[1].ParameterType)
            .ToHashSet();
        Assert.Contains(typeof(Action), callbackTypes);
        Assert.Contains(typeof(Action<>).MakeGenericType(eventArgsType), callbackTypes);
        Assert.Contains(eventHandlerType, callbackTypes);

        foreach (var overload in overloads)
        {
            Assert.NotNull(overload.GetCustomAttribute<UseHookAttribute>());
            var parameters = overload.GetParameters();
            Assert.Equal(typeof(AkburaControl), parameters[0].ParameterType);
            Assert.NotNull(parameters[0].GetCustomAttribute<SelfAttribute>());

            if (isRouted)
            {
                Assert.Equal(4, parameters.Length);
                Assert.Equal(typeof(RoutingStrategies), parameters[2].ParameterType);
                Assert.Equal(RoutingStrategies.Direct | RoutingStrategies.Bubble, parameters[2].DefaultValue);
                Assert.Equal(typeof(bool), parameters[3].ParameterType);
                Assert.Equal(false, parameters[3].DefaultValue);
            }
            else if (isInitialized)
            {
                Assert.Equal(3, parameters.Length);
                Assert.Equal(typeof(bool), parameters[2].ParameterType);
                Assert.Equal(true, parameters[2].DefaultValue);
            }
            else
            {
                Assert.Equal(2, parameters.Length);
            }
        }
    }

    private static Type GetEventArgsType(Type eventHandlerType)
    {
        if (eventHandlerType == typeof(EventHandler))
        {
            return typeof(EventArgs);
        }

        Assert.True(eventHandlerType.IsGenericType);
        Assert.Equal(typeof(EventHandler<>), eventHandlerType.GetGenericTypeDefinition());
        return eventHandlerType.GetGenericArguments()[0];
    }

    private static RoutedEvent? FindRoutedEvent(string eventName)
    {
        for (var type = typeof(Control); type != null; type = type.BaseType)
        {
            var field = type.GetField(
                $"{eventName}Event",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field?.GetValue(null) is RoutedEvent routedEvent)
            {
                return routedEvent;
            }
        }

        return null;
    }
}
