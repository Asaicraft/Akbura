using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceDictionaryStyleTests
{
    [Theory]
    [InlineData("x.")]
    [InlineData("x.k")]
    [InlineData("x.key")]
    public void DictionaryKeyCompletion_UsesCanonicalDirective(string prefix)
    {
        var fixture = Create("<Border><Border.Resources><Value " + prefix + "|/></Border.Resources></Border>");
        using var workspace = fixture.Workspace;

        var result = GetCompletions(fixture);
        var key = Assert.Single(result.Items, static item => item.DisplayText == "x.key");
        Assert.Equal("x.key=\"\"", key.InsertText);
        Assert.Equal(1, key.CaretOffsetFromEnd);
        Assert.DoesNotContain(result.Items, static item => item.DisplayText == "x.Key");
    }

    [Theory]
    [InlineData("x.key")]
    [InlineData("x.Key")]
    public void DictionaryKeyCompletion_SuppressesBothAliasesAfterAssignment(string directive)
    {
        var fixture = Create("<Border><Border.Resources><Value " + directive + "=\"A\" |/></Border.Resources></Border>");
        using var workspace = fixture.Workspace;

        Assert.DoesNotContain(GetCompletions(fixture).Items,
            static item => item.DisplayText is "x.key" or "x.Key");
    }

    [Fact]
    public void DictionaryKeyCompletion_IsAbsentInListContent()
    {
        var fixture = Create("<ListHost><Value x.|/></ListHost>");
        using var workspace = fixture.Workspace;

        Assert.DoesNotContain(GetCompletions(fixture).Items,
            static item => item.DisplayText is "x.key" or "x.Key");
    }

    [Fact]
    public void DictionaryKeyCompletion_IsAbsentForReadOnlyOnlyContract()
    {
        var fixture = Create("<ReadOnlyValues><Value x.|/></ReadOnlyValues>");
        using var workspace = fixture.Workspace;

        Assert.DoesNotContain(GetCompletions(fixture).Items,
            static item => item.DisplayText is "x.key" or "x.Key");
    }

    [Theory]
    [InlineData("Button.", "Button.Background")]
    [InlineData("Button.Back", "Button.Background")]
    [InlineData("Button.Dir", "Button.DirectCount")]
    [InlineData("Grid.R", "Grid.Row")]
    public void PropertyReferenceCompletion_UsesQualifiedOwner(string prefix, string expected)
    {
        var fixture = Create("<Assignment Target=\"" + prefix + "|\"/>");
        using var workspace = fixture.Workspace;

        var result = GetCompletions(fixture);
        Assert.Contains(result.Items, item => item.DisplayText == expected && item.InsertText == expected);
        Assert.Equal(prefix, fixture.Document.Text.ToString(result.ApplicableSpan));
    }

    [Fact]
    public void PropertyReferenceCompletion_OffersOwnersInUnknownTargetContext()
    {
        var fixture = Create("<Assignment Target=\"But|\"/>");
        using var workspace = fixture.Workspace;

        var owner = Assert.Single(GetCompletions(fixture).Items,
            static item => item.DisplayText == "Button.");
        Assert.True(owner.TriggerCompletionAfterInsert);
    }

    [Fact]
    public void PropertyReferenceCompletion_RespectsDeclaredGenericPropertyContract()
    {
        var fixture = Create("<Assignment TypedTarget=\"Button.|\"/>");
        using var workspace = fixture.Workspace;

        var item = Assert.Single(GetCompletions(fixture).Items);
        Assert.Equal("Button.Alignment", item.DisplayText);
        Assert.Equal("Button.Alignment", item.InsertText);
    }

    [Theory]
    [InlineData("Button", "OnlyButton")]
    [InlineData("Button /template/ Border", "OnlyBorder")]
    [InlineData(":is(Button)", "OnlyButton")]
    public void PropertyReferenceCompletion_UsesResolvedSelectorTarget(string selector, string expected)
    {
        var fixture = Create("<Style Selector=\"" + selector + "\"><Setter Property=\"Only|\"/></Style>");
        using var workspace = fixture.Workspace;

        var item = Assert.Single(GetCompletions(fixture).Items,
            candidate => candidate.DisplayText == expected);
        Assert.Equal(expected, item.InsertText);
    }

    [Theory]
    [InlineData("Button, Border")]
    [InlineData(".untyped")]
    public void PropertyReferenceCompletion_DoesNotInventTargetForAmbiguousOrUnknownSelector(string selector)
    {
        var fixture = Create("<Style Selector=\"" + selector + "\"><Setter Property=\"Only|\"/></Style>");
        using var workspace = fixture.Workspace;

        Assert.DoesNotContain(GetCompletions(fixture).Items,
            static item => item.DisplayText is "OnlyButton" or "OnlyBorder");
    }

    [Fact]
    public void NestedSelectorCompletion_InheritsTargetThroughNestingOperator()
    {
        var fixture = Create("<Style Selector=\"Button\"><Style Selector=\"^:pointerover\"><Setter Property=\"Only|\"/></Style></Style>");
        using var workspace = fixture.Workspace;

        Assert.Contains(GetCompletions(fixture).Items, static item => item.DisplayText == "OnlyButton");
        Assert.DoesNotContain(GetCompletions(fixture).Items, static item => item.DisplayText == "OnlyBorder");
    }

    [Theory]
    [InlineData("\"Button\"")]
    [InlineData("{typeof(Button)}")]
    public void ControlThemeCompletion_UsesResolvedTargetType(string targetType)
    {
        var fixture = Create("<ControlTheme TargetType=" + targetType + "><Setter Property=\"Only|\"/></ControlTheme>");
        using var workspace = fixture.Workspace;

        Assert.Contains(GetCompletions(fixture).Items, static item => item.DisplayText == "OnlyButton");
    }

    [Theory]
    [InlineData("Only", null)]
    [InlineData("Button.Only", "Button.OnlyButton")]
    public void DynamicSelectorCompletion_RequiresOwnerQualifiedReference(string prefix, string? expected)
    {
        var fixture = Create("state Selector selector = new(); <Style Selector={selector}><Setter Property=\"" +
            prefix + "|\"/></Style>");
        using var workspace = fixture.Workspace;
        var items = GetCompletions(fixture).Items;

        if (expected == null)
        {
            Assert.DoesNotContain(items, static item => item.DisplayText is "OnlyButton" or "OnlyBorder");
        }
        else
        {
            Assert.Contains(items, item => item.DisplayText == expected);
        }
    }

    [Fact]
    public void ContextualValueCompletion_UsesEnumWithoutChangingDeclaredObjectType()
    {
        var fixture = Create("<Assignment Target=\"Button.Alignment\" Payload=\"Le|\"/>");
        using var workspace = fixture.Workspace;

        Assert.Contains(GetCompletions(fixture).Items, static item => item.DisplayText == "Left");
        var payloadStart = fixture.Source.IndexOf("Payload", StringComparison.Ordinal);
        var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(fixture.Context, payloadStart);
        Assert.NotNull(info);
        Assert.Contains("object", info!.Signature, StringComparison.Ordinal);
        Assert.Contains(info.Details, static detail => detail.Contains("Alignment", StringComparison.Ordinal));
        Assert.Contains(info.Details, static detail => detail.Contains("AlignmentProperty", StringComparison.Ordinal));
    }

    [Fact]
    public void PropertyReferenceNavigationAndHover_UseExactOwnerAndMemberSpans()
    {
        var directory = Directory.CreateTempSubdirectory("akbura-property-reference-tests-");
        try
        {
            var definitionsPath = Path.Combine(directory.FullName, "DictionaryStyleDefinitions.cs");
            File.WriteAllText(definitionsPath, Definitions.ReplaceLineEndings("\r\n"));
            var fixture = Create("<Assignment Target=\"Button.Alignment\" |/>", definitionsPath);
            using var workspace = fixture.Workspace;
            var ownerStart = fixture.Source.IndexOf("Button.Alignment", StringComparison.Ordinal);
            var propertyStart = ownerStart + "Button.".Length;

            AssertReference(ownerStart, "Button", AkburaQuickInfoKind.Type);
            AssertReference(propertyStart, "Alignment", AkburaQuickInfoKind.Property);

            void AssertReference(int position, string text, AkburaQuickInfoKind kind)
            {
                var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(fixture.Context, position);
                var definition = workspace.LanguageServices.Definition.GetDefinition(fixture.Context, position);
                Assert.NotNull(info);
                Assert.NotNull(definition);
                Assert.Equal(kind, info!.Kind);
                Assert.Equal(text, fixture.Source.Substring(info.SourceSpan.Start, info.SourceSpan.Length));
                Assert.Equal(info.SourceSpan, definition!.SourceSpan);
                Assert.Equal(definitionsPath, definition.TargetFilePath);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void PropertyReferenceClassification_PreservesQuotesAndHasNoOverlaps()
    {
        var fixture = Create("<Assignment Target=\"Button.Alignment\" |/>");
        using var workspace = fixture.Workspace;
        var classifications = workspace.LanguageServices.Classification.GetClassifications(
            fixture.Context, new TextSpan(0, fixture.Source.Length));
        var ownerStart = fixture.Source.IndexOf("Button.Alignment", StringComparison.Ordinal);
        var literalStart = ownerStart - 1;
        var literalEnd = ownerStart + "Button.Alignment".Length;

        Assert.Contains(classifications, item => item.Span == new TextSpan(ownerStart, 6) &&
            item.Kind == AkburaClassificationKind.ClassName);
        Assert.Contains(classifications, item => item.Span == new TextSpan(ownerStart + 7, 9) &&
            item.Kind == AkburaClassificationKind.FieldName);
        Assert.Contains(classifications, item => item.Span == new TextSpan(literalStart, 1) &&
            item.Kind == AkburaClassificationKind.String);
        Assert.Contains(classifications, item => item.Span == new TextSpan(literalEnd, 1) &&
            item.Kind == AkburaClassificationKind.String);
        var literalSpans = classifications.Where(item => item.Span.Start >= literalStart &&
            item.Span.End <= literalEnd + 1).OrderBy(static item => item.Span.Start).ToArray();
        for (var i = 1; i < literalSpans.Length; i++)
        {
            Assert.True(literalSpans[i - 1].Span.End <= literalSpans[i].Span.Start);
        }
    }

    [Fact]
    public void SelectorTypes_HoverNavigateAndClassifyThroughSharedParsedSpans()
    {
        var fixture = Create("<Style Selector=\"Button /template/ Border\" |/>");
        using var workspace = fixture.Workspace;
        var selectorStart = fixture.Source.IndexOf("Button /template/ Border", StringComparison.Ordinal);
        var classifications = workspace.LanguageServices.Classification.GetClassifications(
            fixture.Context, new TextSpan(0, fixture.Source.Length));

        AssertType(selectorStart, "Button");
        AssertType(selectorStart + "Button /template/ ".Length, "Border");
        Assert.Contains(classifications, item => item.Span == new TextSpan(selectorStart - 1, 1) &&
            item.Kind == AkburaClassificationKind.String);
        var closingQuote = selectorStart + "Button /template/ Border".Length;
        Assert.Contains(classifications, item => item.Span == new TextSpan(closingQuote, 1) &&
            item.Kind == AkburaClassificationKind.String);

        void AssertType(int position, string typeName)
        {
            var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(fixture.Context, position);
            var definition = workspace.LanguageServices.Definition.GetDefinition(fixture.Context, position);
            Assert.NotNull(info);
            Assert.NotNull(definition);
            Assert.Equal(AkburaQuickInfoKind.Type, info!.Kind);
            Assert.Equal("class " + typeName, info.Signature);
            Assert.Equal(typeName, fixture.Source.Substring(info.SourceSpan.Start, info.SourceSpan.Length));
            Assert.Equal(info.SourceSpan, definition!.SourceSpan);
            Assert.Contains(classifications, item => item.Span == info.SourceSpan &&
                item.Kind == AkburaClassificationKind.ClassName);
        }
    }

    [Fact]
    public void SelectorEditUndoAndRedo_RefreshDescendantCompletionContext()
    {
        var fixture = Create("<Style Selector=\"Button\"><Setter Property=\"Only|\"/></Style>");
        using var workspace = fixture.Workspace;
        var original = fixture.Source;
        var edited = original.Replace("Selector=\"Button\"", "Selector=\"Border\"", StringComparison.Ordinal);
        AssertContext(fixture.Context, original, "OnlyButton", "OnlyBorder");
        AssertContext(Change(edited), edited, "OnlyBorder", "OnlyButton");
        AssertContext(Change(original), original, "OnlyButton", "OnlyBorder");
        AssertContext(Change(edited), edited, "OnlyBorder", "OnlyButton");

        AkburaDocumentContext Change(string source) => workspace.OpenOrChangeDocumentContext(
            fixture.Context.Document.Uri, SourceText.From(source));

        void AssertContext(AkburaDocumentContext context, string source, string expected, string absent)
        {
            var document = AkburaSyntacticDocument.Parse(SourceText.From(source), fixture.Document.FilePath);
            var position = source.IndexOf("Only", StringComparison.Ordinal) + "Only".Length;
            var result = workspace.LanguageServices.Completion.GetCompletions(document, context, position);
            Assert.Contains(result.Items, item => item.DisplayText == expected);
            Assert.DoesNotContain(result.Items, item => item.DisplayText == absent);
        }
    }

    [Theory]
    [InlineData("Button.Al|")]
    [InlineData("Button.Al|ignment")]
    public void PropertyReferenceCommit_ReplacesOnlyLiteralContents(string referenceWithCaret)
    {
        var fixture = Create("<Assignment Target=\"" + referenceWithCaret + "\" Payload=\"Left\"/>");
        using var workspace = fixture.Workspace;
        var item = Assert.Single(GetCompletions(fixture).Items,
            static candidate => candidate.DisplayText == "Button.Alignment");
        var change = workspace.LanguageServices.Completion.GetCompletionChange(
            fixture.Document, fixture.Context, fixture.Position, item);
        var changed = fixture.Document.Text.WithChanges(change.Changes).ToString();

        Assert.Contains("Target=\"Button.Alignment\" Payload=\"Left\"", changed, StringComparison.Ordinal);
        Assert.Equal(fixture.Source.Length - (referenceWithCaret.Length - 1) + "Button.Alignment".Length,
            changed.Length);
    }

    [Theory]
    [InlineData("<Assignment Target=\"Button.Back|")]
    [InlineData("<Style Selector=\"Button\"><Setter Property=\"Only|")]
    public void IncompletePropertyReferenceLiteral_HasSafeApplicableSpan(string source)
    {
        var fixture = Create(source);
        using var workspace = fixture.Workspace;

        var result = GetCompletions(fixture);
        Assert.True(result.ApplicableSpan.Start >= 0);
        Assert.True(result.ApplicableSpan.End <= fixture.Source.Length);
        Assert.Contains(result.Items, static item => item.Kind == AkburaCompletionKind.Property);
    }

    [Fact]
    public void DictionaryKeyProjection_UsesExpectedIntTypeAndMapsHostExpression()
    {
        var fixture = Create("state count = 0; <IntDictionary><Value x.Key={count + 1|}/></IntDictionary>");
        using var workspace = fixture.Workspace;
        Assert.True(fixture.Document.TryGetEmbeddedCSharpContext(fixture.Position, out var embedded));
        Assert.True(AkburaCSharpProjectionFactory.TryCreate(fixture.Document, fixture.Context,
            embedded, out var projection));
        var method = Assert.Single(projection.Root.DescendantNodes().OfType<CSharp.MethodDeclarationSyntax>(),
            static method => method.Identifier.ValueText == "__akbura_probe");
        Assert.Equal("int", method.ReturnType.ToString());
        Assert.Equal("count + 1", fixture.Document.Text.ToString(projection.HostSpan));
        Assert.True(projection.TryMapToHost(projection.ProjectedSpan, out var hostSpan));
        Assert.Equal(projection.HostSpan, hostSpan);
        var countStart = fixture.Source.LastIndexOf("count", StringComparison.Ordinal);
        var definition = workspace.LanguageServices.Definition.GetDefinition(fixture.Context, countStart);
        Assert.NotNull(definition);
        Assert.Equal(fixture.Document.FilePath, definition!.TargetFilePath);
        var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(fixture.Context, countStart);
        Assert.NotNull(info);
        Assert.Equal("count", fixture.Source.Substring(info!.SourceSpan.Start, info.SourceSpan.Length));
    }

    [Fact]
    public void DictionaryKeyUnknownIdentifier_ReportsCSharpDiagnosticAtHostIdentifier()
    {
        var fixture = Create("<IntDictionary><Value x.Key={missingKey|}/></IntDictionary>");
        using var workspace = fixture.Workspace;
        var diagnostics = workspace.LanguageServices.Diagnostics.GetDiagnostics(
            fixture.Context, new TextSpan(0, fixture.Source.Length));
        var identifierStart = fixture.Source.IndexOf("missingKey", StringComparison.Ordinal);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("missingKey", StringComparison.Ordinal) &&
            diagnostic.Span.Contains(identifierStart));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Message.Contains("'x'", StringComparison.Ordinal));
    }

    [Fact]
    public void DictionaryKeyTargetTypedLambda_PreservesInferredParameterHover()
    {
        var fixture = Create("<DelegateDictionary><Value x.Key={value => value| + 1}/></DelegateDictionary>");
        using var workspace = fixture.Workspace;
        var parameterReference = fixture.Source.LastIndexOf("value", StringComparison.Ordinal);

        var info = workspace.LanguageServices.QuickInfo.GetQuickInfo(fixture.Context, parameterReference);
        Assert.NotNull(info);
        Assert.Contains("int", info!.Signature, StringComparison.Ordinal);
        Assert.Equal("value", fixture.Source.Substring(info.SourceSpan.Start, info.SourceSpan.Length));
        Assert.True(fixture.Document.TryGetEmbeddedCSharpContext(fixture.Position, out var embedded));
        Assert.True(AkburaCSharpProjectionFactory.TryCreate(fixture.Document, fixture.Context,
            embedded, out var projection));
        var method = Assert.Single(projection.Root.DescendantNodes().OfType<CSharp.MethodDeclarationSyntax>(),
            static method => method.Identifier.ValueText == "__akbura_probe");
        Assert.Equal("global::System.Func<int, int>", method.ReturnType.ToString());
    }

    [Fact]
    public void CSharpDependsOnEdit_RefreshesContextualCompletionWithoutChangingMarkup()
    {
        var fixture = Create("<Assignment Target=\"Button.Alignment\" Alternate=\"Button.OnlyButton\" Payload=\"Le|\"/>");
        using var workspace = fixture.Workspace;
        Assert.Contains(GetCompletions(fixture).Items, static item => item.DisplayText == "Left");
        var originalContext = fixture.Context.Project.Context;
        var changedDefinitions = Definitions.Replace("DependsOn(nameof(Target))",
            "DependsOn(nameof(Alternate))", StringComparison.Ordinal);
        var changedCompilation = originalContext.CSharpCompilation.RemoveAllSyntaxTrees().AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(changedDefinitions, path: Path.GetFullPath("DictionaryStyleDefinitions.cs")));
        workspace.AddOrUpdateProject(new ProjectContext(originalContext.RoslynProjectId,
            originalContext.ProjectFilePath, originalContext.ProjectDirectory, originalContext.RootNamespace,
            changedCompilation, originalContext.ProjectReferences));
        var changedContext = workspace.OpenOrChangeDocumentContext(fixture.Context.Document.Uri,
            fixture.Document.Text);

        var changed = workspace.LanguageServices.Completion.GetCompletions(fixture.Document,
            changedContext, fixture.Position);
        Assert.DoesNotContain(changed.Items, static item => item.DisplayText == "Left");
        Assert.Contains(GetCompletions(fixture).Items, static item => item.DisplayText == "Left");
    }

    private static AkburaCompletionResult GetCompletions(Fixture fixture) =>
        fixture.Workspace.LanguageServices.Completion.GetCompletions(
            fixture.Document, fixture.Context, fixture.Position);

    private static Fixture Create(string markupWithCaret, string? definitionsPath = null)
    {
        const string imports = "using Avalonia.Controls; using Avalonia.Styling; using TestMarkup; ";
        var position = imports.Length + markupWithCaret.IndexOf('|');
        Assert.True(position >= imports.Length);
        var source = imports + markupWithCaret.Replace("|", string.Empty, StringComparison.Ordinal);
        var platform = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator) ?? [];
        var compilation = CSharpCompilation.Create("WorkspaceDictionaryStyleTests",
            [CSharpSyntaxTree.ParseText(Definitions,
                path: definitionsPath ?? Path.GetFullPath("DictionaryStyleDefinitions.cs"))],
            platform.Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error);
        var workspace = new AkburaWorkspace(new ProjectContext(ProjectId.CreateNewId(),
            string.Empty, Environment.CurrentDirectory, string.Empty, compilation,
            ImmutableArray<ProjectReference>.Empty));
        var path = Path.GetFullPath("Inline.akbura");
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(new Uri(path), text);
        return new Fixture(workspace, context, AkburaSyntacticDocument.Parse(text, path), source, position);
    }

    private sealed record Fixture(AkburaWorkspace Workspace, AkburaDocumentContext Context,
        AkburaSyntacticDocument Document, string Source, int Position);

    private const string Definitions = """
        using System;
        using System.Collections.Generic;
        namespace Avalonia
        {
            public class AvaloniaProperty { }
            public class StyledProperty<T> : AvaloniaProperty { }
            public class AttachedProperty<T> : AvaloniaProperty { }
            public class DirectProperty<TOwner, T> : AvaloniaProperty { }
        }
        namespace Avalonia.Metadata
        {
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class ContentAttribute : Attribute { }
            [AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
            public sealed class DependsOnAttribute(string property) : Attribute { }
        }
        namespace Avalonia.Data
        {
            [AttributeUsage(AttributeTargets.Property)]
            public sealed class AssignBindingAttribute : Attribute { }
        }
        namespace TestMarkup
        {
            public enum Alignment { Left, Center, Right }
            public sealed class Value { }
            public sealed class Assignment
            {
                public Avalonia.AvaloniaProperty Target { get; set; }
                public Avalonia.StyledProperty<Alignment> TypedTarget { get; set; }
                public Avalonia.AvaloniaProperty Alternate { get; set; }
                [Avalonia.Metadata.DependsOn(nameof(Target))]
                [Avalonia.Data.AssignBinding]
                public object Payload { get; set; }
            }
            public sealed class ListHost
            {
                [Avalonia.Metadata.Content]
                public List<Value> Children { get; } = new();
            }
            public sealed class IntDictionary : Dictionary<int, Value> { }
            public sealed class DelegateDictionary : Dictionary<Func<int, int>, Value> { }
            public sealed class ReadOnlyValues : IReadOnlyDictionary<int, Value>
            {
                public Value this[int key] => throw new KeyNotFoundException();
                public IEnumerable<int> Keys => [];
                public IEnumerable<Value> Values => [];
                public int Count => 0;
                public bool ContainsKey(int key) => false;
                public bool TryGetValue(int key, out Value value) { value = null; return false; }
                public IEnumerator<KeyValuePair<int, Value>> GetEnumerator() =>
                    System.Linq.Enumerable.Empty<KeyValuePair<int, Value>>().GetEnumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
        }
        namespace Avalonia.Controls
        {
            public class Control
            {
                public static readonly Avalonia.StyledProperty<object> BackgroundProperty = new();
                public object Background { get; set; }
            }
            public class Button : Control
            {
                public static readonly Avalonia.StyledProperty<TestMarkup.Alignment> AlignmentProperty = new();
                public static readonly Avalonia.StyledProperty<string> OnlyButtonProperty = new();
                public static readonly Avalonia.DirectProperty<Button, int> DirectCountProperty = new();
                public int DirectCount { get; set; }
                public TestMarkup.Alignment Alignment { get; set; }
                public string OnlyButton { get; set; }
            }
            public class Border : Control
            {
                public Dictionary<object, object> Resources { get; } = new();
                public static readonly Avalonia.StyledProperty<string> OnlyBorderProperty = new();
                public string OnlyBorder { get; set; }
            }
            public class Grid : Control
            {
                public static readonly Avalonia.AttachedProperty<int> RowProperty = new();
                public static int GetRow(Control control) => 0;
                public static void SetRow(Control control, int value) { }
            }
        }
        namespace Avalonia.Styling
        {
            public sealed class Selector { }
            public sealed class ControlTheme
            {
                public Type TargetType { get; set; }
                public void Add(Setter setter) { }
            }
            public sealed class Style
            {
                public Selector Selector { get; set; }
                public void Add(Setter setter) { }
                public void Add(Style style) { }
            }
            public sealed class Setter
            {
                public Avalonia.AvaloniaProperty Property { get; set; }
                [Avalonia.Metadata.DependsOn(nameof(Property))]
                public object Value { get; set; }
            }
        }
        namespace Akbura { public class AkburaControl : Avalonia.Controls.Control { } }
        public partial class Inline : Akbura.AkburaControl { }
        """;
}
