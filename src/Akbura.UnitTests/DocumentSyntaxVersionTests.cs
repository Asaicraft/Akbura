using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Linq;
using System.Threading;

namespace Akbura.UnitTests;

public sealed class DocumentSyntaxVersionTests
{
    [Theory]
    [InlineData(false, "using Avalonia.Controls;\r\n<Border />")]
    [InlineData(true, "@using Avalonia.Controls;\r\nBorder.shared { Width: 10; }")]
    public void TrailingTrivia_PreservesGenerationShapeAfterIncrementalAndFreshParse(bool akcss, string source)
    {
        var originalTree = Parse(akcss, source);
        var updatedText = originalTree.Text.WithChanges(new TextChange(new TextSpan(source.Length, 0), "\r\n \t\r\n"));
        var updatedTree = WithChangedText(originalTree, updatedText);
        var freshTree = Parse(akcss, updatedText.ToString());
        var original = DocumentSyntaxVersion.Create(originalTree);
        var updated = DocumentSyntaxVersion.Create(updatedTree);
        var fresh = DocumentSyntaxVersion.Create(freshTree);

        Assert.NotSame(originalTree, updatedTree);
        Assert.True(original.CanReuse);
        Assert.True(updated.CanReuse);
        Assert.Equal(source.Length, updated.MeaningfulLength);
        Assert.True(original.HasSameGenerationShape(updated));
        Assert.True(updated.HasSameGenerationShape(fresh));
        Assert.True(original.HasSameSurface(updated));
        Assert.Equal(original.DependencyNames.ToArray(), updated.DependencyNames.ToArray());
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(false, "\r\n \t\r\n")]
    [InlineData(true, "")]
    [InlineData(true, "\r\n \t\r\n")]
    public void EmptyOrWhitespaceOnlyDocument_HasReusableEmptyShape(bool akcss, string source)
    {
        var version = DocumentSyntaxVersion.Create(Parse(akcss, source));
        var empty = DocumentSyntaxVersion.Create(Parse(akcss, string.Empty));

        Assert.True(version.CanReuse);
        Assert.Equal(0, version.MeaningfulLength);
        Assert.Equal(string.Empty, version.GenerationShape);
        Assert.Empty(version.DependencyNames);
        Assert.True(version.HasSameGenerationShape(empty));
        Assert.True(version.HasSameSurface(empty));
    }

    [Theory]
    [InlineData(false, "<Border />")]
    [InlineData(true, "Border.shared { Width: 10; }")]
    public void CompletedCommentAfterLastToken_IsNotDiscardedAsEofWhitespace(bool akcss, string source)
    {
        var original = DocumentSyntaxVersion.Create(Parse(akcss, source));
        var withComment = source + "\r\n/* retained comment */\r\n \t";
        var updated = DocumentSyntaxVersion.Create(Parse(akcss, withComment));

        Assert.True(updated.CanReuse);
        Assert.Equal(withComment, updated.GenerationShape);
        Assert.Equal(withComment.Length, updated.MeaningfulLength);
        Assert.False(original.HasSameGenerationShape(updated));
    }

    [Fact]
    public void LeadingBlankLine_ChangesActualSourceMappingAndGenerationShape()
    {
        const string source = "using Avalonia.Controls;\r\n<Border Width={10d} />";
        var original = ComponentSyntaxTree.ParseText(source, "Views/Example.akbura");
        var updated = original.WithChangedText(original.Text.WithChanges(new TextChange(new TextSpan(0, 0), "\r\n")));
        var originalExpression = Assert.Single(original.GetRoot().DescendantNodes().OfType<CSharpExpressionSyntax>());
        var updatedExpression = Assert.Single(updated.GetRoot().DescendantNodes().OfType<CSharpExpressionSyntax>());

        Assert.True(new ComponentGenerationSourceMap(original).TryGetLineDirective(originalExpression, out var oldSpan, out _));
        Assert.True(new ComponentGenerationSourceMap(updated).TryGetLineDirective(updatedExpression, out var newSpan, out _));
        Assert.Equal(oldSpan.Start.Line + 1, newSpan.Start.Line);
        Assert.Equal(oldSpan.Start.Character, newSpan.Start.Character);
        Assert.False(DocumentSyntaxVersion.Create(original).HasSameGenerationShape(DocumentSyntaxVersion.Create(updated)));
    }

