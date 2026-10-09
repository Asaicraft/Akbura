using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceAkcssDeclarationClassificationTests(ITestOutputHelper output)
{
    [Fact]
    public void BuiltInStyles_ColorsAllSelectorTypesWithoutBindingAssignmentBodies()
    {
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var src = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Parent!.Parent!.Parent!.Parent!;
        var text = SourceText.From(File.ReadAllText(Path.Combine(src.FullName, "Akbura", "Styles.akcss")));
        var context = Open(workspace, text);
        var timer = Stopwatch.StartNew();
        var classifications = workspace.LanguageServices.Classification.GetDeclarationClassifications(
            context, new TextSpan(0, text.Length));
        output.WriteLine($"Built-in Styles declaration classification: {timer.Elapsed.TotalMilliseconds:F1} ms");

        var root = context.Document.SyntaxTree.GetRootSyntax();
        var utilities = root.DescendantNodes().OfType<AkcssUtilityDeclarationSyntax>().ToArray();
        Assert.True(utilities.Length > 1400);
        var classifiedTypes = classifications.Where(item => item.Kind == AkburaClassificationKind.ClassName)
            .Select(item => item.Span).ToHashSet();
        foreach (var utility in utilities)
        {
            var type = Assert.IsType<CSharpTypeSyntax>(utility.Selector.TargetType);
            var csharpType = type.ToCSharp();
            var identifier = csharpType.GetLastToken();
            var span = new TextSpan(type.Tokens.Span.Start + identifier.SpanStart - csharpType.FullSpan.Start,
                identifier.Span.Length);
            Assert.Contains(span, classifiedTypes);
        }

        AssertBodiesAreUnbound(context);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HeaderRange_DoesNotBindTheUtilityBody(bool declarationPass)
    {
        // Even the full classifier must not bind a body outside the requested range.
        const string source = "@using Avalonia.Controls;\r\n@utilities {\r\n" +
            "    Border.bg-teal-300 { Background: missingIdentifier; }\r\n}";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var text = SourceText.From(source);
        var context = Open(workspace, text);
        var span = new TextSpan(source.IndexOf("Border", StringComparison.Ordinal), "Border".Length);
        var classifications = declarationPass
            ? workspace.LanguageServices.Classification.GetDeclarationClassifications(context, span)
            : workspace.LanguageServices.Classification.GetClassifications(context, span);

        Assert.Contains(new AkburaClassifiedSpan(span, AkburaClassificationKind.ClassName), classifications);
        Assert.All(classifications, item => Assert.True(item.Span.OverlapsWith(span)));
        AssertBodiesAreUnbound(context);
    }

    [Fact]
    public void Declarations_PreserveParametersAndSyntaxWithoutBindingInvalidBodies()
    {
        const string source = "@using Avalonia.Controls;\r\n@utilities {\r\n" +
            "    Border.bg-(string color)-(int shade) { Background: missingIdentifier; }\r\n" +
            "}\r\nBorder.card { Background: missingIdentifier; }";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var text = SourceText.From(source);
        var context = Open(workspace, text);
        var classifications = workspace.LanguageServices.Classification.GetDeclarationClassifications(
            context, new TextSpan(0, text.Length));

        AssertToken(text, classifications, "Border", AkburaClassificationKind.ClassName);
        AssertToken(text, classifications, "color", AkburaClassificationKind.ParameterName);
        AssertToken(text, classifications, "shade", AkburaClassificationKind.ParameterName);
        AssertToken(text, classifications, "string", AkburaClassificationKind.Keyword);
        AssertToken(text, classifications, "card", AkburaClassificationKind.Utility);
        AssertBodiesAreUnbound(context);
    }

    [Fact]
    public void Declarations_UnknownTargetIsNotColoredAsAClass()
    {
        const string source = "@utilities {\r\n" +
            "    MissingControl.bg-red-300 { Background: 1; }\r\n" +
            "    MissingControl.bg-red-400 { Background: 2; }\r\n}";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var text = SourceText.From(source);
        var context = Open(workspace, text);
        var classifications = workspace.LanguageServices.Classification.GetDeclarationClassifications(
            context, new TextSpan(0, text.Length));

        Assert.DoesNotContain(classifications, item => item.Kind == AkburaClassificationKind.ClassName);
        AssertBodiesAreUnbound(context);
    }

    [Fact]
    public void FullClassification_StillColorsAssignmentExpressionsAfterDeclarationPass()
    {
        const string source = "@using Avalonia.Controls;\r\n@utilities {\r\n" +
            "    Border.bg-teal-300 { Background: Amx.DynamicResource<IBrush>(\"--color-teal-300\"); }\r\n}";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var text = SourceText.From(source);
        var context = Open(workspace, text);
        var span = new TextSpan(0, text.Length);
        var declarations = workspace.LanguageServices.Classification.GetDeclarationClassifications(context, span);
        AssertBodiesAreUnbound(context);
        AssertToken(text, declarations, "Border", AkburaClassificationKind.ClassName);

        var classifications = workspace.LanguageServices.Classification.GetClassifications(context, span);
        AssertToken(text, classifications, "Border", AkburaClassificationKind.ClassName);
        AssertToken(text, classifications, "Amx", AkburaClassificationKind.ClassName);
        AssertToken(text, classifications, "DynamicResource", AkburaClassificationKind.MethodName);
        AssertToken(text, classifications, "IBrush", AkburaClassificationKind.InterfaceName);
        AssertToken(text, classifications, "Background", AkburaClassificationKind.PropertyName);
        Assert.Empty(workspace.LanguageServices.Diagnostics.GetDiagnostics(context, span));
    }

    [Fact]
    public void FullClassification_ColorsNestedGenericMethodTypeArguments()
    {
        const string source = "@using Avalonia.Controls;\r\n@using System.Collections.Generic;\r\n@utilities {\r\n" +
            "    Border.resources { Tag: Amx.DynamicResource<Dictionary<string, IBrush>>(\"colors\"); }\r\n}";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var text = SourceText.From(source);
        var context = Open(workspace, text);
        var span = new TextSpan(0, text.Length);
        var classifications = workspace.LanguageServices.Classification.GetClassifications(context, span);

        AssertToken(text, classifications, "Dictionary", AkburaClassificationKind.ClassName);
        AssertToken(text, classifications, "string", AkburaClassificationKind.Keyword);
        AssertToken(text, classifications, "IBrush", AkburaClassificationKind.InterfaceName);
        Assert.Equal(classifications.Length, classifications.Select(item => item.Span).Distinct().Count());
        Assert.Empty(workspace.LanguageServices.Diagnostics.GetDiagnostics(context, span));
    }

    [Fact]
    public void Declarations_DoNotReuseTypesFromAnotherInlineImportScope()
    {
        const string types = "namespace First { public class Target { } }\r\n" +
            "namespace Second { public struct Target { } }";
        const string source = "@akcss {\r\n@using First;\r\nTarget.one { }\r\nTarget.two { }\r\n}\r\n" +
            "@akcss {\r\n@using Second;\r\nTarget.three { }\r\nTarget.four { }\r\n}\r\n" +
            "<Avalonia.Controls.Border/>";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(types);
        var text = SourceText.From(source);
        var context = Open(workspace, text, "DeclarationScopes.akbura");
        var classifications = workspace.LanguageServices.Classification.GetDeclarationClassifications(
            context, new TextSpan(0, text.Length));

        var targets = classifications.Where(item => text.ToString(item.Span) == "Target").ToArray();
        Assert.Equal(4, targets.Length);
        Assert.Equal(new[] { AkburaClassificationKind.ClassName, AkburaClassificationKind.ClassName,
            AkburaClassificationKind.StructName, AkburaClassificationKind.StructName }, targets.Select(item => item.Kind));
    }

    [Fact]
    public void Declarations_ChangedImportsDoNotReuseThePreviousModelType()
    {
        const string types = "namespace First { public class Target { } }\r\n" +
            "namespace Second { public struct Target { } }";
        var text = SourceText.From("@using First;\r\n@utilities { Target.one { } Target.two { } }");
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(types);
        var context = Open(workspace, text);
        var initial = workspace.LanguageServices.Classification.GetDeclarationClassifications(
            context, new TextSpan(0, text.Length));
        AssertToken(text, initial, "Target", AkburaClassificationKind.ClassName);

        text = text.WithChanges(new TextChange(new TextSpan(text.ToString().IndexOf("First", StringComparison.Ordinal), 5), "Second"));
        context = Open(workspace, text);
        var updated = workspace.LanguageServices.Classification.GetDeclarationClassifications(
            context, new TextSpan(0, text.Length));
        AssertToken(text, updated, "Target", AkburaClassificationKind.StructName);
        Assert.DoesNotContain(updated, item => item.Kind == AkburaClassificationKind.ClassName &&
            text.ToString(item.Span) == "Target");
    }

    [Fact]
    public void Declarations_ObserveCancellationWithoutBindingBodies()
    {
        var text = SourceText.From("@using Avalonia.Controls;\r\n@utilities { Border.one { Background: 1; } }");
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var context = Open(workspace, text);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => workspace.LanguageServices.Classification
            .GetDeclarationClassifications(context, new TextSpan(0, text.Length), cancellation.Token));
        AssertBodiesAreUnbound(context);
    }

    private static AkburaDocumentContext Open(AkburaWorkspace workspace, SourceText text, string name = "Declarations.akcss")
        => workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath(name)), text);

    private static void AssertBodiesAreUnbound(AkburaDocumentContext context)
    {
        var model = context.Project.Compilation.GetSemanticModel(context.Document.SyntaxTree);
        foreach (var assignment in context.Document.SyntaxTree.GetRootSyntax().DescendantNodes().OfType<AkcssAssignmentSyntax>())
        {
            Assert.False(model.TryGetCachedBoundNode(assignment, out _));
            Assert.False(model.TryGetCachedOperation(assignment, out _));
        }
    }

    private static void AssertToken(SourceText text, ImmutableArray<AkburaClassifiedSpan> classifications, string token, AkburaClassificationKind kind)
        => Assert.Contains(classifications, item => item.Kind == kind && text.ToString(item.Span) == token);
}
