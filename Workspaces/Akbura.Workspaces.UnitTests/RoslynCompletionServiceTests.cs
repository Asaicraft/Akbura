using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.Workspaces.UnitTests;

public sealed class RoslynCompletionServiceTests
{
    [Fact]
    public async Task InvokeCompletion_InProjectionDocument_ReturnsVisibleSymbol()
    {
        const string source = """
            partial class Counter
            {
                private int count;

                private object Probe()
                {
                    return c;
                }
            }
            """;
        var position = source.IndexOf(
            "return c;",
            StringComparison.Ordinal) + "return c".Length;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "CompletionTests",
            [syntaxTree],
            RoslynCompletionTestHost.CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var completions = await RoslynCompletionTestHost
            .GetCompletionsAsync(
                compilation.RemoveSyntaxTrees(syntaxTree),
                syntaxTree.GetCompilationUnitRoot(),
                position,
                CancellationToken.None);

        Assert.NotNull(completions);
        Assert.Contains(
            completions.ItemsList,
            static item => item.DisplayText == "count");
    }
    [Theory]
    [InlineData(' ', false)]
    [InlineData('1', false)]
    [InlineData('.', true)]
    public async Task InsertionPreflight_UsesRoslynTriggerPolicy(
        char triggerCharacter,
        bool expected)
    {
        var source = """
            partial class Counter
            {
                private int value;

                private int Probe()
                {
                    return value|;
                }
            }
            """.Replace(
                "|",
                triggerCharacter.ToString(),
                StringComparison.Ordinal);
        var position = source.IndexOf(
            $"value{triggerCharacter}",
            StringComparison.Ordinal) +
            "value".Length +
            1;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "CompletionPreflightTests",
            [syntaxTree],
            RoslynCompletionTestHost.CreatePlatformReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var shouldTrigger = await RoslynCompletionTestHost
            .ShouldTriggerCompletionAsync(
                compilation.RemoveSyntaxTrees(syntaxTree),
                syntaxTree.GetCompilationUnitRoot(),
                position,
                triggerCharacter,
                CancellationToken.None);

        Assert.Equal(expected, shouldTrigger);
    }
}

internal static class RoslynCompletionTestHost
{
    private static readonly ImmutableArray<Assembly> s_assemblies =
        MefHostServices.DefaultAssemblies
            .Concat(
            [
                Assembly.Load("Microsoft.CodeAnalysis.Features"),
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features"),
            ])
            .Distinct()
            .ToImmutableArray();

    public static async Task<CompletionList?> GetCompletionsAsync(
        CSharpCompilation compilation,
        Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax root,
        int position,
        CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace(
            MefHostServices.Create(s_assemblies));
        var document = CreateDocument(
            workspace,
            compilation,
            root,
            cancellationToken);
        var service = CompletionService.GetService(document);
        if (service == null)
        {
            return null;
        }

        return await service.GetCompletionsAsync(
            document,
            position,
            CompletionTrigger.Invoke,
            cancellationToken: cancellationToken);
    }

    public static async Task<bool> ShouldTriggerCompletionAsync(
        CSharpCompilation compilation,
        Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax root,
        int position,
        char triggerCharacter,
        CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace(
            MefHostServices.Create(s_assemblies));
        var document = CreateDocument(
            workspace,
            compilation,
            root,
            cancellationToken);
        var service = CompletionService.GetService(document);
        if (service == null)
        {
            return false;
        }

        if (!AkburaRoslynCompletionTriggerPolicy
                .IsSupportedInsertionCharacter(
                    triggerCharacter))
        {
            return false;
        }

        var text = await document.GetTextAsync(
            cancellationToken);
        return service.ShouldTriggerCompletion(
            text,
            position,
            CompletionTrigger.CreateInsertionTrigger(
                triggerCharacter));
    }

