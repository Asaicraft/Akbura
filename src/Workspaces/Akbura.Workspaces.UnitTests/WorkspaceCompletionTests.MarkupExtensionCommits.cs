using Akbura.Workspaces.Completion;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed partial class WorkspaceCompletionTests
{
    [Theory]
    [InlineData(false, "St", "StaticResource")]
    [InlineData(true, "St", "StaticResource")]
    [InlineData(false, "B", "Binding")]
    [InlineData(true, "B", "Binding")]
    public void Completion_MarkupExtensionCatalogCommitsAfterNativePairing(bool pairBeforeCatalog, string typedName, string selectedName)
    {
        const string prefix = "using Akbura.Markup;\r\n\r\n<Card Content=${";
        var source = prefix + (pairBeforeCatalog ? "}" : "") + " />";
        WithWorkspace(source, (workspace, semanticContext, document) =>
        {
            var position = prefix.Length;
            var sourceText = document.Text;
            var sourceContext = document.GetCompletionContext(position);
            var catalog = workspace.LanguageServices.Completion.GetCompletions(
                document, semanticContext, position);
            var item = Assert.Single(catalog.Items, candidate => candidate.DisplayText == selectedName);
            Assert.Equal(AkburaCompletionKind.MarkupExtension, item.Kind);
            Assert.Equal(sourceContext.ApplicableSpan, catalog.ApplicableSpan);

            var text = sourceText;
            if (!pairBeforeCatalog)
            {
                text = text.WithChanges(new TextChange(new TextSpan(position, 0), "}"));
                document = document.WithText(text);
            }

            foreach (var character in typedName)
            {
                text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
                document = document.WithText(text);
            }

            // Commit an item from the original catalog, not a fresh request
            // that would hide the race between opening the list and pairing }.
            Assert.StartsWith(typedName, item.FilterText, StringComparison.Ordinal);
            var translatedSpan = new TextSpan(prefix.Length, typedName.Length + (pairBeforeCatalog ? 0 : 1));
            var currentContext = document.GetCompletionContext(position);
            Assert.True(AkburaMarkupExtensionCompletionFacts.IsMatchingTypeNameContext(
                sourceText, text, sourceContext, currentContext, translatedSpan));
            Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
                text, translatedSpan, out var replacementSpan));
            var committed = text.WithChanges(new TextChange(replacementSpan, item.InsertText));
            var caret = replacementSpan.Start + item.InsertText.Length - item.CaretOffsetFromEnd;
            Assert.Equal(prefix + selectedName + "} />", committed.ToString());
            Assert.Equal('}', committed[caret]);
        });
    }
}
