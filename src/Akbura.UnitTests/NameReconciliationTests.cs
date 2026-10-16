using Akbura.HotReload;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia.Controls;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class NameReconciliationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralNameAssignment_IsGuardedOnlyByNewNodeInitialization(bool conditional)
    {
        var markup = "<TextBlock x.Name=\"title\" Text=\"Dashboard\" />";
        var source = "using Avalonia.Controls; " + (conditional ? "param bool Enabled = true; <StackPanel>$if (Enabled) { " + markup + " }</StackPanel>" : markup);
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source);
        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(symbol, fixture.SemanticModel, "PlannerView.akbura", new Dictionary<AkburaSyntax, string>(),
            CancellationToken.None, conditional ? ComponentGenerationMode.ReleaseConditional : ComponentGenerationMode.DebugStructural);
        var root = CSharpSyntaxTree.ParseText(generated).GetRoot();
        var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Where(assignment => assignment.Left is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Name" }).ToArray();
        Assert.NotEmpty(assignments);
        foreach (var assignment in assignments)
        {
            var method = assignment.Ancestors().OfType<MethodDeclarationSyntax>().First();
            if (method.Identifier.ValueText == "FirstUpdate")
            {
                continue;
            }
            var guard = Assert.IsType<InvocationExpressionSyntax>(assignment.Ancestors().OfType<IfStatementSyntax>().First().Condition);
            Assert.Equal("IsNew", Assert.IsType<MemberAccessExpressionSyntax>(guard.Expression).Name.Identifier.ValueText);
        }
    }

    [Fact]
    public void ReleaseDirect_KeepsOrdinaryOrderedNameInitialization()
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; <TextBlock x.Name=\"title\" Text=\"Dashboard\" />");
        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var generated = ComponentDocumentWriter.Generate(symbol, fixture.SemanticModel, "PlannerView.akbura", new Dictionary<AkburaSyntax, string>(),
            CancellationToken.None, ComponentGenerationMode.ReleaseDirect).ToString();
        Assert.Contains("title.Name = \"title\";", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsNew(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void UnchangedRegistrations_ReuseCompletedScopeAcrossChangedSyntaxAndAbort()
    {
        using var state = new AkburaRenderState();
        Begin(state, "initial", "title");
        var title = state.GetRequired<TextBlock>(1);
        var scope = CompleteScope(state, title, "title");
        state.CompleteRevision();
        Begin(state, "changed-text", "title");
        Assert.True(state.ShouldApplyInitialValues(1));
        Assert.False(state.IsNew(1));
        Assert.Same(title, state.GetRequired<TextBlock>(1));
        Assert.Same(scope, CompleteScope(state, title, "title"));
        state.AbortRevision();
        Assert.Same(scope, state.GetNameScopeForNode(title));
        Begin(state, "retry", "title");
        Assert.Same(scope, CompleteScope(state, title, "title"));
        state.CompleteRevision();
        Assert.Same(scope, state.GetNameScopeForNode(title));
    }

    [Theory]
    [InlineData("old", "new")]
    [InlineData("old", null)]
    [InlineData(null, "new")]
    public void NameTransitions_ReplaceOnlyTheIncompatibleSubtreeAndRollbackPendingScope(string? before, string? after)
    {
        using var state = new AkburaRenderState();
        Begin(state, "initial", before);
        var parent = state.GetRequired<StackPanel>(0);
        var title = state.GetRequired<TextBlock>(1);
        var sibling = state.GetRequired<TextBlock>(2);
        var originalScope = CompleteScope(state, title, before);
        state.ReconcileCollection(0, "children", parent.Children, [title, sibling]);
        state.CompleteRevision();
        Begin(state, "changed-name", after);
        Assert.Same(parent, state.GetRequired<StackPanel>(0));
        Assert.Same(sibling, state.GetRequired<TextBlock>(2));
        var replacement = state.GetRequired<TextBlock>(1);
        Assert.NotSame(title, replacement);
        Assert.True(state.IsNew(1));
        var pendingScope = CompleteScope(state, replacement, after);
        Assert.NotSame(originalScope, pendingScope);
        state.ReconcileCollection(0, "children", parent.Children, [replacement, sibling]);
        state.PrepareRevisionCompletion();
        state.AbortRevision(new InvalidOperationException("later initialization failed"));
        Assert.Same(title, state.GetRequired<TextBlock>(1));
        Assert.Same(title, parent.Children[0]);
        Assert.Same(originalScope, state.GetNameScopeForNode(title));
        Begin(state, "retry", after);
        replacement = state.GetRequired<TextBlock>(1);
        var currentScope = CompleteScope(state, replacement, after);
        state.ReconcileCollection(0, "children", parent.Children, [replacement, sibling]);
        state.CompleteRevision();
        Assert.Same(replacement, parent.Children[0]);
        Assert.Same(currentScope, state.GetNameScopeForNode(replacement));
        if (before != null)
        {
            Assert.Null(currentScope.Find(before));
        }
        if (after != null)
        {
            Assert.Same(replacement, currentScope.Find(after));
        }
    }

    [Fact]
    public void ScopeAttachment_IsIdempotentAndAttachesRetainedLookupToANewUnnamedRoot()
    {
        using var state = new AkburaRenderState();
        Assert.True(state.BeginRevision("initial", builder =>
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(StackPanel), null, "root")), _ => new StackPanel()));
        var root = state.GetRequired<StackPanel>(0);
        var scope = state.GetLocalNameScope(null, []);
        scope.Complete();
        state.ReconcileNameScope(0, root, scope);
        state.ReconcileNameScope(0, root, scope);
        state.CompleteRevision();
        Assert.Same(scope, NameScope.GetNameScope(root));
        Assert.True(state.BeginRevision("changed-root", builder =>
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(Border), null, "root")), _ => new Border()));
        var replacement = state.GetRequired<Border>(0);
        Assert.Same(scope, state.GetLocalNameScope(null, []));
        state.ReconcileNameScope(0, replacement, scope);
        state.ReconcileNameScope(0, replacement, scope);
        Assert.Throws<InvalidOperationException>(() => state.ReconcileNameScope(0, new Border(), scope));
        state.CompleteRevision();
        Assert.Same(scope, NameScope.GetNameScope(replacement));
        Assert.Null(NameScope.GetNameScope(root));
    }

    [Fact]
    public void ScopeIdentity_IncludesActualTargetInstancesAndParent()
    {
        using var state = new AkburaRenderState();
        Begin(state, "initial", "title");
        var element = state.GetRequired<TextBlock>(1);
        var parent = new NameScope();
        var scope = state.GetLocalNameScope(parent, [("title", element)]);
        scope.Register("title", element);
        scope.Complete();
        state.CompleteRevision();
        Begin(state, "next", "title");
        Assert.Same(scope, state.GetLocalNameScope(parent, [("title", element)]));
        Assert.NotSame(scope, state.GetLocalNameScope(new NameScope(), [("title", element)]));
        state.AbortRevision();
        Begin(state, "changed-instance", "title");
        Assert.NotSame(scope, state.GetLocalNameScope(parent, [("title", new TextBlock())]));
        state.AbortRevision();
    }

    private static INameScope CompleteScope(AkburaRenderState state, TextBlock title, string? name)
    {
        var scope = state.GetLocalNameScope(null, name == null ? [] : [(name, title)]);
        if (!scope.IsCompleted)
        {
            if (name != null)
            {
                scope.Register(name, title);
            }
            scope.Complete();
        }
        return scope;
    }

    private static void Begin(AkburaRenderState state, string revision, string? name)
    {
        Assert.True(state.BeginRevision(revision, builder =>
        {
            builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(StackPanel), null, "root"));
            builder.Add(new AkburaRenderNodeDefinition(1, 0, "children", typeof(TextBlock), name, revision));
            builder.Add(new AkburaRenderNodeDefinition(2, 0, "children", typeof(TextBlock), "sibling", "sibling"));
        }, id => id == 0 ? new StackPanel() : new TextBlock()));
    }
}
