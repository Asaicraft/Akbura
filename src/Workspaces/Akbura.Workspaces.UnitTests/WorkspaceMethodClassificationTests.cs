using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceMethodClassificationTests
{
    private const string ComponentSource = """
        using Akbura.ComponentTree;
        using Avalonia.Threading;
        using Avalonia.VisualTree;

        state string? componentUrl = null;

        AkburaControl? FindFirstComponentAncestor()
        {
            var current =
                ((IComponentTree)this).ComponentParent;

            while (current != null)
            {
                var type = current.GetType();
                var currentNamespace = type.Namespace;

                if (currentNamespace ==
                    "Akbura.FeatureGallery.Pages")
                {
                    return null;
                }

                if (current is AkburaControl component &&
                    currentNamespace ==
                        "Akbura.FeatureGallery.Components")
                {
                    return component;
                }

                current = current.ComponentParent;
            }

            return null;
        }

        void ResolveComponent()
        {
            var component =
                FindFirstComponentAncestor();

            var newComponentUrl =
                component == null
                    ? null
                    : $"Components/{component.GetType().Name}.akbura";

            if (componentUrl != newComponentUrl)
            {
                componentUrl = newComponentUrl;
            }
        }

        useEffect(() =>
        {
            EventHandler<VisualTreeAttachmentEventArgs> onAttached =
                (_, _) =>
                {
                    Dispatcher.UIThread.Post(
                        ResolveComponent);
                };

            AttachedToVisualTree += onAttached;

            Dispatcher.UIThread.Post(
                ResolveComponent);

            return () =>
            {
                AttachedToVisualTree -= onAttached;
            };
        }, []);

        <ViewThisInGithub
            Url={componentUrl ?? ""}
            Content="View this component in GitHub"
            IsVisible={componentUrl != null}
            HorizontalAlignment="Left"/>
        """;

    private const string GlobalUsingsSource = """
        using System;
        using Akbura;
        using Akbura.FeatureGallery.Components;
        using Akbura.Hooks;
        using Avalonia.Controls;
        """;

    [Fact]
    public void SyntacticClassification_ComponentMethodDeclarationsAreMethodsWithoutProject()
    {
        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(ComponentSource);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "ViewThisComponentInGithub.akbura");
        var classifications = workspace.LanguageServices.Classification
            .GetSyntacticClassifications(
                document,
                new TextSpan(0, text.Length));

        AssertOnlyClassification(
            classifications,
            GetDeclarationSpan("FindFirstComponentAncestor"),
            AkburaClassificationKind.MethodName);
        AssertOnlyClassification(
            classifications,
            GetDeclarationSpan("ResolveComponent"),
            AkburaClassificationKind.MethodName);
    }

    [Fact]
    public void SemanticClassification_ComponentMethodDeclarationsAndReferencesAreMethods()
    {
        using var workspace = CreateSemanticWorkspace();
        var text = SourceText.From(ComponentSource);
        var context = OpenComponent(workspace, ComponentSource);
        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(context, new TextSpan(0, text.Length));

        foreach (var span in GetExpectedMethodSpans())
        {
            AssertOnlyClassification(
                classifications,
                span,
                AkburaClassificationKind.MethodName);
        }
    }

    [Fact]
    public void CompilerReferences_ComponentMethodUsagesResolveToMethodSymbols()
    {
        using var workspace = CreateSemanticWorkspace();
        var context = OpenComponent(workspace, ComponentSource);
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        var references = context.Document.SyntaxTree.GetRootSyntax()
            .DescendantNodes()
            .OfType<CSharpStatementSyntax>()
            .SelectMany(statement =>
                semanticModel.GetCSharpSymbolReferences(statement))
            .ToArray();
        var spans = GetExpectedMethodSpans();

        AssertMethodSymbol(spans[0], "FindFirstComponentAncestor");
        AssertMethodSymbol(spans[1], "ResolveComponent");
        AssertMethodSymbol(spans[2], "ResolveComponent");

        void AssertMethodSymbol(TextSpan sourceSpan, string expectedName)
        {
            var matching = references
                .Where(reference => reference.SourceSpan == sourceSpan)
                .ToArray();

            Assert.NotEmpty(matching);
            Assert.All(
                matching,
                reference =>
                {
                    var method = Assert.IsAssignableFrom<IMethodSymbol>(
                        reference.CSharpDefinition.Symbol);
                    Assert.Equal(expectedName, method.Name);
                });
        }
    }

    [Fact]
    public void SemanticClassification_ComponentMethodsSurviveProductionGeneratedAndStaleGeneratedTrees()
    {
        var baseCompilation = CreateCSharpCompilation();
        var compilation = AddProductionGeneratedComponent(
            baseCompilation,
            ComponentSource);
        var generatedTree = Assert.Single(
            compilation.SyntaxTrees,
            tree => tree.FilePath.Contains(
                "Akbura.BlackSilence.AkburaBlackSilenceGenerator",
                StringComparison.Ordinal));

        Assert.Contains(
            "<auto-generated",
            generatedTree.GetText().ToString(),
            StringComparison.Ordinal);

        using var workspace = CreateSemanticWorkspace(compilation);
        var context = OpenComponent(workspace, ComponentSource);
        AssertMethodClassifications(
            workspace,
            context,
            ComponentSource,
            "ResolveComponent");

        var staleSource = ComponentSource.Replace(
            "ResolveComponent",
            "RefreshComponent",
            StringComparison.Ordinal);
        context = workspace.OpenOrChangeDocumentContext(
            context.Document.Uri,
            SourceText.From(staleSource));

        AssertMethodClassifications(
            workspace,
            context,
            staleSource,
            "RefreshComponent");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SemanticClassification_PersistentMethodEditsMatchFreshWorkspaceAndUndo(string newLine)
    {
        var compilation = CreateCSharpCompilation();
        var original = ComponentSource.ReplaceLineEndings(newLine);
        var singleCharacterName = original.Replace(
            "ResolveComponent",
            "R",
            StringComparison.Ordinal);
        var renamed = original.Replace(
            "ResolveComponent",
            "RefreshComponent",
            StringComparison.Ordinal);
        var withNewline = InsertLineBeforeFindMethod(renamed, newLine);
        var moved = MoveResolveMethodBeforeFindMethod(
            withNewline,
            "RefreshComponent");
        var stages = new[]
        {
            (Source: original, ResolveName: "ResolveComponent"),
            (Source: singleCharacterName, ResolveName: "R"),
            (Source: renamed, ResolveName: "RefreshComponent"),
            (Source: withNewline, ResolveName: "RefreshComponent"),
            (Source: moved, ResolveName: "RefreshComponent"),
            (Source: withNewline, ResolveName: "RefreshComponent"),
            (Source: renamed, ResolveName: "RefreshComponent"),
            (Source: singleCharacterName, ResolveName: "R"),
            (Source: original, ResolveName: "ResolveComponent"),
            (Source: singleCharacterName, ResolveName: "R"),
            (Source: renamed, ResolveName: "RefreshComponent"),
            (Source: withNewline, ResolveName: "RefreshComponent"),
            (Source: moved, ResolveName: "RefreshComponent"),
        };
        using var workspace = CreateSemanticWorkspace(compilation);
        var context = OpenComponent(workspace, stages[0].Source);
        var text = context.Document.Text;

        for (var i = 0; i < stages.Length; i++)
        {
            if (i != 0)
            {
                text = ApplyMinimalChange(text, stages[i].Source);
                context = workspace.OpenOrChangeDocumentContext(
                    context.Document.Uri,
                    text);
            }

            AssertMethodClassifications(
                workspace,
                context,
                stages[i].Source,
                stages[i].ResolveName);
            AssertMatchesFresh(
                workspace,
                context,
                compilation,
                stages[i].Source);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SemanticClassification_CharacterwiseMethodRenameMatchesFreshWorkspace(string newLine)
    {
        const string methodName = "RefreshComponent";
        var compilation = CreateCSharpCompilation();
        using var workspace = CreateSemanticWorkspace(compilation);
        var source = CreateSource(methodName[..1]);
        var context = OpenComponent(
            workspace,
            source,
            "CharacterwiseMethod.akbura");
        var text = context.Document.Text;

        for (var length = 1; length <= methodName.Length; length++)
        {
            AssertStage(methodName[..length]);
        }

        for (var length = methodName.Length - 1; length >= 1; length--)
        {
            AssertStage(methodName[..length]);
        }

        void AssertStage(string name)
        {
            source = CreateSource(name);
            text = ApplyMinimalChange(text, source);
            context = workspace.OpenOrChangeDocumentContext(
                context.Document.Uri,
                text);
            var declarationStart = source.IndexOf(name + "()", StringComparison.Ordinal);
            var callStart = source.LastIndexOf(name + "();", StringComparison.Ordinal);
            var classifications = workspace.LanguageServices.Classification
                .GetClassifications(
                    context,
                    new TextSpan(0, source.Length));

            AssertOnlyClassification(
                classifications,
                new TextSpan(declarationStart, name.Length),
                AkburaClassificationKind.MethodName);
            AssertOnlyClassification(
                classifications,
                new TextSpan(callStart, name.Length),
                AkburaClassificationKind.MethodName);
            AssertMatchesFresh(
                workspace,
                context,
                compilation,
                source);
        }

        string CreateSource(string name)
        {
            return string.Join(
                newLine,
                "void " + name + "()",
                "{",
                "}",
                string.Empty,
                "void Invoke()",
                "{",
                "    " + name + "();",
                "}",
                string.Empty,
                "<Control/>");
        }
    }

    [Fact]
    public void SemanticClassification_ComponentMethodsSupportNarrowRequestedSpans()
    {
        using var workspace = CreateSemanticWorkspace();
        var context = OpenComponent(workspace, ComponentSource);

        foreach (var span in GetExpectedMethodSpans())
        {
            var classifications = workspace.LanguageServices.Classification
                .GetClassifications(context, span);

            AssertOnlyClassification(
                classifications,
                span,
                AkburaClassificationKind.MethodName);
        }
    }

    [Fact]
    public void Definition_ComponentMethodReferencesNavigateToSourceDeclarations()
    {
        using var workspace = CreateSemanticWorkspace();
        var text = SourceText.From(ComponentSource);
        var filePath = Path.GetFullPath("ViewThisComponentInGithub.akbura");
        var context = OpenComponent(workspace, ComponentSource);
        var spans = GetExpectedMethodSpans();
        var findDeclaration = GetDeclarationSpan("FindFirstComponentAncestor");
        var resolveDeclaration = GetDeclarationSpan("ResolveComponent");

        AssertDefinition(spans[0], findDeclaration);
        AssertDefinition(spans[1], resolveDeclaration);
        AssertDefinition(spans[2], resolveDeclaration);

        void AssertDefinition(TextSpan referenceSpan, TextSpan declarationSpan)
        {
            var definition = workspace.LanguageServices.Definition
                .GetDefinition(context, referenceSpan.Start);

            Assert.NotNull(definition);
            Assert.Equal(referenceSpan, definition!.SourceSpan);
            Assert.Equal(filePath, definition.TargetFilePath);
            Assert.Equal(
                text.Lines.GetLinePositionSpan(declarationSpan),
                definition.TargetLineSpan);
        }
    }

    [Fact]
    public void SyntacticClassification_RecognizesSupportedComponentMethodShapes()
    {
        const string source = """
            static async Task<int?> LoadAsync<T>(T value)
                where T : class
            {
                return await Task.FromResult<int?>(null);
            }

            int Sum(int left, int right) => left + right;

            void Outer()
            {
                int Local() => 1;
                var value = Local();
            }

            <Border/>
            """;
        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(text, "Methods.akbura");
        var classifications = workspace.LanguageServices.Classification
            .GetSyntacticClassifications(document, new TextSpan(0, text.Length));

        foreach (var name in new[] { "LoadAsync", "Sum", "Outer", "Local" })
        {
            var start = source.IndexOf(name, StringComparison.Ordinal);
            AssertOnlyClassification(
                classifications,
                new TextSpan(start, name.Length),
                AkburaClassificationKind.MethodName);
        }
    }

    [Fact]
    public void SemanticClassification_ResolvesGenericExpressionBodiedAndOverloadedMethods()
    {
        const string source = """
            using System.Threading.Tasks;

            static async Task<int?> LoadAsync<T>(T value)
                where T : class
            {
                return await Task.FromResult<int?>(null);
            }

            int Sum(int left, int right) => left + right;

            int Sum(int value) => value;

            void Invoke()
            {
                var total = Sum(1, 2);
                Func<int, int> transform = Sum;
                var task = LoadAsync("value");
                DeclaredLater();
            }

            void DeclaredLater()
            {
            }

            <Control/>
            """;
        using var workspace = CreateSemanticWorkspace();
        var context = OpenComponent(workspace, source, "MethodShapes.akbura");
        var callStart = source.IndexOf("Sum(1, 2)", StringComparison.Ordinal);
        var groupStart = source.IndexOf("Sum;", callStart, StringComparison.Ordinal);
        var genericCallStart = source.IndexOf("LoadAsync(\"value\")", StringComparison.Ordinal);
        var forwardCallStart = source.IndexOf("DeclaredLater();", StringComparison.Ordinal);
        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(
                context,
                new TextSpan(0, source.Length));

        AssertOnlyClassification(
            classifications,
            new TextSpan(callStart, "Sum".Length),
            AkburaClassificationKind.MethodName);
        AssertOnlyClassification(
            classifications,
            new TextSpan(groupStart, "Sum".Length),
            AkburaClassificationKind.MethodName);
        AssertOnlyClassification(
            classifications,
            new TextSpan(genericCallStart, "LoadAsync".Length),
            AkburaClassificationKind.MethodName);
        AssertOnlyClassification(
            classifications,
            new TextSpan(forwardCallStart, "DeclaredLater".Length),
            AkburaClassificationKind.MethodName);

        var groupDefinition = workspace.LanguageServices.Definition.GetDefinition(
            context,
            groupStart);
        var unaryDeclarationStart = source.IndexOf(
            "Sum(int value)",
            StringComparison.Ordinal);

        Assert.NotNull(groupDefinition);
        Assert.Equal(
            context.Document.Text.Lines.GetLinePositionSpan(
                new TextSpan(unaryDeclarationStart, "Sum".Length)),
            groupDefinition!.TargetLineSpan);
    }

    [Fact]
    public void SemanticClassification_PrecedingLocalFunctionCallResolvesToMethod()
    {
        const string source = """
            void Outer()
            {
                int Local(int value) => value;
                var result = Local(1);
            }

            <Control/>
            """;
        using var workspace = CreateSemanticWorkspace();
        var context = OpenComponent(workspace, source, "LocalFunctionCall.akbura");
        var usageStart = source.LastIndexOf("Local(1)", StringComparison.Ordinal);
        var usageSpan = new TextSpan(usageStart, "Local".Length);
        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(context, usageSpan);
        var references = GetStatementReferences(context)
            .Where(reference => reference.SourceSpan == usageSpan)
            .ToArray();

        AssertOnlyClassification(
            classifications,
            usageSpan,
            AkburaClassificationKind.MethodName);
        var reference = Assert.Single(references);
        var method = Assert.IsAssignableFrom<IMethodSymbol>(
            reference.CSharpDefinition.Symbol);
        Assert.Equal(MethodKind.LocalFunction, method.MethodKind);
    }

    [Fact]
    public void SemanticClassification_LocalFunctionBodyPreservesItsParameterAndOuterCaptures()
    {
        const string source = """
            void Helper()
            {
            }

            void Outer(int captured)
            {
                var prefix = captured;

                int Local(int value)
                {
                    Helper();
                    return value + prefix + captured;
                }
            }

            <Control/>
            """;
        using var workspace = CreateSemanticWorkspace();
        var context = OpenComponent(workspace, source, "LocalFunctionBody.akbura");
        var helperStart = source.LastIndexOf("Helper();", StringComparison.Ordinal);
        var valueStart = source.LastIndexOf("value +", StringComparison.Ordinal);
        var prefixStart = source.LastIndexOf("prefix +", StringComparison.Ordinal);
        var capturedStart = source.LastIndexOf("captured;", StringComparison.Ordinal);
        var helperSpan = new TextSpan(helperStart, "Helper".Length);
        var valueSpan = new TextSpan(valueStart, "value".Length);
        var prefixSpan = new TextSpan(prefixStart, "prefix".Length);
        var capturedSpan = new TextSpan(capturedStart, "captured".Length);
        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(context, new TextSpan(0, source.Length));
        var references = GetStatementReferences(context);

        AssertOnlyClassification(
            classifications,
            helperSpan,
            AkburaClassificationKind.MethodName);
        AssertOnlyClassification(
            classifications,
            valueSpan,
            AkburaClassificationKind.ParameterName);
        AssertOnlyClassification(
            classifications,
            prefixSpan,
            AkburaClassificationKind.LocalName);
        AssertOnlyClassification(
            classifications,
            capturedSpan,
            AkburaClassificationKind.ParameterName);
        Assert.IsAssignableFrom<IMethodSymbol>(
            Assert.Single(references, reference => reference.SourceSpan == helperSpan)
                .CSharpDefinition.Symbol);
        Assert.IsAssignableFrom<IParameterSymbol>(
            Assert.Single(references, reference => reference.SourceSpan == valueSpan)
                .CSharpDefinition.Symbol);
        Assert.IsAssignableFrom<ILocalSymbol>(
            Assert.Single(references, reference => reference.SourceSpan == prefixSpan)
                .CSharpDefinition.Symbol);
        Assert.IsAssignableFrom<IParameterSymbol>(
            Assert.Single(references, reference => reference.SourceSpan == capturedSpan)
                .CSharpDefinition.Symbol);
    }

    [Fact]
    public void SemanticClassification_ShadowingDelegateRemainsLocalName()
    {
        const string source = """
            void ResolveComponent()
            {
            }

            void InvokeShadow()
            {
                Action ResolveComponent = () => { };
                ResolveComponent();
            }

            <Control/>
            """;
        using var workspace = CreateSemanticWorkspace();
        var context = OpenComponent(workspace, source, "Shadowing.akbura");
        var invocationStart = source.LastIndexOf("ResolveComponent();", StringComparison.Ordinal);
        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(
                context,
                new TextSpan(invocationStart, "ResolveComponent".Length));

        AssertOnlyClassification(
            classifications,
            new TextSpan(invocationStart, "ResolveComponent".Length),
            AkburaClassificationKind.LocalName);
    }

    [Fact]
    public void SemanticClassification_ShadowingMethodParameterRemainsParameterName()
    {
        const string source = """
            state int value = 0;

            void Apply(int value)
            {
                Console.WriteLine(value);
            }

            <Control/>
            """;
        using var workspace = CreateSemanticWorkspace();
        var context = OpenComponent(workspace, source, "ParameterShadowing.akbura");
        var usageStart = source.LastIndexOf("value);", StringComparison.Ordinal);
        var usageSpan = new TextSpan(usageStart, "value".Length);
        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(context, usageSpan);
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        var references = context.Document.SyntaxTree.GetRootSyntax()
            .DescendantNodes()
            .OfType<CSharpStatementSyntax>()
            .SelectMany(statement =>
                semanticModel.GetCSharpSymbolReferences(statement))
            .Where(reference => reference.SourceSpan == usageSpan)
            .ToArray();

        AssertOnlyClassification(
            classifications,
            usageSpan,
            AkburaClassificationKind.ParameterName);
        Assert.NotEmpty(references);
        Assert.All(references, reference =>
        {
            Assert.IsAssignableFrom<IParameterSymbol>(
                reference.CSharpDefinition.Symbol);
            Assert.Null(reference.AkburaSymbol);
        });
    }

    [Fact]
    public void SemanticClassification_AmbiguousMethodGroupHasNoArbitraryDefinition()
    {
        const string source = """
            void ResolveComponent()
            {
            }

            void ResolveComponent(int value)
            {
            }

            void Capture()
            {
                var callback = ResolveComponent;
            }

            <Control/>
            """;
        using var workspace = CreateSemanticWorkspace();
        var context = OpenComponent(workspace, source, "Overloads.akbura");
        var usageStart = source.LastIndexOf("ResolveComponent;", StringComparison.Ordinal);
        var usageSpan = new TextSpan(usageStart, "ResolveComponent".Length);
        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(context, usageSpan);
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        var references = context.Document.SyntaxTree.GetRootSyntax()
            .DescendantNodes()
            .OfType<CSharpStatementSyntax>()
            .SelectMany(statement =>
                semanticModel.GetCSharpSymbolReferences(statement))
            .Where(reference => reference.SourceSpan == usageSpan)
            .ToArray();

        AssertOnlyClassification(
            classifications,
            usageSpan,
            AkburaClassificationKind.MethodName);
        Assert.NotEmpty(references);
        Assert.All(references, reference =>
        {
            Assert.True(reference.IsMethodGroup);
            Assert.Null(reference.CSharpDefinition.Symbol);
        });
        Assert.Null(workspace.LanguageServices.Definition.GetDefinition(
            context,
            usageSpan.Start));
    }

    [Fact]
    public void SemanticClassification_MethodNamesInsideStringsAndCommentsRemainTrivia()
    {
        const string source = """
            void ResolveComponent()
            {
                var text = "ResolveComponent";
                // ResolveComponent
            }

            <Control/>
            """;
        using var workspace = CreateSemanticWorkspace();
        var context = OpenComponent(workspace, source, "MethodTrivia.akbura");
        var stringNameStart = source.IndexOf(
            "ResolveComponent\"",
            source.IndexOf("var text", StringComparison.Ordinal),
            StringComparison.Ordinal);
        var commentNameStart = source.LastIndexOf(
            "ResolveComponent",
            StringComparison.Ordinal);
        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(
                context,
                new TextSpan(0, source.Length));

        Assert.DoesNotContain(
            classifications,
            classification =>
                classification.Kind == AkburaClassificationKind.MethodName &&
                (classification.Span.Contains(stringNameStart) ||
                 classification.Span.Contains(commentNameStart)));
    }

    [Fact]
    public void SyntacticClassification_IncompleteMethodSignatureKeepsMethodName()
    {
        const string source = "void ResolveComponent(";
        using var workspace = new AkburaWorkspace();
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "IncompleteMethod.akbura");
        var classifications = workspace.LanguageServices.Classification
            .GetSyntacticClassifications(
                document,
                new TextSpan(0, text.Length));
        var span = new TextSpan(
            source.IndexOf("ResolveComponent", StringComparison.Ordinal),
            "ResolveComponent".Length);

        AssertOnlyClassification(
            classifications,
            span,
            AkburaClassificationKind.MethodName);
    }

    private static ImmutableArray<TextSpan> GetExpectedMethodSpans()
    {
        return GetExpectedMethodSpans(ComponentSource, "ResolveComponent");
    }

    private static ImmutableArray<TextSpan> GetExpectedMethodSpans(string source, string resolveName)
    {
        const string findName = "FindFirstComponentAncestor";
        var findDeclaration = GetDeclarationSpan(source, findName);
        var resolveDeclaration = GetDeclarationSpan(source, resolveName);
        var findCallStart = source.IndexOf(
            "FindFirstComponentAncestor();",
            StringComparison.Ordinal);
        var firstMethodGroupStart = source.IndexOf(
            resolveName + ");",
            StringComparison.Ordinal);
        var secondMethodGroupStart = source.IndexOf(
            resolveName + ");",
            firstMethodGroupStart + resolveName.Length,
            StringComparison.Ordinal);

        Assert.True(findCallStart >= 0);
        Assert.True(firstMethodGroupStart >= 0);
        Assert.True(secondMethodGroupStart >= 0);

        return
        [
            new TextSpan(findCallStart, findName.Length),
            new TextSpan(firstMethodGroupStart, resolveName.Length),
            new TextSpan(secondMethodGroupStart, resolveName.Length),
            findDeclaration,
            resolveDeclaration,
        ];
    }

    private static TextSpan GetDeclarationSpan(string name)
    {
        return GetDeclarationSpan(ComponentSource, name);
    }

    private static TextSpan GetDeclarationSpan(string source, string name)
    {
        var prefix = name == "FindFirstComponentAncestor"
            ? "AkburaControl? "
            : "void ";
        var declaration = prefix + name + "()";
        var declarationStart = source.IndexOf(declaration, StringComparison.Ordinal);

        Assert.True(declarationStart >= 0);

        var start = declarationStart + prefix.Length;
        return new TextSpan(start, name.Length);
    }

    private static void AssertMethodClassifications(AkburaWorkspace workspace, AkburaDocumentContext context, string source, string resolveName)
    {
        var classifications = workspace.LanguageServices.Classification
            .GetClassifications(context, new TextSpan(0, source.Length));

        foreach (var span in GetExpectedMethodSpans(source, resolveName))
        {
            AssertOnlyClassification(
                classifications,
                span,
                AkburaClassificationKind.MethodName);
        }
    }

    private static void AssertMatchesFresh(AkburaWorkspace workspace, AkburaDocumentContext context, CSharpCompilation compilation, string source)
    {
        var actual = GetOrderedClassifications(workspace, context);
        using var freshWorkspace = CreateSemanticWorkspace(compilation);
        var freshContext = OpenComponent(
            freshWorkspace,
            source,
            Path.GetFileName(context.Document.FilePath));
        var expected = GetOrderedClassifications(freshWorkspace, freshContext);

        Assert.Equal(expected, actual);
    }

    private static AkburaClassifiedSpan[] GetOrderedClassifications(AkburaWorkspace workspace, AkburaDocumentContext context)
    {
        return workspace.LanguageServices.Classification
            .GetClassifications(
                context,
                new TextSpan(0, context.Document.Text.Length))
            .OrderBy(static classification => classification.Span.Start)
            .ThenBy(static classification => classification.Span.Length)
            .ThenBy(static classification => classification.Kind)
            .ToArray();
    }

    private static CSharpSymbolReference[] GetStatementReferences(AkburaDocumentContext context)
    {
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        return context.Document.SyntaxTree.GetRootSyntax()
            .DescendantNodes()
            .OfType<CSharpStatementSyntax>()
            .SelectMany(statement =>
                semanticModel.GetCSharpSymbolReferences(statement))
            .ToArray();
    }

    private static SourceText ApplyMinimalChange(SourceText current, string nextSource)
    {
        var currentSource = current.ToString();
        var prefixLength = 0;
        while (prefixLength < currentSource.Length && prefixLength < nextSource.Length && currentSource[prefixLength] == nextSource[prefixLength])
        {
            prefixLength++;
        }

        var suffixLength = 0;
        while (suffixLength < currentSource.Length - prefixLength && suffixLength < nextSource.Length - prefixLength && currentSource[^(suffixLength + 1)] == nextSource[^(suffixLength + 1)])
        {
            suffixLength++;
        }

        var replacement = nextSource.Substring(
            prefixLength,
            nextSource.Length - prefixLength - suffixLength);
        return current.WithChanges(new TextChange(
            new TextSpan(
                prefixLength,
                currentSource.Length - prefixLength - suffixLength),
            replacement));
    }

    private static string InsertLineBeforeFindMethod(string source, string newLine)
    {
        var start = source.IndexOf(
            "AkburaControl? FindFirstComponentAncestor()",
            StringComparison.Ordinal);

        Assert.True(start >= 0);
        return source.Insert(start, newLine);
    }

    private static string MoveResolveMethodBeforeFindMethod(string source, string resolveName)
    {
        var findStart = source.IndexOf(
            "AkburaControl? FindFirstComponentAncestor()",
            StringComparison.Ordinal);
        var resolveStart = source.IndexOf(
            "void " + resolveName + "()",
            StringComparison.Ordinal);
        var hookStart = source.IndexOf("useEffect(", StringComparison.Ordinal);

        Assert.True(findStart >= 0);
        Assert.True(resolveStart > findStart);
        Assert.True(hookStart > resolveStart);

        return source[..findStart] +
            source[resolveStart..hookStart] +
            source[findStart..resolveStart] +
            source[hookStart..];
    }

    private static CSharpCompilation AddProductionGeneratedComponent(CSharpCompilation compilation, string source)
    {
        var projectDirectory = Environment.CurrentDirectory;
        var globalUsingsPath = Path.GetFullPath("GlobalUsings.akbura");
        var componentPath = Path.GetFullPath(
            "ViewThisComponentInGithub.akbura");
        var globalUsingsTree = ComponentSyntaxTree.ParseText(
            SourceText.From(GlobalUsingsSource),
            globalUsingsPath);
        var componentTree = ComponentSyntaxTree.ParseText(
            SourceText.From(source),
            componentPath);
        var catalog = AkburaGenerationCatalogBuilder.Create(
            compilation,
            ImmutableArray.Create<AkburaSyntaxTree>(
                globalUsingsTree,
                componentTree),
            "Akbura.FeatureGallery.Components",
            projectDirectory);
        var input = Assert.Single(catalog.Components);
        var generatedText = ComponentDocumentWriter.Generate(
            input.Component,
            input.SemanticModel,
            input.SourcePath,
            catalog.AkcssModuleTypeNames);
        var hintName = ComponentDocumentWriter.GetHintName(
            input.Component,
            input.SourcePath);
        var generatedPath = Path.Combine(
            projectDirectory,
            "obj",
            "Akbura.BlackSilence",
            "Akbura.BlackSilence.AkburaBlackSilenceGenerator",
            hintName);
        var parseOptions = compilation.SyntaxTrees
            .FirstOrDefault()?.Options as CSharpParseOptions;
        var generatedTree = CSharpSyntaxTree.ParseText(
            generatedText,
            parseOptions,
            generatedPath);

        return compilation.AddSyntaxTrees(generatedTree);
    }

    private static AkburaDocumentContext OpenComponent(AkburaWorkspace workspace, string source, string fileName = "ViewThisComponentInGithub.akbura")
    {
        workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath("GlobalUsings.akbura")),
            SourceText.From(GlobalUsingsSource));
        return workspace.OpenOrChangeDocumentContext(
            new Uri(Path.GetFullPath(fileName)),
            SourceText.From(source));
    }

    private static void AssertOnlyClassification(IEnumerable<AkburaClassifiedSpan> classifications, TextSpan expectedSpan, AkburaClassificationKind expectedKind)
    {
        Assert.Contains(
            classifications,
            classification =>
                classification.Span == expectedSpan &&
                classification.Kind == expectedKind);
        Assert.DoesNotContain(
            classifications,
            classification =>
                classification.Span.OverlapsWith(expectedSpan) &&
                (classification.Span != expectedSpan ||
                 classification.Kind != expectedKind));
    }

    private static AkburaWorkspace CreateSemanticWorkspace(CSharpCompilation? compilation = null)
    {
        compilation ??= CreateCSharpCompilation();
        var project = new ProjectContext(
            ProjectId.CreateNewId(),
            projectFilePath: string.Empty,
            projectDirectory: Environment.CurrentDirectory,
            rootNamespace: "Akbura.FeatureGallery.Components",
            compilation,
            ImmutableArray<ProjectReference>.Empty);
        return new AkburaWorkspace(project);
    }

    private static CSharpCompilation CreateCSharpCompilation()
    {
        const string csharpSource = """
            namespace Akbura
            {
                public class AkburaControl : Avalonia.Controls.Control,
                    Akbura.ComponentTree.IComponentTree
                {
                    public AkburaControl? ComponentParent { get; }

                    public event System.EventHandler<
                        Avalonia.VisualTree.VisualTreeAttachmentEventArgs>?
                        AttachedToVisualTree;
                }
            }

            namespace Akbura.CompilerAnotations
            {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class UseHookAttribute : System.Attribute
                {
                }

                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                public sealed class SelfAttribute : System.Attribute
                {
                }
            }

            namespace Akbura.ComponentTree
            {
                public interface IComponentTree
                {
                    Akbura.AkburaControl? ComponentParent { get; }
                }

                public readonly struct State<T>
                {
                }
            }

            namespace Akbura.Hooks
            {
                public static class EffectHooks
                {
                    [Akbura.CompilerAnotations.UseHook]
                    public static void useEffect(
                        [Akbura.CompilerAnotations.Self]
                        this Akbura.AkburaControl control,
                        System.Func<System.Action> effect,
                        object[] dependencies)
                    {
                    }
                }
            }

            namespace Avalonia.Controls
            {
                public class Control
                {
                    public Avalonia.Layout.HorizontalAlignment HorizontalAlignment { get; set; }
                }
            }

            namespace Avalonia.Layout
            {
                public enum HorizontalAlignment
                {
                    Left,
                }
            }

            namespace Avalonia.Threading
            {
                public sealed class Dispatcher
                {
                    public static Dispatcher UIThread { get; } = new();

                    public void Post(System.Action action)
                    {
                    }
                }
            }

            namespace Avalonia.VisualTree
            {
                public sealed class VisualTreeAttachmentEventArgs : System.EventArgs
                {
                }
            }

            namespace Akbura.FeatureGallery.Components
            {
                public sealed class ViewThisInGithub : Avalonia.Controls.Control
                {
                    public string? Url { get; set; }

                    public object? Content { get; set; }

                    public bool IsVisible { get; set; }
                }
            }
            """;
        return CSharpCompilation.Create(
            "WorkspaceMethodClassificationTests",
            [CSharpSyntaxTree.ParseText(csharpSource)],
            CreatePlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference[] CreatePlatformReferences()
    {
        var trustedPlatformAssemblies =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator) ?? [];

        return trustedPlatformAssemblies
            .Select(static path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
