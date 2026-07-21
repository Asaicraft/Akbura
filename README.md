# Akbura

[![Discord](https://img.shields.io/discord/1442893504085757984?color=8a2be2&label=discord)](https://discord.gg/zMj4MmJ9U5)
[![Telegram](https://raw.githubusercontent.com/Patrolavia/telegram-badge/master/chat.svg)](https://t.me/akburaui)
[![Documentation](https://img.shields.io/badge/documentation-Akbura-0ea5e9)](https://asaicraft.github.io/Akbura/)

Akbura is an experimental declarative UI language and compiler for .NET and Avalonia, with reactive state and typed styling through AKCSS.

> [!WARNING]
> Akbura is under active development. Syntax and APIs may change.

## Quick start

```bash
dotnet add package Akbura
```

```akbura
using Avalonia.Controls;

namespace Demo.Pages;

inject ILogger<Counter> Logger;

state count = 0;

useEffect(() =>
{
    Logger.LogInfo("Button width is {0}", button.Width);
}, [count]);

<Button Click={() => count++} x.Name="button" w-10 h-4 p-3>
    Count is {count}
</Button>
```

For installation, language syntax, state, commands, hooks, and AKCSS, see the **[Akbura documentation](https://asaicraft.github.io/Akbura/)**.

## Links

- [Akbura documentation](https://asaicraft.github.io/Akbura/)
- [Discord](https://discord.gg/zMj4MmJ9U5)
- [Telegram](https://t.me/akburaui)
- [License](LICENSE.txt)
