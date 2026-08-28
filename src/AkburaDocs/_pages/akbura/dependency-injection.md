---
title: Dependency Injection
summary: Register services with Akbura and consume them from components with the inject declaration.
---

Akbura components can request application services with the `inject` declaration.

```akbura
inject IUserService UserService;
```

The generated component exposes `UserService` as a public Avalonia property and asks the configured service provider for an `IUserService` when the component is initialized.

## Register a service provider

Akbura accepts any standard `IServiceProvider`.

The following example uses `Microsoft.Extensions.DependencyInjection`:

```csharp
using Akbura.Engine;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection()
    .AddSingleton<IUserService, UserService>()
    .BuildServiceProvider();

AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .UseAkbura(akbura =>
    {
        akbura.WithServiceProvider(services);
    });
```

`WithServiceProvider` adds the provider to the Akbura engine. Service lifetimes such as singleton, scoped, and transient are controlled by the registered provider.

## Inject a required service

A non-nullable injected type is required:

```akbura
inject IUserService UserService;

state string userName = "Loading...";

useEffect(async () =>
{
    var user = await UserService.GetCurrentUser();
    userName = user.Name;
}, []);

<StackPanel>
    <TextBlock Text="Current user" />
    <TextBlock Text={userName} />
</StackPanel>
```

Akbura resolves required services before the component performs its first update. If the provider cannot return the requested service, Akbura throws `AkburaServiceNotFoundException`.

## Inject an optional service

Add `?` to the injected type to make the dependency optional:

```akbura
inject IClipboardService? Clipboard;

<Button
    Content="Copy"
    IsEnabled={Clipboard != null}
    Click={() => Clipboard?.SetTextAsync("Akbura")} />
```

When an optional service is not registered, the generated property remains `null` and component initialization continues normally.

## Explicit values take priority

An injected service is also a public Avalonia property. It may be supplied directly instead of being resolved from the configured provider.

From another Akbura component:

```akbura
<UserCard UserService={previewUserService} />
```

From AXAML:

```xml
<components:UserCard
    UserService="{Binding UserService}" />
```

From C#:

```csharp
var card = new UserCard
{
    UserService = previewUserService
};
```

When the property already contains a non-null value, Akbura keeps that value and does not call the service provider for that dependency. This is useful for previews, tests, and local overrides.

## Configuration object example

A service does not need to contain behavior. Configuration objects can also be injected.

```csharp
public sealed class GalleryOptions
{
    public string RepositoryUrl { get; set; } =
        "https://github.com/Asaicraft/Akbura";

    public string MainBranchName { get; set; } =
        "master";

    public string PathToGallery { get; set; } =
        "src/Akbura.FeatureGallery/Akbura.FeatureGallery";
}
```

Register one shared instance:

```csharp
var services = new ServiceCollection()
    .AddSingleton(new GalleryOptions())
    .BuildServiceProvider();

builder.UseAkbura(akbura =>
{
    akbura.WithServiceProvider(services);
});
```

Use it from a component:

```akbura
inject GalleryOptions GalleryOptions;

param object Content;
param string Url;

<HyperlinkButton
    NavigateUri={new Uri(
        $"{GalleryOptions.RepositoryUrl}/blob/" +
        $"{GalleryOptions.MainBranchName}/" +
        $"{GalleryOptions.PathToGallery}/{Url}")}
    Content={Content} />
```

The component only declares what it needs. Creation and lifetime of `GalleryOptions` remain outside the component.

## Resolution process

For every `inject` declaration, Akbura follows this process:

1. Check whether the generated service property already contains a value.
2. If it does, keep that value.
3. Otherwise, request the service from the configured providers.
4. Assign the resolved object to the generated property.
5. Leave an unresolved optional dependency as `null`.
6. Throw `AkburaServiceNotFoundException` for an unresolved required dependency.

Injection happens before the first component update, so the dependency is available to the initial markup and subsequent effects.

## Multiple service providers

More than one provider may be registered:

```csharp
builder.UseAkbura(akbura =>
{
    akbura
        .WithServiceProvider(featureProvider)
        .WithServiceProvider(applicationProvider);
});
```

Providers are queried in registration order. Standard `IServiceProvider` instances automatically continue to the next provider when they return `null`.

## Custom Akbura service providers

For contextual resolution, implement `IAkburaServiceProvider`:

```csharp
using Akbura.Engine;

public sealed class FeatureServiceProvider : IAkburaServiceProvider
{
    private readonly FeatureState _state = new();

    public object? GetService(
        ref readonly InjectionInfo injectionInfo)
    {
        if (injectionInfo.RequestedService ==
            typeof(FeatureState))
        {
            return _state;
        }

        return injectionInfo.NextProvider?
            .GetService(in injectionInfo);
    }
}
```

`InjectionInfo` provides:

| Property | Description |
| --- | --- |
| `RequestedService` | The requested service type |
| `TargetControl` | The Akbura component requesting the service |
| `FieldName` | The name declared after `inject` |
| `IsOptional` | Whether the injected type is nullable |
| `NextProvider` | The next provider in the configured chain |

A custom provider must call `NextProvider` when it wants resolution to continue. Returning a value or returning `null` without forwarding stops the chain.

## What Akbura generates

For a declaration such as:

```akbura
inject IUserService UserService;
```

Akbura generates the equivalent of:

```csharp
private IUserService? __service_UserService;

public static readonly
    InjectService<MyComponent, IUserService>
    UserServiceProperty =
        InjectService.Create<MyComponent, IUserService>(
            "UserService",
            static owner => owner.__service_UserService,
            static (owner, value) =>
                owner.__SetService_UserService(value),
            isOptional: false);

public IUserService UserService
{
    get => __service_UserService!;
    set => __SetService_UserService(value);
}
```

The exact generated names are implementation details, but the important result is that every injected dependency becomes a settable Avalonia property backed by an `InjectService` descriptor.
