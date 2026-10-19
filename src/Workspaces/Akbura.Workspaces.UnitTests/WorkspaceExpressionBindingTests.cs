using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Akbura.Workspaces.AutomaticPairing;
using Akbura.Workspaces.Classification;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Akbura.Workspaces.Formatting;
using Akbura.Workspaces.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceExpressionBindingTests
{
    private const string Prelude = "namespace Gallery;\r\nusing Avalonia.Controls;\r\n" +
        "inject ViewModel Vm;\r\nparam Person Person;\r\nstate int Count = 0;\r\n";

    [Theory]
    [InlineData("${|}")]
    [InlineData("${x|}")]
    [InlineData("${x.|}")]
    [InlineData("${x.bi|}")]
    [InlineData("${x.bind|}")]
    [InlineData("${x.bi|")]
    public void PrefixCompletion_IsBuiltInAndWorksWithoutSemanticProject(string value)
    {
        using var workspace = new AkburaWorkspace();
        var (document, position) = Parse("<TextBlock Text=" + value + " />");
        Assert.False(document.TryGetCSharpCompletionContext(position, out _));
        var result = workspace.LanguageServices.Completion.GetCompletions(document, null, position);
        var item = Assert.Single(result.Items, item => item.DisplayText == "x.bind");
        Assert.Equal(AkburaCompletionKind.Keyword, item.Kind);
        Assert.True(item.IsMarkupValuePrefix);
        var change = workspace.LanguageServices.Completion.GetCompletionChange(document, null, position, item);
        var changed = document.Text.WithChanges(change.Changes);
        Assert.Equal("<TextBlock Text=${x.bind " + (value.Contains('}') ? "}" : "") + " />", changed.ToString());
        Assert.True(change.TriggerNextCompletion);
        Assert.True(document.WithText(changed).TryGetCSharpCompletionContext(change.NewPosition, out var context));
        Assert.Equal(AkburaCSharpCompletionContextKind.Expression, context.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrefixCompletion_KeepsCatalogIncompleteForSubsequentTyping(bool semanticProject)
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(Prelude + "<TextBlock Text=${|} />");
        var context = semanticProject ? Open(workspace, document.Text) : null;
        var result = workspace.LanguageServices.Completion.GetCompletions(document, context, position);
        Assert.True(result.IsIncomplete);
        Assert.Contains(result.Items, item => item.DisplayText == "x.bind");
    }

    [Theory]
    [InlineData("<TextBlock Text={x.|} />", "Component.akbura")]
    [InlineData("<TextBlock Text=\"${x.|}\" />", "Component.akbura")]
    [InlineData("Control.card { Width: x.|; }", "Styles.akcss")]
    [InlineData("<TextBlock Text=${x.bind Vm.|} />", "Component.akbura")]
    public void PrefixCompletion_DoesNotLeakOutsideThePrefix(string source, string path)
    {
        using var workspace = new AkburaWorkspace();
        var (document, position) = Parse(source, path);
        Assert.DoesNotContain(workspace.LanguageServices.Completion.GetCompletions(document, null, position).Items,
            item => item.DisplayText == "x.bind");
    }

    [Theory]
    [InlineData("Vm.|", "Name")]
    [InlineData("Vm.Profile.|", "Address")]
    [InlineData("Vm.Profile.Address.|", "City")]
    [InlineData("$\"{Vm.FirstName} {Vm.|}\"", "LastName")]
    [InlineData("Vm.Enabled ? Vm.| : \"Empty\"", "Name")]
    [InlineData("Vm.Items[Vm.|].Name", "SelectedIndex")]
    [InlineData("Vm.Items[Vm.SelectedIndex].|", "Address")]
    [InlineData("Person.|", "Name")]
    [InlineData("Count.To|", "ToString")]
    [InlineData("|", "Vm")]
    public async Task MemberCompletion_UsesTheActualCSharpReceiver(string expression, string member)
    {
        await AssertCompletion(Prelude + "<TextBlock Text=${x.bind " + expression + "} />", member);
    }

    [Theory]
    [InlineData("var current = Vm.Profile; <TextBlock Text=${x.bind current.|} />", "Address")]
    [InlineData("var Vm = Person; <TextBlock Text=${x.bind Vm.|} />", "Address")]
    [InlineData("<StackPanel>$foreach (var item in Vm.Items) { <TextBlock Text=${x.bind item.|} /> }</StackPanel>", "Address")]
    [InlineData("<TextBlock x.Name=\"text\" Text=${x.bind text.|} />", "Width")]
    [InlineData("<ItemsControl><ItemsControl.ItemTemplate x.DataType=\"Person\" x.ItemName=\"person\"><TextBlock Text=${x.bind person.|} /></ItemsControl.ItemTemplate></ItemsControl>", "Address")]
    public async Task MemberCompletion_PreservesLocalForeachTemplateAndNamedScopes(string body, string member)
    {
        await AssertCompletion(Prelude + body, member);
    }

    [Theory]
    [InlineData("Vm.Name")]
    [InlineData("  Vm.Name  ")]
    [InlineData("\r\n (Vm.Name) \r\n")]
    [InlineData("$\"😀 {Vm.FirstName} {Vm.LastName}\"")]
    [InlineData("Vm.Enabled ? Vm.Name : \"Empty\"")]
    [InlineData("Vm.Profile?.Address.City ?? \"Unknown\"")]
    [InlineData("Vm.Items[Vm.SelectedIndex].Name")]
    public void Projection_RoundTripsEveryUtf16PositionAndKeepsThePrefixOutside(string expression)
    {
        using var workspace = CreateWorkspace();
        foreach (var prefix in new[] { Prelude, "// 😀 edit before expression\r\n" + Prelude })
        {
            var source = prefix + "<TextBlock Text=${x.bind " + expression + "} />";
            var text = SourceText.From(source);
            var context = Open(workspace, text);
            var document = AkburaSyntacticDocument.Create(context.Document);
            var position = source.IndexOf("Vm.", StringComparison.Ordinal) + 1;
            Assert.True(document.TryGetCSharpCompletionContext(position, out var csharp));
            Assert.True(AkburaCSharpProjectionFactory.TryCreate(document, context, csharp, out var projection));
            Assert.Equal(text.ToString(csharp.HostSpan), projection.Root.ToFullString().Substring(
                projection.ProjectedSpan.Start, projection.ProjectedSpan.Length));
            Assert.DoesNotContain("x.bind", text.ToString(csharp.HostSpan));
            for (var offset = csharp.HostSpan.Start; offset <= csharp.HostSpan.End; offset++)
            {
                Assert.True(projection.TryMapPositionToProjected(offset, out var projected));
                Assert.True(projection.TryMapToHost(new TextSpan(projected, 0), out var host));
                Assert.Equal(offset, host.Start);
            }
        }
    }

    [Theory]
    [InlineData("Vm.Count + 10", "double")]
    [InlineData("default", "double")]
    public void Projection_UsesDestinationValueTypeRatherThanBindingChannel(string expression, string type)
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(Prelude + "<TextBlock Width=${x.bind " + expression + "|} />");
        var context = Open(workspace, document.Text);
        Assert.True(document.TryGetCSharpCompletionContext(position, out var csharp));
        Assert.True(AkburaCSharpProjectionFactory.TryCreate(document, context, csharp, out var projection));
        var method = projection.Root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "__akbura_probe");
        Assert.Equal(type, method.ReturnType.ToString());
        var tree = CSharpSyntaxTree.Create(projection.Root);
        var compilation = context.Project.Compilation.CSharpProbeCompilation.AddSyntaxTrees(tree);
        var model = compilation.GetSemanticModel(tree);
        var returned = CSharpProbeBuilder.GetReturnProbeExpression(tree.GetCompilationUnitRoot())!;
        Assert.Equal(SpecialType.System_Double, model.GetTypeInfo(returned).ConvertedType!.SpecialType);
    }

    [Theory]
    [InlineData("Selected", "Person")]
    [InlineData("Names", "List")]
    [InlineData("OptionalWidth", "Nullable")]
    public void Projection_RetainsNullableAndGenericContextualTypes(string property, string typeName)
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(Prelude + "<ValueControl " + property + "=${x.bind default|} />");
        var context = Open(workspace, document.Text);
        Assert.True(document.TryGetCSharpCompletionContext(position, out var csharp));
        Assert.True(AkburaCSharpProjectionFactory.TryCreate(document, context, csharp, out var projection));
        var tree = CSharpSyntaxTree.Create(projection.Root);
        var model = context.Project.Compilation.CSharpProbeCompilation.AddSyntaxTrees(tree).GetSemanticModel(tree);
        var expression = CSharpProbeBuilder.GetReturnProbeExpression(tree.GetCompilationUnitRoot())!;
        Assert.Equal(typeName, model.GetTypeInfo(expression).ConvertedType!.Name);
    }

    [Fact]
    public void Projection_MapsAnExpressionWithMissingClosingDelimiter()
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(Prelude + "<TextBlock Text=${x.bind Vm.Na|");
        var context = Open(workspace, document.Text);
        Assert.True(document.TryGetCSharpCompletionContext(position, out var csharp));
        Assert.True(AkburaCSharpProjectionFactory.TryCreate(document, context, csharp, out var projection));
        Assert.True(projection.TryMapPositionToProjected(position, out var projected));
        Assert.True(projection.TryMapPositionToHost(projected, out var mapped));
        Assert.Equal(position, mapped);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n ")]
    public void EmptyProjection_RetainsEverySeparatorTriviaPosition(string whitespace)
    {
        using var workspace = CreateWorkspace();
        var source = Prelude + "<TextBlock Text=${x.bind " + whitespace + "} />";
        var context = Open(workspace, SourceText.From(source));
        var document = AkburaSyntacticDocument.Create(context.Document);
        var start = source.IndexOf("x.bind", StringComparison.Ordinal) + "x.bind".Length;
        var end = source.LastIndexOf('}');
        for (var position = start + 1; position <= end; position++)
        {
            Assert.True(document.TryGetCSharpCompletionContext(position, out var csharp));
            Assert.Equal(Akbura.Language.Syntax.SyntaxKind.MarkupExpressionBindingSyntax, csharp.OwnerKind);
            Assert.True(AkburaCSharpProjectionFactory.TryCreate(document, context, csharp, out var projection));
            Assert.Equal(source.Substring(start, end - start), projection.Root.ToFullString().Substring(
                projection.ProjectedSpan.Start, projection.ProjectedSpan.Length));
            Assert.True(projection.TryMapPositionToProjected(position, out var projected));
            Assert.True(projection.TryMapPositionToHost(projected, out var mapped));
            Assert.Equal(position, mapped);
        }
    }

    [Theory]
    [InlineData("Vm.Na|me", "Name", "Vm.Name")]
    [InlineData("Vm.|", "Name", "Vm.Name")]
    [InlineData("$\"Hello {Vm.Na|me}\"", "Name", "$\"Hello {Vm.Name}\"")]
    public async Task MemberCommit_ReplacesOnlyTheMemberAndRetainsBothKindsOfBrace(string expression, string member, string expected)
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(Prelude + "<TextBlock Text=${x.bind " + expression + "} />");
        var context = Open(workspace, document.Text);
        var result = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(document, context,
            position, new(true, false, '\0'));
        Assert.NotNull(result);
        var item = Assert.Single(result.Value.Items, item => item.DisplayText == member);
        var resolution = await workspace.LanguageServices.ProjectedCSharp.ResolveCompletionAsync(document,
            context, position, item.ResolveKey);
        Assert.NotNull(resolution);
        Assert.Equal(Prelude + "<TextBlock Text=${x.bind " + expected + "} />",
            document.Text.WithChanges(resolution.Change.Changes).ToString());
        var mainChange = Assert.Single(resolution.Change.Changes, change => change.NewText == member);
        var deltaBefore = resolution.Change.Changes.Where(change => change.Span.End <= mainChange.Span.Start &&
            change.Span != mainChange.Span).Sum(change => (change.NewText?.Length ?? 0) - change.Span.Length);
        Assert.Equal(mainChange.Span.Start + member.Length + deltaBefore, resolution.Change.NewPosition);
    }

    [Theory]
    [InlineData("Count", "Total")]
    [InlineData("Person", "Customer")]
    [InlineData("Vm", "Model")]
    public void ReferencesAndRename_IncludeOrdinaryAndExpressionBindingOccurrences(string name, string replacement)
    {
        using var workspace = CreateWorkspace();
        var source = Prelude + $"<StackPanel><TextBlock Text={{{name}.ToString()}} />" +
            $"<TextBlock Text=${{x.bind {name}.ToString()}} />" +
            $"<TextBlock Text=${{x.bind $\"Value: {{{name}}}\"}} /></StackPanel>";
        var context = Open(workspace, SourceText.From(source));
        var declarationPosition = source.IndexOf(name + ";", StringComparison.Ordinal);
        if (name == "Count") declarationPosition = source.IndexOf("Count =", StringComparison.Ordinal);
        foreach (var position in new[] { declarationPosition, source.LastIndexOf(name, StringComparison.Ordinal) })
        {
            var references = workspace.LanguageServices.References.FindReferences(context, position, includeDeclaration: true);
            Assert.Equal(4, references.Locations.Length);
            Assert.Single(references.Locations, reference => reference.IsDeclaration);
            var edits = workspace.LanguageServices.Rename.GetRenameChanges(context, position, replacement);
            var changes = Assert.Single(edits.Changes).Value;
            Assert.Equal(4, changes.Length);
            Assert.Equal(changes.Length, changes.Select(change => change.Span).Distinct().Count());
            var changed = context.Document.Text.WithChanges(changes).ToString();
            Assert.Contains("x.bind", changed);
            Assert.Contains("Value:", changed);
            Assert.Contains(replacement + ".ToString()", changed);
        }
    }

    [Theory]
    [InlineData("<StackPanel>$foreach (var item in Vm.Items) { <TextBlock Text=${x.bind ite|m.Name} /><TextBlock Text=${x.bind item.Name} /> }</StackPanel>", "item", 3)]
    [InlineData("<StackPanel><TextBlock x.Name=\"text\" Text=${x.bind tex|t.Width.ToString()} /><TextBlock Text=${x.bind text.Text} /></StackPanel>", "text", 3)]
    [InlineData("state string Name = \"state\"; var Name = Vm.Name; <TextBlock Text=${x.bind Na|me} />", "Name", 2)]
    public void Rename_PreservesProjectedOriginAndShadowing(string body, string name, int count)
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(Prelude + body);
        var context = Open(workspace, document.Text);
        var references = workspace.LanguageServices.References.FindReferences(context, position, includeDeclaration: true);
        Assert.Equal(count, references.Locations.Length);
        var changed = document.Text.WithChanges(Assert.Single(workspace.LanguageServices.Rename
            .GetRenameChanges(context, position, "renamed").Changes).Value).ToString();
        Assert.Contains("x.bind renamed", changed);
        if (name == "Name") Assert.Contains("state string Name", changed);
    }

    [Theory]
    [InlineData("Vm.Pro|file.Name", "Profile", "Person")]
    [InlineData("Vm.Profile.Na|me", "Name", "string")]
    [InlineData("Vm.For|mat(Vm.Name)", "Format", "string")]
    [InlineData("Cou|nt.ToString()", "Count", "Int32")]
    public async Task HoverAndDefinition_MapToRealHostOrCSharpDeclaration(string expression, string name, string type)
    {
        using var csharpSource = new CSharpSourceFixture();
        using var workspace = CreateWorkspace(csharpSource.FilePath);
        var (document, position) = Parse(Prelude + "<TextBlock Text=${x.bind " + expression + "} />");
        var context = Open(workspace, document.Text);
        var nativeInfo = workspace.LanguageServices.QuickInfo.GetQuickInfo(context, position);
        var projectedInfo = await workspace.LanguageServices.ProjectedCSharp.GetQuickInfoAsync(document, context, position);
        Assert.NotNull(nativeInfo);
        Assert.Contains(type, nativeInfo.Signature, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(projectedInfo);
        Assert.Equal(name, document.Text.ToString(projectedInfo.SourceSpan));
        Assert.DoesNotContain("__akbura", projectedInfo.Signature, StringComparison.OrdinalIgnoreCase);
        var definition = workspace.LanguageServices.Definition.GetDefinition(context, position);
        Assert.NotNull(definition);
        Assert.EndsWith(name == "Count" ? ".akbura" : "ViewModel.cs", definition.TargetFilePath);
    }

    [Fact]
    public void Classification_SeparatesDirectiveAndCSharpAndDoesNotDuplicateOccurrences()
    {
        using var workspace = CreateWorkspace();
        var source = Prelude + "<TextBlock Text=${x.bind $\"{Vm.FirstName} {Vm.LastName}\"} />";
        var text = SourceText.From(source);
        var context = Open(workspace, text);
        var document = AkburaSyntacticDocument.Create(context.Document);
        var whole = new TextSpan(0, text.Length);
        foreach (var classifications in new[] {
            workspace.LanguageServices.Classification.GetSyntacticClassifications(document, whole),
            workspace.LanguageServices.Classification.GetClassifications(context, whole) })
        {
            Assert.Single(classifications, span => span.Span == new TextSpan(source.IndexOf("bind $", StringComparison.Ordinal), 4) &&
                span.Kind == AkburaClassificationKind.Directive);
            Assert.Contains(classifications, span => span.Kind == AkburaClassificationKind.String);
        }
        var semantic = workspace.LanguageServices.Classification.GetClassifications(context, whole);
        foreach (var member in new[] { "FirstName", "LastName" })
            Assert.Single(semantic, span => text.ToString(span.Span) == member && span.Kind == AkburaClassificationKind.PropertyName);
        Assert.Equal(semantic.Length, semantic.Distinct().Count());
    }

    [Theory]
    [InlineData("Vm.Format(|)", 0)]
    [InlineData("Vm.Format(Vm.Name, |)", 1)]
    [InlineData("$\"{Vm.Format(Vm.Name, |)}\"", 1)]
    public void SignatureHelp_UsesTheExistingProjection(string expression, int parameter)
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(Prelude + "<TextBlock Text=${x.bind " + expression + "} />");
        var context = Open(workspace, document.Text);
        var help = workspace.LanguageServices.SignatureHelp.GetSignatureHelp(document, context, position);
        Assert.NotNull(help);
        Assert.Equal(parameter, help.ActiveParameter);
        Assert.Contains(help.Signatures, signature => signature.Label.Contains("Format", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Text", "Vm.UnknownMember", "UnknownMember")]
    [InlineData("Width", "Vm.Name", "Vm.Name")]
    [InlineData("x.Name", "Vm.Name", "x.bind")]
    [InlineData("Text", "Vm.", "Vm.")]
    [InlineData("Text", "Vm.Format(Vm.Name)", "Format")]
    public void Diagnostics_StayInTheSourceAndCodeActionsNeverCorruptTheBinding(string property, string expression, string diagnosticText)
    {
        using var workspace = CreateWorkspace();
        var source = Prelude + "// 😀 UTF-16\r\n<TextBlock " + property + "=${x.bind " + expression + "} />";
        var context = Open(workspace, SourceText.From(source));
        var diagnostics = workspace.LanguageServices.Diagnostics.GetDiagnostics(context, new TextSpan(0, source.Length));
        if (expression == "Vm.")
        {
            // Roslyn correctly reports a missing identifier at the caret, not
            // an underline over the valid receiver. Preserve that exact span.
            var expected = new TextSpan(source.LastIndexOf(expression, StringComparison.Ordinal) + expression.Length, 0);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Span == expected &&
                diagnostic.Code == "AKBURA_SEMANTIC_MarkupExpressionError" &&
                diagnostic.Message.Contains("Identifier expected", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(diagnostics, diagnostic => source.Substring(diagnostic.Span.Start, diagnostic.Span.Length)
                .Contains(diagnosticText, StringComparison.Ordinal));
        }
        Assert.All(diagnostics, diagnostic => Assert.InRange(diagnostic.Span.End, 0, source.Length));
        foreach (var action in workspace.LanguageServices.CodeActions.GetCodeActions(context, new TextSpan(0, source.Length)))
        {
            Assert.DoesNotContain(".projection.cs", action.Title, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Vm.Calculate|", '(', ")")]
    [InlineData("Vm.Items|", '[', "]")]
    [InlineData("|", '"', "\"")]
    public void Pairing_UsesCSharpInsideTheBinding(string expression, char opening, string closing)
    {
        var (document, position) = Parse("<TextBlock Text=${x.bind " + expression + "} />");
        var decision = document.GetAutomaticPairDecision(position, opening);
        Assert.True(decision.IsValid);
        Assert.Equal(closing, decision.ClosingText);
    }

    [Fact]
    public void InterpolationTyping_PairsAndOvertypesWithoutConsumingTheOuterBindingBrace()
    {
        using var workspace = new AkburaWorkspace();
        var (document, position) = Parse("<TextBlock Text=${x.bind $\"Hello |\"} />");
        var options = new AkburaTypingOptions(4, 4, true, "\r\n");
        var result = workspace.LanguageServices.Typing.GetResult(document,
            new AkburaTypingCommand(AkburaTypingCommandKind.Type, position, "{", options, null));
        Assert.True(result.Handled);
        Assert.Equal("{}", Assert.Single(result.Changes).NewText);
        Assert.Equal(AkburaPairSessionKind.InterpolationBraces, result.Session!.Kind);
        var paired = document.Text.WithChanges(result.Changes);
        Assert.Equal("<TextBlock Text=${x.bind $\"Hello {}\"} />", paired.ToString());
        var overtype = workspace.LanguageServices.Typing.GetResult(document.WithText(paired),
            new AkburaTypingCommand(AkburaTypingCommandKind.Type, result.NewPosition, "}", options, result.Session));
        Assert.True(overtype.Handled);
        Assert.Empty(overtype.Changes);
        Assert.Equal(result.NewPosition + 1, overtype.NewPosition);
    }

    [Theory]
    [InlineData(false, "x.bind ")]
    [InlineData(true, "x.bind ")]
    [InlineData(false, "x . bind \r\n ")]
    [InlineData(true, "x . bind \r\n ")]
    [InlineData(false, "x.bind /* keep this comment */\r\n ")]
    [InlineData(true, "x.bind /* keep this comment */\r\n ")]
    public void PairedBraceCommit_TracksTheActualAkburaOpener(bool pairBeforeSession, string directive)
    {
        var prefix = Prelude + "<TextBlock Text=${" + directive;
        var original = SourceText.From(prefix + (pairBeforeSession ? "}" : "") + "/>");
        var document = AkburaSyntacticDocument.Parse(original, "ExpressionBindings.akbura");
        var position = prefix.Length;
        Assert.True(document.TryGetCSharpCompletionContext(position, out var source));
        var text = original;
        var owner = source.OwnerSpan;
        var host = source.HostSpan;
        if (!pairBeforeSession) Insert("}", false);
        Insert("C", true);
        Assert.True(document.TryGetCSharpCompletionContext(position, out var current));
        Assert.True(AkburaCSharpCompletionCommitFacts.IsMatchingContext(original, text, source, current,
            owner, host, out var reason), reason.ToString());
        Assert.True(AkburaCSharpCompletionCommitFacts.TryGetReplacementSpan(text, current, host, out var replacement));
        Assert.Equal(prefix + "Count}/>", text.WithChanges(new TextChange(replacement, "Count")).ToString());
        // A prefix edit is outside the C# fragment and cannot authorize reuse.
        var changedPrefix = text.WithChanges(new TextChange(new TextSpan(Prelude.Length + 1, 1), "B"));
        Assert.False(AkburaCSharpCompletionCommitFacts.IsMatchingContext(original, changedPrefix, source,
            current, owner, host, out _));

        void Insert(string insertion, bool advance)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position, 0), insertion));
            document = document.WithText(text);
            owner = WorkspaceCSharpCompletionCommitTests.ExpandForInsertion(owner, position, insertion.Length);
            host = WorkspaceCSharpCompletionCommitTests.ExpandForInsertion(host, position, insertion.Length);
            if (advance) position += insertion.Length;
        }
    }

    [Fact]
    public void BindingCommit_RejectsAnEditConfinedToTheSeparator()
    {
        var (document, position) = Parse("<TextBlock Text=${x.bind    C|} />");
        Assert.True(document.TryGetCSharpCompletionContext(position, out var context));
        Assert.False(AkburaCSharpCompletionCommitFacts.TryGetReplacementSpan(document.Text, context,
            new TextSpan(context.HostSpan.Start, 1), out _));
    }

    [Fact]
    public async Task CancelledRequest_DoesNotPublishExpressionBindingCompletion()
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(Prelude + "<TextBlock Text=${x.bind Vm.|} />");
        var context = Open(workspace, document.Text);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.LanguageServices.ProjectedCSharp
            .GetCompletionsAsync(document, context, position, new(true, false, '\0'), cancellation.Token));
    }

    [Fact]
    public void CompilerReferenceApi_ReportsEachNestedOccurrenceWithItsRealSymbol()
    {
        using var workspace = CreateWorkspace();
        var source = Prelude + "<TextBlock Text=${x.bind $\"{Vm.Items[Vm.SelectedIndex].Name} {Vm.LastName}\"} />";
        var context = Open(workspace, SourceText.From(source));
        var model = context.Project.Compilation.GetSemanticModel(context.Document.SyntaxTree);
        var attribute = Assert.Single(context.Document.SyntaxTree.GetRootSyntax().DescendantNodes().OfType<MarkupAttributeSyntax>());
        var references = model.GetCSharpSymbolReferences(attribute);
        Assert.Equal(3, references.Count(reference => context.Document.Text.ToString(reference.SourceSpan) == "Vm"));
        foreach (var name in new[] { "Items", "SelectedIndex", "Name", "LastName" })
        {
            var reference = Assert.Single(references, reference => context.Document.Text.ToString(reference.SourceSpan) == name);
            Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(reference.CSharpDefinition.Symbol);
        }
        Assert.All(references.Where(reference => context.Document.Text.ToString(reference.SourceSpan) == "Vm"),
            reference => Assert.Equal("Vm", reference.AkburaSymbol!.Name));
    }

    [Theory]
    [InlineData("${")]
    [InlineData("${x")]
    [InlineData("${x.")]
    [InlineData("${x.bind")]
    [InlineData("${x.bind ")]
    [InlineData("${x.bind Vm.")]
    [InlineData("${x.bind $\"{Vm.")]
    public async Task IncompleteTyping_AllSharedServicesRemainBounded(string value)
    {
        using var workspace = CreateWorkspace();
        var source = Prelude + "<TextBlock Text=" + value;
        var text = SourceText.From(source);
        var context = Open(workspace, text);
        var document = AkburaSyntacticDocument.Create(context.Document);
        var position = text.Length;
        workspace.LanguageServices.Completion.GetCompletions(document, context, position);
        await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(document, context, position, new(true, false, '\0'));
        workspace.LanguageServices.SignatureHelp.GetSignatureHelp(document, context, position);
        workspace.LanguageServices.QuickInfo.GetQuickInfo(context, position);
        workspace.LanguageServices.Definition.GetDefinition(context, position);
        var spans = workspace.LanguageServices.Classification.GetClassifications(context, new TextSpan(0, position));
        Assert.All(spans, span => Assert.InRange(span.Span.End, 0, position));
        Assert.Equal(source, document.SyntaxTree.GetRootSyntax().ToFullString());
    }

    [Fact]
    public void Formatting_PreservesMultilineConditionalAndInterpolation()
    {
        using var workspace = CreateWorkspace();
        const string expression = "Vm.Enabled\r\n    ? $\"{Vm.Name}\" : \"Disabled\"";
        var (document, _) = Parse("<TextBlock Text=${x.bind " + expression + "|} Width=\"20\" />");
        foreach (var changes in new[] {
            workspace.LanguageServices.Formatting.FormatDocument(document, new()),
            workspace.LanguageServices.Formatting.FormatRange(document, new TextSpan(0, document.Text.Length), new()),
            workspace.LanguageServices.Formatting.FormatOnType(document, document.Text.Length, '}', new()) })
        {
            var changed = document.Text.WithChanges(changes);
            Assert.Contains("${x.bind " + expression + "}", changed.ToString());
            Assert.Equal(changed.ToString(), document.WithText(changed).SyntaxTree.GetRootSyntax().ToFullString());
        }
    }

    private static async Task AssertCompletion(string source, string member)
    {
        using var workspace = CreateWorkspace();
        var (document, position) = Parse(source);
        var context = Open(workspace, document.Text);
        Assert.True(document.TryGetCSharpCompletionContext(position, out var csharp));
        Assert.Equal(AkburaCSharpCompletionContextKind.Expression, csharp.Kind);
        var result = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(document, context,
            position, new(true, false, '\0'));
        Assert.NotNull(result);
        Assert.Contains(result.Value.Items, item => item.DisplayText == member);
        Assert.DoesNotContain(result.Value.Items, item => item.DisplayText.StartsWith("__akbura", StringComparison.OrdinalIgnoreCase));
    }

    private static AkburaWorkspace CreateWorkspace(string? csharpFilePath = null) =>
        WorkspaceControlEventHookTests.CreateWorkspace(Definitions, csharpFilePath ?? Path.GetFullPath("ViewModel.cs"));

    private sealed class CSharpSourceFixture : IDisposable
    {
        public CSharpSourceFixture()
        {
            var directory = Path.Combine(Path.GetTempPath(), nameof(WorkspaceExpressionBindingTests), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, "ViewModel.cs");
            File.WriteAllText(FilePath, Definitions);
        }

        public string FilePath { get; }

        public void Dispose()
        {
            File.Delete(FilePath);
            Directory.Delete(Path.GetDirectoryName(FilePath)!);
        }
    }

    private static AkburaDocumentContext Open(AkburaWorkspace workspace, SourceText text) =>
        workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("ExpressionBindings.akbura")), text);

    private static (AkburaSyntacticDocument Document, int Position) Parse(string marked, string path = "ExpressionBindings.akbura")
    {
        var position = marked.IndexOf('|');
        Assert.True(position >= 0);
        return (AkburaSyntacticDocument.Parse(SourceText.From(marked.Remove(position, 1)), Path.GetFullPath(path)), position);
    }

    private const string Definitions = """
        #nullable enable
        using System.ComponentModel;
        namespace Gallery;
        public sealed class Address { public string City { get; set; } = "Bishkek"; }
        public sealed class Person
        {
            /// <summary>The displayed name.</summary>
            public string Name { get; set; } = "Ada";
            public Address Address { get; set; } = new();
        }
        public sealed class ViewModel : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            public string Name { get; set; } = "Ada";
            public string FirstName { get; set; } = "Ada";
            public string LastName { get; set; } = "Lovelace";
            public Person Profile { get; set; } = new();
            public Person[] Items { get; set; } = [];
            public int SelectedIndex { get; set; }
            public int Count { get; set; }
            public bool Enabled { get; set; }
            /// <summary>Formats a display name.</summary>
            public string Format(string value, int count = 0) => value;
        }
        public sealed class ValueControl : Avalonia.Controls.Control
        {
            public Person? Selected { get; set; }
            public System.Collections.Generic.List<string>? Names { get; set; }
            public double? OptionalWidth { get; set; }
        }
        """;
}
