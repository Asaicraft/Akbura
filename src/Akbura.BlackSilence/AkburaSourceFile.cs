using Akbura.Language;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.BlackSilence;

/// <summary>
/// Contains source text read directly from one additional file.
/// </summary>
internal readonly record struct AkburaSourceText(
    SyntaxTreeKind Kind,
    string FilePath,
    SourceText SourceText);

/// <summary>
/// Contains all information required to parse one Akbura source file.
/// </summary>
internal readonly record struct AkburaSourceFile(
    SyntaxTreeKind Kind,
    string FilePath,
    string LogicalName,
    SourceText SourceText);
