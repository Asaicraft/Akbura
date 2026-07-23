# Akbura.Diagnostics

Akbura diagnostics displays the live component tree together with injected services,
states, and parameters. Simple state values can be edited while the application is
running.

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

The default shortcut is `F12`. A custom Avalonia `KeyGesture` can be supplied:

```csharp
this.AttachAkburaDevTools(
    new KeyGesture(
        Key.D,
        KeyModifiers.Control | KeyModifiers.Shift));
```
