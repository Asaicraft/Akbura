using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;

namespace Akbura.UnitTests;

public sealed class ComponentRenderCaptureTests
{
    [Fact]
    public void ConditionalLocalScope_CapturesOnlyReferencedRenderLocalsWithOriginalNullableType()
    {
        using var fixture = CreateFixture(
            """
            using Avalonia.Controls;
            var prefix = Text;
            string? optional = Expanded ? Text : null;
            var unused = Text.Length;
            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <Border>
                        $if (optional is { } current) { <TextBlock Text={prefix + current} /> }
                    </Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """);

        var captures = GetCaptures(fixture.Writer.Plan);
        Assert.Equal(["prefix", "optional"], captures.Select(capture => capture.Name));
        Assert.Equal(SpecialType.System_String, captures[0].Type.SpecialType);
        Assert.Equal(NullableAnnotation.Annotated, captures[1].Type.NullableAnnotation);
        var scopeId = Assert.Single(fixture.Writer.Plan.Templates).ScopeId;
        Assert.All(captures, capture => Assert.Equal(scopeId, Assert.Single(capture.ScopeIds)));

        new ComponentRenderCaptureWriter(fixture.CodeWriter).WriteReads(fixture.Writer.Plan, scopeId);
        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains("var prefix = this.__akburaRenderState.GetRenderCapture<" +
            GetStringTypeName(captures[0]) + ">", output);
        Assert.Contains("var optional = this.__akburaRenderState.GetRenderCapture<string?>", output);
        Assert.DoesNotContain("var unused", output);
    }

    [Theory]
    [InlineData("<ItemsControl><ItemsControl.ItemTemplate><TextBlock Text={prefix} /></ItemsControl.ItemTemplate></ItemsControl>")]
    [InlineData("<Border>$if (Expanded) { <TextBlock Text={prefix} /> }</Border>")]
    public void NoConditionalLocalCallback_DoesNotPublishCaptureCells(string markup)
    {
        using var fixture = CreateFixture("using Avalonia.Controls; var prefix = Text; " + markup);

        Assert.Empty(GetCaptures(fixture.Writer.Plan));
        fixture.Writer.WriteLifecycleMembers();
        var output = fixture.CodeWriter.GetText().ToString();
        Assert.DoesNotContain("SetRenderCapture", output);
        Assert.DoesNotContain("GetRenderCapture", output);
    }

    [Fact]
    public void IndependentConditionalTemplates_ReadOnlyTheirOwnLocals()
    {
        using var fixture = CreateFixture(
            """
            using Avalonia.Controls;
            var first = Text;
            var second = Text;
            <StackPanel>
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <Border>$if (Expanded) { <TextBlock Text={first} /> }</Border>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <ItemsControl>
                    <ItemsControl.ItemTemplate>
                        <Border>$if (Expanded) { <TextBlock Text={second} /> }</Border>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            """);
        var captures = GetCaptures(fixture.Writer.Plan);
        Assert.Equal(2, captures.Length);
        Assert.NotEqual(Assert.Single(captures[0].ScopeIds), Assert.Single(captures[1].ScopeIds));

        new ComponentRenderCaptureWriter(fixture.CodeWriter).WriteReads(fixture.Writer.Plan,
            captures[0].ScopeIds[0]);

        var output = fixture.CodeWriter.GetText().ToString();
        Assert.Contains("var first =", output);
        Assert.DoesNotContain("var second =", output);
    }