    [Theory]
    [InlineData("<TextBlock>hello </TextBlock>", "<TextBlock>hello  </TextBlock>")]
    [InlineData("<TextBlock Text=\"hello \" />", "<TextBlock Text=\"hello  \" />")]
    [InlineData("<Border Width={1d /* first */} />", "<Border Width={1d /* other */} />")]
    [InlineData("<Border Width={1d} />", "<Border Width={2d} />")]
    [InlineData("<Border />\r\n// first", "<Border />\r\n// other")]
    public void MeaningfulTextEdits_DoNotCompareEqual(string original, string updated)
    {
        var oldVersion = DocumentSyntaxVersion.Create(Parse(false, original));
        var newVersion = DocumentSyntaxVersion.Create(Parse(false, updated));

        Assert.False(oldVersion.HasSameGenerationShape(newVersion));
        Assert.NotEqual(oldVersion.BodyShape, newVersion.BodyShape);
    }

    [Theory]
    [InlineData("<TextBlock>unfinished ")]
    [InlineData("state string value = \"unfinished")]
    [InlineData("param void Value;")]
    [InlineData("}")]
    public void RecoverySyntax_DisablesReuseAndRetainsEntireText(string source)
    {
        var tree = Parse(false, source);
        var version = DocumentSyntaxVersion.Create(tree);
        var freshVersion = DocumentSyntaxVersion.Create(Parse(false, source));

        Assert.False(version.CanReuse);
        Assert.True(version.RequiresConservativeDependencies);
        Assert.Equal(source, version.GenerationShape);
        Assert.Equal(source.Length, version.MeaningfulLength);
        Assert.False(version.HasSameGenerationShape(freshVersion));
        Assert.False(version.HasSameSurface(freshVersion));
    }

    [Fact]
    public void VoidCommandReturnType_AllowsReuseWithoutExemptingOtherVoidTypes()
    {
        const string source = "command void Save(int value);";
        var tree = Parse(false, source);
        var command = Assert.Single(tree.GetRootSyntax().DescendantNodes().OfType<CommandDeclarationSyntax>());
        Assert.Contains(command.ReturnType.ToCSharp().GetDiagnostics(), diagnostic => diagnostic.Id == "CS1547");
        var original = DocumentSyntaxVersion.Create(tree);
        var trailing = DocumentSyntaxVersion.Create(Parse(false, source + "\r\n \t"));

        Assert.True(original.CanReuse);
        Assert.True(trailing.CanReuse);
        Assert.Equal(source, trailing.GenerationShape);
        Assert.True(original.HasSameGenerationShape(trailing));
        Assert.True(original.HasSameSurface(trailing));

        var invalid = DocumentSyntaxVersion.Create(Parse(false, "param void Value;\r\n \t"));
        Assert.False(invalid.CanReuse);
        Assert.True(invalid.RequiresConservativeDependencies);
        Assert.Equal("param void Value;\r\n \t", invalid.GenerationShape);
    }

    [Fact]
    public void VoidCommandWithInvalidRawParameters_DoesNotReuseRecoverySyntax()
    {
        const string source = "command void Save(int value = );\r\n \t";
        var tree = Parse(false, source);
        var command = Assert.Single(tree.GetRootSyntax().DescendantNodes().OfType<CommandDeclarationSyntax>());
        var parameters = command.Parameters.GetRawCSharpParameterList();
        Assert.NotNull(parameters);
        Assert.True(parameters.ContainsDiagnostics);
        var version = DocumentSyntaxVersion.Create(tree);

        Assert.False(version.CanReuse);
        Assert.True(version.RequiresConservativeDependencies);
        Assert.Equal(source, version.GenerationShape);
        Assert.Equal(source.Length, version.MeaningfulLength);
        Assert.False(version.HasSameGenerationShape(DocumentSyntaxVersion.Create(Parse(false, source))));
    }

