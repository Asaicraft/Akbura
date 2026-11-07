namespace Akbura.AvaloniaEdit.QuickInfo;

public sealed record AkburaEditorHover(TextSpan Span, string Signature,
    ImmutableArray<string> Details, ImmutableArray<AkburaDiagnosticSpan> Diagnostics);
