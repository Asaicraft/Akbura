# Dependency Injection — Advanced Concepts

> **Note:** This section comes from `draft-concepts` and may not be implemented. These are exploratory ideas for extending Akbura's DI system.

Akbura’s standard DI integrates with any `IServiceProvider`. For more advanced behaviors—such as component-scoped services, hierarchical resolution, custom lifetimes, and DI chaining—the framework conceptually supports an extended provider interface.

---

## Extended Service Provider

To enable more complex resolution logic, an optional interface `IAkburaServiceProvider` can be introduced:

```csharp
interface IAkburaServiceProvider : IServiceProvider
{
    object? GetService(Type serviceType)
    {
        var injectInfo = new InjectionInfo(serviceType);
        return GetService(injectInfo);
    }

    object? GetService(InjectionInfo injectionInfo);
}
```

This advanced overload receives contextual metadata about the injection request.

---

## InjectionInfo

```csharp
public ref readonly struct InjectionInfo
{
    public readonly Type RequestedService;
    public readonly AkburaComponent? TargetComponent;
    public readonly IAkburaServiceProvider? NextProvider;
    public readonly bool IsOptional;
    public readonly string? FieldName; // Name of the injected field
}
```

This gives full visibility into:

* which component is requesting the service,
* what field is being injected,
* whether the dependency is optional,
* and what the next provider in the chain is.

This enables powerful behaviors like scoped services tied to component hierarchies.

---

## Example: Component-Scoped Services

```csharp
public class ScopedProvider : IAkburaServiceProvider
{
    public object? GetService(InjectionInfo injectionInfo)
    {
        if (injectionInfo.TargetComponent is not { } component)
            return injectionInfo.NextProvider?.GetService(injectionInfo);

        var scope = component.FindParent<ActivateScope>();

        if (scope == null)
            return injectionInfo.NextProvider?.GetService(injectionInfo);

        return scope.ScopedServiceProvider.GetService(injectionInfo.RequestedService);
    }
}
```

This allows a service to depend on the **location of the component** inside the tree—useful for things like:

* navigation scopes,
* per-module services,
* per-view state containers,
* transactional or request-like lifetimes.

---

## Registering Multiple Providers

A conceptual model for chaining multiple `IAkburaServiceProvider` instances:

```csharp
AkburaRoot.Builder()
    .WithServiceProviders(() =>
    {
        var services = ServiceCollection.BuildServiceProvider();
        var scopedProvider = new ScopedProvider();

        return new IServiceProvider[]
        {
            scopedProvider,
            services
        };
    })
    .Run<MyRoot>();
```

Here Akbura resolves dependencies by walking the chain:

1. `ScopedProvider`
2. then `ServiceCollection` provider
3. and so on

Each gets access to `NextProvider` so it can forward the request.

---

## Status

These ideas are currently **experimental** and part of `draft-concepts`. They may evolve significantly or be redesigned before becoming part of the Akbura framework.

Their main purpose is to explore how DI can adapt to a component-driven UI model, where service lifetime may depend on UI structure rather than the traditional app-wide scope.
