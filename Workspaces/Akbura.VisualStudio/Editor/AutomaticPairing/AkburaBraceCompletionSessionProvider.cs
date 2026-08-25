using Akbura.Workspaces;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.BraceCompletion;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

[Export(typeof(IBraceCompletionSessionProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
[BracePair('{', '}')]
[BracePair('(', ')')]
[BracePair('[', ']')]
[BracePair('"', '"')]
[BracePair('<', '>')]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class AkburaBraceCompletionSessionProvider :
    IBraceCompletionSessionProvider
{
    private static readonly TimeSpan ParseBudget =
        TimeSpan.FromMilliseconds(40);

    private readonly AkburaParserService _parserService;

    [ImportingConstructor]
    public AkburaBraceCompletionSessionProvider(
        AkburaParserService parserService)
    {
        _parserService = parserService ??
            throw new ArgumentNullException(nameof(parserService));

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            "Brace completion provider created.");
    }

    public bool TryCreateSession(
        ITextView textView,
        SnapshotPoint openingPoint,
        char openingBrace,
        char closingBrace,
        out IBraceCompletionSession session)
    {
        session = null!;

        var snapshot = openingPoint.Snapshot;
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            $"Brace session requested: opening='{openingBrace}', " +
            $"closing='{closingBrace}', " +
            $"position={openingPoint.Position}, " +
            $"snapshot={snapshot.Version.VersionNumber}.");

        if (textView == null ||
            textView.IsClosed ||
            !textView.Selection.IsEmpty ||
            textView.GetMultiSelectionBroker().HasMultipleSelections ||
            !IsExpectedPair(openingBrace, closingBrace))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Brace session rejected: editor state or pair is unsupported.");
            return false;
        }

        if (openingPoint.Position < snapshot.Length &&
            snapshot[openingPoint.Position] == closingBrace)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Brace session rejected: closing character already exists.");
            return false;
        }

        try
        {
            using var budget = new CancellationTokenSource(ParseBudget);
            var document = _parserService.GetSyntacticDocument(
                snapshot,
                budget.Token);
            var decision = document.GetAutomaticPairDecision(
                openingPoint.Position,
                openingBrace,
                budget.Token);
            var openingCharacterAlreadyPresent =
                openingPoint.Position > 0 &&
                snapshot[openingPoint.Position - 1] == openingBrace;
            var isStructuralAkcssBrace =
                openingBrace == '{' &&
                openingCharacterAlreadyPresent &&
                document.ShouldAutoCloseCurlyBrace(
                    openingPoint.Position,
                    budget.Token);

            if (!isStructuralAkcssBrace &&
                (!decision.IsFixed ||
                 decision.ClosingText[0] != closingBrace))
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                    $"Brace session rejected by syntax: " +
                    $"context={decision.ContextKind}, " +
                    $"closing='{decision.ClosingText}'.");
                return false;
            }

            if (openingCharacterAlreadyPresent &&
                openingBrace == '{' &&
                decision.ContextKind ==
                    AkburaPairContextKind.AkcssSyntax &&
                !isStructuralAkcssBrace)
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                    "Brace session rejected: AKCSS brace is not structural.");
                return false;
            }

            session = new AkburaBraceCompletionSession(
                textView,
                openingPoint,
                openingBrace,
                closingBrace);
            var contextName = isStructuralAkcssBrace
                ? "StructuralAkcss"
                : decision.ContextKind.ToString();
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                $"Brace session accepted: context={contextName}.");
            return true;
        }
        catch (OperationCanceledException)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Brace session rejected: syntax budget expired.");
            return false;
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Automatic pair decision failed: " + exception);
            return false;
        }
    }

    private static bool IsExpectedPair(
        char openingBrace,
        char closingBrace)
    {
        return openingBrace switch
        {
            '{' => closingBrace == '}',
            '(' => closingBrace == ')',
            '[' => closingBrace == ']',
            '"' => closingBrace == '"',
            '<' => closingBrace == '>',
            _ => false,
        };
    }
}
