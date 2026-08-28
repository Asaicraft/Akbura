---
title: Using Akbura Components in AXAML
summary: Use generated Akbura components directly inside existing Avalonia AXAML views.
---


Akbura components can be used directly inside regular Avalonia AXAML.

A separate host control is not required. Every generated Akbura component is an Avalonia control, so it can be referenced through an XML namespace and placed in the visual tree like any other custom control.

## Basic usage

Suppose the project contains the following component:

```akbura
// Counter.akbura

namespace MyControls;

state int count = 0;

<Button Click={count++}>
    Count: {count}
</Button>
```

Import its namespace in AXAML:

```xml
<UserControl
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:myControls="using:MyControls">

    <myControls:Counter />

</UserControl>
```

The XML namespace points to the C# namespace declared by the `.akbura` component:

```xml
xmlns:myControls="using:MyControls"
```

The component can then be created directly:

```xml
<myControls:Counter />
```

## Passing parameters

Akbura parameters are exposed as Avalonia properties and can be assigned through AXAML attributes.

Consider this component:

```akbura
// AkParams.akbura

namespace MyControls;

param int Count = 1;
param int B;

<StackPanel>
    <TextBlock Text={$"Count: {Count}"} />
    <TextBlock Text={$"B: {B}"} />
</StackPanel>
```

The component declares two parameters:

| Parameter | Required | Reason                       |
| --------- | -------- | ---------------------------- |
| `Count`   | No       | It has the default value `1` |
| `B`       | Yes      | It has no default value      |

You can use the component from AXAML:

```xml
<UserControl
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:myControls="using:MyControls">

    <StackPanel>
        <myControls:AkParams Count="10" />

        <myControls:AkParams B="10" />

        <myControls:AkParams
            Count="1"
            B="10" />
    </StackPanel>

</UserControl>
```

The first component is invalid:

```xml
<myControls:AkParams Count="10" />
```

Although `Count` is set, the required `B` parameter is missing.

When the component is initialized, Akbura throws:

```text
AkburaParameterNotSettedException
```

The following component is valid:

```xml
<myControls:AkParams B="10" />
```

`B` is explicitly set, while `Count` uses its default value of `1`.

Both parameters may also be set explicitly:

```xml
<myControls:AkParams
    Count="10"
    B="20" />
```

## Binding parameters

Because parameters are Avalonia properties, normal AXAML bindings can be used:

```xml
<myControls:AkParams
    Count="{Binding CurrentCount}"
    B="{Binding RequiredValue}" />
```

The binding result must be compatible with the parameter type.

For example, the view model may expose:

```csharp
public sealed class MyViewModel
{
    public int CurrentCount { get; set; }

    public int RequiredValue { get; set; }
}
```

## Passing injected services

Services declared with `inject` are also exposed as Avalonia properties.

Consider the following component:

```akbura
// Counter.akbura

namespace MyControls;

inject ILogger<Counter> Logger;

state int count = 0;

useEffect(() =>
{
    Logger.LogInformation("Count changed to {Count}", count);
}, [count]);

<Button Click={count++}>
    Count: {count}
</Button>
```

Normally, Akbura attempts to resolve `Logger` through its service provider.

The service may instead be passed directly from AXAML:

```xml
<UserControl
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:myControls="using:MyControls">

    <myControls:Counter
        Logger="{Binding Logger}" />

</UserControl>
```

The AXAML view model must expose a compatible service:

```csharp
public sealed class MyViewModel
{
    public ILogger<Counter> Logger { get; }

    public MyViewModel(ILogger<Counter> logger)
    {
        Logger = logger;
    }
}
```

When an injected service property already contains a value, Akbura keeps that value and does not replace it through dependency injection.

This makes it possible to provide services through:

* an AXAML binding;
* a property assignment in C#;
* Akbura's configured service provider.

## Setting services from code

An injected service can also be assigned when creating the component from C#:

```csharp
var counter = new Counter
{
    Logger = logger
};
```

The explicitly assigned service is used when the component initializes.

## Complete example

Akbura component:

```akbura
// UserCard.akbura

namespace MyControls;

param int UserId;
param string Title = "User";

inject IUserService UserService;

state string userName = "";

useEffect(async () =>
{
    var user = await UserService.GetUser(UserId);
    userName = user.Name;
}, [UserId]);

<StackPanel>
    <TextBlock Text={Title} />
    <TextBlock Text={userName} />
</StackPanel>
```

Avalonia view:

```xml
<UserControl
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:myControls="using:MyControls">

    <StackPanel>
        <myControls:UserCard
            UserId="{Binding SelectedUserId}"
            Title="Profile"
            UserService="{Binding UserService}" />
    </StackPanel>

</UserControl>
```

Akbura components therefore integrate into existing Avalonia applications without a wrapper or a separate hosting element.