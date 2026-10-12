using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceEventHandlerProjectionTests
{
    [Theory]
    [InlineData("(sen|der, args) => RenderOptions.SetBitmapInterpolationMode((Visual)sender!, BitmapInterpolationMode.None)", "sender", "object?")]
    [InlineData("(sender, ar|gs) => RenderOptions.SetBitmapInterpolationMode((Visual)sender!, BitmapInterpolationMode.None)", "args", "EventArgs")]
    [InlineData("(sender, args) => Console.WriteLine(ar|gs)", "args", "EventArgs")]
    [InlineData("async (sender, args) => { await Task.Yield(); Console.WriteLine(ar|gs); }", "args", "EventArgs")]
    [InlineData("((sender, args) => Console.WriteLine(ar|gs))", "args", "EventArgs")]
    public async Task InitializedLambda_HoverUsesTheRealEventDelegate(string handler, string name, string type)
    {
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var sourceWithCaret = "using System;\r\nusing System.Threading.Tasks;\r\nusing Avalonia;\r\n" +
            "using Avalonia.Controls;\r\nusing Avalonia.Media;\r\n" +
            $"<Image Initialized={{{handler}}} Stretch=\"Uniform\" MaxWidth=\"640\" MaxHeight=\"640\"/>";
        var position = sourceWithCaret.IndexOf('|');
        var text = SourceText.From(sourceWithCaret.Remove(position, 1));
        var context = workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("EventLambda.akbura")), text);
        var document = AkburaSyntacticDocument.Create(context.Document);

        var info = await workspace.LanguageServices.ProjectedCSharp.GetQuickInfoAsync(document, context, position);

        Assert.NotNull(info);
        Assert.Contains(type + " " + name, info.Signature, StringComparison.Ordinal);
        Assert.Equal(name, text.ToString(info.SourceSpan));
    }

    [Theory]
    [InlineData("<Image Initialized={(sender, args) => args.|}/>", "GetType")]
    [InlineData("<Image KeyDown={(sender, args) => args.|}/>", "KeyModifiers")]
    [InlineData("<Image KeyDown={(sender, args) => args.|}/>", "Handled")]
    [InlineData("<Image Initialized={(sender, args) => sender.|}/>", "ToString")]
    public async Task EventLambda_CompletionUsesInferredParameterTypes(string markup, string member)
    {
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var sourceWithCaret = "using Avalonia.Controls;\r\n" + markup;
        var position = sourceWithCaret.IndexOf('|');
        var text = SourceText.From(sourceWithCaret.Remove(position, 1));
        var context = workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("EventLambda.akbura")), text);

        var completion = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            AkburaSyntacticDocument.Create(context.Document), context, position, new(true, false, '\0'));

        Assert.NotNull(completion);
        Assert.Contains(completion.Value.Items, item => item.DisplayText == member);
    }

    [Fact]
    public async Task CustomClrEvent_HoverUsesItsOwnDelegateSignature()
    {
        const string definitions = "namespace Gallery { public sealed class CustomControl : Avalonia.Controls.Control { " +
            "public event System.Action<string>? Changed; } }";
        const string sourceWithCaret = "using System;\r\nusing Gallery;\r\n" +
            "<CustomControl Changed={value => Console.WriteLine(val|ue.Length)}/>";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(definitions);
        var position = sourceWithCaret.IndexOf('|');
        var text = SourceText.From(sourceWithCaret.Remove(position, 1));
        var context = workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("EventLambda.akbura")), text);

        var info = await workspace.LanguageServices.ProjectedCSharp.GetQuickInfoAsync(
            AkburaSyntacticDocument.Create(context.Document), context, position);

        Assert.NotNull(info);
        Assert.Contains("string value", info.Signature, StringComparison.Ordinal);
    }
}
