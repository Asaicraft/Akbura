using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Formatting;

public interface IAkburaFormattingService
{
    ImmutableArray<TextChange> FormatDocument(
        AkburaSyntacticDocument document,
        AkburaFormattingOptions options,
        CancellationToken cancellationToken = default);

    ImmutableArray<TextChange> FormatRange(
        AkburaSyntacticDocument document,
        TextSpan range,
        AkburaFormattingOptions options,
        CancellationToken cancellationToken = default);

    ImmutableArray<TextChange> FormatOnType(
        AkburaSyntacticDocument document,
        int position,
        char typedCharacter,
        AkburaFormattingOptions options,
        CancellationToken cancellationToken = default);
}