    [Fact]
    public void NestedConditionalTemplate_InnerCaptureIsNotDeclaredByOuterCallback()
    {
        using var fixture = CreateFixture(
            """
            using Avalonia.Controls;
            var prefix = Text;
            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <ItemsControl>
                        <ItemsControl.ItemTemplate>
                            <Border>$if (Expanded) { <TextBlock Text={prefix} /> }</Border>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """);
        var capture = Assert.Single(GetCaptures(fixture.Writer.Plan));
        var innerScope = Assert.Single(capture.ScopeIds);
        var outerScope = Assert.Single(fixture.Writer.Plan.Templates,
            template => template.ScopeId != innerScope).ScopeId;

        new ComponentRenderCaptureWriter(fixture.CodeWriter).WriteReads(fixture.Writer.Plan, outerScope);
        Assert.Equal(string.Empty, fixture.CodeWriter.GetText().ToString());
        new ComponentRenderCaptureWriter(fixture.CodeWriter).WriteReads(fixture.Writer.Plan, innerScope);
        Assert.Contains("var prefix =", fixture.CodeWriter.GetText().ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publication_FollowsAllParentRenderStatementsIncludingLaterMutation(bool structural)
    {
        using var fixture = CreateFixture(
            """
            using Avalonia.Controls;
            var prefix = Text;
            prefix += " final";
            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <Border>$if (Expanded) { <TextBlock Text={prefix} /> }</Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """, structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        fixture.Writer.WriteLifecycleMembers();
        var output = fixture.CodeWriter.GetText().ToString();
        var update = output[output.IndexOf(
            "protected override global::Avalonia.Controls.Control Update()", StringComparison.Ordinal)..];
        var declaration = update.IndexOf("var prefix = Text;", StringComparison.Ordinal);
        var mutation = update.IndexOf("prefix += \" final\";", StringComparison.Ordinal);
        var capture = Assert.Single(GetCaptures(fixture.Writer.Plan));
        var publishMethod = "SetRenderCapture<" + GetStringTypeName(capture) + ">";
        var publication = update.IndexOf(publishMethod, StringComparison.Ordinal);

        Assert.True(declaration >= 0, update);
        Assert.True(mutation > declaration, update);
        Assert.True(publication > mutation, update);
        Assert.Equal(1, Count(update, publishMethod));
    }

    [Fact]
    public void CaptureKey_RemainsStableWhenEarlierRenderStatementsShiftSourcePositions()
    {
        const string component =
            """
            var prefix = Text;
            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <Border>$if (Expanded) { <TextBlock Text={prefix} /> }</Border>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """;
        using var original = CreateFixture("using Avalonia.Controls; " + component);
        using var edited = CreateFixture("using Avalonia.Controls; var unrelated = 123; " + component);

        var before = Assert.Single(GetCaptures(original.Writer.Plan));
        var after = Assert.Single(GetCaptures(edited.Writer.Plan));
        Assert.Equal("render/local/prefix", before.Key);
        Assert.Equal(before.Key, after.Key);
        Assert.NotEqual(original.Writer.Plan.RenderStatements[0].Syntax.Span.Start,
            edited.Writer.Plan.RenderStatements[1].Syntax.Span.Start);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var position = 0; (position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0;
            position += value.Length)
        {
            count++;
        }

        return count;
    }

    private static string GetStringTypeName(in ComponentRenderCapturePlan capture)
    {
        Assert.Equal(SpecialType.System_String, capture.Type.SpecialType);
        return capture.Type.NullableAnnotation == NullableAnnotation.Annotated ? "string?" : "string";
    }

    private static ComponentRenderCapturePlan[] GetCaptures(in ComponentPlan plan)
    {
        var captures = new List<ComponentRenderCapturePlan>();
        foreach (var statement in plan.RenderStatements)
        {
            if (!statement.RenderCaptures.IsDefaultOrEmpty)
            {
                captures.AddRange(statement.RenderCaptures);
            }
        }

        return captures.ToArray();
    }

    private static CaptureFixture CreateFixture(string source,
        ComponentGenerationMode mode = ComponentGenerationMode.ReleaseDirect)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source,
            """
            namespace Demo;
            public partial class PlannerView
            {
                public string Text => "initial";
                public bool Expanded => true;
            }
            """);
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var codeWriter = new CodeWriter("\r\n");
        return new CaptureFixture(codeWriter,
            new ComponentWriter(codeWriter, component, fixture.SemanticModel, "PlannerView.akbura",
                new Dictionary<AkburaSyntax, string>(), mode));
    }

    private sealed class CaptureFixture(CodeWriter codeWriter, ComponentWriter writer) : IDisposable
    {
        public CodeWriter CodeWriter { get; } = codeWriter;

        public ComponentWriter Writer { get; } = writer;

        public void Dispose()
        {
            Writer.Dispose();
            CodeWriter.Dispose();
        }
    }
}
