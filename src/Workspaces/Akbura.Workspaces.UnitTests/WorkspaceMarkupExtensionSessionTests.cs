using System;
using Akbura.Workspaces;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceMarkupExtensionSessionTests
{
    [Theory]
    [InlineData("<NavIcon Geometries=${|} />", "")]
    [InlineData("<NavIcon Geometries=${St|} />", "St")]
    [InlineData("<NavIcon Geometries=${| />", "")]
    [InlineData("<NavIcon ${| />", "")]
    [InlineData("<NavIcon ${St|}:p-5 />", "St")]
    [InlineData("<NavIcon Geometries=${StaticResource \"Icon.Home\"}  ${| />", "")]
    [InlineData("<NavIcon Geometries=${StaticResource \"Icon.Home\"}  ${St|} />", "St")]
    [InlineData("<Border p-${|} />", "")]
    [InlineData("<Border Tag=${Outer Value=${St|}} />", "St")]
    [InlineData("<Border Tag=${  St|} />", "St")]
    public void OpenerSelectsExtensionTypes_NotAttributeNames(string marked, string prefix)
    {
        var (document, position, opening) = Parse(marked);
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            document, position, opening, out var nameSpan));
        Assert.Equal(prefix, document.Text.ToString(nameSpan));
        Assert.Equal(AkburaCompletionContextKind.MarkupExtensionType,
            document.GetCompletionContext(position).Kind);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", true)]
    public void AttributeToExtensionTransition_UsesNewNameAnchor(string newline, bool pairedBrace)
    {
        var prefix = "state int count = 0;" + newline + newline +
            "<NavIcon Geometries=${StaticResource \"Icon.Home\"}  ";
        var text = SourceText.From(prefix + " />");
        var document = AkburaSyntacticDocument.Parse(text, "MainView.akbura");
        var position = prefix.Length;
        var attributeContext = document.GetCompletionContext(position);
        Assert.Equal(AkburaCompletionContextKind.AttributeName, attributeContext.Kind);

        foreach (var character in "${")
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
        }
        var opening = position - 1;
        if (pairedBrace)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position, 0), "}"));
            document = document.WithText(text);
        }

        AssertTransition(document, position, opening, prefix.Length);
        foreach (var character in "St")
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
            AssertTransition(document, position, opening, prefix.Length);
        }

        // Simulate the name-only replacement issued after a fresh request.
        // The surrounding ${, optional }, existing resource and tag must survive.
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            document, position, opening, out var span));
        var committed = text.WithChanges(new TextChange(span, "StaticResource"));
        Assert.Equal(prefix + "${StaticResource" + (pairedBrace ? "}" : "") + " />", committed.ToString());
    }

    [Theory]
    [InlineData("<TextBlock Text=\"literal ${|}\" />")]
    [InlineData("state string text = \"${|}\";")]
    [InlineData("// ${|")]
    [InlineData("<Border Tag=${Binding Path|} />")]
    [InlineData("<Border Tag=${StaticResource \"Icon.|Home\"} />")]
    public void LiteralAndArgumentContexts_DoNotSwitchCatalogs(string marked)
    {
        var (document, position, opening) = Parse(marked);
        Assert.False(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            document, position, opening, out _));
    }

    [Theory]
    [InlineData(
        "<Border Tag=${Binding Customer.Na|me} />",
        AkburaCompletionContextKind.BindingPath,
        "Na",
        "Name",
        "Customer.")]
    [InlineData(
        "<Border Tag=${Binding Path=Customer.Add|ress} />",
        AkburaCompletionContextKind.BindingPath,
        "Add",
        "Address",
        "Customer.")]
    [InlineData(
        "<Border Tag=${Binding Customer.Name, Mo|} />",
        AkburaCompletionContextKind.MarkupExtensionArgumentName,
        "Mo",
        "Mo",
        null)]
    [InlineData(
        "<Border Tag=${Binding Mode=Tw|oWay} />",
        AkburaCompletionContextKind.MarkupExtensionArgumentValue,
        "Tw",
        "TwoWay",
        null)]
    [InlineData(
        "<Border Tag=${ReflectionBinding Customer.Na|me} />",
        AkburaCompletionContextKind.BindingPath,
        "Na",
        "Name",
        "Customer.")]
    [InlineData(
        "<Border Tag=${CompiledBinding Path=Customer.Na|me} />",
        AkburaCompletionContextKind.BindingPath,
        "Na",
        "Name",
        "Customer.")]
    [InlineData(
        "<Border Tag=${Custom 0, Mo|} />",
        AkburaCompletionContextKind.MarkupExtensionArgumentName,
        "Mo",
        "Mo",
        null)]
    [InlineData(
        "<Border Tag=${Custom 0, Mode=Pr|imary} />",
        AkburaCompletionContextKind.MarkupExtensionArgumentValue,
        "Pr",
        "Primary",
        null)]
    [InlineData(
        "<Border Tag=${Custom Text=\"C:\\\\\", Mo|} />",
        AkburaCompletionContextKind.MarkupExtensionArgumentName,
        "Mo",
        "Mo",
        null)]
    public void ArgumentContext_DistinguishesBindingPathNamesAndValues(string marked, AkburaCompletionContextKind expectedKind, string expectedPrefix, string expectedApplicableText, string? expectedCompletedPath)
    {
        var (document, position, _) = Parse(marked);

        var context = document.GetCompletionContext(position);

        Assert.Equal(expectedKind, context.Kind);
        Assert.Equal(expectedPrefix, context.Prefix);
        Assert.Equal(
            expectedApplicableText,
            document.Text.ToString(context.ApplicableSpan));
        Assert.Equal(expectedCompletedPath, context.CompletedPath);
        var extensionNameStart = marked.IndexOf(
            "${",
            StringComparison.Ordinal) + 2;
        var extensionNameEnd = marked.IndexOf(
            ' ',
            extensionNameStart);
        Assert.Equal(
            marked[extensionNameStart..extensionNameEnd],
            context.MarkupExtensionName);
    }

    [Theory]
    [InlineData("<StackPanel><TextBlock Text=${Binding |} /></StackPanel>", AkburaCompletionContextKind.BindingPath)]
    [InlineData("<StackPanel>\r\n<TextBlock Grid.Row=\"0\"\r\n Text=${Binding |   }\r\n Width=\"20\" />\r\n</StackPanel>", AkburaCompletionContextKind.BindingPath)]
    [InlineData("<StackPanel><TextBlock Text=${Binding Path=|} /></StackPanel>", AkburaCompletionContextKind.BindingPath)]
    [InlineData("<StackPanel><TextBlock Text=${Binding Path=| Name} /></StackPanel>", AkburaCompletionContextKind.BindingPath)]
    [InlineData("<StackPanel><TextBlock Text=${Binding |", AkburaCompletionContextKind.BindingPath)]
    [InlineData("<StackPanel><TextBlock Text=${Binding |</StackPanel>", AkburaCompletionContextKind.BindingPath)]
    [InlineData("<StackPanel><TextBlock Text=${ReflectionBinding |} /></StackPanel>", AkburaCompletionContextKind.BindingPath)]
    [InlineData("<StackPanel><TextBlock Text=${CompiledBinding |} /></StackPanel>", AkburaCompletionContextKind.BindingPath)]
    [InlineData("<StackPanel><TextBlock Text=${Binding Name, Converter=${StaticResource |}} /></StackPanel>", AkburaCompletionContextKind.MarkupExtensionArgumentValue)]
    public void ExtensionOwnedPositions_DoNotBecomeMarkupStatements(string marked, AkburaCompletionContextKind expectedKind)
    {
        var (document, position, _) = Parse(marked);
        using var workspace = new AkburaWorkspace();

        var context = document.GetCompletionContext(position);
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);

        Assert.Equal(expectedKind, context.Kind);
        Assert.DoesNotContain(result.Items, static item => item.DisplayText is "$if" or "$foreach" or "$else" or "$else if");
    }

    [Theory]
    [InlineData("<Border Command=${Binding |   } />", "")]
    [InlineData("<Border Command=${Binding Path=|   } />", "")]
    [InlineData("<Border Command=${Binding Path=| Name} />", "")]
    [InlineData("<Border Command=${Binding Path=|Name} />", "Name")]
    [InlineData("<Border Command=${Binding Customer.Name, |} />", "")]
    [InlineData("<Border Command=${Binding Mode=|} />", "")]
    public void ArgumentWhitespaceAndSuffix_UseCursorSafeReplacementSpan(string marked, string expectedApplicableText)
    {
        var (document, position, _) = Parse(marked);

        var context = document.GetCompletionContext(position);

        Assert.True(context.Kind is AkburaCompletionContextKind.BindingPath or AkburaCompletionContextKind.MarkupExtensionArgumentName or AkburaCompletionContextKind.MarkupExtensionArgumentValue);
        Assert.Equal(string.Empty, context.Prefix);
        Assert.Equal(expectedApplicableText, document.Text.ToString(context.ApplicableSpan));
        Assert.True(context.ApplicableSpan.Length != 0 || context.ApplicableSpan.Start == position);
    }

    [Theory]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    public void IncrementalWhitespaceAndRecovery_KeepBindingOwnership(string newline, bool keepAutomaticCloseBrace)
    {
        var initialSource = "<StackPanel>" + newline + "<TextBlock Text=${Binding} />" + newline + "</StackPanel>";
        var initialText = SourceText.From(initialSource);
        var incremental = AkburaSyntacticDocument.Parse(initialText, "MainView.akbura");
        var bindingEnd = initialSource.IndexOf("Binding", StringComparison.Ordinal) + "Binding".Length;
        var changedText = initialText.WithChanges(new TextChange(new TextSpan(bindingEnd, 0), "   "));
        if (!keepAutomaticCloseBrace)
        {
            var closeBrace = changedText.ToString().IndexOf('}', bindingEnd);
            changedText = changedText.WithChanges(new TextChange(new TextSpan(closeBrace, 1), string.Empty));
        }

        incremental = incremental.WithText(changedText);
        var full = AkburaSyntacticDocument.Parse(changedText, "MainView.akbura");
        var position = bindingEnd + 1;

        Assert.Equal(AkburaCompletionContextKind.BindingPath, full.GetCompletionContext(position).Kind);
        Assert.Equal(AkburaCompletionContextKind.BindingPath, incremental.GetCompletionContext(position).Kind);
        Assert.Equal(full.GetCompletionContext(position).ApplicableSpan, incremental.GetCompletionContext(position).ApplicableSpan);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void IncrementalTypingFromEmptyExtension_NeverFallsBackToStatements(string newline)
    {
        var source = "<StackPanel>" + newline + "<TextBlock Text=${} />" + newline + "</StackPanel>";
        var text = SourceText.From(source);
        var incremental = AkburaSyntacticDocument.Parse(text, "MainView.akbura");
        var position = source.IndexOf("${", StringComparison.Ordinal) + 2;

        foreach (var character in "Binding ")
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            incremental = incremental.WithText(text);
            var full = AkburaSyntacticDocument.Parse(text, "MainView.akbura");
            var incrementalContext = incremental.GetCompletionContext(position);
            var fullContext = full.GetCompletionContext(position);

            Assert.DoesNotContain(incrementalContext.Kind, new[] { AkburaCompletionContextKind.MarkupStatement, AkburaCompletionContextKind.MarkupConditionalContinuation });
            Assert.Equal(fullContext.Kind, incrementalContext.Kind);
            Assert.Equal(fullContext.ApplicableSpan, incrementalContext.ApplicableSpan);
        }

        Assert.Equal(AkburaCompletionContextKind.BindingPath, incremental.GetCompletionContext(position).Kind);
    }

    [Theory]
    [InlineData("<TextBlock Text=\"${Binding |}\" />", AkburaCompletionContextKind.AttributeValue)]
    [InlineData("state string text = \"${Binding |}\";", AkburaCompletionContextKind.None)]
    [InlineData("// ${Binding |", AkburaCompletionContextKind.None)]
    public void MarkupLikeTextOutsideExtensions_IsNotBindingOrStatement(string marked, AkburaCompletionContextKind expectedKind)
    {
        var (document, position, _) = Parse(marked);

        var context = document.GetCompletionContext(position);

        Assert.Equal(expectedKind, context.Kind);
    }

    [Theory]
    [InlineData("<Button Content={ /* ${Bi| */ ViewModel.Name } />")]
    [InlineData("<Button Content={ // ${Bi|\r\n ViewModel.Name } />")]
    [InlineData("state string text = \"<Button ${Bi|\";")]
    [InlineData("// <Button ${Bi|")]
    [InlineData("/* <Button ${Bi| */")]
    [InlineData("state string text = \"> <Fake ${Bi|\";")]
    [InlineData("<TextBlock Text=\"prefix <Fake ${Bi|\" />")]
    [InlineData("<TextBlock Text='prefix <Fake ${Bi|' />")]
    public void RawMarkupLikeTextOutsideMarkup_DoesNotSelectExtensionType(string marked)
    {
        var (document, position, _) = Parse(marked);

        Assert.NotEqual(AkburaCompletionContextKind.MarkupExtensionType, document.GetCompletionContext(position).Kind);
    }

    [Theory]
    [InlineData("<Button Content={ /* | */ ViewModel.Name } />")]
    [InlineData("<Button Content={ // |\n ViewModel.Name } />")]
    [InlineData("<Button Content={ // |\r\n ViewModel.Name } />")]
    public void IncrementalMarkupLikeTextInCSharpComments_DoesNotSelectExtensionType(string marked)
    {
        var position = marked.IndexOf('|');
        var text = SourceText.From(marked.Remove(position, 1));
        var incremental = AkburaSyntacticDocument.Parse(text, "MainView.akbura");

        foreach (var character in "${Bi")
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            incremental = incremental.WithText(text);
            var full = AkburaSyntacticDocument.Parse(text, "MainView.akbura");
            var incrementalContext = incremental.GetCompletionContext(position);
            var fullContext = full.GetCompletionContext(position);

            Assert.NotEqual(AkburaCompletionContextKind.MarkupExtensionType, incrementalContext.Kind);
            Assert.Equal(fullContext.Kind, incrementalContext.Kind);
        }
    }

    [Theory]
    [InlineData("<StackPanel><TextBlock |/></StackPanel>")]
    [InlineData("<StackPanel><TextBlock |></TextBlock></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Foo=\"x\" |></TextBlock></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text=${Binding}| /></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text=${Binding} |/></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text={Foo}| /></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text={Foo} |/></StackPanel>")]
    [InlineData("<StackPanel><TextBlock Text=\"foo\" |/></StackPanel>")]
    public void StartTagOwnedPositions_DoNotBecomeMarkupStatements(string marked)
    {
        var position = marked.IndexOf('|');
        var document = AkburaSyntacticDocument.Parse(SourceText.From(marked.Remove(position, 1)), "MainView.akbura");
        using var workspace = new AkburaWorkspace();

        var context = document.GetCompletionContext(position);
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);

        Assert.Equal(AkburaCompletionContextKind.AttributeName, context.Kind);
        Assert.DoesNotContain(result.Items, static item => item.DisplayText is "$if" or "$foreach" or "$else" or "$else if");
    }

    [Fact]
    public void NestedExtensionOwnership_SelectsInnermostArgument()
    {
        var (document, position, _) = Parse("<Border Tag=${Binding Name, Converter=${StaticResource |}} />");

        var context = document.GetCompletionContext(position);

        Assert.Equal(AkburaCompletionContextKind.MarkupExtensionArgumentValue, context.Kind);
        Assert.Equal("StaticResource", context.MarkupExtensionName);
        Assert.Equal(0, context.MarkupExtensionArgumentIndex);
        Assert.Equal(new TextSpan(position, 0), context.ApplicableSpan);
    }

    [Theory]
    [InlineData("<StackPanel><TextBlock Text=|")]
    [InlineData("<StackPanel><TextBlock Text=|   ")]
    public void IncompleteAttributeValues_DoNotBecomeMarkupStatements(string marked)
    {
        var position = marked.IndexOf('|');
        Assert.True(position >= 0);
        var document = AkburaSyntacticDocument.Parse(SourceText.From(marked.Remove(position, 1)), "MainView.akbura");
        using var workspace = new AkburaWorkspace();

        var context = document.GetCompletionContext(position);
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);

        Assert.DoesNotContain(context.Kind, new[] { AkburaCompletionContextKind.MarkupStatement, AkburaCompletionContextKind.MarkupConditionalContinuation });
        Assert.DoesNotContain(result.Items, static item => item.DisplayText is "$if" or "$foreach" or "$else" or "$else if");
    }

    [Fact]
    public void EmbeddedCSharpAttributeValue_RemainsOwnedByCSharp()
    {
        const string marked = "<Button Content={ViewModel.|}/>";
        var position = marked.IndexOf('|');
        var document = AkburaSyntacticDocument.Parse(SourceText.From(marked.Remove(position, 1)), "MainView.akbura");

        Assert.True(document.TryGetCSharpCompletionContext(position, out var csharpContext));
        Assert.InRange(position, csharpContext.HostSpan.Start, csharpContext.HostSpan.End);
        Assert.DoesNotContain(document.GetCompletionContext(position).Kind, new[] { AkburaCompletionContextKind.MarkupStatement, AkburaCompletionContextKind.MarkupConditionalContinuation });
    }

    [Theory]
    [InlineData("<StackPanel><TextBlock /> | <TextBlock /></StackPanel>")]
    [InlineData("<StackPanel>$if (true) { <TextBlock />| }</StackPanel>")]
    [InlineData("<StackPanel>$foreach (var item in items) { <TextBlock />| }</StackPanel>")]
    public void MarkupContentPositions_StillOfferStatements(string marked)
    {
        var position = marked.IndexOf('|');
        Assert.True(position >= 0);
        var document = AkburaSyntacticDocument.Parse(SourceText.From(marked.Remove(position, 1)), "MainView.akbura");
        using var workspace = new AkburaWorkspace();

        var context = document.GetCompletionContext(position);
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);

        Assert.Equal(AkburaCompletionContextKind.MarkupStatement, context.Kind);
        Assert.Contains(result.Items, static item => item.DisplayText == "$if");
        Assert.Contains(result.Items, static item => item.DisplayText == "$foreach");
    }

    [Fact]
    public void ARequestForAnOuterOpener_CannotSwitchANestedSession()
    {
        const string marked = "<Border Tag=${Outer Value=${St|}} />";
        var (document, position, _) = Parse(marked);
        var outerOpening = marked.IndexOf("${", StringComparison.Ordinal) + 1;
        Assert.False(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            document, position, outerOpening, out _));
    }

    [Theory]
    [InlineData("<Border Tag=${| />")]
    [InlineData("<Border ${| />")]
    [InlineData("<Border Tag=${|} />")]
    public void EmptyNameBeforeTagBoundary_CanBeCommittedWithoutRemovingSuffix(string marked)
    {
        var (document, position, _) = Parse(marked);
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
            document.Text, new TextSpan(position, 0), out var span));
        Assert.Equal(new TextSpan(position, 0), span);
        Assert.Equal(marked.Replace("|", "StaticResource", StringComparison.Ordinal),
            document.Text.WithChanges(new TextChange(span, "StaticResource")).ToString());
    }

    private static void AssertTransition(AkburaSyntacticDocument incremental, int position, int opening, int previousStart)
    {
        var full = AkburaSyntacticDocument.Parse(incremental.Text, "MainView.akbura");
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            incremental, position, opening, out var incrementalSpan));
        Assert.True(AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
            full, position, opening, out var fullSpan));
        Assert.Equal(fullSpan, incrementalSpan);
        Assert.False(AkburaMarkupExtensionCompletionFacts.IsTypeNameSession(
            TextSpan.FromBounds(previousStart, position), incrementalSpan));
        Assert.True(AkburaMarkupExtensionCompletionFacts.IsTypeNameSession(incrementalSpan, incrementalSpan));

        if (position < incremental.Text.Length && incremental.Text[position] == '}')
        {
            Assert.True(AkburaMarkupExtensionCompletionFacts.IsTypeNameSession(
                TextSpan.FromBounds(incrementalSpan.Start, position + 1), incrementalSpan));
        }
    }

    private static (AkburaSyntacticDocument Document, int Position, int Opening) Parse(string marked)
    {
        var position = marked.IndexOf('|');
        Assert.True(position >= 0);
        var source = marked.Remove(position, 1);
        var opening = source.LastIndexOf("${", position - 1, StringComparison.Ordinal) + 1;
        Assert.True(opening > 0);
        return (AkburaSyntacticDocument.Parse(SourceText.From(source), "MainView.akbura"), position, opening);
    }
}