    [Fact]
    public void InvalidRawExpression_DisablesReuseAndRetainsTrailingWhitespace()
    {
        const string source = "state int value = 1 + ;\r\n \t";
        var tree = Parse(false, source);
        var expression = Assert.Single(tree.GetRootSyntax().DescendantNodes().OfType<CSharpExpressionSyntax>());
        var rawExpression = expression.GetRawCSharpExpression();
        Assert.NotNull(rawExpression);
        Assert.True(rawExpression.ContainsDiagnostics);
        var version = DocumentSyntaxVersion.Create(tree);

        Assert.False(version.CanReuse);
        Assert.True(version.RequiresConservativeDependencies);
        Assert.Equal(source, version.GenerationShape);
        Assert.Equal(source.Length, version.MeaningfulLength);
    }

    [Fact]
    public void MissingCommandSemicolon_IsNotTreatedAsHarmlessEof()
    {
        const string source = "command void Save()\r\n \t";
        var tree = Parse(false, source);
        var command = Assert.Single(tree.GetRootSyntax().DescendantNodes().OfType<CommandDeclarationSyntax>());
        Assert.True(command.Semicolon.IsMissing);
        var version = DocumentSyntaxVersion.Create(tree);

        Assert.False(version.CanReuse);
        Assert.True(version.RequiresConservativeDependencies);
        Assert.Equal(source, version.GenerationShape);
        Assert.False(version.HasSameSurface(DocumentSyntaxVersion.Create(Parse(false, source))));
    }

    [Theory]
    [InlineData(false, "param double Width;\r\n<Border Width=\"10\" />", "param double Width;\r\n<Border Width=\"20\" />")]
    [InlineData(true, "Border.shared { Width: 10; }", "Border.shared { Width: 20; }")]
    public void BodyOnlyEdit_PreservesDeclarationSurface(bool akcss, string original, string updated)
    {
        var oldVersion = DocumentSyntaxVersion.Create(Parse(akcss, original));
        var newVersion = DocumentSyntaxVersion.Create(Parse(akcss, updated));

        Assert.True(oldVersion.HasSameSurface(newVersion));
        Assert.False(oldVersion.HasSameGenerationShape(newVersion));
    }

    [Theory]
    [InlineData(false, "param double Value;", "param string Value;")]
    [InlineData(false, "param Value = 1;", "param Value = \"one\";")]
    [InlineData(false, "command void Save(int value);", "command void Save(string value);")]
    [InlineData(false, "using First;", "using Other;")]
    [InlineData(true, "Border.first { Width: 10; }", "Border.other { Width: 10; }")]
    [InlineData(true, "@utilities { .width-(double value) { Width: value; } }", "@utilities { .width-(int value) { Width: value; } }")]
    [InlineData(true, "Border.shared { @intercept First; }", "Border.shared { @intercept Other; }")]
    public void ContractEdit_ChangesDeclarationSurface(bool akcss, string original, string updated)
    {
        var oldVersion = DocumentSyntaxVersion.Create(Parse(akcss, original));
        var newVersion = DocumentSyntaxVersion.Create(Parse(akcss, updated));

        Assert.True(oldVersion.CanReuse);
        Assert.True(newVersion.CanReuse);
        Assert.False(oldVersion.HasSameSurface(newVersion));
    }

