# Akbura.Diagnostics

Akbura diagnostics displays the live component tree together with injected services,
states, and parameters. State and parameter values can be edited while the application
is running.

Attach it from `Application.Initialize()` in debug builds:

```csharp
public override void Initialize()
{
    AvaloniaXamlLoader.Load(this);

#if DEBUG
    this.AttachAkburaDevTools();
#endif
}
```

The default shortcut is `F12`. Configure it through `AkburaDiagnosticsOptions`:

```csharp
this.AttachAkburaDevTools(options =>
{
    options.ToggleGesture = new KeyGesture(
        Key.D,
        KeyModifiers.Control | KeyModifiers.Shift);
});
```

The built-in editors cover strings and numeric values. The fallback editor accepts
values supported by a .NET `TypeConverter`, a public `Parse`/`TryParse` method, or a
JSON representation. A custom editor can be given priority for an application type:

```csharp
this.AttachAkburaDevTools(options =>
{
    options.InputBuilders.Insert(0, new CustomerInputBuilder());
    options.Services = serviceProvider;
});
```

An input builder receives an `InputRequest` containing the component, member name,
declared type, current value, and optional service provider. It exposes its value via
`InputBuilder.InputValueProperty`; the diagnostics UI handles Apply, Reload, validation,
and writing the result back to the component.
