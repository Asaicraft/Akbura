# Akbura.BlackSilence

BlackSilence is the production incremental compiler for Akbura component and AKCSS files.

Applications normally receive it from the `Akbura` package. Install `Akbura.BlackSilence` directly only when integrating the compiler without the Akbura runtime package.

## Diagnostics

The `Akbura` package configures BlackSilence to publish diagnostics and automatically coordinates ownership with the Akbura workspace:

```xml
<PropertyGroup>
    <AkburaBlackSilenceDiagnostics>Publish</AkburaBlackSilenceDiagnostics>
    <AkburaDiagnosticPublisher>Auto</AkburaDiagnosticPublisher>
</PropertyGroup>
```

Set `AkburaBlackSilenceDiagnostics` to `Shadow` to compute diagnostics without publishing them, or to `Off` to disable diagnostic computation. Direct programmatic use of the generator remains in `Shadow` mode when no MSBuild property is supplied.

Furioso is retained as a compatibility baseline for tests and benchmarks; it is not the compiler shipped by the `Akbura` package.
