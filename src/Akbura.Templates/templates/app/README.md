# Akbura application

This project includes two complementary diagnostics tools in Debug builds:

- Press `F12` to open Avalonia Developer Tools.
- Press `Ctrl+F12` to open the Akbura component inspector.

The diagnostic package references and startup code are excluded from Release builds.

Avalonia Developer Tools is an external application. Install it separately when needed:

```powershell
dotnet tool install --global AvaloniaUI.DeveloperTools
```

The Akbura inspector is provided by the `Akbura.Diagnostics` package and requires no
additional global tool.