    public static async Task<RoslynImportCompletionResult?>
        GetImportCompletionAsync(
            CSharpCompilation compilation,
            Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax root,
            int position,
            string displayText,
            CancellationToken cancellationToken)
    {
        return await GetCompletionChangeAsync(
            compilation,
            root,
            position,
            displayText,
            requireComplexTextEdit: true,
            cancellationToken);
    }

    public static async Task<RoslynImportCompletionResult?>
        GetCompletionChangeAsync(
            CSharpCompilation compilation,
            Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax root,
            int position,
            string displayText,
            bool requireComplexTextEdit,
            CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace(
            MefHostServices.Create(s_assemblies));
        var document = CreateDocument(
            workspace,
            compilation,
            root,
            cancellationToken);
        var service = CompletionService.GetService(document);
        if (service == null)
        {
            return null;
        }

        var completions = await service.GetCompletionsAsync(
            document,
            position,
            CompletionTrigger.Invoke,
            cancellationToken: cancellationToken);
        var item = completions?.ItemsList.FirstOrDefault(candidate =>
            candidate.DisplayText == displayText &&
            (!requireComplexTextEdit || candidate.IsComplexTextEdit));
        if (item == null)
        {
            return null;
        }

        var change = await service.GetChangeAsync(
            document,
            item,
            commitCharacter: null,
            cancellationToken);
        var text = await document.GetTextAsync(cancellationToken);
        return new RoslynImportCompletionResult(
            item,
            change,
            text);
    }

    public static async Task<QuickInfoItem?> GetQuickInfoAsync(
        CSharpCompilation compilation,
        Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax root,
        int position,
        CancellationToken cancellationToken)
    {
        using var workspace = new AdhocWorkspace(
            MefHostServices.Create(s_assemblies));
        var document = CreateDocument(
            workspace,
            compilation,
            root,
            cancellationToken);
        var service = QuickInfoService.GetService(document);
        return service == null
            ? null
            : await service.GetQuickInfoAsync(
                document,
                position,
                cancellationToken);
    }

    private static Document CreateDocument(
        AdhocWorkspace workspace,
        CSharpCompilation compilation,
        Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax root,
        CancellationToken cancellationToken)
    {
        var parseOptions = compilation.SyntaxTrees
            .Select(static tree => tree.Options)
            .OfType<CSharpParseOptions>()
            .FirstOrDefault() ?? CSharpParseOptions.Default;
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Create(),
            compilation.AssemblyName ?? "CompletionTests",
            compilation.AssemblyName ?? "CompletionTests",
            LanguageNames.CSharp,
            parseOptions: parseOptions,
            compilationOptions: compilation.Options,
            metadataReferences: compilation.References));
        var sourceIndex = 0;
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var sourcePath = string.IsNullOrWhiteSpace(
                    syntaxTree.FilePath)
                ? $"Source{sourceIndex}.cs"
                : syntaxTree.FilePath;
            project = project.AddDocument(
                    Path.GetFileName(sourcePath),
                    syntaxTree.GetText(cancellationToken),
                    filePath: sourcePath)
                .Project;
            sourceIndex++;
        }

        var document = project.AddDocument(
            "Counter.AkburaCompletion.cs",
            SourceText.From(root.ToFullString()),
            filePath: "Counter.akbura.completion.cs")
            .WithSyntaxRoot(root);
        return document;
    }

    public static ImmutableArray<MetadataReference>
        CreatePlatformReferences()
    {
        var trustedAssemblies = AppContext.GetData(
            "TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            return
            [
                MetadataReference.CreateFromFile(
                    typeof(object).Assembly.Location),
            ];
        }

        return trustedAssemblies
            .Split(Path.PathSeparator)
            .Select(static path =>
                MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    }
}

internal readonly struct RoslynImportCompletionResult
{
    public RoslynImportCompletionResult(
        CompletionItem item,
        CompletionChange change,
        SourceText projectedText)
    {
        Item = item;
        Change = change;
        ProjectedText = projectedText;
    }

    public CompletionItem Item { get; }

    public CompletionChange Change { get; }

    public SourceText ProjectedText { get; }
}
