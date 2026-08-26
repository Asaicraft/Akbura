namespace Akbura.LanguageServer.UnitTests;

public sealed class WorkspaceEditMapperTests
{
    [Fact]
    public void MapsMultipleTextChangesWithoutChangingOrder()
    {
        var text = SourceText.From("alpha\nbeta");
        var changes = ImmutableArray.Create(
            new TextChange(new TextSpan(0, 5), "one"),
            new TextChange(new TextSpan(6, 4), "two"));

        var edits = AkburaProtocolMapper.ToTextEdits(
            text,
            changes,
            new Utf16PositionConverter());

        Assert.Equal(2, edits.Length);
        Assert.Equal("one", edits[0].NewText);
        Assert.Equal(0, edits[0].Range.Start.Line);
        Assert.Equal(1, edits[1].Range.Start.Line);
    }

    [Fact]
    public void MapsVersionedDocumentChangesForCapableClients()
    {
        var uri = new Uri("file:///workspace/Component.akbura");
        var text = SourceText.From("old");
        var edit = AkburaWorkspaceEditMapper.Create(
            ImmutableArray.Create(
                new AkburaWorkspaceEditDocument(
                    uri,
                    text,
                    ImmutableArray.Create(
                        new TextChange(
                            new TextSpan(0, 3),
                            "new")),
                    Version: 7)),
            supportsDocumentChanges: true,
            new Utf16PositionConverter());

        Assert.Null(edit.Changes);
        var documentChange = Assert.Single(edit.DocumentChanges!);
        Assert.Equal(uri.AbsoluteUri, documentChange.TextDocument.Uri);
        Assert.Equal(7, documentChange.TextDocument.Version);
        Assert.Equal("new", Assert.Single(documentChange.Edits).NewText);
    }

    [Fact]
    public void FallsBackToLegacyChangesForOlderClients()
    {
        var uri = new Uri("file:///workspace/Component.akbura");
        var edit = AkburaWorkspaceEditMapper.Create(
            ImmutableArray.Create(
                new AkburaWorkspaceEditDocument(
                    uri,
                    SourceText.From("old"),
                    ImmutableArray.Create(
                        new TextChange(
                            new TextSpan(0, 3),
                            "new")),
                    Version: 7)),
            supportsDocumentChanges: false,
            new Utf16PositionConverter());

        Assert.Null(edit.DocumentChanges);
        Assert.Equal(
            "new",
            Assert.Single(edit.Changes![uri.AbsoluteUri]).NewText);
    }}