    [Fact]
    public void ComponentReferences_IncludeTagsAliasesExpressionsTypesAndBindingPaths()
    {
        const string source =
            "using Controls = Demo.Controls;\r\n" +
            "using static Demo.Hooks;\r\n" +
            "using Demo.Styles.Shared.akcss;\r\n" +
            "state object value = new Demo.Controls.F\\u006fo();\r\n" +
            "<Controls::Card Width={Demo.Controls.Meter.WidthProperty.GetHashCode()} Text=\"$parent[Panel].Value\" />";

        var version = DocumentSyntaxVersion.Create(Parse(false, source));

        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.Component, "Controls::Card"), version.DependencyNames);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.Alias, "Demo.Controls", "Controls"), version.DependencyNames);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.StaticUsing, "Demo.Hooks"), version.DependencyNames);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.AkcssModule, "Demo.Styles.Shared.akcss"), version.DependencyNames);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.Identifier, "Foo"), version.DependencyNames);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.Identifier, "Meter"), version.DependencyNames);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.Identifier, "Panel"), version.DependencyNames);
    }

    [Theory]
    [InlineData(false, "param Demo.C\\u0061rd Value;", "Card")]
    [InlineData(false, "state object value = new Demo.C\\u0061rd();", "Card")]
    [InlineData(false, "command void Save(Demo.C\\u0061rd value);", "Card")]
    [InlineData(false, "<TextBlock Text={Demo.C\\u0061rd.Caption} />", "Card")]
    [InlineData(true, "Border.shared { Width: Demo.M\\u0065ter.Value; }", "Meter")]
    public void EscapedIdentifiersInRawCSharp_AreDecodedAcrossSyntaxContexts(bool akcss, string source, string identifier)
    {
        var version = DocumentSyntaxVersion.Create(Parse(akcss, source));
        var trailing = DocumentSyntaxVersion.Create(Parse(akcss, source + "\r\n \t"));
        var dependency = new DocumentDependencyName(DocumentDependencyKind.Identifier, identifier);

        // The source itself contains only the escaped spelling, so a scan of
        // ordinary letter sequences cannot discover the referenced type name.
        Assert.DoesNotContain(identifier, source, StringComparison.Ordinal);
        Assert.True(version.CanReuse);
        Assert.Contains(dependency, version.DependencyNames);
        Assert.Single(version.DependencyNames, name => name == dependency);
        Assert.True(version.HasSameGenerationShape(trailing));
        Assert.Equal(version.DependencyNames.ToArray(), trailing.DependencyNames.ToArray());
    }

    [Fact]
    public void AkcssReferences_IncludeModuleImportApplyTargetAndIntercept()
    {
        const string source =
            "@using Demo.Styles.Shared.akcss;\r\n" +
            "(Demo.Controls.Card).local { @apply shared; @intercept Demo.Styles.CardClass; }";

        var version = DocumentSyntaxVersion.Create(Parse(true, source));

        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.AkcssModule, "Demo.Styles.Shared.akcss"), version.DependencyNames);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.AkcssApply, "shared"), version.DependencyNames);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.Identifier, "Card"), version.DependencyNames);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.Identifier, "CardClass"), version.DependencyNames);
    }

    [Fact]
    public void GlobalUsing_InOrdinaryComponentFileIsTracked()
    {
        var version = DocumentSyntaxVersion.Create(Parse(false, "global using Avalonia.Controls;\r\n<Border />"));

        Assert.True(version.HasGlobalUsings);
        Assert.Contains(new DocumentDependencyName(DocumentDependencyKind.Namespace, "Avalonia.Controls"), version.DependencyNames);
    }

    [Fact]
    public void FileAndLogicalIdentity_ArePartOfBothComparisons()
    {
        const string source = "Border.shared { Width: 10; }";
        var original = DocumentSyntaxVersion.Create(AkcssSyntaxTree.ParseText(source, "Styles/Shared.akcss", "Demo.Shared.akcss"));
        var renamed = DocumentSyntaxVersion.Create(AkcssSyntaxTree.ParseText(source, "Styles/Renamed.akcss", "Demo.Shared.akcss"));
        var changedLogicalName = DocumentSyntaxVersion.Create(AkcssSyntaxTree.ParseText(source, "Styles/Shared.akcss", "Other.Shared.akcss"));

        Assert.False(original.HasSameGenerationShape(renamed));
        Assert.False(original.HasSameSurface(renamed));
        Assert.False(original.HasSameGenerationShape(changedLogicalName));
        Assert.False(original.HasSameSurface(changedLogicalName));
    }

    [Fact]
    public void CanceledExtraction_DoesNotPublishPartialVersion()
    {
        var tree = Parse(false, "<Border />");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => DocumentSyntaxVersion.Create(tree, cancellation.Token));
        Assert.True(DocumentSyntaxVersion.Create(tree).CanReuse);
    }

    private static AkburaSyntaxTree Parse(bool akcss, string source)
    {
        return akcss
            ? AkcssSyntaxTree.ParseText(source, "Styles/Example.akcss", "Demo.Styles.Example.akcss")
            : ComponentSyntaxTree.ParseText(source, "Views/Example.akbura");
    }

    private static AkburaSyntaxTree WithChangedText(AkburaSyntaxTree tree, SourceText text)
    {
        return tree switch
        {
            ComponentSyntaxTree component => component.WithChangedText(text),
            AkcssSyntaxTree akcss => akcss.WithChangedText(text),
            _ => throw new InvalidOperationException(),
        };
    }
}
