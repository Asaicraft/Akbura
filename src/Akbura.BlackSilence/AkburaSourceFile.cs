using Akbura.Language;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.BlackSilence;

internal sealed class AkburaSourceFile(
    SyntaxTreeKind kind,
    string filePath,
    string logicalName,
    SourceText sourceText)
{
    public SyntaxTreeKind Kind { get; } =
        kind;

    public string FilePath { get; } =
        filePath;

    public string LogicalName { get; } =
        logicalName;

    public SourceText SourceText { get; } =
        sourceText;
}
