using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed partial class WorkspaceCompletionTests
{
    [Theory]
    [InlineData(false, "", "c")]
    [InlineData(true, "", "c")]
    [InlineData(false, "c", "ou")]
    [InlineData(true, "c", "ou")]
    public void CSharpCompletion_OriginalCountItemCommitsAfterNativePairing(bool pairBeforeCatalog, string initialName, string typedName)
    {
        const string prefix = "using Avalonia.Controls;\r\nstate int count = 0;\r\n\r\n<TextBlock Text={";
        WithWorkspace(prefix + "count}/>", (_, semanticContext, _) =>
        {
            var text = SourceText.From(prefix + initialName + (pairBeforeCatalog ? "}" : "") + "/>");
            var document = AkburaSyntacticDocument.Parse(text, semanticContext.Document.FilePath);
            var position = prefix.Length + initialName.Length;
            Assert.True(document.TryGetCSharpCompletionContext(position, out var sourceContext));
            Assert.True(AkburaCSharpProjectionFactory.TryCreate(document, semanticContext, sourceContext, out var projection));

            // Keep this item and projection from before the pair/filter edits.
            var completion = RoslynCompletionTestHost.GetCompletionChangeAsync(
                semanticContext.Project.CSharpCompilation, projection.Root, projection.ProjectedPosition,
                "count", requireComplexTextEdit: false, CancellationToken.None).GetAwaiter().GetResult();
            Assert.NotNull(completion);
            Assert.True(AkburaCSharpCompletionChangeMapper.TryMapCompletionChange(
                text, completion.Value.ProjectedText, projection, completion.Value.Change, out var mapped));
            var originalChange = Assert.Single(mapped.Changes);
            Assert.Equal("count", originalChange.NewText);

            var sourceText = text;
            var translatedOwner = sourceContext.OwnerSpan;
            var translatedHost = sourceContext.HostSpan;
            var translatedChange = originalChange.Span;
            if (!pairBeforeCatalog)
            {
                Insert("}", advanceCaret: false);
            }

            foreach (var character in typedName)
            {
                Insert(character.ToString(), advanceCaret: true);
            }

            Assert.True(document.TryGetCSharpCompletionContext(position, out var currentContext));
            Assert.True(AkburaCSharpCompletionCommitFacts.IsMatchingContext(
                sourceText, text, sourceContext, currentContext, translatedOwner, translatedHost, out var reason), reason.ToString());
            Assert.Equal(AkburaCSharpCompletionRejectionReason.None, reason);
            Assert.True(AkburaCSharpCompletionCommitFacts.TryGetReplacementSpan(
                text, currentContext, translatedChange, out var replacement));
            var committed = text.WithChanges(new TextChange(replacement, originalChange.NewText!));
            var caret = mapped.NewHostPosition ?? replacement.Start + originalChange.NewText!.Length;
            Assert.Equal(prefix + "count}/>", committed.ToString());
            Assert.Equal('}', committed[caret]);

            void Insert(string insertion, bool advanceCaret)
            {
                text = text.WithChanges(new TextChange(new TextSpan(position, 0), insertion));
                document = document.WithText(text);
                translatedOwner = WorkspaceCSharpCompletionCommitTests.ExpandForInsertion(translatedOwner, position, insertion.Length);
                translatedHost = WorkspaceCSharpCompletionCommitTests.ExpandForInsertion(translatedHost, position, insertion.Length);
                translatedChange = WorkspaceCSharpCompletionCommitTests.ExpandForInsertion(translatedChange, position, insertion.Length);
                if (advanceCaret)
                {
                    position += insertion.Length;
                }
            }
        });
    }
}
